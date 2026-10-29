# Editor dialogs and windows

Moved verbatim from AGENTS.md; served by `editor_help topic=dialogs`.

- Use `editor_dialogs` to understand editor windows/menus without screenshots: filter or exact window/open name gives controls (labels, types, value kinds) and the native calls each button/menu item runs as ready `editor_call` `{tool, arguments}`. Wrapper→XML window links: `live` (alt UI observed), `xml`, `name` (unobserved). Value fields without scripts have no native setter. Metadata only; regenerate if missing/stale.
- Live dialogs: `editor_dialog_state` detects open dialogs by calibrated pixel signatures (`uilayouts/dialogs-*.json`; normal UI 2560×1440 and 1920×1080, alternative UI 1920×1080 (2560×1440: ScriptManager only), default window position) and maps `editor_dialogs` controls to client rects; `read=true` adds OCR text and a green-tick checkbox heuristic. `editor_dialog_act` open/close/press/select run shipped scripts as validated native calls (view-only gadget scripts and Close/Cancel unconfirmed, else confirmDestructive); click/setText/toggle are pixel input after OCR label verification and refuse on uncalibrated size, moved/occluded windows. setText never sends TAB/ENTER/ESC/Ctrl+A; nothing applies until the dialog's own OK/Generate. Live-verified normal 2560×1440: New Scenario/Victory Conditions/Player Data open+read+close, Seed setText (-1→777→-1); other sizes run scaled best-effort (`bestEffort`) or report no signatures, and pixel actions refuse there. Field text comes from per-control OCR (zoom 2, then 3; `textSource`); focused-field caret can hide short text, so setText verify re-reads up to 3 frames (`framesRead`), never re-types. Observations only; checkpoint stays authoritative.
- Open editor windows with `editor_gadgetReal` names listed in its description (live-verified normal and alternative UI; e.g. `PlayerEditDlg`; `PlayerDataDialog` is an empty shell). Close with `editor_gadgetUnreal` and the same name (alternative UI exceptions: `DiplomacyDialog` → `PlayerDiplomacyDialog`, `ATriggerGadget` → `trEditor`); `uiCloseDialog` did not close them. Acknowledgement is not proof: screenshot.
- Triggers -> XS Scripts list: `editor_xs_scripts` (list/verify/include/remove through the Script Manager, OCR-verified, normal UI only; see `editor_help topic=triggers`).
- Calibrate signatures for another UI kind or client size with full-only `editor_dialog_calibrate` (optional
  `gadgets`). It opens/closes every gadget at four camera poses and keeps points that stay stable while the background
  behind them changes (opacity proof), avoid control rects (button/field colours change with state, e.g. Camera Start
  Show/Delete enabled only after Set) and are unique against the other dialogs. Writes a NEW
  `uilayouts/dialogs-<normal|alt>-en-<W>x<H>.json` (`FILE_EXISTS` if present, never overwrites; `DIALOG_STILL_OPEN`
  stops without writing). Close all dialogs first; takes minutes. Copy the file into the repo `uilayouts/` to ship it.
  Shipped 1920×1080 (2026-10-29): normal 37 gadgets, alternative 31 (CombatCommands has no opaque window; the
  alternative UI has no BrushMask/BrushFunctionsWaterTool/GodPowers/MapElevation/NewScenario/WorldLighting gadget
  window). Ambient motion (cloud shadows) is masked, dark points (modal dim, off-map void) are dropped.
- A frame that two UI kinds' files match equally (e.g. full-screen xsDebugger) reports `uiKind` null with a warning.
  A close that leaves no signature but cannot OCR-confirm the window is gone fails `DIALOG_CLOSE_UNVERIFIED`.
- `gadget` accepts catalog window names as aliases (`CameraStartDlg` → `CameraStartDialog`, `resolvedFrom` reports
  it). `close` re-checks a "closed" score by OCR of the window's control labels before skipping (`ocrCheck`).
  Occlusion refuses only for dialogs scoring `open` over the click point; a `partial` overlap only warns when the
  control label OCR-verified.
- Camera start: use `editor_camera_start` (see `editor_help topic=workflows`), not raw clicks on CameraStartDialog.
