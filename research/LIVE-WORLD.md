# Live world readbacks: terrain types, water, edit mode, paint selections, players, unit heading

Build `dd15d1d838e78faa1bc9854becc3994f4f3a4548ef30efd24108abedc1b84fff` only. Same policy as
`LIVE-UNITS.md` / `LIVE-VIEW.md`: passive `ReadProcessMemory` (PROCESS_QUERY_LIMITED_INFORMATION |
PROCESS_VM_READ), no writes, no debugger, no decoding of XOR/add-obfuscated values. Every offset below
is backed by disassembly of the live module image (RVAs relative to module base) **and** by a live
read/diff experiment in the editor. Accepted values live in the `world` section of
`layouts/<hash>.json`; the host re-checks every listed code fragment (`signatures`) in live memory
before reading and refuses on any mismatch.

Notation: `ctx = [base + contextRva]`, `world = [ctx + 8]` (same as map `worldOffset`),
`terrain = [world + 0x260]` (map `terrainOffset`), `editor = [[base + editorGlobalRva] + 0x6d0]`
(layout `editorPointerOffset`), `tm = [ctx + 0x4e8]` (terrain/type manager).

## Terrain tile type/subtype (tool `editor_terrain_info`)

* XS `trGetTerrainType`/`trGetTerrainSubtype` call terrain vtable `+0x278`/`+0x280`
  → `0x1fdf0f0` / `0x1fdf160`:
  ```
  cmp edx,[rcx+0xc] ; cmp r8d,[rcx+0x10]      tile X/Z bounds (= map tileOffsets)
  imul edx,[rcx+0x124] ; lea ecx,[rdx+r8]      index = tileX*stride + tileZ
  cmp ecx,[rax+0xf8]                           element count
  mov rax,[rax+0x108]                          record array
  movzx eax,byte ptr [rax+rcx*4+2]   (type)    / movzx eax,word ptr [rax+rcx*4] (subtype)
  ```
  4-byte record: `u16 subtype`, `u8 type`, `u8` (not interpreted).
* Names: `uiSampleTerrainAtPointer` handler `0x683bf0` resolves group/subtype through
  `tm`: groups array `[tm+8]`, count `[tm+0x10]`, 0x50-byte group; group `+0` subtype array,
  `+8` subtype count; subtype stride `0xf8`. Subtype `+0x58` → UTF-16 display name
  ("Atlantean Beach 1"), `+0x40` → texture path, `+0xb0` → footprint/sound class. Group `+0x10` →
  group name (PassableLand, Water, Underwater, Shoreline, NonPassableLand, Ice).
* Live checks: (0,0)=Default; painted beach strip=(3,23) Atlantean Beach 1; (0,173) Greek Grass Rocks 2.

## Water body per tile

* `trGetWaterType` → terrain vtable `+0x2b8` → `0x1fdf710` → `0x3d5f90`: inline grid at
  `terrain+0x148`: `+0` element count, `+0x10` byte array, `+0x2c` stride; `index = tileX*stride+tileZ`;
  `0xff` = no water.
* Water type names: `tm+0xb8` count, `tm+0xc8` pointer array; object `+0x140` → UTF-16 name
  (70 types, e.g. 31 = GreekLake). Live check: painted lake tiles (72..81,40..47) = 31.

## Water surface height per node (`editor_terrain_info.waterSurface`)

* `uiCoverTerrainWithWater` handler `0x682db0` → terrain vtable `+0xa8` (`0x1febd70`, args height, depth, waterType, 1)
  → per-node setter vtable `+0x78` (`0x1fe0b20`), which writes the float grid at `terrain+0xb8` (same inline layout as
  heights `terrain+0x78`: `+0` count, `+0x10` data, `+0x2c` stride) and the water tile byte grid `terrain+0x148`.
  Layout `map.waterSurfaceOffset` = 184, signature at RVA 33426228 (live bytes = image).
* Live diff: cover 7.25/2 → ground 5.25, surface 7.25, water byte 63 (AtlanteanSea); cover 9.75/3 → ground 6.75, surface 9.75.
  The host reports the surface only on tiles whose water byte is not `0xff`.
* **Picking:** elevation bumps, texture-paint clicks (exact tile under 8 units of water) and the camera target all hit the
  terrain **under** water, not the surface; projecting at the surface caused `BRUSH_NOT_AT_TARGET`. (The plan's
  surface-pick hypothesis was disproved.) The actual session bug: the 4-pass fixed-point `GroundHit` oscillated next to a
  10-unit underwater plateau wall; it now ray-marches the cached height grid (0.25 tile steps) and bisects (10 passes).
* **Not undoable:** two `editor_undo` after a cover changed nothing. Cover also leaves outline nodes of earlier edits at a
  fixed height (4.15 live, with any cover arguments, also without a map resize); `editor_elevation flatten` clears them.
  `editor_call editor_uiCoverTerrainWithWater` reports them as `verification.residualNodes`.
* Water paint (`paintWater`): a GreekSea stroke changed 441 tiles, 33/34 path samples matched. Single dabs from the
  yaw-45 camera landed ≈(+6,+6) world units from the target with ≈12 bytes changed; unresolved, strokes are unaffected.

## Brush size (`editor_edit_mode.brushSize`)

* `uiChangeBrushSize` handler `0x689820` → editor method `0x536960`: active brush shape `[editor+0x540]` (null in mode
  None), kind int `+0x14` (jump table: 0/3 no-op, 1 one size, 2/5 two sizes, 4 → `0x5365b0`), size float `+0x80`.
  Layout `world.brush` {shapeOffset 1344, typeOffset 20, sizeOffset 128} with signatures at `0x536960`/`0x536a10`.
  (`world.brushOffset` 0x608 is the different paint-selection struct.)
* Live: elevation 4 → `action_key_editor_3` → 3; Paint 1 → key 7 → 7 → `editor_uiChangeBrushSize(5,0,0,0)` → 5;
  elevationsample keeps 4, elevation still 3 — each mode has its own size. Right after an edit-mode switch a size change
  can apply up to ≈1 s late (likely the session's "missed" size keys); re-read `brushSize` to confirm.

## Editor edit mode

* `int32` at `editor+0x4d0`. Code: `0x537730: cmp dword ptr [rcx+0x4d0],0x22; setne al; ret`
  (moveunit=34) and `0x5382c0: cmp dword ptr [rcx+0x4d0],0xe` (PaintCliff=14).
* Live map from `editMode(name)` → value: None 0, Paint 1, PaintLand 2, paintmix 3, paintforest 4,
  terrainDetails 5, elevation 8, elevationsample 9, roughen 10, deleteunits 11, smooth 12,
  paintWater 13, PaintCliff 14, copy 15, editGrass 17, convertunits 18, recalcvariation 19,
  TerrainPaste 22, editWater 24, UnitPaste 25, PlaceUnit 26, PlaceUnitSelect 27, moveunit 34,
  PlaceWall 36, Triggers 39, TrigGroups 40, CameraTracks 42. Unknown names leave the mode unchanged.
* **Observed:** `editMode("None")` while in PlaceUnit (26) moves to PlaceUnitSelect (27) with the
  object palette still open; a second `editMode("None")` reaches 0 and closes it. Exit therefore loops
  until the readback is 0 (bounded).

## Paint selections (verification of setters)

* Terrain texture: `0x538190`/`0x5381e0` (called by `uiSampleTerrainAtPointer`) store
  `[editor+0x608]+0x1c` = type, `+0x20` = subtype. Live: palette "Atlantean Beach 1" → (3,23).
  No console command sets texture by name; only palette clicks / sampling change it.
* Water: `0x539530` (from `uiSetWaterType` handler `0x689bd0`) stores `[editor+0x608]+0x2c`
  (index into water names, bounded by `tm+0xb8`). Live: `uiSetWaterType("GreekLake")` → 31.
* Forest: `0x539ae0` (from `uiSetForestType` handler `0x689d20`) stores `editor+0x678`, bounded by
  `tm+0x88`; names `tm+0x98` pointer array, object `+0x58` UTF-16 name (66 types, 59 = AtlanteanLush).
* Cliff: `0x538290` (from `uiSetCliffType` handler `0x689aa0`) stores `editor+0x638`, bounded by
  `tm+0xa0`; names `tm+0xb0` pointer array, object `+0x158` UTF-16 name (28 types, e.g. "Greek Grass").

## Lighting sets (uiApplyLightingSet index)

* Handler `0x621da0` → `0x8dee30`: `[[base+editorGlobalRva] + 0xf0]` list: `+0` count, `+0x10` pointer
  array; `uiApplyLightingSet(n)` applies element `n`. Lightset object `+0x108` → UTF-16 name
  (662 sets on this build: `04open_bluelight`, …, `aotgotg_autumn`, …, `yas\yas12`).

## Elevation tools (behaviour, not memory)

* `editMode("elevation")`: left click raises (≈+1.06 at the brush center for an 80+60 ms click at the
  default speed slider), right click lowers; brush ≈ 2.2 nodes radius.
* `editMode("elevationsample")`: right click samples the terrain height under the pointer
  (ray hit, interpolated), left click/drag sets every node in the brush disc to exactly that height.
  Undo restores (one undo per stroke).
* Dab footprint (live): a click at world (114,100) set nodes x 110..118 × z 96..104 minus corners — i.e. the
  brush centers on the **lower-left node of the tile under the pointer** (pointer exactly on a node can
  round to the neighbouring tile). Strokes therefore aim at tile middles (+half tile).
* Stale pointer (live, 2026-10-07): after a press/stroke elsewhere, a click that moved via `SetCursorPos` and
  waited 80 ms sampled/painted at the PREVIOUS press position (left click after a sample right click flattened
  the bump instead of the test node; bump clicks after a smooth stroke raised a 33-unit spike at the stroke end).
  250 ms was flaky; one 500 ms wait or two moves with 250 ms settles landed correctly. Host now uses
  `Ui.Settle`/`SettledClick` for elevation presses and drag starts, and aborts bump loops whose anchor does not change.
* Occlusion: a node behind freshly raised terrain (far corner from the camera) cannot be dabbed directly; the
  quantized `GroundHit` back-check accepted such pixels. Touch-ups test the camera ray against node heights and
  aim up to three nodes farther along the camera's horizontal forward (brush disc still covers the node).
* Relief in front (live 2026-10-21): all four bump clicks for anchor (70,190) landed on an older ridge ≈7 units
  toward the camera. Before the first click the host now runs the full ground-hit back-check and refuses with the hit
  point (`TARGET_NOT_CLICKABLE`, nothing sent). Failures after dispatch report `undoSteps` (34 changed nodes restored by
  18 undos, live).
* Steep bump slopes (≈1.8 height per world unit) make `set` sampling pixel-limited (~0.1-0.2 height per pixel);
  `SAMPLE_NOT_CONVERGED` then reports the tried t/measured pairs. Larger tolerance or flatten-from-reference works.

## Unit heading

* Reviewed unit object (LIVE-UNITS) holds a row-major 3×4 transform at `+0x50`; translation is the
  already-reviewed `+0x5c/+0x6c/+0x7c`. Rotation block `m00 +0x50, m02 +0x58, m20 +0x70, m22 +0x78`.
* Live: unrotated objects have block diag(-1,1,-1) (host reports heading 180°). After
  `uiRotateSelection` the block stays orthonormal and translation fixed.
  `headingDegrees = atan2(m02, m22)` normalized to [0,360): temple id 4 → 202.5°.
* `uiRotateSelection` handler `0x683570` → `0x5375c0` selects ±0.39269909 rad (π/8 = 22.5°) by the
  **sign** of `amount` only, and rotates only the first selected unit. Magnitude is ignored.
* Reads refuse when the block is not orthonormal (host tolerance 0.01). Heading is a host derivation,
  not a native getter (`trUnitGetHeading` uses an obfuscated pointer; not used).

## Players

* `0x1bf2550`: `world+0x1e0` count, `world+0x1d8` pointer array of player objects.
* Team `+0xb4` (`0x1bf2a60`), civ `[+0xc0]` first int when `+0xc8 > 0` (`0x1bf27e0`),
  age `+0x178` (`0x1bf26f0`), diplomacy int array `+0x3f0` indexed by target 0..15 (`0x205bf20`:
  1 ally, 2 enemy, 3 neutral; 0 self/unset).
* Display name selection `0x1cd5890`: base `+0x68`, override `+0x80` when `+0xb0>0`, override nonempty,
  `+0x8c>0` and (`+0x8c==2` ? `[override-0x18]>0` : `+0x90>0`).
* Civ names `0x1c5ef70`: civ < 24 → static table RVA `0x5156688` (16-byte entries, ASCII pointer first).
* Resources are add/XOR-obfuscated (`0x1d0a910`) — intentionally **not** decoded; use the
  checkpoint reader `editor_players` for resources.

## UI kind detection (normal vs alternative editor UI)

Pixel gates (not memory): `uilayouts/normal-en-2560x1440.json` holds the normal-UI top menu bar gate,
minimap frame gate and bottom panel gates measured on 2560×1440; `uilayouts/alt-2560x1440.json`
holds the alternative-UI minimap gate. The profile `UserProfile.xml` option
`optionusealternativeeditorui` is reported as a hint only (it can be stale until restart).

## Tools built on these reads (core)

| Tool | Reads / effects |
|---|---|
| `editor_terrain_info` | tile texture/group, water grid, node height, coarse passability (points or area histogram/grid) |
| `editor_live_players` | name/team/civ/age/diplomacy (no resources/minor gods) |
| `editor_edit_mode` | mode readback, enter (`editMode`) / exit loop, UI kind, panels, paint selections |
| `editor_paint_world` | `editMode` + type setter (verified) or texture sampling/palette OCR, `Ui.DragPath` along projected world points, tile/object diff |
| `editor_elevation` | elevation/elevationsample/smooth tools; set = bump + bisected slope sample + test dab + 2 serpentine passes + touch-up dabs |
| `editor_transform_unit` | exact selection, moveunit drag, `uiRotateSelection(±1)` steps verified by heading readback |
| `editor_terrain_catalog` | live texture/water/forest/cliff/lighting/civ tables; generated mixes; edit-mode table |
| `editor_camera` | pose readback (`zoom` = native distance to plane y=0, `groundDistance`, pitch, yaw, target); `cameraMaxZoomSet` / `cameraPitchAngle` / resets with readback verification |
| `editor_capture` | minimap-center on a world area, native zoom loop on projected corners until they fill the viewport minus margin, capture (image or `saveTo` new full-resolution `.png`), restore zoom + center |
| `editor_overview` | screenshot + projected unit IDs; `resolutionScale` 0.1..1 shrinks image (labels drawn after scaling, legend pixels stay full-resolution). Helpers (shipped proto `NotSelectable`+`NotPlayerPlaceable`, or runtime proto absent from proto.xml such as `MythUnitDamageAuraGuardians`) hidden by default (`hiddenHelpers`); `includeHelpers=true` shows cyan cross/ID. Labels stack `id/id`: helper joins nearest regular object within 1.5 world units (live auras drift 0.04..0.62 from hero), same-kind objects within 0.25 share. Live-verified 9 Atalanta+aura pairs |

Live verification (2026-10-07, normal UI, 256×256 map): texture by sampling and by palette OCR (row 127 "Greek Dirt 2"),
mix "Greek Grass 1" (OCR-verified label), water GreekLake, forest GreekOak (29 objects), cliff "Greek Grass";
elevation set 3.0 / 5.0 / 6.5 (all 110 area nodes within 0.05 after touch-up; ring bleed reported), flatten,
smooth; House move (error 0.02) + rotate to 270 (quantized from 186.65) and placement with heading 90 (exact).

## Camera zoom and pitch (live probe 2026-10-13, build 100.19.17020.0, normal UI, 2560×1440)

- Default pose: pitch 47°, yaw 45°, native zoom 75, FOV 0.698 rad.
- `cameraMaxZoomSet(z)` sets the camera distance along the view ray to the **world plane y=0**, exactly and
  unclamped (2, 30, 60, 80, 120, 250 applied in one dispatch). Evidence: zoom 75 and 120 matched
  `position.y / sin(pitch)` within 0.01 while the terrain under the view center was y=4 (terrain-hit distance
  69.53 at zoom 75). The ground pivot does not move. `cameraZoomReset` returns to 75.
- `cameraPitchAngle(a)` sets pitch about the same pivot (60, 70, 89 tested; zoom kept, yaw stayed 45 from the
  default yaw). The terrain point under the view center drifts slightly at steep pitch. `cameraPitchReset` → 47°.
- Mouse wheel (`editor_mouse_wheel` with pointer over the scene): **positive steps zoom in**, 4 units per notch,
  clamped to 29..85, animated. A pose read during the animation fails with "Map/camera changed during query";
  a read ~1.5 s later succeeds. `editor_camera` therefore uses the native command, not the wheel.
- `uiToggleUI` (toggled at zoom 250, screenshot, toggled back, screenshot): both frames identical — top menu bar
  and minimap stayed. No UI-hiding capture option is offered.
- `editor_capture` on a 100×100 area: zoom 75 (extent 7.01) → 250 (clamped, 0.76) → 205.84 (0.98, fits);
  saved 2560×1440 PNG; restore returned zoom 75 and the center within the 3-unit minimap tolerance (~1.8 drift).
  Alternative UI: not live-tested for these two tools; they only use the minimap path of `editor_camera_look_at`,
  which is verified on both UIs, plus native commands.

## Map-building tools (live 2026-10-21, build 100.19.17020.0, normal UI 1600×900 derived layout; alternative UI untested)

Seven core helpers (`src/AomMcp/ShapeTools.cs`, `src/AomMcp/MapDialogTools.cs`); natives, UI input and memory reads
only (no memory writes).

| Tool | Behaviour / live result |
|---|---|
| `editor_project` | world points → pixel, terrain height, water surface, `blockedBy` panel, `clickable`/`why` (same `PixelFor` check as brush tools); pixels → ground hit (terrain under water), `insideMap`, `blockedBy`. Live: (60,60) clickable at (616,280); pixel (800,880) → `blockedBy: objectPalette`; largest UI-free rect [10,70,1570,520]. |
| `editor_brush` | `uiChangeBrushSize(n,0,0,0)` / `uiChangeBrushType(name)` + readback poll ([editor+0x540] +0x80 size, +0x14 type). Live: elevation 3 → 7 and Paint 5 → 3 applied and read back; each edit mode keeps its own size. |
| `editor_stroke` | raise/lower (elevation left/right drag), smooth, roughen, flatten (elevationsample right-click sample, left drag). Live: raise + point mirror (75 nodes up, 2 strokes), lower 2 passes over the fresh ridge (61 down), smooth (25 up/35 down), roughen (17/17); 6 undos restored the map exactly (all nodes back to 4). |
| `editor_terrain_shape` | polygon/circle/ellipse/capsule; 7×7-node sample square (flat square preferred), scanline elevation-sample strokes over the brush-eroded mask, touch-ups, optional water. Live: 56×38 rectangle back to 3 (532/532 nodes exact, 0/318 ring nodes changed, 4 undo steps); circle r16 at 8 + point mirror (197/197 nodes each at 8.23 with tolerance 0.3, 164 land tiles each); concave L polygon at 8 (369/369 nodes, 320 land tiles). |
| `editor_land_analysis` | land = passability class land; 8-connected without diagonal corner cutting; Dijkstra path length, widest-path bottleneck, path-distance Voronoi. Live acropolis seed 11 (135×135 tiles): one land component, TownCenters connected, path 232.3 vs straight 222.4, bottleneck 24.6 at (165,173), territory 8726/8720 tiles (balance 0.999). |
| `editor_map_size` | World > Map Size dialog (`gadgetReal("AMapSizeDialog")`), all four fields retyped (END + backspaces + digits, OCR-verified), Add/Take, live tile dims polled. Live: add right 20 → 138×128, take right 20 → 128×128, add left 10 → 133×128 (island centroid x 61.9 → 71.9, so left grows −X and shifts content), take left 10 → back. |
| `editor_new_scenario` | File > New Scenario (`gadgetReal("NewScenarioGadget")`): type by OCR list scan (scrolls to the top until rows stop changing), players by row position (see below), seed typed, size radio, Generate. Live: Blank Normal 2 → 128×128; acropolis seed 11 Normal 2 → 135×135; yellow_river seed 5 Large 3 → 191×191 (27 list scrolls); Blank players 4/10/2 → live player count 5/11/3. |

Facts found while building them:

* Drags shorter than ~400 ms painted nothing; `PaintChunks` now uses ≥ 800 ms per chunk (`MinChunkDurationMs`).
* Ray back-check misses: after a raise pass the stroke targets sit behind a ridge or in a trench; the ray hit
  missed by 4–6 world units. `editor_stroke` now projects each instance on its pre-stroke mean height (as flatten
  uses the sample height); `PixelFor` refines the pixel up to 4 times by the screen offset between target and hit.
* Auto zoom-out: the engine clamped zoom (camera height stayed ~95) and `Screen` looped forever; it now stops when a
  zoom step does not increase the zoom and after 4 steps.
* A shape's 7×7 sample square on relief (older bumps) broke the bump anchor pick; the host prefers a flat square.
* Sampling a steep bump for height 8 did not converge within 0.05 (8.10–8.36); tolerance 0.3 converged.
* Undo applies a frame or more later: a terrain read right after six quick undos still showed a 10.02 peak; the
  same area read flat a moment later. Re-read after undo before judging it.
* New Scenario: random maps leave editor mode (black loading screen; `Game.Editor` refuses, stale pointers fail
  ReadProcessMemory) for ~10–20 s. Blank → Blank at the same size reuses the terrain object, stays in editor mode and
  kept the camera; only the live player count (players + Gaia) proved it. The dialog resets players to 2 after
  Generate but keeps the last type.
* New Scenario OCR: the dropdown arrow is read as a trailing "t" ("Blank" → "blankt"), so the field text is read
  without the arrow strip. Single digits in the open players list are misread (6→"9", 7→"", 9→"6") while the closed
  field reads reliably, so players are picked by row position (24.8 px pitch at 900 px height, the field rect includes
  the label; the list starts ~30% in) and read back. A single-digit seed ("5") OCRs as "n": seeds are refused only
  when the field reads a different number, otherwise reported `seedVerified:false`.
* The catalog size radio-set rect (469..517 at 900 px) covers only Normal and half of Large; rows are at y 490/519/548.
