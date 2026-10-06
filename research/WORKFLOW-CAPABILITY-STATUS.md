# Session workflow delivery status

Approved plan steps 1–16 have been executed. Completion includes explicitly unavailable runtime capabilities, **not** complete live telemetry/gameplay verification. See [contracts](SESSION-WORKFLOWS.md), [live observations](WORKFLOW-LIVE-OBSERVATIONS.md) and [runtime boundaries](RUNTIME-TELEMETRY.md).

## Capability matrix

| Capability | Implemented / offline verification | Live or saved-source evidence | Not established / unavailable |
| --- | --- | --- | --- |
| Typed TR object references and ordered effect copying | Ordered ID/owner/prototype expectations, immutable source copies, stable handles, preserved metadata/extras/order; positive/refusal/readback fixtures pass | Reviewed real TR encodings informed codec | Newly generated causeway/startup TR application and gameplay effects not exercised |
| Saved unit notes | Shared bounded checkpoint/entity parser, Unicode notes, exact filters/paging/opaque hashes; synthetic malformed/refusal cases pass | Read-only research covered 25,216 saved entities across three checkpoints; current saved scene inspected | Saved IDs are not runtime identities; full decoding and all visible-label corroboration not claimed |
| Managed AI installation | Preview-first exclusive new installs, receipt-owned updates, includes/escape/hash/backup/outcome checks; fake-root tests pass | Two new managed diagnostic files/receipts installed; short Player2 AI binding independently verified through game-written checkpoint | Compilation, execution, stock overwrite, automatic binding, elevation, rollback and reload persistence not established/permitted |
| Startup work orders | Audit/preserve/conflict handling, global minimum-distance XZ assignment, deterministic ties, new TR write; 44 farmer/13 woodcutter fixtures pass | Reviewed source task expressions | Actual farming/woodcutting targets, reachability/pathfinding, live generated-order application and startup timing unverified |
| Semantic checkpoint diff | Reviewed players/TR/groups/entities plus ordered raw occurrence hashes; all assertions before paging; positive/refusal fixtures pass | AI recovery and UI-return checkpoints compared; latest public run: 0 reviewed semantic changes, 8 raw changes | Partial decoding explicitly prevents universal unchanged/valid verdict; raw tails remain opaque |
| XS probe generation | Opt-in own-player fresh KB queries, bounded/race-aware framed records and separate runtime identities; signature/static/refusal fixtures pass | Installed declarations and stock debug wrappers reviewed; separate experimental personality installed/bound | Generated rule/personality compilation, fresh independently authenticated engine transcript and file capture/compiler channel unavailable |
| Supplied runtime report | Strict pinned/fresh run/player/time framing, explicit numeric assertions and unsupported checks; malformed/stale/racy fixtures pass | No authenticated engine capture obtained | Supplied transcript does not prove engine transport, compiler success, gameplay or memory layout |
| Guarded UI playtest | One hash-registered profile; host-owned session token, exact process/build/thread/signature/mode/client/pixels, current saved/live tuples and fresh backups; read-only queued gate polling; synthetic/protocol/package guards pass | Public start → inspect → token-bound normal-menu Quit → returned passed on one v4 host connection; final independent editor status/screenshot and game-written diff | Other UI/language/resolution/overlay/defeat paths not registered; no original viewport restoration, scenario reload or gameplay/XS correctness claim |
| Runtime memory action/target/AI plans | Optional typed layout and stable-read/refusal infrastructure implemented | Existing reviewed editor-mode field independently observed during authorized gameplay; source/transport leads researched passively | No independently corroborated action/target/generation/plan layout, so **all runtime telemetry fields remain disabled**; no arbitrary getters/debugger/writes/scanning during tool calls |

## Supported public playtest profile

- `uilayouts/playtest-alt-en-2560x1440.json`
- SHA-256: `df2311ab670edf906425f3864eec9d2493699ed0a74e923834d5dec291ae5c46`.
- Evidence: `tests/fixtures/playtest-ui-evidence.json`, sanitized hashes/observations only; no tokens/game records/proprietary XML.
- Pinned executable `dd15d1d838e78faa1bc9854becc3994f4f3a4548ef30efd24108abedc1b84fff`, build `100.19.17020.0`, English alternative UI, 2560×1440; observed Player1/Standard popover.
- Start from ready editor with Play popover closed and placement cursor clear. Supply distinct original plus fresh game-written disposable checkpoint/TR backups, every required hash, observed PID/thread, fresh run ID and confirmations. Use `timeoutMs=15000` for loading.
- Only returned `playing` session can authorize Quit. Do not let an external menu/overlay/defeat change the reviewed path. No generic outside-editor input is exposed. Unknown state stops; inspect retained files/screenshots and arrange separately approved recovery, never replay input.
- Profile availability is metadata, not current foreground/readiness or compiler/gameplay proof. Exact hashes/pixels remain fail-closed; caller profiles cannot self-register.

## Remaining runtime blockers

1. `aiEcho`/category/warning documentation establishes an AI Debug Output **window**, not an authenticated file transport. Installed/profile log scans and archive/source inspection did not establish a compiler/capture channel. Discovered XS debugger has breakpoint/stepping controls and was not opened or attached; debug shortcuts were not dispatched.
2. No fresh independent engine-emitted `AOMMCP1` transcript. Binding verification and absence of compiler dialogs do not authenticate probe execution. The current saved scene has no Player2 workers, so it cannot independently corroborate that player's worker transitions.
3. Without independent XS/debug values, no action/target/generation/AI-plan candidate can be registered. Numeric action/plan interpretations, worker/task/retask/wait observations and controlled rebuilding remain unverified.
4. Generated startup/causeway application, actual resource targets, controlled reconstruction and reload persistence remain separate future live tests. Destruction requires specific disposable-object approval; no blanket authorization or automatic restoration is inferred.

Next research must establish a permitted independently corroborated capture/compiler channel and explicit own-context runtime identities before enabling any telemetry fields. Optional layouts reject caller candidates even when their structure is valid. These blockers remain unresolved, not relabeled successful.

## Verification and retained state

- Release build: 0 warnings/errors. Local self-tests and impossible-PID protocol passed: **full899/core49**, 434 commands/418 actions. Core surface unchanged; seven new helpers full-only.
- Queued transition tests poll reads only; expired/foreign/repeated/unknown tokens and unregistered profiles refuse. Final live same-host returned-session inspect passed; repeated Quit refused in preflight with nativeDispatched=false and outcomeUnknown=false. Packaged registered profile/evidence hashes checked by local self-tests.
- Isolated self-contained Windows ZIP/npm checksum/prepack/offline install/npx/shim/launcher checks passed after explicit sanitized-evidence whitelist repair. Failed and successful staging retained; latest successful root `.tmp/agent/workflow-package-b5bf4aefa7784092996c2f19a5b4d7f5/`.
- Stub-only release workflow and `git diff --check` passed. No real publish or commit.
- Live evidence/backups/receipts/staging/failures remain under `.tmp/agent/live-workflow-20261006-14c541c8/` and original game-writer staging paths. Agent-only v4 deployment was explicit, identity-checked maintenance; no automatic restart. V4 host remains alive.
- Final observed game: same process/build/window/owner identity, editor mode, foreground, 2560×1440, no placement cursor. Managed test AI binding retained. No cleanup, rollback, scene reload, stock AI overwrite, object/TR edits or game restart.
