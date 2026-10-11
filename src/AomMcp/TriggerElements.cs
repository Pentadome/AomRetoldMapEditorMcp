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
    internal const int MaxLimit = 50, MaxExemplars = 5;
    internal static readonly string[] Kinds = ["any", "effect", "condition"];
    static readonly string[] Allowed = ["kind", "name", "filter", "path", "expectedSha256", "offset", "limit"];
    static readonly string[] Replaceable = ["Player", "PlayerID", "FromPlayerID", "ToPlayerID", "EventID", "TechID", "Count", "Dist", "Status", "Value", "Duration",
        "ProtoUnit", "UnitType", "Command", "QVName", "Op", "CodeSnippet"];

    internal static void Preflight(JsonElement args)
    {
        Catalog.ValidateObject(args, Allowed);
        if (args.TryGetProperty("kind", out var kind) && !Kinds.Contains(kind.GetString()))
            throw new ArgumentException("kind must be any, effect or condition.");
        if (args.TryGetProperty("path", out _) != args.TryGetProperty("expectedSha256", out _))
            throw new ArgumentException("path and expectedSha256 go together (hash-pinned TR export).");
    }

    static string DataFile() => Path.Combine(Generator.DefaultOutput(), "data", "trigger_data.xml");

    internal static object Query(JsonElement args)
    {
        Preflight(args);
        string Text(string key) => args.TryGetProperty(key, out var v) ? v.GetString()! : "";
        var file = DataFile();
        if (!File.Exists(file))
            throw new WorkflowFailure("METADATA_MISSING", "preflight", "Trigger element catalog (generated/data/trigger_data.xml) is missing.", false, false,
                "Run editor_generate_catalog (no game connection), then retry.");
        var root = XDocument.Load(file).Root!;
        var kind = Text("kind") is { Length: > 0 } k ? k : "any";
        var name = Text("name"); var filter = Text("filter");
        var offset = args.TryGetProperty("offset", out var o) ? o.GetInt32() : 0;
        var limit = args.TryGetProperty("limit", out var l) ? l.GetInt32() : 10;
        if (limit is < 1 or > MaxLimit || offset < 0) throw new ArgumentException($"limit must be 1..{MaxLimit} and offset >= 0.");

        var entries = new List<(string Kind, XElement Node)>();
        foreach (var (section, tag, label) in new[] { ("conditions", "condition", "condition"), ("effects", "effect", "effect") })
            if (kind is "any" || kind == label)
                entries.AddRange((root.Element(section)?.Elements(tag) ?? []).Select(e => (label, e)));
        var matches = entries.Where(e =>
        {
            var n = e.Node.Attribute("name")?.Value ?? "";
            return (name.Length == 0 || n.Equals(name, StringComparison.OrdinalIgnoreCase))
                && (filter.Length == 0 || n.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }).ToArray();

        CampaignTriggers.Document? export = null;
        if (args.TryGetProperty("path", out var path))
        {
            export = CampaignTriggers.Read(path.GetString()!);
            if (!export.Sha256.Equals(args.GetProperty("expectedSha256").GetString(), StringComparison.OrdinalIgnoreCase))
                throw new WorkflowFailure("SOURCE_CHANGED", "preflight", "TR export SHA-256 differs from expectedSha256.", false, false,
                    "Inspect the export again; nothing was read beyond the file.");
        }

        var page = matches.Skip(offset).Take(limit).Select(m =>
        {
            var node = m.Node; var n = node.Attribute("name")!.Value;
            var parameters = node.Elements("param").Select(p =>
            {
                var key = p.Attribute("name")?.Value ?? "";
                return new
                {
                    name = key, label = p.Attribute("dispname")?.Value, vartype = p.Attribute("vartype")?.Value, @default = p.Value,
                    editableByTriggerEdit = Replaceable.Contains(key, StringComparer.OrdinalIgnoreCase),
                };
            }).ToArray();
            object[]? exemplars = null;
            if (export is not null)
                exemplars = [.. export.Triggers.SelectMany(t => (m.Kind == "effect" ? t.Effects : t.Conditions)
                    .Select((e, index) => (t, e, index)).Where(x => x.e.Name.Equals(n, StringComparison.Ordinal)))
                    .Take(MaxExemplars).Select(x => (object)new
                    {
                        triggerId = x.t.Id, triggerName = x.t.Name, elementIndex = x.index,
                        args = x.e.Args.Select(a => new { key = a.Key, valueType = a.ValueType, values = a.Values.Take(3) }),
                    })];
            return new
            {
                kind = m.Kind, name = n,
                commands = node.Elements("command").Select(c => c.Value).ToArray(),
                expression = node.Element("expression")?.Value,
                parameters, exemplars,
            };
        }).ToArray();

        return new
        {
            total = matches.Length, offset, limit,
            nextOffset = offset + page.Length < matches.Length ? (int?)(offset + page.Length) : null,
            entries = page,
            exemplarSource = export is null ? null : new { sha256 = export.Sha256, triggers = export.Triggers.Length },
            usage = "To add an element of a type that already exists in the export, duplicate it with editor_trigger_edit duplicates (same trigger) or copyEffects (another trigger), then replace its editable parameters. "
                + "Building elements of types absent from the export is not supported: the binary layout per vartype is unproven.",
            limitation = "Static shipped definitions (trigger_data.xml); parameter defaults are not what a given export stores, and vartype names are not the numeric valueType in a TR file.",
        };
    }
}
