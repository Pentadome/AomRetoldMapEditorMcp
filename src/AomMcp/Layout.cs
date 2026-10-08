using System.Security.Cryptography;
using System.Text.Json;

namespace AomMcp;

/// <summary>Stores hash-pinned native addresses, offsets, and runtime signature checks for one game build.</summary>
public sealed record Layout
{
    /// <summary>Gets the lowercase SHA-256 hash of the accepted executable.</summary>
    public string ExeSha256 { get; init; } = "";
    /// <summary>Gets the executable file version recorded for this layout.</summary>
    public string FileVersion { get; init; } = "";
    /// <summary>Gets the command dispatcher address relative to the module base.</summary>
    public uint DispatcherRva { get; init; }
    /// <summary>Gets the module-relative address of the command-context pointer.</summary>
    public uint ContextRva { get; init; }
    /// <summary>Gets the context byte offset of its owning thread ID.</summary>
    // Recovered context DWORD at +4 matches owning window thread; not a documented engine ABI.
    // Evidence: research/LIVE-FINDINGS.md and accepted layouts/<ExeSha256>.json; discovery rechecks it.
    public uint OwnerOffset { get; init; } = 4;
    /// <summary>Gets the context byte offset of its command endpoint pointer.</summary>
    // Recovered AMD64 endpoint pointer at +8; same build-specific context evidence as OwnerOffset.
    public uint EndpointOffset { get; init; } = 8;
    /// <summary>Gets the module-relative address of the game/editor global pointer.</summary>
    public uint EditorGlobalRva { get; init; }
    /// <summary>Gets the game-object byte offset of the editor-mode flag.</summary>
    public uint EditorFlagOffset { get; init; }
    /// <summary>Gets the game-object byte offset of the editor context pointer.</summary>
    public uint EditorPointerOffset { get; init; }
    /// <summary>Gets the module-relative address of the pointer/hover state global.</summary>
    public uint PointerGlobalRva { get; init; }
    /// <summary>Gets the pointer-state byte offset of the map-hover flag.</summary>
    public uint HoverOffset { get; init; }
    /// <summary>Gets the editor-context byte offset of the placement prototype ID.</summary>
    public uint ProtoOffset { get; init; }
    /// <summary>Gets the editor-context byte offset of the placement player.</summary>
    public uint PlayerOffset { get; init; }
    /// <summary>Gets the hexadecimal dispatcher prefix expected in runtime memory.</summary>
    public string PrefixHex { get; init; } = "";
    /// <summary>Gets the discovery or review notes supporting this layout.</summary>
    public string Provenance { get; init; } = "";
    /// <summary>Gets optional independently reviewed live-object read fields; null refuses unit listing.</summary>
    /// <remarks>Passive generator candidates do not invent these offsets. See research/LIVE-UNITS.md.</remarks>
    public UnitReadLayout? Units { get; init; }
    /// <summary>Gets optional native selection fields; absent candidates refuse inspection.</summary>
    public SelectionReadLayout? Selection { get; init; }
    /// <summary>Gets optional terrain/camera/render fields; absent candidates refuse map queries.</summary>
    public MapReadLayout? Map { get; init; }
    /// <summary>Gets optional independently corroborated runtime telemetry; unregistered candidates refuse loading.</summary>
    public RuntimeReadLayout? Runtime { get; init; }
    /// <summary>Gets optional reviewed terrain-type/water/edit-mode/paint-selection/heading fields (research/LIVE-WORLD.md).</summary>
    public WorldReadLayout? World { get; init; }
    /// <summary>Gets optional reviewed live player fields; obfuscated resources are intentionally absent.</summary>
    public PlayerReadLayout? Players { get; init; }
    /// <summary>Gets the dispatcher signature bytes decoded from <see cref="PrefixHex"/>.</summary>
    // Spaces are human-readable byte separators in layout JSON, not part of the runtime signature.
    public byte[] Prefix => Convert.FromHexString(PrefixHex.Replace(" ", ""));
    /// <summary>Serialization settings shared by accepted layouts and generated candidates.</summary>
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (long Length, DateTime Written, string Hash)> Hashes =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Computes a file's lowercase SHA-256 hash, reusing it while length and write time are unchanged.</summary>
    /// <param name="path">Path to the file to hash.</param>
    /// <returns>Lowercase hexadecimal digest.</returns>
    public static string Hash(string path)
    {
        // Game exe is ~91 MB and connected on every tool call; rehash only when the file changes.
        var file = new FileInfo(Path.GetFullPath(path));
        if (Hashes.TryGetValue(file.FullName, out var cached) && cached.Length == file.Length
            && cached.Written == file.LastWriteTimeUtc)
            return cached.Hash;
        using var stream = file.OpenRead();
        var hash = Convert.ToHexStringLower(SHA256.HashData(stream));
        Hashes[file.FullName] = (file.Length, file.LastWriteTimeUtc, hash);
        return hash;
    }

    /// <summary>Loads a layout and refuses mismatched hashes or incomplete guard metadata.</summary>
    /// <param name="path">Layout JSON file path.</param>
    /// <param name="hash">SHA-256 hash of the executable being connected.</param>
    /// <returns>Matching layout with a valid signature length and required addresses.</returns>
    /// <exception cref="InvalidDataException">Layout is empty, stale, or incomplete.</exception>
    public static Layout Load(string path, string hash)
    {
        var layout =
            JsonSerializer.Deserialize<Layout>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException("Empty layout.");
        if (layout.ExeSha256 != hash)
            throw new InvalidDataException(
                "Layout hash does not match game build. Run generator; never reuse stale offsets."
            );
        // Host guard policy: at least 16 signature bytes; at most native Packet.prefix's 64 bytes.
        // Zero RVAs mean missing metadata here, not an accepted callable address.
        if (
            layout.Prefix.Length is < 16 or > 64
            || layout.DispatcherRva == 0
            || layout.ContextRva == 0
            || layout.EditorGlobalRva == 0
        )
            throw new InvalidDataException("Incomplete layout.");
        if (layout.Units is { } units)
            LiveUnits.ValidateLayout(units);
        if (layout.Runtime is { } runtime)
            RuntimeTelemetry.ValidateLayout(runtime, hash);
        if (layout.World is { } world)
            LiveWorld.ValidateLayout(world);
        if (layout.Players is { } players)
            LiveWorld.ValidateLayout(players);
        return layout;
    }
}
