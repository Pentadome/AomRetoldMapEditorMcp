using System.Text.Json;
using System.Text.RegularExpressions;
using Sdcb.SimdPaddleOCR;
using Sdcb.SimdPaddleOCR.Models.ChineseV6Tiny;

namespace AomMcp;

/// <summary>Read-only OCR of bounded game-client regions; OCR evidence never authorizes mutations alone.</summary>
internal static class UiRead
{
    static readonly Lazy<PaddleOcrAll> Engine = new(() => PaddleOcrAll.LoadAsync(ChineseV6TinyModels.Default).GetAwaiter().GetResult());
    static readonly Regex PlayerField = new(@"^players\.([1-9]|1[0-2])\.([a-zA-Z]+)$", RegexOptions.Compiled);

    static string LayoutPath() => Path.Combine(AppContext.BaseDirectory, "uilayouts", "alt-2560x1440.json");

    internal static int[] Resolve(string field, int width, int height, string hash)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(LayoutPath()));
        var root = document.RootElement;
        if (width != root.GetProperty("width").GetInt32() || height != root.GetProperty("height").GetInt32()
            || !string.Equals(hash, root.GetProperty("buildHash").GetString(), StringComparison.OrdinalIgnoreCase))
            throw new WorkflowFailure("UI_LAYOUT_MISMATCH", "ui-observe", "OCR field preset only reviewed for alternative 2560×1440 UI/build.", false, false,
                "Use explicit region for read-only OCR or provide a separately reviewed normal-UI layout. No input sent.");
        var row = PlayerField.Match(field);
        if (row.Success)
        {
            var number = int.Parse(row.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (!root.GetProperty("playerFields").TryGetProperty(row.Groups[2].Value, out var rect))
                throw new ArgumentException("Unknown player field preset.");
            var values = rect.EnumerateArray().Select(e => e.GetInt32()).ToArray();
            values[1] += (int)Math.Round(root.GetProperty("rowCenterFirst").GetDouble()
                + (number - 1) * root.GetProperty("rowStep").GetDouble(), MidpointRounding.AwayFromZero);
            return values;
        }
        if (!root.GetProperty("staticFields").TryGetProperty(field, out var fixedRect))
            throw new ArgumentException("Unknown UI field preset.");
        return fixedRect.EnumerateArray().Select(e => e.GetInt32()).ToArray();
    }

    internal static object Query(Game game, JsonElement args, string buildHash)
    {
        var field = args.TryGetProperty("field", out var f) ? f.GetString() : null;
        var explicitRegion = args.TryGetProperty("region", out var r);
        if ((field is null) == !explicitRegion) throw new ArgumentException("Provide exactly one of field or region.");
        var (width, height) = Ui.ClientSize(game);
        if (field is not null) ScreenProbe.RequirePlayersPanel(ScreenProbe.Capture(game));
        var region = explicitRegion ? r.EnumerateArray().Select(e => e.GetInt32()).ToArray() : Resolve(field!, width, height, buildHash);
        var scale = args.TryGetProperty("scale", out var s) ? s.GetInt32() : 3;
        if (region.Length != 4 || region[2] > 600 || region[3] > 400 || (long)region[2] * region[3] > 160_000)
            throw new ArgumentException("OCR region must be [x,y,w,h] ≤600×400 and ≤160000 pixels.");
        var capture = Ui.CapturePixels(game, 2560);
        if (capture.Width != width || capture.Height != height)
            throw new ArgumentException("OCR requires full-resolution client width ≤2560.");
        var crop = Ui.CropZoom(capture.Bgra, width, height, region, scale);
        var result = Engine.Value.Run(crop.Bgra, crop.Width, crop.Height, format: ImagePixelFormat.Bgra32);
        return new
        {
            field, region, scale, text = result.Text,
            lines = result.Lines.Select(line => new
            {
                line.Text, score = line.RecognitionScore,
                detectionScore = line.Box.Score,
                box = new[]
                {
                    new[] { region[0] + line.Box.X1 / scale, region[1] + line.Box.Y1 / scale },
                    new[] { region[0] + line.Box.X2 / scale, region[1] + line.Box.Y2 / scale },
                    new[] { region[0] + line.Box.X3 / scale, region[1] + line.Box.Y3 / scale },
                    new[] { region[0] + line.Box.X4 / scale, region[1] + line.Box.Y4 / scale },
                },
            }).ToArray(),
            limitation = "OCR guesses text, not widget state or checkpoint proof. Named presets pinned to observed alternative UI only. Do not retry a mutation based solely on OCR.",
        };
    }

    internal sealed record ObservedLine(string Text, int X, int Y);

    internal static ObservedLine[] Lines(Game game, int[] rect, int scale = 3)
    {
        if (rect.Length != 4 || rect[0] < 0 || rect[1] < 0 || rect[2] < 1 || rect[3] < 1
            || rect[2] > 850 || rect[3] > 500 || (long)rect[2] * rect[3] > 300_000)
            throw new ArgumentException("UI readback region outside guarded bound.");
        var capture = Ui.CapturePixels(game, 2560);
        if (capture.Width != 2560 || capture.Height != 1440)
            throw new WorkflowFailure("UI_LAYOUT_MISMATCH", "ui-observe", "Unreviewed player-settings UI size.", false, false, "Stop without input.");
        var crop = Ui.CropZoom(capture.Bgra, capture.Width, capture.Height, rect, scale);
        return Engine.Value.Run(crop.Bgra, crop.Width, crop.Height, format: ImagePixelFormat.Bgra32)
            .Lines.Select(l => new ObservedLine(l.Text,
                rect[0] + (int)Math.Round((l.Box.X1 + l.Box.X3) / (2 * scale)),
                rect[1] + (int)Math.Round((l.Box.Y1 + l.Box.Y3) / (2 * scale)))).ToArray();
    }

    internal static string Text(Game game, int[] rect) => string.Join(" ", Lines(game, rect).Select(l => l.Text));

    internal static void SelfTest()
    {
        var hash = "dd15d1d838e78faa1bc9854becc3994f4f3a4548ef30efd24108abedc1b84fff";
        var rect = Resolve("players.6.pop", 2560, 1440, hash);
        if (rect.Length != 4 || rect[1] != 780 || rect[0] != 2102)
            throw new InvalidDataException("Alternative UI OCR preset geometry changed.");
        foreach (var (name, width, height, expected) in new[]
        {
            ("control", 99, 26, "Computer"), ("pop", 79, 27, "500"), ("food", 66, 27, "999999"),
            ("age", 122, 24, "Heroic Age"),
        })
        {
            var source = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "ocr-" + name + ".bgra"));
            var crop = Ui.CropZoom(source, width, height, [0, 0, width, height], 4);
            var text = Engine.Value.Run(crop.Bgra, crop.Width, crop.Height, format: ImagePixelFormat.Bgra32).Text;
            if (!text.Contains(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("OCR fixture " + name + " expected " + expected + ", observed " + text + ".");
        }
    }
}
