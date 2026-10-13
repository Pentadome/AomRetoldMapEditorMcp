using System.Text.Json;
using System.Xml.Linq;

namespace AomMcp;

/// <summary>
/// editor_trigger_elements: the shipped trigger condition/effect catalog (trigger_data.xml) plus, for a supplied TR export,
/// existing exemplars that editor_trigger_edit can byte-copy. Metadata only; it never builds element bytes.
/// </summary>
internal static class TriggerElements
{
    internal const string Tool = "editor_trigger_elements";
    internal const int MaxLimit = 50,
        MaxExemplars = 5;
    internal static readonly string[] Kinds = ["any", "effect", "condition"];
    static readonly string[] Allowed =
    [
        "kind",
        "name",
        "filter",
        "path",
        "expectedSha256",
        "library",
        "offset",
        "limit",
    ];
    static readonly string[] Replaceable =
    [
        "Player",
        "PlayerID",
        "FromPlayerID",
        "ToPlayerID",
        "EventID",
        "TechID",
        "Count",
        "Dist",
        "Status",
        "Value",
        "Duration",
        "ProtoUnit",
        "UnitType",
        "Command",
        "QVName",
        "Op",
        "CodeSnippet",
    ];

    internal static void Preflight(JsonElement args)
    {
        Catalog.ValidateObject(args, Allowed);
        if (args.TryGetProperty("kind", out var kind) && !Kinds.Contains(kind.GetString()))
            throw new ArgumentException("kind must be any, effect or condition.");
        if (args.TryGetProperty("path", out _) != args.TryGetProperty("expectedSha256", out _))
            throw new ArgumentException(
                "path and expectedSha256 go together (hash-pinned TR export)."
            );
        if (
            args.TryGetProperty("library", out var library)
            && library.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
        )
            throw new ArgumentException("library must be a boolean.");
    }

    /// <summary>Loads the install exemplar index for library=true, failing with METADATA_MISSING when it was never generated.</summary>
    static JsonElement LoadLibrary() =>
        TriggerExemplars.Load(Generator.DefaultOutput())
        ?? throw new WorkflowFailure(
            "METADATA_MISSING",
            "preflight",
            "Trigger exemplar index (generated/" + TriggerExemplars.ExemplarsName + ") is missing.",
            false,
            false,
            "Run editor_generate_catalog (no game connection; it scans the shipped campaign scenarios in place), then retry."
        );

    /// <summary>Index definitions keyed by "kind/name".</summary>
    static Dictionary<string, JsonElement> LibraryDefinitions(JsonElement index) =>
        index
            .GetProperty("exemplars")
            .GetProperty("definitions")
            .EnumerateArray()
            .ToDictionary(
                d => d.GetProperty("kind").GetString() + "/" + d.GetProperty("name").GetString(),
                StringComparer.Ordinal
            );

    /// <summary>Index location plus the absolute copy source for editor_trigger_edit copyEffects (sourcePath/sourceSha256/sourceTriggerId/expectedSourceName/effectIndex).</summary>
    static object Location(JsonElement location, string exe) =>
        new
        {
            file = location.GetProperty("file").GetString(),
            triggerId = location.GetProperty("triggerId").GetUInt32(),
            triggerName = location.GetProperty("triggerName").GetString(),
            elementIndex = location.GetProperty("elementIndex").GetInt32(),
            sourcePath = Path.Combine(
                TriggerExemplars.GameRoot(exe),
                location.GetProperty("file").GetString()!
            ),
            sourceSha256 = location.GetProperty("sha256").GetString(),
        };

    static string DataFile() => Path.Combine(Generator.DefaultOutput(), "data", "trigger_data.xml");

    /// <summary>Runs the lookup.</summary>
    /// <param name="args">Validated tool arguments.</param>
    /// <param name="exe">Installed executable, for library freshness.</param>
    /// <param name="hash">Running executable SHA-256, for library freshness.</param>
    internal static object Query(JsonElement args, string exe, string hash)
    {
        Preflight(args);
        string Text(string key) => args.TryGetProperty(key, out var v) ? v.GetString()! : "";
        var file = DataFile();
        if (!File.Exists(file))
            throw new WorkflowFailure(
                "METADATA_MISSING",
                "preflight",
                "Trigger element catalog (generated/data/trigger_data.xml) is missing.",
                false,
                false,
                "Run editor_generate_catalog (no game connection), then retry."
            );
        var root = XDocument.Load(file).Root!;
        var kind = Text("kind") is { Length: > 0 } k ? k : "any";
        var name = Text("name");
        var filter = Text("filter");
        var offset = args.TryGetProperty("offset", out var o) ? o.GetInt32() : 0;
        var limit = args.TryGetProperty("limit", out var l) ? l.GetInt32() : 10;
        if (limit is < 1 or > MaxLimit || offset < 0)
            throw new ArgumentException($"limit must be 1..{MaxLimit} and offset >= 0.");

        var entries = new List<(string Kind, XElement Node)>();
        foreach (
            var (section, tag, label) in new[]
            {
                ("conditions", "condition", "condition"),
                ("effects", "effect", "effect"),
            }
        )
            if (kind is "any" || kind == label)
                entries.AddRange(
                    (root.Element(section)?.Elements(tag) ?? []).Select(e => (label, e))
                );
        var matches = entries
            .Where(e =>
            {
                var n = e.Node.Attribute("name")?.Value ?? "";
                return (name.Length == 0 || n.Equals(name, StringComparison.OrdinalIgnoreCase))
                    && (
                        filter.Length == 0 || n.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    );
            })
            .ToArray();

        var library =
            args.TryGetProperty("library", out var lib) && lib.GetBoolean()
                ? LoadLibrary()
                : (JsonElement?)null;
        var libraryDefinitions = library is { } index ? LibraryDefinitions(index) : null;
        var proven = library is { } provenIndex
            ? provenIndex
                .GetProperty("vartypes")
                .GetProperty("provenVartypes")
                .EnumerateArray()
                .Select(v => v.GetString()!)
                .ToHashSet(StringComparer.Ordinal)
            : null;

        CampaignTriggers.Document? export = null;
        if (args.TryGetProperty("path", out var path))
        {
            export = CampaignTriggers.Read(path.GetString()!);
            if (
                !export.Sha256.Equals(
                    args.GetProperty("expectedSha256").GetString(),
                    StringComparison.OrdinalIgnoreCase
                )
            )
                throw new WorkflowFailure(
                    "SOURCE_CHANGED",
                    "preflight",
                    "TR export SHA-256 differs from expectedSha256.",
                    false,
                    false,
                    "Inspect the export again; nothing was read beyond the file."
                );
        }

        var page = matches
            .Skip(offset)
            .Take(limit)
            .Select(m =>
            {
                var node = m.Node;
                var n = node.Attribute("name")!.Value;
                var parameters = node.Elements("param")
                    .Select(p =>
                    {
                        var key = p.Attribute("name")?.Value ?? "";
                        return new
                        {
                            name = key,
                            label = p.Attribute("dispname")?.Value,
                            vartype = p.Attribute("vartype")?.Value,
                            @default = p.Value,
                            editableByTriggerEdit = Replaceable.Contains(
                                key,
                                StringComparer.OrdinalIgnoreCase
                            ),
                            encodingProven = proven is null
                                ? (bool?)null
                                : proven.Contains(p.Attribute("vartype")?.Value ?? ""),
                        };
                    })
                    .ToArray();
                object[]? exemplars = null;
                if (export is not null)
                    exemplars =
                    [
                        .. export
                            .Triggers.SelectMany(t =>
                                (m.Kind == "effect" ? t.Effects : t.Conditions)
                                    .Select((e, index) => (t, e, index))
                                    .Where(x => x.e.Name.Equals(n, StringComparison.Ordinal))
                            )
                            .Take(MaxExemplars)
                            .Select(x =>
                                (object)
                                    new
                                    {
                                        triggerId = x.t.Id,
                                        triggerName = x.t.Name,
                                        elementIndex = x.index,
                                        args = x.e.Args.Select(a => new
                                        {
                                            key = a.Key,
                                            valueType = a.ValueType,
                                            values = a.Values.Take(3),
                                        }),
                                    }
                            ),
                    ];
                object? libraryEntry = null;
                if (
                    libraryDefinitions is not null
                    && libraryDefinitions.TryGetValue(m.Kind + "/" + n, out var definition)
                )
                    libraryEntry = new
                    {
                        count = definition.GetProperty("count").GetInt32(),
                        files = definition.GetProperty("files").GetInt32(),
                        valueTypes = definition.GetProperty("parameters"),
                        locations = definition
                            .GetProperty("locations")
                            .EnumerateArray()
                            .Take(MaxExemplars)
                            .Select(l => Location(l, exe))
                            .ToArray(),
                    };
                return new
                {
                    kind = m.Kind,
                    name = n,
                    commands = node.Elements("command").Select(c => c.Value).ToArray(),
                    expression = node.Element("expression")?.Value,
                    parameters,
                    exemplars,
                    library = libraryDefinitions is null
                        ? null
                        : libraryEntry
                            ?? (object)
                                new
                                {
                                    count = 0,
                                    files = 0,
                                    note = "No TR v12 install scenario contains this element.",
                                },
                };
            })
            .ToArray();

        return new
        {
            total = matches.Length,
            offset,
            limit,
            nextOffset = offset + page.Length < matches.Length
                ? (int?)(offset + page.Length)
                : null,
            entries = page,
            exemplarSource = export is null
                ? null
                : new { sha256 = export.Sha256, triggers = export.Triggers.Length },
            librarySource = library is not { } source
                ? null
                : new
                {
                    file = TriggerExemplars.ExemplarsName,
                    fresh = TriggerExemplars.IsFresh(source, exe, hash),
                    root = source.GetProperty("root").GetString(),
                    scenarios = source.GetProperty("files").GetInt32(),
                    trV12 = source.GetProperty("withTriggers").GetInt32(),
                    unprovenVartypes = source
                        .GetProperty("vartypes")
                        .GetProperty("unprovenVartypes"),
                    note = "Locations are relative to the install game/ folder and carry the SHA-256 seen at generation; fresh=false means rerun editor_generate_catalog. TR v11 scenarios are not indexed.",
                },
            usage = "To add an element of a type that already exists in the export, duplicate it with editor_trigger_edit duplicates (same trigger) or copyEffects (another trigger), then replace its editable parameters. "
                + "For a type absent from the export, pass library:true and copy a library location with copyEffects sourcePath/sourceSha256 (effects only; conditions cannot be copied), then set values with copyValues where encodingProven. Elements are never built from scratch.",
            limitation = "Static shipped definitions (trigger_data.xml); parameter defaults are not what a given export stores, and vartype names are not the numeric valueType in a TR file.",
        };
    }
}
