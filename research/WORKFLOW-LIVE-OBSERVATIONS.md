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
