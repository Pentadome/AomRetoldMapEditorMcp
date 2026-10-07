# World-space scene helpers

Eleven core helpers added after a hands-on editor session showed agents had to hand-project world→pixel, guessed off-map pixels, lost unit IDs after undo/redo and could not move the camera to arbitrary points.

| Tool | Kind | Purpose |
|---|---|---|
| `editor_camera_look_at` | input (camera only) | Center camera on world X/Z via closed-loop minimap clicks, verified from live camera pose. |
| `editor_view_info` | read-only | Camera target, visible ground bounds (corner rays on quantized terrain), UI occluders, safe-click rule. |
| `editor_ui_state` | read-only | Placement cursor, selection count, foreground, camera target, pixel-gated panels (minimap, object palette, object tool bar). |
| `editor_place_at_world` | mutation | Place one object at world X/Z; catalog proto check (suggestions), camera move, occluder avoidance, ray back-check, observed new unitId + positionError. |
| `editor_apply_layout` | mutation (preview default) | 1..32 items or world-unit rows/ring formation; footprint preflight; sequential world placements with per-item observed IDs. |
| `editor_units_snapshot` / `editor_units_diff` | read-only | In-memory baselines (last 16); diff added/removed/moved/changed/health/reidentified. |
| `editor_scene_summary` | read-only | Per-player counts, top protos, TownCenters, centroid/bounds, players lacking TC. |
| `editor_delete_units` | mutation | Exact tuple-verified deletion: select via `uiLookAtAndSelectUnit`, verify exact selection, DELETE key, verify removal, check collateral. |
| `editor_check_footprints` | read-only | Shipped `obstructionradiusx/z` rectangles vs map bounds, planned items, live objects; node-height unevenness. |
| `editor_terrain_grid` | read-only | Node heights over a rectangle (≤512² nodes, ≤4096 returned), stats, flat-site search. |

## Fixes made in the same change

- Catalog freshness no longer compares executable write time; executable identity is the SHA-256 computed at startup plus length. Steam touched the exe mtime (same hash) and blocked every catalog tool. Stale metadata now returns `METADATA_STALE` with `nativeDispatched:false`, `retrySafe:true`.
- Read-only-annotated helpers classify failures as safe inspections (previously "mutation may have occurred").
- Native bridge refusals on single-command tools (`editor_place_unit`, generated native commands, `editor_place_at_world`) return `BRIDGE_REFUSED`, `refusalCode`, `refusedCommand`, `retrySafe:true`; code 10 explains off-map pointer.
- `editor_place_unit` unknown proto (cursor not accepted) returns `UNKNOWN_PROTO` with catalog suggestions.

## Reviewed UI geometry

`uilayouts/alt-2560x1440.json` → `editorScene` (alternative UI, 2560×1440, pinned build): minimap center (2318,1196), half-diagonal 205 px, orientation screen-up = +X+Z, screen-right = +X−Z; static occluders (top buttons, active-player panel, minimap, toolbar); pixel-gated conditional occluders (object palette, object tool bar). Gates sample 1280×720 screenshots. Minimap corner buttons excluded from the gate after hover-highlight changes made it fail. Object tool bar is translucent; its gate uses opaque icons/text. Unreviewed UI: minimap camera refuses; placement only trusts the central 40%×50% of the client.

`uilayouts/normal-en-2560x1440.json` → `editorScene` (normal UI, English, 2560×1440): `uiKind: normal`, `detect` gate = top menu bar (15 samples, ≥12), minimap gate identical to the alternative UI (same frame), static occluders menu bar [0,0,2560,84] + minimap; conditional `toolPanel` [0,1030,636,410] and `objectPalette` (list panel) [636,1030,1364,410]; `palette` geometry for texture/mix list OCR selection. `SceneUi.TryLoad` checks normal first (menu-bar gate) then alternative; results report `uiKind`. `editor_place_at_world` in normal UI always treats both bottom panels as occluders because PlaceUnit opens them; cleanup loops `editMode("None")` until mode 0 (PlaceUnit → 27 → 0) so the palette closes.

## Live evidence (2026-10-06, blank 128×128 scene, alternative UI 2560×1440, build dd15d1d8…)

- `editor_camera_look_at` (128,128) → target (129.45,126.92) after 1 click; (60,200) → (60.70,199.46); (3,3) → (3.30,0.78) residual 2.24 (camera bounds).
- `editor_place_at_world` Hoplite (100,100): camera moved, placed at (99.98,99.99), error 0.026. Temple (40,60) with object palette open: camera moved, error 0.007. Inside `editor_batch` also worked.
- `editor_apply_layout` formation rows 4×Hoplite P2 spacing 3 at (110,90): preview footprints ok; apply placed IDs 4–7, errors 0.012–0.029.
- `editor_units_diff`: four `added` with exact IDs.
- `editor_delete_units`: missing confirm → preflight refusal; wrong player tuple → `UNIT_TUPLE_MISMATCH`, nothing sent; IDs 7 and 6 deleted, no collateral. A stray raw-pixel `editor_place_unit` Hoplite (pixel 2500,50 — over the top-right buttons, yet the bridge accepted map hover) was deleted as ID 262150.
- `editor_place_unit` at pixel (300,1150) beyond map edge → `BRIDGE_REFUSED` code 10, retrySafe.
- Typo `Hopilte` → `UNKNOWN_PROTO` suggestions `Hoplite, Copil` (preflight and live cursor paths).
- `editor_check_footprints` flagged overlap with existing TownCenter, planned Temple/House overlap and unknown proto.

## Limits

Camera targeting depends on reviewed minimap geometry (or the derived layout for other 16:9 clients 1280..2560 wide, `layoutReviewed:false`, live-verified at 1600×900 and 1280×720 in both UIs); camera bounds keep edge targets offset. Footprints are axis-aligned shipped radii (rotation, terrain type, water, build rules ignored). Terrain heights are quantized nodes, not collision. Snapshots live only in host memory. Deletion moves the camera; undo recreates objects with new IDs. Nothing saves the scenario.
