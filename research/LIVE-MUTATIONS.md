# Authorized live workflow tests

User opened editor and explicitly requested live mutation tests. Accepted build `100.19.17020.0`, SHA-256 `dd15d1d838e78faa1bc9854becc3994f4f3a4548ef30efd24108abedc1b84fff`; observed PID24876/window thread28724 are **session evidence, not reusable constants**.

Evidence: [live-mutation-evidence.json](live-mutation-evidence.json). Raw responses, images and retained local backups: `research/extracted/live-mutations/` (ignored). No new commit.

## Results

| Check | Independently observed result |
|---|---|
| Initial safeguards | Four original objects: villagers IDs0/1, trees IDs2/3; empty selection; measured camera; original game-written trigger export and checkpoint retained before placements/imports. |
| Row formation | Two Player1 Hoplites, actual IDs4/5; reported XYZ and health115/115. Two explicit undos restored original records after queued effects settled. |
| Ring formation | Three Player1 Hoplites, actual IDs262149/262148/6. IDs were read, not inferred from order. Three explicit undos restored original records. |
| Nonempty selection | Delayed read resolved all three selected ring objects, including generation-bearing IDs and actual properties. |
| Trigger import/replacement | Inactive `LiveMutationProbe`, then renamed/Unicode-edited inactive controller. Both game exports byte-identical to supplied supported input. Second import replaced, rather than appended to, first trigger set. No playtest or XS effect claim. |
| Original trigger restoration | Original empty60-byte export restored through separately authorized native `uiLoadTriggers` using retained staging stem. Subsequent exports byte-identical to original SHA-256 `3ddb48ede465c9c72e61230cdbad5162bd882ff97047e8bb2d988ac95584b628`. Strict structured helper still refuses unknown/empty shapes. |
| Checkpoint writes | Game-written before/after snapshots copied to approved **new** paths; decoded length, zlib Adler-32 and copied hash verified. Original destination remained unchanged during deliberate overwrite refusal. |
| Client bounds | Anchor at actual client width2560 refused entire plan before placement. Schema ceiling alone is not actual window bounds. |
| Partial-stop reporting | Known missing prototype: attempted1/verified0, structured error, cursor cleanup; batch stopped before following read-only status step. No placement requested. |
| Validator | Inactive supplied controller produced expected inactive/no-literal-outcome findings. Final original four-object scene: zero findings in covered checks. |
| Final restoration | Original object IDs/prototypes/owners/XYZ/health, empty selection, camera and trigger bytes exactly matched captured baseline. Visible object palette closed with explicit ESC; final screenshot matches original editor view. |

Fixture counts, spacing120px and anchors were chosen small, clear client-space test inputs, not engine limits or world-spacing claims. Ring IDs demonstrate slot reuse/generation bits; never reuse these IDs in a later scenario.

### Retained checkpoints

- `research/extracted/live-mutations/before.mythscn`: stored60141 bytes, decoded3023626; SHA-256 `624a9e13eb021e725f005f176161f5028955dede27ee60929ecae547b6a8a3af`.
- `research/extracted/live-mutations/after.mythscn`: stored60151 bytes, decoded3023638; SHA-256 `c61d3aec660ddf262e58ffb2a7a451c0d32df39e7125ac081b0532b6bcd4aa45`.
- Native staging paths appear in evidence and remain available for recovery. Snapshots are **not byte-identical**; equality claims apply to observed object records/camera/selection and original trigger bytes, not every serialized scenario field.

## Bugs found and corrected

1. **Native filenames require stems.** Passing `.trg`/`.mythscn` produced `.trg.trg`/`.mythscn.mythscn`; reserved expected paths stayed empty. Actual exports were inspected before any new attempt. Shared `NativeProfileStem` now handles trigger load/save and scenario save. Accepted-build import formatter at RVA`0x4347c48` is `%hs.trg`; export counterpart `%ls.trg`. Synthetic writer regressions reproduce extension appending.
2. **Scenario footer boundary was wrong.** Fresh game exports and canonical `BaseDefense.mythscn` contain `l33t` + DWORD decoded length + zlib stream + **four opaque bytes after zlib EOF**. Adler-32 belongs immediately before those bytes, not at file end. Bounded verification now uses correct boundary; opaque trailer preserved but its meaning/checksum not guessed or claimed validated. Synthetic tests refuse truncation across trailer and zlib footer.
3. **Raw native trigger confirmation gap.** Live import proved whole-set replacement; raw export overwrote its owned reservation. `uiLoadTriggers` and `uiSaveTriggers` now require `confirmDestructive:true`, including generated schemas and whole-batch preflight. Helper export retains native approval only for its own fresh staging file after caller write approval. Local tests prove missing/false confirmation refuses before connection.

No automatic mutation retries or rollback. Four persistent host segments were used, restarting only for production code fixes; all production hosts exited0 on EOF. Temporary ignored controller read a partially published close request, failed **before any RPC**, then closed production host in `finally`; read-only publication retry corrected afterward. This was not an unknown native mutation outcome.

## Timing and UI limits

- Batch `delayMs` runs **before that step**. Put delay on following inspection, not preceding mutation. Immediate selection read saw only last placed unit; delayed read resolved all three. Immediate final undo read saw pending deletion; settled read verified restoration without another undo/delete.
- Native placement cleanup verifies cleared cursor, not restoration of every UI panel. Object palette remained visible; explicit ESC closed it after visual inspection. No unconditional ESC added to production placement.
- Save-name/dirty state and undo history may change. No semantic save/reload test, playtest, XS compilation/effect test, opaque trailer interpretation, arbitrary TR shape support, stress/reentrancy or future-patch guarantee.
- Scene mutations remain operator-authorized only. These tests do not authorize future destructive checks on an AFK scene.

Fresh analyzer/warnings-as-errors/XML rebuild and stdlib local protocol checks remain required after fixes; LSP silence alone is not proof of clean code.
