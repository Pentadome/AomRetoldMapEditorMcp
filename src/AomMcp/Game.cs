using System.Diagnostics;
using Microsoft.Win32.SafeHandles;

namespace AomMcp;

/// <summary>Owns a query/read-only connection to a hash- and signature-validated game process.</summary>
internal sealed class Game : IDisposable
{
    /// <summary>Gets the connected game process.</summary>
    public Process Process { get; }
    /// <summary>Gets the process handle with query and memory-read access only.</summary>
    public SafeProcessHandle Handle { get; }
    /// <summary>Gets the main module's runtime base address.</summary>
    public nint Base { get; }
    /// <summary>Gets the game window handle.</summary>
    public nint Window { get; }
    /// <summary>Gets the thread ID that owns the game window.</summary>
    public uint Thread { get; }
    /// <summary>Gets the connected process ID.</summary>
    public uint Pid => checked((uint)Process.Id);
    /// <summary>Gets the accepted build-specific addresses and guards.</summary>
    public Layout Layout { get; }

    /// <summary>Finds the game and validates executable identity, build hash, window, and dispatcher signature.</summary>
    /// <param name="exe">Expected game executable path.</param>
    /// <param name="layout">Accepted layout pinned to the executable hash.</param>
    /// <param name="pid">Explicit process ID, or null to require exactly one running game.</param>
    public Game(string exe, Layout layout, int? pid = null)
    {
        Process = pid.HasValue ? Process.GetProcessById(pid.Value) : FindProcess();
        try
        {
            var image =
                Process.MainModule?.FileName
                ?? throw new InvalidOperationException("Game main module unavailable.");
            if (
                !string.Equals(
                    Path.GetFullPath(image),
                    Path.GetFullPath(exe),
                    StringComparison.OrdinalIgnoreCase
                )
            )
                throw new InvalidOperationException(
                    "Process does not match configured game executable."
                );
            if (Layout.Hash(image) != layout.ExeSha256)
                throw new InvalidOperationException("Game build changed; regenerate layout.");
            Layout = layout;
            Base = Process.MainModule!.BaseAddress;
            // Windows SDK: PROCESS_QUERY_INFORMATION (0x400) | PROCESS_VM_READ (0x10).
            Handle = Win.OpenProcess(0x410, false, Pid); // Query/read only, never debugger or memory-write access.
            if (Handle.IsInvalid)
            {
                Handle.Dispose();
                throw new InvalidOperationException("Cannot query/read game.");
            }
            nint window = 0;
            uint thread = 0;
            Win.Enumerate callback = (hwnd, _) =>
            {
                var tid = Win.GetWindowThreadProcessId(hwnd, out var owner);
                if (owner == Pid)
                {
                    // Host-sized GetClassNameW buffer; "MythRetold" is the observed retail window class
                    // recorded in research/LIVE-FINDINGS.md, not the localized window title.
                    var name = new char[256];
                    var length = Win.GetClassName(hwnd, name, name.Length);
                    if (length > 0 && new string(name, 0, length) == "MythRetold")
                    {
                        window = hwnd;
                        thread = tid;
                    }
                }
                return true;
            };
            Win.Check(Win.EnumWindows(callback, 0), "EnumWindows");
            if (window == 0)
            {
                Handle.Dispose();
                throw new InvalidOperationException("Game window not found.");
            }
            Window = window;
            Thread = thread;
            if (
                !Read(Base + checked((int)layout.DispatcherRva), layout.Prefix.Length)
                    .AsSpan()
                    .SequenceEqual(layout.Prefix)
            )
            {
                Handle.Dispose();
                throw new InvalidOperationException(
                    "Runtime dispatcher signature mismatch; regenerate layout."
                );
            }
        }
        catch
        {
            Process.Dispose();
            throw;
        }
    }

    static Process FindProcess()
    {
        // Installed Steam executable AoMRT_s.exe, without .exe as required by GetProcessesByName.
        var candidates = Process.GetProcessesByName("AoMRT_s");
        if (candidates.Length == 1)
            return candidates[0];
        foreach (var p in candidates)
            p.Dispose();
        throw new InvalidOperationException(
            "Expected one running game. Start offline scenario editor, or specify --pid."
        );
    }

    /// <summary>Reads an exact byte range from the connected process without modifying memory.</summary>
    /// <param name="address">Absolute runtime address.</param>
    /// <param name="size">Byte count from zero to 100000000.</param>
    /// <returns>Bytes read; unreadable or partial ranges throw.</returns>
    public byte[] Read(nint address, int size)
    {
        // Host allocation ceiling: 100 MB covers inspected ~64 MB .text; not an engine object size.
        if (size < 0 || size > 100_000_000)
            throw new ArgumentOutOfRangeException(nameof(size));
        var data = new byte[size];
        Win.Check(
            Win.ReadProcessMemory(Handle, address, data, (nuint)size, out var read)
                && read == (nuint)size,
            "ReadProcessMemory"
        );
        return data;
    }

    /// <summary>Reads a little-endian 64-bit pointer from process memory.</summary>
    /// <param name="address">Absolute address containing the pointer.</param>
    /// <returns>Pointer value stored at the address.</returns>
    // AMD64 process ABI: pointer = 8 bytes; DWORD/uint = 4 bytes (UInt below).
    public nint Pointer(nint address) => (nint)BitConverter.ToUInt64(Read(address, 8));

    /// <summary>Reads a little-endian unsigned 32-bit value from process memory.</summary>
    /// <param name="address">Absolute address containing the value.</param>
    /// <returns>Unsigned value stored at the address.</returns>
    public uint UInt(nint address) => BitConverter.ToUInt32(Read(address, 4));

    /// <summary>Requires scenario-editor mode and resolves the live editor context.</summary>
    /// <returns>Non-null editor context pointer.</returns>
    /// <exception cref="InvalidOperationException">Editor mode or context is unavailable.</exception>
    public nint Editor()
    {
        var game = Pointer(Base + checked((int)Layout.EditorGlobalRva));
        // Recovered one-byte mode flag: 1 means editor; see accepted layout and LIVE-FINDINGS.md.
        if (game == 0 || Read(game + checked((int)Layout.EditorFlagOffset), 1)[0] != 1)
            throw new InvalidOperationException("Not in scenario editor. No operation executed.");
        var editor = Pointer(game + checked((int)Layout.EditorPointerOffset));
        if (editor == 0)
            throw new InvalidOperationException("Editor context unavailable.");
        return editor;
    }

    /// <summary>Reads editor, placement, window, thread, and build state without sending input.</summary>
    /// <returns>A serializable snapshot; requires an active editor context.</returns>
    public object State()
    {
        var editor = Editor();
        Win.Check(Win.GetClientRect(Window, out var rect), "GetClientRect");
        var context = Pointer(Base + checked((int)Layout.ContextRva));
        return new
        {
            connected = true,
            pid = Pid,
            windowThread = Thread,
            editor = true,
            ownerThread = context == 0 ? 0 : UInt(context + checked((int)Layout.OwnerOffset)),
            // Recovered signed IDs use -1 (DWORD 0xffffffff) for no placement selection.
            placementProtoId = unchecked((int)UInt(editor + checked((int)Layout.ProtoOffset))),
            placementPlayer = unchecked((int)UInt(editor + checked((int)Layout.PlayerOffset))),
            foreground = Win.GetForegroundWindow() == Window,
            width = rect.Right,
            height = rect.Bottom,
            build = Layout.FileVersion,
            buildHash = Layout.ExeSha256,
        };
    }

    /// <summary>Validates window ownership and focuses the game, waiting for activation.</summary>
    /// <param name="requireEditor">True for editor operations; false permits read-only playtest screenshots.</param>
    /// <exception cref="InvalidOperationException">Window identity, mode, or foreground activation is invalid.</exception>
    public void Focus(bool requireEditor = true)
    {
        if (Process.HasExited || Win.GetWindowThreadProcessId(Window, out var owner) != Thread
            || owner != Pid)
            throw new InvalidOperationException("Game window/process changed; no operation executed.");
        if (requireEditor)
            _ = Editor();
        if (Win.IsIconic(Window))
            _ = Win.ShowWindow(Window, 9); // winuser.h SW_RESTORE=9; returns previous visibility, not success.
        if (Win.GetForegroundWindow() == Window)
            return;
        if (!Win.SetForegroundWindow(Window))
        {
            // Host buffer 64 bytes covers AMD64 MSG (48); HWND/filter zeros mean all thread messages,
            // PM_NOREMOVE=0 initializes this thread's input queue before AttachThreadInput (Win32).
            _ = Win.PeekMessage(new byte[64], 0, 0, 0, 0);
            var own = Win.GetCurrentThreadId();
            var foreground = Win.GetWindowThreadProcessId(Win.GetForegroundWindow(), out _);
            var attached =
                foreground != 0
                && foreground != own
                && Win.AttachThreadInput(own, foreground, true);
            try
            {
                _ = Win.SetForegroundWindow(Window);
            }
            finally
            {
                if (attached)
                    Win.Check(Win.AttachThreadInput(own, foreground, false), "Detach focus queues");
            }
        }
        // Host activation budget: 25 polls x 20 ms = 500 ms; chosen for queued WM_ACTIVATE,
        // not a guarantee that Windows will grant foreground access (live focus tests).
        for (var i = 0; i < 25 && Win.GetForegroundWindow() != Window; i++)
            System.Threading.Thread.Sleep(20); // WM_ACTIVATE can arrive after SetForegroundWindow returns.
        if (Win.GetForegroundWindow() != Window)
            throw new InvalidOperationException("Game could not receive focus; no input sent.");
    }

    /// <summary>Focuses the editor and moves the pointer within its client rectangle.</summary>
    /// <param name="x">Horizontal client-pixel coordinate, not a scaled screenshot coordinate.</param>
    /// <param name="y">Vertical client-pixel coordinate, not a world coordinate.</param>
    public void Move(int x, int y)
    {
        Focus();
        Win.Check(Win.GetClientRect(Window, out var r), "GetClientRect");
        if (x < 0 || y < 0 || x >= r.Right || y >= r.Bottom)
            throw new ArgumentOutOfRangeException(nameof(x), "Point outside game client.");
        var point = new Win.Point { X = x, Y = y };
        Win.Check(Win.ClientToScreen(Window, ref point), "ClientToScreen");
        Win.Check(Win.SetCursorPos(point.X, point.Y), "SetCursorPos");
    }

    /// <summary>Releases the process handle and process wrapper without closing the game.</summary>
    public void Dispose()
    {
        Handle.Dispose();
        Process.Dispose();
    }
}
