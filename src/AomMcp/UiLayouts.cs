namespace AomMcp;

/// <summary>Reviewed client resolutions for pixel/OCR UI layouts. The game UI scales proportionally with the client
/// (1920×1080 = 0.75 × 2560×1440); each size has its own reviewed layout file with live-sampled gate colors.</summary>
internal static class UiLayouts
{
    internal static readonly (int Width, int Height)[] Reviewed = [(2560, 1440), (1920, 1080)];
    internal const string ReviewedText = "2560×1440 or 1920×1080";

    internal static bool IsReviewed(int width, int height) => Reviewed.Contains((width, height));
    internal static string Alt(int width, int height) => $"alt-{width}x{height}.json";
    internal static string Normal(int width, int height) => $"normal-en-{width}x{height}.json";
    internal static string File(string kind, int width, int height) => kind == "normal" ? Normal(width, height) : Alt(width, height);
    internal static string Path(string file) => System.IO.Path.Combine(AppContext.BaseDirectory, "uilayouts", file);

    /// <summary>Full-resolution client pixels per 2560×1440 reference pixel.</summary>
    internal static double Scale(int width) => width / 2560.0;

    /// <summary>Reference (2560×1440) pixel to client pixel, rounded half away from zero.</summary>
    internal static int ToClient(double reference, double scale) => (int)Math.Round(reference * scale, MidpointRounding.AwayFromZero);

    internal static int[] ToClient(int[] rect, double scale) => rect.Select(v => ToClient(v, scale)).ToArray();

    internal static void SelfTest()
    {
        if (ToClient(2316, 0.75) != 1737 || ToClient(-21, 0.75) != -16 || ToClient(47, 1) != 47 || !IsReviewed(1920, 1080) || IsReviewed(1600, 900)
            || Normal(1920, 1080) != "normal-en-1920x1080.json" || Alt(2560, 1440) != "alt-2560x1440.json")
            throw new InvalidOperationException("UI layout resolution fixture failed.");
        foreach (var (w, h) in Reviewed)
            foreach (var file in new[] { Normal(w, h), Alt(w, h) })
                if (!System.IO.File.Exists(Path(file))) throw new InvalidOperationException("Reviewed UI layout missing: " + file);
    }
}
