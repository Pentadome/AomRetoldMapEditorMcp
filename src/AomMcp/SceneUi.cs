using System.Text.Json;

namespace AomMcp;

/// <summary>Reviewed alternative-UI editor-scene geometry (minimap, panels) gated by build/client size and pixels.</summary>
internal sealed class SceneUi
{
    internal sealed record Area(string Name, int X, int Y, int W, int H, bool Conditional, bool? Active)
    {
        internal bool Contains(double x, double y, int margin) =>
            x >= X - margin && y >= Y - margin && x < X + W + margin && y < Y + H + margin;
    }

    JsonElement _root;
    internal double MinimapX { get; private set; }
    internal double MinimapY { get; private set; }
    internal double MinimapHalf { get; private set; }
    internal int Tolerance { get; private set; }
    internal int SafeMargin { get; private set; }

    static string LayoutPath() => Path.Combine(AppContext.BaseDirectory, "uilayouts", "alt-2560x1440.json");

    /// <summary>Returns reviewed geometry, or null with a reason when build/client size/layout is not reviewed.</summary>
    internal static SceneUi? TryLoad(Game game, out string reason)
    {
        reason = "";
        if (!File.Exists(LayoutPath())) { reason = "uilayouts/alt-2560x1440.json missing."; return null; }
        using var document = JsonDocument.Parse(File.ReadAllText(LayoutPath()));
        var root = document.RootElement;
        var (width, height) = Ui.ClientSize(game);
        if (!root.TryGetProperty("editorScene", out var scene))
        { reason = "Layout lacks reviewed editorScene section."; return null; }
        if (width != root.GetProperty("width").GetInt32() || height != root.GetProperty("height").GetInt32()
            || !string.Equals(game.Layout.ExeSha256, root.GetProperty("buildHash").GetString(), StringComparison.OrdinalIgnoreCase))
        { reason = $"Editor scene UI geometry only reviewed for alternative UI {root.GetProperty("width").GetInt32()}x{root.GetProperty("height").GetInt32()} on pinned build; client is {width}x{height}."; return null; }
        var minimap = scene.GetProperty("minimap");
        var center = minimap.GetProperty("center");
        return new SceneUi
        {
            _root = scene.Clone(),
            MinimapX = center[0].GetDouble(), MinimapY = center[1].GetDouble(),
            MinimapHalf = minimap.GetProperty("halfDiagonal").GetDouble(),
            Tolerance = scene.GetProperty("gateTolerance").GetInt32(),
            SafeMargin = scene.GetProperty("safeMargin").GetInt32(),
        };
    }

    /// <summary>Captures one half-resolution frame (focuses game) for pixel gates.</summary>
    internal static ScreenProbe.Frame Capture(Game game) => ScreenProbe.DecodePng(Ui.Screenshot(game, 1280));

    bool Gate(ScreenProbe.Frame frame, JsonElement gated) =>
        SceneGeometry.GateMatches(frame, gated.GetProperty("gate"), Tolerance) >= gated.GetProperty("minimumMatches").GetInt32();

    internal bool MinimapVisible(ScreenProbe.Frame frame) => Gate(frame, _root.GetProperty("minimap"));

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
            bool? active = frame is null ? null : Gate(frame, p.Value);
            list.Add(new Area(p.Name, r[0], r[1], r[2], r[3], true, active));
        }
        return list.ToArray();
    }

    internal bool Clear(Area[] occluders, double x, double y, int width, int height) =>
        x >= SafeMargin && y >= SafeMargin && x < width - SafeMargin && y < height - SafeMargin
        && !occluders.Any(o => o.Active != false && o.Contains(x, y, SafeMargin));

    /// <summary>Unreviewed UI fallback: only the central client region is trusted as map-clickable.</summary>
    internal static bool CentralRegion(double x, double y, int width, int height) =>
        x >= width * 0.3 && x <= width * 0.7 && y >= height * 0.25 && y <= height * 0.75;
}
