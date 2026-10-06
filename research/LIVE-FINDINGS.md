# Live editor follow-up

## Current result: MCP + reversible live map editing

Production server implemented in `src/AomMcp/` with `native/EditorBridge.cpp`: 864 generated command/action/helper tools. Greek villager placement succeeded in smoke bridge; Hoplite placement succeeded through MCP and visibly disappeared/restored through undo/redo. Final undo left original villager only, unsaved. Hook removal, DLL unload, and game responsiveness independently checked.

See [MCP-COVERAGE.md](MCP-COVERAGE.md), [mcp-integration-evidence.json](mcp-integration-evidence.json), and [../README.md](../README.md). Patch generator recovered current layout exactly but unknown builds require review. Native return acknowledgements are not semantic success or getter values; broad tool surface is not blanket live validation.

## Historical echo result

At the user's request, invoked the registered `trExecuteConsoleCommand` native wrapper at RVA `0x2062ec0` once on window thread `20844`, passing:

```text
echo("AOM_MCP_PROBE_16ce9d63")
```

The wrapper returned normally; hook removal succeeded; a subsequent module enumeration found no probe DLL still loaded. The game remained responsive and readable. No scenario edits or executable patches were performed.

**Important limit:** the current build's registered `echo` target at RVA `0x5f2ed0` is `c2 00 00` / `ret 0`, both before and after this test. It is a no-op. No marker appeared in the inspected logs. This verifies native wrapper invocation and normal return, **not independently confirmed parser acceptance, console output, or working map editing**.

Evidence: [command-evidence.json](command-evidence.json).

The updated bridge uses a 40-byte v2 mapping and an explicit `--echo` opt-in. The only supported engine action is this fixed ASCII echo, with a nonce suffix; no caller-provided command text or RVA is accepted. Host pins the executable hash; native code checks the exact runtime prefix, live context, and owning thread before calling. Atomic in-progress status prevents duplicate execution. Local tests cover identity guards, one-shot status, refusal outside the game, and refusal of unknown operations. Native and managed warnings-as-errors builds and self-tests passed; LSP remains inconclusive.

After building as described below, run from repository root with the **current** game PID:

```powershell
dotnet run --project research/HookSmoke --no-build -- .\research\HookSmoke\bridge\EditorProbe.dll <current-pid> --echo
```

This echo-phase proof was later superseded by independently observed placement/undo/redo and production MCP integration. Save/reload, broad action behavior, and stress/reentrancy remain unvalidated. The sections below preserve earlier identity-only investigation.

## Initial inert result

**Native injection works in this session without debugger attachment.** An identity-only DLL executed inside `AoMRT_s.exe` on the actual game-window thread. Windows hook removal succeeded, the DLL was subsequently absent from the game's module list, and the game remained responsive.

This proves DLL loading and window-thread code delivery, **not yet safe invocation of engine methods or a working scenario-editing MCP server**. No scenario was edited, no game configuration/file changed, no engine function called, and no executable patched. Protection was not disabled or bypassed.

The user opened the editor and requested autonomous continuation without questions. Testing remained limited to passive reads and the inert hook handshake; the open scenario was left alone.

## Runtime memory evidence

Build: `100.19.17020.0`; same executable hash as the initial investigation. Session PID: `20864`.

- Ordinary query/read access succeeded; no native debugger attached.
- Startup entry sample matched disk.
- The exported constructor sample differed: disk began `9d b3 5c 40 ...`; live code began `33 c0 c5 f9 ef c0 ...` and decoded as normal instructions.
- Successfully read **63,722,476 bytes** of live `.text`.
- `.text` entropy dropped from **7.7562** on disk to **6.6316** in memory.
- Previously absent editor-name cross-references appeared in readable runtime code.

This strongly supports runtime transformation of protected on-disk code. It does not identify a DRM vendor or establish that every code region is available permanently. Runtime reads alone did not require a protection-bypass implementation.

Evidence: [live-evidence.json](live-evidence.json), [runtime-candidates.json](runtime-candidates.json).

## Recovered paths

All values below are **RVAs for the inspected hash**, not portable addresses. No listed engine method was invoked.

| Name/path | Candidate RVA | Basis |
|---|---:|---|
| Common command registration | `0x24fe310` | Shared call target after loading command name and native target |
| `saveScenario` native target | `0x5f5f30` | Address passed in R8 during registration |
| `uiPlaceAtPointer` native target | `0x689010` | Address passed in R8 during registration |
| `uiChangeBrushSize` native target | `0x689820` | Address passed in R8 during registration |
| `trExecuteConsoleCommand` native target | `0x2062ec0` | Reconstructed local-slot assignment feeding R8 during registration |
| Command executor | `0x1bb2fa0` | Called by `trExecuteConsoleCommand` wrapper |
| Thread/context helper | `0x1bb57a0` | Called before command execution; imports resolve to `GetCurrentThreadId` |
| Command-context global | `0x542bd10` | RIP-relative load in wrapper; context and field at offset 8 non-null in editor |

The generic adapter at `0x1d1610` is only `mov rcx, [r8]; jmp rdx`; it is **not** the dispatcher. Confusing script adapters, registration functions, and engine targets would produce the wrong call interface.

The wrapper receives a pointer in RCX, tests its global context, and forwards the pointer in RDX to the executor. The executor reads input bytes and calls `MultiByteToWideChar` with code page **0 / CP_ACP**, not explicitly UTF-8. Initial proof commands should be ASCII. This narrows the ABI investigation but is not a complete callable contract.

The context helper compares the current thread ID against the DWORD at context offset 4 and an alternate global thread ID before a conditional vtable call. It was not shown to reject every wrong-thread invocation; do not treat it as a safety guard for arbitrary remote threads.

## Window-thread match

Observed game window:

```text
Class:             MythRetold
HWND:              0x6065a
Window thread:     20844
Context offset +4: 20844
```

This makes a Windows window-thread delivery route attractive. **Same thread does not prove a frame-safe/reentrancy-safe point for editing the world.** Engine calls from a hook still require separate validation.

## Inert injection proof

Implementation: [HookSmoke/](HookSmoke/).

- .NET host loads our own native DLL locally and resolves exported `ProbeHook`.
- `SetWindowsHookExW(WH_CALLWNDPROC, ..., gameWindowThread)` lets Windows map the DLL into the target process.
- A registered message prompts a nonce-checked, PID/thread-checked handshake through a 32-byte local memory mapping.
- The DLL reports its actual thread ID and module base; it does not call the game.
- Host removes the hook in `finally`, sends `WM_NULL` to check responsiveness, and verifies the game remains running.
- A separate read-only module enumeration confirmed **zero `EditorProbe.dll` instances still loaded in the game**.

No `WriteProcessMemory`, `VirtualAllocEx`, `CreateRemoteThread`, detour, debugger attachment, persistent subclass, or executable patch was used. Windows-supported thread hooks supplied the in-process execution route.

Observed handshake:

```text
Target PID:          20864
Expected/actual TID:  20844 / 20844
Injected DLL base:   0x7ffcb9800000
Hook removed:        true
Window responsive:   true
Game still running:  true
Engine calls/edits:  0 / 0
```

Evidence: [hook-evidence.json](hook-evidence.json). These identifiers are session-specific and must not be reused after a restart. Immediate survival does not prove long-term stability, every protection configuration, or acceptability under game/platform terms. No multiplayer use was tested or proposed.

## Build and checks

No new .NET packages. Native compilation uses the installed MSVC x64 toolchain, statically linked CRT, and `user32.lib`.

From an **x64 Native Tools Command Prompt**:

```bat
cd research\HookSmoke\bridge
cl /nologo /LD /O2 /MT /W4 /WX EditorProbe.cpp /link user32.lib /INCREMENTAL:NO /OUT:EditorProbe.dll
```

From repository root:

```powershell
dotnet build research/HookSmoke
dotnet run --project research/HookSmoke --no-build -- .\research\HookSmoke\bridge\EditorProbe.dll --self-test
# Identity-only injection; substitute the CURRENT game PID, not the recorded one.
dotnet run --project research/HookSmoke --no-build -- .\research\HookSmoke\bridge\EditorProbe.dll <current-pid>
```

Native and managed builds passed with warnings treated as errors for native code. Local self-tests cover CWPSTRUCT layout, ignored negative hook codes, PID/thread/magic/nonce guards, and a successful native handshake inside the probe's own process. The separate live hook test passed. LSP was inconclusive; compiler and executable checks supply validation.

## Initial decision after the inert pass

**Go for a small native window-thread bridge with an out-of-process .NET MCP host.** Debugger failure is not a blocker for this demonstrated route. The obfuscated/protected disk image also did not prevent bounded runtime discovery.

Remaining gate: validate one harmless command's ABI, execution result, and safe timing through the bridge. Then validate undo/save/reload on a disposable scenario before exposing editing tools. Build-pin signatures, timeouts, typed/validated requests, mode guards, current-user IPC restrictions, and clean disconnect remain necessary. Do not expose arbitrary RVAs or unrestricted console text to an MCP client.

The initial delivered probe was identity-only. The later fixed-echo extension and its narrower evidence are documented at the top of this file. No MCP scaffold or scenario-editing implementation has been added.
