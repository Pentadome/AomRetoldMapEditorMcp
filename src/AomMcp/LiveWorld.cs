using System.Text;
using System.Text.Json;

namespace AomMcp;

/// <summary>Reviewed terrain-type/water/edit-mode/paint-selection/heading fields; see research/LIVE-WORLD.md.</summary>
/// <remarks>Tile records: index tileX*stride+tileZ; 4 bytes (u16 subtype at TileSubtypeOffset, u8 type at TileTypeOffset).
/// Names live in the terrain/type manager at context+TypeManagerOffset. RotationOffsets: m00,m02,m20,m22 of the reviewed unit transform.</remarks>
public sealed record WorldReadLayout(
    int TileCountOffset, int TileArrayOffset, int TileStrideOffset, int TileRecordSize, int TileSubtypeOffset, int TileTypeOffset,
    int TypeManagerOffset, int GroupArrayOffset, int GroupCountOffset, int GroupStride, int GroupSubtypesOffset,
    int GroupSubtypeCountOffset, int GroupNameOffset, int SubtypeStride, int SubtypeNameOffset,
    int WaterGridOffset, int WaterGridCountOffset, int WaterGridDataOffset, int WaterGridStrideOffset, int WaterNone,
    int WaterTypeCountOffset, int WaterTypeArrayOffset, int WaterTypeNameOffset,
    int ForestTypeCountOffset, int ForestTypeArrayOffset, int ForestTypeNameOffset,
    int CliffTypeCountOffset, int CliffTypeArrayOffset, int CliffTypeNameOffset,
    int EditModeOffset, int BrushOffset, int BrushTerrainTypeOffset, int BrushTerrainSubtypeOffset, int BrushWaterOffset,
    int ForestSelectionOffset, int CliffSelectionOffset, int[] RotationOffsets,
    int LightingArrayOffset, int LightingCountOffset, int LightingDataOffset, int LightingNameOffset, UnitReadSignature[] Signatures);

/// <summary>Reviewed live player fields (world player array); resources are obfuscated and intentionally absent.</summary>
public sealed record PlayerReadLayout(
    int ArrayOffset, int CountOffset, int NameOffset, int OverrideNameOffset, int[] OverrideFlagOffsets, int TeamOffset,
    int CivArrayOffset, int CivCountOffset, int AgeOffset, int DiplomacyOffset, int DiplomacyCount,
    uint CivNameTableRva, int CivNameTableCount, int CivNameStride, UnitReadSignature[] Signatures);

/// <summary>Passive live reads of terrain textures, water, edit mode, paint selections and players.</summary>
internal static class LiveWorld
{
    // Host read bounds, not engine capacities. AMD64 pointer/int widths are ABI.
    const int MaxOffset = 65536, MaxGroups = 64, MaxSubtypes = 4096, MaxNamed = 1024, MaxNameChars = 256,
        MinSignatureBytes = 16, MaxSignatureBytes = 512, MaxSignatures = 16, MaxPlayers = 17, MaxPoints = 64, MaxAreaTiles = 1 << 20,
        PageSize = 4096, PointerSize = 8, IntSize = 4;

    static void CheckSignatures(UnitReadSignature[] signatures)
    {
        if (signatures is null || signatures.Length is < 1 or > MaxSignatures
            || signatures.Any(s => s.Rva == 0 || Convert.FromHexString(s.Hex).Length is < MinSignatureBytes or > MaxSignatureBytes))
            throw new InvalidDataException("Live world/player signatures missing or unbounded.");
    }

    internal static void ValidateLayout(WorldReadLayout l)
    {
        CheckSignatures(l.Signatures);
        int[] offsets = [l.TileCountOffset, l.TileArrayOffset, l.TileStrideOffset, l.TileSubtypeOffset, l.TileTypeOffset,
            l.TypeManagerOffset, l.GroupArrayOffset, l.GroupCountOffset, l.GroupStride, l.GroupSubtypesOffset, l.GroupSubtypeCountOffset,
            l.GroupNameOffset, l.SubtypeStride, l.SubtypeNameOffset, l.WaterGridOffset, l.WaterGridCountOffset, l.WaterGridDataOffset,
            l.WaterGridStrideOffset, l.WaterTypeCountOffset, l.WaterTypeArrayOffset, l.WaterTypeNameOffset, l.ForestTypeCountOffset,
            l.ForestTypeArrayOffset, l.ForestTypeNameOffset, l.CliffTypeCountOffset, l.CliffTypeArrayOffset, l.CliffTypeNameOffset,
            l.EditModeOffset, l.BrushOffset, l.BrushTerrainTypeOffset, l.BrushTerrainSubtypeOffset, l.BrushWaterOffset,
            l.ForestSelectionOffset, l.CliffSelectionOffset, l.LightingArrayOffset, l.LightingCountOffset, l.LightingDataOffset,
            l.LightingNameOffset, .. l.RotationOffsets ?? []];
        if (offsets.Any(o => o is < 0 or > MaxOffset) || l.RotationOffsets is not { Length: 4 } || l.TileRecordSize is < 3 or > 16
            || l.TileSubtypeOffset + 2 > l.TileRecordSize || l.TileTypeOffset >= l.TileRecordSize || l.GroupStride < 1 || l.SubtypeStride < 1
            || l.WaterNone is < 0 or > 255)
            throw new InvalidDataException("Invalid live world layout.");
    }

    internal static void ValidateLayout(PlayerReadLayout l)
    {
        CheckSignatures(l.Signatures);
        int[] offsets = [l.ArrayOffset, l.CountOffset, l.NameOffset, l.OverrideNameOffset, l.TeamOffset, l.CivArrayOffset,
            l.CivCountOffset, l.AgeOffset, l.DiplomacyOffset, l.CivNameStride, .. l.OverrideFlagOffsets ?? []];
        if (offsets.Any(o => o is < 0 or > MaxOffset) || l.OverrideFlagOffsets is not { Length: 3 } || l.DiplomacyCount is < 1 or > 64
            || l.CivNameTableRva == 0 || l.CivNameTableCount is < 0 or > 256 || l.CivNameStride < PointerSize)
            throw new InvalidDataException("Invalid live player layout.");
    }

    static WorldReadLayout World(Game game)
    {
        var l = game.Layout.World ?? throw new InvalidDataException("World read layout unavailable for this build; passive review required, offsets never guessed.");
        ValidateLayout(l);
        LiveUnits.ValidateRuntime(game, l.Signatures);
        return l;
    }

    static int Int(Game game, nint a) => BitConverter.ToInt32(game.Read(a, IntSize));

    static nint Ptr(Game game, nint a)
    {
        var value = game.Pointer(a);
        // AMD64 user-mode canonical range sanity (same policy as LiveUnits).
        if (value != 0 && ((long)value < 0x10000 || (long)value > 0x7fffffffffff))
            throw new InvalidDataException("Invalid live world pointer.");
        return value;
    }

    /// <summary>Reads a NUL-terminated UTF-16 string without crossing page boundaries per read.</summary>
    internal static string WString(Game game, nint address)
    {
        if (address == 0) return "";
        var chars = new StringBuilder();
        var at = address;
        while (chars.Length < MaxNameChars)
        {
            var length = Math.Min(64, PageSize - (int)((long)at & (PageSize - 1)));
            var chunk = game.Read(at, length & ~1);
            for (var i = 0; i + 1 < chunk.Length; i += 2)
            {
                var c = (char)BitConverter.ToUInt16(chunk, i);
                if (c == '\0') return chars.ToString();
                if (char.IsControl(c)) throw new InvalidDataException("Invalid live name encoding.");
                chars.Append(c);
            }
            at += chunk.Length;
        }
        throw new InvalidDataException("Live name exceeds host bound.");
    }

    static string Ascii(Game game, nint address)
    {
        if (address == 0) return "";
        var length = Math.Min(64, PageSize - (int)((long)address & (PageSize - 1)));
        var bytes = game.Read(address, length);
        var end = Array.IndexOf(bytes, (byte)0);
        if (end < 0 || bytes.Take(end).Any(b => b is < 32 or > 126)) throw new InvalidDataException("Invalid live civ name.");
        return Encoding.ASCII.GetString(bytes, 0, end);
    }

    internal sealed record Catalogs(string[] Groups, string[][] Subtypes, string[] Water, string[] Forest, string[] Cliff);

    static (uint Pid, nint Manager, Catalogs Value)? _cache; // Type catalogs are loaded once per game data load; keyed by process, manager pointer and counts.

    internal static Catalogs Names(Game game)
    {
        var l = World(game);
        var context = Ptr(game, game.Base + checked((int)game.Layout.ContextRva));
        var tm = context == 0 ? 0 : Ptr(game, context + l.TypeManagerOffset);
        if (tm == 0) throw new InvalidDataException("Live terrain type manager unavailable.");
        int Count(int offset, int max)
        {
            var n = Int(game, tm + offset);
            if (n < 0 || n > max) throw new InvalidDataException("Live type count outside host bound.");
            return n;
        }
        var groupCount = Count(l.GroupCountOffset, MaxGroups);
        int waterCount = Count(l.WaterTypeCountOffset, MaxNamed), forestCount = Count(l.ForestTypeCountOffset, MaxNamed),
            cliffCount = Count(l.CliffTypeCountOffset, MaxNamed);
        if (_cache is { } c && c.Pid == game.Pid && c.Manager == tm && c.Value.Groups.Length == groupCount && c.Value.Water.Length == waterCount
            && c.Value.Forest.Length == forestCount && c.Value.Cliff.Length == cliffCount)
            return c.Value;
        var groups = Ptr(game, tm + l.GroupArrayOffset);
        var groupNames = new string[groupCount];
        var subtypes = new string[groupCount][];
        for (var g = 0; g < groupCount; g++)
        {
            var group = groups + g * l.GroupStride;
            groupNames[g] = WString(game, Ptr(game, group + l.GroupNameOffset));
            var n = Int(game, group + l.GroupSubtypeCountOffset);
            if (n < 0 || n > MaxSubtypes) throw new InvalidDataException("Live subtype count outside host bound.");
            var array = Ptr(game, group + l.GroupSubtypesOffset);
            subtypes[g] = Enumerable.Range(0, n).Select(s => WString(game, Ptr(game, array + s * l.SubtypeStride + l.SubtypeNameOffset))).ToArray();
        }
        string[] Named(int count, int arrayOffset, int nameOffset)
        {
            var array = Ptr(game, tm + arrayOffset);
            return Enumerable.Range(0, count).Select(i =>
            {
                var o = Ptr(game, array + i * PointerSize);
                return o == 0 ? "" : WString(game, Ptr(game, o + nameOffset));
            }).ToArray();
        }
        var value = new Catalogs(groupNames, subtypes, Named(waterCount, l.WaterTypeArrayOffset, l.WaterTypeNameOffset),
            Named(forestCount, l.ForestTypeArrayOffset, l.ForestTypeNameOffset), Named(cliffCount, l.CliffTypeArrayOffset, l.CliffTypeNameOffset));
        _cache = (game.Pid, tm, value);
        return value;
    }

    /// <summary>uiApplyLightingSet index → lightset name: [editor global + LightingArrayOffset] list (handler 0x8dee30).</summary>
    internal static string[] LightingSets(Game game)
    {
        var l = World(game);
        var root = Ptr(game, game.Base + checked((int)game.Layout.EditorGlobalRva));
        var list = root == 0 ? 0 : Ptr(game, root + l.LightingArrayOffset);
        if (list == 0) throw new InvalidDataException("Live lighting-set list unavailable.");
        var count = Int(game, list + l.LightingCountOffset);
        if (count is < 0 or > 4096) throw new InvalidDataException("Live lighting-set count outside host bound.");
        var data = Ptr(game, list + l.LightingDataOffset);
        return Enumerable.Range(0, count).Select(i =>
        {
            var o = Ptr(game, data + i * PointerSize);
            return o == 0 ? "" : WString(game, Ptr(game, o + l.LightingNameOffset));
        }).ToArray();
    }

    // ---------------- edit mode ----------------

    /// <summary>Live editMode name → value map recorded by research/LIVE-WORLD.md diff experiments.</summary>
    internal static readonly (string Name, int Value)[] Modes =
    [
        ("None", 0), ("Paint", 1), ("PaintLand", 2), ("paintmix", 3), ("paintforest", 4), ("terrainDetails", 5), ("elevation", 8),
        ("elevationsample", 9), ("roughen", 10), ("deleteunits", 11), ("smooth", 12), ("paintWater", 13), ("PaintCliff", 14),
        ("copy", 15), ("editGrass", 17), ("convertunits", 18), ("recalcvariation", 19), ("TerrainPaste", 22), ("editWater", 24),
        ("UnitPaste", 25), ("PlaceUnit", 26), ("PlaceUnitSelect", 27), ("moveunit", 34), ("PlaceWall", 36), ("Triggers", 39),
        ("TrigGroups", 40), ("CameraTracks", 42),
    ];

    internal static string? ModeName(int value) => Modes.Where(m => m.Value == value).Select(m => m.Name).FirstOrDefault();

    internal static int EditMode(Game game)
    {
        var l = World(game);
        return Int(game, game.Editor() + l.EditModeOffset);
    }

    internal sealed record Selections(int TextureType, int TextureSubtype, string? Texture, int Water, string? WaterName,
        int Forest, string? ForestName, int Cliff, string? CliffName);

    internal static Selections ReadSelections(Game game)
    {
        var l = World(game);
        var names = Names(game);
        var editor = game.Editor();
        var brush = Ptr(game, editor + l.BrushOffset);
        if (brush == 0) throw new InvalidDataException("Live editor brush unavailable.");
        int type = Int(game, brush + l.BrushTerrainTypeOffset), subtype = Int(game, brush + l.BrushTerrainSubtypeOffset),
            water = Int(game, brush + l.BrushWaterOffset), forest = Int(game, editor + l.ForestSelectionOffset),
            cliff = Int(game, editor + l.CliffSelectionOffset);
        static string? At(string[] a, int i) => i >= 0 && i < a.Length ? a[i] : null;
        return new(type, subtype, type >= 0 && type < names.Subtypes.Length ? At(names.Subtypes[type], subtype) : null,
            water, At(names.Water, water), forest, At(names.Forest, forest), cliff, At(names.Cliff, cliff));
    }

    // ---------------- terrain tiles ----------------

    internal sealed record Tile(int Type, int Subtype, int Water);

    /// <summary>Row-cached tile reader for one validated view (terrain pointer from map layout).</summary>
    internal sealed class Tiles
    {
        readonly Game _game; readonly WorldReadLayout _l; readonly int _tx, _tz, _stride, _waterStride;
        readonly nint _array, _water; readonly Dictionary<int, (byte[] Tiles, byte[] Water)> _rows = new();
        internal Catalogs Names { get; }

        internal Tiles(Game game, EditorView.ViewState view)
        {
            _game = game; _l = World(game); Names = LiveWorld.Names(game);
            _tx = view.Tiles[0]; _tz = view.Tiles[1];
            var terrain = view.Terrain;
            var count = Int(game, terrain + _l.TileCountOffset);
            _stride = Int(game, terrain + _l.TileStrideOffset);
            _array = Ptr(game, terrain + _l.TileArrayOffset);
            var grid = terrain + _l.WaterGridOffset;
            var waterCount = Int(game, grid + _l.WaterGridCountOffset);
            _waterStride = Int(game, grid + _l.WaterGridStrideOffset);
            _water = Ptr(game, grid + _l.WaterGridDataOffset);
            if (_stride < _tz || count < (long)_tx * _stride || _array == 0 || count > MaxAreaTiles * 4
                || _waterStride < _tz || waterCount < (long)_tx * _waterStride || _water == 0)
                throw new InvalidDataException("Live terrain tile/water grid bounds mismatch.");
        }

        internal int TilesX => _tx;
        internal int TilesZ => _tz;

        internal Tile At(int x, int z)
        {
            if (x < 0 || z < 0 || x >= _tx || z >= _tz) throw new ArgumentException("Tile outside map.");
            if (!_rows.TryGetValue(x, out var row))
            {
                row = (_game.Read(_array + checked(x * _stride * _l.TileRecordSize), _tz * _l.TileRecordSize),
                    _game.Read(_water + checked(x * _waterStride), _tz));
                _rows[x] = row;
            }
            var o = z * _l.TileRecordSize;
            return new(row.Tiles[o + _l.TileTypeOffset], BitConverter.ToUInt16(row.Tiles, o + _l.TileSubtypeOffset), row.Water[z]);
        }

        internal string? TextureName(Tile t) => t.Type < Names.Subtypes.Length && t.Subtype < Names.Subtypes[t.Type].Length ? Names.Subtypes[t.Type][t.Subtype] : null;
        internal string? GroupName(Tile t) => t.Type < Names.Groups.Length ? Names.Groups[t.Type] : null;
        internal string? WaterName(Tile t) => t.Water == _l.WaterNone ? null : t.Water < Names.Water.Length ? Names.Water[t.Water] : $"#{t.Water}";

        /// <summary>Coarse passability class from terrain group plus water grid; not pathing/obstruction proof.</summary>
        internal string Passability(Tile t) => t.Water != _l.WaterNone ? "water"
            : GroupName(t) switch
            {
                "PassableLand" or "Shoreline" => "land",
                "NonPassableLand" => "impassable",
                "Water" or "Underwater" => "waterTexture",
                "Ice" => "ice",
                var g => g ?? "unknown",
            };
    }

    /// <summary>editor_terrain_info: per-point texture/water/height/passability and area histograms.</summary>
    public static object TerrainInfo(Game game, JsonElement args)
    {
        var view = EditorView.ReadView(game);
        var tiles = new Tiles(game, view);
        var inverse = view.InverseScale;
        (int X, int Z) TileOf(double x, double z) =>
            (Math.Clamp((int)(x * inverse), 0, tiles.TilesX - 1), Math.Clamp((int)(z * inverse), 0, tiles.TilesZ - 1)); // Native getter truncates.
        object Describe(double x, double z)
        {
            if (!view.InsideMap(x, z)) throw new ArgumentException($"Point ({x},{z}) outside map 0..{view.WorldWidth} x 0..{view.WorldDepth}.");
            var (tx, tz) = TileOf(x, z);
            var t = tiles.At(tx, tz);
            return new
            {
                x, z, tileX = tx, tileZ = tz, height = view.Height(x, z).Height,
                texture = tiles.TextureName(t), group = tiles.GroupName(t), type = t.Type, subtype = t.Subtype,
                water = tiles.WaterName(t), passability = tiles.Passability(t),
            };
        }
        object[]? points = null;
        if (args.TryGetProperty("points", out var ps))
        {
            if (ps.GetArrayLength() is < 1 or > MaxPoints) throw new ArgumentException("points: 1..64 [x,z] pairs.");
            points = ps.EnumerateArray().Select(p => Describe(p[0].GetDouble(), p[1].GetDouble())).ToArray();
        }
        object? area = null;
        if (args.TryGetProperty("minX", out _))
        {
            double minX = args.GetProperty("minX").GetDouble(), minZ = args.GetProperty("minZ").GetDouble(),
                maxX = args.GetProperty("maxX").GetDouble(), maxZ = args.GetProperty("maxZ").GetDouble();
            if (minX >= maxX || minZ >= maxZ) throw new ArgumentException("Area requires minX<maxX and minZ<maxZ.");
            var (x0, z0) = TileOf(Math.Max(0, minX), Math.Max(0, minZ));
            var (x1, z1) = TileOf(Math.Min(view.WorldWidth, maxX) - 1e-4, Math.Min(view.WorldDepth, maxZ) - 1e-4);
            var textures = new Dictionary<string, int>(); var water = new Dictionary<string, int>(); var pass = new Dictionary<string, int>();
            var total = 0;
            for (var x = x0; x <= x1; x++)
                for (var z = z0; z <= z1; z++)
                {
                    var t = tiles.At(x, z); total++;
                    var name = tiles.TextureName(t) ?? $"#{t.Type}/{t.Subtype}";
                    textures[name] = textures.GetValueOrDefault(name) + 1;
                    if (tiles.WaterName(t) is { } w) water[w] = water.GetValueOrDefault(w) + 1;
                    var p = tiles.Passability(t); pass[p] = pass.GetValueOrDefault(p) + 1;
                }
            object? grid = null;
            if (args.TryGetProperty("includeGrid", out var ig) && ig.GetBoolean())
            {
                // Host output bound: at most 48x48 cells; each cell samples the tile at its lower-left corner.
                var step = Math.Max(1, (int)Math.Ceiling(Math.Max(x1 - x0 + 1, z1 - z0 + 1) / 48.0));
                var legend = new List<string>(); var index = new Dictionary<string, int>();
                var rows = new List<int[]>();
                for (var z = z0; z <= z1; z += step)
                {
                    var row = new List<int>();
                    for (var x = x0; x <= x1; x += step)
                    {
                        var t = tiles.At(x, z);
                        var key = (tiles.TextureName(t) ?? "?") + (tiles.WaterName(t) is { } w ? " +water:" + w : "");
                        if (!index.TryGetValue(key, out var k)) { k = legend.Count; legend.Add(key); index[key] = k; }
                        row.Add(k);
                    }
                    rows.Add(row.ToArray());
                }
                grid = new { tileStep = step, worldStep = step * view.Scale, rowsAlong = "z (first row = minZ)", columnsAlong = "x (first column = minX)", legend, rows };
            }
            area = new
            {
                tiles = new { minX = x0, minZ = z0, maxX = x1, maxZ = z1, count = total },
                textures = textures.OrderByDescending(p => p.Value).Select(p => new { name = p.Key, tiles = p.Value }).ToArray(),
                water = water.OrderByDescending(p => p.Value).Select(p => new { name = p.Key, tiles = p.Value }).ToArray(),
                passability = pass,
                grid,
            };
        }
        if (points is null && area is null) throw new ArgumentException("Provide points and/or minX/minZ/maxX/maxZ.");
        view.Verify();
        return new
        {
            pid = game.Pid, buildHash = game.Layout.ExeSha256, tileWorldSize = view.Scale, points, area,
            limitation = "Passive reads of live tile texture records, per-tile water grid and quantized node heights (research/LIVE-WORLD.md). passability is a coarse class from the terrain group plus water grid, not pathing/obstruction or unit-footprint proof; units, cliffs objects and forests are not included. Atomic frame not claimed.",
        };
    }

    // ---------------- players ----------------

    public static object Players(Game game, JsonElement args)
    {
        var l = game.Layout.Players ?? throw new InvalidDataException("Player read layout unavailable for this build; passive review required.");
        ValidateLayout(l);
        LiveUnits.ValidateRuntime(game, l.Signatures);
        var context = Ptr(game, game.Base + checked((int)game.Layout.ContextRva));
        var mapWorld = game.Layout.Map?.WorldOffset ?? throw new InvalidDataException("Map layout unavailable (world pointer).");
        var world = context == 0 ? 0 : Ptr(game, context + mapWorld);
        if (world == 0) throw new InvalidDataException("Live world unavailable.");
        var count = Int(game, world + l.CountOffset);
        if (count is < 0 or > MaxPlayers) throw new InvalidDataException("Live player count outside host bound.");
        var array = Ptr(game, world + l.ArrayOffset);
        var only = args.TryGetProperty("player", out var pp) ? (int?)pp.GetInt32() : null;
        string Civ(int civ)
        {
            if (civ < 0 || civ >= l.CivNameTableCount) return $"#{civ}";
            return Ascii(game, Ptr(game, game.Base + checked((int)l.CivNameTableRva + civ * l.CivNameStride)));
        }
        var players = new List<object>();
        for (var i = 0; i < count; i++)
        {
            if (only is { } o && o != i) continue;
            var p = Ptr(game, array + i * PointerSize);
            if (p == 0) continue;
            // Display-name rule from 0x1cd5890: override used only when flag/kind conditions hold.
            var baseName = WString(game, Ptr(game, p + l.NameOffset));
            var overridePtr = Ptr(game, p + l.OverrideNameOffset);
            var overrideName = WString(game, overridePtr);
            int flag = Int(game, p + l.OverrideFlagOffsets[0]), kind = Int(game, p + l.OverrideFlagOffsets[1]), enabled = Int(game, p + l.OverrideFlagOffsets[2]);
            var useOverride = flag > 0 && overrideName.Length > 0 && kind > 0
                && (kind == 2 ? Int(game, overridePtr - 0x18) > 0 : enabled > 0); // -0x18: native string header length field (code at 0x1cd58ce).
            var civCount = Int(game, p + l.CivCountOffset);
            int? civ = civCount > 0 ? Int(game, Ptr(game, p + l.CivArrayOffset)) : null;
            var diplomacy = Enumerable.Range(0, Math.Min(count, l.DiplomacyCount)).Select(t => Int(game, p + l.DiplomacyOffset + t * IntSize)).ToArray();
            players.Add(new
            {
                player = i, name = useOverride ? overrideName : baseName, team = Int(game, p + l.TeamOffset),
                civId = civ, civ = civ is { } c ? Civ(c) : null, age = Int(game, p + l.AgeOffset),
                diplomacy = diplomacy.Select((s, t) => new { target = t, stance = s, name = s switch { 1 => "ally", 2 => "enemy", 3 => "neutral", 0 => t == i ? "self" : "unset", _ => "unknown" } }).ToArray(),
            });
        }
        return new
        {
            pid = game.Pid, buildHash = game.Layout.ExeSha256, playerCount = count, players,
            limitation = "Live passive read of the editor's player objects (name, team, civ/major god, age index 0=Archaic, diplomacy 1 ally/2 enemy/3 neutral). Resources and minor gods are not read (resources are obfuscated in memory and not decoded); use editor_players on a checkpoint for them. Not saved-scenario proof.",
        };
    }

    // ---------------- self-test ----------------

    internal static void SelfTest()
    {
        if (ModeName(26) != "PlaceUnit" || ModeName(0) != "None" || Modes.Select(m => m.Value).Distinct().Count() != Modes.Length)
            throw new InvalidOperationException("Edit mode table fixture failed.");
        var sig = new UnitReadSignature(1, new string('0', 32));
        var bad = new WorldReadLayout(0, 0, 0, 4, 0, 2, 0, 0, 0, 0x50, 0, 8, 0x10, 0xf8, 0x58, 0, 0, 0x10, 0x2c, 255, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, [0, 8, 0x20], 0xf0, 0, 0x10, 0x108, [sig]);
        try { ValidateLayout(bad); throw new InvalidOperationException("World layout refusal fixture failed."); }
        catch (InvalidDataException) { }
        ValidateLayout(bad with { RotationOffsets = [0x50, 0x58, 0x70, 0x78] });
    }
}
