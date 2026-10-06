using System.Text.Json;

namespace AomMcp;

/// <summary>Partial semantic comparisons plus complete ordered raw-section evidence. No scenario writes.</summary>
internal static class ScenarioDiff
{
    internal sealed record Change(string Scope, string Key, string Field, JsonElement Before, JsonElement After);
    internal sealed record Coverage(string Scope, string Status, string Detail);
    static readonly Dictionary<string, string[]> Fields = new(StringComparer.Ordinal)
    {
        ["players"] = ["name", "aiPath", "diplomacy", "control", "civId", "colorId", "startAge", "maxAge", "classicalGodId", "heroicGodId", "mythicGodId", "pop", "popLimit", "food", "wood", "gold", "favor", "visibility", "handicap", "presence"],
        ["triggers"] = ["name", "group", "priority", "note", "flags", "conditions", "effects", "presence"],
        ["groups"] = ["name", "members", "presence"],
        ["entities"] = ["player", "proto", "prototypeCandidates", "x", "y", "z", "note", "presence"],
        ["raw"] = ["value", "presence"],
    };
    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    static JsonElement Value(object? o) => JsonSerializer.SerializeToElement(o, Json);
    static object Semantic(CampaignTriggers.Element e) => new { e.Name, e.Kind, e.Command,
        args = e.Args.Select(a => new { a.Key, a.Label, a.ValueType, a.Values, a.Objects, a.SelectionFlag, a.Trailer, a.ValueFlag, a.Magic }).ToArray(), e.Extras, e.Trailer };
    static Dictionary<string, JsonElement> Entities(CheckpointUnits.Snapshot s) => s.Units.ToDictionary(u => u.UnitId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        u => Value(new { u.Player, u.Proto, u.PrototypeCandidates, u.X, u.Y, u.Z, u.Note }), StringComparer.Ordinal);
    static Dictionary<string, JsonElement> Players(ScenarioReader.Player[] players) => players.ToDictionary(p => p.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        p => Value(new { p.Name, p.AiPath, p.Diplomacy, p.Control, p.CivId, p.ColorId, p.StartAge, p.MaxAge, p.ClassicalGodId, p.HeroicGodId, p.MythicGodId,
            p.Pop, p.PopLimit, p.Food, p.Wood, p.Gold, p.Favor, p.Visibility, p.Handicap }), StringComparer.Ordinal);
    static Dictionary<string, JsonElement> Triggers(CampaignTriggers.Document d) => d.Triggers.ToDictionary(t => t.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        t => Value(new { t.Name, t.Group, t.Priority, t.Note, t.Flags, conditions = t.Conditions.Select(Semantic).ToArray(), effects = t.Effects.Select(Semantic).ToArray() }), StringComparer.Ordinal);
    static Dictionary<string, JsonElement> Groups(CampaignTriggers.Document d)
    {
        if (d.Groups.Select(g => g.Id).Distinct().Count() != d.Groups.Length) throw new InvalidDataException("Duplicate group IDs.");
        return d.Groups.ToDictionary(g => g.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), g => Value(new { g.Name, members = g.Indexes }), StringComparer.Ordinal);
    }
    static Dictionary<string, JsonElement> Raw(CheckpointDocument d)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["$stored"] = Value(new { sha256 = d.Sha256 }), ["$decoded"] = Value(new { sha256 = CheckpointDocument.Hash(d.Decoded.Span) }),
            ["/BG/header"] = Value(new { sha256 = CheckpointDocument.Hash(d.Decoded.Span[..10]) }),
            ["/BG/tail"] = Value(new { sha256 = CheckpointDocument.Hash(d.Root.Tail.Span) }),
            ["/BG/J1/header"] = Value(d.WorldHeader), ["/BG/J1/tail"] = Value(new { sha256 = CheckpointDocument.Hash(d.World.Tail.Span) }),
            ["$suffix"] = Value(new { sha256 = CheckpointDocument.Hash(d.StoredSuffix.Span) }),
        };
        foreach (var (prefix, frame) in new[] { ("/BG", d.Root), ("/BG/J1[0]", d.World) })
            for (var i = 0; i < frame.Sections.Length; i++)
            {
                var s = frame.Sections[i]; result[prefix + "/" + s.Tag + "[" + s.Occurrence + "]"] = Value(new { order = i, bytes = s.Data.Length, sha256 = CheckpointDocument.Hash(s.Data.Span) });
            }
        return result;
    }
    static void Compare(string scope, Dictionary<string, JsonElement> before, Dictionary<string, JsonElement> after, List<Change> changes)
    {
        foreach (var key in before.Keys.Union(after.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var left = before.TryGetValue(key, out var a); var right = after.TryGetValue(key, out var b);
            if (!left || !right) { changes.Add(new(scope, key, "presence", left ? a : Value(null), right ? b : Value(null))); continue; }
            if (scope == "raw") { if (!JsonElement.DeepEquals(a, b)) changes.Add(new(scope, key, "value", a, b)); continue; }
            foreach (var p in a.EnumerateObject())
            {
                var other = b.GetProperty(p.Name); if (!JsonElement.DeepEquals(p.Value, other)) changes.Add(new(scope, key, p.Name, p.Value, other));
            }
        }
    }
    internal static void Preflight(JsonElement args)
    {
        Catalog.ValidateObject(args, ["beforePath", "expectedBeforeSha256", "afterPath", "expectedAfterSha256", "assertions", "offset", "limit"]);
        _ = EditorFiles.LocalPath(args.GetProperty("beforePath").GetString()!); _ = EditorFiles.LocalPath(args.GetProperty("afterPath").GetString()!);
        foreach (var name in new[] { "expectedBeforeSha256", "expectedAfterSha256" })
        {
            var sha = args.GetProperty(name).GetString(); if (sha is null || sha.Length != 64 || !sha.All(Uri.IsHexDigit)) throw new ArgumentException("Pinned checkpoint hashes required.");
        }
        if (args.TryGetProperty("assertions", out var list))
        {
            if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() is < 1 or > 200) throw new ArgumentException("assertions requires 1..200 rules.");
            foreach (var a in list.EnumerateArray())
            {
                Catalog.ValidateObject(a, ["scope", "policy", "keys", "fields"]);
                if (!Fields.ContainsKey(a.GetProperty("scope").GetString()!) || a.GetProperty("policy").GetString() is not "preserve" and not "allowChanges")
                    throw new ArgumentException("Unknown assertion scope/policy.");
                foreach (var name in new[] { "keys", "fields" }) if (a.TryGetProperty(name, out var values)
                    && (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() is < 1 or > 200 || values.EnumerateArray().Any(v => string.IsNullOrEmpty(v.GetString()))))
                    throw new ArgumentException("Assertion selectors outside bounds.");
            }
        }
        if (args.TryGetProperty("offset", out var o) && o.GetInt32() < 0 || args.TryGetProperty("limit", out var l) && l.GetInt32() is < 1 or > 200)
            throw new ArgumentException("offset/limit outside bounds.");
    }
    internal static object Query(JsonElement args)
    {
        Preflight(args);
        var before = CheckpointDocument.Read(args.GetProperty("beforePath").GetString()!, args.GetProperty("expectedBeforeSha256").GetString());
        var after = CheckpointDocument.Read(args.GetProperty("afterPath").GetString()!, args.GetProperty("expectedAfterSha256").GetString());
        return Report(before, after, args);
    }
    internal static object Report(CheckpointDocument before, CheckpointDocument after, JsonElement args)
    {
        var changes = new List<Change>(); var coverage = new List<Coverage>();
        var keys = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        void ReadScope(string scope, Func<CheckpointDocument, Dictionary<string, JsonElement>> read)
        {
            try
            {
                var a = read(before); var b = read(after); Compare(scope, a, b, changes);
                keys[scope] = a.Keys.Union(b.Keys, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
                coverage.Add(new(scope, scope == "raw" ? "complete-raw" : "partial", "Decoded fields only; opaque data tracked separately by raw hashes."));
            }
            catch (InvalidDataException e) { coverage.Add(new(scope, "unsupported", e.Message)); }
            catch (ArgumentException e) { coverage.Add(new(scope, "unsupported", "Malformed reviewed structure: " + e.Message)); }
        }
        ReadScope("raw", Raw);
        ReadScope("players", d => Players(ScenarioReader.ReadPlayers(d.World.One("PL").ToArray(), requireReviewedVersion: true)));
        ReadScope("triggers", d => Triggers(CampaignTriggers.ParseScenarioSection(d.Root.One("TR").ToArray())));
        ReadScope("groups", d => Groups(CampaignTriggers.ParseScenarioSection(d.Root.One("TR").ToArray())));
        ReadScope("entities", d => Entities(CheckpointUnits.Read(d)));
        var checks = new List<object>();
        if (args.TryGetProperty("assertions", out var assertions))
            foreach (var rule in assertions.EnumerateArray())
            {
                var scope = rule.GetProperty("scope").GetString()!; var policy = rule.GetProperty("policy").GetString()!;
                var fields = rule.TryGetProperty("fields", out var f) ? f.EnumerateArray().Select(x => x.GetString()!).ToHashSet(StringComparer.Ordinal) : null;
                var selected = rule.TryGetProperty("keys", out var k) ? k.EnumerateArray().Select(x => x.GetString()!).ToHashSet(StringComparer.Ordinal) : null;
                var unsupported = coverage.Single(c => c.Scope == scope).Status == "unsupported" || scope != "raw" && fields is null
                    || fields?.Any(x => !Fields[scope].Contains(x, StringComparer.Ordinal)) == true;
                var missing = !unsupported && selected?.Any(x => !keys[scope].Contains(x)) == true;
                if (!unsupported && scope == "entities" && fields?.Contains("proto") == true)
                {
                    var units = CheckpointUnits.Read(before).Units.Concat(CheckpointUnits.Read(after).Units);
                    unsupported = units.Any(u => u.Proto is null && (selected is null || selected.Contains(u.UnitId.ToString(System.Globalization.CultureInfo.InvariantCulture))));
                }
                var violations = changes.Where(c => c.Scope == scope && (policy == "preserve"
                    ? (selected is null || selected.Contains(c.Key)) && (fields is null || fields.Contains(c.Field) || c.Field == "presence")
                    : selected is not null && !selected.Contains(c.Key) || fields is not null && !fields.Contains(c.Field))).ToArray();
                checks.Add(new { scope, policy, status = unsupported ? "unsupported" : missing || violations.Length > 0 ? "fail" : "pass",
                    violationCount = violations.Length, violations = violations.Take(20).Select(c => new { c.Key, c.Field }).ToArray(), missingRequestedIdentity = missing });
            }
        var offset = args.TryGetProperty("offset", out var o) ? o.GetInt32() : 0; var limit = args.TryGetProperty("limit", out var l) ? l.GetInt32() : 20;
        object Bounded(JsonElement v)
        {
            var text = v.GetRawText(); return text.Length <= 2048 ? v : new { truncated = true, characters = text.Length, preview = text[..2048], sha256 = CheckpointDocument.Hash(System.Text.Encoding.UTF8.GetBytes(text)) };
        }
        return new { before = new { before.Path, before.Sha256 }, after = new { after.Path, after.Sha256 },
            decodingComplete = false, universalUnchangedVerified = false, coverage, assertions = checks,
            total = changes.Count, semanticChanges = changes.Count(c => c.Scope != "raw"), rawChanges = changes.Count(c => c.Scope == "raw"), offset, limit,
            nextOffset = offset + limit < changes.Count ? (int?)(offset + limit) : null,
            changes = changes.Skip(offset).Take(limit).Select(c => new { c.Scope, c.Key, c.Field, before = Bounded(c.Before), after = Bounded(c.After) }).ToArray(),
            limitation = "Assertions evaluated before paging. Only explicit reviewed fields can pass semantic preservation. Raw hashes include unknown sections, order, tails, saving metadata and compressed file. No gameplay proof." };
    }
    internal static void SelfTest()
    {
        var before = CheckpointDocument.Decode("before", WorkflowFixtures.Checkpoint("old")); var after = CheckpointDocument.Decode("after", WorkflowFixtures.Checkpoint("new"));
        var args = Value(new { offset = 999, limit = 1, assertions = new[] {
            new { scope = "entities", policy = "preserve", fields = (string[])["note"] },
            new { scope = "entities", policy = "preserve", fields = (string[])["player", "x", "z"] },
        } });
        var report = Value(Report(before, after, args));
        if (report.GetProperty("changes").GetArrayLength() != 0 || report.GetProperty("assertions")[0].GetProperty("status").GetString() != "fail"
            || report.GetProperty("assertions")[1].GetProperty("status").GetString() != "pass" || report.GetProperty("universalUnchangedVerified").GetBoolean())
            throw new InvalidOperationException("Complete assertion evaluation despite paging fixture failed.");
        var rawOnly = Value(Report(before, CheckpointDocument.Decode("opaque", WorkflowFixtures.Checkpoint("old", opaque: 1)), Value(new { limit = 200 })));
        if (rawOnly.GetProperty("semanticChanges").GetInt32() != 0 || rawOnly.GetProperty("rawChanges").GetInt32() == 0)
            throw new InvalidOperationException("Opaque-only changes misreported as semantic equality/change.");
        var changed = Value(Report(before, CheckpointDocument.Decode("changed", WorkflowFixtures.Checkpoint("old", playerName: "Changed", taskWorkers: 2)), Value(new { limit = 200 })));
        var scopes = changed.GetProperty("changes").EnumerateArray().Select(c => c.GetProperty("scope").GetString()).ToHashSet();
        if (!scopes.Contains("players") || !scopes.Contains("triggers")) throw new InvalidOperationException("Player/TR semantic changes not detected.");
        var wholeScope = Value(new { assertions = new[] { new { scope = "entities", policy = "preserve" } } });
        if (Value(Report(before, before, wholeScope)).GetProperty("assertions")[0].GetProperty("status").GetString() != "unsupported")
            throw new InvalidOperationException("Partial entity decoding yielded universal preservation pass.");
        var allowed = Value(new { assertions = new[] { new { scope = "entities", policy = "allowChanges", keys = (string[])["100"], fields = (string[])["note"] } } });
        if (Value(Report(before, after, allowed)).GetProperty("assertions")[0].GetProperty("status").GetString() != "pass"
            || Value(Report(before, CheckpointDocument.Decode("owner", WorkflowFixtures.Checkpoint("new", owner: 1)), allowed)).GetProperty("assertions")[0].GetProperty("status").GetString() != "fail")
            throw new InvalidOperationException("Allowed-change assertions ignored unapproved fields.");
        if (Value(Report(before, CheckpointDocument.Decode("unsupported", WorkflowFixtures.Checkpoint(worldHeader: 445)), args)).GetProperty("assertions")[0].GetProperty("status").GetString() != "unsupported")
            throw new InvalidOperationException("Unsupported entity shape yielded assertion pass.");
    }
}
