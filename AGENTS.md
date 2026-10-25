# Project work guidance

- Native `loadScenario` tool and shipped expressions referencing it are removed: alternative-UI test crashed game after this call. Full mode/confirmation cannot re-enable it. Normal Load Scenario UI successfully loaded the same edited checkpoint and restored original backup after restart. Wait for loading to finish before scene reads; editor flag/acknowledgement alone does not prove readiness. Core alternative-UI flows tested, not full coverage or an alternative-specific crash cause; see `research/ALTERNATIVE-UI.md`.
- New UI-driving tools must support BOTH normal and alternative UI: detect UI by pixel gates, report `uiKind`, keep per-UI geometry in `uilayouts/`, live-verify each UI (or state untested one explicitly), never fail closed on one UI by design.
- Custom computer-player `.xs` personalities belong in profile `<id>\Game\AI` (e.g. `C:\Users\<user>\Games\Age of Mythology Retold\<id>\Game\AI`); `editor_install_ai` writes there. Stock scripts/includes stay in read-only `INSTALLPATH\game\ai`; profile `<id>\ai` (without `Game`) did NOT work. Trigger files belong in profile `<id>\trigger`. XS runtime, attack waves and reload persistence remain unverified by tools.

- Long-running tool loops should call `ProgressReporter.Report(done, total, message)`: the server turns it into MCP progress notifications every 10 s when the client sent a progressToken (resets client timeouts). Tool calls run on a `Task.Run` thread.

- All Methods, Types and properties should have XML-doc comments, even if they are non-public.
- Non-private fields should also have XML-doc comments

- Avoid magic numbers, except for -1, 0, 1 and 2. Assign the number to a const instead. If the number has a source, cite the source.
- Avoid magic strings. Except empty string. Assign the string to a const instead. If the string has a source, cite the source.