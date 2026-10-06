# Passive live object reads

`editor_units` reads the simulation registry, not the empty XS/KB mirror and not command acknowledgements. No hook, injected DLL, input, focus change, getter execution, memory write or save is needed.

## Reviewed build and numeric provenance

Only executable SHA-256 `dd15d1d838e78faa1bc9854becc3994f4f3a4548ef30efd24108abedc1b84fff` is currently reviewed. JSON cannot carry comments; named fields and this table are the accepted layout's constant annotations. All RVAs below refer to recovered live instructions in this build, not stable engine APIs. Offsets are byte offsets, never XML indices.

| Field | Recovered value | Independent instruction source |
|---|---|---|
| world global RVA | `0x53e5048` | `uiLookAtAndSelectUnit`, entry `0x621580` |
| world member | `+0x8` | same native selection path |
| object-slot capacity / array | `+0x188` / `+0x198` | same path bounds-checks and loads slot |
| ID slot mask | `0x3ffff` | same path masks ID before lookup |
| full object ID | `+0x1d8` | same path compares full ID, including generation |
| runtime prototype ID | `+0x1dc` | `0x449b10` prototype resolution and KB mirror adapter |
| actual player owner | `+0x208` | owner accessor `0x448750` |
| prototype-owner override (NOT actual owner) | `+0x1e0` | `0x449b10`; observed `-1` on all initial objects |
| world X/Y/Z | `+0x5c` / `+0x6c` / `+0x7c` | camera centering block `0x265135` |
| current / effective maximum hitpoints | `+0x23c` / `+0x248` | health-fraction getter `0x2576e0` loads maximum, then current, divides and clamps |
| prototype root member of command context | `+0x4e0` | `kbProtoUnitGetName` target `0x1bf8ed0` |
| prototype count / descriptor array | `+0x1448` / `+0x1458` | native name accessor `0x1c64740` |
| descriptor name pointer | `+0x10` | same accessor returns internal name pointer |

Five reviewed runtime code fragments pin selection, ownership, camera position, prototype names and health. Fragment lengths in accepted JSON are evidence windows, not callable function sizes. Runtime bytes differ from transformed on-disk `.text`.

Windows AMD64 pointer size is 8 bytes; IEEE binary32 health/position fields are 4 bytes. Name chunking uses Windows minimum 4 KiB page boundaries and printable ASCII for internal identifiers, not translated UI labels. Slot, base-prototype, name, page, coordinate, pointer and signature ceilings in `LiveUnits.cs` are explicit host safety/transport policies, not game maxima. The player range is the previously tested editor `0=Gaia` through `12` range. Self-test numbers are synthetic addresses/fields and a damaged fixture with 25/55 health, unrelated to game memory.

## Semantics and limits

- `unitId` is full generation-bearing ID; its low masked portion indexes the slot. Deletion/recreation and scenario loads can invalidate IDs. No persistent save-ID guarantee.
- `protoId` is a runtime identifier. Names come from the live base prototype registry, never metadata order.
- Player-local/virtual IDs outside that registry produce explicit `proto:null`. Exact name filtering refuses when unresolved names could make results incomplete.
- Position is world `{x,y,z}`, not pixels. `area:{x,z,radius}` is a planar circle; boundary included, zero-radius supported.
- Results include buildings, resources and decorations, not just army units. Defaults: offset zero, page 100; host maximum page 200.
- Registry roots/counts/table and full object identity/prototype/owner are rechecked. Changing or unreadable state fails; no automatic retries or partial success claims.
- This is **not an atomic frame snapshot**. Position/health can change during reads; paging across calls may see edits. Tool reports this explicitly.
- Editor mode required; no multiplayer/gameplay-mode relaxation.
- Optional `units` layout absent on unreviewed generated candidates. Missing layout, stale hash or changed code refuses. Future patch workflow: recover fields passively, compare native accessors, review candidate, validate on disposable map, explicitly accept, rebuild/restart. Never copy old offsets into a new hash.

## Checks

`dotnet .../AomMcp.dll --self-test` checks synthetic parsing, exact-name/player/area filters, damaged health, empty registry, bounds, nonfinite values, slot changes and generation mismatch. `python tests/protocol_smoke.py` checks schemas, nested area bounds and batch refusal before connecting to a game.

Passive MCP checks against four **user-placed** objects returned IDs 0/1: VillagerGreek, runtime proto 54, player 1, 55/55 health; IDs 2/3: TreeGaia, runtime proto 566, Gaia 0, 20/20 health. Exact name, player, zero-radius area and paging checks passed. Evidence: `research/live-units-evidence.json`; no scene mutations, input, focus changes, native calls or memory writes. Names and full owner fields agree with independent native accessors. This does not claim stress/reentrancy or future-patch validation.
