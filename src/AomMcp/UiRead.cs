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

    static string LayoutPath(string file) => UiLayouts.Path(file);

    /// <summary>Alternative-UI preset for the given reviewed client size.</summary>
    internal static int[] Resolve(string field, int width, int height, string hash) => Resolve(field, width, height, hash, UiLayouts.Alt(width, height));

    /// <summary>Reviewed layout file by name, or "derived:&lt;file&gt;" for the in-memory layout derived at this client size.</summary>
    static JsonElement? LoadRoot(string file, int width, int height)
    {
        if (file.StartsWith("derived:", StringComparison.Ordinal))
        {
            var layout = UiLayouts.Load(file.StartsWith("derived:normal", StringComparison.Ordinal) ? "normal" : "alternative", width, height);
            return layout is { Reviewed: false } && layout.File == file ? layout.Root : null;
        }
        if (!File.Exists(LayoutPath(file))) return null;
        using var document = JsonDocument.Parse(File.ReadAllText(LayoutPath(file)));
        return document.RootElement.Clone();
    }

    internal static int[] Resolve(string field, int width, int height, string hash, string file)
    {
        var root = LoadRoot(file, width, height) ?? throw new WorkflowFailure("UI_LAYOUT_MISMATCH", "ui-observe", $"No OCR layout {file} for {width}x{height} ({UiLayouts.SupportedText}).", false, false,
                "Use explicit region for read-only OCR. No input sent.");
        var ui = root.GetProperty("ui").GetString();
        if (!LayoutMatches(root, width, height, hash))
            throw new WorkflowFailure("UI_LAYOUT_MISMATCH", "ui-observe", $"OCR field preset only reviewed for {ui} UI at {root.GetProperty("width").GetInt32()}x{root.GetProperty("height").GetInt32()} on pinned build; client is {width}x{height}.", false, false,
                "Use explicit region for read-only OCR. No input sent.");
        var row = PlayerField.Match(field);
        if (row.Success)
        {
            var number = int.Parse(row.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (!root.GetProperty("playerFields").TryGetProperty(row.Groups[2].Value, out var rect))
                throw new ArgumentException($"Unknown player field preset for {ui} UI. Known: "
                    + string.Join(", ", root.GetProperty("playerFields").EnumerateObject().Select(p => p.Name)) + ".");
            var values = rect.EnumerateArray().Select(e => e.GetInt32()).ToArray();
            values[1] += (int)Math.Round(root.GetProperty("rowCenterFirst").GetDouble()
                + (number - 1) * root.GetProperty("rowStep").GetDouble(), MidpointRounding.AwayFromZero);
            return values;
        }
        if (!root.GetProperty("staticFields").TryGetProperty(field, out var fixedRect))
            throw new ArgumentException($"Unknown UI field preset for {ui} UI. Use players.<1-12>.<field> or one of: "
                + string.Join(", ", root.GetProperty("staticFields").EnumerateObject().Select(p => p.Name)) + ".");
        return fixedRect.EnumerateArray().Select(e => e.GetInt32()).ToArray();
    }

    /// <summary>Frame-only check of one reviewed normal-UI dialog gate (half-resolution pixels of the frame's client size; no build check).</summary>
    internal static bool NormalGate(ScreenProbe.Frame frame, string name, out string detail)
    {
        var layout = UiLayouts.Load("normal", frame.ClientWidth, frame.ClientHeight);
        if (layout is null) { detail = $"no normal layout for {frame.ClientWidth}x{frame.ClientHeight}"; return false; }
        var root = layout.Root;
        var gate = root.GetProperty("ocrGates").GetProperty(name);
        var ok = UiLayouts.GatePasses(frame, gate, root.GetProperty("ocrGateTolerance").GetInt32(), layout.Radius, out var hits);
        detail = $"{name} {hits}/{gate.GetProperty("gate").GetArrayLength()} (need {gate.GetProperty("minimumMatches").GetInt32()})";
        return ok;
    }

    static bool LayoutMatches(JsonElement root, int width, int height, string hash) =>
        width == root.GetProperty("width").GetInt32() && height == root.GetProperty("height").GetInt32()
        && string.Equals(hash, root.GetProperty("buildHash").GetString(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Picks the reviewed UI whose panel gates for this field match the frame: normal-UI dialog gates
    /// (Player Data / Age Settings popup / Load Menu) first, then the alternative Players Settings panel.</summary>
    internal static (string Kind, string File) DetectFieldUi(ScreenProbe.Frame frame, string field, int width, int height, string hash)
    {
        var notes = new List<string>();
        var normal = UiLayouts.Load("normal", width, height);
        if (normal is null)
            throw new WorkflowFailure("UI_LAYOUT_UNREVIEWED", "ui-observe", $"OCR field presets only available for {UiLayouts.SupportedText} clients; client is {width}x{height}.", false, false,
                "Use explicit region for read-only OCR, or switch the game to a supported resolution. No input sent.");
        {
            var root = normal.Root;
            var group = field.Split('.')[0];
            if (!LayoutMatches(root, width, height, hash)) notes.Add("normal layout: client size/build not reviewed");
            else if (!root.GetProperty("fieldGates").TryGetProperty(group, out var gates)) notes.Add($"normal layout: no '{group}' presets");
            else
            {
                var tolerance = root.GetProperty("ocrGateTolerance").GetInt32();
                var failed = gates.EnumerateArray().Select(g => g.GetString()!).Select(name =>
                {
                    var gate = root.GetProperty("ocrGates").GetProperty(name);
                    var ok = UiLayouts.GatePasses(frame, gate, tolerance, normal.Radius, out var hits);
                    return ok ? null : $"{name} {hits}/{gate.GetProperty("gate").GetArrayLength()} (need {gate.GetProperty("minimumMatches").GetInt32()})";
                }).Where(f => f is not null).ToArray();
                if (failed.Length == 0) return ("normal", normal.File);
                notes.Add("normal gates failed: " + string.Join(", ", failed));
            }
        }
        if (ScreenProbe.PlayersPanelVisible(frame) && UiLayouts.Load("alternative", width, height) is { } alt) return ("alternative", alt.File);
        notes.Add("alternative Players Settings gate failed");
        throw new WorkflowFailure("UI_LAYOUT_UNREVIEWED", "ui-observe", $"No reviewed panel for '{field}' detected ({string.Join("; ", notes)}).", false, false,
            "Normal UI: open Scenario > Player Data (agePopup.*: its Age Settings popup; fileBrowser.*: AI Set Load Menu). Alternative UI: open Players Settings. Or use explicit region. No input sent.");
    }

    internal static object Query(Game game, JsonElement args, string buildHash)
    {
        var field = args.TryGetProperty("field", out var f) ? f.GetString() : null;
        var explicitRegion = args.TryGetProperty("region", out var r);
        if ((field is null) == !explicitRegion) throw new ArgumentException("Provide exactly one of field or region.");
        var (width, height) = Ui.ClientSize(game);
        string? uiKind = null;
        bool? layoutReviewed = null;
        int[] region;
        if (explicitRegion) region = r.EnumerateArray().Select(e => e.GetInt32()).ToArray();
        else
        {
            if (!UiLayouts.IsSupported(width, height))
                throw new WorkflowFailure("UI_LAYOUT_UNREVIEWED", "ui-observe", $"OCR field presets only available for {UiLayouts.SupportedText} clients; client is {width}x{height}.", false, false,
                    "Use explicit region for read-only OCR, or switch the game to a supported resolution. No input sent.");
            // Read-only: SceneUi.Capture does not require a reviewed size (ScreenProbe.Capture, used by writers, does).
            var (kind, file) = DetectFieldUi(SceneUi.Capture(game), field!, width, height, buildHash);
            uiKind = kind;
            layoutReviewed = !file.StartsWith("derived:", StringComparison.Ordinal);
            region = Resolve(field!, width, height, buildHash, file);
        }
        if (region.Length != 4) throw new ArgumentException("OCR region must be [x,y,w,h].");
        // Default 3x zoom, reduced so wide regions stay within the 1600 px crop output bound.
        var scale = args.TryGetProperty("scale", out var s) ? s.GetInt32() : Math.Clamp(1600 / Math.Max(1, Math.Max(region[2], region[3])), 1, 3);
        if ( region[2] > 600 || region[3] > 400 || (long)region[2] * region[3] > 160_000)
            throw new ArgumentException("OCR region must be [x,y,w,h] ≤600×400 and ≤160000 pixels.");
        var capture = Ui.CapturePixels(game, 2560);
        if (capture.Width != width || capture.Height != height)
            throw new ArgumentException("OCR requires full-resolution client width ≤2560.");
        var crop = Ui.CropZoom(capture.Bgra, width, height, region, scale);
        var result = Engine.Value.Run(crop.Bgra, crop.Width, crop.Height, format: ImagePixelFormat.Bgra32);
        return new
        {
            field, uiKind, layoutReviewed, region, scale, text = result.Text,
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
            limitation = "OCR guesses text, not widget state or checkpoint proof. Named presets pinned to reviewed normal/alternative English panels at 2560×1440 or 1920×1080 (other 16:9 sizes 1280..2560 wide: derived by scaling, layoutReviewed=false), detected by pixel gates. Do not retry a mutation based solely on OCR.",
        };
    }

    internal sealed record ObservedLine(string Text, int X, int Y);

    internal static ObservedLine[] Lines(Game game, int[] rect, int scale = 3)
    {
        if (rect.Length != 4 || rect[0] < 0 || rect[1] < 0 || rect[2] < 1 || rect[3] < 1
            || rect[2] > 850 || rect[3] > 500 || (long)rect[2] * rect[3] > 300_000)
            throw new ArgumentException("UI readback region outside guarded bound.");
        var capture = Ui.CapturePixels(game, 2560);
        if (!UiLayouts.IsSupported(capture.Width, capture.Height))
            throw new WorkflowFailure("UI_LAYOUT_MISMATCH", "ui-observe", $"Unsupported UI client size {capture.Width}x{capture.Height} ({UiLayouts.SupportedText}).", false, false, "Stop without input.");
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
        // Normal UI Player Data: row 12 field box measured y=768..805 (center ≈787), name box x=427..666.
        var normal = Resolve("players.12.name", 2560, 1440, hash, UiLayouts.Normal(2560, 1440));
        if (normal[0] != 429 || normal[1] != 766 || normal[3] != 42)
            throw new InvalidDataException("Normal UI OCR preset geometry changed.");
        foreach (var name in new[] { "agePopup.title", "agePopup.mythicGod", "fileBrowser.title", "fileBrowser.search", "fileBrowser.row", "fileBrowser.loadInGame" })
            if (Resolve(name, 2560, 1440, hash, UiLayouts.Normal(2560, 1440)) is not [_, _, <= 533, <= 400]) // 3x zoom must fit the 1600 px crop output bound.
                throw new InvalidDataException("Normal UI OCR preset exceeds 3x-zoom crop bound: " + name + ".");
        // 1920×1080 layouts: reference geometry ×0.75 (row 12 center 787 → 590, name box 429 → 322).
        var small = Resolve("players.12.name", 1920, 1080, hash, UiLayouts.Normal(1920, 1080));
        var smallAlt = Resolve("players.6.pop", 1920, 1080, hash);
        if (small[0] != 322 || small[1] != 574 || small[3] != 32 || smallAlt[0] != 1577 || smallAlt[1] != 585)
            throw new InvalidDataException("1920×1080 OCR preset geometry changed.");
        // Derived 1600×900 (scale 0.625): reference name box x 429 → 268; alternative row-6 pop x 2102 → 1314.
        var derived = Resolve("players.12.name", 1600, 900, hash, "derived:" + UiLayouts.Normal(1600, 900));
        var derivedAlt = Resolve("players.6.pop", 1600, 900, hash, "derived:" + UiLayouts.Alt(1600, 900));
        if (derived[0] != 268 || derivedAlt[0] != 1314)
            throw new InvalidDataException("Derived 1600×900 OCR preset geometry changed.");
        try { _ = Resolve("players.1.name", 1600, 900, hash, UiLayouts.Normal(1600, 900)); throw new InvalidDataException("Missing reviewed 1600x900 file accepted."); }
        catch (WorkflowFailure e) when (e.Code == "UI_LAYOUT_MISMATCH") { }
        try { _ = Resolve("players.1.name", 2560, 1440, hash, UiLayouts.Normal(1920, 1080)); throw new InvalidDataException("1080p layout accepted 1440p client."); }
        catch (WorkflowFailure e) when (e.Code == "UI_LAYOUT_MISMATCH") { }
        var blank = new ScreenProbe.Frame(new byte[1280 * 720 * 3], 1280, 720);
        try { DetectFieldUi(blank, "players.1.name", 2560, 1440, hash); throw new InvalidDataException("Blank frame matched an OCR panel gate."); }
        catch (WorkflowFailure e) when (e.Code == "UI_LAYOUT_UNREVIEWED") { }
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
