using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AomMcp;

/// <summary>Bounded parser for read-only campaign TR records; retains every original byte for later guarded edits.</summary>
internal static class CampaignTriggers
{
    const int MaxTriggers = 10_000, MaxChars = 250_000;
    internal const int MaxItems = 20_000;
    static readonly UnicodeEncoding Utf16 = new(false, false, true);
    internal sealed record Arg(string Key, string Label, uint ValueType, string[] Values,
        int Start, int ValueStart, int ValueEnd, int End);
    internal sealed record Element(string Name, string Kind, Arg[] Args, string Command, int Start, int End, int LabelOffset, int LabelBytes);
    internal sealed record Trigger(uint Id, uint Group, uint Priority, string Name, string Note,
        byte[] Flags, int Start, int End, int IdOffset, int FlagsOffset, int NameOffset, int NameChars,
        int EffectsCountOffset, Element[] Conditions, Element[] Effects);
    internal sealed record Group(uint Id, string Name, uint[] Indexes, int Start, int End);
    internal sealed record Document(byte[] Original, byte[] Body, Trigger[] Triggers, Group[] Groups,
        int CountOffset, int GroupsStart, int SuffixBytes, string Format, string Sha256);

    internal sealed class Cursor(byte[] data, int offset = 0)
    {
        internal int Offset { get; set; } = offset;
        internal int Length => data.Length;
        internal ReadOnlySpan<byte> Take(int count)
        {
            if (count < 0 || count > data.Length - Offset) throw new InvalidDataException("TR field exceeds bounded payload at " + Offset + ".");
            var span = data.AsSpan(Offset, count); Offset += count; return span;
        }
        internal uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
        internal byte Byte() => Take(1)[0];
        internal int Count(int bound, string label)
        {
            var n = U32();
            if (n > bound) throw new InvalidDataException("TR " + label + " exceeds host bound.");
            return (int)n;
        }
        internal string Wide()
        {
            var n = Count(MaxChars, "UTF-16 string");
            try { return Utf16.GetString(Take(checked(n * 2))).TrimEnd('\0'); }
            catch (DecoderFallbackException e) { throw new InvalidDataException("Invalid TR UTF-16.", e); }
        }
        internal string Narrow()
        {
            var n = Count(MaxChars, "narrow string");
            return Encoding.Latin1.GetString(Take(n)).TrimEnd('\0');
        }
    }

    internal static Document Read(string path)
    {
        var bytes = TriggerCodec.ReadFile(path);
        return Parse(bytes);
    }

    internal static Document Parse(byte[] bytes)
    {
        var shape = ExportFormats.Trigger(bytes);
        if (!shape.Valid) throw new InvalidDataException(shape.Reason);
        var size = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(2));
        var body = bytes.AsSpan(6, size).ToArray();
        var doc = ParseBody(body, standalone: true);
        return new(bytes, body, doc.Triggers, doc.Groups, doc.CountOffset, doc.GroupsStart,
            shape.SuffixBytes, shape.Format, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    internal static Document ParseScenarioSection(byte[] body)
    {
        var doc = ParseBody(body, standalone: false);
        return new(body, body, doc.Triggers, doc.Groups, doc.CountOffset, doc.GroupsStart,
            0, "TR v12 scenario section", Convert.ToHexStringLower(SHA256.HashData(body)));
    }

    static Document ParseBody(byte[] body, bool standalone)
    {
        if (body.Length > ExportFormats.MaxStoredBytes) throw new InvalidDataException("TR payload exceeds host bound.");
        var r = new Cursor(body);
        if (r.U32() != 12) throw new InvalidDataException("Only TR v12 body inspection reviewed.");
        if (!standalone)
        {
            var scripts = r.Count(1000, "script count");
            var lastHadBody = false;
            for (var i = 0; i < scripts; i++)
            {
                _ = r.Wide(); lastHadBody = r.Byte() switch { 0 => false, 1 => true, _ => throw new InvalidDataException("Invalid script flag.") };
                if (lastHadBody)
                    for (var j = r.Count(100_000, "script lines"); j > 0; j--) _ = r.Wide();
            }
            if (!lastHadBody) _ = r.U32(); // Scenario-only separator.
        }
        _ = r.U32(); _ = r.U32(); _ = r.U32(); // Retain opaque header words unchanged.
        var countOffset = r.Offset;
        var triggerCount = r.Count(MaxTriggers, "trigger count");
        var triggers = new Trigger[triggerCount];
        for (var i = 0; i < triggerCount; i++)
        {
            var start = r.Offset;
            if (r.U32() != 9) throw new InvalidDataException("Unsupported trigger record magic.");
            var idOffset = r.Offset;
            var id = r.U32(); var group = r.U32(); var priority = r.U32();
            var nameOffset = r.Offset;
            var name = r.Wide();
            var nameChars = checked((r.Offset - nameOffset - 4) / 2);
            _ = r.U32();
            var flagOffset = r.Offset;
            var flags = r.Take(5).ToArray();
            if (flags[0] > 1 || flags[1] > 1) throw new InvalidDataException("Invalid trigger loop/active flags.");
            var note = r.Wide();
            var conditions = ReadElements(r);
            var effectsCountOffset = r.Offset;
            var effects = ReadElements(r);
            triggers[i] = new(id, group, priority, name, note, flags, start, r.Offset,
                idOffset, flagOffset, nameOffset, nameChars, effectsCountOffset, conditions, effects);
        }
        var groupsStart = r.Offset;
        var groupCount = r.Count(MaxTriggers, "group count");
        var groups = new Group[groupCount];
        for (var i = 0; i < groupCount; i++)
        {
            var start = r.Offset;
            if (r.U32() != 1) throw new InvalidDataException("Unsupported TR group magic.");
            var id = r.U32(); var name = r.Narrow();
            var count = r.Count(MaxTriggers, "group member count");
            var indexes = new uint[count];
            for (var j = 0; j < count; j++) indexes[j] = r.U32();
            groups[i] = new(id, name, indexes, start, r.Offset);
        }
        if (r.Offset != r.Length) throw new InvalidDataException("TR payload contains unparsed trailing bytes.");
        if (triggers.Select(t => t.Id).Distinct().Count() != triggers.Length)
            throw new InvalidDataException("Duplicate trigger IDs; indexing unsafe.");
        return new(body, body, triggers, groups, countOffset, groupsStart, 0,
            "TR v12 body", Convert.ToHexStringLower(SHA256.HashData(body)));
    }

    static Element[] ReadElements(Cursor r)
    {
        var count = r.Count(MaxItems, "condition/effect count");
        var elements = new Element[count];
        for (var i = 0; i < count; i++)
        {
            var start = r.Offset;
            if (r.U32() != 6) throw new InvalidDataException("Unsupported condition/effect magic.");
            var name = r.Narrow(); var labelOffset = r.Offset; var kind = r.Narrow();
            var labelBytes = r.Offset - labelOffset;
            var argc = r.Count(512, "argument count");
            var args = new Arg[argc];
            for (var j = 0; j < argc; j++)
            {
                var argStart = r.Offset;
                _ = r.U32(); var key = r.Narrow(); var label = r.Narrow(); var type = r.U32();
                var valueStart = r.Offset;
                string[] values;
                switch (type)
                {
                    case 4:
                        values = WideList(r);
                        for (var k = r.Count(512, "proto list"); k > 0; k--) { _ = r.U32(); _ = r.U32(); _ = r.Wide(); }
                        _ = r.Byte(); break;
                    case 7: values = WideList(r); break;
                    case 22:
                        var n = r.Count(512, "string-id count"); _ = r.U32();
                        values = new string[n]; for (var k = 0; k < n; k++) values[k] = r.Wide();
                        break;
                    case 42 or 43 or 50:
                        _ = r.U32();
                        values = new string[type == 42 ? 3 : type == 43 ? 4 : 2];
                        for (var k = 0; k < values.Length; k++) values[k] = r.Wide();
                        break;
                    default:
                        if (type > 82) throw new InvalidDataException("Unsupported TR argument value type " + type + ".");
                        _ = r.U32(); values = [r.Wide()];
                        if (type is 2 or 5 or 8 or 56) _ = r.Byte();
                        break;
                }
                var valueEnd = r.Offset;
                _ = r.Byte(); // TR v12 argument trailer, preserved.
                args[j] = new(key, label, type, values, argStart, valueStart, valueEnd, r.Offset);
            }
            var cmd = r.Narrow();
            for (var extras = r.Count(512, "extra expression count"); extras > 0; extras--)
            {
                _ = r.Narrow(); _ = r.Byte();
                for (var k = r.Count(512, "extra string count"); k > 0; k--) _ = r.Narrow();
            }
            _ = r.Take(2);
            elements[i] = new(name, kind, args, cmd, start, r.Offset, labelOffset, labelBytes);
        }
        return elements;
    }

    static string[] WideList(Cursor r)
    {
        var count = r.Count(512, "value count");
        var values = new string[count];
        for (var i = 0; i < count; i++) values[i] = r.Wide();
        return values;
    }

    internal static bool EquivalentGameExport(byte[] desired, byte[] observed, out string reason)
    {
        var left = Parse(desired); var right = Parse(observed);
        if (!left.Body.AsSpan().SequenceEqual(right.Body))
        {
            reason = "TR body differs; unreviewed trigger/header data changed."; return false;
        }
        if (left.Format != right.Format)
        {
            reason = "TR suffix format changed."; return false;
        }
        if (left.Format != "TR v12 cameras")
        {
            var equal = desired.AsSpan(desired.Length - left.SuffixBytes)
                .SequenceEqual(observed.AsSpan(observed.Length - right.SuffixBytes));
            reason = equal ? "Exact TR body and sentinel verified." : "TR suffix differs.";
            return equal;
        }
        var names = CameraNames(left); var other = CameraNames(right);
        var equivalent = names.Length == other.Length && names.Skip(1).SequenceEqual(other.Skip(1));
        reason = equivalent ? "Exact TR body/camera entries verified; export-basename camera header ignored."
            : "Camera count/names differ beyond export-basename header.";
        return equivalent;
    }

    static string[] CameraNames(Document doc)
    {
        var bytes = doc.Original.AsSpan(doc.Original.Length - doc.SuffixBytes);
        var count = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        var offset = 4;
        var names = new string[count + 1];
        for (var i = 0; i < names.Length; i++)
        {
            var chars = BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]); offset += 4;
            names[i] = Utf16.GetString(bytes.Slice(offset, chars * 2)); offset += chars * 2;
        }
        return names;
    }

    internal static void SelfTest()
    {
        var controller = TriggerCodec.ReadFile(Path.Combine(AppContext.BaseDirectory, "trigger-controller-template.trg"));
        var bodyOnly = controller.AsSpan(0, controller.Length - 4).ToArray(); // Reviewed sentinel fixture.
        byte[] WithCameras(string exportName, string cameraName)
        {
            using var stream = new MemoryStream();
            stream.Write(bodyOnly);
            stream.Write(BitConverter.GetBytes(1));
            foreach (var name in new[] { exportName, cameraName })
            {
                stream.Write(BitConverter.GetBytes(name.Length)); stream.Write(Utf16.GetBytes(name));
            }
            return stream.ToArray();
        }
        var first = WithCameras("export-before", "Intro");
        var second = WithCameras("AomMcp-trigger-roundtrip-abcdef", "Intro");
        var tampered = WithCameras("export-after", "Other");
        if (!EquivalentGameExport(first, second, out _) || EquivalentGameExport(first, tampered, out _)
            || !EquivalentGameExport(controller, controller, out _))
            throw new InvalidOperationException("TR semantic comparison camera-basename fixture failed.");
    }

    internal static object Query(JsonElement args)
    {
        var doc = Read(args.GetProperty("path").GetString()!);
        var filter = args.TryGetProperty("filter", out var f) ? f.GetString() ?? "" : "";
        var offset = args.TryGetProperty("offset", out var o) ? o.GetInt32() : 0;
        var limit = args.TryGetProperty("limit", out var l) ? l.GetInt32() : 20;
        var candidates = doc.Triggers.Where(t => t.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || t.Conditions.Concat(t.Effects).Any(e => e.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || e.Kind.Contains(filter, StringComparison.OrdinalIgnoreCase))).ToArray();
        var search = args.TryGetProperty("player", out _) || args.TryGetProperty("arg", out _) || args.TryGetProperty("references", out _);
        if (search)
        {
            if (offset < 0 || limit is < 1 or > 200) throw new ArgumentException("offset/limit outside bounds.");
            var player = args.TryGetProperty("player", out var p) ? p.GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
            if (player is not null && (int.Parse(player, System.Globalization.CultureInfo.InvariantCulture) is < 0 or > 12))
                throw new ArgumentException("player outside 0..12.");
            string? key = null, value = null;
            if (args.TryGetProperty("arg", out var match))
            {
                Catalog.ValidateObject(match, ["key", "value"]);
                key = match.GetProperty("key").GetString(); value = match.GetProperty("value").GetString();
                if (string.IsNullOrWhiteSpace(key) || value is null) throw new ArgumentException("arg requires key/value strings.");
            }
            uint? references = args.TryGetProperty("references", out var r) ? r.GetUInt32() : null;
            if (references is not null && !doc.Triggers.Any(t => t.Id == references))
                throw new ArgumentException("Unknown referenced trigger ID.");
            var matches = new List<object>();
            foreach (var t in candidates)
            {
                if (args.TryGetProperty("triggerId", out var triggerFilter) && t.Id != triggerFilter.GetUInt32()) continue;
                foreach (var (kind, elements) in new[] { ("condition", t.Conditions), ("effect", t.Effects) })
                    for (var index = 0; index < elements.Length; index++)
                    {
                        var e = elements[index];
                        var hasPlayer = player is null || e.Args.Any(a => a.Key is "Player" or "PlayerID" or "FromPlayerID" or "ToPlayerID"
                            && a.Values.Contains(player));
                        var matchedArgs = e.Args.Where(a => key is not null
                            ? a.Key.Equals(key, StringComparison.OrdinalIgnoreCase) && a.Values.Contains(value!, StringComparer.Ordinal)
                            : player is null || a.Key is "Player" or "PlayerID" or "FromPlayerID" or "ToPlayerID"
                                && a.Values.Contains(player)).ToArray();
                        if (!hasPlayer || matchedArgs.Length == 0) continue;
                        if (references is not null)
                        {
                            if (!e.Name.StartsWith("Trigger:", StringComparison.OrdinalIgnoreCase)) continue;
                            matchedArgs = matchedArgs.Where(a => a.Key == "EventID" && a.Values.Any(v =>
                                uint.TryParse(v, out var id) && (id == references || t.Id == references))).ToArray();
                        }
                        foreach (var a in matchedArgs)
                            foreach (var v in a.Values.Where(v => key is null || v == value))
                            {
                                if (references is not null && (!uint.TryParse(v, out var target) || t.Id != references && target != references)) continue;
                                matches.Add(new { triggerId = t.Id, triggerName = t.Name, active = t.Flags[1] != 0,
                                    kind, elementIndex = index, elementName = e.Name, label = e.Kind, key = a.Key, value = Clip(v),
                                    direction = references is null ? null : t.Id == references ? "outgoing" : "incoming" });
                            }
                    }
            }
            return new { doc.Sha256, doc.Format, doc.SuffixBytes, total = matches.Count, offset, limit,
                nextOffset = offset + limit < matches.Count ? (int?)(offset + limit) : null,
                matches = matches.Skip(offset).Take(limit).ToArray(),
                limitation = "Exported TR snapshot only. Reference graph uses Trigger:* EventID links; no runtime proof." };
        }
        if (args.TryGetProperty("triggerId", out var wanted))
        {
            var id = wanted.GetUInt32();
            var t = doc.Triggers.SingleOrDefault(t => t.Id == id)
                ?? throw new ArgumentException("Unknown trigger ID in supplied export.");
            return new { doc.Sha256, doc.Format, doc.SuffixBytes, trigger = Describe(t, true),
                limitation = "Read-only exported snapshot. Unknown XS/AI runtime references not evaluated." };
        }
        return new { doc.Sha256, doc.Format, doc.SuffixBytes, total = candidates.Length, offset, limit,
            nextOffset = offset + limit < candidates.Length ? (int?)(offset + limit) : null,
            triggers = candidates.Skip(offset).Take(limit).Select(t => Describe(t, false)).ToArray(),
            limitation = "Exported snapshot only; names/IDs and parameters not proof of live trigger execution." };
    }

    static string Clip(string value) => value.Length <= 240 ? value : value[..240] + "…";
    static object Describe(Trigger t, bool full) => new
    {
        t.Id, t.Name, t.Group, t.Priority, active = t.Flags[1] != 0, loop = t.Flags[0] != 0,
        conditions = full ? t.Conditions.Select(DescribeElement).ToArray() : t.Conditions.Select(e => new { e.Name }).Cast<object>().ToArray(),
        effects = full ? t.Effects.Select(DescribeElement).ToArray() : t.Effects.Select(e => new { e.Name }).Cast<object>().ToArray(),
        note = full ? Clip(t.Note) : null,
    };
    static object DescribeElement(Element e) => new
    {
        e.Name, e.Kind, args = e.Args.Select(a => new { a.Key, a.Label, a.ValueType,
            values = a.Values.Take(20).Select(Clip).ToArray(), totalValues = a.Values.Length }).ToArray(),
    };
}
