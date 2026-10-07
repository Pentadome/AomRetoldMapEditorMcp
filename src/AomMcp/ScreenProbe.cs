using System.Buffers.Binary;
using System.IO.Compression;

namespace AomMcp;

/// <summary>Half-resolution (1280×720) pixel gates for reviewed editor dialogs at reviewed client sizes (2560×1440, 1920×1080).</summary>
internal static class ScreenProbe
{
    static readonly byte[] PngHeader = [137, 80, 78, 71, 13, 10, 26, 10];
    internal readonly record struct Rgb(byte R, byte G, byte B);
    /// <summary>One 1280×720 screenshot frame; ClientWidth/Height record the full-resolution client it was scaled from.</summary>
    internal sealed class Frame(byte[] rgb, int width, int height, int clientWidth = 2560, int clientHeight = 1440)
    {
        internal int ClientWidth => clientWidth;
        internal int ClientHeight => clientHeight;
        /// <summary>Frame pixel to full-resolution client pixel.</summary>
        internal int ToClient(int frameCoordinate) => (int)Math.Round(frameCoordinate * (double)clientWidth / width, MidpointRounding.AwayFromZero);
        internal Rgb At(int x, int y)
        {
            if (x < 0 || y < 0 || x >= width || y >= height) throw new InvalidDataException("Screen probe outside frame.");
            var offset = (y * width + x) * 3;
            return new(rgb[offset], rgb[offset + 1], rgb[offset + 2]);
        }
    }

    internal static Frame Capture(Game game)
    {
        Win.Check(Win.GetClientRect(game.Window, out var rect), "GetClientRect");
        if (game.Layout.ExeSha256 != "dd15d1d838e78faa1bc9854becc3994f4f3a4548ef30efd24108abedc1b84fff"
            || !UiLayouts.IsReviewed(rect.Right, rect.Bottom))
            throw new WorkflowFailure("UI_LAYOUT_UNREVIEWED", "ui-preflight", $"Dialog pixel gates only reviewed for {UiLayouts.ReviewedText} clients on the pinned build; client is {rect.Right}x{rect.Bottom}.", false, false,
                "Switch the game to a reviewed resolution, or use manual UI and verify a game-written checkpoint; no input sent.");
        var png = Ui.Screenshot(game, 1280);
        return DecodePng(png, rect.Right, rect.Bottom);
    }

    internal static Frame DecodePng(byte[] png, int clientWidth = 2560, int clientHeight = 1440)
    {
        if (!png.AsSpan(0, Math.Min(8, png.Length)).SequenceEqual(PngHeader))
            throw new InvalidDataException("Unexpected PNG screenshot signature.");
        var pos = 8; var width = 0; var height = 0;
        using var ids = new MemoryStream();
        while (pos <= png.Length - 12)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(pos)); pos += 4;
            if (length > png.Length - pos - 8) throw new InvalidDataException("PNG chunk outside screenshot.");
            var chunk = png.AsSpan(pos, 4); pos += 4;
            if (chunk.SequenceEqual("IHDR"u8))
            {
                if (length != 13) throw new InvalidDataException("Unexpected PNG IHDR.");
                width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(pos));
                height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(pos + 4));
                if (png[pos + 8] != 8 || png[pos + 9] != 2) throw new InvalidDataException("Unsupported screenshot color type.");
            }
            if (chunk.SequenceEqual("IDAT"u8)) ids.Write(png.AsSpan(pos, (int)length));
            pos += checked((int)length) + 4; // Skip payload and CRC (trusted host-generated screenshot).
            if (chunk.SequenceEqual("IEND"u8)) break;
        }
        if (width != 1280 || height != 720 || ids.Length is < 1 or > 8_000_000)
            throw new InvalidDataException("Unexpected screenshot dimensions or payload.");
        ids.Position = 0;
        using var z = new ZLibStream(ids, CompressionMode.Decompress);
        var scan = new byte[(width * 3 + 1) * height];
        z.ReadExactly(scan);
        if (z.ReadByte() != -1) throw new InvalidDataException("Unexpected screenshot decompressed tail.");
        var rgb = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        {
            var start = y * (width * 3 + 1);
            if (scan[start] != 0) throw new InvalidDataException("Screenshot PNG filter unreviewed.");
            scan.AsSpan(start + 1, width * 3).CopyTo(rgb.AsSpan(y * width * 3));
        }
        return new(rgb, width, height, clientWidth, clientHeight);
    }

    internal static (int X, int Y) Cell(int player, int target)
    {
        // Measured client/2 coordinates, not XML size1024 (different UI). Pixel check guards each click.
        return (283 + 50 * target, 108 + (int)Math.Round(27.5 * player, MidpointRounding.AwayFromZero));
    }

    // Exact half-res pixels of the alternative-UI Players Settings panel / its Diplomacy view, per reviewed client width.
    static readonly Dictionary<int, (int X, int Y, Rgb C)[]> AltPlayersPanel = new()
    {
        [2560] = [(20, 35, new(231, 214, 161)), (640, 60, new(36, 39, 42)), (1064, 55, new(33, 34, 38)), (300, 300, new(18, 20, 21)), (1240, 690, new(57, 53, 49))],
        [1920] = [(20, 35, new(246, 227, 171)), (640, 60, new(36, 39, 43)), (1064, 55, new(32, 34, 37)), (300, 300, new(20, 23, 24)), (1240, 690, new(56, 52, 49))],
    };
    static readonly Dictionary<int, (int X, int Y, Rgb C)[]> AltDiplomacy = new()
    {
        [2560] = [(20, 35, new(231, 214, 161)), (640, 60, new(6, 6, 6)), (1064, 55, new(156, 156, 156)), (300, 300, new(15, 16, 18))],
        [1920] = [(20, 35, new(246, 227, 171)), (560, 60, new(28, 28, 28)), (1064, 55, new(148, 148, 148)), (300, 300, new(15, 16, 18)), (600, 472, new(44, 45, 46))],
    };

    static bool Exact(Frame frame, Dictionary<int, (int X, int Y, Rgb C)[]> gates) =>
        gates.TryGetValue(frame.ClientWidth, out var points) && points.All(p => frame.At(p.X, p.Y) == p.C);

    /// <summary>Half-res cell center to full-resolution client click (×2 at 2560×1440, ×1.5 at 1920×1080).</summary>
    internal static (int X, int Y) CellClient(Game game, int x, int y)
    {
        var (width, _) = Ui.ClientSize(game);
        var scale = width / 1280.0;
        return ((int)Math.Round(x * scale, MidpointRounding.AwayFromZero), (int)Math.Round(y * scale, MidpointRounding.AwayFromZero));
    }

    /// <summary>Exact-pixel gate for the reviewed alternative-UI Players Settings panel (fails closed for unreviewed client widths).</summary>
    internal static bool PlayersPanelVisible(Frame frame) => Exact(frame, AltPlayersPanel);

    internal static void RequirePlayersPanel(Frame frame)
    {
        if (!PlayersPanelVisible(frame))
            throw new WorkflowFailure("UI_LAYOUT_UNREVIEWED", "ui-observe", "Players Settings pixels differ from reviewed alternative UI.", false, false,
                "STOP. Inspect layout (normal UI: open Scenario > Player Data). No input sent.");
    }

    /// <summary>Alternative Players Settings + Diplomacy pixels, or the reviewed normal-UI Scenario > Diplomacy dialog.
    /// Both share the same stance-cell grid and colors (measured 2560×1440).</summary>
    internal static string DiplomacyUi(Frame frame)
    {
        if (Exact(frame, AltDiplomacy)) return "alternative";
        if (UiRead.NormalGate(frame, "diplomacy", out var detail)) return "normal";
        throw new WorkflowFailure("UI_PANEL_MISMATCH", "ui-observe", "Observed pixels match neither reviewed Diplomacy dialog (alternative gate failed; normal " + detail + ").", false, false,
            "Alternative UI: open Players Settings, then Diplomacy. Normal UI: open Scenario > Diplomacy. Inspect screenshot. No input sent.");
    }

    internal static void RequireDialog(Frame frame) => _ = DiplomacyUi(frame);

    internal static void SelfTest()
    {
        var bgra = new byte[1280 * 720 * 4];
        var index = (136 * 1280 + 383) * 4;
        bgra[index + 2] = 255; // GDI BGRA red; host PNG conversion must emit RGB red.
        var frame = DecodePng(Ui.Png(1280, 720, bgra));
        if (frame.At(383, 136) != new Rgb(255, 0, 0) || frame.At(20, 35) != new Rgb(0, 0, 0))
            throw new InvalidOperationException("Alternative UI pixel decode fixture failed.");
        try { _ = DecodePng([0, 1, 2]); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Malformed screenshot accepted by pixel gate.");
    }

    internal static void RequireCell(Frame frame, int from, int to, int stance)
    {
        RequireDialog(frame);
        var (x, y) = Cell(from, to);
        var expected = stance switch
        {
            1 => new Rgb(0, 255, 0), // Ally, confirmed by checkpoint P1→P2=1.
            2 => new Rgb(255, 0, 0), // Enemy, confirmed by checkpoint P1→P2=2.
            3 => new Rgb(238, 238, 238), // Neutral, confirmed by checkpoint P1→P2=3.
            _ => throw new InvalidDataException("Self/unset stance cannot be clicked safely."),
        };
        if (frame.At(x, y) != expected)
            throw new WorkflowFailure("UI_CELL_MISMATCH", "ui-observe", $"P{from}→P{to} pixel ({x},{y}) not expected stance {stance} color; no click.", false, false,
                "Inspect correct visible cell/scene, then game-written checkpoint. No guessed click/retry.");
    }
}
