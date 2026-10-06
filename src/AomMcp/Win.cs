using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AomMcp;

/// <summary>Declares Windows interop used for process reads, guarded input, hooks, and GDI capture.</summary>
internal static class Win
{
    // DLL/export names and structure layouts come from the Windows SDK (winuser.h, wingdi.h,
    // processthreadsapi.h, memoryapi.h); W suffixes select UTF-16 APIs, not game-defined names.
    internal static void Check(bool ok, string name)
    {
        if (!ok)
            throw new Win32Exception(Marshal.GetLastWin32Error(), name);
    }

    /// <summary>Native RECT client bounds in pixels.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left,
            Top,
            Right,
            Bottom;
    }

    /// <summary>Native POINT coordinates used for client-to-screen conversion.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        public int X,
            Y;
    }

    /// <summary>Native MOUSEINPUT payload for pointer buttons and wheel events.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Mouse
    {
        public int X,
            Y;
        public uint Data,
            Flags,
            Time;
        public nuint Extra;
    }

    /// <summary>Native KEYBDINPUT payload for scan-code or Unicode keyboard events.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Key
    {
        public ushort Vk,
            Scan;
        public uint Flags,
            Time;
        public nuint Extra;
    }

    /// <summary>AMD64 INPUT union with the native 40-byte layout expected by SendInput.</summary>
    // Win32 AMD64 INPUT: DWORD type at 0, 4 bytes padding, 32-byte union at 8 => total 40.
    // Both MOUSEINPUT and KEYBDINPUT occupy that same union; these are ABI offsets, not RVAs.
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    internal struct Input
    {
        [FieldOffset(0)]
        public uint Type;

        [FieldOffset(8)]
        public Mouse Mouse;

        [FieldOffset(8)]
        public Key Key;
    }

    /// <summary>Native CWPSTRUCT data delivered to the window-thread hook.</summary>
    // winuser.h CWPSTRUCT is 32 bytes on AMD64: LPARAM/WPARAM at 0/8, UINT at 16, HWND at 24.
    [StructLayout(LayoutKind.Sequential)]
    internal struct CallMessage
    {
        public nint LParam;
        public nuint WParam;
        public uint Message;
        public nint Window;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint Hook(int code, nuint wParam, nint lParam);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal delegate bool Enumerate(nint window, nint data);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern SafeProcessHandle OpenProcess(
        uint access,
        [MarshalAs(UnmanagedType.Bool)] bool inherit,
        uint pid
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReadProcessMemory(
        SafeProcessHandle process,
        nint address,
        byte[] data,
        nuint length,
        out nuint read
    );

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll")]
    internal static extern ulong GetTickCount64();

    [DllImport("kernel32.dll")]
    internal static extern uint GetACP();

    [DllImport(
        "user32.dll",
        EntryPoint = "RegisterWindowMessageW",
        CharSet = CharSet.Unicode,
        SetLastError = true
    )]
    internal static extern uint RegisterMessage(string name);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(Enumerate callback, nint data);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint window, out uint pid);

    [DllImport(
        "user32.dll",
        EntryPoint = "GetClassNameW",
        CharSet = CharSet.Unicode,
        SetLastError = true
    )]
    internal static extern int GetClassName(nint window, [Out] char[] name, int size);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    internal static extern int ShowWindow(nint window, int mode);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AttachThreadInput(
        uint from,
        uint to,
        [MarshalAs(UnmanagedType.Bool)] bool attach
    );

    [DllImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PeekMessage(
        byte[] message,
        nint window,
        uint min,
        uint max,
        uint remove
    );

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetClientRect(nint window, out Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ClientToScreen(nint window, ref Point point);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    internal static extern uint MapVirtualKey(uint code, uint mapType);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(uint count, Input[] input, int size);

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    internal static extern nint SetHook(int kind, nint callback, nint module, uint thread);

    [DllImport("user32.dll", EntryPoint = "UnhookWindowsHookEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool Unhook(nint hook);

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    internal static extern nint SendTimeout(
        nint window,
        uint message,
        nuint wParam,
        nint lParam,
        uint flags,
        uint timeout,
        out nuint result
    );

    [DllImport("user32.dll")]
    internal static extern nint GetDC(nint window);

    [DllImport("user32.dll")]
    internal static extern int ReleaseDC(nint window, nint dc);

    [DllImport("gdi32.dll")]
    internal static extern nint CreateCompatibleDC(nint dc);

    [DllImport("gdi32.dll")]
    internal static extern nint CreateCompatibleBitmap(nint dc, int width, int height);

    [DllImport("gdi32.dll")]
    internal static extern nint SelectObject(nint dc, nint obj);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(nint obj);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteDC(nint dc);

    [DllImport("gdi32.dll")]
    internal static extern int SetStretchBltMode(nint dc, int mode);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetBrushOrgEx(nint dc, int x, int y, nint previous);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool StretchBlt(
        nint dst,
        int x,
        int y,
        int width,
        int height,
        nint src,
        int sx,
        int sy,
        int sw,
        int sh,
        uint operation
    );

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern int GetDIBits(
        nint dc,
        nint bitmap,
        uint start,
        uint lines,
        byte[] pixels,
        byte[] info,
        uint usage
    );
}
