using System.Text;
using System.Text.RegularExpressions;

namespace AomMcp;

/// <summary>Finds saved scenarios in a profile scenario folder by recency.</summary>
internal static class ScenarioFiles
{
    /// <summary>Active profile Games\Age of Mythology Retold\&lt;id&gt; (parent of Game\AI), or null when not exactly one.</summary>
    internal static string? ProfileHome() =>
        AiScripts.SafeProfileRoot() is { } ai
            ? Path.GetDirectoryName(Path.GetDirectoryName(ai))
            : null;

    /// <summary>"latest" → newest saved scenario in the active profile scenario folder; any other value is returned unchanged.</summary>
    internal static string Resolve(string requested) =>
        requested != "latest"
            ? requested
            : Latest(
                Path.Combine(
                    ProfileHome()
                        ?? throw new InvalidDataException(
                            "\"latest\" needs one active profile (Games\\Age of Mythology Retold\\<id>\\Game\\AI); pass an explicit path."
                        ),
                    "scenario"
                )
            ).FullName;

    /// <summary>Newest game-saved .mythscn under <paramref name="scenarioDirectory"/> (recursive, no reparse points),
    /// excluding the editor's ~testing scratch save and host AomMcp-checkpoint files.</summary>
    internal static FileInfo Latest(string scenarioDirectory)
    {
        var dir = new DirectoryInfo(EditorFiles.LocalPath(scenarioDirectory));
        if (!dir.Exists)
            throw new FileNotFoundException("Profile scenario folder missing: " + dir.FullName);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            MaxRecursionDepth = 8,
        };
        return dir.EnumerateFiles("*.mythscn", options)
                .Where(f =>
                    f.Extension.Equals(".mythscn", StringComparison.OrdinalIgnoreCase)
                    && !f.Name.StartsWith("~testing", StringComparison.OrdinalIgnoreCase)
                    && !f.Name.StartsWith("AomMcp-checkpoint", StringComparison.OrdinalIgnoreCase)
                )
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ThenBy(f => f.FullName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault()
            ?? throw new FileNotFoundException(
                "No saved .mythscn (excluding ~testing*/AomMcp-checkpoint*) under " + dir.FullName
            );
    }
}

/// <summary>
/// XS files a saved scenario needs: every player's AI personality (PL aiPath) and every TR XS Scripts entry, each with its
/// include closure. Lookup order per file: profile (Game\AI or trigger\), existing mod (game\ai or trigger\), stock install
/// (loose game\ai; trigger scripts inside game\data\Data.bar under trigger\). Read-only; never compiles or runs XS.
/// </summary>
internal static partial class ScenarioDependencies
{
    const int MaxNodes = 256,
        MaxDepth = 32;

    [GeneratedRegex("^\\s*include\\s+\"([^\"\\r\\n]+)\"\\s*;?\\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex IncludeLine();

    /// <summary>Null roots are skipped. StockTrigger answers whether the install ships trigger\&lt;relative&gt;.</summary>
    internal sealed record Roots(
        string? ProfileAi,
        string? ProfileTrigger,
        string? ModAi,
        string? ModTrigger,
        string? StockAi,
        Func<string, bool> StockTrigger
    );

    /// <summary>Kind ai|trigger; From profile|mod|stock|embedded|missing; Path null for embedded/archived/missing.</summary>
    internal sealed record Node(
        string Kind,
        string Relative,
        string From,
        string? Path,
        string? Sha256,
        long Bytes,
        string Via,
        string[] Includes
    );

    internal sealed record PlayerAi(uint Player, string Name, string Control, string AiPath);

    internal sealed record Report(
        bool Readable,
        string? Reason,
        PlayerAi[] Players,
        CampaignTriggers.Script[] Scripts,
        Node[] Nodes,
        string[] Warnings
    );

    /// <summary>Editor-side lookup roots: active profile Game\AI and trigger\, then the stock install (no mods).</summary>
    internal static Roots ProfileRoots(string exe)
    {
        var home = ScenarioFiles.ProfileHome();
        var trigger =
            home is not null && Directory.Exists(Path.Combine(home, "trigger"))
                ? EditorFiles.LocalPath(Path.Combine(home, "trigger"))
                : null;
        Func<string, bool> stock;
        try
        {
            var archive = StockTriggers(exe); // Lazy: the archive is read on first lookup, so failures surface there.
            stock = relative =>
            {
                try
                {
                    return archive(relative);
                }
                catch (Exception e)
                    when (e is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    return false;
                }
            };
        }
        catch (Exception e)
            when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            stock = _ => false;
        }
        return new(
            AiScripts.SafeProfileRoot(),
            trigger,
            null,
            null,
            AiScripts.InstalledRoot(exe),
            stock
        );
    }

    /// <summary>Missing files in the include closure below one root (the root itself excluded). ai roots accept a PL aiPath.</summary>
    internal static string[] MissingIncludes(Report report, string kind, string root)
    {
        if (kind == "ai")
        {
            try
            {
                root = AiScripts.Relative(root, appendExtension: true);
            }
            catch (ArgumentException)
            {
                return [];
            }
        }
        var map = report
            .Nodes.Where(n => n.Kind == kind)
            .GroupBy(n => n.Relative, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>([root]);
        while (queue.TryDequeue(out var relative))
        {
            if (!seen.Add(relative) || !map.TryGetValue(relative, out var node))
                continue;
            if (node.From == "missing" && relative != root)
                missing.Add(relative);
            foreach (var include in node.Includes)
                queue.Enqueue(include);
        }
        return missing.ToArray();
    }

    internal static Report Analyze(CheckpointDocument document, Roots roots)
    {
        ScenarioReader.Snapshot snapshot;
        CampaignTriggers.Script[] scripts;
        try
        {
            snapshot = ScenarioReader.Read(document);
            scripts = snapshot.TriggerSection is { } tr ? CampaignTriggers.ScenarioScripts(tr) : [];
        }
        catch (InvalidDataException e)
        {
            return new(
                false,
                "Player/trigger sections not readable: " + e.Message,
                [],
                [],
                [],
                [
                    "XS dependencies unknown: scenario PL/TR sections could not be decoded; self-containment NOT checked.",
                ]
            );
        }
        var nodes = new List<Node>();
        var warnings = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var players = new List<PlayerAi>();

        void Visit(string kind, string relative, string via, int depth, string? embeddedText = null)
        {
            if (!seen.Add(kind + ":" + relative))
                return;
            if (nodes.Count >= MaxNodes || depth > MaxDepth)
            {
                warnings.Add(
                    "Dependency walk stopped at host bound (256 files / depth 32): " + relative
                );
                return;
            }
            string from = "missing";
            string? path = null;
            if (embeddedText is not null)
                from = "embedded";
            else
            {
                var sources =
                    kind == "ai"
                        ? new[]
                        {
                            ("profile", roots.ProfileAi),
                            ("mod", roots.ModAi),
                            ("stock", roots.StockAi),
                        }
                        : new[] { ("profile", roots.ProfileTrigger), ("mod", roots.ModTrigger) };
                foreach (var (name, root) in sources)
                {
                    if (root is null)
                        continue;
                    var candidate = AiScripts.Contained(root, relative);
                    if (File.Exists(candidate))
                    {
                        from = name;
                        path = candidate;
                        break;
                    }
                }
                if (path is null && kind == "trigger" && roots.StockTrigger(relative))
                    from = "stock";
            }
            byte[]? bytes = null;
            if (path is not null)
            {
                var info = new FileInfo(path);
                if (info.Length > 1_000_000)
                    warnings.Add("XS file over 1 MB not hashed/walked: " + path);
                else
                    bytes = File.ReadAllBytes(path);
            }
            var includes = new List<string>();
            // Stock files are part of every install; their own closure is not walked.
            var text =
                embeddedText
                ?? (
                    bytes is not null && from != "stock"
                        ? new UTF8Encoding(false, false).GetString(bytes).TrimStart('\uFEFF')
                        : null
                );
            if (text is not null)
            {
                try
                {
                    foreach (var line in AiInstaller.StripComments(text).Split('\n'))
                    {
                        var trimmed = line.TrimStart();
                        if (
                            !trimmed.StartsWith("include", StringComparison.Ordinal)
                            || trimmed.Length > 7
                                && (char.IsLetterOrDigit(trimmed[7]) || trimmed[7] == '_')
                        )
                            continue;
                        var match = IncludeLine().Match(line);
                        if (!match.Success)
                        {
                            warnings.Add(
                                $"Unreviewed include directive in {relative}: {line.Trim()}"
                            );
                            continue;
                        }
                        try
                        {
                            includes.Add(AiScripts.Relative(match.Groups[1].Value));
                        }
                        catch (ArgumentException)
                        {
                            warnings.Add(
                                $"Unsafe include path in {relative}: {match.Groups[1].Value}"
                            );
                        }
                    }
                }
                catch (InvalidDataException e)
                {
                    warnings.Add($"Include analysis skipped for {relative}: {e.Message}");
                }
            }
            nodes.Add(
                new(
                    kind,
                    relative,
                    from,
                    path,
                    bytes is null ? null : CheckpointDocument.Hash(bytes),
                    bytes?.LongLength ?? 0,
                    via,
                    includes.ToArray()
                )
            );
            foreach (var include in includes)
                Visit(kind, include, "include in " + relative, depth + 1);
        }

        foreach (var p in snapshot.Players)
        {
            if (p.Id == 0 || string.IsNullOrWhiteSpace(p.AiPath))
                continue;
            var control = p.Control switch
            {
                0 => "Human",
                1 => "Computer",
                3 => "Unavailable",
                _ => "unknown",
            };
            players.Add(new(p.Id, p.Name, control, p.AiPath));
            string relative;
            try
            {
                relative = AiScripts.Relative(p.AiPath, appendExtension: true);
            }
            catch (ArgumentException)
            {
                warnings.Add($"Player {p.Id} aiPath is not a safe relative path: {p.AiPath}");
                continue;
            }
            Visit("ai", relative, $"player {p.Id} aiPath", 0);
        }
        foreach (var s in scripts)
        {
            if (s.Embedded)
            {
                Visit(
                    "trigger",
                    s.Name,
                    "TR XS script (embedded body)",
                    0,
                    string.Join('\n', s.Lines)
                );
                continue;
            }
            string relative;
            try
            {
                relative = AiScripts.Relative(s.Name);
            }
            catch (ArgumentException)
            {
                warnings.Add("TR XS script name is not a safe relative .xs path: " + s.Name);
                continue;
            }
            Visit("trigger", relative, "TR XS script", 0);
        }
        if (
            nodes.Any(n =>
                n.Kind == "trigger" && n.Via.StartsWith("include", StringComparison.Ordinal)
            )
        )
            warnings.Add(
                "Trigger-script includes are resolved against trigger\\ roots (assumption, not verified in game)."
            );
        return new(true, null, players.ToArray(), scripts, nodes.ToArray(), warnings.ToArray());
    }

    static readonly Dictionary<string, HashSet<string>> StockTriggerCache = new(
        StringComparer.OrdinalIgnoreCase
    );

    /// <summary>Stock trigger scripts ship inside game\data\Data.bar as trigger\&lt;relative&gt;; loaded once per archive path.</summary>
    internal static Func<string, bool> StockTriggers(string exe)
    {
        var archive = GameDataCatalog.Archive(exe);
        HashSet<string>? names = null;
        return relative =>
        {
            if (names is null)
                lock (StockTriggerCache)
                    if (!StockTriggerCache.TryGetValue(archive, out names))
                    {
                        names = new(StringComparer.OrdinalIgnoreCase);
                        if (File.Exists(archive))
                        {
                            using var bar = new BarArchive(archive);
                            foreach (var entry in bar.Paths)
                            {
                                var at =
                                    entry.StartsWith("trigger/", StringComparison.OrdinalIgnoreCase)
                                        ? 0
                                    : entry.IndexOf("/trigger/", StringComparison.OrdinalIgnoreCase)
                                        is var i
                                            and >= 0
                                        ? i + 1
                                    : -1;
                                if (at >= 0)
                                    names.Add(entry[(at + 8)..].Replace('/', '\\'));
                            }
                        }
                        StockTriggerCache[archive] = names;
                    }
            return names.Contains(relative);
        };
    }

    internal static void SelfTest()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "AomMcp-deps-selftest-" + Guid.NewGuid().ToString("N")
        );
        try
        {
            var ai = Directory.CreateDirectory(Path.Combine(root, "ai", "pack")).Parent!.FullName;
            var trigger = Directory
                .CreateDirectory(Path.Combine(root, "trigger", "pack"))
                .Parent!.FullName;
            var stock = Directory
                .CreateDirectory(Path.Combine(root, "stock", "core"))
                .Parent!.FullName;
            File.WriteAllText(
                Path.Combine(ai, "pack", "p2.xs"),
                "// include \"pack\\ghost.xs\";\ninclude \"core\\main.xs\";\ninclude \"pack\\shared.xs\";\nvoid main() {}\n"
            );
            File.WriteAllText(
                Path.Combine(ai, "pack", "shared.xs"),
                "include \"pack\\p2.xs\";\ninclude \"pack\\gone.xs\";\n"
            );
            File.WriteAllText(Path.Combine(ai, "pack", "stray.txt"), "not referenced");
            File.WriteAllText(
                Path.Combine(stock, "core", "main.xs"),
                "include \"core\\never_walked.xs\";\n"
            );
            File.WriteAllText(Path.Combine(trigger, "pack", "bonus.xs"), "rule r { }\n");
            var roots = new Roots(
                ai,
                trigger,
                null,
                null,
                stock,
                r => r.Equals(@"campaign\x.xs", StringComparison.OrdinalIgnoreCase)
            );
            var path = Path.Combine(root, "fixture.mythscn");
            File.WriteAllBytes(
                path,
                WorkflowFixtures.Checkpoint(
                    aiPaths: ["", "", @"pack\p2", @"pack\p2.xs"],
                    scripts: [@"pack\bonus.xs", @"campaign\x.xs", @"pack\none.xs"]
                )
            );
            var report = Analyze(CheckpointDocument.Read(path), roots);
            string From(string kind, string relative) =>
                report
                    .Nodes.Single(n =>
                        n.Kind == kind
                        && n.Relative.Equals(relative, StringComparison.OrdinalIgnoreCase)
                    )
                    .From;
            if (
                !report.Readable
                || report.Players.Length != 2
                || report.Scripts.Length != 3
                || report.Nodes.Length != 7
                || From("ai", @"pack\p2.xs") != "profile"
                || From("ai", @"pack\shared.xs") != "profile"
                || From("ai", @"core\main.xs") != "stock"
                || From("ai", @"pack\gone.xs") != "missing"
                || From("trigger", @"pack\bonus.xs") != "profile"
                || From("trigger", @"campaign\x.xs") != "stock"
                || From("trigger", @"pack\none.xs") != "missing"
                || report.Nodes.Any(n =>
                    n.Relative.Contains("ghost", StringComparison.Ordinal)
                    || n.Relative.Contains("never_walked", StringComparison.Ordinal)
                )
            )
                throw new InvalidOperationException("Scenario dependency closure fixture failed.");
            var bare = Path.Combine(root, "bare.mythscn");
            File.WriteAllBytes(bare, WorkflowFixtures.Checkpoint());
            File.SetLastWriteTimeUtc(bare, File.GetLastWriteTimeUtc(path).AddMinutes(1));
            File.WriteAllBytes(Path.Combine(root, "~testing.mythscn"), []);
            if (
                Analyze(CheckpointDocument.Read(bare), roots).Nodes.Length != 0
                || ScenarioFiles.Latest(root).Name != "bare.mythscn"
            )
                throw new InvalidOperationException(
                    "ScenarioFiles.Latest/empty dependency fixture failed."
                );
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch (IOException) { }
        }
    }
}
