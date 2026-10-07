using System.Numerics;
using System.Text.Json;

namespace AomMcp;

/// <summary>World-space scene helpers: camera targeting, world placement, scene diffs/summaries, guarded deletion, footprints, terrain and layouts.</summary>
internal sealed partial class Server
{
    static readonly HashSet<string> SceneNames = new(StringComparer.Ordinal)
    {
        "editor_camera_look_at", "editor_view_info", "editor_ui_state", "editor_place_at_world", "editor_units_snapshot",
        "editor_units_diff", "editor_scene_summary", "editor_delete_units", "editor_check_footprints", "editor_terrain_grid",
        "editor_apply_layout",
    };
    // Host snapshot retention: bounded in-memory diff baselines per host process, never persisted.
    const int MaxSnapshots = 16;
    readonly Dictionary<string, (DateTime At, uint Pid, LiveUnits.Unit[] Units, string? Label)> _snapshots = new(StringComparer.Ordinal);
    readonly Queue<string> _snapshotOrder = new();
    int _snapshotCounter;
    static readonly string[] LayoutItemRequired = ["proto", "x", "z"];
    static readonly string[] FormationRequired = ["proto", "shape", "count", "spacing", "x", "z"];
    static readonly string[] DeleteRequired = ["unitId", "proto", "player"];
    static readonly string[] TerrainRequired = ["minX", "minZ", "maxX", "maxZ"];

    /// <summary>Simple tools whose bridge refusal means nothing affecting the scene was dispatched.</summary>
    bool SingleCommandTool(string name) => name is "editor_place_unit" or "editor_place_at_world"
        || (name.StartsWith("editor_", StringComparison.Ordinal) && _catalog.Commands.ContainsKey(name[7..]));

    static object RefusalDetails(BridgeRefusal refusal, string name) => new
    {
        code = "BRIDGE_REFUSED",
        phase = "dispatch",
        message = refusal.Message,
        refusalCode = refusal.Code,
        refusedCommand = refusal.Command,
        nativeDispatched = false,
        outcomeUnknown = false,
        retrySafe = true,
        stagingPath = (string?)null,
        destinationPath = (string?)null,
        nextAction = refusal.Code == 10
            ? "Pointer was not over the map (off-map, map edge or UI panel). Refused command did not run; scene unchanged. Choose an on-map point (editor_view_info / editor_place_at_world) and retry."
            : name is "editor_place_unit" or "editor_place_at_world"
                ? "Bridge refused before the placement command; preparatory cursor commands ran and were cleaned up, scene unchanged. Fix the reported precondition, then retry."
                : "Bridge refused before dispatch; nothing ran. Fix the reported precondition, then retry.",
    };

    /// <summary>Close catalog prototype names for an unknown/misspelled name; empty when metadata unavailable.</summary>
    string[] ProtoSuggestions(string proto)
    {
        try { return GameDataCatalog.Suggest(proto, (_gameData ??= new GameDataCatalog()).Footprints(exe, _layout.ExeSha256).Keys); }
        catch (Exception e) when (e is WorkflowFailure or FileNotFoundException or InvalidDataException or JsonException) { return []; }
    }

    /// <summary>Exact catalog footprint lookup; null catalog means metadata missing/stale (checks skipped, reported).</summary>
    Dictionary<string, (string Name, float? X, float? Z)>? TryFootprints(out string? reason)
    {
        try { reason = null; return (_gameData ??= new GameDataCatalog()).Footprints(exe, _layout.ExeSha256); }
        catch (Exception e) when (e is WorkflowFailure or FileNotFoundException or InvalidDataException or JsonException) { reason = e.Message; return null; }
    }

    void RequireKnownProto(string proto)
    {
        var catalog = TryFootprints(out _);
        if (catalog is null || catalog.ContainsKey(proto)) return;
        var suggestions = GameDataCatalog.Suggest(proto, catalog.Keys);
        throw new WorkflowFailure("UNKNOWN_PROTO", "preflight", $"Prototype '{proto}' not in shipped catalog. Suggestions: {(suggestions.Length == 0 ? "none" : string.Join(", ", suggestions))}.",
            false, false, "Use an exact suggested name (editor_prototypes / editor_pantheon). Nothing was sent to the game.");
    }

    void PreflightScene(string name, JsonElement args)
    {
        switch (name)
        {
            case "editor_place_at_world":
                RequireKnownProto(String(args, "proto"));
                break;
            case "editor_delete_units":
                EditorFiles.Confirm(args, "confirmDestructive");
                var ids = args.GetProperty("units").EnumerateArray().Select(u => u.GetProperty("unitId").GetInt32()).ToArray();
                if (ids.Distinct().Count() != ids.Length) throw new ArgumentException("Duplicate unitId in delete request.");
                break;
            case "editor_terrain_grid":
                if (args.GetProperty("minX").GetDouble() >= args.GetProperty("maxX").GetDouble()
                    || args.GetProperty("minZ").GetDouble() >= args.GetProperty("maxZ").GetDouble())
                    throw new ArgumentException("Terrain area requires minX<maxX and minZ<maxZ.");
                if (args.TryGetProperty("maxDelta", out _) && !args.TryGetProperty("flatSize", out _))
                    throw new ArgumentException("maxDelta requires flatSize (flat-site search).");
                break;
            case "editor_apply_layout":
                var items = LayoutItems(args);
                foreach (var proto in items.Select(i => i.Proto).Distinct(StringComparer.OrdinalIgnoreCase)) RequireKnownProto(proto);
                if (!LayoutPreview(args)) EditorFiles.Confirm(args, "confirmPlacement");
                break;
            case "editor_check_footprints":
                if (args.GetProperty("items").GetArrayLength() > SceneGeometry.MaxFootprintItems) throw new ArgumentException("At most 64 footprint items.");
                break;
        }
    }

    object InvokeScene(string name, JsonElement args, Game? batchGame)
    {
        if (_layout.Units is null || _layout.Map is null)
            throw new InvalidDataException("Live unit/map read layout unavailable for this build; scene helpers refuse. Offsets are never guessed.");
        using var owned = batchGame is null ? new Game(exe, _layout, pid) : null;
        var game = batchGame ?? owned!;
        return name switch
        {
            "editor_camera_look_at" => CameraLookAt(game, args.GetProperty("x").GetDouble(), args.GetProperty("z").GetDouble(),
                Dbl(args, "tolerance", 3), Int(args, "maxClicks", 4)),
            "editor_view_info" => ViewInfo(game, !args.TryGetProperty("probeUi", out var probe) || probe.GetBoolean()),
            "editor_ui_state" => UiState(game),
            "editor_place_at_world" => PlaceAtWorld(game, String(args, "proto"), Int(args, "player", 1), args.GetProperty("x").GetDouble(),
                args.GetProperty("z").GetDouble(), !args.TryGetProperty("moveCamera", out var mc) || mc.GetBoolean(), Dbl(args, "tolerance", 4),
                args.TryGetProperty("heading", out var heading) ? heading.GetDouble() : null),
            "editor_units_snapshot" => Snapshot(game, args.TryGetProperty("label", out var label) ? label.GetString() : null),
            "editor_units_diff" => UnitsDiff(game, args),
            "editor_scene_summary" => SceneSummary(game, args),
            "editor_delete_units" => DeleteUnits(game, args),
            "editor_check_footprints" => CheckFootprints(game, args),
            "editor_terrain_grid" => TerrainGrid(game, args),
            "editor_apply_layout" => ApplyLayout(game, args),
            _ => throw new ArgumentException("Unknown scene tool."),
        };
    }

    static WorkflowFailure OutsideMap(EditorView.ViewState view) => new("TARGET_OUTSIDE_MAP", "preflight",
        $"Target outside map 0..{view.WorldWidth} x 0..{view.WorldDepth} (world X/Z).", false, false, "Choose world X/Z inside the map; no input sent.");

    static double Dbl(JsonElement args, string name, double fallback) => args.TryGetProperty(name, out var v) ? v.GetDouble() : fallback;
    static object P(Vector3 v) => new { x = v.X, y = v.Y, z = v.Z };

    void RequireNoCursor(Game game)
    {
        var editor = game.Editor();
        if (unchecked((int)game.UInt(editor + (int)_layout.ProtoOffset)) != -1)
            throw new WorkflowFailure("PLACEMENT_CURSOR_ACTIVE", "preflight", "An object placement cursor is active; minimap/scene clicks could place objects.",
                false, false, "Press ESC or call editor_uiClearSelection/uiClearCursor via editor_place_unit cleanup, verify editor_ui_state, then retry. No input sent.");
    }

    // ---------------- camera ----------------

    object CameraLookAt(Game game, double x, double z, double tolerance, int maxClicks)
    {
        var view = EditorView.ReadView(game);
        if (!view.InsideMap(x, z)) throw OutsideMap(view);
        var before = view.Target();
        double Error(Vector3? t) => t is { } p ? Math.Sqrt(Math.Pow(p.X - x, 2) + Math.Pow(p.Z - z, 2)) : double.PositiveInfinity;
        if (Error(before) <= tolerance)
            return new { requested = new { x, z }, before = before is { } b0 ? P(b0) : null, after = before is { } b1 ? P(b1) : null,
                residual = Error(before), withinTolerance = true, clicks = Array.Empty<object>(), cameraOnly = true,
                note = "Camera already centered within tolerance; no input sent." };
        RequireNoCursor(game);
        var ui = SceneUi.TryLoad(game, out var reason) ?? throw new WorkflowFailure("UI_LAYOUT_UNREVIEWED", "ui-preflight",
            $"Minimap camera targeting needs known UI geometry (normal or alternative; {UiLayouts.SupportedText}): " + reason, false, false,
            "Center camera with editor_uiLookAtUnit on a nearby unit, or pan manually; no input sent.");
        if (!ui.MinimapVisible(SceneUi.Capture(game)))
            throw new WorkflowFailure("MINIMAP_NOT_VISIBLE", "ui-preflight", "Minimap chrome pixel gate failed (hidden, covered by dialog, or different UI).",
                false, false, "Close dialogs/menus and take editor_screenshot; no input sent.");
        double width = view.WorldWidth, depth = view.WorldDepth;
        var (px, py) = SceneGeometry.MinimapPixel(x, z, width, depth, ui.MinimapX, ui.MinimapY, ui.MinimapHalf);
        var clicks = new List<object>();
        var current = before;
        (int X, int Y)? last = null;
        for (var i = 0; i < maxClicks; i++)
        {
            var pixel = SceneGeometry.ClampDiamond(px, py, ui.MinimapX, ui.MinimapY, ui.MinimapHalf, 3); // 3 px inside chrome border.
            if (last == pixel) break; // Correction below pixel resolution or clamped at camera bounds.
            last = pixel;
            Ui.Click(game, pixel.X, pixel.Y, "left");
            var settled = WaitCamera(game);
            var previous = current;
            current = settled.Target();
            clicks.Add(new { pixel = new { x = pixel.X, y = pixel.Y }, target = current is { } t ? P(t) : null, residual = Error(current) });
            if (i == 0 && previous is { } p0 && current is { } p1 && Vector3.Distance(p0, p1) < 0.25f && Error(previous) > 5)
                throw new WorkflowFailure("MINIMAP_NO_EFFECT", "ui-observe", "Minimap click did not move the camera.", false, true,
                    "Take editor_screenshot to inspect; one left click was sent to the minimap area. Do not repeat blindly.");
            if (Error(current) <= tolerance || current is null) break;
            var (dx, dy) = SceneGeometry.MinimapDelta(x - current.Value.X, z - current.Value.Z, width, depth, ui.MinimapHalf);
            px = pixel.X + dx; py = pixel.Y + dy;
        }
        var residual = Error(current);
        return new
        {
            requested = new { x, z }, before = before is { } b ? P(b) : null, after = current is { } a ? P(a) : null,
            residual, withinTolerance = residual <= tolerance, clicks, cameraOnly = true,
            note = residual <= tolerance ? "Camera center (forward ray on quantized terrain) verified by live camera read."
                : "Residual above tolerance: camera bounds near map edges/minimap pixel resolution limit. Projection of target still usable if visible.",
            limitation = ui.Reviewed ? "Left clicks on the reviewed minimap (normal or alternative UI); no scene mutation intended. Camera center measured from live pose, not occlusion proof."
                : "Left clicks on a derived (scaled, not live-reviewed at this size) minimap; no scene mutation intended. Camera center measured from live pose, not occlusion proof.",
        };
    }

    static EditorView.ViewState WaitCamera(Game game)
    {
        // Host settle policy: camera snaps on following frames; poll until two reads 60 ms apart agree (max ~1.5 s).
        Thread.Sleep(120);
        var previous = EditorView.ReadView(game);
        for (var i = 0; i < 20; i++)
        {
            Thread.Sleep(60);
            var next = EditorView.ReadView(game);
            if (next.Pose.Position == previous.Pose.Position && next.Pose.Forward == previous.Pose.Forward) return next;
            previous = next;
        }
        return previous;
    }

    // ---------------- view/ui state ----------------

    static object ViewInfo(Game game, bool probeUi)
    {
        var view = EditorView.ReadView(game);
        var vp = view.Viewport;
        var samples = new (string Name, float X, float Y)[]
        {
            ("topLeft", vp.X, vp.Y), ("top", vp.X + vp.Width / 2f, vp.Y), ("topRight", vp.X + vp.Width - 1, vp.Y),
            ("right", vp.X + vp.Width - 1, vp.Y + vp.Height / 2f), ("bottomRight", vp.X + vp.Width - 1, vp.Y + vp.Height - 1),
            ("bottom", vp.X + vp.Width / 2f, vp.Y + vp.Height - 1), ("bottomLeft", vp.X, vp.Y + vp.Height - 1), ("left", vp.X, vp.Y + vp.Height / 2f),
        };
        var ground = samples.Select(s => (s.Name, Hit: view.GroundHit(view.RayAt(s.X, s.Y)))).ToArray();
        var hits = ground.Where(g => g.Hit is not null).Select(g => g.Hit!.Value).ToArray();
        var target = view.Target();
        object? bounds = hits.Length == 0 ? null : new
        {
            minX = Math.Clamp(hits.Min(h => h.X), 0, view.WorldWidth), maxX = Math.Clamp(hits.Max(h => h.X), 0, view.WorldWidth),
            minZ = Math.Clamp(hits.Min(h => h.Z), 0, view.WorldDepth), maxZ = Math.Clamp(hits.Max(h => h.Z), 0, view.WorldDepth),
        };
        var frame = SceneUi.Capture(game);
        var ui = SceneUi.TryLoad(game, frame, out var reason);
        SceneUi.Area[]? occluders = null;
        if (ui is not null) occluders = ui.Occluders(probeUi ? frame : null);
        view.Verify();
        return new
        {
            pid = game.Pid, buildHash = game.Layout.ExeSha256,
            map = new { worldWidth = view.WorldWidth, worldDepth = view.WorldDepth, tilesX = view.Tiles[0], tilesZ = view.Tiles[1], tileWorldSize = view.Scale },
            camera = new { position = P(view.Pose.Position), target = target is { } t ? P(t) : null },
            viewport = vp,
            groundCorners = ground.Select(g => new { g.Name, hit = g.Hit is { } h ? P(h) : null, insideMap = g.Hit is { } i && view.InsideMap(i.X, i.Z) }).ToArray(),
            visibleGroundBounds = bounds,
            uiKind = ui?.Kind ?? "unknown",
            uiGeometry = ui is null ? "unreviewed: " + reason : probeUi ? ui.Describe + ", pixel-gated" : ui.Describe + ", conditional panels assumed active (probeUi=false)",
            layoutReviewed = ui?.Reviewed,
            occluders = occluders?.Select(o => new { o.Name, rect = new[] { o.X, o.Y, o.W, o.H }, o.Conditional, active = o.Active }).ToArray(),
            safeClickRule = ui is null ? "Unreviewed UI: only central 40%x50% client region trusted for map clicks."
                : $"Map clicks must be ≥{ui.SafeMargin}px from client edges and active/unknown occluders.",
            limitation = "Corner rays intersect quantized terrain node heights (not rendered collision); hills can hide ground. Visible bounds approximate the frustum footprint, not occlusion. UI kind detection always captures one screenshot (focuses game); probeUi=false only skips panel gates.",
        };
    }

    object UiState(Game game)
    {
        var editor = game.Editor();
        Win.Check(Win.GetClientRect(game.Window, out var rect), "GetClientRect");
        var placementProto = unchecked((int)game.UInt(editor + (int)_layout.ProtoOffset));
        var placementPlayer = unchecked((int)game.UInt(editor + (int)_layout.PlayerOffset));
        int? selected = null;
        if (_layout.Selection is not null) selected = EditorView.ReadSelection(game).Length;
        var view = EditorView.ReadView(game);
        var target = view.Target();
        var frame = SceneUi.Capture(game);
        var ui = SceneUi.TryLoad(game, frame, out var reason);
        int? editMode = _layout.World is null ? null : LiveWorld.EditMode(game);
        object? panels = null;
        if (ui is not null)
        {
            panels = new
            {
                minimapVisible = ui.MinimapVisible(frame),
                conditional = ui.ConditionalGates(frame),
            };
        }
        return new
        {
            pid = game.Pid, editor = true, foreground = Win.GetForegroundWindow() == game.Window,
            client = new { width = rect.Right, height = rect.Bottom },
            placementCursorActive = placementProto != -1, placementProtoId = placementProto, placementPlayer,
            selectedCount = selected,
            cameraTarget = target is { } t ? P(t) : null,
            editMode = editMode is { } m ? LiveWorld.ModeName(m) ?? $"unknown({m})" : null,
            uiKind = ui?.Kind ?? "unknown", profileAlternativeUiHint = SceneUi.ProfileAlternativeHint(),
            panels, uiGeometry = ui is null ? "unreviewed: " + reason : ui.Describe, layoutReviewed = ui?.Reviewed,
            limitation = "Placement cursor/selection read from memory; panel flags from pixel gates on a fresh screenshot (focuses game). Gate failure means not detected, not proof of absence. Dialogs/menus outside reviewed gates are not enumerated.",
        };
    }

    // ---------------- placement ----------------

    sealed record WorldPlacement(LiveUnits.Unit Unit, (int X, int Y) Pixel, double Error, bool CameraMoved, object? Camera);

    WorldPlacement PlaceWorld(Game game, string proto, int player, double x, double z, bool moveCamera, double tolerance)
    {
        if (player is < 0 or > LiveUnits.MaxPlayer) throw new ArgumentException("Player 0..12 required.");
        RequireNoCursor(game);
        var view = EditorView.ReadView(game);
        if (!view.InsideMap(x, z)) throw OutsideMap(view);
        var ui = SceneUi.TryLoad(game, out _);
        var (width, height) = Ui.ClientSize(game);
        bool Clickable(EditorView.ViewState v, out EditorView.Projected p, SceneUi.Area[]? occ)
        {
            var h = v.Height(x, z).Height;
            p = v.Project(new Vector3((float)x, h, (float)z));
            var q = p;
            if (!q.VisibleInViewport) return false;
            if (!(ui is null ? SceneUi.CentralRegion(q.X, q.Y, width, height) : ui.Clear(occ!, q.X, q.Y, width, height))) return false;
            // Back-project the pixel: terrain/elevation must not redirect the click elsewhere.
            var hit = v.GroundHit(v.RayAt(q.X, q.Y));
            return hit is { } g && Math.Sqrt(Math.Pow(g.X - x, 2) + Math.Pow(g.Z - z, 2)) <= Math.Max(1, tolerance / 2);
        }
        // Normal UI: PlaceUnit mode opens the bottom tool panel + object list palette, so treat those conditional
        // panels as active even if closed now (a click there placed objects hidden under the palette).
        SceneUi.Area[]? Occ() => ui is null ? null : ui.Kind == "normal" ? ui.Occluders(null) : ui.Occluders(SceneUi.Capture(game));
        var occluders = Occ();
        object? camera = null;
        var moved = false;
        if (!Clickable(view, out var projected, occluders))
        {
            if (!moveCamera || ui is null)
                throw new WorkflowFailure("TARGET_NOT_CLICKABLE", "preflight", ui is null
                        ? "Target not in trusted central viewport region and minimap camera targeting is unavailable for this UI."
                        : "Target not visible/clear of UI panels and moveCamera=false.", false, false,
                    "Move camera (editor_camera_look_at, or editor_uiLookAtUnit near target) then retry. No input sent.");
            camera = CameraLookAt(game, x, z, Math.Max(1, tolerance), 4);
            moved = true;
            view = EditorView.ReadView(game);
            occluders = Occ();
            if (!Clickable(view, out projected, occluders))
                throw new WorkflowFailure("TARGET_NOT_CLICKABLE", "camera", "Target still not clickable after camera move (map edge, camera bounds or elevation).",
                    false, false, "Camera moved; scene unchanged. Inspect editor_view_info; choose a point further from the map edge. No placement requested.");
        }
        var pixel = ((int)Math.Round(projected.X), (int)Math.Round(projected.Y));
        var before = LiveUnits.Read(game);
        Place(game, JsonSerializer.SerializeToElement(new { proto, player, x = pixel.Item1, y = pixel.Item2 }));
        LiveUnits.Unit? unit = null;
        Exception? last = null;
        for (var i = 0; i < 10 && unit is null; i++) // Host 1 s observation window for queued creation.
        {
            try { unit = Formations.ObservedNewUnit(before, LiveUnits.Read(game), proto, player); }
            catch (InvalidDataException e) { last = e; Thread.Sleep(100); }
        }
        if (unit is null)
            throw new WorkflowFailure("PLACEMENT_NOT_OBSERVED", "observe", "Placement command returned but exactly one new matching object was not observed: " + last?.Message
                + PlacementDiagnostics(game, proto, x, z),
                true, true, "Do not retry. Inspect editor_units_diff/editor_units around target; obstruction or invalid terrain may have blocked placement.");
        var error = Math.Sqrt(Math.Pow(unit.Position.X - x, 2) + Math.Pow(unit.Position.Z - z, 2));
        return new(unit, pixel, error, moved, camera);
    }

    /// <summary>Best-effort read-only hints after a failed placement: footprint overlaps/slope and terrain class.</summary>
    string PlacementDiagnostics(Game game, string proto, double x, double z)
    {
        var hints = new List<string>();
        try
        {
            var report = JsonSerializer.SerializeToElement(FootprintCore(game, [new FootprintItem(0, proto, x, z)], true, 0, 2).Result);
            foreach (var item in report.GetProperty("items").EnumerateArray())
                foreach (var issue in item.GetProperty("issues").EnumerateArray())
                    hints.Add(issue.GetProperty("kind").GetString() + (issue.TryGetProperty("Proto", out var p) && issue.TryGetProperty("UnitId", out var id)
                        ? $" {p.GetString()}#{id.GetInt32()}" : ""));
        }
        catch (Exception e) when (e is InvalidDataException or KeyNotFoundException or InvalidOperationException or WorkflowFailure) { hints.Add("footprint check unavailable"); }
        if (_layout.World is not null)
            try
            {
                var info = JsonSerializer.SerializeToElement(LiveWorld.TerrainInfo(game, JsonSerializer.SerializeToElement(new { points = new[] { new[] { x, z } } })));
                var point = info.GetProperty("points")[0];
                hints.Add($"terrain {point.GetProperty("texture").GetString()} ({point.GetProperty("passability").GetString()})");
            }
            catch (Exception e) when (e is InvalidDataException or KeyNotFoundException or InvalidOperationException or ArgumentException) { }
        return hints.Count == 0 ? " No footprint/terrain issue detected (hidden UI panel or rule outside host checks)." : " Diagnostics: " + string.Join("; ", hints.Take(8)) + ".";
    }

    object PlaceAtWorld(Game game, string proto, int player, double x, double z, bool moveCamera, double tolerance, double? heading = null)
    {
        var placed = PlaceWorld(game, proto, player, x, z, moveCamera, tolerance);
        object? rotation = null;
        if (heading is { } h)
        {
            if (_layout.World is null || _layout.Selection is null) throw new InvalidDataException("Heading requires reviewed world/selection layouts; object placed without rotation.");
            rotation = RotateTo(game, placed.Unit.UnitId, h, true);
            placed = placed with { Unit = LiveUnits.Read(game).First(u => u.UnitId == placed.Unit.UnitId) };
        }
        return new
        {
            placed = true, requested = new { proto, player, x, z }, unit = placed.Unit,
            pixel = new { x = placed.Pixel.X, y = placed.Pixel.Y }, positionError = placed.Error,
            withinTolerance = placed.Error <= tolerance, snapped = placed.Error > 0.05, // Host threshold: >0.05 units = engine grid snap/obstruction shift.
            cameraMoved = placed.CameraMoved, camera = placed.Camera, rotation,
            note = placed.Error <= tolerance ? "Exactly one new object observed at requested world position (within tolerance). Scenario not saved."
                : "Object observed but offset from request (snapping/obstruction). Not undone; inspect and decide.",
        };
    }

    // ---------------- snapshots/diff/summary ----------------

    object Snapshot(Game game, string? label)
    {
        var units = LiveUnits.Read(game);
        var token = $"snap-{++_snapshotCounter}-{Guid.NewGuid():N}"[..20];
        _snapshots[token] = (DateTime.UtcNow, game.Pid, units, label);
        _snapshotOrder.Enqueue(token);
        while (_snapshotOrder.Count > MaxSnapshots) _snapshots.Remove(_snapshotOrder.Dequeue());
        return new
        {
            token, label, capturedAtUtc = _snapshots[token].At, pid = game.Pid, totalObjects = units.Length,
            byPlayer = units.GroupBy(u => u.Player).OrderBy(g => g.Key).ToDictionary(g => g.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), g => g.Count()),
            retention = $"In host memory only; last {MaxSnapshots} tokens kept; lost on MCP reconnect.",
        };
    }

    (DateTime At, uint Pid, LiveUnits.Unit[] Units, string? Label) RequireSnapshot(string token) =>
        _snapshots.TryGetValue(token, out var s) ? s : throw new ArgumentException("Unknown/expired snapshot token (host memory, last 16 kept, reset on reconnect).");

    object UnitsDiff(Game game, JsonElement args)
    {
        var baseline = RequireSnapshot(String(args, "token"));
        var other = args.TryGetProperty("against", out var a) ? RequireSnapshot(a.GetString()!) : (DateTime.UtcNow, game.Pid, LiveUnits.Read(game), (string?)"live");
        if (baseline.Pid != other.Item2) throw new ArgumentException("Snapshots come from different game processes; IDs not comparable.");
        var changes = SceneGeometry.Diff(baseline.Units, other.Item3, Dbl(args, "positionTolerance", 0.05));
        var offset = Int(args, "offset", 0); var limit = Int(args, "limit", LiveUnits.DefaultLimit);
        var page = changes.Skip(offset).Take(limit).Select(c => new
        {
            c.Kind, c.UnitId, c.PreviousUnitId, before = c.Before, after = c.After, moved = c.Moved,
        }).ToArray();
        return new
        {
            token = String(args, "token"), against = args.TryGetProperty("against", out _) ? a.GetString() : "live",
            counts = changes.GroupBy(c => c.Kind).ToDictionary(g => g.Key, g => g.Count()),
            total = changes.Length, offset, limit,
            nextOffset = offset + page.Length < changes.Length ? (int?)(offset + page.Length) : null,
            changes = page, unchanged = changes.Length == 0,
            limitation = "Full-ID comparison of non-atomic registry reads. 'reidentified' pairs a removed and an added ID with same proto/player/position (e.g. undo/redo recreated object) — likely the same logical object, not proof. Health changes reported separately.",
        };
    }

    object SceneSummary(Game game, JsonElement args)
    {
        var units = LiveUnits.Read(game);
        if (args.TryGetProperty("player", out var p)) units = units.Where(u => u.Player == p.GetInt32()).ToArray();
        var view = EditorView.ReadView(game);
        var target = view.Target();
        var top = Int(args, "topProtos", 20);
        bool IsTownCenter(LiveUnits.Unit u) => u.Proto is { } n && n.Contains("TownCenter", StringComparison.OrdinalIgnoreCase);
        var players = units.GroupBy(u => u.Player).OrderBy(g => g.Key).Select(g => new
        {
            player = g.Key, objects = g.Count(),
            townCenters = g.Where(IsTownCenter).Select(u => new { u.UnitId, u.Proto, u.Position }).ToArray(),
            centroid = new { x = g.Average(u => u.Position.X), z = g.Average(u => u.Position.Z) },
            bounds = new { minX = g.Min(u => u.Position.X), maxX = g.Max(u => u.Position.X), minZ = g.Min(u => u.Position.Z), maxZ = g.Max(u => u.Position.Z) },
            protos = g.GroupBy(u => u.Proto ?? $"#proto{u.ProtoId}").OrderByDescending(x => x.Count()).ThenBy(x => x.Key, StringComparer.Ordinal)
                .Take(top).ToDictionary(x => x.Key, x => x.Count()),
            distinctProtos = g.Select(u => u.ProtoId).Distinct().Count(),
        }).ToArray();
        var outside = units.Count(u => !view.InsideMap(u.Position.X, u.Position.Z));
        return new
        {
            pid = game.Pid, totalObjects = units.Length,
            map = new { worldWidth = view.WorldWidth, worldDepth = view.WorldDepth, tilesX = view.Tiles[0], tilesZ = view.Tiles[1] },
            cameraTarget = target is { } t ? P(t) : null,
            players,
            playersWithoutTownCenter = players.Where(x => x.player > 0 && x.townCenters.Length == 0).Select(x => x.player).ToArray(),
            objectsOutsideMap = outside,
            unresolvedPrototypeCount = units.Count(u => u.Proto is null),
            limitation = "Live registry snapshot (non-atomic). Players without any objects are not listed (player count/settings live in checkpoints: editor_players). TownCenter detection by base proto name substring. Includes Gaia trees/resources/decorations.",
        };
    }

    // ---------------- deletion ----------------

    object DeleteUnits(Game game, JsonElement args)
    {
        if (_layout.Selection is null) throw new InvalidDataException("Selection read layout unavailable; deletion cannot verify selection and refuses.");
        RequireNoCursor(game);
        var requested = args.GetProperty("units").EnumerateArray().Select(u => (Id: u.GetProperty("unitId").GetInt32(),
            Proto: u.GetProperty("proto").GetString()!, Player: u.GetProperty("player").GetInt32())).ToArray();
        var live = LiveUnits.Read(game).ToDictionary(u => u.UnitId);
        var mismatch = requested.Where(r => !live.TryGetValue(r.Id, out var u) || u.Player != r.Player
            || !string.Equals(u.Proto, r.Proto, StringComparison.OrdinalIgnoreCase)).Select(r => r.Id).ToArray();
        if (mismatch.Length > 0)
            throw new WorkflowFailure("UNIT_TUPLE_MISMATCH", "preflight", "Requested {unitId,proto,player} tuples not all present live: " + string.Join(", ", mismatch),
                false, false, "Re-read editor_units; IDs change after reload/undo/redo. Nothing deleted, no input sent.");
        var done = new List<object>();
        string? error = null;
        foreach (var r in requested)
        {
            try
            {
                var before = LiveUnits.Read(game);
                _bridge.Execute(game, "uiClearSelection()");
                _bridge.Execute(game, $"uiLookAtAndSelectUnit({r.Id})");
                EditorView.Selection[] selection = [];
                for (var i = 0; i < 20; i++) // Host 1 s poll for queued selection.
                {
                    Thread.Sleep(50);
                    selection = EditorView.ReadSelection(game);
                    if (selection.Length == 1 && selection[0].Kind == 0 && selection[0].Id == r.Id) break;
                }
                if (!(selection.Length == 1 && selection[0].Kind == 0 && selection[0].Id == r.Id))
                    throw new WorkflowFailure("SELECTION_NOT_EXACT", "select", $"Selection is not exactly unit {r.Id}; DELETE not pressed.", true, false,
                        "Camera/selection changed only; inspect editor_inspect_selection. Nothing deleted for this unit.");
                RequireNoCursor(game);
                Ui.Press(game, "DELETE", []);
                LiveUnits.Unit[] after = [];
                for (var i = 0; i < 20; i++) // Host 2 s poll for queued deletion.
                {
                    Thread.Sleep(100);
                    after = LiveUnits.Read(game);
                    if (after.All(u => u.UnitId != r.Id)) break;
                }
                if (after.Any(u => u.UnitId == r.Id))
                    throw new WorkflowFailure("DELETE_NOT_OBSERVED", "observe", $"DELETE sent but unit {r.Id} still present.", true, true,
                        "Do not repeat. Inspect editor_ui_state/screenshot for confirmation dialog or focus loss.");
                var expected = before.Select(u => u.UnitId).Where(id => id != r.Id).ToHashSet();
                var collateral = expected.Except(after.Select(u => u.UnitId)).ToArray();
                done.Add(new { unitId = r.Id, r.Proto, r.Player, deleted = true, otherRemoved = collateral });
                if (collateral.Length > 0)
                    throw new WorkflowFailure("COLLATERAL_REMOVAL", "observe", "Other objects disappeared during deletion: " + string.Join(", ", collateral), true, false,
                        "Stop. Inspect with editor_units_diff; use editor_undo only after checking what changed.");
            }
            catch (Exception e) { error = e is WorkflowFailure w ? $"{w.Code}: {w.Message}" : e.Message; break; }
        }
        return new
        {
            requested = requested.Length, deleted = done.Count, stoppedOnError = error is not null, error, results = done,
            atomic = false,
            limitation = "Per unit: clear selection, uiLookAtAndSelectUnit (moves camera), verify exact selection from memory, DELETE key, verify ID gone and no collateral removal. Non-atomic; earlier deletions remain on error. Undo via editor_undo is possible but recreated objects get NEW IDs. Scenario not saved.",
        };
    }

    // ---------------- footprints/terrain ----------------

    sealed record FootprintItem(int Index, string Proto, double X, double Z);

    object CheckFootprints(Game game, JsonElement args)
    {
        var items = args.GetProperty("items").EnumerateArray().Select((e, i) => new FootprintItem(i, e.GetProperty("proto").GetString()!,
            e.GetProperty("x").GetDouble(), e.GetProperty("z").GetDouble())).ToArray();
        return Footprints(game, items, !args.TryGetProperty("includeExisting", out var ie) || ie.GetBoolean(), Dbl(args, "margin", 0),
            Dbl(args, "maxHeightDelta", 2));
    }

    sealed record FootprintReport(bool Ok, object Result);

    object Footprints(Game game, FootprintItem[] items, bool includeExisting, double margin, double maxHeightDelta) =>
        FootprintCore(game, items, includeExisting, margin, maxHeightDelta).Result;

    FootprintReport FootprintCore(Game game, FootprintItem[] items, bool includeExisting, double margin, double maxHeightDelta)
    {
        var catalog = TryFootprints(out var reason) ?? throw new WorkflowFailure("METADATA_UNAVAILABLE", "metadata",
            "Footprint checks need shipped prototype obstruction radii: " + reason, false, false, "Run --generate generated and restart MCP. No game input.");
        var view = EditorView.ReadView(game);
        var existing = includeExisting ? LiveUnits.Read(game) : [];
        var skippedExisting = 0;
        var obstacles = new List<(LiveUnits.Unit Unit, SceneGeometry.Rect Rect)>();
        foreach (var u in existing)
        {
            if (u.Proto is null || !catalog.TryGetValue(u.Proto, out var f) || f.X is not > 0 || f.Z is not > 0) { skippedExisting++; continue; }
            obstacles.Add((u, SceneGeometry.Rect.Around(u.Position.X, u.Position.Z, f.X.Value, f.Z.Value, 0)));
        }
        var rects = new SceneGeometry.Rect?[items.Length];
        var results = new List<object>();
        var allOk = true;
        foreach (var item in items)
        {
            var issues = new List<object>();
            float rx = 0, rz = 0;
            if (!catalog.TryGetValue(item.Proto, out var f))
                issues.Add(new { kind = "unknownProto", suggestions = GameDataCatalog.Suggest(item.Proto, catalog.Keys) });
            else { rx = f.X ?? 0; rz = f.Z ?? 0; }
            var rect = SceneGeometry.Rect.Around(item.X, item.Z, rx, rz, margin);
            rects[item.Index] = rect;
            if (!rect.Inside(view.WorldWidth, view.WorldDepth)) issues.Add(new { kind = "outsideMap" });
            for (var j = 0; j < item.Index; j++)
                if (rects[j] is { } other && rect.Overlaps(other)) issues.Add(new { kind = "overlapsPlanned", index = j });
            foreach (var o in obstacles.Where(o => rect.Overlaps(o.Rect)))
                issues.Add(new { kind = "overlapsExisting", o.Unit.UnitId, o.Unit.Proto, o.Unit.Player });
            double? lo = null, hi = null;
            if (rect.Inside(view.WorldWidth, view.WorldDepth))
            {
                int ix0 = (int)(rect.MinX * view.InverseScale), ix1 = Math.Min(view.Vertices[0] - 1, (int)Math.Ceiling(rect.MaxX * view.InverseScale));
                int iz0 = (int)(rect.MinZ * view.InverseScale), iz1 = Math.Min(view.Vertices[1] - 1, (int)Math.Ceiling(rect.MaxZ * view.InverseScale));
                for (var ix = ix0; ix <= ix1; ix++)
                {
                    var row = view.HeightRow(ix, iz0, iz1 - iz0 + 1);
                    lo = Math.Min(lo ?? double.MaxValue, row.Min()); hi = Math.Max(hi ?? double.MinValue, row.Max());
                }
                if (hi - lo > maxHeightDelta) issues.Add(new { kind = "uneven", heightDelta = hi - lo, maxHeightDelta });
            }
            allOk &= issues.Count == 0;
            results.Add(new { index = item.Index, item.Proto, item.X, item.Z, obstructionRadius = new { x = rx, z = rz },
                rect = new { rect.MinX, rect.MinZ, rect.MaxX, rect.MaxZ }, heightRange = lo is null ? null : (object)new { min = lo, max = hi }, ok = issues.Count == 0, issues });
        }
        view.Verify();
        return new(allOk, new
        {
            ok = allOk, items = results, existingConsidered = obstacles.Count, existingSkipped = skippedExisting,
            limitation = "Axis-aligned rectangles from shipped obstructionradiusx/z (rotation ignored), quantized node heights. Not the engine's placement rules (terrain type, water, build distance, player restrictions). Existing objects with unknown/zero radii skipped.",
        });
    }

    static object TerrainGrid(Game game, JsonElement args)
    {
        var view = EditorView.ReadView(game);
        double minX = Math.Max(0, args.GetProperty("minX").GetDouble()), minZ = Math.Max(0, args.GetProperty("minZ").GetDouble());
        double maxX = Math.Min(view.WorldWidth, args.GetProperty("maxX").GetDouble()), maxZ = Math.Min(view.WorldDepth, args.GetProperty("maxZ").GetDouble());
        if (minX >= maxX || minZ >= maxZ) throw new ArgumentException("Area does not intersect map.");
        int ix0 = (int)(minX * view.InverseScale), ix1 = Math.Min(view.Vertices[0] - 1, (int)(maxX * view.InverseScale));
        int iz0 = (int)(minZ * view.InverseScale), iz1 = Math.Min(view.Vertices[1] - 1, (int)(maxZ * view.InverseScale));
        int nx = ix1 - ix0 + 1, nz = iz1 - iz0 + 1;
        if ((long)nx * nz > 262_144) throw new ArgumentException("Area exceeds host bound of 512x512 height nodes.");
        var heights = Enumerable.Range(ix0, nx).Select(ix => view.HeightRow(ix, iz0, nz)).ToArray();
        // Output sampling: default node step keeps the returned grid at most 32x32 values.
        var step = args.TryGetProperty("step", out var s) ? Math.Max(1, (int)Math.Round(s.GetDouble() * view.InverseScale)) : Math.Max(1, (int)Math.Ceiling(Math.Max(nx, nz) / 32.0));
        if ((long)Math.Ceiling(nx / (double)step) * (long)Math.Ceiling(nz / (double)step) > 4096) throw new ArgumentException("Returned grid would exceed 4096 samples; increase step.");
        var rows = Enumerable.Range(0, (nx + step - 1) / step).Select(i => Enumerable.Range(0, (nz + step - 1) / step)
            .Select(j => MathF.Round(heights[i * step][j * step], 3)).ToArray()).ToArray();
        var all = heights.SelectMany(r => r).ToArray();
        object? flats = null;
        if (args.TryGetProperty("flatSize", out var fs))
        {
            var window = Math.Max(2, (int)Math.Ceiling(fs.GetDouble() * view.InverseScale) + 1);
            flats = SceneGeometry.FlatSpots(heights, ix0, iz0, view.Scale, window, Dbl(args, "maxDelta", 0.5), Int(args, "maxResults", 10)) // Host default flatness 0.5 height units.
                .Select(f => new { x = f.X, z = f.Z, f.MinHeight, f.MaxHeight, f.Delta, size = fs.GetDouble() }).ToArray();
        }
        view.Verify();
        return new
        {
            area = new { minX = ix0 * view.Scale, minZ = iz0 * view.Scale, maxX = ix1 * view.Scale, maxZ = iz1 * view.Scale },
            nodeSpacing = view.Scale, sampleStepNodes = step, sampleSpacing = step * view.Scale,
            layout = "heights[i][j] = node at x = area.minX + i*sampleSpacing, z = area.minZ + j*sampleSpacing",
            heights = rows,
            stats = new { min = all.Min(), max = all.Max(), mean = all.Average(v => (double)v) },
            flatSpots = flats,
            limitation = "Native quantized node heights (same as editor_map_info terrainAt), not terrain type, passability, water or cliffs. Flat spots are non-overlapping square windows with max-min ≤ maxDelta, nearest region center first.",
        };
    }

    // ---------------- layouts ----------------

    sealed record LayoutItem(string Proto, int Player, double X, double Z);

    static bool LayoutPreview(JsonElement args) => !args.TryGetProperty("preview", out var p) || p.GetBoolean();

    static LayoutItem[] LayoutItems(JsonElement args)
    {
        var hasItems = args.TryGetProperty("items", out var items);
        var hasFormation = args.TryGetProperty("formation", out var f);
        if (hasItems == hasFormation) throw new ArgumentException("Provide exactly one of items or formation.");
        LayoutItem[] result;
        if (hasItems)
            result = items.EnumerateArray().Select(i => new LayoutItem(i.GetProperty("proto").GetString()!,
                i.TryGetProperty("player", out var p) ? p.GetInt32() : 1, i.GetProperty("x").GetDouble(), i.GetProperty("z").GetDouble())).ToArray();
        else
        {
            var proto = f.GetProperty("proto").GetString()!;
            var player = f.TryGetProperty("player", out var p) ? p.GetInt32() : 1;
            result = SceneGeometry.Formation(f.GetProperty("shape").GetString()!, f.GetProperty("count").GetInt32(), f.GetProperty("spacing").GetDouble(),
                    f.GetProperty("x").GetDouble(), f.GetProperty("z").GetDouble(), f.TryGetProperty("columns", out var c) ? c.GetInt32() : null,
                    f.TryGetProperty("angleDegrees", out var a) ? a.GetDouble() : 0)
                .Select(w => new LayoutItem(proto, player, w.X, w.Z)).ToArray();
        }
        if (result.Length is < 1 or > SceneGeometry.MaxLayoutItems) throw new ArgumentException("Layout needs 1..32 items.");
        if (result.Any(i => string.IsNullOrWhiteSpace(i.Proto))) throw new ArgumentException("Empty prototype name.");
        return result;
    }

    object ApplyLayout(Game game, JsonElement args)
    {
        var items = LayoutItems(args);
        var preview = LayoutPreview(args);
        var check = !args.TryGetProperty("checkFootprints", out var cf) || cf.GetBoolean();
        var tolerance = Dbl(args, "tolerance", 4);
        FootprintReport? footprints = null;
        if (check) footprints = FootprintCore(game, items.Select((it, i) => new FootprintItem(i, it.Proto, it.X, it.Z)).ToArray(), true, 0, Dbl(args, "maxHeightDelta", 2));
        var plan = items.Select((it, i) => new { index = i, it.Proto, it.Player, it.X, it.Z }).ToArray();
        if (preview)
            return new { preview = true, plan, footprints = footprints?.Result, footprintsOk = footprints?.Ok,
                note = "Plan only; no input sent. Apply with preview=false + confirmPlacement=true." };
        if (footprints is { Ok: false } && !(args.TryGetProperty("allowIssues", out var ai) && ai.GetBoolean()))
            throw new WorkflowFailure("FOOTPRINT_ISSUES", "preflight", "Footprint check reported issues; nothing placed.", false, false,
                "Review preview footprints, adjust positions, or pass allowIssues=true deliberately. No input sent.");
        var placed = new List<object>();
        string? error = null;
        foreach (var (it, i) in items.Select((it, i) => (it, i)))
        {
            try
            {
                var r = PlaceWorld(game, it.Proto, it.Player, it.X, it.Z, true, tolerance);
                placed.Add(new { index = i, unit = r.Unit, positionError = r.Error, withinTolerance = r.Error <= tolerance, cameraMoved = r.CameraMoved });
            }
            catch (Exception e) { error = e is WorkflowFailure w ? $"{w.Code}: {w.Message}" : e.Message; break; }
        }
        return new
        {
            preview = false, attempted = Math.Min(items.Length, placed.Count + (error is null ? 0 : 1)), placedCount = placed.Count,
            stoppedOnError = error is not null, error, placed, plan, atomic = false,
            limitation = "Sequential world placements with camera moves as needed; each observes exactly one new ID. Earlier placements remain on error; no retry/rollback. Scenario not saved.",
        };
    }

    // ---------------- specs ----------------

    static IEnumerable<object> SceneExtras()
    {
        var coord = new { type = "number", minimum = -LiveUnits.MaxCoordinate, maximum = LiveUnits.MaxCoordinate };
        var player = new { type = "integer", minimum = 0, maximum = LiveUnits.MaxPlayer };
        yield return Spec("editor_camera_look_at",
            "Center editor camera on world X/Z by clicking the minimap (normal or alternative UI, auto-detected; reviewed 2560×1440/1920×1080 or derived for other 16:9 clients 1280..2560 wide, pinned build) in a closed loop: each click is verified from the live camera pose and corrected (maxClicks 1..6, default 4; tolerance world units default 3). Refuses if placement cursor active, minimap pixel gate fails, or first click has no effect. Camera only; no scene mutation. Edge targets may stay offset because of camera bounds.",
            new Dictionary<string, object> { ["x"] = coord, ["z"] = coord,
                ["tolerance"] = new { type = "number", minimum = 0.5, maximum = 50 }, ["maxClicks"] = new { type = "integer", minimum = 1, maximum = 6 } },
            ["x", "z"]);
        yield return Spec("editor_view_info",
            "Read which part of the map is visible: camera target, map size, ground hits of viewport corners/edges, visible world bounds, and UI panels covering the screen (auto-detected normal/alternative UI: static panels plus pixel-gated panels — normal: bottom tool panel/list palette; alternative: object palette/tool bar; probeUi=false skips screenshot and treats them as active). Use before pixel-based clicks. Read-only.",
            new Dictionary<string, object> { ["probeUi"] = new { type = "boolean" } }, [], true);
        yield return Spec("editor_ui_state",
            "Read editor UI state: placement cursor active/proto/player, selection count, current edit mode (memory), detected UI kind (normal/alternative via pixel gates, plus profile hint), foreground, client size, camera target, and pixel-gated panels (minimap visible; normal UI: toolPanel/objectPalette bottom panels; alternative UI: object palette/tool bar). Captures one screenshot (focuses game). Read-only.",
            [], [], true);
        yield return Spec("editor_place_at_world",
            "Place one object at WORLD X/Z (not pixels). Validates proto against shipped catalog (suggestions on typo), moves camera via minimap if target is not clearly clickable (moveCamera default true), projects terrain-height point to pixels avoiding UI panels and back-checks the ray, places with existing guarded placement, then observes exactly one new object and returns its full live unitId, actual position and positionError (tolerance default 4). Optional heading (degrees, 180 = editor default facing) rotates the new object afterwards in native 22.5° steps (verified). Does not save. Never retry on PLACEMENT_NOT_OBSERVED.",
            new Dictionary<string, object> { ["proto"] = new { type = "string" }, ["player"] = player, ["x"] = coord, ["z"] = coord,
                ["moveCamera"] = new { type = "boolean" }, ["tolerance"] = new { type = "number", minimum = 0.5, maximum = 50 },
                ["heading"] = new { type = "number", minimum = -3600, maximum = 3600 } },
            ["proto", "x", "z"]);
        yield return Spec("editor_units_snapshot",
            "Capture live object registry into host memory and return a token for editor_units_diff (last 16 kept, reset on reconnect). Optional label. Read-only.",
            new Dictionary<string, object> { ["label"] = new { type = "string", maxLength = 100 } }, [], true);
        yield return Spec("editor_units_diff",
            "Compare a snapshot token with live objects (or another token via against): added, removed, moved (> positionTolerance, default 0.05), changed owner/proto, health, and reidentified (removed+added pair with same proto/player/position — e.g. undo/redo gives new IDs). Paged offset/limit 1..200. Read-only.",
            new Dictionary<string, object> { ["token"] = new { type = "string" }, ["against"] = new { type = "string" },
                ["positionTolerance"] = new { type = "number", minimum = 0, maximum = 1000 },
                ["offset"] = new { type = "integer", minimum = 0 }, ["limit"] = new { type = "integer", minimum = 1, maximum = LiveUnits.MaxLimit } },
            ["token"], true);
        yield return Spec("editor_scene_summary",
            "Cheap scene overview: per-player object counts, top prototypes (topProtos 1..100, default 20), TownCenters with IDs, centroid/bounds, players lacking TownCenter, map size, camera target, objects outside map. Optional player filter. Read-only.",
            new Dictionary<string, object> { ["player"] = player, ["topProtos"] = new { type = "integer", minimum = 1, maximum = 100 } }, [], true);
        yield return Spec("editor_delete_units",
            "Delete 1..32 live objects by exact {unitId, proto, player} tuples (all verified live first). Per unit: clear selection, uiLookAtAndSelectUnit (moves camera), verify selection is exactly that ID from memory, DELETE key, verify ID gone and nothing else removed. Stops on first error with partial results. confirmDestructive=true required. Undo recreates objects with NEW IDs. Not saved.",
            new Dictionary<string, object>
            {
                ["units"] = new { type = "array", minItems = 1, maxItems = SceneGeometry.MaxLayoutItems, items = new
                {
                    type = "object", properties = new Dictionary<string, object> { ["unitId"] = new { type = "integer", minimum = 0 },
                        ["proto"] = new { type = "string" }, ["player"] = player },
                    required = DeleteRequired, additionalProperties = false,
                } },
                ["confirmDestructive"] = new { type = "boolean" },
            }, ["units", "confirmDestructive"]);
        var footprintItem = new
        {
            type = "object", properties = new Dictionary<string, object> { ["proto"] = new { type = "string" }, ["x"] = coord, ["z"] = coord },
            required = LayoutItemRequired, additionalProperties = false,
        };
        yield return Spec("editor_check_footprints",
            "Check planned objects (1..64 {proto,x,z}) before placing: unknown proto (with suggestions), outside map, overlap with other planned items and existing live objects (shipped obstruction radii, axis-aligned, margin default 0), and uneven ground (node height range > maxHeightDelta, default 2). includeExisting default true. Read-only; not the engine's full placement rules.",
            new Dictionary<string, object> { ["items"] = new { type = "array", minItems = 1, maxItems = SceneGeometry.MaxFootprintItems, items = footprintItem },
                ["includeExisting"] = new { type = "boolean" }, ["margin"] = new { type = "number", minimum = 0, maximum = 100 },
                ["maxHeightDelta"] = new { type = "number", minimum = 0, maximum = 1000 } },
            ["items"], true);
        yield return Spec("editor_terrain_grid",
            "Read terrain node heights over a world rectangle (≤512×512 nodes) with stats and a downsampled grid (≤4096 values; step in world units, default fits 32×32). Optional flatSize (+maxDelta, default 0.5) finds up to maxResults (default 10) non-overlapping flat square sites, nearest area center first. Read-only.",
            new Dictionary<string, object> { ["minX"] = coord, ["minZ"] = coord, ["maxX"] = coord, ["maxZ"] = coord,
                ["step"] = new { type = "number", minimum = 0.1, maximum = 10000 }, ["flatSize"] = new { type = "number", minimum = 0.1, maximum = 10000 },
                ["maxDelta"] = new { type = "number", minimum = 0, maximum = 1000 }, ["maxResults"] = new { type = "integer", minimum = 1, maximum = 50 } },
            TerrainRequired, true);
        var layoutItem = new
        {
            type = "object", properties = new Dictionary<string, object> { ["proto"] = new { type = "string" }, ["player"] = player, ["x"] = coord, ["z"] = coord },
            required = LayoutItemRequired, additionalProperties = false,
        };
        var formation = new
        {
            type = "object", properties = new Dictionary<string, object> { ["proto"] = new { type = "string" }, ["player"] = player,
                ["shape"] = new { type = "string", @enum = FormationShapes }, ["count"] = new { type = "integer", minimum = 1, maximum = SceneGeometry.MaxLayoutItems },
                ["spacing"] = new { type = "number", minimum = 0.1, maximum = 1000 }, ["x"] = coord, ["z"] = coord,
                ["columns"] = new { type = "integer", minimum = 1, maximum = SceneGeometry.MaxLayoutItems }, ["angleDegrees"] = new { type = "number", minimum = -360, maximum = 360 } },
            required = FormationRequired, additionalProperties = false,
        };
        yield return Spec("editor_apply_layout",
            "Declarative WORLD-unit placement of 1..32 objects: items [{proto,player,x,z}] or formation {proto,player,shape rows|ring,count,spacing (world units),x,z,columns,angleDegrees}. preview=true (default) returns plan + footprint check (connects read-only, no input). preview=false requires confirmPlacement=true; refuses on footprint issues unless allowIssues=true; then places each via editor_place_at_world logic (camera moves as needed), observing every new ID. Stops on first error, partial results; no retry/rollback/save.",
            new Dictionary<string, object> { ["items"] = new { type = "array", minItems = 1, maxItems = SceneGeometry.MaxLayoutItems, items = layoutItem },
                ["formation"] = formation, ["preview"] = new { type = "boolean" }, ["confirmPlacement"] = new { type = "boolean" },
                ["checkFootprints"] = new { type = "boolean" }, ["allowIssues"] = new { type = "boolean" },
                ["maxHeightDelta"] = new { type = "number", minimum = 0, maximum = 1000 }, ["tolerance"] = new { type = "number", minimum = 0.5, maximum = 50 } },
            []);
    }
}
