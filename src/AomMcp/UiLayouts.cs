using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AomMcp;

/// <summary>Client resolutions for pixel/OCR UI layouts. The game UI scales proportionally with a 16:9 client
/// (1920×1080 = 0.75 × 2560×1440); reviewed sizes have their own layout file with live-sampled gate colors.
/// Other 16:9 clients (1280..2560 wide, 1280×720 half-res frame) get a derived layout: the 2560×1440 geometry
/// scaled by height/1440, with gates checked against both reviewed references within a 2-pixel neighborhood (minimap frame at tolerance 30).</summary>
internal static class UiLayouts
{
    internal static readonly (int Width, int Height)[] Reviewed = [(2560, 1440), (1920, 1080)];
    internal const string ReviewedText = "2560×1440 or 1920×1080";
    internal const string SupportedText = "reviewed 2560×1440/1920×1080, or derived for other 16:9 clients 1280..2560 wide";
    /// <summary>Gate neighborhood radius (half-res frame pixels) for derived layouts; reviewed layouts stay exact-position.</summary>
    internal const int DerivedGateRadius = 2;
    /// <summary>Per-channel tolerance for the derived minimap-frame gate: its thin ornaments resample differently at
    /// non-reviewed scales (1600×900 live: reviewed points off by up to 29). 0 false positives at 30 (and 50) over the
    /// non-editor/no-minimap frames of the reviewed-size corpus.</summary>
    internal const int DerivedMinimapTolerance = 30;

    internal static bool IsReviewed(int width, int height) => Reviewed.Contains((width, height));

    /// <summary>Unreviewed client whose host screenshot frame is exactly 1280×720 (same gate coordinates) and whose
    /// full-resolution capture fits the 2560 px OCR/readback bound.</summary>
    internal static bool IsDerivable(int width, int height) =>
        !IsReviewed(width, height) && width is >= 1280 and <= 2560 && height * 1280 / width == 720;

    internal static bool IsSupported(int width, int height) => IsReviewed(width, height) || IsDerivable(width, height);

    internal static string Alt(int width, int height) => $"alt-{width}x{height}.json";
    internal static string Normal(int width, int height) => $"normal-en-{width}x{height}.json";
    internal static string File(string kind, int width, int height) => kind == "normal" ? Normal(width, height) : Alt(width, height);
    internal static string Path(string file) => System.IO.Path.Combine(AppContext.BaseDirectory, "uilayouts", file);

    /// <summary>Full-resolution client pixels per 2560×1440 reference pixel.</summary>
    internal static double Scale(int width) => width / 2560.0;

    /// <summary>Reference (2560×1440) pixel to client pixel, rounded half away from zero.</summary>
    internal static int ToClient(double reference, double scale) => (int)Math.Round(reference * scale, MidpointRounding.AwayFromZero);

    internal static int[] ToClient(int[] rect, double scale) => rect.Select(v => ToClient(v, scale)).ToArray();

    /// <summary>One loaded layout: reviewed file, or derived in memory from the 2560×1440 reference.</summary>
    internal sealed record Layout(JsonElement Root, string File, bool Reviewed, string? DerivedFrom)
    {
        internal int Radius => Reviewed ? 0 : DerivedGateRadius;
    }

    static readonly ConcurrentDictionary<string, Layout?> Cache = new(StringComparer.Ordinal);

    /// <summary>Layout for the normal ("normal") or alternative UI at this client size; null when neither reviewed nor derivable or the file is missing.</summary>
    internal static Layout? Load(string kind, int width, int height) => Cache.GetOrAdd($"{kind}|{width}x{height}|{BaseStamp(kind, width, height)}", _ =>
    {
        var file = File(kind, width, height);
        if (IsReviewed(width, height))
        {
            if (!System.IO.File.Exists(Path(file))) return null;
            using var document = JsonDocument.Parse(System.IO.File.ReadAllText(Path(file)));
            return new(document.RootElement.Clone(), file, true, null);
        }
        if (!IsDerivable(width, height)) return null;
        var reference = File(kind, 2560, 1440);
        var second = File(kind, 1920, 1080);
        if (!System.IO.File.Exists(Path(reference))) return null;
        var root = Derive(JsonNode.Parse(System.IO.File.ReadAllText(Path(reference)))!.AsObject(),
            System.IO.File.Exists(Path(second)) ? JsonNode.Parse(System.IO.File.ReadAllText(Path(second)))!.AsObject() : null, width, height);
        using var derived = JsonDocument.Parse(root.ToJsonString());
        return new(derived.RootElement.Clone(), $"derived:{file}", false, reference);
    });

    static string BaseStamp(string kind, int width, int height)
    {
        var file = Path(IsReviewed(width, height) ? File(kind, width, height) : File(kind, 2560, 1440));
        return System.IO.File.Exists(file) ? System.IO.File.GetLastWriteTimeUtc(file).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) : "missing";
    }

    // Counts, tolerances and frame-pixel gates are not client geometry; text/notes and gate lists are copied verbatim.
    static readonly HashSet<string> NoScale = new(StringComparer.Ordinal)
    { "minimumMatches", "gateTolerance", "ocrGateTolerance", "columns", "visibleRows", "width", "height", "gate", "buildHash", "ui", "uiKind", "notes", "ocrNotes", "fieldGates", "paletteNotes", "note", "scaledFrom" };

    /// <summary>Scales reference geometry by height/1440 and attaches the second reference's gates (same path) as gateAlternates.</summary>
    internal static JsonObject Derive(JsonObject reference, JsonObject? second, int width, int height)
    {
        var scale = height / 1440.0;
        JsonNode? Walk(JsonNode? node, string? key, JsonNode? other)
        {
            if (node is null) return null;
            if (key is not null && NoScale.Contains(key)) return node.DeepClone();
            switch (node)
            {
                case JsonObject obj:
                {
                    var result = new JsonObject();
                    foreach (var (k, v) in obj) result[k] = Walk(v, k, other is JsonObject o && o.TryGetPropertyValue(k, out var ov) ? ov : null);
                    if (obj.ContainsKey("gate") && other is JsonObject alt && alt["gate"] is JsonArray altGate && !JsonNode.DeepEquals(altGate, obj["gate"]))
                        result["gateAlternates"] = new JsonArray(altGate.DeepClone());
                    return result;
                }
                case JsonArray array:
                {
                    var result = new JsonArray();
                    for (var i = 0; i < array.Count; i++)
                        result.Add(Walk(array[i], null, other is JsonArray oa && oa.Count == array.Count ? oa[i] : null));
                    return result;
                }
                case JsonValue value when value.TryGetValue<int>(out var i32):
                    return JsonValue.Create(ToClient(i32, scale));
                case JsonValue value when value.GetValueKind() == JsonValueKind.Number:
                    return JsonValue.Create(Math.Round(value.GetValue<double>() * scale, 4));
                default:
                    return node.DeepClone();
            }
        }
        var root = (JsonObject)Walk(reference, null, second)!;
        root["width"] = width;
        root["height"] = height;
        if (root["editorScene"]?["minimap"] is JsonObject minimap) minimap["tolerance"] = DerivedMinimapTolerance;
        root["derived"] = new JsonObject { ["from"] = "2560x1440", ["scale"] = Math.Round(scale, 6), ["gateRadius"] = DerivedGateRadius,
            ["note"] = "Derived in memory: reference geometry x height/1440; gates (half-res 1280x720 frame) match either reviewed reference within the radius. Not live-reviewed at this size." };
        return root;
    }

    /// <summary>Gate points in reviewed or derived form: passes when the primary gate or any gateAlternates list reaches minimumMatches.</summary>
    internal static bool GatePasses(ScreenProbe.Frame frame, JsonElement gated, int tolerance, int radius, out int bestHits)
    {
        var need = gated.GetProperty("minimumMatches").GetInt32();
        // Derived layouts may widen one gate's tolerance (never narrower than the caller's).
        if (gated.TryGetProperty("tolerance", out var own)) tolerance = Math.Max(tolerance, own.GetInt32());
        bestHits = SceneGeometry.GateMatches(frame, gated.GetProperty("gate"), tolerance, radius);
        if (bestHits >= need) return true;
        if (gated.TryGetProperty("gateAlternates", out var alternates))
            foreach (var gate in alternates.EnumerateArray())
            {
                var hits = SceneGeometry.GateMatches(frame, gate, tolerance, radius);
                if (hits > bestHits) bestHits = hits;
                if (hits >= need) return true;
            }
        return false;
    }

    internal static void SelfTest()
    {
        if (ToClient(2316, 0.75) != 1737 || ToClient(-21, 0.75) != -16 || ToClient(47, 1) != 47 || !IsReviewed(1920, 1080) || IsReviewed(1600, 900)
            || Normal(1920, 1080) != "normal-en-1920x1080.json" || Alt(2560, 1440) != "alt-2560x1440.json")
            throw new InvalidOperationException("UI layout resolution fixture failed.");
        if (!IsDerivable(1600, 900) || !IsDerivable(1280, 720) || !IsDerivable(1706, 960) || IsDerivable(1366, 768) || IsDerivable(1920, 1080)
            || IsDerivable(1920, 1200) || IsDerivable(3840, 2160) || IsDerivable(1024, 576))
            throw new InvalidOperationException("UI layout derivable-size fixture failed.");
        foreach (var (w, h) in Reviewed)
            foreach (var file in new[] { Normal(w, h), Alt(w, h) })
                if (!System.IO.File.Exists(Path(file))) throw new InvalidOperationException("Reviewed UI layout missing: " + file);
        // Deriving 1920×1080 from the 2560 reference must reproduce the reviewed 1920 geometry (gates and re-split OCR columns excepted).
        foreach (var kind in new[] { "normal", "alternative" })
        {
            var reviewed = JsonNode.Parse(System.IO.File.ReadAllText(Path(File(kind, 1920, 1080))))!.AsObject();
            var derived = Derive(JsonNode.Parse(System.IO.File.ReadAllText(Path(File(kind, 2560, 1440))))!.AsObject(), reviewed, 1920, 1080);
            var scene = (derived["editorScene"]!, reviewed["editorScene"]!);
            foreach (var path in new[] { "minimap.center", "minimap.halfDiagonal", "occluders", "safeMargin" })
            {
                JsonNode? a = scene.Item1, b = scene.Item2;
                foreach (var part in path.Split('.')) { a = a?[part]; b = b?[part]; }
                if (a is null || !JsonNode.DeepEquals(a, b)) throw new InvalidOperationException($"Derived {kind} 1920x1080 {path} differs from reviewed.");
            }
            foreach (var (name, occ) in scene.Item1["conditionalOccluders"]!.AsObject())
                if (!JsonNode.DeepEquals(occ!["rect"], scene.Item2["conditionalOccluders"]![name]!["rect"]))
                    throw new InvalidOperationException($"Derived {kind} 1920x1080 conditional {name} rect differs from reviewed.");
            if (derived["editorScene"]!["minimap"]!["gateAlternates"] is not JsonArray
                || derived["editorScene"]!["minimap"]!["tolerance"]?.GetValue<int>() != DerivedMinimapTolerance)
                throw new InvalidOperationException("Derived layout lacks second-reference minimap gate.");
        }
        var small = Load("normal", 1600, 900) ?? throw new InvalidOperationException("Derived 1600x900 normal layout missing.");
        if (small.Reviewed || small.Radius != DerivedGateRadius || small.Root.GetProperty("width").GetInt32() != 1600
            || Load("normal", 1366, 768) is not null || Load("alternative", 2560, 1440) is not { Reviewed: true })
            throw new InvalidOperationException("Derived layout load fixture failed.");
    }
}
