using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace AomMcp;

/// <summary>Offline census of game-written scenario triggers: exemplar locations per condition/effect definition and
/// per-value-type decode/re-encode proofs. Reads scenario files in place; the written index holds locations and ids
/// only, never scenario bytes (generated/ is git-ignored and not packaged).</summary>
internal static class TriggerExemplars
{
    /// <summary>Exemplar index file name in the generated metadata directory.</summary>
    internal const string ExemplarsName = "trigger_exemplars.json";

    /// <summary>Index schema version; bump when the JSON shape changes so stale indexes regenerate.</summary>
    internal const int IndexVersion = 1;

    /// <summary>Samples a value type needs before an all-matching re-encode counts as proven.</summary>
    internal const int MinProofSamples = 3;

    /// <summary>Locations kept per definition (callers only need a few independent sources).</summary>
    internal const int MaxLocations = 20;

    /// <summary>Install-relative folder holding the shipped campaign scenarios (loose files).</summary>
    internal const string CampaignFolder = "campaign";

    /// <summary>Install subfolder that holds <see cref="CampaignFolder"/> and data (see <c>GameDataCatalog.Archive</c>).</summary>
    const string GameFolder = "game";

    /// <summary>Generated subfolder that <c>Generator.ExportGameData</c> writes trigger_data.xml into.</summary>
    const string DataFolder = "data";

    /// <summary>Scenario file extension.</summary>
    const string ScenarioPattern = "*.mythscn";

    /// <summary>Trigger catalog written by <c>Generator.ExportGameData</c> into generated/data.</summary>
    const string TriggerData = "trigger_data.xml";

    /// <summary>Supported trigger section version (TR v12, see <see cref="CampaignTriggers"/>).</summary>
    const uint TriggerVersion = 12;

    /// <summary>Byte width of the little-endian count and undecoded U32 prefix fields.</summary>
    const int U32Bytes = 4;

    /// <summary>Unit/object reference list value type (values + object refs + selection flag); not re-encoded.</summary>
    const uint ObjectListType = 4;

    /// <summary>Plain wide-string list value type.</summary>
    const uint WideListType = 7;

    /// <summary>String-id list value type (count, U32, strings); catalog vartype stringid.</summary>
    internal const uint StringIdType = 22;

    /// <summary>Fixed three/four/two string value types (vectors/areas), each with a U32 prefix.</summary>
    static readonly Dictionary<uint, int> FixedTypes = new()
    {
        [42] = 3,
        [43] = 4,
        [50] = 2,
    };

    /// <summary>Single-value types followed by a one-byte flag (mirrors <see cref="CampaignTriggers"/> reader).</summary>
    static readonly HashSet<uint> FlaggedTypes = [2, 5, 8, 56];
    static readonly UnicodeEncoding Utf16 = new(false, false, true);

    /// <summary>One exemplar location: a game-written element that can be byte-copied.</summary>
    internal sealed record Location(
        string File,
        string Sha256,
        uint TriggerId,
        string TriggerName,
        int ElementIndex
    );

    /// <summary>Accumulated census data for one definition.</summary>
    sealed class Definition(string kind, string name)
    {
        /// <summary>condition or effect.</summary>
        public string Kind { get; } = kind;

        /// <summary>Catalog definition name, e.g. "Chat: Send Chat".</summary>
        public string Name { get; } = name;

        /// <summary>Occurrences across all scanned files.</summary>
        public int Count { get; set; }

        /// <summary>Files containing at least one occurrence.</summary>
        public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>First <see cref="MaxLocations"/> occurrences, one per file first.</summary>
        public List<Location> Locations { get; } = [];

        /// <summary>Observed parameter key → numeric value types.</summary>
        public SortedDictionary<string, SortedSet<uint>> Parameters { get; } =
            new(StringComparer.Ordinal);
    }

    /// <summary>Accumulated re-encode evidence for one numeric value type.</summary>
    sealed class TypeProof(uint valueType)
    {
        /// <summary>Numeric TR value type.</summary>
        public uint ValueType { get; } = valueType;

        /// <summary>Catalog vartype names whose parameters used this numeric type.</summary>
        public SortedSet<string> Vartypes { get; } = new(StringComparer.Ordinal);

        /// <summary>Arguments checked.</summary>
        public int Samples { get; set; }

        /// <summary>Arguments whose decoded values re-encoded to identical bytes.</summary>
        public int Matches { get; set; }

        /// <summary>Files contributing samples.</summary>
        public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Distinct undecoded U32 prefix values (kept verbatim by the encoder).</summary>
        public SortedSet<uint> Prefixes { get; } = [];

        /// <summary>First mismatch or unsupported shape, for the report.</summary>
        public string? FirstFailure { get; set; }
    }

    /// <summary>Census result: serialisable exemplar index and value-type proofs plus per-file failures.</summary>
    internal sealed record Census(
        object Exemplars,
        object Vartypes,
        int Files,
        int Parsed,
        int WithTriggers,
        object[] Failures,
        string[] ProvenVartypes,
        string[] UnprovenVartypes
    );

    /// <summary>Install corpus identity used for freshness: scenario count, total bytes and newest UTC write time.</summary>
    internal sealed record Corpus(int Files, long Bytes, long LatestWriteUtcTicks);

    /// <summary>Install game directory (parent of campaign/ and data/) for <paramref name="exe"/>.</summary>
    internal static string GameRoot(string exe) =>
        Path.Combine(Path.GetDirectoryName(exe)!, GameFolder);

    /// <summary>Index path inside <paramref name="output"/> (normally <see cref="Generator.DefaultOutput"/>).</summary>
    internal static string IndexPath(string output) => Path.Combine(output, ExemplarsName);

    /// <summary>Current fingerprint of the shipped campaign scenarios.</summary>
    internal static Corpus Fingerprint(string exe)
    {
        var files = ScenarioFiles(Path.Combine(GameRoot(exe), CampaignFolder))
            .Select(f => new FileInfo(f))
            .ToArray();
        return new(
            files.Length,
            files.Sum(f => f.Length),
            files.Length == 0 ? 0 : files.Max(f => f.LastWriteTimeUtc.Ticks)
        );
    }

    /// <summary>Scans the install campaign corpus (D8: install only, mods are census-only) and writes the index.</summary>
    /// <param name="exe">Installed game executable path.</param>
    /// <param name="hash">Executable SHA-256 the index is keyed on.</param>
    /// <param name="output">Generated metadata directory; trigger_data.xml is read from its data/ subfolder for vartype names.</param>
    /// <returns>Census counts for the generator result.</returns>
    internal static Census Export(string exe, string hash, string output)
    {
        var root = GameRoot(exe);
        var corpus = Fingerprint(exe);
        var census = Scan(
            root,
            ScenarioFiles(Path.Combine(root, CampaignFolder)),
            CatalogVartypes(Path.Combine(output, DataFolder))
        );
        Directory.CreateDirectory(output);
        File.WriteAllText(
            IndexPath(output),
            JsonSerializer.Serialize(
                new
                {
                    version = IndexVersion,
                    exeSha256 = hash,
                    corpus,
                    root = GameFolder,
                    census.Files,
                    census.Parsed,
                    census.WithTriggers,
                    skipped = census.Failures,
                    exemplars = census.Exemplars,
                    vartypes = census.Vartypes,
                    limitation = "Locations are install-relative (under game/) with the SHA-256 seen at generation; readers re-hash before copying. TR v11 files are skipped (not a v12 byte source).",
                },
                Layout.Json
            )
        );
        return census;
    }

    /// <summary>Loads the index, or null when it is missing or unreadable.</summary>
    internal static JsonElement? Load(string output)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(IndexPath(output)));
            return document.RootElement.Clone();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>True when the index matches <paramref name="hash"/>, <see cref="IndexVersion"/> and the current campaign fingerprint.</summary>
    internal static bool IsFresh(JsonElement index, string exe, string hash)
    {
        try
        {
            var corpus = index.GetProperty("corpus").Deserialize<Corpus>(Layout.Json);
            return index.GetProperty("version").GetInt32() == IndexVersion
                && string.Equals(
                    index.GetProperty("exeSha256").GetString(),
                    hash,
                    StringComparison.OrdinalIgnoreCase
                )
                && corpus == Fingerprint(exe);
        }
        catch (Exception e)
            when (e
                    is KeyNotFoundException
                        or InvalidOperationException
                        or JsonException
                        or IOException
            )
        {
            return false;
        }
    }

    /// <summary>True when the index file exists and is fresh (see <see cref="IsFresh(JsonElement, string, string)"/>).</summary>
    internal static bool IsFresh(string output, string exe, string hash) =>
        Load(output) is { } index && IsFresh(index, exe, hash);

    /// <summary>All scenario files under <paramref name="root"/>, ordinal-sorted for stable output.</summary>
    internal static string[] ScenarioFiles(string root) =>
        Directory.Exists(root)
            ?
            [
                .. Directory
                    .EnumerateFiles(root, ScenarioPattern, SearchOption.AllDirectories)
                    .Order(StringComparer.OrdinalIgnoreCase),
            ]
            : [];

    /// <summary>Catalog parameter vartypes keyed by "kind/definition/parameter" from generated trigger_data.xml; empty when absent.</summary>
    internal static Dictionary<string, string> CatalogVartypes(string dataDirectory)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var file = Path.Combine(dataDirectory, TriggerData);
        if (!File.Exists(file))
            return result;
        foreach (
            var element in XDocument
                .Load(file)
                .Descendants()
                .Where(e => e.Name.LocalName is "condition" or "effect")
        )
        {
            var name = (string?)element.Attribute("name");
            if (name is null)
                continue;
            foreach (var param in element.Elements("param"))
                if (
                    (string?)param.Attribute("name") is { } key
                    && (string?)param.Attribute("vartype") is { } vartype
                )
                    result.TryAdd(element.Name.LocalName + "/" + name + "/" + key, vartype);
        }
        return result;
    }

    /// <summary>Scans scenario files and builds the exemplar index and re-encode proofs.</summary>
    /// <param name="root">Directory that file paths in locations are made relative to.</param>
    /// <param name="files">Scenario files to read (in place, read-only).</param>
    /// <param name="vartypes">Catalog vartypes from <see cref="CatalogVartypes"/>.</param>
    internal static Census Scan(
        string root,
        IEnumerable<string> files,
        Dictionary<string, string> vartypes
    )
    {
        var definitions = new Dictionary<string, Definition>(StringComparer.Ordinal);
        var proofs = new SortedDictionary<uint, TypeProof>();
        var failures = new List<object>();
        int total = 0,
            parsed = 0,
            withTriggers = 0;
        foreach (var path in files)
        {
            total++;
            var relative = Path.GetRelativePath(root, path);
            CampaignTriggers.Document doc;
            string sha;
            try
            {
                // Trigger section only: player-block layouts of older scenarios are not needed (and not all reviewed).
                var document = CheckpointDocument.Read(path);
                sha = document.Sha256;
                parsed++;
                var section = document.Root.One("TR").ToArray();
                var version =
                    section.Length >= U32Bytes
                        ? BinaryPrimitives.ReadUInt32LittleEndian(section)
                        : 0;
                if (version != TriggerVersion)
                {
                    failures.Add(
                        new
                        {
                            file = relative,
                            reason = "TR v" + version + " (only v" + TriggerVersion + " reviewed)",
                        }
                    );
                    continue;
                }
                doc = CampaignTriggers.ParseScenarioSection(section);
            }
            catch (Exception e)
                when (e
                        is InvalidDataException
                            or ArgumentException
                            or IOException
                            or EndOfStreamException
                )
            {
                failures.Add(new { file = relative, reason = e.Message });
                continue;
            }
            withTriggers++;
            foreach (var trigger in doc.Triggers)
            foreach (
                var (kind, elements) in new[]
                {
                    ("condition", trigger.Conditions),
                    ("effect", trigger.Effects),
                }
            )
                for (var i = 0; i < elements.Length; i++)
                {
                    var element = elements[i];
                    var key = kind + "/" + element.Name;
                    if (!definitions.TryGetValue(key, out var definition))
                        definitions[key] = definition = new(kind, element.Name);
                    definition.Count++;
                    var firstInFile = definition.Files.Add(relative);
                    if (
                        definition.Locations.Count < MaxLocations
                        && (firstInFile || definition.Locations.Count < definition.Files.Count)
                    )
                        definition.Locations.Add(new(relative, sha, trigger.Id, trigger.Name, i));
                    foreach (var arg in element.Args)
                    {
                        if (!definition.Parameters.TryGetValue(arg.Key, out var types))
                            definition.Parameters[arg.Key] = types = [];
                        types.Add(arg.ValueType);
                        if (!proofs.TryGetValue(arg.ValueType, out var proof))
                            proofs[arg.ValueType] = proof = new(arg.ValueType);
                        if (vartypes.TryGetValue(key + "/" + arg.Key, out var vartype))
                            proof.Vartypes.Add(vartype);
                        proof.Samples++;
                        proof.Files.Add(relative);
                        var region = doc.Body.AsSpan(arg.ValueStart, arg.ValueEnd - arg.ValueStart);
                        if (Prefix(arg.ValueType) is { } at && region.Length >= at + U32Bytes)
                            proof.Prefixes.Add(
                                BinaryPrimitives.ReadUInt32LittleEndian(region[at..])
                            );
                        var encoded = Encode(doc.Body, arg, arg.Values);
                        if (encoded is not null && region.SequenceEqual(encoded))
                            proof.Matches++;
                        else
                            proof.FirstFailure ??=
                                (encoded is null ? "unsupported shape" : "re-encode differs")
                                + $" at {relative} trigger {trigger.Id} {kind} {i} '{element.Name}' {arg.Key}";
                    }
                }
        }
        var typeRows = proofs
            .Values.Select(p => new
            {
                valueType = p.ValueType,
                vartypes = p.Vartypes,
                samples = p.Samples,
                matches = p.Matches,
                files = p.Files.Count,
                proven = Proven(p),
                prefixes = p.Prefixes.Take(MaxLocations),
                firstFailure = p.FirstFailure,
            })
            .ToArray();
        var proven = proofs
            .Values.Where(Proven)
            .SelectMany(p => p.Vartypes)
            .ToHashSet(StringComparer.Ordinal);
        // A catalog vartype is proven only when every numeric type it was seen with is proven.
        var unproven = proofs
            .Values.Where(p => !Proven(p))
            .SelectMany(p => p.Vartypes)
            .ToHashSet(StringComparer.Ordinal);
        proven.ExceptWith(unproven);
        var exemplars = new
        {
            version = IndexVersion,
            files = total,
            withTriggers,
            definitions = definitions
                .Values.OrderBy(d => d.Kind, StringComparer.Ordinal)
                .ThenBy(d => d.Name, StringComparer.Ordinal)
                .Select(d => new
                {
                    kind = d.Kind,
                    name = d.Name,
                    count = d.Count,
                    files = d.Files.Count,
                    parameters = d.Parameters,
                    locations = d.Locations,
                }),
        };
        var vartypeIndex = new
        {
            version = IndexVersion,
            minSamples = MinProofSamples,
            types = typeRows,
            provenVartypes = proven.Order(StringComparer.Ordinal),
            unprovenVartypes = unproven.Order(StringComparer.Ordinal),
            rule = "A numeric value type is proven when it has at least minSamples arguments and every decoded value re-encodes to identical bytes (undecoded U32 prefix and flag byte kept verbatim). A catalog vartype is proven when all numeric types it appeared with are proven.",
        };
        return new(
            exemplars,
            vartypeIndex,
            total,
            parsed,
            withTriggers,
            [.. failures],
            [.. proven.Order(StringComparer.Ordinal)],
            [.. unproven.Order(StringComparer.Ordinal)]
        );
    }

    static bool Proven(TypeProof p) => p.Samples >= MinProofSamples && p.Matches == p.Samples;

    /// <summary>Offset of the undecoded U32 inside a value region, or null when the shape has none.</summary>
    static int? Prefix(uint type) =>
        type switch
        {
            ObjectListType or WideListType => null,
            StringIdType => U32Bytes,
            _ => 0,
        };

    /// <summary>Re-encodes <paramref name="values"/> as the value region of <paramref name="arg"/>, keeping the undecoded
    /// U32 and flag byte from <paramref name="body"/>. Null for object lists or a value count the shape cannot hold.</summary>
    internal static byte[]? Encode(byte[] body, CampaignTriggers.Arg arg, string[] values)
    {
        var region = body.AsSpan(arg.ValueStart, arg.ValueEnd - arg.ValueStart);
        var output = new List<byte>();
        switch (arg.ValueType)
        {
            case ObjectListType:
                return null;
            case WideListType:
                Count(output, values.Length);
                foreach (var v in values)
                    Wide(output, v);
                break;
            case StringIdType:
                if (region.Length < 2 * U32Bytes)
                    return null;
                Count(output, values.Length);
                output.AddRange(region.Slice(U32Bytes, U32Bytes).ToArray());
                foreach (var v in values)
                    Wide(output, v);
                break;
            default:
                var expected = FixedTypes.TryGetValue(arg.ValueType, out var n) ? n : 1;
                if (values.Length != expected || region.Length < U32Bytes)
                    return null;
                output.AddRange(region[..U32Bytes].ToArray());
                foreach (var v in values)
                    Wide(output, v);
                if (!FixedTypes.ContainsKey(arg.ValueType) && FlaggedTypes.Contains(arg.ValueType))
                {
                    if (region.Length == 0)
                        return null;
                    output.Add(region[^1]);
                }
                break;
        }
        return [.. output];
    }

    static void Count(List<byte> output, int value)
    {
        Span<byte> bytes = stackalloc byte[U32Bytes];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        output.AddRange(bytes.ToArray());
    }

    static void Wide(List<byte> output, string value)
    {
        Count(output, value.Length);
        output.AddRange(Utf16.GetBytes(value));
    }
}
