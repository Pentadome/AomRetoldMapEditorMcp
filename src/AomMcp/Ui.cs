using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace AomMcp;

/// <summary>Provides guarded editor input and foreground client screenshots using Windows APIs.</summary>
internal static class Ui
{
    // Win32 SendInput uses AMD64 INPUT's 40-byte size (Win.Input; Windows SDK winuser.h).
    static void Send(params Win.Input[] input) =>
        Win.Check(Win.SendInput((uint)input.Length, input, 40) == input.Length, "SendInput");

    static Win.Input Mouse(uint flags, uint data = 0) =>
        new()
        {
            Type = 0, // winuser.h INPUT_MOUSE=0.
            Mouse = new Win.Mouse { Flags = flags, Data = data },
        };

    static Win.Input Key(ushort key, bool up)
    {
        // winuser.h MAPVK_VK_TO_VSC_EX=4: low byte is scan code, high byte is E0/E1 prefix.
        // KEYEVENTF_SCANCODE=8, KEYUP=2, EXTENDEDKEY=1. VK_PRIOR..DOWN (0x21..0x28),
        // INSERT (0x2d), DELETE (0x2e) also need extended flags; verified by Ui.SelfTest/live input.
        var scan = Win.MapVirtualKey(key, 4);
        return new Win.Input
        {
            Type = 1, // winuser.h INPUT_KEYBOARD=1.
            Key = new Win.Key { Scan = (ushort)(scan & 0xff), Flags = 8u | (up ? 2u : 0u) | ((scan & 0xff00) != 0 || key is >= 0x21 and <= 0x28 or 0x2d or 0x2e ? 1u : 0u) },
        };
    }

    /// <summary>Clicks a client-pixel position with a frame-visible hold and finally releases the button.</summary>
    /// <param name="game">Validated editor connection.</param>
    /// <param name="x">Horizontal client-pixel coordinate.</param>
    /// <param name="y">Vertical client-pixel coordinate.</param>
    /// <param name="button">Mouse button: left, right, or middle.</param>
    public static void Click(Game game, int x, int y, string button)
    {
        game.Move(x, y);
        Thread.Sleep(80); // Host-chosen pointer settle time; frame-polled input observed in live tests.
        // Windows SDK MOUSEEVENTF_* pairs: LEFTDOWN/UP=2/4, RIGHT=8/16, MIDDLE=32/64.
        (var down, var up) = button switch
        {
            "left" => (2u, 4u),
            "right" => (8u, 16u),
            "middle" => (32u, 64u),
            _ => throw new ArgumentException("button: left/right/middle"),
        };
        Send(Mouse(down));
        try
        {
            Thread.Sleep(60); // Same frame-polling constraint as keyboard input.
        }
        finally
        {
            Send(Mouse(up));
        }
    }

    /// <summary>Drags the left button along a client-pixel path and releases it in finally.</summary>
    /// <param name="game">Validated editor connection.</param>
    /// <param name="x1">Starting horizontal coordinate.</param>
    /// <param name="y1">Starting vertical coordinate.</param>
    /// <param name="x2">Ending horizontal coordinate.</param>
    /// <param name="y2">Ending vertical coordinate.</param>
    /// <param name="duration">Drag duration in milliseconds, from 100 to 5000.</param>
    public static void Drag(Game game, int x1, int y1, int x2, int y2, int duration)
    {
        // Host policy: bound drag to 0.1..5 s, interpolate 20 positions; not an engine constant.
        if (duration is < 100 or > 5000)
            throw new ArgumentException("Drag duration 100..5000 ms.");
        game.Move(x1, y1);
        Send(Mouse(2)); // winuser.h MOUSEEVENTF_LEFTDOWN.
        try
        {
            const int steps = 20;
            for (var i = 1; i <= steps; i++)
            {
                game.Move(x1 + (x2 - x1) * i / steps, y1 + (y2 - y1) * i / steps);
                Thread.Sleep(duration / steps);
            }
        }
        finally
        {
            Send(Mouse(4)); // winuser.h MOUSEEVENTF_LEFTUP.
        }
    }

    /// <summary>Focuses the editor and sends mouse-wheel input at the current pointer position.</summary>
    /// <param name="game">Validated editor connection.</param>
    /// <param name="steps">Signed wheel-notch count from -20 to 20.</param>
    public static void Wheel(Game game, int steps)
    {
        // Host policy: at most 20 wheel notches per call; Win32 uses 120 units per notch.
        if (steps is < -20 or > 20)
            throw new ArgumentException("Wheel steps -20..20.");
        game.Focus();
        Send(Mouse(0x800, unchecked((uint)(steps * 120)))); // MOUSEEVENTF_WHEEL / WHEEL_DELTA (winuser.h).
    }

    /// <summary>Sends a scan-code key press with frame-visible dwell and finally releases held keys.</summary>
    /// <param name="game">Validated editor connection.</param>
    /// <param name="name">Supported letter, digit, function key, or named navigation/control key.</param>
    /// <param name="modifiers">Up to three distinct CTRL, SHIFT, or ALT modifiers.</param>
    public static void Press(Game game, string name, string[] modifiers)
    {
        game.Focus();
        // Three supported modifiers only: VK_CONTROL=0x11, VK_SHIFT=0x10, VK_MENU/ALT=0x12.
        // Names are this host's input API; numbers come from Windows SDK virtual-key codes.
        if (
            modifiers.Length > 3
            || modifiers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != modifiers.Length
        )
            throw new ArgumentException("Invalid modifiers.");
        var key = VirtualKey(name);
        var mods = modifiers
            .Select(s =>
                s.ToUpperInvariant() switch
                {
                    "CTRL" => (ushort)0x11,
                    "SHIFT" => (ushort)0x10,
                    "ALT" => (ushort)0x12,
                    _ => throw new ArgumentException("Modifiers: CTRL/SHIFT/ALT"),
                }
            )
            .ToArray();
        // Host-chosen 30 ms modifier/after-release settle and 60 ms key hold prevent same-frame loss;
        // chosen after missed down/up events in live editor trials, not published engine requirements.
        foreach (var m in mods)
            Send(Key(m, false));
        try
        {
            Thread.Sleep(30);
            Send(Key(key, false));
            Thread.Sleep(60); // DirectInput polls each frame; same-batch down/up can disappear.
        }
        finally
        {
            Send(Key(key, true));
            foreach (var m in mods.Reverse())
                Send(Key(m, true));
        }
        Thread.Sleep(30);
    }

    static ushort VirtualKey(string name)
    {
        // Windows SDK virtual-key codes: letters/digits equal ASCII 'A'..'Z'/'0'..'9';
        // F1=0x70 through F12=0x7b, hence 0x6f+f. Named-key values below are VK_* from winuser.h.
        name = name.ToUpperInvariant();
        if (name.Length == 1 && name[0] is >= 'A' and <= 'Z' or >= '0' and <= '9')
            return name[0];
        if (name.StartsWith('F') && int.TryParse(name.AsSpan(1), out var f) && f is >= 1 and <= 12)
            return (ushort)(0x6f + f);
        return name switch
        {
            "ESC" => 0x1b,
            "ENTER" => 0x0d,
            "TAB" => 9,
            "SPACE" => 0x20,
            "LEFT" => 0x25,
            "UP" => 0x26,
            "RIGHT" => 0x27,
            "DOWN" => 0x28,
            "DELETE" => 0x2e,
            "BACKSPACE" => 8,
            "HOME" => 0x24,
            "END" => 0x23,
            "PGUP" => 0x21,
            "PGDN" => 0x22,
            _ => throw new ArgumentException("Unsupported key."),
        };
    }

    /// <summary>Types Unicode text into the focused editor control without changing the clipboard.</summary>
    /// <param name="game">Validated editor connection.</param>
    /// <param name="text">Text up to 4096 characters with no NUL characters.</param>
    public static void Text(Game game, string text)
    {
        // Host 4096 UTF-16-unit input ceiling; NUL is rejected to avoid native text truncation.
        // INPUT_KEYBOARD=1, KEYEVENTF_UNICODE=4 and UNICODE|KEYUP=6 (Windows SDK).
        if (text.Length > 4096 || text.Contains('\0'))
            throw new ArgumentException("Text exceeds limit or contains NUL.");
        game.Focus();
        foreach (var c in text)
            Send(
                new Win.Input
                {
                    Type = 1,
                    Key = new Win.Key { Scan = c, Flags = 4 },
                },
                new Win.Input
                {
                    Type = 1,
                    Key = new Win.Key { Scan = c, Flags = 6 },
                }
            );
    }

    /// <summary>Captures the focused game client as PNG, including during playtest.</summary>
    /// <param name="game">Validated game connection; editor mode is not required for observation.</param>
    /// <param name="maxWidth">Maximum output width from 320 to 2560 pixels; no upscaling.</param>
    /// <returns>PNG bytes at the client's aspect ratio.</returns>
    /// <remarks>Captures screen pixels, so overlays must not cover the game client.</remarks>
    public static byte[] Screenshot(Game game, int maxWidth) => Screenshot(game, maxWidth, null, 1);

    public static byte[] Screenshot(Game game, int maxWidth, int[]? region, int scale)
    {
        if (scale is < 1 or > 4) throw new ArgumentException("Screenshot scale must be 1..4.");
        if (region is null && scale != 1) throw new ArgumentException("scale requires region.");
        var (width, height, pixels) = CapturePixels(game, region is null ? maxWidth : 2560);
        if (region is null) return Png(width, height, pixels);
        var (clientWidth, clientHeight) = ClientSize(game);
        if (clientWidth != width || clientHeight != height)
            throw new ArgumentException("Region requires full-resolution client ≤2560 pixels wide.");
        var crop = CropZoom(pixels, width, height, region, scale);
        return Png(crop.Width, crop.Height, crop.Bgra);
    }

    internal static (int Width, int Height) ClientSize(Game game)
    {
        Win.Check(Win.GetClientRect(game.Window, out var rect), "GetClientRect");
        return (rect.Right, rect.Bottom);
    }

    internal static (int Width, int Height, byte[] Bgra) CropZoom(byte[] pixels, int width, int height, int[] region, int scale)
    {
        if (region.Length != 4 || scale is < 1 or > 4) throw new ArgumentException("Region [x,y,w,h] and scale 1..4 required.");
        var (x, y, w, h) = (region[0], region[1], region[2], region[3]);
        if (x < 0 || y < 0 || w < 1 || h < 1 || (long)x + w > width || (long)y + h > height
            || (long)w * scale > 1600 || (long)h * scale > 1600 || pixels.Length != checked(width * height * 4))
            throw new ArgumentException("Screenshot crop outside client/output bounds.");
        var dest = new byte[checked(w * scale * h * scale * 4)];
        for (var row = 0; row < h * scale; row++)
            for (var col = 0; col < w * scale; col++)
                pixels.AsSpan(((y + row / scale) * width + x + col / scale) * 4, 4)
                    .CopyTo(dest.AsSpan((row * w * scale + col) * 4, 4));
        return (w * scale, h * scale, dest);
    }

    internal static (int Width, int Height, byte[] Bgra) CapturePixels(Game game, int maxWidth)
    {
        // Host output/allocation policy: 320..2560 px, default 1280 in Server; 2560 matches tested client.
        // 80 ms below allows focus/render to settle; timing is empirical, not a GDI requirement.
        if (maxWidth is < 320 or > 2560)
            throw new ArgumentException("Screenshot maxWidth 320..2560.");
        game.Focus(requireEditor: false); // Read-only observation also works during playtest.
        Thread.Sleep(80);
        Win.Check(Win.GetClientRect(game.Window, out var rect), "GetClientRect");
        var origin = new Win.Point();
        Win.Check(Win.ClientToScreen(game.Window, ref origin), "ClientToScreen");
        int sw = rect.Right,
            sh = rect.Bottom,
            width = Math.Min(sw, maxWidth),
            height = Math.Max(1, sh * width / sw);
        // Win32 GetDC(NULL/0) selects screen DC; only the game's client rectangle is copied.
        nint screen = Win.GetDC(0),
            dc = Win.CreateCompatibleDC(screen),
            bitmap = Win.CreateCompatibleBitmap(screen, width, height);
        nint old = 0;
        try
        {
            if (screen == 0 || dc == 0 || bitmap == 0)
                throw new InvalidOperationException("Screenshot GDI allocation failed.");
            old = Win.SelectObject(dc, bitmap);
            Win.Check(old != 0 && old != -1, "Select bitmap"); // GDI failure sentinels: NULL / HGDI_ERROR.
            Win.Check(
                Win.GetForegroundWindow() == game.Window,
                "Game lost foreground before capture"
            );
            // wingdi.h HALFTONE=4; SetBrushOrgEx(0,0) resets brush origin after HALFTONE selection.
            Win.Check(
                Win.SetStretchBltMode(dc, 4) != 0 && Win.SetBrushOrgEx(dc, 0, 0, 0),
                "Set halftone scaling"
            );
            Win.Check(
                Win.StretchBlt(
                    dc,
                    0,
                    0,
                    width,
                    height,
                    screen,
                    origin.X,
                    origin.Y,
                    sw,
                    sh,
                    0x00cc0020 | 0x40000000 // wingdi.h SRCCOPY | CAPTUREBLT (include layered windows).
                ),
                "Capture game client"
            );
            Win.Check(
                Win.GetForegroundWindow() == game.Window,
                "Game lost foreground during capture"
            );
            Win.SelectObject(dc, old);
            // Windows BITMAPINFOHEADER: 40 bytes, width +4, height +8 (negative = top-down),
            // planes +12 = 1, bitCount +14 = 32 (BGRA), compression defaults to BI_RGB=0.
            var header = new byte[40];
            BinaryPrimitives.WriteInt32LittleEndian(header, 40);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), width);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), -height);
            BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(12), 1);
            BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(14), 32);
            var pixels = new byte[checked(width * height * 4)];
            Win.Check(
                Win.GetDIBits(dc, bitmap, 0, (uint)height, pixels, header, 0) == height, // DIB_RGB_COLORS=0.
                "GetDIBits"
            );
            return (width, height, pixels);
        }
        finally
        {
            if (old != 0 && old != -1)
                Win.SelectObject(dc, old);
            if (bitmap != 0)
                Win.DeleteObject(bitmap);
            if (dc != 0)
                Win.DeleteDC(dc);
            if (screen != 0)
                Win.Check(Win.ReleaseDC(0, screen) != 0, "Release screen DC");
        }
    }

    // PNG is three small chunks; stdlib zlib keeps screenshot transport dependency-free.
    internal static byte[] Png(int width, int height, byte[] bgra)
    {
        using var output = new MemoryStream();
        // W3C PNG spec: fixed 8-byte signature 89 50 4E 47 0D 0A 1A 0A (not host-chosen magic).
        output.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        // PNG IHDR is 13 bytes: BE width +0/height +4; depth +8=8 bits, color +9=2 (RGB).
        // Compression/filter/interlace +10..12 remain zero per PNG spec (deflate, standard, no interlace).
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = 2;
        // PNG-defined chunk names: IHDR=image header, IDAT=zlib pixel stream, IEND=end marker.
        Chunk(output, "IHDR", header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, true))
        {
            // PNG scanline starts with filter byte 0 (None); RGB has 3 bytes, GDI BGRA has 4.
            // Source channels +2/+1/+0 become R/G/B; alpha is intentionally omitted.
            var row = new byte[1 + width * 3];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    int source = (y * width + x) * 4,
                        dest = 1 + x * 3;
                    row[dest] = bgra[source + 2];
                    row[dest + 1] = bgra[source + 1];
                    row[dest + 2] = bgra[source];
                }
                zlib.Write(row);
            }
        }
        Chunk(output, "IDAT", compressed.ToArray());
        Chunk(output, "IEND", []);
        return output.ToArray();
    }

    static void Chunk(Stream output, string type, byte[] data)
    {
        // PNG chunk length/CRC are 4-byte big-endian fields; chunk type is four ASCII letters.
        Span<byte> size = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(size, data.Length);
        output.Write(size);
        var name = Encoding.ASCII.GetBytes(type);
        output.Write(name);
        output.Write(data);
        // PNG spec CRC-32 (ISO 3309): initial all-ones, reflected polynomial 0xedb88320,
        // 8 shifts per byte, final complement; covers type+data, not length.
        var crc = 0xffffffff;
        foreach (var b in name.Concat(data))
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0u);
        }
        BinaryPrimitives.WriteUInt32BigEndian(size, ~crc);
        output.Write(size);
    }

    internal static void SelfTest()
    {
        // Test fixture: one red BGRA pixel; PNG signature matches spec above.
        // Enter scan=0x1c (PC scan set 1); End down=SCANCODE|EXTENDED=9, up adds KEYUP=11.
        var png = Png(1, 1, [0, 0, 255, 0]);
        var crop = CropZoom([0, 0, 0, 0, 0, 0, 255, 0, 0, 255, 0, 0, 0, 0, 0, 0], 2, 2, [1, 0, 1, 1], 2);
        if (crop.Width != 2 || crop.Height != 2 || crop.Bgra.Length != 16
            || !crop.Bgra.AsSpan(0, 4).SequenceEqual(new byte[] { 0, 0, 255, 0 })
            || !crop.Bgra.AsSpan(12, 4).SequenceEqual(new byte[] { 0, 0, 255, 0 }))
            throw new InvalidOperationException("Screenshot region/nearest scale self-test failed.");
        if (
            !png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            || VirtualKey("F12") != 0x7b
            || VirtualKey("ESC") != 0x1b
            || Key(0x0d, false).Key.Scan != 0x1c
            || Key(0x23, false).Key.Flags != 9
            || Key(0x23, true).Key.Flags != 11
        )
            throw new InvalidOperationException("UI encoding self-test failed.");
    }
}
