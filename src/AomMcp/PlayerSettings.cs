using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AomMcp;

/// <summary>Guarded Players Settings (alternative UI) / Player Data (normal UI) recipes. Never edits scenario bytes directly.</summary>
internal static class PlayerSettings
{
    internal sealed record Change(int Player, string Field, string Expected, string Desired, string? ExpectedLabel, string? DesiredLabel, string? SwatchRgb, int Scroll, bool ObservedOnly);
    static readonly string[] Fields = ["name", "control", "aiPath", "civ", "color", "visibility", "food", "wood", "gold", "favor", "pop", "popLimit", "handicap", "startAge", "maxAge", "classicalGod", "heroicGod", "mythicGod"];
    static readonly string[] AgeNames = ["Archaic Age", "Classical Age", "Heroic Age", "Mythic Age"];
    static readonly string[] Keys = ["operation", "scenarioPath", "expectedSha256", "changes", "verificationPath", "backupScenarioPath", "backupTriggerPath", "verificationDirectory", "scenarioProfileDirectory", "triggerProfileDirectory", "confirmDestructive", "confirmIsolatedScene"];
    static readonly Regex AiPath = new(@"^[a-zA-Z0-9_]+(\\[a-zA-Z0-9_]+)+?(\.xs)?\z", RegexOptions.Compiled);
    static readonly Regex BareAiName = new(@"^[a-zA-Z0-9_]+(\.xs)?\z", RegexOptions.Compiled);
    static bool ValidAiChange(string expected, string desired) => AiPath.IsMatch(desired)
        && (AiPath.IsMatch(expected) || BareAiName.IsMatch(expected));
    // Reviewed game-written AI selection omits the optional .xs extension.
    // This equivalence is limited to an explicitly requested AI destination;
    // source expectations and preservation of unrequested fields stay exact.
    static bool MatchesRequestedAi(string actual, string desired) => actual == desired
        || desired.EndsWith(".xs", StringComparison.Ordinal) && actual == desired[..^3];
    internal static void SelfTest()
    {
        if (!MatchesRequestedAi(@"aom_mcp\fixture", @"aom_mcp\fixture.xs")
            || !MatchesRequestedAi(@"aom_mcp\fixture.xs", @"aom_mcp\fixture.xs")
            || MatchesRequestedAi(@"other\fixture", @"aom_mcp\fixture.xs")
            || MatchesRequestedAi(@"aom_mcp\different", @"aom_mcp\fixture.xs")
            || MatchesRequestedAi(@"aom_mcp\fixture.txt", @"aom_mcp\fixture.xs")
            || MatchesRequestedAi(@"aom_mcp\fixture.xs", @"aom_mcp\fixture"))
            throw new InvalidOperationException("Reviewed AI extension omission comparison failed.");
        if (!ValidAiChange("chairon", @"aom_mcp\fixture.xs") || !ValidAiChange("chairon.xs", @"aom_mcp\fixture.xs")
            || !ValidAiChange(@"aom_mcp\old.xs", @"aom_mcp\fixture.xs"))
            throw new InvalidOperationException("Valid legacy/default AI path refused.");
        foreach (var unsafePath in new[] { "", "../chairon", @"..\chairon", @"C:\chairon.xs", @"\\host\chairon.xs", "chairon\n", "chairon.txt" })
            if (ValidAiChange(unsafePath, @"aom_mcp\fixture.xs")) throw new InvalidOperationException("Unsafe legacy AI path accepted.");
        if (ValidAiChange("chairon", "stock.xs") || ValidAiChange("chairon", @"aom_mcp\..\stock.xs")
            || ValidAiChange("chairon", "aom_mcp\\fixture.xs\n"))
            throw new InvalidOperationException("Destination AI path restrictions weakened.");
        if (Alternative.Row(1) != 402 || Alternative.Row(12) != 1288 || Normal.Row(1) != 312 || Normal.Row(2) != 355 || Normal.Row(12) != 787
            || Normal.AgeArrowY("mythicGod") != 662 || Alternative.AgeArrowY("startAge") != 424
            || !Normal.Dropdowns.Keys.Order().SequenceEqual(Alternative.Dropdowns.Keys.Order())
            || Normal.For(1920, 1080) is not { File: "normal-en-1920x1080.json" } small || small.Row(1) != 234 || small.Row(12) != 590
            || small.P(Normal.AgeArrowY("mythicGod")) != 497 || Alternative.For(1920, 1080).Row(12) != 966)
            throw new InvalidOperationException("Player settings UI geometry fixture failed.");
    }
    // Reviewed per-UI geometry in 2560×1440 reference pixels; S scales to the client (1920×1080: 0.75, UI scales
    // proportionally). Every click/rect goes through P(). List = [x, dy below row, width, height].
    internal sealed record Geometry(string Kind, double RowFirst, double RowStep, int AgeButtonX, int AiSetX,
        int ColorButtonX, int SwatchX, int SwatchFirst, int SwatchEnd, int PaletteWheelX, int PaletteWheelDy,
        int AgeArrowX, int[] AgeArrowYs, int[] AgeList, int AgeCloseX, int AgeCloseY,
        Dictionary<string, (int Arrow, int ListX, int ListDy, int ListW, int ListH)> Dropdowns, int ListScrolls)
    {
        internal double S { get; init; } = 1;
        internal int W { get; init; } = 2560;
        internal int H { get; init; } = 1440;
        internal string File => UiLayouts.File(Kind, W, H);
        internal int P(double reference) => UiLayouts.ToClient(reference, S);
        internal int[] P(int[] rect) => UiLayouts.ToClient(rect, S);
        internal Geometry For(int width, int height) => this with { S = UiLayouts.Scale(width), W = width, H = height };
        internal int Row(int player) => P(RowFirst + (player - 1) * RowStep);
        internal int AgeArrowY(string field) => AgeArrowYs[Array.IndexOf(AgeFields, field)];
    }
    static readonly string[] AgeFields = ["startAge", "maxAge", "classicalGod", "heroicGod", "mythicGod"];

    internal static readonly Geometry Alternative = new("alternative", 402, 80.5, 1532, 732,
        1222, 1148, 64, 306, 1164, 154, 2004, [424, 470, 572, 622, 672], [1768, 29, 246, 370], 2024, 366,
        new() { ["control"] = (450, 230, 32, 230, 340), ["civ"] = (1048, 828, 32, 230, 340), ["visibility"] = (1460, 1250, 32, 220, 340) }, 0);

    // Normal UI Scenario > Player Data, measured live: dropdown lists open below the field (civ list scrolls,
    // 9 visible gods), color palette swatches at x=88 every 47 px, Age Settings popup at a fixed position.
    internal static readonly Geometry Normal = new("normal", 312, 43.14, 1604, 2350,
        156, 88, 56, 300, 100, 150, 2000, [422, 470, 568, 616, 662], [1770, 22, 216, 370], 2026, 364,
        new() { ["control"] = (1880, 1698, 24, 198, 220), ["civ"] = (388, 198, 24, 162, 440), ["visibility"] = (2034, 1914, 24, 138, 140) }, 6);

    static readonly int[] Panel = [0, 22, 420, 74];
    static readonly int[] NormalTitle = [1100, 128, 360, 40];
    static readonly int[] BrowserTitle = [1080, 240, 420, 100];
    static readonly int[] BrowserRows = [360, 415, 430, 485];

    static void PanelGate(Game game, Geometry ui)
    {
        if (ui.Kind == "alternative") { Gate(game, ui.P(Panel), "Players Settings"); return; }
        if (!UiRead.NormalGate(ScreenProbe.Capture(game), "playerData", out var detail))
            throw new WorkflowFailure("PLAYER_UI_GATE", "ui-observe", "Normal-UI Player Data dialog pixels differ: " + detail, false, false,
                "Stop; inspect UI manually. No automatic retry.");
        Gate(game, ui.P(NormalTitle), "Player Data");
    }

    static void Gate(Game game, int[] rect, string expected) => Gate(game, rect, [expected]);

    static void Gate(Game game, int[] rect, string[] expected)
    {
        // Normal-UI text fields keep a blinking caret after TAB ("300|" read as "30d", "Bob|" as "Bot"):
        // re-read (read-only) across blink phases before refusing.
        var text = "";
        for (var attempt = 0; attempt < 4; attempt++)
        {
            if (attempt != 0) Thread.Sleep(300);
            text = UiRead.Text(game, rect);
            if (expected.Any(e => GateText(text, e))) return;
        }
        throw new WorkflowFailure("PLAYER_UI_GATE", "ui-observe", $"Expected '{string.Join("' or '", expected)}', observed '{text}' in reviewed region.", false, false,
            "Stop; inspect UI manually. No automatic retry.");
    }

    static bool GateText(string text, string expected)
    {
        // ChineseV6Tiny consistently recognizes this game's small glyph '9' as '6' (verified against
        // captured 999999 fixture). Checkpoint comparison remains authoritative for both source/destination.
        var normalized = expected.Replace('9', '6');
        var digitConfusion = Regex.IsMatch(expected, "^[0-9]+$")
            && (text.Trim() == normalized || text.Trim() == new string(normalized.Reverse().ToArray()));
        return text.Contains(expected, StringComparison.OrdinalIgnoreCase) || digitConfusion;
    }

    static int[] Rect(Geometry ui, int player, string field, string hash) =>
        UiRead.Resolve($"players.{player}.{field}", ui.W, ui.H, hash, ui.File);
    static void Click(Game game, int x, int y) { Ui.Click(game, x, y, "left"); Thread.Sleep(150); }

    static string Value(ScenarioReader.Player p, string field) => field switch
    {
        "name" => p.DisplayName, "control" => p.Control switch { 0 => "Human", 1 => "Computer", 3 => "Unavailable", _ => "unknown" },
        "aiPath" => p.AiPath, "civ" => p.CivId.ToString(CultureInfo.InvariantCulture),
        "color" => p.ColorId.ToString(CultureInfo.InvariantCulture),
        "visibility" => p.Visibility == 1 ? "Hidden" : "Normal",
        "food" => p.Food.ToString("G9", CultureInfo.InvariantCulture), "wood" => p.Wood.ToString("G9", CultureInfo.InvariantCulture),
        "gold" => p.Gold.ToString("G9", CultureInfo.InvariantCulture), "favor" => p.Favor.ToString("G9", CultureInfo.InvariantCulture),
        "pop" => p.Pop.ToString(CultureInfo.InvariantCulture), "popLimit" => p.PopLimit.ToString(CultureInfo.InvariantCulture),
        "handicap" => p.Handicap.ToString("0.00", CultureInfo.InvariantCulture),
        "startAge" => p.StartAge.ToString(CultureInfo.InvariantCulture), "maxAge" => p.MaxAge.ToString(CultureInfo.InvariantCulture),
        "classicalGod" => p.ClassicalGodId.ToString(CultureInfo.InvariantCulture),
        "heroicGod" => p.HeroicGodId.ToString(CultureInfo.InvariantCulture),
        "mythicGod" => p.MythicGodId.ToString(CultureInfo.InvariantCulture),
        _ => throw new ArgumentException("Unknown player setting."),
    };

    static Change[] Parse(JsonElement args, ScenarioReader.Snapshot source)
    {
        Catalog.ValidateObject(args, Keys);
        var changes = args.GetProperty("changes");
        if (changes.ValueKind != JsonValueKind.Array || changes.GetArrayLength() is < 1 or > 16)
            throw new ArgumentException("changes must contain 1..16 fields.");
        var seen = new HashSet<(int, string)>();
        var parsed = changes.EnumerateArray().Select(e =>
        {
            Catalog.ValidateObject(e, ["player", "field", "expected", "desired", "expectedLabel", "desiredLabel", "swatchRgb", "scroll", "observedOnly"]);
            var player = e.GetProperty("player").GetInt32();
            var field = e.GetProperty("field").GetString()!;
            var expected = e.GetProperty("expected").GetString()!;
            var desired = e.GetProperty("desired").GetString()!;
            var oldLabel = e.TryGetProperty("expectedLabel", out var ol) ? ol.GetString() : null;
            var newLabel = e.TryGetProperty("desiredLabel", out var nl) ? nl.GetString() : null;
            var rgb = e.TryGetProperty("swatchRgb", out var sw) ? sw.GetString() : null;
            var scroll = e.TryGetProperty("scroll", out var sc) ? sc.GetInt32() : 0;
            var observedOnly = e.TryGetProperty("observedOnly", out var obs) && obs.GetBoolean();
            if (player < 1 || player >= source.Players.Length || !Fields.Contains(field) || !seen.Add((player, field))
                || expected.Length > 256 || desired.Length > 256 || expected == desired && field != "aiPath" && !observedOnly
                || oldLabel?.Length > 60 || newLabel?.Length > 60 || scroll is < 0 or > 8)
                throw new ArgumentException("Invalid/duplicate player field, value or scroll count.");
            if (Value(source.Players[player], field) != expected)
                throw new WorkflowFailure("STALE_PLAYER", "preflight", $"P{player}.{field} differs from expected checkpoint value.", false, false,
                    "Inspect game-written source checkpoint; no input sent.");
            if (field == "aiPath" && !ValidAiChange(expected, desired))
                throw new ArgumentException("AI path must be relative game\\ai path (installed .xs file required)." );
            if (observedOnly && field is not ("classicalGod" or "heroicGod" or "mythicGod"))
                throw new ArgumentException("observedOnly limited to age-dependent minor god IDs.");
            if (!observedOnly && field is "civ" or "classicalGod" or "heroicGod" or "mythicGod" && (string.IsNullOrWhiteSpace(newLabel) || string.IsNullOrWhiteSpace(oldLabel)))
                throw new ArgumentException("God/civilization changes require observed expectedLabel and desiredLabel.");
            if (field == "visibility" && desired is not ("Normal" or "Hidden"))
                throw new ArgumentException("Visibility must be Normal or Hidden (only options in both reviewed UIs).");
            if (field == "color" && (rgb is null || !Regex.IsMatch(rgb, "^#[0-9a-fA-F]{6}$")))
                throw new ArgumentException("Color change requires exact observed swatchRgb and checkpoint ID.");
            if (field is "startAge" or "maxAge" && (!int.TryParse(desired, out var d) || d is < -1 or > 3 || (field == "startAge" && d == -1)))
                throw new ArgumentException("Age IDs: 0..3; maxAge also supports -1=Default.");
            if (field is "food" or "wood" or "gold" or "favor" or "pop" or "popLimit" or "handicap"
                && (!float.TryParse(desired, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || number < 0 || !float.IsFinite(number) || number > 999999))
                throw new ArgumentException("Numeric player setting outside reviewed nonnegative bounds.");
            return new Change(player, field, expected, desired, oldLabel, newLabel, rgb, scroll, observedOnly);
        }).ToArray();
        foreach (var age in parsed.Where(c => c.Field is "startAge" or "maxAge"))
            foreach (var god in new[] { "classicalGod", "heroicGod", "mythicGod" })
                if (!parsed.Any(c => c.Player == age.Player && c.Field == god && c.ObservedOnly))
                    throw new ArgumentException("Age changes require observedOnly assertions for all three minor gods (may reset automatically).");
        foreach (var sideEffect in parsed.Where(c => c.ObservedOnly))
            if (!parsed.Any(c => c.Player == sideEffect.Player && c.Field is "startAge" or "maxAge"))
                throw new ArgumentException("observedOnly requires same-player age change.");
        var direct = parsed.Where(c => !c.ObservedOnly).ToArray();
        if (direct.Where(c => c.Field == "aiPath").Skip(1).Any() || direct.Take(direct.Length - 1).Any(c => c.Field == "aiPath"))
            throw new ArgumentException("AI browser path must be final and only AI operation in batch (browser returns to map).");
        return parsed;
    }

    static string UiLabel(Change c) => c.Field switch
    {
        "civ" or "classicalGod" or "heroicGod" or "mythicGod" => c.ExpectedLabel!,
        "startAge" or "maxAge" => c.Expected == "-1" ? "Default" : AgeNames[int.Parse(c.Expected, CultureInfo.InvariantCulture)],
        "color" => "", "aiPath" => c.Expected.Split('\\')[^1].Replace(".xs", "", StringComparison.OrdinalIgnoreCase),
        _ => c.Expected,
    };
    static string DesiredLabel(Change c) => c.Field switch
    {
        "civ" or "classicalGod" or "heroicGod" or "mythicGod" => c.DesiredLabel!,
        "startAge" or "maxAge" => c.Desired == "-1" ? "Default" : AgeNames[int.Parse(c.Desired, CultureInfo.InvariantCulture)],
        _ => c.Desired,
    };

    static int[] AgeRect(Geometry ui, Change c, string hash) => UiRead.Resolve("agePopup." + c.Field, ui.W, ui.H, hash, ui.File);
    static int[] FieldRect(Geometry ui, Change c, string hash) => Rect(ui, c.Player, c.Field, hash);

    static void Select(Game game, int[] labelRect, int dropdownX, int dropdownY, int[] listRect, string[] before, string after, int scrolls = 0)
    {
        Gate(game, labelRect, before);
        Click(game, dropdownX, dropdownY);
        var matches = Array.Empty<UiRead.ObservedLine>();
        if (scrolls > 0)
        {
            // Scrollable lists open scrolled to the current selection: wheel to the top first, then scan down.
            // Small events only: one +20-notch event did not scroll the list and zoomed the map camera instead.
            game.Move(listRect[0] + listRect[2] / 2, listRect[1] + listRect[3] / 2);
            for (var up = 0; up < scrolls + 1; up++) { Ui.Wheel(game, 3); Thread.Sleep(60); }
            Thread.Sleep(150);
        }
        for (var n = 0; ; n++)
        {
            matches = UiRead.Lines(game, listRect).Where(l => l.Text.Trim().Equals(after, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 0 || n >= scrolls) break;
            // Scrollable list (normal-UI civ): wheel inside the list only, then re-read; selection still needs one exact match.
            // Measured: one notch scrolls about one row (9 of 23 civ rows visible), so 3 notches per re-read.
            game.Move(listRect[0] + listRect[2] / 2, listRect[1] + listRect[3] / 2);
            Ui.Wheel(game, -3);
            Thread.Sleep(150);
        }
        if (matches.Length != 1)
            throw new WorkflowFailure("PLAYER_UI_GATE", "dropdown", $"Expected one exact '{after}' option, observed {matches.Length}.", false, true,
                "STOP. Dropdown open; inspect UI, no retry.");
        Click(game, matches[0].X, matches[0].Y);
        Gate(game, labelRect, after);
    }

    static void SetText(Game game, int[] rect, string before, string after)
    {
        Gate(game, rect, before);
        // Unnamed players display a default label ("Player 2") while the checkpoint stores "": erase observed length too.
        var shown = UiRead.Text(game, rect).Trim();
        var x = rect[0] + rect[2] / 2; var y = rect[1] + rect[3] / 2;
        Click(game, x, y);
        Ui.Press(game, "END", []);
        for (var i = 0; i < Math.Max(Math.Max(before.Length, shown.Length), after.Length) + 3 && i < 40; i++) Ui.Press(game, "BACKSPACE", []);
        Ui.Text(game, after);
        Ui.Press(game, "TAB", []);
        Thread.Sleep(150);
        Gate(game, rect, after);
    }

    static void Age(Game game, Geometry ui, Change change, string hash)
    {
        PanelGate(game, ui);
        Click(game, ui.P(ui.AgeButtonX), ui.Row(change.Player));
        Gate(game, UiRead.Resolve("agePopup.title", ui.W, ui.H, hash, ui.File), "Age Settings for Player " + change.Player);
        var rect = AgeRect(ui, change, hash);
        var y = ui.P(ui.AgeArrowY(change.Field));
        // Menu extends down; bounded OCR list distinguishes selected field from options.
        // Normal UI shows saved startAge 0 as "Default" (alternative UI: "Archaic Age").
        string[] before = change.Field == "startAge" && change.Expected == "0" ? [UiLabel(change), "Default"] : [UiLabel(change)];
        Select(game, rect, ui.P(ui.AgeArrowX), y, [ui.P(ui.AgeList[0]), y + ui.P(ui.AgeList[1]), ui.P(ui.AgeList[2]), ui.P(ui.AgeList[3])], before, DesiredLabel(change));
        Click(game, ui.P(ui.AgeCloseX), ui.P(ui.AgeCloseY));
        PanelGate(game, ui);
    }

    static bool BrowserName(string observed, string expected)
    {
        var text = observed.Trim().TrimEnd('\\');
        if (text.Length != expected.Length) return false;
        for (var i = 0; i < text.Length; i++)
            if (char.ToLowerInvariant(text[i]) != char.ToLowerInvariant(expected[i])
                && !(expected[i] == '1' && char.ToLowerInvariant(text[i]) == 'i')
                && !(expected[i] == '0' && char.ToLowerInvariant(text[i]) == 'o')) return false;
        return true;
    }

    static void BrowserRow(Game game, Geometry ui, string name, bool folder)
    {
        var rows = UiRead.Lines(game, ui.P(BrowserRows)).Where(l => BrowserName(l.Text, name)).ToArray();
        if (rows.Length != 1)
            throw new WorkflowFailure("PLAYER_UI_GATE", "file-browser", $"Expected one visible '{name}' browser row, got {rows.Length}.", false, true,
                "STOP. File browser open; inspect manually, no retry.");
        Click(game, rows[0].X, rows[0].Y);
        Thread.Sleep(300);
        // Open Directory selects a directory as the result; Open navigates highlighted folders.
        Gate(game, ui.P([1810, 1090, 270, 85]), "Open");
        Click(game, ui.P(1956), ui.P(1140));
        Thread.Sleep(300);
    }

    static void Ai(Game game, Geometry ui, Change change, string hash)
    {
        PanelGate(game, ui);
        Click(game, ui.P(ui.AiSetX), ui.Row(change.Player));
        // Load Menu file browser geometry is identical in both reviewed UIs (measured).
        Gate(game, ui.P(BrowserTitle), "Load Menu");
        // Profile-only listing does not include INSTALLPATH game\\ai. Toggle only if campaign absent.
        var root = UiRead.Text(game, ui.P(BrowserRows));
        if (!root.Contains("campaign", StringComparison.OrdinalIgnoreCase))
        {
            Click(game, ui.P(1792), ui.P(1044));
            Gate(game, ui.P(BrowserRows), "campaign");
        }
        var parts = change.Desired.Split('\\');
        foreach (var folder in parts[..^1]) BrowserRow(game, ui, folder, true);
        var file = parts[^1].EndsWith(".xs", StringComparison.OrdinalIgnoreCase) ? parts[^1] : parts[^1] + ".xs";
        if (!UiRead.Text(game, ui.P(BrowserRows)).Contains(file, StringComparison.OrdinalIgnoreCase))
        {
            Gate(game, ui.P(BrowserTitle), "Load Menu");
            Click(game, ui.P(1000), ui.P(1044));
            Ui.Text(game, parts[^1].Replace(".xs", "", StringComparison.OrdinalIgnoreCase));
            Thread.Sleep(400);
            var query = parts[^1].Replace(".xs", "", StringComparison.OrdinalIgnoreCase);
            var observedQuery = UiRead.Text(game, ui.P([390, 1014, 480, 70]));
            if (!Regex.Replace(observedQuery, "[ _]", "").Equals(Regex.Replace(query, "[ _]", ""), StringComparison.OrdinalIgnoreCase))
                throw new WorkflowFailure("PLAYER_UI_GATE", "file-browser", "AI browser search readback mismatch: " + observedQuery,
                    false, true, "STOP; inspect browser; no retry.");
        }
        BrowserRow(game, ui, file, false);
        // File browser closes to map, not Players Settings; checkpoint must verify exact AI path.
        var title = UiRead.Text(game, ui.P(BrowserTitle));
        if (title.Contains("Load Menu", StringComparison.OrdinalIgnoreCase))
            throw new WorkflowFailure("PLAYER_UI_GATE", "file-browser", "Browser still open after file selection.", false, true,
                "STOP; inspect file selection and checkpoint before any further input.");
    }

    static void ApplyOne(Game game, Geometry ui, Change c, string hash)
    {
        if (c.Field == "aiPath") { Ai(game, ui, c, hash); return; }
        PanelGate(game, ui);
        if (c.Field is "startAge" or "maxAge" or "classicalGod" or "heroicGod" or "mythicGod") { Age(game, ui, c, hash); return; }
        var row = ui.Row(c.Player);
        if (c.Field is "control" or "civ" or "visibility")
        {
            var rect = FieldRect(ui, c, hash);
            var d = ui.Dropdowns[c.Field];
            Select(game, rect, ui.P(d.Arrow), row, [ui.P(d.ListX), row + ui.P(d.ListDy), ui.P(d.ListW), ui.P(d.ListH)],
                [UiLabel(c)], DesiredLabel(c), c.Field == "civ" ? ui.ListScrolls : 0);
            return;
        }
        if (c.Field == "color")
        {
            // No numeric color labels: require exact RGB in bounded visible palette and final checkpoint ID.
            Click(game, ui.P(ui.ColorButtonX), row);
            var screen = Ui.CapturePixels(game, 2560);
            var rgb = Convert.FromHexString(c.SwatchRgb![1..]);
            var matches = new List<(int X, int Y)>();
            for (var n = 0; n <= c.Scroll; n++)
            {
                if (n != 0) { game.Move(ui.P(ui.PaletteWheelX), row + ui.P(ui.PaletteWheelDy)); Ui.Wheel(game, -1); Thread.Sleep(150); screen = Ui.CapturePixels(game, 2560); }
                // Swatch centers every 47 reference px below the row; flat swatch interiors keep exact RGB at any reviewed scale.
                for (var offset = ui.SwatchFirst; offset < ui.SwatchEnd; offset += 47)
                {
                    var y = row + ui.P(offset);
                    if (y >= ui.P(1360)) break;
                    var pos = (y * screen.Width + ui.P(ui.SwatchX)) * 4;
                    if (screen.Bgra[pos] == rgb[2] && screen.Bgra[pos + 1] == rgb[1] && screen.Bgra[pos + 2] == rgb[0])
                        matches.Add((ui.P(ui.SwatchX), y));
                }
                if (matches.Count != 0) break;
            }
            if (matches.Count != 1)
                throw new WorkflowFailure("PLAYER_UI_GATE", "color-palette", "Desired exact RGB swatch absent/ambiguous.", false, true,
                    "STOP. Palette open; inspect screenshot. No input retry.");
            Click(game, matches[0].X, matches[0].Y);
            return; // ID comparison uses game-written checkpoint, not OCR of swatch.
        }
        SetText(game, FieldRect(ui, c, hash), UiLabel(c), c.Desired);
    }

    static string[] Differences(ScenarioReader.Snapshot source, ScenarioReader.Snapshot actual, Change[] changes)
    {
        var errors = new List<string>();
        if (source.Players.Length != actual.Players.Length || source.TriggerSection is null || actual.TriggerSection is null
            || !source.TriggerSection.AsSpan().SequenceEqual(actual.TriggerSection)) { errors.Add("player count / TR section"); return errors.ToArray(); }
        foreach (var before in source.Players)
        {
            var after = actual.Players[(int)before.Id];
            foreach (var field in Fields)
            {
                var request = changes.FirstOrDefault(c => c.Player == before.Id && c.Field == field);
                var wanted = request is null ? Value(before, field) : request.Desired;
                var observed = Value(after, field);
                var matches = request is not null && field == "aiPath"
                    ? MatchesRequestedAi(observed, wanted) : observed == wanted;
                if (!matches) errors.Add($"P{before.Id}.{field}: expected {wanted}, observed {observed}");
            }
            if (!before.Diplomacy.AsSpan().SequenceEqual(after.Diplomacy)) errors.Add($"P{before.Id}.diplomacy");
        }
        return errors.Take(100).ToArray();
    }

    /// <summary>Side-effect-free apply argument checks so missing confirmations/paths refuse as preflight errors before any connection.</summary>
    internal static void PreflightApply(JsonElement args)
    {
        if (!args.TryGetProperty("confirmDestructive", out var conf) || conf.ValueKind != JsonValueKind.True
            || !args.TryGetProperty("confirmIsolatedScene", out var isolated) || isolated.ValueKind != JsonValueKind.True)
            throw new ArgumentException("apply requires confirmDestructive=true and confirmIsolatedScene=true.");
        var missing = PlayerWorkflow.ApplyPathFields
            .Where(f => !args.TryGetProperty(f, out var v) || v.ValueKind != JsonValueKind.String).ToArray();
        if (missing.Length > 0)
            throw new ArgumentException("apply requires new backup paths, existing verificationDirectory and profile directories; missing: " + string.Join(", ", missing) + ".");
        _ = EditorFiles.ApprovedNewPath(args.GetProperty("backupScenarioPath").GetString()!, ".mythscn");
        _ = EditorFiles.ApprovedNewPath(args.GetProperty("backupTriggerPath").GetString()!, ".trg");
    }

    internal static object Execute(Game? game, JsonElement args, string exe, string hash, Action<string, JsonElement> native)
    {
        var source = ScenarioReader.Read(args.GetProperty("scenarioPath").GetString()!);
        if (!source.Sha256.Equals(args.GetProperty("expectedSha256").GetString(), StringComparison.OrdinalIgnoreCase))
            throw new WorkflowFailure("SOURCE_CHANGED", "preflight", "Checkpoint SHA-256 mismatch.", false, false, "Reinspect checkpoint; no input.");
        var changes = Parse(args, source);
        var op = args.GetProperty("operation").GetString();
        if (op == "preview") return new { preview = true, changes, source.Sha256,
            limitation = "Reviewed 2560×1440/1920×1080 alternative (Players Settings) or normal (Scenario > Player Data) UI, auto-detected at apply; each input OCR gated, one final game-writer checkpoint. Other layouts fail closed." };
        if (op == "verify")
        {
            var observed = ScenarioReader.Read(args.GetProperty("verificationPath").GetString()!);
            var errors = Differences(source, observed, changes);
            return new { verified = errors.Length == 0, mismatches = errors, sourceSha256 = source.Sha256, observedSha256 = observed.Sha256 };
        }
        if (op != "apply" || game is null) throw new ArgumentException("operation: preview/verify/apply.");
        if (!args.TryGetProperty("confirmDestructive", out var conf) || !conf.GetBoolean()
            || !args.TryGetProperty("confirmIsolatedScene", out var isolated) || !isolated.GetBoolean())
            throw new ArgumentException("apply requires confirmDestructive=true and confirmIsolatedScene=true.");
        if (!args.TryGetProperty("scenarioProfileDirectory", out var sp) || !args.TryGetProperty("triggerProfileDirectory", out var tp)
            || !args.TryGetProperty("backupScenarioPath", out var bp) || !args.TryGetProperty("backupTriggerPath", out var bt)
            || !args.TryGetProperty("verificationDirectory", out var vd)) throw new ArgumentException("apply requires explicit profile directories, backups and verificationDirectory.");
        var backup = EditorFiles.ApprovedNewPath(bp.GetString()!, ".mythscn");
        var trigger = EditorFiles.ApprovedNewPath(bt.GetString()!, ".trg");
        var dir = Path.GetDirectoryName(EditorFiles.LocalPath(Path.Combine(vd.GetString()!, "aom-player-probe.mythscn")))!;
        var profile = sp.GetString()!; var triggerProfile = tp.GetString()!;
        var (width, height) = Ui.ClientSize(game);
        game.Focus();
        var ui = (UiRead.DetectFieldUi(ScreenProbe.Capture(game), "players.1.control", width, height, hash).Kind == "normal" ? Normal : Alternative).For(width, height);
        _ = UiRead.Resolve("players.1.control", width, height, hash, ui.File);
        PanelGate(game, ui);
        if (ui.Kind == "normal")
            foreach (var a in changes.Where(c => c.Field == "aiPath"))
            {
                var control = changes.FirstOrDefault(c => c.Player == a.Player && c.Field == "control")?.Desired
                    ?? Value(source.Players[a.Player], "control");
                if (control != "Computer")
                    throw new WorkflowFailure("PLAYER_UI_REFUSED", "ui-preflight", $"Normal UI AI Set button is disabled unless P{a.Player} control is Computer.", false, false,
                        "Include control=Computer earlier in the batch or change control first. No input sent.");
            }
        // All source UI values must be observed BEFORE first input. Color requires checkpoint+swatch instead.
        foreach (var c in changes.Where(c => c.Field is not "color" and not "aiPath" and not "startAge" and not "maxAge" and not "classicalGod" and not "heroicGod" and not "mythicGod"))
            Gate(game, FieldRect(ui, c, hash), UiLabel(c));
        _ = EditorFiles.Checkpoint(game, JsonSerializer.SerializeToElement(new { path = backup, profileDirectory = profile, confirmWrite = true }), native);
        _ = EditorFiles.Triggers(game, JsonSerializer.SerializeToElement(new { operation = "export", path = trigger, profileDirectory = triggerProfile, confirmWrite = true }), native);
        var baseline = ScenarioReader.Read(backup);
        var baselineErrors = Differences(source, baseline, []);
        if (baselineErrors.Length != 0) throw new WorkflowFailure("STALE_PLAYER", "backup", string.Join("; ", baselineErrors), false, false,
            "No input. Inspect new checkpoint versus source.", backup, trigger);
        var done = new List<string>(); string? latest = null; var attempted = false;
        try
        {
            foreach (var c in changes.Where(c => !c.ObservedOnly))
            {
                attempted = true;
                ApplyOne(game, ui, c, hash);
                done.Add($"P{c.Player}.{c.Field}");
            }
            latest = EditorFiles.ApprovedNewPath(Path.Combine(dir, "aom-player-settings-verify-" + Guid.NewGuid().ToString("N") + ".mythscn"), ".mythscn");
            _ = EditorFiles.Checkpoint(game, JsonSerializer.SerializeToElement(new { path = latest, profileDirectory = profile, confirmWrite = true }), native);
            var errors = Differences(source, ScenarioReader.Read(latest), changes);
            if (errors.Length != 0) throw new InvalidDataException("Final checkpoint mismatch: " + string.Join("; ", errors));
            return new { verified = true, uiKind = ui.Kind, backupScenarioPath = backup, backupTriggerPath = trigger,
                finalCheckpointPath = latest, completedFields = done,
                limitation = "OCR readback and game-written checkpoint; no scenario file edit, runtime proof or automatic retry. Backups retained." };
        }
        catch (Exception error)
        {
            if (attempted && latest is null)
                try
                {
                    if (UiRead.Text(game, ui.P(BrowserTitle)).Contains("Load Menu", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("File browser open; cannot safely checkpoint.");
                    latest = EditorFiles.ApprovedNewPath(Path.Combine(dir, "aom-player-settings-diagnostic-" + Guid.NewGuid().ToString("N") + ".mythscn"), ".mythscn");
                    _ = EditorFiles.Checkpoint(game, JsonSerializer.SerializeToElement(new { path = latest, profileDirectory = profile, confirmWrite = true }), native);
                }
                catch { if (latest is not null && !File.Exists(latest)) latest = null; /* Preserve first error; never retry input. */ }
            throw new WorkflowFailure(attempted ? "PLAYER_UI_OUTCOME_UNKNOWN" : "PLAYER_UI_REFUSED", "input/verify",
                error.Message + " Completed readback fields: " + string.Join(", ", done), attempted, attempted,
                "STOP; inspect diagnostic checkpoint and backups. Never retry input blindly.", backup, latest ?? trigger, error);
        }
    }
}
