namespace AomMcp;

/// <summary>Reads AMD64 PE32+ section metadata and embedded names from an executable on disk.</summary>
internal sealed class PeImage
{
    /// <summary>Describes a PE section's mapped and on-disk ranges.</summary>
    /// <param name="Name">Section name.</param>
    /// <param name="Rva">Address relative to the mapped module base.</param>
    /// <param name="VirtualSize">Section size in the mapped image.</param>
    /// <param name="Offset">Start of section data in the executable file.</param>
    /// <param name="RawSize">Size of section data stored on disk.</param>
    internal sealed record Section(string Name, int Rva, int VirtualSize, int Offset, int RawSize);

    /// <summary>Gets the executable's complete on-disk bytes.</summary>
    public byte[] Bytes { get; }
    /// <summary>Gets section headers parsed from the PE image.</summary>
    public Section[] Sections { get; }
    /// <summary>Gets the PE image's mapped size in bytes.</summary>
    public int ImageSize { get; }

    /// <summary>Reads the file and requires an AMD64 PE32+ image.</summary>
    /// <param name="path">Executable file path.</param>
    public PeImage(string path)
    {
        Bytes = File.ReadAllBytes(path);
        // Microsoft PE/COFF spec: DOS e_lfanew is at 0x3c; PE signature (4 bytes) +
        // IMAGE_FILE_HEADER (20 bytes) put the optional header at PE + 24.
        int pe = BitConverter.ToInt32(Bytes, 0x3c),
            optional = pe + 24;
        // 1024 is a host sanity floor, not a PE minimum. Spec values below are:
        // 0x4550 = little-endian "PE\0\0", 0x8664 = AMD64, 0x20b = PE32+ optional header magic.
        if (
            Bytes.Length < 1024
            || BitConverter.ToUInt32(Bytes, pe) != 0x4550
            || BitConverter.ToUInt16(Bytes, pe + 4) != 0x8664
            || BitConverter.ToUInt16(Bytes, optional) != 0x20b
        )
            throw new InvalidDataException("Expected AMD64 PE32+ game image.");
        // PE/COFF offsets: SizeOfImage +56 in optional header; NumberOfSections +6 and
        // SizeOfOptionalHeader +20 relative to PE signature; section headers follow optional header.
        ImageSize = BitConverter.ToInt32(Bytes, optional + 56);
        int count = BitConverter.ToUInt16(Bytes, pe + 6),
            table = optional + BitConverter.ToUInt16(Bytes, pe + 20);
        Sections = Enumerable
            .Range(0, count)
            .Select(i =>
            {
                // IMAGE_SECTION_HEADER is 40 bytes: 8-byte NUL-padded name, VirtualSize +8,
                // VirtualAddress +12, SizeOfRawData +16, PointerToRawData +20 (PE/COFF spec).
                var p = table + i * 40;
                var name = System.Text.Encoding.ASCII.GetString(Bytes, p, 8).TrimEnd('\0');
                return new Section(
                    name,
                    BitConverter.ToInt32(Bytes, p + 12),
                    BitConverter.ToInt32(Bytes, p + 8),
                    BitConverter.ToInt32(Bytes, p + 20),
                    BitConverter.ToInt32(Bytes, p + 16)
                );
            })
            .ToArray();
    }

    /// <summary>Finds null-delimited ASCII names in sections other than .text.</summary>
    /// <param name="name">Exact embedded name to locate.</param>
    /// <returns>Module-relative addresses of matching names, not file offsets.</returns>
    public IEnumerable<int> Names(string name)
    {
        // Native registration/help names are ASCII C strings; NUL terminates them.
        // .text is the conventional code section; scan data sections for standalone names.
        var needle = System.Text.Encoding.ASCII.GetBytes(name + "\0");
        foreach (var section in Sections.Where(s => s.Name != ".text"))
        {
            var pos = section.Offset;
            while (pos < section.Offset + section.RawSize)
            {
                var relative = Bytes
                    .AsSpan(pos, section.Offset + section.RawSize - pos)
                    .IndexOf(needle);
                if (relative < 0)
                    break;
                var found = pos + relative;
                if (found == section.Offset || Bytes[found - 1] == 0)
                    yield return section.Rva + found - section.Offset;
                pos = found + needle.Length;
            }
        }
    }
}
