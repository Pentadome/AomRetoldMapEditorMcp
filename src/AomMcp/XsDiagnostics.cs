using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AomMcp;

/// <summary>Read-only XS compile diagnostics from the game's MythLog and the last compiled trigger script (trigtemp.xs).</summary>
/// <remarks>Both files are written by the game itself; nothing here compiles XS or connects to the game.</remarks>
internal static partial class XsDiagnostics
{
    internal const string Tool = "editor_xs_diagnostics";
    internal static readonly string[] Operations = ["errors", "trigtemp"];
    static readonly string[] TrigtempOnly = ["trigger", "line", "eventId", "triggerPath", "expectedSha256", "bodyOffset", "maxLines"];
    static readonly string[] TrigtempTargets = ["trigger", "line", "eventId"];
    internal static readonly string[] LoadModes = ["latest", "latestWithErrors", "all"];
    // Host read bounds, not game limits: observed MythLog ~25 KB per session, trigtemp ~160 KB for a large campaign.
    const long MaxLogBytes = 4_000_000, MaxTrigtempBytes = 2_000_000, MaxSourceBytes = 1_000_000;
    internal const int MaxLimit = 100, MaxContext = 10, MaxBodyLines = 1000;

    // Log entries start with a tick/elapsed prefix ("3250     00:00:03:  ...") or "PreGame  6265:  ..."; continuation lines do not.
    [GeneratedRegex(@"^(?:\d+|PreGame)\s+(?:\d{2}:\d{2}:\d{2}:|\d+:)\s")]
    private static partial Regex EntryPrefix();
    [GeneratedRegex(@"(?<file>[^\s:][^:()]*?)\((?<line>\d+)\) : XS: Error(?: (?<code>\d{4}))?: (?<msg>.*)$")]
    private static partial Regex Located();
    [GeneratedRegex(@"XS: Error: could not compile file '(?<file>[^']+)'")]
    private static partial Regex CompileFailed();
    [GeneratedRegex(@"could not parse the code for '(?<name>[^']+)' (?<kind>function|rule)")]
    private static partial Regex Scope();
    [GeneratedRegex(@"Title: Player #(?<player>-?\d+)")]
    private static partial Regex Player();
    [GeneratedRegex(@"Loading game file: (?<file>.+)$")]
    private static partial Regex Loading();
    [GeneratedRegex(@"File '[^']+' opened at (?<when>.+)$")]
    private static partial Regex Opened();
    [GeneratedRegex(@"^rule\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*$")]
    private static partial Regex RuleLine();
    [GeneratedRegex(@"^\s*case\s+(?<event>\d+)\s*:")]
    private static partial Regex CaseLine();
    [GeneratedRegex(@"xsEnableRule\(""(?<rule>[^""]+)""\)")]
    private static partial Regex EnableRule();
    [GeneratedRegex(@"^\s*include\s+""(?<path>[^""]+)""\s*;")]
    private static partial Regex IncludeLine();

    internal sealed record Diagnostic(string File, int Line, string? Code, string Message, string? ScopeKind, string? ScopeName,
        string[] Chain, SortedSet<string> CompiledFiles, SortedSet<int> Players, int LogLine)
    {
        internal int Occurrences { get; set; } = 1;
    }
    internal sealed record Load(int Index, string File, int StartLine, bool Playtest, List<Diagnostic> Diagnostics, List<string> OtherErrors);
    internal sealed record LogModel(string? OpenedAt, Load[] Loads);

    internal static string DefaultLogPath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Games", "Age of Mythology Retold", "temp", "Logs", "MythLog.txt");

    internal static void Preflight(JsonElement args)
    {
        var operation = args.TryGetProperty("operation", out var o) ? o.GetString() : "errors";
        if (args.TryGetProperty("logPath", out var log)) _ = LogPath(log.GetString()!);
        if (operation == "errors")
        {
            foreach (var key in TrigtempOnly)
                if (args.TryGetProperty(key, out _)) throw new ArgumentException($"{key} applies to operation=trigtemp only.");
        }
        else
        {
            if (args.TryGetProperty("load", out _) || args.TryGetProperty("loadIndex", out _) || args.TryGetProperty("logPath", out _))
                throw new ArgumentException("load/logPath apply to operation=errors only.");
            if (args.TryGetProperty("triggerPath", out _) != args.TryGetProperty("expectedSha256", out _))
                throw new ArgumentException("triggerPath and expectedSha256 must be supplied together (rule→trigger mapping uses a hash-pinned export).");
            if (TrigtempTargets.Count(k => args.TryGetProperty(k, out _)) > 1)
                throw new ArgumentException("Choose at most one of trigger, line, eventId.");
        }
        if (args.TryGetProperty("load", out _) && args.TryGetProperty("loadIndex", out _))
            throw new ArgumentException("Choose load or loadIndex, not both.");
    }

    static string LogPath(string supplied)
    {
        var path = EditorFiles.LocalPath(supplied);
        var name = Path.GetFileName(path);
        if (!name.StartsWith("MythLog", StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("logPath must name a MythLog*.txt file (game log), e.g. <Games>\\Age of Mythology Retold\\temp\\Logs\\MythLog.txt.");
        return path;
    }

    /// <summary>Reads a file the game may still append to: shared read, retried until length and timestamp are stable.</summary>
    internal static (byte[] Bytes, DateTime LastWriteUtc) StableRead(string path, long max)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var before = new FileInfo(path);
            if (!before.Exists) throw new FileNotFoundException("File not found: " + path, path);
            if (before.Length > max) throw new InvalidDataException($"{Path.GetFileName(path)} is {before.Length} bytes; host read bound is {max}.");
            byte[] bytes;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                bytes = new byte[stream.Length];
                stream.ReadExactly(bytes);
            }
            var after = new FileInfo(path);
            if (after.Length == bytes.Length && after.LastWriteTimeUtc == before.LastWriteTimeUtc) return (bytes, after.LastWriteTimeUtc);
            Thread.Sleep(50); // Game is appending; re-read (read-only, no side effect).
        }
        throw new InvalidDataException(Path.GetFileName(path) + " kept changing while being read; call again.");
    }

    static string Decode(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    internal static LogModel ParseLog(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        string? opened = null;
        var loads = new List<Load> { new(0, "(session start, before any scenario load)", 1, false, [], []) };
        var block = new List<(int Line, string Text)>();
        void Flush()
        {
            if (block.Count == 0) return;
            var first = block[0].Text;
            var load = loads[^1];
            if (Loading().Match(first) is { Success: true } m)
                loads.Add(load = new(loads.Count, m.Groups["file"].Value.Trim(), block[0].Line, false, [], []));
            if (first.Contains("entering mode: 15 (Playtest)", StringComparison.Ordinal) && !load.Playtest)
                loads[^1] = load = load with { Playtest = true };
            if (block.Any(b => b.Text.Contains("XS: Error", StringComparison.Ordinal)))
                AddDiagnostic(load, block);
            else if (first.Contains("AI Error Encountered", StringComparison.Ordinal))
                load.OtherErrors.Add(string.Join("\n", block.Select(b => b.Text)).Trim());
            block.Clear();
        }
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (opened is null && Opened().Match(line) is { Success: true } o) opened = o.Groups["when"].Value.Trim();
            if (EntryPrefix().IsMatch(line) || line.StartsWith("====", StringComparison.Ordinal) || line.Length == 0) Flush();
            if (line.Length > 0 && !line.StartsWith("====", StringComparison.Ordinal)) block.Add((i + 1, line));
        }
        Flush();
        return new(opened, loads.ToArray());
    }

    static void AddDiagnostic(Load load, List<(int Line, string Text)> block)
    {
        var chain = new List<string>();
        Match? root = null;
        string? scopeKind = null, scopeName = null, compiled = null;
        foreach (var (_, text) in block)
        {
            var body = text.Contains("Description: ", StringComparison.Ordinal) ? text[(text.IndexOf("Description: ", StringComparison.Ordinal) + 13)..] : text;
            if (Located().Match(body) is { Success: true } m)
            {
                chain.Add(body.Trim());
                if (root is null || (!root.Groups["code"].Success && m.Groups["code"].Success)) root = m;
            }
            else if (body.Contains("XS: Error", StringComparison.Ordinal)) chain.Add(body.Trim());
            if (Scope().Match(body) is { Success: true } s) { scopeKind = s.Groups["kind"].Value; scopeName = s.Groups["name"].Value; }
            if (CompileFailed().Match(body) is { Success: true } c) compiled = c.Groups["file"].Value;
        }
        var player = Player().Match(block[0].Text) is { Success: true } p ? int.Parse(p.Groups["player"].Value, CultureInfo.InvariantCulture) : (int?)null;
        string file = root?.Groups["file"].Value.Trim() ?? compiled ?? "(unknown)";
        var line = root is null ? 0 : int.Parse(root.Groups["line"].Value, CultureInfo.InvariantCulture);
        var code = root?.Groups["code"].Success == true ? root.Groups["code"].Value : null;
        var message = root?.Groups["msg"].Value.Trim() ?? chain.FirstOrDefault() ?? "XS error";
        // The game repeats one include error once per AI player compiling that include; merge by location/code/message.
        var existing = load.Diagnostics.FirstOrDefault(d => d.File.Equals(file, StringComparison.OrdinalIgnoreCase) && d.Line == line && d.Code == code && d.Message == message);
        if (existing is null)
            load.Diagnostics.Add(existing = new(file, line, code, message, scopeKind, scopeName, chain.ToArray(), new(StringComparer.OrdinalIgnoreCase), [], block[0].Line) { Occurrences = 0 });
        existing.Occurrences++;
        if (compiled is not null) existing.CompiledFiles.Add(compiled);
        if (player is not null) existing.Players.Add(player.Value);
    }

    internal static object Query(JsonElement args, string exe)
    {
        var operation = args.TryGetProperty("operation", out var o) ? o.GetString()! : "errors";
        return operation == "trigtemp" ? Trigtemp(args, exe) : Errors(args, exe);
    }

    static object Errors(JsonElement args, string exe)
    {
        var path = args.TryGetProperty("logPath", out var lp) ? LogPath(lp.GetString()!) : EditorFiles.LocalPath(DefaultLogPath());
        var (bytes, written) = StableRead(path, MaxLogBytes);
        var model = ParseLog(Decode(bytes));
        var offset = args.TryGetProperty("offset", out var off) ? off.GetInt32() : 0;
        var limit = args.TryGetProperty("limit", out var lim) ? lim.GetInt32() : 20;
        var context = args.TryGetProperty("context", out var ctx) ? ctx.GetInt32() : 3;
        var latestWithErrors = model.Loads.LastOrDefault(l => l.Diagnostics.Count > 0 || l.OtherErrors.Count > 0);
        Load[] selected;
        string selection;
        var mode = args.TryGetProperty("load", out var load) ? load.GetString() : "latest";
        if (!args.TryGetProperty("loadIndex", out var loadIndex) && mode == "latest")
            (selected, selection) = ([model.Loads[^1]], "latest");
        else if (mode == "latestWithErrors")
            (selected, selection) = (latestWithErrors is null ? [] : [latestWithErrors], "latestWithErrors");
        else if (mode == "all")
            (selected, selection) = (model.Loads, "all");
        else
        {
            var index = loadIndex.GetInt32();
            if (index < 0 || index >= model.Loads.Length)
                throw new ArgumentException($"load {index} outside 0..{model.Loads.Length - 1} (see loads[] in a default call).");
            (selected, selection) = ([model.Loads[index]], index.ToString(CultureInfo.InvariantCulture));
        }
        var diagnostics = selected.SelectMany(l => l.Diagnostics.Select(d => (Load: l.Index, D: d))).ToArray();
        var aiRoot = AiScripts.SafeProfileRoot();
        var stockAiRoot = SafeAiRoot(exe);
        var triggerRoot = SafeTriggerRoot(args);
        var page = diagnostics.Skip(offset).Take(limit).Select(x => new
        {
            load = x.Load, file = x.D.File, line = x.D.Line, code = x.D.Code, message = x.D.Message,
            scope = x.D.ScopeName is null ? null : new { kind = x.D.ScopeKind, name = x.D.ScopeName },
            chain = x.D.Chain, compiledFiles = x.D.CompiledFiles.ToArray(), players = x.D.Players.ToArray(),
            occurrences = x.D.Occurrences, logLine = x.D.LogLine,
            hint = Hint(x.D.Code),
            source = Source(x.D.File, x.D.Line, context, aiRoot, stockAiRoot, triggerRoot, written),
        }).ToArray();
        return new
        {
            logPath = path, logSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)), logBytes = bytes.Length,
            logLastWriteUtc = written.ToString("O", CultureInfo.InvariantCulture), sessionOpenedAt = model.OpenedAt,
            loads = model.Loads.Select(l => new { index = l.Index, file = l.File, logLine = l.StartLine, playtest = l.Playtest,
                diagnostics = l.Diagnostics.Count, otherAiErrors = l.OtherErrors.Count }).ToArray(),
            latestLoadWithErrors = latestWithErrors?.Index,
            selection, total = diagnostics.Length, offset, limit,
            nextOffset = offset + page.Length < diagnostics.Length ? (int?)(offset + page.Length) : null,
            diagnostics = page,
            otherAiErrors = selected.SelectMany(l => l.OtherErrors).Take(20).ToArray(),
            limitation = "Game-written log of the current game session (one MythLog per launch; lines carry no wall-clock time, so loads[] separates scenario loads). "
                + "Zero diagnostics in a load does not prove scripts compiled: AI scripts only compile when their player's AI loads. "
                + "source.modifiedAfterLog=true means the file changed after the log was last written; the excerpt may not match the logged error.",
        };
    }

    // Short, host-written explanations of codes seen in campaign sessions (not official engine docs).
    static string? Hint(string? code) => code switch
    {
        "0172" => "Unexpected identifier where an operator was expected; often a variable that is not declared (or not yet declared) at this point, e.g. an extern from an include used before/without that include.",
        "0310" => "Symbol not found; declare it, include the file that declares it before use, or check spelling (editor_xs_api names=[...]).",
        "0135" or "0165" => "Expression in a rule/condition could not be parsed; check the identifiers it uses are declared before the rule.",
        "0139" => "for-loop header could not be parsed; XS for loops take the form for (i = a; < b) and need a declared counter.",
        _ => null,
    };

    static string? SafeAiRoot(string exe)
    {
        try { return AiScripts.InstalledRoot(exe); } catch (ArgumentException) { return null; }
    }

    static string? SafeTriggerRoot(JsonElement args)
    {
        try { return EditorFiles.ProfileDirectory(args, "trigger"); }
        catch (Exception e) when (e is ArgumentException or InvalidDataException) { return null; }
    }

    static object Source(string file, int line, int context, string? aiRoot, string? stockAiRoot, string? triggerRoot, DateTime logWritten)
    {
        foreach (var (root, kind) in new[] { (aiRoot, "ai"), (stockAiRoot, "stockAi"), (triggerRoot, "trigger") })
        {
            if (root is null) continue;
            string path;
            try { path = AiScripts.Contained(root, AiScripts.Relative(file)); }
            catch (ArgumentException) { continue; }
            if (!File.Exists(path)) continue;
            var info = new FileInfo(path);
            if (info.Length > MaxSourceBytes) return new { path, root = kind, resolved = true, excerpt = Array.Empty<object>(), note = "Source exceeds host excerpt bound." };
            var lines = Decode(File.ReadAllBytes(path)).Replace("\r\n", "\n").Split('\n');
            var excerpt = line <= 0 ? [] : Enumerable.Range(Math.Max(1, line - context), Math.Min(lines.Length, line + context) - Math.Max(1, line - context) + 1)
                .Select(n => (object)new { line = n, text = lines[n - 1].TrimEnd('\r'), error = n == line }).ToArray();
            return new { path, root = kind, resolved = true, modifiedAfterLog = info.LastWriteTimeUtc > logWritten,
                lastWriteUtc = info.LastWriteTimeUtc.ToString("O", CultureInfo.InvariantCulture), excerpt };
        }
        return new { path = (string?)null, root = (string?)null, resolved = false, note = "Not found under active profile Game\\AI, INSTALLPATH\\game\\ai or the active profile trigger directory." };
    }

    internal sealed record Rule(string Name, int Line, int EndLine, string[] Flags);
    internal sealed record Compiled(string[] Lines, Rule[] Rules, Dictionary<int, string> Events, string[] Includes);

    internal static Compiled ParseTrigtemp(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var rules = new List<Rule>();
        var events = new Dictionary<int, string>();
        var includes = new List<string>();
        int? pendingEvent = null;
        for (var i = 0; i < lines.Length; i++)
        {
            if (IncludeLine().Match(lines[i]) is { Success: true } inc) includes.Add(inc.Groups["path"].Value);
            if (CaseLine().Match(lines[i]) is { Success: true } c) pendingEvent = int.Parse(c.Groups["event"].Value, CultureInfo.InvariantCulture);
            else if (pendingEvent is not null && EnableRule().Match(lines[i]) is { Success: true } en) { events.TryAdd(pendingEvent.Value, en.Groups["rule"].Value); pendingEvent = null; }
            var m = RuleLine().Match(lines[i].TrimEnd());
            if (!m.Success) continue;
            var flags = new List<string>();
            var j = i + 1;
            for (; j < lines.Length && !lines[j].TrimStart().StartsWith('{'); j++)
                if (lines[j].Trim().Length > 0) flags.Add(lines[j].Trim());
            var depth = 0; var end = j;
            for (; end < lines.Length; end++)
            {
                depth += lines[end].Count(ch => ch == '{') - lines[end].Count(ch => ch == '}');
                if (depth <= 0 && end > j - 1 && lines[end].Contains('}')) break;
            }
            rules.Add(new(m.Groups["name"].Value, i + 1, Math.Min(end + 1, lines.Length), flags.ToArray()));
            i = Math.Max(i, end);
        }
        return new(lines, rules.ToArray(), events, includes.ToArray());
    }

    /// <summary>Generated rule name the game uses for a trigger: leading underscore, non-identifier characters as underscores.</summary>
    internal static string RuleName(string triggerName) =>
        "_" + new string(triggerName.Select(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_' ? ch : '_').ToArray());

    static object Trigtemp(JsonElement args, string exe)
    {
        var directory = EditorFiles.ProfileDirectory(args, "trigger");
        var path = EditorFiles.LocalPath(Path.Combine(directory, "trigtemp.xs"));
        var (bytes, written) = StableRead(path, MaxTrigtempBytes);
        var compiled = ParseTrigtemp(Decode(bytes));
        var byRule = compiled.Rules.ToDictionary(r => r.Name, StringComparer.Ordinal);
        Dictionary<string, (uint Id, string Name, bool Active)>? triggers = null;
        string[] ambiguous = [], missingRules = [];
        object? export = null;
        if (args.TryGetProperty("triggerPath", out var tp))
        {
            var doc = CampaignTriggers.Read(EditorFiles.LocalPath(tp.GetString()!));
            if (!doc.Sha256.Equals(args.GetProperty("expectedSha256").GetString(), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("expectedSha256 does not match triggerPath; re-read the export hash.");
            var grouped = doc.Triggers.GroupBy(t => RuleName(t.Name), StringComparer.Ordinal).ToArray();
            ambiguous = grouped.Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
            triggers = grouped.Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => (g.Single().Id, g.Single().Name, g.Single().Flags[1] != 0), StringComparer.Ordinal);
            missingRules = triggers.Keys.Where(k => !byRule.ContainsKey(k)).Order(StringComparer.Ordinal).ToArray();
            var exportWritten = File.GetLastWriteTimeUtc(EditorFiles.LocalPath(tp.GetString()!));
            export = new { path = tp.GetString(), sha256 = doc.Sha256, triggers = doc.Triggers.Length,
                lastWriteUtc = exportWritten.ToString("O", CultureInfo.InvariantCulture),
                possiblyStale = exportWritten > written || missingRules.Length > 0,
                unmappedTriggers = missingRules.Length, unmappedTriggerSample = missingRules.Take(10).ToArray(),
                ambiguousRuleNames = ambiguous };
        }
        object Describe(Rule r) => new
        {
            name = r.Name, line = r.Line, endLine = r.EndLine, flags = r.Flags,
            trigger = triggers is not null && triggers.TryGetValue(r.Name, out var t) ? new { id = t.Id, name = t.Name, active = t.Active } : null,
            enabledByEvents = compiled.Events.Where(e => e.Value == r.Name).Select(e => e.Key).Order().ToArray(),
        };
        var header = new
        {
            path, sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes = bytes.Length,
            lastWriteUtc = written.ToString("O", CultureInfo.InvariantCulture), lineCount = compiled.Lines.Length,
            rules = compiled.Rules.Length, eventHandlers = compiled.Events.Count, includes = compiled.Includes, export,
        };
        const string limitation = "trigtemp.xs is the game's output of its LAST trigger compile (playtest/test/save), not the current editor state. "
            + "Rule names are '_' + trigger name with non-identifier characters as '_'; mapping uses the supplied hash-pinned export.";
        Rule Find(string wanted)
        {
            if (byRule.TryGetValue(wanted, out var direct)) return direct;
            if (uint.TryParse(wanted, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && triggers is not null)
            {
                var hit = triggers.FirstOrDefault(t => t.Value.Id == id);
                if (hit.Key is not null && byRule.TryGetValue(hit.Key, out var byId)) return byId;
            }
            if (byRule.TryGetValue(RuleName(wanted), out var byName)) return byName;
            var suggestions = GameDataCatalog.Suggest(RuleName(wanted), byRule.Keys.ToArray());
            throw new ArgumentException($"No compiled rule for trigger '{wanted}'" + (uint.TryParse(wanted, out _) && triggers is null ? " (numeric IDs need triggerPath+expectedSha256)" : "")
                + (suggestions.Length > 0 ? ". Did you mean: " + string.Join(", ", suggestions) + "?" : ". The trigger may be newer than the last compile; playtest to regenerate trigtemp.xs."));
        }
        object Body(Rule r)
        {
            var bodyOffset = args.TryGetProperty("bodyOffset", out var bo) ? bo.GetInt32() : 0;
            var maxLines = args.TryGetProperty("maxLines", out var ml) ? ml.GetInt32() : 200;
            var total = r.EndLine - r.Line + 1;
            var lines = Enumerable.Range(r.Line + bodyOffset, Math.Max(0, Math.Min(maxLines, total - bodyOffset)))
                .Select(n => new { line = n, text = compiled.Lines[n - 1].TrimEnd('\r') }).ToArray();
            return new { totalLines = total, bodyOffset, nextBodyOffset = bodyOffset + lines.Length < total ? (int?)(bodyOffset + lines.Length) : null, lines };
        }
        if (args.TryGetProperty("trigger", out var trigger))
        {
            var r = Find(trigger.GetString()!);
            return new { header, rule = Describe(r), body = Body(r), limitation };
        }
        if (args.TryGetProperty("eventId", out var ev))
        {
            var id = ev.GetInt32();
            if (!compiled.Events.TryGetValue(id, out var name))
                throw new ArgumentException($"Event {id} is not handled in the last compiled trigger script ({compiled.Events.Count} handled events).");
            var r = byRule.GetValueOrDefault(name);
            return new { header, eventId = id, enablesRule = name, rule = r is null ? null : Describe(r), limitation };
        }
        if (args.TryGetProperty("line", out var ln))
        {
            var line = ln.GetInt32();
            if (line < 1 || line > compiled.Lines.Length) throw new ArgumentException($"line {line} outside 1..{compiled.Lines.Length}.");
            var r = compiled.Rules.FirstOrDefault(x => x.Line <= line && line <= x.EndLine);
            var from = Math.Max(1, line - 3); var to = Math.Min(compiled.Lines.Length, line + 3);
            return new { header, line, text = compiled.Lines[line - 1].TrimEnd('\r'), rule = r is null ? null : Describe(r),
                context = Enumerable.Range(from, to - from + 1).Select(n => new { line = n, text = compiled.Lines[n - 1].TrimEnd('\r') }).ToArray(),
                region = r is null ? (line < (compiled.Rules.FirstOrDefault()?.Line ?? int.MaxValue) ? "main/eventHandler preamble" : "outside rules (include or functions)") : "rule",
                limitation };
        }
        var filter = args.TryGetProperty("filter", out var f) ? f.GetString() ?? "" : "";
        var offset = args.TryGetProperty("offset", out var off) ? off.GetInt32() : 0;
        var limit = args.TryGetProperty("limit", out var lim) ? lim.GetInt32() : 20;
        var matches = compiled.Rules.Where(r => r.Name.Contains(filter.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase)).ToArray();
        return new { header, total = matches.Length, offset, limit,
            nextOffset = offset + limit < matches.Length ? (int?)(offset + limit) : null,
            rules = matches.Skip(offset).Take(limit).Select(Describe).ToArray(), limitation };
    }

    internal static void SelfTest()
    {
        // Synthetic log reproducing the four error shapes observed in a campaign session (names invented).
        const string log = """
            ===============================================================
            File 'MythLog.txt' opened at Sat Oct 10 23:29:04 2026

            ===============================================================

            PreGame  1866:  ************* Memory Report ***************
            39250    00:00:39:  ModeTrack - entering mode: 14 (Editor) from mode: 1 (Pregame)
            3250     00:00:03:  Loading game file: demo\demo.mythscn
            3250     00:00:03:  AI Error Encountered. Title: Player #-1 (????): AI Error. Description: campaign\demo\shared.xs(162) : XS: Error 0172: 'gWave' is not a valid operator.
            campaign\demo\shared.xs(162) : XS: Error: parseExpression failed.
            campaign\demo\shared.xs(162) : XS: Error 0139: parseForLoop failed.
            campaign\demo\shared.xs(162) : XS: Error 0047: could not parse the code for 'addWaveUnit' function.
            XS: Error: could not compile file 'campaign\demo\p3.xs'.
            3250     00:00:03:  AI Error Encountered. Title: Player #-1 (????): AI Error. Description: campaign\demo\shared.xs(162) : XS: Error 0172: 'gWave' is not a valid operator.
            campaign\demo\shared.xs(162) : XS: Error: parseExpression failed.
            campaign\demo\shared.xs(162) : XS: Error 0047: could not parse the code for 'addWaveUnit' function.
            XS: Error: could not compile file 'campaign\demo\p4.xs'.
            2200     00:00:02:  Loading game file: demo\demo.mythscn
            2200     00:00:02:  AI Error Encountered. Title: Player #-1 (????): AI Error. Description: campaign\demo\shared.xs(161) : XS: Error 0310: invalid symbol lookup for 'gWave'.
            campaign\demo\shared.xs(161) : XS: Error 0047: could not parse the code for 'addWaveUnit' function.
            XS: Error: could not compile file 'campaign\demo\p3.xs'.
            1702     00:00:01:  Loading game file: demo\demo.mythscn
            1702     00:00:01:  AI Error Encountered. Title: Player #-1 (????): AI Error. Description: campaign\demo\p3.xs(333) : XS: Error 0172: 'gLevel' is not a valid operator.
            campaign\demo\p3.xs(333) : XS: Error 0165: high level parseExpr2 failed.
            campaign\demo\p3.xs(333) : XS: Error 0135: parseConditionDecl failed.
            campaign\demo\p3.xs(333) : XS: Error 0273: could not parse the code for 'ageUp' rule.
            XS: Error: could not compile file 'campaign\demo\p3.xs'.
            15579    00:00:15:  Loading game file: ~testing.mythscn
            15579    00:00:15:  ModeTrack - entering mode: 15 (Playtest) from mode: 9 (ScenarioGame)
            """;
        var model = ParseLog(log);
        var first = model.Loads[1].Diagnostics;
        if (model.OpenedAt != "Sat Oct 10 23:29:04 2026" || model.Loads.Length != 5
            || first.Count != 1 || first[0].Occurrences != 2 || first[0].Code != "0172" || first[0].Line != 162
            || first[0].ScopeName != "addWaveUnit" || first[0].ScopeKind != "function"
            || !first[0].CompiledFiles.SetEquals(["campaign\\demo\\p3.xs", "campaign\\demo\\p4.xs"]) || !first[0].Players.SetEquals([-1])
            || model.Loads[2].Diagnostics.Single().Code != "0310"
            || model.Loads[3].Diagnostics.Single() is not { ScopeKind: "rule", ScopeName: "ageUp", Line: 333, Code: "0172" }
            || !model.Loads[4].Playtest || model.Loads[4].Diagnostics.Count != 0 || model.Loads[4].File != "~testing.mythscn")
            throw new InvalidOperationException("MythLog XS diagnostic grouping fixture failed.");
        const string trig = """
            void main()
            {
               trEventSetHandler(185,  "eventHandler");
            }

            void eventHandler(int eventID=-1)
            {
               switch(eventID)
               {
                  case 185:
                  {
                     xsEnableRule("_Tech_Rules");
                     break;
                  }
               }
            }

            rule _Tech_Rules
            highFrequency
            inactive
            {
                  if (true) { trTechSetStatus(2, 371, 2); }
                  xsDisableRule("_Tech_Rules");
            }

            rule _Win_01_Message
            highFrequency
            active
            {
                  trChatSend(0, "x");
            }

            include "demo\demo.xs";
            """;
        var compiled = ParseTrigtemp(trig);
        if (compiled.Rules.Length != 2 || compiled.Rules[0] is not { Name: "_Tech_Rules", Line: 18, EndLine: 24 }
            || !compiled.Rules[0].Flags.SequenceEqual(["highFrequency", "inactive"]) || compiled.Rules[1].Line != 26
            || compiled.Events.GetValueOrDefault(185) != "_Tech_Rules" || compiled.Includes.Single() != "demo\\demo.xs"
            || RuleName("Win 01: Message") != "_Win_01__Message" || RuleName("Tech Rules") != "_Tech_Rules")
            throw new InvalidOperationException("trigtemp rule index fixture failed.");
    }
}
