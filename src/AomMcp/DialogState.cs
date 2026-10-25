using System.Globalization;
using System.Text.Json;

namespace AomMcp;

/// <summary>Live editor dialog detection, control geometry, OCR text/heuristic reads and guarded dialog actions
/// (editor_dialog_state / editor_dialog_act). Open dialogs are recognised by calibrated pixel signatures
/// (uilayouts/dialogs-*.json); controls come from generated/dialogs.json. No memory reads of UI state.</summary>
internal static class DialogState
{
    internal const string StateTool = "editor_dialog_state";
    internal const string ActTool = "editor_dialog_act";
    internal static readonly string[] Actions =
    [
        "open",
        "close",
        "press",
        "click",
        "setText",
        "toggle",
        "select",
    ];
    internal const int MaxText = 256;
    internal const int DefaultControlLimit = 60;
    internal const int MaxControlLimit = 200;
    const int MaxRead = 3;

    // Host polling policy (empirical, research/LIVE-MUTATIONS.md): gadget show/hide lands within one or two frames.
    const int PollMs = 200;
    const int SettleMs = 400;
    const int VerifyFrames = 3;
    const int VerifyFrameMs = 300;
    internal const int DefaultTimeout = 3000;

    // Window geometry policy at the 2560-wide reference: observed signature bounds sit a few px inside the predicted
    // size1024 rect (changed-pixel box); 30 px per edge separates nominal windows from anchored/stretched ones.
    const int NominalEdge = 30;
    const int FitRange = 24;
    const int FitStep = 4;
    const int FitMargin = 8;

    // OCR tiling: UiRead guard is w<=850, h<=500, area<=300k; 3x zoom output must stay <=1600 px per side.
    const int TileWidth = 520;
    const int TileHeight = 480;
    const int TileOverlap = 40;
    const int TileScale = 3;
    const int MaxFieldReads = 32; // Per-control OCR reads (text inputs/dropdowns) per dialog; the rest use tile text.
    static readonly int[] FieldZooms = [2, 3]; // Whole-dialog zoom-3 tiles drop thin glyphs (Seed "-1" read "1"); the field alone at zoom 2 keeps them.
    const int MaxTiles = 24;

    // Checkbox tick heuristic (normal UI, calibration frames 2026-10-09): checked boxes showed ~6% bright-green
    // tick pixels in the box square, unchecked boxes 0%. Not engine state; reported as observedChecked.
    const double CheckedFraction = 0.01; // Live normal UI: ticked Conquest 0.028, unticked boxes 0.
    const double UncheckedFraction = 0.003;

    // setText clearing: OCR length plus margin, bounded (no Ctrl+A: editor hotkeys).
    const int MinBackspaces = 16;
    const int MaxBackspaces = 80;

    // Normal-UI calibration (2026-10-09): these open names ignored gadgetUnreal(open name); the inner XML window name closed them.
    internal static readonly Dictionary<string, string> InnerClose = new(StringComparer.Ordinal)
    {
        ["ATrigGroupEditor"] = "TriggerGroupEditor",
        ["ATriggerArmyGadget"] = "ArmyEditor",
        ["ScriptManager"] = "scriptsDlg",
        ["TriggerEchoWindow"] = "triggerEcho",
        ["ACameraTrackGadget"] = "camEditor",
        ["CameraStatesGadget"] = "CameraStates",
        ["PitchEditorGadget"] = "pitchEditor",
        ["CameraStartDialog"] = "CameraStartDlg",
        ["StringIdDialog"] = "StringIdDlg",
        ["DiplomacyDialog"] = "PlayerDiplomacyDialog",
        ["ATriggerGadget"] = "trEditor",
    };

    // Shipped scripts that only show/hide/refresh gadgets: press without confirmDestructive.
    static readonly HashSet<string> ViewCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "gadgetReal",
        "gadgetUnreal",
        "gadgetToggle",
        "gadgetRefresh",
        "gadgetRealIfNotMP",
        "gadgetToggleIfNotMP",
        "gadgetFlash",
        "uiCloseDialog",
    };

    const string Limitation =
        "Open state comes from calibrated pixel signatures of default (unmoved) windows: moved, covered or uncalibrated windows are not detected (closedCount covers calibrated gadgets only). Control rects are shipped size1024 geometry mapped to client pixels plus an OCR-fitted offset, not live widget positions. OCR text and observedChecked (green tick pixel heuristic) are observations, not engine state; the game-written checkpoint stays authoritative. Shipped scripts run only the listed native calls, not engine Apply/OK/Generate handlers.";

    // ---------------- signatures ----------------

    internal sealed record Signature(string Gadget, int[] Bounds, int[][] Points);

    internal sealed record SignatureSet(
        string File,
        string UiKind,
        int Width,
        int Height,
        string BuildHash,
        int Tolerance,
        double OpenFraction,
        double PartialFraction,
        Dictionary<string, Signature> Dialogs
    );

    internal sealed record Match(Signature Signature, int Matched, double Score, string State);

    internal static SignatureSet[] LoadAll(string? directory = null)
    {
        directory ??= Path.Combine(AppContext.BaseDirectory, "uilayouts");
        return Directory.Exists(directory)
            ? Directory
                .GetFiles(directory, "dialogs-*.json")
                .Order(StringComparer.Ordinal)
                .Select(f => Parse(Path.GetFileName(f), File.ReadAllText(f)))
                .ToArray()
            : [];
    }

    internal static SignatureSet Parse(string file, string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("schema").GetInt32() != 1)
            throw new InvalidDataException($"{file}: unsupported dialog signature schema.");
        var frame = root.GetProperty("frame").EnumerateArray().Select(v => v.GetInt32()).ToArray();
        if (frame is not [1280, 720])
            throw new InvalidDataException(
                $"{file}: signatures must use the 1280x720 screenshot frame."
            );
        var dialogs = new Dictionary<string, Signature>(StringComparer.Ordinal);
        foreach (var d in root.GetProperty("dialogs").EnumerateObject())
        {
            var bounds = d
                .Value.GetProperty("bounds")
                .EnumerateArray()
                .Select(v => v.GetInt32())
                .ToArray();
            var points = d
                .Value.GetProperty("points")
                .EnumerateArray()
                .Select(p => p.EnumerateArray().Select(v => v.GetInt32()).ToArray())
                .ToArray();
            if (
                bounds.Length != 4
                || bounds[2] < 1
                || bounds[3] < 1
                || points.Length < 4
                || points.Any(p =>
                    p.Length != 5
                    || p[0] is < 0 or >= 1280
                    || p[1] is < 0 or >= 720
                    || p.Skip(2).Any(c => c is < 0 or > 255)
                )
            )
                throw new InvalidDataException($"{file}: invalid signature {d.Name}.");
            dialogs[d.Name] = new(d.Name, bounds, points);
        }
        return new(
            file,
            root.GetProperty("uiKind").GetString()!,
            root.GetProperty("width").GetInt32(),
            root.GetProperty("height").GetInt32(),
            root.GetProperty("buildHash").GetString()!,
            root.GetProperty("tolerance").GetInt32(),
            root.GetProperty("openFraction").GetDouble(),
            root.GetProperty("partialFraction").GetDouble(),
            dialogs
        );
    }

    /// <summary>Scores one signature: points authored in the 1280×720 reference frame are mapped per axis into the frame;
    /// off the calibrated client size each point may match within <see cref="UiLayouts.DerivedGateRadius"/> (best effort).</summary>
    internal static Match Score(ScreenProbe.Frame frame, Signature signature, SignatureSet set)
    {
        var radius =
            frame.ClientWidth == set.Width && frame.ClientHeight == set.Height
                ? 0
                : UiLayouts.DerivedGateRadius;
        bool Near(int x, int y, int[] p)
        {
            if (!frame.Contains(x, y))
                return false;
            var c = frame.At(x, y);
            return Math.Max(
                    Math.Abs(c.R - p[2]),
                    Math.Max(Math.Abs(c.G - p[3]), Math.Abs(c.B - p[4]))
                ) <= set.Tolerance;
        }
        var matched = signature.Points.Count(p =>
        {
            int x = frame.MapX(p[0]),
                y = frame.MapY(p[1]);
            for (var dy = -radius; dy <= radius; dy++)
            for (var dx = -radius; dx <= radius; dx++)
                if (Near(x + dx, y + dy, p))
                    return true;
            return false;
        });
        var score = (double)matched / signature.Points.Length;
        return new(
            signature,
            matched,
            Math.Round(score, 3),
            score >= set.OpenFraction ? "open"
                : score >= set.PartialFraction ? "partial"
                : "closed"
        );
    }

    // ---------------- observation ----------------

    internal sealed class Observation
    {
        internal required ScreenProbe.Frame Frame { get; init; }
        internal required int Width { get; init; }
        internal required int Height { get; init; }
        internal string? UiKind { get; init; }
        internal string UiSource { get; init; } = "undetected";
        internal SignatureSet? Set { get; init; }
        internal bool Exact { get; init; }
        internal Match[] Matches { get; init; } = [];
        internal List<string> Warnings { get; } = [];

        /// <summary>Client pixels per 2560-reference pixel along x.</summary>
        internal double Scale => UiLayouts.Scale(Width);

        /// <summary>Client pixels per 2560-reference pixel along y (differs from <see cref="Scale"/> off 16:9).</summary>
        internal double ScaleY => UiLayouts.ScaleY(Height);

        /// <summary>True when signatures calibrated at another size are reused on a supported client (reviewed scene layout
        /// without own dialog signatures, e.g. 1920×1080 in session 3, or derivable): pixel actions are best effort.</summary>
        internal bool BestEffort => !Exact && UiLayouts.IsSupported(Width, Height);

        /// <summary>Score of one gadget's signature.</summary>
        internal Match? Find(string gadget) =>
            Matches.FirstOrDefault(m => m.Signature.Gadget == gadget);

        /// <summary>Signature bounds (2560 reference) to client pixels per axis.</summary>
        internal int[] Bounds(Signature s) =>
            [.. s.Bounds.Select((v, i) => (int)Math.Round(v * (i % 2 == 0 ? Scale : ScaleY)))];
    }

    /// <summary>One screenshot: UI kind by SceneUi gates, then scoring of all hash/UI-matching signatures.</summary>
    internal static Observation Observe(Game game, string hash, SignatureSet[] sets)
    {
        _ = game.Editor();
        var (width, height) = Ui.ClientSize(game);
        var frame = SceneUi.Capture(game);
        if (!UiLayouts.IsSupported(width, height))
        {
            var unsupported = new Observation
            {
                Frame = frame,
                Width = width,
                Height = height,
            };
            unsupported.Warnings.Add(
                $"Dialog signatures need a client {UiLayouts.SupportedText}; client is {width}x{height}. No dialog detection."
            );
            return unsupported;
        }
        var ui = SceneUi.TryLoad(game, frame, out var reason);
        var candidates = sets.Where(s => s.BuildHash == hash && (ui is null || s.UiKind == ui.Kind))
            .ToArray();
        var warnings = new List<string>();
        if (sets.Length == 0)
            warnings.Add(
                "No dialog signature files (uilayouts/dialogs-*.json) shipped with this host."
            );
        else if (!sets.Any(s => s.BuildHash == hash))
            warnings.Add(
                "Dialog signatures were calibrated on another game build; detection unavailable."
            );
        if (ui is null && candidates.Length > 0)
            warnings.Add(
                "UI kind not detected (a dialog may cover the minimap gate): scored every signature for this build. "
                    + reason
            );
        // Several files may match (other UI kinds/sizes when undetected): pick exact size first, then the best total score.
        var set = candidates
            .OrderByDescending(s => s.Width == width && s.Height == height)
            .ThenByDescending(s => s.Dialogs.Values.Sum(d => Score(frame, d, s).Score))
            .FirstOrDefault();
        if (set is null && ui is not null && sets.Any(s => s.BuildHash == hash))
            warnings.Add(
                $"No calibrated dialog signatures for the {ui.Kind} UI; open/close detection and pixel clicks unavailable (script presses still work)."
            );
        var exact = set is not null && set.Width == width && set.Height == height;
        if (set is not null && !exact)
            warnings.Add(
                $"Signatures calibrated at {set.Width}x{set.Height}; reused on {width}x{height} (signatureReviewed=false, points mapped per axis). "
                    + (
                        UiLayouts.IsSupported(width, height)
                            ? "Pixel clicks/typing are best effort (layoutReviewed=false)."
                            : "Pixel clicks/typing refused."
                    )
            );
        var observation = new Observation
        {
            Frame = frame,
            Width = width,
            Height = height,
            UiKind =
                ui?.Kind
                ?? (
                    set?.Dialogs.Values.Any(d => Score(frame, d, set).State == "open") == true
                        ? set.UiKind
                        : null
                ),
            UiSource =
                ui is not null ? "pixelGates"
                : set is not null ? "signatureMatch"
                : "undetected",
            Set = set,
            Exact = exact,
            Matches = set is null ? [] : [.. set.Dialogs.Values.Select(d => Score(frame, d, set))],
        };
        observation.Warnings.AddRange(warnings);
        if (observation.UiSource == "signatureMatch" && observation.UiKind is null)
            observation.Warnings.Add("UI kind unknown (no gate and no open signature).");
        return observation;
    }

    // ---------------- catalog geometry ----------------

    internal sealed record Target(
        DialogWindow Window,
        string Gadget,
        string TargetName,
        string Close,
        DialogControl[] Controls
    );

    /// <summary>Window/open-name entry for a gadget in one UI kind; null when the catalog has no decoded XML for it.</summary>
    internal static Target? Resolve(IReadOnlyList<DialogWindow> windows, string ui, string gadget)
    {
        foreach (var w in windows.Where(w => w.Ui == ui))
        {
            var open =
                w.OpenNames.FirstOrDefault(n => n.Name == gadget)
                ?? (
                    w.Window == gadget
                        ? new DialogOpenName(gadget, w.Window, "window", "untested", gadget)
                        : null
                );
            if (open is null)
                continue;
            var controls =
                open.Target == w.Window
                    ? w.Controls
                    : [.. w.Controls.Where(c => Ancestors(w, c).Contains(open.Target))];
            return new(w, gadget, open.Target, open.Close, controls);
        }
        return null;
    }

    static IEnumerable<string> Ancestors(DialogWindow w, DialogControl c)
    {
        var byName = w.Controls.GroupBy(x => x.Name).ToDictionary(g => g.Key, g => g.First());
        var seen = new HashSet<string>();
        for (
            var parent = c.Parent;
            parent is not null && seen.Add(parent);
            parent = byName.GetValueOrDefault(parent)?.Parent
        )
            yield return parent;
    }

    static bool Hidden(DialogWindow w, DialogControl c)
    {
        if (c.DefaultHidden == true)
            return true;
        var byName = w.Controls.GroupBy(x => x.Name).ToDictionary(g => g.Key, g => g.First());
        return Ancestors(w, c).Any(a => byName.GetValueOrDefault(a)?.DefaultHidden == true);
    }

    /// <summary>size1024 [x1,y1,x2,y2] to client [x,y,w,h]: 1024x768 design space stretched over the client.</summary>
    internal static int[] ToClient(double[] s, int width, int height)
    {
        double fx = width / 1024.0,
            fy = height / 768.0;
        static int R(double v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);
        int x = R(s[0] * fx),
            y = R(s[1] * fy);
        return [x, y, R(s[2] * fx) - x, R(s[3] * fy) - y];
    }

    static double[]? TargetRect(Target t) =>
        t.TargetName == t.Window.Window
            ? t.Window.Size1024
            : t.Window.Controls.FirstOrDefault(c => c.Name == t.TargetName)?.Size1024;

    internal static string ControlKind(DialogControl c)
    {
        var type = c.Type.ToLowerInvariant();
        if (c.Dropdown == true || type.Contains("dropdown"))
            return "dropdown";
        return type switch
        {
            "button" or "statebutton" or "meshbutton" => "button",
            "checkbutton" => "checkbox",
            "textfield" when c.ReadOnly != true => "textInput",
            "editbox" => "textInput",
            "radioset" => "radio",
            "list" => "list",
            "slider" => "slider",
            "textfield" or "textbox" or "formattext" or "static" => "text",
            "gadget" or "dialog" => "container",
            _ => "other",
        };
    }

    /// <summary>Shortest normalized label that may match with OCR edits on best-effort client sizes.</summary>
    const int FuzzyLabelMinChars = 6;

    /// <summary>Normalized label characters per tolerated OCR edit. Live 1920×1080 (session 3 follow-up, 2026-10-25):
    /// "Include Script" read as "Inelude Seript" (2 edits in 13 characters).</summary>
    const int FuzzyLabelCharsPerEdit = 6;

    /// <summary>True when some window of <paramref name="read"/> lies within len/<see cref="FuzzyLabelCharsPerEdit"/>
    /// Levenshtein edits of <paramref name="label"/> (both normalized). Used only for scaled (best-effort) signatures.</summary>
    /// <param name="read">Normalized OCR text.</param>
    /// <param name="label">Normalized expected label.</param>
    internal static bool NearLabel(string read, string label)
    {
        if (label.Length < FuzzyLabelMinChars)
            return false;
        var edits = label.Length / FuzzyLabelCharsPerEdit;
        for (var start = 0; start < read.Length; start++)
        for (var length = label.Length - edits; length <= label.Length + edits; length++)
            if (
                length > 0
                && start + length <= read.Length
                && PlayerSettings.Distance(read.Substring(start, length), label) <= edits
            )
                return true;
        return false;
    }

    internal static string Norm(string? s) =>
        new((s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>Path separators that OCR may drop or misread as a thin glyph.</summary>
    static readonly char[] Separators = ['\\', '/', '|'];

    /// <summary>Glyphs OCR substituted for a path separator in live reads (2026-10-13: "om10\om10_diffbonus.xs" read as "om10lom10_diffbonus.xs").</summary>
    static readonly string[] SeparatorReads = ["", "l", "i", "1"];

    /// <summary>True when OCR text equals typed text after <see cref="Norm"/>, allowing every path separator in
    /// <paramref name="typed"/> to read as nothing or one thin glyph (same substitution for all; other glyphs exact).</summary>
    /// <param name="typed">Text that was typed or is expected.</param>
    /// <param name="read">OCR text.</param>
    internal static bool SameText(string typed, string read) =>
        MatchesVariant(typed, Norm(read), (candidate, observed) => candidate == observed);

    /// <summary>Shortest normalized OCR prefix <see cref="StartsText"/> accepts (fewer characters match too many names).</summary>
    internal const int MinPrefixChars = 4;

    /// <summary>True when OCR text is a cut-off start of typed text: <see cref="SameText"/>'s separator tolerance, but the
    /// read may be shorter (a list column clipped it). At least <see cref="MinPrefixChars"/> normalized characters are required.</summary>
    /// <param name="typed">Text that was typed or is expected.</param>
    /// <param name="read">OCR text of a possibly clipped row.</param>
    internal static bool StartsText(string typed, string read) =>
        Norm(read) is { Length: >= MinPrefixChars } observed
        && (
            MatchesVariant(
                typed,
                observed,
                (candidate, seen) => candidate.StartsWith(seen, StringComparison.Ordinal)
            )
            // The clip edge may cut the last glyph (alternative UI 1920x1080, 2026-10-25: "aommcp_probe.xs" read as
            // "aommcp_prok"), so its read is unreliable; the rest must still match.
            || (
                observed.Length > MinPrefixChars
                && MatchesVariant(
                    typed,
                    observed[..^1],
                    (candidate, seen) => candidate.StartsWith(seen, StringComparison.Ordinal)
                )
            )
        );

    /// <summary>True when OCR text lies inside typed text (a text field shows only the part around its caret, and OCR may drop or
    /// misread the last glyph): same separator tolerance as <see cref="SameText"/>, at least <see cref="MinPrefixChars"/>
    /// normalized characters.</summary>
    /// <param name="typed">Text that was typed.</param>
    /// <param name="read">OCR text of the field.</param>
    internal static bool InsideText(string typed, string read) =>
        Norm(read) is { Length: >= MinPrefixChars } observed
        && MatchesVariant(
            typed,
            observed,
            (candidate, seen) => candidate.Contains(seen, StringComparison.Ordinal)
        );

    /// <summary>Tries the typed text as is, then with every path separator replaced by each OCR read glyph.</summary>
    /// <param name="typed">Text that was typed or is expected.</param>
    /// <param name="observed">Normalized OCR text.</param>
    /// <param name="compare">Comparison of a normalized typed variant with the normalized OCR text.</param>
    static bool MatchesVariant(string typed, string observed, Func<string, string, bool> compare)
    {
        if (compare(Norm(typed), observed))
            return true;
        if (typed.IndexOfAny(Separators) < 0)
            return false;
        foreach (var glyph in SeparatorReads)
        {
            var candidate = typed;
            foreach (var separator in Separators)
                candidate = candidate.Replace(
                    separator.ToString(),
                    glyph,
                    StringComparison.Ordinal
                );
            if (compare(Norm(candidate), observed))
                return true;
        }
        return false;
    }

    /// <summary>Normalized text with common OCR glyph confusions folded (l/1/| → i, 0 → o); label comparisons only.</summary>
    static string Loose(string norm) => norm.Replace('l', 'i').Replace('1', 'i').Replace('0', 'o');

    internal sealed record Placed(DialogControl Control, string Kind, int[] Rect, bool Hidden);

    internal sealed record Geometry(
        string Mode,
        int[]? Predicted,
        int Dx,
        int Dy,
        int Anchors,
        bool Fitted
    );

    static Placed[] Place(Target t, Observation o, int dx, int dy) =>
        [
            .. t
                .Controls.Where(c => c.Size1024 is { Length: 4 })
                .Select(c =>
                {
                    var r = ToClient(c.Size1024!, o.Width, o.Height);
                    return new Placed(
                        c,
                        ControlKind(c),
                        [r[0] + dx, r[1] + dy, r[2], r[3]],
                        Hidden(t.Window, c)
                    );
                }),
        ];

    static string Mode(Target t, Observation o, Signature s)
    {
        var rect = TargetRect(t);
        if (rect is not { Length: 4 })
            return "unverified";
        var p = ToClient(rect, o.Width, o.Height);
        var b = o.Bounds(s);
        var edge = NominalEdge * o.Scale;
        return
            Math.Abs(p[0] - b[0]) <= edge
            && Math.Abs(p[1] - b[1]) <= edge
            && Math.Abs(p[0] + p[2] - b[0] - b[2]) <= edge
            && Math.Abs(p[1] + p[3] - b[1] - b[3]) <= edge
            ? "nominal"
            : "approximate";
    }

    /// <summary>Offset (±24 px at 2560 wide, 4 px steps) maximizing labelled controls whose label OCR line lies in the shifted rect.</summary>
    internal static (int Dx, int Dy, int Anchors) Fit(
        IReadOnlyList<Placed> controls,
        IReadOnlyList<UiRead.ObservedLine> lines,
        double scale = 1
    )
    {
        var labelled = controls
            .Where(c => !c.Hidden && Norm(c.Control.Label).Length >= 2 && c.Kind != "container")
            .ToArray();
        var normalized = lines
            .Select(l => (Line: l, Text: Norm(l.Text)))
            .Where(l => l.Text.Length > 0)
            .ToArray();
        int range = (int)Math.Round(FitRange * scale),
            step = Math.Max(1, (int)Math.Round(FitStep * scale)),
            margin = (int)Math.Round(FitMargin * scale);
        (int Dx, int Dy, int Anchors) best = (0, 0, -1);
        for (var dy = -range; dy <= range; dy += step)
        for (var dx = -range; dx <= range; dx += step)
        {
            var anchors = labelled.Count(c =>
                normalized.Any(l =>
                    l.Text.Contains(Norm(c.Control.Label), StringComparison.Ordinal)
                    && Inside(
                        l.Line.X,
                        l.Line.Y,
                        [
                            c.Rect[0] + dx - margin,
                            c.Rect[1] + dy - margin,
                            c.Rect[2] + 2 * margin,
                            c.Rect[3] + 2 * margin,
                        ]
                    )
                )
            );
            if (
                anchors > best.Anchors
                || (
                    anchors == best.Anchors
                    && Math.Abs(dx) + Math.Abs(dy) < Math.Abs(best.Dx) + Math.Abs(best.Dy)
                )
            )
                best = (dx, dy, anchors);
        }
        return best.Anchors >= 2 ? best : (0, 0, Math.Max(0, best.Anchors));
    }

    static bool Inside(int x, int y, int[] r) =>
        x >= r[0] && y >= r[1] && x < r[0] + r[2] && y < r[1] + r[3];

    // ---------------- OCR ----------------

    /// <summary>OCR of a client rect in guarded tiles with overlap; duplicate lines (same text, nearby center) dropped.</summary>
    internal static List<UiRead.ObservedLine> Read(
        (int Width, int Height, byte[] Bgra) capture,
        int[] rect,
        ref int tilesLeft
    )
    {
        int x0 = Math.Max(0, rect[0]),
            y0 = Math.Max(0, rect[1]);
        int x1 = Math.Min(capture.Width, rect[0] + rect[2]),
            y1 = Math.Min(capture.Height, rect[1] + rect[3]);
        var lines = new List<UiRead.ObservedLine>();
        for (var y = y0; y < y1; y += TileHeight - TileOverlap)
        {
            for (var x = x0; x < x1; x += TileWidth - TileOverlap)
            {
                if (tilesLeft-- <= 0)
                    return Dedupe(lines);
                int w = Math.Min(TileWidth, x1 - x),
                    h = Math.Min(TileHeight, y1 - y);
                if (w >= 8 && h >= 8)
                    lines.AddRange(UiRead.Lines(capture, [x, y, w, h], TileScale));
                if (x + TileWidth >= x1)
                    break;
            }
            if (y + TileHeight >= y1)
                break;
        }
        return Dedupe(lines);
    }

    internal static List<UiRead.ObservedLine> Dedupe(IEnumerable<UiRead.ObservedLine> lines)
    {
        var kept = new List<UiRead.ObservedLine>();
        foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l.Text)))
        {
            var twin = kept.FindIndex(k =>
                Math.Abs(k.X - line.X) <= 16
                && Math.Abs(k.Y - line.Y) <= 10
                && (
                    Norm(k.Text).Contains(Norm(line.Text)) || Norm(line.Text).Contains(Norm(k.Text))
                )
            );
            if (twin < 0)
                kept.Add(line);
            else if (line.Text.Length > kept[twin].Text.Length)
                kept[twin] = line; // Tile edges clip text: keep the longer read.
        }
        return [.. kept.OrderBy(l => l.Y / 12).ThenBy(l => l.X)];
    }

    /// <summary>Assigns each line to the smallest containing non-container control rect; others stay unassigned.</summary>
    internal static (
        Dictionary<Placed, List<UiRead.ObservedLine>> Assigned,
        List<UiRead.ObservedLine> Other
    ) Assign(
        IReadOnlyList<Placed> controls,
        IEnumerable<UiRead.ObservedLine> lines,
        double scale = 1
    )
    {
        var assigned = new Dictionary<Placed, List<UiRead.ObservedLine>>();
        var other = new List<UiRead.ObservedLine>();
        var pad = (int)Math.Round(4 * scale);
        foreach (var line in lines)
        {
            var owner = controls
                .Where(c =>
                    !c.Hidden
                    && c.Kind != "container"
                    && Inside(
                        line.X,
                        line.Y,
                        [c.Rect[0] - pad, c.Rect[1] - pad, c.Rect[2] + 2 * pad, c.Rect[3] + 2 * pad]
                    )
                )
                .OrderBy(c => (long)c.Rect[2] * c.Rect[3])
                .FirstOrDefault();
            if (owner is null)
                other.Add(line);
            else
                (assigned.TryGetValue(owner, out var list) ? list : assigned[owner] = []).Add(line);
        }
        return (assigned, other);
    }

    /// <summary>Text of a control without its own label (text inputs show "Label: value").</summary>
    internal static string Value(Placed c, IEnumerable<UiRead.ObservedLine> lines)
    {
        var label = Norm(c.Control.Label);
        var loose = Loose(label);
        var parts = new List<string>();
        var arrow = c.Kind == "dropdown" ? c.Rect[0] + c.Rect[2] - c.Rect[3] * 3 / 2 : int.MaxValue; // Yellow arrow button OCRs as "A".
        foreach (var line in lines.Where(l => l.X < arrow).OrderBy(l => l.X))
        {
            var text = line.Text.Trim();
            var key = Loose(Norm(text));
            if (label.Length > 0 && key == loose)
                continue;
            // Static captions OCR in pieces ("Handi" "cap", "Al Name"): drop fragments of the label itself.
            if (
                label.Length > 0
                && c.Kind is "text" or "button" or "checkbox"
                && key.Length >= 2
                && loose.Contains(key, StringComparison.Ordinal)
            )
                continue;
            if (label.Length > 0 && Norm(text).StartsWith(label, StringComparison.Ordinal))
            {
                // Drop the label prefix: advance until its normalized characters are consumed.
                var consumed = 0;
                var i = 0;
                for (; i < text.Length && consumed < label.Length; i++)
                    if (char.IsLetterOrDigit(text[i]))
                        consumed++;
                text = text[i..].TrimStart(':', ' ');
            }
            if (text.Length > 0)
                parts.Add(text);
        }
        return string.Join(" ", parts);
    }

    /// <summary>OCR of one control's own rect (padded 4 px) at the given zoom, capped by the 1600 px crop output bound.</summary>
    internal static List<UiRead.ObservedLine> ReadControl(
        (int Width, int Height, byte[] Bgra) capture,
        Placed c,
        int zoom
    )
    {
        var rect = Clip(Pad(c.Rect, 4), capture);
        zoom = Math.Min(zoom, 1600 / Math.Max(rect[2], rect[3]));
        return zoom < 1 ? [] : Dedupe(UiRead.Lines(capture, rect, zoom));
    }

    /// <summary>Field text (label removed) from per-control reads: first non-empty zoom of <see cref="FieldZooms"/>.</summary>
    internal static (string Text, int Zoom) FieldValue(
        (int Width, int Height, byte[] Bgra) capture,
        Placed c
    )
    {
        foreach (var zoom in FieldZooms)
            if (Value(c, ReadControl(capture, c, zoom)) is { Length: > 0 } text)
                return (text, zoom);
        return ("", 0);
    }

    /// <summary>Green-tick fraction in the checkbox square (left of the control rect, full-resolution capture), clipped to the
    /// dialog bounds so map grass behind the window never counts.</summary>
    internal static double TickFraction(
        (int Width, int Height, byte[] Bgra) capture,
        int[] rect,
        int[] bounds
    )
    {
        var side = rect[3];
        int x0 = Math.Max(Math.Max(0, bounds[0]), rect[0] - side / 4),
            y0 = Math.Max(Math.Max(0, bounds[1]), rect[1] - side / 4);
        int x1 = Math.Min(Math.Min(capture.Width, bounds[0] + bounds[2]), rect[0] + 2 * side);
        int y1 = Math.Min(
            Math.Min(capture.Height, bounds[1] + bounds[3]),
            rect[1] + rect[3] + side / 4
        );
        if (x1 <= x0 || y1 <= y0)
            return 0;
        var hits = 0;
        for (var y = y0; y < y1; y++)
        for (var x = x0; x < x1; x++)
        {
            var o = (y * capture.Width + x) * 4;
            int b = capture.Bgra[o],
                g = capture.Bgra[o + 1],
                r = capture.Bgra[o + 2];
            if (g >= 90 && g - r >= 30 && g - b >= 30)
                hits++; // Tick anti-aliasing is pale, e.g. (147,223,134) and (114,202,74).
        }
        return (double)hits / ((x1 - x0) * (y1 - y0));
    }

    internal static bool? Checked(double fraction) =>
        fraction >= CheckedFraction ? true
        : fraction <= UncheckedFraction ? false
        : null;

    // ---------------- state tool ----------------

    internal sealed record Dialogs(IReadOnlyList<DialogWindow> Windows, string Status);

    /// <summary>Reads open calibrated dialogs, their controls and (read=true) OCR text/checkbox ticks. Read-only.</summary>
    internal static object State(
        Game game,
        string hash,
        Dialogs catalog,
        SignatureSet[] sets,
        JsonElement args
    )
    {
        var gadget = args.TryGetProperty("gadget", out var g) ? g.GetString()!.Trim() : "";
        var read = args.TryGetProperty("read", out var r) && r.GetBoolean();
        var includeControls = args.TryGetProperty("controls", out var c)
            ? c.GetBoolean()
            : gadget.Length > 0;
        var offset = args.TryGetProperty("offset", out var of) ? of.GetInt32() : 0;
        var limit = args.TryGetProperty("limit", out var li) ? li.GetInt32() : DefaultControlLimit;
        var o = Observe(game, hash, sets);
        if (gadget.Length > 0 && o.Set is not null && o.Find(gadget) is null)
            throw new ArgumentException(
                $"Gadget '{gadget}' has no calibrated signature. Calibrated: {string.Join(", ", o.Set.Dialogs.Keys.Order(StringComparer.Ordinal))}. Use editor_dialogs for static metadata."
            );
        var ui = o.UiKind ?? o.Set?.UiKind;
        var selected = o
            .Matches.Where(m =>
                gadget.Length > 0 ? m.Signature.Gadget == gadget : m.State != "closed"
            )
            .OrderByDescending(m => m.Score)
            .ToArray();
        (int Width, int Height, byte[] Bgra)? capture =
            read && selected.Any(m => m.State != "closed")
                ? Ui.CapturePixels(game, Ui.FullResolutionWidth)
                : null;
        var tiles = MaxTiles;
        var reads = 0;
        var dialogs = selected
            .Select(m =>
            {
                var target = ui is null ? null : Resolve(catalog.Windows, ui, m.Signature.Gadget);
                var doRead = capture is not null && m.State != "closed" && reads++ < MaxRead;
                return Describe(
                    o,
                    m,
                    target,
                    doRead ? capture : null,
                    ref tiles,
                    includeControls,
                    offset,
                    limit
                );
            })
            .ToArray();
        if (capture is not null && reads > MaxRead)
            o.Warnings.Add($"OCR limited to {MaxRead} dialogs; name one with gadget.");
        if (tiles < 0)
            o.Warnings.Add(
                $"OCR stopped after {MaxTiles} tiles; name one gadget for a complete read."
            );
        return new
        {
            uiKind = ui,
            uiSource = o.UiSource,
            client = new { width = o.Width, height = o.Height },
            signatureFile = o.Set?.File,
            signatureReviewed = o.Exact,
            calibratedGadgets = o.Set?.Dialogs.Count ?? 0,
            catalog = catalog.Status,
            dialogs,
            closedCount = gadget.Length > 0
                ? (int?)null
                : o.Matches.Count(m => m.State == "closed"),
            warnings = o.Warnings,
            nextAction = "Act with editor_dialog_act (open/close/press/select work by gadget/control name; click/setText/toggle need an open nominal dialog at the calibrated size). Uncalibrated windows: editor_dialogs + editor_gadgetReal/editor_gadgetUnreal + screenshot.",
            limitation = Limitation,
        };
    }

    static object Describe(
        Observation o,
        Match m,
        Target? target,
        (int Width, int Height, byte[] Bgra)? capture,
        ref int tiles,
        bool includeControls,
        int offset,
        int limit
    )
    {
        var bounds = o.Bounds(m.Signature);
        if (target is null)
            return new
            {
                gadget = m.Signature.Gadget,
                state = m.State,
                score = m.Score,
                bounds,
                closeName = CloseNames(m.Signature.Gadget, null)[0],
                note = "No decoded XML window for this gadget (or dialog catalog unavailable); controls unknown.",
            };
        var mode = Mode(target, o, m.Signature);
        var placed = Place(target, o, 0, 0);
        List<UiRead.ObservedLine>? lines = null;
        var geometry = new Geometry(
            mode,
            TargetRect(target) is { Length: 4 } tr ? ToClient(tr, o.Width, o.Height) : null,
            0,
            0,
            0,
            false
        );
        if (capture is not null)
        {
            lines = Read(capture.Value, bounds, ref tiles);
            var (dx, dy, anchors) = Fit(placed, lines, o.Scale);
            geometry = geometry with { Dx = dx, Dy = dy, Anchors = anchors, Fitted = anchors >= 2 };
            placed = Place(target, o, dx, dy);
        }
        var (assigned, other) = lines is null ? ([], []) : Assign(placed, lines, o.Scale);
        int[] fieldReads = [MaxFieldReads]; // Shared countdown; negative = fields that fell back to tile text.
        var listed = placed
            .Where(p =>
                p.Kind is not ("container" or "other")
                && (p.Kind != "text" || Norm(p.Control.Label).Length > 0 || assigned.ContainsKey(p))
            )
            .ToArray();
        return new
        {
            gadget = m.Signature.Gadget,
            window = target.Window.Window,
            target = target.TargetName,
            title = target.Window.Title,
            state = m.State,
            score = m.Score,
            bounds,
            closeName = CloseNames(m.Signature.Gadget, target)[0],
            geometry = new
            {
                mode = geometry.Mode,
                predicted = geometry.Predicted,
                offset = new[] { geometry.Dx, geometry.Dy },
                anchors = geometry.Anchors,
                fitted = geometry.Fitted,
            },
            controlsTotal = listed.Length,
            offset,
            nextOffset = includeControls && offset + limit < listed.Length
                ? offset + limit
                : (int?)null,
            controls = includeControls
                ? listed
                    .Skip(offset)
                    .Take(limit)
                    .Select(p =>
                        ControlEntry(p, bounds, assigned.GetValueOrDefault(p), capture, fieldReads)
                    )
                    .ToArray()
                : null,
            fieldReadsSkipped = fieldReads[0] < 0 ? -fieldReads[0] : (int?)null,
            text = lines is null
                ? null
                : new
                {
                    lines = lines.Select(l => new
                    {
                        l.Text,
                        l.X,
                        l.Y,
                    }),
                    otherText = other.Select(l => l.Text),
                },
        };
    }

    static object ControlEntry(
        Placed p,
        int[] bounds,
        List<UiRead.ObservedLine>? lines,
        (int Width, int Height, byte[] Bgra)? capture,
        int[] fieldReads
    )
    {
        var c = p.Control;
        var tick =
            p.Kind == "checkbox" && capture is not null && !p.Hidden
                ? TickFraction(capture.Value, p.Rect, bounds)
                : (double?)null;
        (string Text, int Zoom)? field = null;
        if (
            capture is not null
            && !p.Hidden
            && p.Kind is "textInput" or "dropdown"
            && fieldReads[0]-- > 0
        )
            field = FieldValue(capture.Value, p);
        return new
        {
            name = c.Name,
            kind = p.Kind,
            type = c.Type,
            label = c.Label,
            tooltip = c.Tooltip,
            rect = p.Rect,
            defaultHidden = p.Hidden ? true : (bool?)null,
            insideBounds = Inside(p.Rect[0] + p.Rect[2] / 2, p.Rect[1] + p.Rect[3] / 2, bounds),
            valueKind = c.ValueKind,
            script = c.Scripts is { Length: > 0 } s
                ? string.Join("; ", s.Select(x => x.Script))
                : null,
            items = c.Items?.Select(i => i.Label).ToArray(),
            menu = c.Menu?.Select(e => e.Path).ToArray(),
            text = field?.Text ?? (lines is null ? null : Value(p, lines)),
            textSource = field is { Zoom: > 0 } f ? $"control@{f.Zoom}x"
            : field is not null ? "control"
            : lines is null ? null
            : "dialogTiles",
            observedChecked = tick is null ? null : Checked(tick.Value),
        };
    }

    // ---------------- act tool ----------------

    internal sealed record Native(
        IReadOnlyDictionary<string, Command> Commands,
        Func<string, object> Dispatch,
        Action RequireNoCursor
    );

    internal static void Preflight(JsonElement args)
    {
        var action = args.GetProperty("action").GetString()!;
        if (!Actions.Contains(action, StringComparer.Ordinal))
            throw new ArgumentException(
                "action must be one of " + string.Join(", ", Actions) + "."
            );
        if (
            action is "press" or "click" or "setText" or "toggle" or "select"
            && !args.TryGetProperty("control", out _)
        )
            throw new ArgumentException(
                $"action {action} requires control (exact name or unique label)."
            );
        if (action == "select" && !args.TryGetProperty("item", out _))
            throw new ArgumentException("select requires item (label, index or menu path).");
        if (action == "setText")
        {
            if (!args.TryGetProperty("text", out var text))
                throw new ArgumentException("setText requires text.");
            var s = text.GetString()!;
            if (s.Length > MaxText || s.Any(char.IsControl))
                throw new ArgumentException(
                    $"text must be at most {MaxText} characters without control characters."
                );
        }
        if (action is "setText" or "toggle")
            EditorFiles.Confirm(args, "confirmDestructive");
    }

    static bool Confirmed(JsonElement args) =>
        args.TryGetProperty("confirmDestructive", out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>Executes one dialog action; every step is verified or reported unverified, never retried.</summary>
    internal static object Act(
        Game game,
        string hash,
        Dialogs catalog,
        SignatureSet[] sets,
        Native native,
        JsonElement args
    )
    {
        var gadget = args.GetProperty("gadget").GetString()!.Trim();
        var action = args.GetProperty("action").GetString()!;
        var timeout = args.TryGetProperty("timeout", out var t) ? t.GetInt32() : DefaultTimeout;
        var before = Observe(game, hash, sets);
        return action switch
        {
            "open" => Open(game, hash, sets, native, before, gadget, timeout),
            "close" => Close(game, hash, sets, native, catalog, before, gadget, timeout),
            _ => Control(game, hash, catalog, sets, native, before, gadget, action, args, timeout),
        };
    }

    static Signature Calibrated(Observation o, string gadget, string action)
    {
        if (o.Set is null)
            throw new WorkflowFailure(
                "DIALOG_UNCALIBRATED",
                "preflight",
                $"No dialog signatures for this UI/build ({string.Join(" ", o.Warnings)}); {action} cannot be verified.",
                false,
                false,
                $"Use editor_gadget{(action == "open" ? "Real" : "Unreal")} with name={gadget} and a screenshot; no input sent."
            );
        return o.Set.Dialogs.GetValueOrDefault(gadget)
            ?? throw new WorkflowFailure(
                "DIALOG_UNCALIBRATED",
                "preflight",
                $"Gadget '{gadget}' has no calibrated signature (calibrated: {string.Join(", ", o.Set.Dialogs.Keys.Order(StringComparer.Ordinal))}).",
                false,
                false,
                $"Use editor_gadget{(action == "open" ? "Real" : "Unreal")} name={gadget} and verify by screenshot; no input sent."
            );
    }

    static Match Poll(
        Game game,
        string hash,
        SignatureSet[] sets,
        string gadget,
        Func<string, bool> done,
        int timeout
    )
    {
        var deadline = Environment.TickCount64 + timeout;
        while (true)
        {
            Thread.Sleep(PollMs);
            var m = Observe(game, hash, sets).Find(gadget);
            if (m is null || done(m.State) || Environment.TickCount64 >= deadline)
                return m!;
        }
    }

    static object Open(
        Game game,
        string hash,
        SignatureSet[] sets,
        Native native,
        Observation before,
        string gadget,
        int timeout
    )
    {
        var signature = Calibrated(before, gadget, "open");
        var was = Score(before.Frame, signature, before.Set!);
        if (was.State == "open")
            return new
            {
                gadget,
                action = "open",
                skipped = true,
                state = was.State,
                score = was.Score,
                nativeDispatched = false,
            };
        native.RequireNoCursor();
        var command = Catalog.Require(native.Commands, "gadgetReal");
        var expression = Catalog.Build(
            command,
            Json(new Dictionary<string, object?> { [command.Parameters[0].Name] = gadget })
        );
        var ack = Dispatch(native, expression, []);
        var after = Poll(game, hash, sets, gadget, s => s == "open", timeout);
        if (after?.State != "open")
            throw new WorkflowFailure(
                "DIALOG_NOT_OBSERVED",
                "verify",
                $"gadgetReal(\"{gadget}\") acknowledged but the signature scored {after?.Score} ({after?.State}).",
                true,
                true,
                "Take a screenshot (a modal, menu or moved window can hide it); do not repeat blindly."
            );
        return new
        {
            gadget,
            action = "open",
            verified = true,
            state = after.State,
            score = after.Score,
            dispatched = new[] { expression },
            acknowledgement = ack,
        };
    }

    internal static string[] CloseNames(string gadget, Target? target)
    {
        var names = new List<string> { target?.Close ?? gadget };
        if (
            Catalog.AlternativeCloseNames.TryGetValue(gadget, out var alt)
            && target?.Window.Ui == "alternative"
        )
            names.Insert(0, alt);
        if (InnerClose.TryGetValue(gadget, out var inner))
            names.Add(inner);
        if (target is not null)
            names.Add(target.TargetName);
        return [.. names.Distinct(StringComparer.Ordinal)];
    }

    static object Close(
        Game game,
        string hash,
        SignatureSet[] sets,
        Native native,
        Dialogs catalog,
        Observation before,
        string gadget,
        int timeout
    )
    {
        var signature = Calibrated(before, gadget, "close");
        var was = Score(before.Frame, signature, before.Set!);
        if (was.State == "closed")
            return new
            {
                gadget,
                action = "close",
                skipped = true,
                state = was.State,
                score = was.Score,
                nativeDispatched = false,
            };
        var ui = before.UiKind ?? before.Set!.UiKind;
        var target = Resolve(catalog.Windows, ui, gadget);
        var command = Catalog.Require(native.Commands, "gadgetUnreal");
        var attempts = new List<object>();
        var dispatched = new List<string>();
        Match? after = null;
        foreach (var name in CloseNames(gadget, target))
        {
            var expression = Catalog.Build(
                command,
                Json(new Dictionary<string, object?> { [command.Parameters[0].Name] = name })
            );
            Dispatch(native, expression, dispatched);
            dispatched.Add(expression);
            after = Poll(game, hash, sets, gadget, s => s == "closed", timeout);
            attempts.Add(
                new
                {
                    name,
                    state = after?.State,
                    score = after?.Score,
                }
            );
            if (after?.State == "closed")
                break;
        }
        if (after?.State != "closed")
            throw new WorkflowFailure(
                "DIALOG_STILL_OPEN",
                "verify",
                $"{gadget} still scores {after?.Score} after {string.Join(", ", dispatched)}.",
                true,
                true,
                "Screenshot, then close via the dialog's own Close button (editor_dialog_act press) or ESC. Never reopen with gadgetReal to 'restore'."
            );
        return new
        {
            gadget,
            action = "close",
            verified = true,
            attempts,
            dispatched,
        };
    }

    static object Dispatch(Native native, string expression, List<string> done)
    {
        try
        {
            return native.Dispatch(expression);
        }
        catch (BridgeRefusal e)
        {
            throw new WorkflowFailure(
                "BRIDGE_REFUSED",
                "dispatch",
                $"{expression} refused ({e.Message}). Already dispatched: {(done.Count == 0 ? "none" : string.Join("; ", done))}.",
                done.Count > 0,
                false,
                "Inspect with editor_dialog_state before any further action.",
                inner: e
            );
        }
        catch (TimeoutException e)
        {
            throw new WorkflowFailure(
                "DISPATCH_OUTCOME_UNKNOWN",
                "dispatch",
                $"{expression} timed out; outcome unknown. Already dispatched: {(done.Count == 0 ? "none" : string.Join("; ", done))}.",
                true,
                true,
                "Inspect with editor_dialog_state and a screenshot; do not repeat.",
                inner: e
            );
        }
    }

    static JsonElement Json(Dictionary<string, object?> values) =>
        JsonSerializer.SerializeToElement(values);

    sealed record Resolved(Target Target, DialogControl Control, string Ui);

    static Resolved ResolveControl(Dialogs catalog, Observation o, string gadget, string control)
    {
        if (catalog.Status != "fresh")
            throw new WorkflowFailure(
                "METADATA_" + catalog.Status.ToUpperInvariant(),
                "preflight",
                $"Dialog catalog {catalog.Status}; control actions need fresh generated/dialogs.json.",
                false,
                false,
                "Run editor_generate_catalog, then retry; no input sent."
            );
        var uis = o.UiKind is { } kind ? [kind] : new[] { "normal", "alternative" };
        var found = new List<Resolved>();
        foreach (var ui in uis)
        {
            var target = Resolve(catalog.Windows, ui, gadget);
            if (target is null)
                continue;
            var exact = target
                .Controls.Where(c =>
                    string.Equals(c.Name, control, StringComparison.OrdinalIgnoreCase)
                )
                .ToArray();
            var matches =
                exact.Length > 0
                    ? exact
                    : target
                        .Controls.Where(c =>
                            Norm(c.Label) is { Length: > 0 } l && l == Norm(control)
                        )
                        .ToArray();
            if (matches.Length > 1)
                throw new ArgumentException(
                    $"Control '{control}' is ambiguous in {target.Window.Window}: {string.Join(", ", matches.Select(m => m.Name))}. Use the exact name."
                );
            if (matches.Length == 1)
                found.Add(new(target, matches[0], ui));
        }
        if (found.Count == 0)
        {
            var names = uis.Select(u => Resolve(catalog.Windows, u, gadget))
                .Where(x => x is not null)
                .SelectMany(x => x!.Controls.Select(c => c.Name))
                .Distinct();
            throw new ArgumentException(
                names.Any()
                    ? $"No control '{control}' in {gadget}. Close names: {string.Join(", ", GameDataCatalog.Suggest(control, names))}."
                    : $"No decoded XML window for gadget '{gadget}'{(o.UiKind is null ? "" : $" in the {o.UiKind} UI")}. List windows with editor_dialogs."
            );
        }
        // UI undetected: both UIs must agree on the script for script actions; pixel actions require a detected UI anyway.
        if (found.Count > 1 && Scripts(found[0].Control) != Scripts(found[1].Control))
            throw new WorkflowFailure(
                "UI_UNKNOWN",
                "preflight",
                $"UI kind not detected and '{control}' differs between UIs.",
                false,
                false,
                "Close covering dialogs so the minimap gate is visible (editor_ui_state), then retry; no input sent."
            );
        return found[0];
    }

    static string Scripts(DialogControl c) =>
        string.Join("|", (c.Scripts ?? []).Select(s => s.Script))
        + "|"
        + string.Join("|", (c.Items ?? []).Select(i => i.Script.Script))
        + "|"
        + string.Join("|", (c.Menu ?? []).Select(m => m.Script?.Script));

    static object Control(
        Game game,
        string hash,
        Dialogs catalog,
        SignatureSet[] sets,
        Native native,
        Observation o,
        string gadget,
        string action,
        JsonElement args,
        int timeout
    )
    {
        var control = args.GetProperty("control").GetString()!.Trim();
        var resolved = ResolveControl(catalog, o, gadget, control);
        var kind = ControlKind(resolved.Control);
        if (action == "press" && kind == "checkbox")
            action = "toggle";
        if (action is "toggle" or "setText")
            EditorFiles.Confirm(args, "confirmDestructive");
        return action switch
        {
            "press" => resolved.Control.Scripts is { Length: > 0 } scripts
                ? RunScripts(game, hash, sets, native, o, resolved, "press", scripts, args, timeout)
                : throw new WorkflowFailure(
                    "NO_SCRIPT",
                    "preflight",
                    $"{resolved.Control.Name} ({resolved.Control.Type}) has no shipped script; its effect is engine dialog code.",
                    false,
                    false,
                    "Use action=click (pixel click on an open nominal dialog, OCR-label verified); no input sent."
                ),
            "select" => Select(game, hash, sets, native, o, resolved, args, timeout),
            "toggle" => MarkBestEffort(
                Toggle(game, hash, sets, native, o, resolved, args, timeout),
                o
            ),
            "click" => MarkBestEffort(Click(game, hash, sets, native, o, resolved, args), o),
            "setText" => MarkBestEffort(
                SetText(
                    game,
                    hash,
                    sets,
                    native,
                    o,
                    resolved,
                    args.GetProperty("text").GetString()!
                ),
                o
            ),
            _ => throw new ArgumentException("Unknown action."),
        };
    }

    /// <summary>Adds <c>layoutReviewed:false</c> and the best-effort note to a pixel-action result on a derived client size.</summary>
    static object MarkBestEffort(object result, Observation o)
    {
        if (!o.BestEffort)
            return result;
        var node = JsonSerializer.SerializeToNode(result)!.AsObject();
        node["layoutReviewed"] = false;
        node["bestEffort"] = ScreenProbe.BestEffortNote;
        return node;
    }

    static object RunScripts(
        Game game,
        string hash,
        SignatureSet[] sets,
        Native native,
        Observation o,
        Resolved r,
        string action,
        IReadOnlyList<DialogScript> scripts,
        JsonElement args,
        int timeout,
        object? extra = null
    )
    {
        var calls = scripts.SelectMany(s => s.Calls).ToArray();
        if (
            calls.Length == 0
            || scripts.Any(s => s.Unparsed == true)
            || calls.Any(c => c.Tool is null || c.Arguments is null || c.Problem is not null)
        )
            throw new WorkflowFailure(
                "SCRIPT_UNSUPPORTED",
                "preflight",
                $"Script of {r.Control.Name} has calls without validated arguments: {string.Join("; ", scripts.Select(s => s.Script))}.",
                false,
                false,
                "Use action=click on an open nominal dialog, or call the native tools yourself after checking editor_dialogs; no input sent."
            );
        var close = IsCloseControl(r.Control);
        if (
            !close
            && !Confirmed(args)
            && calls.Any(c => !ViewCommands.Contains(c.Command) || c.RequiresConfirmation == true)
        )
            throw new ArgumentException(
                $"Script runs non-view commands ({string.Join(", ", calls.Select(c => c.Command).Distinct())}); confirmDestructive=true required."
            );
        var expressions = calls
            .Select(c =>
            {
                var values = new Dictionary<string, object?>(c.Arguments!);
                if (Confirmed(args))
                    values["confirmDestructive"] = true;
                return Catalog.Build(
                    native.Commands.GetValueOrDefault(c.Command)
                        ?? throw new WorkflowFailure(
                            "SCRIPT_UNSUPPORTED",
                            "preflight",
                            $"Native command {c.Command} unknown to this host catalog.",
                            false,
                            false,
                            "Regenerate metadata; no input sent."
                        ),
                    Json(values)
                );
            })
            .ToArray();
        var done = new List<string>();
        var acks = new List<object>();
        foreach (var expression in expressions)
        {
            acks.Add(Dispatch(native, expression, done));
            done.Add(expression);
        }
        var signature = o.Set?.Dialogs.GetValueOrDefault(r.Target.Gadget);
        Match? after = null;
        if (signature is not null)
        {
            // Closing scripts are verified by polling to closed; others report the state after one settle.
            if (!close)
                Thread.Sleep(SettleMs);
            after = close
                ? Poll(game, hash, sets, r.Target.Gadget, s => s == "closed", timeout)
                : Observe(game, hash, sets).Find(r.Target.Gadget);
        }
        return new
        {
            gadget = r.Target.Gadget,
            action,
            control = r.Control.Name,
            label = r.Control.Label,
            ui = r.Ui,
            extra,
            dispatched = done,
            acknowledgements = acks,
            dialogAfter = after is null ? null : new { after.State, after.Score },
            verified = close ? after?.State == "closed" : (bool?)null,
            note = "Only the shipped script's native calls ran (acknowledgements are not proof); engine handlers of the control did not run.",
        };
    }

    static bool IsCloseControl(DialogControl c) =>
        Norm(c.Label) is "close" or "cancel" or "x"
        || c.Name.EndsWith("CloseButton", StringComparison.OrdinalIgnoreCase)
        || c.Name.EndsWith("CancelButton", StringComparison.OrdinalIgnoreCase);

    static object Select(
        Game game,
        string hash,
        SignatureSet[] sets,
        Native native,
        Observation o,
        Resolved r,
        JsonElement args,
        int timeout
    )
    {
        var item = args.GetProperty("item").GetString()!.Trim();
        if (r.Control.Items is { Length: > 0 } items)
        {
            var index = Array.FindIndex(
                items,
                i => Norm(i.Label) == Norm(item) && Norm(item).Length > 0
            );
            if (
                index < 0
                && int.TryParse(item, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
                && n < items.Length
            )
                index = n;
            if (index < 0)
                throw new ArgumentException(
                    $"No item '{item}' in {r.Control.Name}: {string.Join(", ", items.Select((i, k) => $"{k}:{i.Label}"))}."
                );
            return RunScripts(
                game,
                hash,
                sets,
                native,
                o,
                r,
                "select",
                [items[index].Script],
                args,
                timeout,
                new { item = items[index].Label, index }
            );
        }
        if (r.Control.Menu is { Length: > 0 } menu)
        {
            var entries = menu.Where(m =>
                    string.Equals(m.Path, item, StringComparison.OrdinalIgnoreCase)
                )
                .ToArray();
            if (entries.Length == 0)
                entries = [.. menu.Where(m => Norm(m.Path.Split('>')[^1]) == Norm(item))];
            if (entries.Length != 1)
                throw new ArgumentException(
                    $"Menu item '{item}' {(entries.Length == 0 ? "not found" : "ambiguous")} in {r.Control.Name}. Paths: {string.Join(", ", menu.Select(m => m.Path).Take(40))}."
                );
            if (entries[0].Script is null)
                throw new WorkflowFailure(
                    "NO_SCRIPT",
                    "preflight",
                    $"Menu entry {entries[0].Path} has no script.",
                    false,
                    false,
                    "No input sent."
                );
            return RunScripts(
                game,
                hash,
                sets,
                native,
                o,
                r,
                "select",
                [entries[0].Script!],
                args,
                timeout,
                new { item = entries[0].Path }
            );
        }
        throw new WorkflowFailure(
            ControlKind(r.Control) == "dropdown" ? "DROPDOWN_UNSUPPORTED" : "NO_ITEMS",
            "preflight",
            $"{r.Control.Name} ({r.Control.Type}) has no scripted items; dropdown/list values are engine-managed.",
            false,
            false,
            "Use a workflow helper (editor_player_settings etc.) or set the value manually; no input sent."
        );
    }

    // ---------------- pixel actions ----------------

    sealed record Live(
        Placed Control,
        int[] Bounds,
        (int Width, int Height, byte[] Bgra) Capture,
        List<UiRead.ObservedLine> Lines,
        Geometry Geometry,
        bool LabelVerified
    );

    static readonly string[] ClickKinds = ["button", "checkbox", "textInput"];

    /// <summary>All pixel-action preconditions: calibrated exact size, open nominal dialog, visible in-bounds unoccluded control, OCR fit and label.</summary>
    static Live Locate(Game game, Observation o, Resolved r)
    {
        if (o.UiKind is null || o.UiKind != r.Ui)
            throw new WorkflowFailure(
                "UI_UNKNOWN",
                "preflight",
                "Pixel actions need a detected UI kind matching the calibrated signatures.",
                false,
                false,
                "Inspect with editor_dialog_state; no input sent."
            );
        if (o.Set is null || !(o.Exact || o.BestEffort))
            throw new WorkflowFailure(
                "DIALOG_UNCALIBRATED",
                "preflight",
                $"Pixel actions need signatures calibrated at this client size or a derivable one ({o.Width}x{o.Height}; {UiLayouts.SupportedText}). {string.Join(" ", o.Warnings)}",
                false,
                false,
                "Use script actions (press/select) or a reviewed size; no input sent."
            );
        var signature =
            o.Set.Dialogs.GetValueOrDefault(r.Target.Gadget)
            ?? throw new WorkflowFailure(
                "DIALOG_UNCALIBRATED",
                "preflight",
                $"{r.Target.Gadget} has no calibrated signature.",
                false,
                false,
                "Use script actions or manual UI; no input sent."
            );
        var match = Score(o.Frame, signature, o.Set);
        if (match.State != "open")
            throw new WorkflowFailure(
                "DIALOG_NOT_OPEN",
                "preflight",
                $"{r.Target.Gadget} is {match.State} (score {match.Score}); a moved/covered window is not clicked.",
                false,
                false,
                "Open it with editor_dialog_act action=open (default position) and retry; no input sent."
            );
        if (Mode(r.Target, o, signature) != "nominal")
            throw new WorkflowFailure(
                "GEOMETRY_UNVERIFIED",
                "preflight",
                $"{r.Target.Window.Window} is anchored/stretched; size1024 control geometry does not map nominally.",
                false,
                false,
                "Use script actions or a screenshot-guided editor_click; no input sent."
            );
        var kind = ControlKind(r.Control);
        if (!ClickKinds.Contains(kind))
            throw new WorkflowFailure(
                kind == "dropdown" ? "DROPDOWN_UNSUPPORTED" : "NOT_CLICKABLE",
                "preflight",
                $"{r.Control.Name} is {r.Control.Type} ({kind}); only buttons, checkboxes and text inputs are clicked.",
                false,
                false,
                "No input sent."
            );
        var bounds = o.Bounds(signature);
        var capture = Ui.CapturePixels(game, Ui.FullResolutionWidth);
        var tiles = MaxTiles;
        var lines = Read(capture, bounds, ref tiles);
        var nominal = Place(r.Target, o, 0, 0);
        var (dx, dy, anchors) = Fit(nominal, lines, o.Scale);
        var placed = Place(r.Target, o, dx, dy);
        var control =
            placed.FirstOrDefault(p => p.Control == r.Control)
            ?? throw new WorkflowFailure(
                "GEOMETRY_UNVERIFIED",
                "preflight",
                $"{r.Control.Name} has no size1024 rect.",
                false,
                false,
                "No input sent."
            );
        if (control.Hidden)
            throw new WorkflowFailure(
                "CONTROL_HIDDEN",
                "preflight",
                $"{r.Control.Name} (or a parent) is hidden by default; its visibility is unknown.",
                false,
                false,
                "Screenshot first; no input sent."
            );
        var (cx, cy) = Point(control);
        if (!Inside(cx, cy, bounds))
            throw new WorkflowFailure(
                "GEOMETRY_UNVERIFIED",
                "preflight",
                $"{r.Control.Name} center ({cx},{cy}) lies outside the dialog bounds {string.Join(",", bounds)}.",
                false,
                false,
                "No input sent."
            );
        foreach (
            var other in o.Matches.Where(m =>
                m.Signature.Gadget != r.Target.Gadget && m.State != "closed"
            )
        )
            if (Inside(cx, cy, o.Bounds(other.Signature)))
                throw new WorkflowFailure(
                    "OCCLUDED",
                    "preflight",
                    $"Click point lies inside {other.Signature.Gadget} ({other.State}); stacking order unknown.",
                    false,
                    false,
                    $"Close {other.Signature.Gadget} first; no input sent."
                );
        var (assigned, _) = Assign(placed, lines, o.Scale);
        var label = Norm(r.Control.Label);
        var own = assigned.GetValueOrDefault(control) ?? [];
        var labelVerified =
            label.Length >= 2
            && own.Any(l =>
                Norm(l.Text).Contains(label, StringComparison.Ordinal)
                || (o.BestEffort && NearLabel(Norm(l.Text), label))
            );
        if (label.Length >= 2 && !labelVerified)
            throw new WorkflowFailure(
                "LABEL_NOT_VERIFIED",
                "preflight",
                $"OCR did not find label '{r.Control.Label}' inside {r.Control.Name} at [{string.Join(",", control.Rect)}] (read: {string.Join(" | ", own.Select(l => l.Text))}).",
                false,
                false,
                "Screenshot to check the dialog; no input sent."
            );
        if (label.Length < 2 && anchors < 2)
            throw new WorkflowFailure(
                "GEOMETRY_UNVERIFIED",
                "preflight",
                $"{r.Control.Name} has no label and the OCR fit found {anchors} anchors.",
                false,
                false,
                "No input sent."
            );
        return new(
            control,
            bounds,
            capture,
            lines,
            new("nominal", null, dx, dy, anchors, anchors >= 2),
            labelVerified
        );
    }

    // Text inputs show "Label:" left of the edit area: click right of center.
    static (int X, int Y) Point(Placed p) =>
        (
            p.Kind == "textInput" && Norm(p.Control.Label).Length > 0
                ? p.Rect[0] + p.Rect[2] * 3 / 4
                : p.Rect[0] + p.Rect[2] / 2,
            p.Rect[1] + p.Rect[3] / 2
        );

    static double Changed(
        (int Width, int Height, byte[] Bgra) a,
        (int Width, int Height, byte[] Bgra) b,
        int[] rect
    )
    {
        if (a.Width != b.Width || a.Height != b.Height)
            return 1;
        int x0 = Math.Max(0, rect[0]),
            y0 = Math.Max(0, rect[1]),
            x1 = Math.Min(a.Width, rect[0] + rect[2]),
            y1 = Math.Min(a.Height, rect[1] + rect[3]);
        long changed = 0,
            total = 0;
        for (var y = y0; y < y1; y += 2)
        for (var x = x0; x < x1; x += 2, total++)
        {
            var o = (y * a.Width + x) * 4;
            if (
                Math.Abs(a.Bgra[o] - b.Bgra[o])
                    + Math.Abs(a.Bgra[o + 1] - b.Bgra[o + 1])
                    + Math.Abs(a.Bgra[o + 2] - b.Bgra[o + 2])
                > 60
            )
                changed++;
        }
        return total == 0 ? 0 : Math.Round((double)changed / total, 4);
    }

    static object Click(
        Game game,
        string hash,
        SignatureSet[] sets,
        Native native,
        Observation o,
        Resolved r,
        JsonElement args
    )
    {
        if (!IsCloseControl(r.Control) && !Confirmed(args))
            throw new ArgumentException(
                $"Clicking {r.Control.Name} ('{r.Control.Label}') can apply/generate/discard; confirmDestructive=true required (only Close/Cancel buttons are exempt)."
            );
        var live = Locate(game, o, r);
        var (x, y) = Point(live.Control);
        native.RequireNoCursor();
        Ui.SettledClick(game, x, y, "left");
        Thread.Sleep(SettleMs);
        var after = Ui.CapturePixels(game, Ui.FullResolutionWidth);
        var dialog = Observe(game, hash, sets).Find(r.Target.Gadget);
        return new
        {
            gadget = r.Target.Gadget,
            action = "click",
            control = r.Control.Name,
            label = r.Control.Label,
            point = new[] { x, y },
            labelVerified = live.LabelVerified,
            fit = new
            {
                offset = new[] { live.Geometry.Dx, live.Geometry.Dy },
                anchors = live.Geometry.Anchors,
            },
            changedFraction = Changed(live.Capture, after, live.Bounds),
            dialogAfter = dialog is null ? null : new { dialog.State, dialog.Score },
            verified = false,
            note = "Pixel click sent; changedFraction is evidence, not proof of the handler's effect. Verify with editor_dialog_state read=true or a checkpoint.",
        };
    }

    static object Toggle(
        Game game,
        string hash,
        SignatureSet[] sets,
        Native native,
        Observation o,
        Resolved r,
        JsonElement args,
        int timeout
    )
    {
        if (ControlKind(r.Control) != "checkbox")
            throw new ArgumentException($"{r.Control.Name} is {r.Control.Type}, not a checkbox.");
        if (r.Control.Scripts is { Length: > 0 } scripts)
            return RunScripts(game, hash, sets, native, o, r, "toggle", scripts, args, timeout);
        var live = Locate(game, o, r);
        var before = TickFraction(live.Capture, live.Control.Rect, live.Bounds);
        var (x, y) = Point(live.Control);
        native.RequireNoCursor();
        Ui.SettledClick(game, x, y, "left");
        Thread.Sleep(SettleMs);
        var capture = Ui.CapturePixels(game, Ui.FullResolutionWidth);
        var after = TickFraction(capture, live.Control.Rect, live.Bounds);
        bool? was = Checked(before),
            now = Checked(after);
        return new
        {
            gadget = r.Target.Gadget,
            action = "toggle",
            control = r.Control.Name,
            label = r.Control.Label,
            point = new[] { x, y },
            observedBefore = was,
            observedAfter = now,
            verified = was is not null && now is not null && was != now,
            changedFraction = Changed(live.Capture, capture, live.Bounds),
            note = "observedChecked is a green-tick pixel heuristic; the checkpoint is authoritative. Some dialogs apply immediately (e.g. Victory Conditions).",
        };
    }

    static object SetText(
        Game game,
        string hash,
        SignatureSet[] sets,
        Native native,
        Observation o,
        Resolved r,
        string text
    )
    {
        if (ControlKind(r.Control) != "textInput")
            throw new ArgumentException(
                $"{r.Control.Name} is {r.Control.Type}{(r.Control.Dropdown == true ? " (dropdown)" : "")}, not an editable text input."
            );
        var live = Locate(game, o, r);
        var before = FieldValue(live.Capture, live.Control).Text;
        var (x, y) = Point(live.Control);
        native.RequireNoCursor();
        Ui.SettledClick(game, x, y, "left");
        Thread.Sleep(SettleMs);
        var still = Observe(game, hash, sets).Find(r.Target.Gadget);
        if (still?.State != "open")
            throw new WorkflowFailure(
                "DIALOG_CHANGED",
                "focus",
                $"{r.Target.Gadget} no longer scores open after the focus click ({still?.Score}); no keys sent.",
                true,
                true,
                "Screenshot and inspect; the click landed but typing was skipped."
            );
        var backspaces = Math.Clamp(before.Length + 8, MinBackspaces, MaxBackspaces);
        Ui.Press(game, "END", []);
        for (var i = 0; i < backspaces; i++)
            Ui.Press(game, "BACKSPACE", []);
        if (text.Length > 0)
            Ui.Text(game, text);
        Thread.Sleep(SettleMs);
        // Never TAB/ENTER: in some dialogs they trigger default buttons (e.g. New Scenario Generate).
        // Exact match at any field zoom verifies; reads differ mostly by dropped thin glyphs ('-', '.'). The focused field's
        // blinking caret can hide short text ("-1|" read as nothing), so re-read later frames (reads only; no keys re-sent).
        var after = "";
        var frames = 0;
        for (; frames < VerifyFrames; frames++)
        {
            if (frames > 0)
                Thread.Sleep(VerifyFrameMs);
            var capture = Ui.CapturePixels(game, Ui.FullResolutionWidth);
            var reads = FieldZooms
                .Select(zoom => Value(live.Control, ReadControl(capture, live.Control, zoom)))
                .ToArray();
            after =
                reads.FirstOrDefault(v => SameText(text, v))
                ?? reads.FirstOrDefault(v => v.Length > 0)
                ?? after;
            if (Norm(text).Length > 0 && SameText(text, after))
            {
                frames++;
                break;
            } // Clearing must read empty on every frame.
        }
        var verified = SameText(text, after);
        if (!verified)
            throw new WorkflowFailure(
                "TEXT_NOT_VERIFIED",
                "verify",
                $"Typed '{text}' into {r.Control.Name} but OCR reads '{after}' (before '{before}').",
                true,
                true,
                "Screenshot the field; OCR may misread (9/6, O/0). Do not retype blindly; close with Cancel/Close if unsure."
            );
        return new
        {
            gadget = r.Target.Gadget,
            action = "setText",
            control = r.Control.Name,
            label = r.Control.Label,
            point = new[] { x, y },
            before,
            after,
            verified,
            backspaces,
            framesRead = frames,
            note = "Text typed into the field only; nothing applied. Dialog buttons (OK/Generate/Apply) decide whether it is used; no TAB/ENTER sent.",
        };
    }

    static int[] Pad(int[] r, int p) => [r[0] - p, r[1] - p, r[2] + 2 * p, r[3] + 2 * p];

    static int[] Clip(int[] r, (int Width, int Height, byte[] Bgra) capture)
    {
        int x0 = Math.Max(0, r[0]),
            y0 = Math.Max(0, r[1]);
        int x1 = Math.Min(capture.Width, r[0] + r[2]),
            y1 = Math.Min(capture.Height, r[1] + r[3]);
        return [x0, y0, Math.Clamp(x1 - x0, 1, 850), Math.Clamp(y1 - y0, 1, 500)];
    }

    // ---------------- self-test ----------------

    /// <summary>Self-test sample: a script name as typed (project script om10\om10_diffbonus.xs).</summary>
    const string SampleDiffBonus = "om10\\om10_diffbonus.xs";

    /// <summary>Self-test sample: another script name as typed.</summary>
    const string SampleModifiers = "om10\\om10_wrket_modifiers.xs";

    /// <summary>Self-test sample: how the alternative-UI list clipped and OCR-read <see cref="SampleDiffBonus"/> (live 2026-10-14).</summary>
    const string SampleDiffBonusClippedRead = "om 10\\om 10 diff";

    /// <summary>Self-test sample: a read shorter than <see cref="MinPrefixChars"/> normalized characters.</summary>
    const string SampleTooShortRead = "om";

    /// <summary>Self-test sample: a probe script name as typed (live alternative UI 1920x1080, 2026-10-25).</summary>
    const string SampleProbe = "aommcp_probe.xs";

    /// <summary>Self-test sample: how the alternative-UI list clipped <see cref="SampleProbe"/>, last glyph cut (live 2026-10-25).</summary>
    const string SampleProbeClippedRead = "aommcp_prok";

    /// <summary>Self-test sample: the popup field's visible part of <see cref="SampleModifiers"/>, OCR-misread (live 2026-10-14).</summary>
    const string SampleModifiersFieldRead = "wrket _modifiers.x$";

    internal static void SelfTest()
    {
        // Live 1920×1080 OCR misread; short labels and unrelated text stay refused.
        if (
            !NearLabel(Norm("Inelude Seript"), Norm("Include Script"))
            || NearLabel(Norm("Remove Script"), Norm("Include Script"))
            || NearLabel(Norm("Oh"), Norm("OK"))
        )
            throw new InvalidOperationException("Dialog fuzzy label self-test failed.");
        var sets = LoadAll(Path.Combine(AppContext.BaseDirectory, "uilayouts"));
        var normal =
            sets.SingleOrDefault(s => s.UiKind == "normal" && s.Width == 2560)
            ?? throw new InvalidDataException(
                "Normal-UI dialog signature file missing from uilayouts."
            );
        if (
            normal.Dialogs.Count < 30
            || !normal.Dialogs.TryGetValue("NewScenarioGadget", out var signature)
            || normal.BuildHash.Length != 64
        )
            throw new InvalidDataException("Dialog signature file incomplete.");
        // Synthetic frame: paint one signature's points; it must score open and no other signature may score open.
        var rgb = new byte[1280 * 720 * 3];
        foreach (var p in signature.Points)
        {
            var o = (p[1] * 1280 + p[0]) * 3;
            rgb[o] = (byte)p[2];
            rgb[o + 1] = (byte)p[3];
            rgb[o + 2] = (byte)p[4];
        }
        var frame = new ScreenProbe.Frame(rgb, 1280, 720);
        if (Score(frame, signature, normal).State != "open")
            throw new InvalidDataException("Dialog signature scoring failed on its own points.");
        foreach (var rival in normal.Dialogs.Values.Where(d => d != signature))
            if (Score(frame, rival, normal).State == "open")
                throw new InvalidDataException(
                    $"Signature {rival.Gadget} matches NewScenarioGadget points."
                );
        // Alternative-UI ScriptManager signature: loads, scores open on its own points, and is not NewScenarioGadget.
        var alternative =
            sets.SingleOrDefault(s => s.UiKind == "alternative" && s.Width == 2560)
            ?? throw new InvalidDataException(
                "Alternative-UI dialog signature file missing from uilayouts."
            );
        var scripts = alternative.Dialogs["ScriptManager"];
        var altRgb = new byte[1280 * 720 * 3];
        foreach (var p in scripts.Points)
        {
            var o = (p[1] * 1280 + p[0]) * 3;
            altRgb[o] = (byte)p[2];
            altRgb[o + 1] = (byte)p[3];
            altRgb[o + 2] = (byte)p[4];
        }
        if (Score(new ScreenProbe.Frame(altRgb, 1280, 720), scripts, alternative).State != "open")
            throw new InvalidDataException(
                "Alternative ScriptManager signature failed on its own points."
            );
        // Clipped list rows (alternative UI) match by start; popup text by containment (live OCR reads 2026-10-14).
        if (
            !StartsText(SampleDiffBonus, SampleDiffBonusClippedRead)
            || StartsText(SampleModifiers, SampleDiffBonusClippedRead)
            || StartsText(SampleDiffBonus, SampleTooShortRead)
            || !StartsText(SampleProbe, SampleProbeClippedRead)
            || StartsText(SampleModifiers, SampleProbeClippedRead)
            || !InsideText(SampleModifiers, SampleModifiersFieldRead)
            || InsideText(SampleDiffBonus, SampleModifiersFieldRead)
        )
            throw new InvalidDataException("Clipped-text matching changed.");
        // Geometry: NewScenarioDialog size1024 [332,284,700,552] maps to [830,533,920,502] at 2560x1440 (observed [836,536,906,494]).
        if (ToClient([332, 284, 700, 552], 2560, 1440) is not [830, 533, 920, 502])
            throw new InvalidDataException("size1024 mapping changed.");
        // Fit + assignment on synthetic OCR lines shifted by (+8,-4).
        var window = new DialogWindow(
            "normal",
            "W",
            "w.xml",
            "dialog",
            null,
            [100, 100, 500, 400],
            null,
            null,
            null,
            null,
            [new("G", "W", "window", "opens", "G")],
            [],
            null,
            [
                new(
                    0,
                    "SeedEntry",
                    "textField",
                    "W",
                    "Seed:",
                    null,
                    null,
                    null,
                    [120, 120, 300, 140],
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null
                ),
                new(
                    1,
                    "OkButton",
                    "button",
                    "W",
                    "OK",
                    null,
                    null,
                    null,
                    [120, 300, 200, 330],
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null
                ),
                new(
                    2,
                    "CloseButton",
                    "button",
                    "W",
                    "Close",
                    null,
                    null,
                    null,
                    [300, 300, 380, 330],
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null
                ),
                new(
                    3,
                    "Secret",
                    "button",
                    "W",
                    "Hidden",
                    null,
                    null,
                    null,
                    [300, 200, 380, 230],
                    true,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null
                ),
            ]
        );
        var target =
            Resolve([window], "normal", "G")
            ?? throw new InvalidDataException("Dialog target resolution failed.");
        var observation = new Observation
        {
            Frame = frame,
            Width = 2560,
            Height = 1440,
        };
        var placed = Place(target, observation, 0, 0);
        var seed = placed.Single(p => p.Control.Name == "SeedEntry");
        var ok = placed.Single(p => p.Control.Name == "OkButton");
        var close = placed.Single(p => p.Control.Name == "CloseButton");
        UiRead.ObservedLine Line(string text, Placed p, int fx) =>
            new(text, p.Rect[0] + p.Rect[2] * fx / 8 + 8, p.Rect[1] + p.Rect[3] / 2 - 4);
        var lines = Dedupe([
            Line("Seed:", seed, 1),
            Line("-1", seed, 4),
            Line("OK", ok, 4),
            Line("Close", close, 4),
            Line("Clos", close, 4),
            Line("Stray", ok, 40),
        ]);
        if (lines.Count != 5)
            throw new InvalidDataException("OCR line dedupe failed.");
        var fit = Fit(placed, lines);
        if (fit.Anchors != 3 || fit.Dx != 0 || fit.Dy != 0)
            throw new InvalidDataException($"Dialog OCR fit failed ({fit}).");
        var (assigned, other) = Assign(placed, lines);
        if (
            Value(seed, assigned[seed]) != "-1"
            || other.Count != 1
            || placed.Single(p => p.Control.Name == "Secret").Hidden != true
        )
            throw new InvalidDataException("Dialog OCR assignment failed.");
        if (Value(seed, [new("Seed: 42", 0, 0)]) != "42")
            throw new InvalidDataException("Label prefix stripping failed.");
        if (
            !IsCloseControl(close.Control)
            || IsCloseControl(ok.Control)
            || Point(seed).X <= seed.Rect[0] + seed.Rect[2] / 2
        )
            throw new InvalidDataException("Dialog click policy changed.");
        if (Checked(0.028) != true || Checked(0) != false || Checked(0.005) is not null)
            throw new InvalidDataException("Checkbox heuristic thresholds changed.");
        var caption = ok with { Kind = "text", Control = ok.Control with { Label = "Handicap" } };
        if (
            Value(caption, [new("Handi", 0, 0), new("cap", 40, 0)]) != ""
            || Value(caption, [new("Hand1cap", 0, 0)]) != ""
        )
            throw new InvalidDataException("Caption fragment stripping failed.");
        if (Value(seed, [new("ee", 0, 0)]) != "ee")
            throw new InvalidDataException("Input values must not be treated as label fragments.");
        var dropdown = seed with { Kind = "dropdown" };
        if (
            Value(
                dropdown,
                [new("Blank", seed.Rect[0] + 40, 0), new("A", seed.Rect[0] + seed.Rect[2] - 10, 0)]
            ) != "Blank"
        )
            throw new InvalidDataException("Dropdown arrow glyph not dropped.");
        var tickFrame = (Width: 64, Height: 32, Bgra: new byte[64 * 32 * 4]);
        for (var i = 0; i < tickFrame.Bgra.Length; i += 4) // Map grass left of x=24, gray dialog from x=24.
            (tickFrame.Bgra[i], tickFrame.Bgra[i + 1], tickFrame.Bgra[i + 2]) =
                i / 4 % 64 < 24
                    ? ((byte)40, (byte)150, (byte)110)
                    : ((byte)100, (byte)100, (byte)100);
        if (
            TickFraction(tickFrame, [20, 8, 30, 16], [24, 0, 40, 32]) != 0
            || TickFraction(tickFrame, [20, 8, 30, 16], [0, 0, 64, 32]) <= 0
        )
            throw new InvalidDataException("Tick sampling must stay inside dialog bounds.");
        if (CloseNames("ATriggerGadget", null) is not ["ATriggerGadget", "trEditor"])
            throw new InvalidDataException("Close-name order changed.");
        if (
            !Actions.SequenceEqual([
                "open",
                "close",
                "press",
                "click",
                "setText",
                "toggle",
                "select",
            ])
        )
            throw new InvalidDataException("Dialog action vocabulary changed.");
    }
}
