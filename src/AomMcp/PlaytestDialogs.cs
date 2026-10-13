using System.Globalization;
using System.Text.RegularExpressions;
using static AomMcp.UiRead;

namespace AomMcp;

/// <summary>
/// Read-only OCR reader for the game's modal error dialogs during a playtest ("Trigger Error." with an OK button,
/// live 2026-10-13, normal UI 2560x1440; see research/WORKFLOW-LIVE-OBSERVATIONS.md "Playtest compile gate").
/// The dialog is a game modal, not editor UI; it is located by OCR (title line + dismiss button), never by fixed coordinates.
/// </summary>
internal static partial class PlaytestDialogs
{
    /// <summary>Horizontal OCR tile width: the UiRead per-region maximum (850 px).</summary>
    const int TileWidth = 850;

    /// <summary>Vertical tile height; 850 x 340 stays under the UiRead 300,000-pixel region bound.</summary>
    const int TileHeight = 340;

    /// <summary>Tile step; the 100 px overlap keeps every text line whole in at least one tile.</summary>
    const int TileStep = 240;

    /// <summary>Lines whose centre lies this close to an inner tile edge are taken from the neighbouring tile instead.</summary>
    const int EdgeMargin = 30;

    /// <summary>OCR zoom; the dialog text (about 30 px at 2560x1440) reads cleanly unscaled.</summary>
    const int OcrScale = 1;

    /// <summary>Two lines closer than this vertically and horizontally are the same line read in two tiles.</summary>
    const int SameLineDy = 12,
        SameLineDx = 60;

    /// <summary>Scanned band as a fraction of the client height (the modal is centred; observed y 365..1075 of 1440).</summary>
    const double ScanTop = 0.15,
        ScanBottom = 0.85;

    /// <summary>A re-read button closer than this to the first read is the same button (pixels).</summary>
    const int ButtonTolerance = 6;

    /// <summary>Button labels a dismiss may click, in preference order. Only "OK" was observed live.</summary>
    internal static readonly string[] DismissLabels = ["OK", "Next", "Close"];

    /// <summary>Kind reported for a title starting with "Trigger" (observed "Trigger Error.").</summary>
    internal const string TriggerErrorKind = "TriggerError";

    /// <summary>Kind reported for a title starting with "AI" (assumed by analogy; not observed).</summary>
    internal const string AiErrorKind = "AIError";

    /// <summary>Kind reported for any other "... Error" title.</summary>
    internal const string OtherErrorKind = "Error";

    /// <summary>One error entry parsed from the dialog text.</summary>
    /// <param name="File">Script file as read by OCR (path separators may read as l/i/1).</param>
    /// <param name="Line">Line number, when readable.</param>
    /// <param name="Code">Four-digit XS error code (old-style numerals normalized), when present.</param>
    /// <param name="Message">Message text after the code.</param>
    internal sealed record Entry(string? File, int? Line, string? Code, string Message);

    /// <summary>One read of a modal error dialog.</summary>
    /// <param name="Title">Title line as read.</param>
    /// <param name="Kind">TriggerError, AIError or Error.</param>
    /// <param name="Text">Body text (lines joined by spaces) between title and button.</param>
    /// <param name="Button">The dismiss button line (label and client-coordinate centre).</param>
    /// <param name="Entries">Parsed error entries.</param>
    internal sealed record Dialog(
        string Title,
        string Kind,
        string Text,
        ObservedLine Button,
        Entry[] Entries
    );

    [GeneratedRegex(@"^\s*(?<name>[A-Za-z ]{1,30}?)\s*Error\.?\s*$")]
    private static partial Regex TitlePattern();

    // file(line) : XS: Error 0172: message   |   XS: Error: message
    [GeneratedRegex(
        @"(?:(?<file>[\w\\/.\-]+\.xs)\s*\((?<line>[0-9oOilI]+)\)\s*:\s*)?XS:\s*Error\s*(?<code>[0-9oOilI]{4})?\s*:\s*(?<msg>.*?)(?=(?:[\w\\/.\-]+\.xs\s*\([0-9oOilI]+\)\s*:\s*)?XS:|$)"
    )]
    private static partial Regex EntryPattern();

    /// <summary>Reads one client capture; null when no titled error dialog with a dismiss button is visible.</summary>
    internal static Dialog? Read((int Width, int Height, byte[] Bgra) capture) =>
        FromLines(Scan(capture));

    /// <summary>Overlapping centred tiles over the scan band; duplicate and edge-cut lines removed.</summary>
    static ObservedLine[] Scan((int Width, int Height, byte[] Bgra) capture)
    {
        var width = Math.Min(TileWidth, capture.Width);
        var x = (capture.Width - width) / 2;
        var top = (int)(capture.Height * ScanTop);
        var bottom = (int)(capture.Height * ScanBottom);
        var kept = new List<ObservedLine>();
        for (var y = top; y < bottom; y += TileStep)
        {
            var height = Math.Min(TileHeight, bottom - y);
            if (height < EdgeMargin * 2)
                break;
            foreach (var line in UiRead.Lines(capture, [x, y, width, height], OcrScale))
            {
                var nearTop = y > top && line.Y - y < EdgeMargin;
                var nearBottom = y + height < bottom && y + height - line.Y < EdgeMargin;
                if (nearTop || nearBottom)
                    continue;
                if (
                    !kept.Any(k =>
                        Math.Abs(k.Y - line.Y) < SameLineDy && Math.Abs(k.X - line.X) < SameLineDx
                    )
                )
                    kept.Add(line);
            }
        }
        return kept.OrderBy(l => l.Y).ThenBy(l => l.X).ToArray();
    }

    /// <summary>Title = first "... Error" line; button = first dismiss label below it; body = the lines between.</summary>
    internal static Dialog? FromLines(ObservedLine[] lines)
    {
        var ordered = lines.OrderBy(l => l.Y).ThenBy(l => l.X).ToArray();
        var title = ordered.FirstOrDefault(l => TitlePattern().IsMatch(l.Text));
        if (title is null)
            return null;
        var button = DismissLabels
            .Select(label =>
                ordered.FirstOrDefault(l =>
                    l.Y > title.Y && l.Text.Trim().Equals(label, StringComparison.OrdinalIgnoreCase)
                )
            )
            .FirstOrDefault(l => l is not null);
        if (button is null)
            return null;
        var text = string.Join(
            " ",
            ordered.Where(l => l.Y > title.Y && l.Y < button.Y).Select(l => l.Text.Trim())
        );
        var name = TitlePattern().Match(title.Text).Groups["name"].Value.Trim();
        var kind =
            name.Equals("Trigger", StringComparison.OrdinalIgnoreCase) ? TriggerErrorKind
            : name.Equals("AI", StringComparison.OrdinalIgnoreCase) ? AiErrorKind
            : OtherErrorKind;
        return new(title.Text.Trim(), kind, text, button, Parse(text));
    }

    /// <summary>Splits dialog text into XS error entries; codes and line numbers use the game's old-style numerals (o = 0, i/l/I = 1).</summary>
    internal static Entry[] Parse(string text) =>
        EntryPattern()
            .Matches(text)
            .Select(m => new Entry(
                m.Groups["file"].Success ? m.Groups["file"].Value : null,
                m.Groups["line"].Success
                && int.TryParse(
                    Digits(m.Groups["line"].Value),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var line
                )
                    ? line
                    : null,
                m.Groups["code"].Success ? Digits(m.Groups["code"].Value) : null,
                m.Groups["msg"].Value.Trim()
            ))
            .ToArray();

    /// <summary>Maps OCR readings of old-style numerals back to digits.</summary>
    static string Digits(string value) =>
        new(
            value
                .Select(c =>
                    c switch
                    {
                        'o' or 'O' => '0',
                        'i' or 'I' or 'l' => '1',
                        _ => c,
                    }
                )
                .ToArray()
        );

    /// <summary>True when a re-read shows the same dialog with its button where it was.</summary>
    internal static bool Same(Dialog a, Dialog b) =>
        a.Title == b.Title
        && a.Button.Text == b.Button.Text
        && Math.Abs(a.Button.X - b.Button.X) <= ButtonTolerance
        && Math.Abs(a.Button.Y - b.Button.Y) <= ButtonTolerance;

    /// <summary>Serializable view with source excerpts for each entry that has a file and line.</summary>
    internal static object Describe(Dialog dialog, string? exe, DateTime readUtc) =>
        new
        {
            kind = dialog.Kind,
            title = dialog.Title,
            text = dialog.Text,
            button = new
            {
                label = dialog.Button.Text.Trim(),
                x = dialog.Button.X,
                y = dialog.Button.Y,
            },
            parsed = dialog
                .Entries.Select(e => new
                {
                    file = e.File,
                    line = e.Line,
                    code = e.Code,
                    hint = XsDiagnostics.Hint(e.Code),
                    message = e.Message,
                    source = e.File is not null && e.Line is int n
                        ? XsDiagnostics.SourceFor(e.File, n, exe, readUtc)
                        : null,
                })
                .ToArray(),
            textSource = "ocr",
        };

    /// <summary>Offline checks of title/button/body detection, deduplication and entry parsing.</summary>
    internal static void SelfTest()
    {
        // OCR lines read live from the C4 reproduction (normal UI 2560x1440).
        ObservedLine[] lines =
        [
            new("Trigger Error.", 1068, 421),
            new("o0:oo:0o(o): trigtemp.xs(14): XS: Error", 1260, 537),
            new("o172:')' is not a valid operator.", 1188, 580),
            new("trigtemp.xs(14) : XS: Error: parseExpression", 1285, 622),
            new("failed.", 1005, 662),
            new("trigtemp.xs(14): XS: Error oi39:", 1197, 713),
            new("parseForLoop failed.", 1111, 750),
            new("trigtemp.xs(14) : XS: Error 0273: could not", 1275, 791),
            new("parse the code for '_C4_BadFor' rule.", 1236, 833),
            new("XS: Error: could not compile file", 1203, 876),
            new("trigtemp.xs'.", 1057, 919),
            new("OK", 1281, 1004),
        ];
        var dialog =
            FromLines(lines)
            ?? throw new InvalidOperationException("Trigger Error dialog not detected.");
        if (
            dialog.Kind != TriggerErrorKind
            || dialog.Button.Y != 1004
            || dialog.Entries.Length != 5
        )
            throw new InvalidOperationException("Trigger Error dialog misread.");
        if (
            dialog.Entries[0] is not { File: "trigtemp.xs", Line: 14, Code: "0172" }
            || dialog.Entries[2].Code != "0139"
            || dialog.Entries[1].Code is not null
            || dialog.Entries[4].File is not null
            || !dialog.Entries[3].Message.Contains("_C4_BadFor", StringComparison.Ordinal)
        )
            throw new InvalidOperationException("Trigger Error entries misparsed.");
        if (FromLines(lines[..^1]) is not null || FromLines(lines[1..]) is not null)
            throw new InvalidOperationException("Dialog without title or button accepted.");
        if (
            !Same(
                dialog,
                dialog with
                {
                    Button = dialog.Button with { X = dialog.Button.X + ButtonTolerance },
                }
            )
            || Same(
                dialog,
                dialog with
                {
                    Button = dialog.Button with { Y = dialog.Button.Y + ButtonTolerance + 1 },
                }
            )
        )
            throw new InvalidOperationException("Dialog re-read comparison wrong.");
    }
}
