using System.ComponentModel;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Host-defined ASCII "AOMP" numeric tag and private v2 handshake name, not game/Win32 IDs;
// must match research/HookSmoke/bridge/EditorProbe.cpp Magic/MessageName (distinct from production ABI).
const uint magic = 0x414f4d50;
const string messageName = "AomEditorProbe.Handshake.v2";
// Observed Steam installation and inspected build SHA-256; see research/hook-evidence.json.
const string gameExe =
    @"C:\Program Files (x86)\Steam\steamapps\common\Age of Mythology Retold\AoMRT_s.exe";
const string gameHash = "dd15d1d838e78faa1bc9854becc3994f4f3a4548ef30efd24108abedc1b84fff";
if (!Environment.Is64BitProcess || args.Length is < 2 or > 3)
    throw new ArgumentException(
        "Usage (x64): HookSmoke <EditorProbe.dll> <game-pid | --self-test> [fixed-action]"
    );
// Private probe operation IDs defined in EditorProbe.cpp Result/RunOperation:
// 0=identity only, 1=echo, 2=prepare Greek villager, 3=place once, 4=clear preview/finish.
var operation =
    args.Length == 2
        ? 0u
        : args[2] switch
        {
            "--echo" => 1u,
            "--prepare-unit" => 2u,
            "--place-unit" => 3u,
            "--finish-unit" => 4u,
            _ => throw new ArgumentException(
                "Unknown action; only --echo, --prepare-unit, --place-unit, --finish-unit supported."
            ),
        };
var dll = Path.GetFullPath(args[0]);
var message = Native.RegisterWindowMessage(messageName);
Check(message != 0, "RegisterWindowMessage");
var library = NativeLibrary.Load(dll);
try
{
    var callback = NativeLibrary.GetExport(library, "ProbeHook");
    var selfTest = args[1] == "--self-test";
    Require(
        !selfTest || operation == 0,
        "Editor actions require a verified game process, not --self-test."
    );
    using var process = selfTest
        ? Process.GetCurrentProcess()
        : Process.GetProcessById(int.Parse(args[1]));
    var pid = checked((uint)process.Id);
    var tid = selfTest ? Native.GetCurrentThreadId() : 0;
    nint window = 0;
    if (!selfTest)
    {
        var image =
            process.MainModule?.FileName ?? throw new InvalidOperationException("No game module.");
        Require(
            string.Equals(Path.GetFullPath(image), gameExe, StringComparison.OrdinalIgnoreCase),
            "Wrong executable."
        );
        Require(
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(image))) == gameHash,
            "Unknown game build."
        );
        Native.EnumWindowsCallback findWindow = (hwnd, _) =>
        {
            var ownerTid = Native.GetWindowThreadProcessId(hwnd, out var ownerPid);
            if (ownerPid == pid)
            {
                // Host-sized buffer for observed retail window class MythRetold, not its localized title.
                var name = new StringBuilder(256);
                Check(Native.GetClassName(hwnd, name, name.Capacity) > 0, "GetClassName");
                if (name.ToString() == "MythRetold")
                {
                    window = hwnd;
                    tid = ownerTid;
                }
            }
            return true;
        };
        Check(Native.EnumWindows(findWindow, 0), "EnumWindows");
        Require(window != 0 && tid != 0, "Game window not found.");
    }
    // Private Result nonce is a DWORD (4 random bytes); Local\\ scopes Windows mapping to session,
    // decimal PID/x8 nonce match EditorProbe.cpp's %lu/%08lx format. Result totals 40 bytes.
    var nonce = BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4));
    var name = $"Local\\AomEditorProbe-v2-{pid}-{nonce:x8}";
    using var mapping = MemoryMappedFile.CreateNew(name, 40);
    using var view = mapping.CreateViewAccessor();
    Reset(tid);
    view.Write(32, operation);
    if (selfTest)
    {
        // Windows SDK AMD64 CWPSTRUCT=32 bytes (LPARAM/WPARAM/UINT/padding/HWND); not Result size.
        Require(Marshal.SizeOf<Native.CallWindowMessage>() == 32, "CWPSTRUCT layout.");
        var hook = Marshal.GetDelegateForFunctionPointer<Native.HookCallback>(callback);
        var data = Marshal.AllocHGlobal(32);
        try
        {
            var input = new Native.CallWindowMessage
            {
                LParam = (nint)(long)nonce,
                WParam = magic,
                Message = message,
            };
            Marshal.StructureToPtr(input, data, false);
            // Win32 negative hook codes must be ignored/forwarded; altered IDs/nonces test refusals.
            hook(-1, 0, data);
            Require(view.ReadUInt32(12) == 0, "Negative hook code must be ignored.");
            Reset(tid + 1);
            hook(0, 0, data);
            Require(view.ReadUInt32(12) == 0, "Wrong thread must be ignored.");
            Reset(tid);
            input.WParam = magic + 1;
            Marshal.StructureToPtr(input, data, false);
            hook(0, 0, data);
            Require(view.ReadUInt32(12) == 0, "Wrong magic must be ignored.");
            input.WParam = magic;
            Marshal.StructureToPtr(input, data, false);
            view.Write(4, pid + 1);
            hook(0, 0, data);
            Require(view.ReadUInt32(12) == 0, "Wrong PID must be ignored.");
            Reset(tid);
            view.Write(8, nonce ^ 1u);
            hook(0, 0, data);
            Require(view.ReadUInt32(12) == 0, "Wrong nonce must be ignored.");
            Reset(tid);
            hook(0, 0, data);
            Require(
                view.ReadUInt32(12) == 1
                    && view.ReadUInt32(20) == tid
                    && view.ReadUInt64(24) == (ulong)library,
                "Local native handshake failed."
            );
            view.Write(32, 2u);
            hook(0, 0, data);
            Require(
                view.ReadUInt32(12) == 1 && view.ReadUInt32(36) == 0,
                "Completed request must not run again."
            );
            for (uint action = 1; action <= 4; action++)
            {
                Reset(tid);
                view.Write(32, action);
                hook(0, 0, data);
                Require(
                    view.ReadUInt32(12) == 1 && view.ReadUInt32(36) >= 2,
                    "Every engine action must be refused outside game."
                );
            }
            Reset(tid);
            view.Write(32, 99u); // Deliberately invalid fixture outside supported operation IDs 0..4.
            hook(0, 0, data);
            Require(
                view.ReadUInt32(12) == 1 && view.ReadUInt32(36) == 2,
                "Unknown operation must be refused."
            );
            Console.WriteLine(
                "Self-test passed: layout, PID/thread/magic/nonce guards, handshake, one-shot status, engine/operation refusal."
            );
        }
        finally
        {
            Marshal.FreeHGlobal(data);
        }
        return;
    }
    var installed = Native.SetWindowsHookEx(4, callback, library, tid); // WH_CALLWNDPROC
    Check(installed != 0, "SetWindowsHookEx");
    var removed = false;
    try
    {
        Check(
            Native.SendMessageTimeout(
                window,
                message,
                magic,
                (nint)(long)nonce,
                0x0001 | 0x0002, // Windows SDK SMTO_BLOCK | SMTO_ABORTIFHUNG.
                5000, // Host handshake deadline: 5 seconds, not engine timing requirement.
                out _
            ) != 0,
            "Handshake message"
        );
        Require(
            view.ReadUInt32(12) == 1 && view.ReadUInt32(20) == tid && view.ReadUInt64(24) != 0,
            "No matching in-game handshake."
        );
        if (operation != 0)
            Require(
                view.ReadUInt32(36) == 1,
                $"Requested action refused (code {view.ReadUInt32(36)}: 2 unknown, 3 module, 4 prefix, 5 context/thread, 6 formatting, 7 foreground, 8 editor, 9 placement state)."
            );
    }
    finally
    {
        removed = Native.UnhookWindowsHookEx(installed);
        Check(removed, "UnhookWindowsHookEx");
    }
    // WM_NULL=0 is Win32's harmless responsiveness probe; host gives it 2 seconds.
    Check(
        Native.SendMessageTimeout(window, 0, 0, 0, 0x0001 | 0x0002, 2000, out _) != 0,
        "Post-probe window responsiveness"
    );
    process.Refresh();
    Require(!process.HasExited, "Game exited during probe.");
    Console.WriteLine(
        JsonSerializer.Serialize(
            new
            {
                pid,
                expectedThreadId = tid,
                actualThreadId = view.ReadUInt32(20),
                injectedModuleBase = $"0x{view.ReadUInt64(24):x}",
                window = $"0x{window:x}",
                handshake = true,
                hookRemoved = removed,
                windowResponsive = true,
                gameStillRunning = true,
                operation,
                // Fixed sequences in EditorProbe.cpp: prepare issues 3 calls, finish 2, echo/place 1.
                engineCalls = view.ReadUInt32(36) == 1
                    ? (
                        operation == 2 ? 3
                        : operation == 4 ? 2
                        : 1
                    )
                    : 0,
                command = operation == 1 ? $"echo(\"AOM_MCP_PROBE_{nonce:x8}\")" : null,
                dispatcherReturned = operation != 0 && view.ReadUInt32(36) == 1,
                consoleOutputVerified = false,
                retailEchoIsNoOp = operation == 1,
                executablePatches = 0,
                placementRequested = operation == 3,
                scenarioEdits = operation == 3 ? (int?)null : 0, // Actual placement needs independent verification.
            },
            new JsonSerializerOptions { WriteIndented = true }
        )
    );

    void Reset(uint expectedTid)
    {
        // Private Result byte offsets (EditorProbe.cpp): magic/PID/nonce/status=0/4/8/12,
        // expected/actual thread=16/20, 64-bit moduleBase=24, operation/result=32/36.
        // Status 0=pending, -1=claimed, 1=finished; result 0=no action, 1=returned, >=2=refused.
        view.Write(0, magic);
        view.Write(4, pid);
        view.Write(8, nonce);
        view.Write(12, 0u);
        view.Write(16, expectedTid);
        view.Write(20, 0u);
        view.Write(24, 0ul);
        view.Write(32, 0u);
        view.Write(36, 0u);
        view.Flush();
    }
}
finally
{
    NativeLibrary.Free(library);
}

static void Check(bool ok, string operation)
{
    if (!ok)
        throw new Win32Exception(Marshal.GetLastWin32Error(), operation);
}
static void Require(bool ok, string message)
{
    if (!ok)
        throw new InvalidOperationException(message);
}

/// <summary>Windows interop for the temporary window-thread hook smoke probe.</summary>
static class Native
{
    /// <summary>Native CWPSTRUCT payload observed by the smoke-probe hook.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct CallWindowMessage
    {
        internal nint LParam;
        internal nuint WParam;
        internal uint Message;
        internal nint Window;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint HookCallback(int code, nuint wParam, nint lParam);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal delegate bool EnumWindowsCallback(nint window, nint parameter);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    [DllImport(
        "user32.dll",
        EntryPoint = "RegisterWindowMessageW",
        CharSet = CharSet.Unicode,
        SetLastError = true
    )]
    internal static extern uint RegisterWindowMessage(string name);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetWindowThreadProcessId(nint window, out uint pid);

    [DllImport(
        "user32.dll",
        EntryPoint = "GetClassNameW",
        CharSet = CharSet.Unicode,
        SetLastError = true
    )]
    internal static extern int GetClassName(nint window, StringBuilder name, int maxCount);

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    internal static extern nint SetWindowsHookEx(
        int hook,
        nint callback,
        nint module,
        uint threadId
    );

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    internal static extern nint SendMessageTimeout(
        nint window,
        uint message,
        nuint wParam,
        nint lParam,
        uint flags,
        uint timeout,
        out nuint result
    );
}
