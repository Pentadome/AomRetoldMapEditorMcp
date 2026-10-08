# Base Defense — MCP-built scenario

## Handoff

- Editor left at initial, intact base; placement cursor cleared. Click top-right **Play** to test as Player 1.
- Full scenario backup: `research/BaseDefense.mythscn` (engine-generated testing snapshot copied into repository).
- Trigger export: `research/BaseDefense.trg`; readable inline effect: `research/defense-controller.xs`.
- To reopen backup, use editor **File → Load** and browse to repository's `research` directory.
- No user-named scenario overwritten. Game itself writes `~testing.mythscn` during playtest; final snapshot contains canonical controller, not validation variant.

## Rules

Protect central Greek Town Center. Lose when it falls. Win after third wave's surviving attackers are eliminated.

Initial base: Town Center, Fortress, Temple, Barracks, Archery Range, Armory, four houses, four Sentry Towers. Defenders: six Hoplites, six Toxotes, two Minotaurs, three villagers. **31 editor-placed objects**, plus game-provided starting units during playtest.

Initialization grants Player 1 1,000 food/wood/gold and 100 favor, sets mutual enemy diplomacy with Player 2, centers camera, and shows instructions.

| Time | Attackers | Direction relative to base |
|---|---:|---|
| 10 seconds | 12: 8 Hoplites, 4 Toxotes | Positive Z |
| 50 seconds | 18: 9 Hoplites, 6 Toxotes, 3 Minotaurs | Positive X |
| 90 seconds | 24: 12 Hoplites, 8 Toxotes, 4 Minotaurs | Negative Z |

Units spawn in spaced rows and receive aggressive attack-move orders toward Town Center. Gameplay deliberately small: three waves, no economy terrain or endless-wave system. Idle defenders lost at approximately 2:10; player intervention matters. Difficulty not extensively balanced.

## Verification

- All objects placed through `editor_place_unit`; screenshots independently checked.
- Official editor trigger serializer template used for one active looping **Always / XS: Code Snippet** trigger. Import/export round-trip byte-identical.
- `defense-test2.png`: normal scenario initialized, camera centered, first red attackers fighting.
- `defense-wave2.png`: all three wave announcements and third-wave combat visible at 1:30.
- `defense-wave3.png`: Town Center loss and defeat screen at 2:10. Normal canonical controller, no test cleanup.
- `defense-win-verified.png`: victory branch verified with separate validation-only effect that cleared Player 2 units at 100 seconds. **Not proof of a normal player winning unaided.** Validation effect absent from delivered scenario.
- Canonical controller restored, normal start rechecked (`defense-final-start.png`), then editor restored (`defense-final-editor.png`). Final trigger export again matched canonical bytes and contained no validation cleanup.
- Placement remained refused outside editor mode. Read-only MCP screenshots now work during playtest; other mode guards unchanged.
- .NET warnings-as-errors build, local self-tests, and live protocol smoke passed: 864 tools. Game responsive; no bridge modules remained loaded after host exit. LSP probes inconclusive, compiler/tests provide validation.

## Findings worth keeping

- One native `uiStartScenarioTest()` call crashed game. Base recovered from testing snapshot. Subsequent ordinary **UI Play / Quit** transitions worked. Prefer UI input for mode transitions; native reentrancy remains unproven.
- Trigger template expansion consumes percent-delimited text even inside snippets. Avoid modulo syntax here; use `i - 6 * (i / 6)` instead. Compound arithmetic victory predicate moved into integer variable for XS parser compatibility.
- Keyboard uses scan codes; key and mouse button presses held briefly across frame polls and released in `finally`. Focus acquisition waits for asynchronous activation.
- Base's first editor-placed object is scenario unit ID 0; controller depends on that identity. Replacing/deleting it requires updating controller target.

Screenshot `.png` files named here were removed from the tree to keep clones small; read them from git history at commit `00f9fe1` (e.g. `git show 00f9fe1:research/<name>.png > <name>.png`).
