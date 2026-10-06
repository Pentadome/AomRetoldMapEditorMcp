using System.Text.Json;

namespace AomMcp;

/// <summary>Partial, read-only dependency graph over exported TR and game-written scenario snapshot.</summary>
internal static class ScenarioAudit
{
    static readonly string[] PlayerKeys = ["Player", "PlayerID", "FromPlayerID", "ToPlayerID"];
    internal static object Query(JsonElement args, string exe)
    {
        var snapshot = ScenarioReader.Read(args.GetProperty("scenarioPath").GetString()!);
        var triggers = CampaignTriggers.Read(args.GetProperty("triggerPath").GetString()!);
        var playerId = args.GetProperty("player").GetInt32();
        var player = snapshot.Players.SingleOrDefault(p => p.Id == playerId)
            ?? throw new ArgumentException("Player absent from supplied scenario snapshot.");
        var offset = args.TryGetProperty("offset", out var o) ? o.GetInt32() : 0;
        var limit = args.TryGetProperty("limit", out var l) ? l.GetInt32() : 20;
        var references = new List<object>();
        var relevantIds = new HashSet<uint>();
        var eventEdges = new List<(uint Source, uint Target, string Effect)>();
        var tribute = new List<object>();
        var starts = new List<object>();
        var victoryChecks = new List<object>();
        var triggerIds = triggers.Triggers.Select(t => t.Id).ToHashSet();
        var unresolved = new List<object>();
        foreach (var trigger in triggers.Triggers)
        {
            foreach (var element in trigger.Conditions.Concat(trigger.Effects))
            {
                var targetPlayers = element.Args.Where(a => PlayerKeys.Contains(a.Key, StringComparer.OrdinalIgnoreCase))
                    .SelectMany(a => a.Values.Select(v => new { a.Key, value = v })).ToArray();
                if (targetPlayers.Any(p => p.value == playerId.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                {
                    relevantIds.Add(trigger.Id);
                    references.Add(new { trigger.Id, trigger.Name, elementName = element.Name,
                        playerArgs = targetPlayers.Where(p => p.value == playerId.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray() });
                }
                if (element.Name.Contains("Send Tribute", StringComparison.OrdinalIgnoreCase))
                    tribute.Add(new { trigger.Id, trigger.Name, triggerActive = trigger.Flags[1] != 0,
                        triggerLoop = trigger.Flags[0] != 0, players = targetPlayers });
                if (element.Name.Contains("AI: Call Function", StringComparison.OrdinalIgnoreCase)
                    && element.Args.Any(a => a.Values.Any(v => v.Contains("startAttacking", StringComparison.OrdinalIgnoreCase))))
                    starts.Add(new { trigger.Id, trigger.Name, players = targetPlayers, elementName = element.Name });
                if (element.Name.Contains("All Valid Units and Buildings Dead", StringComparison.OrdinalIgnoreCase))
                    victoryChecks.Add(new { trigger.Id, trigger.Name, players = targetPlayers });
                if (element.Name.StartsWith("Trigger:", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var a in element.Args.Where(a => a.Key.Equals("EventID", StringComparison.OrdinalIgnoreCase)))
                        foreach (var value in a.Values)
                            if (uint.TryParse(value, out var target))
                            {
                                eventEdges.Add((trigger.Id, target, element.Name));
                                if (!triggerIds.Contains(target))
                                    unresolved.Add(new { sourceId = trigger.Id, targetId = target, effect = element.Name });
                            }
                }
            }
        }
        var ai = AiScripts.Resolve(exe, player.AiPath);
        var exampleAis = snapshot.Players.Where(p => p.Id != player.Id && !string.IsNullOrEmpty(p.AiPath))
            .Take(12).Select(p => new { playerId = p.Id, script = AiScripts.Resolve(exe, p.AiPath) }).ToArray();
        var incoming = eventEdges.Where(e => relevantIds.Contains(e.Target)).Take(100)
            .Select(e => new { sourceId = e.Source, targetId = e.Target, effect = e.Effect }).ToArray();
        var outgoing = eventEdges.Where(e => relevantIds.Contains(e.Source)).Take(100)
            .Select(e => new { sourceId = e.Source, targetId = e.Target, effect = e.Effect }).ToArray();
        var paged = references.Skip(offset).Take(limit).ToArray();
        return new
        {
            player = new { player.Id, player.Name, player.AiPath,
                stances = player.Diplomacy.Select((stance, target) => new { target, stance }).ToArray() },
            scenarioSha256 = snapshot.Sha256, triggerSha256 = triggers.Sha256,
            referenceTotal = references.Count, offset, limit,
            nextOffset = offset + limit < references.Count ? (int?)(offset + limit) : null,
            references = paged, incomingEventEdges = incoming, outgoingEventEdges = outgoing,
            tribute, waveStarts = starts, victoryChecks, unresolvedEventIds = unresolved.Take(100).ToArray(),
            ai, referenceAis = exampleAis,
            warnings = new[]
            {
                "Exported files may not be current live state. Existing triggers can override player settings at runtime.",
                "Wave AI needs a valid personality, gameplay-start call, eligible units/buildings, and victory/objective updates; static references do not prove waves spawn.",
                "Player-relative AI personality must be under INSTALLPATH\\game\\ai; active-profile ai folder did not work. Trigger exports/imports use active-profile trigger directory.",
            },
        };
    }
}
