using System.Text.Json;

namespace AomMcp;

/// <summary>Live dialog tools (editor_dialog_state / editor_dialog_act): see <see cref="DialogState"/>.</summary>
internal sealed partial class Server
{
    static readonly HashSet<string> DialogNames = new(StringComparer.Ordinal)
    {
        DialogState.StateTool,
        DialogState.ActTool,
    };
    static readonly string[] DialogActRequired = ["gadget", "action"];
    DialogState.SignatureSet[]? _dialogSignatures; // Shipped calibrated signatures; immutable per host.

    static void PreflightDialog(string name, JsonElement args)
    {
        if (name == DialogState.ActTool)
            DialogState.Preflight(args);
    }

    DialogState.Dialogs DialogMetadata()
    {
        try
        {
            var catalog = _dialogs ??= DialogCatalog.Load();
            return new(catalog.AllWindows, catalog.Freshness(exe, _layout.ExeSha256));
        }
        catch (Exception e)
            when (e
                    is FileNotFoundException
                        or JsonException
                        or InvalidDataException
                        or NotSupportedException
            )
        {
            return new([], "missing");
        }
    }

    object InvokeDialog(string name, JsonElement args, Game? batchGame)
    {
        _dialogSignatures ??= DialogState.LoadAll();
        var metadata = DialogMetadata();
        using var owned = batchGame is null ? new Game(exe, _layout, pid) : null;
        var game = batchGame ?? owned!;
        if (name == DialogState.StateTool)
            return DialogState.State(game, _layout.ExeSha256, metadata, _dialogSignatures, args);
        var native = new DialogState.Native(
            _catalog.Commands,
            expression => _bridge.Execute(game, expression),
            () => RequireNoCursor(game)
        );
        return DialogState.Act(game, _layout.ExeSha256, metadata, _dialogSignatures, native, args);
    }

    static IEnumerable<object> DialogExtras()
    {
        yield return Spec(
            DialogState.StateTool,
            "Which editor dialogs are open right now and what they show, without reading screenshots. Detects calibrated dialogs (normal UI, 2560x1440 reference; 38 gadgetReal names) by pixel signatures: state open/partial with score and client bounds, close name; closedCount for the rest. With gadget (exact open name, e.g. NewScenarioGadget, VictoryConditionsGadget, PlayerEditDlg) or controls=true: its controls from editor_dialogs metadata mapped to client rects (kind button/checkbox/textInput/dropdown/radio/list/slider/text, label, tooltip, script). read=true adds OCR: lines, per-control text (text inputs/dropdowns read per control at zoom 2 then 3, textSource control@Nx|control|dialogTiles, up to 32 per dialog, rest fieldReadsSkipped; values without their label or dropdown arrow, e.g. Seed -1), observedChecked for checkboxes (green tick pixel heuristic) and an OCR-fitted offset (geometry.mode nominal/approximate). OCR covers up to 3 open dialogs. offset/limit page controls (default 60). Moved windows, uncalibrated windows and the alternative UI (not yet calibrated) are not detected: use editor_dialogs + screenshot there. Observations only; checkpoints stay authoritative. Read-only.",
            new Dictionary<string, object>
            {
                ["gadget"] = new { type = "string" },
                ["read"] = new { type = "boolean" },
                ["controls"] = new { type = "boolean" },
                ["offset"] = new { type = "integer", minimum = 0 },
                ["limit"] = new
                {
                    type = "integer",
                    minimum = 1,
                    maximum = DialogState.MaxControlLimit,
                },
            },
            [],
            true
        );
        yield return Spec(
            DialogState.ActTool,
            "Operate an editor dialog by gadget/control name, verified by pixel signature/OCR. action=open (gadgetReal, polls until the signature shows; skipped if open) | close (gadgetUnreal with the catalog close name, then inner window names; verified closed, DIALOG_STILL_OPEN otherwise) | press (runs the control's shipped script as validated native calls; view-only gadget scripts and Close/Cancel need no confirmation, anything else confirmDestructive=true; controls without scripts: NO_SCRIPT, use click) | select (radio item by label/index or menu entry path via its script) | click (pixel click on a button/checkbox/text field of an open, default-position dialog after OCR label verification; confirmDestructive=true except Close/Cancel) | setText (click the field, END, BACKSPACEs, type text, OCR verify over up to 3 frames (framesRead; the caret can hide short text, keys are never re-sent); never TAB/ENTER/ESC; nothing is applied until the dialog's own OK/Generate; confirmDestructive=true) | toggle (checkbox script or click, before/after green-tick heuristic; confirmDestructive=true; some dialogs such as Victory Conditions apply immediately). control = exact control name or unique label (case/punctuation-insensitive). Pixel actions need the calibrated client size (2560x1440 normal UI) and refuse on occlusion/moved windows (no input sent). Failures carry code/nativeDispatched/outcomeUnknown/nextAction; never retry blindly.",
            new Dictionary<string, object>
            {
                ["gadget"] = new { type = "string" },
                ["action"] = new { type = "string", @enum = DialogState.Actions },
                ["control"] = new { type = "string" },
                ["item"] = new { type = "string" },
                ["text"] = new { type = "string", maxLength = DialogState.MaxText },
                ["confirmDestructive"] = new { type = "boolean" },
                ["timeout"] = new
                {
                    type = "integer",
                    minimum = 500,
                    maximum = 10000,
                },
            },
            DialogActRequired,
            false,
            true
        );
    }
}
