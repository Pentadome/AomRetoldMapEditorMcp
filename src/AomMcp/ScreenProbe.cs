using System.Buffers.Binary;
using System.IO.Compression;

namespace AomMcp;

/// <summary>Half-resolution pixel gates for editor dialogs: authored in the 1280×720 reference frame at reviewed client sizes
/// (2560×1440, 1920×1080) and mapped per axis into other frames (derived sizes, best effort).</summary>
internal static class ScreenProbe
{
    static readonly byte[] PngHeader = [137, 80, 78, 71, 13, 10, 26, 10];

    internal readonly record struct Rgb(byte R, byte G, byte B);

    /// <summary>Host screenshot frame width (<see cref="Ui.DefaultScreenshotWidth"/>); narrower clients keep their own width.</summary>
    internal const int FrameWidth = Ui.DefaultScreenshotWidth;

    /// <summary>Reference frame height: 16:9 at <see cref="FrameWidth"/> (all reviewed gate coordinates use it).</summary>
    internal const int ReferenceFrameHeight = 720;

    /// <summary>Accepted decoded frame bounds (host policy: aspect 0.25..5 of the 1280 frame; well past 21:9 and 4:3).</summary>
    const int MinFrameSide = 200,
        MaxFrameHeight = 1600;

    /// <summary>Upper bound of the compressed screenshot payload (host allocation policy).</summary>
    const int MaxPayloadBytes = 8_000_000;

    /// <summary>One screenshot frame (1280 wide, height follows the client aspect; 1280×720 for 16:9).
    /// ClientWidth/Height record the full-resolution client it was scaled from.</summary>
    internal sealed class Frame(
        byte[] rgb,
        int width,
        int height,
        int clientWidth = 2560,
        int clientHeight = 1440
    )
    {
        /// <summary>Full-resolution client width the frame was scaled from.</summary>
        internal int ClientWidth => clientWidth;

        /// <summary>Full-resolution client height the frame was scaled from.</summary>
        internal int ClientHeight => clientHeight;

        /// <summary>Frame width in pixels.</summary>
        internal int Width => width;

        /// <summary>Frame height in pixels.</summary>
        internal int Height => height;

        /// <summary>True for the reference 1280×720 geometry (all 16:9 clients ≥1280 wide).</summary>
        internal bool IsReference => width == FrameWidth && height == ReferenceFrameHeight;

        /// <summary>Reference-frame (1280×720) x to this frame (per-axis stretch; identity on the reference frame).</summary>
        internal int MapX(double x) =>
            (int)Math.Round(x * width / FrameWidth, MidpointRounding.AwayFromZero);

        /// <summary>Reference-frame (1280×720) y to this frame (per-axis stretch; identity on the reference frame).</summary>
        internal int MapY(double y) =>
            (int)Math.Round(y * height / ReferenceFrameHeight, MidpointRounding.AwayFromZero);

        /// <summary>Frame pixel to full-resolution client pixel.</summary>
        internal int ToClient(int frameCoordinate) =>
            (int)
                Math.Round(
                    frameCoordinate * (double)clientWidth / width,
                    MidpointRounding.AwayFromZero
                );

        /// <summary>True when the frame pixel exists.</summary>
        internal bool Contains(int x, int y) => x >= 0 && y >= 0 && x < width && y < height;

        /// <summary>Pixel at a frame coordinate; throws outside the frame.</summary>
        internal Rgb At(int x, int y)
        {
            if (x < 0 || y < 0 || x >= width || y >= height)
                throw new InvalidDataException("Screen probe outside frame.");
            var offset = (y * width + x) * 3;
            return new(rgb[offset], rgb[offset + 1], rgb[offset + 2]);
        }
    }

    /// <summary>Writer frame: pinned build and a reviewed or derivable client (derived sizes are best effort: callers keep
    /// their pre-click gates and post-action readback and report <c>layoutReviewed:false</c>).</summary>
    internal static Frame Capture(Game game)
    {
        Win.Check(Win.GetClientRect(game.Window, out var rect), "GetClientRect");
        if (
            !UiLayouts.BuildMatches(game.Layout.ExeSha256)
            || !UiLayouts.IsSupported(rect.Right, rect.Bottom)
        )
            throw new WorkflowFailure(
                "UI_LAYOUT_UNREVIEWED",
                "ui-preflight",
                $"Dialog pixel gates need the pinned build and a client {UiLayouts.SupportedText}; client is {rect.Right}x{rect.Bottom}.",
                false,
                false,
                "Switch the game to a supported resolution, or use manual UI and verify a game-written checkpoint; no input sent."
            );
        var png = Ui.Screenshot(game, FrameWidth);
        return DecodePng(png, rect.Right, rect.Bottom);
    }

    /// <summary>Note attached by writers working on a derived (unreviewed) client size.</summary>
    internal const string BestEffortNote =
        "Client size not live-reviewed: geometry derived from 2560x1440 (per-axis stretch, anchored minimap); pixel gates and readback still guard every click (best effort).";

    /// <summary>True when the client size has a live-reviewed layout.</summary>
    internal static bool Reviewed(Frame frame) =>
        UiLayouts.IsReviewed(frame.ClientWidth, frame.ClientHeight);

    internal static Frame DecodePng(byte[] png, int clientWidth = 2560, int clientHeight = 1440)
    {
        if (!png.AsSpan(0, Math.Min(8, png.Length)).SequenceEqual(PngHeader))
            throw new InvalidDataException("Unexpected PNG screenshot signature.");
        var pos = 8;
        var width = 0;
        var height = 0;
        using var ids = new MemoryStream();
        while (pos <= png.Length - 12)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(pos));
            pos += 4;
            if (length > png.Length - pos - 8)
                throw new InvalidDataException("PNG chunk outside screenshot.");
            var chunk = png.AsSpan(pos, 4);
            pos += 4;
            if (chunk.SequenceEqual("IHDR"u8))
            {
                if (length != 13)
                    throw new InvalidDataException("Unexpected PNG IHDR.");
                width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(pos));
                height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(pos + 4));
                if (png[pos + 8] != 8 || png[pos + 9] != 2)
                    throw new InvalidDataException("Unsupported screenshot color type.");
            }
            if (chunk.SequenceEqual("IDAT"u8))
                ids.Write(png.AsSpan(pos, (int)length));
            pos += checked((int)length) + 4; // Skip payload and CRC (trusted host-generated screenshot).
            if (chunk.SequenceEqual("IEND"u8))
                break;
        }
        if (
            width is < MinFrameSide or > FrameWidth
            || height is < MinFrameSide or > MaxFrameHeight
            || ids.Length is < 1 or > MaxPayloadBytes
        )
            throw new InvalidDataException("Unexpected screenshot dimensions or payload.");
        // Host captures keep the client aspect (Ui.CapturePixels: height = client height × width / client width).
        if (Math.Abs(height - (long)clientHeight * width / clientWidth) > 1)
            throw new InvalidDataException(
                $"Screenshot {width}x{height} does not match client {clientWidth}x{clientHeight} aspect."
            );
        ids.Position = 0;
        using var z = new ZLibStream(ids, CompressionMode.Decompress);
        var scan = new byte[(width * 3 + 1) * height];
        z.ReadExactly(scan);
        if (z.ReadByte() != -1)
            throw new InvalidDataException("Unexpected screenshot decompressed tail.");
        var rgb = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        {
            var start = y * (width * 3 + 1);
            if (scan[start] != 0)
                throw new InvalidDataException("Screenshot PNG filter unreviewed.");
            scan.AsSpan(start + 1, width * 3).CopyTo(rgb.AsSpan(y * width * 3));
        }
        return new(rgb, width, height, clientWidth, clientHeight);
    }

    /// <summary>Reference-frame (1280×720) centre of a Diplomacy stance cell.</summary>
    internal static (int X, int Y) Cell(int player, int target)
    {
        // Measured client/2 coordinates, not XML size1024 (different UI). Pixel check guards each click.
        return (
            283 + 50 * target,
            108 + (int)Math.Round(27.5 * player, MidpointRounding.AwayFromZero)
        );
    }

    // Exact half-res pixels of the alternative-UI Players Settings panel / its Diplomacy view, per reviewed client width.
    static readonly Dictionary<int, (int X, int Y, Rgb C)[]> AltPlayersPanel = new()
    {
        [2560] =
        [
            (20, 35, new(231, 214, 161)),
            (640, 60, new(36, 39, 42)),
            (1064, 55, new(33, 34, 38)),
            (300, 300, new(18, 20, 21)),
            (1240, 690, new(57, 53, 49)),
        ],
        [1920] =
        [
            (20, 35, new(246, 227, 171)),
            (640, 60, new(36, 39, 43)),
            (1064, 55, new(32, 34, 37)),
            (300, 300, new(20, 23, 24)),
            (1240, 690, new(56, 52, 49)),
        ],
    };
    static readonly Dictionary<int, (int X, int Y, Rgb C)[]> AltDiplomacy = new()
    {
        [2560] =
        [
            (20, 35, new(231, 214, 161)),
            (640, 60, new(6, 6, 6)),
            (1064, 55, new(156, 156, 156)),
            (300, 300, new(15, 16, 18)),
        ],
        [1920] =
        [
            (20, 35, new(246, 227, 171)),
            (560, 60, new(28, 28, 28)),
            (1064, 55, new(148, 148, 148)),
            (300, 300, new(15, 16, 18)),
            (600, 472, new(44, 45, 46)),
        ],
    };

    /// <summary>Per-channel tolerance of derived (unreviewed-size) matches against reviewed exact-pixel gates.</summary>
    const int DerivedPixelTolerance = 20;

    /// <summary>Exact gate at a reviewed size.</summary>
    static bool Exact(Frame frame, Dictionary<int, (int X, int Y, Rgb C)[]> gates) =>
        gates.TryGetValue(frame.ClientWidth, out var points)
        && Reviewed(frame)
        && points.All(p => frame.At(p.X, p.Y) == p.C);

    /// <summary>Derived-size gate: any reviewed reference, points mapped per axis, tolerant neighborhood match.</summary>
    static bool Derived(Frame frame, Dictionary<int, (int X, int Y, Rgb C)[]> gates) =>
        UiLayouts.IsDerivable(frame.ClientWidth, frame.ClientHeight)
        && gates.Values.Any(points =>
            points.All(p =>
                Near(
                    frame,
                    frame.MapX(p.X),
                    frame.MapY(p.Y),
                    p.C,
                    DerivedPixelTolerance,
                    UiLayouts.DerivedGateRadius
                )
            )
        );

    /// <summary>Reference-frame (1280×720) point to full-resolution client click, per axis (×2 at 2560×1440, ×1.5 at 1920×1080).</summary>
    internal static (int X, int Y) CellClient(Game game, int x, int y)
    {
        var (width, height) = Ui.ClientSize(game);
        return (
            (int)Math.Round(x * (double)width / FrameWidth, MidpointRounding.AwayFromZero),
            (int)
                Math.Round(y * (double)height / ReferenceFrameHeight, MidpointRounding.AwayFromZero)
        );
    }

    /// <summary>Exact-pixel gate for the reviewed alternative-UI Players Settings panel. Derived clients: tolerant match of either
    /// reviewed reference mapped per axis (±20, DerivedGateRadius neighborhood; best effort).</summary>
    internal static bool PlayersPanelVisible(Frame frame) =>
        Exact(frame, AltPlayersPanel) || Derived(frame, AltPlayersPanel);

    /// <summary>True when any pixel within the radius matches the colour within the tolerance.</summary>
    static bool Near(Frame frame, int x, int y, Rgb c, int tolerance, int radius)
    {
        for (var dy = -radius; dy <= radius; dy++)
        for (var dx = -radius; dx <= radius; dx++)
            if (
                frame.Contains(x + dx, y + dy)
                && frame.At(x + dx, y + dy) is var o
                && Math.Abs(o.R - c.R) <= tolerance
                && Math.Abs(o.G - c.G) <= tolerance
                && Math.Abs(o.B - c.B) <= tolerance
            )
                return true;
        return false;
    }

    /// <summary>Throws unless the alternative Players Settings panel is visible.</summary>
    internal static void RequirePlayersPanel(Frame frame)
    {
        if (!PlayersPanelVisible(frame))
            throw new WorkflowFailure(
                "UI_LAYOUT_UNREVIEWED",
                "ui-observe",
                "Players Settings pixels differ from reviewed alternative UI.",
                false,
                false,
                "STOP. Inspect layout (normal UI: open Scenario > Player Data). No input sent."
            );
    }

    /// <summary>Alternative Players Settings + Diplomacy pixels, or the reviewed normal-UI Scenario > Diplomacy dialog.
    /// Both share the same stance-cell grid and colors (measured 2560×1440).</summary>
    internal static string DiplomacyUi(Frame frame)
    {
        if (Exact(frame, AltDiplomacy) || Derived(frame, AltDiplomacy))
            return "alternative";
        if (UiRead.NormalGate(frame, "diplomacy", out var detail))
            return "normal";
        throw new WorkflowFailure(
            "UI_PANEL_MISMATCH",
            "ui-observe",
            "Observed pixels match neither reviewed Diplomacy dialog (alternative gate failed; normal "
                + detail
                + ").",
            false,
            false,
            "Alternative UI: open Players Settings, then Diplomacy. Normal UI: open Scenario > Diplomacy. Inspect screenshot. No input sent."
        );
    }

    /// <summary>Throws unless either reviewed Diplomacy dialog is visible.</summary>
    internal static void RequireDialog(Frame frame) => _ = DiplomacyUi(frame);

    /// <summary>Offline decode/mapping fixtures.</summary>
    internal static void SelfTest()
    {
        var bgra = new byte[1280 * 720 * 4];
        var index = (136 * 1280 + 383) * 4;
        bgra[index + 2] = 255; // GDI BGRA red; host PNG conversion must emit RGB red.
        var frame = DecodePng(Ui.Png(1280, 720, bgra));
        if (frame.At(383, 136) != new Rgb(255, 0, 0) || frame.At(20, 35) != new Rgb(0, 0, 0))
            throw new InvalidOperationException("Alternative UI pixel decode fixture failed.");
        // 16:10 client (1440×900) → 1280×800 frame; bottom row decodes, aspect mismatch refused.
        var tall = new byte[1280 * 800 * 4];
        tall[(799 * 1280 + 5) * 4 + 1] = 255;
        var t = DecodePng(Ui.Png(1280, 800, tall), 1440, 900);
        if (
            t.IsReference
            || t.Height != 800
            || t.At(5, 799) != new Rgb(0, 255, 0)
            || t.ToClient(640) != 720
            || t.MapX(640) != 640
            || t.MapY(360) != 400
            || frame.MapY(360) != 360
        )
            throw new InvalidOperationException("Variable-height frame decode fixture failed.");
        try
        {
            _ = DecodePng(Ui.Png(1280, 800, tall), 1920, 1080);
            throw new InvalidOperationException("Aspect-mismatched screenshot accepted.");
        }
        catch (InvalidDataException) { }
        try
        {
            _ = DecodePng([0, 1, 2]);
        }
        catch (InvalidDataException)
        {
            return;
        }
        throw new InvalidOperationException("Malformed screenshot accepted by pixel gate.");
    }

    /// <summary>Throws unless the stance cell shows the expected colour (cell mapped from the reference frame).</summary>
    internal static void RequireCell(Frame frame, int from, int to, int stance)
    {
        RequireDialog(frame);
        var (rx, ry) = Cell(from, to);
        var (x, y) = (frame.MapX(rx), frame.MapY(ry));
        var expected = stance switch
        {
            1 => new Rgb(0, 255, 0), // Ally, confirmed by checkpoint P1→P2=1.
            2 => new Rgb(255, 0, 0), // Enemy, confirmed by checkpoint P1→P2=2.
            3 => new Rgb(238, 238, 238), // Neutral, confirmed by checkpoint P1→P2=3.
            _ => throw new InvalidDataException("Self/unset stance cannot be clicked safely."),
        };
        if (frame.At(x, y) != expected)
            throw new WorkflowFailure(
                "UI_CELL_MISMATCH",
                "ui-observe",
                $"P{from}→P{to} pixel ({x},{y}) not expected stance {stance} color; no click.",
                false,
                false,
                "Inspect correct visible cell/scene, then game-written checkpoint. No guessed click/retry."
            );
    }
}
