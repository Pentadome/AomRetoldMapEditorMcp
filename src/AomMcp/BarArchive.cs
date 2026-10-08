using CryBar.Bar;

namespace AomMcp;

/// <summary>Reads one installed BAR archive in-process with the CryBar library (lib/CryBarEditor submodule).</summary>
internal sealed class BarArchive : IDisposable
{
    readonly FileStream _stream;
    readonly Dictionary<string, BarFileEntry> _entries = new(StringComparer.Ordinal);

    /// <exception cref="InvalidDataException">CryBar could not parse the archive header/table.</exception>
    public BarArchive(string path)
    {
        _stream = File.OpenRead(path);
        var bar = new BarFile(_stream);
        if (!bar.Load(out var error) || bar.Entries is null)
        {
            _stream.Dispose();
            throw new InvalidDataException($"Failed to load BAR archive {path}: {error}");
        }
        // CryBar CLI list normalizes separators to '/'; generator paths use the same form.
        foreach (var entry in bar.Entries)
            _entries[entry.RelativePath.Replace('\\', '/')] = entry;
    }

    public IEnumerable<string> Paths => _entries.Keys;

    /// <summary>Decompresses one entry and decodes XMB to text XML (same as CLI export --decompress --convert).</summary>
    public void ExportXml(string path, string outputFile)
    {
        var entry = _entries.TryGetValue(path, out var e) ? e : throw new FileNotFoundException("BAR entry missing: " + path);
        using var data = entry.ReadDataDecompressedPooled(_stream) ?? throw new InvalidDataException("Failed to decompress " + path);
        var bytes = path.EndsWith(".xmb", StringComparison.OrdinalIgnoreCase)
            ? ConversionHelper.ConvertXmbToXmlBytes(data.Span) ?? data.Span.ToArray()
            : data.Span.ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
        File.WriteAllBytes(outputFile, bytes);
    }

    public void Dispose() => _stream.Dispose();
}
