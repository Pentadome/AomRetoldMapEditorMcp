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
    internal const int MaxLimit = 200, MaxIncludes = 32, MaxBytes = 1_000_000;
    internal sealed record Finding(string File, int Line, int Col, string Rule, string Severity, string Message, string? MythLogCode = null);

    static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "if", "else", "elif", "for", "while", "switch", "case", "break", "continue", "return", "rule", "include", "void", "int", "float", "bool",
        "string", "vector", "const", "extern", "static", "mutable", "class", "sizeof", "default", "active", "inactive", "highFrequency", "runImmediately",
        "minInterval", "maxInterval", "priority", "group", "label", "true", "false", "override", "overrides", "ref", "export",
    };
    [GeneratedRegex(@"\bfor\s*\(")] private static partial Regex ForStart();
    [GeneratedRegex(@"\brule\s+(?<name>\w+)(?<attrs>[^{;]*)\{")] private static partial Regex RuleHeader();
    [GeneratedRegex(@"(?<!\.)\b(?<name>[A-Za-z_]\w*)\s*\(")] private static partial Regex Call();
    [GeneratedRegex(@"^\s*include\s+""(?<path>[^""]+)""", RegexOptions.Multiline)] private static partial Regex IncludeLine();
    [GeneratedRegex(@"(?<!\d)[A-Za-z_]\w*\.[A-Za-z_]\w*(?!\w|\s*\()")] private static partial Regex MemberAccess();

    internal static void Preflight(JsonElement args)
    {
        Catalog.ValidateObject(args, ["path", "offset", "limit"]);
        _ = Resolve(args.GetProperty("path").GetString()!);
    }

    static string Resolve(string path)
    {
        var full = EditorFiles.LocalPath(path);
        if (!full.EndsWith(".xs", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("path must be an .xs file.");
        var info = new FileInfo(full);
        if (!info.Exists || info.Length is < 1 or > MaxBytes) throw new ArgumentException("XS file missing, empty or over 1 MB.");
        return full;
    }

    // Blank string-literal contents (same length) so quoted text never looks like code.
    static string MaskStrings(string text)
    {
        var chars = text.ToCharArray(); var quoted = false;
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] == '"' && (i == 0 || chars[i - 1] != '\\')) { quoted = !quoted; continue; }
            if (quoted && chars[i] != '\n') chars[i] = ' ';
        }
        return new string(chars);
    }

    static (int Line, int Col) Position(string text, int index)
    {
        var line = 1; var start = 0;
        for (var i = 0; i < index && i < text.Length; i++)
            if (text[i] == '\n') { line++; start = i + 1; }
        return (line, index - start + 1);
    }

    internal static object Query(JsonElement args, string exe)
    {
        var path = Resolve(args.GetProperty("path").GetString()!);
        var offset = args.TryGetProperty("offset", out var o) ? o.GetInt32() : 0;
        var limit = args.TryGetProperty("limit", out var l) ? l.GetInt32() : 50;
        var bytes = File.ReadAllBytes(path);
        var aiRoots = AiScripts.SearchRoots(exe);
        var findings = new List<Finding>();
        XsApi.Model? model = null;
        try { model = XsApi.LoadModel(); } catch (Exception e) when (e is IOException or FileNotFoundException or JsonException or InvalidOperationException) { }

        // Include walk: sibling-relative first, then installed AI root. Missing includes are reported, not fatal.
        var files = new List<(string Path, string Text, bool Root)>();
        var queue = new Queue<(string Path, bool Root, int Depth)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        queue.Enqueue((path, true, 0));
        while (queue.Count > 0)
        {
            var (current, isRoot, depth) = queue.Dequeue();
            if (!seen.Add(current)) continue;
            var text = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(current)).TrimStart('\uFEFF');
            string stripped;
            try { stripped = AiInstaller.StripComments(text); }
            catch (InvalidDataException e) { findings.Add(new(current, 1, 1, "unterminated", "error", e.Message)); stripped = text; }
            files.Add((current, MaskStrings(stripped), isRoot));
            foreach (Match m in IncludeLine().Matches(stripped))
            {
                var rel = m.Groups["path"].Value.Replace('/', '\\');
                string? found = null;
                if (!Path.IsPathRooted(rel) && !rel.Contains(':') && rel.Split('\\').All(s => s is not ("" or "." or "..")))
                {
                    var near = Path.Combine(Path.GetDirectoryName(current)!, rel);
                    found = File.Exists(near) ? near : aiRoots.Select(r => Path.Combine(r, rel)).FirstOrDefault(File.Exists);
                }
                if (found is null || new FileInfo(found).Length > MaxBytes)
                {
                    var (ln, col) = Position(stripped, m.Index);
                    findings.Add(new(current, ln, col, "include-not-found", "warning", $"include \"{m.Groups["path"].Value}\" not found beside the file, under profile Game\\AI or under INSTALLPATH\\game\\ai."));
                }
                else if (depth < 8 && seen.Count + queue.Count < MaxIncludes) queue.Enqueue((EditorFiles.LocalPath(found), false, depth + 1));
            }
        }

        // Names declared in the file set.
        var declared = new HashSet<string>(StringComparer.Ordinal);
        var localFunctions = new Dictionary<string, XsApi.Function>(StringComparer.Ordinal);
        foreach (var f in files)
        {
            var parsed = XsApi.Parse(f.Text);
            foreach (var fn in parsed.Functions) { declared.Add(fn.Name); localFunctions[fn.Name] = fn; }
            foreach (var c in parsed.Classes) declared.Add(c.Name);
            foreach (var g in parsed.Globals) declared.Add(g.Name);
            foreach (var r in parsed.Rules) declared.Add(r.Name);
        }
        var syscalls = model?.Syscalls.GroupBy(s => s.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var modelFunctions = model?.Functions.Where(fn => fn.Class is null).GroupBy(fn => fn.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var known = new HashSet<string>(declared, StringComparer.Ordinal);
        if (syscalls is not null) known.UnionWith(syscalls.Keys);
        if (modelFunctions is not null) known.UnionWith(modelFunctions.Keys);
        if (model is not null) foreach (var c in model.Classes) known.Add(c.Name);

        foreach (var (file, text, _) in files)
        {
            CheckBraces(file, text, findings);
            CheckFor(file, text, findings);
            foreach (Match m in RuleHeader().Matches(text))
            {
                var attrs = m.Groups["attrs"].Value;
                if (!Regex.IsMatch(attrs, @"\b(active|inactive)\b"))
                {
                    var (ln, col) = Position(text, m.Index);
                    findings.Add(new(file, ln, col, "rule-no-state", "warning", $"rule {m.Groups["name"].Value} has neither active nor inactive; XS rules start inactive unless enabled with xsEnableRule."));
                }
            }
            CheckCalls(file, text, known, localFunctions, syscalls, modelFunctions, findings);
        }

        var ordered = findings.OrderBy(f => f.File, StringComparer.OrdinalIgnoreCase).ThenBy(f => f.Line).ThenBy(f => f.Col).ToArray();
        return new
        {
            path,
            sha256 = CheckpointDocument.Hash(bytes),
            heuristic = true,
            filesChecked = files.Select(f => f.Path).ToArray(),
            apiModel = model is null ? "unavailable (run editor_generate_catalog): unknown-call and argument-count checks skipped" : "loaded",
            counts = new { error = ordered.Count(f => f.Severity == "error"), warning = ordered.Count(f => f.Severity == "warning") },
            diagnostics = ordered.Skip(offset).Take(limit).ToArray(),
            total = ordered.Length,
            nextOffset = offset + limit < ordered.Length ? offset + limit : (int?)null,
            limitation = "Heuristic pattern checks only, not an XS compiler: no findings does not mean the script compiles, and warnings may be false positives. MythLog via editor_xs_diagnostics is authoritative after the game loads the script.",
        };
    }

    static void CheckBraces(string file, string text, List<Finding> findings)
    {
        var stack = new Stack<(char Ch, int Index)>();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"' && (i == 0 || text[i - 1] != '\\')) quoted = !quoted;
            if (quoted) continue;
            if (c is '{' or '(') stack.Push((c, i));
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
            var (ch, index) = stack.Peek(); var (ln, col) = Position(text, index);
            findings.Add(new(file, ln, col, "unbalanced", "error", $"'{ch}' is never closed."));
        }
    }

    static void CheckFor(string file, string text, List<Finding> findings)
    {
        foreach (Match m in ForStart().Matches(text))
        {
            var start = m.Index + m.Length; var depth = 1; var semis = new List<int>(); var end = -1;
            for (var i = start; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '(') depth++;
                else if (c == ')' && --depth == 0) { end = i; break; }
                else if (c == ';' && depth == 1) semis.Add(i);
            }
            if (end < 0 || semis.Count < 2) continue;
            var condition = text[(semis[0] + 1)..semis[1]];
            var member = MemberAccess().Match(condition);
            if (member.Success)
            {
                var (ln, col) = Position(text, semis[0] + 1 + member.Index);
                findings.Add(new(file, ln, col, "for-condition-member-access", "warning",
                    $"member access '{member.Value}' in a for-loop condition; consider copying it to a local before the loop (one scenario script failed to compile with this shape; stock scripts also use it, so this may be a false positive).", "0172"));
            }
        }
    }

    static void CheckCalls(string file, string text, HashSet<string> known, Dictionary<string, XsApi.Function> local,
        Dictionary<string, XsApi.Syscall>? syscalls, Dictionary<string, XsApi.Function>? modelFunctions, List<Finding> findings)
    {
        foreach (Match m in Call().Matches(text))
        {
            var name = m.Groups["name"].Value;
            if (Keywords.Contains(name)) continue;
            // Declarations: a type/modifier word immediately before the name.
            var before = text[..m.Index].TrimEnd();
            var prev = Regex.Match(before, @"(\w+)$").Groups[1].Value;
            if (prev.Length > 0 && !Keywords.Contains(prev) || prev is "void" or "int" or "float" or "bool" or "string" or "vector" or "mutable" or "extern" or "static" or "rule" or "class") continue;
            var (line, col) = Position(text, m.Index);
            var open = m.Index + m.Length - 1;
            var args = CountArgs(text, open);
            if (args < 0) continue;
            int? min = null, max = null; string? signature = null;
            if (local.TryGetValue(name, out var fn)) { min = fn.Params.Count(p => p.Default is null); max = fn.Params.Length; signature = fn.Signature; }
            else if (syscalls is not null && syscalls.TryGetValue(name, out var sys)) { min = sys.Params.Count(p => p.Default is null); max = sys.Params.Length; signature = sys.Signature; }
            else if (modelFunctions is not null && modelFunctions.TryGetValue(name, out var mf)) { min = mf.Params.Count(p => p.Default is null); max = mf.Params.Length; signature = mf.Signature; }
            if (min is not null && (args < min || args > max))
            {
                findings.Add(new(file, line, col, "argument-count", "warning",
                    $"{name} called with {args.ToString(CultureInfo.InvariantCulture)} argument(s); signature takes {min}..{max}: {signature}"));
            }
            else if (min is null && syscalls is not null && !known.Contains(name))
            {
                var suggestions = GameDataCatalog.Suggest(name, known, 3);
                findings.Add(new(file, line, col, "unknown-call", "warning",
                    $"'{name}' is not declared in this file, its includes or the shipped API model" + (suggestions.Length > 0 ? "; similar: " + string.Join(", ", suggestions) : "") + "."));
            }
        }
    }

    // Number of top-level arguments of the call whose '(' is at index; -1 if unterminated.
    static int CountArgs(string text, int open)
    {
        var depth = 0; var args = 0; var any = false; var quoted = false;
        for (var i = open; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"' && text[i - 1] != '\\') { quoted = !quoted; any = true; continue; }
            if (quoted) continue;
            if (c == '(') { depth++; continue; }
            if (c == ')') { if (--depth == 0) return any ? args + 1 : 0; continue; }
            if (c == ',' && depth == 1) args++;
            else if (!char.IsWhiteSpace(c) && depth >= 1) any = true;
        }
        return -1;
    }

    internal static void SelfTest()
    {
        if (CountArgs("f(a, g(b, c), \"x,y\")", 1) != 3 || CountArgs("f()", 1) != 0 || CountArgs("f(", 1) != -1)
            throw new InvalidOperationException("XsCheck argument counting self-test failed.");
        if (Position("ab\ncd", 4) != (2, 2)) throw new InvalidOperationException("XsCheck position self-test failed.");
    }
}
