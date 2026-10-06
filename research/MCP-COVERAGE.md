# MCP coverage and live validation

## Implemented surface

Tested build: `100.19.17020.0`; SHA-256 `dd15d1d838e78faa1bc9854becc3994f4f3a4548ef30efd24108abedc1b84fff`.

Historical generated snapshot below; current runtime surface is **49 core / 892 full** (40 helpers, 434 native commands, 418 actions). Historical evidence counts are retained.

| Source | Generated tools |
|---|---:|
| Native editor/UI/gadget help + functions referenced by shipped editing sources | 435 |
| Commands in editor XML elements (28 exported XML files) | 116 |
| Commands in editor XML attributes | 41 |
| Editor configuration hotkey/context expressions | 261 |
| Tool-set selection/state/catalogs/input/placement/batch + eight workflow helpers | 28 |
| Full total | **881** |
| Default core (all helpers + ten essential native commands) | **38** |

`--toolset core|full` chooses initial set (default core). Always-visible `editor_toolset` inspects/changes mode mid-session without game connection; actual changes emit `notifications/tools/list_changed`, advertised by `tools.listChanged:true`. Both immutable tool snapshots are cached. Direct calls and batch preflight reject hidden tools; mode operations are standalone-only, so batch surface cannot change halfway. `editor_search_tools` searches full-catalog names/descriptions from either set without connection, mode switch or match execution; compact results report current availability and minimum toolset, never runtime readiness. Search emits no list-change notification and hidden calls remain blocked. `editor_catalog` reports only current surface. Existing permissions/editor/hash/confirmation checks are unchanged; tool sets are not security sandboxes.

Native help scan sees 2,483 definitions overall; server deliberately does not expose the entire unrelated engine/XS/multiplayer syscall surface. Typed parameter schemas use documented `string`, `bool`, `int`, `float`, and `vector` forms. Primitive argument validation and string escaping occur before bridge requests. Getter returns are **not captured**.

Editor menus, toolbar/dialog actions, object/proto selection, terrain/brush operations, object/clipboard/rotation operations, map/scenario settings, player/diplomacy/environment controls, triggers and cinematics have generated command/action reach. Fields/list entries/file dialogs and controls without a command are reached using screenshot + keyboard/mouse fallback. This covers shipped editing sources and interactive access, **not a separately validated semantic API for every possible action**.

Protocol: `ModelContextProtocol.Core` 2.2.0 stdio server; SDK initialization, ping, errors, and cancellation notifications; paginated tools/list, tools/call, empty resources/prompts. Tool calls are serialized; cancellation can stop queued calls, not interrupt already-started bounded native operations/batches or their cleanup. Source command tools and action tools are generated on server startup from current installation/resources; generator also emits inspectable JSON catalog.

## Pantheon lookup

`editor_pantheon` returns exact unit/building names by culture, case-insensitive singular/plural aliases, major gods, source/hash and unresolved techs. Generated from `Data.bar` starting units, explicit culture tags and reachable tech effects; artwork is not used to infer affiliation. Greek includes `VillagerGreek`/`MilitaryAcademy`, excludes Egyptian `Barracks` and reused-art `TempleChineseSPC`; shared buildings included. Potential union across gods/ages, not current-player trainability or complete god-power/scenario analysis. Seven cultures in current data; two unresolved Japanese tech references surfaced explicitly.

Metadata is cached; executable hash and executable/archive file stamps refuse ordinary update mismatches. `--generate` refreshes metadata; restart host afterward. Standalone packaged metadata works without repository XML or a running game. Local 881-full/38-core smoke uses impossible PID and verifies aliases, schema/unknown-argument refusals, mixed lookup/catalog batches, packaged lookup, stale hash/archive and missing metadata refusals using temporary copies only. No scene changes.

## Game-data catalogs

Six additional read-only tools share `generated/game_catalog.json` with pantheon lookup: `editor_prototypes` (1,757), `editor_gods` (93), `editor_technologies` (896), `editor_god_powers` (161), `editor_terrain_types` (483), `editor_water_types` (70). Data comes from shipped proto/god/tech/power and map-definition XML; no names, IDs, culture links or placement permission guessed from artwork or obsolete blacklist IDs.

Prototypes classify units/buildings/trees/resources/other objects through `unittype` and `initialresource`; `unitType` supports further classifications. Resource/category overlap retained. God/tech/power culture associations use the reachable tech graph. Minor god identifiers are canonical age-tech keys. Power source variants and terrain parent/UI context preserved. Source numeric strings and string IDs retained; optional original XML exposes complex definitions without speculative interpretation.

Every catalog has exact-name/substring filters and bounded paging (default 50, cap 200); XML detail opt-in. Lookup and metadata-only batches require no running game. Local smoke verifies every tool's paging/detail/schema/refusals, source samples (TreePine, MineGoldSmall, MilitaryAcademy, VillagerGreek, Zeus/Athena, Bolt, default terrain, ocean preset), packaged operation, stale/missing refusals and numeric-bound preflight before native connection. Temporary metadata copies only; no game mutation. These are static definition checks, not proof all listed objects/settings work in live editor.

## Eight workflow helpers

All eight implemented; [WORKFLOW-TOOLS.md](WORKFLOW-TOOLS.md) details limits/provenance. `editor_units`, `editor_inspect_selection`, `editor_map_info` use separately recovered optional fields/signatures, not dispatcher getter returns. Prior passive evidence validates four objects/map projection and empty selection. User-authorized live ring test resolved three selected objects including full generation-bearing IDs; delayed reads verify queued selection/undo effects.

`editor_triggers` supports strict verified TR v12 single Always/CodeSnippet controller inspect/validate/new-file patch plus confirmed native export/apply with backup/serialized equality; unsupported shapes refuse structured editing. `editor_validate_scenario` is partial read-only object/map/declared-reference checks and supplied-controller heuristics, not objective UI/diplomacy/XS validation. `editor_place_formation` is preview-first client-pixel rows/rings with observed IDs and partial-stop reporting. `editor_save_checkpoint` uses GUID native staging, bounded stable l33t/zlib length/Adler verification and approved CreateNew copy/hash verification, never overwrites. `editor_dependencies` reports exact source train/build/effect/prerequisite/god paths, not evaluated trainability.

Local 881-full/38-core protocol/synthetic checks passed, including default/explicit core, mid-session full/core switching, list-change notification counts, schema equality, hidden direct/batch refusals, invalid/no-op mode handling and retained confirmations. User-authorized native rows/rings, checkpoint writes, inactive controller replacement/Unicode round-trips, original trigger restoration, client bounds/no-overwrite refusals and partial-stop behavior passed; original four objects, camera, selection and trigger bytes restored. Evidence: [LIVE-MUTATIONS.md](LIVE-MUTATIONS.md). Passive validator used one host and left four object records/foreground unchanged (`live-workflow-evidence.json`); supplied historical controller not asserted current scene triggers. No native calls, input, focus, saves or scene edits for that check. New helper confirmations/conditional arguments preflight in batches before connection/effects.

## Live observations

`research/unit-evidence.json` and `research/placed-unit.png`: fixed smoke bridge placed one real Player 1 Greek villager. Preview cleared; unit persisted. Scenario not saved.

`research/mcp-integration-evidence.json` plus `mcp-final-*.png`: production MCP bridge placed Player 1 Hoplite through `editor_place_unit`. Exactly one `uiPlaceAtPointer(false)` in placement steps; selected runtime proto ID 58. Stale cursor cleared before preparation; selected proto/player observed; map hover and held-key guards enforced. Cursor field was -1 after cleanup.

Independently viewed images:

- `mcp-final-before.png`: original villager only.
- `mcp-final-placed.png`: original villager plus blue Hoplite at requested client point.
- `mcp-final-undo.png`: Hoplite absent; original villager remains.
- `mcp-final-redo.png`: Hoplite restored.
- `mcp-final-restored.png`: Hoplite undone again, original villager only.

Hook removed after requests. Separate process module enumeration after host exit found neither production bridge nor smoke bridge loaded in game. WM_NULL responsiveness check passed. No scenario save/overwrite, executable patch, debugger attachment, remote thread, or protection bypass.

## Checks passed

- Native MSVC `/W4 /WX`; .NET warnings-as-errors build.
- Local bridge self-checks/refusals; string/control-character and PNG self-checks.
- SDK refactor: warnings-as-errors build, 865-tool stdio smoke checks (three runs), initialized notification, ping/empty lists, protocol error codes, 2 MB refusal/recovery, omitted arguments, serialized pipelined calls, queued cancellation, clean EOF shutdown. Read-only live state and SDK PNG transport also checked; no scene mutations.
- `tests/protocol_smoke.py --live`: 865 unique valid tool names, cached pagination, object schemas, stdio protocol, wrong type refused, load/close confirmation refused, safe deselect, valid screenshot chunk CRCs/decompression, clean server EOF exit. Batch schema/native-confirmation preflight, nesting/count/delay bounds, stop-on-error, and image transport passed.
- Five-run ten-command benchmark: separate 705.5 ms median, batch 228.2 ms (3.09×); no scene mutations. One scoped hash-validated connection per batch; native guards remain per command. No bridge modules loaded after host exit.
- Generator decoded all editor resources and hotkeys. Passive runtime candidate matched all 11 accepted dispatcher/context/editor layout fields plus prefix; no ASLR base baked in.
- Wrong/stale layout SHA-256 refused before game mutation.

## Issues found and fixed during validation

- Incorrect P/Invoke alias `Unhook` corrected to `UnhookWindowsHookEx`; live removal re-tested.
- UI command acknowledgements precede asynchronous cursor activation. Immediate field check initially refused placement without creating a unit. Bounded field polling now verifies both activation and cleanup; stale prior proto cannot satisfy new placement preparation.
- Default GDI stretch mode bit-combined downsampled pixels. HALFTONE scaling now gives correct readable screenshots.

## Boundaries

Generated means **exposed with schema**, not that each tool's result has been independently observed. Checkpoint writes and scoped formation/import workflows now have authorized live evidence; semantic save/reload persistence, broad trigger/cinematic workflows, long brush strokes and reentrancy under stress remain unvalidated. Four opaque game trailer bytes are preserved, not semantically verified; zlib length/Adler-32 and copied bytes are checked. No playtest/reload was performed; save-name/dirty state and undo history may change. No arbitrary script/RVA execution tool is exposed.

Known builds use accepted hash-pinned layouts; unknown builds fail closed. New-build generator writes review-only candidates. Narrow compiler patterns may stop matching after updates; no promise of indefinite patch survival. Manually inspect/adapt generator/layout if needed rather than activating guessed addresses.

Native dispatcher normal return means an acknowledgement only. Queued effects can happen later. Timeout may follow successful mutation; inspect before retry, never automatic retry. UI interaction requires focused/uncovered game and appropriate dialog/mode. Destructive-capable tools need client/operator approvals even when explicit action confirmations are present.
