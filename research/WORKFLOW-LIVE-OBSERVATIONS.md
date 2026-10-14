# Authorized workflow observations — 2026-10-06

This records bounded observations, not complete runtime validation. Step 14 research established ordinary UI Play/Quit and identified unavailable compiler/capture/runtime corroboration. Step 15 enabled only that evidence-backed profile and passed public end-to-end/offline/package checks; missing telemetry is not marked verified. Step 16 records the [implemented/offline/live/unavailable capability split](WORKFLOW-CAPABILITY-STATUS.md). Approved steps 1–16 are complete without claiming complete live telemetry or gameplay verification.

## Scope and preservation

The user explicitly authorized the current scene, then requested continued unattended work without further permission questions. No stock AI, objects, triggers, game settings, or scenario bytes were edited. Original game-written checkpoint/TR and a separate disposable-before checkpoint were retained before transitions. A single persistent MCP connection was used for each host version; the staged-build maintenance replacement was explicit, not an automatic reconnect. The game process was never restarted.

Reviewed build: `dd15d1d838e78faa1bc9854becc3994f4f3a4548ef30efd24108abedc1b84fff`, version `100.19.17020.0`, client 2560×1440, alternative UI. Process PID8912 and owner/window thread34500 matched observed guards. These process identities are session-specific, not reusable capabilities.

## Managed AI and binding

Two new diagnostic AI files and ownership receipts were installed only under `INSTALLPATH/game/ai/aom_mcp`. Stock and unregistered files were untouched. The source/installed XS SHA-256 was `e640755f0ce9c6f97a4124341e99646d43e63f1f3dd72e522c22cac105d219cb`. This standalone experimental diagnostic emits own-context `AOMMCP1` records via `aiEcho`; it issues no orders and does not initialize cheating map visibility or switch context.

1. A safe saved default AI name, `chairon`, initially failed preview. Validation now accepts safe bare **expected** names while retaining nested installed destination constraints, exact source assertions and all UI gates. Synthetic path-refusal tests passed.
2. Binding the long filename stopped on search OCR readback. No binding was claimed; input was not retried. Backups were hashed and the browser inspected. After explicit recovery approval, only the observed browser was closed and a new game-written diagnostic checkpoint established that Player2 still had `chairon`. No reviewed semantic changes were found; raw-only changes remain opaque.
3. A different short filename, `probe14c5.xs`, was separately installed with a new receipt and staging file, then selected using fresh backups. The final checkpoint stored `aom_mcp\probe14c5`, without `.xs`. The original strict comparison correctly stopped rather than assuming success. Read-only verification with the exact stored path passed all reviewed player fields, diplomacy and embedded TR preservation. No rebind was sent.
4. Destination verification now recognizes only the observed optional `.xs` omission on an **explicitly requested AI destination**. Unrequested fields and source expectations remain exact. No case, directory, basename, extension, OCR or installed-file checks were broadened. Regression tests and fresh staged build/protocol checks passed.

Binding is saved-checkpoint evidence only. AI compilation, execution and reload persistence are not established.

## UI and passive evidence

The ordinary UI Play popover was inspected. The user pressed Play. Screenshots showed active gameplay, subsequently defeat; no compiler dialog appeared in these captures. This is not proof of the diagnostic personality's compilation/execution or of correct defense/farming/rebuilding logic.

A separate query/read-only (0x410) observation matched the executable hash/module and existing reviewed editor flag `0` during gameplay. No worker action, target, generation or AI-plan offsets were guessed, scanned or enabled.

After the user went AFK, one narrowly scoped private research exit was prepared for the observed Defeat > Quit button. It pinned process lifetime, module/build, owner/window thread, foreground, client size, known gameplay mode and exact screenshot-derived pixels, with a one-shot token. No arbitrary click coordinates or native dispatcher calls were exposed. Initial preparation refused missing image support and an inappropriate DX client DC; existing Windows System.Drawing and the same screen DC coordinate system as `Ui.CapturePixels` resolved those read-only problems.

The first attempt moved the pointer but sent **no mouse-down**: a hover-dependent border changed and the pixel gate refused. The consumed token/failure files were retained. Fresh full-resolution inspection established the exact hovered Defeat state. Explicit inspected recovery used a new token and immutable hovered evidence, required the pointer already at the observed Quit button, and sent one fixed click without another move. No click was blindly repeated. Screenshot, mode/thread/build readback and a new game-written checkpoint then established return to editor.

The returned checkpoint retained Player2's managed AI path and showed zero reviewed semantic changes from the pre-Play bound checkpoint. Raw section differences remain explicit; this does not establish universal unchanged bytes, reload persistence, camera-pose restoration or runtime identity persistence. That initial Defeat-Quit observation did not establish a general in-game menu/Quit profile; registration remained unavailable until the subsequent ordinary-menu research below.

## Subsequent ordinary-menu and capture research

Installed `config/game.con` binds Alt+Shift+Q to `AIDebugInfoToggle` in the `aidebug` context, and Alt+Shift+D to `toggleXSDebugger` in the game context. Neither shortcut was dispatched. VSIX declarations describe `aiEcho` output in the AI Debug Output window's All category; category/warning variants do not document file transport. Read-only archive listing and isolated extraction identified `ui_xs_debugger.xml` with Run, Single Step and breakpoint controls. This is not a permitted compiler/capture shortcut: no debugger was opened or attached. Stock debug wrappers call `aiEchoCategory`; installed/profile log scans did not establish an authenticated engine transcript. Without independent XS/debug values, no passive action/target/generation/plan field can be corroborated or registered.

Two further distinct backed-up UI runs used the same persistent v2 MCP connection. The second reached defeat before menu research; fresh inspected pointer-only recovery and one Quit click returned to editor after queued input completed. No click was repeated from an immediate acknowledgement. The third retained fresh game-written `step14-ui3-before.mythscn` and `.trg`, then used the observed normal Play button. A loading screenshot was not accepted as gameplay. Fresh gameplay/mode evidence preceded a narrow token/identity/signature/pixel-guarded gear click. Subsequent independent screenshots established Paused > Quit and the Quit YES confirmation. Each fixed input was separately gated before and after pointer movement. One YES click returned to editor with the same process/build/owner/window identity and no placement cursor.

The resulting normal path is Play popover `(2340,30)` → Play `(2206,316)` → gameplay gear `(2528,28)` → Paused Quit `(1280,1008)` → YES `(1074,782)` → editor, for alternative UI, English, 2560×1440, Player1/Standard. Screenshot frames 018–023 and private consumed-state files remain local. Raw GetPixel checks independently agreed with editor PNG gate values. Game-written `step14-ui3-returned.mythscn` had zero reviewed semantic changes and eight raw changes; incomplete decoding, unchanged viewport and reload/runtime-ID persistence remain unproven. This normal-path evidence permits a narrowly pinned UI profile, not runtime telemetry or a profile for arbitrary languages/resolutions/menus/defeat states.

## Registered public helper verification

Step 15 registered only the independently reviewed `uilayouts/playtest-alt-en-2560x1440.json`, pinned SHA-256 `df2311ab670edf906425f3864eec9d2493699ed0a74e923834d5dec291ae5c46`, with sanitized observation manifest `tests/fixtures/playtest-ui-evidence.json`. Raw-byte hash files have explicit LF checkout attributes. A shared bounded read-only wait now checks the next step's gate before following queued UI input; no earlier click is replayed. Hover-sensitive controls are excluded from relevant gates, and selected-player digit pixels are included.

A fresh v4 staged build/local/protocol check passed before explicit maintenance replaced only the identity-checked v2 agent MCP/proxy, not the game. One new persistent v4 connection retained new checkpoint/TR backups and called public `editor_playtest start`, `inspect`, then host-token `quit` with timeoutMs=15000. Start reported playing; independent inspect matched identity/mode/gates; Quit reported returned. Final MCP status/screenshot independently showed editor mode with the original game process, build/thread identity and cleared cursor. Runtime telemetry, compilation and gameplay success flags remained false. The game-written post-return checkpoint had zero reviewed semantic/eight raw changes, with decodingComplete=false and universalUnchangedVerified=false. No scenario load, object/TR edit, automatic rollback, cleanup, debugger, runtime field enablement or game restart occurred.

## Retained evidence

Session artifacts remain under `.tmp/agent/live-workflow-20261006-14c541c8/`; private tokens/logs are not copied into tracked fixtures. The original checkpoints, TR exports, both installed test AIs/receipts, writer staging and failed attempts were not cleaned up or rolled back.

| Artifact | SHA-256 |
| --- | --- |
| original.mythscn | 36324f8dfc49b213dfae28bcb0a7593198867513841e490650352426d3e4aa99 |
| original.trg | 4cbd6c31f70716280d50ea37b67bc6b016696ecbd7f9d3c25cd52425e54a09b1 |
| players-recovery-observed.mythscn | 18da21f501c7e5a86ba5db53a1a6c2f0ebc788d8496eb4031dcd1719cf87a69a |
| aom-player-settings-verify-34e5efe16e27443193cc1c7ba271e194.mythscn | 80bdde897193431ecd593701d0cc58d9f3d70db4f426fea1fbc1d887f642b0cb |
| players-returned.mythscn | fad1d97e4ccbfd2cbeefd35852ac2b4de23afc50371f0f31e08e4b28d2c0eb84 |
| step14-ui3-before.mythscn | 0cca0cbbb6bcfe3b727ee936edcf40fd67ebe9fe6c2e899a6d40b605a79db333 |
| step14-ui3-before.trg | 4cbd6c31f70716280d50ea37b67bc6b016696ecbd7f9d3c25cd52425e54a09b1 |
| step14-ui3-returned.mythscn | a2382b06df21ae68267f2da336d6bbcfa7050f433aabe90e0aa4f912ab045ad1 |
| step15-disposable-1.mythscn | a9ce047a1395293d95fb5b01184110f13ec0fd14e50c76501c76fcf6e00bde88 |
| step15-trigger-1.trg | 4cbd6c31f70716280d50ea37b67bc6b016696ecbd7f9d3c25cd52425e54a09b1 |
| step15-returned-1.mythscn | a9544d2a28473b68101dbac3c8856d22902f998aaae17f98092860ab32b3b711 |

## Unavailable evidence and remaining work

Bounded profile log/temp/config/users inspection found no independently confirmed compiler/debug file transport. `aiEcho` declarations still identify a debug window, not a file sink. There is no fresh run-bound engine transcript authenticating the probe, no compiler proof and no independent action/target/AI-plan corroboration. No runtime fields were enabled. Ordinary-menu evidence permitted one narrowly pinned UI profile, whose public workflow passed. Capture/compiler/runtime-field proof remains unavailable.

Actual farmer/woodcutter/defense work targets, controlled reconstruction, generated startup/causeway application and reload persistence were not exercised in this run. Original-scene preservation and observed Play/Defeat/Quit do not satisfy those tests. The seven helpers' offline infrastructure is implemented; comprehensive live verification is incomplete, not silently marked done.

Latest checks after registration: standard and staged Release builds (0 warnings/errors), local self-tests, full/core protocol (`full=899`, `core=49`, `live=False`), `git diff --check`, stub-only release workflow and isolated self-contained ZIP/npm/checksum/install/npx/launcher smoke passed. Initial new-profile npm smoke found the sanitized evidence omitted by the package whitelist; failed staging/logs remain under `.tmp/agent/workflow-package-1d2d03b1850d4e8d9f9b747b4a0a55ef/` and the live root. The repaired explicit evidence-only whitelist and profile/evidence equality checks passed in fresh `.tmp/agent/workflow-package-b5bf4aefa7784092996c2f19a5b4d7f5/` staging. Earlier package artifacts remain retained. No real publish occurred. The v4 host was deployed only through explicit inspected agent-only maintenance and remains alive; the game was never restarted.

## Multi-profile playtest verification (normal/alternative × 2560×1440/1920×1080)

Three new reviewed profiles were researched manually with exact full-resolution gates stable across captures and while hovering the Play control (hover-sensitive Play pixels excluded), then registered and run through public `editor_playtest` start → inspect → host-token Quit (timeoutMs=15000) on one MCP connection per run. Each run retained a distinct original checkpoint, a fresh disposable checkpoint and a TR export (`.tmp/nui/<tag>-*`, not cleaned). Scene: blank map plus two TownCenters (P1, P2). Start reported playing; inspect matched; Quit returned to the editor with the reported `uiKind` unchanged. The alternative 2560×1440 profile was re-run as a regression check.

| Run | Profile | Original | Disposable | TR |
| --- | --- | --- | --- | --- |
| ptn2560a | normal 2560×1440 (Play `(2526,58)`) | 6a06bcf2… | 135eab14… | 3ddb48ed… |
| pta2560b | alternative 2560×1440 (re-run) | 38a64375… | c2b044c6… | 3ddb48ed… |
| pta1920a | alternative 1920×1080 (Play `(1755,23)` → popover `(1654,236)`) | 5b919d19… | d21b86ef… | 3ddb48ed… |
| ptn1920a | normal 1920×1080 (Play `(1894,44)`) | 9966f71e… | 8a8ae231… | 3ddb48ed… |

Quit path is identical across UIs at a given size: gear → Paused Quit → YES (2560: `(2528,28)`/`(1280,1008)`/`(1074,782)`; 1920: `(1896,21)`/`(960,756)`/`(805,586)`). The alt-2560 start gate fails closed on the normal UI. Compilation, runtime telemetry and gameplay correctness remain unverified.

## Focus-free capture and focus tiers (C1, 2026-10-13)

Build 100.19.17020.0, normal editor UI, 2560×1440 client (`MythRetold` window). Alternative UI not tested; the capture path does not depend on UI kind.

- **PrintWindow capture.** `PrintWindow(hwnd, dc, PW_CLIENTONLY|PW_RENDERFULLCONTENT)` with the game foreground was pixel-identical to the screen-DC copy (0 of 3,686,400 pixels differed). With a topmost 1400×900 magenta window covering the client, PrintWindow still returned the clean editor frame, and `editor_screenshot` (via MCP) returned it while the cover stayed foreground. `Ui.CapturePixels` now tries PrintWindow first without focusing (validation only). It falls back to the old focused screen copy when the window is minimised, PrintWindow fails, or the frame is all black. Every pixel gate (UI-kind detection, dialog state, playtest profile) uses this path.
- **Base focus tier.** With Windows Terminal foreground, `editor_focus` in default mode activated the game in ~0.6 s (SetForegroundWindow / AttachThreadInput). No lease is taken by default.
- **`AOM_MCP_FOCUS=assertive`.** Opt-in tiers after the base tier fails: SwitchToThisWindow; a VK_F24 tap then SetForegroundWindow; minimise a terminal foreground (WindowsTerminal/conhost/OpenConsole/pwsh/powershell/cmd), focus, restore on release. The base tier always succeeded from this harness, so **the extra tiers were not reached live** (untested).
- **Focus lease.** In assertive mode the window that was foreground before a connection took focus is restored when the connection (one call, or a whole `editor_batch`) is disposed. A plain SetForegroundWindow back to Windows Terminal was refused (foreground lock). Tapping VK_F24 first made it succeed: terminal → game at 0.67 s → terminal at 0.70 s. `editor_focus` drops the lease so the game stays foreground.

## XS Scripts list (C3, 2026-10-13)

Normal UI, 2560x1440, build 100.19.17020.0, scratch scenario with 0 triggers. Script Manager = `gadgetReal("ScriptManager")`, inner window `scriptsDlg`, default position bounds [1052, 192, 454, 604]; list control rect [1075, 356, 410, 235].

- **Include accepts anything.** Typing `zzprobe\does_not_exist.xs` into `scriptsNameEdit` and clicking Include Script listed it; the editor does not check that the file exists. `editor_xs_scripts include` therefore reports `resolution` (profile `trigger\` or stock archive; mod folders not searched).
- **OCR of backslashes.** `om10\om10_diffbonus.xs` reads as `om10lom10_diffbonus.xs` in the list and in the name field, so `editor_dialog_act setText` refused `TEXT_NOT_VERIFIED`. Text comparison now treats every path separator as possibly read as nothing or one of `l`/`i`/`1` (`DialogState.SameText`; other glyphs exact).
- **Rows and removal.** Rows are 31 px apart (centers y 386, 417). A click on a row selects it (no visible highlight); Remove Script removed exactly that row, leaving the others in order.
- **Import reset confirmed.** `editor_triggers apply` (autoBackup) of `research/defense-probe.trg` emptied a one-row list: `scripts="restore"` had to include `om10\om10_diffbonus.xs` again (`action: included`), and the final readback matched. `lastKnown` came from the newest profile save (trigtemp.xs had no include lines).
- **Not tested.** Alternative UI (no Script Manager signature; refused before input), moved Script Manager windows, lists longer than the 7 visible rows (`mayBeTruncated`), and persistence after save (check with `editor_xs_check scenarioPath="latest"`).

## Playtest compile gate (C4, 2026-10-13)

Build 100.19.17020.0, normal editor UI, 2560x1440, scratch scenario with one active trigger `C4_BadFor` whose code snippet is `for (int i = 0; < 3) { trChatSend(0, "c4 loop"); }`.

- **Dialog.** Play wrote `trigger\trigtemp.xs` (the bad `for` at line 14) and showed a modal "Trigger Error." dialog with one OK button (title y 421, OK (1281,1004)), text: `trigtemp.xs(14): XS: Error 0172: ')' is not a valid operator.`, `... Error: parseExpression failed.`, `... Error 0139: parseForLoop failed.`, `... Error 0273: could not parse the code for '_C4_BadFor' rule.`, `XS: Error: could not compile file 'trigtemp.xs'.`
- **MythLog has none of it.** The playtest load (`~testing.mythscn`) logged 0 diagnostics; trigger compile errors exist only in the dialog. S2/S3 saw the same (their `om10\om10_diffbonus.xs(158)` 0172 also came from the dialog).
- **OCR.** PaddleOCR reads the game's old-style numerals as letters (`o172`, `oi39`); `PlaytestDialogs` maps o/O to 0 and i/I/l to 1 in codes and line numbers. Unscaled reads (scale 1) are clean; a tile edge through a line garbles it, so tiles overlap by 100 px and edge lines are taken from the neighbour tile.
- **Flow.** `editor_playtest start` returned `state:"dialog"` with five parsed entries and the trigtemp excerpt; `dismiss` re-read the dialog, clicked OK and reached the playing gate (the game plays on with the trigger missing); `inspect` and `quit` then returned to the editor. Runs `c4run2`/`c4run3`.
- **Quit hover.** In `c4run2` the Paused-menu Quit click was lost (gate matched, no confirm within 15 s; session became unknown, recovered by hand). With the hover settle raised from 80 ms to 300 ms, `c4run3` quit cleanly. Cause not proven.
- **Not tested.** Alternative UI and 1920x1080 dialog reads, AI error dialogs (kind `AIError` is assumed by analogy), dialogs with Next/Close, several dialogs in a row, runtime (non-compile) error dialogs during play.

## R1/R2: trigger scripts in mods, merging import (2026-10-13)

Build 100.19.17020.0, normal editor UI, 2560x1440, scratch scenario. Mod registry `mods\myth-mod-status.json`: subscribed `551120_obsidian mirror - dominion betrayed` Enabled=true (Priority 1); the local copy Enabled=false (Priority 2). Profile `trigger\` has no `om10\` folder.

- **R1(b), editor compile roots.** XS Scripts list = `om10\om10_diffbonus.xs` (only under the mods' `trigger\om10\`). Playtest `c4run4`: trigtemp.xs line 19 `include "om10\om10_diffbonus.xs";`, no dialog, gameplay reached, quit clean. Control `c4run5` added `zzprobe\does_not_exist.xs`: Trigger Error dialog `trigtemp.xs(20): XS: Error 0123: failed to open file 'zzprobe\does_not_exist.xs'.` plus `could not compile file 'trigtemp.xs'`; dismissed and quit by `editor_playtest`. So the editor's compile searches the enabled mod's `trigger\`. Whether the disabled local copy would also be searched cannot be told apart here (both copies hold the file).
- **R1(a), campaign play with only the mod.** Not tested (needs leaving the editor through the campaign menus). D7 (difficulty prompt and credits chat) stays open.
- **R1(c), local vs subscribed.** Only the enabled subscribed root is shown to work; a local-only enabled mod is untested.
- **R2, editor Import.** `editor_uiImportTriggers` opens a "Load Menu" file browser on the profile `trigger\` folder (folders first, a filter-as-you-type name field that showed only the first 30 typed characters, Import Cameras / Import Units toggles both off, Close / Open Directory / Delete / Directory Up / Open). With one trigger `Trigger_0Defense_Init` and list `om10\om10_diffbonus.xs`, importing an export of the same set gave two triggers (`Trigger_0Defense_Init`, `Trigger_0Defense_Init1`) and kept the list. Import therefore merges and is not a drop-in for `apply` (which replaces); a host-driven merge would be a new UI tool (both UIs, Load Menu calibration). Not done; `apply scripts="restore"` remains the path. Live re-check: apply without `scripts` refused naming `lastKnown`, and `scripts="restore"` restored the list.

## B1: cross-file effect copy, live (2026-10-13)

Scratch export (one trigger `Trigger_0Defense_Init`, fires at start). `editor_trigger_edit operation=patch copyEffects` copied `Chat: Send Chat` from `campaign\om\om09.mythscn` (trigger 647 `Chat_Village_Help_Us`, element 1, from `editor_trigger_elements library:true`) before effect 0; the preview warned about the source key `{STR_OM_SCN_09_CHAT_25}`. `copyValues` set `PlayerID` 5 -> 1 and `Message` -> `B1 probe chat line` (valueTypes 6 and 22). `editor_triggers apply` with `autoBackup`, `expectedBasePath/Sha256` and `scripts="restore"` round-trip verified and re-included `om10\om10_diffbonus.xs`; the stale `zzprobe\does_not_exist.xs` that `lastKnown` took from the previous trigtemp.xs was not listed before the import and so was not re-added. Playtest `c4run6`: trigtemp.xs had `trChatSend(1, "B1 probe chat line");`, and the chat line `Player 1: B1 probe chat line` was on screen at start. Scratch triggers and an empty XS list restored afterwards. Note: calling `editor_trigger_edit` without `operation` used to fail with an opaque `The given key was not present in the dictionary.`; it now names the missing argument.

## XS Scripts on the alternative UI (2026-10-14)

Build 100.19.17020.0, alternative editor UI, 2560x1440, blank scratch map after a game restart (the user switched the UI). New file `uilayouts/dialogs-alt-en-2560x1440.json` holds a ScriptManager-only signature (18 points, three camera poses, own 1.00 / closed 0.00); no other alternative dialog is calibrated.

- **Dialog.** Title "XS Scripts"; list with a "Scripts list" caption inside its control rect (41 px), five icon-only buttons (add, remove, up, down, open script), no name field and no Close button. Add Script opens a child popup "New Script Name" (field plus a check-mark confirm button) and the new row becomes the selection. Row pitch about 31 px like the normal UI. The list clips long names: `om10\om10_diffbonus.xs` reads `om10\om 10 diff`, `om10\om10_wrket_modifiers.xs` reads `om 10\om10 wrk`, so only the start of a name can be read back. The popup field shows only the tail of long text, and its OCR misreads (`x$` for `xs`).
- **Tool path.** `editor_dialog_act` refuses the icon-only buttons and the popup controls (no OCR anchors, hidden by default), so `editor_xs_scripts` uses `press` for Add Script and pixel clicks from the mapped control rects for the popup field, its confirm button and Remove Script, after the dialog scored open at nominal geometry. It checks the popup caption by OCR, requires the field to be empty, types, checks that the visible field text lies inside the typed name, clicks confirm and requires exactly one new matching row.
- **Hover tooltips.** After clicking Remove Script the tooltip "Remove Script" stays over the list while the pointer rests on the button, and the list read returned `dUas aOUUAH`; the pointer is now parked on the map before each read.
- **Live results.** list, include (rows 1 to 2, `fullNameVerified:false`), alreadyListed, remove (row removed, list minus that row), verify (`prefixMatched:true`, `verified:false`). `editor_triggers apply autoBackup scripts="restore"` of a one-trigger file round-tripped; in this run the import did NOT empty the XS Scripts list (both rows survived), so the re-include step ran only as alreadyListed and the extra row was reported unrestorable. Why the list was kept here (blank scenario, alternative UI, or no earlier compile) is unknown. Include itself was verified separately.
- **Not tested.** Alternative UI at 1920x1080 or other sizes, lists longer than 7 rows, two names with the same first characters, a reset-then-restore on the alternative UI, and the normal UI after these code changes (only offline tests).
