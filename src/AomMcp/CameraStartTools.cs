using System.Numerics;
using System.Text.Json;

namespace AomMcp;

/// <summary>editor_camera_start: per-player camera start (Camera Start Location dialog, CameraStartDialog). Session 4 live
/// research (normal UI 1920x1080, 2026-10-29): Set stores the current camera look point for the dialog's Target Player,
/// Delete clears it, Show moves the camera to it; native uiSetCameraStartLoc()/uiDeleteCameraStartLoc() changed no start
/// record, so the dialog buttons are clicked. The checkpoint keeps the start at the PL player block P9 tail
/// (<see cref="ScenarioReader.CameraStart"/>).</summary>
internal sealed partial class Server
{
    /// <summary>Tool name.</summary>
    internal const string CameraStartTool = "editor_camera_start";

    /// <summary>Signature gadget of the Camera Start Location dialog (uilayouts/dialogs-*.json).</summary>
    const string CameraStartGadget = "CameraStartDialog";

    /// <summary>Operations.</summary>
    static readonly string[] CameraStartOperations = ["get", "show", "set", "delete"];

    /// <summary>Required arguments.</summary>
    static readonly string[] CameraStartRequired = ["operation"];

    /// <summary>Control name suffixes (editor_dialogs catalog: CameraStartDlg-Player/-SetBtn/-ShowBtn/-DeleteBtn).</summary>
    const string PlayerControlSuffix = "-Player";

    /// <summary>Set button suffix.</summary>
    const string SetControlSuffix = "-SetBtn";

    /// <summary>Show button suffix.</summary>
    const string ShowControlSuffix = "-ShowBtn";

    /// <summary>Delete button suffix.</summary>
    const string DeleteControlSuffix = "-DeleteBtn";

    /// <summary>Default dropdown item prefix (live list 2026-10-29: "Player 1", "Player 2").</summary>
    const string PlayerItemPrefix = "Player ";

    /// <summary>Highest player number (PL player count bound of the editor, 12 players + Gaia).</summary>
    const int MaxStartPlayer = 12;

    /// <summary>Accepted Show-to-Set residual in world units (plan verification: residual ≤ 2).</summary>
    const double StartTolerance = 2;

    /// <summary>Camera movement (world units) that counts as Show having moved the camera (host choice).</summary>
    const double ShowMovedEpsilon = 1;

    /// <summary>Camera centring tolerance and click budget (same as the paint camera, WorldTools.Screen).</summary>
    const double StartLookTolerance = 2;

    /// <summary>Minimap click budget per look.</summary>
    const int StartLookClicks = 4;

    /// <summary>Map fractions of the probe poses before Show: a start within <see cref="ShowMovedEpsilon"/> of the first
    /// is far from the second.</summary>
    static readonly double[] ProbeFractions = [0.25, 0.75];

    /// <summary>Wait after opening the dropdown list or picking an item (host choice; list drawn next frame, live).</summary>
    const int StartDropdownSettleMs = 400;

    /// <summary>Dropdown list rows searched below the field: every player plus one spare.</summary>
    const int ListRows = MaxStartPlayer + 1;

    /// <summary>OCR region bounds of UiRead.Lines.</summary>
    const int MaxReadWidth = 850;

    /// <summary>OCR region height bound of UiRead.Lines.</summary>
    const int MaxReadHeight = 500;

    /// <summary>Spec (core toolset).</summary>
    static object CameraStartSpec() =>
        Spec(
            CameraStartTool,
            "Per-player camera start (where a playtest/game opens). operation=get (read-only, no UI): every player's stored start from a checkpoint (scenarioPath, default \"latest\"; PL player P9 record: set flag + x/y/z look point). operation=show|set|delete need player (1..12) and drive the Camera Start Location dialog (CameraStartDialog, pixel-verified, both UIs where its signatures are calibrated; uiKind reported): opens it, picks the Target Player in its dropdown by OCR (playerLabel overrides the default \"Player N\" item text, e.g. after renaming), then: show = moves the camera to a probe point, clicks Show and returns the start (hasStart=false when Show did not move the camera), camera restored; set = centres the camera on x/z (world units), clicks Set, verifies by Show (residual <= 2) and leaves the camera on the start; delete = clicks Delete, verified by Show no longer moving the camera. The dialog is closed afterwards (verified). set/delete need confirmDestructive=true. Save a checkpoint and use get to prove the stored record. Native uiSetCameraStartLoc/uiDeleteCameraStartLoc stored nothing in live tests; do not use them.",
            new Dictionary<string, object>
            {
                ["operation"] = new { type = "string", @enum = CameraStartOperations },
                ["player"] = new
                {
                    type = "integer",
                    minimum = 1,
                    maximum = MaxStartPlayer,
                },
                ["x"] = new { type = "number" },
                ["z"] = new { type = "number" },
                ["playerLabel"] = new { type = "string", maxLength = DialogState.MaxText },
                ["scenarioPath"] = new { type = "string" },
                ["expectedSha256"] = new { type = "string" },
                ["confirmDestructive"] = new { type = "boolean" },
            },
            CameraStartRequired,
            false,
            true,
            false
        );

    /// <summary>Argument checks before any game call.</summary>
    /// <param name="args">Arguments.</param>
    static void PreflightCameraStart(JsonElement args)
    {
        var operation = args.GetProperty("operation").GetString()!;
        if (operation == "get")
        {
            if (args.TryGetProperty("player", out _) || args.TryGetProperty("x", out _))
                throw new ArgumentException(
                    "get reads every player from a checkpoint; omit player/x/z."
                );
            if (
                args.TryGetProperty("scenarioPath", out var path)
                && path.GetString() is var requested
                && requested != "latest"
            )
                _ = EditorFiles.LocalPath(requested!);
            return;
        }
        if (
            args.TryGetProperty("scenarioPath", out _)
            || args.TryGetProperty("expectedSha256", out _)
        )
            throw new ArgumentException("scenarioPath/expectedSha256 belong to operation=get.");
        if (!args.TryGetProperty("player", out _))
            throw new ArgumentException(
                $"operation={operation} needs player (1..{MaxStartPlayer})."
            );
        var hasX = args.TryGetProperty("x", out var x);
        var hasZ = args.TryGetProperty("z", out var z);
        if (operation == "set")
        {
            if (
                !hasX
                || !hasZ
                || !double.IsFinite(x.GetDouble())
                || !double.IsFinite(z.GetDouble())
            )
                throw new ArgumentException("set needs finite world x and z.");
        }
        else if (hasX || hasZ)
            throw new ArgumentException("x/z belong to operation=set.");
        if (
            operation is "set" or "delete"
            && !(args.TryGetProperty("confirmDestructive", out var c) && c.GetBoolean())
        )
            throw new ArgumentException(
                $"{operation} changes the scenario's camera start; confirmDestructive=true required."
            );
    }

    /// <summary>Runs one camera start operation.</summary>
    /// <param name="args">Preflighted arguments.</param>
    /// <param name="batchGame">Batch connection, or null for an own one.</param>
    object InvokeCameraStart(JsonElement args, Game? batchGame)
    {
        var operation = args.GetProperty("operation").GetString()!;
        if (operation == "get")
            return CameraStartGet(args);
        _dialogSignatures ??= DialogState.LoadAll();
        var metadata = DialogMetadata();
        using var owned = batchGame is null ? new Game(exe, _layout, pid) : null;
        var game = batchGame ?? owned!;
        var native = new DialogState.Native(
            _catalog.Commands,
            expression => _bridge.Execute(game, expression),
            () => RequireNoCursor(game)
        );
        var sets = _dialogSignatures;
        JsonElement Act(string action, string? control = null)
        {
            var values = new Dictionary<string, object?>
            {
                ["gadget"] = CameraStartGadget,
                ["action"] = action,
            };
            if (control is not null)
            {
                values["control"] = control;
                values["confirmDestructive"] = true;
            }
            return JsonSerializer.SerializeToElement(
                DialogState.Act(
                    game,
                    _layout.ExeSha256,
                    metadata,
                    sets,
                    native,
                    DialogState.Json(values)
                )
            );
        }
        JsonElement State() =>
            JsonSerializer.SerializeToElement(
                DialogState.State(
                    game,
                    _layout.ExeSha256,
                    metadata,
                    sets,
                    DialogState.Json(
                        new Dictionary<string, object?>
                        {
                            ["gadget"] = CameraStartGadget,
                            ["read"] = true,
                        }
                    )
                )
            );
        var player = args.GetProperty("player").GetInt32();
        var label = args.TryGetProperty("playerLabel", out var l)
            ? l.GetString()!.Trim()
            : PlayerItemPrefix + player;
        var original = EditorView.ReadView(game).Target();
        object? look = null;
        Vector3? setAt = null;
        if (operation == "set")
        {
            look = CameraLookAt(
                game,
                args.GetProperty("x").GetDouble(),
                args.GetProperty("z").GetDouble(),
                StartLookTolerance,
                StartLookClicks
            );
            if (
                !JsonSerializer.SerializeToElement(look).GetProperty("withinTolerance").GetBoolean()
            )
                throw new WorkflowFailure(
                    "CAMERA_NOT_CENTRED",
                    "preflight",
                    $"Camera did not centre on ({args.GetProperty("x").GetDouble()}, {args.GetProperty("z").GetDouble()}) within {StartLookTolerance}: {JsonSerializer.Serialize(look)}",
                    false,
                    false,
                    "Pick a reachable point (map edges clamp the camera); no start changed."
                );
            setAt = EditorView.ReadView(game).Target();
        }
        var opened = Act("open");
        (JsonElement State, object Report) selection;
        JsonElement state;
        object? click = null;
        (bool HasStart, Vector3? Start, List<object> Tries) probe;
        bool verified;
        double? residual = null;
        string? restoreNote = null;
        try
        {
            selection = SelectStartPlayer(game, native, State, label);
            state = selection.State;
            string Button(string suffix) =>
                StartControl(state, suffix).GetProperty("name").GetString()!;
            if (operation == "set")
                click = Act("click", Button(SetControlSuffix));
            if (operation == "delete")
                click = Act("click", Button(DeleteControlSuffix));
            probe = ProbeStart(game, () => Act("click", Button(ShowControlSuffix)));
            switch (operation)
            {
                case "set":
                    residual = probe.Start is { } s && setAt is { } a ? Distance(s, a) : null;
                    verified = residual <= StartTolerance;
                    break;
                case "delete":
                    verified = !probe.HasStart;
                    if (original is { } back)
                        _ = CameraLookAt(game, back.X, back.Z, StartLookTolerance, StartLookClicks);
                    break;
                default:
                    verified = true;
                    if (original is { } o)
                    {
                        _ = CameraLookAt(game, o.X, o.Z, StartLookTolerance, StartLookClicks);
                        restoreNote = "Camera restored to its start pose target.";
                    }
                    break;
            }
        }
        catch (WorkflowFailure)
        {
            // Leave no dialog behind on a refusal (live 2026-10-29: PLAYER_NOT_LISTED on the normal UI and a
            // GEOMETRY_UNVERIFIED button press on the alternative UI both left it open).
            try
            {
                _ = Act("close");
            }
            catch (Exception e)
                when (e is WorkflowFailure or InvalidDataException or ArgumentException) { }
            throw;
        }
        var closed = Act("close");
        var result = new
        {
            operation,
            player,
            playerLabel = label,
            uiKind = state.GetProperty("uiKind").GetString(),
            verified,
            hasStart = probe.HasStart,
            start = probe.Start is { } p
                ? new
                {
                    x = p.X,
                    y = p.Y,
                    z = p.Z,
                }
                : null,
            requested = operation == "set"
                ? new
                {
                    x = args.GetProperty("x").GetDouble(),
                    z = args.GetProperty("z").GetDouble(),
                }
                : null,
            setAt = setAt is { } t
                ? new
                {
                    x = t.X,
                    y = t.Y,
                    z = t.Z,
                }
                : null,
            residual,
            tolerance = operation == "set" ? StartTolerance : (double?)null,
            dropdown = selection.Report,
            look,
            opened,
            click,
            probes = probe.Tries,
            closed,
            restoreNote,
            note = "Verified by the dialog's Show button moving the camera (pixel/camera evidence). Save a checkpoint and run operation=get to prove the stored record.",
        };
        if (!verified)
            throw new WorkflowFailure(
                "CAMERA_START_UNVERIFIED",
                "verify",
                $"{operation} for player {player} not confirmed by Show: {JsonSerializer.Serialize(result)}",
                true,
                false,
                "Inspect with editor_dialog_state gadget=CameraStartDialog and a checkpoint (operation=get); do not retry blindly."
            );
        return result;
    }

    /// <summary>Distance in the XZ plane.</summary>
    /// <param name="a">First point.</param>
    /// <param name="b">Second point.</param>
    static double Distance(Vector3 a, Vector3 b) =>
        Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Z - b.Z, 2));

    /// <summary>The dialog control whose catalog name ends with <paramref name="suffix"/>.</summary>
    /// <param name="state">editor_dialog_state result for the gadget.</param>
    /// <param name="suffix">Name suffix.</param>
    static JsonElement StartControl(JsonElement state, string suffix)
    {
        foreach (var dialog in state.GetProperty("dialogs").EnumerateArray())
        {
            if (dialog.GetProperty("gadget").GetString() != CameraStartGadget)
                continue;
            if (dialog.GetProperty("state").GetString() != "open")
                break;
            if (dialog.GetProperty("controls") is { ValueKind: JsonValueKind.Array } controls)
                foreach (var c in controls.EnumerateArray())
                    if (
                        c.GetProperty("name")
                            .GetString()!
                            .EndsWith(suffix, StringComparison.Ordinal)
                    )
                        return c;
        }
        throw new WorkflowFailure(
            "DIALOG_NOT_OPEN",
            "ui-observe",
            $"{CameraStartGadget} is not open with a *{suffix} control: {state.GetRawText()[..Math.Min(state.GetRawText().Length, MaxReadHeight)]}",
            false,
            false,
            "Inspect with editor_dialog_state gadget=CameraStartDialog; no further input sent."
        );
    }

    /// <summary>Picks <paramref name="label"/> in the Target Player dropdown: opens its list (arrow at the field's right
    /// end), OCRs the rows below the field and clicks the matching row (only an OCR-seen row is clicked, so a list that did
    /// not open never sends a click onto the map), then verifies the field text.</summary>
    /// <param name="game">Connected editor.</param>
    /// <param name="native">Cursor guard.</param>
    /// <param name="state">editor_dialog_state read=true for the gadget.</param>
    /// <param name="label">Item text.</param>
    static (JsonElement State, object Report) SelectStartPlayer(
        Game game,
        DialogState.Native native,
        Func<JsonElement> state,
        string label
    )
    {
        var before = state();
        var field = StartControl(before, PlayerControlSuffix);
        var text = field.GetProperty("text").GetString() ?? "";
        if (DialogState.SameText(label, text))
            return (before, new { before = text, clicked = false });
        var rect = field.GetProperty("rect").EnumerateArray().Select(v => v.GetInt32()).ToArray();
        native.RequireNoCursor();
        Ui.SettledClick(game, rect[0] + rect[2] - rect[3] / 2, rect[1] + rect[3] / 2, "left");
        Thread.Sleep(StartDropdownSettleMs);
        var capture = Ui.CapturePixels(game, Ui.FullResolutionWidth);
        var top = rect[1] + rect[3];
        int[] list =
        [
            rect[0],
            top,
            Math.Min(rect[2], MaxReadWidth),
            Math.Min(Math.Min(rect[3] * ListRows, MaxReadHeight), capture.Height - top),
        ];
        var lines = UiRead.Lines(capture, list).OrderBy(x => x.Y).ToArray();
        var rows = lines.Where(x => DialogState.SameText(label, x.Text)).ToArray();
        if (rows.Length != 1)
        {
            // Close the list again with its arrow (a toggle); nothing was picked.
            Ui.SettledClick(game, rect[0] + rect[2] - rect[3] / 2, rect[1] + rect[3] / 2, "left");
            throw new WorkflowFailure(
                "PLAYER_NOT_LISTED",
                "ui-observe",
                $"Target Player list shows [{string.Join(", ", lines.Select(x => x.Text))}]; {(rows.Length == 0 ? "no" : "several")} row(s) read '{label}'.",
                false,
                false,
                "Pass playerLabel with the row text (renamed players show their name); no start changed."
            );
        }
        Ui.SettledClick(game, rows[0].X, rows[0].Y, "left");
        Thread.Sleep(StartDropdownSettleMs);
        var after = state();
        var now = StartControl(after, PlayerControlSuffix).GetProperty("text").GetString() ?? "";
        if (!DialogState.SameText(label, now))
            throw new WorkflowFailure(
                "PLAYER_NOT_SELECTED",
                "verify",
                $"Target Player reads '{now}' after clicking row '{rows[0].Text}' at ({rows[0].X}, {rows[0].Y}).",
                true,
                false,
                "Inspect with editor_dialog_state gadget=CameraStartDialog read=true; no start changed."
            );
        return (
            after,
            new
            {
                before = text,
                clicked = true,
                row = new
                {
                    rows[0].Text,
                    rows[0].X,
                    rows[0].Y,
                },
                after = now,
            }
        );
    }

    /// <summary>Show probe: centres the camera on a probe pose, clicks Show and reads the camera; Show that moves the
    /// camera proves a stored start. A second pose rules out a start lying at the first one.</summary>
    /// <param name="game">Connected editor.</param>
    /// <param name="show">Clicks the Show button.</param>
    (bool HasStart, Vector3? Start, List<object> Tries) ProbeStart(
        Game game,
        Func<JsonElement> show
    )
    {
        var tries = new List<object>();
        foreach (var f in ProbeFractions)
        {
            var view = EditorView.ReadView(game);
            _ = CameraLookAt(
                game,
                view.WorldWidth * f,
                view.WorldDepth * f,
                StartLookTolerance,
                StartLookClicks
            );
            var before = EditorView.ReadView(game).Target();
            var clicked = show();
            var after = EditorView.ReadView(game).Target();
            var moved = before is { } b && after is { } a ? Distance(a, b) : (double?)null;
            tries.Add(
                new
                {
                    probe = before is { } pb ? new { x = pb.X, z = pb.Z } : null,
                    after = after is { } pa
                        ? new
                        {
                            x = pa.X,
                            y = pa.Y,
                            z = pa.Z,
                        }
                        : null,
                    moved,
                    labelVerified = clicked.TryGetProperty("labelVerified", out var v)
                        && v.GetBoolean(),
                }
            );
            if (moved > ShowMovedEpsilon)
                return (true, after, tries);
        }
        return (false, null, tries);
    }

    /// <summary>operation=get: every player's stored camera start from a checkpoint (read-only).</summary>
    /// <param name="args">Arguments.</param>
    static object CameraStartGet(JsonElement args)
    {
        var requested = args.TryGetProperty("scenarioPath", out var p) ? p.GetString()! : "latest";
        var doc = CheckpointDocument.Read(
            ScenarioFiles.Resolve(requested),
            CheckpointDocument.Expected(args)
        );
        var players = ScenarioReader.ReadPlayers(doc.World.One("PL").ToArray());
        return new
        {
            scenarioPath = doc.Path,
            sha256 = doc.Sha256,
            resolvedFrom = requested == "latest" ? "latest" : null,
            players = players.Select(x => new
            {
                player = x.Id,
                name = x.DisplayName,
                layoutReviewed = x.Camera is not null,
                hasStart = x.Camera?.Set,
                start = x.Camera is { Set: true } c
                    ? new
                    {
                        x = c.X,
                        y = c.Y,
                        z = c.Z,
                    }
                    : null,
            }),
            note = "Stored look point of the dialog's Set (the y/x/z may differ from the live camera target by the terrain/water height the engine used; Show restores the same camera). Player 0 is Gaia.",
        };
    }

    /// <summary>Offline checks: P9 camera start decoding (session 4 bytes) and preflight refusals.</summary>
    internal static void CameraStartSelfTest()
    {
        // P9 tail of Player 2 after Set at (200.39, 6.01, 300.37), cs6 checkpoint 2026-10-29.
        var body = new byte[ScenarioP9Length];
        byte[] tail = [1, 0xa3, 0x62, 0x48, 0x43, 0xc8, 0x5d, 0xc0, 0x40, 0xf8, 0x2e, 0x96, 0x43];
        tail.CopyTo(body, body.Length - tail.Length);
        var start = ScenarioReader.ReadCameraStart(body);
        if (
            start is not { Set: true }
            || Math.Abs(start.X - 200.39) > 0.01
            || Math.Abs(start.Z - 300.37) > 0.01
        )
            throw new InvalidOperationException(
                "Camera start P9 decode self-test failed: " + start
            );
        if (ScenarioReader.ReadCameraStart(new byte[ScenarioP9Length])?.Set != false)
            throw new InvalidOperationException("Camera start empty P9 self-test failed.");
        if (ScenarioReader.ReadCameraStart(new byte[ScenarioP9Length + 1]) is not null)
            throw new InvalidOperationException(
                "Camera start unreviewed P9 length self-test failed."
            );
        foreach (
            var bad in new[]
            {
                """{"operation":"set","player":1,"x":1,"z":2}""",
                """{"operation":"set","player":1,"x":1,"confirmDestructive":true}""",
                """{"operation":"delete","player":1}""",
                """{"operation":"show"}""",
                """{"operation":"show","player":1,"x":1}""",
                """{"operation":"get","player":1}""",
            }
        )
        {
            var refused = false;
            try
            {
                using var doc = JsonDocument.Parse(bad);
                PreflightCameraStart(doc.RootElement);
            }
            catch (ArgumentException)
            {
                refused = true;
            }
            if (!refused)
                throw new InvalidOperationException("Camera start preflight accepted " + bad);
        }
        using var ok = JsonDocument.Parse(
            """{"operation":"set","player":2,"x":190,"z":312,"confirmDestructive":true}"""
        );
        PreflightCameraStart(ok.RootElement);
    }

    /// <summary>Reviewed P9 length (mirror of ScenarioReader's reviewed layout, self-test fixture size).</summary>
    const int ScenarioP9Length = 59;
}
