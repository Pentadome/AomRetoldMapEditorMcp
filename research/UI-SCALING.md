# Editor UI scaling across window sizes (R2)

Live captures 2026-10-22, build `100.19.17020.0` (SHA-256 `dd15d1d8…4fff`), windowed, Settings → Window Size **and** Resolution set to the same value (Window Size alone can leave the render resolution behind; at 1920×1200 it stayed 1440×900 until set explicitly). Both UIs captured at **1440×900** and **1920×1200** (16:10), compared against the reviewed 2560×1440 / 1920×1080 references (`uilayouts/*-2560x1440.json`, `.tmp/ptev/*/editor.png`). Raw frames: `%TEMP%\r1\frames\*-{1440x900,1920x1200}.png` (not retained in repo).

**Re-enter the editor after a resolution change.** Directly after Apply the scene UI keeps stale positions (minimap centre y 738 instead of 762 at 1440×900). Return to Main Menu → Editor yields the layout measured below.

## Model

Reference space is 2560×1440. Let `W×H` be the client size.

| Element class | Transform | Evidence |
|---|---|---|
| Top menu/toolbar (normal), top-right buttons and Active Player box (alt), bottom tool bar (alt), Paint/Object palettes and tool panels (both), Player Data / Players Settings / Diplomacy dialogs | **Per-axis stretch**: `x' = x·W/2560`, `y' = y·H/1440` | Gate fits: normal `playerData` 14/14 (both sizes), `toolPanel` 8/8, `objectPalette` 11/11, `editorScene.detect` 13–14/15; alt `paintPalette` 5/5, `objectPalette` 4/4, `objectToolBar` 6–7/8; region template error alt topButtons 8–15 stretch vs 25 uniform, activePlayer 16–19 vs 36–38. Alt Players Settings 1920×1200: row 1 centre y 334 (stretch 335, uniform 302), row step 68 (stretch 67). |
| Minimap (both UIs) | **Uniform** `s = min(W/2560, H/1440)`, anchored **bottom-right**: `x' = W − (2560 − x)·s`, `y' = H − (1440 − y)·s` | Interior colour (142,184,52) bbox: 1440×900 centre (1302,762) half 110; 1920×1200 centre (1737,1017) half 147 — both exactly predicted (s = 0.5625 / 0.75). Gate fit uniform 8/8 vs stretch 3–4/8. |
| Dropdown menus (File, Scenario, alt "…") | Fixed-height rows near the anchoring button, not part of the gate model | Rows ~31 px (normal) / ~40 px (alt) at 1920×1200. Menus are only clicked by item offset, never gated. |
| Game Settings dialog (main menu / editor) | Width-limited uniform, centred | 1440×900 dialog = 0.75× the 1920 one; same relative position at 1920×1200. Not used by tools. |

16:9 sizes are the special case `W/2560 = H/1440`, where both transforms coincide; that is why the previous height-only derivation worked for 1280..2560 16:9 clients.

## Matching tolerance at derived sizes

Stretch at non-integer factors resamples thin features. Misses seen with the stretch model:

- Diplomacy (normal) 1920×1200: 13/15 vs needed 14. The two 1-px separator lines (`y=944` ref) blend to (110,110,110)-ish, error 17 > tolerance 14; Close button offset ≈5 px in y.
- `editorScene.detect` 13/15 at 1920×1200 (needs 12) and 14/15 at 1440×900.
- Minimap ornament needs the existing frame tolerance 30.

So derived gates keep the neighbourhood radius (2 px in the 1280-wide frame) and the reference `minimumMatches`, but dialogs that rely on thin lines may fall one short; derived dialog gates therefore allow `minimumMatches − 1` (never below 3/4 of the points) and writers stay best effort (`layoutReviewed:false`).

## Live verification of the derived layouts (2026-10-22, build 100.19.17020.0)

All results report `layoutReviewed:false` plus the `bestEffort` note. Every writer was reverted afterwards.

| Size / UI | Verified |
|---|---|
| 1920×1200 normal | `editor_ui_state` uiKind normal; `camera_look_at` 1 click (residual 0.95); `place_at_world` errors 0.007/0.025; OCR presets `players.1.name`/`players.2.food`/`players.2.civ` read correctly; `editor_player_settings` apply P2 food 200→201→200 verified by checkpoint; `editor_set_diplomacy` apply P1→P2 verified; `paint_world kind=texture` (palette selection by OCR, 25 tiles changed). |
| 1920×1200 alternative | ui_state, camera (residual 2.4, 1 click), placement 0.026, texture paint (31 tiles), OCR presets incl. `players.1.control`, Players Settings writer and Diplomacy writer verified. `editor_dialog_state` reported the alt Diplomacy overlay through the normal signature file (`uiKind:"normal"` from signatureMatch) — detection only, not used by writers. |
| 1440×900 alternative | ui_state, camera (residual 0.65), placement 0.012, texture paint (24 tiles), `players.2.food` OCR, Players Settings writer (after fix below), alt Diplomacy writer verified. |

Fix found live: the alternative Players Settings title gate region `[0,22,420,74]` (2560 ref) clipped "Settings" at 1440×900 (glyphs scale with height there) and OCR read "Plavers Settir". The region now extends to x=600 (empty title bar) and title gates on unreviewed sizes accept letter edit distance ≤2.

## Not established

- Agepopup and file browser dialogs at 16:10 were not opened (stretch assumed, same XML panel family).
- Normal UI at 1440×900 was captured (R2) but writers not run there.
- 21:9, 4K, 1280×800 and other sizes not captured; the model is applied as best effort (`layoutReviewed:false`).
