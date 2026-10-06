using System.Buffers.Binary;
using System.IO.Compression;

namespace AomMcp;

/// <summary>Pixel gate for explicitly observed 2560×1440 alternative-editor diplomacy panel only.</summary>
internal static class ScreenProbe
{
    static readonly byte[] PngHeader = [137, 80, 78, 71, 13, 10, 26, 10];
    internal readonly record struct Rgb(byte R, byte G, byte B);
    internal sealed class Frame(byte[] rgb, int width, int height)
    {
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
            || rect.Right != 2560 || rect.Bottom != 1440)
            throw new WorkflowFailure("UI_LAYOUT_UNREVIEWED", "ui-preflight", "Diplomacy geometry only observed for pinned 2560×1440 alternative editor build.", false, false,
                "Use manual UI and verify game-written checkpoint; no guessed clicks.");
        var png = Ui.Screenshot(game, 1280);
        return DecodePng(png);
    }

    internal static Frame DecodePng(byte[] png)
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
        return new(rgb, width, height);
    }

    internal static (int X, int Y) Cell(int player, int target)
    {
        // Measured client/2 coordinates, not XML size1024 (different UI). Pixel check guards each click.
        return (283 + 50 * target, 108 + (int)Math.Round(27.5 * player, MidpointRounding.AwayFromZero));
    }

    internal static void RequirePlayersPanel(Frame frame)
    {
        if (frame.At(20, 35) != new Rgb(231, 214, 161)
            || frame.At(640, 60) != new Rgb(36, 39, 42)
            || frame.At(1064, 55) != new Rgb(33, 34, 38)
            || frame.At(300, 300) != new Rgb(18, 20, 21)
            || frame.At(1240, 690) != new Rgb(57, 53, 49))
            throw new WorkflowFailure("UI_LAYOUT_UNREVIEWED", "ui-observe", "Players Settings pixels differ from reviewed alternative UI.", false, false,
                "STOP. Normal UI unsupported; inspect layout. No input sent.");
    }

    internal static void RequireDialog(Frame frame)
    {
        if (frame.At(20, 35) != new Rgb(231, 214, 161)
            || frame.At(640, 60) != new Rgb(6, 6, 6)
            || frame.At(1064, 55) != new Rgb(156, 156, 156)
            || frame.At(300, 300) != new Rgb(15, 16, 18))
            throw new WorkflowFailure("UI_PANEL_MISMATCH", "ui-observe", "Observed panel pixels differ from reviewed Players Settings + Diplomacy dialog.", false, false,
                "Open Players Settings (upper-right Players Settings icon), then Diplomacy; inspect screenshot. No input sent.");
    }

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
