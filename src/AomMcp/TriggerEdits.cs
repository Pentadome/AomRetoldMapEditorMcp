using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AomMcp;

/// <summary>Preview-first, byte-preserving edits of reviewed TR v12 fields. Never touches a scenario file.</summary>
internal static class TriggerEdits
{
    sealed record Change(int Offset, int Length, byte[] Bytes);
    static readonly UnicodeEncoding Wide = new(false, false, true);
    static readonly string[] NumericKeys = ["Player", "PlayerID", "FromPlayerID", "ToPlayerID", "EventID", "TechID", "Count", "Dist", "Status", "Value", "Duration"];
    static readonly string[] StringKeys = ["ProtoUnit", "UnitType", "Command", "QVName", "Op"];

    internal static void Preflight(JsonElement args)
    {
        var batch = args.TryGetProperty("edits", out var list);
        if (batch)
        {
            Catalog.ValidateObject(args, Preview(args)
                ? ["path", "expectedSha256", "edits", "preview"]
                : ["path", "expectedSha256", "edits", "preview", "outputPath", "confirmWrite"]);
            if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() is < 1 or > 64)
                throw new ArgumentException("edits must contain 1..64 operations.");
            var seen = new HashSet<int>();
            var newIds = new HashSet<int>();
            foreach (var entry in list.EnumerateArray())
            {
                ValidateEdit(entry);
                if (!seen.Add(entry.GetProperty("triggerId").GetInt32()))
                    throw new ArgumentException("Each source trigger may be edited only once per batch.");
                if (entry.GetProperty("operation").GetString() == "clone" && !newIds.Add(entry.GetProperty("newId").GetInt32()))
                    throw new ArgumentException("Duplicate clone ID in batch.");
            }
            if (newIds.Overlaps(seen)) throw new ArgumentException("Clone ID collides with batch source ID.");
        }
        else
        {
            Catalog.ValidateObject(args, Preview(args)
                ? ["operation", "path", "triggerId", "expectedSha256", "expectedName", "newId", "newName", "active", "loop", "replacements", "removeEffects", "removeConditions", "labels", "duplicates", "preview"]
                : ["operation", "path", "triggerId", "expectedSha256", "expectedName", "newId", "newName", "active", "loop", "replacements", "removeEffects", "removeConditions", "labels", "duplicates", "preview", "outputPath", "confirmWrite"]);
            ValidateEdit(args);
        }
        _ = TriggerCodec.ReadFile(args.GetProperty("path").GetString()!);
        if (args.GetProperty("expectedSha256").GetString() is not { Length: 64 } sha || !sha.All(Uri.IsHexDigit))
            throw new ArgumentException("expectedSha256 must be source file's 64-digit SHA-256.");
        if (!Preview(args))
        {
            EditorFiles.Confirm(args, "confirmWrite");
            if (!args.TryGetProperty("outputPath", out var path)) throw new ArgumentException("Non-preview edit requires outputPath.");
            EditorFiles.ApprovedNewPath(path.GetString()!, ".trg");
        }
    }

    static void ValidateEdit(JsonElement entry)
    {
        Catalog.ValidateObject(entry, ["operation", "triggerId", "expectedName", "newId", "newName", "active", "loop",
            "replacements", "removeEffects", "removeConditions", "labels", "duplicates", "path", "expectedSha256", "preview", "outputPath", "confirmWrite"]);
        var op = entry.GetProperty("operation").GetString();
        if (op is not "patch" and not "clone") throw new ArgumentException("Trigger edit operation: patch/clone.");
        if (entry.GetProperty("triggerId").GetInt32() < 0) throw new ArgumentException("triggerId must be nonnegative.");
        if (string.IsNullOrWhiteSpace(entry.GetProperty("expectedName").GetString()))
            throw new ArgumentException("expectedName must identify source trigger explicitly.");
        if (op == "clone" && (!entry.TryGetProperty("newId", out var id) || id.GetInt32() < 0 || !entry.TryGetProperty("newName", out _)))
            throw new ArgumentException("Clone requires nonnegative newId and newName.");
        if (op == "patch" && entry.TryGetProperty("newId", out _)) throw new ArgumentException("Only clone can assign a new trigger ID.");
    }

    static bool Preview(JsonElement args) => !args.TryGetProperty("preview", out var p) || p.GetBoolean();

    static byte[] Text(string value, int max = 128)
    {
        if (value.Length is < 1 || value.Length > max || value.Any(char.IsControl))
            throw new ArgumentException("Trigger text outside reviewed bounds.");
        return Wide.GetBytes(value);
    }

    static byte[] WideField(string value, int max = 128)
    {
        var text = Text(value, max);
        var result = new byte[4 + text.Length];
        BinaryPrimitives.WriteInt32LittleEndian(result, value.Length);
        text.CopyTo(result, 4);
        return result;
    }

    static byte[] ApplyChanges(byte[] source, IEnumerable<Change> changes)
    {
        var sorted = changes.OrderByDescending(c => c.Offset).ThenByDescending(c => c.Length).ToArray();
        var previous = source.Length + 1;
        foreach (var edit in sorted)
        {
            if (edit.Offset < 0 || edit.Length < 0 || edit.Offset + edit.Length > source.Length || edit.Offset + edit.Length > previous)
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
    static Change ValueChange(byte[] source, CampaignTriggers.Element element, JsonElement replacement, int localBase)
    {
        var key = replacement.GetProperty("parameter").GetString()!;
        var numeric = NumericKeys.Contains(key, StringComparer.OrdinalIgnoreCase);
        if (!numeric && !StringKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Parameter not in reviewed numeric/string allowlist.");
        var found = element.Args.Where(a => a.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (found.Length != 1 || found[0].Values.Length != 1 || found[0].ValueType is 4 or 7 or 22 or 42 or 43 or 50)
            throw new InvalidDataException("Parameter encoding ambiguous/unsupported; no write.");
        var arg = found[0];
        var before = replacement.GetProperty("expected").GetString()!;
        var after = replacement.GetProperty("value").GetString()!;
        if (arg.Values[0] != before) throw new ArgumentException("Parameter expected value differs from source.");
        if (numeric)
        {
            if (!double.TryParse(before, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _)
                || !double.TryParse(after, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
                throw new ArgumentException("Numeric parameter precondition/value invalid.");
            if (key.Equals("EventID", StringComparison.OrdinalIgnoreCase) && (!int.TryParse(after, out var eventId) || eventId < 0)
                || (key.Equals("Player", StringComparison.OrdinalIgnoreCase) || key.EndsWith("PlayerID", StringComparison.OrdinalIgnoreCase))
                    && (!int.TryParse(after, out var player) || player is < 0 or > 12))
                throw new ArgumentException("EventID/player outside reviewed bounds.");
        }
        else _ = Text(after);
        var valueLength = arg.ValueEnd - arg.ValueStart;
        var trailing = arg.ValueType is 2 or 5 or 8 or 56 ? 1 : 0;
        var originalChars = BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(arg.ValueStart + 4));
        if (originalChars < 0 || 8 + originalChars * 2 + trailing != valueLength)
            throw new InvalidDataException("Unreviewed numeric argument structure; no write.");
        return new(arg.ValueStart + 4 - localBase, 4 + originalChars * 2, WideField(after, numeric ? 32 : 128));
    }

    /// <summary>Source element indexes of requested duplicates per kind, in creation order (appended after originals).</summary>
    static (int[] Conditions, int[] Effects) Duplicates(JsonElement args, CampaignTriggers.Trigger trigger, HashSet<int> removedEffects, HashSet<int>? removedConditions = null)
    {
        var conditions = new List<int>(); var effects = new List<int>();
        if (!args.TryGetProperty("duplicates", out var list)) return ([], []);
        if (list.GetArrayLength() is < 1 or > MaxDuplicates) throw new ArgumentException("duplicates must contain 1.." + MaxDuplicates + " entries.");
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
            if (index < 0 || index >= elements.Length || (kind == "effect" && removedEffects.Contains(index))
                || (kind == "condition" && removedConditions?.Contains(index) == true))
                throw new ArgumentException("Duplicate source must be an existing, non-removed original element.");
            target.Add(index);
        }
        return (conditions.ToArray(), effects.ToArray());
    }

    static byte[] BuildRecord(CampaignTriggers.Trigger trigger, byte[] source, JsonElement args, bool clone, uint newId)
    {
        var raw = source.AsSpan(trigger.Start, trigger.End - trigger.Start).ToArray();
        var edits = new List<Change>();
        int Local(int offset) => offset - trigger.Start;
        if (clone) edits.Add(new(Local(trigger.IdOffset), 4, BitConverter.GetBytes(newId)));
        if (args.TryGetProperty("newName", out var name))
            edits.Add(new(Local(trigger.NameOffset), 4 + trigger.NameChars * 2, WideField(name.GetString()!)));
        foreach (var (key, index) in new[] { ("loop", 0), ("active", 1) })
            if (args.TryGetProperty(key, out var value))
                edits.Add(new(Local(trigger.FlagsOffset + index), 1, [(byte)(value.GetBoolean() ? 1 : 0)]));
        var removed = new HashSet<int>();
        if (args.TryGetProperty("removeEffects", out var removals))
        {
            if (removals.GetArrayLength() == 0) throw new ArgumentException("removeEffects must not be empty.");
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
                if (index < 0 || index >= trigger.Conditions.Length || !removedConditions.Add(index))
                    throw new ArgumentException("removeConditions index invalid/duplicate.");
                var condition = trigger.Conditions[index];
                edits.Add(new(Local(condition.Start), condition.End - condition.Start, []));
            }
        }
        var (dupConditions, dupEffects) = Duplicates(args, trigger, removed, removedConditions);
        if (trigger.Effects.Length - removed.Count + dupEffects.Length < 1)
            throw new ArgumentException("At least one effect must remain in edited trigger.");
        if (trigger.Effects.Length - removed.Count + dupEffects.Length > CampaignTriggers.MaxItems
            || trigger.Conditions.Length - removedConditions.Count + dupConditions.Length > CampaignTriggers.MaxItems
            || trigger.Conditions.Length - removedConditions.Count + dupConditions.Length < 1)
            throw new ArgumentException("Edited condition/effect count exceeds reviewed bound.");
        if (removed.Count > 0 || dupEffects.Length > 0)
            edits.Add(new(Local(trigger.EffectsCountOffset), 4, BitConverter.GetBytes(trigger.Effects.Length - removed.Count + dupEffects.Length)));
        var conditionsCountOffset = (trigger.Conditions.Length > 0 ? trigger.Conditions[0].Start : trigger.EffectsCountOffset) - 4;
        if (dupConditions.Length > 0 || removedConditions.Count > 0)
            edits.Add(new(Local(conditionsCountOffset), 4, BitConverter.GetBytes(trigger.Conditions.Length - removedConditions.Count + dupConditions.Length)));
        // Each duplicate starts as the source element's exact bytes; replacements apply relative to that copy.
        var dupChanges = new Dictionary<(string, int), List<Change>>();
        var replaced = new HashSet<string>(StringComparer.Ordinal);
        var relabeled = new HashSet<(string Kind, int Index)>();
        if (args.TryGetProperty("replacements", out var replacements))
        {
            foreach (var replacement in replacements.EnumerateArray())
            {
                Catalog.ValidateObject(replacement, ["kind", "elementIndex", "parameter", "expected", "value"]);
                var kind = replacement.GetProperty("kind").GetString();
                var index = replacement.GetProperty("elementIndex").GetInt32();
                var elements = kind switch
                {
                    "effect" => trigger.Effects,
                    "condition" => trigger.Conditions,
                    _ => throw new ArgumentException("Replacement kind: condition/effect."),
                };
                var dups = kind == "effect" ? dupEffects : dupConditions;
                if (index < 0 || index >= elements.Length + dups.Length || (kind == "effect" && removed.Contains(index))
                    || (kind == "condition" && removedConditions.Contains(index)))
                    throw new ArgumentException("Replacement targets missing/removed element.");
                var key = replacement.GetProperty("parameter").GetString()!;
                if (!replaced.Add(kind + index + ":" + key.ToLowerInvariant())) throw new ArgumentException("Duplicate parameter replacement.");
                if (index < elements.Length)
                {
                    edits.Add(ValueChange(source, elements[index], replacement, trigger.Start));
                    continue;
                }
                var sourceElement = elements[dups[index - elements.Length]];
                if (!dupChanges.TryGetValue((kind!, index), out var local)) dupChanges[(kind!, index)] = local = [];
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
                    "effect" => trigger.Effects, "condition" => trigger.Conditions,
                    _ => throw new ArgumentException("Label kind must be effect/condition."),
                };
                var dups = kind == "effect" ? dupEffects : dupConditions;
                var index = label.GetProperty("elementIndex").GetInt32();
                if (index < 0 || index >= originals.Length + dups.Length
                    || (kind == "effect" && removed.Contains(index)) || (kind == "condition" && removedConditions.Contains(index)))
                    throw new ArgumentException("Label targets missing/removed element.");
                var sourceElement = originals[index < originals.Length ? index : dups[index - originals.Length]];
                var before = label.GetProperty("expected").GetString()!;
                var after = label.GetProperty("value").GetString()!;
                if (!relabeled.Add((kind!, index))) throw new ArgumentException("Duplicate label edit.");
                if (sourceElement.Kind != before || after.Length is < 1 or > 64 || after.Any(c => c is < ' ' or > '~'))
                    throw new ArgumentException("Label expected mismatch or new label outside printable ASCII bound.");
                if (sourceElement.Kind == after) throw new ArgumentException("Label must change.");
                var bytes = new byte[4 + after.Length];
                BinaryPrimitives.WriteInt32LittleEndian(bytes, after.Length);
                Encoding.ASCII.GetBytes(after).CopyTo(bytes, 4);
                var change = new Change(sourceElement.LabelOffset - (index < originals.Length ? trigger.Start : sourceElement.Start),
                    sourceElement.LabelBytes, bytes);
                if (index < originals.Length) edits.Add(change);
                else
                {
                    if (!dupChanges.TryGetValue((kind!, index), out var local)) dupChanges[(kind!, index)] = local = [];
                    local.Add(change);
                }
            }
        byte[] Copies(CampaignTriggers.Element[] elements, int[] dups, string kind) => dups.SelectMany((src, k) =>
        {
            var element = elements[src];
            var bytes = source.AsSpan(element.Start, element.End - element.Start).ToArray();
            return dupChanges.TryGetValue((kind, elements.Length + k), out var local) ? ApplyChanges(bytes, local) : bytes;
        }).ToArray();
        if (dupConditions.Length > 0)
            edits.Add(new(Local(trigger.EffectsCountOffset), 0, Copies(trigger.Conditions, dupConditions, "condition")));
        if (dupEffects.Length > 0)
            edits.Add(new(raw.Length, 0, Copies(trigger.Effects, dupEffects, "effect")));
        return ApplyChanges(raw, edits);
    }

    static (byte[] Output, object Detail, uint SourceId, uint TargetId) ApplyOne(CampaignTriggers.Document input, JsonElement args)
    {
        var sourceId = (uint)args.GetProperty("triggerId").GetInt32();
        var trigger = input.Triggers.SingleOrDefault(t => t.Id == sourceId)
            ?? throw new ArgumentException("Trigger ID not found in supplied export.");
        if (trigger.Name != args.GetProperty("expectedName").GetString())
            throw new WorkflowFailure("TRIGGER_CHANGED", "preflight", "Trigger name differs from expectedName.", false, false,
                "Inspect trigger list/detail and re-preview; no file/game write attempted.");
        var clone = args.GetProperty("operation").GetString() == "clone";
        var newId = clone ? (uint)args.GetProperty("newId").GetInt32() : sourceId;
        if (clone && (newId == sourceId || input.Triggers.Any(t => t.Id == newId)))
            throw new ArgumentException("Clone ID must be new and unique.");
        var name = args.TryGetProperty("newName", out var title) ? title.GetString()! : trigger.Name;
        if (clone && input.Triggers.Any(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Clone name must be new and unique.");
        // Reference graph must be resolvable; clones must explicitly rewire source-self references.
        foreach (var el in trigger.Conditions.Concat(trigger.Effects))
            foreach (var a in el.Args.Where(a => a.Key == "EventID" && el.Name.StartsWith("Trigger:", StringComparison.OrdinalIgnoreCase)))
                foreach (var value in a.Values)
                    if (uint.TryParse(value, out var target) && !input.Triggers.Any(t => t.Id == target))
                        throw new InvalidDataException("Unresolved trigger reference " + value + "; no edit.");
        var record = BuildRecord(trigger, input.Body, args, clone, newId);
        var changes = new List<Change>();
        if (clone)
        {
            var group = input.Groups.SingleOrDefault(g => g.Id == trigger.Group)
                ?? throw new InvalidDataException("Source trigger group missing; clone refused.");
            if (!group.Indexes.Contains(sourceId) || group.Indexes.Contains(newId))
                throw new InvalidDataException("Source trigger group membership unresolved; clone refused.");
            var membersOffset = group.End - group.Indexes.Length * 4 - 4;
            changes.Add(new(membersOffset, 4, BitConverter.GetBytes(group.Indexes.Length + 1)));
            changes.Add(new(group.End, 0, BitConverter.GetBytes(newId)));
            changes.Add(new(input.GroupsStart, 0, record));
            changes.Add(new(input.CountOffset, 4, BitConverter.GetBytes(input.Triggers.Length + 1)));
        }
        else changes.Add(new(trigger.Start, trigger.End - trigger.Start, record));
        var body = ApplyChanges(input.Body, changes);
        var output = new byte[6 + body.Length + input.SuffixBytes];
        input.Original.AsSpan(0, 6).CopyTo(output);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(2), body.Length);
        body.CopyTo(output, 6);
        if (input.SuffixBytes > 0)
            input.Original.AsSpan(input.Original.Length - input.SuffixBytes).CopyTo(output.AsSpan(6 + body.Length));
        var parsed = CampaignTriggers.Parse(output);
        var edited = parsed.Triggers.Single(t => t.Id == newId);
        if (args.TryGetProperty("loop", out var wantedLoop) && (edited.Flags[0] != 0) != wantedLoop.GetBoolean()
            || args.TryGetProperty("active", out var wantedActive) && (edited.Flags[1] != 0) != wantedActive.GetBoolean())
            throw new InvalidDataException("Edited flags did not round-trip; no write.");
        var removedIndexes = args.TryGetProperty("removeEffects", out var removedArg)
            ? removedArg.EnumerateArray().Select(v => v.GetInt32()).ToHashSet() : [];
        var removedConditionIndexes = args.TryGetProperty("removeConditions", out var removedConditionsArg)
            ? removedConditionsArg.EnumerateArray().Select(v => v.GetInt32()).ToHashSet() : [];
        if (args.TryGetProperty("replacements", out var expectedReplacements))
            foreach (var entry in expectedReplacements.EnumerateArray())
            {
                var index = entry.GetProperty("elementIndex").GetInt32();
                var kind = entry.GetProperty("kind").GetString();
                var actualElements = kind == "effect" ? edited.Effects : edited.Conditions;
                index -= (kind == "effect" ? removedIndexes : removedConditionIndexes).Count(n => n < index);
                var actual = actualElements[index].Args.Single(a => a.Key.Equals(entry.GetProperty("parameter").GetString(), StringComparison.OrdinalIgnoreCase)).Values.Single();
                if (actual != entry.GetProperty("value").GetString())
                    throw new InvalidDataException("Edited parameter did not round-trip; no write.");
            }
        var (dupConditions, dupEffects) = Duplicates(args, trigger, removedIndexes, removedConditionIndexes);
        if (edited.Conditions.Length != trigger.Conditions.Length - removedConditionIndexes.Count + dupConditions.Length
            || edited.Effects.Length != trigger.Effects.Length - removedIndexes.Count + dupEffects.Length)
            throw new InvalidDataException("Edited condition/effect counts did not round-trip; no write.");
        foreach (var (kind, sources, originals, actual, firstNew) in new[]
        {
            ("condition", dupConditions, trigger.Conditions, edited.Conditions, trigger.Conditions.Length - removedConditionIndexes.Count),
            ("effect", dupEffects, trigger.Effects, edited.Effects, trigger.Effects.Length - removedIndexes.Count),
        })
            for (var k = 0; k < sources.Length; k++)
            {
                var src = originals[sources[k]]; var copy = actual[firstNew + k];
                if (copy.Name != src.Name || copy.Command != src.Command
                    || !copy.Args.Select(a => a.Key).SequenceEqual(src.Args.Select(a => a.Key)))
                    throw new InvalidDataException("Duplicated " + kind + " did not round-trip; no write.");
            }
        if (args.TryGetProperty("labels", out var expectedLabels))
            foreach (var entry in expectedLabels.EnumerateArray())
            {
                var index = entry.GetProperty("elementIndex").GetInt32();
                var kind = entry.GetProperty("kind").GetString();
                index -= (kind == "effect" ? removedIndexes : removedConditionIndexes).Count(n => n < index);
                var actual = (kind == "effect" ? edited.Effects : edited.Conditions)[index].Kind;
                if (actual != entry.GetProperty("value").GetString())
                    throw new InvalidDataException("Edited label did not round-trip; no write.");
            }
        if (clone && edited.Conditions.Concat(edited.Effects).Any(e => e.Name.StartsWith("Trigger:", StringComparison.OrdinalIgnoreCase)
                && e.Args.Any(a => a.Key == "EventID" && a.Values.Contains(sourceId.ToString(System.Globalization.CultureInfo.InvariantCulture)))))
            throw new InvalidDataException("Clone retains source-self EventID; explicitly rewire via replacement.");
        if (parsed.Triggers.Length != input.Triggers.Length + (clone ? 1 : 0)
            || parsed.Triggers.Single(t => t.Id == newId).Name != name
            || !input.Original.AsSpan(input.Original.Length - input.SuffixBytes).SequenceEqual(output.AsSpan(output.Length - input.SuffixBytes)))
            throw new InvalidDataException("Edited TR structure/identity/suffix verification failed; no write.");
        foreach (var before in input.Triggers.Where(t => clone || t.Id != sourceId))
        {
            var after = parsed.Triggers.Single(t => t.Id == before.Id);
            if (!input.Body.AsSpan(before.Start, before.End - before.Start)
                .SequenceEqual(parsed.Body.AsSpan(after.Start, after.End - after.Start)))
                throw new InvalidDataException("Unrelated trigger bytes changed; no write.");
        }
        if (input.Groups.Length != parsed.Groups.Length) throw new InvalidDataException("Group count changed; no write.");
        foreach (var before in input.Groups)
        {
            var after = parsed.Groups.Single(g => g.Id == before.Id);
            var expectedIndexes = clone && before.Id == trigger.Group ? before.Indexes.Append(newId) : before.Indexes.AsEnumerable();
            if (after.Name != before.Name || !after.Indexes.SequenceEqual(expectedIndexes))
                throw new InvalidDataException("Unexpected trigger group mutation; no write.");
        }
        var diff = new List<object>();
        if (clone)
        {
            diff.Add(new { field = "triggerId", before = sourceId.ToString(System.Globalization.CultureInfo.InvariantCulture), after = newId.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            var originalGroup = input.Groups.Single(g => g.Id == trigger.Group);
            diff.Add(new { field = "group[" + trigger.Group.ToString(System.Globalization.CultureInfo.InvariantCulture) + "].members",
                before = originalGroup.Indexes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
                after = (originalGroup.Indexes.Length + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) });
        }
        if (args.TryGetProperty("newName", out _)) diff.Add(new { field = "name", before = trigger.Name, after = name });
        if (args.TryGetProperty("active", out var active)) diff.Add(new { field = "active", before = (trigger.Flags[1] != 0).ToString(), after = active.GetBoolean().ToString() });
        if (args.TryGetProperty("loop", out var loop)) diff.Add(new { field = "loop", before = (trigger.Flags[0] != 0).ToString(), after = loop.GetBoolean().ToString() });
        if (args.TryGetProperty("replacements", out var changedArgs))
            foreach (var entry in changedArgs.EnumerateArray())
                diff.Add(new { field = entry.GetProperty("kind").GetString() + "[" + entry.GetProperty("elementIndex").GetInt32() + "]." + entry.GetProperty("parameter").GetString(),
                    before = entry.GetProperty("expected").GetString(), after = entry.GetProperty("value").GetString() });
        foreach (var index in removedIndexes.Order())
            diff.Add(new { field = "effect[" + index + "]", before = trigger.Effects[index].Name, after = "<removed>" });
        foreach (var index in removedConditionIndexes.Order())
            diff.Add(new { field = "condition[" + index + "]", before = trigger.Conditions[index].Name, after = "<removed>" });
        if (args.TryGetProperty("labels", out var changedLabels))
            foreach (var entry in changedLabels.EnumerateArray())
                diff.Add(new { field = entry.GetProperty("kind").GetString() + "[" + entry.GetProperty("elementIndex").GetInt32() + "].label",
                    before = entry.GetProperty("expected").GetString(), after = entry.GetProperty("value").GetString() });
        for (var k = 0; k < dupConditions.Length; k++)
            diff.Add(new { field = "condition[" + (trigger.Conditions.Length + k) + "]", before = "<new>",
                after = "copy of condition[" + dupConditions[k] + "] " + trigger.Conditions[dupConditions[k]].Name });
        for (var k = 0; k < dupEffects.Length; k++)
            diff.Add(new { field = "effect[" + (trigger.Effects.Length + k) + "]", before = "<new>",
                after = "copy of effect[" + dupEffects[k] + "] " + trigger.Effects[dupEffects[k]].Name });
        return (output, new {
            operation = clone ? "clone" : "patch", sourceId, targetId = newId, targetName = name,
            diff, originalRecordSha256 = Convert.ToHexStringLower(SHA256.HashData(input.Body.AsSpan(trigger.Start, trigger.End - trigger.Start))),
            editedRecordSha256 = Convert.ToHexStringLower(SHA256.HashData(parsed.Body.AsSpan(edited.Start, edited.End - edited.Start))),
            effectCount = edited.Effects.Length, conditionCount = edited.Conditions.Length,
        }, sourceId, newId);
    }

    static string[] PrototypeWarnings(JsonElement[] operations)
    {
        var names = operations.SelectMany(op => op.TryGetProperty("replacements", out var replacements)
            ? replacements.EnumerateArray().Where(r => r.GetProperty("parameter").GetString() is "ProtoUnit" or "UnitType")
                .Select(r => r.GetProperty("value").GetString()!) : []).Distinct(StringComparer.Ordinal).ToArray();
        if (names.Length == 0) return [];
        string? file = null;
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "generated", "game_catalog.json");
            if (File.Exists(candidate)) { file = candidate; break; }
        }
        file ??= Path.Combine(AppContext.BaseDirectory, "game_catalog.json");
        if (!File.Exists(file)) return ["Prototype catalog unavailable; names not validated."];
        using var document = JsonDocument.Parse(File.ReadAllText(file));
        var known = document.RootElement.GetProperty("catalogs").GetProperty("prototypes")
            .EnumerateArray().Select(p => p.GetProperty("name").GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return names.Where(name => !known.Contains(name)).Select(name => "Prototype not found in source catalog: " + name).ToArray();
    }

    internal static object Edit(JsonElement args)
    {
        Preflight(args);
        var input = CampaignTriggers.Read(args.GetProperty("path").GetString()!);
        if (!input.Sha256.Equals(args.GetProperty("expectedSha256").GetString(), StringComparison.OrdinalIgnoreCase))
            throw new WorkflowFailure("SOURCE_CHANGED", "preflight", "Source TR SHA-256 differs from expectedSha256.", false, false,
                "Inspect export again; no write or game operation attempted.");
        var batch = args.TryGetProperty("edits", out var list);
        var operations = batch ? list.EnumerateArray().ToArray() : [args];
        var warnings = PrototypeWarnings(operations);
        var current = input;
        var details = new List<object>();
        var affected = new HashSet<uint>();
        foreach (var operation in operations)
        {
            var step = ApplyOne(current, operation);
            affected.Add(step.SourceId);
            details.Add(step.Detail);
            current = CampaignTriggers.Parse(step.Output);
        }
        // Validate whole batch against initial source, not only each intermediate version.
        foreach (var before in input.Triggers.Where(t => !affected.Contains(t.Id)))
        {
            var after = current.Triggers.Single(t => t.Id == before.Id);
            if (!input.Body.AsSpan(before.Start, before.End - before.Start)
                .SequenceEqual(current.Body.AsSpan(after.Start, after.End - after.Start)))
                throw new InvalidDataException("Batch changed unrelated trigger bytes; no write.");
        }
        if (!input.Original.AsSpan(input.Original.Length - input.SuffixBytes)
            .SequenceEqual(current.Original.AsSpan(current.Original.Length - current.SuffixBytes)))
            throw new InvalidDataException("Batch changed TR suffix; no write.");
        var resultSha = Convert.ToHexStringLower(SHA256.HashData(current.Original));
        string? written = null;
        if (!Preview(args))
        {
            written = EditorFiles.ApprovedNewPath(args.GetProperty("outputPath").GetString()!, ".trg");
            using (var file = new FileStream(written, FileMode.CreateNew, FileAccess.Write, FileShare.None)) file.Write(current.Original);
            if (Layout.Hash(written) != resultSha) throw new IOException("New TR output copy verification failed.");
        }
        // Keep legacy shape for single edits; batched calls carry each edit's original/edited record hash and diff.
        var detail = (JsonElement)JsonSerializer.SerializeToElement(details[0]);
        return batch
            ? new { preview = Preview(args), path = written, sourceSha256 = input.Sha256, sha256 = resultSha,
                edits = details, warnings, originalBytes = input.Original.Length, outputBytes = current.Original.Length,
                suffixBytesPreserved = input.SuffixBytes,
                limitation = "Prepared TR file only. No scenario edits; game import and XS effects unverified." }
            : (object)new { preview = Preview(args), path = written, sourceSha256 = input.Sha256, sha256 = resultSha,
                operation = detail.GetProperty("operation").GetString(), sourceId = detail.GetProperty("sourceId").GetUInt32(),
                targetId = detail.GetProperty("targetId").GetUInt32(), targetName = detail.GetProperty("targetName").GetString(),
                diff = detail.GetProperty("diff"), warnings, originalRecordSha256 = detail.GetProperty("originalRecordSha256").GetString(),
                editedRecordSha256 = detail.GetProperty("editedRecordSha256").GetString(),
                originalBytes = input.Original.Length, outputBytes = current.Original.Length, suffixBytesPreserved = input.SuffixBytes,
                effectCount = detail.GetProperty("effectCount").GetInt32(), conditionCount = detail.GetProperty("conditionCount").GetInt32(),
                limitation = "Prepared TR file only. No .mythscn edits; game import may replace whole trigger set. No XS compilation/effect proof." };
    }
}
