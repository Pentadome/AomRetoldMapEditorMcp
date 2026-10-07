using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace AomMcp;

/// <summary>Read-only bounded game-written checkpoint reader; never serializes or changes scenario bytes.</summary>
internal static class ScenarioReader
{
    static readonly UnicodeEncoding Utf16 = new(false, false, true);
    internal sealed record Player(uint Id, string Name, string AiPath, int[] Diplomacy,
        int Control, int CivId, int ColorId, int StartAge, int MaxAge, int ClassicalGodId, int HeroicGodId, int MythicGodId, int Pop, int PopLimit,
        float Food, float Wood, float Gold, float Favor, int Visibility, float Handicap, byte[][] Sections, string DisplayName = "");
    internal sealed record Snapshot(string Path, string Sha256, Player[] Players, byte[]? TriggerSection,
        string Format, int SuffixBytes);

    static int U32(ReadOnlySpan<byte> bytes, int offset)
    {
        if (offset < 0 || bytes.Length - offset < 4) throw new InvalidDataException("Truncated scenario field.");
        var value = BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
        if (value > int.MaxValue) throw new InvalidDataException("Scenario field exceeds host bound.");
        return (int)value;
    }

    static string Wide(ReadOnlySpan<byte> data, ref int offset)
    {
        var n = U32(data, offset); offset += 4;
        if (n > 100_000 || n > (data.Length - offset) / 2)
            throw new InvalidDataException("Scenario UTF-16 string outside bound.");
        try { var value = Utf16.GetString(data.Slice(offset, n * 2)).TrimEnd('\0'); offset += n * 2; return value; }
        catch (DecoderFallbackException e) { throw new InvalidDataException("Invalid scenario UTF-16.", e); }
    }

    internal static Snapshot Read(string path) => Read(CheckpointDocument.Read(path));

    internal static Snapshot Read(CheckpointDocument document)
    {
        var players = ReadPlayers(document.World.One("PL").ToArray());
        return new(document.Path, document.Sha256, players, document.Root.One("TR").ToArray(),
            document.Format, document.SuffixBytes);
    }

    internal static Player[] ReadPlayers(byte[] data, bool requireReviewedVersion = false)
    {
        if (data.Length < 8) throw new InvalidDataException("PL section truncated.");
        var count = U32(data, 4);
        if (count is < 1 or > 32) throw new InvalidDataException("PL player count outside host bound.");
        var players = new Player[count];
        var off = 8;
        for (var i = 0; i < count; i++)
        {
            if (data.Length - off < 7 || !data.AsSpan(off, 3).SequenceEqual(new byte[] { 1, (byte)'B', (byte)'P' }))
                throw new InvalidDataException("PL player header mismatch.");
            var bpSize = U32(data, off + 3); off += 7;
            if (bpSize > data.Length - off) throw new InvalidDataException("PL player size outside bound.");
            var bp = data.AsSpan(off, bpSize); off += bpSize;
            var version = U32(bp, 0); // Player block version; field layout reviewed for game writer's v319.
            if (requireReviewedVersion && version != 319) throw new InvalidDataException("Unsupported player block version for semantic diff.");
            var sub = 4;
            uint playerId = uint.MaxValue;
            string? name = null, ai = null, displayName = null;
            int[]? diplomacy = null;
            var sections = new byte[6][];
            int control = -1, civ = -1, color = -1, age = -1, maxAge = -1, classicalGod = -1, heroicGod = -1, mythicGod = -1, pop = -1, popLimit = -1;
            float food = float.NaN, wood = float.NaN, gold = float.NaN, favor = float.NaN, handicap = float.NaN;
            int visibility = -1;
            for (var n = 1; n <= 6; n++)
            {
                if (bp.Length - sub < 6 || bp[sub] != 'P' || bp[sub + 1] != (byte)('0' + n))
                    throw new InvalidDataException("PL P" + n + " marker mismatch.");
                var length = U32(bp, sub + 2); sub += 6;
                if (length > bp.Length - sub) throw new InvalidDataException("PL subsection outside bound.");
                var body = bp.Slice(sub, length); sub += length;
                sections[n - 1] = body.ToArray();
                if (n == 1)
                {
                    playerId = (uint)U32(body, 0);
                    // First string is the editable player name (normal-UI Player Data typing "Bob" replaced "Player 2" here; the
                    // second string stayed empty). Name keeps its legacy (second-string) meaning for existing comparisons.
                    var at = 5; displayName = Wide(body, ref at); name = Wide(body, ref at);
                    _ = Wide(body, ref at); // String ID; observed independent of displayed name.
                    if (at >= body.Length) throw new InvalidDataException("PL control byte missing.");
                    control = body[at++];
                    _ = Wide(body, ref at); // Original slot label.
                }
                if (n == 2) civ = U32(body, 4); // P2 v319 culture ID; 21 Huitzilopochtli, 23 Quetzalcoatl.
                if (n == 3)
                {
                    age = U32(body, 0); // 0 Archaic, 1 Classical, 2 Heroic, 3 Mythic.
                    classicalGod = BinaryPrimitives.ReadInt32LittleEndian(body.Slice(8));
                    heroicGod = BinaryPrimitives.ReadInt32LittleEndian(body.Slice(12));
                    mythicGod = BinaryPrimitives.ReadInt32LittleEndian(body.Slice(16));
                    maxAge = BinaryPrimitives.ReadInt32LittleEndian(body.Slice(20)); // -1 Default, 3 Mythic (controlled UI diff).
                    if (age > 3 || maxAge is < -1 or > 3) throw new InvalidDataException("PL age outside observed bounds.");
                    popLimit = U32(body, 24); pop = U32(body, 28);
                }
                if (n == 5)
                {
                    var at = 3; ai = Wide(body, ref at);
                    if (body.Length - at < 18) throw new InvalidDataException("PL player color tail truncated.");
                    color = body[at + 5]; // Red P2/P7=2, cyan P3=5, grey P6=12.
                }
                if (n == 6)
                {
                    var dCount = U32(body, 0);
                    if (dCount is < 1 or > 32 || dCount > (body.Length - 4) / 4 || body.Length < 4 + dCount * 4 + 37)
                        throw new InvalidDataException("PL diplomacy/resources outside bound.");
                    diplomacy = new int[dCount];
                    for (var j = 0; j < dCount; j++) diplomacy[j] = BinaryPrimitives.ReadInt32LittleEndian(body.Slice(4 + 4 * j));
                    var at = body.Length - 37; // Four floats + their sum; resource order Gold/Wood/Food/Favor.
                    gold = BinaryPrimitives.ReadSingleLittleEndian(body[at..]);
                    wood = BinaryPrimitives.ReadSingleLittleEndian(body[(at + 4)..]);
                    food = BinaryPrimitives.ReadSingleLittleEndian(body[(at + 8)..]);
                    favor = BinaryPrimitives.ReadSingleLittleEndian(body[(at + 12)..]);
                    var sum = BinaryPrimitives.ReadSingleLittleEndian(body[(at + 16)..]);
                    if (!float.IsFinite(sum) || !float.IsFinite(food) || !float.IsFinite(gold) || !float.IsFinite(wood)
                        || !float.IsFinite(favor) || Math.Abs(sum - (food + wood + gold + favor)) > Math.Max(1, Math.Abs(sum) * 0.0001f))
                        throw new InvalidDataException("PL resource sum validation failed.");
                }
            }
            // Post-P6 versioned P7/P8 blocks carry handicap and visibility, observed via controlled UI change.
            if (bp.Length - sub < 6 || bp[sub] != 'P' || bp[sub + 1] != '7')
                throw new InvalidDataException("PL P7 marker missing.");
            var p7Len = U32(bp, sub + 2); sub += 6;
            if (p7Len != 9 || bp.Length - sub < p7Len) throw new InvalidDataException("PL P7 layout unreviewed.");
            handicap = BinaryPrimitives.ReadSingleLittleEndian(bp.Slice(sub + 5)); sub += p7Len;
            if (bp.Length - sub < 6 || bp[sub] != 'P' || bp[sub + 1] != '8')
                throw new InvalidDataException("PL P8 marker missing.");
            var p8Len = U32(bp, sub + 2); sub += 6;
            if (p8Len is < 12 or > 256 || bp.Length - sub < p8Len)
                throw new InvalidDataException("PL P8 layout unreviewed.");
            visibility = U32(bp.Slice(sub, p8Len), p8Len - 4);
            if (visibility is < 0 or > 2 || !float.IsFinite(handicap))
                throw new InvalidDataException("PL visibility/handicap outside reviewed bounds.");
            if (playerId != i || name is null || ai is null || diplomacy is null)
                throw new InvalidDataException("PL player ID/fields mismatch; no guessed mapping.");
            players[i] = new(playerId, name, ai, diplomacy, control, civ, color, age, maxAge, classicalGod, heroicGod, mythicGod,
                pop, popLimit, food, wood, gold, favor, visibility, handicap, sections, displayName ?? "");
        }
        return players;
    }

    internal static object Query(JsonElement args)
    {
        var snapshot = Read(args.GetProperty("path").GetString()!);
        var player = args.TryGetProperty("player", out var p) ? p.GetInt32() : -1;
        if (player < -1 || player >= snapshot.Players.Length) throw new ArgumentException("Player slot not present in checkpoint.");
        var raw = args.TryGetProperty("raw", out var rawArg) && rawArg.GetBoolean();
        if (raw && player < 0) throw new ArgumentException("raw subsection dump requires one player (bounded to 6×512 bytes).");
        var rawSections = raw ? snapshot.Players[player].Sections.Select((bytes, index) => new
        {
            section = "P" + (index + 1), length = bytes.Length,
            hex = Convert.ToHexStringLower(bytes.AsSpan(0, Math.Min(bytes.Length, 512))), truncated = bytes.Length > 512,
        }).ToArray() : null;
        return new { snapshot.Path, snapshot.Sha256, snapshot.Format, snapshot.SuffixBytes,
            players = snapshot.Players.Where(p => player == -1 || p.Id == player)
                .Select(p => new { p.Id, p.Name, p.DisplayName, p.AiPath,
                    control = p.Control switch { 0 => "Human", 1 => "Computer", 3 => "Unavailable", _ => "unknown" },
                    p.CivId, p.ColorId, p.StartAge, p.MaxAge,
                    minorGodIds = new { p.ClassicalGodId, p.HeroicGodId, p.MythicGodId },
                    p.Pop, p.PopLimit, p.Handicap,
                    visibility = p.Visibility switch { 0 => "Normal", 1 => "Hidden", _ => "unknown" },
                    resources = new { p.Food, p.Wood, p.Gold, p.Favor },
                    diplomacy = p.Diplomacy.Select((stance, target) => new
                    { target, stance, label = stance switch { 0 => "self/unset (observed)", 1 => "ally (observed)", 2 => "enemy (observed)", 3 => "neutral (live checkpoint observed)", _ => "unknown" } }).ToArray() }).ToArray(),
            rawSections, limitation = "Read-only game-written checkpoint. Fields observed in v319 om10; age IDs 0=Archaic/1=Classical/2=Heroic/3=Mythic, maxAge -1=Default. Minor gods are raw engine IDs; names not resolved. Visibility 0=Normal/1=Hidden. No .mythscn edits or runtime proof." };
    }
}
