# Technical reference

Detailed reference for developers and AI agents. For a quick overview, see the [main README](../README.md).

## Build and run

Windows source build requires .NET 10 SDK and MSVC x64 Build Tools. Clone with `--recurse-submodules` (or `git submodule update --init lib/CryBarEditor`): CryBar's BAR/XMB library is built from that submodule and linked in-process. Metadata generation needs installed Retold; live operations need running scenario editor. NuGet restores `ModelContextProtocol.Core` 2.2.0 plus `Sdcb.SimdPaddleOCR`/embedded ChineseV6Tiny OCR models; Python smoke checks use only stdlib. Self-contained releases include the OCR assemblies, model DLL, reviewed UI layout and OCR fixtures.

```powershell
.\build.ps1 -Generate -Test
python tests\protocol_smoke.py          # protocol + local bridge refusal checks
python tests\protocol_smoke.py --live   # state/image, harmless deselect, refusal guards
```

Add to client's standard MCP configuration (adjust path):

```json
{
  "mcpServers": {
    "aom-editor": {
      "command": "dotnet",
      "args": [
        "DOWNLOAD_LOCATION\\AomMcp.dll"
      ]
    }
  }
}
```

Server startup has no game side effects. Game operations require editor mode, matching executable hash/runtime signature, and window-owner thread. Command/game-data catalog lookups need no running game. Read-only screenshots also work during playtest; mutations still require editor mode. Mutations focus game. **Use only in offline scenario editor, not multiplayer.** Tool output goes to stdout JSON-RPC only; diagnostics go to stderr. Server exits at stdin EOF. Official SDK handles stdio, initialization, protocol errors, and cancellation notifications. Tool calls remain serialized; queued calls can be cancelled, but started native operations/batches finish their bounded work and cleanup. Cancellation is not rollback; inspect editor before retrying. Decoded incoming messages retain a 2 MB size guard.

Options: `--exe <path>`, `--pid <pid>`, `--bridge <dll>`, `--layout <json>`, `--ui <decoded-xml-directory>`, `--generate <output-directory>`, `--self-test`, `--self-test-local`, `--toolset core|full` (initial set, default `core`). Full self-tests include installed catalog coverage; local-only mode runs managed/native synthetic checks without installed game or metadata.

### Linux cross-build and releases

`.github/workflows/release.yml` builds on `ubuntu-24.04`, validates downloaded ZIP on `windows-2022`, then publishes ZIP, npm tarball and checksums for pushed `v*` tags only. npm tarball wraps same release payload; offline Windows install/launcher validation runs before release. Tag-gated `publish-npm` job automatically publishes validated tarball through npm trusted publishing (OIDC), with no npm token. Stable tags update `latest`; prerelease tags publish under `next`. Configure npm package's trusted publisher once as documented in [npm/README.md](RELEASING.md). Pull requests, `main` pushes and manual dispatch build/validate artifacts without publishing a release. Actions are commit-pinned; only release job receives `contents: write`.

Local Linux build requires .NET 10 SDK and `g++-mingw-w64-x86-64`:

```bash
bash native/build.sh
dotnet publish src/AomMcp -c Release -r win-x64 --self-contained true \
  -p:IncludeGameCatalog=false -p:GenerateDocumentationFile=true -warnaserror -o package
```

CI statically links compiler runtimes, checks native imports for Windows system DLLs only, and excludes game catalogs even if locally generated files exist. Windows validation checks ZIP SHA-256, required files, x64 PE headers, bundled runtime and setup-script syntax, then runs packaged `--self-test-local` with deliberately missing game/UI paths. It tests native wrong-process/one-shot refusal and synthetic host logic; **not installed catalog/protocol coverage, live engine operations or gameplay compatibility**. Existing full `tests/protocol_smoke.py` remains an installed-game check.

Maintainer release, after committing/pushing workflow and reviewing supported layouts:

```bash
git tag v0.1.1
git push origin v0.1.1
```

Example tag only: use a new, unpublished semver version for each release. Package version is derived from tag; no manual npm manifest bump needed. Existing GitHub releases/npm versions are not overwritten. Unknown game patches still require passive layout/signature review, not an automatic CI offset update.

## Tool surface

Default **core exposes 75 tools**: 66 core helpers plus nine essential native commands. **Full exposes 925 tools**:

- **434 `editor_*` command tools**: typed scalar/vector arguments from native help. Editor/UI/gadget functions, core editor operations, and additional functions referenced by shipped editor controls/hotkeys. Selected multiplayer/online prefixes excluded.
- **418 `action_*` tools**: 116 XML command elements, 41 command attributes, 261 `editor.con` hotkey/context expressions. Original compound expressions preserved; `confirmDestructive: true` required.
- **73 helper tools**: 66 core helpers (tool search/selection, `editor_call` proxy, state/catalogs/input/placement/batch, capabilities, export recovery, trigger list/edit/parity, players, dependency audit, diplomacy, screenshot crop/OCR, player settings and AI staging, plus eleven [world-space scene helpers](../research/SCENE-TOOLS.md): camera look-at, view/UI state, world placement, layouts, snapshots/diffs, scene summary, guarded deletion, footprint and terrain checks; and twelve [world editing helpers](../research/LIVE-WORLD.md): terrain/water readback, live players, edit-mode/UI-kind readout and exit, world-point painting of texture/mix/water/forest/cliff, deterministic elevation, unit move/rotate, terrain/lighting/civ catalogs, camera framing, ID-annotated overview, resource balance, mirroring and cluster generation), plus seven **full-only** [session workflow helpers](../research/SESSION-WORKFLOWS.md): saved notes, checkpoint diffs, managed AI installation, startup orders, XS probes, supplied evidence reports and fail-closed playtest sessions.

Core native commands: `editor_undo`, `editor_redo`, `editor_uiClearSelection`, `editor_uiSelectType`, `editor_uiLookAtAndSelectUnit`, `editor_uiSetCameraStartLoc`, `editor_saveScenario`, `editor_uiLoadTriggers`, `editor_uiSaveTriggers`. All `action_*` aliases, other native commands and session workflow helpers are listed only in full mode; from core they are reachable through `editor_call` (`{name, arguments}`), which preflights the target's full-catalog schema and confirmations exactly like a direct call. `editor_search_tools includeSchema=true` returns their schemas. Core helpers retain their guarded internal native workflows in both sets. Four independently reviewed Play/Quit profiles are available: English normal and alternative UI at 2560×1440 and 1920×1080, Player1/Standard (`uilayouts/playtest-{alt,normal}-en-{2560x1440,1920x1080}.json`). Public start/inspect/token-bound Quit passed an authorized isolated run for each. Other UI states fail closed; runtime memory telemetry remains unavailable. Generated XS/transcript assertions are not compiler or authenticated runtime proof.

### Find tools without switching modes

`editor_search_tools` is always available, including in core. It searches names and descriptions across all helper, native and action tools without connecting to the game, switching mode or executing matches:

```json
{"name":"editor_search_tools","arguments":{"query":"camera","limit":5}}
{"name":"editor_search_tools","arguments":{"query":"uiPlaceAtPointer"}}
```

`query` is required, nonblank and at most 256 characters; surrounding whitespace is trimmed. Every whitespace-separated term must match a name or description, case-insensitively. Exact names rank first, then whole-query name substrings, then all-name-term matches, then description/mixed matches; ties use ordinal tool-name order. No regex or fuzzy search.

Results include `toolset`, trimmed `query`, `total`, `offset`, `limit`, nullable `nextOffset`, `guidance`, and compact `tools` entries: `name`, `description`, `available`, `requiredToolset`. No input schemas or scripts. `offset` defaults to 0 (>=0); `limit` defaults to 10 (1..50). No matches or offsets past the end return an empty page. `requiredToolset` is the minimum set (`core` or `full`); `available` only means currently exposed, not runtime readiness or verified effects.

For unavailable results, switch with standalone `editor_toolset mode=full`, then refresh `tools/list` without an old cursor to obtain schemas. Search itself emits no list-change notification and leaves hidden direct/batch calls blocked. Metadata-only search batches need no game connection. Native `loadScenario` and removed action aliases remain excluded.

### Switch tools mid-session

`editor_toolset` is always available; no game connection, restart or regeneration needed:

```json
{"name":"editor_toolset","arguments":{}}
{"name":"editor_toolset","arguments":{"mode":"full"}}
{"name":"editor_toolset","arguments":{"mode":"core"}}
```

Mode omitted inspects current set. Response includes `toolset`, `exposedToolCount`, `changed`. Actual changes emit one MCP `notifications/tools/list_changed`; initialization advertises `tools.listChanged:true`. Clients supporting this notification refresh automatically; otherwise use client's manual tool refresh/reconnect. Restart `tools/list` paging without old cursor after a change. Invalid/no-op requests emit no notification.

Selection is session-local; next process starts with configured `--toolset` (default core). Tool-set operations must be standalone, not inside `editor_batch`, whose entire preflight uses one surface. Hidden direct calls and batch steps refuse before any game connection/effect. This reduces client context, **not a security/permissions sandbox**: core still has input/write tools; all hash/editor/confirmation guards and client approval policies remain.

`tools/list` paginates 100 tools/page. Full surface may exceed some clients' tool limits. `editor_catalog` reports current set/count and only currently exposed native signatures/actions; optional name filter. Original UI dialog values must be set before calling their action tools. Hotkey aliases retain their context in descriptions; caller must establish appropriate editor state.

### Eight workflow tools

| Tool                       | Implemented scope                                                                                                                                                                     |
| -------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `editor_units`             | Actual prototype/player/world-area matches, full live IDs, XYZ and health; IDs not persistent across reloads.                                                                         |
| `editor_inspect_selection` | Typed selection joined to actual object properties; unresolved kinds explicit.                                                                                                        |
| `editor_map_info`          | Dimensions/native quantized height/camera/projection; inverse ray or explicit horizontal-plane intersection, not guessed collision.                                                   |
| `editor_triggers`          | Inspect/validate/new-file patch of verified TR v12 single Always/CodeSnippet controller; confirmed native export/apply with backup/equality checks. Unknown structured shapes refuse. |
| `editor_validate_scenario` | Partial read-only live-object/reference checks plus supplied exported-controller heuristics; skipped objective/diplomacy/XS coverage explicit, no automatic fixes.                    |
| `editor_place_formation`   | Preview-first rows/rings, 1–32 objects, **client-pixel spacing**; confirmed placement reuses guards and observes actual IDs, stops with partial progress.                             |
| `editor_save_checkpoint`   | Game writer → unique staging → structurally/hash-verified approved **new** destination. No overwrite option.                                                                          |
| `editor_dependencies`      | Source train/build/tech/prerequisite/god paths for exact proto; potential links, not current-player availability.                                                                     |

Local trigger inspect/validate/patch, formation preview and dependencies need no game connection, including connection-free batches. Writes require `confirmWrite:true`; trigger apply requires `confirmDestructive:true`; nonpreview formation requires `confirmPlacement:true`. Native profile writers receive unique staging **stems** (game appends `.trg`/`.mythscn`), never caller-named destinations. Raw `editor_uiLoadTriggers` / `editor_uiSaveTriggers` require `confirmDestructive:true`. No automatic mutation retries/rollback.

Campaign additions: `editor_trigger_edit` accepts `edits` (≤64 distinct source trigger IDs) for one preview/new-file output, including labels, typed values and condition removal. `editor_trigger_list` filters `player`, `arg`, `references`; `editor_trigger_player_parity` proposes gaps without changing files. `editor_players` reads all reviewed Players Settings fields from a game-written checkpoint, including raw numeric minor-god IDs; `editor_set_diplomacy` accepts a matrix `changes` array (≤64 directed cells) with RGB gates and one final checkpoint. `editor_screenshot` crops/zooms; `editor_ui_read` runs fallible OCR on reviewed normal/alternative-UI fields. `editor_player_settings` previews/verifies offline and applies OCR/pixel-gated changes only on the pinned 2560×1440 or 1920×1080 normal or alternative UI with fresh scenario/TR backups and checkpoint readback. An age change can automatically reset minor gods: include all three as `observedOnly` assertions; never infer from OCR alone. Normal UI writing is unsupported and must refuse before input. AI `.xs` personality files belong under installed `game\ai`, not the profile `ai` folder.

Details, examples, numeric provenance and validation boundaries: [research/WORKFLOW-TOOLS.md](../research/WORKFLOW-TOOLS.md). Object/map/selection reads require independently reviewed optional layout sections; generator never guesses these fields. User-authorized live tests verified rows/rings, full-ID multi-selection, checkpoint writes and byte-identical trigger replacement/restoration. Original four objects, selection, camera and trigger bytes restored; save-name/dirty state and undo history may change. Evidence: [research/LIVE-MUTATIONS.md](../research/LIVE-MUTATIONS.md). No save/reload persistence or XS execution claim.

### Pantheon units and buildings

```json
{ "name": "editor_pantheon", "arguments": { "pantheon": "greeks" } }
```

Returns separate `units` and `buildings` arrays of exact prototype names, e.g. `VillagerGreek`, `Hoplite`, `MilitaryAcademy`, `TownCenter`; plus canonical culture, major gods, source/build hash and unresolved tech references. Singular/plural names are case-insensitive. Current data includes Greek, Egyptian, Norse, Atlantean, Chinese, Japanese and Aztec. Shared buildings can belong to several cultures. No runtime IDs or guessed spellings.

Source: `Data.bar` major-god starting units, reachable active/obtainable tech effects, explicit prototype culture tags and Unit/Building classes—not reused artwork. Results are a potential union across gods/ages; prerequisites/exclusions, god-power scripts and scenario overrides are not fully evaluated. Not a current-player trainability query. Japanese data currently references two missing tech definitions, reported in `unresolvedTechs` rather than hidden.

Run MCP tool `editor_generate_catalog` (or `--generate generated`) once to create ignored `generated/game_catalog.json`; builds copy it beside the host for standalone deployment. This shared cache replaces the earlier pantheon-only artifact. Missing/stale metadata refuses lookup with regeneration guidance. Executable hash plus executable/archive size and UTC modification time detect ordinary updates; file stamps are not cryptographic authenticity checks. Restart host after regeneration. All metadata-only batches also need no game connection.

### XS scripting API

`editor_xs_api` searches engine syscalls (from the game's shipped VS Code extension, `vscodeextensionretail/xs.vsix`) and the reusable shipped XS libraries (`random_maps/lib`, `random_maps/lib2`, `ai/core`, `ai/human_assist`): functions, class members/methods, globals and rules, with script contexts (AI, random map, trigger), include paths and file:line. The repository ships only signatures, names, types and default values in `xs/xs_api.json` plus this project's own one-line summaries in `xs/xs_summaries.json`; official syscall help text and source comments are read from the user's install at query time and never committed or packaged (`tests/package_smoke.ps1` checks this). After a game update, maintainers refresh with `AomMcp --exe <game> --build-xs-api xs/xs_api.json` and add summaries for new library entries.

### Objects, gods, technology, terrain and water catalogs

| Tool                   | Current source entries | Filters/details                                                                                                                                                                                |
| ---------------------- | ---------------------: | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `editor_prototypes`    |                  1,757 | `category`: `all`, `units`, `buildings`, `trees`, `resources`, `objects`; exact `unitType`; optional `pantheon`. Costs, base stats, initial resources, flags, train/build links.               |
| `editor_gods`          |                     93 | `category`: `all`, `major`, `minor`; optional `pantheon`. Starting units, age techs, direct effects/powers, unresolved techs. Minor identifiers are age-tech names, e.g. `ClassicalAgeAthena`. |
| `editor_technologies`  |                    896 | Optional `pantheon`. Costs, research points, status, prerequisites and effects.                                                                                                                |
| `editor_god_powers`    |                    161 | Optional `pantheon`. Power types, costs, placement, created units and direct god/tech grants; same-name source variants retained.                                                              |
| `editor_terrain_types` |                    483 | Exact `terrainType`, e.g. `PassableLand`. Texture identifiers, UI labels/classes, parent passability groups and texture settings.                                                              |
| `editor_water_types`   |                     70 | `category`: `all`, `lake`, `river`, `ocean`. Named water presets and settings.                                                                                                                 |

All six accept `name` (exact, case-insensitive), `filter` (name/label/string-ID substring), `offset` (>=0), `limit` (1–200, default 50) and `includeDefinition` (default false, original XML). Responses include filtered `total`, `entries`, and `nextOffset` (null at end). Offset follows stable source order. Use narrow queries instead of dumping all definitions.

```json
{"name":"editor_prototypes","arguments":{"category":"trees","filter":"pine","limit":10}}
{"name":"editor_prototypes","arguments":{"category":"resources","unitType":"GoldResource"}}
{"name":"editor_prototypes","arguments":{"category":"objects","limit":20}}
{"name":"editor_gods","arguments":{"category":"minor","pantheon":"greeks"}}
{"name":"editor_technologies","arguments":{"name":"ClassicalAgeAthena"}}
{"name":"editor_god_powers","arguments":{"name":"Bolt"}}
{"name":"editor_terrain_types","arguments":{"terrainType":"PassableLand","filter":"grass"}}
{"name":"editor_water_types","arguments":{"category":"ocean","limit":10}}
```

Prototype categories use shipped `unittype`/`initialresource` fields, not guessed names: trees can also be resource nodes and other objects. `unitType` covers wildlife, fish, gold nodes, projectiles and further game classifications. Nature/resource objects often have no pantheon association; that does not mean they cannot be placed. `objects` means prototypes not classified as units/buildings, not an exclusive tree/resource category.

Catalogs preserve source names, string IDs and numeric text; no translated-name guesses or runtime IDs. They describe base definitions, not current-player trainability, effective upgraded stats, mode availability or live editor placement proof. `NotPlayerPlaceable` is a gameplay flag, not an editor placement verdict. Unavailable/campaign/effect definitions can be present. Terrain names retain backslashes; repeated texture/power identifiers keep their source/type context. XML detail preserves complex prerequisites/effects, rendering and defaults for inspection rather than inventing interpretations.

Example placement `tools/call` arguments:

```json
{
  "name": "editor_place_unit",
  "arguments": { "proto": "VillagerGreek", "player": 1, "x": 1280, "y": 720 }
}
```

Coordinates are **game-client pixels**, not scaled screenshot pixels or world coordinates. Read `editor_status` dimensions; default screenshot width 1280 represents half-width of tested 2560-pixel window. Pixel/OCR layouts are reviewed at 2560×1440 and 1920×1080; other 16:9 clients 1280..2560 wide get read-only derived geometry (`layoutReviewed:false`) for UI detection, occluders, camera look-at, OCR presets and world helpers (placement/layout/delete, forest/water/cliff paint, elevation; live-verified 1600×900, texture/mix palette selection enabled but not live-tested at derived sizes), while dialog writers and playtest stay pinned to reviewed sizes. Placement clears stale cursor, waits for asynchronous activation, checks proto/player/map-hover/held keys, issues one native placement call, clears preview, then confirms cursor clear. It does not save.

Native tools return `nativeReturned`, **not semantic success or a getter value**. Dispatcher parses/queues some effects for a subsequent editor frame. Observe screenshot/state after actions. Screenshot captures focused game-client rectangle; `editor_screenshot`/`editor_overview` optional `resolutionScale` 0.1..1 (default 1) shrinks output (0.6 = 60% width/height) to save context/tokens; other windows/OS overlays must not cover it. Native-return acknowledgements never prove scenario creation/save/load completed.

Potential data-loss commands require `confirmDestructive: true`; action aliases always require it. UI input remains destructive-capable: clicking confirmation buttons or typing into dialogs can change/discard data. Keyboard new/load/save/close shortcuts require explicit confirmation. Agent/client approval policies still matter.

## Fast workflows

Keep one MCP host alive per editing session; do not launch a new process per action. Use `editor_search_tools` from core to find tools by name/description; switch to full only when needed, then use narrow `editor_catalog` for unfamiliar native signatures instead of paging all tools. Cache known signatures. Group established operations with `editor_batch`, then inspect one final screenshot (or an intermediate checkpoint when next action depends on visible state):

```json
{
  "name": "editor_batch",
  "arguments": {
    "steps": [
      { "name": "editor_uiClearSelection" },
      {
        "name": "editor_place_unit",
        "arguments": {
          "proto": "MilitaryAcademy",
          "player": 1,
          "x": 940,
          "y": 774
        }
      },
      {
        "name": "editor_screenshot",
        "arguments": { "maxWidth": 1280 },
        "delayMs": 50
      }
    ]
  }
}
```

Batch accepts 1–32 existing calls, preflights schemas/native confirmations, shares one hash-validated connection, and retains per-step native/editor/input guards. Images remain MCP image content. Optional `delayMs` (0–2000) waits before a step for queued UI effects; it is **not semantic verification**. Nested batches forbidden. First failure stops remaining calls; earlier changes remain. **Not atomic; never retry entire batch blindly.** Each destructive step still needs its own confirmation.

Measured five-run median for ten harmless commands: **705.5 ms separate → 228.2 ms batch (3.09×)**, excluding startup. Larger user-visible gain comes from fewer agent/tool round trips. No scenario edits during benchmark. Evidence: `research/latency-before.json`, `research/latency-after.json`.

## Base-defense demo

Built through MCP: central Greek base, 14 defenders, three attack-move waves (12/18/24 units at 10/50/90 seconds), Town Center loss and last-wave victory conditions. Editor restored to initial state. Backup: `research/BaseDefense.mythscn`; trigger source: `research/defense-controller.xs`. Details/evidence: [research/DEFENSE-SCENARIO.md](../research/DEFENSE-SCENARIO.md).

**Use UI Play button for playtest transitions.** One native `uiStartScenarioTest()` invocation crashed the game; normal UI entry/exit worked. No blanket reentrancy guarantee. XS Code Snippet is a supported trigger effect, not an unrestricted MCP script tool. Its template expands percent-delimited placeholders, so inline modulo expressions must be avoided.

## Updates / generator

ASLR-safe RVAs + **SHA-256-pinned** layouts. Unknown builds refuse mutation; stale offsets never silently reused. This is **not guaranteed patch-proof**.

After update, open scenario editor and regenerate:

```powershell
dotnet src\AomMcp\bin\Release\net10.0-windows\AomMcp.dll --generate generated
```

Generator:

1. Regenerates typed tools from installed executable's embedded native help.
2. Uses the linked CryBar library to decode installed `UIDefaultEditor.bar`; harvests XML commands/attributes and shipped editor hotkeys. Generated proprietary resources stay ignored by Git.
3. Decodes gameplay `proto`, `major_gods`, `minor_gods`, `techtree`, every current archive-listed godpower file, terrain types and water bodies from `Data.bar`; writes shared build/archive-tagged `generated/game_catalog.json`. No game calls required for these catalogs; obsolete power exports are not ingested.
4. Reads running image, resolves named registration references, recovers dispatcher/context/editor fields through narrow compiler patterns, and verifies context/thread/editor state.
5. Writes `generated/candidates/<sha256>.json`, **without automatic activation or engine calls**.

Review candidate; validate safe command, placement, cleanup and undo on disposable unsaved map before accepting under `layouts/` or supplying `--layout`. Owner/endpoint ABI assumptions remain explicit. Changed compiler patterns fail discovery; update generator/manual layout rather than guessing addresses. Current-build discovery exactly matched all 11 accepted dispatcher/context/editor layout fields and dispatcher prefix. Optional `units`, `selection`, `map` sections are separate passive-review work; candidates leave them absent. Do not copy old sections into a new hash. TR format/fixture changes require separate serializer review.

## Verified / unverified

Verified on `100.19.17020.0`: native thread-hook delivery/removal, safe generic MCP command, typed argument/data-loss refusals, protocol pagination, valid PNG transport, Greek villager placement, MCP Hoplite placement, cursor cleanup, and independently observed **undo / redo / undo**. Game stayed responsive; bridge DLL unloaded. Integration test left original villager only, unsaved. See `research/MCP-COVERAGE.md`, `research/mcp-integration-evidence.json`, and `research/mcp-final-*.png`.

Not claimed: every generated tool live-tested; return-value capture; all dialog workflows semantically validated; save/reload persistence; continuous brush/frame reentrancy safety; future-patch compatibility. Broad action reach comes from complete shipped command sources plus UI fallback, not a claim that every possible operation has an independently validated native API.

Commands can outlive host acknowledgement. A timed-out command may have run: **inspect editor before retrying; never blindly retry mutations**. One-shot packet/deadline guards prevent ordinary duplicate delivery, not proof of engine-level transactional semantics.
