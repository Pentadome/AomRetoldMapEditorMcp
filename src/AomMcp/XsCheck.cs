using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AomMcp;

/// <summary>
/// Heuristic, offline XS checks seeded from observed MythLog failures. NOT a compiler: findings are warnings/hints
/// and an empty result proves nothing. MythLog (editor_xs_diagnostics) stays authoritative after a playtest.
/// </summary>
internal static partial class XsCheck
{
    internal const string Tool = "editor_xs_check";
    internal const int MaxLimit = 200,
        MaxIncludes = 32,
        MaxBytes = 1_000_000;

    internal sealed record Finding(
        string File,
        int Line,
        int Col,
        string Rule,
        string Severity,
        string Message,
        string? MythLogCode = null,
        object? Detail = null
    );

    static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "if",
        "else",
        "elif",
        "for",
        "while",
        "switch",
        "case",
        "break",
        "continue",
        "return",
        "rule",
        "include",
        "void",
        "int",
        "float",
        "bool",
        "string",
        "vector",
        "const",
        "extern",
        "static",
        "mutable",
        "class",
        "sizeof",
        "default",
        "active",
        "inactive",
        "highFrequency",
        "runImmediately",
        "minInterval",
        "maxInterval",
        "priority",
        "group",
        "label",
        "true",
        "false",
        "override",
        "overrides",
        "ref",
        "export",
    };

    [GeneratedRegex(@"\bfor\s*\(")]
    private static partial Regex ForStart();

    [GeneratedRegex(@"\brule\s+(?<name>\w+)(?<attrs>[^{;]*)\{")]
    private static partial Regex RuleHeader();

    [GeneratedRegex(@"(?<!\.)\b(?<name>[A-Za-z_]\w*)\s*\(")]
    private static partial Regex Call();

    [GeneratedRegex(@"^\s*include\s+""(?<path>[^""]+)""", RegexOptions.Multiline)]
    private static partial Regex IncludeLine();

    [GeneratedRegex(@"(?<!\d)[A-Za-z_]\w*\.[A-Za-z_]\w*(?!\w|\s*\()")]
    private static partial Regex MemberAccess();

    internal static readonly string[] ContextModes = ["auto", "ai", "trigger"];

    internal static void Preflight(JsonElement args)
    {
        Catalog.ValidateObject(
            args,
            ["path", "scenarioPath", "expectedSha256", "context", "offset", "limit"]
        );
        var file = args.TryGetProperty("path", out var p);
        var scenario = args.TryGetProperty("scenarioPath", out var s);
        if (file == scenario)
            throw new ArgumentException(
                "Pass exactly one of path (.xs file) or scenarioPath (.mythscn or \"latest\")."
            );
        if (file && args.TryGetProperty("expectedSha256", out _))
            throw new ArgumentException(
                "expectedSha256 pins scenarioPath; file mode reports the .xs hash instead."
            );
        if (scenario && args.TryGetProperty("context", out _))
            throw new ArgumentException(
                "Scenario mode derives context per file (player AI = ai, TR XS scripts = trigger)."
            );
        if (
            args.TryGetProperty("expectedSha256", out var e)
            && (e.GetString() is not { Length: 64 } h || !h.All(Uri.IsHexDigit))
        )
            throw new ArgumentException("expectedSha256 must have 64 hexadecimal digits.");
        if (file)
            _ = Resolve(p.GetString()!);
        else if (
            s.GetString() is not { Length: > 0 } sp
            || sp != "latest" && !sp.EndsWith(".mythscn", StringComparison.OrdinalIgnoreCase)
        )
            throw new ArgumentException("scenarioPath must be a .mythscn file or \"latest\".");
    }

    static string Resolve(string path)
    {
        var full = EditorFiles.LocalPath(path);
        if (!full.EndsWith(".xs", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("path must be an .xs file.");
        var info = new FileInfo(full);
        if (!info.Exists || info.Length is < 1 or > MaxBytes)
            throw new ArgumentException("XS file missing, empty or over 1 MB.");
        return full;
    }

    // Blank string-literal contents (same length) so quoted text never looks like code.
    static string MaskStrings(string text)
    {
        var chars = text.ToCharArray();
        var quoted = false;
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] == '"' && (i == 0 || chars[i - 1] != '\\'))
            {
                quoted = !quoted;
                continue;
            }
            if (quoted && chars[i] != '\n')
                chars[i] = ' ';
        }
        return new string(chars);
    }

    static (int Line, int Col) Position(string text, int index)
    {
        var line = 1;
        var start = 0;
        for (var i = 0; i < index && i < text.Length; i++)
            if (text[i] == '\n')
            {
                line++;
                start = i + 1;
            }
        return (line, index - start + 1);
    }

    /// <summary>Include roots and API model shared by every closure checked in one call.</summary>
    sealed class Env(string exe)
    {
        internal readonly string? ProfileAi = AiScripts.SafeProfileRoot();
        internal readonly string? ProfileTrigger =
            ScenarioFiles.ProfileHome() is { } home
            && Directory.Exists(Path.Combine(home, "trigger"))
                ? EditorFiles.LocalPath(Path.Combine(home, "trigger"))
                : null;
        internal readonly string StockAi = AiScripts.InstalledRoot(exe);
        internal readonly Func<string, bool> StockTrigger = StockTriggers(exe);
        internal readonly XsApi.Model? Model = LoadModel();
        internal readonly Dictionary<string, string[]> Candidates = new(
            StringComparer.OrdinalIgnoreCase
        );

        static Func<string, bool> StockTriggers(string exe)
        {
            try
            {
                return ScenarioDependencies.StockTriggers(exe);
            }
            catch (Exception e)
                when (e
                        is IOException
                            or InvalidDataException
                            or UnauthorizedAccessException
                            or FileNotFoundException
                )
            {
                return _ => false;
            }
        }

        static XsApi.Model? LoadModel()
        {
            try
            {
                return XsApi.LoadModel();
            }
            catch (Exception e)
                when (e
                        is IOException
                            or FileNotFoundException
                            or JsonException
                            or InvalidOperationException
                )
            {
                return null;
            }
        }
    }

    // auto: a path segment trigger\ (profile or mod trigger folder) = trigger context; game\ai = ai context.
    static string DetectContext(string path) =>
        path.Contains(@"\trigger\", StringComparison.OrdinalIgnoreCase) ? "trigger"
        : path.Contains(@"\game\ai\", StringComparison.OrdinalIgnoreCase) ? "ai"
        : "unknown";

    // Folder that include names are relative to: nearest ancestor named trigger (trigger context) or ai under game (ai context).
    static string? OwnRoot(string path, string context)
    {
        for (
            var dir = Path.GetDirectoryName(path);
            dir is not null;
            dir = Path.GetDirectoryName(dir)
        )
        {
            var name = Path.GetFileName(dir);
            if (context == "trigger" && name.Equals("trigger", StringComparison.OrdinalIgnoreCase))
                return dir;
            if (
                context != "trigger"
                && name.Equals("ai", StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(Path.GetDirectoryName(dir) ?? "")
                    .Equals("game", StringComparison.OrdinalIgnoreCase)
            )
                return dir;
        }
        return null;
    }

    static string[] IncludeRoots(string? path, string context, Env env)
    {
        var roots = new List<string>();
        if (path is not null && OwnRoot(path, context) is { } own)
            roots.Add(own);
        if (context == "trigger")
        {
            if (env.ProfileTrigger is not null)
                roots.Add(env.ProfileTrigger);
        }
        else
        {
            if (env.ProfileAi is not null)
                roots.Add(env.ProfileAi);
            roots.Add(env.StockAi);
        }
        return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    // Closest existing .xs names (relative to each tried root) for a missing include/script; bounded directory walk.
    static string[] Suggestions(string relative, IEnumerable<string> roots, Env env)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (!env.Candidates.TryGetValue(root, out var found))
            {
                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint,
                    MaxRecursionDepth = 6,
                };
                found = Directory.Exists(root)
                    ? Directory
                        .EnumerateFiles(root, "*.xs", options)
                        .Take(5000)
                        .Select(f => Path.GetRelativePath(root, f))
                        .ToArray()
                    : [];
                env.Candidates[root] = found;
            }
            names.UnionWith(found);
        }
        var byPath = GameDataCatalog.Suggest(relative, names, 3);
        var byName = GameDataCatalog
            .Suggest(Path.GetFileName(relative), names.Select(Path.GetFileName).OfType<string>(), 3)
            .SelectMany(n =>
                names.Where(c => Path.GetFileName(c).Equals(n, StringComparison.OrdinalIgnoreCase))
            );
        return byPath.Concat(byName).Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToArray();
    }

    internal static object Query(JsonElement args, string exe)
    {
        var offset = args.TryGetProperty("offset", out var o) ? o.GetInt32() : 0;
        var limit = args.TryGetProperty("limit", out var l) ? l.GetInt32() : 50;
        var env = new Env(exe);
        var findings = new List<Finding>();
        var files = new List<string>();
        if (args.TryGetProperty("scenarioPath", out _))
            return Scenario(args, env, findings, files, offset, limit);

        var path = Resolve(args.GetProperty("path").GetString()!);
        var requested = args.TryGetProperty("context", out var c) ? c.GetString()! : "auto";
        if (!ContextModes.Contains(requested))
            throw new ArgumentException("context must be auto|ai|trigger.");
        var context = requested == "auto" ? DetectContext(path) : requested;
        var bytes = File.ReadAllBytes(path);
        CheckClosure(path, path, null, context, env, findings, files);
        return Result(
            findings,
            offset,
            limit,
            new
            {
                path,
                sha256 = CheckpointDocument.Hash(bytes),
                context,
                contextSource = requested == "auto"
                    ? (
                        context == "unknown"
                            ? "path (not under trigger\\ or game\\ai; AI include roots, no context check)"
                            : "path"
                    )
                    : "argument",
                filesChecked = files.ToArray(),
                apiModel = env.Model is null
                    ? "unavailable (run editor_generate_catalog): unknown-call, argument-count and syscall-context checks skipped"
                    : "loaded",
            }
        );
    }

    // Scenario mode: every player AI (PL aiPath) and every TR XS Scripts entry with its include closure, as the game would load them.
    static object Scenario(
        JsonElement args,
        Env env,
        List<Finding> findings,
        List<string> files,
        int offset,
        int limit
    )
    {
        var requested = args.GetProperty("scenarioPath").GetString()!;
        var path = EditorFiles.LocalPath(ScenarioFiles.Resolve(requested));
        var document = CheckpointDocument.Read(path, CheckpointDocument.Expected(args));
        var roots = new ScenarioDependencies.Roots(
            env.ProfileAi,
            env.ProfileTrigger,
            null,
            null,
            env.StockAi,
            env.StockTrigger
        );
        var report = ScenarioDependencies.Analyze(document, roots);
        var byRelative = report
            .Nodes.GroupBy(n => n.Kind + ":" + n.Relative, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        foreach (
            var n in report.Nodes.Where(n => !n.Via.StartsWith("include", StringComparison.Ordinal))
        )
        {
            if (n.From == "profile" && n.Path is not null)
                CheckClosure(n.Path, n.Path, null, n.Kind, env, findings, files);
            else if (
                n.From == "embedded"
                && report.Scripts.FirstOrDefault(s => s.Embedded && s.Name == n.Relative)
                    is { } script
            )
                CheckClosure(
                    path + "#TR:" + n.Relative,
                    null,
                    string.Join('\n', script.Lines),
                    "trigger",
                    env,
                    findings,
                    files
                );
            else if (n.From == "missing")
            {
                var tried = IncludeRoots(null, n.Kind, env);
                var suggestions = Suggestions(n.Relative, tried, env);
                findings.Add(
                    new(
                        path,
                        1,
                        1,
                        "scenario-script-missing",
                        "error",
                        $"{n.Via} '{n.Relative}' not found under {(n.Kind == "ai" ? "profile Game\\AI or INSTALLPATH\\game\\ai" : "the profile trigger folder or the stock trigger scripts")}; the game cannot load it"
                            + (
                                suggestions.Length > 0
                                    ? "; similar: " + string.Join(", ", suggestions)
                                    : ""
                            )
                            + ".",
                        null,
                        new { resolvedAgainst = tried, suggestions }
                    )
                );
            }
        }
        string From(string kind, string relative) =>
            byRelative.TryGetValue(kind + ":" + relative, out var n) ? n.From : "unchecked";
        string? SafeRelative(string aiPath)
        {
            try
            {
                return AiScripts.Relative(aiPath, appendExtension: true);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
        return Result(
            findings,
            offset,
            limit,
            new
            {
                scenarioPath = requested,
                resolvedPath = path,
                sha256 = document.Sha256,
                modifiedUtc = File.GetLastWriteTimeUtc(path)
                    .ToString("O", CultureInfo.InvariantCulture),
                readable = report.Readable,
                players = report
                    .Players.Select(p => new
                    {
                        player = p.Player,
                        name = p.Name,
                        control = p.Control,
                        aiPath = p.AiPath,
                        resolvedFrom = SafeRelative(p.AiPath) is { } r
                            ? From("ai", r)
                            : "unsafe-path",
                    })
                    .ToArray(),
                triggerScripts = report
                    .Scripts.Select(s => new
                    {
                        name = s.Name,
                        embedded = s.Embedded,
                        resolvedFrom = s.Embedded
                            ? "embedded"
                            : From("trigger", s.Name.Replace('/', '\\')),
                    })
                    .ToArray(),
                includes = report
                    .Nodes.Where(n => n.Via.StartsWith("include", StringComparison.Ordinal))
                    .Select(n => new
                    {
                        kind = n.Kind,
                        relative = n.Relative,
                        resolvedFrom = n.From,
                        via = n.Via,
                    })
                    .ToArray(),
                warnings = report
                    .Warnings.Append(
                        "Stock (install) scripts are not linted; profile files are linted with their include closure."
                    )
                    .ToArray(),
                filesChecked = files.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                apiModel = env.Model is null
                    ? "unavailable (run editor_generate_catalog): unknown-call, argument-count and syscall-context checks skipped"
                    : "loaded",
            }
        );
    }

    static object Result(List<Finding> findings, int offset, int limit, object header)
    {
        var ordered = findings
            .DistinctBy(f => (f.File.ToLowerInvariant(), f.Line, f.Col, f.Rule, f.Message))
            .OrderBy(f => f.Severity == "error" ? 0 : 1)
            .ThenBy(f => f.File, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.Line)
            .ThenBy(f => f.Col)
            .ToArray();
        return new
        {
            target = header,
            heuristic = true,
            counts = new
            {
                error = ordered.Count(f => f.Severity == "error"),
                warning = ordered.Count(f => f.Severity == "warning"),
            },
            diagnostics = ordered.Skip(offset).Take(limit).ToArray(),
            total = ordered.Length,
            nextOffset = offset + limit < ordered.Length ? offset + limit : (int?)null,
            limitation = "Heuristic pattern checks only, not an XS compiler: no findings does not mean the script compiles, and warnings may be false positives. MythLog via editor_xs_diagnostics is authoritative after the game loads the script; trigger-script compile errors show only as an in-game dialog.",
        };
    }

    // One root (file or embedded text) plus its include closure: sibling-relative, own root, then context roots.
    static void CheckClosure(
        string label,
        string? rootPath,
        string? rootText,
        string context,
        Env env,
        List<Finding> findings,
        List<string> filesChecked
    )
    {
        var files = new List<(string Path, string Text)>();
        var queue = new Queue<(string Label, string? Path, string? Text, int Depth)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roots = IncludeRoots(rootPath, context, env);
        queue.Enqueue((label, rootPath, rootText, 0));
        while (queue.Count > 0)
        {
            var (current, file, given, depth) = queue.Dequeue();
            if (!seen.Add(current))
                continue;
            var text =
                given
                ?? System
                    .Text.Encoding.UTF8.GetString(File.ReadAllBytes(file!))
                    .TrimStart('\uFEFF');
            string stripped;
            try
            {
                stripped = AiInstaller.StripComments(text);
            }
            catch (InvalidDataException e)
            {
                findings.Add(new(current, 1, 1, "unterminated", "error", e.Message));
                stripped = text;
            }
            files.Add((current, MaskStrings(stripped)));
            filesChecked.Add(current);
            foreach (Match m in IncludeLine().Matches(stripped))
            {
                var rel = m.Groups["path"].Value.Replace('/', '\\');
                string? found = null;
                var tried = new List<string>();
                var safe =
                    !Path.IsPathRooted(rel)
                    && !rel.Contains(':')
                    && rel.Split('\\').All(s => s is not ("" or "." or ".."));
                if (safe)
                {
                    if (file is not null)
                        tried.Add(Path.GetDirectoryName(file)!);
                    tried.AddRange(roots);
                    found = tried
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Select(r => Path.Combine(r, rel))
                        .FirstOrDefault(File.Exists);
                }
                if (found is null && safe && context == "trigger" && env.StockTrigger(rel))
                    continue; // Stock trigger script (Data.bar), not walked.
                if (found is null || new FileInfo(found).Length > MaxBytes)
                {
                    var (ln, col) = Position(stripped, m.Index);
                    var resolvedAgainst = tried
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    var suggestions = safe ? Suggestions(rel, resolvedAgainst, env) : [];
                    findings.Add(
                        new(
                            current,
                            ln,
                            col,
                            "include-not-found",
                            "warning",
                            $"include \"{m.Groups["path"].Value}\" not found ({(found is null ? "tried " + string.Join("; ", resolvedAgainst) : "over 1 MB")})"
                                + (
                                    suggestions.Length > 0
                                        ? "; similar: " + string.Join(", ", suggestions)
                                        : ""
                                )
                                + ".",
                            null,
                            new { resolvedAgainst, suggestions }
                        )
                    );
                }
                else if (depth < 8 && seen.Count + queue.Count < MaxIncludes)
                    queue.Enqueue(
                        (
                            EditorFiles.LocalPath(found),
                            EditorFiles.LocalPath(found),
                            null,
                            depth + 1
                        )
                    );
            }
        }

        // Names declared in the file set.
        var model = env.Model;
        var declared = new HashSet<string>(StringComparer.Ordinal);
        var localFunctions = new Dictionary<string, XsApi.Function>(StringComparer.Ordinal);
        foreach (var f in files)
        {
            var parsed = XsApi.Parse(f.Text);
            foreach (var fn in parsed.Functions)
            {
                declared.Add(fn.Name);
                localFunctions[fn.Name] = fn;
            }
            foreach (var c in parsed.Classes)
                declared.Add(c.Name);
            foreach (var g in parsed.Globals)
                declared.Add(g.Name);
            foreach (var r in parsed.Rules)
                declared.Add(r.Name);
        }
        var syscalls = model
            ?.Syscalls.GroupBy(s => s.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var modelFunctions = model
            ?.Functions.Where(fn => fn.Class is null)
            .GroupBy(fn => fn.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var known = new HashSet<string>(declared, StringComparer.Ordinal);
        if (syscalls is not null)
            known.UnionWith(syscalls.Keys);
        if (modelFunctions is not null)
            known.UnionWith(modelFunctions.Keys);
        if (model is not null)
            foreach (var c in model.Classes)
                known.Add(c.Name);

        foreach (var (file, text) in files)
        {
            CheckBraces(file, text, findings);
            CheckFor(file, text, findings);
            foreach (Match m in RuleHeader().Matches(text))
            {
                var attrs = m.Groups["attrs"].Value;
                if (!Regex.IsMatch(attrs, @"\b(active|inactive)\b"))
                {
                    var (ln, col) = Position(text, m.Index);
                    findings.Add(
                        new(
                            file,
                            ln,
                            col,
                            "rule-no-state",
                            "warning",
                            $"rule {m.Groups["name"].Value} has neither active nor inactive; XS rules start inactive unless enabled with xsEnableRule."
                        )
                    );
                }
            }
            CheckCalls(
                file,
                text,
                known,
                localFunctions,
                syscalls,
                modelFunctions,
                context,
                findings
            );
        }
    }

    static void CheckBraces(string file, string text, List<Finding> findings)
    {
        var stack = new Stack<(char Ch, int Index)>();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"' && (i == 0 || text[i - 1] != '\\'))
                quoted = !quoted;
            if (quoted)
                continue;
            if (c is '{' or '(')
                stack.Push((c, i));
            else if (c is '}' or ')')
            {
                var want = c == '}' ? '{' : '(';
                if (stack.Count == 0 || stack.Peek().Ch != want)
                {
                    var (ln, col) = Position(text, i);
                    findings.Add(new(file, ln, col, "unbalanced", "error", $"unmatched '{c}'."));
                    return;
                }
                stack.Pop();
            }
        }
        if (stack.Count > 0)
        {
            var (ch, index) = stack.Peek();
            var (ln, col) = Position(text, index);
            findings.Add(new(file, ln, col, "unbalanced", "error", $"'{ch}' is never closed."));
        }
    }

    static void CheckFor(string file, string text, List<Finding> findings)
    {
        foreach (Match m in ForStart().Matches(text))
        {
            var start = m.Index + m.Length;
            var depth = 1;
            var semis = new List<int>();
            var end = -1;
            for (var i = start; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '(')
                    depth++;
                else if (c == ')' && --depth == 0)
                {
                    end = i;
                    break;
                }
                else if (c == ';' && depth == 1)
                    semis.Add(i);
            }
            if (end < 0)
                continue;
            if (semis.Count != 2)
            {
                var (fl, fc) = Position(text, m.Index);
                findings.Add(
                    new(
                        file,
                        fl,
                        fc,
                        "for-loop-form",
                        "error",
                        $"for-loop header has {semis.Count} top-level ';' but Retold XS needs the C-style form for (init; condition; step), e.g. for (int i = 0; i < n; i++); the legacy for (i = 0; < n) form fails to compile (MythLog 0172/0139); every shipped stock loop is C-style."
                            + (
                                Rewrite(text[start..end], semis.Select(i => i - start).ToArray())
                                    is { } fix
                                    ? " Rewrite: " + fix
                                    : ""
                            ),
                        "0172/0139"
                    )
                );
                continue;
            }
            var condition = text[(semis[0] + 1)..semis[1]];
            var member = MemberAccess().Match(condition);
            if (member.Success)
            {
                var (ln, col) = Position(text, semis[0] + 1 + member.Index);
                findings.Add(
                    new(
                        file,
                        ln,
                        col,
                        "for-condition-member-access",
                        "warning",
                        $"member access '{member.Value}' in a for-loop condition; consider copying it to a local before the loop (one scenario script failed to compile with this shape; stock scripts also use it, so this may be a false positive).",
                        "0172"
                    )
                );
            }
        }
    }

    // Legacy "init; <op bound" header → C-style suggestion; null when the shape is not that simple.
    static string? Rewrite(string header, int[] semis)
    {
        if (semis.Length != 1)
            return null;
        var init = header[..semis[0]].Trim();
        var rest = header[(semis[0] + 1)..].Trim();
        var variable = Regex.Match(init, @"(\w+)\s*=");
        var op = Regex.Match(rest, @"^(<=|>=|<|>)\s*(.+)$");
        if (!variable.Success || !op.Success)
            return null;
        var v = variable.Groups[1].Value;
        return $"for ({init}; {v} {op.Groups[1].Value} {op.Groups[2].Value.Trim()}; {v}{(op.Groups[1].Value.StartsWith('>') ? "--" : "++")})";
    }

    static void CheckCalls(
        string file,
        string text,
        HashSet<string> known,
        Dictionary<string, XsApi.Function> local,
        Dictionary<string, XsApi.Syscall>? syscalls,
        Dictionary<string, XsApi.Function>? modelFunctions,
        string context,
        List<Finding> findings
    )
    {
        foreach (Match m in Call().Matches(text))
        {
            var name = m.Groups["name"].Value;
            if (Keywords.Contains(name))
                continue;
            // Declarations: a type/modifier word immediately before the name.
            var before = text[..m.Index].TrimEnd();
            var prev = Regex.Match(before, @"(\w+)$").Groups[1].Value;
            if (
                prev.Length > 0 && !Keywords.Contains(prev)
                || prev
                    is "void"
                        or "int"
                        or "float"
                        or "bool"
                        or "string"
                        or "vector"
                        or "mutable"
                        or "extern"
                        or "static"
                        or "rule"
                        or "class"
            )
                continue;
            var (line, col) = Position(text, m.Index);
            var open = m.Index + m.Length - 1;
            var args = CountArgs(text, open);
            if (args < 0)
                continue;
            int? min = null,
                max = null;
            string? signature = null;
            if (local.TryGetValue(name, out var fn))
            {
                min = fn.Params.Count(p => p.Default is null);
                max = fn.Params.Length;
                signature = fn.Signature;
            }
            else if (syscalls is not null && syscalls.TryGetValue(name, out var sys))
            {
                min = sys.Params.Count(p => p.Default is null);
                max = sys.Params.Length;
                signature = sys.Signature;
            }
            else if (modelFunctions is not null && modelFunctions.TryGetValue(name, out var mf))
            {
                min = mf.Params.Count(p => p.Default is null);
                max = mf.Params.Length;
                signature = mf.Signature;
            }
            if (
                context is "ai" or "trigger"
                && !local.ContainsKey(name)
                && syscalls is not null
                && syscalls.TryGetValue(name, out var scoped)
                && scoped.Contexts.Length > 0
                && !scoped.Contexts.Contains(context)
            )
                findings.Add(
                    new(
                        file,
                        line,
                        col,
                        "syscall-wrong-context",
                        "warning",
                        $"{name} is a {string.Join('/', scoped.Contexts)} syscall ({scoped.Group}); this file is checked as a {context} script, where it is not available (context from "
                            + "BANG_Documentation families; pass context=ai|trigger if the detection is wrong)."
                    )
                );
            if (min is not null && (args < min || args > max))
            {
                findings.Add(
                    new(
                        file,
                        line,
                        col,
                        "argument-count",
                        "warning",
                        $"{name} called with {args.ToString(CultureInfo.InvariantCulture)} argument(s); signature takes {min}..{max}: {signature}"
                    )
                );
            }
            else if (min is null && syscalls is not null && !known.Contains(name))
            {
                var suggestions = GameDataCatalog.Suggest(name, known, 3);
                findings.Add(
                    new(
                        file,
                        line,
                        col,
                        "unknown-call",
                        "warning",
                        $"'{name}' is not declared in this file, its includes or the shipped API model"
                            + (
                                suggestions.Length > 0
                                    ? "; similar: " + string.Join(", ", suggestions)
                                    : ""
                            )
                            + "."
                    )
                );
            }
        }
    }

    // Number of top-level arguments of the call whose '(' is at index; -1 if unterminated.
    static int CountArgs(string text, int open)
    {
        var depth = 0;
        var args = 0;
        var any = false;
        var quoted = false;
        for (var i = open; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"' && text[i - 1] != '\\')
            {
                quoted = !quoted;
                any = true;
                continue;
            }
            if (quoted)
                continue;
            if (c == '(')
            {
                depth++;
                continue;
            }
            if (c == ')')
            {
                if (--depth == 0)
                    return any ? args + 1 : 0;
                continue;
            }
            if (c == ',' && depth == 1)
                args++;
            else if (!char.IsWhiteSpace(c) && depth >= 1)
                any = true;
        }
        return -1;
    }

    internal static void SelfTest()
    {
        if (
            CountArgs("f(a, g(b, c), \"x,y\")", 1) != 3
            || CountArgs("f()", 1) != 0
            || CountArgs("f(", 1) != -1
        )
            throw new InvalidOperationException("XsCheck argument counting self-test failed.");
        if (Position("ab\ncd", 4) != (2, 2))
            throw new InvalidOperationException("XsCheck position self-test failed.");
        var loop = new List<Finding>();
        CheckFor(
            "t.xs",
            "void f() { for (int i = 0; < count) { } for (int j = 0; j < 3; j++) { } for (k = 5; > 0) { } }",
            loop
        );
        if (
            loop.Count != 2
            || loop.Any(f => f.Rule != "for-loop-form" || f.Severity != "error")
            || !loop[0].Message.Contains("for (int i = 0; i < count; i++)")
            || !loop[1].Message.Contains("for (k = 5; k > 0; k--)")
        )
            throw new InvalidOperationException("XsCheck for-loop-form self-test failed.");
        if (
            DetectContext(@"C:\p\trigger\om10\a.xs") != "trigger"
            || DetectContext(@"C:\p\Game\AI\x\a.xs") != "ai"
            || DetectContext(@"C:\tmp\a.xs") != "unknown"
            || OwnRoot(@"C:\m\trigger\om10\a.xs", "trigger") != @"C:\m\trigger"
            || OwnRoot(@"C:\m\game\ai\om10\a.xs", "ai") != @"C:\m\game\ai"
        )
            throw new InvalidOperationException("XsCheck context self-test failed.");
    }
}
