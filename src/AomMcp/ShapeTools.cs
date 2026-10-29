using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace AomMcp;

/// <summary>Map-building helpers (plans/map-building-tools.md): world/pixel projection, brush control, world strokes with
/// the elevation tool family, exact-height terrain shapes and read-only land connectivity analysis. Mutations use native
/// commands and UI input only; process memory is read-only.</summary>
internal sealed partial class Server
{
    /// <summary>editor_stroke tool vocabulary (host API), mapped by <see cref="StrokeMode"/>.</summary>
    static readonly string[] StrokeTools = ["raise", "lower", "smooth", "roughen", "flatten"];

    /// <summary>editor_terrain_shape shape kinds (host API).</summary>
    static readonly string[] ShapeKinds = ["polygon", "circle", "ellipse", "capsule"];

    /// <summary>Edit modes whose tool owns a brush (native editMode names, research/LIVE-WORLD.md mode table).</summary>
    static readonly string[] BrushModes =
    [
        "Paint",
        "PaintLand",
        "paintmix",
        "paintforest",
        "elevation",
        "elevationsample",
        "roughen",
        "smooth",
        "paintWater",
        "PaintCliff",
        "editGrass",
        "deleteunits",
    ];

    /// <summary>Native brush shapes (ui_editor_menu.xml items 69..71 run uiChangeBrushType with these names).</summary>
    static readonly string[] BrushShapes = ["circular", "Rectangular", "HollowSquare"];

    /// <summary>Required arguments of editor_stroke.</summary>
    static readonly string[] StrokeRequired = ["tool", "points", "confirmDestructive"];

    /// <summary>Required arguments of editor_terrain_shape.</summary>
    static readonly string[] ShapeRequired = ["shape", "height", "confirmDestructive"];

    /// <summary>Native elevation-sample mode name (research/LIVE-WORLD.md: right click samples, left drag sets).</summary>
    const string SampleMode = "elevationsample";

    /// <summary>Native water paint mode name (research/LIVE-WORLD.md mode table).</summary>
    const string WaterMode = "paintWater";

    /// <summary>Largest brush size the host requests (host bound; hotkeys set 1..10, editor.con).</summary>
    const int MaxBrushSize = 50;

    /// <summary>Brush readback poll window: a size change can apply about 1 s late after a mode switch
    /// (research/LIVE-WORLD.md "Brush size").</summary>
    const int BrushPollMs = 1500;

    /// <summary>Host readback poll step for frame-applied natives.</summary>
    const int PollStepMs = 40;

    /// <summary>Shortest editor_stroke drag: the 2026-10-20 Wake Island session saw drags under ~400 ms paint nothing and
    /// 800 ms drags paint reliably (1600×900 normal UI).</summary>
    const int MinStrokeMs = 800;

    /// <summary>Longest drag Ui.DragPath accepts (host input bound).</summary>
    const int MaxStrokeMs = 5000;

    /// <summary>Default editor_stroke duration per vertex on top of <see cref="MinStrokeMs"/> (host choice).</summary>
    const int StrokeMsPerVertex = 60;

    /// <summary>Most world points per editor_project call (host bound, same as a stroke).</summary>
    const int MaxProjectPoints = 256;

    /// <summary>Most client pixels per editor_project call (host bound).</summary>
    const int MaxProjectPixels = 64;

    /// <summary>Most passes per editor_stroke call (host bound).</summary>
    const int MaxStrokePasses = 10;

    /// <summary>Node margin around a stroke for the before/after height diff (host choice: covers brush sizes ≤10, whose
    /// disc stays within ~7 nodes, research/LIVE-WORLD.md brush measurements).</summary>
    const int StrokeMarginNodes = 12;

    /// <summary>Side of the node square that hosts the exact-height bump/sample/test-dab loop (editor_elevation set needs
    /// at least 7×7 nodes; test node ≥3 nodes from the anchor, see SetSample).</summary>
    const int ShapeSampleNodes = 7;

    /// <summary>Brush radius in nodes used to erode shapes before painting (brush ≈2.2 nodes, research/LIVE-WORLD.md).</summary>
    const double BrushErodeNodes = 2;

    /// <summary>Scanline row spacing in nodes: 2-node rows overlap with a ≈2.2-node brush (as in editor_elevation).</summary>
    const int ShapeRowStepNodes = 2;

    /// <summary>Ring width (nodes) outside a shape reported for brush bleed (as in editor_elevation).</summary>
    const int ShapeRingNodes = 3;

    /// <summary>Most polygon/capsule vertices (host bound).</summary>
    const int MaxShapeVertices = 64;

    /// <summary>Default node height tolerance (matches editor_elevation).</summary>
    const double DefaultShapeTolerance = 0.05;

    /// <summary>Approximate brush radius in world units per brush size step: the 2026-10-20 Wake Island session measured
    /// size 10 ≈ 13 world units (research/LIVE-WORLD.md). Used as the stroke pick tolerance (a hit inside the brush
    /// radius still paints the intended vertex).</summary>
    const double BrushWorldPerSize = 1.3;

    /// <summary>Stroke pick tolerance in brush radii: the dab still overlaps the target when the ray hits relief in
    /// front of it within 1.5 radii (live 2026-10-21: lowering a fresh size-3 ridge missed by 4.4 units with a 3.3 unit
    /// radius and painted nothing at 1.0). Effect is verified afterwards by the node-height diff.</summary>
    const double StrokePickRadii = 1.5;

    /// <summary>Segment sampling step (nodes) for the stroke-join inside test (host choice).</summary>
    const double JoinSampleNodes = 0.25;

    /// <summary>Default speck threshold in tiles for editor_land_analysis (host choice).</summary>
    const int DefaultSpeckTiles = 4;

    /// <summary>Default number of listed land components (host output bound).</summary>
    const int DefaultMaxComponents = 20;

    /// <summary>Most bases for editor_land_analysis (12 players, LiveUnits.MaxPlayer).</summary>
    const int MaxBases = 12;

    /// <summary>Listed specks / anomaly tiles per report (host output bound).</summary>
    const int LandListLimit = 20;

    /// <summary>Search radius (tiles) to snap a base on a non-land tile to land (host choice; TownCenter footprints are
    /// about 4 tiles wide).</summary>
    const int BaseSnapTiles = 8;

    /// <summary>Height change below which a node counts as unchanged (float noise bound, as in editor_paint_world).</summary>
    const float HeightEpsilon = 0.001f;

    /// <summary>Settle after strokes before verification reads (host choice, as in editor_elevation).</summary>
    const int StrokeSettleMs = 300;

    /// <summary>Diagonal step cost for 8-neighbour tile distances.</summary>
    static readonly double Diagonal = Math.Sqrt(2);

    /// <summary>Native edit mode and held button for an editor_stroke tool name.</summary>
    /// <param name="tool">raise, lower, smooth, roughen or flatten.</param>
    static (string Mode, string Button) StrokeMode(string tool) =>
        tool switch
        {
            // research/LIVE-WORLD.md: elevation left raises, right lowers; elevationsample left drag sets the sample.
            "raise" => ("elevation", "left"),
            "lower" => ("elevation", "right"),
            "smooth" => ("smooth", "left"),
            "roughen" => ("roughen", "left"),
            "flatten" => (SampleMode, "left"),
            _ => throw new ArgumentException("tool: raise/lower/smooth/roughen/flatten"),
        };

    /// <summary>Parses [[x,z],...] world points.</summary>
    /// <param name="e">JSON array of 2-element arrays.</param>
    static (double X, double Z)[] WorldPoints(JsonElement e) =>
        [.. e.EnumerateArray().Select(p => (p[0].GetDouble(), p[1].GetDouble()))];

    /// <summary>Preflight for the map-building tools (no game connection).</summary>
    /// <param name="name">Tool name.</param>
    /// <param name="args">Tool arguments (schema-validated).</param>
    static void PreflightShape(string name, JsonElement args)
    {
        switch (name)
        {
            case "editor_project":
                if (!args.TryGetProperty("points", out _) && !args.TryGetProperty("pixels", out _))
                    throw new ArgumentException(
                        "Provide points [[x,z],...] and/or pixels [[px,py],...]."
                    );
                break;
            case "editor_brush":
                if (
                    (args.TryGetProperty("size", out _) || args.TryGetProperty("shape", out _))
                    && !args.TryGetProperty("mode", out _)
                    && args.TryGetProperty("keepMode", out _)
                )
                    throw new ArgumentException("keepMode needs mode.");
                break;
            case "editor_stroke":
                EditorFiles.Confirm(args, "confirmDestructive");
                if (
                    args.TryGetProperty("referenceX", out _)
                    != args.TryGetProperty("referenceZ", out _)
                )
                    throw new ArgumentException("referenceX and referenceZ go together.");
                if (args.TryGetProperty("referenceX", out _) && String(args, "tool") != "flatten")
                    throw new ArgumentException(
                        "referenceX/referenceZ apply to tool=flatten only."
                    );
                if (args.TryGetProperty("height", out _) && String(args, "tool") != "flatten")
                    throw new ArgumentException("height applies to tool=flatten only.");
                if (
                    args.TryGetProperty("height", out _) && args.TryGetProperty("referenceX", out _)
                )
                    throw new ArgumentException(
                        "Give either height (exact sample made by a bump at the first vertex) or referenceX/referenceZ (sample existing terrain), not both."
                    );
                break;
            case "editor_terrain_shape":
                EditorFiles.Confirm(args, "confirmDestructive");
                _ = ParseShape(args);
                break;
            case "editor_land_analysis":
                if (
                    args.TryGetProperty("minX", out _)
                    && !WorldAreaRequired.All(k => args.TryGetProperty(k, out _))
                )
                    throw new ArgumentException("Area needs minX, minZ, maxX and maxZ together.");
                if (
                    args.TryGetProperty("minX", out _)
                    && (
                        Dbl(args, "minX", 0) >= Dbl(args, "maxX", 0)
                        || Dbl(args, "minZ", 0) >= Dbl(args, "maxZ", 0)
                    )
                )
                    throw new ArgumentException("Area requires minX<maxX and minZ<maxZ.");
                break;
        }
    }

    // ---------------- editor_project ----------------

    /// <summary>World↔pixel projection with the same clickability checks the brush tools use.</summary>
    /// <param name="game">Validated editor connection.</param>
    /// <param name="args">points and/or pixels, optional height plane.</param>
    static object Project(Game game, JsonElement args)
    {
        var view = EditorView.ReadView(game);
        var (width, height) = Ui.ClientSize(game);
        var frame = SceneUi.Capture(game);
        var ui = SceneUi.TryLoad(game, frame, out var reason);
        var occ = ui?.Occluders(frame);
        float? plane = args.TryGetProperty("height", out var he) ? (float)he.GetDouble() : null;
        string? Blocker(double px, double py)
        {
            if (ui is null)
                return SceneUi.CentralRegion(px, py, width, height)
                    ? null
                    : "outsideTrustedCentralRegion";
            var o = occ!.FirstOrDefault(a =>
                a.Active != false && a.Contains(px, py, ui.SafeMargin)
            );
            if (o is not null)
                return o.Name;
            return ui.Clear(occ!, px, py, width, height) ? null : "margin";
        }
        var points = args.TryGetProperty("points", out var pe)
            ? WorldPoints(pe)
                .Select(p =>
                {
                    var inside = view.InsideMap(p.X, p.Z);
                    float? terrain = inside ? view.Height(p.X, p.Z).Height : null;
                    var y = plane ?? terrain ?? 0;
                    var projected = view.Project(new Vector3((float)p.X, y, (float)p.Z));
                    var pixel = PixelFor(view, ui, occ, width, height, p.X, p.Z, 2, plane);
                    return new
                    {
                        world = new[] { p.X, p.Z },
                        insideMap = inside,
                        terrainHeight = terrain,
                        waterSurface = inside ? view.WaterSurface(p.X, p.Z) : null,
                        projectedAtHeight = y,
                        pixel = new[] { Math.Round(projected.X, 1), Math.Round(projected.Y, 1) },
                        inFront = projected.InFront,
                        inViewport = projected.VisibleInViewport,
                        blockedBy = projected.VisibleInViewport
                            ? Blocker(projected.X, projected.Y)
                            : null,
                        clickable = pixel is not null,
                        clickPixel = pixel is { } c ? new[] { c.X, c.Y } : null,
                        why = pixel is null
                            ? PixelWhy(view, ui, occ, width, height, p.X, p.Z, 2, plane)
                            : null,
                    };
                })
                .ToArray()
            : null;
        var pixels = args.TryGetProperty("pixels", out var xe)
            ? xe.EnumerateArray()
                .Select(p =>
                {
                    float px = (float)p[0].GetDouble(),
                        py = (float)p[1].GetDouble();
                    var hit = view.GroundHit(view.RayAt(px, py));
                    var inside = hit is { } g && view.InsideMap(g.X, g.Z);
                    return new
                    {
                        pixel = new[] { px, py },
                        ground = hit is { } h ? EditorView.XYZ(h) : null,
                        insideMap = inside,
                        waterSurface = inside ? view.WaterSurface(hit!.Value.X, hit.Value.Z) : null,
                        blockedBy = Blocker(px, py),
                    };
                })
                .ToArray()
            : null;
        var clear = ui is not null ? LargestClear(ui, occ!, width, height) : null;
        return new
        {
            points,
            pixels,
            uiKind = ui?.Kind ?? "unknown",
            uiDetection = ui is null ? reason : $"pixel gates matched {ui.File}",
            layoutReviewed = ui?.Reviewed,
            client = new[] { width, height },
            camera = PoseOf(view).Json(),
            largestClearRect = clear is { } r
                ? new[] { (int)r.X, (int)r.Y, (int)r.W, (int)r.H }
                : null,
            limitation = "Read-only. World→pixel projects at terrain height (brushes pick the terrain under water, research/LIVE-WORLD.md) or at the given height plane; clickable = same visibility, UI-panel and ray back-check the brush tools apply (PixelFor). Pixel→world ray-marches the height grid. Undetected UI falls back to the trusted central client region. Camera and pointer unchanged (one screenshot focuses the game).",
        };
    }

    // ---------------- editor_brush ----------------

    /// <summary>Sets the active tool's brush size and polls the memory readback.</summary>
    /// <param name="game">Validated editor connection with a brush tool active.</param>
    /// <param name="size">Wanted size.</param>
    /// <returns>Readback after the poll window and whether it equals the request.</returns>
    (LiveWorld.Brush? After, bool Applied) SetBrushSize(Game game, int size)
    {
        // uiChangeBrushSize(5,0,0,0) set size 5 live (research/LIVE-WORLD.md "Brush size").
        _bridge.Execute(
            game,
            string.Create(CultureInfo.InvariantCulture, $"uiChangeBrushSize({size},0,0,0)")
        );
        return PollBrush(game, b => Math.Abs(b.Size - size) < HeightEpsilon);
    }

    /// <summary>Polls <see cref="LiveWorld.ReadBrush"/> until <paramref name="done"/> holds or the poll window ends.</summary>
    /// <param name="game">Validated editor connection.</param>
    /// <param name="done">Completion test on the readback.</param>
    static (LiveWorld.Brush? After, bool Applied) PollBrush(
        Game game,
        Func<LiveWorld.Brush, bool> done
    )
    {
        var deadline = Win.GetTickCount64() + BrushPollMs;
        LiveWorld.Brush? brush;
        do
        {
            brush = LiveWorld.ReadBrush(game);
            if (brush is not null && done(brush))
                return (brush, true);
            Thread.Sleep(PollStepMs);
        } while (Win.GetTickCount64() < deadline);
        return (brush, false);
    }

    /// <summary>editor_brush: read or set the active (or given) tool's brush size and shape, verified from memory.</summary>
    /// <param name="game">Validated editor connection.</param>
    /// <param name="args">mode, size, shape, keepMode.</param>
    object BrushTool(Game game, JsonElement args)
    {
        if (game.Layout.World?.Brush is null)
            throw new InvalidDataException(
                "Live brush layout unavailable for this build; editor_brush refuses. Offsets are never guessed."
            );
        var mode = args.TryGetProperty("mode", out var me) ? me.GetString() : null;
        int? size = args.TryGetProperty("size", out var se) ? se.GetInt32() : null;
        var shape = args.TryGetProperty("shape", out var sh) ? sh.GetString() : null;
        var keepMode = args.TryGetProperty("keepMode", out var km) && km.GetBoolean();
        var modeBefore = LiveWorld.EditMode(game);
        var entered = false;
        if (mode is not null && LiveWorld.ModeName(modeBefore) != mode)
        {
            RequireNoCursor(game);
            ExitEditModes(game);
            EnterMode(game, mode);
            entered = true;
        }
        var before = LiveWorld.ReadBrush(game);
        if (before is null && (size is not null || shape is not null))
            throw new WorkflowFailure(
                "NO_BRUSH_TOOL",
                "preflight",
                $"No brush tool active (edit mode {LiveWorld.ModeName(modeBefore) ?? modeBefore.ToString(CultureInfo.InvariantCulture)}).",
                false,
                false,
                "Pass mode (e.g. elevation, Paint) so the tool is entered first. No input sent."
            );
        LiveWorld.Brush? after = before;
        bool? sizeApplied = null,
            shapeChanged = null;
        try
        {
            if (shape is not null)
            {
                _bridge.Execute(game, $"uiChangeBrushType({Catalog.Quote(shape)})");
                bool changed;
                (after, changed) = PollBrush(game, b => b.Type != before!.Type);
                shapeChanged = changed;
            }
            if (size is { } wanted)
            {
                bool applied;
                (after, applied) = SetBrushSize(game, wanted);
                sizeApplied = applied;
            }
        }
        finally
        {
            if (entered && !keepMode)
                ExitEditModes(game);
        }
        if (sizeApplied == false)
            throw new WorkflowFailure(
                "BRUSH_SIZE_NOT_APPLIED",
                "brush",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"uiChangeBrushSize({size}) readback is {after?.Size} after {BrushPollMs} ms (engine clamp or ignored)."
                ),
                true,
                false,
                "Brush only; scene unchanged. Use the reported size, or retry with a value in the engine's range."
            );
        return new
        {
            mode = mode ?? LiveWorld.ModeName(modeBefore),
            enteredMode = entered,
            before = before is null ? null : new { size = before.Size, type = before.Type },
            after = after is null ? null : new { size = after.Size, type = after.Type },
            requestedSize = size,
            sizeApplied,
            requestedShape = shape,
            shapeTypeChanged = shapeChanged,
            editModeAfter = LiveWorld.ModeName(LiveWorld.EditMode(game)),
            limitation = "Brush readback is the active tool's brush ([editor+0x540], research/LIVE-WORLD.md); each edit mode keeps its own size, so a size set here applies to that mode's tool only. type is the native shape kind int (shapeTypeChanged reports whether uiChangeBrushType changed it; an already-active shape reads unchanged). Not undoable; scene unchanged. Measured footprints: research/LIVE-WORLD.md.",
        };
    }

    // ---------------- editor_stroke ----------------

    /// <summary>Node-height snapshot of an inclusive node box.</summary>
    /// <param name="X0">First X node.</param>
    /// <param name="Z0">First Z node.</param>
    /// <param name="Heights">Heights[ix - X0][iz - Z0].</param>
    sealed record NodeBox(int X0, int Z0, float[][] Heights)
    {
        /// <summary>Height at absolute node (ix, iz), or null outside the box.</summary>
        internal float? At(int ix, int iz) =>
            ix >= X0 && ix < X0 + Heights.Length && iz >= Z0 && iz - Z0 < Heights[ix - X0].Length
                ? Heights[ix - X0][iz - Z0]
                : null;
    }

    /// <summary>Reads node heights of the box [ix0..ix1]×[iz0..iz1], clamped to the map.</summary>
    /// <param name="v">Live view.</param>
    /// <param name="ix0">First X node.</param>
    /// <param name="iz0">First Z node.</param>
    /// <param name="ix1">Last X node (inclusive).</param>
    /// <param name="iz1">Last Z node (inclusive).</param>
    static NodeBox Nodes(EditorView.ViewState v, int ix0, int iz0, int ix1, int iz1)
    {
        ix0 = Math.Max(0, ix0);
        iz0 = Math.Max(0, iz0);
        ix1 = Math.Min(v.Vertices[0] - 1, ix1);
        iz1 = Math.Min(v.Vertices[1] - 1, iz1);
        return new(
            ix0,
            iz0,
            [
                .. Enumerable
                    .Range(ix0, ix1 - ix0 + 1)
                    .Select(ix => v.HeightRow(ix, iz0, iz1 - iz0 + 1)),
            ]
        );
    }

    /// <summary>Water flags (true = tile has water) of the tiles whose lower-left node lies in the box.</summary>
    /// <param name="tiles">Live tile reader.</param>
    /// <param name="box">Node box.</param>
    static bool[,] WaterFlags(LiveWorld.Tiles tiles, NodeBox box)
    {
        var flags = new bool[box.Heights.Length, box.Heights[0].Length];
        for (var i = 0; i < flags.GetLength(0); i++)
        for (var j = 0; j < flags.GetLength(1); j++)
            if (box.X0 + i < tiles.TilesX && box.Z0 + j < tiles.TilesZ)
                flags[i, j] = tiles.WaterName(tiles.At(box.X0 + i, box.Z0 + j)) is not null;
        return flags;
    }

    /// <summary>Brush size for the exact-height sample loop of editor_stroke flatten height=: SetSample was live-tuned with
    /// a ≈2.2-2.5-node brush (research/LIVE-WORLD.md "Elevation tools"; elevation mode showed size 3 live). Larger stroke
    /// brushes would make the bump and the test dab paint far outside the stroke.</summary>
    const int SampleBrushSize = 3;

    /// <summary>Node radius around the sample anchor checked for bump/test-dab residue: test node 3 nodes from the
    /// anchor per axis (diagonal ≈4.2) plus the ≈2.5-node dab radius (SetSample).</summary>
    const double SampleResidueNodes = 7;

    /// <summary>
    /// Makes an exact elevation-sample height for flatten: both elevation tools get <see cref="SampleBrushSize"/>
    /// (originals queued in <paramref name="restore"/>, restored by the caller), then <see cref="SetSample"/> raises or
    /// lowers a bump at the anchor, bisects the slope sample and verifies a test dab, all inside a 7×7-node square.
    /// </summary>
    /// <param name="game">Validated editor connection.</param>
    /// <param name="height">Requested node height.</param>
    /// <param name="x">Anchor world X (first stroke vertex).</param>
    /// <param name="z">Anchor world Z.</param>
    /// <param name="tolerance">Accepted sample error.</param>
    /// <param name="moveCamera">Camera may move to show the anchor.</param>
    /// <param name="steps">Step log.</param>
    /// <param name="restore">Receives (mode, original size) pairs to restore afterwards.</param>
    /// <param name="strokes">Undo counter; bump and test dabs are added.</param>
    /// <returns>Sampled height now active in the elevationsample tool.</returns>
    float SampleExactHeight(
        Game game,
        double height,
        double x,
        double z,
        double tolerance,
        bool moveCamera,
        List<object> steps,
        List<(string Mode, int Size)> restore,
        ref int strokes
    )
    {
        foreach (var sampleMode in new[] { "elevation", SampleMode })
        {
            EnterMode(game, sampleMode);
            if (LiveWorld.ReadBrush(game) is not { } current)
                continue;
            var original = (int)Math.Round(current.Size);
            if (original == SampleBrushSize)
                continue;
            if (!SetBrushSize(game, SampleBrushSize).Applied)
                throw new WorkflowFailure(
                    "BRUSH_SIZE_NOT_APPLIED",
                    "brush",
                    $"{sampleMode} brush size did not change to {SampleBrushSize} for the sample loop.",
                    false,
                    false,
                    "Nothing painted. Set the brush with editor_brush and retry."
                );
            restore.Add((sampleMode, original));
        }
        var view = EditorView.ReadView(game);
        var half = (ShapeSampleNodes - 1) / 2 * view.Scale;
        _ = Screen(game, [(x, z), (x - half, z - half), (x + half, z + half)], moveCamera, 1);
        _elevationEdits = 0;
        double sampled;
        try
        {
            steps.AddRange(
                SetSample(
                    game,
                    height,
                    x,
                    z,
                    x - half,
                    z - half,
                    x + half,
                    z + half,
                    tolerance,
                    out sampled
                )
            );
        }
        finally
        {
            strokes += _elevationEdits;
        }
        steps.Add(
            new
            {
                exactSample = new
                {
                    x,
                    z,
                    requested = height,
                    sampled,
                },
            }
        );
        return (float)sampled;
    }

    /// <summary>editor_stroke: one world polyline with raise/lower/smooth/roughen/flatten, optionally mirrored.</summary>
    /// <param name="game">Validated editor connection.</param>
    /// <param name="args">Tool arguments.</param>
    object Stroke(Game game, JsonElement args)
    {
        var tool = String(args, "tool");
        var (mode, button) = StrokeMode(tool);
        var points = WorldPoints(args.GetProperty("points"));
        var passes = Int(args, "passes", 1);
        var moveCamera = !args.TryGetProperty("moveCamera", out var mc) || mc.GetBoolean();
        var duration = Int(args, "durationMs", StrokeDuration(points.Length));
        var tolerance = Dbl(args, "tolerance", DefaultShapeTolerance);
        RequireNoCursor(game);
        var view = EditorView.ReadView(game);
        var instances = new List<(string Name, (double X, double Z)[] Points)>
        {
            ("original", points),
        };
        if (args.TryGetProperty("mirror", out var mirrorElement))
        {
            var mirror = mirrorElement.GetString()!;
            instances.Add(
                (
                    mirror,
                    [
                        .. points.Select(p =>
                            MirrorPoint(mirror, p.X, p.Z, view.WorldWidth, view.WorldDepth)
                        ),
                    ]
                )
            );
        }
        foreach (var (_, pts) in instances)
        foreach (var (x, z) in pts)
            if (!view.InsideMap(x, z))
                throw OutsideMap(view);
        double rx = Dbl(args, "referenceX", points[0].X),
            rz = Dbl(args, "referenceZ", points[0].Z);
        if (tool == "flatten" && !view.InsideMap(rx, rz))
            throw OutsideMap(view);
        double? requestedHeight =
            tool == "flatten" && args.TryGetProperty("height", out var wanted)
                ? wanted.GetDouble()
                : null;
        var all = instances.SelectMany(i => i.Points).ToArray();
        int bx0 = (int)(all.Min(p => p.X) * view.InverseScale) - StrokeMarginNodes,
            bz0 = (int)(all.Min(p => p.Z) * view.InverseScale) - StrokeMarginNodes,
            bx1 = (int)(all.Max(p => p.X) * view.InverseScale) + StrokeMarginNodes,
            bz1 = (int)(all.Max(p => p.Z) * view.InverseScale) + StrokeMarginNodes;
        var before = Nodes(view, bx0, bz0, bx1, bz1);
        var waterBefore = WaterFlags(new LiveWorld.Tiles(game, view), before);
        var exited = ExitEditModes(game);
        var steps = new List<object> { new { exitedMode = LiveWorld.ModeName(exited.Before) } };
        var strokes = 0;
        float? sampled = null;
        object? brush = null;
        double brushSize = 1;
        var restoreBrushes = new List<(string Mode, int Size)>();
        int? sampleTouchUps = null;
        try
        {
            if (requestedHeight is { } exact)
                sampled = SampleExactHeight(
                    game,
                    exact,
                    rx,
                    rz,
                    tolerance,
                    moveCamera,
                    steps,
                    restoreBrushes,
                    ref strokes
                );
            else // SetSample leaves elevationsample active with the sample set; re-entering it is not live-verified.
                EnterMode(game, mode);
            if (args.TryGetProperty("brushSize", out var bs))
            {
                var (after, applied) = SetBrushSize(game, bs.GetInt32());
                if (!applied)
                    throw new WorkflowFailure(
                        "BRUSH_SIZE_NOT_APPLIED",
                        "brush",
                        $"Brush size readback {after?.Size} after uiChangeBrushSize({bs.GetInt32()}).",
                        true,
                        false,
                        "Nothing painted. Check editor_brush for the engine's size range."
                    );
            }
            if (LiveWorld.ReadBrush(game) is { } b)
            {
                brush = new { size = b.Size, type = b.Type };
                brushSize = b.Size;
            }
            if (tool == "flatten" && requestedHeight is null)
            {
                var refPixel = Screen(game, [(rx, rz)], moveCamera, 1).Pixels[0];
                RequireNoHeldInput();
                Ui.SettledClick(game, refPixel.X, refPixel.Y, "right"); // Right click samples (elevationsample tool).
                sampled = EditorView.ReadView(game).Height(rx, rz).Height;
                steps.Add(new { sampledAt = new[] { rx, rz }, sampledHeight = sampled });
            }
            var start = EditorView.ReadView(game);
            foreach (var (name, pts) in instances)
            {
                // Project on the instance's pre-stroke mean height (as flatten projects on its sample height): after a
                // pass the targets can sit in a trench or behind a ridge the camera cannot see into (live 2026-10-21:
                // lowering a fresh ridge failed the ray back-check by 4-6 units).
                var plane = sampled ?? (float)pts.Average(q => start.Height(q.X, q.Z).Height);
                for (var pass = 0; pass < passes; pass++)
                {
                    var path = pass % 2 == 0 ? pts : [.. pts.Reverse()];
                    steps.Add(new { instance = name, pass });
                    ProgressReporter.Report(strokes, null, $"{name} pass {pass}, strokes");
                    PaintChunks(
                        game,
                        path,
                        moveCamera,
                        duration,
                        PathLength(path),
                        0,
                        steps,
                        ref strokes,
                        plane,
                        button,
                        Math.Max(
                            DefaultPickTolerance,
                            brushSize * BrushWorldPerSize * StrokePickRadii
                        )
                    );
                    Thread.Sleep(StrokeSettleMs / 2);
                }
            }
            Thread.Sleep(StrokeSettleMs);
            if (requestedHeight is not null && sampled is { } goal)
            {
                // Bump/test-dab residue the drag did not cover (small brushes): dab changed nodes near the sample anchor
                // to the requested height. Nodes the call did not change are left alone.
                var r = (int)Math.Ceiling(SampleResidueNodes);
                int ax = (int)Math.Round(rx * view.InverseScale),
                    az = (int)Math.Round(rz * view.InverseScale);
                var now = EditorView.ReadView(game);
                bool Changed(int ix, int iz) =>
                    ix - before.X0 is var i
                    && iz - before.Z0 is var j
                    && i >= 0
                    && j >= 0
                    && i < before.Heights.Length
                    && j < before.Heights[i].Length
                    && Math.Abs(now.HeightRow(ix, iz, 1)[0] - before.Heights[i][j]) > HeightEpsilon;
                _elevationEdits = 0;
                try
                {
                    TouchUpNodes(
                        game,
                        Changed,
                        ax - r,
                        az - r,
                        ax + r,
                        az + r,
                        goal,
                        tolerance,
                        moveCamera,
                        steps
                    );
                }
                finally
                {
                    sampleTouchUps = _elevationEdits;
                    strokes += _elevationEdits;
                }
            }
        }
        catch (Exception e) when (strokes > 0 && e is not WorkflowFailure { UndoSteps: not null })
        {
            var f = e as WorkflowFailure;
            throw new WorkflowFailure(
                f?.Code ?? ElevationIncompleteCode,
                f?.Phase ?? "stroke",
                $"{e.Message} {strokes} stroke chunk(s) were already applied.",
                true,
                false,
                $"Scene changed: run editor_undo {strokes} time(s) to revert, or continue from the reported steps.",
                inner: e,
                undoSteps: strokes
            );
        }
        finally
        {
            foreach (var (restoreMode, size) in restoreBrushes)
            {
                EnterMode(game, restoreMode);
                SetBrushSize(game, size);
            }
            ExitEditModes(game);
        }
        var viewAfter = EditorView.ReadView(game);
        var after2 = Nodes(
            viewAfter,
            before.X0,
            before.Z0,
            before.X0 + before.Heights.Length - 1,
            before.Z0 + before.Heights[0].Length - 1
        );
        var waterAfter = WaterFlags(new LiveWorld.Tiles(game, viewAfter), after2);
        int raised = 0,
            lowered = 0,
            atSample = 0,
            waterChanged = 0;
        float maxRaise = 0,
            maxLower = 0,
            min = float.MaxValue,
            max = float.MinValue;
        for (var i = 0; i < before.Heights.Length; i++)
        for (var j = 0; j < before.Heights[i].Length; j++)
        {
            var d = after2.Heights[i][j] - before.Heights[i][j];
            if (d > HeightEpsilon)
            {
                raised++;
                maxRaise = Math.Max(maxRaise, d);
            }
            else if (d < -HeightEpsilon)
            {
                lowered++;
                maxLower = Math.Min(maxLower, d);
            }
            if (Math.Abs(d) > HeightEpsilon)
            {
                min = Math.Min(min, after2.Heights[i][j]);
                max = Math.Max(max, after2.Heights[i][j]);
                if (sampled is { } s && Math.Abs(after2.Heights[i][j] - s) <= tolerance)
                    atSample++;
            }
            if (waterBefore[i, j] != waterAfter[i, j])
                waterChanged++;
        }
        return new
        {
            tool,
            mode,
            button,
            points = points.Length,
            instances = instances.Select(i => i.Name).ToArray(),
            passes,
            strokes,
            brush,
            requestedHeight,
            sampledHeight = sampled,
            sampleSource = tool != "flatten" ? null
            : requestedHeight is null ? "reference"
            : "bump",
            sampleTouchUps,
            steps,
            verification = new
            {
                nodeBox = new
                {
                    minX = before.X0 * viewAfter.Scale,
                    minZ = before.Z0 * viewAfter.Scale,
                    maxX = (before.X0 + before.Heights.Length - 1) * viewAfter.Scale,
                    maxZ = (before.Z0 + before.Heights[0].Length - 1) * viewAfter.Scale,
                },
                nodesRaised = raised,
                nodesLowered = lowered,
                maxRaise,
                maxLower,
                changedMin = raised + lowered > 0 ? min : (float?)null,
                changedMax = raised + lowered > 0 ? max : (float?)null,
                changedNodesAtSampledHeight = sampled is null ? (int?)null : atSample,
                tolerance,
                tilesWaterFlagChanged = waterChanged,
                changed = raised + lowered > 0,
            },
            limitation = "Drags the native tool along projected stroke pixels with the CURRENT brush (or brushSize): raise/lower = elevation left/right drag, smooth/roughen = their tools, flatten = elevationsample right-click sample at referenceX/Z (default first vertex) then left drag at that height. Projection on the current terrain (under water). Strokes that do not fit one view are split (camera re-aimed, auto zoom-out for flatten). Verification diffs node heights and tile water flags in the stroke bbox +12 nodes. strokes = undo count (editor_undo once each). Not saved.",
        };
    }

    // ---------------- shapes ----------------

    /// <summary>Parsed editor_terrain_shape geometry in world units.</summary>
    /// <param name="Kind">polygon, circle, ellipse or capsule.</param>
    /// <param name="Points">Polygon / capsule vertices (empty otherwise).</param>
    /// <param name="X">Circle/ellipse center X.</param>
    /// <param name="Z">Circle/ellipse center Z.</param>
    /// <param name="Radius">Circle/capsule radius.</param>
    /// <param name="RadiusX">Ellipse semi-axis along its rotated X.</param>
    /// <param name="RadiusZ">Ellipse semi-axis along its rotated Z.</param>
    /// <param name="Angle">Ellipse rotation in radians (counter-clockwise from +X toward +Z).</param>
    internal sealed record ShapeSpec(
        string Kind,
        (double X, double Z)[] Points,
        double X,
        double Z,
        double Radius,
        double RadiusX,
        double RadiusZ,
        double Angle
    )
    {
        /// <summary>True when world (x, z) lies inside the shape (polygon: even-odd rule).</summary>
        internal bool Inside(double x, double z)
        {
            switch (Kind)
            {
                case "circle":
                    return Math.Pow(x - X, 2) + Math.Pow(z - Z, 2) <= Radius * Radius;
                case "ellipse":
                {
                    double dx = x - X,
                        dz = z - Z,
                        c = Math.Cos(Angle),
                        s = Math.Sin(Angle);
                    double u = (dx * c + dz * s) / RadiusX,
                        v = (-dx * s + dz * c) / RadiusZ;
                    return u * u + v * v <= 1;
                }
                case "capsule":
                    return Points
                        .Zip(Points.Skip(1))
                        .Any(seg => SegmentDistance(x, z, seg.First, seg.Second) <= Radius);
                default:
                {
                    var inside = false;
                    for (int i = 0, j = Points.Length - 1; i < Points.Length; j = i++)
                        if (
                            (Points[i].Z > z) != (Points[j].Z > z)
                            && x
                                < (Points[j].X - Points[i].X)
                                    * (z - Points[i].Z)
                                    / (Points[j].Z - Points[i].Z)
                                    + Points[i].X
                        )
                            inside = !inside;
                    return inside;
                }
            }
        }

        /// <summary>Half extent of the rotated ellipse along world X.</summary>
        double EllipseHalfX() =>
            Math.Sqrt(
                Math.Pow(RadiusX * Math.Cos(Angle), 2) + Math.Pow(RadiusZ * Math.Sin(Angle), 2)
            );

        /// <summary>Half extent of the rotated ellipse along world Z.</summary>
        double EllipseHalfZ() =>
            Math.Sqrt(
                Math.Pow(RadiusX * Math.Sin(Angle), 2) + Math.Pow(RadiusZ * Math.Cos(Angle), 2)
            );

        /// <summary>Axis-aligned world bounds (minX, minZ, maxX, maxZ).</summary>
        internal (double MinX, double MinZ, double MaxX, double MaxZ) Bounds() =>
            Kind switch
            {
                "circle" => (X - Radius, Z - Radius, X + Radius, Z + Radius),
                // Exact extents of the rotated ellipse (session 4 live: the bounding circle refused a flat
                // radiusX 60 / radiusZ 14 ellipse 42 nodes inside the map edge with TARGET_OUTSIDE_MAP).
                "ellipse" => (
                    X - EllipseHalfX(),
                    Z - EllipseHalfZ(),
                    X + EllipseHalfX(),
                    Z + EllipseHalfZ()
                ),
                "capsule" => (
                    Points.Min(p => p.X) - Radius,
                    Points.Min(p => p.Z) - Radius,
                    Points.Max(p => p.X) + Radius,
                    Points.Max(p => p.Z) + Radius
                ),
                _ => (
                    Points.Min(p => p.X),
                    Points.Min(p => p.Z),
                    Points.Max(p => p.X),
                    Points.Max(p => p.Z)
                ),
            };
    }

    /// <summary>Distance from (x, z) to segment a–b.</summary>
    static double SegmentDistance(
        double x,
        double z,
        (double X, double Z) a,
        (double X, double Z) b
    )
    {
        double dx = b.X - a.X,
            dz = b.Z - a.Z,
            len = dx * dx + dz * dz;
        var t = len < 1e-12 ? 0 : Math.Clamp(((x - a.X) * dx + (z - a.Z) * dz) / len, 0, 1);
        return Math.Sqrt(Math.Pow(x - a.X - t * dx, 2) + Math.Pow(z - a.Z - t * dz, 2));
    }

    /// <summary>Parses and validates editor_terrain_shape geometry (no game connection).</summary>
    /// <param name="args">shape plus points/x/z/radius/radiusX/radiusZ/angleDegrees.</param>
    internal static ShapeSpec ParseShape(JsonElement args)
    {
        var kind = String(args, "shape");
        var points = args.TryGetProperty("points", out var pe) ? WorldPoints(pe) : [];
        double x = Dbl(args, "x", 0),
            z = Dbl(args, "z", 0);
        switch (kind)
        {
            case "polygon":
                if (points.Length is < 3 or > MaxShapeVertices)
                    throw new ArgumentException(
                        $"shape=polygon needs 3..{MaxShapeVertices} points."
                    );
                break;
            case "capsule":
                if (points.Length is < 2 or > MaxShapeVertices)
                    throw new ArgumentException(
                        $"shape=capsule needs 2..{MaxShapeVertices} points."
                    );
                if (!args.TryGetProperty("radius", out _))
                    throw new ArgumentException("shape=capsule needs radius.");
                break;
            case "circle":
                if (
                    !args.TryGetProperty("x", out _)
                    || !args.TryGetProperty("z", out _)
                    || !args.TryGetProperty("radius", out _)
                )
                    throw new ArgumentException("shape=circle needs x, z and radius.");
                break;
            case "ellipse":
                if (
                    !args.TryGetProperty("x", out _)
                    || !args.TryGetProperty("z", out _)
                    || !args.TryGetProperty("radiusX", out _)
                    || !args.TryGetProperty("radiusZ", out _)
                )
                    throw new ArgumentException("shape=ellipse needs x, z, radiusX and radiusZ.");
                break;
            default:
                throw new ArgumentException("shape: polygon/circle/ellipse/capsule");
        }
        if (kind is "circle" or "ellipse" && points.Length > 0)
            throw new ArgumentException($"shape={kind} takes x/z, not points.");
        return new(
            kind,
            points,
            x,
            z,
            Dbl(args, "radius", 0),
            Dbl(args, "radiusX", 0),
            Dbl(args, "radiusZ", 0),
            double.DegreesToRadians(Dbl(args, "angleDegrees", 0))
        );
    }

    /// <summary>One shape instance on the node grid: inside mask, brush-eroded mask, sample square.</summary>
    /// <param name="Name">original or the mirror mode.</param>
    /// <param name="X0">First X node of the mask box.</param>
    /// <param name="Z0">First Z node of the mask box.</param>
    /// <param name="Inside">Nodes inside the shape.</param>
    /// <param name="Eroded">Inside nodes whose brush disc stays inside.</param>
    /// <param name="Square">Lower-left (mask-relative) node of the sample square, or null.</param>
    internal sealed record ShapeMask(
        string Name,
        int X0,
        int Z0,
        bool[,] Inside,
        bool[,] Eroded,
        (int I, int J)? Square
    )
    {
        /// <summary>True when absolute node (ix, iz) is inside.</summary>
        internal bool Contains(int ix, int iz) =>
            ix - X0 >= 0
            && iz - Z0 >= 0
            && ix - X0 < Inside.GetLength(0)
            && iz - Z0 < Inside.GetLength(1)
            && Inside[ix - X0, iz - Z0];

        /// <summary>Inside node count.</summary>
        internal int Count => Inside.Cast<bool>().Count(b => b);
    }

    /// <summary>Rasterizes a world-space inside test onto nodes (spacing <paramref name="scale"/>) clamped to the map.</summary>
    /// <param name="name">Instance name.</param>
    /// <param name="inside">World inside test.</param>
    /// <param name="bounds">World bounds of the shape.</param>
    /// <param name="scale">Node spacing (world units).</param>
    /// <param name="vertsX">Node count along X.</param>
    /// <param name="vertsZ">Node count along Z.</param>
    internal static ShapeMask Rasterize(
        string name,
        Func<double, double, bool> inside,
        (double MinX, double MinZ, double MaxX, double MaxZ) bounds,
        double scale,
        int vertsX,
        int vertsZ
    )
    {
        int x0 = Math.Max(0, (int)Math.Floor(bounds.MinX / scale)),
            z0 = Math.Max(0, (int)Math.Floor(bounds.MinZ / scale)),
            x1 = Math.Min(vertsX - 1, (int)Math.Ceiling(bounds.MaxX / scale)),
            z1 = Math.Min(vertsZ - 1, (int)Math.Ceiling(bounds.MaxZ / scale));
        var mask = new bool[Math.Max(0, x1 - x0 + 1), Math.Max(0, z1 - z0 + 1)];
        for (var i = 0; i < mask.GetLength(0); i++)
        for (var j = 0; j < mask.GetLength(1); j++)
            mask[i, j] = inside((x0 + i) * scale, (z0 + j) * scale);
        var eroded = Erode(mask, BrushErodeNodes);
        return new(name, x0, z0, mask, eroded, FindSquare(mask, ShapeSampleNodes));
    }

    /// <summary>Mask nodes whose disc of <paramref name="radius"/> nodes lies entirely inside the mask.</summary>
    /// <param name="mask">Node mask.</param>
    /// <param name="radius">Disc radius in nodes.</param>
    internal static bool[,] Erode(bool[,] mask, double radius)
    {
        int w = mask.GetLength(0),
            h = mask.GetLength(1),
            r = (int)Math.Ceiling(radius);
        var eroded = new bool[w, h];
        for (var i = 0; i < w; i++)
        for (var j = 0; j < h; j++)
        {
            if (!mask[i, j])
                continue;
            var ok = true;
            for (var di = -r; di <= r && ok; di++)
            for (var dj = -r; dj <= r && ok; dj++)
            {
                if (di * di + dj * dj > radius * radius)
                    continue;
                int a = i + di,
                    b = j + dj;
                ok = a >= 0 && b >= 0 && a < w && b < h && mask[a, b];
            }
            eroded[i, j] = ok;
        }
        return eroded;
    }

    /// <summary>Lower-left node of an n×n all-inside node square nearest the mask centroid, or null.</summary>
    /// <param name="mask">Node mask.</param>
    /// <param name="n">Square side in nodes.</param>
    internal static (int I, int J)? FindSquare(bool[,] mask, int n) =>
        Squares(mask, n).Select(q => ((int I, int J)?)q).FirstOrDefault();

    /// <summary>All n×n node squares fully inside the mask, nearest the mask centroid first.</summary>
    /// <param name="mask">Inside nodes.</param>
    /// <param name="n">Square side in nodes.</param>
    internal static (int I, int J)[] Squares(bool[,] mask, int n)
    {
        int w = mask.GetLength(0),
            h = mask.GetLength(1);
        var sum = new int[w + 1, h + 1];
        double cx = 0,
            cz = 0;
        var count = 0;
        for (var i = 0; i < w; i++)
        for (var j = 0; j < h; j++)
        {
            var v = mask[i, j] ? 1 : 0;
            sum[i + 1, j + 1] = v + sum[i, j + 1] + sum[i + 1, j] - sum[i, j];
            if (v == 1)
            {
                cx += i;
                cz += j;
                count++;
            }
        }
        if (count == 0)
            return [];
        cx /= count;
        cz /= count;
        var found = new List<(int I, int J, double D)>();
        for (var i = 0; i + n <= w; i++)
        for (var j = 0; j + n <= h; j++)
            if (sum[i + n, j + n] - sum[i, j + n] - sum[i + n, j] + sum[i, j] == n * n)
                found.Add(
                    (
                        i,
                        j,
                        Math.Pow(i + (n - 1) / 2.0 - cx, 2) + Math.Pow(j + (n - 1) / 2.0 - cz, 2)
                    )
                );
        return [.. found.OrderBy(f => f.D).Select(f => (f.I, f.J))];
    }

    /// <summary>Largest node height range of a sample square still treated as flat: below one live quantization step
    /// (3 → 4.15 single-dab bumps on the 2026-10-21 scratch map).</summary>
    const double FlatSquareRange = 0.5;

    /// <summary>Sample square of a shape: the nearest-centroid square whose nodes are flat (relief in the square broke the
    /// bump anchor pick live 2026-10-21), else the nearest-centroid square.</summary>
    /// <param name="view">View state for node heights.</param>
    /// <param name="m">Shape mask.</param>
    static (int I, int J) FlatSquare(EditorView.ViewState view, ShapeMask m)
    {
        var s = view.Scale;
        foreach (var (i, j) in Squares(m.Inside, ShapeSampleNodes))
        {
            double lo = double.MaxValue,
                hi = double.MinValue;
            for (var a = 0; a < ShapeSampleNodes; a++)
            for (var b = 0; b < ShapeSampleNodes; b++)
            {
                var y = view.Height((m.X0 + i + a) * s, (m.Z0 + j + b) * s).Height;
                (lo, hi) = (Math.Min(lo, y), Math.Max(hi, y));
            }
            if (hi - lo <= FlatSquareRange)
                return (i, j);
        }
        return m.Square!.Value;
    }

    /// <summary>True when every sample of segment a–b (node units) rounds to a mask node.</summary>
    /// <param name="mask">Node mask.</param>
    /// <param name="a">Start (mask-relative nodes).</param>
    /// <param name="b">End (mask-relative nodes).</param>
    internal static bool SegmentInside(bool[,] mask, (double I, double J) a, (double I, double J) b)
    {
        var length = Math.Sqrt(Math.Pow(b.I - a.I, 2) + Math.Pow(b.J - a.J, 2));
        var n = Math.Max(1, (int)Math.Ceiling(length / JoinSampleNodes));
        for (var k = 0; k <= n; k++)
        {
            int i = (int)Math.Round(a.I + (b.I - a.I) * k / n),
                j = (int)Math.Round(a.J + (b.J - a.J) * k / n);
            if (i < 0 || j < 0 || i >= mask.GetLength(0) || j >= mask.GetLength(1) || !mask[i, j])
                return false;
        }
        return true;
    }

    /// <summary>Serpentine scanline strokes over a mask: X rows every <paramref name="step"/> nodes (last row added),
    /// Z runs per row, consecutive runs joined only when the joining segment stays inside the mask.</summary>
    /// <param name="mask">Node mask (usually brush-eroded).</param>
    /// <param name="step">Row spacing in nodes.</param>
    /// <returns>Strokes as mask-relative node vertices (one vertex = dab).</returns>
    internal static List<List<(double I, double J)>> ScanStrokes(bool[,] mask, int step)
    {
        int w = mask.GetLength(0),
            h = mask.GetLength(1);
        var rows = Enumerable
            .Range(0, w)
            .Where(i => Enumerable.Range(0, h).Any(j => mask[i, j]))
            .ToArray();
        var strokes = new List<List<(double I, double J)>>();
        if (rows.Length == 0)
            return strokes;
        var picked = new List<int>();
        for (var i = rows[0]; i <= rows[^1]; i += step)
            picked.Add(i);
        if (picked[^1] != rows[^1])
            picked.Add(rows[^1]);
        List<(double I, double J)>? current = null;
        for (var r = 0; r < picked.Count; r++)
        {
            var i = picked[r];
            var runs = new List<(int A, int B)>();
            for (var j = 0; j < h; j++)
            {
                if (!mask[i, j])
                    continue;
                var start = j;
                while (j + 1 < h && mask[i, j + 1])
                    j++;
                runs.Add((start, j));
            }
            if (r % 2 == 1)
            {
                runs.Reverse();
                runs = [.. runs.Select(t => (t.B, t.A))];
            }
            foreach (var (a, b) in runs)
            {
                (double I, double J) first = (i, a),
                    last = (i, b);
                if (current is not null && SegmentInside(mask, current[^1], first))
                    current.Add(first);
                else
                {
                    current = [first];
                    strokes.Add(current);
                }
                if (a != b)
                    current.Add(last);
            }
        }
        return strokes;
    }

    /// <summary>Side of a terrain_shape view cell in nodes before splitting; session 4: offline pieces up to 70×75 world
    /// units (35×38 nodes at scale 2) painted at default zoom, 1920×1080.</summary>
    const int ShapeCellNodes = 32;

    /// <summary>Smallest cell side in nodes; smaller unreachable cells are reported (host bound, 4 brush radii).</summary>
    const int MinShapeCellNodes = 8;

    /// <summary>Most unreachable cells listed in the preflight refusal (host output bound).</summary>
    const int MaxUnreachableCells = 10;

    /// <summary>Copy of <paramref name="mask"/> with everything outside the inclusive cell rectangle cleared.</summary>
    /// <param name="mask">Mask-relative node mask.</param>
    /// <param name="cell">Inclusive cell bounds (mask-relative).</param>
    internal static bool[,] Clip(bool[,] mask, (int I0, int J0, int I1, int J1) cell)
    {
        var clip = new bool[mask.GetLength(0), mask.GetLength(1)];
        for (var i = Math.Max(0, cell.I0); i <= Math.Min(mask.GetLength(0) - 1, cell.I1); i++)
        for (var j = Math.Max(0, cell.J0); j <= Math.Min(mask.GetLength(1) - 1, cell.J1); j++)
            clip[i, j] = mask[i, j];
        return clip;
    }

    /// <summary>Inclusive square cells of side <paramref name="side"/> tiling the mask, empty cells dropped.</summary>
    /// <param name="mask">Mask-relative node mask.</param>
    /// <param name="side">Cell side in nodes.</param>
    internal static List<(int I0, int J0, int I1, int J1)> Cells(bool[,] mask, int side)
    {
        var cells = new List<(int I0, int J0, int I1, int J1)>();
        for (var i = 0; i < mask.GetLength(0); i += side)
        for (var j = 0; j < mask.GetLength(1); j += side)
        {
            (int, int, int, int) cell = (
                i,
                j,
                Math.Min(mask.GetLength(0), i + side) - 1,
                Math.Min(mask.GetLength(1), j + side) - 1
            );
            if (Clip(mask, cell).Cast<bool>().Any(b => b))
                cells.Add(cell);
        }
        return cells;
    }

    /// <summary>Cell halves (or quarters) used when a cell does not fit one view.</summary>
    /// <param name="cell">Inclusive cell bounds.</param>
    /// <param name="min">Smallest side that may still be split.</param>
    /// <returns>Sub-cells covering the cell; empty when it is already at the minimum on both axes.</returns>
    internal static (int I0, int J0, int I1, int J1)[] SplitCell(
        (int I0, int J0, int I1, int J1) cell,
        int min
    )
    {
        int w = cell.I1 - cell.I0 + 1,
            h = cell.J1 - cell.J0 + 1;
        int mi = cell.I0 + w / 2 - 1,
            mj = cell.J0 + h / 2 - 1;
        return (w > min, h > min) switch
        {
            (true, true) =>
            [
                (cell.I0, cell.J0, mi, mj),
                (cell.I0, mj + 1, mi, cell.J1),
                (mi + 1, cell.J0, cell.I1, mj),
                (mi + 1, mj + 1, cell.I1, cell.J1),
            ],
            (true, false) => [(cell.I0, cell.J0, mi, cell.J1), (mi + 1, cell.J0, cell.I1, cell.J1)],
            (false, true) => [(cell.I0, cell.J0, cell.I1, mj), (cell.I0, mj + 1, cell.I1, cell.J1)],
            _ => [],
        };
    }

    /// <summary>terrain_shape preflight: tiles the eroded mask into cells, aims the camera at each (zoom-out and steeper
    /// pitch fallback, no edits) and splits cells whose dab vertices do not fit one view, down to
    /// <see cref="MinShapeCellNodes"/>.</summary>
    /// <param name="game">Validated editor connection.</param>
    /// <param name="m">Shape instance.</param>
    /// <param name="moveCamera">Whether the camera may move.</param>
    /// <param name="at">Projection height (target height).</param>
    /// <param name="unreachable">Receives unreachable cells with the first failing point.</param>
    /// <returns>Reachable cells in paint order.</returns>
    List<(int I0, int J0, int I1, int J1)> PlanCells(
        Game game,
        ShapeMask m,
        bool moveCamera,
        float at,
        List<object> unreachable
    )
    {
        var view = EditorView.ReadView(game);
        var half = view.Scale / 2;
        var plan = new List<(int I0, int J0, int I1, int J1)>();
        void Visit((int I0, int J0, int I1, int J1) cell)
        {
            var strokes = ScanStrokes(Clip(m.Eroded, cell), ShapeRowStepNodes);
            if (strokes.Count == 0)
                return;
            (double X, double Z)[] pts =
            [
                .. strokes.SelectMany(st =>
                    st.Select(p =>
                        ((m.X0 + p.I) * view.Scale + half, (m.Z0 + p.J) * view.Scale + half)
                    )
                ),
            ];
            ProgressReporter.Report(plan.Count, null, $"shape {m.Name} preflight cell");
            try
            {
                _ = Screen(game, pts, moveCamera, DefaultPickTolerance, at, moveCamera, moveCamera);
                plan.Add(cell);
            }
            catch (WorkflowFailure f) when (f.Code == NotClickableCode)
            {
                var parts = SplitCell(cell, MinShapeCellNodes);
                if (parts.Length == 0)
                    unreachable.Add(
                        new
                        {
                            instance = m.Name,
                            minX = (m.X0 + cell.I0) * view.Scale,
                            minZ = (m.Z0 + cell.J0) * view.Scale,
                            maxX = (m.X0 + cell.I1) * view.Scale,
                            maxZ = (m.Z0 + cell.J1) * view.Scale,
                            reason = f.Message,
                        }
                    );
                foreach (var part in parts)
                    Visit(part);
            }
        }
        foreach (var cell in Cells(m.Eroded, ShapeCellNodes))
            Visit(cell);
        return plan;
    }

    /// <summary>editor_terrain_shape: fill a polygon/circle/ellipse/capsule with an exact node height (optionally water).</summary>
    /// <param name="game">Validated editor connection.</param>
    /// <param name="args">Tool arguments.</param>
    object TerrainShape(Game game, JsonElement args)
    {
        var spec = ParseShape(args);
        var height = args.GetProperty("height").GetDouble();
        var tolerance = Dbl(args, "tolerance", DefaultShapeTolerance);
        var moveCamera = !args.TryGetProperty("moveCamera", out var mc) || mc.GetBoolean();
        RequireNoCursor(game);
        var view = EditorView.ReadView(game);
        double ww = view.WorldWidth,
            wd = view.WorldDepth;
        var shapes = new List<(
            string Name,
            Func<double, double, bool> Inside,
            (double, double, double, double) Bounds
        )>
        {
            ("original", spec.Inside, spec.Bounds()),
        };
        if (args.TryGetProperty("mirror", out var me))
        {
            var mode = me.GetString()!;
            var b = spec.Bounds();
            (double X, double Z)[] corners =
            [
                MirrorPoint(mode, b.MinX, b.MinZ, ww, wd),
                MirrorPoint(mode, b.MaxX, b.MaxZ, ww, wd),
                MirrorPoint(mode, b.MinX, b.MaxZ, ww, wd),
                MirrorPoint(mode, b.MaxX, b.MinZ, ww, wd),
            ];
            // All mirror modes are involutions, so the mirrored shape contains p exactly when the original contains mirror(p).
            shapes.Add(
                (
                    mode,
                    (x, z) =>
                    {
                        var (sx, sz) = MirrorPoint(mode, x, z, ww, wd);
                        return spec.Inside(sx, sz);
                    },
                    (
                        corners.Min(c => c.X),
                        corners.Min(c => c.Z),
                        corners.Max(c => c.X),
                        corners.Max(c => c.Z)
                    )
                )
            );
        }
        foreach (var (_, _, (minX, minZ, maxX, maxZ)) in shapes)
            if (!view.InsideMap(minX, minZ) || !view.InsideMap(maxX, maxZ))
                throw OutsideMap(view);
        var masks = shapes
            .Select(s =>
                Rasterize(
                    s.Name,
                    s.Inside,
                    s.Bounds,
                    view.Scale,
                    view.Vertices[0],
                    view.Vertices[1]
                )
            )
            .ToArray();
        foreach (var m in masks)
            if (m.Square is null)
                throw new WorkflowFailure(
                    "AREA_TOO_SMALL",
                    "preflight",
                    $"Shape '{m.Name}' ({m.Count} nodes) contains no {ShapeSampleNodes}×{ShapeSampleNodes}-node square for the exact-height sample loop.",
                    false,
                    false,
                    "Enlarge the shape (at least ~14 world units across), or use editor_stroke tool=flatten. No input sent."
                );
        string? water = null;
        if (args.TryGetProperty("water", out var we))
        {
            var names = LiveWorld.Names(game);
            water =
                ExactName(names.Water, we.GetString()!)
                ?? throw new WorkflowFailure(
                    "UNKNOWN_PAINT_TYPE",
                    "preflight",
                    $"Unknown water type '{we.GetString()}'. Suggestions: {string.Join(", ", GameDataCatalog.Suggest(we.GetString()!, names.Water))}",
                    false,
                    false,
                    "Use editor_terrain_catalog kind=water for exact names. No input sent."
                );
        }
        int bx0 = masks.Min(m => m.X0) - ShapeRingNodes,
            bz0 = masks.Min(m => m.Z0) - ShapeRingNodes,
            bx1 = masks.Max(m => m.X0 + m.Inside.GetLength(0)) + ShapeRingNodes,
            bz1 = masks.Max(m => m.Z0 + m.Inside.GetLength(1)) + ShapeRingNodes;
        var before = Nodes(view, bx0, bz0, bx1, bz1);
        var exited = ExitEditModes(game);
        var steps = new List<object> { new { exitedMode = LiveWorld.ModeName(exited.Before) } };
        var half = view.Scale / 2;
        var goal = (float)height;
        var waterStrokes = 0;
        _elevationEdits = 0;
        (double X, double Z)[] World(ShapeMask m, List<(double I, double J)> stroke) =>
            [
                .. stroke.Select(p =>
                    ((m.X0 + p.I) * view.Scale + half, (m.Z0 + p.J) * view.Scale + half)
                ),
            ];
        // Preflight (camera only, before the first edit): every cell of every instance must project clickable.
        _pitchBefore = null;
        var plans = new List<(int I0, int J0, int I1, int J1)>[masks.Length];
        var unreachable = new List<object>();
        try
        {
            for (var k = 0; k < masks.Length; k++)
                plans[k] = PlanCells(game, masks[k], moveCamera, goal, unreachable);
        }
        catch
        {
            _ = RestorePitch(game);
            throw;
        }
        if (unreachable.Count > 0)
        {
            var restored = RestorePitch(game);
            throw new WorkflowFailure(
                NotClickableCode,
                "preflight",
                $"{unreachable.Count} cell(s) cannot be brought into a clickable view even at zoom {AutoZoomLimit} and pitch {SteepPitches[^1]}°: {JsonSerializer.Serialize(unreachable.Take(MaxUnreachableCells))}. Pitch restore: {JsonSerializer.Serialize(restored)}.",
                false,
                false,
                "0 edits. Camera moved only. Shrink/move the shape away from the camera-side map edge, or rotate the camera (editor_camera), then retry."
            );
        }
        try
        {
            for (var k = 0; k < masks.Length; k++)
            {
                var m = masks[k];
                if (k == 0)
                {
                    // One sample per call: the mirror instance and all cells reuse it (session 4: per-piece samples left
                    // seams up to 0.27). Sample at the default zoom (session 4: the loop did not converge at zoom 190).
                    var (si, sj) = FlatSquare(view, m);
                    double minX = (m.X0 + si) * view.Scale,
                        minZ = (m.Z0 + sj) * view.Scale,
                        maxX = minX + (ShapeSampleNodes - 1) * view.Scale,
                        maxZ = minZ + (ShapeSampleNodes - 1) * view.Scale;
                    if (
                        moveCamera
                        && (PoseOf(EditorView.ReadView(game)).Zoom ?? DefaultZoom) > DefaultZoom
                    )
                        SetZoom(game, DefaultZoom);
                    // Bring the sample square into a clear view first (camera may move/zoom out).
                    var aim = Screen(
                        game,
                        [(minX, minZ), (maxX, maxZ), (minX, maxZ), (maxX, minZ)],
                        moveCamera,
                        4,
                        // Flat square (FlatSquare): visibility/UI check at its height; SetSample checks its own picks.
                        view.Height((minX + maxX) / 2, (minZ + maxZ) / 2).Height,
                        zoomOut: moveCamera,
                        steepen: moveCamera
                    );
                    steps.Add(
                        new
                        {
                            instance = m.Name,
                            nodes = m.Count,
                            sampleCamera = aim.Camera,
                        }
                    );
                    steps.AddRange(
                        SetSample(
                            game,
                            height,
                            (minX + maxX) / 2,
                            (minZ + maxZ) / 2,
                            minX,
                            minZ,
                            maxX,
                            maxZ,
                            tolerance,
                            out var sampled
                        )
                    );
                    goal = (float)sampled;
                }
                else
                    steps.Add(
                        new
                        {
                            instance = m.Name,
                            nodes = m.Count,
                            sample = "reused",
                        }
                    );
                var cells = plans[k];
                var chunks = 0;
                var scanStrokes = 0;
                for (var pass = 0; pass < 2; pass++) // Two passes, as editor_elevation (nodes missed mid-change).
                {
                    var order = pass == 0 ? cells : [.. cells.AsEnumerable().Reverse()];
                    for (var index = 0; index < order.Count; index++)
                    {
                        ProgressReporter.Report(
                            index,
                            order.Count,
                            $"shape {m.Name} pass {pass + 1}/2 cell"
                        );
                        var strokes = ScanStrokes(Clip(m.Eroded, order[index]), ShapeRowStepNodes);
                        if (pass == 1)
                            strokes =
                            [
                                .. strokes
                                    .AsEnumerable()
                                    .Reverse()
                                    .Select(st => st.AsEnumerable().Reverse().ToList()),
                            ];
                        else
                            scanStrokes += strokes.Count;
                        // Pose reuse: aim once at the whole cell (as in preflight) so its strokes need no camera move.
                        _ = Screen(
                            game,
                            [.. strokes.SelectMany(st => World(m, st))],
                            moveCamera,
                            DefaultPickTolerance,
                            goal,
                            zoomOut: moveCamera,
                            steepen: moveCamera
                        );
                        foreach (var stroke in strokes)
                        {
                            var pts = World(m, stroke);
                            var prior = chunks;
                            try
                            {
                                PaintChunks(
                                    game,
                                    pts,
                                    moveCamera,
                                    StrokeDuration(pts.Length),
                                    PathLength(pts),
                                    0,
                                    steps,
                                    ref chunks,
                                    goal
                                );
                            }
                            finally
                            {
                                _elevationEdits += chunks - prior;
                            }
                        }
                    }
                    Thread.Sleep(StrokeSettleMs / 2);
                }
                steps.Add(
                    new
                    {
                        instance = m.Name,
                        cells = cells.Count,
                        scanStrokes,
                        strokeChunks = chunks,
                    }
                );
                TouchUpNodes(
                    game,
                    m.Contains,
                    m.X0,
                    m.Z0,
                    m.X0 + m.Inside.GetLength(0) - 1,
                    m.Z0 + m.Inside.GetLength(1) - 1,
                    goal,
                    tolerance,
                    moveCamera,
                    steps
                );
            }
            if (water is not null)
            {
                ExitEditModes(game);
                EnterMode(game, WaterMode);
                _bridge.Execute(game, $"uiSetWaterType({Catalog.Quote(water)})");
                var selected = false;
                for (var i = 0; i < BrushPollMs / PollStepMs && !selected; i++)
                {
                    selected = LiveWorld.ReadSelections(game).WaterName == water;
                    if (!selected)
                        Thread.Sleep(PollStepMs);
                }
                if (!selected)
                    throw new WorkflowFailure(
                        "PAINT_TYPE_NOT_SELECTED",
                        "select",
                        $"Water selection did not become '{water}' (memory readback).",
                        true,
                        false,
                        "Heights were set; water not painted."
                    );
                for (var k = 0; k < masks.Length; k++)
                    foreach (var cell in plans[k])
                    foreach (
                        var stroke in ScanStrokes(Clip(masks[k].Eroded, cell), ShapeRowStepNodes)
                    )
                    {
                        var pts = World(masks[k], stroke);
                        var prior = waterStrokes;
                        try
                        {
                            PaintChunks(
                                game,
                                pts,
                                moveCamera,
                                StrokeDuration(pts.Length),
                                PathLength(pts),
                                0,
                                steps,
                                ref waterStrokes
                            );
                        }
                        finally
                        {
                            _elevationEdits += waterStrokes - prior;
                        }
                    }
                steps.Add(new { water, waterStrokes });
            }
            Thread.Sleep(StrokeSettleMs);
        }
        catch (Exception e)
            when (_elevationEdits > 0 && e is not WorkflowFailure { UndoSteps: not null })
        {
            // No automatic undo: a wrong count would revert user work (plans/wake-island-session-bugs.md B3).
            var f = e as WorkflowFailure;
            throw new WorkflowFailure(
                f?.Code ?? ElevationIncompleteCode,
                f?.Phase ?? "shape",
                $"{e.Message} {_elevationEdits} edit(s) were already applied.",
                true,
                false,
                $"Scene changed. The exact-height fill is idempotent: re-running the same call is safe (idempotentRetry). Or run editor_undo {_elevationEdits} time(s) to revert (check with editor_terrain_grid).",
                inner: e,
                undoSteps: _elevationEdits,
                idempotentRetry: true
            );
        }
        finally
        {
            ExitEditModes(game);
            if (RestorePitch(game) is { } restored)
                steps.Add(new { pitch = restored });
        }
        var viewAfter = EditorView.ReadView(game);
        var after = Nodes(
            viewAfter,
            before.X0,
            before.Z0,
            before.X0 + before.Heights.Length - 1,
            before.Z0 + before.Heights[0].Length - 1
        );
        var tiles = new LiveWorld.Tiles(game, viewAfter);
        var reports = masks
            .Select(m =>
            {
                int inside = 0,
                    within = 0,
                    ring = 0,
                    ringChanged = 0;
                float lo = float.MaxValue,
                    hi = float.MinValue;
                for (var ix = before.X0; ix < before.X0 + before.Heights.Length; ix++)
                for (var iz = before.Z0; iz < before.Z0 + before.Heights[0].Length; iz++)
                {
                    var h = after.At(ix, iz)!.Value;
                    if (m.Contains(ix, iz))
                    {
                        inside++;
                        lo = Math.Min(lo, h);
                        hi = Math.Max(hi, h);
                        if (Math.Abs(h - goal) <= tolerance)
                            within++;
                        continue;
                    }
                    var near = false;
                    for (var dx = -ShapeRingNodes; dx <= ShapeRingNodes && !near; dx++)
                    for (var dz = -ShapeRingNodes; dz <= ShapeRingNodes && !near; dz++)
                        near = m.Contains(ix + dx, iz + dz);
                    if (!near || masks.Any(o => o.Contains(ix, iz)))
                        continue;
                    ring++;
                    if (Math.Abs(h - before.At(ix, iz)!.Value) > HeightEpsilon)
                        ringChanged++;
                }
                // Tiles whose four corner nodes are all inside: passability and the water-corner rule.
                var classes = new Dictionary<string, int>(StringComparer.Ordinal);
                int waterAllAbove = 0,
                    dryBelow = 0;
                for (var ix = m.X0; ix < m.X0 + m.Inside.GetLength(0) - 1; ix++)
                for (var iz = m.Z0; iz < m.Z0 + m.Inside.GetLength(1) - 1; iz++)
                {
                    if (
                        !(
                            m.Contains(ix, iz)
                            && m.Contains(ix + 1, iz)
                            && m.Contains(ix, iz + 1)
                            && m.Contains(ix + 1, iz + 1)
                        )
                        || ix >= tiles.TilesX
                        || iz >= tiles.TilesZ
                    )
                        continue;
                    var t = tiles.At(ix, iz);
                    var c = tiles.Passability(t);
                    classes[c] = classes.GetValueOrDefault(c) + 1;
                    if (
                        viewAfter.WaterSurface(
                            (ix + 0.5) * viewAfter.Scale,
                            (iz + 0.5) * viewAfter.Scale
                        )
                            is { } s
                        && new[]
                        {
                            after.At(ix, iz),
                            after.At(ix + 1, iz),
                            after.At(ix, iz + 1),
                            after.At(ix + 1, iz + 1),
                        }.All(h => h > s)
                    )
                        waterAllAbove++;
                    if (tiles.WaterName(t) is null && water is not null)
                        dryBelow++;
                }
                return new
                {
                    instance = m.Name,
                    insideNodes = inside,
                    withinTolerance = within,
                    insideMin = inside > 0 ? lo : (float?)null,
                    insideMax = inside > 0 ? hi : (float?)null,
                    ringNodes = ring,
                    ringNodesChanged = ringChanged,
                    insideTiles = classes,
                    waterTilesWithAllCornersAboveSurface = waterAllAbove,
                    dryInsideTilesAfterWaterPaint = water is null ? (int?)null : dryBelow,
                    success = inside == within,
                };
            })
            .ToArray();
        return new
        {
            shape = spec.Kind,
            height,
            sampledHeight = goal,
            tolerance,
            water,
            edits = _elevationEdits,
            steps,
            instances = reports,
            success = reports.All(r => r.success),
            limitation = "Native tools only (no memory writes): per instance an exact-height bump/sample/test-dab loop inside a 7×7-node square, two passes of elevation-sample scanline strokes over the brush-eroded shape (strokes split where joins would leave a concave shape), then touch-up dabs (≤40) on inside nodes still off target. Uses the CURRENT elevation-sample brush (erosion assumes ≈2.2 nodes, research/LIVE-WORLD.md); larger brushes bleed further (ringNodesChanged). water paints the water tool along the same strokes; land/water still follows the tile corner rule (editor_land_analysis). edits = undo count (editor_undo once each). Not saved.",
        };
    }

    /// <summary>Default drag duration for a stroke of <paramref name="vertices"/> vertices (≥ <see cref="MinStrokeMs"/>).</summary>
    /// <param name="vertices">Stroke vertex count.</param>
    static int StrokeDuration(int vertices) =>
        Math.Clamp(MinStrokeMs + StrokeMsPerVertex * vertices, MinStrokeMs, MaxStrokeMs);

    // ---------------- editor_land_analysis ----------------

    /// <summary>True when a unit may step from land tile (x, z) by (dx, dz): diagonal steps need both orthogonal
    /// neighbours to be land (no corner cutting between two water tiles).</summary>
    static bool Step(bool[,] land, int x, int z, int dx, int dz)
    {
        int nx = x + dx,
            nz = z + dz;
        if (nx < 0 || nz < 0 || nx >= land.GetLength(0) || nz >= land.GetLength(1) || !land[nx, nz])
            return false;
        return dx == 0 || dz == 0 || (land[x + dx, z] && land[x, z + dz]);
    }

    /// <summary>8-neighbour offsets.</summary>
    static readonly (int Dx, int Dz)[] Neighbours =
    [
        (1, 0),
        (-1, 0),
        (0, 1),
        (0, -1),
        (1, 1),
        (1, -1),
        (-1, 1),
        (-1, -1),
    ];

    /// <summary>Connected components of a boolean grid (8-connected with the corner rule when <paramref name="corner"/>,
    /// else 4-connected).</summary>
    /// <returns>Component id per tile (-1 outside) and size per component.</returns>
    internal static (int[,] Label, List<int> Sizes) Components(bool[,] grid, bool corner)
    {
        int w = grid.GetLength(0),
            h = grid.GetLength(1);
        var label = new int[w, h];
        for (var i = 0; i < w; i++)
        for (var j = 0; j < h; j++)
            label[i, j] = -1;
        var sizes = new List<int>();
        var stack = new Stack<(int, int)>();
        for (var i = 0; i < w; i++)
        for (var j = 0; j < h; j++)
        {
            if (!grid[i, j] || label[i, j] >= 0)
                continue;
            var id = sizes.Count;
            var size = 0;
            label[i, j] = id;
            stack.Push((i, j));
            while (stack.Count > 0)
            {
                var (x, z) = stack.Pop();
                size++;
                foreach (var (dx, dz) in corner ? Neighbours : Neighbours[..4])
                {
                    if (!Step(grid, x, z, dx, dz) || label[x + dx, z + dz] >= 0)
                        continue;
                    label[x + dx, z + dz] = id;
                    stack.Push((x + dx, z + dz));
                }
            }
            sizes.Add(size);
        }
        return (label, sizes);
    }

    /// <summary>Multi-source 8-neighbour Dijkstra over land (corner rule); distances in tiles.</summary>
    /// <param name="land">Land grid.</param>
    /// <param name="sources">Source tiles (all land).</param>
    /// <returns>Distance and nearest source index per tile (infinity / -1 when unreachable).</returns>
    internal static (double[,] Distance, int[,] Owner) LandDistances(
        bool[,] land,
        (int X, int Z)[] sources
    )
    {
        int w = land.GetLength(0),
            h = land.GetLength(1);
        var dist = new double[w, h];
        var owner = new int[w, h];
        for (var i = 0; i < w; i++)
        for (var j = 0; j < h; j++)
        {
            dist[i, j] = double.PositiveInfinity;
            owner[i, j] = -1;
        }
        var queue = new PriorityQueue<(int X, int Z), double>();
        for (var s = 0; s < sources.Length; s++)
        {
            var (x, z) = sources[s];
            if (!land[x, z] || dist[x, z] == 0)
                continue;
            dist[x, z] = 0;
            owner[x, z] = s;
            queue.Enqueue((x, z), 0);
        }
        while (queue.TryDequeue(out var t, out var d))
        {
            if (d > dist[t.X, t.Z])
                continue;
            foreach (var (dx, dz) in Neighbours)
            {
                if (!Step(land, t.X, t.Z, dx, dz))
                    continue;
                var nd = d + (dx != 0 && dz != 0 ? Diagonal : 1);
                if (nd >= dist[t.X + dx, t.Z + dz])
                    continue;
                dist[t.X + dx, t.Z + dz] = nd;
                owner[t.X + dx, t.Z + dz] = owner[t.X, t.Z];
                queue.Enqueue((t.X + dx, t.Z + dz), nd);
            }
        }
        return (dist, owner);
    }

    /// <summary>Clearance per land tile: chamfer distance (tiles) from its center to the nearest non-land tile center or
    /// the outside of the map, minus half a tile, i.e. distance to the land edge. Corridor width ≈ 2 × clearance.</summary>
    /// <param name="land">Land grid.</param>
    internal static double[,] Clearance(bool[,] land)
    {
        int w = land.GetLength(0),
            h = land.GetLength(1);
        var dist = new double[w, h];
        var queue = new PriorityQueue<(int X, int Z), double>();
        for (var i = 0; i < w; i++)
        for (var j = 0; j < h; j++)
        {
            dist[i, j] = double.PositiveInfinity;
            if (!land[i, j])
            {
                dist[i, j] = 0;
                queue.Enqueue((i, j), 0);
            }
            else if (i == 0 || j == 0 || i == w - 1 || j == h - 1)
            {
                dist[i, j] = 1; // Outside-map ring counts as non-land one tile away.
                queue.Enqueue((i, j), 1);
            }
        }
        while (queue.TryDequeue(out var t, out var d))
        {
            if (d > dist[t.X, t.Z])
                continue;
            foreach (var (dx, dz) in Neighbours)
            {
                int nx = t.X + dx,
                    nz = t.Z + dz;
                if (nx < 0 || nz < 0 || nx >= w || nz >= h)
                    continue;
                var nd = d + (dx != 0 && dz != 0 ? Diagonal : 1);
                if (nd >= dist[nx, nz])
                    continue;
                dist[nx, nz] = nd;
                queue.Enqueue((nx, nz), nd);
            }
        }
        for (var i = 0; i < w; i++)
        for (var j = 0; j < h; j++)
            dist[i, j] = land[i, j] ? dist[i, j] - 0.5 : 0;
        return dist;
    }

    /// <summary>Widest-path (maximin clearance) bottleneck between every base pair: land tiles are activated in order of
    /// descending clearance and joined by union-find; the clearance at which two bases first connect is their bottleneck.</summary>
    /// <param name="land">Land grid.</param>
    /// <param name="clearance">Clearance per tile (<see cref="Clearance"/>).</param>
    /// <param name="bases">Base tiles.</param>
    /// <returns>[a,b] = (clearance, tile) or null when never connected.</returns>
    internal static (double Clearance, int X, int Z)?[,] Bottlenecks(
        bool[,] land,
        double[,] clearance,
        (int X, int Z)[] bases
    )
    {
        int w = land.GetLength(0),
            h = land.GetLength(1);
        var parent = new int[w * h];
        var active = new bool[w * h];
        var members = new Dictionary<int, List<int>>();
        for (var i = 0; i < parent.Length; i++)
            parent[i] = i;
        for (var b = 0; b < bases.Length; b++)
        {
            var key = bases[b].X * h + bases[b].Z;
            if (!members.TryGetValue(key, out var list))
                members[key] = list = [];
            list.Add(b);
        }
        int Find(int a)
        {
            while (parent[a] != a)
                a = parent[a] = parent[parent[a]];
            return a;
        }
        var result = new (double, int, int)?[bases.Length, bases.Length];
        var order = Enumerable
            .Range(0, w * h)
            .Where(k => land[k / h, k % h])
            .OrderByDescending(k => clearance[k / h, k % h])
            .ToArray();
        foreach (var k in order)
        {
            int x = k / h,
                z = k % h;
            active[k] = true;
            if (members.TryGetValue(k, out var own))
                for (var a = 0; a < own.Count; a++)
                for (var b = a + 1; b < own.Count; b++)
                    result[own[a], own[b]] = result[own[b], own[a]] = (clearance[x, z], x, z);
            foreach (var (dx, dz) in Neighbours)
            {
                if (!Step(land, x, z, dx, dz) || !active[(x + dx) * h + z + dz])
                    continue;
                int ra = Find(k),
                    rb = Find((x + dx) * h + z + dz);
                if (ra == rb)
                    continue;
                members.TryGetValue(ra, out var la);
                members.TryGetValue(rb, out var lb);
                if (la is not null && lb is not null)
                    foreach (var a in la)
                    foreach (var b in lb)
                        result[a, b] = result[b, a] = (clearance[x, z], x, z);
                parent[rb] = ra;
                if (lb is not null)
                {
                    if (la is null)
                        members[ra] = la = [];
                    la.AddRange(lb);
                    members.Remove(rb);
                }
            }
        }
        return result;
    }

    /// <summary>editor_land_analysis: land components, base connectivity, bottlenecks and territory from live tiles.</summary>
    /// <param name="game">Validated editor connection.</param>
    /// <param name="args">Optional area, bases, minComponentTiles, maxComponents.</param>
    object LandAnalysis(Game game, JsonElement args)
    {
        var view = EditorView.ReadView(game);
        var tiles = new LiveWorld.Tiles(game, view);
        var s = view.Scale;
        int x0 = 0,
            z0 = 0,
            x1 = tiles.TilesX - 1,
            z1 = tiles.TilesZ - 1;
        if (args.TryGetProperty("minX", out _))
        {
            x0 = Math.Clamp((int)(Dbl(args, "minX", 0) / s), 0, x1);
            z0 = Math.Clamp((int)(Dbl(args, "minZ", 0) / s), 0, z1);
            x1 = Math.Clamp((int)Math.Ceiling(Dbl(args, "maxX", 0) / s) - 1, x0, x1);
            z1 = Math.Clamp((int)Math.Ceiling(Dbl(args, "maxZ", 0) / s) - 1, z0, z1);
        }
        int w = x1 - x0 + 1,
            h = z1 - z0 + 1;
        var land = new bool[w, h];
        var wet = new bool[w, h];
        var classes = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < w; i++)
        for (var j = 0; j < h; j++)
        {
            var t = tiles.At(x0 + i, z0 + j);
            var c = tiles.Passability(t);
            classes[c] = classes.GetValueOrDefault(c) + 1;
            land[i, j] = c == "land";
            wet[i, j] = tiles.WaterName(t) is not null;
        }
        // Bases: explicit, else each player's first TownCenter (as editor_resource_balance).
        var bases = new List<(string Name, int? Player, double X, double Z)>();
        if (args.TryGetProperty("bases", out var be))
        {
            foreach (var b in be.EnumerateArray())
                bases.Add(
                    (
                        b.TryGetProperty("name", out var n)
                            ? n.GetString()!
                            : $"base{bases.Count + 1}",
                        b.TryGetProperty("player", out var p) ? p.GetInt32() : null,
                        b.GetProperty("x").GetDouble(),
                        b.GetProperty("z").GetDouble()
                    )
                );
        }
        else if (_layout.Units is not null)
            foreach (
                var tc in LiveUnits
                    .Read(game)
                    .Where(u =>
                        u.Player > 0
                        && (u.Proto ?? "").StartsWith(
                            "TownCenter",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    .GroupBy(u => u.Player)
                    .OrderBy(g => g.Key)
                    .Take(MaxBases)
            )
                bases.Add(
                    (
                        $"player{tc.Key} TownCenter {tc.First().UnitId}",
                        tc.Key,
                        tc.First().Position.X,
                        tc.First().Position.Z
                    )
                );
        var baseTiles = new List<(int X, int Z)>();
        var baseInfo = new List<object>();
        foreach (var b in bases)
        {
            int bx = Math.Clamp((int)(b.X / s) - x0, 0, w - 1),
                bz = Math.Clamp((int)(b.Z / s) - z0, 0, h - 1);
            var onLand = land[bx, bz];
            if (!onLand)
            {
                var near = Enumerable
                    .Range(-BaseSnapTiles, 2 * BaseSnapTiles + 1)
                    .SelectMany(dx =>
                        Enumerable
                            .Range(-BaseSnapTiles, 2 * BaseSnapTiles + 1)
                            .Select(dz => (dx, dz))
                    )
                    .Where(d =>
                        bx + d.dx >= 0
                        && bz + d.dz >= 0
                        && bx + d.dx < w
                        && bz + d.dz < h
                        && land[bx + d.dx, bz + d.dz]
                    )
                    .OrderBy(d => d.dx * d.dx + d.dz * d.dz)
                    .Select(d => ((int, int)?)(bx + d.dx, bz + d.dz))
                    .FirstOrDefault();
                if (near is { } nt)
                    (bx, bz) = nt;
            }
            baseTiles.Add((bx, bz));
            baseInfo.Add(
                new
                {
                    name = b.Name,
                    player = b.Player,
                    x = b.X,
                    z = b.Z,
                    onLand,
                    tile = new[] { x0 + bx, z0 + bz },
                    snappedToLand = !onLand && land[bx, bz],
                }
            );
        }
        var (label, sizes) = Components(land, true);
        var speck = Int(args, "minComponentTiles", DefaultSpeckTiles);
        var maxComponents = Int(args, "maxComponents", DefaultMaxComponents);
        var bbox = new (int MinX, int MinZ, int MaxX, int MaxZ, double Sx, double Sz)[sizes.Count];
        for (var k = 0; k < bbox.Length; k++)
            bbox[k] = (int.MaxValue, int.MaxValue, 0, 0, 0, 0);
        for (var i = 0; i < w; i++)
        for (var j = 0; j < h; j++)
            if (label[i, j] is var id and >= 0)
            {
                var b = bbox[id];
                bbox[id] = (
                    Math.Min(b.MinX, i),
                    Math.Min(b.MinZ, j),
                    Math.Max(b.MaxX, i),
                    Math.Max(b.MaxZ, j),
                    b.Sx + i,
                    b.Sz + j
                );
            }
        var area = s * s;
        var components = Enumerable
            .Range(0, sizes.Count)
            .Where(id => sizes[id] > speck)
            .OrderByDescending(id => sizes[id])
            .Take(maxComponents)
            .Select(id => new
            {
                tiles = sizes[id],
                areaWorld = sizes[id] * area,
                bbox = new
                {
                    minX = (x0 + bbox[id].MinX) * s,
                    minZ = (z0 + bbox[id].MinZ) * s,
                    maxX = (x0 + bbox[id].MaxX + 1) * s,
                    maxZ = (z0 + bbox[id].MaxZ + 1) * s,
                },
                centroid = new[]
                {
                    Math.Round((x0 + bbox[id].Sx / sizes[id] + 0.5) * s, 1),
                    Math.Round((z0 + bbox[id].Sz / sizes[id] + 0.5) * s, 1),
                },
                bases = baseTiles
                    .Select((t, k) => (t, k))
                    .Where(p => label[p.t.X, p.t.Z] == id)
                    .Select(p => bases[p.k].Name)
                    .ToArray(),
            })
            .ToArray();
        var specks = Enumerable.Range(0, sizes.Count).Where(id => sizes[id] <= speck).ToArray();
        var (_, waterSizes) = Components(wet, false);
        var clearance = Clearance(land);
        var bottlenecks = Bottlenecks(land, clearance, [.. baseTiles]);
        var perBase = baseTiles.Select(t => LandDistances(land, [t]).Distance).ToArray();
        var (_, owner) = LandDistances(land, [.. baseTiles]);
        var territory = new int[baseTiles.Count];
        for (var i = 0; i < w; i++)
        for (var j = 0; j < h; j++)
            if (owner[i, j] >= 0)
                territory[owner[i, j]]++;
        var pairs = new List<object>();
        for (var a = 0; a < baseTiles.Count; a++)
        for (var b = a + 1; b < baseTiles.Count; b++)
        {
            var d = perBase[a][baseTiles[b].X, baseTiles[b].Z];
            var bn = bottlenecks[a, b];
            pairs.Add(
                new
                {
                    a = bases[a].Name,
                    b = bases[b].Name,
                    connected = !double.IsInfinity(d),
                    landPathWorld = double.IsInfinity(d) ? (double?)null : Math.Round(d * s, 1),
                    straightWorld = Math.Round(
                        Math.Sqrt(
                            Math.Pow(bases[a].X - bases[b].X, 2)
                                + Math.Pow(bases[a].Z - bases[b].Z, 2)
                        ),
                        1
                    ),
                    bottleneckWidthWorld = bn is { } n
                        ? Math.Round(2 * n.Clearance * s, 1)
                        : (double?)null,
                    bottleneckAt = bn is { } m
                        ? new[] { (x0 + m.X + 0.5) * s, (z0 + m.Z + 0.5) * s }
                        : null,
                }
            );
        }
        // Water-corner rule (research/LIVE-WORLD.md / Wake Island session): water flags vs corner node heights.
        var heights = Nodes(view, x0, z0, x1 + 1, z1 + 1);
        int waterAllAbove = 0,
            dryBelow = 0;
        var anomalies = new List<object>();
        float? Surface(int i, int j) =>
            i >= 0 && j >= 0 && i < w && j < h && wet[i, j]
                ? view.WaterSurface((x0 + i + 0.5) * s, (z0 + j + 0.5) * s)
                : null;
        for (var i = 0; i < w; i++)
        for (var j = 0; j < h; j++)
        {
            var corners = new[]
            {
                heights.At(x0 + i, z0 + j),
                heights.At(x0 + i + 1, z0 + j),
                heights.At(x0 + i, z0 + j + 1),
                heights.At(x0 + i + 1, z0 + j + 1),
            };
            if (wet[i, j])
            {
                var nearDry = Neighbours[..4]
                    .Any(n =>
                        i + n.Dx >= 0
                        && j + n.Dz >= 0
                        && i + n.Dx < w
                        && j + n.Dz < h
                        && !wet[i + n.Dx, j + n.Dz]
                    );
                if (nearDry && Surface(i, j) is { } ws && corners.All(c => c > ws))
                {
                    waterAllAbove++;
                    if (anomalies.Count < LandListLimit)
                        anomalies.Add(
                            new
                            {
                                kind = "waterFlagAllCornersAbove",
                                x = (x0 + i) * s,
                                z = (z0 + j) * s,
                                surface = ws,
                            }
                        );
                }
            }
            else
            {
                var neighbour = Neighbours[..4]
                    .Select(n => Surface(i + n.Dx, j + n.Dz))
                    .FirstOrDefault(v => v is not null);
                if (neighbour is { } ns && corners.Any(c => c < ns))
                {
                    dryBelow++;
                    if (anomalies.Count < LandListLimit)
                        anomalies.Add(
                            new
                            {
                                kind = "dryWithCornerBelowSurface",
                                x = (x0 + i) * s,
                                z = (z0 + j) * s,
                                surface = ns,
                            }
                        );
                }
            }
        }
        return new
        {
            area = new
            {
                minX = x0 * s,
                minZ = z0 * s,
                maxX = (x1 + 1) * s,
                maxZ = (z1 + 1) * s,
                tilesX = w,
                tilesZ = h,
                tileSize = s,
            },
            tileClasses = classes,
            landComponents = new
            {
                count = sizes.Count(n => n > speck),
                listed = components,
                specks = new
                {
                    count = specks.Length,
                    threshold = speck,
                    tiles = specks.Sum(id => sizes[id]),
                    first = specks
                        .Take(LandListLimit)
                        .Select(id =>
                            new[]
                            {
                                Math.Round((x0 + bbox[id].Sx / sizes[id] + 0.5) * s, 1),
                                Math.Round((z0 + bbox[id].Sz / sizes[id] + 0.5) * s, 1),
                            }
                        )
                        .ToArray(),
                },
            },
            waterComponents = new
            {
                count = waterSizes.Count,
                largestTiles = waterSizes.Count > 0 ? waterSizes.Max() : 0,
            },
            bases = baseInfo,
            pairs,
            territory = baseTiles
                .Select(
                    (_, k) =>
                        new
                        {
                            basis = bases[k].Name,
                            tiles = territory[k],
                            areaWorld = territory[k] * area,
                        }
                )
                .ToArray(),
            territoryBalance = territory.Length > 1 && territory.Max() > 0
                ? Math.Round((double)territory.Min() / territory.Max(), 3)
                : (double?)null,
            waterCornerRule = new
            {
                waterTilesWithAllCornersAboveSurface = waterAllAbove,
                dryTilesWithCornerBelowAdjacentSurface = dryBelow,
                first = anomalies,
            },
            note = bases.Count == 0
                ? "No bases: pass bases [{name?,player?,x,z}] or place TownCenters."
                : null,
            limitation = "Read-only. Land = tile passability class 'land' (PassableLand/Shoreline group without water flag; LiveWorld.Passability); water, impassable and ice are barriers. 8-connected with no diagonal corner cutting. Distances and Voronoi territory are tile-center path lengths over land, bottleneck = widest-path minimum clearance (2 × distance to the land edge, outside of the map counts as edge). Not unit obstruction, pathing classes, cliffs/forest objects or buildings.",
        };
    }

    /// <summary>Self-test of the pure shape and land-analysis helpers (no game).</summary>
    internal static void ShapeSelfTest()
    {
        static void Check(bool ok, string what)
        {
            if (!ok)
                throw new InvalidDataException("Shape self-test failed: " + what);
        }
        // Map size math (live 2026-10-21: add right 20 on 128x128 tiles of size 2 gave 138x128).
        Check(
            ExpectedTiles(128, 128, 2, "add", new Dictionary<string, int> { ["right"] = 20 })
                == (138, 128),
            "map size add right"
        );
        Check(
            ExpectedTiles(
                133,
                128,
                2,
                "take",
                new Dictionary<string, int> { ["left"] = 10, ["top"] = 4 }
            ) == (128, 126),
            "map size take left/top"
        );
        // Shapes.
        var circle = new ShapeSpec("circle", [], 10, 10, 5, 0, 0, 0);
        Check(circle.Inside(10, 14.9) && !circle.Inside(10, 15.1), "circle");
        var ellipse = new ShapeSpec("ellipse", [], 0, 0, 0, 10, 2, Math.PI / 2);
        Check(ellipse.Inside(0, 9) && !ellipse.Inside(9, 0), "rotated ellipse");
        var (eMinX, eMinZ, eMaxX, eMaxZ) = ellipse.Bounds();
        Check(
            Math.Abs(eMaxX - 2) < 1e-9
                && Math.Abs(eMinX + 2) < 1e-9
                && Math.Abs(eMaxZ - 10) < 1e-9
                && Math.Abs(eMinZ + 10) < 1e-9,
            "rotated ellipse bounds"
        );
        var capsule = new ShapeSpec("capsule", [(0, 0), (10, 0)], 0, 0, 2, 0, 0, 0);
        Check(
            capsule.Inside(5, 1.9) && !capsule.Inside(5, 2.1) && capsule.Inside(11.5, 0),
            "capsule"
        );
        var square = new ShapeSpec("polygon", [(0, 0), (4, 0), (4, 4), (0, 4)], 0, 0, 0, 0, 0, 0);
        Check(square.Inside(2, 2) && !square.Inside(5, 2), "polygon");
        // Mirror: point mirror of (2,2) in a 100×100 map is (98,98).
        Check(MirrorPoint("point", 2, 2, 100, 100) == (98, 98), "mirror");
        // U shape on nodes: arms 0..8 and 22..30, bottom bar z 0..8.
        var u = new bool[31, 31];
        for (var i = 0; i <= 30; i++)
        for (var j = 0; j <= 30; j++)
            u[i, j] = i <= 8 || i >= 22 || j <= 8;
        var eroded = Erode(u, BrushErodeNodes);
        Check(eroded[4, 20] && !eroded[1, 20] && !eroded[12, 12], "erode");
        var strokes = ScanStrokes(eroded, ShapeRowStepNodes);
        Check(strokes.Count > 1, "concave split");
        foreach (var stroke in strokes)
            for (var k = 0; k + 1 < stroke.Count; k++)
                Check(SegmentInside(eroded, stroke[k], stroke[k + 1]), "stroke inside");
        var covered = strokes.SelectMany(st => st).Select(p => (int)p.I).Distinct().ToHashSet();
        Check(covered.Contains(2) && covered.Contains(28), "rows reach both arms");
        // Cells: clipped strokes stay inside their cell, together they reach every eroded node row, and splitting
        // covers a cell exactly.
        var cells = Cells(eroded, MinShapeCellNodes * 2);
        Check(cells.Count > 1, "cells");
        var cellRows = new HashSet<int>();
        foreach (var cell in cells)
        foreach (var st in ScanStrokes(Clip(eroded, cell), ShapeRowStepNodes))
        foreach (var (pi, pj) in st)
        {
            Check(
                pi >= cell.I0 && pi <= cell.I1 && pj >= cell.J0 && pj <= cell.J1,
                "clipped stroke inside cell"
            );
            cellRows.Add((int)pi);
        }
        for (var i = 0; i < eroded.GetLength(0); i++)
            if (Enumerable.Range(0, eroded.GetLength(1)).Any(j => eroded[i, j]))
                Check(
                    cellRows.Contains(i) || cellRows.Contains(i - 1) || cellRows.Contains(i + 1),
                    "cell rows cover mask"
                );
        var parts = SplitCell((0, 0, 15, 9), MinShapeCellNodes);
        Check(
            parts.Length == 4
                && parts.Sum(c => (c.I1 - c.I0 + 1) * (c.J1 - c.J0 + 1)) == 16 * 10
                && SplitCell(
                    (0, 0, MinShapeCellNodes - 1, MinShapeCellNodes - 1),
                    MinShapeCellNodes
                ).Length == 0,
            "split cell"
        );
        Check(
            FindSquare(u, ShapeSampleNodes) is { } sq
                && Enumerable
                    .Range(0, ShapeSampleNodes)
                    .All(a =>
                        Enumerable.Range(0, ShapeSampleNodes).All(b => u[sq.I + a, sq.J + b])
                    ),
            "square"
        );
        Check(FindSquare(new bool[6, 6], ShapeSampleNodes) is null, "no square");
        // Land: two 15-tile-wide islands joined by a 3-wide causeway.
        var land = new bool[40, 20];
        for (var i = 0; i < 40; i++)
        for (var j = 0; j < 20; j++)
            land[i, j] = i < 15 || i >= 25 || (j >= 8 && j <= 10);
        (int X, int Z)[] bases = [(5, 10), (34, 10)];
        Check(Components(land, true).Sizes.Count == 1, "one component");
        var bottleneck = Bottlenecks(land, Clearance(land), bases)[0, 1];
        Check(
            bottleneck is { } bn && Math.Abs(2 * bn.Clearance - 3) < 1e-9 && bn.Z == 9,
            "causeway width"
        );
        var (dist, owner) = LandDistances(land, bases);
        Check(
            Math.Abs(LandDistances(land, [bases[0]]).Distance[34, 10] - 29) < 1e-9,
            "path length"
        );
        int left = 0,
            right = 0;
        foreach (var o in owner)
            if (o == 0)
                left++;
            else if (o == 1)
                right++;
        Check(left == right && dist[20, 9] < double.PositiveInfinity, "voronoi");
        // Cut the causeway: two islands, no path, no bottleneck; diagonal-only contact does not connect.
        for (var j = 8; j <= 10; j++)
            land[20, j] = false;
        Check(Components(land, true).Sizes.Count == 2, "two islands");
        Check(Bottlenecks(land, Clearance(land), bases)[0, 1] is null, "no bottleneck");
        var diagonal = new bool[2, 2];
        diagonal[0, 0] = diagonal[1, 1] = true;
        Check(Components(diagonal, true).Sizes.Count == 2, "corner rule");
    }

    /// <summary>Tool specs of the map-building tools (yielded from WorldExtras).</summary>
    /// <param name="coord">World coordinate schema.</param>
    /// <param name="point">[x,z] point schema.</param>
    /// <param name="area">minX/minZ/maxX/maxZ schema properties.</param>
    static IEnumerable<object> ShapeExtras(
        object coord,
        object point,
        Dictionary<string, object> area
    )
    {
        object Points(int min, int max) =>
            new
            {
                type = "array",
                minItems = min,
                maxItems = max,
                items = point,
            };
        var height = new
        {
            type = "number",
            minimum = -100,
            maximum = 200,
        };
        var mirror = new { type = "string", @enum = MirrorModes };
        var tolerance = new
        {
            type = "number",
            minimum = 0.001,
            maximum = 2,
        };
        var radius = new
        {
            type = "number",
            minimum = 0.5,
            maximum = 500,
        };
        var meters = new
        {
            type = "integer",
            minimum = 0,
            maximum = MaxMapSizeMeters,
        };
        yield return Spec(
            "editor_project",
            "World↔screen projection for planning strokes. points [[x,z],...] (1..256) → client pixel, terrain height, water surface, inFront/inViewport, blockedBy (UI panel name or margin) and clickable (the same visibility/UI-panel/ray back-check the brush tools use, with why when not). height = project on that plane instead of terrain. pixels [[px,py],...] (1..64) → ground hit (terrain under water), water surface, insideMap, blockedBy. Also uiKind (normal/alternative pixel gates), camera pose and the largest UI-free client rect. Read-only.",
            new Dictionary<string, object>
            {
                ["points"] = Points(1, MaxProjectPoints),
                ["pixels"] = new
                {
                    type = "array",
                    minItems = 1,
                    maxItems = MaxProjectPixels,
                    items = new
                    {
                        type = "array",
                        minItems = 2,
                        maxItems = 2,
                        items = new
                        {
                            type = "number",
                            minimum = 0,
                            maximum = MaxClientPixel,
                        },
                    },
                },
                ["height"] = height,
            },
            [],
            true
        );
        yield return Spec(
            "editor_brush",
            "Read or set the brush of an edit tool, verified from memory. No args: active tool's brush size/type. mode=<brush tool> enters it (exits again unless keepMode); size=1..50 runs uiChangeBrushSize and polls the readback (refuses BRUSH_SIZE_NOT_APPLIED with the read value when the engine clamps/ignores it); shape=circular|Rectangular|HollowSquare runs uiChangeBrushType. Each edit mode keeps its own size. Not undoable; scene unchanged.",
            new Dictionary<string, object>
            {
                ["mode"] = new { type = "string", @enum = BrushModes },
                ["size"] = new
                {
                    type = "integer",
                    minimum = 1,
                    maximum = MaxBrushSize,
                },
                ["shape"] = new { type = "string", @enum = BrushShapes },
                ["keepMode"] = new { type = "boolean" },
            },
            []
        );
        yield return Spec(
            "editor_stroke",
            "One WORLD polyline (1..256 [x,z]) with an elevation-family tool: raise/lower (elevation tool left/right drag), smooth, roughen, flatten (elevationsample: samples at referenceX/Z, default first vertex, then drags at that height; or height=<exact> builds the sample with a size-3 bump + test dab at the first vertex (as editor_elevation set; brushes restored), drags over it and dabs leftover bump nodes, sampleTouchUps). passes 1..10 (alternating direction), optional brushSize (verified), mirror point|flipX|flipZ|swapXZ about the map center (adds the mirrored stroke), durationMs >=800 (shorter drags painted nothing live). Camera moves/splits strokes as needed (UI-panel aware). Verifies node raises/lowers and water-flag changes in the stroke bbox; strokes = undo count. confirmDestructive=true required.",
            new Dictionary<string, object>
            {
                ["tool"] = new { type = "string", @enum = StrokeTools },
                ["points"] = Points(1, MaxStrokeVertices),
                ["passes"] = new
                {
                    type = "integer",
                    minimum = 1,
                    maximum = MaxStrokePasses,
                },
                ["brushSize"] = new
                {
                    type = "integer",
                    minimum = 1,
                    maximum = MaxBrushSize,
                },
                ["mirror"] = mirror,
                ["referenceX"] = coord,
                ["referenceZ"] = coord,
                ["height"] = height,
                ["tolerance"] = tolerance,
                ["durationMs"] = new
                {
                    type = "integer",
                    minimum = MinStrokeMs,
                    maximum = MaxStrokeMs,
                },
                ["moveCamera"] = new { type = "boolean" },
                ["confirmDestructive"] = new { type = "boolean" },
            },
            StrokeRequired,
            destructive: true
        );
        yield return Spec(
            "editor_terrain_shape",
            "Fill a WORLD shape with an exact node height (islands, lakes, plateaus): shape=polygon (points 3..64) | circle (x,z,radius) | ellipse (x,z,radiusX,radiusZ,angleDegrees) | capsule (points 2..64 polyline + radius). Any size in one call: the brush-eroded shape is tiled into view cells (32 nodes, split down to 8 when needed) and a camera-only preflight aims every cell before the first edit (zoom-out to 130, then steeper pitch 60/75°, pitch restored at the end; yaw resets to default); unreachable cells refuse TARGET_NOT_CLICKABLE phase=preflight with 0 edits. One exact-height sample loop per call (7×7-node square inside the shape, default zoom; refuses AREA_TOO_SMALL before input); the mirror instance and all cells reuse the sampled height (no seams). Two passes of elevation-sample scanline strokes per cell, touch-up dabs. A mid-run failure reports undoSteps and idempotentRetry=true (re-running the same call is safe). Optional water=<water type> paints water over it; mirror adds the mirrored shape. Verifies inside nodes (tolerance default 0.05), bleed in a 3-node ring, inside tile passability. edits = undo count. confirmDestructive=true required.",
            new Dictionary<string, object>
            {
                ["shape"] = new { type = "string", @enum = ShapeKinds },
                ["points"] = Points(2, MaxShapeVertices),
                ["x"] = coord,
                ["z"] = coord,
                ["radius"] = radius,
                ["radiusX"] = radius,
                ["radiusZ"] = radius,
                ["angleDegrees"] = new
                {
                    type = "number",
                    minimum = -360,
                    maximum = 360,
                },
                ["height"] = height,
                ["water"] = new { type = "string" },
                ["mirror"] = mirror,
                ["tolerance"] = tolerance,
                ["moveCamera"] = new { type = "boolean" },
                ["confirmDestructive"] = new { type = "boolean" },
            },
            ShapeRequired,
            destructive: true
        );
        yield return Spec(
            "editor_land_analysis",
            "Land connectivity report from live tiles (whole map or minX/minZ/maxX/maxZ): land components (8-connected, no diagonal corner cutting; tiles, area, bbox, centroid, bases), specks <= minComponentTiles (default 4), water components; per base pair (bases [{name?,player?,x,z}] <=12, default each player's first TownCenter): connected, land path length, straight distance, bottleneck width (widest path) and location; path-distance Voronoi territory per base and balance; water-corner-rule anomalies (water tiles with all corners above the surface, dry tiles with a corner below adjacent water). Read-only.",
            new Dictionary<string, object>(area)
            {
                ["bases"] = new
                {
                    type = "array",
                    minItems = 1,
                    maxItems = MaxBases,
                    items = new
                    {
                        type = "object",
                        properties = new Dictionary<string, object>
                        {
                            ["name"] = new { type = "string" },
                            ["player"] = new
                            {
                                type = "integer",
                                minimum = 0,
                                maximum = LiveUnits.MaxPlayer,
                            },
                            ["x"] = coord,
                            ["z"] = coord,
                        },
                        required = BaseRequired,
                        additionalProperties = false,
                    },
                },
                ["minComponentTiles"] = new
                {
                    type = "integer",
                    minimum = 0,
                    maximum = MaxSpeckTiles,
                },
                ["maxComponents"] = new
                {
                    type = "integer",
                    minimum = 1,
                    maximum = MaxListedComponents,
                },
            },
            [],
            true
        );
        yield return Spec(
            "editor_map_size",
            "Map size. operation=get (default): live tile dims/world size (readDialog=true also OCRs the dialog). add|take: per-side meters right (+X), left (-X), top (+Z), bottom (-Z) (multiples of the tile size) typed into the World > Map Size dialog, Add/Take clicked, live tile dims verified (MAP_SIZE_MISMATCH otherwise), dialog closed. New strips copy edge terrain. Not proven undoable: reverse with the opposite operation. confirmDestructive=true required for add/take.",
            new Dictionary<string, object>
            {
                ["operation"] = new { type = "string", @enum = MapSizeOperations },
                ["right"] = meters,
                ["left"] = meters,
                ["top"] = meters,
                ["bottom"] = meters,
                ["readDialog"] = new { type = "boolean" },
                ["confirmDestructive"] = new { type = "boolean" },
            },
            [],
            destructive: true
        );
        yield return Spec(
            "editor_new_scenario",
            "Create a new scenario through File > New Scenario: type (default Blank, or a random map script name such as acropolis; checked against game/random_maps), players 1..12, seed, size normal|large|giant, then Generate. REPLACES the open scenario (unsaved work lost; save a checkpoint first). Verifies a new terrain was created and reports tile dims and live player count. confirmDestructive=true required.",
            new Dictionary<string, object>
            {
                ["type"] = new { type = "string" },
                ["players"] = new
                {
                    type = "integer",
                    minimum = 1,
                    maximum = LiveUnits.MaxPlayer,
                },
                ["seed"] = new { type = "integer", minimum = -1 },
                ["size"] = new { type = "string", @enum = ScenarioSizes },
                ["confirmDestructive"] = new { type = "boolean" },
            },
            NewScenarioRequired,
            destructive: true
        );
    }

    /// <summary>Required keys of an editor_land_analysis base.</summary>
    static readonly string[] BaseRequired = ["x", "z"];

    /// <summary>Required arguments of editor_new_scenario.</summary>
    static readonly string[] NewScenarioRequired = ["confirmDestructive"];

    /// <summary>Largest client pixel coordinate accepted by editor_project (host bound above 2560×1440 clients).</summary>
    const int MaxClientPixel = 8192;

    /// <summary>Largest speck threshold (host bound).</summary>
    const int MaxSpeckTiles = 10000;

    /// <summary>Most listed land components (host output bound).</summary>
    const int MaxListedComponents = 100;
}
