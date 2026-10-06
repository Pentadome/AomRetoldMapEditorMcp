using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace AomMcp;

/// <summary>Bounded structural checks for game-written snapshots. Structure is not gameplay validation.</summary>
internal static class ExportFormats
{
    internal const int MaxStoredBytes = 64_000_000, MaxDecodedBytes = 256_000_000;
    internal sealed record Check(bool Valid, string Format, string Reason, int SuffixBytes = 0);
    static readonly UnicodeEncoding Utf16 = new(false, false, true);

    internal static Check Trigger(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 10 || bytes[0] != 'T' || bytes[1] != 'R')
            return new(false, "trg", "TR header missing or truncated.");
        var size = BinaryPrimitives.ReadUInt32LittleEndian(bytes[2..]);
        if (size < 4 || size > MaxStoredBytes - 6 || size > bytes.Length - 6)
            return new(false, "trg", "TR declared payload outside file/bounds.");
        var version = BinaryPrimitives.ReadInt32LittleEndian(bytes[6..]);
        if (version != 12) return new(false, "trg", "Unsupported TR version " + version + ".");
        var suffix = bytes[(6 + (int)size)..];
        if (suffix.IsEmpty) return new(true, "TR v12", "No suffix.");
        if (suffix.SequenceEqual(new byte[] { 255, 255, 255, 255 }))
            return new(true, "TR v12 controller", "Legacy four-byte sentinel preserved.", 4);
        if (suffix.Length < 12) return new(false, "trg", "Unsupported TR suffix.", suffix.Length);
        // Game-written campaign export: camera count, then export-name + count camera names (UTF-16 lengths).
        var count = BinaryPrimitives.ReadUInt32LittleEndian(suffix);
        if (count > 1024) return new(false, "trg", "Camera count outside host bound.", suffix.Length);
        var off = 4;
        try
        {
            for (var i = 0; i <= count; i++)
            {
                if (off > suffix.Length - 4) return new(false, "trg", "Truncated camera name length.", suffix.Length);
                var chars = BinaryPrimitives.ReadUInt32LittleEndian(suffix[off..]); off += 4;
                if (chars > 4096 || chars * 2 > suffix.Length - off)
                    return new(false, "trg", "Camera name outside host bounds.", suffix.Length);
                _ = Utf16.GetString(suffix.Slice(off, (int)chars * 2));
                off += (int)chars * 2;
            }
        }
        catch (DecoderFallbackException) { return new(false, "trg", "Invalid camera UTF-16.", suffix.Length); }
        return off == suffix.Length
            ? new(true, "TR v12 cameras", "Camera suffix structurally bounded and preserved.", suffix.Length)
            : new(false, "trg", "Unrecognized bytes after camera suffix.", suffix.Length);
    }

    internal static Check Scenario(byte[] bytes)
    {
        if (bytes.Length < 12 || !bytes.AsSpan(0, 4).SequenceEqual("l33t"u8))
            return new(false, "mythscn", "l33t header missing or truncated.");
        var expected = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4));
        if (expected <= 0 || expected > MaxDecodedBytes || bytes.Length > MaxStoredBytes)
            return new(false, "mythscn", "Decoded/stored length outside host bounds.");
        foreach (var suffix in new[] { 0, 4 })
        {
            if (bytes.Length < 8 + 6 + suffix) continue;
            var zlibEnd = bytes.Length - suffix;
            uint a = 1, b = 0;
            long length = 0;
            try
            {
                using var input = new MemoryStream(bytes, 8, zlibEnd - 8, false);
                using var decompressor = new ZLibStream(input, CompressionMode.Decompress);
                var buffer = new byte[8192];
                int n;
                while ((n = decompressor.Read(buffer)) > 0)
                {
                    length += n;
                    if (length > expected) break;
                    // Adler's 5552-byte block bound keeps both accumulators within uint.
                    for (var start = 0; start < n; start += 5552)
                    {
                        var end = Math.Min(start + 5552, n);
                        for (var i = start; i < end; i++) { a += buffer[i]; b += a; }
                        a %= 65521; b %= 65521;
                    }
                }
                if (length == expected && BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(zlibEnd - 4, 4)) == (b << 16 | a))
                    return new(true, "l33t/zlib", suffix == 0 ? "No game trailer." : "Four opaque game trailer bytes preserved.", suffix);
            }
            catch (InvalidDataException) { /* Try reviewed alternate trailer shape. */ }
            catch (IOException) { /* Try reviewed alternate trailer shape. */ }
        }
        return new(false, "mythscn", "Zlib stream/Adler-32/decoded length invalid or unsupported trailer.");
    }

    internal static void SelfTest()
    {
        using var mem = new MemoryStream();
        mem.Write("l33t"u8); mem.Write(BitConverter.GetBytes(3));
        using (var z = new ZLibStream(mem, CompressionMode.Compress, true)) z.Write("abc"u8);
        var plain = mem.ToArray();
        if (!Scenario(plain).Valid || Scenario(plain).SuffixBytes != 0) throw new InvalidOperationException("No-trailer scenario refused.");
        var withTrailer = plain.Concat("tail"u8.ToArray()).ToArray();
        if (!Scenario(withTrailer).Valid || Scenario(withTrailer).SuffixBytes != 4) throw new InvalidOperationException("Four-byte trailer scenario refused.");
        if (Scenario(plain[..^2]).Valid || Scenario(withTrailer[..^2]).Valid) throw new InvalidOperationException("Truncated scenario accepted.");
        var corrupt = plain.ToArray(); corrupt[^1] ^= 1;
        if (Scenario(corrupt).Valid) throw new InvalidOperationException("Corrupt checksum accepted.");
        if (Scenario(plain.Concat("unexpected"u8.ToArray()).ToArray()).Valid) throw new InvalidOperationException("Unknown scenario trailer accepted.");
        var controller = new byte[14]; controller[0] = (byte)'T'; controller[1] = (byte)'R';
        BinaryPrimitives.WriteInt32LittleEndian(controller.AsSpan(2), 4);
        BinaryPrimitives.WriteInt32LittleEndian(controller.AsSpan(6), 12);
        controller.AsSpan(10).Fill(255);
        if (!Trigger(controller).Valid) throw new InvalidOperationException("Controller envelope refused.");
        using var camera = new MemoryStream();
        camera.Write(controller, 0, 10);
        camera.Write(BitConverter.GetBytes(1)); // One camera plus export-name string.
        foreach (var name in new[] { "export_cameras", "Intro" })
        {
            camera.Write(BitConverter.GetBytes(name.Length)); camera.Write(Utf16.GetBytes(name));
        }
        if (!Trigger(camera.ToArray()).Valid || Trigger(camera.ToArray()).SuffixBytes <= 4)
            throw new InvalidOperationException("Campaign camera envelope refused.");
        var wrong = camera.ToArray()[..^1];
        if (Trigger(wrong).Valid || Trigger(controller.Concat(new byte[] { 0, 0, 0, 0, 1 }).ToArray()).Valid)
            throw new InvalidOperationException("Invalid TR suffix accepted.");
    }
}
