using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AomMcp;

/// <summary>Reviewed saved-entity fields only. No scenario serialization or live-identity claims.</summary>
internal static class CheckpointUnits
{
    internal sealed record Unit(uint UnitId, uint Player, string? Proto, string[] PrototypeCandidates,
        float X, float Y, float Z, string? Note, string RecordSha256, string OpaqueSha256);
    internal sealed record Snapshot(Unit[] Units, int UnresolvedPrototypes);
    internal static Snapshot Read(CheckpointDocument doc)
    {
        if (doc.WorldHeader != 444) throw new InvalidDataException("Entity layout supports observed J1 header 444 only.");
        var pt = new CheckpointDocument.Cursor(doc.World.One("PT"));
        if (pt.U32() != 0) throw new InvalidDataException("Unsupported PT header.");
        var names = new string[pt.Count(100_000)];
        for (var i = 0; i < names.Length; i++)
        {
            names[i] = Encoding.Latin1.GetString(pt.Take(pt.Count(1024)).Span).TrimEnd('\0');
            if (names[i].Length == 0 || names[i].Any(char.IsControl)) throw new InvalidDataException("Invalid PT prototype string.");
        }
        if (pt.Remaining != 0) throw new InvalidDataException("Unparsed PT suffix.");
        var z = new CheckpointDocument.Cursor(doc.World.One("Z1")); var count = z.Count(200_000);
        if (z.Byte() != 1) throw new InvalidDataException("Unsupported Z1 header.");
        var units = new Unit[count]; var ids = new HashSet<uint>();
        for (var i = 0; i < count; i++)
        {
            var id = z.U32(); if (!ids.Add(id)) throw new InvalidDataException("Duplicate saved entity ID.");
            var body = z.Block("H1"); var h = new CheckpointDocument.Cursor(body);
            var en = h.Block("EN"); var p1 = h.Block("P1"); var p2 = h.Block("P2");
            if (en.Length != 80 || p1.Length is not (86 or 128 or 178 or 220) || p2.Length != 15)
                throw new InvalidDataException("Unsupported EN/P1/P2 record size.");
            if (BinaryPrimitives.ReadUInt32LittleEndian(en.Span) != id) throw new InvalidDataException("Registry/EN ID mismatch.");
            var owner = BinaryPrimitives.ReadUInt32LittleEndian(en.Span[8..]);
            var x = BinaryPrimitives.ReadSingleLittleEndian(en.Span[32..]);
            var y = BinaryPrimitives.ReadSingleLittleEndian(en.Span[36..]);
            var zz = BinaryPrimitives.ReadSingleLittleEndian(en.Span[40..]);
            if (owner > 12 || !float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(zz)
                || Math.Abs(x) > 1_000_000 || Math.Abs(y) > 1_000_000 || Math.Abs(zz) > 1_000_000)
                throw new InvalidDataException("Saved owner/coordinates outside reviewed bounds.");
            var first = BinaryPrimitives.ReadUInt32LittleEndian(p1.Span); var second = BinaryPrimitives.ReadUInt32LittleEndian(p1.Span[4..]);
            string? Name(uint index) => index < names.Length ? names[index] : null;
            var a = Name(first); var b = Name(second); var proto = a is not null && a == b ? a : null;
            var candidates = new[] { a, b }.Where(s => s is not null).Cast<string>().Distinct(StringComparer.Ordinal).ToArray();
            var reserved = h.Take(24); if (h.U32() != 10) throw new InvalidDataException("Unsupported entity field-count variant.");
            var fields = h.Take(40); var present = h.Byte();
            if (present > 1) throw new InvalidDataException("Invalid saved note flag.");
            var note = present == 1 ? h.Wide(100_000) : null; var tail = h.Take(h.Remaining);
            using var opaque = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            opaque.AppendData(en.Span.Slice(4, 4)); opaque.AppendData(en.Span.Slice(12, 20)); opaque.AppendData(en.Span[44..]);
            opaque.AppendData(p1.Span[8..]); opaque.AppendData(p2.Span); opaque.AppendData(reserved.Span); opaque.AppendData(fields.Span); opaque.AppendData(tail.Span);
            units[i] = new(id, owner, proto, candidates, x, y, zz, note, CheckpointDocument.Hash(body.Span), Convert.ToHexStringLower(opaque.GetHashAndReset()));
        }
        if (z.Remaining != 0) throw new InvalidDataException("Unparsed Z1 suffix.");
        return new(units, units.Count(u => u.Proto is null));
    }
    internal static void Preflight(JsonElement args)
    {
        Catalog.ValidateObject(args, ["scenarioPath", "expectedSha256", "unitIds", "proto", "player", "noteContains", "hasNote", "area", "offset", "limit"]);
        _ = EditorFiles.LocalPath(args.GetProperty("scenarioPath").GetString()!);
        var offset = args.TryGetProperty("offset", out var o) ? o.GetInt32() : 0; var limit = args.TryGetProperty("limit", out var l) ? l.GetInt32() : 20;
        if (offset < 0 || limit is < 1 or > 200) throw new ArgumentException("offset/limit outside bounds.");
        if (args.TryGetProperty("player", out var player) && player.GetUInt32() > 12) throw new ArgumentException("Player outside 0..12.");
        if (args.TryGetProperty("unitIds", out var ids) && (ids.ValueKind != JsonValueKind.Array || ids.GetArrayLength() is < 1 or > 200))
            throw new ArgumentException("unitIds requires 1..200 saved IDs.");
        if (args.TryGetProperty("area", out var area))
        {
            Catalog.ValidateObject(area, ["minX", "maxX", "minZ", "maxZ"]);
            var xmin = area.GetProperty("minX").GetDouble(); var xmax = area.GetProperty("maxX").GetDouble();
            var zmin = area.GetProperty("minZ").GetDouble(); var zmax = area.GetProperty("maxZ").GetDouble();
            if (!double.IsFinite(xmin + xmax + zmin + zmax) || xmin > xmax || zmin > zmax) throw new ArgumentException("Invalid world-XZ area.");
        }
    }
    internal static object Query(JsonElement args)
    {
        Preflight(args); var doc = CheckpointDocument.Read(args.GetProperty("scenarioPath").GetString()!, CheckpointDocument.Expected(args)); var snapshot = Read(doc);
        IEnumerable<Unit> found = snapshot.Units;
        if (args.TryGetProperty("unitIds", out var ids)) { var wanted = ids.EnumerateArray().Select(x => x.GetUInt32()).ToHashSet(); found = found.Where(u => wanted.Contains(u.UnitId)); }
        if (args.TryGetProperty("proto", out var proto)) found = found.Where(u => u.Proto is not null && u.Proto == proto.GetString());
        if (args.TryGetProperty("player", out var player)) found = found.Where(u => u.Player == player.GetUInt32());
        if (args.TryGetProperty("noteContains", out var text)) found = found.Where(u => u.Note?.Contains(text.GetString()!, StringComparison.OrdinalIgnoreCase) == true);
        if (args.TryGetProperty("hasNote", out var has)) found = found.Where(u => (u.Note is not null) == has.GetBoolean());
        if (args.TryGetProperty("area", out var area)) found = found.Where(u => u.X >= area.GetProperty("minX").GetDouble() && u.X <= area.GetProperty("maxX").GetDouble()
            && u.Z >= area.GetProperty("minZ").GetDouble() && u.Z <= area.GetProperty("maxZ").GetDouble());
        var all = found.OrderBy(u => u.UnitId).ToArray(); var offset = args.TryGetProperty("offset", out var o) ? o.GetInt32() : 0; var limit = args.TryGetProperty("limit", out var l) ? l.GetInt32() : 20;
        return new { scenarioPath = doc.Path, sha256 = doc.Sha256, doc.Format, total = all.Length, offset, limit,
            nextOffset = offset + limit < all.Length ? (int?)(offset + limit) : null,
            units = all.Skip(offset).Take(limit).Select(u => new { u.UnitId, u.Player, u.Proto, u.PrototypeCandidates,
                prototypeStatus = u.Proto is null ? "unresolved-or-ambiguous" : "resolved-saved", u.X, u.Y, u.Z,
                note = u.Note is { Length: > 4096 } ? u.Note[..4096] : u.Note, noteCharacters = u.Note?.Length,
                noteTruncated = u.Note is { Length: > 4096 }, u.RecordSha256, u.OpaqueSha256 }).ToArray(), snapshot.UnresolvedPrototypes, decoding = "partial-reviewed-entity-fields",
            provenance = "Saved Z1/H1/EN/P1/P2, observed J1=444/PT=0. IDs are saved identities, not verified current live IDs.",
            limitation = "Opaque entity fields/tails retained by hashes. No pathfinding, live state, or complete scenario validation." };
    }
    internal static void SelfTest()
    {
        foreach (var note in new string?[] { null, "", "Tower", "塔 — Δέντρο 🌴" })
        foreach (var size in new[] { 86, 128, 178, 220 })
        {
            var unit = Read(CheckpointDocument.Decode("synthetic", WorkflowFixtures.Checkpoint(note, p1Size: size))).Units.Single();
            if (unit.UnitId != 100 || unit.Player != 6 || unit.Proto != "CinematicBlockSpawnPoint" || unit.Note != note || unit.X != 10 || unit.Z != 20)
                throw new InvalidOperationException("Saved entity/note fixture failed.");
        }
        try { _ = Read(CheckpointDocument.Decode("unsupported", WorkflowFixtures.Checkpoint(worldHeader: 445))); throw new InvalidOperationException("Unsupported entity header accepted."); }
        catch (InvalidDataException) { }
        try { _ = Read(CheckpointDocument.Decode("unsupported", WorkflowFixtures.Checkpoint(p1Size: 87))); throw new InvalidOperationException("Unsupported P1 size accepted."); }
        catch (InvalidDataException) { }
        foreach (var bytes in new[] { WorkflowFixtures.Checkpoint(duplicateId: true), WorkflowFixtures.Checkpoint(owner: 13),
            WorkflowFixtures.Checkpoint(x: float.NaN), WorkflowFixtures.Checkpoint(noteFlag: 2), WorkflowFixtures.Checkpoint(badUtf16: true) })
        {
            try { _ = Read(CheckpointDocument.Decode("invalid", bytes)); throw new InvalidOperationException("Malformed saved entity accepted."); }
            catch (InvalidDataException) { }
        }
        if (Read(CheckpointDocument.Decode("ambiguous", WorkflowFixtures.Checkpoint(secondPrototype: 1))).Units[0].Proto is not null
            || Read(CheckpointDocument.Decode("unresolved", WorkflowFixtures.Checkpoint(secondPrototype: 999))).Units[0].Proto is not null)
            throw new InvalidOperationException("Ambiguous/unresolved saved prototype inferred.");
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "aom-notes-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = System.IO.Path.Combine(directory, "synthetic.mythscn"); File.WriteAllBytes(path, WorkflowFixtures.Checkpoint());
            var args = JsonSerializer.SerializeToElement(new { scenarioPath = path, expectedSha256 = Layout.Hash(path), player = 6, proto = "CinematicBlockSpawnPoint", noteContains = "塔", offset = 0, limit = 1 });
            var result = JsonSerializer.SerializeToElement(Query(args));
            if (result.GetProperty("total").GetInt32() != 1) throw new InvalidOperationException("Saved note filters failed.");
            var paged = JsonSerializer.SerializeToElement(Query(JsonSerializer.SerializeToElement(new { scenarioPath = path, offset = 1, limit = 1 })));
            if (paged.GetProperty("units").GetArrayLength() != 0 || paged.GetProperty("total").GetInt32() != 1) throw new InvalidOperationException("Saved note paging failed.");
        }
        finally { Directory.Delete(directory, true); } // Only this self-test's owned synthetic fixture.
        if (File.Exists("research/BaseDefense.mythscn") && Read(CheckpointDocument.Read(System.IO.Path.GetFullPath("research/BaseDefense.mythscn"))).Units.Length != 25)
            throw new InvalidOperationException("Reviewed BaseDefense entity count differs.");
    }
}
