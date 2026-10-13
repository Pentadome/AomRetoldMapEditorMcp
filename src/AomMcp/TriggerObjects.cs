using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace AomMcp;

/// <summary>Reviewed type-4 encoding and ordered effect composition shared by edits and startup planning.</summary>
internal static class TriggerObjects
{
    internal const int MaxObjects = 200,
        MaxCopies = 32;

    /// <summary>Maximum copyValues entries per operation (same bound as replacements).</summary>
    internal const int MaxCopyValues = 200;

    /// <summary>Trigger section version a cross-file source must carry (TR v12, see <see cref="CampaignTriggers"/>).</summary>
    const uint SourceTriggerVersion = 12;

    /// <summary>Byte width of the TR section version field.</summary>
    const int VersionBytes = 4;

    /// <summary>Scenario file extension; other copy sources are read as .trg exports.</summary>
    const string ScenarioExtension = ".mythscn";

    /// <summary>Object-list value type (unit tuples); edited through objectReplacements, never copyValues.</summary>
    const uint ObjectListType = 4;

    /// <summary>Stored value meaning "no trigger" in EventID arguments.</summary>
    const string NoEvent = "-1";

    /// <summary>Parameter key holding trigger ids.</summary>
    const string EventKey = "EventID";

    /// <summary>Element-name prefix of elements whose EventID names another trigger (activate/deactivate/run).</summary>
    const string TriggerPrefix = "Trigger:";

    /// <summary>Per-value-type re-encode proof from the exemplar index (see <see cref="TriggerExemplars.Scan"/>).</summary>
    internal sealed record Proof(bool Proven, int Samples, int Matches);

    /// <summary>Loads per-value-type proofs from generated/trigger_exemplars.json; METADATA_MISSING when absent.</summary>
    internal static IReadOnlyDictionary<uint, Proof> LoadProofs()
    {
        var index =
            TriggerExemplars.Load(Generator.DefaultOutput())
            ?? throw new WorkflowFailure(
                "METADATA_MISSING",
                "preflight",
                "Trigger exemplar index (generated/"
                    + TriggerExemplars.ExemplarsName
                    + ") is missing; copyValues needs its value-type proofs.",
                false,
                false,
                "Run editor_generate_catalog (no game connection), then retry."
            );
        return index
            .GetProperty("vartypes")
            .GetProperty("types")
            .EnumerateArray()
            .ToDictionary(
                t => t.GetProperty("valueType").GetUInt32(),
                t => new Proof(
                    t.GetProperty("proven").GetBoolean(),
                    t.GetProperty("samples").GetInt32(),
                    t.GetProperty("matches").GetInt32()
                )
            );
    }

    /// <summary>
    /// Hash-pinned copy source outside the edited export: a .trg export or a game-written .mythscn (embedded TR v12 only).
    /// Returns the parsed trigger document and the file SHA-256 that was verified.
    /// </summary>
    internal static (CampaignTriggers.Document Doc, string FileSha256) LoadSource(
        string path,
        string sha256
    )
    {
        path = EditorFiles.LocalPath(path);
        if (!path.EndsWith(ScenarioExtension, StringComparison.OrdinalIgnoreCase))
        {
            var export = CampaignTriggers.Read(path);
            if (!export.Sha256.Equals(sha256, StringComparison.OrdinalIgnoreCase))
                throw new WorkflowFailure(
                    "SOURCE_CHANGED",
                    "preflight",
                    "Copy source SHA-256 differs from sourceSha256: " + path,
                    false,
                    false,
                    "Re-run editor_trigger_elements library:true (or hash the file again); no write."
                );
            return (export, export.Sha256);
        }
        CheckpointDocument scenario;
        try
        {
            scenario = CheckpointDocument.Read(path, sha256);
        }
        catch (ArgumentException e)
            when (e.Message.StartsWith("Source changed", StringComparison.Ordinal))
        {
            throw new WorkflowFailure(
                "SOURCE_CHANGED",
                "preflight",
                "Copy source SHA-256 differs from sourceSha256: " + path,
                false,
                false,
                "Re-run editor_trigger_elements library:true (or hash the file again); no write."
            );
        }
        var section = scenario.Root.One("TR").ToArray();
        var version =
            section.Length >= VersionBytes ? BinaryPrimitives.ReadUInt32LittleEndian(section) : 0;
        if (version != SourceTriggerVersion)
            throw new WorkflowFailure(
                "UNSUPPORTED_SOURCE",
                "preflight",
                $"Copy source has TR v{version}; only v{SourceTriggerVersion} records are a reviewed byte source.",
                false,
                false,
                "Pick another library location, or open that scenario in the editor and save a copy (the game writes v12; untested route), then copy from the copy."
            );
        return (CampaignTriggers.ParseScenarioSection(section), scenario.Sha256);
    }

    /// <summary>Schema of copyValues: single-value parameter edits on copied effects, proof-gated per value type.</summary>
    internal static object CopyValueSchema() =>
        new
        {
            type = "array",
            minItems = 1,
            maxItems = MaxCopyValues,
            items = new
            {
                type = "object",
                properties = new Dictionary<string, object>
                {
                    ["copyHandle"] = new { type = "string" },
                    ["parameter"] = new { type = "string" },
                    ["expected"] = new { type = "string" },
                    ["value"] = new { type = "string" },
                },
                required = new[] { "copyHandle", "parameter", "expected", "value" },
                additionalProperties = false,
            },
        };

    internal static object ReplacementSchema()
    {
        var tuples = new
        {
            type = "array",
            minItems = 1,
            maxItems = MaxObjects,
            items = new
            {
                type = "object",
                properties = new Dictionary<string, object>
                {
                    ["unitId"] = new { type = "integer", minimum = 0 },
                    ["player"] = new
                    {
                        type = "integer",
                        minimum = 0,
                        maximum = 12,
                    },
                    ["proto"] = new { type = "string" },
                },
                required = new[] { "unitId", "player", "proto" },
                additionalProperties = false,
            },
        };
        return new
        {
            type = "array",
            minItems = 1,
            maxItems = 200,
            items = new
            {
                type = "object",
                properties = new Dictionary<string, object>
                {
                    ["kind"] = new { type = "string", @enum = new[] { "condition", "effect" } },
                    ["elementIndex"] = new { type = "integer", minimum = 0 },
                    ["copyHandle"] = new { type = "string" },
                    ["parameter"] = new { type = "string" },
                    ["expected"] = tuples,
                    ["objects"] = tuples,
                },
                required = new[] { "parameter", "expected", "objects" },
                additionalProperties = false,
            },
        };
    }

    internal static object CopySchema() =>
        new
        {
            type = "array",
            minItems = 1,
            maxItems = MaxCopies,
            items = new
            {
                type = "object",
                properties = new Dictionary<string, object>
                {
                    ["handle"] = new { type = "string" },
                    ["sourceTriggerId"] = new { type = "integer", minimum = 0 },
                    ["expectedSourceName"] = new { type = "string" },
                    ["effectIndex"] = new { type = "integer", minimum = 0 },
                    ["beforeEffectIndex"] = new { type = "integer", minimum = 0 },
                    ["sourcePath"] = new
                    {
                        type = "string",
                        description = "Optional other file to copy from: .trg export or game-written .mythscn (TR v12), e.g. a library location's sourcePath. Requires sourceSha256.",
                    },
                    ["sourceSha256"] = new { type = "string" },
                },
                required = new[]
                {
                    "handle",
                    "sourceTriggerId",
                    "expectedSourceName",
                    "effectIndex",
                    "beforeEffectIndex",
                },
                additionalProperties = false,
            },
        };

    internal static void ValidateSelectors(JsonElement args)
    {
        if (args.TryGetProperty("objectReplacements", out var list))
        {
            if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() is < 1 or > 200)
                throw new ArgumentException("objectReplacements requires 1..200 entries.");
            var tuples = 0;
            foreach (var r in list.EnumerateArray())
            {
                Catalog.ValidateObject(
                    r,
                    ["kind", "elementIndex", "copyHandle", "parameter", "expected", "objects"]
                );
                if (r.TryGetProperty("copyHandle", out var h))
                {
                    if (
                        string.IsNullOrEmpty(h.GetString())
                        || r.TryGetProperty("kind", out _)
                        || r.TryGetProperty("elementIndex", out _)
                    )
                        throw new ArgumentException("Copy selector cannot mix original selector.");
                }
                else if (
                    r.GetProperty("kind").GetString() is not "condition" and not "effect"
                    || r.GetProperty("elementIndex").GetInt32() < 0
                )
                    throw new ArgumentException(
                        "Object selector must identify original condition/effect index."
                    );
                if (string.IsNullOrWhiteSpace(r.GetProperty("parameter").GetString()))
                    throw new ArgumentException("Object parameter required.");
                _ = ReadObjects(r.GetProperty("expected"));
                tuples += ReadObjects(r.GetProperty("objects")).Length;
                if (tuples > MaxObjects)
                    throw new ArgumentException(
                        "At most 200 replacement object tuples per operation."
                    );
            }
        }
        if (args.TryGetProperty("copyEffects", out var copies))
        {
            if (
                copies.ValueKind != JsonValueKind.Array
                || copies.GetArrayLength() is < 1 or > MaxCopies
            )
                throw new ArgumentException("copyEffects requires 1..32 entries.");
            foreach (var c in copies.EnumerateArray())
            {
                Catalog.ValidateObject(c, CopyKeys);
                if (
                    string.IsNullOrWhiteSpace(c.GetProperty("handle").GetString())
                    || string.IsNullOrWhiteSpace(c.GetProperty("expectedSourceName").GetString())
                    || c.GetProperty("sourceTriggerId").GetInt32() < 0
                    || c.GetProperty("effectIndex").GetInt32() < 0
                    || c.GetProperty("beforeEffectIndex").GetInt32() < 0
                )
                    throw new ArgumentException("Copy source/handle/insertion required.");
                if (
                    c.TryGetProperty("sourcePath", out var sourcePath)
                    != c.TryGetProperty("sourceSha256", out var sourceSha)
                )
                    throw new ArgumentException(
                        "sourcePath and sourceSha256 go together (hash-pinned copy source)."
                    );
                if (sourcePath.ValueKind != JsonValueKind.Undefined)
                {
                    _ = EditorFiles.LocalPath(sourcePath.GetString() ?? "");
                    if (sourceSha.GetString() is not { Length: 64 } sha || !sha.All(Uri.IsHexDigit))
                        throw new ArgumentException("sourceSha256 must be a 64-digit SHA-256.");
                }
            }
        }
        if (args.TryGetProperty("copyValues", out var values))
        {
            if (
                values.ValueKind != JsonValueKind.Array
                || values.GetArrayLength() is < 1 or > MaxCopyValues
            )
                throw new ArgumentException(
                    "copyValues requires 1.." + MaxCopyValues + " entries."
                );
            if (!args.TryGetProperty("copyEffects", out _))
                throw new ArgumentException(
                    "copyValues edits copied effects; add the copyEffects entries they name."
                );
            foreach (var v in values.EnumerateArray())
            {
                Catalog.ValidateObject(v, CopyValueKeys);
                if (
                    string.IsNullOrWhiteSpace(v.GetProperty("copyHandle").GetString())
                    || string.IsNullOrWhiteSpace(v.GetProperty("parameter").GetString())
                    || v.GetProperty("expected").ValueKind != JsonValueKind.String
                    || v.GetProperty("value").ValueKind != JsonValueKind.String
                )
                    throw new ArgumentException(
                        "copyValues entries need copyHandle, parameter and string expected/value."
                    );
            }
        }
    }

    /// <summary>Allowed keys of one copyEffects entry.</summary>
    static readonly string[] CopyKeys =
    [
        "handle",
        "sourceTriggerId",
        "expectedSourceName",
        "effectIndex",
        "beforeEffectIndex",
        "sourcePath",
        "sourceSha256",
    ];

    /// <summary>Allowed keys of one copyValues entry.</summary>
    static readonly string[] CopyValueKeys = ["copyHandle", "parameter", "expected", "value"];

    internal static CampaignTriggers.ObjectRef[] ReadObjects(JsonElement list)
    {
        if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() is < 1 or > MaxObjects)
            throw new ArgumentException("Object tuples must contain 1..200 entries.");
        var result = list.EnumerateArray()
            .Select(o =>
            {
                Catalog.ValidateObject(o, ["unitId", "player", "proto"]);
                var id = o.GetProperty("unitId").GetUInt32();
                var player = o.GetProperty("player").GetUInt32();
                var proto = o.GetProperty("proto").GetString()!;
                if (player > 12 || proto.Length is < 1 or > 128 || proto.Any(char.IsControl))
                    throw new ArgumentException("Object owner/prototype outside reviewed bounds.");
                return new CampaignTriggers.ObjectRef(id, player, proto);
            })
            .ToArray();
        if (result.Select(o => o.UnitId).Distinct().Count() != result.Length)
            throw new ArgumentException("Duplicate object tuple IDs refused.");
        return result;
    }

    internal static byte[] Value(
        CampaignTriggers.Arg arg,
        CampaignTriggers.ObjectRef[] expected,
        CampaignTriggers.ObjectRef[] objects
    )
    {
        if (
            arg.ValueType != 4
            || arg.Objects is null
            || arg.SelectionFlag is not (0 or 1)
            || arg.Values.Length != arg.Objects.Length
            || arg.Values.Length is < 1 or > MaxObjects
            || !arg.Values.SequenceEqual(
                arg.Objects.Select(o =>
                    o.UnitId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                )
            )
            || !arg.Objects.SequenceEqual(expected)
        )
            throw new InvalidDataException(
                "Object argument encoding/expected tuples mismatch; no write."
            );
        if (
            objects.Length is < 1 or > MaxObjects
            || objects.Select(o => o.UnitId).Distinct().Count() != objects.Length
            || objects.Any(o =>
                o.Player > 12 || o.Proto.Length is < 1 or > 128 || o.Proto.Any(char.IsControl)
            )
        )
            throw new ArgumentException("New object tuples outside reviewed bounds.");
        using var memory = new MemoryStream();
        using var w = new BinaryWriter(memory, Encoding.UTF8, true);
        void Wide(string s)
        {
            w.Write(s.Length);
            w.Write(new UnicodeEncoding(false, false, true).GetBytes(s));
        }
        w.Write(objects.Length);
        foreach (var o in objects)
            Wide(o.UnitId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        w.Write(objects.Length);
        foreach (var o in objects)
        {
            w.Write(o.UnitId);
            w.Write(o.Player);
            Wide(o.Proto);
        }
        w.Write(arg.SelectionFlag.Value);
        w.Flush();
        return memory.ToArray();
    }

    internal static byte[] Retarget(
        byte[] source,
        CampaignTriggers.Element element,
        IReadOnlyDictionary<string, CampaignTriggers.ObjectRef[]> replacements
    )
    {
        var raw = source.AsSpan(element.Start, element.End - element.Start).ToArray();
        var changes = new List<(int Offset, int Length, byte[] Bytes)>();
        foreach (var (key, objects) in replacements)
        {
            var found = element
                .Args.Where(a => a.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (found.Length != 1 || found[0].Objects is null)
                throw new InvalidDataException("Object argument missing/ambiguous.");
            var arg = found[0];
            changes.Add(
                (
                    arg.ValueStart - element.Start,
                    arg.ValueEnd - arg.ValueStart,
                    Value(arg, arg.Objects!, objects)
                )
            );
        }
        return Patch(raw, changes);
    }

    static byte[] Patch(byte[] source, IEnumerable<(int Offset, int Length, byte[] Bytes)> changes)
    {
        var sorted = changes.OrderBy(c => c.Offset).ToArray();
        var end = 0;
        using var result = new MemoryStream();
        foreach (var c in sorted)
        {
            if (c.Offset < end || c.Length < 0 || c.Offset > source.Length - c.Length)
                throw new InvalidDataException("Overlapping object edit refused.");
            result.Write(source.AsSpan(end, c.Offset - end));
            result.Write(c.Bytes);
            end = c.Offset + c.Length;
        }
        result.Write(source.AsSpan(end));
        return result.ToArray();
    }

    static void ValidateGroups(CampaignTriggers.Document doc)
    {
        if (doc.Groups.Select(g => g.Id).Distinct().Count() != doc.Groups.Length)
            throw new InvalidDataException("Ambiguous trigger groups.");
        var seen = new HashSet<uint>();
        var triggers = doc.Triggers.ToDictionary(t => t.Id);
        foreach (var g in doc.Groups)
        foreach (var id in g.Indexes)
            if (!seen.Add(id) || !triggers.TryGetValue(id, out var t) || t.Group != g.Id)
                throw new InvalidDataException("Unresolved group membership.");
        if (seen.Count != doc.Triggers.Length)
            throw new InvalidDataException("Missing trigger group membership.");
    }

    internal static byte[] ReplaceElements(
        CampaignTriggers.Document doc,
        CampaignTriggers.Trigger trigger,
        byte[][] conditions,
        byte[][] effects
    )
    {
        ValidateGroups(doc);
        if (
            conditions.Length > CampaignTriggers.MaxItems
            || effects.Length is < 1 or > CampaignTriggers.MaxItems
        )
            throw new ArgumentException(
                "At least one effect required; conditions/effects within reviewed count bound."
            );
        var countOffset =
            (
                trigger.Conditions.Length > 0
                    ? trigger.Conditions[0].Start
                    : trigger.EffectsCountOffset
            ) - 4;
        using var record = new MemoryStream();
        record.Write(doc.Body.AsSpan(trigger.Start, countOffset - trigger.Start));
        record.Write(BitConverter.GetBytes(conditions.Length));
        foreach (var c in conditions)
            record.Write(c);
        record.Write(BitConverter.GetBytes(effects.Length));
        foreach (var e in effects)
            record.Write(e);
        var body = Patch(
            doc.Body,
            [(trigger.Start, trigger.End - trigger.Start, record.ToArray())]
        );
        if (body.Length > ExportFormats.MaxStoredBytes - 6 - doc.SuffixBytes)
            throw new InvalidDataException("Prepared TR exceeds file bound.");
        var output = new byte[6 + body.Length + doc.SuffixBytes];
        doc.Original.AsSpan(0, 6).CopyTo(output);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(2), body.Length);
        body.CopyTo(output, 6);
        doc.Original.AsSpan(doc.Original.Length - doc.SuffixBytes)
            .CopyTo(output.AsSpan(6 + body.Length));
        var observed = CampaignTriggers.Parse(output);
        var actual = observed.Triggers.Single(t => t.Id == trigger.Id);
        void Check(byte[][] wanted, CampaignTriggers.Element[] got)
        {
            if (wanted.Length != got.Length)
                throw new InvalidDataException("Composed element count failed readback.");
            for (var i = 0; i < wanted.Length; i++)
                if (
                    !wanted[i]
                        .AsSpan()
                        .SequenceEqual(
                            observed.Body.AsSpan(got[i].Start, got[i].End - got[i].Start)
                        )
                )
                    throw new InvalidDataException("Composed element bytes failed readback.");
        }
        Check(conditions, actual.Conditions);
        Check(effects, actual.Effects);
        foreach (var before in doc.Triggers.Where(t => t.Id != trigger.Id))
        {
            var after = observed.Triggers.Single(t => t.Id == before.Id);
            if (
                !doc
                    .Body.AsSpan(before.Start, before.End - before.Start)
                    .SequenceEqual(observed.Body.AsSpan(after.Start, after.End - after.Start))
            )
                throw new InvalidDataException("Composition changed unrelated trigger bytes.");
        }
        if (
            !doc
                .Body.AsSpan(doc.GroupsStart)
                .SequenceEqual(observed.Body.AsSpan(observed.GroupsStart))
        )
            throw new InvalidDataException("Composition changed groups.");
        foreach (
            var e in actual
                .Conditions.Concat(actual.Effects)
                .Where(e => e.Name.StartsWith("Trigger:", StringComparison.OrdinalIgnoreCase))
        )
        foreach (var a in e.Args.Where(a => a.Key == "EventID"))
        foreach (var v in a.Values)
            if (!uint.TryParse(v, out var id) || !observed.Triggers.Any(t => t.Id == id))
                throw new InvalidDataException("Copied effect has unresolved trigger reference.");
        return output;
    }

    internal static void SelfTest()
    {
        static object Obj(uint id, uint player = 1, string proto = "VillagerAztec") =>
            new
            {
                unitId = id,
                player,
                proto,
            };
        static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);
        static void Refuse(Action action)
        {
            try
            {
                action();
            }
            catch (ArgumentException)
            {
                return;
            }
            catch (InvalidDataException)
            {
                return;
            }
            throw new InvalidOperationException("Unsafe object/copy edit accepted.");
        }
        var single = CampaignTriggers.Parse(WorkflowFixtures.Trigger(selection: 1, trailer: 127));
        var cloned = TriggerEdits.Transform(
            single,
            Json(
                new
                {
                    operation = "clone",
                    triggerId = 704,
                    expectedName = "Startup",
                    newId = 705,
                    newName = "East",
                }
            )
        );
        var input = CampaignTriggers.Parse(cloned.Output);
        var operation = Json(
            new
            {
                operation = "patch",
                triggerId = 705,
                expectedName = "East",
                copyEffects = new[]
                {
                    new
                    {
                        handle = "first",
                        sourceTriggerId = 704,
                        expectedSourceName = "Startup",
                        effectIndex = 0,
                        beforeEffectIndex = 0,
                    },
                    new
                    {
                        handle = "second",
                        sourceTriggerId = 704,
                        expectedSourceName = "Startup",
                        effectIndex = 0,
                        beforeEffectIndex = 0,
                    },
                },
                objectReplacements = new object[]
                {
                    new
                    {
                        copyHandle = "first",
                        parameter = "SrcObject",
                        expected = new[] { Obj(100) },
                        objects = new[] { Obj(300) },
                    },
                    new
                    {
                        copyHandle = "second",
                        parameter = "SrcObject",
                        expected = new[] { Obj(100) },
                        objects = new[] { Obj(400) },
                    },
                    new
                    {
                        kind = "effect",
                        elementIndex = 0,
                        parameter = "DstObject",
                        expected = new[] { Obj(200, 1, "Farm") },
                        objects = new[] { Obj(201, 1, "Farm") },
                    },
                },
            }
        );
        var sourceChanged = CampaignTriggers.Parse(
            TriggerEdits
                .Transform(
                    input,
                    Json(
                        new
                        {
                            operation = "patch",
                            triggerId = 704,
                            expectedName = "Startup",
                            objectReplacements = new[]
                            {
                                new
                                {
                                    kind = "effect",
                                    elementIndex = 0,
                                    parameter = "SrcObject",
                                    expected = new[] { Obj(100) },
                                    objects = new[] { Obj(111) },
                                },
                            },
                        }
                    )
                )
                .Output
        );
        var changed = CampaignTriggers.Parse(
            TriggerEdits.Transform(sourceChanged, operation, input).Output
        );
        var effects = changed.Triggers.Single(t => t.Id == 705).Effects;
        if (
            !effects
                .Select(e => e.Args[0].Objects![0].UnitId)
                .SequenceEqual(new uint[] { 300, 400, 100 })
            || effects[2].Args[1].Objects![0].UnitId != 201
            || effects.Any(e =>
                e.Args[0].SelectionFlag != 1
                || e.Args[0].Trailer != 127
                || e.Extras![2].Expression != "trUnitDoWorkOnUnit(%DstObject%, %EventID%);"
            )
        )
            throw new InvalidOperationException(
                "Immutable ordered cross-trigger copy/metadata preservation fixture failed."
            );
        var original = input.Triggers[0].Effects[0];
        var raw = input.Body.AsSpan(original.Start, original.End - original.Start).ToArray();
        var triple = CampaignTriggers.Parse(
            ReplaceElements(
                input,
                input.Triggers[0],
                input
                    .Triggers[0]
                    .Conditions.Select(e => input.Body.AsSpan(e.Start, e.End - e.Start).ToArray())
                    .ToArray(),
                [raw, raw, raw]
            )
        );
        var removals = Json(
            new
            {
                operation = "patch",
                triggerId = 704,
                expectedName = "Startup",
                removeEffects = (int[])[0],
                duplicates = new[] { new { kind = "effect", elementIndex = 1 } },
                objectReplacements = new[]
                {
                    new
                    {
                        kind = "effect",
                        elementIndex = 2,
                        parameter = "SrcObject",
                        expected = new[] { Obj(100) },
                        objects = new[] { Obj(222) },
                    },
                    new
                    {
                        kind = "effect",
                        elementIndex = 3,
                        parameter = "SrcObject",
                        expected = new[] { Obj(100) },
                        objects = new[] { Obj(333) },
                    },
                },
            }
        );
        if (
            !CampaignTriggers
                .Parse(TriggerEdits.Transform(triple, removals).Output)
                .Triggers[0]
                .Effects.Select(e => e.Args[0].Objects![0].UnitId)
                .SequenceEqual(new uint[] { 100, 222, 333 })
        )
            throw new InvalidOperationException(
                "Original/duplicate object indexes shifted incorrectly."
            );
        foreach (var bad in new[] { Obj(100, 2), Obj(100, 1, "WrongProto") })
            Refuse(() =>
                TriggerEdits.Transform(
                    single,
                    Json(
                        new
                        {
                            operation = "patch",
                            triggerId = 704,
                            expectedName = "Startup",
                            objectReplacements = new[]
                            {
                                new
                                {
                                    kind = "effect",
                                    elementIndex = 0,
                                    parameter = "SrcObject",
                                    expected = new[] { bad },
                                    objects = new[] { Obj(301) },
                                },
                            },
                        }
                    )
                )
            );
        Refuse(() => ReadObjects(Json(new[] { Obj(100), Obj(100) })));
        var unsupported = CampaignTriggers.Parse(WorkflowFixtures.Trigger(selection: 2));
        Refuse(() =>
            Value(
                unsupported.Triggers[0].Effects[0].Args[0],
                [new(100, 1, "VillagerAztec")],
                [new(300, 1, "VillagerAztec")]
            )
        );
        Refuse(() =>
            TriggerEdits.Transform(
                input,
                Json(
                    new
                    {
                        operation = "patch",
                        triggerId = 705,
                        expectedName = "East",
                        copyEffects = new[]
                        {
                            new
                            {
                                handle = "x",
                                sourceTriggerId = 704,
                                expectedSourceName = "Changed",
                                effectIndex = 0,
                                beforeEffectIndex = 0,
                            },
                        },
                    }
                )
            )
        );
        CrossFileSelfTest();
    }

    /// <summary>Event id the cross-file self-test writes into the copied EventID (fixture stores -1).</summary>
    const string SelfTestEvent = "5";

    /// <summary>Value type of the fixture EventID argument (flagged single value).</summary>
    const uint SelfTestEventType = 8;

    /// <summary>Unit count of the fixture source effect, distinct from the target's single worker.</summary>
    const int SelfTestWorkers = 3;

    /// <summary>Copies an effect from another hash-pinned .trg (temp file, deleted afterwards) and edits a proven value.</summary>
    static void CrossFileSelfTest()
    {
        static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);
        static void Refused(Action action, string? code = null)
        {
            try
            {
                action();
            }
            catch (WorkflowFailure e) when (code is null || e.Code == code)
            {
                return;
            }
            catch (ArgumentException) when (code is null)
            {
                return;
            }
            throw new InvalidOperationException(
                "Unsafe cross-file copy accepted" + (code is null ? "." : " (wanted " + code + ").")
            );
        }
        var target = CampaignTriggers.Parse(WorkflowFixtures.Trigger());
        var sourceBytes = WorkflowFixtures.Trigger(workers: SelfTestWorkers, selection: 1);
        var file = Path.Combine(
            Path.GetTempPath(),
            "aommcp-selftest-" + Guid.NewGuid().ToString("N") + ".trg"
        );
        File.WriteAllBytes(file, sourceBytes);
        try
        {
            var sha = CheckpointDocument.Hash(sourceBytes);
            object Op(string value, string? pin = null, string parameter = EventKey) =>
                new
                {
                    operation = "patch",
                    triggerId = 704,
                    expectedName = "Startup",
                    copyEffects = new[]
                    {
                        new
                        {
                            handle = "ext",
                            sourceTriggerId = 704,
                            expectedSourceName = "Startup",
                            effectIndex = 0,
                            beforeEffectIndex = 0,
                            sourcePath = file,
                            sourceSha256 = pin ?? sha,
                        },
                    },
                    copyValues = new[]
                    {
                        new
                        {
                            copyHandle = "ext",
                            parameter,
                            expected = NoEvent,
                            value,
                        },
                    },
                };
            var proven = new Dictionary<uint, Proof>
            {
                [SelfTestEventType] = new(
                    true,
                    TriggerExemplars.MinProofSamples,
                    TriggerExemplars.MinProofSamples
                ),
            };
            var step = TriggerEdits.Transform(target, Json(Op(SelfTestEvent)), proofs: proven);
            var output = CampaignTriggers.Parse(step.Output);
            var effects = output.Triggers[0].Effects;
            var source = CampaignTriggers.Parse(sourceBytes).Triggers[0].Effects[0];
            var copied = effects[0];
            var eventArg = copied.Args.Single(a => a.Key == EventKey);
            if (
                effects.Length != 2
                || copied.Args[0].Objects!.Length != SelfTestWorkers
                || !eventArg.Values.SequenceEqual([SelfTestEvent])
                || effects[1].Args[0].Objects!.Length != 1
            )
                throw new InvalidOperationException("Cross-file copy fixture failed.");
            // Everything outside the edited value region is the source element's bytes, unchanged.
            var sourceBody = CampaignTriggers.Parse(sourceBytes).Body;
            var sourceEvent = source.Args.Single(a => a.Key == EventKey);
            if (
                !output
                    .Body.AsSpan(copied.Start, eventArg.ValueStart - copied.Start)
                    .SequenceEqual(
                        sourceBody.AsSpan(source.Start, sourceEvent.ValueStart - source.Start)
                    )
                || !output
                    .Body.AsSpan(eventArg.ValueEnd, copied.End - eventArg.ValueEnd)
                    .SequenceEqual(
                        sourceBody.AsSpan(sourceEvent.ValueEnd, source.End - sourceEvent.ValueEnd)
                    )
            )
                throw new InvalidOperationException(
                    "Cross-file copy changed bytes outside the edited value."
                );
            if (
                JsonSerializer
                    .SerializeToElement(step.Detail)
                    .GetProperty("renderedXs")
                    .EnumerateArray()
                    .All(r =>
                        !r.GetProperty("xs")
                            .GetString()!
                            .Contains(SelfTestEvent, StringComparison.Ordinal)
                    )
            )
                throw new InvalidOperationException(
                    "Cross-file copy renderedXs missing the edited value."
                );
            Refused(
                () =>
                    TriggerEdits.Transform(
                        target,
                        Json(Op(SelfTestEvent, new string('0', sha.Length))),
                        proofs: proven
                    ),
                "SOURCE_CHANGED"
            );
            Refused(
                () =>
                    TriggerEdits.Transform(
                        target,
                        Json(Op(SelfTestEvent)),
                        proofs: new Dictionary<uint, Proof>()
                    ),
                "UNPROVEN_VARTYPE"
            );
            Refused(() =>
                TriggerEdits.Transform(
                    target,
                    Json(Op(SelfTestEvent, parameter: "SrcObject")),
                    proofs: proven
                )
            );
        }
        finally
        {
            File.Delete(file);
        }
    }

    internal static (byte[] Output, object Detail) Apply(
        CampaignTriggers.Document original,
        byte[] legacyOutput,
        JsonElement args,
        CampaignTriggers.Document immutableSources,
        object legacyDetail,
        uint targetId,
        bool allowRaw = false,
        IReadOnlyDictionary<uint, Proof>? proofs = null
    )
    {
        if (
            !args.TryGetProperty("objectReplacements", out var replacements)
            && !args.TryGetProperty("copyEffects", out _)
        )
            return (legacyOutput, legacyDetail);
        var doc = CampaignTriggers.Parse(legacyOutput);
        ValidateGroups(doc);
        ValidateGroups(immutableSources);
        var target = doc.Triggers.Single(t => t.Id == targetId);
        var before = original.Triggers.Single(t =>
            t.Id == args.GetProperty("triggerId").GetUInt32()
        );
        var removed = args.TryGetProperty("removeEffects", out var rem)
            ? rem.EnumerateArray().Select(x => x.GetInt32()).ToHashSet()
            : [];
        var removedConditions = args.TryGetProperty("removeConditions", out var rc)
            ? rc.EnumerateArray().Select(x => x.GetInt32()).ToHashSet()
            : [];
        var conditionBytes = target
            .Conditions.Select(e => doc.Body.AsSpan(e.Start, e.End - e.Start).ToArray())
            .ToArray();
        var effectBytes = target
            .Effects.Select(e => doc.Body.AsSpan(e.Start, e.End - e.Start).ToArray())
            .ToArray();
        var copies = new Dictionary<
            string,
            (
                CampaignTriggers.Element Element,
                byte[] Source,
                byte[] Raw,
                int Before,
                string? SourcePath
            )
        >(StringComparer.Ordinal);
        var external = new Dictionary<string, (CampaignTriggers.Document Doc, string FileSha256)>(
            StringComparer.OrdinalIgnoreCase
        );
        var copyOrder = new List<string>();
        long copyBytes = 0;
        var copyWarnings = new List<string>();
        var diff = new List<object>();
        if (args.TryGetProperty("copyEffects", out var list))
        {
            if (list.GetArrayLength() is < 1 or > MaxCopies)
                throw new ArgumentException("copyEffects requires 1..32 entries.");
            foreach (var c in list.EnumerateArray())
            {
                Catalog.ValidateObject(c, CopyKeys);
                var handle = c.GetProperty("handle").GetString()!;
                if (
                    handle.Length is < 1 or > 64
                    || handle.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch is not '_' and not '-')
                )
                    throw new ArgumentException("Copy handle outside bounds.");
                var (source, sourceSha, sourcePath) = (
                    immutableSources,
                    immutableSources.Sha256,
                    (string?)null
                );
                if (c.TryGetProperty("sourcePath", out var pathArg))
                {
                    sourcePath = EditorFiles.LocalPath(pathArg.GetString()!);
                    var pin = c.GetProperty("sourceSha256").GetString()!;
                    if (!external.TryGetValue(sourcePath, out var loaded))
                    {
                        external[sourcePath] = loaded = LoadSource(sourcePath, pin);
                        ValidateGroups(loaded.Doc);
                    }
                    else if (!loaded.FileSha256.Equals(pin, StringComparison.OrdinalIgnoreCase))
                        throw new ArgumentException(
                            "Copy entries pin different sourceSha256 values for " + sourcePath + "."
                        );
                    (source, sourceSha) = (loaded.Doc, loaded.FileSha256);
                }
                var src = source.Triggers.SingleOrDefault(t =>
                    t.Id == c.GetProperty("sourceTriggerId").GetUInt32()
                );
                if (src is null || src.Name != c.GetProperty("expectedSourceName").GetString())
                    throw new ArgumentException("Copied source trigger identity mismatch.");
                var index = c.GetProperty("effectIndex").GetInt32();
                var insert = c.GetProperty("beforeEffectIndex").GetInt32();
                if (
                    index < 0
                    || index >= src.Effects.Length
                    || insert < 0
                    || insert > before.Effects.Length
                )
                    throw new ArgumentException(
                        "Copy source/insertion index outside original effects."
                    );
                var element = src.Effects[index];
                copyBytes += element.End - element.Start;
                if (copyBytes > ExportFormats.MaxStoredBytes - doc.Original.Length)
                    throw new InvalidDataException("Copied effect bytes exceed output file bound.");
                var raw = source.Body.AsSpan(element.Start, element.End - element.Start).ToArray();
                if (
                    !copies.TryAdd(
                        handle,
                        (
                            element,
                            source.Body,
                            raw,
                            insert - removed.Count(n => n < insert),
                            sourcePath
                        )
                    )
                )
                    throw new ArgumentException("Duplicate copy handle.");
                copyOrder.Add(handle);
                diff.Add(
                    new
                    {
                        field = "copy[" + handle + "]",
                        element = element.Name,
                        sourcePath,
                        sourceTriggerId = src.Id,
                        effectIndex = index,
                        beforeEffectIndex = insert,
                        sourceSha256 = sourceSha,
                    }
                );
            }
        }
        var edits = new Dictionary<string, List<(int Offset, int Length, byte[] Bytes)>>(
            StringComparer.Ordinal
        );
        var wantedObjects =
            new List<(
                string Selector,
                string Parameter,
                CampaignTriggers.ObjectRef[] Objects,
                byte? Flag,
                byte Trailer
            )>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (replacements.ValueKind != JsonValueKind.Undefined)
        {
            if (replacements.GetArrayLength() is < 1 or > 200)
                throw new ArgumentException("objectReplacements requires 1..200 entries.");
            foreach (var r in replacements.EnumerateArray())
            {
                Catalog.ValidateObject(
                    r,
                    ["kind", "elementIndex", "copyHandle", "parameter", "expected", "objects"]
                );
                var parameter = r.GetProperty("parameter").GetString()!;
                CampaignTriggers.Element element;
                string key;
                if (r.TryGetProperty("copyHandle", out var handleArg))
                {
                    if (r.TryGetProperty("kind", out _) || r.TryGetProperty("elementIndex", out _))
                        throw new ArgumentException(
                            "Copy replacement cannot mix original element selector."
                        );
                    var handle = handleArg.GetString()!;
                    if (!copies.TryGetValue(handle, out var copy))
                        throw new ArgumentException("Unknown copied-effect handle.");
                    element = copy.Element;
                    key = "copy:" + handle;
                }
                else
                {
                    var kind = r.GetProperty("kind").GetString();
                    var index = r.GetProperty("elementIndex").GetInt32();
                    if (kind is not "effect" and not "condition")
                        throw new ArgumentException("Object replacement kind: condition/effect.");
                    var removals = kind == "effect" ? removed : removedConditions;
                    if (index < 0 || removals.Contains(index))
                        throw new ArgumentException("Object replacement targets removed element.");
                    index -= removals.Count(n => n < index);
                    var elements = kind == "effect" ? target.Effects : target.Conditions;
                    if (index >= elements.Length)
                        throw new ArgumentException(
                            "Object replacement index outside original/duplicate elements."
                        );
                    element = elements[index];
                    key = kind + ":" + index;
                }
                if (!seen.Add(key + ":" + parameter.ToLowerInvariant()))
                    throw new ArgumentException("Duplicate object parameter replacement.");
                var matches = element
                    .Args.Where(a => a.Key.Equals(parameter, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (matches.Length != 1)
                    throw new InvalidDataException("Object parameter missing/ambiguous.");
                var arg = matches[0];
                var expected = ReadObjects(r.GetProperty("expected"));
                var objects = ReadObjects(r.GetProperty("objects"));
                var value = Value(arg, expected, objects);
                if (!edits.TryGetValue(key, out var changes))
                    edits[key] = changes = [];
                changes.Add((arg.ValueStart - element.Start, arg.ValueEnd - arg.ValueStart, value));
                wantedObjects.Add((key, parameter, objects, arg.SelectionFlag, arg.Trailer));
                diff.Add(
                    new
                    {
                        field = key + "." + parameter,
                        expected = expected
                            .Select(o => new
                            {
                                unitId = o.UnitId,
                                player = o.Player,
                                proto = o.Proto,
                            })
                            .ToArray(),
                        objects = objects
                            .Select(o => new
                            {
                                unitId = o.UnitId,
                                player = o.Player,
                                proto = o.Proto,
                            })
                            .ToArray(),
                    }
                );
            }
        }
        var wantedValues = new List<(string Selector, string Parameter, string Value)>();
        var edited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (args.TryGetProperty("copyValues", out var valueList))
        {
            if (valueList.GetArrayLength() is < 1 or > MaxCopyValues)
                throw new ArgumentException(
                    "copyValues requires 1.." + MaxCopyValues + " entries."
                );
            foreach (var v in valueList.EnumerateArray())
            {
                Catalog.ValidateObject(v, CopyValueKeys);
                var handle = v.GetProperty("copyHandle").GetString()!;
                var parameter = v.GetProperty("parameter").GetString()!;
                var expected = v.GetProperty("expected").GetString()!;
                var value = v.GetProperty("value").GetString()!;
                if (!copies.TryGetValue(handle, out var copy))
                    throw new ArgumentException(
                        "copyValues names unknown copied-effect handle '" + handle + "'."
                    );
                var key = "copy:" + handle;
                var where = $"copy[{handle}] {copy.Element.Name}";
                if (!seen.Add(key + ":" + parameter.ToLowerInvariant()))
                    throw new ArgumentException(
                        "Duplicate replacement of " + where + "." + parameter + "."
                    );
                var matches = copy
                    .Element.Args.Where(a =>
                        a.Key.Equals(parameter, StringComparison.OrdinalIgnoreCase)
                    )
                    .ToArray();
                if (matches.Length != 1)
                    throw new ArgumentException(
                        $"{where}: parameter '{parameter}' missing/ambiguous (has: {string.Join(", ", copy.Element.Args.Select(a => a.Key))})."
                    );
                var arg = matches[0];
                if (arg.ValueType == ObjectListType)
                    throw new ArgumentException(
                        $"{where}.{parameter} is a unit list; use objectReplacements with copyHandle."
                    );
                if (arg.Values.Length != 1)
                    throw new ArgumentException(
                        $"{where}.{parameter} stores {arg.Values.Length} values; copyValues edits single-value parameters only."
                    );
                if (arg.Values[0] != expected)
                    throw new ArgumentException(
                        $"{where}.{parameter}: expected '{expected}' differs from the copied value '{arg.Values[0]}'."
                    );
                if (
                    value.Length > CampaignTriggers.MaxChars
                    || value.Any(ch =>
                        char.IsControl(ch) && ch is not '\n' and not '\r' and not '\t'
                    )
                )
                    throw new ArgumentException(
                        $"{where}.{parameter}: value outside 0..{CampaignTriggers.MaxChars} characters or has control characters other than tab/line breaks."
                    );
                proofs ??= LoadProofs();
                if (!proofs.TryGetValue(arg.ValueType, out var proof) || !proof.Proven)
                    throw new WorkflowFailure(
                        "UNPROVEN_VARTYPE",
                        "preflight",
                        $"{where}.{parameter} has value type {arg.ValueType}; its re-encode is unproven ({(proof is null ? "no corpus samples" : $"{proof.Matches}/{proof.Samples} samples matched, {TriggerExemplars.MinProofSamples} all-matching needed")}).",
                        false,
                        false,
                        "Copy an exemplar that already stores the wanted value, or keep the copied value; no write."
                    );
                var region = copy.Source.AsSpan(arg.ValueStart, arg.ValueEnd - arg.ValueStart);
                if (
                    TriggerExemplars.Encode(copy.Source, arg, arg.Values) is not { } same
                    || !region.SequenceEqual(same)
                )
                    throw new InvalidDataException(
                        $"{where}.{parameter}: this argument does not re-encode byte-exact; no write."
                    );
                var encoded = TriggerExemplars.Encode(copy.Source, arg, [value])!;
                if (!edits.TryGetValue(key, out var changes))
                    edits[key] = changes = [];
                changes.Add(
                    (arg.ValueStart - copy.Element.Start, arg.ValueEnd - arg.ValueStart, encoded)
                );
                wantedValues.Add((key, arg.Key, value));
                edited.Add(handle + "/" + arg.Key);
                TriggerEdits.GuardValue(
                    arg.Key,
                    value,
                    TriggerEdits.Classify(TriggerEdits.Templates(copy.Element), arg.Key),
                    allowRaw,
                    copyWarnings,
                    where
                );
                diff.Add(
                    new
                    {
                        field = key + "." + arg.Key,
                        valueType = arg.ValueType,
                        before = expected,
                        after = value,
                    }
                );
            }
        }
        foreach (var handle in copyOrder.Where(h => copies[h].SourcePath is not null))
        {
            var copy = copies[handle];
            var where = $"copy[{handle}] {copy.Element.Name}";
            foreach (var arg in copy.Element.Args)
            {
                if (
                    arg.Key == EventKey
                    && !edited.Contains(handle + "/" + arg.Key)
                    && arg.Values.Any(v => v != NoEvent)
                )
                {
                    if (
                        copy.Element.Name.StartsWith(
                            TriggerPrefix,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                        throw new ArgumentException(
                            $"{where}: EventID {string.Join(",", arg.Values)} numbers a trigger of the source file; set it for this export with copyValues."
                        );
                    copyWarnings.Add(
                        $"{where}: EventID {string.Join(",", arg.Values)} came from the source file's trigger numbering; check it or set it with copyValues."
                    );
                }
                if (
                    arg.ValueType == TriggerExemplars.StringIdType
                    && !edited.Contains(handle + "/" + arg.Key)
                )
                {
                    var region = copy.Source.AsSpan(arg.ValueStart, arg.ValueEnd - arg.ValueStart);
                    var id =
                        region.Length >= 2 * VersionBytes
                            ? BinaryPrimitives.ReadUInt32LittleEndian(region[VersionBytes..])
                            : 0;
                    if (id != 0 || arg.Values.Any(v => v.StartsWith('{')))
                        copyWarnings.Add(
                            $"{where}: {arg.Key} references the source's string {(id != 0 ? "table id " + id + " " : "key ")}'{string.Join(",", arg.Values)}'; unless the target mod's stringmods.txt has it, the text may not show."
                        );
                }
            }
        }
        foreach (var (key, changes) in edits)
        {
            if (key.StartsWith("copy:", StringComparison.Ordinal))
            {
                var handle = key[5..];
                var copy = copies[handle];
                copies[handle] = copy with { Raw = Patch(copy.Raw, changes) };
            }
            else
            {
                var parts = key.Split(':');
                var index = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                var bytes = parts[0] == "effect" ? effectBytes : conditionBytes;
                bytes[index] = Patch(bytes[index], changes);
            }
        }
        var effects = new List<byte[]>();
        var positions = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i <= effectBytes.Length; i++)
        {
            foreach (var handle in copyOrder.Where(h => copies[h].Before == i))
            {
                positions["copy:" + handle] = effects.Count;
                effects.Add(copies[handle].Raw);
            }
            if (i < effectBytes.Length)
            {
                positions["effect:" + i] = effects.Count;
                effects.Add(effectBytes[i]);
            }
        }
        var output = ReplaceElements(doc, target, conditionBytes, effects.ToArray());
        var detail = JsonSerializer
            .SerializeToElement(legacyDetail)
            .EnumerateObject()
            .ToDictionary(p => p.Name, p => (object)p.Value.Clone());
        var legacyDiff = ((JsonElement)detail["diff"])
            .EnumerateArray()
            .Select(x => (object)x.Clone());
        detail["diff"] = legacyDiff.Concat(diff).ToArray();
        detail["effectCount"] = effects.Count;
        var parsed = CampaignTriggers.Parse(output);
        var result = parsed.Triggers.Single(t => t.Id == targetId);
        foreach (var wanted in wantedObjects)
        {
            var element = wanted.Selector.StartsWith("condition:", StringComparison.Ordinal)
                ? result.Conditions[
                    int.Parse(
                        wanted.Selector[10..],
                        System.Globalization.CultureInfo.InvariantCulture
                    )
                ]
                : result.Effects[positions[wanted.Selector]];
            var arg = element.Args.Single(a =>
                a.Key.Equals(wanted.Parameter, StringComparison.OrdinalIgnoreCase)
            );
            if (
                arg.Objects is null
                || !arg.Objects.SequenceEqual(wanted.Objects)
                || arg.SelectionFlag != wanted.Flag
                || arg.Trailer != wanted.Trailer
                || !arg.Values.SequenceEqual(
                    wanted.Objects.Select(o =>
                        o.UnitId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    )
                )
            )
                throw new InvalidDataException(
                    "Object tuple/flag/trailer semantic readback failed; no write."
                );
        }
        foreach (var (selector, parameter, value) in wantedValues)
            if (
                !result
                    .Effects[positions[selector]]
                    .Args.Single(a => a.Key == parameter)
                    .Values.SequenceEqual([value])
            )
                throw new InvalidDataException("Copied-effect value readback failed; no write.");
        var rendered = ((JsonElement)detail["renderedXs"])
            .EnumerateArray()
            .Select(x => (object)x.Clone())
            .ToList();
        foreach (var handle in copyOrder)
        {
            var element = result.Effects[positions["copy:" + handle]];
            foreach (
                var code in TriggerEdits
                    .Templates(element)
                    .Select(t => TriggerEdits.Render(t, element))
            )
            {
                var problem = TriggerEdits.Balance(code);
                if (problem is not null)
                    copyWarnings.Add(
                        $"copy[{handle}] {element.Name}: rendered template looks unbalanced on its own ({problem})."
                    );
                rendered.Add(
                    new
                    {
                        copyHandle = handle,
                        effectIndex = positions["copy:" + handle],
                        element = element.Name,
                        xs = code,
                        balanced = problem is null,
                        problem,
                    }
                );
            }
        }
        detail["renderedXs"] = rendered.ToArray();
        detail["injectionWarnings"] = ((JsonElement)detail["injectionWarnings"])
            .EnumerateArray()
            .Select(x => (object)x.GetString()!)
            .Concat(copyWarnings)
            .ToArray();
        if (
            args.GetProperty("operation").GetString() == "clone"
            && result
                .Conditions.Concat(result.Effects)
                .Any(e =>
                    e.Name.StartsWith("Trigger:", StringComparison.OrdinalIgnoreCase)
                    && e.Args.Any(a =>
                        a.Key == "EventID"
                        && a.Values.Contains(
                            before.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        )
                    )
                )
        )
            throw new InvalidDataException(
                "Clone retains source-self EventID after copying; no write."
            );
        detail["editedRecordSha256"] = CheckpointDocument.Hash(
            parsed.Body.AsSpan(result.Start, result.End - result.Start)
        );
        return (output, detail);
    }
}
