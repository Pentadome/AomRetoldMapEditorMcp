# Runtime telemetry: intentionally unavailable

## Current evidence

Reviewed executable: `dd15d1d838e78faa1bc9854becc3994f4f3a4548ef30efd24108abedc1b84fff`.

Existing layouts corroborate editor object IDs/prototypes/owners/positions/health, selection and camera fields. They do **not** establish runtime worker action/target or AI-plan structures. Editor object layouts must not silently become playtest layouts.

Debugger attachment does not work in this environment. Research is restricted to query/read-only process access (0x410), passive bounded observations and **independent** XS/debug evidence. No debugger, memory writes, arbitrary native getter dispatcher, guessed field offsets, native scenario loading or native start-test.

Local `xs.vsix`, `extension/syscalls/syscalls.json`, declares `xsGetTime`, `xsGetContextPlayer`, own KB queries and unit action/target/state/owner/prototype/position getters, `aiPlanGetActiveCount(false)`, `aiPlanGetIDByActiveIndex(index,false)` and `aiPlanGetState(id)`. The generated own-context probe uses these exact declaration signatures. Supplied metadata is not current-build compile or effect proof. Existing inline research XS uses vector `.x/.z` syntax; generated AI rule compilation remains unverified.

`aiEcho` documentation says **AI Debug Output window**. No file capture/compiler transport is confirmed. A caller-supplied transcript can be checked for hashes, freshness, run/player identity, complete monotonically timed frames, numeric values and races; it is not independently authenticated engine evidence. Absence of compiler-error text never proves compilation.

## Fail-closed infrastructure

- `RuntimeReadLayout` is optional and absent from accepted layouts. Candidate validation checks exact build, playtest mode, distinct independent evidence hashes and bounded/aligned/distinct offsets.
- **No runtime layout is registered**. Even structurally valid candidate metadata refuses loading before any candidate reads. Generator never supplies these offsets.
- Synthetic stable-read fixtures require matching pointer/full ID/owner/prototype/generation/action/target on both reads; changed generations/reused IDs are refused without retries. This detects observed races, not atomic snapshot/ABA proof.
- Worker actions, targets and AI plans all report unavailable; no partially populated result is presented as complete telemetry.
- `PlaytestWorkflow` registers **one independently reviewed pixel profile**: English alternative UI, 2560×1440, Player1/Standard on the pinned build. Candidate validation/preview and foreign-token refusals remain connection-free. The public start/inspect/Quit workflow passed with fresh game-written backups, saved/live tuples and independently observed editor return. Ordinary editor input guards are unchanged; private outside-editor clicks are only reachable for registered-profile known-playing host-token Quit.

## Required isolated research

Before live work, approve exact disposable scenario, original/disposable checkpoints, TR backup/export, managed custom-AI/staging/receipt/backup paths and evidence outputs. Take game-written original backups before any transitions; if isolation/authorization cannot be established, stop live work.

Use one persistent MCP host. Inspect initial state once; use normal UI Play/Quit, never native start/load. Observe passive memory candidates across independently distinguished idle/gather/task/retask/wait, active-plan and controlled death/rebuild cases. Destruction needs its own exact approval; speed checks never place/delete/change triggers. No automatic input retry, rollback, cleanup or elevation.

Accept a field/profile only when pinned build signatures, identity/player/generation lifetime, mode, bounds and independent XS/debug observations corroborate it. Record source hashes and limitations; use sanitized synthetic/evidence descriptions rather than copying proprietary scenarios. Reject unknown/unsupported states. Compilation, observed behavior, reload persistence and full telemetry must each be reported separately.

Until runtime corroboration exists, telemetry capability flags remain false. UI-profile availability is separate from compiler/gameplay/runtime proof. Offline fixtures/build/protocol checks validate infrastructure; the authorized public workflow additionally verifies bounded UI transitions.

The [authorized 2026-10-06 observations](WORKFLOW-LIVE-OBSERVATIONS.md) establish saved managed AI binding, ordinary gameplay, private Defeat exits, independent normal-menu Quit geometry and the subsequently registered public helper end-to-end path. They do not authenticate XS probe execution/capture or establish worker telemetry. Installed bindings/stock AI wrappers and isolated UI-source inspection found debug-window declarations, not a confirmed file transport. The discovered XS debugger includes breakpoint/stepping controls and was not opened. Without independent values, no passive action/target/generation/plan candidate is registered; telemetry remains unavailable, not partially verified.
