# Eight workflow tools: implementation and limits

All eight original workflow names and subsequent campaign/UI helpers are exposed in both tool sets. Default **core: 49 tools = 40 helpers + nine essential native commands**. Full: **892 tools = 434 native commands + 418 shipped actions + 40 helpers**. Always-visible `editor_search_tools` finds full-catalog tools by name/description while staying in core; compact results label current availability and minimum toolset, not runtime readiness. Search is connection-free and does not execute matches or emit list changes. Always-visible `editor_toolset` changes core/full mid-session; `notifications/tools/list_changed` tells clients to refresh tools. Mode operations are standalone-only; hidden calls/batch steps refuse before connection. Implementation is not blanket live validation.

| Tool | Implemented behavior | Important boundary |
|---|---|---|
| `editor_units` | Exact prototype/player/world-XZ circle filters; bounded paging; actual IDs, owner, XYZ and current/effective max health. | Full generation-bearing IDs live only for current object/scenario lifetime, not stable across reloads. Reviewed unit layout required; non-atomic snapshots. |
| `editor_inspect_selection` | Typed selection entries joined to live object properties; unresolved/non-unit kinds explicit. | No guessed IDs or values; reviewed selection/unit layouts required. |
| `editor_map_info` | Dimensions, quantized native height, actual camera/render projection, world-to-client point, inverse ray and caller-specified horizontal-plane intersection. | Not terrain collision/occlusion proof. Reviewed map layout required. See [LIVE-VIEW.md](LIVE-VIEW.md). |
| `editor_triggers` | Local inspect/validate/lossless patch; native export/apply with original backup and game serialization equality checks. | **Only independently verified TR v12 single Always/CodeSnippet controller shape** supports structured inspection/edit/import. Unknown shapes refuse; export can preserve other TR shapes without interpreting them. No XS compiler/effect validation. |
| `editor_validate_scenario` | Missing caller-declared required IDs/players, suspicious object health/map bounds, exported-controller config/outcome/rule-reference candidates. | Partial diagnostics, not valid/invalid verdict. Objective UI/diplomacy/modes/dynamic references/external scripts not inspected. Supplied file is not asserted current live trigger state. |
| `editor_place_formation` | Pure preview; confirmed 1–32-member rows/rings using existing one-shot placement/cleanup and actual observed IDs. | **Client-pixel spacing, not world spacing.** Camera/UI affect resulting world layout. First error stops; earlier effects remain. Authorized row/ring placement and undo restoration verified; see [LIVE-MUTATIONS.md](LIVE-MUTATIONS.md). |
| `editor_save_checkpoint` | Native game writer to unique staging, stable l33t/zlib decoded-length/Adler-32 verification, new destination copy/hash verification. | No overwrite option; writer may alter save-name/dirty state. Native writes verified live; opaque game trailer not interpreted, no semantic reload proof. |
| `editor_dependencies` | Exact train/build links, positive Enable/CreateUnit/replacement effects, raw tech prerequisites, god starting units and shortest active/obtainable tech paths. | Static potential associations, not evaluated prerequisites/exclusions/current-player trainability. Scripts/power spawns/default inheritance not comprehensively analyzed. |

## Agent workflow additions (campaign triggers, players, AI)

Start with `editor_capabilities`; failures return structured `code`/`phase`/`nativeDispatched`/`fileVerified`/`safeInspection`/`nextAction` (e.g. `FOCUS_NOT_GRANTED` = no input sent; `IMPORT_OUTCOME_UNKNOWN`/`PLAYER_UI_OUTCOME_UNKNOWN` = stop, inspect retained paths, never retry blindly).

| Tool | Use | Boundary |
|---|---|---|
| `editor_export_recovery` | `inspect` staged export (format/suffix: controller `ff ff ff ff`, campaign 216-byte camera list, scenario `l33t`); `recover` to NEW path with `expectedSha256`. | No native call. |
| `editor_trigger_list` | Bounded list/detail of TR v12 exports; optional `player`, `{arg:{key,value}}` and `references` produce element-level matches and graph edges. | Read-only; names/params are serialized values, not runtime proof. |
| `editor_trigger_player_parity` | Read-only template→target player gaps, target-only/review hints and proposed trigger-edit batch. | Heuristic; no evaluated game behavior. |
| `editor_trigger_edit` | Preview-first `patch`/`clone` or up to 64 distinct-trigger `edits` in one in-memory pass/output. Supports typed numeric/string replacements, element labels, byte-exact duplicates and `removeConditions`/`removeEffects`; source SHA and per-trigger expected name required. | Clone appended to source group; refuses leftover self-EventID. Unrelated records/groups/suffix byte-identical; writes new output only. |
| `editor_players` / `editor_player_dependency_audit` | Game-written checkpoint: names, control, civ/color IDs, AI path, age/max age and raw minor-god IDs, visibility, handicap, resources/pop and directional stances; dependency audit joins trigger references, wave starts and victory checks. `raw:true` provides bounded P1–P6 subsection hex. | Static evidence from supplied files; minor-god IDs not names or runtime proof. |
| `editor_set_diplomacy` | `preview`/`verify` offline; legacy oneWay/mutual apply checkpoints per click, or `changes` array (≤64 directed cells) with RGB gates before/after each click and one final checkpoint. | Pinned build/2560×1440 or 1920×1080, alternative (Players Settings > Diplomacy) or normal (Scenario > Diplomacy) dialog auto-detected; backed up game-writer source and TR; no AI-path text typing or automatic retry. |
| `editor_screenshot` / `editor_ui_read` | Bounded [x,y,w,h] screenshot crop/scale and offline model-backed OCR on reviewed named fields or explicit regions. | OCR is fallible, not widget proof; named presets pinned to reviewed normal (Player Data/Age Settings/Load Menu) or alternative (Players Settings) layouts, chosen by panel pixel gates. No mutation. |
| `editor_player_settings` | `preview`/`verify` offline, guarded `apply` for player fields with expected/desired strings and pre-opened Players Settings. AI path through in-game file browser; age changes require `observedOnly` assertions for all minor-god IDs. | 2560×1440 or 1920×1080 alternative Players Settings or normal Scenario > Player Data (auto-detected), fresh checkpoint/TR backups, pixel/OCR gates and final checkpoint. Ambiguous option/file/color refuses; diagnostic checkpoint may be unavailable under a modal. Normal UI: AI Set needs control Computer. |
| `editor_stage_ai` | Copies reviewed `.xs` to a NEW file and reports resolution/include/wave checks. | Computer-player personalities must live under `INSTALLPATH\game\ai` (e.g. `C:\Program Files (x86)\Steam\steamapps\common\Age of Mythology Retold\game\ai`); profile `<id>\ai` did **not** work. Trigger files go in profile `<id>\trigger`. Staging does not write the install directory. |

Live evidence (isolated scene, build 100.19.17020.0, alternative UI): diplomacy apply P1→P2 enemy→neutral verified by checkpoint, restored neutral→enemy (two clicks) verified; disabled-clone trigger apply round-trip verified and re-exported (2 inactive triggers). Later campaign tests verified P6 food 999999→999998→999999, P7 Heroic→Classical→Heroic with automatically reset/restored heroic god ID, and P6 AI path reselected via INSTALLPATH file browser. Typing P2 AI Name was acknowledged but saved path stayed `chairon`. **Not verified:** XS compilation/runtime, attack waves, AI behaviour, persistence across scenario reload.

Diplomacy geometry (half-scale 1280×720 screenshot): cell(p,t)=(283+50t, 108+round(27.5p)), click at double; colours ally (0,255,0), enemy (255,0,0), neutral (238,238,238). `PlayerDataDialog` is absent from shipped XML; `gadgetReal("PlayerDataDialog")` opened an unrelated XS dialog.

## Trigger workflow

`inspect` / `validate`: `operation`, absolute local `.trg` `path`; no running game. Codec parses supplied bytes, reconstructs them and demands byte equality. This is **host serialization verification**, distinct from the game round-trip in `apply`.

`patch`: same source plus distinct new `outputPath`, `confirmWrite:true`, and at least one of `name`, `active`, `loop`, `code`. Source never overwritten. Supplied code normalizes CRLF; Unicode/surrogate lengths checked synthetically. Local file writes are not scene edits.

`export`: approved new `.trg` destination, `confirmWrite:true`, optional `profileDirectory`. Native writer receives only a unique GUID staging filename stem; game appends `.trg`. Verified bytes are copied to destination with `CreateNew`; caller-named basename never reaches profile-relative native writer. Staging retained.

`apply`: supported `.trg` input plus `expectedSha256`, reviewed live export `expectedLivePath` + `expectedLiveSha256`, `confirmDestructive:true`, optional active trigger `profileDirectory`. Exports original backup (must semantically equal reviewed live export) before import, stages supported input under unique basename, invokes native import once, exports round-trip and requires byte-identical equality. Backup/staged/round-trip paths retained. Failure identifies paths and warns import may already have applied. No automatic rollback/retry; undo support not assumed.

Example local patch (paths must be actual existing parent directories):

```json
{"name":"editor_triggers","arguments":{"operation":"patch","path":"C:\\Users\\wrket\\repos\\aom\\research\\BaseDefense.trg","outputPath":"C:\\Users\\wrket\\repos\\aom\\research\\DefenseEdited.trg","loop":true,"confirmWrite":true}}
```

Do not run example against an existing output. Inspection flags inactive/empty code and percent signs; `%` may be consumed by game trigger-template expansion, as previously observed with modulo.

### TR field provenance

These constants come from game-exported `research/defense-code-sample.trg` and independent canonical controller round-trip, not a universal TR ABI. Fixture SHA-256: `78fc459576ae5ca4dc2d081a58eadbceb264aef400a45784f90cdce6686a4f49`. Build packages fixture beside host; corruption/substitution refuses parsing.

| Constant | Meaning/source |
|---|---|
| Header 10; size at 2; version at 6; version 12 | `TR` magic followed by little-endian DWORD payload length excluding header and serializer version. |
| Name length at 42; name data at 46; fixture name length 21 | UTF-16 code-unit count followed by exact wide trigger name in verified record. |
| Name end +4/+5 | Loop/active bytes after four-byte native `-1` sentinel; fixture begins loop=false, active=true. |
| Fixture code-length offset 248; data delta 4 | Wide CodeSnippet length after known parameter metadata; offset shifts by two bytes per changed name code unit. |
| UTF-16 width 2 | Serialization encoding/Windows wide-char unit, not Unicode scalar count. |
| File 2,000,000 bytes; code 250,000 units; name 128 units | Host allocation/message safety ceilings, not engine maxima. |

All other bytes must match pinned template. Unknown version, flags, bounds, invalid UTF-16, trailing data or record shape refuse; unsupported fields are not guessed or dropped.

## Validator and dependencies

`editor_validate_scenario` reads actual live objects and optional measured map bounds. `requiredUnitIds` are **caller-declared current full live IDs**; strings in XS/scenario-name references are never treated as simulation IDs. `requireTownCenterPlayers` checks living exact `TownCenter`; variants may deliberately satisfy custom objectives. Optional `triggerPath` is an exported file, not current scene proof. Literal `xsEnableRule`/`xsDisableRule` matches outside this controller are unresolved candidates, including possible comments/text; external rules may resolve them. Literal outcome scan recognizes shipped `trPlayerSetVictorious`/`trPlayerSetDefeated` names, not guessed aliases. No automatic fixes.

`editor_dependencies` takes exact `proto` (case-insensitive), `offset>=0`, `limit1..200` (default 50). Relation entries include raw source evidence/prerequisites; abstract ProtoUnit targets match shipped unit-type tags. BFS visits each tech once per god, preserves shortest potential path and handles cycles. Example `Minotaur` links `ClassicalAgeAthena` and `Zeus` → `ArchaicAgeZeus` → `ClassicalAgeAthena`. Hoplite train link identifies `MilitaryAcademy`, not Egyptian `Barracks`. Uses existing source metadata/freshness checks and lazy XML cache; no game connection.

## Formations and checkpoints

Formation required fields: `proto`, `shape:rows|ring`, `count:1..32`, `spacingPixels:1..10000`, full-resolution client `x`,`y`. `player` defaults 1; Gaia 0. Rows `columns` default `ceil(sqrt(count))`, must not exceed count; incomplete rows also centered. Ring rejects columns; chord radius is `spacing/(2*sin(pi/count))`, one object at anchor. Integer pixel rounding can collapse points; then whole plan refuses. Schema pixel ceiling 65535 is host bound; actual client bounds checked before mutations.

`preview` defaults true: points only, no game connection/focus/input. `preview:false` requires `confirmPlacement:true`. Entire plan preflights before placement, keeps existing guards/cleanup, checks client resize and observes exactly one newly added matching full ID after each command. Missing/extra objects, concurrent edits, asynchronous visibility or cleanup failure stop workflow, even if placement occurred. Structured partial progress also sets MCP `isError`; batch stops. No inferred IDs, retries, rollback, save or exact world-spacing claim.

Checkpoint takes new absolute `.mythscn` `path`, `confirmWrite:true`, optional active **scenario** `profileDirectory`. Host calls typed `saveScenario` once using random ASCII staging filename stem (game appends `.mythscn`), not caller's destination name. Reserves unique staging file; if native writer refuses reservation or path semantics differ, tool times out rather than guessing/retrying. Structural check uses observed `l33t` magic + DWORD decoded length + zlib stream + four opaque game trailer bytes. Validates zlib Adler-32 footer (RFC1950) **before** that trailer to catch truncated completion. Trailer semantics are unknown and not claimed verified; bytes preserved unchanged. Stable identical read required across two polls. Host budgets: five-second read polling, 25ms interval, 64MB stored/256MB decoded ceilings, 8192-byte decompression buffer. These are safety policies, not engine limits.

Writes refuse relative/UNC/device/ADS paths, reserved DOS names, redirected files/directories and existing destinations. Parent directory must exist. `CreateNew` prevents ordinary destination-existence races. These are local host safeguards, not protection against an adversarial process replacing filesystem objects after preflight. Explicit profile must match active game account; ambiguous discovery refuses. GUID staging avoids accidentally overwriting a user-named file if profile selection is wrong. Staging kept for recovery; no overwrite support added.

## Verification

- Fresh warnings-as-errors/analyzer/XML documentation build; local stdlib protocol tests expose 891 unique tools.
- Trigger golden template/canonical codec reconstruction, Unicode/CRLF patch, unchanged source, unknown/truncated shapes, new-file/conditional confirmation refusals and connection-free batches.
- Dependencies source links/god paths, source prerequisites, case-insensitive names, paging and unknown/stale metadata paths.
- Synthetic scenario finding/outcome-name checks; invalid live arguments refuse before connection.
- Formation row/ring math, real-ID matching logic, unchanged-scene refusal, pure-preview/impossible-PID batches and confirmation/bounds refusals.
- Synthetic game-writer callback validates routing, reservation, stable structural read and no-overwrite rules; invalid checkpoints leave user fixture unchanged. Additional authorized live save/import/formation tests are recorded separately in [LIVE-MUTATIONS.md](LIVE-MUTATIONS.md); no automatic/destructive tests against an AFK scene.
- Passive `editor_validate_scenario` checked current four-object editor map using one host. Object records and foreground unchanged before/after; historical exported defense controller explicitly not claimed current scene state. Evidence: `live-workflow-evidence.json`; no native calls/focus/input/game-memory writes.
- Prior passive object/selection/map evidence: [LIVE-UNITS.md](LIVE-UNITS.md), [LIVE-VIEW.md](LIVE-VIEW.md). Initial empty selection verified passively; authorized live ring selection resolved three actual objects with generation-bearing IDs.

## Patch handling

`units`, `selection`, `map` are optional reviewed layout sections with pinned runtime signatures. Generator candidates do not guess/copy them; absence refuses dependent reads/formations. New executable hash requires passive field/signature review, explicit accepted layout, regenerated catalogs, rebuild/restart. TR serializer changes need separately reviewed format/fixture support; unknown record shapes refuse. Native command acknowledgements remain acknowledgements, not getter values/effect proofs. No arbitrary XS/RVA execution tool or new production dependency.

Normal-UI live evidence (2026-10-07, same build, isolated 2-player scene, every step game-checkpoint verified then restored): P2 food/gold, display name, visibility, civ Hades→Poseidon→Amaterasu→Hades (civ list opens scrolled to the selection; tool wheels up in 3-notch events, then scans down — one +20-notch event zoomed the map camera instead), control Computer↔Human, color red→yellow→red, maxAge, startAge Default→Classical→Archaic (classical god auto-set 695), classical god 695→696 via label, AI path `chairon`→`demo\aomspe02_p2.xs` through Load Menu (returns to Player Data); P1 wood/favor/pop/popLimit/handicap. Diplomacy matrix P1→P2 enemy→neutral→ally and P2→P1 enemy→neutral, then reverted. Findings: TAB leaves a blinking caret in normal-UI text fields (OCR read `300|` as `30d`, `Bob|` as `Bot`), so gates re-read up to 4× (read-only); typed player names are saved in the first PL P1 string (shown as `displayName`), the legacy `name` field stayed empty; handicap desired values must use checkpoint format `0.00`.
