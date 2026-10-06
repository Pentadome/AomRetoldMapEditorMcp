using System.Text.Json;
using System.Text.RegularExpressions;

namespace AomMcp;

/// <summary>Read-only, heuristic parity hints over a reviewed TR export. Never assumes a gap should be filled.</summary>
internal static class TriggerParity
{
    static readonly string[] PlayerKeys = ["Player", "PlayerID", "FromPlayerID", "ToPlayerID"];

    sealed record Entry(CampaignTriggers.Trigger Trigger, string Kind, int Index,
        CampaignTriggers.Element Element, CampaignTriggers.Arg PlayerArg);

    static Entry? Candidate(CampaignTriggers.Trigger trigger, string kind, int index, CampaignTriggers.Element element)
    {
        var playerArgs = element.Args.Where(a => PlayerKeys.Contains(a.Key, StringComparer.OrdinalIgnoreCase) && a.Values.Length == 1).ToArray();
        return playerArgs.Length == 1 ? new(trigger, kind, index, element, playerArgs[0]) : null;
    }

    static string Signature(Entry entry) => JsonSerializer.Serialize(new
    {
        entry.Element.Name,
        entry.PlayerArg.Key,
        args = entry.Element.Args.Where(a => !ReferenceEquals(a, entry.PlayerArg))
            .Select(a => new { a.Key, a.ValueType, a.Values }).ToArray(),
    });

    internal static object Query(JsonElement args)
    {
        var doc = CampaignTriggers.Read(args.GetProperty("path").GetString()!);
        var template = args.GetProperty("templatePlayer").GetInt32();
        var target = args.GetProperty("targetPlayer").GetInt32();
        if (template is < 0 or > 12 || target is < 0 or > 12 || template == target)
            throw new ArgumentException("templatePlayer and targetPlayer must be distinct 0..12 slots.");
        var offset = args.TryGetProperty("offset", out var o) ? o.GetInt32() : 0;
        var limit = args.TryGetProperty("limit", out var l) ? l.GetInt32() : 50;
        if (offset < 0 || limit is < 1 or > 200) throw new ArgumentException("Parity offset/limit outside bounds.");
        HashSet<uint>? ids = args.TryGetProperty("triggerIds", out var idArg)
            ? idArg.EnumerateArray().Select(v => v.GetUInt32()).ToHashSet() : null;
        if (ids is { Count: 0 or > 200 }) throw new ArgumentException("triggerIds must contain 1..200 IDs.");
        if (ids is not null && ids.Any(id => !doc.Triggers.Any(t => t.Id == id)))
            throw new ArgumentException("Unknown triggerId in parity selection.");
        var filter = args.TryGetProperty("filter", out var f) ? f.GetString() ?? "" : "";
        var triggers = doc.Triggers.Where(t => (ids is null || ids.Contains(t.Id)) && t.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
        var all = new List<object>();
        var proposals = new List<object>();
        foreach (var trigger in triggers)
        {
            var entries = trigger.Conditions.Select((el, i) => Candidate(trigger, "condition", i, el))
                .Concat(trigger.Effects.Select((el, i) => Candidate(trigger, "effect", i, el)))
                .Where(e => e is not null).Select(e => e!).ToArray();
            var covered = new HashSet<Entry>();
            var missing = new List<Entry>(); var targetOnly = new List<Entry>();
            foreach (var from in entries.Where(e => e.PlayerArg.Values[0] == template.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            {
                var counterpart = entries.FirstOrDefault(e => !covered.Contains(e) && e.Kind == from.Kind
                    && e.PlayerArg.Values[0] == target.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    && Signature(e) == Signature(from));
                if (counterpart is null) missing.Add(from);
                else covered.Add(counterpart);
            }
            targetOnly.AddRange(entries.Where(e => e.PlayerArg.Values[0] == target.ToString(System.Globalization.CultureInfo.InvariantCulture)
                && !covered.Contains(e)));
            if (missing.Count == 0 && targetOnly.Count == 0) continue;
            var duplicates = new List<object>(); var replacements = new List<object>(); var labels = new List<object>();
            var condOrdinal = 0; var effectOrdinal = 0;
            foreach (var item in missing)
            {
                var index = item.Kind == "condition" ? trigger.Conditions.Length + condOrdinal++ : trigger.Effects.Length + effectOrdinal++;
                duplicates.Add(new { kind = item.Kind, elementIndex = item.Index });
                replacements.Add(new { kind = item.Kind, elementIndex = index, parameter = item.PlayerArg.Key,
                    expected = item.PlayerArg.Values[0], value = target.ToString(System.Globalization.CultureInfo.InvariantCulture) });
                var after = Regex.Replace(item.Element.Kind, $@"(?<!\w)P{template}(?!\d)", "P" + target, RegexOptions.IgnoreCase);
                if (after != item.Element.Kind)
                    labels.Add(new { kind = item.Kind, elementIndex = index, expected = item.Element.Kind, value = after });
            }
            object Detail(Entry e) => new { triggerId = trigger.Id, triggerName = trigger.Name, kind = e.Kind, elementIndex = e.Index,
                elementName = e.Element.Name, label = e.Element.Kind, parameter = e.PlayerArg.Key,
                player = e.PlayerArg.Values[0], args = e.Element.Args.Select(a => new { a.Key, a.Values }).ToArray() };
            all.Add(new { triggerId = trigger.Id, triggerName = trigger.Name,
                gaps = missing.Select(Detail).ToArray(), targetOnly = targetOnly.Select(Detail).ToArray() });
            if (missing.Count > 0)
                proposals.Add(new { operation = "patch", triggerId = trigger.Id, expectedName = trigger.Name,
                    duplicates, replacements, labels });
        }
        var templateToken = new Regex($@"(?<!\w)(P{template}|Player_{template})(?!\d)", RegexOptions.IgnoreCase);
        var targetToken = new Regex($@"(?<!\w)(P{target}|Player_{target})(?!\d)", RegexOptions.IgnoreCase);
        var perPlayerTriggers = triggers.Where(t => templateToken.IsMatch(t.Name)
            && !doc.Triggers.Any(other => targetToken.IsMatch(other.Name)
                && templateToken.Replace(t.Name, "#").Equals(targetToken.Replace(other.Name, "#"), StringComparison.OrdinalIgnoreCase)))
            .Select(t => new { t.Id, t.Name }).ToArray();
        return new { doc.Sha256, templatePlayer = template, targetPlayer = target, total = all.Count, offset, limit,
            nextOffset = offset + limit < all.Count ? (int?)(offset + limit) : null,
            findings = all.Skip(offset).Take(limit).ToArray(), perPlayerTriggers,
            proposedEdits = proposals.Skip(offset).Take(Math.Min(limit, 64)).ToArray(),
            limitation = "Heuristic review hints only. Signatures ignore element display labels, include other args verbatim; asymmetric story triggers may be intentional. Review each proposed edit before writing." };
    }
}
