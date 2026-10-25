using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AomMcp;

/// <summary>Dialog-driven map tools (plans/map-building-tools.md): editor_map_size (World > Map Size dialog) and
/// editor_new_scenario (File > New Scenario dialog). Geometry comes from the decoded dialog catalog per UI kind
/// (generated/dialogs.json, DialogState.ToClient); input is clicks/keys only, never TAB/ENTER.</summary>
internal sealed partial class Server
{
    /// <summary>Gadget that opens the map size dialog (World > Map Size, ui_editor_menu.xml; live 2026-10-21 normal UI;
    /// alternative UI opens it inside MapSettings).</summary>
    const string MapSizeGadget = "AMapSizeDialog";

    /// <summary>Alternative-UI map settings section holding the size controls (dialogs.json MapSettings MapSizeTab
    /// script gadgetReal("MapSizeSection")).</summary>
    const string AltMapSizeSection = "MapSizeSection";

    /// <summary>Gadget that opens the New Scenario dialog (dialogs.json NewScenarioDialog open name; live 2026-10-21).</summary>
    const string NewScenarioGadget = "NewScenarioGadget";

    /// <summary>Default New Scenario type (first dropdown entry, live screenshot 2026-10-21).</summary>
    const string BlankScenario = "Blank";

    /// <summary>Camera move that counts as the New Scenario camera reset (world units, host choice; the dialog never
    /// moves the camera).</summary>
    const float CameraResetDistance = 0.5f;

    /// <summary>Wait without any observable change before reporting an unverified new scenario (host choice; random
    /// maps leave editor mode within ~1 s live, blank maps of equal size showed no terrain/camera change).</summary>
    const int NoChangeGraceMs = 5000;

    /// <summary>Live player array length (includes Gaia).</summary>
    /// <param name="game">Validated editor connection.</param>
    static int PlayerCount(Game game) =>
        JsonSerializer
            .SerializeToElement(LiveWorld.Players(game, JsonDocument.Parse("{}").RootElement))
            .GetProperty("playerCount")
            .GetInt32();

    /// <summary>Normal UI kind name (SceneUi).</summary>
    const string NormalUi = "normal";

    /// <summary>editor_map_size operations (host API).</summary>
    static readonly string[] MapSizeOperations = ["get", "add", "take"];

    /// <summary>editor_new_scenario sizes (host API) and their dialog labels (normal radio set / alternative dropdown,
    /// live screenshot 2026-10-21: Normal, Large, Giant).</summary>
    static readonly string[] ScenarioSizes = ["normal", "large", "giant"];

    /// <summary>Map size side arguments and their dialog text fields (ui_map_size_dlg.xml): right = MaxX (+X, live),
    /// left = MinX, top = MaxZ, bottom = MinZ.</summary>
    static readonly (string Arg, string Control, bool AlongX)[] MapSides =
    [
        ("right", "mapsize_MaxXText", true),
        ("left", "mapsize_MinXText", true),
        ("top", "mapsize_MaxZText", false),
        ("bottom", "mapsize_MinZText", false),
    ];

    /// <summary>Largest meters per side (host bound).</summary>
    const int MaxMapSizeMeters = 1024;

    /// <summary>Dialog open/close settle (host choice; live: both dialogs drew on the next frames).</summary>
    const int DialogSettleMs = 500;

    /// <summary>Resize poll window: live the tile dims changed within ~1 s after Add.</summary>
    const int MapResizePollMs = 3000;

    /// <summary>Poll step while waiting for map changes (host choice).</summary>
    const int MapPollStepMs = 100;

    /// <summary>Backspaces that clear a short numeric/seed field (host choice; fields hold ≤ 11 characters).</summary>
    const int FieldBackspaces = 12;

    /// <summary>OCR frames per field verification (focused-field caret can hide short text, DialogState.SetText).</summary>
    const int FieldVerifyFrames = 3;

    /// <summary>Pause between field verification frames (host choice).</summary>
    const int FieldVerifyMs = 250;

    /// <summary>Bound for New Scenario generation (host choice; random maps take seconds).</summary>
    const int NewScenarioTimeoutMs = 60000;

    /// <summary>Poll step while a new scenario generates (host choice).</summary>
    const int NewScenarioPollMs = 500;

    /// <summary>Dropdown list height below/above its field in 1024×768 design units: live 9 rows × 25 px at 900 px client
    /// height ≈ 192 units plus border (2026-10-21 New Scenario Type list).</summary>
    const double ListHeight1024 = 200;

    /// <summary>Dropdown arrow center inside the field's right edge in design units (live: 14 px at 1600 wide).</summary>
    const double ArrowInset1024 = 9;

    /// <summary>Radio circle center right of the radio set's left edge in design units (live: 18 px at 1600 wide).</summary>
    const double RadioInset1024 = 11.5;

    /// <summary>Size radio row pitch in design units: live rows at y 490/519/548 at 900 px client height (2026-10-21);
    /// the catalog radio-set rect (469..517) misses the last rows.</summary>
    const double RadioRow1024 = 24.7;

    /// <summary>Wheel notches per dropdown scroll step (host choice; fewer than the 9 visible rows).</summary>
    const int ListWheelNotches = 2;

    /// <summary>Most dropdown scroll steps (host bound; the RMS list has ~62 entries).</summary>
    const int MaxListScrolls = 60;

    /// <summary>Wheel notches that scroll any dropdown list to its top (Ui.Wheel maximum).</summary>
    const int ListTopNotches = 20;

    /// <summary>Settle after a dropdown click (host choice).</summary>
    const int DropdownSettleMs = 350;

    /// <summary>OCR padding around dialog controls (client pixels, host choice).</summary>
    const int OcrPadding = 2;

    /// <summary>Design-space width (DialogState.ToClient 1024×768).</summary>
    const double DesignWidth = 1024;

    /// <summary>Design-space height (DialogState.ToClient 1024×768).</summary>
    const double DesignHeight = 768;

    /// <summary>"276 x 256 meters" text of mapsize_CurrentSize (live 2026-10-21).</summary>
    static readonly Regex MapSizeText = new(
        @"(\d+)\s*[xX×]\s*(\d+)",
        RegexOptions.CultureInvariant
    );

    /// <summary>Preflight for the dialog tools (no game connection).</summary>
    /// <param name="name">Tool name.</param>
    /// <param name="args">Tool arguments (schema-validated).</param>
    void PreflightMapDialog(string name, JsonElement args)
    {
        switch (name)
        {
            case "editor_map_size" when String(args, "operation", "get") != "get":
                EditorFiles.Confirm(args, "confirmDestructive");
                if (MapSides.All(s => Int(args, s.Arg, 0) == 0))
                    throw new ArgumentException(
                        "add/take needs at least one of right/left/top/bottom > 0."
                    );
                break;
            case "editor_new_scenario":
                EditorFiles.Confirm(args, "confirmDestructive");
                var type = String(args, "type", BlankScenario);
                var known = RandomMapNames(exe);
                if (
                    known.Length > 0
                    && Norm(type) != Norm(BlankScenario)
                    && !known.Any(k => Norm(k) == Norm(type))
                )
                    throw new ArgumentException(
                        $"Unknown scenario type '{type}' (not Blank or a game/random_maps/*.xs name). Suggestions: {string.Join(", ", GameDataCatalog.Suggest(type, known))}"
                    );
                break;
        }
    }

    /// <summary>Random map script names offered by New Scenario: install game/random_maps/*.xs plus profile
    /// random_maps folders when present (empty when the install folder is missing).</summary>
    /// <param name="exe">Game executable path.</param>
    static string[] RandomMapNames(string exe)
    {
        var dirs = new List<string>
        {
            Path.Combine(Path.GetDirectoryName(exe) ?? "", "game", "random_maps"),
        };
        var profiles = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Games",
            "Age of Mythology Retold"
        );
        if (Directory.Exists(profiles))
            foreach (var p in Directory.GetDirectories(profiles))
            {
                dirs.Add(Path.Combine(p, "random_maps"));
                dirs.Add(Path.Combine(p, "game", "random_maps"));
            }
        return
        [
            .. dirs.Where(Directory.Exists)
                .SelectMany(d => Directory.GetFiles(d, "*.xs", SearchOption.TopDirectoryOnly))
                .Select(Path.GetFileNameWithoutExtension)
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase),
        ];
    }

    /// <summary>UI kind by scene pixel gates (before a dialog covers them), else the profile alternative-UI hint.</summary>
    /// <param name="game">Validated editor connection.</param>
    static (string Kind, string Detection) DialogUiKind(Game game)
    {
        var ui = SceneUi.TryLoad(game, out var reason);
        if (ui is not null)
            return (ui.Kind, $"pixel gates matched {ui.File}");
        var hint = SceneUi.ProfileAlternativeHint();
        return (
            hint == true ? AlternativeUi : NormalUi,
            $"pixel gates failed ({reason}); profile alternative-UI hint {hint?.ToString() ?? "unknown"}"
        );
    }

    /// <summary>Resolves a dialog for the UI kind from the decoded catalog, else refuses before input.</summary>
    /// <param name="kind">normal or alternative.</param>
    /// <param name="gadget">Open name.</param>
    DialogState.Target DialogTarget(string kind, string gadget) =>
        DialogState.Resolve((_dialogs ??= DialogCatalog.Load()).AllWindows, kind, gadget)
        ?? throw new WorkflowFailure(
            "DIALOG_CATALOG_MISSING",
            "preflight",
            $"No decoded {kind}-UI dialog for {gadget} in generated/dialogs.json.",
            false,
            false,
            "Run editor_generate_catalog. No input sent."
        );

    /// <summary>Client rect [x,y,w,h] of a catalog control.</summary>
    /// <param name="target">Resolved dialog.</param>
    /// <param name="control">Control name.</param>
    /// <param name="width">Client width.</param>
    /// <param name="height">Client height.</param>
    static int[] ControlRect(DialogState.Target target, string control, int width, int height) =>
        DialogState.ToClient(
            target.Window.Controls.FirstOrDefault(c => c.Name == control)?.Size1024
                ?? throw new InvalidDataException($"Dialog control {control} has no geometry."),
            width,
            height
        );

    /// <summary>Catalog control by name.</summary>
    /// <param name="target">Resolved dialog.</param>
    /// <param name="control">Control name.</param>
    static DialogControl Control(DialogState.Target target, string control) =>
        target.Window.Controls.First(c => c.Name == control);

    /// <summary>OCR text of a padded client rect, clamped to the client.</summary>
    /// <param name="game">Validated editor connection.</param>
    /// <param name="rect">Client rect.</param>
    static UiRead.ObservedLine[] ReadRect(Game game, int[] rect)
    {
        var (w, h) = Ui.ClientSize(game);
        int x = Math.Max(0, rect[0] - OcrPadding),
            y = Math.Max(0, rect[1] - OcrPadding);
        return UiRead.Lines(
            game,
            [
                x,
                y,
                Math.Min(w - x, rect[2] + 2 * OcrPadding),
                Math.Min(h - y, rect[3] + 2 * OcrPadding),
            ],
            2
        );
    }

    /// <summary>Normalized OCR value of a labelled field (label prefix stripped).</summary>
    /// <param name="game">Validated editor connection.</param>
    /// <param name="rect">Field rect.</param>
    /// <param name="label">Catalog label, or null.</param>
    static string FieldText(Game game, int[] rect, string? label)
    {
        var text = Norm(string.Join(" ", ReadRect(game, rect).Select(l => l.Text)));
        var prefix = Norm(label ?? "");
        return prefix.Length > 0 && text.StartsWith(prefix, StringComparison.Ordinal)
            ? text[prefix.Length..]
            : text;
    }

    /// <summary>Text part of a dropdown field: the field without its right arrow strip (live 2026-10-21: OCR read the
    /// arrow glyph as a trailing "t", "Blank" → "blankt").</summary>
    /// <param name="game">Validated editor connection.</param>
    /// <param name="field">Dropdown field rect.</param>
    static int[] DropdownText(Game game, int[] field)
    {
        var (w, _) = Ui.ClientSize(game);
        var strip = (int)Math.Round(2 * ArrowInset1024 * w / DesignWidth);
        return [field[0], field[1], Math.Max(1, field[2] - strip), field[3]];
    }

    /// <summary>Center of a client rect.</summary>
    /// <param name="r">Rect [x,y,w,h].</param>
    static (int X, int Y) Center(int[] r) => (r[0] + r[2] / 2, r[1] + r[3] / 2);

    /// <summary>Focuses a text field, clears it with END + backspaces, types the value (never TAB/ENTER) and reads it
    /// back by OCR.</summary>
    /// <param name="game">Validated editor connection.</param>
    /// <param name="rect">Field rect.</param>
    /// <param name="label">Catalog label (stripped from the read).</param>
    /// <param name="value">Text to type.</param>
    /// <returns>Normalized read and whether it equals the typed text.</returns>
    static (string Read, bool Verified) TypeField(
        Game game,
        int[] rect,
        string? label,
        string value
    )
    {
        var (x, y) = Center(rect);
        RequireNoHeldInput();
        Ui.SettledClick(game, x, y, "left");
        Thread.Sleep(FieldVerifyMs);
        Ui.Press(game, "END", []);
        for (var i = 0; i < FieldBackspaces; i++)
            Ui.Press(game, "BACKSPACE", []);
        Ui.Text(game, value);
        var read = "";
        for (var frame = 0; frame < FieldVerifyFrames; frame++)
        {
            Thread.Sleep(FieldVerifyMs);
            read = FieldText(game, rect, label);
            if (read == Norm(value))
                return (read, true);
        }
        return (read, false);
    }

    /// <summary>Expected tile dims after Add/Take: each side adds or removes meters / tile size tiles on its axis.</summary>
    /// <param name="tilesX">Tiles along X before.</param>
    /// <param name="tilesZ">Tiles along Z before.</param>
    /// <param name="tileSize">World units per tile.</param>
    /// <param name="operation">add or take.</param>
    /// <param name="meters">Meters per side argument (right/left/top/bottom).</param>
    internal static (int X, int Z) ExpectedTiles(
        int tilesX,
        int tilesZ,
        int tileSize,
        string operation,
        IReadOnlyDictionary<string, int> meters
    )
    {
        var sign = operation == "add" ? 1 : -1;
        foreach (var (arg, _, alongX) in MapSides)
        {
            var tiles = sign * meters.GetValueOrDefault(arg) / tileSize;
            if (alongX)
                tilesX += tiles;
            else
                tilesZ += tiles;
        }
        return (tilesX, tilesZ);
    }

    /// <summary>editor_map_size: read the map size, or grow/shrink sides through the Map Size dialog.</summary>
    /// <param name="game">Validated editor connection.</param>
    /// <param name="args">operation, right/left/top/bottom meters, readDialog.</param>
    object MapSize(Game game, JsonElement args)
    {
        var op = String(args, "operation", "get");
        var view = EditorView.ReadView(game);
        int tx = view.Tiles[0],
            tz = view.Tiles[1];
        var scale = view.Scale;
        object Dims(int x, int z) =>
            new
            {
                tilesX = x,
                tilesZ = z,
                worldX = x * scale,
                worldZ = z * scale,
            };
        var readDialog = args.TryGetProperty("readDialog", out var rd) && rd.GetBoolean();
        if (op == "get" && !readDialog)
            return new
            {
                operation = op,
                size = Dims(tx, tz),
                tileSize = scale,
                limitation = "Live memory read of map tile dims (map_info). readDialog=true also opens the Map Size dialog and OCRs its current-size text.",
            };
        var values = MapSides.ToDictionary(s => s.Arg, s => Int(args, s.Arg, 0));
        foreach (var (arg, value) in values)
            if (value % (int)scale != 0)
                throw new WorkflowFailure(
                    "MAP_SIZE_NOT_TILE_MULTIPLE",
                    "preflight",
                    $"{arg}={value} meters is not a multiple of the tile size {scale}.",
                    false,
                    false,
                    "Use multiples of the tile size. No input sent."
                );
        RequireNoCursor(game);
        var (kind, detection) = DialogUiKind(game);
        var target = DialogTarget(kind, MapSizeGadget);
        var (w, h) = Ui.ClientSize(game);
        var current = ControlRect(target, "mapsize_CurrentSize", w, h);
        var closeName =
            Control(target, "mapsize_CancelBtn")
                .Scripts?.SelectMany(s => s.Calls)
                .Select(c => c.Arguments?.GetValueOrDefault("name")?.ToString())
                .FirstOrDefault(n => n is not null)
            ?? MapSizeGadget;
        (int X, int Z)? Shown()
        {
            var text = string.Join(" ", ReadRect(game, current).Select(l => l.Text));
            var m = MapSizeText.Match(text);
            return m.Success
                ? (
                    int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                    int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)
                )
                : null;
        }
        var steps = new List<object>();
        int ex = tx,
            ez = tz;
        (int X, int Z)? shownBefore,
            shownAfter = null;
        var fields = new List<object>();
        int ax = tx,
            az = tz;
        _bridge.Execute(game, $"gadgetReal({Catalog.Quote(MapSizeGadget)})");
        try
        {
            if (kind == AlternativeUi)
                _bridge.Execute(game, $"gadgetReal({Catalog.Quote(AltMapSizeSection)})");
            Thread.Sleep(DialogSettleMs);
            shownBefore = Shown();
            if (shownBefore is null)
                throw new WorkflowFailure(
                    "DIALOG_NOT_OPEN",
                    "observe",
                    $"Map Size dialog current-size text not readable at {string.Join(",", current)} ({kind} UI, {detection}).",
                    true,
                    false,
                    "Dialog closed again; scene unchanged. Screenshot and open World > Map Size manually to compare."
                );
            steps.Add(
                new
                {
                    opened = MapSizeGadget,
                    shownMeters = new[] { shownBefore.Value.X, shownBefore.Value.Z },
                }
            );
            if (op != "get")
            {
                foreach (var (arg, control, alongX) in MapSides)
                {
                    var rect = ControlRect(target, control, w, h);
                    var text = values[arg].ToString(CultureInfo.InvariantCulture);
                    var (read, verified) = TypeField(game, rect, null, text);
                    fields.Add(
                        new
                        {
                            side = arg,
                            typed = text,
                            read,
                            verified,
                        }
                    );
                    // A different non-empty number means the field did not take the value: refuse before Add/Take.
                    if (!verified && read.Length > 0 && read.All(char.IsAsciiDigit))
                        throw new WorkflowFailure(
                            "MAP_SIZE_FIELD_MISMATCH",
                            "type",
                            $"Field {arg} reads '{read}' after typing '{text}'.",
                            true,
                            false,
                            "Dialog closed; scene unchanged. Screenshot the dialog and retry."
                        );
                }
                (ex, ez) = ExpectedTiles(tx, tz, (int)scale, op, values);
                var button = ControlRect(
                    target,
                    op == "add" ? "mapsize_AddBtn" : "mapsize_TakeBtn",
                    w,
                    h
                );
                var (bx, by) = Center(button);
                RequireNoHeldInput();
                Ui.SettledClick(game, bx, by, "left");
                steps.Add(new { clicked = op == "add" ? "Add" : "Take", at = new[] { bx, by } });
                var deadline = Win.GetTickCount64() + MapResizePollMs;
                do
                {
                    Thread.Sleep(MapPollStepMs);
                    var v = EditorView.ReadView(game);
                    (ax, az) = (v.Tiles[0], v.Tiles[1]);
                } while ((ax, az) == (tx, tz) && Win.GetTickCount64() < deadline);
                Thread.Sleep(DialogSettleMs);
                shownAfter = Shown();
            }
        }
        finally
        {
            _bridge.Execute(game, $"gadgetUnreal({Catalog.Quote(closeName)})");
        }
        if (op != "get" && (ax, az) != (ex, ez))
            throw new WorkflowFailure(
                "MAP_SIZE_MISMATCH",
                "verify",
                $"Map is {ax}×{az} tiles after {op}; expected {ex}×{ez} (before {tx}×{tz}).",
                (ax, az) != (tx, tz),
                false,
                (ax, az) == (tx, tz)
                    ? "Map unchanged. Screenshot the dialog (fields/buttons) and retry."
                    : "Map resized differently: inspect with map_info and reverse with the opposite operation."
            );
        return new
        {
            operation = op,
            uiKind = kind,
            uiDetection = detection,
            before = Dims(tx, tz),
            after = Dims(ax, az),
            expected = op == "get" ? null : Dims(ex, ez),
            dialogMeters = new
            {
                before = shownBefore is { } b ? new[] { b.X, b.Z } : null,
                after = shownAfter is { } a ? new[] { a.X, a.Z } : null,
            },
            fields,
            steps,
            success = op == "get" || (ax, az) == (ex, ez),
            limitation = "Drives the World > Map Size dialog (catalog geometry per UI kind; normal UI live-tested 2026-10-21, alternative UI untested). right = +X, left = -X, top = +Z, bottom = -Z (control names mapsize_MaxX/MinX/MaxZ/MinZ; right=+X live). All four fields are retyped every call (the dialog keeps typed values). New strips copy the edge terrain/water (live). Resize is not proven undoable: reverse with the opposite operation. Not saved.",
        };
    }

    /// <summary>Opens a dropdown list and scrolls it to one end until the visible rows stop changing (live 2026-10-21:
    /// the list opens at the current entry; one wheel burst did not reach the top from yellow_river).</summary>
    /// <param name="game">Validated editor connection.</param>
    /// <param name="field">Dropdown field rect.</param>
    /// <param name="toTop">Scroll to the first entry, else to the last.</param>
    /// <returns>List region and the field's arrow pixel.</returns>
    static (int[] Region, (int X, int Y) Arrow) OpenList(Game game, int[] field, bool toTop)
    {
        var (w, h) = Ui.ClientSize(game);
        int arrowX = field[0] + field[2] - (int)Math.Round(ArrowInset1024 * w / DesignWidth),
            arrowY = field[1] + field[3] / 2,
            listH = (int)Math.Round(ListHeight1024 * h / DesignHeight);
        int[] below =
            [field[0], field[1] + field[3], field[2], Math.Min(listH, h - field[1] - field[3])],
            above = [field[0], Math.Max(0, field[1] - listH), field[2], Math.Min(listH, field[1])];
        RequireNoHeldInput();
        Ui.SettledClick(game, arrowX, arrowY, "left");
        Thread.Sleep(DropdownSettleMs);
        var regions = new[] { below, above }.Where(r => r[3] > 0).ToArray();
        // The list opens on the side with entries: pick the region whose OCR finds the most lines.
        var region = regions.OrderByDescending(r => ReadRect(game, r).Length).First();
        var (lx, ly) = Center(region);
        string? last = null;
        for (var step = 0; step < MaxListScrolls; step++)
        {
            game.Move(lx, ly);
            Ui.Wheel(game, toTop ? ListTopNotches : -ListTopNotches);
            Thread.Sleep(DropdownSettleMs);
            var rows = string.Join("|", ReadRect(game, region).Select(l => l.Text));
            if (rows == last)
                break;
            last = rows;
        }
        return (region, (arrowX, arrowY));
    }

    /// <summary>Opens a dropdown, scans its list by OCR (scrolling from the top) and clicks the exact entry.</summary>
    /// <param name="game">Validated editor connection.</param>
    /// <param name="field">Dropdown field rect.</param>
    /// <param name="label">Field label (stripped from the read).</param>
    /// <param name="wanted">Entry text.</param>
    /// <returns>Scroll steps used.</returns>
    static int PickDropdown(Game game, int[] field, string? label, string wanted)
    {
        var (region, arrow) = OpenList(game, field, true);
        var (lx, ly) = Center(region);
        string? previous = null;
        for (var scroll = 0; scroll <= MaxListScrolls; scroll++)
        {
            var lines = ReadRect(game, region);
            var hit = lines.FirstOrDefault(l => Norm(l.Text) == Norm(wanted));
            if (hit is not null)
            {
                RequireNoHeldInput();
                Ui.SettledClick(game, hit.X, hit.Y, "left");
                Thread.Sleep(DropdownSettleMs);
                if (
                    FieldText(game, DropdownText(game, field), label) is var read
                    && read != Norm(wanted)
                )
                    throw new WorkflowFailure(
                        "DROPDOWN_NOT_SET",
                        "select",
                        $"Clicked '{hit.Text}' but the field reads '{read}'.",
                        true,
                        false,
                        "Dialog closed; scene unchanged. Screenshot and retry."
                    );
                return scroll;
            }
            var signature = string.Join("|", lines.Select(l => l.Text));
            if (signature == previous)
                break; // List end: scrolling no longer changes the visible rows.
            previous = signature;
            game.Move(lx, ly);
            Ui.Wheel(game, -ListWheelNotches);
            Thread.Sleep(DropdownSettleMs);
        }
        Ui.SettledClick(game, arrow.X, arrow.Y, "left"); // Close the list again.
        throw new WorkflowFailure(
            "DROPDOWN_ENTRY_NOT_FOUND",
            "select",
            $"'{wanted}' not found in the dropdown list by OCR.",
            true,
            false,
            "Dialog closed; scene unchanged. Check the name (editor_new_scenario preflight lists random maps)."
        );
    }

    /// <summary>List row pitch in design units: live 24.8 px at 900 px client height (New Scenario players list,
    /// 2026-10-21).</summary>
    const double ListRow1024 = 21.2;

    /// <summary>Horizontal click position inside a list region as a fraction of the field width: the field rect includes
    /// its label and the list starts ~30% in (live 2026-10-21 players field 535..945, list text from x 660).</summary>
    const double ListClickFraction = 0.75;

    /// <summary>Most click/verify rounds for a numeric dropdown (host bound).</summary>
    const int NumberPickTries = 3;

    /// <summary>Picks entry n of a numeric 1..count dropdown by row position: OCR misreads single digits in the open
    /// list (live 2026-10-21: 6→"9", 7→"", 9→"6") but reads the closed field reliably. Rows are counted from the top
    /// (or back from the end for entries below the first page); the field is read back and the row offset corrected.</summary>
    /// <param name="game">Validated editor connection.</param>
    /// <param name="field">Dropdown field rect.</param>
    /// <param name="label">Field label (stripped from the read).</param>
    /// <param name="n">Wanted number.</param>
    /// <param name="count">Entries in the list (1..count).</param>
    /// <returns>Click rounds used.</returns>
    static int PickNumber(Game game, int[] field, string? label, int n, int count)
    {
        var (_, h) = Ui.ClientSize(game);
        var pitch = ListRow1024 * h / DesignHeight;
        var text = DropdownText(game, field);
        var offset = 0; // Row correction from the previous round.
        var read = "";
        for (var round = 1; round <= NumberPickTries; round++)
        {
            var (probe, _) = OpenList(game, field, true);
            var visible = (int)Math.Floor(probe[3] / pitch);
            var fromTop = n <= visible;
            // Entry beyond the first page: rescroll to the end and count back from the last row.
            var region = fromTop ? probe : OpenListEnd(game, probe);
            var y = fromTop
                ? region[1] + pitch * (n - 1 + 0.5 + offset)
                : region[1] + pitch * (visible - 0.5) - pitch * (count - n - offset);
            RequireNoHeldInput();
            if (y < region[1] || y > region[1] + region[3])
                break; // Correction left the list: give up instead of clicking outside it.
            Ui.SettledClick(
                game,
                region[0] + (int)Math.Round(region[2] * ListClickFraction),
                (int)Math.Round(y),
                "left"
            );
            Thread.Sleep(DropdownSettleMs);
            read = FieldText(game, text, label);
            if (read == n.ToString(CultureInfo.InvariantCulture))
                return round;
            if (!int.TryParse(read, NumberStyles.None, CultureInfo.InvariantCulture, out var got))
                break;
            offset += n - got;
        }
        throw new WorkflowFailure(
            "DROPDOWN_NOT_SET",
            "select",
            $"Field reads '{read}' after picking row {n} of {count}.",
            true,
            false,
            "Dialog closed; scene unchanged. Screenshot and retry."
        );
    }

    /// <summary>Scrolls an open list to its last entry until the visible rows stop changing.</summary>
    /// <param name="game">Validated editor connection.</param>
    /// <param name="region">Open list region.</param>
    /// <returns>The same region.</returns>
    static int[] OpenListEnd(Game game, int[] region)
    {
        var (lx, ly) = Center(region);
        string? last = null;
        for (var step = 0; step < MaxListScrolls; step++)
        {
            game.Move(lx, ly);
            Ui.Wheel(game, -ListTopNotches);
            Thread.Sleep(DropdownSettleMs);
            var rows = string.Join("|", ReadRect(game, region).Select(l => l.Text));
            if (rows == last)
                break;
            last = rows;
        }
        return region;
    }

    /// <summary>editor_new_scenario: create a scenario through the New Scenario dialog (replaces the open scenario).</summary>
    /// <param name="game">Validated editor connection.</param>
    /// <param name="args">type, seed, players, size, confirmDestructive.</param>
    object NewScenario(Game game, JsonElement args)
    {
        var type = String(args, "type", BlankScenario);
        int? players = args.TryGetProperty("players", out var pe) ? pe.GetInt32() : null;
        int? seed = args.TryGetProperty("seed", out var se) ? se.GetInt32() : null;
        var size = args.TryGetProperty("size", out var ze) ? ze.GetString() : null;
        RequireNoCursor(game);
        ExitEditModes(game);
        var before = EditorView.ReadView(game);
        var (kind, detection) = DialogUiKind(game);
        var target = DialogTarget(kind, NewScenarioGadget);
        var (w, h) = Ui.ClientSize(game);
        var typeControl = Control(target, "NewScenarioTypeDropdown");
        var playersControl = Control(target, "NewScenarioPlayersDropdown");
        var seedControl = Control(target, "NewScenarioSeedEntry");
        var generate = Control(target, "NewScenarioGenerateButton");
        var steps = new List<object>();
        var generated = false;
        _bridge.Execute(game, $"gadgetReal({Catalog.Quote(NewScenarioGadget)})");
        try
        {
            Thread.Sleep(DialogSettleMs);
            var generateRect = ControlRect(target, generate.Name, w, h);
            var buttonText = Norm(
                string.Join(" ", ReadRect(game, generateRect).Select(l => l.Text))
            );
            if (buttonText != Norm(generate.Label ?? ""))
                throw new WorkflowFailure(
                    "DIALOG_NOT_OPEN",
                    "observe",
                    $"New Scenario '{generate.Label}' button not read at {string.Join(",", generateRect)} (read '{buttonText}'; {kind} UI, {detection}).",
                    true,
                    false,
                    "Dialog closed again; scene unchanged. Screenshot and compare."
                );
            var typeRect = ControlRect(target, typeControl.Name, w, h);
            var currentType = FieldText(game, DropdownText(game, typeRect), typeControl.Label);
            if (currentType != Norm(type))
                steps.Add(
                    new { type, scrolls = PickDropdown(game, typeRect, typeControl.Label, type) }
                );
            else
                steps.Add(new { type, alreadySelected = true });
            if (players is { } p)
            {
                var rect = ControlRect(target, playersControl.Name, w, h);
                var text = p.ToString(CultureInfo.InvariantCulture);
                if (FieldText(game, DropdownText(game, rect), playersControl.Label) != text)
                    steps.Add(
                        new
                        {
                            players = p,
                            rounds = PickNumber(
                                game,
                                rect,
                                playersControl.Label,
                                p,
                                LiveUnits.MaxPlayer
                            ),
                        }
                    );
                else
                    steps.Add(new { players = p, alreadySelected = true });
            }
            if (seed is { } s)
            {
                var text = s.ToString(CultureInfo.InvariantCulture);
                var (read, verified) = TypeField(
                    game,
                    ControlRect(target, seedControl.Name, w, h),
                    seedControl.Label,
                    text
                );
                // OCR misreads single characters (live: "5" read as "n"): refuse only on a different number.
                if (!verified && read.Length > 0 && read.All(char.IsAsciiDigit))
                    throw new WorkflowFailure(
                        "SEED_NOT_SET",
                        "type",
                        $"Seed field reads '{read}' after typing '{text}'.",
                        true,
                        false,
                        "Dialog closed; scene unchanged. Retry or omit seed."
                    );
                steps.Add(
                    new
                    {
                        seed = s,
                        seedRead = read,
                        seedVerified = verified,
                    }
                );
            }
            if (size is not null)
            {
                var label = char.ToUpperInvariant(size[0]) + size[1..];
                if (kind == AlternativeUi)
                {
                    var c = Control(target, "NewScenarioSizeDropdown");
                    steps.Add(
                        new
                        {
                            size = label,
                            scrolls = PickDropdown(
                                game,
                                ControlRect(target, c.Name, w, h),
                                c.Label,
                                label
                            ),
                        }
                    );
                }
                else
                {
                    var set = ControlRect(target, "NewScenarioSizeRadioButtons", w, h);
                    // The catalog rect covers only the first rows (live: Giant drawn below it): read all rows.
                    int[] radios =
                    [
                        set[0],
                        set[1],
                        set[2],
                        Math.Max(
                            set[3],
                            (int)
                                Math.Round(
                                    (ScenarioSizes.Length + 1) * RadioRow1024 * h / DesignHeight
                                )
                        ),
                    ];
                    var line =
                        ReadRect(game, radios).FirstOrDefault(l => Norm(l.Text) == Norm(label))
                        ?? throw new WorkflowFailure(
                            "SIZE_OPTION_NOT_FOUND",
                            "select",
                            $"Size option '{label}' not read in the radio set at {string.Join(",", radios)}.",
                            true,
                            false,
                            "Dialog closed; scene unchanged. Screenshot and retry."
                        );
                    var rx = radios[0] + (int)Math.Round(RadioInset1024 * w / DesignWidth);
                    RequireNoHeldInput();
                    Ui.SettledClick(game, rx, line.Y, "left");
                    steps.Add(new { size = label, radio = new[] { rx, line.Y } });
                }
            }
            var (gx, gy) = Center(generateRect);
            RequireNoHeldInput();
            Ui.SettledClick(game, gx, gy, "left");
            generated = true;
            steps.Add(new { clicked = generate.Label });
        }
        finally
        {
            if (!generated)
                _bridge.Execute(game, $"gadgetUnreal({Catalog.Quote(NewScenarioGadget)})");
        }
        // Wait for an observable new scenario that reads twice in a row: new terrain object/dims, editor mode left
        // (random maps), a camera move, or the requested live player count (live 2026-10-21: Blank → Blank at equal size
        // reuses the terrain object, stays in editor mode and kept the camera; only the player count proved it).
        var start = Win.GetTickCount64();
        var deadline = start + NewScenarioTimeoutMs;
        EditorView.ViewState? after = null;
        var changed = false;
        var left = false;
        var cameraReset = false;
        var stable = 0;
        ProgressReporter.Report(0, null, "waiting for new scenario");
        while (Win.GetTickCount64() < deadline && stable < 2)
        {
            Thread.Sleep(NewScenarioPollMs);
            try
            {
                var v = EditorView.ReadView(game);
                var moved =
                    Vector3.Distance(v.Pose.Position, before.Pose.Position) > CameraResetDistance;
                var differs =
                    v.Terrain != before.Terrain
                    || v.Tiles[0] != before.Tiles[0]
                    || v.Tiles[1] != before.Tiles[1]
                    || moved;
                cameraReset |= moved;
                if ((differs || left) && after is not null && after.Terrain == v.Terrain)
                    stable++;
                changed |= differs || left;
                after = v;
                if (!changed && !left && Win.GetTickCount64() - start > NoChangeGraceMs)
                    break; // Editor stayed and nothing moved: likely an identical blank scenario.
            }
            catch (Exception e)
                when (e
                        is InvalidDataException
                            or InvalidOperationException
                            or System.ComponentModel.Win32Exception
                )
            {
                // Live 2026-10-21: an RMS type leaves editor mode (black loading screen, Game.Editor refuses) for
                // ~10-20 s; map/camera are not readable meanwhile (stale pointers fail ReadProcessMemory).
                stable = 0;
                after = null;
                left = true;
            }
        }
        var generateStillShown =
            Norm(
                string.Join(
                    " ",
                    ReadRect(game, ControlRect(target, generate.Name, w, h)).Select(l => l.Text)
                )
            ) == Norm(generate.Label ?? "");
        var settled = stable >= 2 || (!left && after is not null);
        int? livePlayerCount = settled ? PlayerCount(game) : null; // Loading: player objects not safely readable.
        var playersMatch = players is { } want && livePlayerCount == want + 1; // Player array includes Gaia.
        if (!changed && (generateStillShown || (players is not null && !playersMatch)))
            throw new WorkflowFailure(
                "NEW_SCENARIO_NOT_OBSERVED",
                "verify",
                $"No new scenario observed after {generate.Label} (dialog still shown: {generateStillShown}; live players {livePlayerCount}).",
                true,
                true,
                "Screenshot: a confirmation dialog may be open. Do not retry blindly; inspect map_info."
            );
        return new
        {
            type,
            players,
            seed,
            size,
            uiKind = kind,
            uiDetection = detection,
            steps,
            before = new { tilesX = before.Tiles[0], tilesZ = before.Tiles[1] },
            after = after is null
                ? null
                : new
                {
                    tilesX = after.Tiles[0],
                    tilesZ = after.Tiles[1],
                    tileSize = after.Scale,
                },
            livePlayerCount,
            dialogStillShown = generateStillShown,
            leftEditorWhileLoading = left,
            cameraReset,
            settled,
            verified = changed || playersMatch,
            limitation = "Drives File > New Scenario (catalog geometry per UI kind; normal UI live-tested 2026-10-21, alternative UI untested): type/players by dropdown OCR scan, seed typed (never TAB/ENTER), size radio (normal) or dropdown (alternative), then Generate/Create. Replaces the open scenario: unsaved work is lost (save a checkpoint first). livePlayerCount is the live player array length (includes Gaia). verified = new terrain object/dims, editor mode left while loading (random maps), camera reset, or live player count = players + 1; a Blank of the same size/players can be indistinguishable (verified false, Generate still clicked). Not RMS content.",
        };
    }
}
