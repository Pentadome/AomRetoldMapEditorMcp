using System.Text.Json;

namespace AomMcp;

/// <summary>Connection-free guidance tools: editor_help (topic guides moved out of AGENTS.md) and editor_scenario_digest.</summary>
internal static class HelpTools
{
    internal const string HelpTool = "editor_help", DigestTool = "editor_scenario_digest";
    const int MaxTopicBytes = 64 * 1024, MaxTriggerNames = 40;

    static string? HelpDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "research", "help");
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }

    static string[] Topics(string directory) => Directory.GetFiles(directory, "*.md").Select(f => Path.GetFileNameWithoutExtension(f)).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    internal static void PreflightHelp(JsonElement args)
    {
        Catalog.ValidateObject(args, ["topic"]);
        if (args.TryGetProperty("topic", out var t) && (t.GetString() is not { Length: > 0 and <= 40 } topic || !topic.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')))
            throw new ArgumentException("topic must be a short name made of letters, digits and '-'. Call editor_help without arguments for the list.");
    }

    internal static object Help(JsonElement args)
    {
        var directory = HelpDirectory() ?? throw new FileNotFoundException("research/help not found beside the server install; topic guides ship with the repository.");
        var topics = Topics(directory);
        if (!args.TryGetProperty("topic", out var t))
            return new
            {
                topics = topics.Select(n => new { name = n, title = File.ReadLines(Path.Combine(directory, n + ".md")).FirstOrDefault()?.TrimStart('#', ' ') }).ToArray(),
                usage = "Call editor_help with topic=<name> for the full guide.",
            };
        var name = t.GetString()!;
        if (!topics.Contains(name, StringComparer.Ordinal))
            throw new ArgumentException($"Unknown topic '{name}'. Topics: {string.Join(", ", topics)}.");
        var file = new FileInfo(Path.Combine(directory, name + ".md"));
        if (file.Length > MaxTopicBytes) throw new InvalidDataException("Topic guide exceeds size bound.");
        return new { topic = name, text = File.ReadAllText(file.FullName) };
    }

    internal static void PreflightDigest(JsonElement args)
    {
        Catalog.ValidateObject(args, ["path", "expectedSha256", "triggerPath", "expectedTriggerSha256"]);
        _ = EditorFiles.LocalPath(args.GetProperty("path").GetString()!);
        if (args.TryGetProperty("triggerPath", out _) != args.TryGetProperty("expectedTriggerSha256", out _))
            throw new ArgumentException("triggerPath and expectedTriggerSha256 must be supplied together.");
        if (args.TryGetProperty("triggerPath", out var tp)) _ = EditorFiles.LocalPath(tp.GetString()!);
    }

    internal static object Digest(JsonElement args, string exe)
    {
        var snapshot = ScenarioReader.Read(args.GetProperty("path").GetString()!);
        CheckpointDocument.RequireHash(snapshot.Sha256, args.GetProperty("expectedSha256").GetString());
        var players = snapshot.Players.Select(p =>
        {
            var ai = AiScripts.Resolve(exe, p.AiPath);
            var allies = p.Diplomacy.Select((s, i) => (s, i)).Where(x => x.s == 1 && x.i != p.Id && x.i < snapshot.Players.Length).Select(x => x.i).ToArray();
            var enemies = p.Diplomacy.Select((s, i) => (s, i)).Where(x => x.s == 2 && x.i < snapshot.Players.Length).Select(x => x.i).ToArray();
            return new
            {
                id = p.Id, name = p.DisplayName.Length > 0 ? p.DisplayName : p.Name, civId = p.CivId,
                control = p.Control switch { 0 => "Human", 1 => "Computer", _ => "other" },
                aiPath = p.AiPath.Length > 0 ? p.AiPath : null, aiResolved = p.AiPath.Length > 0 ? (bool?)ResolvedFlag(ai) : null,
                allies, enemies,
            };
        }).ToArray();
        object? triggers = null;
        if (args.TryGetProperty("triggerPath", out var tp))
        {
            var doc = CampaignTriggers.Read(tp.GetString()!);
            CheckpointDocument.RequireHash(doc.Sha256, args.GetProperty("expectedTriggerSha256").GetString());
            var snippets = doc.Triggers.Count(t => t.Effects.Any(e => e.Name.Contains("Code Snippet", StringComparison.OrdinalIgnoreCase)));
            triggers = new
            {
                count = doc.Triggers.Length,
                groups = doc.Groups.Select(g => new { g.Id, g.Name, triggers = g.Indexes.Length }).ToArray(),
                withCodeSnippetEffect = snippets,
                victoryLike = doc.Triggers.Where(t => t.Effects.Any(e => e.Name.Contains("Victory", StringComparison.OrdinalIgnoreCase) || e.Name.Contains("Defeat", StringComparison.OrdinalIgnoreCase)))
                    .Take(MaxTriggerNames).Select(t => t.Name).ToArray(),
                names = doc.Triggers.Take(MaxTriggerNames).Select(t => t.Name).ToArray(),
                namesTruncated = doc.Triggers.Length > MaxTriggerNames,
            };
        }
        return new
        {
            path = snapshot.Path, sha256 = snapshot.Sha256, playerCount = players.Length, players, triggers,
            limitation = "Summary of saved files only (no live/runtime proof). Details: editor_players, editor_trigger_list, editor_player_dependency_audit; XS compile state: editor_xs_diagnostics after a playtest load.",
        };
    }

    static bool ResolvedFlag(object resolution) => (bool)(resolution.GetType().GetProperty("resolved")?.GetValue(resolution) ?? false);

    internal static void SelfTest()
    {
        PreflightHelp(JsonDocument.Parse("""{"topic":"xs"}""").RootElement);
        try { PreflightHelp(JsonDocument.Parse("""{"topic":"../x"}""").RootElement); throw new InvalidOperationException("help topic traversal accepted."); }
        catch (ArgumentException) { }
    }
}
