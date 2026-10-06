using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AomMcp;

/// <summary>Read-only bounded checkpoint envelope and ordered section views. Never serializes a scenario.</summary>
internal sealed class CheckpointDocument
{
    internal sealed record Section(string Tag, int Occurrence, ReadOnlyMemory<byte> Data);
    internal sealed record Frame(Section[] Sections, ReadOnlyMemory<byte> Tail)
    {
        internal ReadOnlyMemory<byte> One(string tag)
        {
            var found = Sections.Where(s => s.Tag == tag).ToArray();
            if (found.Length != 1) throw new InvalidDataException("Checkpoint requires exactly one section: " + tag);
            return found[0].Data;
        }
    }
    internal string Path { get; }
    internal string Sha256 { get; }
    internal string Format { get; }
    internal int SuffixBytes { get; }
    internal ReadOnlyMemory<byte> Decoded { get; }
    internal ReadOnlyMemory<byte> StoredSuffix { get; }
    internal Frame Root { get; }
    internal Frame World { get; }
    internal uint WorldHeader { get; }

    CheckpointDocument(string path, byte[] stored, ExportFormats.Check check, byte[] decoded)
    {
        Path = path; Sha256 = Hash(stored); Format = check.Format; SuffixBytes = check.SuffixBytes;
        Decoded = decoded; StoredSuffix = stored.AsMemory(stored.Length - check.SuffixBytes);
        if (decoded.Length < 10 || !decoded.AsSpan(0, 2).SequenceEqual("BG"u8))
            throw new InvalidDataException("Checkpoint decoded BG/length mismatch.");
        Root = Sections(decoded, 10);
        var world = Root.One("J1");
        if (world.Length < 4) throw new InvalidDataException("Checkpoint J1 header truncated.");
        WorldHeader = BinaryPrimitives.ReadUInt32LittleEndian(world.Span);
        World = Sections(world, 4);
    }

    internal static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    internal static void RequireHash(string observed, string? expected)
    {
        if (expected is null) return;
        if (expected.Length != 64 || !expected.All(Uri.IsHexDigit)) throw new ArgumentException("Expected SHA-256 must have 64 hexadecimal digits.");
        if (!observed.Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Source changed since expected SHA-256; no write attempted.");
    }
    internal static string? Expected(JsonElement args, string name = "expectedSha256") => args.TryGetProperty(name, out var e) ? e.GetString() : null;

    internal static CheckpointDocument Read(string path, string? expectedSha256 = null)
    {
        path = EditorFiles.LocalPath(path);
        if (!path.EndsWith(".mythscn", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Read-only checkpoint requires .mythscn.");
        var length = new FileInfo(path).Length;
        if (length <= 0 || length > ExportFormats.MaxStoredBytes) throw new InvalidDataException("Checkpoint outside stored-file bound.");
        var bytes = File.ReadAllBytes(path);
        RequireHash(Hash(bytes), expectedSha256);
        return Decode(path, bytes);
    }

    internal static CheckpointDocument Decode(string path, byte[] bytes)
    {
        var check = ExportFormats.Scenario(bytes);
        if (!check.Valid) throw new InvalidDataException(check.Reason);
        var expected = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4));
        var decoded = new byte[expected]; // Exact validated bound; no unbounded CopyTo allocation.
        using var input = new MemoryStream(bytes, 8, bytes.Length - 8 - check.SuffixBytes, false);
        using var z = new ZLibStream(input, CompressionMode.Decompress);
        z.ReadExactly(decoded);
        if (z.ReadByte() != -1) throw new InvalidDataException("Checkpoint decoded length mismatch.");
        return new(path, bytes, check, decoded);
    }

    internal static Frame Sections(ReadOnlyMemory<byte> bytes, int start)
    {
        if (start < 0 || start > bytes.Length) throw new InvalidDataException("Checkpoint section start outside bounds.");
        var result = new List<Section>(); var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var p = start;
        while (bytes.Length - p >= 6)
        {
            if (result.Count >= 50_000) throw new InvalidDataException("Checkpoint section count exceeds bound.");
            var tagBytes = bytes.Span.Slice(p, 2);
            if (tagBytes[0] is < 32 or > 126 || tagBytes[1] is < 32 or > 126) throw new InvalidDataException("Unreviewed checkpoint section tag.");
            var tag = Encoding.ASCII.GetString(tagBytes);
            var n = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Span.Slice(p + 2, 4));
            if (n > ExportFormats.MaxDecodedBytes || n > bytes.Length - p - 6) throw new InvalidDataException("Checkpoint section outside bounds: " + tag);
            var ordinal = counts.GetValueOrDefault(tag); counts[tag] = ordinal + 1;
            result.Add(new(tag, ordinal, bytes.Slice(p + 6, (int)n))); p += 6 + (int)n;
        }
        return new(result.ToArray(), bytes[p..]); // Opaque tail explicitly retained, never silently discarded.
    }

    internal sealed class Cursor(ReadOnlyMemory<byte> data)
    {
        internal int Offset { get; private set; }
        internal int Remaining => data.Length - Offset;
        internal ReadOnlyMemory<byte> Take(int n)
        {
            if (n < 0 || n > Remaining) throw new InvalidDataException("Checkpoint record truncated at " + Offset + ".");
            var value = data.Slice(Offset, n); Offset += n; return value;
        }
        internal uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4).Span);
        internal byte Byte() => Take(1).Span[0];
        internal int Count(int max)
        {
            var n = U32(); if (n > max) throw new InvalidDataException("Checkpoint count exceeds host bound."); return (int)n;
        }
        internal ReadOnlyMemory<byte> Block(string tag)
        {
            if (!Take(2).Span.SequenceEqual(Encoding.ASCII.GetBytes(tag))) throw new InvalidDataException("Checkpoint record marker mismatch: " + tag);
            return Take(Count(ExportFormats.MaxDecodedBytes));
        }
        internal string Wide(int max = 4096)
        {
            var count = Count(max);
            try { return new UnicodeEncoding(false, false, true).GetString(Take(checked(count * 2)).Span); }
            catch (DecoderFallbackException e) { throw new InvalidDataException("Invalid checkpoint UTF-16.", e); }
        }
    }

    internal static void SelfTest()
    {
        var doc = Decode("synthetic", WorkflowFixtures.Checkpoint());
        if (doc.WorldHeader != 444 || doc.World.Sections.Count(s => s.Tag == "UA") != 2 || doc.Root.Tail.Length != 1)
            throw new InvalidOperationException("Checkpoint envelope fixture failed.");
        using var section = new MemoryStream();
        foreach (var value in new[] { 1, 2 }) { section.Write("SS"u8); section.Write(BitConverter.GetBytes(4)); section.Write(BitConverter.GetBytes(value)); }
        section.WriteByte(0);
        var f = Sections(section.ToArray(), 0);
        if (f.Sections.Length != 2 || f.Sections[1].Occurrence != 1 || f.Tail.Length != 1) throw new InvalidOperationException("Ordered checkpoint sections fixture failed.");
        try { _ = f.One("SS"); throw new InvalidOperationException("Duplicate required section accepted."); } catch (InvalidDataException) { }
        try { _ = Sections(new byte[] { 65, 66, 255, 255, 255, 255 }, 0); throw new InvalidOperationException("Truncated section accepted."); } catch (InvalidDataException) { }
        try { RequireHash(new string('a', 64), new string('b', 64)); throw new InvalidOperationException("Changed source accepted."); } catch (ArgumentException) { }
        var unicode = new Cursor(BitConverter.GetBytes(4).Concat(Encoding.Unicode.GetBytes("塔🌴Δ")).ToArray());
        if (unicode.Wide() != "塔🌴Δ" || unicode.Remaining != 0) throw new InvalidOperationException("Unicode cursor fixture failed.");
        try { _ = new Cursor(new byte[] { 1, 0, 0, 0, 0, 216 }).Wide(); throw new InvalidOperationException("Invalid UTF-16 accepted."); } catch (InvalidDataException) { }
    }
}
