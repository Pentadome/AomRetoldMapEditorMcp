using System.Numerics;
using System.Text.Json;
using System.Xml.Linq;

namespace AomMcp;

/// <summary>World editing helpers: live terrain/player/edit-mode reads, world-space painting/elevation, unit transforms,
/// catalogs and map-layout helpers (overview, camera frame, resource balance, mirroring, clusters).</summary>
internal sealed partial class Server
{
    static readonly HashSet<string> WorldNames = new(StringComparer.Ordinal)
    {
        "editor_terrain_info", "editor_live_players", "editor_edit_mode", "editor_paint_world", "editor_elevation",
        "editor_transform_unit", "editor_terrain_catalog", "editor_camera_frame", "editor_overview",
        "editor_resource_balance", "editor_mirror_units", "editor_scatter",
    };
    // Host vocabularies (tool API), mapped to native editMode names below.
    static readonly string[] PaintKinds = ["texture", "mix", "water", "forest", "cliff"];
    static readonly string[] ElevationOperations = ["set", "flatten", "smooth"];
    static readonly string[] TerrainCatalogKinds = ["textures", "water", "forest", "cliff", "mixes", "lighting", "civs", "editModes"];
    static readonly string[] MirrorModes = ["point", "flipX", "flipZ", "swapXZ"];
    static readonly string[] ScatterShapes = ["disc", "ring", "line"];
    static readonly string[] WorldAreaRequired = ["minX", "minZ", "maxX", "maxZ"];
    static readonly string[] TransformRequired = ["unitId", "proto", "player"];
    static readonly string[] PaintRequired = ["kind", "type", "points", "confirmDestructive"];
    static readonly string[] ElevationRequired = ["operation", "minX", "minZ", "maxX", "maxZ", "confirmDestructive"];
    static readonly string[] MirrorRequired = ["sourcePlayer", "targetPlayer", "mode"];
    static readonly string[] ScatterRequired = ["proto", "count", "x", "z", "radius"];
    static readonly string[] CenterRequired = ["player", "x", "z"];
    // Rotation step of uiRotateSelection: ±pi/8 by sign of amount only (0x5375c0, research/LIVE-WORLD.md).
    const double RotationStepDegrees = 22.5;

    static string PaintMode(string kind) => kind switch
    {
        "texture" => "Paint", "mix" => "paintmix", "water" => "paintWater", "forest" => "paintforest", "cliff" => "PaintCliff",
        _ => throw new ArgumentException("kind: texture/mix/water/forest/cliff"),
    };

    void PreflightWorld(string name, JsonElement args)
    {
        switch (name)
        {
            case "editor_paint_world":
                EditorFiles.Confirm(args, "confirmDestructive");
                if (args.GetProperty("points").GetArrayLength() is < 1 or > 64) throw new ArgumentException("points: 1..64 [x,z] pairs.");
                foreach (var p in args.GetProperty("points").EnumerateArray())
                    if (p.GetArrayLength() != 2) throw new ArgumentException("Each point must be [x,z].");
                break;
            case "editor_elevation":
                EditorFiles.Confirm(args, "confirmDestructive");
                if (args.GetProperty("minX").GetDouble() >= args.GetProperty("maxX").GetDouble()
                    || args.GetProperty("minZ").GetDouble() >= args.GetProperty("maxZ").GetDouble())
                    throw new ArgumentException("Area requires minX<maxX and minZ<maxZ.");
                if (String(args, "operation") == "set" && !args.TryGetProperty("height", out _))
                    throw new ArgumentException("operation=set requires height.");
                break;
            case "editor_terrain_info":
                if (args.TryGetProperty("minX", out _) && !WorldAreaRequired.All(k => args.TryGetProperty(k, out _)))
                    throw new ArgumentException("Area needs minX, minZ, maxX and maxZ together.");
                break;
            case "editor_transform_unit":
                if (!args.TryGetProperty("x", out _) && !args.TryGetProperty("heading", out _))
                    throw new ArgumentException("Provide x+z (move) and/or heading (rotate).");
                if (args.TryGetProperty("x", out _) != args.TryGetProperty("z", out _)) throw new ArgumentException("x and z go together.");
                break;
            case "editor_mirror_units" or "editor_scatter":
                if (!LayoutPreview(args)) EditorFiles.Confirm(args, "confirmPlacement");
                if (name == "editor_scatter") RequireKnownProto(String(args, "proto"));
                break;
            case "editor_edit_mode":
                if (args.TryGetProperty("mode", out _) && args.TryGetProperty("exit", out var exit) && exit.GetBoolean())
                    throw new ArgumentException("Use either mode or exit, not both.");
                break;
        }
    }

    object InvokeWorld(string name, JsonElement args, Game? batchGame)
    {
        if (name == "editor_terrain_catalog" && String(args, "kind") is "mixes" or "editModes")
            return TerrainCatalog(null, args);
        if (_layout.Map is null || (_layout.World is null && name is not "editor_live_players")
            || (_layout.Players is null && name == "editor_live_players"))
            throw new InvalidDataException("Live world/player read layout unavailable for this build; helpers refuse. Offsets are never guessed.");
        using var owned = batchGame is null ? new Game(exe, _layout, pid) : null;
        var game = batchGame ?? owned!;
        return name switch
        {
            "editor_terrain_info" => LiveWorld.TerrainInfo(game, args),
            "editor_live_players" => LiveWorld.Players(game, args),
            "editor_edit_mode" => EditModeTool(game, args),
            "editor_paint_world" => PaintWorld(game, args),
            "editor_elevation" => Elevation(game, args),
            "editor_transform_unit" => TransformUnit(game, args),
            "editor_terrain_catalog" => TerrainCatalog(game, args),
            "editor_camera_frame" => CameraFrame(game, args),
            "editor_overview" => Overview(game, args),
            "editor_resource_balance" => ResourceBalance(game, args),
            "editor_mirror_units" => MirrorUnits(game, args),
            "editor_scatter" => Scatter(game, args),
            _ => throw new ArgumentException("Unknown world tool."),
        };
    }

    // ---------------- edit mode ----------------

    static int WaitMode(Game game, Func<int, bool> done, int milliseconds)
    {
        // Host poll: editMode changes apply on a following editor frame (observed); 40 ms polling.
        var deadline = Win.GetTickCount64() + (ulong)milliseconds;
        int mode;
        do
        {
            mode = LiveWorld.EditMode(game);
            if (done(mode)) return mode;
            Thread.Sleep(40);
        } while (Win.GetTickCount64() < deadline);
        return mode;
    }

    /// <summary>Leaves any edit tool: PlaceUnit needs two editMode("None") calls (26→27→0), so loop until readback 0.</summary>
    (int Before, int After, int Calls) ExitEditModes(Game game)
    {
        var before = LiveWorld.EditMode(game);
        var mode = before;
        var calls = 0;
        for (var i = 0; i < 4 && mode != 0; i++) // Host bound; observed worst case two calls.
        {
            if (unchecked((int)game.UInt(game.Editor() + (int)_layout.ProtoOffset)) != -1) _bridge.Execute(game, "uiClearCursor()");
            _bridge.Execute(game, "editMode(\"None\")");
            calls++;
            mode = WaitMode(game, m => m == 0, 600);
        }
        if (mode != 0)
            throw new WorkflowFailure("EDIT_MODE_NOT_EXITED", "exit-mode", $"Edit mode still {LiveWorld.ModeName(mode) ?? mode.ToString(System.Globalization.CultureInfo.InvariantCulture)} after {calls} editMode(\"None\") calls.",
                true, false, "Inspect editor_ui_state/screenshot (dialog open?). Press ESC manually if a modal tool is active.");
        return (before, mode, calls);
    }

    void EnterMode(Game game, string mode)
    {
        var value = LiveWorld.Modes.First(m => m.Name == mode).Value;
        _bridge.Execute(game, $"editMode({Catalog.Quote(mode)})");
        var now = WaitMode(game, m => m == value, 1500);
        if (now != value)
            throw new WorkflowFailure("EDIT_MODE_NOT_ENTERED", "enter-mode", $"editMode(\"{mode}\") did not take effect (mode {now}).", true, false,
                "Inspect editor_edit_mode; close dialogs and retry once.");
        Thread.Sleep(250); // Host settle: tool panel opens on following frames before pixel gates are sampled.
    }

    object EditModeTool(Game game, JsonElement args)
    {
        object? action = null;
        if (args.TryGetProperty("exit", out var exit) && exit.GetBoolean())
        {
            var (before, after, calls) = ExitEditModes(game);
            action = new { exit = true, before = LiveWorld.ModeName(before) ?? before.ToString(System.Globalization.CultureInfo.InvariantCulture), after = LiveWorld.ModeName(after), editModeCalls = calls };
        }
        else if (args.TryGetProperty("mode", out var m))
        {
            RequireNoCursor(game);
            var before = LiveWorld.EditMode(game);
            EnterMode(game, m.GetString()!);
            action = new { mode = m.GetString(), before = LiveWorld.ModeName(before) ?? before.ToString(System.Globalization.CultureInfo.InvariantCulture), after = LiveWorld.ModeName(LiveWorld.EditMode(game)) };
        }
        var mode = LiveWorld.EditMode(game);
        var selections = LiveWorld.ReadSelections(game);
        var frame = SceneUi.Capture(game);
        var ui = SceneUi.TryLoad(game, frame, out var reason);
        return new
        {
            action,
            editMode = LiveWorld.ModeName(mode) ?? $"unknown({mode})", editModeValue = mode,
            placementCursorActive = unchecked((int)game.UInt(game.Editor() + (int)_layout.ProtoOffset)) != -1,
            uiKind = ui?.Kind ?? "unknown", uiDetection = ui is null ? reason : $"pixel gates matched {ui.File}",
            profileAlternativeUiHint = SceneUi.ProfileAlternativeHint(),
            panels = ui?.Occluders(frame).Where(o => o.Conditional).ToDictionary(o => o.Name, o => o.Active),
            paintSelections = new
            {
                texture = selections.Texture, textureType = selections.TextureType, textureSubtype = selections.TextureSubtype,
                water = selections.WaterName, forest = selections.ForestName, cliff = selections.CliffName,
            },
            modes = LiveWorld.Modes.Select(x => x.Name).ToArray(),
            limitation = "Edit mode and paint selections are passive memory reads (research/LIVE-WORLD.md); UI kind/panels from pixel gates on one screenshot (focuses game). exit loops editMode(\"None\") until mode 0 (PlaceUnit needs two calls). Mix selection is not readable from memory.",
        };
    }

    // ---------------- world→pixel ----------------

    /// <summary>Projects a world X/Z to a clickable client pixel (visible, clear of UI, ray back-check), else null.</summary>
    static (int X, int Y)? PixelFor(EditorView.ViewState v, SceneUi? ui, SceneUi.Area[]? occ, int width, int height,
        double x, double z, double tolerance, float? atHeight = null)
    {
        if (!v.InsideMap(x, z)) return null;
        var h = atHeight ?? v.Height(x, z).Height;
        var p = v.Project(new Vector3((float)x, h, (float)z));
        if (!p.VisibleInViewport) return null;
        if (!(ui is null ? SceneUi.CentralRegion(p.X, p.Y, width, height) : ui.Clear(occ!, p.X, p.Y, width, height))) return null;
        if (atHeight is null)
        {
            var hit = v.GroundHit(v.RayAt(p.X, p.Y));
            if (hit is not { } g || Math.Sqrt(Math.Pow(g.X - x, 2) + Math.Pow(g.Z - z, 2)) > Math.Max(1, tolerance)) return null;
        }
        return ((int)Math.Round(p.X), (int)Math.Round(p.Y));
    }

    sealed record ScreenPath((int X, int Y)[] Pixels, EditorView.ViewState View, SceneUi? Ui, SceneUi.Area[]? Occluders, object? Camera);

    /// <summary>Maps world points to clickable pixels in one view, moving the camera (minimap) to the centroid once if needed.</summary>
    ScreenPath Screen(Game game, (double X, double Z)[] points, bool moveCamera, double tolerance, float? atHeight = null)
    {
        var (width, height) = Ui.ClientSize(game);
        var view = EditorView.ReadView(game);
        foreach (var (x, z) in points) if (!view.InsideMap(x, z)) throw OutsideMap(view);
        var frame = SceneUi.Capture(game);
        var ui = SceneUi.TryLoad(game, frame, out _);
        var occ = ui?.Occluders(frame);
        (int X, int Y)?[] Map(EditorView.ViewState v, SceneUi.Area[]? o) => points.Select(p => PixelFor(v, ui, o, width, height, p.X, p.Z, tolerance, atHeight)).ToArray();
        var pixels = Map(view, occ);
        object? camera = null;
        if (pixels.Any(p => p is null))
        {
            if (!moveCamera || ui is null)
                throw new WorkflowFailure("TARGET_NOT_CLICKABLE", "preflight", ui is null ? "Points not in trusted central region and UI not detected (no minimap camera)."
                    : "Some points not visible/clear of UI panels and moveCamera=false.", false, false, "Move camera (editor_camera_frame) then retry. No input sent.");
            double cx = (points.Min(p => p.X) + points.Max(p => p.X)) / 2, cz = (points.Min(p => p.Z) + points.Max(p => p.Z)) / 2;
            camera = CameraLookAt(game, cx, cz, 2, 4);
            view = EditorView.ReadView(game);
            frame = SceneUi.Capture(game);
            occ = ui.Occluders(frame);
            pixels = Map(view, occ);
            var missing = pixels.Select((p, i) => (p, i)).Where(t => t.p is null).Select(t => t.i).ToArray();
            if (missing.Length > 0)
                throw new WorkflowFailure("TARGET_NOT_CLICKABLE", "camera", $"Points {string.Join(",", missing.Take(10))} not clickable after centering camera (too spread for one view, hidden behind UI panels or map edge).",
                    false, false, "Camera moved only; scene unchanged. Split the stroke/area into smaller parts.");
        }
        return new(pixels.Select(p => p!.Value).ToArray(), view, ui, occ, camera);
    }

    static void RequireNoHeldInput()
    {
        if (HeldInputKeys.Any(k => (Win.GetAsyncKeyState(k) & 0x8000) != 0)) // GetAsyncKeyState high bit = currently held.
            throw new InvalidOperationException("Mouse button/modifier held; no input sent.");
    }

    // ---------------- paint ----------------

    static string Norm(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    static string? ExactName(IEnumerable<string> names, string requested) =>
        names.FirstOrDefault(n => n == requested) ?? names.FirstOrDefault(n => string.Equals(n, requested, StringComparison.OrdinalIgnoreCase))
        ?? names.FirstOrDefault(n => n.Length > 0 && Norm(n) == Norm(requested));

    static string[] MixTitles()
    {
        for (DirectoryInfo? d = new(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var dir = Path.Combine(d.FullName, "generated", "data", "mixes");
            if (Directory.Exists(dir))
                return Directory.GetFiles(dir, "*.xml").OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
                    .Select(f => XDocument.Load(f).Root?.Element("title")?.Value.Trim() ?? Path.GetFileNameWithoutExtension(f)).ToArray();
        }
        throw new FileNotFoundException("Mix definitions missing. Run --generate generated (CryBar) to export map_definitions/mixes.");
    }

    sealed record TileSnapshot(int X0, int Z0, int X1, int Z1, LiveWorld.Tile[,] Tiles, float[][] Heights);

    static TileSnapshot Snap(Game game, EditorView.ViewState view, double minX, double minZ, double maxX, double maxZ, int marginTiles)
    {
        var tiles = new LiveWorld.Tiles(game, view);
        int x0 = Math.Max(0, (int)(minX * view.InverseScale) - marginTiles), z0 = Math.Max(0, (int)(minZ * view.InverseScale) - marginTiles);
        int x1 = Math.Min(tiles.TilesX - 1, (int)(maxX * view.InverseScale) + marginTiles), z1 = Math.Min(tiles.TilesZ - 1, (int)(maxZ * view.InverseScale) + marginTiles);
        var t = new LiveWorld.Tile[x1 - x0 + 1, z1 - z0 + 1];
        for (var x = x0; x <= x1; x++) for (var z = z0; z <= z1; z++) t[x - x0, z - z0] = tiles.At(x, z);
        var heights = Enumerable.Range(x0, x1 - x0 + 2).Select(ix => view.HeightRow(Math.Min(ix, view.Vertices[0] - 1), z0, Math.Min(z1 - z0 + 2, view.Vertices[1] - z0))).ToArray();
        return new(x0, z0, x1, z1, t, heights);
    }

    object PaintWorld(Game game, JsonElement args)
    {
        var kind = String(args, "kind");
        var requested = String(args, "type");
        var points = args.GetProperty("points").EnumerateArray().Select(p => (X: p[0].GetDouble(), Z: p[1].GetDouble())).ToArray();
        var moveCamera = !args.TryGetProperty("moveCamera", out var mc) || mc.GetBoolean();
        var keepMode = args.TryGetProperty("keepMode", out var km) && km.GetBoolean();
        var duration = Int(args, "durationMs", Math.Clamp(300 + 60 * points.Length, 300, 3000)); // Host default: slower for longer strokes.
        RequireNoCursor(game);
        var names = LiveWorld.Names(game);
        string type = kind switch
        {
            "water" => ExactName(names.Water, requested),
            "forest" => ExactName(names.Forest, requested),
            "cliff" => ExactName(names.Cliff, requested),
            "texture" => ExactName(names.Subtypes.SelectMany(s => s), requested),
            "mix" => ExactName(MixTitles(), requested),
            _ => null,
        } ?? throw new WorkflowFailure("UNKNOWN_PAINT_TYPE", "preflight", $"Unknown {kind} type '{requested}'. Suggestions: "
            + string.Join(", ", GameDataCatalog.Suggest(requested, kind switch
            {
                "water" => names.Water, "forest" => names.Forest, "cliff" => names.Cliff, "mix" => MixTitles(),
                _ => names.Subtypes.SelectMany(s => s).Where(s => s.Length > 0),
            })), false, false, "Use editor_terrain_catalog for exact names. No input sent.");
        var view0 = EditorView.ReadView(game);
        foreach (var (x, z) in points) if (!view0.InsideMap(x, z)) throw OutsideMap(view0);
        double minX = points.Min(p => p.X), maxX = points.Max(p => p.X), minZ = points.Min(p => p.Z), maxZ = points.Max(p => p.Z);
        var before = Snap(game, view0, minX, minZ, maxX, maxZ, 8); // Host margin: 8 tiles covers default/large brushes.
        var unitsBefore = LiveUnits.Read(game);
        var exited = ExitEditModes(game);
        var steps = new List<object> { new { exitedMode = LiveWorld.ModeName(exited.Before) } };
        EnterMode(game, PaintMode(kind));
        steps.Add(new { editMode = PaintMode(kind) });
        string? selectionNote = null;
        try
        {
            switch (kind)
            {
                case "water": _bridge.Execute(game, $"uiSetWaterType({Catalog.Quote(type)})"); break;
                case "forest": _bridge.Execute(game, $"uiSetForestType({Catalog.Quote(type)})"); break;
                case "cliff": _bridge.Execute(game, $"uiSetCliffType({Catalog.Quote(type)})"); break;
                case "texture": selectionNote = SelectTexture(game, type, names); break;
                case "mix": selectionNote = SelectFromPalette(game, type, MixTitles(), mix: true); break;
            }
            if (kind is not "mix")
            {
                var ok = false;
                for (var i = 0; i < 25 && !ok; i++) // Host 1 s poll; setters apply on following frame.
                {
                    var s = LiveWorld.ReadSelections(game);
                    ok = kind switch { "water" => s.WaterName == type, "forest" => s.ForestName == type, "cliff" => s.CliffName == type, _ => s.Texture == type };
                    if (!ok) Thread.Sleep(40);
                }
                if (!ok)
                    throw new WorkflowFailure("PAINT_TYPE_NOT_SELECTED", "select", $"Paint selection did not become '{type}' (memory readback).", true, false,
                        "Nothing painted. Inspect editor_edit_mode paintSelections.");
            }
            steps.Add(new { selected = type, selectionNote });
            var path = Screen(game, points, moveCamera, 2);
            RequireNoHeldInput();
            Ui.DragPath(game, path.Pixels, duration);
            steps.Add(new { stroke = path.Pixels.Select(p => new[] { p.X, p.Y }).ToArray(), durationMs = duration, camera = path.Camera });
            Thread.Sleep(300); // Host settle for terrain/object updates before verification reads.
        }
        finally
        {
            if (!keepMode) ExitEditModes(game);
        }
        var view = EditorView.ReadView(game);
        var after = Snap(game, view, minX, minZ, maxX, maxZ, 8);
        var tiles = new LiveWorld.Tiles(game, view);
        int changed = 0, matching = 0, heightChanged = 0;
        for (var x = 0; x < before.Tiles.GetLength(0); x++)
            for (var z = 0; z < before.Tiles.GetLength(1); z++)
            {
                var b = before.Tiles[x, z]; var a = after.Tiles[x, z];
                if (b != a) changed++;
                if (b != a && (kind == "texture" ? tiles.TextureName(a) == type : kind == "water" ? tiles.WaterName(a) == type : true)) matching++;
            }
        for (var i = 0; i < before.Heights.Length; i++)
            for (var j = 0; j < before.Heights[i].Length; j++)
                if (Math.Abs(before.Heights[i][j] - after.Heights[i][j]) > 0.001f) heightChanged++;
        // Path coverage: tiles under stroke vertices/segments now carrying the requested texture/water.
        var samples = new List<(double X, double Z)>();
        for (var i = 0; i < points.Length; i++)
        {
            samples.Add(points[i]);
            if (i + 1 < points.Length)
            {
                var n = (int)Math.Ceiling(Math.Sqrt(Math.Pow(points[i + 1].X - points[i].X, 2) + Math.Pow(points[i + 1].Z - points[i].Z, 2)) / view.Scale);
                for (var k = 1; k < n; k++) samples.Add((points[i].X + (points[i + 1].X - points[i].X) * k / n, points[i].Z + (points[i + 1].Z - points[i].Z) * k / n));
            }
        }
        int onPath = 0;
        foreach (var (x, z) in samples)
        {
            var t = tiles.At(Math.Min(tiles.TilesX - 1, (int)(x * view.InverseScale)), Math.Min(tiles.TilesZ - 1, (int)(z * view.InverseScale)));
            if (kind == "texture" ? tiles.TextureName(t) == type : kind == "water" ? tiles.WaterName(t) == type : kind == "cliff" ? tiles.GroupName(t) == "NonPassableLand" : true) onPath++;
        }
        var unitsAfter = LiveUnits.Read(game);
        var beforeIds = unitsBefore.Select(u => u.UnitId).ToHashSet();
        var afterIds = unitsAfter.Select(u => u.UnitId).ToHashSet();
        var added = unitsAfter.Where(u => !beforeIds.Contains(u.UnitId)).ToArray();
        var removed = unitsBefore.Where(u => !afterIds.Contains(u.UnitId)).ToArray();
        return new
        {
            kind, type, points = points.Length, steps,
            verification = new
            {
                tilesChanged = changed, tilesChangedToRequested = kind is "texture" or "water" ? matching : (int?)null,
                pathSamples = samples.Count, pathSamplesMatching = kind is "texture" or "water" or "cliff" ? onPath : (int?)null,
                heightNodesChanged = heightChanged,
                objectsAdded = added.Length, objectsRemoved = removed.Length,
                addedProtos = added.GroupBy(u => u.Proto ?? "?").ToDictionary(g => g.Key, g => g.Count()),
                removedObjects = removed.Take(50).Select(u => new { u.UnitId, u.Proto, u.Player, u.Position }).ToArray(),
            },
            editModeAfter = LiveWorld.ModeName(LiveWorld.EditMode(game)),
            limitation = "Drags the left mouse along projected stroke pixels in the native paint tool with the CURRENT brush size/shape. Verification diffs live tile textures/water grid/heights in the stroke bounds (+8 tiles) and live objects; mixes cannot be read back except through tile changes. Forest painting may remove objects under the brush (panel 'Remove Nature/Non-Nature' options). Undo with editor_undo (one per stroke). Scenario not saved.",
        };
    }

    /// <summary>Texture selection: already selected → sample a visible tile with that texture → normal-UI palette OCR search.</summary>
    string SelectTexture(Game game, string type, LiveWorld.Catalogs names)
    {
        if (LiveWorld.ReadSelections(game).Texture == type) return "already selected";
        var view = EditorView.ReadView(game);
        var tiles = new LiveWorld.Tiles(game, view);
        var target = view.Target() ?? new Vector3(view.WorldWidth / 2, 0, view.WorldDepth / 2);
        var (width, height) = Ui.ClientSize(game);
        var frame = SceneUi.Capture(game);
        var ui = SceneUi.TryLoad(game, frame, out _);
        var occ = ui?.Occluders(frame);
        // Host scan bound: nearest 400 matching tiles within 60 tiles of camera target.
        int cx = (int)(target.X * view.InverseScale), cz = (int)(target.Z * view.InverseScale);
        var candidates = new List<(int X, int Z, double D)>();
        for (var x = Math.Max(0, cx - 60); x <= Math.Min(tiles.TilesX - 1, cx + 60); x++)
            for (var z = Math.Max(0, cz - 60); z <= Math.Min(tiles.TilesZ - 1, cz + 60); z++)
                if (tiles.TextureName(tiles.At(x, z)) == type) candidates.Add((x, z, Math.Pow(x - cx, 2) + Math.Pow(z - cz, 2)));
        foreach (var c in candidates.OrderBy(c => c.D).Take(400))
        {
            double wx = (c.X + 0.5) * view.Scale, wz = (c.Z + 0.5) * view.Scale;
            if (PixelFor(view, ui, occ, width, height, wx, wz, 0.9) is not { } px) continue;
            game.Move(px.X, px.Y);
            Thread.Sleep(120); // Host hover settle before pointer-dependent native.
            _bridge.Execute(game, "uiSampleTerrainAtPointer()", true);
            for (var i = 0; i < 15; i++) { if (LiveWorld.ReadSelections(game).Texture == type) return $"sampled visible tile ({c.X},{c.Z})"; Thread.Sleep(40); }
            break;
        }
        var ordered = names.Subtypes.SelectMany(s => s).Where(s => s.Length > 0).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray();
        return SelectFromPalette(game, type, ordered, mix: false);
    }

    /// <summary>Normal-UI bottom list palette: scroll by estimated index, OCR labels, click, verify (memory for textures, OCR label for mixes).</summary>
    static string SelectFromPalette(Game game, string type, string[] ordered, bool mix)
    {
        var frame = SceneUi.Capture(game);
        var ui = SceneUi.TryLoad(game, frame, out var reason);
        if (ui?.Palette is not { } palette)
            throw new WorkflowFailure("PALETTE_UNREVIEWED", "select", "Palette selection only reviewed for the normal 2560x1440 UI" + (ui is null ? ": " + reason : $" (detected {ui.Kind})."),
                true, false, kind(mix) + " not on visible map to sample. Select it manually in the palette, then retry (already-selected types are accepted).");
        static string kind(bool m) => m ? "Mix" : "Texture";
        static int[] R(JsonElement e) => e.EnumerateArray().Select(v => v.GetInt32()).ToArray();
        var columns = palette.GetProperty("labelColumns").EnumerateArray().Select(R).ToArray();
        var anchor = R(palette.GetProperty("scrollAnchor"));
        var index = Array.FindIndex(ordered, n => n == type);
        var perRow = columns.Length;
        game.Move(anchor[0], anchor[1]);
        Thread.Sleep(80);
        for (var i = 0; i < ordered.Length / perRow / 20 + 2; i++) { Ui.Wheel(game, 20); Thread.Sleep(30); } // Scroll to top.
        var row = 0;
        void ScrollTo(int wanted)
        {
            var delta = wanted - row;
            while (delta != 0) { var s = Math.Clamp(delta, -20, 20); game.Move(anchor[0], anchor[1]); Ui.Wheel(game, -s); delta -= s; Thread.Sleep(40); }
            row = wanted;
        }
        ScrollTo(index / perRow);
        for (var attempt = 0; attempt < 6; attempt++) // Host bound on OCR correction rounds.
        {
            Thread.Sleep(200);
            var lines = columns.SelectMany(c => UiRead.Lines(game, c, 2)).Where(l => l.Text.Trim().Length > 2).ToArray();
            var hit = lines.FirstOrDefault(l => Norm(l.Text) == Norm(type));
            if (hit is not null)
            {
                Ui.Click(game, hit.X, hit.Y, "left");
                Thread.Sleep(200);
                if (!mix)
                {
                    for (var i = 0; i < 15; i++) { if (LiveWorld.ReadSelections(game).Texture == type) return $"palette row {row}"; Thread.Sleep(40); }
                    throw new WorkflowFailure("PAINT_TYPE_NOT_SELECTED", "select", $"Clicked palette label '{hit.Text}' but selection is not '{type}'.", true, false, "Nothing painted; inspect palette.");
                }
                var label = string.Join(" ", UiRead.Lines(game, R(palette.GetProperty("selectedLabel")), 2).Select(l => l.Text)); // Scale 2 keeps 560px crop under the 1600px OCR bound.
                if (Norm(label) == Norm(type)) return $"palette row {row} (OCR-verified label)";
                throw new WorkflowFailure("PAINT_TYPE_NOT_SELECTED", "select", $"Selected-mix label reads '{label}', not '{type}'.", true, false, "Nothing painted; inspect palette.");
            }
            // Correct the scroll position from a recognised label's known index (row = upper/lower half of the column).
            var midY = columns[0][1] + columns[0][3] / 2;
            var known = lines.Select(l => (l, i: Array.FindIndex(ordered, n => Norm(n) == Norm(l.Text)))).Where(t => t.i >= 0).ToArray();
            if (known.Length == 0) break;
            var firstRow = known[0].i / perRow - (known[0].l.Y > midY ? 1 : 0);
            row = Math.Max(0, firstRow);
            var targetRow = index / perRow;
            // Target should have been visible (OCR miss): put it on the other visible row and read again.
            ScrollTo(targetRow >= row && targetRow <= row + 1 ? Math.Max(0, targetRow - (targetRow == row ? 1 : 0)) : targetRow);
        }
        throw new WorkflowFailure("PAINT_TYPE_NOT_FOUND_IN_PALETTE", "select", $"'{type}' not found in the palette list by OCR (filter dropdown not 'All', OCR misread or different sort).",
            true, false, "Palette scrolled only; nothing painted. Select the type manually, then retry.");
    }

    // ---------------- elevation ----------------

    static (double X, double Z)[] Serpentine(double minX, double minZ, double maxX, double maxZ, double inset, double spacing)
    {
        double x0 = minX + inset, x1 = maxX - inset, z0 = minZ + inset, z1 = maxZ - inset;
        if (x0 > x1) x0 = x1 = (minX + maxX) / 2;
        if (z0 > z1) z0 = z1 = (minZ + maxZ) / 2;
        var rows = Math.Max(1, (int)Math.Ceiling((x1 - x0) / spacing)) + 1;
        var path = new List<(double, double)>();
        for (var r = 0; r < rows; r++)
        {
            var x = rows == 1 ? x0 : x0 + (x1 - x0) * r / (rows - 1);
            if (r % 2 == 0) { path.Add((x, z0)); path.Add((x, z1)); } else { path.Add((x, z1)); path.Add((x, z0)); }
        }
        return path.ToArray();
    }

    sealed record HeightStats(int Nodes, int Within, float Min, float Max, double Mean);

    static HeightStats AreaHeights(EditorView.ViewState v, double minX, double minZ, double maxX, double maxZ, double target, double tolerance)
    {
        int ix0 = (int)Math.Ceiling(minX * v.InverseScale), ix1 = Math.Min(v.Vertices[0] - 1, (int)Math.Floor(maxX * v.InverseScale));
        int iz0 = (int)Math.Ceiling(minZ * v.InverseScale), iz1 = Math.Min(v.Vertices[1] - 1, (int)Math.Floor(maxZ * v.InverseScale));
        var values = Enumerable.Range(ix0, Math.Max(0, ix1 - ix0 + 1)).SelectMany(ix => v.HeightRow(ix, iz0, Math.Max(1, iz1 - iz0 + 1))).ToArray();
        return new(values.Length, values.Count(h => Math.Abs(h - target) <= tolerance), values.Min(), values.Max(), values.Average(h => (double)h));
    }

    object Elevation(Game game, JsonElement args)
    {
        var op = String(args, "operation");
        double minX = args.GetProperty("minX").GetDouble(), minZ = args.GetProperty("minZ").GetDouble(),
            maxX = args.GetProperty("maxX").GetDouble(), maxZ = args.GetProperty("maxZ").GetDouble();
        var tolerance = Dbl(args, "tolerance", 0.05);
        var moveCamera = !args.TryGetProperty("moveCamera", out var mc) || mc.GetBoolean();
        RequireNoCursor(game);
        var view = EditorView.ReadView(game);
        if (!view.InsideMap(minX, minZ) || !view.InsideMap(maxX, maxZ)) throw OutsideMap(view);
        var spacing = view.Scale * 2; // Brush ≈2.2 nodes radius (research/LIVE-WORLD.md): 2-node rows overlap.
        var inset = view.Scale * 2;
        // Brush centers on the lower-left node of the tile under the pointer (live dab test, research/LIVE-WORLD.md):
        // aim at tile middles (+half tile) so pixel rounding cannot drop the center one node.
        var half = view.Scale / 2;
        var path = Serpentine(minX, minZ, maxX, maxZ, inset, spacing).Select(p => (X: p.X + half, Z: p.Z + half)).ToArray();
        if (path.Length > 256) throw new ArgumentException("Area too large for one stroke (>256 vertices); split it.");
        var cx = (minX + maxX) / 2; var cz = (minZ + maxZ) / 2;
        var before = AreaHeights(view, minX, minZ, maxX, maxZ, 0, 0);
        var exited = ExitEditModes(game);
        var steps = new List<object> { new { exitedMode = LiveWorld.ModeName(exited.Before) } };
        double? target = op == "set" ? args.GetProperty("height").GetDouble() : null;
        double? sampled = null;
        try
        {
            // Ensure the whole stroke is visible first (camera may move once).
            // Visibility pre-check (may move camera): at the target height for set; otherwise loose ray tolerance because
            // steep existing edges make quantized back-projection noisy.
            var screen = Screen(game, path, moveCamera, 4, target is { } th ? (float)th : null);
            if (screen.Camera is not null) steps.Add(new { camera = screen.Camera });
            if (op == "smooth")
            {
                EnterMode(game, "smooth");
                screen = Screen(game, path, false, 4);
                RequireNoHeldInput();
                Ui.DragPath(game, screen.Pixels, Int(args, "durationMs", Math.Clamp(200 * path.Length, 400, 5000)));
                steps.Add(new { smoothStroke = path.Length });
            }
            else
            {
                if (op == "set")
                {
                    if (maxX - minX < 7 * view.Scale || maxZ - minZ < 7 * view.Scale)
                        throw new WorkflowFailure("AREA_TOO_SMALL", "preflight", "operation=set needs an area of at least 7×7 nodes (bump + test dab inside the area).",
                            false, false, "Enlarge the area or use operation=flatten with a reference point at the wanted height. No input sent.");
                    steps.AddRange(SetSample(game, target!.Value, cx, cz, minX, minZ, tolerance, out var s));
                    sampled = s;
                }
                else
                {
                    EnterMode(game, "elevationsample");
                    double rx = Dbl(args, "referenceX", cx), rz = Dbl(args, "referenceZ", cz);
                    var v = EditorView.ReadView(game);
                    var refPixel = Screen(game, [(rx, rz)], moveCamera, 1).Pixels[0];
                    RequireNoHeldInput();
                    Ui.Click(game, refPixel.X, refPixel.Y, "right"); // Right click samples height under pointer (elevationsample tool).
                    sampled = v.Height(rx, rz).Height;
                    steps.Add(new { sampledAt = new { x = rx, z = rz }, referenceNodeHeight = sampled });
                }
                // Project at the goal height: the brush acts where the pointer ray meets terrain, which is at the goal
                // height once painted (projecting at the old height shifted strokes toward the camera by ~2-4 units).
                // Two passes fill nodes missed while the first pass was still raising/lowering terrain under the pointer.
                var goalHeight = (float)(target ?? sampled ?? 0);
                for (var pass = 0; pass < 2; pass++)
                {
                    screen = Screen(game, pass == 0 ? path : path.Reverse().ToArray(), false, 2, goalHeight);
                    RequireNoHeldInput();
                    Ui.DragPath(game, screen.Pixels, Int(args, "durationMs", Math.Clamp(120 * path.Length, 300, 5000)));
                    Thread.Sleep(150);
                }
                steps.Add(new { paintStroke = path.Length, passes = 2 });
                // Touch-up: dab remaining off-target nodes (stroke ends/disc corners), host bound 40 dabs.
                var touched = 0;
                for (var round = 0; round < 2; round++)
                {
                    var v2 = EditorView.ReadView(game);
                    var misses = new List<(double X, double Z)>();
                    int jx0 = (int)Math.Ceiling(minX * v2.InverseScale), jx1 = Math.Min(v2.Vertices[0] - 1, (int)Math.Floor(maxX * v2.InverseScale));
                    int jz0 = (int)Math.Ceiling(minZ * v2.InverseScale), jz1 = Math.Min(v2.Vertices[1] - 1, (int)Math.Floor(maxZ * v2.InverseScale));
                    for (var ix = jx0; ix <= jx1; ix++)
                    {
                        var row = v2.HeightRow(ix, jz0, jz1 - jz0 + 1);
                        for (var j = 0; j < row.Length; j++)
                            if (Math.Abs(row[j] - goalHeight) > tolerance) misses.Add((ix * v2.Scale + half, (jz0 + j) * v2.Scale + half));
                    }
                    if (misses.Count == 0 || touched + misses.Count > 40) break;
                    var dabs = Screen(game, misses.ToArray(), false, 4, goalHeight);
                    foreach (var d in dabs.Pixels) { RequireNoHeldInput(); Ui.Click(game, d.X, d.Y, "left"); touched++; }
                    Thread.Sleep(200);
                }
                if (touched > 0) steps.Add(new { touchUpDabs = touched });
            }
            Thread.Sleep(300);
        }
        finally { ExitEditModes(game); }
        var after = EditorView.ReadView(game);
        var goal = target ?? sampled ?? 0;
        var stats = AreaHeights(after, minX, minZ, maxX, maxZ, goal, tolerance);
        var ring = AreaHeights(after, minX - 3 * after.Scale, minZ - 3 * after.Scale, maxX + 3 * after.Scale, maxZ + 3 * after.Scale, goal, tolerance);
        return new
        {
            operation = op, area = new { minX, minZ, maxX, maxZ }, target, sampledHeight = sampled, steps,
            before = new { before.Nodes, before.Min, before.Max, before.Mean },
            after = new { stats.Nodes, withinTolerance = op == "smooth" ? (int?)null : stats.Within, stats.Min, stats.Max, stats.Mean, tolerance },
            success = op == "smooth" ? (bool?)null : stats.Within == stats.Nodes,
            nodesWithin3NodeMarginAtTarget = op == "smooth" ? (int?)null : ring.Within - stats.Within,
            limitation = "Uses native elevation tools with the CURRENT brush: set = raise/lower a bump inside the area, right-click sample at a bisected slope point, verify on a test dab, then paint the area with the elevation-sample tool; flatten = sample at reference point then paint; smooth = smooth tool stroke. Brush edges can alter nodes up to ~2 nodes outside the area (reported). Heights are native quantized node heights. Undo with editor_undo (several strokes). Not saved.",
        };
    }

    /// <summary>Closed loop for an exact sample height: bump at area center, slope bisection sampling, test dab verification.</summary>
    List<object> SetSample(Game game, double h, double cx, double cz, double minX, double minZ, double tolerance, out double sampled)
    {
        var steps = new List<object>();
        var v = EditorView.ReadView(game);
        var s = v.Scale;
        // Snap anchor to a node; test node 3 nodes inside the min corner (>4 nodes from the anchor for 7x7+ areas).
        double ax = Math.Round(cx / s) * s, az = Math.Round(cz / s) * s;
        double tx = Math.Ceiling(minX / s) * s + 2 * s, tz = Math.Ceiling(minZ / s) * s + 2 * s;
        var (width, height) = Ui.ClientSize(game);
        EnterMode(game, "elevation");
        var raise = h > v.Height(ax, az).Height;
        var frame = SceneUi.Capture(game);
        var ui = SceneUi.TryLoad(game, frame, out _);
        var occ = ui?.Occluders(frame);
        var clicks = 0;
        for (; clicks < 60; clicks++) // Host bound: ~1 world unit per click observed; 60 covers tall targets.
        {
            v = EditorView.ReadView(game);
            var now = v.Height(ax, az).Height;
            if (raise ? now >= h + 0.5 : now <= h - 0.5) break; // Overshoot 0.5 so the slope brackets h.
            var px = PixelFor(v, ui, occ, width, height, ax + s / 2, az + s / 2, 2, now) // Tile middle → brush centers on node (ax,az).
                ?? throw new WorkflowFailure("TARGET_NOT_CLICKABLE", "elevation", "Anchor not clickable.", clicks > 0, false, "Inspect area; undo partial bump if needed.");
            RequireNoHeldInput();
            Ui.Click(game, px.X, px.Y, raise ? "left" : "right");
            Thread.Sleep(60);
        }
        v = EditorView.ReadView(game);
        steps.Add(new { bumpClicks = clicks, anchor = new { x = ax, z = az, height = v.Height(ax, az).Height } });
        EnterMode(game, "elevationsample");
        // Slope along +X from anchor: find node pair (k,k+1) whose heights bracket h; bilinear along X at z=az.
        float H(double x) => v.Height(x, az).Height;
        double lo = ax, hi = ax;
        for (var k = 0; k < 6; k++)
        {
            double a = ax + k * s, b = ax + (k + 1) * s;
            if ((H(a) - h) * (H(b) - h) <= 0) { lo = a; hi = b; break; }
        }
        if (lo == hi) throw new WorkflowFailure("SLOPE_NOT_BRACKETED", "sample", "Bump slope does not bracket the target height.", true, false, "Undo bump (editor_undo) and retry.");
        double ha = H(lo), hb = H(hi);
        var t = Math.Abs(hb - ha) < 1e-6 ? 0.5 : (h - ha) / (hb - ha);
        var tested = new List<(double T, double M)>();
        double measured = double.NaN;
        for (var iter = 0; iter < 6; iter++) // Host bound: secant iterations on (t, measured).
        {
            v = EditorView.ReadView(game);
            var sx = lo + t * (hi - lo);
            var sh = (float)(ha + t * (hb - ha));
            frame = SceneUi.Capture(game); occ = ui?.Occluders(frame);
            var sp = PixelFor(v, ui, occ, width, height, sx, az, 2, sh)
                ?? throw new WorkflowFailure("TARGET_NOT_CLICKABLE", "sample", "Sample point not clickable.", true, false, "Undo bump and retry with camera closer.");
            var tp = PixelFor(v, ui, occ, width, height, tx + s / 2, tz + s / 2, 2)
                ?? throw new WorkflowFailure("TARGET_NOT_CLICKABLE", "sample", "Test node not clickable.", true, false, "Undo bump and retry with camera closer.");
            RequireNoHeldInput();
            Ui.Click(game, sp.X, sp.Y, "right");
            Thread.Sleep(80);
            Ui.Click(game, tp.X, tp.Y, "left");
            Thread.Sleep(200);
            measured = EditorView.ReadView(game).Height(tx, tz).Height;
            tested.Add((t, measured));
            if (Math.Abs(measured - h) <= tolerance) break;
            if (tested.Count >= 2 && Math.Abs(tested[^1].M - tested[^2].M) > 1e-6)
                t = tested[^1].T + (h - tested[^1].M) * (tested[^1].T - tested[^2].T) / (tested[^1].M - tested[^2].M);
            else t += (h - measured) / (hb - ha == 0 ? 1 : hb - ha);
            t = Math.Clamp(t, -0.5, 1.5);
        }
        sampled = measured;
        steps.Add(new { sampleIterations = tested.Select(p => new { t = p.T, measured = p.M }).ToArray(), testNode = new { x = tx, z = tz } });
        if (Math.Abs(measured - h) > tolerance)
            throw new WorkflowFailure("SAMPLE_NOT_CONVERGED", "sample", $"Sampled height {measured:F3} not within {tolerance} of {h} after {tested.Count} tries.", true, false,
                "Bump and test dabs remain inside the area; editor_undo reverts them, or raise tolerance.");
        return steps;
    }

    // ---------------- unit transform ----------------

    void SelectExact(Game game, int id)
    {
        _bridge.Execute(game, "uiClearSelection()");
        _bridge.Execute(game, $"uiLookAtAndSelectUnit({id})");
        for (var i = 0; i < 20; i++) // Host 1 s poll for queued selection.
        {
            Thread.Sleep(50);
            var selection = EditorView.ReadSelection(game);
            if (selection.Length == 1 && selection[0].Kind == 0 && selection[0].Id == id) return;
        }
        throw new WorkflowFailure("SELECTION_NOT_EXACT", "select", $"Selection is not exactly unit {id}.", true, false, "Camera/selection changed only; nothing transformed.");
    }

    object TransformUnit(Game game, JsonElement args)
    {
        if (_layout.Selection is null || _layout.Units is null) throw new InvalidDataException("Selection/unit layouts required.");
        var id = Int(args, "unitId"); var proto = String(args, "proto"); var player = Int(args, "player");
        RequireNoCursor(game);
        LiveUnits.Unit Live() => LiveUnits.Read(game).FirstOrDefault(u => u.UnitId == id)
            ?? throw new WorkflowFailure("UNIT_NOT_FOUND", "observe", $"Unit {id} not present.", false, false, "Re-read editor_units.");
        var unit = Live();
        if (unit.Player != player || !string.Equals(unit.Proto, proto, StringComparison.OrdinalIgnoreCase))
            throw new WorkflowFailure("UNIT_TUPLE_MISMATCH", "preflight", $"Unit {id} is {unit.Proto}/player {unit.Player}.", false, false, "Re-read editor_units; nothing changed.");
        var tolerance = Dbl(args, "tolerance", 1);
        var start = unit;
        var steps = new List<object>();
        ExitEditModes(game);
        if (args.TryGetProperty("x", out var xe))
        {
            double x = xe.GetDouble(), z = args.GetProperty("z").GetDouble();
            SelectExact(game, id);
            Thread.Sleep(300); // uiLookAtAndSelectUnit camera move settles on following frames.
            EnterMode(game, "moveunit");
            try
            {
                var path = Screen(game, [(unit.Position.X, unit.Position.Z), (x, z)], !args.TryGetProperty("moveCamera", out var mc) || mc.GetBoolean(), 2);
                RequireNoHeldInput();
                Ui.DragPath(game, path.Pixels, Int(args, "durationMs", 600));
                steps.Add(new { move = new { from = new[] { path.Pixels[0].X, path.Pixels[0].Y }, to = new[] { path.Pixels[1].X, path.Pixels[1].Y } }, camera = path.Camera });
                for (var i = 0; i < 15; i++) { Thread.Sleep(80); unit = Live(); if (Math.Sqrt(Math.Pow(unit.Position.X - x, 2) + Math.Pow(unit.Position.Z - z, 2)) <= tolerance) break; }
            }
            finally { ExitEditModes(game); }
        }
        if (args.TryGetProperty("heading", out var he))
        {
            steps.Add(new { rotate = RotateTo(game, id, he.GetDouble(), steps.Count > 0) });
            unit = Live();
        }
        var headingRequested = args.TryGetProperty("heading", out var hq) ? ((hq.GetDouble() % 360) + 360) % 360 : (double?)null;
        double? moveError = args.TryGetProperty("x", out var x2) ? Math.Sqrt(Math.Pow(unit.Position.X - x2.GetDouble(), 2) + Math.Pow(unit.Position.Z - args.GetProperty("z").GetDouble(), 2)) : null;
        double? headingError = headingRequested is { } hr && unit.HeadingDegrees is { } hd ? Math.Abs(((hd - hr) % 360 + 540) % 360 - 180) : null;
        return new
        {
            unitId = id, before = start, after = unit, steps,
            positionError = moveError, moved = moveError is null ? (bool?)null : moveError <= tolerance,
            headingError, headingWithinStep = headingError is null ? (bool?)null : headingError <= RotationStepDegrees / 2 + 0.01,
            limitation = "Move: exact selection then moveunit-tool drag from the unit's projected pixel to the target (camera may move). Rotate: uiRotateSelection steps of exactly 22.5° (native ignores magnitude), so headings are quantized; heading read from the reviewed transform (180 = editor default). Verified from live memory. Undo with editor_undo. Not saved.",
        };
    }

    /// <summary>Rotates one unit to an absolute heading with exact selection and 22.5° native steps; verified per step.</summary>
    object RotateTo(Game game, int id, double heading, bool earlierEffects)
    {
        LiveUnits.Unit Live() => LiveUnits.Read(game).FirstOrDefault(u => u.UnitId == id)
            ?? throw new WorkflowFailure("UNIT_NOT_FOUND", "observe", $"Unit {id} not present.", earlierEffects, false, "Re-read editor_units.");
        var wanted = ((heading % 360) + 360) % 360;
        if (Live().HeadingDegrees is not { } current)
            throw new WorkflowFailure("HEADING_UNREADABLE", "rotate", "Unit transform is not a pure Y rotation; heading unreadable.", earlierEffects, false, "Rotate manually.");
        var delta = ((wanted - current) % 360 + 540) % 360 - 180; // (-180,180]
        var count = (int)Math.Round(delta / RotationStepDegrees);
        if (count != 0)
        {
            SelectExact(game, id);
            for (var i = 0; i < Math.Abs(count); i++)
            {
                var previous = Live().HeadingDegrees;
                _bridge.Execute(game, $"uiRotateSelection({Math.Sign(count)})");
                for (var k = 0; k < 15 && Live().HeadingDegrees == previous; k++) Thread.Sleep(40); // Host 0.6 s per-step poll.
            }
        }
        var final = Live().HeadingDegrees;
        return new { requested = wanted, from = current, to = final, rotateCalls = Math.Abs(count), stepDegrees = RotationStepDegrees,
            error = final is { } f ? Math.Abs(((f - wanted) % 360 + 540) % 360 - 180) : (double?)null };
    }

    // ---------------- catalogs ----------------

    static object TerrainCatalog(Game? game, JsonElement args)
    {
        var kind = String(args, "kind");
        var filter = String(args, "filter", "");
        var offset = Int(args, "offset", 0); var limit = Int(args, "limit", 100);
        IEnumerable<object> items = kind switch
        {
            "mixes" => MixTitles().Select((n, i) => (object)new { index = i, name = n }),
            "editModes" => LiveWorld.Modes.Select(m => (object)new { name = m.Name, value = m.Value }),
            _ => LiveCatalog(game!, kind),
        };
        var all = items.Where(i => filter.Length == 0 || JsonSerializer.Serialize(i).Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
        return new
        {
            kind, total = all.Length, offset, limit,
            nextOffset = offset + limit < all.Length ? (int?)(offset + limit) : null,
            entries = all.Skip(offset).Take(limit).ToArray(),
            usage = kind switch
            {
                "textures" => "editor_paint_world kind=texture type=<name>",
                "water" => "uiSetWaterType(name) / editor_paint_world kind=water",
                "forest" => "uiSetForestType(name) / editor_paint_world kind=forest",
                "cliff" => "uiSetCliffType(name) / editor_paint_world kind=cliff",
                "mixes" => "editor_paint_world kind=mix type=<title> (normal-UI palette OCR selection)",
                "lighting" => "uiApplyLightingSet(n) with n = index",
                "civs" => "civ id → major god name (live table; ids match editor_players civId / editor_live_players civId)",
                _ => "editor_edit_mode mode=<name>",
            },
            limitation = kind is "mixes" ? "Titles from generated map_definitions/mixes (file order = palette order observed)." : kind == "editModes" ? "Host table from live editMode diff experiments."
                : "Read live from the running game's loaded definitions (research/LIVE-WORLD.md); names are exactly what the natives accept. Minor-god id mapping is not provided (unverified).",
        };
    }

    static IEnumerable<object> LiveCatalog(Game game, string kind)
    {
        var names = LiveWorld.Names(game);
        return kind switch
        {
            "textures" => names.Subtypes.SelectMany((s, t) => s.Select((n, i) => (Name: n, Type: t, Sub: i))).Where(e => e.Name.Length > 0)
                .Select(e => (object)new { name = e.Name, group = names.Groups[e.Type], type = e.Type, subtype = e.Sub }),
            "water" => names.Water.Select((n, i) => (object)new { index = i, name = n }),
            "forest" => names.Forest.Select((n, i) => (object)new { index = i, name = n }),
            "cliff" => names.Cliff.Select((n, i) => (object)new { index = i, name = n }),
            "lighting" => LiveWorld.LightingSets(game).Select((n, i) => (object)new { index = i, name = n }),
            "civs" => LiveCivs(game),
            _ => throw new ArgumentException("Unknown catalog kind."),
        };
    }

    static List<object> LiveCivs(Game game)
    {
        var l = game.Layout.Players ?? throw new InvalidDataException("Player layout unavailable.");
        var list = new List<object>();
        for (var c = 0; c < l.CivNameTableCount; c++)
        {
            var ptr = game.Pointer(game.Base + checked((int)l.CivNameTableRva + c * l.CivNameStride));
            var bytes = ptr == 0 ? [] : game.Read(ptr, 32);
            var end = Array.IndexOf(bytes, (byte)0);
            list.Add(new { civId = c, name = end <= 0 ? "" : System.Text.Encoding.ASCII.GetString(bytes, 0, end) });
        }
        return list;
    }

    // ---------------- map-layout helpers ----------------

    object CameraFrame(Game game, JsonElement args)
    {
        double minX = args.GetProperty("minX").GetDouble(), minZ = args.GetProperty("minZ").GetDouble(),
            maxX = args.GetProperty("maxX").GetDouble(), maxZ = args.GetProperty("maxZ").GetDouble();
        var look = CameraLookAt(game, (minX + maxX) / 2, (minZ + maxZ) / 2, Dbl(args, "tolerance", 3), 4);
        var view = EditorView.ReadView(game);
        var (width, height) = Ui.ClientSize(game);
        var frame = SceneUi.Capture(game);
        var ui = SceneUi.TryLoad(game, frame, out _);
        var occ = ui?.Occluders(frame);
        var corners = new[] { (minX, minZ), (minX, maxZ), (maxX, minZ), (maxX, maxZ), ((minX + maxX) / 2, (minZ + maxZ) / 2) };
        var visible = corners.Select(c => new { x = c.Item1, z = c.Item2, clickable = PixelFor(view, ui, occ, width, height, c.Item1, c.Item2, 2) is not null }).ToArray();
        return new
        {
            camera = look, corners = visible, fits = visible.All(c => c.clickable),
            note = visible.All(c => c.clickable) ? "Whole area visible and clickable (clear of reviewed UI panels)." : "Area larger than the clickable view at this zoom; split the work or zoom out (mouse wheel) and re-check.",
            limitation = "Centers via minimap closed loop; does not change zoom. Clickability = projection inside viewport, outside reviewed UI panels and ray back-check.",
        };
    }

    // Tiny 3x5 digit font for overview labels (host drawing, not a game asset).
    static readonly string[] Digits = ["111101101101111", "010110010010111", "111001111100111", "111001111001111", "101101111001001",
        "111100111001111", "111100111101111", "111001001001001", "111101111101111", "111101111001111"];

    static ImageResult Overview(Game game, JsonElement args)
    {
        var view = EditorView.ReadView(game);
        var units = LiveUnits.Read(game);
        var player = args.TryGetProperty("player", out var pp) ? (int?)pp.GetInt32() : null;
        var protoFilter = String(args, "proto", "");
        var maxLabels = Int(args, "maxLabels", 80);
        var scale = Int(args, "labelScale", 2);
        var capture = Ui.CapturePixels(game, Int(args, "maxWidth", 1280));
        var (cw, ch) = Ui.ClientSize(game);
        double f = capture.Width / (double)cw;
        var labels = new List<object>();
        void Set(int x, int y, byte r, byte g, byte b)
        {
            if (x < 0 || y < 0 || x >= capture.Width || y >= capture.Height) return;
            var o = (y * capture.Width + x) * 4; capture.Bgra[o] = b; capture.Bgra[o + 1] = g; capture.Bgra[o + 2] = r;
        }
        foreach (var u in units.Where(u => (player is null || u.Player == player) && (protoFilter.Length == 0 || (u.Proto ?? "").Contains(protoFilter, StringComparison.OrdinalIgnoreCase)))
                     .OrderBy(u => u.UnitId))
        {
            if (labels.Count >= maxLabels) break;
            var p = view.Project(new Vector3(u.Position.X, u.Position.Y, u.Position.Z));
            if (!p.VisibleInViewport) continue;
            int x = (int)(p.X * f), y = (int)(p.Y * f);
            var text = u.UnitId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            int w = text.Length * 4 * scale + scale, h = 7 * scale;
            for (var dx = -3; dx <= 3; dx++) { Set(x + dx, y, 255, 255, 0); Set(x, y + dx, 255, 255, 0); }
            for (var yy = 0; yy < h; yy++) for (var xx = 0; xx < w; xx++) Set(x + 4 + xx, y - h - 2 + yy, 0, 0, 0);
            for (var i = 0; i < text.Length; i++)
            {
                var glyph = Digits[text[i] - '0'];
                for (var gy = 0; gy < 5; gy++) for (var gx = 0; gx < 3; gx++)
                    if (glyph[gy * 3 + gx] == '1')
                        for (var sy = 0; sy < scale; sy++) for (var sx = 0; sx < scale; sx++)
                            Set(x + 4 + scale + i * 4 * scale + gx * scale + sx, y - h - 2 + scale + gy * scale + sy, 255, 255, 255);
            }
            labels.Add(new { u.UnitId, u.Proto, u.Player, pixel = new[] { (int)Math.Round(p.X), (int)Math.Round(p.Y) }, u.Position, u.HeadingDegrees });
        }
        view.Verify();
        return new ImageResult(Ui.Png(capture.Width, capture.Height, capture.Bgra), new
        {
            labeled = labels.Count, imageScale = f, labels,
            limitation = "Screenshot with host-drawn markers (yellow cross at projected unit origin) and white unit-ID labels; pixel coordinates in labels are full-resolution client pixels. Projection is frustum-only (occluded units still labeled).",
        });
    }

    object ResourceBalance(Game game, JsonElement args)
    {
        var units = LiveUnits.Read(game);
        var catalog = (_gameData ??= new GameDataCatalog()).Resources(exe, _layout.ExeSha256);
        var radii = args.TryGetProperty("radii", out var r) ? r.EnumerateArray().Select(e => e.GetDouble()).ToArray() : [30.0, 60.0]; // Host default rings (world units).
        var centers = new List<(int Player, double X, double Z, string Source)>();
        if (args.TryGetProperty("centers", out var cs))
            foreach (var c in cs.EnumerateArray())
                centers.Add((c.GetProperty("player").GetInt32(), c.GetProperty("x").GetDouble(), c.GetProperty("z").GetDouble(), "explicit"));
        else
            foreach (var tc in units.Where(u => u.Player > 0 && (u.Proto ?? "").StartsWith("TownCenter", StringComparison.OrdinalIgnoreCase)).GroupBy(u => u.Player))
                centers.Add((tc.Key, tc.First().Position.X, tc.First().Position.Z, $"TownCenter {tc.First().UnitId}"));
        var resources = units.Where(u => u.Proto is { } p && catalog.TryGetValue(p, out var info) && info.Resource is not null && (u.Player == 0 || info.UnitTypes.Contains("Huntable")))
            .Select(u => (Unit: u, Info: catalog[u.Proto!])).ToArray();
        var rows = centers.Select(c => new
        {
            player = c.Player, center = new { x = c.X, z = c.Z }, source = c.Source,
            rings = radii.Select(radius =>
            {
                var inside = resources.Where(t => Math.Pow(t.Unit.Position.X - c.X, 2) + Math.Pow(t.Unit.Position.Z - c.Z, 2) <= radius * radius).ToArray();
                return new
                {
                    radius,
                    byResource = inside.GroupBy(t => t.Info.Resource!.ToLowerInvariant()).ToDictionary(g => g.Key, g => new
                    {
                        objects = g.Count(), amount = g.Sum(t => t.Info.Amount),
                        nearest = Math.Round(g.Min(t => Math.Sqrt(Math.Pow(t.Unit.Position.X - c.X, 2) + Math.Pow(t.Unit.Position.Z - c.Z, 2))), 1),
                        protos = g.GroupBy(t => t.Unit.Proto!).ToDictionary(p => p.Key, p => p.Count()),
                    }),
                };
            }).ToArray(),
        }).ToArray();
        return new
        {
            players = rows, resourceObjectsTotal = resources.Length,
            note = centers.Count == 0 ? "No TownCenters found; pass centers [{player,x,z}]." : null,
            limitation = "Counts live Gaia resource objects (and huntables) within world-unit radii of each player's first TownCenter (or explicit centers). Amounts are static initial values from proto.xml, not runtime remaining amounts; forests count per tree. Not pathing distance.",
        };
    }

    static (double X, double Z) MirrorPoint(string mode, double x, double z, double w, double d) => mode switch
    {
        "point" => (w - x, d - z),
        "flipX" => (w - x, z),
        "flipZ" => (x, d - z),
        "swapXZ" => (z, x),
        _ => throw new ArgumentException("mode: point/flipX/flipZ/swapXZ"),
    };

    object MirrorUnits(Game game, JsonElement args)
    {
        var source = Int(args, "sourcePlayer"); var target = Int(args, "targetPlayer"); var mode = String(args, "mode");
        var view = EditorView.ReadView(game);
        var units = LiveUnits.Read(game);
        var radius = Dbl(args, "radius", 0);
        double? cx = args.TryGetProperty("x", out var xe) ? xe.GetDouble() : null, cz = args.TryGetProperty("z", out var ze) ? ze.GetDouble() : null;
        var includeGaia = args.TryGetProperty("includeGaiaNear", out var ig) && ig.GetBoolean();
        var picked = units.Where(u => (u.Player == source || (includeGaia && u.Player == 0))
            && (radius <= 0 || cx is null || Math.Pow(u.Position.X - cx.Value, 2) + Math.Pow(u.Position.Z - cz!.Value, 2) <= radius * radius)).ToArray();
        if (includeGaia && (radius <= 0 || cx is null)) throw new ArgumentException("includeGaiaNear requires x, z and radius.");
        var items = picked.Select(u =>
        {
            var (mx, mz) = MirrorPoint(mode, u.Position.X, u.Position.Z, view.WorldWidth, view.WorldDepth);
            return new LayoutItem(u.Proto ?? "?", u.Player == 0 ? 0 : target, Math.Round(mx, 2), Math.Round(mz, 2));
        }).ToArray();
        if (items.Length is < 1 or > SceneGeometry.MaxLayoutItems)
            throw new ArgumentException($"Mirror selection has {items.Length} objects; need 1..32 (narrow with x/z/radius).");
        return RunLayout(game, items, args, "Mirror of player " + source + " (" + mode + ") about map " + view.WorldWidth + "x" + view.WorldDepth);
    }

    object Scatter(Game game, JsonElement args)
    {
        var proto = String(args, "proto"); var count = Int(args, "count"); var player = Int(args, "player", 0);
        double x = args.GetProperty("x").GetDouble(), z = args.GetProperty("z").GetDouble(), radius = args.GetProperty("radius").GetDouble();
        var shape = String(args, "shape", "disc");
        // Default spacing: footprint diameter + 1 world unit (shipped obstruction radii), at least 1.
        var footprint = TryFootprints(out _) is { } fp && fp.TryGetValue(proto, out var f) ? Math.Max(f.X ?? 0, f.Z ?? 0) : 0;
        var minSpacing = Dbl(args, "minSpacing", Math.Max(1, 2 * footprint + 1));
        var seed = Int(args, "seed", 1);
        if (count is < 1 or > SceneGeometry.MaxLayoutItems) throw new ArgumentException("count 1..32.");
        var rng = new Random(seed); // Deterministic host PRNG for reproducible layouts.
        var points = new List<(double X, double Z)>();
        var angle = Dbl(args, "angleDegrees", 0) * Math.PI / 180;
        for (var tries = 0; points.Count < count && tries < count * 200; tries++) // Host rejection-sampling bound.
        {
            (double X, double Z) p = shape switch
            {
                "ring" => ((Func<(double, double)>)(() => { var a = rng.NextDouble() * 2 * Math.PI; var rr = radius * (0.8 + 0.2 * rng.NextDouble()); return (x + rr * Math.Cos(a), z + rr * Math.Sin(a)); }))(),
                "line" => ((Func<(double, double)>)(() => { var t = (rng.NextDouble() * 2 - 1) * radius; var j = (rng.NextDouble() * 2 - 1) * minSpacing * 0.5; return (x + t * Math.Cos(angle) - j * Math.Sin(angle), z + t * Math.Sin(angle) + j * Math.Cos(angle)); }))(),
                _ => ((Func<(double, double)>)(() => { var a = rng.NextDouble() * 2 * Math.PI; var rr = radius * Math.Sqrt(rng.NextDouble()); return (x + rr * Math.Cos(a), z + rr * Math.Sin(a)); }))(),
            };
            if (points.All(q => Math.Pow(q.X - p.X, 2) + Math.Pow(q.Z - p.Z, 2) >= minSpacing * minSpacing)) points.Add(p);
        }
        if (points.Count < count) throw new ArgumentException($"Only {points.Count} of {count} points fit with minSpacing {minSpacing}; lower minSpacing or enlarge radius.");
        var items = points.Select(p => new LayoutItem(proto, player, Math.Round(p.X, 2), Math.Round(p.Z, 2))).ToArray();
        return RunLayout(game, items, args, $"{shape} cluster of {count} {proto} seed {seed}");
    }

    /// <summary>Shared preview/apply path for generated layouts (same guards as editor_apply_layout).</summary>
    object RunLayout(Game game, LayoutItem[] items, JsonElement args, string description)
    {
        var preview = LayoutPreview(args);
        var check = !args.TryGetProperty("checkFootprints", out var cf) || cf.GetBoolean();
        var footprints = check ? FootprintCore(game, items.Select((it, i) => new FootprintItem(i, it.Proto, it.X, it.Z)).ToArray(), true, 0, Dbl(args, "maxHeightDelta", 2)) : null;
        var plan = items.Select((it, i) => new { index = i, it.Proto, it.Player, it.X, it.Z }).ToArray();
        if (preview)
            return new { preview = true, description, plan, footprints = footprints?.Result, footprintsOk = footprints?.Ok,
                note = "Plan only; no input sent. Apply with preview=false + confirmPlacement=true (allowIssues=true to ignore footprint issues)." };
        if (footprints is { Ok: false } && !(args.TryGetProperty("allowIssues", out var ai) && ai.GetBoolean()))
            throw new WorkflowFailure("FOOTPRINT_ISSUES", "preflight", "Footprint check reported issues; nothing placed.", false, false,
                "Review preview footprints or pass allowIssues=true deliberately. No input sent.");
        var placed = new List<object>();
        string? error = null;
        foreach (var (it, i) in items.Select((it, i) => (it, i)))
        {
            try
            {
                var r = PlaceWorld(game, it.Proto, it.Player, it.X, it.Z, true, Dbl(args, "tolerance", 4));
                placed.Add(new { index = i, unit = r.Unit, positionError = r.Error });
            }
            catch (Exception e) { error = e is WorkflowFailure w ? $"{w.Code}: {w.Message}" : e.Message; break; }
        }
        return new { preview = false, description, placedCount = placed.Count, stoppedOnError = error is not null, error, placed, plan, atomic = false,
            limitation = "Sequential world placements (editor_place_at_world pipeline). Earlier placements remain on error; no retry/rollback. Headings are not mirrored (use editor_transform_unit). Not saved." };
    }

    // ---------------- specs ----------------

    static IEnumerable<object> WorldExtras()
    {
        var coord = new { type = "number", minimum = -LiveUnits.MaxCoordinate, maximum = LiveUnits.MaxCoordinate };
        var player = new { type = "integer", minimum = 0, maximum = LiveUnits.MaxPlayer };
        var point = new { type = "array", minItems = 2, maxItems = 2, items = coord };
        var area = new Dictionary<string, object> { ["minX"] = coord, ["minZ"] = coord, ["maxX"] = coord, ["maxZ"] = coord };
        yield return Spec("editor_terrain_info",
            "Read live terrain per tile: texture name/group, water body, node height and coarse passability class (land/impassable/water/ice) at up to 64 points [[x,z],...] and/or histogram over an area (minX/minZ/maxX/maxZ, world units; includeGrid adds a ≤48×48 legend-coded grid). Read-only memory reads.",
            new Dictionary<string, object>(area) { ["points"] = new { type = "array", minItems = 1, maxItems = 64, items = point }, ["includeGrid"] = new { type = "boolean" } },
            [], true);
        yield return Spec("editor_live_players",
            "Read live players without a checkpoint: display name, team, civ (major god) id+name, age index, directed diplomacy (1 ally, 2 enemy, 3 neutral). Optional player filter. Resources/minor gods not available live (use editor_players on a checkpoint). Read-only.",
            new Dictionary<string, object> { ["player"] = player }, [], true);
        yield return Spec("editor_edit_mode",
            "Read/change the editor tool mode. No args: current edit mode (from memory), active UI kind (normal/alternative via pixel gates + profile hint), open bottom panels, current texture/water/forest/cliff paint selections. exit=true leaves any tool (loops editMode None until mode 0; closes palettes). mode=<name> enters a tool and verifies.",
            new Dictionary<string, object> { ["mode"] = new { type = "string", @enum = LiveWorld.Modes.Select(m => m.Name).ToArray() }, ["exit"] = new { type = "boolean" } }, []);
        yield return Spec("editor_paint_world",
            "Paint along WORLD points (1..64 [x,z], one point = dab, stroke follows the polyline): kind texture|mix|water|forest|cliff with exact type name (editor_terrain_catalog; case/space-insensitive accepted). Exits current tool, enters the paint tool, selects the type (native setter verified from memory; textures by sampling a visible tile or normal-UI palette OCR; mixes via palette OCR), moves camera if needed, drags with the current brush, exits tool (keepMode=false), then verifies tiles/water/heights/objects changed. confirmDestructive=true required (overwrites terrain; forest tool may remove objects).",
            new Dictionary<string, object>
            {
                ["kind"] = new { type = "string", @enum = PaintKinds }, ["type"] = new { type = "string" },
                ["points"] = new { type = "array", minItems = 1, maxItems = 64, items = point },
                ["moveCamera"] = new { type = "boolean" }, ["keepMode"] = new { type = "boolean" },
                ["durationMs"] = new { type = "integer", minimum = 100, maximum = 5000 }, ["confirmDestructive"] = new { type = "boolean" },
            }, PaintRequired);
        yield return Spec("editor_elevation",
            "Deterministic elevation over a world rectangle: operation=set (exact height via bump+sample closed loop; area ≥7×7 nodes), flatten (to height sampled at referenceX/referenceZ, default area center), smooth (smooth tool stroke). Serpentine stroke inset by the brush radius; verifies node heights (tolerance default 0.05) and reports nodes changed just outside. confirmDestructive=true required. Undo with editor_undo.",
            new Dictionary<string, object>(area)
            {
                ["operation"] = new { type = "string", @enum = ElevationOperations }, ["height"] = new { type = "number", minimum = -100, maximum = 200 },
                ["referenceX"] = coord, ["referenceZ"] = coord, ["tolerance"] = new { type = "number", minimum = 0.001, maximum = 10 },
                ["moveCamera"] = new { type = "boolean" }, ["durationMs"] = new { type = "integer", minimum = 100, maximum = 5000 },
                ["confirmDestructive"] = new { type = "boolean" },
            }, ElevationRequired);
        yield return Spec("editor_transform_unit",
            "Move and/or rotate one existing object by exact {unitId, proto, player}: x/z moves it (exact selection + moveunit tool drag, verified live position within tolerance, default 1); heading sets absolute Y heading in degrees (180 = editor default facing), quantized to native 22.5° rotate steps, verified from the live transform. Camera may move.",
            new Dictionary<string, object>
            {
                ["unitId"] = new { type = "integer", minimum = 0 }, ["proto"] = new { type = "string" }, ["player"] = player,
                ["x"] = coord, ["z"] = coord, ["heading"] = new { type = "number", minimum = -3600, maximum = 3600 },
                ["tolerance"] = new { type = "number", minimum = 0.1, maximum = 50 }, ["moveCamera"] = new { type = "boolean" },
                ["durationMs"] = new { type = "integer", minimum = 100, maximum = 5000 },
            }, TransformRequired);
        yield return Spec("editor_terrain_catalog",
            "List exact names the editor accepts: textures (with group/type/subtype), water, forest, cliff, lighting (uiApplyLightingSet index→name), civs (civ id→major god), mixes (titles, palette order), editModes. Live kinds read the running game's loaded definitions. filter substring, offset/limit (≤500). Read-only.",
            new Dictionary<string, object>
            {
                ["kind"] = new { type = "string", @enum = TerrainCatalogKinds }, ["filter"] = new { type = "string" },
                ["offset"] = new { type = "integer", minimum = 0 }, ["limit"] = new { type = "integer", minimum = 1, maximum = 500 },
            }, ["kind"], true);
        yield return Spec("editor_camera_frame",
            "Center the camera on a world rectangle (minimap closed loop) and report whether all corners are visible and clickable clear of UI panels (fits). Camera only.",
            new Dictionary<string, object>(area) { ["tolerance"] = new { type = "number", minimum = 0.5, maximum = 50 } }, WorldAreaRequired);
        yield return Spec("editor_overview",
            "Screenshot annotated with live unit IDs (yellow origin cross + white ID label) for visible objects, plus a JSON legend (id, proto, player, pixel, position, heading). Filters: player, proto substring; maxLabels 1..300 (default 80); maxWidth 320..2560 (default 1280); labelScale 1..4. Read-only.",
            new Dictionary<string, object>
            {
                ["player"] = player, ["proto"] = new { type = "string" }, ["maxLabels"] = new { type = "integer", minimum = 1, maximum = 300 },
                ["maxWidth"] = new { type = "integer", minimum = 320, maximum = 2560 }, ["labelScale"] = new { type = "integer", minimum = 1, maximum = 4 },
            }, [], true);
        yield return Spec("editor_resource_balance",
            "Per-player resource report: Gaia resource objects/huntables within radii (default [30,60] world units) of each player's TownCenter (or explicit centers [{player,x,z}]): counts, static initial amounts, nearest distance, protos per resource. Read-only.",
            new Dictionary<string, object>
            {
                ["radii"] = new { type = "array", minItems = 1, maxItems = 5, items = new { type = "number", minimum = 1, maximum = 1000 } },
                ["centers"] = new { type = "array", minItems = 1, maxItems = 12, items = new { type = "object",
                    properties = new Dictionary<string, object> { ["player"] = player, ["x"] = coord, ["z"] = coord }, required = CenterRequired, additionalProperties = false } },
            }, [], true);
        yield return Spec("editor_mirror_units",
            "Mirror a player's objects (optionally only within x/z/radius; includeGaiaNear also copies Gaia objects there) to targetPlayer using map symmetry mode point (180° about map center), flipX, flipZ or swapXZ. Preview by default (plan + footprint check); preview=false + confirmPlacement=true places via the world-placement pipeline (≤32 objects). Headings not mirrored.",
            new Dictionary<string, object>
            {
                ["sourcePlayer"] = player, ["targetPlayer"] = player, ["mode"] = new { type = "string", @enum = MirrorModes },
                ["x"] = coord, ["z"] = coord, ["radius"] = new { type = "number", minimum = 0, maximum = 2000 }, ["includeGaiaNear"] = new { type = "boolean" },
                ["preview"] = new { type = "boolean" }, ["confirmPlacement"] = new { type = "boolean" }, ["allowIssues"] = new { type = "boolean" },
                ["checkFootprints"] = new { type = "boolean" }, ["maxHeightDelta"] = new { type = "number", minimum = 0, maximum = 1000 },
                ["tolerance"] = new { type = "number", minimum = 0.5, maximum = 50 },
            }, MirrorRequired);
        yield return Spec("editor_scatter",
            "Generate a deterministic cluster of 1..32 objects (e.g. gold mines, trees, berry bushes, hunt) around x/z: shape disc|ring|line (line uses angleDegrees), radius, minSpacing, seed, player (default 0 Gaia). Preview by default with footprint check; preview=false + confirmPlacement=true places via the world-placement pipeline. Large forests: prefer editor_paint_world kind=forest.",
            new Dictionary<string, object>
            {
                ["proto"] = new { type = "string" }, ["count"] = new { type = "integer", minimum = 1, maximum = SceneGeometry.MaxLayoutItems },
                ["x"] = coord, ["z"] = coord, ["radius"] = new { type = "number", minimum = 0.5, maximum = 500 },
                ["shape"] = new { type = "string", @enum = ScatterShapes }, ["minSpacing"] = new { type = "number", minimum = 0.1, maximum = 100 },
                ["angleDegrees"] = new { type = "number", minimum = -360, maximum = 360 }, ["seed"] = new { type = "integer" }, ["player"] = player,
                ["preview"] = new { type = "boolean" }, ["confirmPlacement"] = new { type = "boolean" }, ["allowIssues"] = new { type = "boolean" },
                ["checkFootprints"] = new { type = "boolean" }, ["maxHeightDelta"] = new { type = "number", minimum = 0, maximum = 1000 },
                ["tolerance"] = new { type = "number", minimum = 0.5, maximum = 50 },
            }, ScatterRequired);
    }
}
