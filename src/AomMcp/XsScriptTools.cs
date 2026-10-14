using System.Text.Json;

namespace AomMcp;

/// <summary>editor_xs_scripts: read and edit the scenario's Triggers -> XS Scripts list through the Script Manager
/// dialog (plan C3), plus the editor_triggers apply scripts="restore" wrapper.</summary>
/// <remarks>Live findings 2026-10-13 (normal UI, 2560x1440, research/WORKFLOW-LIVE-OBSERVATIONS.md "XS Scripts list"):
/// Include Script accepts any text (a nonexistent path was listed), a row click selects that row (no visible highlight),
/// Remove Script removes only the selected row, OCR reads a backslash as "l".</remarks>
internal sealed partial class Server
{
    /// <summary>Tool name.</summary>
    const string XsScriptsTool = "editor_xs_scripts";

    /// <summary>editor_triggers apply scripts mode that re-includes the list (<see cref="EditorFiles.TriggerScriptModes"/>).</summary>
    const string RestoreScripts = "restore";

    /// <summary>Operations accepted by editor_xs_scripts.</summary>
    static readonly string[] XsScriptOperations = ["list", "verify", "include", "remove"];

    /// <summary>Script Manager open name and its controls (generated/dialogs.json, ui_scripts_dlg.xml).</summary>
    const string ScriptGadget = "ScriptManager",
        ScriptNameControl = "scriptsNameEdit",
        ScriptIncludeControl = "scriptsIns",
        ScriptListControl = "scriptsList",
        ScriptRemoveControl = "scriptsDel";

    /// <summary>editor_dialog_state geometry mode of an open default-position dialog (pixel actions need it).</summary>
    const string NominalGeometry = "nominal";

    /// <summary>uiKind of the alternative editor UI (editor_dialog_state).</summary>
    const string AlternativeUi = "alternative";

    /// <summary>Alternative UI: button that opens the "New Script Name" popup (child gadget Add-dialog; ui_scripts_dlg.xml).</summary>
    const string ScriptAddControl = "scriptsInsert";

    /// <summary>Alternative UI: popup caption control and its text (ui_scripts_dlg.xml NewName-TXT, label "New Script Name").</summary>
    const string AltPopupCaptionControl = "NewName-TXT",
        AltPopupCaption = "New Script Name";

    /// <summary>Alternative UI: height of the "Scripts list" caption inside the list control rect, in client pixels
    /// (live 2560x1440, 2026-10-14: control y 206, first row text at y about 279, list body from y 246).</summary>
    const int AltListHeaderPx = 41;

    /// <summary>Script list row pitch in client pixels (live: rows at y 386 and 417 on 2560x1440).</summary>
    const int ScriptRowPitch = 31;

    /// <summary>Maximum names per include/verify call.</summary>
    const int MaxScriptNames = 32;

    /// <summary>XS file extension required for list entries.</summary>
    const string XsExtension = ".xs";

    /// <summary>Delay after a list mutation before the readback (host frame settle, not an engine guarantee).</summary>
    const int ScriptSettleMs = 250;

    /// <summary>Limitation text for list reads.</summary>
    const string ScriptListLimitation =
        "Names come from OCR of the Script Manager list (a backslash often reads as 'l'); matching treats path separators as OCR-ambiguous. "
        + "OCR is evidence, not proof: the saved scenario (editor_xs_check scenarioPath=\"latest\") stays authoritative. Rows beyond the visible list (scrolling) are not read.";

    /// <summary>editor_xs_scripts tool spec (core).</summary>
    static object XsScriptsSpec() =>
        Spec(
            XsScriptsTool,
            "Read or edit the open scenario's Triggers -> XS Scripts list through the Script Manager dialog (opened if needed and closed again unless keepOpen). "
                + "operation=list (OCR rows) | verify (names = the complete expected list; verified=true only when each name matches exactly one row and nothing else is listed) | "
                + "include (types each missing name into the name field, clicks Include Script, verifies one new matching row; already listed names are skipped; result.resolution says whether the name resolves under the profile trigger\\ folder, an enabled mod's trigger\\ folder (mods\\myth-mod-status.json) or the stock archive; confirmDestructive=true) | "
                + "remove (exactly one name; clicks its row, then Remove Script, verifies the list lost only that row; confirmDestructive=true). "
                + "Names are relative .xs paths as the editor lists them, e.g. om10\\om10_diffbonus.xs. The editor accepts names that do not exist. Changes stay in the open scenario until it is saved. "
                + "Live-verified on the normal UI at 2560x1440 and on the alternative UI at 2560x1440 (list, include, remove, verify; result.list.uiKind says which). The alternative UI has no name field: include presses Add Script, types into its 'New Script Name' popup and clicks the confirm button, and its list clips long names, so rows match by their start and verify reports prefixMatched instead of verified. "
                + "Other client sizes and moved windows are refused before any input (re-add by hand there); 1920x1080 is untested. "
                + "OCR is evidence (a backslash often reads as 'l'); after saving, editor_xs_check scenarioPath=\"latest\" is authoritative. editor_triggers apply scripts=\"restore\" uses this to undo the import's list reset. Failures carry code/nativeDispatched/outcomeUnknown/nextAction; never retry blindly.",
            new Dictionary<string, object>
            {
                ["operation"] = new { type = "string", @enum = XsScriptOperations },
                ["names"] = new
                {
                    type = "array",
                    items = new { type = "string", maxLength = DialogState.MaxText },
                    maxItems = MaxScriptNames,
                },
                ["keepOpen"] = new { type = "boolean" },
                ["confirmDestructive"] = new { type = "boolean" },
            },
            XsScriptRequired,
            false,
            true
        );

    /// <summary>Required editor_xs_scripts arguments.</summary>
    static readonly string[] XsScriptRequired = ["operation"];

    /// <summary>Validates editor_xs_scripts arguments before any game connection.</summary>
    /// <param name="args">Schema-validated arguments.</param>
    static void PreflightXsScripts(JsonElement args)
    {
        var operation = String(args, "operation");
        var names = XsNameArgs(args);
        if (operation is "include" or "remove")
        {
            EditorFiles.Confirm(args, "confirmDestructive");
            if (names.Length == 0)
                throw new ArgumentException($"operation={operation} requires names.");
        }
        if (operation == "remove" && names.Length != 1)
            throw new ArgumentException("operation=remove takes exactly one name.");
        if (operation == "verify" && !args.TryGetProperty("names", out _))
            throw new ArgumentException(
                "operation=verify requires names (the complete expected list, may be empty)."
            );
    }

    /// <summary>Validated names argument (relative .xs paths, no quotes/control characters/parent segments).</summary>
    /// <param name="args">Arguments.</param>
    static string[] XsNameArgs(JsonElement args)
    {
        if (!args.TryGetProperty("names", out var list))
            return [];
        var names = list.EnumerateArray().Select(n => n.GetString()!.Trim()).ToArray();
        if (names.Length > MaxScriptNames)
            throw new ArgumentException($"At most {MaxScriptNames} names.");
        foreach (var name in names)
            if (
                name.Length is 0 or > DialogState.MaxText
                || !name.EndsWith(XsExtension, StringComparison.OrdinalIgnoreCase)
                || name.Any(c => char.IsControl(c) || c is '"' or ':')
                || Path.IsPathRooted(name)
                || name.Replace('/', '\\').Split('\\').Any(p => p is ".." or "")
            )
                throw new ArgumentException(
                    $"Script name '{name}' must be a relative .xs path below a trigger root (e.g. om10\\om10_diffbonus.xs), no quotes/drive/'..'."
                );
        return names;
    }

    /// <summary>One OCR'd list row.</summary>
    /// <param name="Text">OCR text.</param>
    /// <param name="X">Client X of the text center.</param>
    /// <param name="Y">Client Y of the text center.</param>
    sealed record ScriptRow(string Text, int X, int Y);

    /// <summary>Live Script Manager placement for one read.</summary>
    /// <param name="UiKind">Detected UI kind.</param>
    /// <param name="List">Client rect of the list control.</param>
    /// <param name="Rows">OCR'd rows top to bottom.</param>
    /// <param name="MayBeTruncated">True when the rows fill the visible list (more may be scrolled out of view).</param>
    /// <param name="Clipped">True when row text is clipped by the list column (alternative UI), so names match by start only.</param>
    sealed record ScriptListRead(
        string UiKind,
        int[] List,
        ScriptRow[] Rows,
        bool MayBeTruncated,
        bool Clipped
    );

    /// <summary>Runs editor_dialog_state/act in-process with the same guards as the public tools.</summary>
    /// <param name="game">Validated editor connection.</param>
    /// <param name="act">True for editor_dialog_act, false for editor_dialog_state.</param>
    /// <param name="args">Tool arguments.</param>
    JsonElement Dialog(Game game, bool act, object args)
    {
        _dialogSignatures ??= DialogState.LoadAll();
        var element = JsonSerializer.SerializeToElement(args);
        var metadata = DialogMetadata();
        object result;
        if (act)
        {
            DialogState.Preflight(element);
            var native = new DialogState.Native(
                _catalog.Commands,
                expression => _bridge.Execute(game, expression),
                () => RequireNoCursor(game)
            );
            result = DialogState.Act(
                game,
                _layout.ExeSha256,
                metadata,
                _dialogSignatures,
                native,
                element
            );
        }
        else
            result = DialogState.State(
                game,
                _layout.ExeSha256,
                metadata,
                _dialogSignatures,
                element
            );
        return JsonSerializer.SerializeToElement(result);
    }

    /// <summary>Script Manager entry of an editor_dialog_state result, or null when not detected open.</summary>
    /// <param name="state">editor_dialog_state result.</param>
    static JsonElement? ScriptDialog(JsonElement state) =>
        state.TryGetProperty("dialogs", out var dialogs)
            ? dialogs
                .EnumerateArray()
                .Where(d =>
                    d.GetProperty("gadget").GetString() == ScriptGadget
                    && d.GetProperty("state").GetString() == "open"
                )
                .Select(d => (JsonElement?)d)
                .FirstOrDefault()
            : null;

    /// <summary>Client rect of a named control in a dialog entry, or null.</summary>
    /// <param name="dialog">Dialog entry with controls.</param>
    /// <param name="control">Control name.</param>
    /// <param name="includeHidden">Also accept controls hidden by default (children of a popup that is open now).</param>
    static int[]? ControlRect(JsonElement dialog, string control, bool includeHidden = false) =>
        dialog.TryGetProperty("controls", out var controls)
            ? controls
                .EnumerateArray()
                .Where(c =>
                    c.GetProperty("name").GetString() == control
                    && c.TryGetProperty("rect", out var r)
                    && r.ValueKind == JsonValueKind.Array
                    && (
                        includeHidden
                        || !c.TryGetProperty("defaultHidden", out var h)
                        || h.ValueKind != JsonValueKind.True
                    )
                )
                .Select(c =>
                    c.GetProperty("rect").EnumerateArray().Select(v => v.GetInt32()).ToArray()
                )
                .FirstOrDefault()
            : null;

    /// <summary>Opens the Script Manager when needed.</summary>
    /// <param name="game">Validated editor connection.</param>
    /// <returns>True when this call opened it.</returns>
    bool OpenScripts(Game game)
    {
        if (ScriptDialog(Dialog(game, false, new { gadget = ScriptGadget })) is not null)
            return false;
        Dialog(game, true, new { gadget = ScriptGadget, action = "open" });
        return true;
    }

    /// <summary>Reads the list of an open Script Manager (refuses unless its geometry is calibrated and fitted).</summary>
    /// <param name="game">Validated editor connection.</param>
    ScriptListRead ReadScripts(Game game)
    {
        var state = Dialog(game, false, new { gadget = ScriptGadget, controls = true });
        var uiKind = state.TryGetProperty("uiKind", out var k)
            ? k.GetString() ?? "unknown"
            : "unknown";
        var dialog = ScriptDialog(state);
        var list = dialog is { } d ? ControlRect(d, ScriptListControl) : null;
        var nominal =
            dialog is { } fd
            && fd.TryGetProperty("geometry", out var g)
            && g.TryGetProperty("mode", out var m)
            && m.GetString() == NominalGeometry;
        if (list is null || !nominal)
            throw new WorkflowFailure(
                "UI_LAYOUT_UNREVIEWED",
                "ui-preflight",
                $"Script Manager not detected at calibrated geometry (uiKind={uiKind}). Dialog signatures exist for the normal and alternative UI at 2560x1440 only; "
                    + "other client sizes and moved windows are refused. No input sent.",
                false,
                false,
                "Close and reopen the dialog at its default position, or use a 2560x1440 client. Otherwise edit Triggers -> XS Scripts by hand (editor_dialog_act open gadget=ScriptManager + editor_screenshot), then verify with editor_xs_check scenarioPath=\"latest\" after saving."
            );
        var alternative = uiKind == AlternativeUi;
        if (alternative)
            list = [list[0], list[1] + AltListHeaderPx, list[2], list[3] - AltListHeaderPx];
        var rows = UiRead
            .Lines(game, list)
            .Where(l => l.Text.Trim().Length > 0)
            .OrderBy(l => l.Y)
            .Select(l => new ScriptRow(l.Text.Trim(), l.X, l.Y))
            .ToArray();
        return new ScriptListRead(
            uiKind,
            list,
            rows,
            rows.Length >= list[3] / ScriptRowPitch,
            alternative
        );
    }

    /// <summary>Rows matching a name (separator-tolerant OCR comparison).</summary>
    /// <param name="read">List read.</param>
    /// <param name="name">Expected name.</param>
    static ScriptRow[] Matching(ScriptListRead read, string name) =>
        [.. read.Rows.Where(r => RowMatches(read, name, r))];

    /// <summary>Whether a row reads as the name: whole text, or (clipped rows) its start.</summary>
    /// <param name="read">List read the row came from.</param>
    /// <param name="name">Expected name.</param>
    /// <param name="row">One row.</param>
    static bool RowMatches(ScriptListRead read, string name, ScriptRow row) =>
        read.Clipped
            ? DialogState.StartsText(name, row.Text)
            : DialogState.SameText(name, row.Text);

    /// <summary>JSON-friendly list read.</summary>
    /// <param name="read">List read.</param>
    static object ListJson(ScriptListRead read) =>
        new
        {
            uiKind = read.UiKind,
            rows = read.Rows.Select(r => r.Text).ToArray(),
            mayBeTruncated = read.MayBeTruncated,
            rowsClipped = read.Clipped,
        };

    /// <summary>Where a trigger script name resolves for the editor: profile trigger\, enabled mods' trigger\ (registry order), stock archive.</summary>
    /// <param name="name">Relative script name.</param>
    object ScriptResolution(string name)
    {
        var roots = ScenarioDependencies.ProfileRoots(exe);
        var relative = name.Replace('/', '\\');
        if (roots.ProfileTrigger is { } profile && File.Exists(Path.Combine(profile, relative)))
            return new
            {
                resolved = true,
                from = "profile",
                path = Path.Combine(profile, relative),
            };
        foreach (var mod in ScenarioDependencies.EnabledModTriggers())
        {
            var path = Path.Combine(mod.Trigger, relative);
            if (File.Exists(path))
                return new
                {
                    resolved = true,
                    from = "mod",
                    path,
                    mod = mod.Title,
                    note = "Found under an enabled mod's trigger\\ folder; a playtest compiled such an include (live 2026-10-13). Precedence over profile/stock copies is unverified.",
                };
        }
        if (roots.StockTrigger(relative))
            return new
            {
                resolved = true,
                from = "stock",
                path = (string?)null,
            };
        return new
        {
            resolved = false,
            from = "missing",
            path = (string?)null,
            note = "Not under the profile trigger\\ folder, an enabled mod's trigger\\ folder or the stock archive. The editor accepts any name, but Play then fails with a Trigger Error dialog (XS Error 0123: failed to open file; live 2026-10-13).",
        };
    }

    /// <summary>Alternative UI: opens the "New Script Name" popup (press scriptsInsert), checks it by OCR, types the name and
    /// clicks its confirm button. The popup's controls are hidden by default, so the generic dialog click/setText refuse them;
    /// this uses the mapped control rects of the open nominal dialog (live 2560x1440, 2026-10-14) and verifies by OCR only the
    /// popup caption and that the visible part of the field lies inside the typed name. Never presses TAB/ENTER.</summary>
    /// <param name="game">Validated editor connection.</param>
    /// <param name="name">Exact name to include.</param>
    void AlternativeAddName(Game game, string name)
    {
        Dialog(
            game,
            true,
            new
            {
                gadget = ScriptGadget,
                action = "press",
                control = ScriptAddControl,
                confirmDestructive = true,
            }
        );
        Thread.Sleep(ScriptSettleMs);
        var state = Dialog(game, false, new { gadget = ScriptGadget, controls = true });
        var dialog = ScriptDialog(state);
        var caption = dialog is { } c1 ? ControlRect(c1, AltPopupCaptionControl, true) : null;
        var field = dialog is { } c2 ? ControlRect(c2, ScriptNameControl, true) : null;
        var confirm = dialog is { } c3 ? ControlRect(c3, ScriptIncludeControl, true) : null;
        if (caption is null || field is null || confirm is null)
            throw new WorkflowFailure(
                "UI_LAYOUT_UNREVIEWED",
                "ui-preflight",
                "Script Manager popup controls were not located; no text typed.",
                false,
                false,
                "Screenshot the Script Manager; add the name by hand."
            );
        if (
            !DialogState
                .Norm(UiRead.Text(game, caption))
                .Contains(DialogState.Norm(AltPopupCaption), StringComparison.Ordinal)
        )
            throw new WorkflowFailure(
                "POPUP_NOT_OPEN",
                "ui-preflight",
                $"The '{AltPopupCaption}' popup was not read at {string.Join(",", caption)} after pressing Add Script; no text typed.",
                false,
                false,
                "Screenshot the Script Manager; no input beyond the Add Script press was sent."
            );
        if (DialogState.Norm(UiRead.Text(game, field)).Length > 0)
            throw new WorkflowFailure(
                "POPUP_FIELD_NOT_EMPTY",
                "ui-preflight",
                "The popup's name field is not empty; no text typed.",
                false,
                false,
                "Screenshot the Script Manager and clear the field by hand."
            );
        RequireNoCursor(game);
        Ui.SettledClick(game, field[0] + field[2] / 2, field[1] + field[3] / 2, "left");
        Thread.Sleep(ScriptSettleMs);
        Ui.Text(game, name);
        Thread.Sleep(ScriptSettleMs);
        var tail = UiRead.Text(game, field);
        if (!DialogState.InsideText(name, tail))
            throw new WorkflowFailure(
                "TEXT_NOT_VERIFIED",
                "verify",
                $"Typed '{name}' into the popup name field but OCR reads '{tail}' (not part of the typed name); the confirm button was not clicked.",
                true,
                true,
                "Screenshot the popup; it may still be open with the text in it. Do not retype blindly."
            );
        Ui.SettledClick(game, confirm[0] + confirm[2] / 2, confirm[1] + confirm[3] / 2, "left");
    }

    /// <summary>Alternative UI: clicks the icon-only Remove Script button at the center of its mapped control rect (the generic
    /// click refuses unlabeled buttons without OCR anchors). Only after the dialog scored open at nominal geometry (the state read).</summary>
    /// <param name="game">Validated editor connection.</param>
    void AlternativeClickRemove(Game game)
    {
        var state = Dialog(game, false, new { gadget = ScriptGadget, controls = true });
        var rect = ScriptDialog(state) is { } dialog
            ? ControlRect(dialog, ScriptRemoveControl)
            : null;
        if (rect is null)
            throw new WorkflowFailure(
                "UI_LAYOUT_UNREVIEWED",
                "ui-preflight",
                "Remove Script button rect not located after selecting the row; the button was not clicked.",
                false,
                false,
                "Screenshot the Script Manager; remove the row by hand."
            );
        RequireNoCursor(game);
        Ui.SettledClick(game, rect[0] + rect[2] / 2, rect[1] + rect[3] / 2, "left");
        ParkPointer(game, state);
    }

    /// <summary>Moves the pointer to the middle of the client area (terrain) and waits, so a button tooltip does not cover the
    /// list (live 2026-10-14: the "Remove Script" tooltip stays over the list while the pointer rests on the button).</summary>
    /// <param name="game">Validated editor connection.</param>
    /// <param name="state">editor_dialog_state result with the client size.</param>
    static void ParkPointer(Game game, JsonElement state)
    {
        var client = state.GetProperty("client");
        game.Move(
            client.GetProperty("width").GetInt32() / 2,
            client.GetProperty("height").GetInt32() / 2
        );
        Thread.Sleep(ScriptSettleMs);
    }

    /// <summary>Includes names missing from the open list; verifies each by readback.</summary>
    /// <param name="game">Validated editor connection.</param>
    /// <param name="names">Exact names to include.</param>
    List<object> IncludeScripts(Game game, IEnumerable<string> names)
    {
        var steps = new List<object>();
        foreach (var name in names)
        {
            var before = ReadScripts(game);
            if (Matching(before, name).Length > 0)
            {
                steps.Add(
                    new
                    {
                        name,
                        action = "alreadyListed",
                        verified = true,
                        fullNameVerified = !before.Clipped,
                    }
                );
                continue;
            }
            if (before.UiKind == AlternativeUi)
                AlternativeAddName(game, name);
            else
            {
                Dialog(
                    game,
                    true,
                    new
                    {
                        gadget = ScriptGadget,
                        action = "setText",
                        control = ScriptNameControl,
                        text = name,
                        confirmDestructive = true,
                    }
                );
                Dialog(
                    game,
                    true,
                    new
                    {
                        gadget = ScriptGadget,
                        action = "click",
                        control = ScriptIncludeControl,
                        confirmDestructive = true,
                    }
                );
            }
            Thread.Sleep(ScriptSettleMs);
            var after = ReadScripts(game);
            var added =
                Matching(after, name).Length > 0 && after.Rows.Length == before.Rows.Length + 1;
            if (!added)
                throw new WorkflowFailure(
                    "INCLUDE_NOT_VERIFIED",
                    "verify",
                    $"Clicked Include Script for '{name}' but the list readback does not show exactly one new matching row (before {before.Rows.Length}, after {after.Rows.Length}: {string.Join(" | ", after.Rows.Select(r => r.Text))}).",
                    true,
                    true,
                    "Screenshot the Script Manager; do not include again blindly (duplicates are possible). Remove extras with operation=remove."
                );
            steps.Add(
                new
                {
                    name,
                    action = "included",
                    verified = true,
                    fullNameVerified = !after.Clipped,
                    rows = after.Rows.Length,
                }
            );
        }
        return steps;
    }

    /// <summary>Closes the Script Manager if this call opened it.</summary>
    /// <param name="game">Validated editor connection.</param>
    /// <param name="opened">Whether this call opened it.</param>
    /// <param name="keepOpen">Caller asked to keep it open.</param>
    void CloseScripts(Game game, bool opened, bool keepOpen)
    {
        if (opened && !keepOpen)
            Dialog(game, true, new { gadget = ScriptGadget, action = "close" });
    }

    /// <summary>editor_xs_scripts.</summary>
    /// <param name="args">Preflighted arguments.</param>
    /// <param name="batchGame">Shared batch connection, if any.</param>
    object InvokeXsScripts(JsonElement args, Game? batchGame)
    {
        using var owned = batchGame is null ? new Game(exe, _layout, pid) : null;
        var game = batchGame ?? owned!;
        _ = game.Editor();
        var operation = String(args, "operation");
        var names = XsNameArgs(args);
        var keepOpen = args.TryGetProperty("keepOpen", out var ko) && ko.GetBoolean();
        var opened = OpenScripts(game);
        try
        {
            switch (operation)
            {
                case "list":
                case "verify":
                {
                    var read = ReadScripts(game);
                    var expected = names
                        .Select(n => new { name = n, rows = Matching(read, n).Length })
                        .ToArray();
                    var unexpected = read
                        .Rows.Where(r => !names.Any(n => RowMatches(read, n, r)))
                        .Select(r => r.Text)
                        .ToArray();
                    var ok =
                        expected.All(e => e.rows == 1)
                        && unexpected.Length == 0
                        && !read.MayBeTruncated;
                    return new
                    {
                        list = ListJson(read),
                        expected = operation == "verify" || names.Length > 0 ? expected : null,
                        unexpected = operation == "verify" ? unexpected : null,
                        verified = operation == "verify" ? ok && !read.Clipped : (bool?)null,
                        prefixMatched = operation == "verify" && read.Clipped ? ok : (bool?)null,
                        openedHere = opened,
                        limitation = ScriptListLimitation,
                    };
                }
                case "include":
                {
                    var resolution = names.ToDictionary(n => n, ScriptResolution);
                    var steps = IncludeScripts(game, names);
                    return new
                    {
                        steps,
                        resolution,
                        list = ListJson(ReadScripts(game)),
                        openedHere = opened,
                        note = "List changed in the open scenario only; save the scenario to keep it. The editor accepts names that do not resolve (see resolution).",
                        limitation = ScriptListLimitation,
                    };
                }
                default:
                {
                    var name = names[0];
                    var before = ReadScripts(game);
                    var rows = Matching(before, name);
                    if (rows.Length != 1)
                        throw new WorkflowFailure(
                            rows.Length == 0 ? "SCRIPT_NOT_LISTED" : "SCRIPT_AMBIGUOUS",
                            "ui-preflight",
                            $"'{name}' matches {rows.Length} list rows ({string.Join(" | ", before.Rows.Select(r => r.Text))}); no input sent.",
                            false,
                            false,
                            "Check the list with operation=list; use the exact name of one row."
                        );
                    RequireNoCursor(game);
                    Ui.Click(game, rows[0].X, rows[0].Y, "left");
                    if (before.UiKind == AlternativeUi)
                        AlternativeClickRemove(game);
                    else
                        Dialog(
                            game,
                            true,
                            new
                            {
                                gadget = ScriptGadget,
                                action = "click",
                                control = ScriptRemoveControl,
                                confirmDestructive = true,
                            }
                        );
                    Thread.Sleep(ScriptSettleMs);
                    var after = ReadScripts(game);
                    var removed =
                        Matching(after, name).Length == 0
                        && after.Rows.Length == before.Rows.Length - 1
                        && before
                            .Rows.Where(r => r != rows[0])
                            .Select(r => DialogState.Norm(r.Text))
                            .SequenceEqual(after.Rows.Select(r => DialogState.Norm(r.Text)));
                    if (!removed)
                        throw new WorkflowFailure(
                            "REMOVE_NOT_VERIFIED",
                            "verify",
                            $"Clicked row '{rows[0].Text}' and Remove Script, but the readback is not the old list minus that row (after: {string.Join(" | ", after.Rows.Select(r => r.Text))}).",
                            true,
                            true,
                            "Screenshot the Script Manager and compare; re-include a wrongly removed name with operation=include. Do not repeat blindly."
                        );
                    return new
                    {
                        removed = name,
                        verified = true,
                        list = ListJson(after),
                        openedHere = opened,
                        note = "List changed in the open scenario only; save the scenario to keep it.",
                        limitation = ScriptListLimitation,
                    };
                }
            }
        }
        finally
        {
            CloseScripts(game, opened, keepOpen);
        }
    }

    /// <summary>editor_triggers apply with scripts="restore": snapshot the live list, apply, re-include the exact
    /// lastKnown names that matched a snapshot row, verify the list.</summary>
    /// <param name="game">Validated editor connection.</param>
    /// <param name="args">Preflighted apply arguments.</param>
    /// <param name="apply">The guarded apply itself.</param>
    object ApplyWithScriptRestore(Game game, JsonElement args, Func<object> apply)
    {
        var trigger = EditorFiles.ProfileDirectory(args, "trigger");
        var (known, _) = XsScripts.LastKnown(trigger);
        var opened = OpenScripts(game);
        ScriptListRead snapshot;
        try
        {
            snapshot = ReadScripts(game);
        }
        finally
        {
            CloseScripts(game, opened, false);
        }
        // Only exact source strings are re-typed; OCR text is never typed back.
        var restore = known
            .Where(k => Matching(snapshot, k.Name).Length > 0)
            .Select(k => k.Name)
            .ToArray();
        var unrestorable = snapshot
            .Rows.Where(r => !known.Any(k => RowMatches(snapshot, k.Name, r)))
            .Select(r => r.Text)
            .ToArray();
        var applied = apply();
        object scripts;
        if (restore.Length == 0)
            scripts = new
            {
                snapshot = ListJson(snapshot),
                restored = Array.Empty<object>(),
                unrestorable,
                verified = unrestorable.Length == 0 && !snapshot.MayBeTruncated,
                prefixOnly = snapshot.Clipped,
            };
        else
        {
            var reopened = OpenScripts(game);
            try
            {
                var steps = IncludeScripts(game, restore);
                var final = ReadScripts(game);
                scripts = new
                {
                    snapshot = ListJson(snapshot),
                    restored = steps,
                    unrestorable,
                    list = ListJson(final),
                    verified = unrestorable.Length == 0
                        && !snapshot.MayBeTruncated
                        && restore.All(n => Matching(final, n).Length == 1)
                        && !final.Clipped,
                    prefixMatched = final.Clipped
                        && unrestorable.Length == 0
                        && !snapshot.MayBeTruncated
                        && restore.All(n => Matching(final, n).Length == 1),
                };
            }
            finally
            {
                CloseScripts(game, reopened, false);
            }
        }
        return new
        {
            apply = applied,
            scriptRestore = scripts,
            note = "Order follows lastKnown, not necessarily the original list order. unrestorable = snapshot rows without an exact source name (trigtemp.xs includes / newest profile save): re-add those by hand. Save the scenario to keep the list.",
        };
    }
}
