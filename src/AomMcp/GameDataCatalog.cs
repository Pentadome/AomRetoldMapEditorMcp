using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;

namespace AomMcp;

/// <summary>Generates and caches build-tagged, read-only catalogs from shipped gameplay/map XML.</summary>
internal sealed class GameDataCatalog
{
    // Host artifact/tool conventions; proprietary decoded definitions remain under ignored generated/data.
    const string FileName = "game_catalog.json";
    internal static readonly Dictionary<string, string> ToolKinds = new(StringComparer.Ordinal)
    {
        ["editor_prototypes"] = "prototypes",
        ["editor_gods"] = "gods",
        ["editor_technologies"] = "technologies",
        ["editor_god_powers"] = "godPowers",
        ["editor_terrain_types"] = "terrainTypes",
        ["editor_water_types"] = "waterTypes",
    };
    // Source fields searched by the host, not a game query-language vocabulary.
    static readonly string[] SearchFields = ["name", "label", "displayNameId"];
    readonly JsonElement _data;
    // Lazy source XML cache shared by dependency calls; generator freshness still checked each call.
    Dictionary<string, XElement>? _protoXml, _techXml;

    /// <summary>Loads generated metadata without connecting to the game.</summary>
    /// <exception cref="FileNotFoundException">Gameplay metadata has not been generated.</exception>
    public GameDataCatalog()
    {
        string? file = null;
        // Prefer repository metadata so --generate takes effect after host restart, without another build.
        for (DirectoryInfo? d = new(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "generated", FileName);
            if (File.Exists(candidate))
            {
                file = candidate;
                break;
            }
        }
        file ??= Path.Combine(AppContext.BaseDirectory, FileName); // Standalone packaged fallback.
        if (!File.Exists(file))
            throw new FileNotFoundException("Game catalog metadata missing. Run --generate generated with CryBar installed.");
        using var document = JsonDocument.Parse(File.ReadAllText(file));
        _data = document.RootElement.Clone();
    }

    // Installed archive path, shared by generator and lookup freshness checks.
    internal static string Archive(string exe) =>
        Path.Combine(Path.GetDirectoryName(exe)!, "game", "data", "Data.bar");

    void CheckFresh(string exe, string hash)
    {
        var archive = new FileInfo(Archive(exe));
        var image = new FileInfo(exe);
        // Executable identity is the SHA-256 the host computed from the live file at startup; launcher/Steam
        // validation can touch its timestamp without changing bytes, so exe write time is not compared.
        // Archive size/UTC modification time detect ordinary data updates, not adversarial stamp-preserving changes.
        if (_data.GetProperty("exeSha256").GetString() != hash
            || !image.Exists || image.Length != _data.GetProperty("exeLength").GetInt64()
            || !archive.Exists || archive.Length != _data.GetProperty("archiveLength").GetInt64()
            || archive.LastWriteTimeUtc != _data.GetProperty("archiveWriteTimeUtc").GetDateTime())
            throw new WorkflowFailure("METADATA_STALE", "metadata", "Game catalog metadata stale (executable hash/length or Data.bar size/time changed).", false, false,
                "Run --generate generated and restart MCP. Metadata read only; no game connection or native command occurred.");
    }

    string Culture(string name)
    {
        var groups = _data.GetProperty("pantheons");
        // Culture spellings come from major_gods.xml; trailing s accepts Greeks/Egyptians/Atlanteans/Aztecs.
        var match = groups.EnumerateObject().FirstOrDefault(p =>
            p.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)
            || (p.Name + "s").Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (match.Value.ValueKind == JsonValueKind.Undefined)
            throw new ArgumentException("Unknown pantheon. Available: "
                + string.Join(", ", groups.EnumerateObject().Select(p => p.Name)));
        return match.Name;
    }

    Dictionary<string, (string Name, float? X, float? Z)>? _footprints;

    /// <summary>Exact prototype names plus source obstruction radii; refuses stale metadata.</summary>
    internal Dictionary<string, (string Name, float? X, float? Z)> Footprints(string exe, string hash)
    {
        CheckFresh(exe, hash);
        if (_footprints is not null) return _footprints;
        float? Radius(XElement unit, string tag) =>
            float.TryParse(unit.Element(tag)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var r) && float.IsFinite(r) && r >= 0 ? r : null;
        var result = new Dictionary<string, (string, float?, float?)>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in _data.GetProperty("catalogs").GetProperty("prototypes").EnumerateArray())
        {
            var name = e.GetProperty("name").GetString()!;
            if (result.ContainsKey(name)) continue;
            var xml = e.TryGetProperty("definition", out var d) && d.ValueKind == JsonValueKind.String ? XElement.Parse(d.GetString()!) : null;
            result[name] = (name, xml is null ? null : Radius(xml, "obstructionradiusx"), xml is null ? null : Radius(xml, "obstructionradiusz"));
        }
        return _footprints = result;
    }

    Dictionary<string, string[]>? _flags;

    /// <summary>Shipped proto.xml flag tags per prototype (case-insensitive name); absent name = not a shipped proto.</summary>
    internal Dictionary<string, string[]> Flags(string exe, string hash)
    {
        CheckFresh(exe, hash);
        if (_flags is not null) return _flags;
        var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in _data.GetProperty("catalogs").GetProperty("prototypes").EnumerateArray())
            result.TryAdd(e.GetProperty("name").GetString()!,
                e.TryGetProperty("flags", out var f) && f.ValueKind == JsonValueKind.Array ? f.EnumerateArray().Select(x => x.GetString()!).ToArray() : []);
        return _flags = result;
    }

    Dictionary<string, (string? Resource, double Amount, string[] UnitTypes)>? _resources;

    /// <summary>Static resource subtype/initial amount/unit types per prototype (case-insensitive name).</summary>
    internal Dictionary<string, (string? Resource, double Amount, string[] UnitTypes)> Resources(string exe, string hash)
    {
        CheckFresh(exe, hash);
        if (_resources is not null) return _resources;
        var result = new Dictionary<string, (string?, double, string[])>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in _data.GetProperty("catalogs").GetProperty("prototypes").EnumerateArray())
        {
            var name = e.GetProperty("name").GetString()!;
            if (result.ContainsKey(name)) continue;
            string? resource = e.TryGetProperty("resourceSubtype", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
            double amount = 0;
            if (e.TryGetProperty("initialResources", out var ir) && ir.ValueKind == JsonValueKind.Array)
                foreach (var item in ir.EnumerateArray())
                {
                    resource ??= item.GetProperty("resource").GetString();
                    if (double.TryParse(item.GetProperty("amount").GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var a)) amount += a;
                }
            var types = e.TryGetProperty("unitTypes", out var ut) ? ut.EnumerateArray().Select(t => t.GetString()!).ToArray() : [];
            result[name] = (resource, amount, types);
        }
        return _resources = result;
    }

    /// <summary>Ranks close exact-name candidates by containment then edit distance; metadata only.</summary>
    internal static string[] Suggest(string requested, IEnumerable<string> names, int max = 5)
    {
        var wanted = requested.Trim();
        if (wanted.Length == 0) return [];
        static int Distance(string a, string b)
        {
            a = a.ToLowerInvariant(); b = b.ToLowerInvariant();
            var previous = Enumerable.Range(0, b.Length + 1).ToArray();
            for (var i = 1; i <= a.Length; i++)
            {
                var current = new int[b.Length + 1]; current[0] = i;
                for (var j = 1; j <= b.Length; j++)
                    current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                previous = current;
            }
            return previous[b.Length];
        }
        var limit = Math.Max(3, wanted.Length / 3); // Host fuzzy policy: about one edit per three characters.
        return names.Select(n => (Name: n,
                Rank: n.Equals(wanted, StringComparison.OrdinalIgnoreCase) ? 0
                    : n.StartsWith(wanted, StringComparison.OrdinalIgnoreCase) ? 1
                    : n.Contains(wanted, StringComparison.OrdinalIgnoreCase) || wanted.Contains(n, StringComparison.OrdinalIgnoreCase) && n.Length >= 4 ? 2 : 3,
                Distance: Distance(n, wanted)))
            .Where(c => c.Rank < 3 || c.Distance <= limit)
            .OrderBy(c => c.Rank).ThenBy(c => c.Distance).ThenBy(c => c.Name.Length).ThenBy(c => c.Name, StringComparer.Ordinal)
            .Take(max).Select(c => c.Name).ToArray();
    }

    /// <summary>Returns a culture's potential unit/building union, refusing stale metadata.</summary>
    /// <param name="exe">Configured game executable path.</param>
    /// <param name="hash">Executable hash already validated by the server.</param>
    /// <param name="pantheon">Case-insensitive singular/plural culture name.</param>
    /// <returns>Exact names, major gods, source identity, and unresolved tech references.</returns>
    public object Lookup(string exe, string hash, string pantheon)
    {
        CheckFresh(exe, hash);
        var culture = Culture(pantheon);
        var match = _data.GetProperty("pantheons").GetProperty(culture);
        return new
        {
            pantheon = culture,
            units = match.GetProperty("units"),
            buildings = match.GetProperty("buildings"),
            majorGods = match.GetProperty("majorGods"),
            unresolvedTechs = match.GetProperty("unresolvedTechs"),
            buildHash = hash,
            source = _data.GetProperty("source"),
            limitation = "Potential union across major/minor gods and ages; prerequisites, exclusions and scenario overrides are not evaluated. Not a current-player trainability query; decorations/art-only associations excluded.",
        };
    }

    // Host schema policy: bounded pages keep prototype/terrain catalogs out of giant tool responses.
    internal static Dictionary<string, object> Properties(string kind)
    {
        var properties = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["filter"] = new { type = "string", description = "Case-insensitive substring of name or label/string ID." },
            ["name"] = new { type = "string", description = "Exact source identifier (case-insensitive)." },
            ["offset"] = new { type = "integer", minimum = 0 },
            ["limit"] = new { type = "integer", minimum = 1, maximum = 200 },
            ["includeDefinition"] = new { type = "boolean", description = "Include original XML definition; default false." },
        };
        if (kind is "prototypes" or "gods" or "technologies" or "godPowers")
            properties["pantheon"] = new { type = "string", description = "Optional culture association (not current-player availability)." };
        if (kind == "prototypes")
        {
            properties["category"] = new { type = "string", @enum = new[] { "all", "units", "buildings", "trees", "resources", "objects" } };
            properties["unitType"] = new { type = "string", description = "Exact unittype tag, e.g. GoldResource, FishResource, Herdable, Projectile." };
        }
        if (kind == "gods")
            properties["category"] = new { type = "string", @enum = new[] { "all", "major", "minor" } };
        if (kind == "waterTypes")
            properties["category"] = new { type = "string", @enum = new[] { "all", "lake", "river", "ocean" } };
        if (kind == "terrainTypes")
            properties["terrainType"] = new { type = "string", description = "Exact parent type, e.g. PassableLand; not a runtime numeric ID." };
        return properties;
    }

    /// <summary>Returns a bounded, filtered page of source definitions without opening a game connection.</summary>
    /// <param name="exe">Configured game executable path.</param>
    /// <param name="hash">Accepted executable hash.</param>
    /// <param name="kind">Catalog kind from the host tool map.</param>
    /// <param name="args">Schema-validated filters, pagination and optional XML detail flag.</param>
    /// <returns>Stable source-order page, filtered total, next offset, source identity and limits.</returns>
    public object Query(string exe, string hash, string kind, JsonElement args)
    {
        CheckFresh(exe, hash);
        string Text(string key) => args.TryGetProperty(key, out var v) ? v.GetString()! : "";
        var offset = args.TryGetProperty("offset", out var o) ? o.GetInt32() : 0;
        var limit = args.TryGetProperty("limit", out var l) ? l.GetInt32() : 50; // Host default/cap, not game constants.
        if (offset < 0 || limit is < 1 or > 200)
            throw new ArgumentException("Catalog offset must be >= 0; limit must be 1..200.");
        var definition = args.TryGetProperty("includeDefinition", out var d) && d.GetBoolean();
        var culture = args.TryGetProperty("pantheon", out var p) ? Culture(p.GetString()!) : "";
        bool Equal(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase);
        bool Has(JsonElement e, string key, string value) => e.GetProperty(key).EnumerateArray()
            .Any(v => Equal(v.GetString()!, value));
        var filter = Text("filter");
        var name = Text("name");
        var category = Text("category");
        var unitType = Text("unitType");
        var terrainType = Text("terrainType");
        var matches = _data.GetProperty("catalogs").GetProperty(kind).EnumerateArray().Where(e =>
            (name.Length == 0 || Equal(e.GetProperty("name").GetString()!, name))
            && (filter.Length == 0 || SearchFields.Any(k =>
                e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String
                && v.GetString()!.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            && (culture.Length == 0 || Has(e, "pantheons", culture))
            && (category.Length == 0 || category == "all" || Has(e, "categories", category))
            && (unitType.Length == 0 || Has(e, "unitTypes", unitType))
            && (terrainType.Length == 0 || Equal(e.GetProperty("terrainType").GetString()!, terrainType)))
            .ToArray();
        var entries = matches.Skip(offset).Take(limit).Select(e => e.EnumerateObject()
            .Where(v => definition || v.Name != "definition").ToDictionary(v => v.Name, v => v.Value)).ToArray();
        return new
        {
            catalog = kind,
            total = matches.Length,
            offset,
            limit,
            nextOffset = offset < matches.Length && entries.Length < matches.Length - offset
                ? (int?)(offset + entries.Length) : null,
            entries,
            buildHash = hash,
            archive = "game/data/Data.bar",
            limitation = "Static base definitions, not runtime IDs, translated labels, current-player trainability or live placement proof. Resource amounts/stats can change through techs, scripts or game modes. Pantheon links are potential associations, not exclusive availability. XML detail preserves source fields; game interprets defaults/inheritance.",
        };
    }

    /// <summary>Explains direct train/build/enable/create/replacement links and shortest potential god-to-tech paths.</summary>
    /// <param name="exe">Configured game image for source freshness checks.</param>
    /// <param name="hash">Accepted executable hash.</param>
    /// <param name="args">Exact prototype name and bounded relation paging.</param>
    /// <returns>Source-backed relations, raw prerequisites and explicit non-trainability limitations.</returns>
    public object Dependencies(string exe, string hash, JsonElement args)
    {
        CheckFresh(exe, hash);
        var catalogs = _data.GetProperty("catalogs");
        Dictionary<string, XElement> Definitions(string kind) => catalogs.GetProperty(kind).EnumerateArray()
            .ToDictionary(e => e.GetProperty("name").GetString()!, e => XElement.Parse(e.GetProperty("definition").GetString()!), StringComparer.OrdinalIgnoreCase);
        _protoXml ??= Definitions("prototypes");
        _techXml ??= Definitions("technologies");
        var requested = args.GetProperty("proto").GetString()!;
        if (!_protoXml.TryGetValue(requested, out var prototype)) throw new ArgumentException("Unknown prototype: " + requested);
        var name = prototype.Attribute("name")!.Value;
        // Abstract ProtoUnit targets use shipped unittype tags; never substitute artwork associations.
        var targets = prototype.Elements("unittype").Select(t => t.Value.Trim()).Append(name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var relations = new List<object>();
        foreach (var (owner, definition) in _protoXml)
            foreach (var link in definition.Elements().Where(e => e.Name.LocalName is "train" or "build"))
                if (targets.Contains(link.Value.Trim()))
                    relations.Add(new { kind = "prototype", name = owner, relationship = link.Name.LocalName,
                        target = link.Value.Trim(), source = "gameplay/proto.xml.XMB", evidence = link.ToString(SaveOptions.DisableFormatting) });
        var direct = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (tech, definition) in _techXml)
        {
            var matches = definition.Elements("effects").Elements("effect").Where(e =>
                ((string?)e.Attribute("type") == "Data" && (string?)e.Attribute("subtype") == "Enable"
                    && double.TryParse((string?)e.Attribute("amount"), NumberStyles.Float, CultureInfo.InvariantCulture, out var amount) && amount > 0
                    && e.Elements("target").Any(t => (string?)t.Attribute("type") == "ProtoUnit" && targets.Contains(t.Value.Trim())))
                || ((string?)e.Attribute("type") == "CreateUnit" && targets.Contains((string?)e.Attribute("unit") ?? ""))
                || ((string?)e.Attribute("subtype") == "ModifyReplacement" && targets.Contains((string?)e.Attribute("proto") ?? "")))
                .Select(e => e.ToString(SaveOptions.DisableFormatting)).ToArray();
            if (matches.Length == 0) continue;
            direct.Add(tech);
            relations.Add(new { kind = "technology", name = tech, relationship = "enable/create/replacement",
                source = "gameplay/techtree.xml.XMB", effects = matches,
                prerequisites = definition.Elements("prereqs").Select(e => e.ToString(SaveOptions.DisableFormatting)).ToArray() });
        }
        var links = _techXml.ToDictionary(p => p.Key, p => p.Value.Elements("effects").Elements("effect")
            .Where(e => (string?)e.Attribute("type") == "TechStatus" && (string?)e.Attribute("status") is "active" or "obtainable")
            .Select(e => e.Value.Trim()).ToArray(), StringComparer.OrdinalIgnoreCase);
        foreach (var god in catalogs.GetProperty("gods").EnumerateArray())
        {
            var godName = god.GetProperty("name").GetString()!;
            foreach (var start in god.GetProperty("startingUnits").EnumerateArray())
                if (XElement.Parse(start.GetString()!).Elements("unit").Any(u => targets.Contains(u.Value.Trim())))
                    relations.Add(new { kind = "god", name = godName, relationship = "startingUnit", source = god.GetProperty("source"), evidence = start });
            var pending = new Queue<string[]>();
            foreach (var root in god.GetProperty("ageTechs").EnumerateArray()) pending.Enqueue([root.GetString()!]);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (pending.TryDequeue(out var path))
            {
                var current = path[^1];
                if (!seen.Add(current)) continue; // Finite graph; cycles/shared techs processed once per god.
                if (direct.Contains(current))
                    relations.Add(new { kind = "god", name = godName, relationship = "potentialTechPath", tech = current,
                        path, source = god.GetProperty("source"), limitation = "Active/obtainable source path; mutually exclusive choices and prerequisites not evaluated." });
                if (links.TryGetValue(current, out var children))
                    foreach (var child in children) if (!seen.Contains(child)) pending.Enqueue([.. path, child]);
            }
        }
        var offset = args.TryGetProperty("offset", out var o) ? o.GetInt32() : 0;
        var limit = args.TryGetProperty("limit", out var l) ? l.GetInt32() : 50; // Same host metadata default/cap as Query.
        var entries = relations.Skip(offset).Take(limit).ToArray();
        return new { proto = name, total = relations.Count, offset, limit,
            nextOffset = offset < relations.Count && entries.Length < relations.Count - offset ? (int?)(offset + entries.Length) : null,
            entries, buildHash = hash,
            limitation = "Static source links, not current-player trainability, evaluated prerequisites/exclusions, live stats or placement permission. Potential god paths include age choices, not promises. Script/godpower spawns, inheritance/defaults and scenario overrides not comprehensively evaluated. Exact train/build tags, positive Enable effects and CreateUnit/replacement destinations shown." };
    }

    /// <summary>Writes associations and searchable prototypes/gods/techs/powers/terrain/water catalogs.</summary>
    /// <param name="exe">Installed executable used to tag the catalog.</param>
    /// <param name="output">Operator-selected generator output directory.</param>
    /// <param name="data">Directory containing decoded gameplay/map XML and godpower files.</param>
    /// <param name="powerFiles">Decoded godpower basenames listed in the current archive, excluding stale exports.</param>
    public static void Generate(string exe, string output, string data, string[] powerFiles)
    {
        XElement Root(string file) => XDocument.Load(Path.Combine(data, file)).Root!;
        static bool PowerGrant(XElement effect) => (string?)effect.Attribute("type") == "Data"
            && (string?)effect.Attribute("subtype") == "GodPower"
            && double.Parse(effect.Attribute("amount")!.Value, CultureInfo.InvariantCulture) > 0;
        var protos = Root("proto.xml").Elements("unit")
            .ToDictionary(p => p.Attribute("name")!.Value, StringComparer.Ordinal);
        var techs = Root("techtree.xml").Elements("tech")
            .ToDictionary(t => t.Attribute("name")!.Value, StringComparer.Ordinal);
        var gods = Root("major_gods.xml").Elements("civ")
            .Where(c => c.Element("culture") is not null).GroupBy(c => c.Element("culture")!.Value).ToArray();
        var groups = new SortedDictionary<string, object>(StringComparer.Ordinal);
        var cultureProtos = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var cultureTechs = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var culturePowers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var culture in gods)
        {
            var names = culture.SelectMany(c => c.Elements("startingunits").Elements("unit"))
                .Select(u => u.Value.Trim()).ToHashSet(StringComparer.Ordinal);
            names.UnionWith(protos.Where(p => p.Value.Element("culture")?.Value == culture.Key).Select(p => p.Key));
            var pending = new Queue<string>(culture.SelectMany(c => c.Elements("agetech").Elements("tech"))
                .Select(t => t.Value.Trim()));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var powers = new HashSet<string>(StringComparer.Ordinal);
            var missing = new SortedSet<string>(StringComparer.Ordinal);
            while (pending.TryDequeue(out var name))
            {
                if (!seen.Add(name))
                    continue; // Shared/cyclic tech links processed once.
                if (!techs.TryGetValue(name, out var tech))
                {
                    missing.Add(name);
                    continue;
                }
                foreach (var effect in tech.Elements("effects").Elements("effect"))
                {
                    var type = (string?)effect.Attribute("type");
                    var subtype = (string?)effect.Attribute("subtype");
                    if (type == "TechStatus" && (string?)effect.Attribute("status") is "active" or "obtainable")
                        pending.Enqueue(effect.Value.Trim());
                    if (type == "Data" && subtype == "Enable"
                        && double.Parse(effect.Attribute("amount")!.Value, CultureInfo.InvariantCulture) > 0)
                        names.UnionWith(effect.Elements("target")
                            .Where(t => (string?)t.Attribute("type") == "ProtoUnit").Select(t => t.Value.Trim()));
                    if (type == "CreateUnit" && effect.Attribute("unit") is { } unit)
                        names.Add(unit.Value);
                    if (subtype == "ModifyReplacement" && effect.Attribute("proto") is { } replacement)
                        names.Add(replacement.Value);
                    if (PowerGrant(effect) && effect.Attribute("power") is { } power)
                        powers.Add(power.Value);
                }
            }
            bool HasType(string name, string kind) => protos.TryGetValue(name, out var proto)
                && proto.Elements("unittype").Any(t => t.Value == kind || t.Value == kind + "Class");
            groups[culture.Key] = new
            {
                majorGods = culture.Select(c => c.Element("name")!.Value).Order(StringComparer.Ordinal).ToArray(),
                units = names.Where(n => HasType(n, "Unit") && !HasType(n, "Building"))
                    .Order(StringComparer.Ordinal).ToArray(),
                buildings = names.Where(n => HasType(n, "Building")).Order(StringComparer.Ordinal).ToArray(),
                unresolvedTechs = missing.ToArray(),
            };
            cultureProtos[culture.Key] = names;
            cultureTechs[culture.Key] = seen;
            culturePowers[culture.Key] = powers;
        }

        // XML tags/attributes below are shipped definitions, not inferred runtime fields or IDs.
        static string[] Values(XElement e, string tag) => e.Elements(tag).Select(v => v.Value.Trim()).ToArray();
        static object[] Resources(XElement e, string tag) => e.Elements(tag).Select(v => (object)new
        {
            resource = (string?)v.Attribute("resourcetype"), amount = v.Value.Trim(),
        }).ToArray();
        static string[] Xml(XElement e, string tag) => e.Elements(tag)
            .Select(v => v.ToString(SaveOptions.DisableFormatting)).ToArray();
        string[] Associations(Dictionary<string, HashSet<string>> associations, string name) => associations
            .Where(p => p.Value.Contains(name)).Select(p => p.Key).Order(StringComparer.Ordinal).ToArray();
        static Dictionary<string, object?> Entry(string name, XElement e, string source) => new(StringComparer.Ordinal)
        {
            ["name"] = name,
            ["source"] = source,
            ["displayNameId"] = e.Element("displaynameid")?.Value,
            ["definition"] = e.ToString(SaveOptions.DisableFormatting),
        };
        var catalogs = new Dictionary<string, List<Dictionary<string, object?>>>(StringComparer.Ordinal);
        foreach (var kind in ToolKinds.Values)
            catalogs[kind] = [];
        foreach (var (name, proto) in protos.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var entry = Entry(name, proto, "gameplay/proto.xml.XMB");
            var types = Values(proto, "unittype");
            bool Has(string type) => types.Contains(type, StringComparer.Ordinal);
            var categories = new List<string>();
            if (Has("Building") || Has("BuildingClass")) categories.Add("buildings");
            else if (Has("Unit") || Has("UnitClass")) categories.Add("units");
            else categories.Add("objects");
            if (Has("Tree")) categories.Add("trees");
            if (Has("Resource") || proto.Element("initialresource") is not null) categories.Add("resources");
            entry["categories"] = categories;
            entry["unitTypes"] = types;
            entry["pantheons"] = Associations(cultureProtos, name);
            entry["explicitCulture"] = proto.Element("culture")?.Value;
            entry["flags"] = Values(proto, "flag");
            entry["costs"] = Resources(proto, "cost");
            entry["initialResources"] = Resources(proto, "initialresource");
            entry["resourceSubtype"] = proto.Element("resourcesubtype")?.Value;
            entry["stats"] = proto.Elements().Where(e => e.Name.LocalName is "maxhitpoints" or "maxvelocity"
                or "los" or "populationcount" or "buildpoints" or "trainpoints" or "buildlimit")
                .GroupBy(e => e.Name.LocalName).ToDictionary(g => g.Key, g => g.Select(e => e.Value).ToArray());
            entry["train"] = Xml(proto, "train");
            entry["build"] = Xml(proto, "build");
            catalogs["prototypes"].Add(entry);
        }
        foreach (var (name, tech) in techs.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            var entry = Entry(name, tech, "gameplay/techtree.xml.XMB");
            entry["pantheons"] = Associations(cultureTechs, name);
            entry["status"] = tech.Element("status")?.Value;
            entry["costs"] = Resources(tech, "cost");
            entry["researchPoints"] = tech.Element("researchpoints")?.Value;
            entry["prerequisites"] = Xml(tech, "prereqs");
            entry["effects"] = Xml(tech, "effects");
            catalogs["technologies"].Add(entry);
        }
        var techKeys = techs.Keys.ToDictionary(k => k, StringComparer.OrdinalIgnoreCase);
        void God(string name, XElement definition, string category, string[] roots, string[] cultures, string source)
        {
            var entry = Entry(name, definition, source);
            entry["categories"] = new[] { category };
            entry["pantheons"] = cultures;
            entry["ageTechs"] = roots;
            entry["unresolvedTechs"] = roots.Where(r => !techs.ContainsKey(r)).ToArray();
            entry["godPowers"] = roots.Where(techs.ContainsKey).SelectMany(r => techs[r]
                .Elements("effects").Elements("effect").Where(PowerGrant))
                .Select(e => e.Attribute("power")!.Value).Distinct(StringComparer.Ordinal).ToArray();
            entry["displayNameId"] ??= roots.Where(techs.ContainsKey)
                .Select(r => techs[r].Element("displaynameid")?.Value).FirstOrDefault();
            entry["startingUnits"] = Xml(definition, "startingunits");
            entry["directTechEffects"] = roots.Where(techs.ContainsKey).SelectMany(r => Xml(techs[r], "effects")).ToArray();
            catalogs["gods"].Add(entry);
        }
        foreach (var culture in gods)
            foreach (var god in culture)
                God(god.Element("name")!.Value, god, "major", god.Elements("agetech").Elements("tech")
                    .Select(t => t.Value.Trim()).ToArray(), [culture.Key], "gameplay/major_gods.xml.XMB");
        foreach (var god in Root("minor_gods.xml").Elements())
        {
            // minor_gods uses lowercase age-tech element keys; map to canonical techtree name, not art path.
            var key = god.Name.LocalName;
            var name = techKeys.GetValueOrDefault(key, key);
            God(name, god, "minor", [name], Associations(cultureTechs, name), "gameplay/minor_gods.xml.XMB");
        }
        catalogs["gods"] = catalogs["gods"].OrderBy(e => (string)e["name"]!, StringComparer.Ordinal).ToList();
        foreach (var file in powerFiles.Order(StringComparer.Ordinal))
            foreach (var power in Root(file).Elements("power"))
            {
                var name = power.Attribute("name")!.Value;
                var entry = Entry(name, power, "gameplay/god_powers/" + Path.GetFileName(file) + ".XMB");
                entry["pantheons"] = Associations(culturePowers, name);
                // ponytail: generation-only tech scan per power; index grants if large mods make this slow.
                var grants = techs.Where(t => t.Value.Elements("effects").Elements("effect")
                    .Any(e => PowerGrant(e) && (string?)e.Attribute("power") == name))
                    .Select(t => t.Key).Order(StringComparer.Ordinal).ToArray();
                entry["grantedByTechs"] = grants;
                entry["grantedByGods"] = catalogs["gods"].Where(g => ((string[])g["ageTechs"]!)
                    .Intersect(grants, StringComparer.Ordinal).Any()).Select(g => g["name"]).ToArray();
                entry["powerType"] = (string?)power.Attribute("type");
                entry["cost"] = power.Element("cost")?.Value;
                entry["repeatCost"] = power.Element("repeatcost")?.Value;
                entry["activeTime"] = power.Element("activetime")?.Value;
                entry["placement"] = Xml(power, "placement");
                entry["createdUnits"] = Values(power, "createunit");
                catalogs["godPowers"].Add(entry); // Same-name variants retained with their source file.
            }
        foreach (var type in Root("terrain_types.xml").Elements("type"))
            foreach (var ui in type.Elements("uiclass"))
                foreach (var texture in ui.Elements("subtype"))
                {
                    var entry = Entry(texture.Value.Trim(), texture, "map_definitions/terrain_types.xml.XMB");
                    entry["label"] = (string?)texture.Attribute("uiname");
                    entry["terrainType"] = (string?)type.Attribute("name");
                    entry["terrainLabel"] = (string?)type.Attribute("uiname");
                    entry["uiClass"] = (string?)ui.Attribute("uiname");
                    entry["settings"] = texture.Attributes().ToDictionary(a => a.Name.LocalName, a => a.Value);
                    entry["terrainSettings"] = type.Attributes().ToDictionary(a => a.Name.LocalName, a => a.Value);
                    catalogs["terrainTypes"].Add(entry); // Preserve type/UI context for shared texture identifiers.
                }
        foreach (var water in Root("water_bodies.xml").Elements())
        {
            var entry = Entry(water.Attribute("name")!.Value, water, "map_definitions/water_bodies.xml.XMB");
            entry["categories"] = new[] { water.Name.LocalName };
            entry["settings"] = water.Attributes().ToDictionary(a => a.Name.LocalName, a => a.Value);
            catalogs["waterTypes"].Add(entry);
        }
        var archive = new FileInfo(Archive(exe));
        var image = new FileInfo(exe);
        File.WriteAllText(Path.Combine(output, FileName), JsonSerializer.Serialize(new
        {
            exeSha256 = Layout.Hash(exe),
            exeLength = image.Length,
            exeWriteTimeUtc = image.LastWriteTimeUtc,
            archiveLength = archive.Length,
            archiveWriteTimeUtc = archive.LastWriteTimeUtc,
            source = "game/data/Data.bar: gameplay/major_gods.xml.XMB, techtree.xml.XMB, proto.xml.XMB; starting units, active/obtainable tech links, positive Enable/CreateUnit/ModifyReplacement effects, explicit culture tags and Unit/Building classes.",
            pantheons = groups,
            catalogs,
        }, Layout.Json));
    }
}
