using System.Globalization;
using System.Text.Json;

namespace AomMcp;

/// <summary>editor_paint_area: fills world shapes with texture, water or forest. Plans brush-eroded serpentine strokes on
/// the tile grid, paints them with the native paint tools (UI drags only), verifies tile/object coverage and leakage from
/// live memory reads, and touches up uncovered tiles with a smaller brush.</summary>
internal sealed partial class Server
{
    /// <summary>editor_paint_area kinds (host API subset of <see cref="PaintKinds"/> with tile readback).</summary>
    static readonly string[] AreaPaintKinds = ["texture", "mix", "water", "forest"];

    /// <summary>band.distanceTo values: distance from live water tiles or from dry (land) tiles.</summary>
    static readonly string[] BandTargets = ["water", "land"];

    /// <summary>Required properties of the band argument.</summary>
    static readonly string[] BandRequired = ["distanceTo", "maxTiles"];

    /// <summary>Largest band distance in tiles (host bound: wider bands are ordinary shapes).</summary>
    const int MaxBandTiles = 32;

    /// <summary>Directory below generated/data holding the exported mix definitions (editor_generate_catalog).</summary>
    const string MixDirectory = "mixes";

    /// <summary>Required arguments of editor_paint_area.</summary>
    static readonly string[] PaintAreaRequired = ["kind", "type", "shapes"];

    /// <summary>Most include/exclude shapes per call (host bound).</summary>
    const int MaxAreaShapes = 16;

    /// <summary>Texture/forest brush disc radius in tiles per brush size step. Live 2026-10-22 (1440×900 alternative UI,
    /// single 800 ms dabs): sizes 6/10/20 changed 30/78/257 tiles (radius 3.1/5.0/9.0 tiles); forest added one tree per
    /// changed tile with the same footprint (research/LIVE-WORLD.md "Paint brush footprint").</summary>
    const double AreaBrushTilesPerSize = 0.45;

    /// <summary>Water paint half-width in tiles: a GreekLake stroke was 14 world units (7 tiles) wide at brush sizes 1
    /// and 10 alike; paintWater ignores the brush size (live 2026-10-22, research/LIVE-WORLD.md).</summary>
    const double WaterBrushTiles = 3.5;

    /// <summary>Largest brush size the planner picks or accepts (host bound: largest live-measured size).</summary>
    const int MaxAreaBrushSize = 20;

    /// <summary>Scanline row spacing in brush radii (host choice: 1.5 radii leaves overlap between disc rows).</summary>
    const double AreaRowStepRadii = 1.5;

    /// <summary>Planner target: largest brush whose predicted coverage reaches this fraction (host choice; the
    /// touch-up passes cover the edge band the <see cref="AreaEdgeMarginTiles"/> margin leaves).</summary>
    const double AreaTargetCoverage = 0.75;

    /// <summary>Most touch-up passes (plans/wake-session-2-fixes.md E): pass 1 at half the brush, pass 2 at size 1
    /// (one tile, live 2026-10-22), which reaches every area tile without leaking.</summary>
    const int MaxAreaTouchUpPasses = 2;

    /// <summary>Drag duration per world unit of stroke (host choice: about the 800 ms per 30 units that painted
    /// continuously live).</summary>
    const double AreaMsPerWorldUnit = 25;

    /// <summary>Most planned stroke chunks per apply (host bound; preview reports larger plans with a hint).</summary>
    const int MaxAreaStrokes = 200;

    /// <summary>Segment sampling step in tiles for the coverage prediction (host choice).</summary>
    const double AreaSampleTiles = 0.25;

    /// <summary>Listed uncovered/leaked tiles per report (host output bound).</summary>
    const int AreaListLimit = 20;

    /// <summary>Planned strokes listed in a preview (host output bound).</summary>
    const int AreaPreviewStrokes = 16;

    /// <summary>Extra erosion beyond the disc radius for brushes wider than one tile: live 2026-10-22 a size-5 texture
    /// fill (radius model 2.25 tiles) still changed 14 tiles one tile outside a 144-tile circle.</summary>
    const double AreaEdgeMarginTiles = 1;

    /// <summary>Brush centres allowed for a radius: area tiles whose disc plus <see cref="AreaEdgeMarginTiles"/> stays
    /// inside. No margin for single-tile brushes or water (the 2026-10-22 GreekLake band edges were exact).</summary>
    static bool[,] AreaCentres(bool[,] inside, string kind, double radius) =>
        Erode(inside, radius < 1 || kind == "water" ? radius : radius + AreaEdgeMarginTiles);

    /// <summary>Water pick tolerance in world units: live 2026-10-22 a touch-up target on a freshly lowered lake bed
    /// ray-hit the shore 2.5 units away (TARGET_NOT_CLICKABLE at the default 2). 4 units (2 tiles) stays well inside
    /// the 7-unit half band; leakage is still verified from tiles afterwards.</summary>
    const double WaterPickTolerance = 4;

    /// <summary>Extra tiles around the brush radius scanned for leakage.</summary>
    const int AreaLeakMarginTiles = 2;

    /// <summary>Percent scale for coverage reports.</summary>
    const double Percent = 100;

    /// <summary>Offset from a tile's corner to its centre, in tiles.</summary>
    const double TileCentre = 0.5;

    /// <summary>Preflight for editor_paint_area (no game connection).</summary>
    /// <param name="args">Tool arguments (schema-validated).</param>
    static void PreflightPaintArea(JsonElement args)
    {
        if (!LayoutPreview(args))
            EditorFiles.Confirm(args, "confirmDestructive");
        foreach (var key in new[] { "shapes", "exclude" })
            if (args.TryGetProperty(key, out var list))
            {
                if (list.GetArrayLength() > MaxAreaShapes)
                    throw new ArgumentException($"{key}: at most {MaxAreaShapes} shapes.");
                foreach (var shape in list.EnumerateArray())
                    _ = ParseShape(shape);
            }
        if (args.GetProperty("shapes").GetArrayLength() == 0)
            throw new ArgumentException("shapes: at least one shape.");
        if (args.TryGetProperty("band", out var band))
            _ = ParseBand(band);
    }

    /// <summary>Coast band filter: area tiles whose 8-connected tile distance to the nearest live water (or land) tile
    /// lies in [Min, Max]; distance 0 is a water (land) tile itself.</summary>
    /// <param name="To">"water" or "land".</param>
    /// <param name="Min">Smallest distance in tiles.</param>
    /// <param name="Max">Largest distance in tiles.</param>
    internal sealed record Band(string To, int Min, int Max);

    /// <summary>Parses and checks the band argument.</summary>
    /// <param name="band">band object.</param>
    static Band ParseBand(JsonElement band)
    {
        Catalog.ValidateObject(band, ["distanceTo", "minTiles", "maxTiles"]);
        var to = band.TryGetProperty("distanceTo", out var t) ? t.GetString() : null;
        if (to is null || !BandTargets.Contains(to, StringComparer.Ordinal))
            throw new ArgumentException("band.distanceTo: water or land.");
        var min = band.TryGetProperty("minTiles", out var lo) ? lo.GetInt32() : 0;
        var max = band.TryGetProperty("maxTiles", out var hi)
            ? hi.GetInt32()
            : throw new ArgumentException("band.maxTiles required.");
        if (min < 0 || max > MaxBandTiles || min > max)
            throw new ArgumentException($"band: 0 <= minTiles <= maxTiles <= {MaxBandTiles}.");
        return new(to, min, max);
    }

    /// <summary>Multi-source 8-connected (Chebyshev step) tile distance from every source tile; int.MaxValue where no
    /// source exists.</summary>
    /// <param name="source">Source tiles (distance 0).</param>
    internal static int[,] TileDistances(bool[,] source)
    {
        int w = source.GetLength(0),
            h = source.GetLength(1);
        var distance = new int[w, h];
        var queue = new Queue<(int X, int Z)>();
        for (var x = 0; x < w; x++)
        for (var z = 0; z < h; z++)
        {
            distance[x, z] = source[x, z] ? 0 : int.MaxValue;
            if (source[x, z])
                queue.Enqueue((x, z));
        }
        while (queue.Count > 0)
        {
            var (x, z) = queue.Dequeue();
            for (var dx = -1; dx <= 1; dx++)
            for (var dz = -1; dz <= 1; dz++)
            {
                int nx = x + dx,
                    nz = z + dz;
                if (nx < 0 || nz < 0 || nx >= w || nz >= h || distance[nx, nz] != int.MaxValue)
                    continue;
                distance[nx, nz] = distance[x, z] + 1;
                queue.Enqueue((nx, nz));
            }
        }
        return distance;
    }

    /// <summary>Keeps only area tiles inside the band (live water grid at call time).</summary>
    /// <param name="mask">Area mask.</param>
    /// <param name="tiles">Live tiles.</param>
    /// <param name="band">Band filter.</param>
    /// <returns>Filtered mask and the number of area tiles the band removed.</returns>
    static (AreaMask Mask, int Removed) ApplyBand(AreaMask mask, LiveWorld.Tiles tiles, Band band)
    {
        var source = new bool[tiles.TilesX, tiles.TilesZ];
        for (var x = 0; x < tiles.TilesX; x++)
        for (var z = 0; z < tiles.TilesZ; z++)
            source[x, z] = (tiles.WaterName(tiles.At(x, z)) is not null) == (band.To == "water");
        var distance = TileDistances(source);
        var inside = (bool[,])mask.Inside.Clone();
        var removed = 0;
        for (var i = 0; i < mask.W; i++)
        for (var j = 0; j < mask.H; j++)
        {
            if (!inside[i, j])
                continue;
            int x = mask.X0 + i,
                z = mask.Z0 + j;
            var d =
                x >= 0 && z >= 0 && x < tiles.TilesX && z < tiles.TilesZ
                    ? distance[x, z]
                    : int.MaxValue;
            if (d >= band.Min && d <= band.Max)
                continue;
            inside[i, j] = false;
            removed++;
        }
        return (mask with { Inside = inside }, removed);
    }

    /// <summary>Live texture names a mix paints: its &lt;paint&gt;&lt;texture&gt; paths (generated/data/mixes) matched
    /// to live texture names by normalized file name ("greek\greek_grass_rocks_2" ↔ "Greek Grass Rocks 2").</summary>
    /// <param name="title">Mix title.</param>
    /// <param name="names">Live catalogs.</param>
    /// <returns>Mapped live names and the mix texture paths without a live name.</returns>
    static (HashSet<string> Live, string[] Unmapped) MixTextures(
        string title,
        LiveWorld.Catalogs names
    )
    {
        var live = new HashSet<string>(StringComparer.Ordinal);
        var unmapped = new List<string>();
        var byNorm = names
            .Subtypes.SelectMany(n => n)
            .Where(n => n.Length > 0)
            .GroupBy(Norm)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        for (DirectoryInfo? d = new(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var dir = Path.Combine(d.FullName, "generated", "data", MixDirectory);
            if (!Directory.Exists(dir))
                continue;
            foreach (var file in Directory.GetFiles(dir, "*.xml"))
            {
                var root = System.Xml.Linq.XDocument.Load(file).Root;
                if (root?.Element("title")?.Value.Trim() != title)
                    continue;
                foreach (var t in root.Element("paint")?.Elements("texture") ?? [])
                {
                    var path = t.Value.Trim();
                    var leaf = path[(path.LastIndexOfAny(['\\', '/']) + 1)..];
                    if (byNorm.TryGetValue(Norm(leaf), out var name))
                        live.Add(name);
                    else
                        unmapped.Add(path);
                }
            }
            break;
        }
        return (live, [.. unmapped]);
    }

    /// <summary>Brush disc radius in tiles for a paint kind and brush size.</summary>
    /// <param name="kind">texture, water or forest.</param>
    /// <param name="size">Brush size (ignored for water).</param>
    static double AreaRadius(string kind, int size) =>
        kind == "water"
            ? WaterBrushTiles
            : Math.Max(AreaBrushTilesPerSize, AreaBrushTilesPerSize * size);

    /// <summary>Tile mask of the requested area, padded so brush discs around edge tiles stay inside the arrays.</summary>
    /// <param name="X0">Tile X of mask column 0.</param>
    /// <param name="Z0">Tile Z of mask row 0.</param>
    /// <param name="Inside">Area tiles (included shapes minus exclusions, mirrored copies included).</param>
    /// <param name="Excluded">Tiles inside an exclusion shape.</param>
    sealed record AreaMask(int X0, int Z0, bool[,] Inside, bool[,] Excluded)
    {
        /// <summary>Mask width in tiles.</summary>
        internal int W => Inside.GetLength(0);

        /// <summary>Mask height in tiles.</summary>
        internal int H => Inside.GetLength(1);

        /// <summary>Area tile count.</summary>
        internal int Count => Inside.Cast<bool>().Count(b => b);
    }

    /// <summary>Planned strokes for one brush size: world polylines plus the predicted cover.</summary>
    /// <param name="Size">Brush size (texture/forest; water reports its fixed radius).</param>
    /// <param name="Radius">Brush radius in tiles.</param>
    /// <param name="Step">Row step in tiles.</param>
    /// <param name="Strokes">Mask-relative tile-centre polylines.</param>
    /// <param name="Covered">Predicted covered area tiles.</param>
    /// <param name="Leaked">Predicted painted tiles outside the area.</param>
    sealed record AreaPlan(
        int Size,
        double Radius,
        int Step,
        List<List<(double I, double J)>> Strokes,
        int Covered,
        int Leaked
    );

    /// <summary>Rasterizes included minus excluded shapes (plus mirrored copies) onto tile centres.</summary>
    /// <param name="view">View state (tile size, map size).</param>
    /// <param name="tilesX">Map tiles along X.</param>
    /// <param name="tilesZ">Map tiles along Z.</param>
    /// <param name="include">Included shapes.</param>
    /// <param name="exclude">Excluded shapes.</param>
    /// <param name="mirror">Optional mirror mode (<see cref="MirrorModes"/>).</param>
    /// <param name="pad">Padding in tiles on every side.</param>
    static AreaMask AreaTiles(
        EditorView.ViewState view,
        int tilesX,
        int tilesZ,
        ShapeSpec[] include,
        ShapeSpec[] exclude,
        string? mirror,
        int pad
    )
    {
        double ww = view.WorldWidth,
            wd = view.WorldDepth,
            s = view.Scale;
        bool Any(ShapeSpec[] shapes, double x, double z)
        {
            if (shapes.Any(sh => sh.Inside(x, z)))
                return true;
            if (mirror is null)
                return false;
            // Mirror modes are involutions: the mirrored shape contains p exactly when the original contains mirror(p).
            var (mx, mz) = MirrorPoint(mirror, x, z, ww, wd);
            return shapes.Any(sh => sh.Inside(mx, mz));
        }
        double minX = double.MaxValue,
            minZ = double.MaxValue,
            maxX = double.MinValue,
            maxZ = double.MinValue;
        foreach (var shape in include)
        {
            var b = shape.Bounds();
            (double X, double Z)[] corners = mirror is null
                ? [(b.MinX, b.MinZ), (b.MaxX, b.MaxZ)]
                :
                [
                    (b.MinX, b.MinZ),
                    (b.MaxX, b.MaxZ),
                    MirrorPoint(mirror, b.MinX, b.MinZ, ww, wd),
                    MirrorPoint(mirror, b.MaxX, b.MaxZ, ww, wd),
                    MirrorPoint(mirror, b.MinX, b.MaxZ, ww, wd),
                    MirrorPoint(mirror, b.MaxX, b.MinZ, ww, wd),
                ];
            foreach (var (x, z) in corners)
            {
                if (!view.InsideMap(x, z))
                    throw OutsideMap(view);
                (minX, minZ) = (Math.Min(minX, x), Math.Min(minZ, z));
                (maxX, maxZ) = (Math.Max(maxX, x), Math.Max(maxZ, z));
            }
        }
        int x0 = Math.Max(0, (int)Math.Floor(minX / s)) - pad,
            z0 = Math.Max(0, (int)Math.Floor(minZ / s)) - pad,
            x1 = Math.Min(tilesX - 1, (int)Math.Ceiling(maxX / s)) + pad,
            z1 = Math.Min(tilesZ - 1, (int)Math.Ceiling(maxZ / s)) + pad;
        var inside = new bool[x1 - x0 + 1, z1 - z0 + 1];
        var excluded = new bool[x1 - x0 + 1, z1 - z0 + 1];
        for (var i = 0; i < inside.GetLength(0); i++)
        for (var j = 0; j < inside.GetLength(1); j++)
        {
            int tx = x0 + i,
                tz = z0 + j;
            if (tx < 0 || tz < 0 || tx >= tilesX || tz >= tilesZ)
                continue;
            double cx = (tx + TileCentre) * s,
                cz = (tz + TileCentre) * s;
            excluded[i, j] = Any(exclude, cx, cz);
            inside[i, j] = !excluded[i, j] && Any(include, cx, cz);
        }
        return new(x0, z0, inside, excluded);
    }

    /// <summary>Tiles a disc of <paramref name="radius"/> tiles swept along the polylines would paint (tile centres
    /// within the radius of a sampled stroke point).</summary>
    /// <param name="w">Mask width.</param>
    /// <param name="h">Mask height.</param>
    /// <param name="strokes">Mask-relative polylines (one vertex = dab).</param>
    /// <param name="radius">Brush radius in tiles.</param>
    internal static bool[,] Sweep(
        int w,
        int h,
        IEnumerable<List<(double I, double J)>> strokes,
        double radius
    )
    {
        var painted = new bool[w, h];
        var r = (int)Math.Ceiling(radius);
        void Stamp(double ci, double cj)
        {
            for (
                var i = Math.Max(0, (int)Math.Floor(ci) - r);
                i <= Math.Min(w - 1, (int)Math.Ceiling(ci) + r);
                i++
            )
            for (
                var j = Math.Max(0, (int)Math.Floor(cj) - r);
                j <= Math.Min(h - 1, (int)Math.Ceiling(cj) + r);
                j++
            )
                if ((i - ci) * (i - ci) + (j - cj) * (j - cj) <= radius * radius)
                    painted[i, j] = true;
        }
        foreach (var stroke in strokes)
        {
            Stamp(stroke[0].I, stroke[0].J);
            for (var k = 1; k < stroke.Count; k++)
            {
                var (a, b) = (stroke[k - 1], stroke[k]);
                var n = Math.Max(
                    1,
                    (int)
                        Math.Ceiling(
                            Math.Sqrt(Math.Pow(b.I - a.I, 2) + Math.Pow(b.J - a.J, 2))
                                / AreaSampleTiles
                        )
                );
                for (var t = 1; t <= n; t++)
                    Stamp(a.I + (b.I - a.I) * t / n, a.J + (b.J - a.J) * t / n);
            }
        }
        return painted;
    }

    /// <summary>Strokes for one brush size over the area: erode by the radius, serpentine rows every
    /// <see cref="AreaRowStepRadii"/> radii, predicted cover/leak from <see cref="Sweep"/>.</summary>
    /// <param name="mask">Area mask.</param>
    /// <param name="kind">Paint kind.</param>
    /// <param name="size">Brush size.</param>
    static AreaPlan PlanArea(AreaMask mask, string kind, int size)
    {
        var radius = AreaRadius(kind, size);
        var step = Math.Max(1, (int)Math.Floor(AreaRowStepRadii * radius));
        var strokes = ScanStrokes(AreaCentres(mask.Inside, kind, radius), step);
        var painted = Sweep(mask.W, mask.H, strokes, radius);
        int covered = 0,
            leaked = 0;
        for (var i = 0; i < mask.W; i++)
        for (var j = 0; j < mask.H; j++)
            if (painted[i, j])
            {
                if (mask.Inside[i, j])
                    covered++;
                else
                    leaked++;
            }
        return new(size, radius, step, strokes, covered, leaked);
    }

    /// <summary>Odd brush size at or below <paramref name="size"/> (≥1): live 2026-10-22 a size-2 texture fill leaked a
    /// whole tile row on the −X and −Z edges only, so even sizes paint offset by half a tile.</summary>
    static int OddSize(int size) => Math.Max(1, size % 2 == 0 ? size - 1 : size);

    /// <summary>Default brush: the largest odd size whose leak-free plan reaches <see cref="AreaTargetCoverage"/>, else
    /// the best-covering leak-free odd size (water has one fixed footprint).</summary>
    /// <param name="mask">Area mask.</param>
    /// <param name="kind">Paint kind.</param>
    /// <param name="requested">Requested brush size, if any.</param>
    static AreaPlan ChooseAreaPlan(AreaMask mask, string kind, int? requested)
    {
        if (kind == "water" || requested is not null)
            return PlanArea(mask, kind, requested ?? 1);
        var count = Math.Max(1, mask.Count);
        AreaPlan? best = null;
        for (var size = OddSize(MaxAreaBrushSize); size >= 1; size -= 2)
        {
            var plan = PlanArea(mask, kind, size);
            if (plan.Leaked == 0 && plan.Covered >= AreaTargetCoverage * count)
                return plan;
            if (
                best is null
                || (plan.Leaked, -plan.Covered).CompareTo((best.Leaked, -best.Covered)) < 0
            )
                best = plan;
        }
        return best!;
    }

    /// <summary>Touch-up dabs for uncovered tiles: brush centres inside the area eroded by <paramref name="radius"/>,
    /// greedily nearest to each uncovered tile, joined serpentine-wise while the joint stays inside.</summary>
    /// <param name="inside">Area tiles.</param>
    /// <param name="kind">Paint kind (edge margin rule).</param>
    /// <param name="uncovered">Mask-relative uncovered area tiles.</param>
    /// <param name="radius">Touch-up brush radius in tiles.</param>
    /// <param name="unreachable">Uncovered tiles no inside-only dab reaches.</param>
    internal static List<List<(double I, double J)>> TouchUpStrokes(
        bool[,] inside,
        string kind,
        IReadOnlyList<(int I, int J)> uncovered,
        double radius,
        out int unreachable
    )
    {
        var centres = AreaCentres(inside, kind, radius);
        int w = inside.GetLength(0),
            h = inside.GetLength(1),
            r = (int)Math.Ceiling(radius);
        var done = new bool[uncovered.Count];
        var dabs = new List<(int I, int J)>();
        unreachable = 0;
        for (var u = 0; u < uncovered.Count; u++)
        {
            if (done[u])
                continue;
            var (ui, uj) = uncovered[u];
            (int I, int J)? pick = null;
            var bestDistance = double.MaxValue;
            for (var i = Math.Max(0, ui - r); i <= Math.Min(w - 1, ui + r); i++)
            for (var j = Math.Max(0, uj - r); j <= Math.Min(h - 1, uj + r); j++)
            {
                var d = (i - ui) * (i - ui) + (j - uj) * (j - uj);
                if (centres[i, j] && d <= radius * radius && d < bestDistance)
                    (pick, bestDistance) = ((i, j), d);
            }
            if (pick is not { } c)
            {
                unreachable++;
                continue;
            }
            dabs.Add(c);
            for (var v = u; v < uncovered.Count; v++)
                if (
                    !done[v]
                    && Math.Pow(uncovered[v].I - c.I, 2) + Math.Pow(uncovered[v].J - c.J, 2)
                        <= radius * radius
                )
                    done[v] = true;
        }
        var ordered = dabs.Distinct()
            .GroupBy(d => d.I)
            .OrderBy(g => g.Key)
            .SelectMany(
                (g, row) => row % 2 == 0 ? g.OrderBy(d => d.J) : g.OrderByDescending(d => d.J)
            );
        var strokes = new List<List<(double I, double J)>>();
        List<(double I, double J)>? current = null;
        foreach (var (i, j) in ordered)
        {
            (double I, double J) p = (i, j);
            if (current is not null && SegmentInside(centres, current[^1], p))
                current.Add(p);
            else
            {
                current = [p];
                strokes.Add(current);
            }
        }
        return strokes;
    }

    /// <summary>World polyline (tile centres) for a mask-relative stroke.</summary>
    /// <param name="mask">Area mask.</param>
    /// <param name="stroke">Mask-relative polyline.</param>
    /// <param name="scale">World units per tile.</param>
    static (double X, double Z)[] AreaWorld(
        AreaMask mask,
        List<(double I, double J)> stroke,
        double scale
    ) =>
        [
            .. stroke.Select(p =>
                ((mask.X0 + p.I + TileCentre) * scale, (mask.Z0 + p.J + TileCentre) * scale)
            ),
        ];

    /// <summary>Drag duration for a world polyline (<see cref="AreaMsPerWorldUnit"/>, clamped to the drag bounds).</summary>
    /// <param name="points">World polyline.</param>
    static int AreaDuration((double X, double Z)[] points) =>
        Math.Clamp(
            MinStrokeMs + (int)(AreaMsPerWorldUnit * PathLength(points)),
            MinStrokeMs,
            MaxStrokeMs
        );

    /// <summary>editor_paint_area: preview the plan, or paint, verify and touch up.</summary>
    /// <param name="game">Validated editor connection.</param>
    /// <param name="args">Tool arguments.</param>
    object PaintArea(Game game, JsonElement args)
    {
        var kind = String(args, "kind");
        var requested = String(args, "type");
        var preview = LayoutPreview(args);
        var moveCamera = !args.TryGetProperty("moveCamera", out var mc) || mc.GetBoolean();
        var touchUp = !args.TryGetProperty("touchUp", out var tu) || tu.GetBoolean();
        int? brushSize = args.TryGetProperty("brushSize", out var bs) ? bs.GetInt32() : null;
        var mirror = args.TryGetProperty("mirror", out var me) ? me.GetString() : null;
        ShapeSpec[] Shapes(string key) =>
            args.TryGetProperty(key, out var list)
                ? [.. list.EnumerateArray().Select(ParseShape)]
                : [];
        var include = Shapes("shapes");
        var exclude = Shapes("exclude");
        var warnings = new List<string>();
        if (kind == "water" && brushSize is not null)
        {
            warnings.Add(
                $"brushSize ignored for water: paintWater paints a fixed ≈{WaterBrushTiles * 2:0} tile wide band (live-measured)."
            );
            brushSize = null;
        }
        if (brushSize is { } even && even % 2 == 0)
        {
            warnings.Add(
                $"brushSize {even} → {OddSize(even)}: even sizes paint offset by half a tile toward -X/-Z (live-measured)."
            );
            brushSize = OddSize(even);
        }
        if (!preview)
            RequireNoCursor(game);
        var names = LiveWorld.Names(game);
        var type = ResolvePaintType(kind, requested, names, () => MixTitles());
        var view = EditorView.ReadView(game);
        var tiles0 = new LiveWorld.Tiles(game, view);
        var pad = (int)Math.Ceiling(AreaRadius(kind, MaxAreaBrushSize)) + AreaLeakMarginTiles;
        var mask = AreaTiles(view, tiles0.TilesX, tiles0.TilesZ, include, exclude, mirror, pad);
        object? bandReport = null;
        if (args.TryGetProperty("band", out var bandArg))
        {
            var band = ParseBand(bandArg);
            var shapeTiles = mask.Count;
            (mask, var removed) = ApplyBand(mask, tiles0, band);
            bandReport = new
            {
                distanceTo = band.To,
                minTiles = band.Min,
                maxTiles = band.Max,
                shapeTiles,
                bandTiles = mask.Count,
                removedTiles = removed,
            };
        }
        HashSet<string>? mixSet = null;
        if (kind == "mix")
        {
            var (live, unmapped) = MixTextures(type, names);
            mixSet = live;
            if (unmapped.Length > 0)
                warnings.Add(
                    $"Mix textures without a live texture name (not counted as coverage): {string.Join(", ", unmapped)}."
                );
            if (live.Count == 0)
                warnings.Add(
                    "No mix texture maps to a live texture name: coverage counts every changed area tile."
                );
        }
        if (mask.Count == 0)
            throw new WorkflowFailure(
                "AREA_EMPTY",
                "preflight",
                "No tile centre lies inside the shapes (after exclusions and band).",
                false,
                false,
                "Enlarge the shapes or reduce exclusions. No input sent."
            );
        var plan = ChooseAreaPlan(mask, kind, brushSize);
        var plannedVertices = plan.Strokes.Sum(s => s.Count);
        var plannedLength = plan.Strokes.Sum(s => PathLength(AreaWorld(mask, s, view.Scale)));
        object Planned() =>
            new
            {
                areaTiles = mask.Count,
                excludedTiles = mask.Excluded.Cast<bool>().Count(b => b),
                brushSize = kind == "water" ? (int?)null : plan.Size,
                brushRadiusTiles = Math.Round(plan.Radius, 2),
                rowStepTiles = plan.Step,
                strokes = plan.Strokes.Count,
                vertices = plannedVertices,
                worldLength = Math.Round(plannedLength, 1),
                predictedCoveragePercent = Math.Round(Percent * plan.Covered / mask.Count, 1),
                predictedLeakTiles = plan.Leaked,
                band = bandReport,
                mixTextures = mixSet?.Order(StringComparer.Ordinal).ToArray(),
            };
        if (preview)
            return new
            {
                preview = true,
                kind,
                type,
                plan = Planned(),
                strokesWorld = plan
                    .Strokes.Take(AreaPreviewStrokes)
                    .Select(s =>
                        AreaWorld(mask, s, view.Scale)
                            .Select(p => new[] { Math.Round(p.X, 2), Math.Round(p.Z, 2) })
                    )
                    .ToArray(),
                warnings,
                tooLarge = plan.Strokes.Count > MaxAreaStrokes
                    ? $"Plan has {plan.Strokes.Count} strokes (> {MaxAreaStrokes}); apply refuses. Split the area or pass a larger brushSize."
                    : null,
                limitation = "Plan only; no input sent. Prediction assumes a circular brush of the live-measured radius on tile centres; touch-up (default on) repaints uncovered tiles with half the brush after painting.",
            };
        if (plan.Strokes.Count > MaxAreaStrokes)
            throw new WorkflowFailure(
                "AREA_PLAN_TOO_LARGE",
                "preflight",
                $"Plan has {plan.Strokes.Count} strokes (> {MaxAreaStrokes}).",
                false,
                false,
                "Split the area into several calls or pass a larger brushSize. No input sent."
            );
        var tilesBefore = SnapshotArea(tiles0, mask);
        var unitsBefore = LiveUnits.Read(game);
        var exited = ExitEditModes(game);
        var steps = new List<object> { new { exitedMode = LiveWorld.ModeName(exited.Before) } };
        var mode = PaintMode(kind);
        var tolerance = kind == "water" ? WaterPickTolerance : DefaultPickTolerance;
        EnterMode(game, mode);
        var strokes = 0;
        float? originalSize = null;
        var passes = new List<object>();
        AreaCoverage coverage;
        try
        {
            if (kind != "water")
            {
                originalSize = LiveWorld.ReadBrush(game)?.Size;
                SetAreaBrush(game, plan.Size);
            }
            var note = SelectPaintType(game, kind, type, names, () => MixTitles());
            steps.Add(
                new
                {
                    editMode = mode,
                    selected = type,
                    selectionNote = note,
                    brushSize = kind == "water" ? (int?)null : plan.Size,
                }
            );
            PaintAreaStrokes(
                game,
                mask,
                plan.Strokes,
                view.Scale,
                moveCamera,
                tolerance,
                steps,
                ref strokes
            );
            Thread.Sleep(StrokeSettleMs);
            coverage = MeasureArea(game, mask, kind, type, tilesBefore, unitsBefore, mixSet);
            passes.Add(
                new
                {
                    pass = "main",
                    strokes,
                    coverage.CoveragePercent,
                    uncovered = coverage.Uncovered.Count,
                }
            );
            for (
                var pass = 1;
                touchUp && pass <= MaxAreaTouchUpPasses && coverage.Uncovered.Count > 0;
                pass++
            )
            {
                var size =
                    kind == "water" ? 1
                    : pass == 1 ? OddSize(plan.Size / 2)
                    : 1;
                if (kind != "water")
                    SetAreaBrush(game, size);
                var dabs = TouchUpStrokes(
                    mask.Inside,
                    kind,
                    coverage.Uncovered,
                    AreaRadius(kind, size),
                    out var unreachable
                );
                var skipped = Math.Max(0, dabs.Count - MaxAreaStrokes);
                if (skipped > 0)
                    dabs = dabs[..MaxAreaStrokes];
                if (dabs.Count == 0)
                {
                    passes.Add(
                        new
                        {
                            pass = $"touchUp{pass}",
                            strokes = 0,
                            unreachable,
                        }
                    );
                    break;
                }
                var prior = strokes;
                PaintAreaStrokes(
                    game,
                    mask,
                    dabs,
                    view.Scale,
                    moveCamera,
                    tolerance,
                    steps,
                    ref strokes
                );
                Thread.Sleep(StrokeSettleMs);
                coverage = MeasureArea(game, mask, kind, type, tilesBefore, unitsBefore, mixSet);
                passes.Add(
                    new
                    {
                        pass = $"touchUp{pass}",
                        brushSize = kind == "water" ? (int?)null : size,
                        strokes = strokes - prior,
                        unreachable,
                        skippedStrokes = skipped,
                        coverage.CoveragePercent,
                        uncovered = coverage.Uncovered.Count,
                    }
                );
            }
            if (originalSize is { } restore)
                SetBrushSize(game, (int)Math.Round(restore));
        }
        catch (Exception e) when (strokes > 0 && e is not WorkflowFailure { UndoSteps: not null })
        {
            // No automatic undo: a wrong count would revert user work (plans/wake-island-session-bugs.md B3).
            var f = e as WorkflowFailure;
            throw new WorkflowFailure(
                f?.Code ?? "PAINT_AREA_INCOMPLETE",
                f?.Phase ?? "paint",
                $"{e.Message} {strokes} stroke chunk(s) were already painted.",
                true,
                false,
                $"Scene changed: run editor_undo {strokes} time(s) to revert, or rerun on the remaining area.",
                inner: e,
                undoSteps: strokes
            );
        }
        finally
        {
            ExitEditModes(game);
        }
        return new
        {
            preview = false,
            kind,
            type,
            plan = Planned(),
            strokes,
            passes,
            verification = new
            {
                areaTiles = mask.Count,
                coveredTiles = mask.Count - coverage.Uncovered.Count,
                coveragePercent = coverage.CoveragePercent,
                uncoveredSample = coverage
                    .Uncovered.Take(AreaListLimit)
                    .Select(t => new[] { mask.X0 + t.I, mask.Z0 + t.J })
                    .ToArray(),
                leakedTiles = coverage.Leaked.Count,
                leakedSample = coverage
                    .Leaked.Take(AreaListLimit)
                    .Select(t => new[] { mask.X0 + t.I, mask.Z0 + t.J })
                    .ToArray(),
                otherTilesChangedOutside = coverage.OtherChanged,
                excludedTilesChanged = coverage.ExcludedChanged,
                objectsAdded = coverage.Added,
                objectsRemoved = coverage.Removed,
            },
            warnings,
            editModeAfter = LiveWorld.ModeName(LiveWorld.EditMode(game)),
            limitation = "Native paint tool drags along planned tile-centre strokes (brush-eroded, so discs stay inside), camera re-aimed per chunk. Coverage = area tiles carrying the requested texture/water (forest: tiles holding an object added by this call); leaked = tiles outside the area that newly carry the request (tile coordinates); otherTilesChangedOutside = other outside changes (water paints a shore texture ring). Brush size restored afterwards (texture/forest). strokes = undo count (editor_undo once each). Not saved.",
        };
    }

    /// <summary>Sets the active paint tool's brush size or throws BRUSH_SIZE_NOT_APPLIED.</summary>
    /// <param name="game">Editor connection.</param>
    /// <param name="size">Brush size.</param>
    void SetAreaBrush(Game game, int size)
    {
        var (after, applied) = SetBrushSize(game, size);
        if (!applied)
            throw new WorkflowFailure(
                "BRUSH_SIZE_NOT_APPLIED",
                "brush",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"uiChangeBrushSize({size}) readback is {after?.Size}."
                ),
                true,
                false,
                "Pass a brushSize the engine accepts (editor_brush)."
            );
    }

    /// <summary>Paints mask-relative strokes through <see cref="PaintChunks"/> (chunked per view, camera re-aimed).</summary>
    /// <param name="game">Editor connection.</param>
    /// <param name="mask">Area mask.</param>
    /// <param name="plan">Mask-relative strokes.</param>
    /// <param name="scale">World units per tile.</param>
    /// <param name="moveCamera">Re-aim the camera per chunk.</param>
    /// <param name="tolerance">Pick tolerance in world units.</param>
    /// <param name="steps">Step log.</param>
    /// <param name="strokes">Undo counter.</param>
    void PaintAreaStrokes(
        Game game,
        AreaMask mask,
        List<List<(double I, double J)>> plan,
        double scale,
        bool moveCamera,
        double tolerance,
        List<object> steps,
        ref int strokes
    )
    {
        for (var index = 0; index < plan.Count; index++)
        {
            ProgressReporter.Report(index, plan.Count, "area stroke");
            var points = AreaWorld(mask, plan[index], scale);
            PaintChunks(
                game,
                points,
                moveCamera,
                AreaDuration(points),
                PathLength(points),
                0,
                steps,
                ref strokes,
                pickTolerance: tolerance
            );
        }
    }

    /// <summary>Tiles of the mask box before painting (null outside the map).</summary>
    /// <param name="tiles">Live tile grid.</param>
    /// <param name="mask">Area mask.</param>
    static LiveWorld.Tile?[,] SnapshotArea(LiveWorld.Tiles tiles, AreaMask mask)
    {
        var snapshot = new LiveWorld.Tile?[mask.W, mask.H];
        for (var i = 0; i < mask.W; i++)
        for (var j = 0; j < mask.H; j++)
        {
            int x = mask.X0 + i,
                z = mask.Z0 + j;
            if (x >= 0 && z >= 0 && x < tiles.TilesX && z < tiles.TilesZ)
                snapshot[i, j] = tiles.At(x, z);
        }
        return snapshot;
    }

    /// <summary>Measured area result.</summary>
    /// <param name="Uncovered">Mask-relative area tiles not carrying the request.</param>
    /// <param name="Leaked">Mask-relative tiles outside the area that newly carry the request.</param>
    /// <param name="OtherChanged">Other tiles outside the area changed by this call.</param>
    /// <param name="ExcludedChanged">Changed tiles inside exclusions.</param>
    /// <param name="CoveragePercent">Covered share of the area.</param>
    /// <param name="Added">Objects added by this call.</param>
    /// <param name="Removed">Objects removed by this call.</param>
    sealed record AreaCoverage(
        List<(int I, int J)> Uncovered,
        List<(int I, int J)> Leaked,
        int OtherChanged,
        int ExcludedChanged,
        double CoveragePercent,
        int Added,
        int Removed
    );

    /// <summary>Reads live tiles/objects and compares them with the area and the before-snapshot.</summary>
    /// <param name="game">Editor connection.</param>
    /// <param name="mask">Area mask.</param>
    /// <param name="kind">Paint kind.</param>
    /// <param name="type">Requested type name.</param>
    /// <param name="before">Tile snapshot from <see cref="SnapshotArea"/>.</param>
    /// <param name="unitsBefore">Objects before painting.</param>
    /// <param name="mixSet">Live texture names of the mix (kind=mix); empty: any texture change counts.</param>
    static AreaCoverage MeasureArea(
        Game game,
        AreaMask mask,
        string kind,
        string type,
        LiveWorld.Tile?[,] before,
        LiveUnits.Unit[] unitsBefore,
        HashSet<string>? mixSet = null
    )
    {
        var view = EditorView.ReadView(game);
        var tiles = new LiveWorld.Tiles(game, view);
        var units = LiveUnits.Read(game);
        var beforeIds = unitsBefore.Select(u => u.UnitId).ToHashSet();
        var afterIds = units.Select(u => u.UnitId).ToHashSet();
        var added = units.Where(u => !beforeIds.Contains(u.UnitId)).ToArray();
        var withNew = new bool[mask.W, mask.H];
        foreach (var u in added)
        {
            int i = (int)Math.Floor(u.Position.X * view.InverseScale) - mask.X0,
                j = (int)Math.Floor(u.Position.Z * view.InverseScale) - mask.Z0;
            if (i >= 0 && j >= 0 && i < mask.W && j < mask.H)
                withNew[i, j] = true;
        }
        var uncovered = new List<(int I, int J)>();
        var leaked = new List<(int I, int J)>();
        int excludedChanged = 0,
            otherChanged = 0;
        for (var i = 0; i < mask.W; i++)
        for (var j = 0; j < mask.H; j++)
        {
            if (before[i, j] is not { } b)
                continue;
            var a = tiles.At(mask.X0 + i, mask.Z0 + j);
            var changed = a != b || withNew[i, j];
            bool Match(LiveWorld.Tile t) =>
                kind switch
                {
                    "texture" => tiles.TextureName(t) == type,
                    "water" => tiles.WaterName(t) == type,
                    "mix" => mixSet is { Count: > 0 }
                        ? tiles.TextureName(t) is { } name && mixSet.Contains(name)
                        : t != b,
                    _ => false,
                };
            var match = kind == "forest" ? withNew[i, j] : Match(a);
            if (mask.Inside[i, j])
            {
                if (!match)
                    uncovered.Add((i, j));
            }
            else if (changed)
            {
                if (match && (kind == "forest" || !Match(b)))
                    leaked.Add((i, j));
                else
                    otherChanged++;
                if (mask.Excluded[i, j])
                    excludedChanged++;
            }
        }
        return new(
            uncovered,
            leaked,
            otherChanged,
            excludedChanged,
            Math.Round(Percent * (mask.Count - uncovered.Count) / mask.Count, 1),
            added.Length,
            unitsBefore.Count(u => !afterIds.Contains(u.UnitId))
        );
    }

    /// <summary>Synthetic planner fixtures (no game).</summary>
    internal static void PaintAreaSelfTest()
    {
        // 30×30 tile square padded by 4: the default plan covers ≥90 % without leaking; single-tile touch-up reaches the corners.
        const int side = 30,
            pad = 4;
        var inside = new bool[side + 2 * pad, side + 2 * pad];
        for (var i = pad; i < side + pad; i++)
        for (var j = pad; j < side + pad; j++)
            inside[i, j] = true;
        var mask = new AreaMask(0, 0, inside, new bool[inside.GetLength(0), inside.GetLength(1)]);
        var plan = ChooseAreaPlan(mask, "texture", null);
        if (
            plan.Leaked != 0
            || plan.Covered < AreaTargetCoverage * mask.Count
            || plan.Strokes.Count == 0
        )
            throw new InvalidOperationException("Paint area plan fixture failed.");
        var painted = Sweep(mask.W, mask.H, plan.Strokes, plan.Radius);
        var missing = new List<(int I, int J)>();
        for (var i = 0; i < mask.W; i++)
        for (var j = 0; j < mask.H; j++)
            if (inside[i, j] && !painted[i, j])
                missing.Add((i, j));
        var single = AreaRadius("texture", 1);
        var dabs = TouchUpStrokes(inside, "texture", missing, single, out var unreachable);
        var fixedUp = Sweep(mask.W, mask.H, dabs, single);
        if (
            unreachable != 0
            || missing.Any(t => !fixedUp[t.I, t.J])
            || Enumerable
                .Range(0, mask.W)
                .Any(i => Enumerable.Range(0, mask.H).Any(j => fixedUp[i, j] && !inside[i, j]))
        )
            throw new InvalidOperationException("Paint area touch-up fixture failed.");
        // Band: one water tile in a 5×5 grid; Chebyshev distances 0..2.
        var water = new bool[5, 5];
        water[2, 2] = true;
        var distances = TileDistances(water);
        if (
            distances[2, 2] != 0
            || distances[1, 1] != 1
            || distances[0, 4] != 2
            || TileDistances(new bool[2, 2])[0, 0] != int.MaxValue
        )
            throw new InvalidOperationException("Paint area band distance fixture failed.");
        foreach (
            var bad in new[]
            {
                """{"distanceTo":"sea","maxTiles":1}""",
                """{"distanceTo":"water","minTiles":2,"maxTiles":1}""",
                """{"distanceTo":"water"}""",
            }
        )
        {
            try
            {
                _ = ParseBand(JsonDocument.Parse(bad).RootElement);
                throw new InvalidOperationException("Invalid band accepted: " + bad);
            }
            catch (ArgumentException) { }
        }
    }
}
