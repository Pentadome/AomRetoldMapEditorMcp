# Triggers

Moved verbatim from AGENTS.md; served by `editor_help topic=triggers`.

- `editor_triggers` structured support is strictly verified TR v12 single Always/CodeSnippet controller. Local patch requires new output/confirmWrite; apply requires confirmDestructive and retains original backup/round-trip files. Unknown shapes refuse; serialization equality is not XS compilation/effect proof.
- Native `uiLoadTriggers` / `uiSaveTriggers` / `saveScenario` receive filename stems, not suffixed filenames: game appends `.trg` / `.mythscn`. Raw trigger import/export require confirmDestructive. Unknown structured original triggers need separately authorized native backup restoration; helper apply does not guess their shapes.

## Related tools added later

- `editor_trigger_edit`: edits on the same trigger in one batch are merged (original numbering); only real conflicts refuse. `outputPath:"auto"` writes `<stem>.eN.trg`; the result's `next {path, expectedSha256}` chains the next edit. CodeSnippet replacements (ValueType 72) may be long and multi-line.
- `editor_trigger_elements` looks up the condition/effect catalog and lists byte-copy exemplars (use `duplicates`/`copyEffects`); creating elements from the catalog is not supported (binary layout unproven).
- Bulky readers accept `fields` and `saveTo` (new .json path); results over 16 KB carry a `truncationHint`.
