using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace AomMcp;

/// <summary>Delivers guarded editor commands through a temporary hook on the game window thread.</summary>
internal sealed class Bridge : IDisposable
{
    // Host-defined packed ABI; native/EditorBridge.cpp Packet/static_assert is the source of truth.
    // 176-byte header/signature area + 16 KiB command buffer = 16,560 bytes; no game offsets here.
    internal const int PacketSize = 16560,
        PayloadOffset = 176,
        PayloadCapacity = 16384;
    // Host-chosen ASCII "AOMC" numeric tag, not a game constant; must match native/EditorBridge.cpp Magic.
    internal const uint Magic = 0x414f4d43;

    readonly nint _library,
        _callback;

    readonly uint _message;
    readonly Encoding _encoding;

    /// <summary>Loads a content-addressed bridge DLL into the host without calling the game.</summary>
    /// <param name="dll">Path to the compiled native bridge DLL.</param>
    public Bridge(string dll)
    {
        // Observed dispatcher calls MultiByteToWideChar(CP_ACP); see research/LIVE-FINDINGS.md.
        // GetACP supplies the local Windows ANSI code page, not UTF-8.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _encoding = Encoding.GetEncoding(
            checked((int)Win.GetACP()),
            EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback
        );
        // Host naming policy: first 16 SHA-256 hex chars distinguish compiled DLL versions.
        // A content-addressed DLL path prevents Windows reusing a previously loaded build.
        var versioned = Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(dll))!,
            $"AomEditorBridge-{Layout.Hash(dll)[..16]}.dll"
        );
        if (!File.Exists(versioned))
            File.Copy(dll, versioned);
        _library = NativeLibrary.Load(versioned);
        try
        {
            // Export and host-defined registered-message name/version must match native/EditorBridge.cpp.
            _callback = NativeLibrary.GetExport(_library, "EditorHook");
            _message = Win.RegisterMessage("AomEditorMcp.Dispatch.v1");
            Win.Check(_message != 0, "RegisterWindowMessage");
        }
        catch
        {
            NativeLibrary.Free(_library);
            throw;
        }
    }

    /// <summary>Dispatches one command and removes its temporary hook before returning.</summary>
    /// <param name="game">Hash-validated game connection.</param>
    /// <param name="command">Command expression encoded using the Windows ANSI code page.</param>
    /// <param name="mapHover">Whether the pointer must be over the map.</param>
    /// <param name="expectedProto">Required placement prototype ID, or null to omit this check.</param>
    /// <param name="expectedPlayer">Required placement player when a prototype check is enabled.</param>
    /// <param name="timeout">Acknowledgement deadline in milliseconds, from 100 to 30000.</param>
    /// <returns>Dispatcher acknowledgement, not a captured getter value or verified effect.</returns>
    /// <exception cref="TimeoutException">Outcome is unknown; do not automatically retry a mutation.</exception>
    public object Execute(
        Game game,
        string command,
        bool mapHover = false,
        uint? expectedProto = null,
        uint? expectedPlayer = null,
        int timeout = 5000
    )
    {
        // Host policy: default 5 s, allowed 0.1..30 s; bounds waiting, not an engine timing guarantee.
        if (timeout is < 100 or > 30000)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        // Native dispatcher accepts a NUL-terminated char*; embedded NUL would truncate the command.
        if (command.Contains('\0'))
            throw new ArgumentException("NUL in command.");
        var payload = _encoding.GetBytes(command);
        if (payload.Length is 0 or >= PayloadCapacity)
            throw new ArgumentException("Command exceeds bridge limit.");
        game.Focus();
        // Packet nonce is a 32-bit DWORD (4 random bytes); x8 matches native %08lx formatting.
        // Local\\ scopes the mapping to this Windows session; PID + nonce identify one host request.
        var nonce = BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4));
        using var map = MemoryMappedFile.CreateNew(
            $"Local\\AomEditorMcp-{game.Pid}-{nonce:x8}",
            PacketSize
        );
        using var view = map.CreateViewAccessor();
        var p = game.Layout;
        // First 24 DWORDs follow native Packet field order; version 1 is this private ABI.
        // State/result start at 0; flags bit 0 requires map hover, bit 1 pins proto/player.
        uint[] header =
        [
            Magic,
            1,
            PacketSize,
            game.Pid,
            game.Thread,
            nonce,
            0,
            0,
            p.DispatcherRva,
            p.ContextRva,
            p.OwnerOffset,
            p.EndpointOffset,
            p.EditorGlobalRva,
            p.EditorFlagOffset,
            p.EditorPointerOffset,
            (uint)p.Prefix.Length,
            p.PointerGlobalRva,
            p.HoverOffset,
            (mapHover ? 1u : 0u) | (expectedProto.HasValue ? 2u : 0u),
            (uint)payload.Length,
            p.ProtoOffset,
            p.PlayerOffset,
            expectedProto ?? 0,
            expectedPlayer ?? 0,
        ];
        view.WriteArray(0, header, 0, header.Length);
        // Packed Packet byte offsets: deadline=96, actualThread=104, callsStarted=108,
        // prefix=112 (64-byte capacity), payload=176; state=24 and result=28 below.
        view.Write(96, Win.GetTickCount64() + (ulong)timeout);
        view.WriteArray(112, p.Prefix, 0, p.Prefix.Length);
        view.WriteArray(PayloadOffset, payload, 0, payload.Length);
        view.Write(PayloadOffset + payload.Length, (byte)0);
        // Win32 winuser.h: 4 = WH_CALLWNDPROC, invokes EditorHook on the window's owning thread.
        var hook = Win.SetHook(4, _callback, _library, game.Thread);
        Win.Check(hook != 0, "SetWindowsHookEx");
        bool delivered;
        try
        {
            delivered =
                Win.SendTimeout(
                    game.Window,
                    _message,
                    Magic,
                    (nint)(long)nonce,
                    3, // winuser.h: SMTO_BLOCK (1) | SMTO_ABORTIFHUNG (2).
                    (uint)timeout,
                    out _
                ) != 0;
        }
        finally
        {
            Win.Check(Win.Unhook(hook), "UnhookWindowsHookEx");
        }
        // Private ABI: state 0=pending, -1=claimed, 1=finished; result 1=dispatcher returned.
        var state = view.ReadInt32(24);
        uint result = view.ReadUInt32(28),
            started = view.ReadUInt32(108);
        if (!delivered || state != 1)
            throw new TimeoutException(
                $"Native call did not acknowledge completion. started={started}; outcome unknown. Do not automatically retry a mutation."
            );
        if (result != 1)
            throw new BridgeRefusal(result, command);
        return new
        {
            nativeReturned = true,
            semanticSuccessVerified = false,
            returnValueCaptured = false,
            command,
            pid = game.Pid,
            executionThread = view.ReadUInt32(104),
            hookRemoved = true, // Win.Check throws otherwise.
            elapsedDeadlineRemainingMs = Math.Max(
                0L,
                (long)view.ReadUInt64(96) - (long)Win.GetTickCount64()
            ),
        };
    }

    /// <summary>Describes a native bridge refusal code.</summary>
    /// <param name="code">Refusal code from the shared packet.</param>
    /// <returns>Human-readable refusal reason, or an unknown-code description.</returns>
    // These refusal numbers are assigned by native/EditorBridge.cpp Execute, not Win32 error codes.
    public static string Reason(uint code) =>
        code switch
        {
            2 => "expired request",
            3 => "invalid packet",
            4 => "wrong process/module",
            5 => "invalid layout bounds",
            6 => "runtime signature changed",
            7 => "context/thread mismatch",
            8 => "not editor",
            9 => "not foreground",
            10 => "pointer not on map",
            11 => "selection/player changed",
            _ => "unknown",
        };

    /// <summary>Unloads the host's native bridge library.</summary>
    public void Dispose() => NativeLibrary.Free(_library);

    /// <summary>Checks packet refusals and one-shot handling locally without calling the game.</summary>
    public void SelfTest()
    {
        uint pid = (uint)Environment.ProcessId,
            tid = Win.GetCurrentThreadId(),
            nonce = 1234567; // Arbitrary deterministic test token; live requests use random DWORDs.
        using var mapping = MemoryMappedFile.CreateNew(
            $"Local\\AomEditorMcp-{pid}-{nonce:x8}",
            PacketSize
        );
        using var view = mapping.CreateViewAccessor();
        var fn = Marshal.GetDelegateForFunctionPointer<Win.Hook>(_callback);
        var data = Marshal.AllocHGlobal(Marshal.SizeOf<Win.CallMessage>());
        try
        {
            Marshal.StructureToPtr(
                new Win.CallMessage
                {
                    Message = _message,
                    WParam = Magic,
                    LParam = (nint)nonce,
                },
                data,
                false
            );
            // Same packed ABI as Execute: magic/version/size/PID/thread/nonce/state/result at 0..28.
            void Reset()
            {
                view.Write(0, Magic);
                view.Write(4, 1u);
                view.Write(8, (uint)PacketSize);
                view.Write(12, pid);
                view.Write(16, tid);
                view.Write(20, nonce);
                view.Write(24, 0);
                view.Write(28, 0u);
            }
            void Require(bool ok)
            {
                if (!ok)
                    throw new InvalidOperationException("Native bridge self-test failed.");
            }
            // Win32 hook code < 0 must be forwarded untouched; +/-1 identity changes test refusal.
            Reset();
            fn(-1, 0, data);
            Require(view.ReadInt32(24) == 0);
            Reset();
            view.Write(16, tid + 1);
            fn(0, 0, data);
            Require(view.ReadInt32(24) == 0);
            Reset();
            view.Write(12, pid + 1);
            fn(0, 0, data);
            Require(view.ReadInt32(24) == 0);
            Reset();
            fn(0, 0, data);
            Require(view.ReadInt32(24) == 1 && view.ReadUInt32(28) >= 2);
            var old = view.ReadUInt32(28);
            view.Write(28, 999u); // Arbitrary sentinel: repeated delivery must not replace this result.
            fn(0, 0, data);
            Require(view.ReadUInt32(28) == 999 && old >= 2);
        }
        finally
        {
            Marshal.FreeHGlobal(data);
        }
    }
}

/// <summary>Native bridge refused before dispatching the requested command (packet state finished, result != 1).</summary>
/// <param name="code">Refusal code assigned by native/EditorBridge.cpp.</param>
/// <param name="command">Refused command text; it was NOT dispatched.</param>
internal sealed class BridgeRefusal(uint code, string command)
    : InvalidOperationException($"No command executed: bridge refusal {code} ({Bridge.Reason(code)}). Refused command: {command}")
{
    internal uint Code { get; } = code;
    internal string Command { get; } = command;
}
