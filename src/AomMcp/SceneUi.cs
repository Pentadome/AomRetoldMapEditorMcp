using System.Text.Json;

namespace AomMcp;

/// <summary>Reviewed editor-scene geometry (minimap, panels) for the normal or alternative editor UI, gated by build/client size and pixels.</summary>
internal sealed class SceneUi
{
    internal sealed record Area(string Name, int X, int Y, int W, int H, bool Conditional, bool? Active)
    {
        internal bool Contains(double x, double y, int margin) =>
            x >= X - margin && y >= Y - margin && x < X + W + margin && y < Y + H + margin;
    }

    JsonElement _root;
    internal string Kind { get; private set; } = "";
    internal string File { get; private set; } = "";
    internal double MinimapX { get; private set; }
    internal double MinimapY { get; private set; }
    internal double MinimapHalf { get; private set; }
    internal int Tolerance { get; private set; }
    internal int SafeMargin { get; private set; }
    /// <summary>False for a layout derived from the 2560×1440 reference at an unreviewed 16:9 client size.</summary>
    internal bool Reviewed { get; private set; } = true;
    int Radius { get; set; }
    internal string Describe => Reviewed ? $"reviewed {Kind} UI ({File})" : $"derived {Kind} UI ({File}; scaled from reviewed 2560x1440, not live-reviewed at this size; conditional panels treated as present, palette selection disabled)";

    // Reviewed layout files per client size; normal first: its menu-bar detect gate discriminates (minimap frames are identical in both UIs).
    static readonly string[] Kinds = ["normal", "alternative"];

    /// <summary>Loads one reviewed or derived layout without pixel detection; null with reason when build/size mismatch.</summary>
    static SceneUi? Load(Game game, string kind, out string reason)
    {
        reason = "";
        var (width, height) = Ui.ClientSize(game);
        var layout = UiLayouts.Load(kind, width, height);
        if (layout is null) { reason = $"uilayouts/{UiLayouts.File(kind, width, height)} missing."; return null; }
        var root = layout.Root;
        var file = layout.File;
        if (!root.TryGetProperty("editorScene", out var scene))
        { reason = $"{file} lacks reviewed editorScene section."; return null; }
        if (width != root.GetProperty("width").GetInt32() || height != root.GetProperty("height").GetInt32()
            || !string.Equals(game.Layout.ExeSha256, root.GetProperty("buildHash").GetString(), StringComparison.OrdinalIgnoreCase))
        { reason = $"Editor scene UI geometry only reviewed for {root.GetProperty("width").GetInt32()}x{root.GetProperty("height").GetInt32()} on pinned build; client is {width}x{height}."; return null; }
        var minimap = scene.GetProperty("minimap");
        var center = minimap.GetProperty("center");
        return new SceneUi
        {
            _root = scene.Clone(), File = file, Reviewed = layout.Reviewed, Radius = layout.Radius,
            Kind = scene.TryGetProperty("uiKind", out var uiKind) ? uiKind.GetString()! : "alternative",
            MinimapX = center[0].GetDouble(), MinimapY = center[1].GetDouble(),
            MinimapHalf = minimap.GetProperty("halfDiagonal").GetDouble(),
            Tolerance = scene.GetProperty("gateTolerance").GetInt32(),
            SafeMargin = scene.GetProperty("safeMargin").GetInt32(),
        };
    }

    /// <summary>Detects which reviewed UI (normal/alternative) is on screen from pixel gates on one frame.</summary>
    /// <returns>Matching geometry, or null with reason when no reviewed layout's gates match.</returns>
    internal static SceneUi? TryLoad(Game game, out string reason) => TryLoad(game, null, out reason);

    internal static SceneUi? TryLoad(Game game, ScreenProbe.Frame? frame, out string reason)
    {
        var reasons = new List<string>();
        var candidates = new List<SceneUi>();
        var (width, height) = Ui.ClientSize(game);
        if (!UiLayouts.IsSupported(width, height))
        {
            reason = $"Editor scene UI geometry only available for {UiLayouts.SupportedText} clients on pinned build; client is {width}x{height}.";
            return null;
        }
        foreach (var kind in Kinds)
        {
            var ui = Load(game, kind, out var why);
            if (ui is null) reasons.Add(why); else candidates.Add(ui);
        }
        if (candidates.Count == 0) { reason = string.Join(" ", reasons.Distinct()); return null; }
        frame ??= Capture(game);
        foreach (var ui in candidates)
            if (ui.Detected(frame)) { reason = ""; return ui; }
        reason = "No reviewed UI detected (normal-UI menu bar+minimap gate and alternative-UI minimap gate both failed: dialog/menu open, minimap hidden, other language/UI scale).";
        return null;
    }

    /// <summary>Captures one half-resolution frame (focuses game) for pixel gates.</summary>
    internal static ScreenProbe.Frame Capture(Game game)
    {
        var (width, height) = Ui.ClientSize(game);
        return ScreenProbe.DecodePng(Ui.Screenshot(game, 1280), width, height);
    }

    bool Gate(ScreenProbe.Frame frame, JsonElement gated) => UiLayouts.GatePasses(frame, gated, Tolerance, Radius, out _);

    /// <summary>UI-kind identification: optional detect gate (normal-UI menu bar) plus the minimap frame gate.</summary>
    internal bool Detected(ScreenProbe.Frame frame) =>
        (!_root.TryGetProperty("detect", out var detect) || Gate(frame, detect)) && MinimapVisible(frame);

    internal bool MinimapVisible(ScreenProbe.Frame frame) => Gate(frame, _root.GetProperty("minimap"));

    /// <summary>Reviewed bottom list-panel palette geometry (normal UI only); null when not reviewed.</summary>
    internal JsonElement? Palette => Reviewed && _root.TryGetProperty("palette", out var p) ? p : null;

    /// <summary>Static panels plus pixel-gated conditional panels; unknown frame treats conditionals as active.</summary>
    internal Area[] Occluders(ScreenProbe.Frame? frame)
    {
        static int[] R(JsonElement e) => e.EnumerateArray().Select(v => v.GetInt32()).ToArray();
        var list = _root.GetProperty("occluders").EnumerateObject().Select(p =>
        {
            var r = R(p.Value);
            return new Area(p.Name, r[0], r[1], r[2], r[3], false, true);
        }).ToList();
        foreach (var p in _root.GetProperty("conditionalOccluders").EnumerateObject())
        {
            var r = R(p.Value.GetProperty("rect"));
            // Derived layouts never trust a failed conditional gate: the panel is treated as present (fail closed for clicks).
            bool? active = !Reviewed ? true : frame is null ? null : Gate(frame, p.Value);
            list.Add(new Area(p.Name, r[0], r[1], r[2], r[3], true, active));
        }
        return list.ToArray();
    }

    /// <summary>Raw conditional-panel gate observations (independent of the derived-layout fail-closed occluder policy).</summary>
    internal Dictionary<string, bool> ConditionalGates(ScreenProbe.Frame frame) =>
        _root.GetProperty("conditionalOccluders").EnumerateObject().ToDictionary(p => p.Name, p => Gate(frame, p.Value));

    internal bool Clear(Area[] occluders, double x, double y, int width, int height) =>
        x >= SafeMargin && y >= SafeMargin && x < width - SafeMargin && y < height - SafeMargin
        && !occluders.Any(o => o.Active != false && o.Contains(x, y, SafeMargin));

    /// <summary>Unreviewed UI fallback: only the central client region is trusted as map-clickable.</summary>
    internal static bool CentralRegion(double x, double y, int width, int height) =>
        x >= width * 0.3 && x <= width * 0.7 && y >= height * 0.25 && y <= height * 0.75;

    /// <summary>Profile hint only: optionusealternativeeditorui from the newest UserProfile.xml (may be stale until restart).</summary>
    internal static bool? ProfileAlternativeHint()
    {
        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Games", "Age of Mythology Retold");
            if (!Directory.Exists(root)) return null;
            var file = Directory.EnumerateFiles(root, "UserProfile.xml", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3 })
                .OrderByDescending(System.IO.File.GetLastWriteTimeUtc).FirstOrDefault();
            if (file is null) return null;
            var text = System.IO.File.ReadAllText(file); // StreamReader detects the UTF-16 BOM used by the game.
            var match = System.Text.RegularExpressions.Regex.Match(text, "optionusealternativeeditorui[^>]*>\\s*(true|false)\\s*<",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!match.Success)
                match = System.Text.RegularExpressions.Regex.Match(text, "optionusealternativeeditorui\"?\\s*(?:value=)?\"?(true|false)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success ? bool.Parse(match.Groups[1].Value) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }
}
