using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AomMcp;

/// <summary>Client resolutions for pixel/OCR UI layouts. Reviewed sizes (2560×1440, 1920×1080) have their own layout file with
/// live-sampled gate colors. Other clients 1024..3840 wide with aspect 1.25..2.4 get a best-effort layout derived from the 2560×1440
/// reference (research/UI-SCALING.md): geometry stretches per axis (x·W/2560, y·H/1440) except paths listed in the layout's
/// <c>anchors</c> map, which scale uniformly by min(W/2560, H/1440) pinned to the named client edge (minimap). Gates (authored in the
/// 1280×720 reference frame) map the same way into the variable-height screenshot frame and match either reviewed reference
/// within a 2-pixel neighborhood (minimap frame at tolerance 30).</summary>
internal static class UiLayouts
{
    /// <summary>Client sizes with live-reviewed layout files.</summary>
    internal static readonly (int Width, int Height)[] Reviewed = [(2560, 1440), (1920, 1080)];

    /// <summary>Human-readable list of <see cref="Reviewed"/> for messages.</summary>
    internal const string ReviewedText = "2560×1440 or 1920×1080";

    /// <summary>Human-readable support statement for messages and tool descriptions.</summary>
    internal const string SupportedText =
        "reviewed 2560×1440/1920×1080; other clients 1024..3840 wide with aspect 1.25..2.4 derived best effort (live-checked 1440×900, 1920×1200, 1600×900, 1280×720)";

    /// <summary>Reference layout size every derived layout is scaled from.</summary>
    internal const int ReferenceWidth = 2560,
        ReferenceHeight = 1440;

    /// <summary>Width bounds for derived layouts: below 1024 the game clips panel text; above 3840 (4K) OCR captures would need
    /// more than 1.5× downscale to the 2560 capture bound (host policy).</summary>
    internal const int MinDerivedWidth = 1024,
        MaxDerivedWidth = 3840;

    /// <summary>Aspect bounds for derived layouts (5:4 .. ~21:9); outside them panels overlap (host policy, untested).</summary>
    internal const double MinDerivedAspect = 1.25,
        MaxDerivedAspect = 2.4;

    /// <summary>Name of the layout-file map of dotted paths to uniformly scaled, edge-anchored elements.</summary>
    internal const string AnchorsKey = "anchors";

    /// <summary>Gate-match count a derived non-16:9 dialog gate may fall short (thin 1-px separators blend under per-axis
    /// resampling: normal Diplomacy 13/15 at 1920×1200, research/UI-SCALING.md).</summary>
    internal const int DerivedDialogSlack = 1;

    /// <summary>Lower bound for a slackened dialog gate as a fraction of its points.</summary>
    internal const double DerivedDialogMinFraction = 0.75;

    /// <summary>Gate neighborhood radius (half-res frame pixels) for derived layouts; reviewed layouts stay exact-position.</summary>
    internal const int DerivedGateRadius = 2;

    /// <summary>Per-channel tolerance for the derived minimap-frame gate: its thin ornaments resample differently at
    /// non-reviewed scales (1600×900 live: reviewed points off by up to 29). 0 false positives at 30 (and 50) over the
    /// non-editor/no-minimap frames of the reviewed-size corpus.</summary>
    internal const int DerivedMinimapTolerance = 30;

    /// <summary>True when a live-reviewed layout file exists for this client size.</summary>
    internal static bool IsReviewed(int width, int height) => Reviewed.Contains((width, height));

    /// <summary>Unreviewed client inside the derived width/aspect bounds (best effort, <c>layoutReviewed:false</c>).</summary>
    internal static bool IsDerivable(int width, int height) =>
        !IsReviewed(width, height)
        && width is >= MinDerivedWidth and <= MaxDerivedWidth
        && height > 0
        && (double)width / height is >= MinDerivedAspect and <= MaxDerivedAspect;

    /// <summary>Reviewed or derivable client size.</summary>
    internal static bool IsSupported(int width, int height) =>
        IsReviewed(width, height) || IsDerivable(width, height);

    /// <summary>True for a 16:9 client, where per-axis stretch and uniform scaling coincide.</summary>
    internal static bool IsWide16x9(int width, int height) =>
        (long)height * ReferenceWidth == (long)width * ReferenceHeight;

    /// <summary>Host screenshot frame for a client (<see cref="Ui.CapturePixels"/>: width min(client, 1280), height client·width/clientWidth).</summary>
    internal static (int Width, int Height) FrameSize(int width, int height)
    {
        var frameWidth = Math.Min(width, ScreenProbe.FrameWidth);
        return (frameWidth, Math.Max(1, height * frameWidth / width));
    }

    /// <summary>Edge anchor fractions (0 left/top, 0.5 center, 1 right/bottom) of a uniformly scaled element.</summary>
    internal readonly record struct Anchor(double X, double Y)
    {
        /// <summary>Parses <c>{"x":"left|center|right","y":"top|center|bottom"}</c>.</summary>
        internal static Anchor Parse(JsonNode node) =>
            new(
                Fraction(node["x"]?.GetValue<string>(), "left", "right"),
                Fraction(node["y"]?.GetValue<string>(), "top", "bottom")
            );

        static double Fraction(string? value, string low, string high) =>
            value == low || value is null ? 0
            : value == high ? 1
            : value == "center" ? 0.5
            : throw new InvalidDataException(
                $"Layout anchor '{value}' must be {low}, center or {high}."
            );
    }

    /// <summary>Reference-to-target mapping for one axis pair: per-axis stretch, or uniform scale pinned to an anchor.</summary>
    internal readonly record struct Transform(
        double SourceWidth,
        double SourceHeight,
        double TargetWidth,
        double TargetHeight,
        Anchor? Pin
    )
    {
        double Sx => TargetWidth / SourceWidth;
        double Sy => TargetHeight / SourceHeight;

        /// <summary>Uniform scale of anchored elements.</summary>
        internal double Uniform => Math.Min(Sx, Sy);

        /// <summary>Scale of x lengths.</summary>
        internal double LengthX => Pin is null ? Sx : Uniform;

        /// <summary>Scale of y lengths.</summary>
        internal double LengthY => Pin is null ? Sy : Uniform;

        /// <summary>Scale of scalar lengths with no axis (margins, half-diagonals).</summary>
        internal double Length => Pin is null ? Math.Min(Sx, Sy) : Uniform;

        /// <summary>Maps an x position.</summary>
        internal double X(double x) =>
            Pin is { } a ? a.X * TargetWidth + (x - a.X * SourceWidth) * Uniform : x * Sx;

        /// <summary>Maps a y position.</summary>
        internal double Y(double y) =>
            Pin is { } a ? a.Y * TargetHeight + (y - a.Y * SourceHeight) * Uniform : y * Sy;
    }

    /// <summary>Executable hash equals the buildHash pinned in a reviewed 2560×1440 layout (either UI).</summary>
    internal static bool BuildMatches(string exeSha256) =>
        ((string[])["alt", "normal"]).Any(kind =>
            Load(kind, 2560, 1440)?.Root.TryGetProperty("buildHash", out var hash) == true
            && string.Equals(exeSha256, hash.GetString(), StringComparison.OrdinalIgnoreCase)
        );

    /// <summary>Alternative-UI layout file name for a client size.</summary>
    internal static string Alt(int width, int height) => $"alt-{width}x{height}.json";

    /// <summary>Normal-UI (English) layout file name for a client size.</summary>
    internal static string Normal(int width, int height) => $"normal-en-{width}x{height}.json";

    /// <summary>Layout file name for a UI kind ("normal" or alternative) and client size.</summary>
    internal static string File(string kind, int width, int height) =>
        kind == "normal" ? Normal(width, height) : Alt(width, height);

    /// <summary>Shipped path of a layout file.</summary>
    internal static string Path(string file) =>
        System.IO.Path.Combine(AppContext.BaseDirectory, "uilayouts", file);

    /// <summary>Full-resolution client pixels per 2560×1440 reference pixel along x.</summary>
    internal static double Scale(int width) => width / (double)ReferenceWidth;

    /// <summary>Full-resolution client pixels per 2560×1440 reference pixel along y.</summary>
    internal static double ScaleY(int height) => height / (double)ReferenceHeight;

    /// <summary>Reference (2560×1440) pixel to client pixel, rounded half away from zero.</summary>
    internal static int ToClient(double reference, double scale) =>
        (int)Math.Round(reference * scale, MidpointRounding.AwayFromZero);

    /// <summary>Reference rect/point to client pixels with one scale for both axes.</summary>
    internal static int[] ToClient(int[] rect, double scale) =>
        rect.Select(v => ToClient(v, scale)).ToArray();

    /// <summary>Reference rect [x,y,w,h] (or point [x,y]) to client pixels with separate x/y scales.</summary>
    internal static int[] ToClient(int[] rect, double scaleX, double scaleY) =>
        rect.Select((v, i) => ToClient(v, i % 2 == 0 ? scaleX : scaleY)).ToArray();

    /// <summary>One loaded layout: reviewed file, or derived in memory from the 2560×1440 reference.</summary>
    internal sealed record Layout(JsonElement Root, string File, bool Reviewed, string? DerivedFrom)
    {
        /// <summary>Gate neighborhood radius: 0 for reviewed layouts.</summary>
        internal int Radius => Reviewed ? 0 : DerivedGateRadius;
    }

    /// <summary>Loaded layouts keyed by kind, size and reference file timestamp.</summary>
    static readonly ConcurrentDictionary<string, Layout?> Cache = new(StringComparer.Ordinal);

    /// <summary>Layout for the normal ("normal") or alternative UI at this client size; null when neither reviewed nor derivable or the file is missing.</summary>
    internal static Layout? Load(string kind, int width, int height) =>
        Cache.GetOrAdd(
            $"{kind}|{width}x{height}|{BaseStamp(kind, width, height)}",
            _ =>
            {
                var file = File(kind, width, height);
                if (IsReviewed(width, height))
                {
                    if (!System.IO.File.Exists(Path(file)))
                        return null;
                    using var document = JsonDocument.Parse(System.IO.File.ReadAllText(Path(file)));
                    return new(document.RootElement.Clone(), file, true, null);
                }
                if (!IsDerivable(width, height))
                    return null;
                var reference = File(kind, 2560, 1440);
                var second = File(kind, 1920, 1080);
                if (!System.IO.File.Exists(Path(reference)))
                    return null;
                var root = Derive(
                    JsonNode.Parse(System.IO.File.ReadAllText(Path(reference)))!.AsObject(),
                    System.IO.File.Exists(Path(second))
                        ? JsonNode.Parse(System.IO.File.ReadAllText(Path(second)))!.AsObject()
                        : null,
                    width,
                    height
                );
                using var derived = JsonDocument.Parse(root.ToJsonString());
                return new(
                    derived.RootElement.Clone(),
                    UiRead.DerivedPrefix + file,
                    false,
                    reference
                );
            }
        );

    /// <summary>Last-write stamp of the file a layout is loaded or derived from (cache invalidation).</summary>
    static string BaseStamp(string kind, int width, int height)
    {
        var file = Path(
            IsReviewed(width, height) ? File(kind, width, height) : File(kind, 2560, 1440)
        );
        return System.IO.File.Exists(file)
            ? System
                .IO.File.GetLastWriteTimeUtc(file)
                .Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "missing";
    }

    /// <summary>Counts, tolerances and text are not client geometry; copied verbatim.</summary>
    static readonly HashSet<string> NoScale = new(StringComparer.Ordinal)
    {
        "minimumMatches",
        "gateTolerance",
        "ocrGateTolerance",
        "tolerance",
        "columns",
        "visibleRows",
        "width",
        "height",
        "buildHash",
        "ui",
        "uiKind",
        "notes",
        "ocrNotes",
        "fieldGates",
        "paletteNotes",
        "note",
        "scaledFrom",
        AnchorsKey,
    };

    /// <summary>Scalar keys measured along y (Players Settings / Player Data row grid).</summary>
    static readonly HashSet<string> VerticalScalars = new(StringComparer.Ordinal)
    {
        "rowCenterFirst",
        "rowStep",
    };

    /// <summary>Gate point lists (1280×720 reference-frame samples), mapped into the screenshot frame instead of the client.</summary>
    const string GateKey = "gate";

    /// <summary>Second-reference gate lists attached to derived gates.</summary>
    const string GateAlternatesKey = "gateAlternates";

    /// <summary>Reference geometry → client: rects [x,y,w,h] / points [x,y] per axis (stretch, or uniform+anchor for paths in
    /// <c>anchors</c>), scalars per <see cref="VerticalScalars"/> or the smaller scale; gate points into the client's screenshot frame.
    /// The second reference's gates (same path) are attached as gateAlternates. 16:9 clients reproduce the former height/1440 scaling.</summary>
    internal static JsonObject Derive(
        JsonObject reference,
        JsonObject? second,
        int width,
        int height
    )
    {
        var anchors = new List<(string Path, Anchor Pin)>();
        if (reference[AnchorsKey] is JsonObject declared)
            foreach (var (path, value) in declared)
                if (value is JsonObject)
                    anchors.Add((path, Anchor.Parse(value)));
        Anchor? PinFor(string path) =>
            anchors
                .Where(a =>
                    path == a.Path || path.StartsWith(a.Path + ".", StringComparison.Ordinal)
                )
                .Select(a => (Anchor?)a.Pin)
                .FirstOrDefault();
        var (frameWidth, frameHeight) = FrameSize(width, height);
        var wide = IsWide16x9(width, height);

        JsonNode? Gate(JsonNode? gate, Anchor? pin)
        {
            var map = new Transform(
                ScreenProbe.FrameWidth,
                ScreenProbe.ReferenceFrameHeight,
                frameWidth,
                frameHeight,
                pin
            );
            var result = new JsonArray();
            foreach (var point in gate?.AsArray() ?? [])
            {
                var copy = point!.DeepClone().AsObject();
                copy["x"] = Math.Clamp(
                    ToClient(map.X(point["x"]!.GetValue<int>()), 1),
                    0,
                    frameWidth - 1
                );
                copy["y"] = Math.Clamp(
                    ToClient(map.Y(point["y"]!.GetValue<int>()), 1),
                    0,
                    frameHeight - 1
                );
                result.Add(copy);
            }
            return result;
        }

        JsonNode? Walk(JsonNode? node, string path, string? key, JsonNode? other)
        {
            if (node is null)
                return null;
            if (key is not null && NoScale.Contains(key))
                return node.DeepClone();
            var pin = PinFor(path);
            var map = new Transform(ReferenceWidth, ReferenceHeight, width, height, pin);
            switch (node)
            {
                case JsonObject obj:
                {
                    var result = new JsonObject();
                    foreach (var (k, v) in obj)
                    {
                        var child = path.Length == 0 ? k : path + "." + k;
                        result[k] =
                            k == GateKey
                                ? Gate(v, PinFor(child))
                                : Walk(
                                    v,
                                    child,
                                    k,
                                    other is JsonObject o && o.TryGetPropertyValue(k, out var ov)
                                        ? ov
                                        : null
                                );
                    }
                    if (
                        obj.ContainsKey(GateKey)
                        && other is JsonObject alt
                        && alt[GateKey] is JsonArray altGate
                        && !JsonNode.DeepEquals(altGate, obj[GateKey])
                    )
                        result[GateAlternatesKey] = new JsonArray(Gate(altGate, pin));
                    // Thin dialog separators blend under per-axis resampling: allow a slack match off 16:9.
                    if (
                        !wide
                        && path.StartsWith("ocrGates.", StringComparison.Ordinal)
                        && obj["minimumMatches"] is JsonValue need
                        && obj[GateKey] is JsonArray points
                    )
                        result["minimumMatches"] = Math.Max(
                            need.GetValue<int>() - DerivedDialogSlack,
                            (int)Math.Ceiling(points.Count * DerivedDialogMinFraction)
                        );
                    return result;
                }
                case JsonArray array when array.Count is 2 or 4 && array.All(v => v is JsonValue):
                {
                    var v = array.Select(e => e!.GetValue<double>()).ToArray();
                    var x = map.X(v[0]);
                    var y = map.Y(v[1]);
                    var result = new JsonArray(ToClient(x, 1), ToClient(y, 1));
                    if (v.Length == 4)
                    {
                        result.Add(ToClient(v[2], map.LengthX));
                        result.Add(ToClient(v[3], map.LengthY));
                    }
                    return result;
                }
                case JsonArray array:
                {
                    var result = new JsonArray();
                    for (var i = 0; i < array.Count; i++)
                        result.Add(
                            Walk(
                                array[i],
                                path,
                                null,
                                other is JsonArray oa && oa.Count == array.Count ? oa[i] : null
                            )
                        );
                    return result;
                }
                case JsonValue value when value.GetValueKind() == JsonValueKind.Number:
                {
                    var scale =
                        key is not null && VerticalScalars.Contains(key) ? map.LengthY : map.Length;
                    return value.TryGetValue<int>(out var i32)
                        ? JsonValue.Create(ToClient(i32, scale))
                        : JsonValue.Create(Math.Round(value.GetValue<double>() * scale, 4));
                }
                default:
                    return node.DeepClone();
            }
        }
        var root = (JsonObject)Walk(reference, "", null, second)!;
        root["width"] = width;
        root["height"] = height;
        if (root["editorScene"]?["minimap"] is JsonObject minimap)
            minimap["tolerance"] = DerivedMinimapTolerance;
        root["derived"] = new JsonObject
        {
            ["from"] = $"{ReferenceWidth}x{ReferenceHeight}",
            ["scaleX"] = Math.Round(Scale(width), 6),
            ["scaleY"] = Math.Round(ScaleY(height), 6),
            ["frame"] = new JsonArray(frameWidth, frameHeight),
            ["gateRadius"] = DerivedGateRadius,
            ["bestEffort"] = !wide,
            ["note"] =
                "Derived in memory (best effort): reference geometry stretched per axis, anchored paths scaled uniformly by min(W/2560,H/1440) and pinned to their edge; gates mapped into the screenshot frame and matched against either reviewed reference within the radius. Not live-reviewed at this size.",
        };
        return root;
    }

    /// <summary>Gate points in reviewed or derived form: passes when the primary gate or any gateAlternates list reaches minimumMatches.</summary>
    internal static bool GatePasses(
        ScreenProbe.Frame frame,
        JsonElement gated,
        int tolerance,
        int radius,
        out int bestHits
    )
    {
        var need = gated.GetProperty("minimumMatches").GetInt32();
        // Derived layouts may widen one gate's tolerance (never narrower than the caller's).
        if (gated.TryGetProperty("tolerance", out var own))
            tolerance = Math.Max(tolerance, own.GetInt32());
        bestHits = SceneGeometry.GateMatches(frame, gated.GetProperty("gate"), tolerance, radius);
        if (bestHits >= need)
            return true;
        if (gated.TryGetProperty("gateAlternates", out var alternates))
            foreach (var gate in alternates.EnumerateArray())
            {
                var hits = SceneGeometry.GateMatches(frame, gate, tolerance, radius);
                if (hits > bestHits)
                    bestHits = hits;
                if (hits >= need)
                    return true;
            }
        return false;
    }

    /// <summary>Number of worst-matching gate points listed by <see cref="GateReport"/>.</summary>
    internal const int WorstGatePoints = 3;

    /// <summary>Diagnostic for one gate: hits/need of the best variant (primary or a gateAlternates entry) and its
    /// <see cref="WorstGatePoints"/> worst points with expected vs observed RGB (best pixel within the radius).</summary>
    internal static JsonObject GateReport(
        ScreenProbe.Frame frame,
        JsonElement gated,
        int tolerance,
        int radius
    )
    {
        var need = gated.GetProperty("minimumMatches").GetInt32();
        if (gated.TryGetProperty("tolerance", out var own))
            tolerance = Math.Max(tolerance, own.GetInt32());
        var variants = new List<JsonElement> { gated.GetProperty("gate") };
        if (gated.TryGetProperty("gateAlternates", out var alternates))
            variants.AddRange(alternates.EnumerateArray());
        (int Error, int X, int Y, int[] Expected, ScreenProbe.Rgb? Observed)[] Points(
            JsonElement gate
        ) =>
            [
                .. gate.EnumerateArray()
                    .Select(p =>
                    {
                        int x = p.GetProperty("x").GetInt32(),
                            y = p.GetProperty("y").GetInt32();
                        int[] rgb =
                        [
                            p.GetProperty("r").GetInt32(),
                            p.GetProperty("g").GetInt32(),
                            p.GetProperty("b").GetInt32(),
                        ];
                        var best = int.MaxValue;
                        ScreenProbe.Rgb? seen = null;
                        for (var dy = -radius; dy <= radius; dy++)
                        for (var dx = -radius; dx <= radius; dx++)
                        {
                            if (!frame.Contains(x + dx, y + dy))
                                continue;
                            var c = frame.At(x + dx, y + dy);
                            var error = Math.Max(
                                Math.Abs(c.R - rgb[0]),
                                Math.Max(Math.Abs(c.G - rgb[1]), Math.Abs(c.B - rgb[2]))
                            );
                            if (error < best)
                                (best, seen) = (error, c);
                        }
                        return (best, x, y, rgb, seen);
                    }),
            ];
        var scored = variants
            .Select((gate, index) => (Index: index, Points: Points(gate)))
            .Select(v => (v.Index, v.Points, Hits: v.Points.Count(p => p.Error <= tolerance)))
            .OrderByDescending(v => v.Hits)
            .First();
        return new JsonObject
        {
            ["hits"] = scored.Hits,
            ["need"] = need,
            ["points"] = scored.Points.Length,
            ["passed"] = scored.Hits >= need,
            ["tolerance"] = tolerance,
            ["radius"] = radius,
            ["variant"] = scored.Index == 0 ? "primary" : $"alternate{scored.Index}",
            ["variantsTried"] = variants.Count,
            ["worst"] = new JsonArray([
                .. scored
                    .Points.Where(p => p.Error > tolerance)
                    .OrderByDescending(p => p.Error)
                    .Take(WorstGatePoints)
                    .Select(p =>
                        (JsonNode)
                            new JsonObject
                            {
                                ["frame"] = new JsonArray(p.X, p.Y),
                                ["expected"] = new JsonArray(
                                    p.Expected[0],
                                    p.Expected[1],
                                    p.Expected[2]
                                ),
                                ["observed"] = p.Observed is { } o
                                    ? new JsonArray(o.R, o.G, o.B)
                                    : null,
                                ["error"] = p.Error == int.MaxValue ? null : p.Error,
                            }
                    ),
            ]),
        };
    }

    /// <summary>One-line summary of a <see cref="GateReport"/>: hits/need plus worst points.</summary>
    internal static string GateSummary(string name, JsonObject report)
    {
        var worst = string.Join(
            "; ",
            report["worst"]!
                .AsArray()
                .Select(w =>
                    $"({w!["frame"]![0]},{w["frame"]![1]}) expected {w["expected"]!.ToJsonString()} observed {w["observed"]?.ToJsonString() ?? "off-frame"}"
                )
        );
        return $"{name} {report["hits"]}/{report["points"]} (need {report["need"]}, {report["variantsTried"]} variant(s))"
            + (worst.Length == 0 ? "" : $": {worst}");
    }

    internal static void SelfTest()
    {
        if (
            ToClient(2316, 0.75) != 1737
            || ToClient(-21, 0.75) != -16
            || ToClient(47, 1) != 47
            || !IsReviewed(1920, 1080)
            || IsReviewed(1600, 900)
            || Normal(1920, 1080) != "normal-en-1920x1080.json"
            || Alt(2560, 1440) != "alt-2560x1440.json"
        )
            throw new InvalidOperationException("UI layout resolution fixture failed.");
        if (
            !IsDerivable(1600, 900)
            || !IsDerivable(1280, 720)
            || !IsDerivable(1706, 960)
            || !IsDerivable(1366, 768)
            || !IsDerivable(1920, 1200)
            || !IsDerivable(1440, 900)
            || !IsDerivable(3840, 2160)
            || !IsDerivable(3440, 1440)
            || !IsDerivable(1024, 576)
            || IsDerivable(1920, 1080)
            || IsDerivable(800, 600)
            || IsDerivable(1024, 1024)
            || IsDerivable(5120, 1440)
            || FrameSize(1440, 900) != (1280, 800)
            || FrameSize(1024, 768) != (1024, 768)
            || FrameSize(3840, 2160) != (1280, 720)
        )
            throw new InvalidOperationException("UI layout derivable-size fixture failed.");
        var pinned = new Transform(ReferenceWidth, ReferenceHeight, 1440, 900, new Anchor(1, 1));
        if (
            ToClient(pinned.X(2316), 1) != 1303
            || ToClient(pinned.Y(1196), 1) != 763
            || ToClient(197, pinned.Length) != 111
        )
            throw new InvalidOperationException("UI layout anchor transform fixture failed.");
        // Gate report: black 4×4 frame, one gate expecting black at (0,0) and white at (1,1)/(2,2); 1/3 hits, need 2.
        var black = new ScreenProbe.Frame(new byte[4 * 4 * 3], 4, 4, 4, 4);
        using var gateDoc = JsonDocument.Parse(
            """{"minimumMatches":2,"gate":[{"x":0,"y":0,"r":0,"g":0,"b":0},{"x":1,"y":1,"r":255,"g":255,"b":255},{"x":2,"y":2,"r":200,"g":0,"b":0}]}"""
        );
        var report = GateReport(black, gateDoc.RootElement, 14, 0);
        if (
            (int)report["hits"]! != 1
            || (bool)report["passed"]!
            || report["worst"]!.AsArray().Count != 2
            || (int)report["worst"]![0]!["error"]! != 255
        )
            throw new InvalidOperationException("UI gate report fixture failed.");
        foreach (var (w, h) in Reviewed)
        foreach (var file in new[] { Normal(w, h), Alt(w, h) })
            if (!System.IO.File.Exists(Path(file)))
                throw new InvalidOperationException("Reviewed UI layout missing: " + file);
        // Deriving 1920×1080 from the 2560 reference must reproduce the reviewed 1920 geometry (gates and re-split OCR columns excepted).
        foreach (var kind in new[] { "normal", "alternative" })
        {
            var reviewed = JsonNode
                .Parse(System.IO.File.ReadAllText(Path(File(kind, 1920, 1080))))!
                .AsObject();
            var derived = Derive(
                JsonNode
                    .Parse(System.IO.File.ReadAllText(Path(File(kind, 2560, 1440))))!
                    .AsObject(),
                reviewed,
                1920,
                1080
            );
            var scene = (derived["editorScene"]!, reviewed["editorScene"]!);
            foreach (
                var path in new[]
                {
                    "minimap.center",
                    "minimap.halfDiagonal",
                    "occluders",
                    "safeMargin",
                }
            )
            {
                JsonNode? a = scene.Item1,
                    b = scene.Item2;
                foreach (var part in path.Split('.'))
                {
                    a = a?[part];
                    b = b?[part];
                }
                if (a is null || !JsonNode.DeepEquals(a, b))
                    throw new InvalidOperationException(
                        $"Derived {kind} 1920x1080 {path} differs from reviewed."
                    );
            }
            foreach (var (name, occ) in scene.Item1["conditionalOccluders"]!.AsObject())
                if (
                    !JsonNode.DeepEquals(
                        occ!["rect"],
                        scene.Item2["conditionalOccluders"]![name]!["rect"]
                    )
                )
                    throw new InvalidOperationException(
                        $"Derived {kind} 1920x1080 conditional {name} rect differs from reviewed."
                    );
            if (
                derived["editorScene"]!["minimap"]!["gateAlternates"] is not JsonArray
                || derived["editorScene"]!["minimap"]!["tolerance"]?.GetValue<int>()
                    != DerivedMinimapTolerance
            )
                throw new InvalidOperationException(
                    "Derived layout lacks second-reference minimap gate."
                );
        }
        var small =
            Load("normal", 1600, 900)
            ?? throw new InvalidOperationException("Derived 1600x900 normal layout missing.");
        if (
            small.Reviewed
            || small.Radius != DerivedGateRadius
            || small.Root.GetProperty("width").GetInt32() != 1600
            || Load("normal", 800, 600) is not null
            || Load("alternative", 2560, 1440) is not { Reviewed: true }
        )
            throw new InvalidOperationException("Derived layout load fixture failed.");
        static double ReferenceMinimapHalf(string kind) =>
            JsonNode.Parse(
                System.IO.File.ReadAllText(Path(File(kind, ReferenceWidth, ReferenceHeight)))
            )!["editorScene"]!["minimap"]!["halfDiagonal"]!.GetValue<double>();
        // 16:10 1440x900 (live, research/UI-SCALING.md): minimap centre (1302,762), uniform 0.5625 scale, bottom-right;
        // panels stretch per axis; gates land in the 1280x800 frame.
        foreach (var kind in new[] { "normal", "alternative" })
        {
            var tall =
                Load(kind, 1440, 900)
                ?? throw new InvalidOperationException($"Derived 1440x900 {kind} layout missing.");
            var minimap = tall.Root.GetProperty("editorScene").GetProperty("minimap");
            var center = minimap.GetProperty("center");
            if (
                Math.Abs(center[0].GetInt32() - 1302) > DerivedGateRadius
                || Math.Abs(center[1].GetInt32() - 762) > DerivedGateRadius
                || Math.Abs(
                    minimap.GetProperty("halfDiagonal").GetDouble()
                        - ReferenceMinimapHalf(kind) * Scale(1440)
                ) > 1
                || minimap
                    .GetProperty("gate")
                    .EnumerateArray()
                    .Any(p => p.GetProperty("y").GetInt32() is < 560 or >= 800)
            )
                throw new InvalidOperationException(
                    $"Derived 1440x900 {kind} minimap fixture failed."
                );
            var reference = JsonNode
                .Parse(
                    System.IO.File.ReadAllText(Path(File(kind, ReferenceWidth, ReferenceHeight)))
                )!
                .AsObject();
            var (name, occ) = reference["editorScene"]!["conditionalOccluders"]!.AsObject().First();
            var rect = occ!["rect"]!.AsArray().Select(v => v!.GetValue<int>()).ToArray();
            var derived = tall
                .Root.GetProperty("editorScene")
                .GetProperty("conditionalOccluders")
                .GetProperty(name)
                .GetProperty("rect")
                .EnumerateArray()
                .Select(v => v.GetInt32())
                .ToArray();
            if (!derived.SequenceEqual(ToClient(rect, Scale(1440), ScaleY(900))))
                throw new InvalidOperationException(
                    $"Derived 1440x900 {kind} {name} stretch fixture failed."
                );
        }
    }
}
