# AoM Retold live scenario-editor MCP: viability investigation

## Verdict

**Latest: live unit placement and undo/redo independently verified. Production .NET stdio MCP implemented: 864 generated command/action/helper tools, guarded native window-thread backend, UI fallback, and review-only patch-layout generator. Scenario left unsaved.**

See [../README.md](../README.md), [MCP-COVERAGE.md](MCP-COVERAGE.md), and [mcp-integration-evidence.json](mcp-integration-evidence.json) for current implementation and limits. Older investigation statements below describe earlier phases.

See [LIVE-FINDINGS.md](LIVE-FINDINGS.md), [live-evidence.json](live-evidence.json), [runtime-candidates.json](runtime-candidates.json), [hook-evidence.json](hook-evidence.json), and [command-evidence.json](command-evidence.json). The sections below preserve the initial offline investigation; statements about missing runtime evidence describe that initial pass, not the follow-up.

A .NET MCP server can orchestrate a native bridge into this x64 game. The promising target is the game's existing **editor/console command dispatcher**, not individual undocumented engine methods. Editor menus and hotkeys already route through that command language.

The installed executable contains strong indicators of protected code regions. The live follow-up recovered readable code, command-registration targets, and a dispatcher candidate. An inert native DLL ran through a Windows thread hook, without debugger attachment or executable patches. Pinned-build dispatcher calls now power MCP and reversible placement tests. Complete engine ABI and reentrancy safety across every operation remain unverified.

**Current recommendation:** use guarded MCP only in offline editor, verify effects through state/screenshots, and refuse unknown builds pending layout review. Placement and undo/redo were observed independently; broad generated tool exposure is not proof every operation works. Retail echo remains a no-op. No official supported API or future-patch guarantee is claimed.

## Initial offline scope and reproducibility

Inspected installation:

```text
C:\Program Files (x86)\Steam\steamapps\common\Age of Mythology Retold
```

Build inspected: `AoMRT_s.exe`, file version **100.19.17020.0**, 91,081,056 bytes.

```text
SHA-256: dd15d1d838e78faa1bc9854becc3994f4f3a4548ef30efd24108abedc1b84fff
```

Repository additions:

- `research/Probe/`: dependency-free .NET 10 x64 read-only console probe.
- `research/evidence.json`: probe output for this installation.
- `research/extracted/doxygen/`: local extraction of shipped `doxygen_retail.7z`.
- `research/extracted/editor/`: two editor XML files decoded with CryBar.
- `research/.gitignore`: excludes build output and extracted proprietary resources.

No game files, user configuration, or scenarios were changed. No debugger was attached, game launched, memory written, DLL injected, or protection disabled. Process enumeration found **no running `AoMRT_s.exe`** during investigation; live behavior remains untested.

Commands, from repository root:

```powershell
dotnet build research/Probe
dotnet run --project research/Probe --no-build -- --self-test
dotnet run --project research/Probe --no-build
```

Optional read-only live check, after opening the editor through Steam:

```powershell
# Replace <pid> with the actual AoMRT_s process ID; remove angle brackets.
dotnet run --project research/Probe --no-build -- "C:\Program Files (x86)\Steam\steamapps\common\Age of Mythology Retold\AoMRT_s.exe" <pid>
```

The live check verifies the PID's executable path, requests only query/read access, and reads two 32-byte samples for the inspected build. It does not attach a debugger or inject code. A different build gets only an entry-point sample; the second sample is SHA-256-gated. Different memory bytes are evidence of runtime transformation or other changes, **not by themselves proof of decryption**. Matching samples do not establish that the rest of the module is unprotected. Treat any failed access as a result, not a reason to bypass protection.

Build passed with zero compiler errors/warnings. Embedded self-check passed; an additional check confirmed rejection of a PID belonging to a different executable. LSP returned an inconclusive check, not a confirmed clean result; compiler validation is the usable code-check evidence.

## 1. Binary findings

| Finding | Evidence | Consequence |
|---|---|---|
| Native x64 executable | PE machine `0x8664`/AMD64, PE32+, no CLR directory | No managed reflection/ordinary C# method injection. A normal managed DLL is not a native `LoadLibrary` bridge. |
| ASLR and NX enabled | PE DLL characteristics `0x8160` | Use validated module-relative addresses, not absolute addresses or writable-data execution assumptions. |
| Symbol information limited | No COFF symbols; embedded build PDB path, no matching PDB identified | Source-level method lookup unavailable. The PDB path is not a downloadable symbol source. |
| 280 named exports | Export table parsed | Examined exports were third-party Microsoft telemetry APIs/types, not an exposed editor API. Do not mistake these for game methods. |
| Numerous names survive | Editor commands, `trExecuteConsoleCommand`, help/signature strings, BANG class names in RTTI | Useful semantic anchors survive despite apparent protection. Names are not exported callable addresses. |
| Protected-region indicators | `.text` entropy **7.7562 bits/byte**; multiple 4 MiB chunks approximately 7.8–7.9 | A simple static string-to-function scan is unlikely to be sufficient. Entropy alone would not prove protection. |
| Disassembly supports that concern | Exported `CorrelationVector` constructor at RVA `0x2ddc460` starts `9d b3 5c 40 67 d2 45 81 ...`; sampled decode is implausible constructor code. Startup entry at `0x36f9e70` decodes normally. | Consistent with selectively encrypted/transformed code, not merely stripped symbols. Protection mechanism/vendor not identified. |
| Simple references absent | Narrow/UTF-16 searches plus direct RIP-relative LEA and absolute-pointer searches did not resolve tested editor anchors | No dispatcher/callback recovered by this bounded static pass. Other reference encodings or registration mechanisms remain possible. |
| `IsDebuggerPresent` present | Import/string inspection | Not proof of anti-debugging: CRT/diagnostic code can import it. User's debugger failure was reported, not reproduced here. |

The initial hypothesis of an ordinary stripped native binary became less plausible after entropy and disassembly checks. **The result is not “definitely Arxan” or “definitely Denuvo.”** Neither vendor nor cause of debugger failure was established. Nor was absence of anti-cheat established from missing product-name strings.

## 2. Existing editor command route

These are installation-local facts, not guesses from the original AoM:

- `game/config/editor.con` maps editor keys to `uiSaveScenarioPrompt`, `uiPlaceAtPointer`, `uiChangeBrushSize`, `editMode`, `undo`, `redo`, and other commands.
- `game/config/developer.con` defines console mappings and commands including `saveScenario("QuickSave")` and `loadScenario("QuickSave")`. Shipping a mapping does **not** prove its developer feature is available in retail.
- Decoded `UIDefaultEditor.bar` → `ui_editor_menu.xml` contains the same commands in menu entries and gadgets.
- Decoded `ui_trigger_editor.xml` calls `uiImportTriggers` and `uiExportTriggers`.
- Shipped config documentation describes per-user `user.cfg`, read **on startup**, under `Games\Age of Mythology Retold\<Steam ID>\config`.
- Shipped XS-debugger documentation describes `user.con` remapping and the `toggleXSDebugger` binding. `AIDebug`, `DebugTriggers`, or `DebugRandomMaps` enables that debugger. This is an **XS runtime debugger**, not a native Windows debugger.

Executable documentation strings include:

```text
void saveScenario(string fname): saves out a scenario file.
void loadScenario(string scenarioName): loads in a scenario file.
void uiSetProtoCursor(string proto, bool setPlacement): sets the cursor to a proto-unit.
void uiSetPlacementPlayer(int playerID): sets current placement player to the given player ID
void uiPlaceAtPointer(bool changeVariation): intended for ui use only. Places unit at pointer location.
void uiPaint(bool keyState, bool offset): intended for ui use only. Indicates that the paint button has gone up/down.
void undo(): undoes the last editing operation.
void redo(): Re-does the last undone operation.
```

These establish an internal command vocabulary and existing UI call sites. They do **not** establish C++ callback signatures, argument layouts, dispatcher return semantics, undo behavior for every operation, or arbitrary world-coordinate placement. Some commands are mouse-state-dependent; others open dialogs. A dispatched string is not automatically a completed, deterministic editor transaction.

A minimal non-injected proof can use a user hotkey or UI gadget for a harmless operation, with .NET delivering input while the correct game window has focus. This tests the same command route without native address discovery. It is a fallback and test aid, not equivalent to direct method calls or robust bidirectional IPC. Retail developer-console access and command-file execution/reload remain unverified. **No file watcher, remote console, or external automation socket was found or demonstrated.**

## 3. XS and scenario files: useful, but different runtime

The supplied `xs.vsix` is extension version **1.1.0**. Its `syscalls/syscalls.json` contains **1,807 definitions** covering AI, KB, RM, trigger, main-thread trigger, and XS APIs. The accompanying `.cpp` files are file-marker placeholders, **not implementation source**. Console `.cpp` filenames exist, but the JSON has no console-function category.

Important definitions include:

```text
trExecuteConsoleCommand(string command) -> void
trUnitCreate(string protoName, float x, float y, float z,
             int heading, int playerID, bool skipBirth) -> int
trPaintTerrain(int terrainType, int terrainSubtype,
               float minX, float minZ, float maxX, float maxZ,
               bool updateObstructions) -> void
```

These are XS signatures, not native ABI declarations. `trExecuteConsoleCommand` is present in the current binary and shipped Doxygen. However, **a trigger VM during scenario play is not an editor VM during editing**. Calling a `tr*` wrapper from editor mode might need state that does not exist, and might bypass editor undo/bookkeeping even if it appears to work. The `triggerfuncs_mainthread.cpp` category is another warning against assuming a remote worker can safely invoke everything.

The supplied extension also lists `xsExecute`. That exact name was **absent from this executable's exact-name scan and shipped `xsfuncs_8cpp.html`**. Treat that definition as a version/context mismatch or otherwise unavailable until demonstrated. Do not implement a supposed live XS evaluator around it.

CryBar supports BAR extraction/XMB decoding and scenario/trigger conversion. Verified local CLI syntax:

```powershell
./crybar/crybar.exe bar list "<archive.bar>" --json
./crybar/crybar.exe bar export "<archive.bar>" ui_editor_menu.xml.XMB ui_trigger_editor.xml.XMB --convert --decompress --flat -o "<output-directory>"
./crybar/crybar.exe convert scenario-to-xml --help
./crybar/crybar.exe convert xml-to-scenario --help
```

Scenario XML editing plus explicit editor reload is a credible offline backend. It is **not** live mutation of an already-open scenario. Conversion support is documented; a scenario round-trip and loading/saving the result in this game build were not tested. Back up scenarios and validate round-trip preservation before relying on it.

## 4. Injection architecture, if proof gates pass

```text
MCP client
   │ stdio
.NET MCP server (out of process, typed/validated tools)
   │ local named pipe, restricted to current user
small x64 native bridge inside AoMRT_s.exe
   │ bounded request queue
verified editor/UI-thread execution point
   │ existing console/editor dispatcher
scenario editor
```

The **MCP server stays in .NET**. A small native bridge avoids hosting a full managed runtime in a native game. A NativeAOT shared library is another possible bridge implementation, not a magic solution to locating functions, DLL initialization, native ABI, or thread scheduling.

The hard work is below MCP:

1. Locate the dispatcher/registration path and confirm its ABI. Strings and RTTI can guide this once usable runtime code is observable; no callable RVA is supplied by this investigation.
2. Establish object lifetimes and marshaling. BANG `BString`, native script argument objects, allocator ownership, and implicit `this`/context pointers cannot be replaced blindly with C# strings and integers.
3. Execute on the verified editor-owning thread. The DLL's IPC worker must queue requests, not directly mutate the world. Do not assume the thread receiving Windows messages is also the engine's editor thread.
4. Preserve transactions and editor bookkeeping. Prefer existing editor commands; verify undo, redraw, selection, trigger ownership, and save state.
5. Return an acknowledgment from actual execution plus meaningful result/error. Queue acceptance, input delivery, or console parsing success alone is insufficient.
6. Pin supported builds, validate signatures/context, and refuse unknown builds. Updates, ASLR, protected regions, and changes to registries make hard-coded addresses brittle.

Debugger attachment and injection are different mechanisms. Failure of one does not prove failure of the other. Conversely, DLL loading can still fail because of loader policy, process access, integrity checks, or protection behavior. Avoid treating a hook/patch as harmless: integrity checking may detect it after apparent initial success.

Initial tools should be a small allowlist: status, harmless editor display change, save to an approved new path, then one bounded edit. Do not expose unrestricted native pointer calls or arbitrary console text to the model. Validate strings, coordinates, players, paths, mode, and request size; escape command-language values rather than interpolating untrusted text.

## 5. Proof gates and stop conditions

| Gate | Smallest useful test | Pass condition |
|---|---|---|
| Existing command route | Open editor on a disposable map; execute one reversible editor command via existing UI/hotkey/console if available | Visible state changes correctly; command works in retail editor context |
| Passive access | Run probe with the exact game PID | Query/read works without debugger; compare memory samples with disk and assess usable code |
| Inert bridge | With explicit permission, load a native DLL that only establishes local IPC and reports identity | Game remains stable through editor entry/exit and save; no protection failures |
| Safe dispatch | Recover/validate dispatcher and editor-thread route; execute one harmless command | Execution acknowledgment, correct thread/context, no crashes, no unsafe assumptions |
| Edit semantics | One deterministic edit on a disposable map, undo, save, reload | Correct result and undo; persisted result survives reload without damage |
| MCP wrapper | Only after backend passes, expose the validated operations through .NET MCP SDK | End-to-end typed request/result, timeouts, mode guards, safe disconnect |

These are proposed tests, **not completed milestones**. The probe's offline build/self-tests are completed; none of the injection gates is passed yet.

Use only an offline/local editor session and disposable scenarios. Injection may violate game/platform terms or trigger protection; review applicable terms before proceeding. No multiplayer use or anti-cheat/license-check bypass is part of this plan. If ordinary DLL loading or code access is blocked, stop and assess supported automation rather than silently turning this into a protection-bypass project.

**Decision:** proceed with the offline command-route and passive-memory checks. Broad direct calls into undocumented methods are currently the highest-risk option. One verified dispatcher bridge is the narrowest plausible injection solution; UI command automation is the simpler fallback; CryBar scenario conversion is the non-live fallback.

## Sources

Primary evidence is local and version-specific:

- `AoMRT_s.exe`; metadata and byte samples in `research/evidence.json`.
- `game/config/editor.con`, `developer.con`, `game.con`, `game.cfg`.
- `BANG_Documentation/Config documentation/Config basics.pdf`.
- `BANG_Documentation/XS documentation/XS debugger.pdf`.
- `BANG_Documentation/Trigger documentation/Trigger dev environment setup.pdf` and `What is allowed in TR scripts.pdf`.
- `doxygen_retail.7z`, extracted locally; `xsfuncs_8cpp.html`, `triggerfuncs_8cpp.html`, `triggerfuncs__mainthread_8cpp.html`.
- `xs.vsix`: `extension/package.json`, `extension/syscalls/syscalls.json`, placeholder `.cpp` files.
- `game/ui/UIDefaultEditor.bar`, decoded with supplied CryBar CLI.

External corroboration, not runtime proof:

- [CryBar README and supported conversions](https://raw.githubusercontent.com/CryShana/CryBarEditor/refs/heads/main/README.md).
- [CryBar XS reference](https://raw.githubusercontent.com/CryShana/CryBarEditor/main/Documentation/XSScriptingReference.md), explicitly incomplete and referring users to shipped docs.
- [Existing editor-gadget mod](https://github.com/Skrylas/AoMR_EditorTools), demonstrating community use of editor UI commands.
- [2024 request for in-editor trigger testing](https://forums.ageofempires.com/t/developer-request-return-in-editor-game-testing/260920), historical context only, not a statement of current-build limitations.
