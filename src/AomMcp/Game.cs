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

    /// <summary>Cross-process per-game lock name prefix (Win32 Local\ = this session); one MCP server drives a game at a time.</summary>
    const string LockPrefix = @"Local\AomEditorMcp-Game-";

    /// <summary>Wait for another server's call on the same game before refusing (host choice: longer than typical batches).</summary>
    const int LockTimeoutMs = 30_000;

    /// <summary>Held cross-process lock (recursive for nested connections on the same thread); null after dispose.</summary>
    Mutex? _lock;

    /// <summary>Managed thread that acquired <see cref="_lock"/> (mutexes are thread-affine).</summary>
    int _lockThread;

    /// <summary>Process-wide count of game-affecting actions (dispatched native commands, clicks, key presses, wheel,
    /// drags; not pointer moves). Calls are serialized, so a zero delta over a failed call proves nothing reached the game.</summary>
    static long _effects;

    /// <summary>Current effect count (see <see cref="CountEffect"/>).</summary>
    internal static long Effects => Interlocked.Read(ref _effects);

    /// <summary>Records one game-affecting action before or as it is sent.</summary>
    internal static void CountEffect() => Interlocked.Increment(ref _effects);

    /// <summary>True when the lock was taken over from a server that exited while holding it.</summary>
    public bool LockAbandoned { get; private set; }

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
        try
        {
            Process = pid.HasValue ? Process.GetProcessById(pid.Value) : FindProcess();
        }
        catch (ArgumentException)
        {
            throw new GameConnectionException(
                "GAME_NOT_RUNNING",
                $"No process with --pid {pid}; no operation executed.",
                "Start the offline scenario editor and restart the MCP server with the right --pid (or without --pid), then retry."
            );
        }
        try
        {
            var image =
                Process.MainModule?.FileName
                ?? throw new GameConnectionException(
                    "GAME_NOT_RUNNING",
                    "Game main module unavailable; no operation executed."
                );
            if (
                !string.Equals(
                    Path.GetFullPath(image),
                    Path.GetFullPath(exe),
                    StringComparison.OrdinalIgnoreCase
                )
            )
                throw new GameConnectionException(
                    "GAME_MISMATCH",
                    "Process does not match configured game executable; no operation executed.",
                    "Start the configured game executable or pass --exe/--pid for the running one, then retry."
                );
            if (Layout.Hash(image) != layout.ExeSha256)
                throw new GameConnectionException(
                    "GAME_MISMATCH",
                    "Game build changed; regenerate layout. No operation executed.",
                    "Regenerate layout/catalog for this game build (see README), then retry."
                );
            Layout = layout;
            Base = Process.MainModule!.BaseAddress;
            // Windows SDK: PROCESS_QUERY_INFORMATION (0x400) | PROCESS_VM_READ (0x10).
            Handle = Win.OpenProcess(0x410, false, Pid); // Query/read only, never debugger or memory-write access.
            if (Handle.IsInvalid)
            {
                Handle.Dispose();
                throw new GameConnectionException(
                    "GAME_NOT_RUNNING",
                    "Cannot query/read game; no operation executed.",
                    "Run the MCP server at the same privilege level as the game, then retry."
                );
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
                throw new GameConnectionException(
                    "GAME_NOT_RUNNING",
                    "Game window not found (still starting or closing?); no operation executed."
                );
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
                throw new GameConnectionException(
                    "GAME_MISMATCH",
                    "Runtime dispatcher signature mismatch; regenerate layout. No operation executed.",
                    "Regenerate layout/catalog for this game build (see README), then retry."
                );
            }
        }
        catch
        {
            Process.Dispose();
            throw;
        }
        AcquireLock();
    }

    /// <summary>Takes the per-game cross-process lock so two MCP servers (sub-agents, second clients) never send input or
    /// native commands to one game at once; calls inside one server were already serialized.</summary>
    /// <exception cref="GameConnectionException">GAME_BUSY after <see cref="LockTimeoutMs"/>; nothing was sent.</exception>
    void AcquireLock()
    {
        var mutex = new Mutex(false, LockPrefix + Pid);
        try
        {
            if (!mutex.WaitOne(LockTimeoutMs))
            {
                mutex.Dispose();
                Handle.Dispose();
                Process.Dispose();
                throw new GameConnectionException(
                    "GAME_BUSY",
                    $"Another MCP server has been using this game for over {LockTimeoutMs / 1000} s; no operation executed.",
                    "Wait for the other agent/client to finish (or stop it), then retry."
                );
            }
        }
        catch (AbandonedMutexException)
        {
            LockAbandoned = true; // Previous holder exited mid-call: acquired, but its last call may be incomplete.
        }
        _lock = mutex;
        _lockThread = Environment.CurrentManagedThreadId;
    }

    static Process FindProcess()
    {
        // Installed Steam executable AoMRT_s.exe, without .exe as required by GetProcessesByName.
        var candidates = Process.GetProcessesByName("AoMRT_s");
        if (candidates.Length == 1)
            return candidates[0];
        foreach (var p in candidates)
            p.Dispose();
        throw new GameConnectionException(
            "GAME_NOT_RUNNING",
            candidates.Length == 0
                ? "No running game (AoMRT_s) found; no operation executed."
                : $"{candidates.Length} running games found, expected one; no operation executed.",
            candidates.Length == 0
                ? null
                : "Close the extra game instances or restart the MCP server with --pid, then retry."
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
    /// <exception cref="NotInEditorException">The game is not in scenario-editor mode.</exception>
    /// <exception cref="InvalidOperationException">Editor context is unavailable.</exception>
    public nint Editor()
    {
        var game = Pointer(Base + checked((int)Layout.EditorGlobalRva));
        // Recovered one-byte mode flag: 1 means editor; see accepted layout and LIVE-FINDINGS.md.
        if (game == 0 || Read(game + checked((int)Layout.EditorFlagOffset), 1)[0] != 1)
            throw new NotInEditorException();
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
        Validate(requireEditor);
        if (Win.IsIconic(Window))
            _ = Win.ShowWindow(Window, 9); // winuser.h SW_RESTORE=9; returns previous visibility, not success.
        if (Win.GetForegroundWindow() == Window)
            return;
        var previous = Win.GetForegroundWindow();
        TryForeground();
        // Host activation budget: 25 polls x 20 ms = 500 ms; chosen for queued WM_ACTIVATE,
        // not a guarantee that Windows will grant foreground access (live focus tests).
        for (var i = 0; i < 25 && Win.GetForegroundWindow() != Window; i++)
            System.Threading.Thread.Sleep(20); // WM_ACTIVATE can arrive after SetForegroundWindow returns.
        // Bounded backoff (total extra wait < 1 s) before any input is sent; two more activation attempts.
        var attempts = 1;
        foreach (var backoff in FocusBackoffMs)
        {
            if (Win.GetForegroundWindow() == Window)
                break;
            attempts++;
            TryForeground();
            for (
                var waited = 0;
                waited < backoff && Win.GetForegroundWindow() != Window;
                waited += 20
            )
                System.Threading.Thread.Sleep(20);
        }
        if (Win.GetForegroundWindow() != Window && Assertive)
            attempts += TryAssertive();
        if (Win.GetForegroundWindow() != Window)
            throw new FocusRefusedException(attempts, ForegroundProcessName());
        // Focus lease (assertive mode only): the first window we took focus from gets it back on Dispose.
        if (Assertive && _leaseWindow == 0 && previous != 0 && previous != Window)
            _leaseWindow = previous;
    }

    /// <summary>Checks the window still belongs to the connected process/thread (and optionally the editor) without focusing.</summary>
    /// <param name="requireEditor">True when an active editor context is required.</param>
    /// <exception cref="InvalidOperationException">Window identity or mode is invalid.</exception>
    public void Validate(bool requireEditor = true)
    {
        if (
            Process.HasExited
            || Win.GetWindowThreadProcessId(Window, out var owner) != Thread
            || owner != Pid
        )
            throw new InvalidOperationException(
                "Game window/process changed; no operation executed."
            );
        if (requireEditor)
            _ = Editor();
    }

    /// <summary>Environment variable selecting the focus policy.</summary>
    public const string FocusVariable = "AOM_MCP_FOCUS";

    /// <summary><see cref="FocusVariable"/> value enabling the extra focus tiers and the focus lease.</summary>
    public const string AssertiveFocus = "assertive";

    /// <summary>True when <see cref="FocusVariable"/> is <see cref="AssertiveFocus"/> (read per call; default off).</summary>
    public static bool Assertive =>
        string.Equals(
            Environment.GetEnvironmentVariable(FocusVariable),
            AssertiveFocus,
            StringComparison.OrdinalIgnoreCase
        );

    /// <summary>Process names treated as terminals by the minimise tier (Windows Terminal, console host, shells).</summary>
    static readonly string[] TerminalProcesses =
    [
        "WindowsTerminal",
        "conhost",
        "OpenConsole",
        "pwsh",
        "powershell",
        "cmd",
    ];

    /// <summary>Unassigned virtual key VK_F24 (winuser.h 0x87) tapped so this process counts as the last input source.</summary>
    const ushort UnassignedKey = 0x87;

    /// <summary>winuser.h INPUT_KEYBOARD.</summary>
    const uint KeyboardInput = 1;

    /// <summary>winuser.h KEYEVENTF_KEYUP.</summary>
    const uint KeyUpFlag = 2;

    /// <summary>AMD64 sizeof(INPUT) (see <see cref="Win.Input"/>).</summary>
    const int InputSize = 40;

    /// <summary>winuser.h SW_MINIMIZE.</summary>
    const int MinimizeCommand = 6;

    /// <summary>winuser.h SW_RESTORE.</summary>
    const int RestoreCommand = 9;

    /// <summary>Per-tier activation wait: polls of <see cref="PollMs"/> (host choice, same budget as the base tier).</summary>
    const int TierPolls = 25,
        PollMs = 20;

    /// <summary>Window that held the foreground before this connection took it (assertive mode); restored on Dispose.</summary>
    nint _leaseWindow;

    /// <summary>True when the minimise tier minimised <see cref="_leaseWindow"/>; Dispose restores it first.</summary>
    bool _leaseMinimized;

    /// <summary>Opt-in focus tiers (AOM_MCP_FOCUS=assertive): SwitchToThisWindow, F24 tap + SetForegroundWindow, minimise a terminal.</summary>
    /// <returns>Number of tiers tried.</returns>
    /// <remarks>The F24 tap goes to whichever window has focus (no game input); input to the game still requires it to be foreground.</remarks>
    int TryAssertive()
    {
        var tiers = 0;
        bool Wait()
        {
            for (var i = 0; i < TierPolls && Win.GetForegroundWindow() != Window; i++)
                System.Threading.Thread.Sleep(PollMs);
            return Win.GetForegroundWindow() == Window;
        }
        tiers++;
        Win.SwitchToThisWindow(Window, true);
        if (Wait())
            return tiers;
        tiers++;
        TapUnassigned();
        TryForeground();
        if (Wait())
            return tiers;
        var foreground = Win.GetForegroundWindow();
        var name = ForegroundProcessName();
        if (
            foreground == 0
            || name is null
            || !TerminalProcesses.Contains(name, StringComparer.OrdinalIgnoreCase)
        )
            return tiers;
        tiers++;
        _ = Win.ShowWindow(foreground, MinimizeCommand);
        _leaseWindow = foreground;
        _leaseMinimized = true;
        TryForeground();
        if (!Wait())
            Win.SwitchToThisWindow(Window, true);
        _ = Wait();
        return tiers;
    }

    /// <summary>Taps VK_F24 so Windows treats this process as the last input source (foreground-lock exemption).</summary>
    /// <remarks>No application binds F24; the tap reaches whichever window currently has keyboard focus.</remarks>
    static void TapUnassigned()
    {
        Win.Input[] tap =
        [
            new()
            {
                Type = KeyboardInput,
                Key = new Win.Key { Vk = UnassignedKey },
            },
            new()
            {
                Type = KeyboardInput,
                Key = new Win.Key { Vk = UnassignedKey, Flags = KeyUpFlag },
            },
        ];
        _ = Win.SendInput((uint)tap.Length, tap, InputSize);
    }

    /// <summary>Drops the focus lease so Dispose leaves the game foreground (explicit editor_focus requests).</summary>
    public void KeepFocus()
    {
        _leaseWindow = 0;
        _leaseMinimized = false;
    }

    /// <summary>Gives the foreground back to the leased window (restoring it if the terminal tier minimised it); best effort.</summary>
    void ReleaseLease()
    {
        var window = _leaseWindow;
        _leaseWindow = 0;
        if (window == 0 || !Win.IsWindow(window))
            return;
        if (_leaseMinimized)
            _ = Win.ShowWindow(window, RestoreCommand);
        if (Win.GetForegroundWindow() != Window)
            return; // Someone else took the foreground meanwhile; leave it.
        // Live 2026-10-13: a plain SetForegroundWindow back to Windows Terminal was refused (foreground lock);
        // the F24 tap first makes it succeed.
        TapUnassigned();
        Activate(window);
        for (var i = 0; i < TierPolls && Win.GetForegroundWindow() != window; i++)
            System.Threading.Thread.Sleep(PollMs);
        if (Win.GetForegroundWindow() != window)
            Win.SwitchToThisWindow(window, true);
    }

    static readonly int[] FocusBackoffMs = [150, 400];

    static string? ForegroundProcessName()
    {
        try
        {
            _ = Win.GetWindowThreadProcessId(Win.GetForegroundWindow(), out var pid);
            using var other = Process.GetProcessById((int)pid);
            return other.ProcessName;
        }
        catch (Exception e)
            when (e
                    is ArgumentException
                        or InvalidOperationException
                        or System.ComponentModel.Win32Exception
            )
        {
            return null;
        }
    }

    void TryForeground() => Activate(Window);

    /// <summary>SetForegroundWindow, retried with this thread's input queue attached to the current foreground thread.</summary>
    /// <param name="target">Window to activate.</param>
    static void Activate(nint target)
    {
        if (!Win.SetForegroundWindow(target))
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
                _ = Win.SetForegroundWindow(target);
            }
            finally
            {
                if (attached)
                    Win.Check(Win.AttachThreadInput(own, foreground, false), "Detach focus queues");
            }
        }
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
        if (_lock is { } held)
        {
            _lock = null;
            // A release from another thread is impossible (thread-affine); the OS then frees it when that thread ends.
            if (_lockThread == Environment.CurrentManagedThreadId)
                held.ReleaseMutex();
            held.Dispose();
        }
        ReleaseLease();
        Handle.Dispose();
        Process.Dispose();
    }
}

/// <summary>Connecting to the game failed before any input or native dispatch (no/ambiguous process, wrong build, no window).</summary>
public sealed class GameConnectionException(string code, string message, string? nextAction = null)
    : InvalidOperationException(message)
{
    /// <summary>Stable failure code: GAME_NOT_RUNNING or GAME_MISMATCH.</summary>
    public string Code { get; } = code;

    /// <summary>What the caller should do before retrying.</summary>
    public string NextAction { get; } =
        nextAction ?? "Start the offline scenario editor (or pass --pid), then retry.";
}

/// <summary>Game window did not become foreground; thrown before any input or native dispatch.</summary>
public sealed class FocusRefusedException(int attempts = 1, string? foregroundProcess = null)
    : InvalidOperationException(
        "Game could not receive focus after "
            + attempts
            + " attempt(s); no input sent."
            + (
                foregroundProcess is null
                    ? ""
                    : " Foreground window belongs to '"
                        + foregroundProcess
                        + "'; close or minimize it and retry once."
            )
    )
{
    /// <summary>Number of activation attempts made before giving up.</summary>
    public int Attempts { get; } = attempts;

    /// <summary>Name of the process that held the foreground when focus was refused, if known.</summary>
    public string? ForegroundProcess { get; } = foregroundProcess;
}

/// <summary>The game is not in scenario-editor mode (memory flag read); thrown before any editor command or input.</summary>
public sealed class NotInEditorException()
    : InvalidOperationException("Not in scenario editor. No operation executed.");
