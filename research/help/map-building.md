# Map building: shapes, strokes, brush, projection, land analysis, map size, new scenario

Served by `editor_help topic=map-building`. Live facts: `research/LIVE-WORLD.md` (Map-building tools).

- Start a map with `editor_new_scenario` (confirmDestructive; REPLACES the open scenario — `editor_save_checkpoint`
  first). `type` is Blank or a random map script name (checked against `game/random_maps`), `players` 1..12, `seed`,
  `size` normal|large|giant. Check `verified`; a Blank of equal size and players can be indistinguishable.
- Grow or shrink the map with `editor_map_size operation=add|take` (meters per side, multiples of the tile size:
  right = +X, left = −X and shifts content, top = +Z, bottom = −Z). Not proven undoable: reverse with the opposite
  operation. `operation=get` (default) reads tile dims.
- Plan in world coordinates. `editor_project` tells whether points are clickable now (`blockedBy`, `why`) and maps
  pixels back to the ground (terrain under water).
- Land masses, plateaus, lakes: `editor_terrain_shape` with `shape=polygon|circle|ellipse|capsule`, `height`,
  optional `water=<water type>` and `mirror=point|flipX|flipZ|swapXZ` for symmetric 1v1 maps. Needs a 7×7-node
  square inside the shape (`AREA_TOO_SMALL` otherwise; narrow parts are fine). `edits` = undo count. If sampling
  fails on steep relief (`SAMPLE_NOT_CONVERGED`), use `tolerance` 0.2–0.3.
- Land/water follows the tile corner rule: a tile is water only when a corner is under the water surface. An island
  needs height above the water surface (e.g. 8 on a surface of 6).
- Ridges, valleys, coast shaping: `editor_stroke tool=raise|lower|smooth|roughen|flatten` along a world polyline
  (≤256 points; `passes`, `brushSize`, `mirror`). `strokes` = undo count. Flatten samples `referenceX/Z` (default first
  vertex).
- `editor_brush` reads or sets the brush (`mode`, `size` 1..50, `shape`); each edit mode keeps its own size.
- Check playability with `editor_land_analysis`: land components and specks, per base pair connection, land path
  length, bottleneck width, Voronoi territory balance (default bases: each player's TownCenter), water-corner-rule
  anomalies. Fix specks or unintended bridges before placing units.
- Undo applies a frame or more later; re-read terrain after `editor_undo` before judging the result.
