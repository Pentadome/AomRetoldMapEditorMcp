using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AomMcp;

/// <summary>Preview-first, byte-preserving edits of reviewed TR v12 fields. Never touches a scenario file.</summary>
internal static class TriggerEdits
{
    sealed record Change(int Offset, int Length, byte[] Bytes);

    static readonly UnicodeEncoding Wide = new(false, false, true);
    static readonly string[] NumericKeys =
    [
        "Player",
        "PlayerID",
        "FromPlayerID",
        "ToPlayerID",
        "EventID",
        "TechID",
        "Count",
        "Dist",
        "Status",
        "Value",
        "Duration",
    ];
    static readonly string[] StringKeys = ["ProtoUnit", "UnitType", "Command", "QVName", "Op"];

    static readonly string[] ScalarKeys = ["newName", "active", "loop"];
    static readonly string[] ListKeys =
    [
        "replacements",
        "labels",
        "duplicates",
        "objectReplacements",
        "copyEffects",
        "copyValues",
    ];
    static readonly string[] IndexListKeys = ["removeEffects", "removeConditions"];

    // Identity of the element/parameter an entry in a list key targets; two entries with the same identity conflict.
    static string? Target(string key, JsonElement entry)
    {
        static string Part(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) ? v.ToString() : "";
        return key switch
        {
            "replacements" or "objectReplacements" => Part(entry, "copyHandle") is { Length: > 0 } h
                ? "copy " + h + "." + Part(entry, "parameter").ToLowerInvariant()
                : Part(entry, "kind")
                    + "["
                    + Part(entry, "elementIndex")
                    + "]."
                    + Part(entry, "parameter").ToLowerInvariant(),
            "labels" => Part(entry, "kind") + "[" + Part(entry, "elementIndex") + "].label",
            "copyEffects" => "copy handle " + Part(entry, "handle"),
            "copyValues" => "copy "
                + Part(entry, "copyHandle")
                + "."
                + Part(entry, "parameter").ToLowerInvariant(),
            _ => null, // duplicates may legitimately repeat a source element.
        };
    }

    /// <summary>
    /// Merges patch operations on the same trigger into one operation (all indices stay original-numbered).
    /// Clones are independent and pass through. Real conflicts are refused, naming both operation positions.
    /// </summary>
    internal static JsonElement[] Merge(JsonElement[] operations)
    {
        var result = new List<JsonElement>();
        var merged =
            new Dictionary<int, (JsonObject Node, int First, Dictionary<string, int> Owners)>();
        var order = new List<object>();
        for (var i = 0; i < operations.Length; i++)
        {
            var op = operations[i];
            var id = op.GetProperty("triggerId").GetInt32();
            if (op.GetProperty("operation").GetString() != "patch")
            {
                order.Add(op);
                continue;
            }
            if (!merged.TryGetValue(id, out var state))
            {
                state = (
                    (JsonObject)JsonNode.Parse(op.GetRawText())!,
                    i,
                    new Dictionary<string, int>(StringComparer.Ordinal)
                );
                merged[id] = state;
                order.Add(id);
                foreach (var key in ListKeys.Where(k => op.TryGetProperty(k, out _)))
                foreach (var entry in op.GetProperty(key).EnumerateArray())
                    if (Target(key, entry) is { } t)
                        state.Owners[key + ":" + t] = i;
                continue;
            }
            var node = state.Node;
            if (
                op.GetProperty("expectedName").GetString()
                != node["expectedName"]!.GetValue<string>()
            )
                throw new ArgumentException(
                    $"Edits {state.First} and {i} target trigger {id} with different expectedName."
                );
            foreach (var key in ScalarKeys.Where(k => op.TryGetProperty(k, out _)))
            {
                var value = JsonNode.Parse(op.GetProperty(key).GetRawText())!;
                if (
                    node.TryGetPropertyValue(key, out var existing)
                    && existing is not null
                    && existing.ToJsonString() != value.ToJsonString()
                )
                    throw new ArgumentException(
                        $"Edits {state.First} and {i} set conflicting '{key}' on trigger {id}."
                    );
                node[key] = value;
            }
            foreach (var key in ListKeys.Where(k => op.TryGetProperty(k, out _)))
            {
                var array = node[key] as JsonArray ?? (JsonArray)(node[key] = new JsonArray());
                foreach (var entry in op.GetProperty(key).EnumerateArray())
                {
                    if (Target(key, entry) is { } t)
                    {
                        if (state.Owners.TryGetValue(key + ":" + t, out var owner))
                            throw new ArgumentException(
                                $"Edits {owner} and {i} both change {t} on trigger {id}; combine them into one {key} entry."
                            );
                        state.Owners[key + ":" + t] = i;
                    }
                    array.Add(JsonNode.Parse(entry.GetRawText()));
                }
            }
            foreach (var key in IndexListKeys.Where(k => op.TryGetProperty(k, out _)))
            {
                var array = node[key] as JsonArray ?? (JsonArray)(node[key] = new JsonArray());
                var present = array.Select(n => n!.GetValue<int>()).ToHashSet();
                foreach (
                    var index in op.GetProperty(key).EnumerateArray().Select(v => v.GetInt32())
                )
                    if (present.Add(index))
                        array.Add(index);
            }
            foreach (
                var property in op.EnumerateObject()
                    .Select(p => p.Name)
                    .Except(ScalarKeys)
                    .Except(ListKeys)
                    .Except(IndexListKeys)
            )
                if (property is not ("operation" or "triggerId" or "expectedName"))
                    throw new ArgumentException(
                        $"Edit {i} sets '{property}', which cannot be merged into edit {state.First} for trigger {id}."
                    );
        }
        foreach (var item in order)
        {
            if (item is JsonElement element)
            {
                result.Add(element);
                continue;
            }
            var (node, first, _) = merged[(int)item];
            result.Add(JsonDocument.Parse(node.ToJsonString()).RootElement.Clone());
            _ = first;
        }
        // Removing an element while another entry (possibly from a different edit) changes it is a conflict too.
        foreach (var op in result.Where(o => o.GetProperty("operation").GetString() == "patch"))
        {
            ConflictWithRemoval(op, "removeEffects", "effect");
            ConflictWithRemoval(op, "removeConditions", "condition");
        }
        return [.. result];
    }

    static void ConflictWithRemoval(JsonElement op, string removalKey, string kind)
    {
        if (!op.TryGetProperty(removalKey, out var removals))
            return;
        var removed = removals.EnumerateArray().Select(v => v.GetInt32()).ToHashSet();
        foreach (var key in new[] { "replacements", "labels", "objectReplacements" })
            if (op.TryGetProperty(key, out var list))
                foreach (var entry in list.EnumerateArray())
                    if (
                        entry.TryGetProperty("kind", out var k)
                        && k.GetString() == kind
                        && entry.TryGetProperty("elementIndex", out var index)
                        && removed.Contains(index.GetInt32())
                    )
                        throw new ArgumentException(
                            $"Trigger {op.GetProperty("triggerId").GetInt32()}: {kind}[{index.GetInt32()}] is both removed and changed ({key})."
                        );
    }

    internal static void Preflight(JsonElement args)
    {
        foreach (var key in TopKeysRequired)
            if (!args.TryGetProperty(key, out _))
                throw new ArgumentException($"Missing required argument '{key}'.");
        var batch = args.TryGetProperty("edits", out var list);
        if (batch)
        {
            Catalog.ValidateObject(
                args,
                [
                    "path",
                    "expectedSha256",
                    "edits",
                    "preview",
                    "outputPath",
                    "confirmWrite",
                    "allowRawXsInjection",
                    "basePath",
                    "baseSha256",
                ]
            );
            if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() is < 1 or > 64)
                throw new ArgumentException("edits must contain 1..64 operations.");
            var seen = new HashSet<int>();
            var newIds = new HashSet<int>();
            var objectTuples = 0;
            foreach (var entry in list.EnumerateArray())
            {
                ValidateEdit(entry);
                foreach (var topLevel in new[] { "allowRawXsInjection", "basePath", "baseSha256" })
                    if (entry.TryGetProperty(topLevel, out _))
                        throw new ArgumentException(
                            topLevel + " is a top-level argument, not a per-edit one."
                        );
                if (entry.TryGetProperty("objectReplacements", out var objectEdits))
                    objectTuples += objectEdits
                        .EnumerateArray()
                        .Sum(r => r.GetProperty("objects").GetArrayLength());
                if (objectTuples > TriggerObjects.MaxObjects)
                    throw new ArgumentException("At most 200 replacement object tuples per batch.");
                seen.Add(entry.GetProperty("triggerId").GetInt32());
                if (
                    entry.GetProperty("operation").GetString() == "clone"
                    && !newIds.Add(entry.GetProperty("newId").GetInt32())
                )
                    throw new ArgumentException("Duplicate clone ID in batch.");
            }
            var all = list.EnumerateArray().ToArray();
            var mergedOperations = Merge(all);
            if (
                all.GroupBy(o => o.GetProperty("triggerId").GetInt32())
                    .Any(g =>
                        g.Select(o => o.GetProperty("operation").GetString()).Distinct().Count() > 1
                    )
            )
                throw new ArgumentException(
                    "A trigger cannot be both patched and cloned in one batch (order-dependent); use two batches."
                );
            foreach (var m in mergedOperations)
                ValidateEdit(m);
            if (newIds.Overlaps(seen))
                throw new ArgumentException("Clone ID collides with batch source ID.");
        }
        else
        {
            Catalog.ValidateObject(
                args,
                [
                    "operation",
                    "path",
                    "triggerId",
                    "expectedSha256",
                    "expectedName",
                    "newId",
                    "newName",
                    "active",
                    "loop",
                    "replacements",
                    "removeEffects",
                    "removeConditions",
                    "labels",
                    "duplicates",
                    "objectReplacements",
                    "copyEffects",
                    "copyValues",
                    "preview",
                    "outputPath",
                    "confirmWrite",
                    "allowRawXsInjection",
                    "basePath",
                    "baseSha256",
                ]
            );
            ValidateEdit(args);
        }
        _ = TriggerCodec.ReadFile(args.GetProperty("path").GetString()!);
        if (
            args.GetProperty("expectedSha256").GetString() is not { Length: 64 } sha
            || !sha.All(Uri.IsHexDigit)
        )
            throw new ArgumentException("expectedSha256 must be source file's 64-digit SHA-256.");
        if (
            args.TryGetProperty("basePath", out var basePath)
            != args.TryGetProperty("baseSha256", out var baseSha)
        )
            throw new ArgumentException(
                "basePath and baseSha256 go together (pass next.basePath/next.baseSha256 from the previous edit)."
            );
        if (basePath.ValueKind == JsonValueKind.String)
        {
            _ = EditorFiles.LocalPath(basePath.GetString()!);
            if (baseSha.GetString() is not { Length: 64 } b || !b.All(Uri.IsHexDigit))
                throw new ArgumentException("baseSha256 must be a 64-digit SHA-256.");
        }
        if (!Preview(args))
        {
            EditorFiles.Confirm(args, "confirmWrite");
            if (!args.TryGetProperty("outputPath", out var path))
                throw new ArgumentException("Non-preview edit requires outputPath.");
            EditorFiles.ApprovedNewPath(
                EditorFiles.ResolveOutput(
                    path.GetString()!,
                    args.GetProperty("path").GetString()!,
                    ".trg"
                ),
                ".trg"
            );
        }
    }

    /// <summary>Keys every edit (single-operation arguments or one edits[] entry) must carry.</summary>
    static readonly string[] EditKeysRequired = ["operation", "triggerId", "expectedName"];

    /// <summary>Top-level keys every editor_trigger_edit call must carry.</summary>
    static readonly string[] TopKeysRequired = ["path", "expectedSha256"];

    /// <summary>Checks one edit's required and allowed keys and value shapes.</summary>
    /// <param name="entry">Single-operation arguments or one edits[] entry.</param>
    static void ValidateEdit(JsonElement entry)
    {
        foreach (var key in EditKeysRequired)
            if (!entry.TryGetProperty(key, out _))
                throw new ArgumentException(
                    $"Missing required argument '{key}' (each edit needs operation patch|clone, triggerId and expectedName)."
                );
        Catalog.ValidateObject(
            entry,
            [
                "operation",
                "triggerId",
                "expectedName",
                "newId",
                "newName",
                "active",
                "loop",
                "replacements",
                "removeEffects",
                "removeConditions",
                "labels",
                "duplicates",
                "objectReplacements",
                "copyEffects",
                "copyValues",
                "path",
                "expectedSha256",
                "preview",
                "outputPath",
                "confirmWrite",
                "allowRawXsInjection",
                "basePath",
                "baseSha256",
            ]
        );
        TriggerObjects.ValidateSelectors(entry);
        var op = entry.GetProperty("operation").GetString();
        if (op is not "patch" and not "clone")
            throw new ArgumentException("Trigger edit operation: patch/clone.");
        if (entry.GetProperty("triggerId").GetInt32() < 0)
            throw new ArgumentException("triggerId must be nonnegative.");
        if (string.IsNullOrWhiteSpace(entry.GetProperty("expectedName").GetString()))
            throw new ArgumentException("expectedName must identify source trigger explicitly.");
        if (
            op == "clone"
            && (
                !entry.TryGetProperty("newId", out var id)
                || id.GetInt32() < 0
                || !entry.TryGetProperty("newName", out _)
            )
        )
            throw new ArgumentException("Clone requires nonnegative newId and newName.");
        if (op == "patch" && entry.TryGetProperty("newId", out _))
            throw new ArgumentException("Only clone can assign a new trigger ID.");
    }

    static bool Preview(JsonElement args) =>
        !args.TryGetProperty("preview", out var p) || p.GetBoolean();

    // Verified in game exports: the XS Code Snippet effect parameter has value type 72. Other long-text types are unproven.
    const uint SnippetValueType = 72;

    static byte[] Text(
        string value,
        int max = 128,
        string field = "trigger text",
        bool multiline = false
    )
    {
        if (value.Length < 1 || value.Length > max)
            throw new ArgumentException(
                $"{field}: length {value.Length} is outside the reviewed 1..{max} characters."
            );
        if (value.Any(c => char.IsControl(c) && !(multiline && c is '\n' or '\r' or '\t')))
            throw new ArgumentException(
                $"{field}: control characters are not allowed{(multiline ? " (only tab and line breaks)" : "")}."
            );
        return Wide.GetBytes(value);
    }

    static byte[] WideField(
        string value,
        int max = 128,
        string field = "trigger text",
        bool multiline = false
    )
    {
        var text = Text(value, max, field, multiline);
        var result = new byte[4 + text.Length];
        BinaryPrimitives.WriteInt32LittleEndian(result, value.Length);
        text.CopyTo(result, 4);
        return result;
    }

    static byte[] ApplyChanges(byte[] source, IEnumerable<Change> changes)
    {
        var sorted = changes
            .OrderByDescending(c => c.Offset)
            .ThenByDescending(c => c.Length)
            .ToArray();
        var previous = source.Length + 1;
        foreach (var edit in sorted)
        {
            if (
                edit.Offset < 0
                || edit.Length < 0
                || edit.Offset + edit.Length > source.Length
                || edit.Offset + edit.Length > previous
            )
                throw new InvalidDataException("Overlapping or out-of-range trigger edit.");
            previous = edit.Offset;
        }
        var output = source.ToList();
        foreach (var edit in sorted)
        {
            output.RemoveRange(edit.Offset, edit.Length);
            output.InsertRange(edit.Offset, edit.Bytes);
        }
        return output.ToArray();
    }

    internal const int MaxDuplicates = 32;

    /// <summary>Reviewed single-value encoding: magic U32 + UTF-16 length/content + optional type flag.</summary>
    static Change ValueChange(
        byte[] source,
        CampaignTriggers.Element element,
        JsonElement replacement,
        int localBase
    )
    {
        var key = replacement.GetProperty("parameter").GetString()!;
        var numeric = NumericKeys.Contains(key, StringComparer.OrdinalIgnoreCase);
        var snippet = key.Equals("CodeSnippet", StringComparison.OrdinalIgnoreCase);
        if (!numeric && !snippet && !StringKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"Parameter '{key}' is not in the reviewed allowlist (numeric: {string.Join(", ", NumericKeys)}; text: {string.Join(", ", StringKeys)}, CodeSnippet)."
            );
        var found = element
            .Args.Where(a => a.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (
            found.Length != 1
            || found[0].Values.Length != 1
            || found[0].ValueType is 4 or 7 or 22 or 42 or 43 or 50
        )
            throw new InvalidDataException("Parameter encoding ambiguous/unsupported; no write.");
        var arg = found[0];
        if (snippet && arg.ValueType != SnippetValueType)
            throw new ArgumentException(
                $"CodeSnippet has value type {arg.ValueType}, not the verified snippet type {SnippetValueType}; refusing."
            );
        var before = replacement.GetProperty("expected").GetString()!;
        var after = replacement.GetProperty("value").GetString()!;
        if (arg.Values[0] != before)
            throw new ArgumentException("Parameter expected value differs from source.");
        if (numeric)
        {
            if (
                !double.TryParse(
                    before,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var oldNumber
                )
                || !double.TryParse(
                    after,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var newNumber
                )
                || !double.IsFinite(oldNumber)
                || !double.IsFinite(newNumber)
            )
                throw new ArgumentException("Numeric parameter precondition/value invalid.");
            if (
                key.Equals("EventID", StringComparison.OrdinalIgnoreCase)
                    && (!int.TryParse(after, out var eventId) || eventId < 0)
                || (
                    key.Equals("Player", StringComparison.OrdinalIgnoreCase)
                    || key.EndsWith("PlayerID", StringComparison.OrdinalIgnoreCase)
                ) && (!int.TryParse(after, out var player) || player is < 0 or > 12)
            )
                throw new ArgumentException(
                    $"{key}: value '{after}' is outside the reviewed range (EventID >= 0; player fields 0..12)."
                );
        }
        else
            _ = Text(after, snippet ? CampaignTriggers.MaxChars : 128, key, snippet);
        var valueLength = arg.ValueEnd - arg.ValueStart;
        var trailing = arg.ValueType is 2 or 5 or 8 or 56 ? 1 : 0;
        var originalChars = BinaryPrimitives.ReadInt32LittleEndian(
            source.AsSpan(arg.ValueStart + 4)
        );
        if (originalChars < 0 || 8 + originalChars * 2 + trailing != valueLength)
            throw new InvalidDataException("Unreviewed numeric argument structure; no write.");
        return new(
            arg.ValueStart + 4 - localBase,
            4 + originalChars * 2,
            WideField(
                after,
                numeric ? 32
                    : snippet ? CampaignTriggers.MaxChars
                    : 128,
                key,
                snippet
            )
        );
    }

    /// <summary>Source element indexes of requested duplicates per kind, in creation order (appended after originals).</summary>
    static (int[] Conditions, int[] Effects) Duplicates(
        JsonElement args,
        CampaignTriggers.Trigger trigger,
        HashSet<int> removedEffects,
        HashSet<int>? removedConditions = null
    )
    {
        var conditions = new List<int>();
        var effects = new List<int>();
        if (!args.TryGetProperty("duplicates", out var list))
            return ([], []);
        if (list.GetArrayLength() is < 1 or > MaxDuplicates)
            throw new ArgumentException(
                "duplicates must contain 1.." + MaxDuplicates + " entries."
            );
        foreach (var entry in list.EnumerateArray())
        {
            Catalog.ValidateObject(entry, ["kind", "elementIndex"]);
            var kind = entry.GetProperty("kind").GetString();
            var index = entry.GetProperty("elementIndex").GetInt32();
            var (elements, target) = kind switch
            {
                "effect" => (trigger.Effects, effects),
                "condition" => (trigger.Conditions, conditions),
                _ => throw new ArgumentException("Duplicate kind: condition/effect."),
            };
            if (
                index < 0
                || index >= elements.Length
                || (kind == "effect" && removedEffects.Contains(index))
                || (kind == "condition" && removedConditions?.Contains(index) == true)
            )
                throw new ArgumentException(
                    "Duplicate source must be an existing, non-removed original element."
                );
            target.Add(index);
        }
        return (conditions.ToArray(), effects.ToArray());
    }

    static byte[] BuildRecord(
        CampaignTriggers.Trigger trigger,
        byte[] source,
        JsonElement args,
        bool clone,
        uint newId
    )
    {
        var raw = source.AsSpan(trigger.Start, trigger.End - trigger.Start).ToArray();
        var edits = new List<Change>();
        int Local(int offset) => offset - trigger.Start;
        if (clone)
            edits.Add(new(Local(trigger.IdOffset), 4, BitConverter.GetBytes(newId)));
        if (args.TryGetProperty("newName", out var name))
            edits.Add(
                new(
                    Local(trigger.NameOffset),
                    4 + trigger.NameChars * 2,
                    WideField(name.GetString()!)
                )
            );
        foreach (var (key, index) in new[] { ("loop", 0), ("active", 1) })
            if (args.TryGetProperty(key, out var value))
                edits.Add(
                    new(Local(trigger.FlagsOffset + index), 1, [(byte)(value.GetBoolean() ? 1 : 0)])
                );
        var removed = new HashSet<int>();
        if (args.TryGetProperty("removeEffects", out var removals))
        {
            if (removals.GetArrayLength() == 0)
                throw new ArgumentException("removeEffects must not be empty.");
            foreach (var entry in removals.EnumerateArray())
            {
                var index = entry.GetInt32();
                if (index < 0 || index >= trigger.Effects.Length || !removed.Add(index))
                    throw new ArgumentException("removeEffects index invalid/duplicate.");
                var effect = trigger.Effects[index];
                edits.Add(new(Local(effect.Start), effect.End - effect.Start, []));
            }
        }
        var removedConditions = new HashSet<int>();
        if (args.TryGetProperty("removeConditions", out var conditionRemovals))
        {
            foreach (var entry in conditionRemovals.EnumerateArray())
            {
                var index = entry.GetInt32();
                if (
                    index < 0
                    || index >= trigger.Conditions.Length
                    || !removedConditions.Add(index)
                )
                    throw new ArgumentException("removeConditions index invalid/duplicate.");
                var condition = trigger.Conditions[index];
                edits.Add(new(Local(condition.Start), condition.End - condition.Start, []));
            }
        }
        var (dupConditions, dupEffects) = Duplicates(args, trigger, removed, removedConditions);
        if (trigger.Effects.Length - removed.Count + dupEffects.Length < 1)
            throw new ArgumentException("At least one effect must remain in edited trigger.");
        if (
            trigger.Effects.Length - removed.Count + dupEffects.Length > CampaignTriggers.MaxItems
            || trigger.Conditions.Length - removedConditions.Count + dupConditions.Length
                > CampaignTriggers.MaxItems
        )
            throw new ArgumentException("Edited condition/effect count exceeds reviewed bound.");
        if (removed.Count > 0 || dupEffects.Length > 0)
            edits.Add(
                new(
                    Local(trigger.EffectsCountOffset),
                    4,
                    BitConverter.GetBytes(
                        trigger.Effects.Length - removed.Count + dupEffects.Length
                    )
                )
            );
        var conditionsCountOffset =
            (
                trigger.Conditions.Length > 0
                    ? trigger.Conditions[0].Start
                    : trigger.EffectsCountOffset
            ) - 4;
        if (dupConditions.Length > 0 || removedConditions.Count > 0)
            edits.Add(
                new(
                    Local(conditionsCountOffset),
                    4,
                    BitConverter.GetBytes(
                        trigger.Conditions.Length - removedConditions.Count + dupConditions.Length
                    )
                )
            );
        // Each duplicate starts as the source element's exact bytes; replacements apply relative to that copy.
        var dupChanges = new Dictionary<(string, int), List<Change>>();
        var replaced = new HashSet<string>(StringComparer.Ordinal);
        var relabeled = new HashSet<(string Kind, int Index)>();
        if (args.TryGetProperty("replacements", out var replacements))
        {
            foreach (var replacement in replacements.EnumerateArray())
            {
                Catalog.ValidateObject(
                    replacement,
                    ["kind", "elementIndex", "parameter", "expected", "value"]
                );
                var kind = replacement.GetProperty("kind").GetString();
                var index = replacement.GetProperty("elementIndex").GetInt32();
                var elements = kind switch
                {
                    "effect" => trigger.Effects,
                    "condition" => trigger.Conditions,
                    _ => throw new ArgumentException("Replacement kind: condition/effect."),
                };
                var dups = kind == "effect" ? dupEffects : dupConditions;
                if (
                    index < 0
                    || index >= elements.Length + dups.Length
                    || (kind == "effect" && removed.Contains(index))
                    || (kind == "condition" && removedConditions.Contains(index))
                )
                    throw new ArgumentException("Replacement targets missing/removed element.");
                var key = replacement.GetProperty("parameter").GetString()!;
                if (!replaced.Add(kind + index + ":" + key.ToLowerInvariant()))
                    throw new ArgumentException("Duplicate parameter replacement.");
                if (index < elements.Length)
                {
                    edits.Add(ValueChange(source, elements[index], replacement, trigger.Start));
                    continue;
                }
                var sourceElement = elements[dups[index - elements.Length]];
                if (!dupChanges.TryGetValue((kind!, index), out var local))
                    dupChanges[(kind!, index)] = local = [];
                local.Add(ValueChange(source, sourceElement, replacement, sourceElement.Start));
            }
        }
        if (args.TryGetProperty("labels", out var labels))
            foreach (var label in labels.EnumerateArray())
            {
                Catalog.ValidateObject(label, ["kind", "elementIndex", "expected", "value"]);
                var kind = label.GetProperty("kind").GetString();
                var originals = kind switch
                {
                    "effect" => trigger.Effects,
                    "condition" => trigger.Conditions,
                    _ => throw new ArgumentException("Label kind must be effect/condition."),
                };
                var dups = kind == "effect" ? dupEffects : dupConditions;
                var index = label.GetProperty("elementIndex").GetInt32();
                if (
                    index < 0
                    || index >= originals.Length + dups.Length
                    || (kind == "effect" && removed.Contains(index))
                    || (kind == "condition" && removedConditions.Contains(index))
                )
                    throw new ArgumentException("Label targets missing/removed element.");
                var sourceElement = originals[
                    index < originals.Length ? index : dups[index - originals.Length]
                ];
                var before = label.GetProperty("expected").GetString()!;
                var after = label.GetProperty("value").GetString()!;
                if (!relabeled.Add((kind!, index)))
                    throw new ArgumentException("Duplicate label edit.");
                if (
                    sourceElement.Kind != before
                    || after.Length is < 1 or > 64
                    || after.Any(c => c is < ' ' or > '~')
                )
                    throw new ArgumentException(
                        "Label expected mismatch or new label outside printable ASCII bound."
                    );
                if (sourceElement.Kind == after)
                    throw new ArgumentException("Label must change.");
                var bytes = new byte[4 + after.Length];
                BinaryPrimitives.WriteInt32LittleEndian(bytes, after.Length);
                Encoding.ASCII.GetBytes(after).CopyTo(bytes, 4);
                var change = new Change(
                    sourceElement.LabelOffset
                        - (index < originals.Length ? trigger.Start : sourceElement.Start),
                    sourceElement.LabelBytes,
                    bytes
                );
                if (index < originals.Length)
                    edits.Add(change);
                else
                {
                    if (!dupChanges.TryGetValue((kind!, index), out var local))
                        dupChanges[(kind!, index)] = local = [];
                    local.Add(change);
                }
            }
        byte[] Copies(CampaignTriggers.Element[] elements, int[] dups, string kind) =>
            dups.SelectMany(
                    (src, k) =>
                    {
                        var element = elements[src];
                        var bytes = source
                            .AsSpan(element.Start, element.End - element.Start)
                            .ToArray();
                        return dupChanges.TryGetValue((kind, elements.Length + k), out var local)
                            ? ApplyChanges(bytes, local)
                            : bytes;
                    }
                )
                .ToArray();
        if (dupConditions.Length > 0)
            edits.Add(
                new(
                    Local(trigger.EffectsCountOffset),
                    0,
                    Copies(trigger.Conditions, dupConditions, "condition")
                )
            );
        if (dupEffects.Length > 0)
            edits.Add(new(raw.Length, 0, Copies(trigger.Effects, dupEffects, "effect")));
        return ApplyChanges(raw, edits);
    }

    internal enum Slot
    {
        StringLiteral,
        Raw,
        Unknown,
    }

    /// <summary>XS templates the game expands for an element: condition command, effect expression extras.</summary>
    internal static string[] Templates(CampaignTriggers.Element element) =>
        [
            .. new[] { element.Command }
                .Concat((element.Extras ?? []).Select(x => x.Expression))
                .Where(t => t.Contains('%')),
        ];

    /// <summary>Where %key% sits in the element's templates: inside "..." (string literal) or bare code. Any literal use wins.</summary>
    internal static Slot Classify(IEnumerable<string> templates, string key)
    {
        var found = Slot.Unknown;
        var token = "%" + key + "%";
        foreach (var template in templates)
        {
            var quoted = false;
            for (var i = 0; i < template.Length; i++)
            {
                if (template[i] == '"' && (i == 0 || template[i - 1] != '\\'))
                    quoted = !quoted;
                else if (
                    string.Compare(
                        template,
                        i,
                        token,
                        0,
                        token.Length,
                        StringComparison.OrdinalIgnoreCase
                    ) == 0
                )
                {
                    if (quoted)
                        return Slot.StringLiteral;
                    found = Slot.Raw;
                    i += token.Length - 1;
                }
            }
        }
        return found;
    }

    /// <summary>Templates with every %Key% replaced by the element's stored first value (approximation of the game's expansion).</summary>
    internal static string Render(string template, CampaignTriggers.Element element)
    {
        foreach (var arg in element.Args)
            template = template.Replace(
                "%" + arg.Key + "%",
                arg.Values.FirstOrDefault() ?? "",
                StringComparison.OrdinalIgnoreCase
            );
        return template;
    }

    /// <summary>Unclosed string literal or unbalanced ()/{} in rendered XS; null when balanced.</summary>
    internal static string? Balance(string code)
    {
        var stack = new Stack<char>();
        var quoted = false;
        for (var i = 0; i < code.Length; i++)
        {
            var c = code[i];
            if (c == '"' && (i == 0 || code[i - 1] != '\\'))
            {
                quoted = !quoted;
                continue;
            }
            if (quoted)
                continue;
            if (c is '(' or '{')
                stack.Push(c);
            else if (c is ')' or '}')
            {
                if (stack.Count == 0 || stack.Pop() != (c == ')' ? '(' : '{'))
                    return $"unmatched '{c}' at column {i + 1}";
            }
        }
        return quoted ? "unclosed string literal"
            : stack.Count > 0 ? $"'{stack.Peek()}' is never closed"
            : null;
    }

    /// <summary>
    /// A2 guard: the game writes string parameters as "..." and escapes quotes (\" in trigtemp.xs), so code injected through
    /// a string slot breaks the compile of every trigger. Raw slots (operators) accept code only with allowRawXsInjection.
    /// </summary>
    internal static void GuardValue(
        string key,
        string value,
        Slot slot,
        bool allowRaw,
        List<string> warnings,
        string where
    )
    {
        if (
            NumericKeys.Contains(key, StringComparer.OrdinalIgnoreCase)
            || key.Equals("CodeSnippet", StringComparison.OrdinalIgnoreCase)
        )
            return;
        var literalBreakers = value.IndexOfAny(['"', '\\', '\n', '\r']) >= 0;
        if (slot == Slot.StringLiteral && literalBreakers)
            throw new WorkflowFailure(
                "STRING_PARAM_NOT_CODE",
                "preflight",
                $"{where}: {key} is expanded inside a string literal (\"%{key}%\"); the game escapes quotes/backslashes (trigtemp.xs gets \\\") so injected code breaks the compile of EVERY trigger.",
                false,
                false,
                "Use a plain name. To run code, copy an existing 'XS: Code Snippet' effect (copyEffects or duplicates) and set its CodeSnippet parameter; or byte-copy an element from another export."
            );
        var code = value.IndexOfAny([';', '{', '}', '(', '"']) >= 0;
        if ((slot != Slot.StringLiteral && code) || (slot == Slot.Unknown && literalBreakers))
        {
            if (!allowRaw)
                throw new WorkflowFailure(
                    "RAW_XS_INJECTION",
                    "preflight",
                    $"{where}: {key} value contains XS code characters and is expanded {(slot == Slot.Raw ? "as bare code" : "in an unknown template position")}.",
                    false,
                    false,
                    "Prefer an 'XS: Code Snippet' effect (copyEffects + CodeSnippet). Pass allowRawXsInjection=true only for a deliberate hack and check renderedXs."
                );
            warnings.Add(
                $"{where}: raw XS injected through {key} (unsupported hack). Verify with editor_xs_diagnostics operation=trigtemp after a playtest."
            );
        }
    }

    internal static void InjectionSelfTest()
    {
        string[] qv = ["trQuestVarGet(\"%QVName%\") %Op% %Value%"];
        if (
            Classify(qv, "QVName") != Slot.StringLiteral
            || Classify(qv, "op") != Slot.Raw
            || Classify(qv, "Count") != Slot.Unknown
            || Balance("trQuestVarGet(\"a\\\"); x(\") >= 1") is not null
            || Balance("f(\"a\" ") is null
            || Balance("a)") is null
            || Balance("trQuestVarGet(\"a\") == 1") is not null
        )
            throw new InvalidOperationException(
                "TriggerEdits injection classifier self-test failed."
            );
        var warnings = new List<string>();
        void Refused(Action action, string code)
        {
            try
            {
                action();
            }
            catch (WorkflowFailure e) when (e.Code == code)
            {
                return;
            }
            throw new InvalidOperationException(
                "TriggerEdits guard did not refuse with " + code + "."
            );
        }
        Refused(
            () =>
                GuardValue(
                    "QVName",
                    "a\"); trChatSend(0, \"x",
                    Slot.StringLiteral,
                    true,
                    warnings,
                    "t"
                ),
            "STRING_PARAM_NOT_CODE"
        );
        Refused(
            () => GuardValue("Op", "== 1) && f(", Slot.Raw, false, warnings, "t"),
            "RAW_XS_INJECTION"
        );
        GuardValue("Op", "== 1) && f(", Slot.Raw, true, warnings, "t");
        GuardValue("QVName", "QV_Plain_Name", Slot.StringLiteral, false, warnings, "t");
        GuardValue("Op", ">=", Slot.Raw, false, warnings, "t");
        if (warnings.Count != 1)
            throw new InvalidOperationException(
                "TriggerEdits raw-injection warning self-test failed."
            );
    }

    static (byte[] Output, object Detail, uint SourceId, uint TargetId) ApplyLegacy(
        CampaignTriggers.Document input,
        JsonElement args,
        bool allowRaw = false
    )
    {
        var sourceId = (uint)args.GetProperty("triggerId").GetInt32();
        var trigger =
            input.Triggers.SingleOrDefault(t => t.Id == sourceId)
            ?? throw new ArgumentException("Trigger ID not found in supplied export.");
        if (trigger.Name != args.GetProperty("expectedName").GetString())
            throw new WorkflowFailure(
                "TRIGGER_CHANGED",
                "preflight",
                "Trigger name differs from expectedName.",
                false,
                false,
                "Inspect trigger list/detail and re-preview; no file/game write attempted."
            );
        var clone = args.GetProperty("operation").GetString() == "clone";
        var newId = clone ? (uint)args.GetProperty("newId").GetInt32() : sourceId;
        if (clone && (newId == sourceId || input.Triggers.Any(t => t.Id == newId)))
            throw new ArgumentException("Clone ID must be new and unique.");
        var name = args.TryGetProperty("newName", out var title)
            ? title.GetString()!
            : trigger.Name;
        if (
            clone
            && input.Triggers.Any(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        )
            throw new ArgumentException("Clone name must be new and unique.");
        // Reference graph must be resolvable; clones must explicitly rewire source-self references.
        foreach (var el in trigger.Conditions.Concat(trigger.Effects))
        foreach (
            var a in el.Args.Where(a =>
                a.Key == "EventID"
                && el.Name.StartsWith("Trigger:", StringComparison.OrdinalIgnoreCase)
            )
        )
        foreach (var value in a.Values)
            if (uint.TryParse(value, out var target) && !input.Triggers.Any(t => t.Id == target))
                throw new InvalidDataException(
                    "Unresolved trigger reference " + value + "; no edit."
                );
        var record = BuildRecord(trigger, input.Body, args, clone, newId);
        var changes = new List<Change>();
        if (clone)
        {
            var group =
                input.Groups.SingleOrDefault(g => g.Id == trigger.Group)
                ?? throw new InvalidDataException("Source trigger group missing; clone refused.");
            if (!group.Indexes.Contains(sourceId) || group.Indexes.Contains(newId))
                throw new InvalidDataException(
                    "Source trigger group membership unresolved; clone refused."
                );
            var membersOffset = group.End - group.Indexes.Length * 4 - 4;
            changes.Add(new(membersOffset, 4, BitConverter.GetBytes(group.Indexes.Length + 1)));
            changes.Add(new(group.End, 0, BitConverter.GetBytes(newId)));
            changes.Add(new(input.GroupsStart, 0, record));
            changes.Add(
                new(input.CountOffset, 4, BitConverter.GetBytes(input.Triggers.Length + 1))
            );
        }
        else
            changes.Add(new(trigger.Start, trigger.End - trigger.Start, record));
        var body = ApplyChanges(input.Body, changes);
        var output = new byte[6 + body.Length + input.SuffixBytes];
        input.Original.AsSpan(0, 6).CopyTo(output);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(2), body.Length);
        body.CopyTo(output, 6);
        if (input.SuffixBytes > 0)
            input
                .Original.AsSpan(input.Original.Length - input.SuffixBytes)
                .CopyTo(output.AsSpan(6 + body.Length));
        var parsed = CampaignTriggers.Parse(output);
        var edited = parsed.Triggers.Single(t => t.Id == newId);
        if (
            args.TryGetProperty("loop", out var wantedLoop)
                && (edited.Flags[0] != 0) != wantedLoop.GetBoolean()
            || args.TryGetProperty("active", out var wantedActive)
                && (edited.Flags[1] != 0) != wantedActive.GetBoolean()
        )
            throw new InvalidDataException("Edited flags did not round-trip; no write.");
        var removedIndexes = args.TryGetProperty("removeEffects", out var removedArg)
            ? removedArg.EnumerateArray().Select(v => v.GetInt32()).ToHashSet()
            : [];
        var removedConditionIndexes = args.TryGetProperty(
            "removeConditions",
            out var removedConditionsArg
        )
            ? removedConditionsArg.EnumerateArray().Select(v => v.GetInt32()).ToHashSet()
            : [];
        var rendered = new List<object>();
        var injectionWarnings = new List<string>();
        var renderedKeys = new HashSet<(string?, int)>();
        if (args.TryGetProperty("replacements", out var expectedReplacements))
            foreach (var entry in expectedReplacements.EnumerateArray())
            {
                var originalIndex = entry.GetProperty("elementIndex").GetInt32();
                var index = originalIndex;
                var kind = entry.GetProperty("kind").GetString();
                var actualElements = kind == "effect" ? edited.Effects : edited.Conditions;
                index -= (kind == "effect" ? removedIndexes : removedConditionIndexes).Count(n =>
                    n < index
                );
                var key = entry.GetProperty("parameter").GetString()!;
                var value = entry.GetProperty("value").GetString()!;
                var element = actualElements[index];
                var actual = element
                    .Args.Single(a => a.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                    .Values.Single();
                if (actual != value)
                    throw new InvalidDataException(
                        "Edited parameter did not round-trip; no write."
                    );
                var templates = Templates(element);
                var where = $"trigger {newId} {kind}[{originalIndex}] {element.Name}";
                GuardValue(
                    key,
                    value,
                    Classify(templates, key),
                    allowRaw,
                    injectionWarnings,
                    where
                );
                if (!renderedKeys.Add((kind, originalIndex)))
                    continue;
                foreach (var code in templates.Select(t => Render(t, element)))
                {
                    var problem = Balance(code);
                    if (problem is not null)
                        injectionWarnings.Add(
                            $"{where}: rendered template looks unbalanced on its own ({problem}); unless the game's wrapper balances it, the compile of every trigger breaks."
                        );
                    rendered.Add(
                        new
                        {
                            kind,
                            elementIndex = originalIndex,
                            element = element.Name,
                            xs = code,
                            balanced = problem is null,
                            problem,
                        }
                    );
                }
            }
        var (dupConditions, dupEffects) = Duplicates(
            args,
            trigger,
            removedIndexes,
            removedConditionIndexes
        );
        if (
            edited.Conditions.Length
                != trigger.Conditions.Length - removedConditionIndexes.Count + dupConditions.Length
            || edited.Effects.Length
                != trigger.Effects.Length - removedIndexes.Count + dupEffects.Length
        )
            throw new InvalidDataException(
                "Edited condition/effect counts did not round-trip; no write."
            );
        foreach (
            var (kind, sources, originals, actual, firstNew) in new[]
            {
                (
                    "condition",
                    dupConditions,
                    trigger.Conditions,
                    edited.Conditions,
                    trigger.Conditions.Length - removedConditionIndexes.Count
                ),
                (
                    "effect",
                    dupEffects,
                    trigger.Effects,
                    edited.Effects,
                    trigger.Effects.Length - removedIndexes.Count
                ),
            }
        )
            for (var k = 0; k < sources.Length; k++)
            {
                var src = originals[sources[k]];
                var copy = actual[firstNew + k];
                if (
                    copy.Name != src.Name
                    || copy.Command != src.Command
                    || !copy.Args.Select(a => a.Key).SequenceEqual(src.Args.Select(a => a.Key))
                )
                    throw new InvalidDataException(
                        "Duplicated " + kind + " did not round-trip; no write."
                    );
            }
        if (args.TryGetProperty("labels", out var expectedLabels))
            foreach (var entry in expectedLabels.EnumerateArray())
            {
                var index = entry.GetProperty("elementIndex").GetInt32();
                var kind = entry.GetProperty("kind").GetString();
                index -= (kind == "effect" ? removedIndexes : removedConditionIndexes).Count(n =>
                    n < index
                );
                var actual = (kind == "effect" ? edited.Effects : edited.Conditions)[index].Kind;
                if (actual != entry.GetProperty("value").GetString())
                    throw new InvalidDataException("Edited label did not round-trip; no write.");
            }
        if (
            clone
            && edited
                .Conditions.Concat(edited.Effects)
                .Any(e =>
                    e.Name.StartsWith("Trigger:", StringComparison.OrdinalIgnoreCase)
                    && e.Args.Any(a =>
                        a.Key == "EventID"
                        && a.Values.Contains(
                            sourceId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        )
                    )
                )
        )
            throw new InvalidDataException(
                "Clone retains source-self EventID; explicitly rewire via replacement."
            );
        if (
            parsed.Triggers.Length != input.Triggers.Length + (clone ? 1 : 0)
            || parsed.Triggers.Single(t => t.Id == newId).Name != name
            || !input
                .Original.AsSpan(input.Original.Length - input.SuffixBytes)
                .SequenceEqual(output.AsSpan(output.Length - input.SuffixBytes))
        )
            throw new InvalidDataException(
                "Edited TR structure/identity/suffix verification failed; no write."
            );
        foreach (var before in input.Triggers.Where(t => clone || t.Id != sourceId))
        {
            var after = parsed.Triggers.Single(t => t.Id == before.Id);
            if (
                !input
                    .Body.AsSpan(before.Start, before.End - before.Start)
                    .SequenceEqual(parsed.Body.AsSpan(after.Start, after.End - after.Start))
            )
                throw new InvalidDataException("Unrelated trigger bytes changed; no write.");
        }
        if (input.Groups.Length != parsed.Groups.Length)
            throw new InvalidDataException("Group count changed; no write.");
        foreach (var before in input.Groups)
        {
            var after = parsed.Groups.Single(g => g.Id == before.Id);
            var expectedIndexes =
                clone && before.Id == trigger.Group
                    ? before.Indexes.Append(newId)
                    : before.Indexes.AsEnumerable();
            if (after.Name != before.Name || !after.Indexes.SequenceEqual(expectedIndexes))
                throw new InvalidDataException("Unexpected trigger group mutation; no write.");
        }
        var diff = new List<object>();
        if (clone)
        {
            diff.Add(
                new
                {
                    field = "triggerId",
                    before = sourceId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    after = newId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                }
            );
            var originalGroup = input.Groups.Single(g => g.Id == trigger.Group);
            diff.Add(
                new
                {
                    field = "group["
                        + trigger.Group.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        + "].members",
                    before = originalGroup.Indexes.Length.ToString(
                        System.Globalization.CultureInfo.InvariantCulture
                    ),
                    after = (originalGroup.Indexes.Length + 1).ToString(
                        System.Globalization.CultureInfo.InvariantCulture
                    ),
                }
            );
        }
        if (args.TryGetProperty("newName", out _))
            diff.Add(
                new
                {
                    field = "name",
                    before = trigger.Name,
                    after = name,
                }
            );
        if (args.TryGetProperty("active", out var active))
            diff.Add(
                new
                {
                    field = "active",
                    before = (trigger.Flags[1] != 0).ToString(),
                    after = active.GetBoolean().ToString(),
                }
            );
        if (args.TryGetProperty("loop", out var loop))
            diff.Add(
                new
                {
                    field = "loop",
                    before = (trigger.Flags[0] != 0).ToString(),
                    after = loop.GetBoolean().ToString(),
                }
            );
        if (args.TryGetProperty("replacements", out var changedArgs))
            foreach (var entry in changedArgs.EnumerateArray())
                diff.Add(
                    new
                    {
                        field = entry.GetProperty("kind").GetString()
                            + "["
                            + entry.GetProperty("elementIndex").GetInt32()
                            + "]."
                            + entry.GetProperty("parameter").GetString(),
                        before = entry.GetProperty("expected").GetString(),
                        after = entry.GetProperty("value").GetString(),
                    }
                );
        foreach (var index in removedIndexes.Order())
            diff.Add(
                new
                {
                    field = "effect[" + index + "]",
                    before = trigger.Effects[index].Name,
                    after = "<removed>",
                }
            );
        foreach (var index in removedConditionIndexes.Order())
            diff.Add(
                new
                {
                    field = "condition[" + index + "]",
                    before = trigger.Conditions[index].Name,
                    after = "<removed>",
                }
            );
        if (args.TryGetProperty("labels", out var changedLabels))
            foreach (var entry in changedLabels.EnumerateArray())
                diff.Add(
                    new
                    {
                        field = entry.GetProperty("kind").GetString()
                            + "["
                            + entry.GetProperty("elementIndex").GetInt32()
                            + "].label",
                        before = entry.GetProperty("expected").GetString(),
                        after = entry.GetProperty("value").GetString(),
                    }
                );
        for (var k = 0; k < dupConditions.Length; k++)
            diff.Add(
                new
                {
                    field = "condition[" + (trigger.Conditions.Length + k) + "]",
                    before = "<new>",
                    after = "copy of condition["
                        + dupConditions[k]
                        + "] "
                        + trigger.Conditions[dupConditions[k]].Name,
                }
            );
        for (var k = 0; k < dupEffects.Length; k++)
            diff.Add(
                new
                {
                    field = "effect[" + (trigger.Effects.Length + k) + "]",
                    before = "<new>",
                    after = "copy of effect["
                        + dupEffects[k]
                        + "] "
                        + trigger.Effects[dupEffects[k]].Name,
                }
            );
        return (
            output,
            new
            {
                operation = clone ? "clone" : "patch",
                sourceId,
                targetId = newId,
                targetName = name,
                diff,
                originalRecordSha256 = Convert.ToHexStringLower(
                    SHA256.HashData(input.Body.AsSpan(trigger.Start, trigger.End - trigger.Start))
                ),
                editedRecordSha256 = Convert.ToHexStringLower(
                    SHA256.HashData(parsed.Body.AsSpan(edited.Start, edited.End - edited.Start))
                ),
                effectCount = edited.Effects.Length,
                conditionCount = edited.Conditions.Length,
                renderedXs = rendered,
                injectionWarnings,
            },
            sourceId,
            newId
        );
    }

    internal static (byte[] Output, object Detail, uint SourceId, uint TargetId) Transform(
        CampaignTriggers.Document input,
        JsonElement args,
        CampaignTriggers.Document? immutableSources = null,
        bool allowRawXsInjection = false,
        IReadOnlyDictionary<uint, TriggerObjects.Proof>? proofs = null
    )
    {
        ValidateEdit(args);
        var legacy = ApplyLegacy(input, args, allowRawXsInjection);
        var advanced = TriggerObjects.Apply(
            input,
            legacy.Output,
            args,
            immutableSources ?? input,
            legacy.Detail,
            legacy.TargetId,
            allowRawXsInjection,
            proofs
        );
        return (advanced.Output, advanced.Detail, legacy.SourceId, legacy.TargetId);
    }

    static string[] PrototypeWarnings(JsonElement[] operations)
    {
        var names = operations
            .SelectMany(op =>
                op.TryGetProperty("replacements", out var replacements)
                    ? replacements
                        .EnumerateArray()
                        .Where(r =>
                            r.GetProperty("parameter").GetString() is "ProtoUnit" or "UnitType"
                        )
                        .Select(r => r.GetProperty("value").GetString()!)
                    : []
            )
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (names.Length == 0)
            return [];
        string? file = null;
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "generated", "game_catalog.json");
            if (File.Exists(candidate))
            {
                file = candidate;
                break;
            }
        }
        file ??= Path.Combine(AppContext.BaseDirectory, "game_catalog.json");
        if (!File.Exists(file))
            return
            [
                "Prototype catalog unavailable; names not validated. Run editor_generate_catalog.",
            ];
        using var document = JsonDocument.Parse(File.ReadAllText(file));
        var known = document
            .RootElement.GetProperty("catalogs")
            .GetProperty("prototypes")
            .EnumerateArray()
            .Select(p => p.GetProperty("name").GetString()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return names
            .Where(name => !known.Contains(name))
            .Select(name => "Prototype not found in source catalog: " + name)
            .ToArray();
    }

    // Chain the next edit onto the file just written: pass these as path/expectedSha256 and basePath/baseSha256 (a preview has nothing to chain).
    static object? Next(string? written, string sha256, Lineage lineage) =>
        written is null
            ? null
            : new
            {
                path = written,
                expectedSha256 = sha256,
                lineage.BasePath,
                lineage.BaseSha256,
            };

    /// <summary>The game-written export a chain of edits started from; apply autoBackup compares the fresh backup against it.</summary>
    internal sealed record Lineage(string BasePath, string BaseSha256);

    /// <summary>Base of this edit: the caller's carried basePath/baseSha256 (re-hashed), else the edited file itself.</summary>
    static Lineage BaseOf(JsonElement args, CampaignTriggers.Document input)
    {
        if (!args.TryGetProperty("basePath", out var basePath))
            return new(EditorFiles.LocalPath(args.GetProperty("path").GetString()!), input.Sha256);
        var path = EditorFiles.LocalPath(basePath.GetString()!);
        if (
            !Layout
                .Hash(path)
                .Equals(
                    args.GetProperty("baseSha256").GetString(),
                    StringComparison.OrdinalIgnoreCase
                )
        )
            throw new WorkflowFailure(
                "SOURCE_CHANGED",
                "preflight",
                "Base TR SHA-256 differs from baseSha256.",
                false,
                false,
                "Pass the next.basePath/next.baseSha256 from the first edit of this chain; no write."
            );
        _ = CampaignTriggers.Read(path);
        return new(path, Layout.Hash(path));
    }

    internal static object Edit(JsonElement args)
    {
        Preflight(args);
        var input = CampaignTriggers.Read(args.GetProperty("path").GetString()!);
        if (
            !input.Sha256.Equals(
                args.GetProperty("expectedSha256").GetString(),
                StringComparison.OrdinalIgnoreCase
            )
        )
            throw new WorkflowFailure(
                "SOURCE_CHANGED",
                "preflight",
                "Source TR SHA-256 differs from expectedSha256.",
                false,
                false,
                "Inspect export again; no write or game operation attempted."
            );
        var lineage = BaseOf(args, input);
        var batch = args.TryGetProperty("edits", out var list);
        var operations = batch ? Merge(list.EnumerateArray().ToArray()) : [args];
        var warnings = PrototypeWarnings(operations).ToList();
        if (
            Preview(args)
            && (
                args.TryGetProperty("outputPath", out _)
                || args.TryGetProperty("confirmWrite", out _)
            )
        )
            // Session 2: preview calls carrying outputPath failed validation; accept them, write nothing, say so.
            warnings.Add(
                "preview=true: outputPath/confirmWrite ignored, nothing written. Repeat with preview=false to write."
            );
        var allowRaw = args.TryGetProperty("allowRawXsInjection", out var raw) && raw.GetBoolean();
        var current = input;
        // copyValues needs the exemplar index's value-type proofs; load them once per call.
        var proofs = operations.Any(o => o.TryGetProperty("copyValues", out _))
            ? TriggerObjects.LoadProofs()
            : null;
        var details = new List<object>();
        var affected = new HashSet<uint>();
        foreach (var operation in operations)
        {
            var step = Transform(current, operation, input, allowRaw, proofs);
            affected.Add(step.SourceId);
            details.Add(step.Detail);
            warnings.AddRange(
                JsonSerializer
                    .SerializeToElement(step.Detail)
                    .GetProperty("injectionWarnings")
                    .EnumerateArray()
                    .Select(w => w.GetString()!)
            );
            current = CampaignTriggers.Parse(step.Output);
        }
        // Validate whole batch against initial source, not only each intermediate version.
        foreach (var before in input.Triggers.Where(t => !affected.Contains(t.Id)))
        {
            var after = current.Triggers.Single(t => t.Id == before.Id);
            if (
                !input
                    .Body.AsSpan(before.Start, before.End - before.Start)
                    .SequenceEqual(current.Body.AsSpan(after.Start, after.End - after.Start))
            )
                throw new InvalidDataException("Batch changed unrelated trigger bytes; no write.");
        }
        if (
            !input
                .Original.AsSpan(input.Original.Length - input.SuffixBytes)
                .SequenceEqual(
                    current.Original.AsSpan(current.Original.Length - current.SuffixBytes)
                )
        )
            throw new InvalidDataException("Batch changed TR suffix; no write.");
        var resultSha = Convert.ToHexStringLower(SHA256.HashData(current.Original));
        string? written = null;
        if (!Preview(args))
        {
            written = EditorFiles.ApprovedNewPath(
                EditorFiles.ResolveOutput(
                    args.GetProperty("outputPath").GetString()!,
                    args.GetProperty("path").GetString()!,
                    ".trg"
                ),
                ".trg"
            );
            using (
                var file = new FileStream(
                    written,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None
                )
            )
                file.Write(current.Original);
            if (Layout.Hash(written) != resultSha)
                throw new IOException("New TR output copy verification failed.");
        }
        // Keep legacy shape for single edits; batched calls carry each edit's original/edited record hash and diff.
        var detail = (JsonElement)JsonSerializer.SerializeToElement(details[0]);
        return batch
            ? new
            {
                preview = Preview(args),
                path = written,
                sourceSha256 = input.Sha256,
                sha256 = resultSha,
                next = Next(written, resultSha, lineage),
                basePath = lineage.BasePath,
                baseSha256 = lineage.BaseSha256,
                edits = details,
                warnings,
                originalBytes = input.Original.Length,
                outputBytes = current.Original.Length,
                suffixBytesPreserved = input.SuffixBytes,
                limitation = "Prepared TR file only. No scenario edits; game import and XS effects unverified.",
            }
            : (object)
                new
                {
                    preview = Preview(args),
                    path = written,
                    sourceSha256 = input.Sha256,
                    sha256 = resultSha,
                    next = Next(written, resultSha, lineage),
                    basePath = lineage.BasePath,
                    baseSha256 = lineage.BaseSha256,
                    operation = detail.GetProperty("operation").GetString(),
                    sourceId = detail.GetProperty("sourceId").GetUInt32(),
                    targetId = detail.GetProperty("targetId").GetUInt32(),
                    targetName = detail.GetProperty("targetName").GetString(),
                    diff = detail.GetProperty("diff"),
                    warnings,
                    renderedXs = detail.GetProperty("renderedXs"),
                    originalRecordSha256 = detail.GetProperty("originalRecordSha256").GetString(),
                    editedRecordSha256 = detail.GetProperty("editedRecordSha256").GetString(),
                    originalBytes = input.Original.Length,
                    outputBytes = current.Original.Length,
                    suffixBytesPreserved = input.SuffixBytes,
                    effectCount = detail.GetProperty("effectCount").GetInt32(),
                    conditionCount = detail.GetProperty("conditionCount").GetInt32(),
                    limitation = "Prepared TR file only. No .mythscn edits; game import may replace whole trigger set. No XS compilation/effect proof.",
                };
    }
}
