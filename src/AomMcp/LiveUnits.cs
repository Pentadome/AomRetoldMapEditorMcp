using System.Text;
using System.Text.Json;

namespace AomMcp;

/// <summary>Pins a reviewed live-code fragment that supports passive object field interpretation.</summary>
/// <param name="Rva">Code address relative to the executable module.</param>
/// <param name="Hex">Exact reviewed runtime instruction bytes, not transformed on-disk code.</param>
public sealed record UnitReadSignature(uint Rva, string Hex);

/// <summary>Optional, build-pinned simulation-object layout; absent on unreviewed generator candidates.</summary>
/// <param name="WorldGlobalRva">Global loaded by uiLookAtAndSelectUnit.</param>
/// <param name="WorldPointerOffset">Root pointer's simulation-world member.</param>
/// <param name="CapacityOffset">World object-slot capacity member.</param>
/// <param name="ArrayOffset">World object-pointer array member.</param>
/// <param name="IndexMask">Mask extracting slot index from generation-bearing object ID.</param>
/// <param name="IdOffset">Object's full ID, checked by native selection lookup.</param>
/// <param name="ProtoOffset">Object's runtime prototype ID.</param>
/// <param name="PlayerOffset">Object's actual owner, not prototype override player.</param>
/// <param name="PositionOffsets">World X/Y/Z members read by native camera centering.</param>
/// <param name="HealthOffset">Current hitpoints numerator in native health fraction.</param>
/// <param name="MaxHealthOffset">Effective maximum hitpoints denominator.</param>
/// <param name="ProtoRootOffset">Command context's prototype registry member.</param>
/// <param name="ProtoCapacityOffset">Prototype registry's base prototype count.</param>
/// <param name="ProtoArrayOffset">Prototype registry's descriptor-pointer array.</param>
/// <param name="ProtoNameOffset">Descriptor's internal ASCII name pointer, used by kbProtoUnitGetName.</param>
/// <param name="Signatures">Independent code fragments supporting the recovered fields.</param>
public sealed record UnitReadLayout(
    uint WorldGlobalRva, int WorldPointerOffset, int CapacityOffset, int ArrayOffset, uint IndexMask,
    int IdOffset, int ProtoOffset, int PlayerOffset, int[] PositionOffsets, int HealthOffset,
    int MaxHealthOffset, int ProtoRootOffset, int ProtoCapacityOffset, int ProtoArrayOffset,
    int ProtoNameOffset, UnitReadSignature[] Signatures
);

/// <summary>Reads bounded simulation-object snapshots without injection, focus, input or game calls.</summary>
internal static class LiveUnits
{
    // Host ceilings, not guaranteed engine limits: at most 2 MiB of AMD64 slots, 100k base protos,
    // 256 ASCII name bytes, bounded pages and finite world-coordinate/radius range.
    const int MaxSlots = 262144, MaxProtos = 100000, MaxName = 256;
    internal const int DefaultLimit = 100, MaxLimit = 200, MaxPlayer = 12;
    internal const double MaxCoordinate = 1000000;
    // AMD64 pointers / IEEE binary32 fields (Windows process ABI), and Windows minimum page size.
    const int PointerSize = 8, ScalarSize = 4, PageSize = 4096, NameChunkSize = 32;
    // Host pointer/offset/signature sanity policy, not inferred object size or ABI guarantees.
    const long MinPointer = 65536, MaxPointer = 0x7fffffffffff;
    const int MaxOffset = 65536, MinSignatures = 5, MaxSignatures = 16, MinSignatureBytes = 16, MaxSignatureBytes = 512;
    internal sealed record Position(float X, float Y, float Z);
    internal sealed record Unit(int UnitId, int ProtoId, string? Proto, int Player, Position Position,
        float Health, float MaxHealth, float? HeadingDegrees = null);

    // Host tolerance for treating the reviewed transform's rotation block as a pure Y rotation.
    const float RotationTolerance = 0.01f;

    /// <summary>Derives a Y-axis heading from the reviewed 3x4 transform rotation block (research/LIVE-WORLD.md).</summary>
    /// <param name="m00">Row 0 column 0.</param><param name="m02">Row 0 column 2.</param>
    /// <param name="m20">Row 2 column 0.</param><param name="m22">Row 2 column 2.</param>
    /// <returns>Degrees in [0,360) where untouched editor placements read 180; null when not a pure Y rotation.</returns>
    internal static float? Heading(float m00, float m02, float m20, float m22)
    {
        if (!float.IsFinite(m00) || !float.IsFinite(m02) || !float.IsFinite(m20) || !float.IsFinite(m22)
            || Math.Abs(m00 * m00 + m02 * m02 - 1) > RotationTolerance || Math.Abs(m20 * m20 + m22 * m22 - 1) > RotationTolerance
            || Math.Abs(m00 - m22) > RotationTolerance || Math.Abs(m02 + m20) > RotationTolerance)
            return null;
        var degrees = (float)(Math.Atan2(m02, m22) * 180 / Math.PI);
        degrees = (degrees % 360 + 360) % 360;
        return (float)Math.Round(degrees, 2);
    }

    internal static void ValidateLayout(UnitReadLayout layout)
    {
        if (layout.PositionOffsets is null || layout.Signatures is null)
            throw new InvalidDataException("Missing live-unit fields/signatures.");
        var offsets = new[] { layout.WorldPointerOffset, layout.CapacityOffset, layout.ArrayOffset,
            layout.IdOffset, layout.ProtoOffset, layout.PlayerOffset, layout.HealthOffset,
            layout.MaxHealthOffset, layout.ProtoRootOffset, layout.ProtoCapacityOffset,
            layout.ProtoArrayOffset, layout.ProtoNameOffset }.Concat(layout.PositionOffsets);
        if (layout.WorldGlobalRva == 0 || layout.PositionOffsets.Length != 3
            || offsets.Any(o => o is < 0 or > MaxOffset)
            || layout.IndexMask is 0 or >= MaxSlots || (layout.IndexMask & (layout.IndexMask + 1)) != 0
            || layout.Signatures.Length is < MinSignatures or > MaxSignatures
            || layout.Signatures.Any(s => s.Rva == 0 || Convert.FromHexString(s.Hex).Length is < MinSignatureBytes or > MaxSignatureBytes))
            throw new InvalidDataException("Incomplete/unbounded live-unit layout; no object reads attempted.");
    }

    /// <summary>Captures and filters live editor objects, failing on registry/identity changes.</summary>
    /// <param name="game">Existing hash/signature-validated read-only connection.</param>
    /// <param name="args">Schema-validated prototype/player/area filters and page bounds.</param>
    /// <returns>Object IDs, exact resolvable names, owners, world positions and current/effective maximum health.</returns>
    public static object Query(Game game, JsonElement args)
    {
        var offset = args.TryGetProperty("offset", out var o) ? o.GetInt32() : 0;
        var limit = args.TryGetProperty("limit", out var l) ? l.GetInt32() : DefaultLimit;
        if (offset < 0 || limit is < 1 or > MaxLimit)
            throw new ArgumentException("Unit offset must be >= 0; limit must be 1..200.");
        var units = Read(game);
        var unresolved = units.Count(u => u.Proto is null);
        if (args.TryGetProperty("proto", out _) && unresolved != 0)
            throw new InvalidDataException("Prototype name filter cannot be complete: player-local/unresolved prototype names present. Query without proto to inspect numeric IDs.");
        var matches = Filter(units, args);
        var page = matches.Skip(offset).Take(limit).ToArray();
        return new
        {
            pid = game.Pid,
            buildHash = game.Layout.ExeSha256,
            capturedAtUtc = DateTime.UtcNow,
            totalObjects = units.Length,
            total = matches.Length,
            unresolvedPrototypeCount = unresolved,
            offset,
            limit,
            nextOffset = offset < matches.Length && page.Length < matches.Length - offset
                ? (int?)(offset + page.Length) : null,
            units = page,
            atomic = false,
            limitation = "ReadProcessMemory only; no focus, input or game-function calls. Registry and IDs rechecked, not an atomic simulation-frame snapshot. IDs valid for current scenario/object lifetime; reload/delete/recreate can invalidate them. Positions are world X/Y/Z, area uses X/Z. Names from live base registry; player-local prototypes can be null, never guessed from XML order. Includes buildings/resources/decorations in object registry, not only military units.",
        };
    }

    // Shared by the three passive live-state tools, never used to invoke native functions.
    internal static nint ValidateRuntime(Game game, UnitReadSignature[] signatures)
    {
        var editor = game.Editor();
        if (game.Process.HasExited || Win.GetWindowThreadProcessId(game.Window, out var owner) != game.Thread
            || owner != game.Pid)
            throw new InvalidOperationException("Game window/process changed; no object reads attempted.");
        if (signatures.Length is < 1 or > MaxSignatures)
            throw new InvalidDataException("Live read signatures missing/unbounded.");
        foreach (var signature in signatures)
        {
            var expected = Convert.FromHexString(signature.Hex);
            if (expected.Length is < MinSignatureBytes or > MaxSignatureBytes
                || (ulong)signature.Rva + (uint)expected.Length > (ulong)game.Process.MainModule!.ModuleMemorySize
                || !game.Read(game.Base + checked((int)signature.Rva), expected.Length).AsSpan().SequenceEqual(expected))
                throw new InvalidDataException("Live read code signature mismatch; no object reads attempted.");
        }
        var context = game.Pointer(game.Base + checked((int)game.Layout.ContextRva));
        if (context == 0 || game.UInt(context + checked((int)game.Layout.OwnerOffset)) != game.Thread)
            throw new InvalidDataException("Live command context does not match window thread.");
        return editor;
    }

    internal static Unit[] Read(Game game)
    {
        var layout = game.Layout.Units ?? throw new InvalidDataException(
            "Live-unit layout unavailable for this build. Passive review required; offsets are never guessed.");
        ValidateLayout(layout);
        var editor = ValidateRuntime(game, layout.Signatures);
        if (game.Layout.World is { } world) ValidateRuntime(game, world.Signatures); // Heading fields are pinned by world signatures.
        var units = Capture(game.Read, game.Base, game.Layout);
        if (game.Editor() != editor)
            throw new InvalidDataException("Editor context changed during unit read; no listing returned.");
        return units;
    }

    static Unit[] Capture(Func<nint, int, byte[]> read, nint module, Layout accepted)
    {
        var layout = accepted.Units!;
        nint Pointer(nint address)
        {
            var value = checked((nint)BitConverter.ToInt64(read(address, PointerSize)));
            // AMD64 user-address sanity; readable pages still verified by exact process reads.
            if (value != 0 && ((long)value < MinPointer || (long)value > MaxPointer))
                throw new InvalidDataException("Invalid live object pointer.");
            return value;
        }
        int Int(nint address) => BitConverter.ToInt32(read(address, ScalarSize));
        var root = Pointer(module + checked((int)layout.WorldGlobalRva));
        if (root == 0)
            throw new InvalidDataException("Live world unavailable.");
        var world = Pointer(root + layout.WorldPointerOffset);
        if (world == 0)
            throw new InvalidDataException("Live world unavailable.");
        var count = Int(world + layout.CapacityOffset);
        var table = Pointer(world + layout.ArrayOffset);
        if (count < 0 || count > layout.IndexMask + 1 || count > MaxSlots || (count > 0 && table == 0))
            throw new InvalidDataException("Invalid live object registry bounds.");
        var slots = count == 0 ? [] : read(table, checked(count * PointerSize)); // Empty registry permits null table.
        var context = Pointer(module + checked((int)accepted.ContextRva));
        var contextOwner = context == 0 ? 0 : Int(context + checked((int)accepted.OwnerOffset));
        if (context == 0 || contextOwner <= 0)
            throw new InvalidDataException("Live command context unavailable.");
        var protoRoot = Pointer(context + layout.ProtoRootOffset);
        if (protoRoot == 0)
            throw new InvalidDataException("Live prototype registry unavailable.");
        var protoCount = Int(protoRoot + layout.ProtoCapacityOffset);
        var protoTable = Pointer(protoRoot + layout.ProtoArrayOffset);
        if (protoCount < 0 || protoCount > MaxProtos || (protoCount > 0 && protoTable == 0))
            throw new InvalidDataException("Invalid live prototype registry bounds.");
        var names = new Dictionary<int, string?>(); // Per-snapshot cache, never stale runtime ID guesses.
        string? Name(int id)
        {
            if (names.TryGetValue(id, out var cached))
                return cached;
            string? name = null;
            if (id < protoCount)
            {
                var proto = Pointer(protoTable + checked(id * PointerSize));
                if (proto == 0)
                    throw new InvalidDataException("Live base prototype descriptor missing.");
                var address = Pointer(proto + layout.ProtoNameOffset);
                if (address == 0)
                    throw new InvalidDataException("Live prototype name missing.");
                var bytes = new List<byte>();
                for (var at = 0; at < MaxName;)
                {
                    // Windows pages are at least 4 KiB; don't cross page boundaries for short C strings.
                    var length = Math.Min(NameChunkSize, PageSize - (int)((long)(address + at) & (PageSize - 1)));
                    var chunk = read(address + at, Math.Min(length, MaxName - at));
                    var end = Array.IndexOf(chunk, (byte)0);
                    bytes.AddRange(chunk.Take(end < 0 ? chunk.Length : end));
                    if (end >= 0)
                    {
                        // Printable ASCII 32..126; internal prototype names are not localized labels.
                        if (bytes.Count == 0 || bytes.Any(b => b is < 32 or > 126))
                            throw new InvalidDataException("Invalid live prototype name encoding.");
                        name = Encoding.ASCII.GetString(bytes.ToArray());
                        break;
                    }
                    at += chunk.Length;
                }
                if (name is null)
                    throw new InvalidDataException("Live prototype name exceeds host bound.");
            }
            names[id] = name; // Player-local IDs remain explicitly unresolved.
            return name;
        }
        var rotation = accepted.World?.RotationOffsets is { Length: 4 } r && r.All(o => o is >= 0 and <= MaxOffset) ? r : null;
        var length = new[] { layout.IdOffset, layout.ProtoOffset, layout.PlayerOffset,
            layout.HealthOffset, layout.MaxHealthOffset }.Concat(layout.PositionOffsets).Concat(rotation ?? []).Max() + ScalarSize;
        var results = new List<Unit>();
        for (var slot = 0; slot < count; slot++)
        {
            var address = (nint)BitConverter.ToInt64(slots, slot * PointerSize);
            if (address == 0)
                continue;
            var bytes = read(address, length);
            var id = BitConverter.ToInt32(bytes, layout.IdOffset);
            var proto = BitConverter.ToInt32(bytes, layout.ProtoOffset);
            var player = BitConverter.ToInt32(bytes, layout.PlayerOffset);
            var position = layout.PositionOffsets.Select(p => BitConverter.ToSingle(bytes, p)).ToArray();
            var hp = BitConverter.ToSingle(bytes, layout.HealthOffset);
            var maximum = BitConverter.ToSingle(bytes, layout.MaxHealthOffset);
            if (id < 0 || (id & layout.IndexMask) != slot || proto < 0 || player is < 0 or > MaxPlayer
                || position.Any(v => !float.IsFinite(v) || Math.Abs(v) > MaxCoordinate)
                || !float.IsFinite(hp) || !float.IsFinite(maximum) || hp < 0 || maximum < 0)
                throw new InvalidDataException("Invalid/torn live object record; no listing returned.");
            float? heading = rotation is null ? null : Heading(BitConverter.ToSingle(bytes, rotation[0]),
                BitConverter.ToSingle(bytes, rotation[1]), BitConverter.ToSingle(bytes, rotation[2]), BitConverter.ToSingle(bytes, rotation[3]));
            results.Add(new Unit(id, proto, Name(proto), player,
                new Position(position[0], position[1], position[2]), hp, maximum, heading));
            if (Pointer(table + slot * PointerSize) != address || Int(address + layout.IdOffset) != id
                || Int(address + layout.ProtoOffset) != proto || Int(address + layout.PlayerOffset) != player)
                throw new InvalidDataException("Object changed during unit read; no listing returned.");
        }
        if (Pointer(module + checked((int)layout.WorldGlobalRva)) != root
            || Pointer(root + layout.WorldPointerOffset) != world || Int(world + layout.CapacityOffset) != count
            || Pointer(world + layout.ArrayOffset) != table || (count > 0 && !read(table, count * PointerSize).AsSpan().SequenceEqual(slots))
            || Pointer(module + checked((int)accepted.ContextRva)) != context
            || Int(context + checked((int)accepted.OwnerOffset)) != contextOwner
            || Pointer(context + layout.ProtoRootOffset) != protoRoot
            || Int(protoRoot + layout.ProtoCapacityOffset) != protoCount
            || Pointer(protoRoot + layout.ProtoArrayOffset) != protoTable)
            throw new InvalidDataException("Registry changed during unit read; no listing returned.");
        return results.ToArray();
    }

    static Unit[] Filter(Unit[] units, JsonElement args)
    {
        var proto = args.TryGetProperty("proto", out var p) ? p.GetString() : null;
        var player = args.TryGetProperty("player", out var owner) ? (int?)owner.GetInt32() : null;
        double? x = null, z = null;
        double radius = 0;
        if (args.TryGetProperty("area", out var area))
        {
            x = area.GetProperty("x").GetDouble();
            z = area.GetProperty("z").GetDouble();
            radius = area.GetProperty("radius").GetDouble();
        }
        return units.Where(u => (proto is null || string.Equals(u.Proto, proto, StringComparison.OrdinalIgnoreCase))
            && (player is null || u.Player == player)
            && (x is null || Math.Pow(u.Position.X - x.Value, 2) + Math.Pow(u.Position.Z - z!.Value, 2) <= radius * radius))
            .OrderBy(u => u.UnitId).ToArray();
    }

    /// <summary>Checks passive parsing/filter/race refusals using synthetic memory, never a game process.</summary>
    public static void SelfTest()
    {
        // Synthetic fixture only: five 16-byte zero signatures with dummy RVAs 1..5; no game code.
        // Two slots (mask 3 permits 4 slots), one ID0/proto0/owner1 villager at (10,4,20), HP25/max55.
        // Fixture record: ID+0, proto+4, owner+8, XYZ+12/16/20, HP+24, maxHP+28 (32 bytes).
        // Pointer containers: root+8 world; world/protoRoot count+0 and array+8; context owner+0/protoRoot+16.
        // Synthetic addresses increment by 1 MiB; named variables identify each fake allocation.
        var signatures = Enumerable.Range(1, 5).Select(i => new UnitReadSignature((uint)i, new string('0', 32))).ToArray();
        var layout = new UnitReadLayout(8, 8, 0, 8, 3, 0, 4, 8, [12, 16, 20], 24, 28, 16, 0, 8, 0, signatures);
        ValidateLayout(layout);
        var accepted = new Layout { ContextRva = 16, OwnerOffset = 0, Units = layout };
        nint module = 0x100000, root = 0x200000, world = 0x300000, table = 0x400000,
            context = 0x500000, protoRoot = 0x600000, protoTable = 0x700000, proto = 0x800000,
            text = 0x900000, unit = 0xa00000;
        var memory = new Dictionary<nint, byte[]>
        {
            [module] = new byte[32], [root] = new byte[16], [world] = new byte[16],
            [table] = new byte[16], [context] = new byte[24], [protoRoot] = new byte[16],
            [protoTable] = new byte[8], [proto] = new byte[8], [text] = new byte[64], [unit] = new byte[32],
        };
        void Set(nint address, byte[] bytes)
        {
            var block = memory.First(p => address >= p.Key && address + bytes.Length <= p.Key + p.Value.Length);
            bytes.CopyTo(block.Value, (int)(address - block.Key));
        }
        byte[] Read(nint address, int count) => count == 0 ? [] : memory
            .Where(p => address >= p.Key && address + count <= p.Key + p.Value.Length)
            .Select(p => p.Value.AsSpan((int)(address - p.Key), count).ToArray()).Single();
        Set(module + 8, BitConverter.GetBytes((long)root)); Set(root + 8, BitConverter.GetBytes((long)world));
        Set(world, BitConverter.GetBytes(2)); Set(world + 8, BitConverter.GetBytes((long)table));
        Set(table, BitConverter.GetBytes((long)unit)); Set(module + 16, BitConverter.GetBytes((long)context));
        Set(context, BitConverter.GetBytes(123)); Set(context + 16, BitConverter.GetBytes((long)protoRoot));
        Set(protoRoot, BitConverter.GetBytes(1)); Set(protoRoot + 8, BitConverter.GetBytes((long)protoTable));
        Set(protoTable, BitConverter.GetBytes((long)proto)); Set(proto, BitConverter.GetBytes((long)text));
        Set(text, Encoding.ASCII.GetBytes("VillagerGreek\0"));
        Set(unit + 8, BitConverter.GetBytes(1)); Set(unit + 12, BitConverter.GetBytes(10f));
        Set(unit + 16, BitConverter.GetBytes(4f)); Set(unit + 20, BitConverter.GetBytes(20f));
        Set(unit + 24, BitConverter.GetBytes(25f)); Set(unit + 28, BitConverter.GetBytes(55f));
        var found = Capture(Read, module, accepted);
        if (found.Length != 1 || found[0].Proto != "VillagerGreek" || found[0].Health != 25 || found[0].MaxHealth != 55)
            throw new InvalidOperationException("Live units fixture failed.");
        using var inside = JsonDocument.Parse("{\"proto\":\"villagergreek\",\"player\":1,\"area\":{\"x\":10,\"z\":20,\"radius\":0}}");
        using var outside = JsonDocument.Parse("{\"area\":{\"x\":11,\"z\":20,\"radius\":0}}");
        if (Filter(found, inside.RootElement).Length != 1 || Filter(found, outside.RootElement).Length != 0)
            throw new InvalidOperationException("Live units filter fixture failed.");
        void Refuses(Action action)
        {
            try { action(); }
            catch (InvalidDataException) { return; }
            throw new InvalidOperationException("Live units refusal fixture failed.");
        }
        // -1 count, wrong slot ID, NaN HP, invalid pointer 1 and changed generation ID4 are refusal sentinels.
        Set(world, BitConverter.GetBytes(-1)); Refuses(() => Capture(Read, module, accepted));
        Set(world, BitConverter.GetBytes(2)); Set(unit, BitConverter.GetBytes(1));
        Refuses(() => Capture(Read, module, accepted)); Set(unit, BitConverter.GetBytes(0));
        Set(unit + 24, BitConverter.GetBytes(float.NaN)); Refuses(() => Capture(Read, module, accepted));
        Set(unit + 24, BitConverter.GetBytes(25f));
        Refuses(() => Capture((address, count) => address == table && count == 8 ? BitConverter.GetBytes(1L) : Read(address, count), module, accepted));
        // Same low slot but changed full generation ID must be refused during identity recheck.
        var changed = false;
        Refuses(() => Capture((address, count) =>
        {
            if (address == unit && count == 4)
                return BitConverter.GetBytes(4);
            if (address == unit && count == 32) changed = true;
            return Read(address, count);
        }, module, accepted));
        if (!changed) throw new InvalidOperationException("Live units race fixture not reached.");
        // Heading fixtures: editor default diag(-1,1,-1) reads 180; pi/8 step reads 202.5; scaled block refuses.
        if (Heading(-1, 0, 0, -1) != 180 || Heading(-0.9238795f, -0.3826834f, 0.3826834f, -0.9238795f) != 202.5f
            || Heading(-2, 0, 0, -2) is not null)
            throw new InvalidOperationException("Heading fixture failed.");
        Set(world, BitConverter.GetBytes(0)); Set(world + 8, BitConverter.GetBytes(0L));
        if (Capture(Read, module, accepted).Length != 0) throw new InvalidOperationException("Empty world fixture failed.");
    }
}
