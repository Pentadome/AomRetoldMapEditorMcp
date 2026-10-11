# Writing XS and AI personalities

Moved verbatim from AGENTS.md; served by `editor_help topic=xs`.

- Writing AI/random-map/trigger XS: look up syscalls and shipped library functions with core `editor_xs_api` (name/filter/kind/context/library). Repo ships signatures + own summaries only (`xs/`); official syscall help and source comments come from the local install at query time. Library functions need `include` relative to `game/random_maps` or `game/ai`. Maintainer refresh after game updates: `--build-xs-api xs/xs_api.json` (summaries in `xs/xs_summaries.json` keyed `library/[Class.]name`).
- Custom computer-player `.xs` personalities belong in profile `<id>\Game\AI` (e.g. `C:\Users\<user>\Games\Age of Mythology Retold\<id>\Game\AI`); `editor_install_ai` writes there. Stock scripts/includes stay in read-only `INSTALLPATH\game\ai`; profile `<id>\ai` (without `Game`) did NOT work. Trigger files belong in profile `<id>\trigger`. XS runtime, attack waves and reload persistence remain unverified by tools.

## Related tools added later

- `editor_xs_diagnostics` reads the game's own MythLog compile errors (authoritative); `editor_xs_check` is an offline heuristic lint (calibrated against stock AI; empty result proves nothing).
- `editor_xs_api` accepts `names[]` (batch exact lookup), `kind=constant` (names scanned from the exe, unverified as XS-visible), `detail`, `fields`, `saveTo`.
- `editor_install_ai` accepts `destinationRoot=campaign` (profile <id>/Game/AI/campaign/<subdir>/*.xs; replacing an existing file is the `force` flow with backup) and `lineEndings`.
