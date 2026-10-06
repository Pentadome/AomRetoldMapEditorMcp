# Passive selection and map/view fields

Reviewed only for accepted executable hash `dd15d1d838e78faa1bc9854becc3994f4f3a4548ef30efd24108abedc1b84fff`. `selection`/`map` layout sections remain optional and absent on unreviewed candidates. No dispatcher getter invocation or acknowledgement is treated as data.

## Constant provenance (JSON cannot contain comments)

Offsets below are recovered byte members, RVAs are module-relative runtime instruction addresses, not an ABI/API contract. Signature lengths are reviewed instruction windows, not function sizes.

### Selection

- `uiLookAtSelection` at RVA `0x6214f0` loads existing editor game global, computes selection slot `(player + 0x6c) * 16`: base `+0x6c0`, stride **16** bytes.
- Current-player helper `0x1b1080` checks editor flag; editor branch `0x1b10f0` returns **1**, independent of cursor placement owner.
- Holder `+0x10` points to group. `0x4b5932`/`0x4b5990` read group count `+8` and records pointer `+0x18`; records are **16** bytes.
- Record kind `+8` equal to **0** is a simulation object; full ID at `+12` is masked/bounds-checked and compared against object `+0x1d8` by native group code. Nonzero kinds are not guessed as unit references.
- Joining uses `LiveUnits.Read`, with full generation-bearing IDs and independent object guards.

### Map/terrain

- `kbGetMapXSize`/`kbGetMapZSize` targets `0x1bfbca0`/`0x1bfbd00`: command context `+8` world mirror, terrain pointer `+0x260`, tile X/Z `+0xc`/`+0x10`, world scale `+0x1c`.
- Vertex X/Z are `+0x14`/`+0x18`, confirmed by terrain height virtual getter at `0x1fd9d80`.
- `trGetTerrainHeight` `0x207a162` multiplies world X/Z by terrain inverse scale `+0x20`, truncates nonnegative coordinates and dispatches to height getter.
- Height getter adds terrain `+0x78` for inline grid, then `0x3d5d70` reads scalar count `+0`, float-array pointer `+0x10`, row stride `+0x2c`. Index is `tileX * stride + tileZ`; Z-fast, not inferred row order.
- Height query deliberately matches quantized native getter. No claim of bilinear surface or rendered-triangle collision intersection.

### Active camera and render projection

- Native `fov` command `0x5f81f0` proves game object's **active** camera pointer `+0xd8`, camera FOV radians `+0xb0`. Default camera `+0xd0` can differ; it is not used by map tool.
- Pose vector offsets `+0` position, `+0x10` forward, `+0x20` up, `+0x30` right recovered from camera pitch/zoom math (`0x19ebf0`, `0x19ec80`, `0x167e9d0`) and cross-validated as finite orthonormal basis.
- `0x163d650` proves render-state global RVA `0x5429b30`, viewport X/Y/width/height `+0x2418/+0x241c/+0x2420/+0x2424`.
- `0x163c694` stores untransposed projection's four rows at `+0x1340/+0x1350/+0x1360/+0x1370`. Tool reads actual matrix, not a guessed FOV/aspect formula.
- Matrix constructor `0x25c5f50` proves Direct3D left-handed row-major perspective convention: homogeneous W from positive view Z, clip X/Y `[-1,1]`, depth `[0,1]`.
- Project uses measured basis/matrix; screen inverse uses standard `Matrix4x4.Invert`. Returned ray starts at camera. World point requires caller's **explicit** horizontal `planeY`; no guessed terrain hit. Frustum visibility does not prove no occlusion or UI coverage.

## Host policy and checks

`EditorView.cs` names/annotates selection/grid/offset ceilings, AMD64/binary32 widths and numerical tolerances. Matrix/vector dimensions, homogeneous coordinate constants and Direct3D clip normalization are mathematical conventions. Self-test numbers are deliberately synthetic selection kinds/IDs and a 90-degree square perspective camera, not game constants.

Fresh analyzer/XML build and self-tests passed; local protocol smoke sees **875 tools** after these two additions. Passive MCP checks on PID **24876** found empty current selection (no selection changes made), map **128×128 tiles**, **256×256 world units**, scale **2**, sampled height **4** at `(79,89)`. Native matrix projected `(79,4,89)` to client `(851.19495,758.808)`; inverse ray intersected caller plane Y=4 back at `(79,4,89)` within 0.001 world unit. Evidence: `research/live-view-evidence.json`.

No scene, selection, viewport, focus or save mutation for these checks. Nonempty selection paths have synthetic/code-source checks, not a new live selection test. All fields/read roots are version-pinned; unsupported or changing state refuses rather than returning invented data.
