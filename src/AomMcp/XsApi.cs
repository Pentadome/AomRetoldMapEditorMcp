using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AomMcp;

/// <summary>
/// XS scripting API: engine syscalls and shipped library functions/classes/globals/rules.
/// The repository ships signatures only (xs/xs_api.json) plus own-words library summaries (xs/xs_summaries.json).
/// Official syscall help and source comments are read from the local install at query time and never shipped.
/// </summary>
internal sealed partial class XsApi
{
    internal sealed record Param(string Type, string Name, string? Default = null, bool? Ref = null);
    internal sealed record Syscall(string Name, string ReturnType, Param[] Params, string Signature, string Group, string[] Contexts);
    internal sealed record Function(string Name, string ReturnType, Param[] Params, string Signature, string[]? Modifiers,
        bool? Prototype, string Library, string File, int Line, string Include, string? Class = null);
    internal sealed record Member(string Type, string Name, string? Default = null);
    internal sealed record ClassDef(string Name, Member[] Members, string Library, string File, int Line, string Include);
    internal sealed record Global(string Name, string Type, bool? Const, bool? Extern, string? Value, string Library, string File, int Line, string Include);
    internal sealed record Rule(string Name, string Attributes, string Library, string File, int Line);
    internal sealed record PlanConstant(string Name, int Value, string VariableType);
    internal sealed record Library(string Id, string Context, string Root, string Directory, int Files, string[] Entries);
    internal sealed record Model(int SchemaVersion, string GameFileVersion, string Note, Library[] Libraries,
        Syscall[] Syscalls, PlanConstant[] AiPlanConstants, Function[] Functions, ClassDef[] Classes, Global[] Globals, Rule[] Rules);

    // Install-relative locations observed in Retold (vscodeextensionretail/xs.vsix, game/random_maps, game/ai).
    const string Vsix = "vscodeextensionretail/xs.vsix";
    const string FileName = "xs_api.json";
    const string SummariesName = "xs_summaries.json";
    internal static readonly string[] Contexts = ["ai", "randomMap", "trigger"];
    static readonly string[] Kinds = ["syscall", "function", "class", "global", "rule", "aiPlanConstant"];
    // BANG_Documentation "What is allowed in AI/RM/TR scripts": XS + KB syscalls everywhere, plus AI/RM/TR families.
    static string[] SyscallContexts(string group) => group switch
    {
        "xsfuncs" => Contexts,
        _ when group.StartsWith("kb", StringComparison.Ordinal) => Contexts,
        "aifuncs" => ["ai"],
        "randommapfuncs" => ["randomMap"],
        _ when group.StartsWith("triggerfuncs", StringComparison.Ordinal) => ["trigger"],
        _ => [],
    };
    // Reusable shipped libraries: (id, context, include root under game/, directory under root).
    static readonly (string Id, string Context, string Root, string Dir)[] LibraryRoots =
    [
        ("rm/lib", "randomMap", "random_maps", "lib"),
        ("rm/lib2", "randomMap", "random_maps", "lib2"),
        ("ai/core", "ai", "ai", "core"),
        ("ai/human_assist", "ai", "ai", "human_assist"),
    ];

    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
    };

    readonly Model _model;
    readonly Dictionary<string, string> _summaries;
    readonly string _installDir;
    Dictionary<string, JsonObject>? _localSyscalls;
    string? _localError;

    XsApi(Model model, Dictionary<string, string> summaries, string installDir)
    {
        _model = model;
        _summaries = summaries;
        _installDir = installDir;
    }

    /// <summary>Loads the shipped API beside the host (or repository xs/ directory).</summary>
    /// <exception cref="FileNotFoundException">Shipped API file is missing from this build.</exception>
    internal static XsApi Load(string exe)
    {
        string? dir = null;
        for (DirectoryInfo? d = new(AppContext.BaseDirectory); d != null && dir == null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "xs", FileName)))
                dir = Path.Combine(d.FullName, "xs");
        if (dir == null)
            throw new FileNotFoundException("Shipped XS API (xs/xs_api.json) missing from this build.");
        var model = JsonSerializer.Deserialize<Model>(File.ReadAllText(Path.Combine(dir, FileName)), Json)
            ?? throw new InvalidDataException("Invalid xs_api.json.");
        var summariesFile = Path.Combine(dir, SummariesName);
        var summaries = File.Exists(summariesFile)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(summariesFile)) ?? []
            : [];
        return new XsApi(model, summaries, Path.GetDirectoryName(exe)!);
    }

    /// <summary>Searches shipped API entries; adds official help/source comments from the local install when present.</summary>
    internal object Query(JsonElement args)
    {
        string Text(string key) => args.TryGetProperty(key, out var v) ? v.GetString() ?? "" : "";
        var offset = args.TryGetProperty("offset", out var o) ? o.GetInt32() : 0;
        var limit = args.TryGetProperty("limit", out var l) ? l.GetInt32() : 20; // Host default/cap.
        if (offset < 0 || limit is < 1 or > 100)
            throw new ArgumentException("offset must be >= 0; limit must be 1..100.");
        var name = Text("name").Trim();
        var terms = Text("filter").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var kind = Text("kind") is { Length: > 0 } k ? k : "any";
        var context = Text("context") is { Length: > 0 } c ? c : "any";
        var library = Text("library");
        var local = !args.TryGetProperty("includeLocal", out var il) || il.GetBoolean();
        if (kind != "any" && !Kinds.Contains(kind))
            throw new ArgumentException("kind must be any|" + string.Join('|', Kinds) + ".");
        if (context != "any" && !Contexts.Contains(context))
            throw new ArgumentException("context must be any|" + string.Join('|', Contexts) + ".");
        var contexts = _model.Libraries.ToDictionary(x => x.Id, x => x.Context);
        bool LibraryMatch(string lib) => (library.Length == 0 || lib.Equals(library, StringComparison.OrdinalIgnoreCase))
            && (context == "any" || contexts[lib] == context);
        bool Match(string entryName, string? summary) =>
            (name.Length == 0 || entryName.Equals(name, StringComparison.OrdinalIgnoreCase))
            && terms.All(t => entryName.Contains(t, StringComparison.OrdinalIgnoreCase)
                || (summary?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false));
        string? Summary(string lib, string? cls, string entryName) =>
            _summaries.GetValueOrDefault(lib + "/" + (cls is null ? "" : cls + ".") + entryName);
        var results = new List<(string Kind, string Name, object Entry)>();
        if (kind is "any" or "syscall")
            foreach (var s in _model.Syscalls)
                if ((library.Length == 0 || s.Group.Equals(library, StringComparison.OrdinalIgnoreCase))
                    && (context == "any" || s.Contexts.Contains(context)) && Match(s.Name, null))
                    results.Add(("syscall", s.Name, s));
        if (kind is "any" or "function")
            foreach (var f in _model.Functions)
                if (LibraryMatch(f.Library) && Match(f.Name, Summary(f.Library, f.Class, f.Name)))
                    results.Add(("function", f.Name, f));
        if (kind is "any" or "class")
            foreach (var x in _model.Classes)
                if (LibraryMatch(x.Library) && Match(x.Name, Summary(x.Library, null, x.Name)))
                    results.Add(("class", x.Name, x));
        if (kind is "any" or "global")
            foreach (var g in _model.Globals)
                if (LibraryMatch(g.Library) && Match(g.Name, null))
                    results.Add(("global", g.Name, g));
        if (kind is "any" or "rule")
            foreach (var r in _model.Rules)
                if (LibraryMatch(r.Library) && Match(r.Name, null))
                    results.Add(("rule", r.Name, r));
        if (kind is "any" or "aiPlanConstant" && library.Length == 0 && context is "any" or "ai")
            foreach (var p in _model.AiPlanConstants)
                if (Match(p.Name, null))
                    results.Add(("aiPlanConstant", p.Name, p));
        if (local && kind is "any" or "syscall" && library.Length == 0)
        {
            var shipped = _model.Syscalls.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var (localName, node) in LocalSyscalls())
                if (!shipped.Contains(localName) && Match(localName, null))
                    results.Add(("syscall", localName, new LocalOnly(localName, LocalSignature(node),
                        Path.GetFileNameWithoutExtension(node["filename"]?.GetValue<string>() ?? ""))));
        }
        // Exact name first, then shorter names.
        var ordered = results.OrderBy(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(r => r.Name.Length).ThenBy(r => r.Name, StringComparer.Ordinal).ToArray();
        var page = ordered.Skip(offset).Take(limit).Select(r => Present(r.Kind, r.Entry, local)).ToArray();
        return new
        {
            total = ordered.Length,
            offset,
            limit,
            nextOffset = offset + page.Length < ordered.Length ? (int?)(offset + page.Length) : null,
            entries = page,
            shippedFromGameVersion = _model.GameFileVersion,
            localHelp = local ? LocalStatus() : "disabled",
            usage = "AI scripts may call ai/kb/xs syscalls; random maps rm/kb/xs; triggers trigger/kb/xs. Library entries need include \"<include>\"; relative to game/random_maps (rm/*) or game/ai (ai/*). summary = this project's own words; officialHelp/sourceComment are read from your install, never redistributed.",
        };
    }

    internal sealed record LocalOnly(string Name, string Signature, string Group, bool LocalOnlyEntry = true);

    string LocalStatus()
    {
        _ = LocalSyscalls();
        return _localError ?? "official help and source comments read from local install " + _installDir;
    }

    JsonObject Present(string kind, object entry, bool local)
    {
        var node = JsonSerializer.SerializeToNode(entry, entry.GetType(), Json)!.AsObject();
        node["kind"] = kind;
        switch (entry)
        {
            case Syscall s when local && LocalSyscalls().TryGetValue(s.Name, out var l):
                node["officialHelp"] = l["help"]?.GetValue<string>();
                if (LocalSignature(l) != s.Signature) node["localSignature"] = LocalSignature(l);
                break;
            case Syscall when local && LocalSyscalls().Count > 0:
                node["missingLocally"] = true;
                break;
            case Function f:
                node["summary"] = _summaries.GetValueOrDefault(f.Library + "/" + (f.Class is null ? "" : f.Class + ".") + f.Name);
                if (local) node["sourceComment"] = SourceComment(f.File, f.Line, f.Name);
                break;
            case ClassDef x:
                node["summary"] = _summaries.GetValueOrDefault(x.Library + "/" + x.Name);
                node["methods"] = new JsonArray(_model.Functions.Where(f => f.Class == x.Name && f.Library == x.Library)
                    .Select(f => (JsonNode)f.Signature).ToArray());
                if (local) node["sourceComment"] = SourceComment(x.File, x.Line, x.Name);
                break;
        }
        return node;
    }

    Dictionary<string, JsonObject> LocalSyscalls()
    {
        if (_localSyscalls != null) return _localSyscalls;
        try
        {
            using var zip = ZipFile.OpenRead(Path.Combine(_installDir, Vsix));
            using var stream = zip.GetEntry("extension/syscalls/syscalls.json")?.Open()
                ?? throw new FileNotFoundException("syscalls.json missing in xs.vsix");
            _localSyscalls = JsonNode.Parse(stream)!["syscalls"]!.AsArray()
                .Select(n => n!.AsObject()).GroupBy(n => n["name"]!.GetValue<string>())
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            _localError = "local install help unavailable: " + e.Message;
            _localSyscalls = [];
        }
        return _localSyscalls;
    }

    static string LocalSignature(JsonObject n)
    {
        var parameters = n["params"]!.AsArray().Select(p => new Param(p!["type"]!.GetValue<string>(), p["name"]!.GetValue<string>(),
            p["default"]?.GetValue<string>())).ToArray();
        return Signature(n["return_type"]!.GetValue<string>(), n["name"]!.GetValue<string>(), parameters, null);
    }

    /// <summary>Comment lines directly above a declaration in the installed source; null when absent or the line moved.</summary>
    string? SourceComment(string file, int line, string name)
    {
        var path = Path.Combine(_installDir, "game", file);
        if (!File.Exists(path)) return null;
        var lines = File.ReadAllLines(path);
        if (line < 1 || line > lines.Length || !lines[line - 1].Contains(name, StringComparison.Ordinal)) return null;
        var collected = new List<string>();
        for (var i = line - 2; i >= 0; i--)
        {
            var t = lines[i].Trim();
            if (!t.StartsWith("//", StringComparison.Ordinal) && !t.StartsWith("/*", StringComparison.Ordinal)
                && !t.StartsWith('*') && !t.EndsWith("*/", StringComparison.Ordinal)) break;
            var content = t.TrimStart('/', '*').TrimEnd('/', '*').Trim();
            if (content.Trim('=', '-').Length > 0) collected.Insert(0, content);
        }
        // Banner headers that only repeat the declaration name carry no information.
        collected.RemoveAll(c => c == name);
        return collected.Count > 0 ? string.Join('\n', collected) : null;
    }

    /// <summary>Maintainer command: extracts signature-only API from an installed game (no help text, comments or bodies).</summary>
    internal static void Build(string exe, string output)
    {
        var install = Path.GetDirectoryName(exe)!;
        var game = Path.Combine(install, "game");
        using var zip = ZipFile.OpenRead(Path.Combine(install, Vsix));
        JsonNode Entry(string name) => JsonNode.Parse(zip.GetEntry(name)?.Open()
            ?? throw new FileNotFoundException("xs.vsix entry missing: " + name))!;
        var syscalls = Entry("extension/syscalls/syscalls.json")["syscalls"]!.AsArray().Select(s =>
        {
            var parameters = s!["params"]!.AsArray().Select(p => new Param(
                p!["type"]!.GetValue<string>(), p["name"]!.GetValue<string>(), p["default"]?.GetValue<string>())).ToArray();
            var group = Path.GetFileNameWithoutExtension(s["filename"]!.GetValue<string>());
            var ret = s["return_type"]!.GetValue<string>();
            var name = s["name"]!.GetValue<string>();
            return new Syscall(name, ret, parameters, Signature(ret, name, parameters, null), group, SyscallContexts(group));
        }).OrderBy(s => s.Name, StringComparer.Ordinal).ToArray();
        var plans = Entry("extension/constants/aiplans.json")["constants"]!.AsArray().Select(c => new PlanConstant(
            c!["name"]!.GetValue<string>(), c["value"]!.GetValue<int>(), c["variable_type"]!.GetValue<string>())).ToArray();
        var libraries = new List<Library>();
        var functions = new List<Function>();
        var classes = new List<ClassDef>();
        var globals = new List<Global>();
        var rules = new List<Rule>();
        foreach (var (id, context, root, dir) in LibraryRoots)
        {
            var rootPath = Path.Combine(game, root);
            var files = Directory.GetFiles(Path.Combine(rootPath, dir), "*.xs", SearchOption.AllDirectories)
                .OrderBy(f => f, StringComparer.Ordinal).ToArray();
            var includes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in files)
            {
                var include = Path.GetRelativePath(rootPath, path).Replace('\\', '/');
                var file = root + "/" + include;
                var parsed = Parse(File.ReadAllText(path));
                includes.UnionWith(parsed.Includes);
                functions.AddRange(parsed.Functions.Select(f => f with { Library = id, File = file, Include = include }));
                classes.AddRange(parsed.Classes.Select(c => c with { Library = id, File = file, Include = include }));
                globals.AddRange(parsed.Globals.Select(g => g with { Library = id, File = file, Include = include }));
                rules.AddRange(parsed.Rules.Select(r => r with { Library = id, File = file }));
            }
            // Entry points = library files nothing else in the library includes.
            var entries = files.Select(f => Path.GetRelativePath(rootPath, f).Replace('\\', '/'))
                .Where(f => !includes.Contains(f)).ToArray();
            libraries.Add(new Library(id, context, root, dir, files.Length, entries));
        }
        var model = new Model(1, System.Diagnostics.FileVersionInfo.GetVersionInfo(exe).FileVersion ?? "",
            "Signatures/names/types/defaults only, extracted from an installed game for interoperability. No official help text, comments or code bodies.",
            [.. libraries], syscalls, plans, [.. functions], [.. classes], [.. globals], [.. rules]);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(model, Json));
        Console.WriteLine($"XS API: {syscalls.Length} syscalls, {functions.Count} functions, {classes.Count} classes, {globals.Count} globals, {rules.Count} rules -> {output}");
    }

    static string Signature(string ret, string name, Param[] parameters, string[]? modifiers) =>
        (modifiers is { Length: > 0 } ? string.Join(' ', modifiers) + " " : "") + ret + " " + name + "("
        + string.Join(", ", parameters.Select(p => (p.Ref == true ? "ref " : "") + p.Type + " " + p.Name
            + (p.Default is null ? "" : " = " + p.Default))) + ")";

    internal sealed record Parsed(List<Function> Functions, List<ClassDef> Classes, List<Global> Globals, List<Rule> Rules, List<string> Includes);

    [GeneratedRegex(@"^(?<mods>(?:(?:mutable|extern|static|override)\s+)*)(?<ret>[\w\[\]]+(?:\([^()]*\))?)\s+(?<name>\w+)\s*\((?<params>.*)\)$", RegexOptions.Singleline)]
    private static partial Regex FunctionHeader();
    [GeneratedRegex(@"^(?<mods>(?:(?:extern|const|static)\s+)*)(?<type>[\w\[\]]+(?:\([^()]*\))?)\s+(?<name>\w+)\s*(?:=\s*(?<value>.+))?$", RegexOptions.Singleline)]
    private static partial Regex Declaration();
    [GeneratedRegex(@"^(?<ref>ref\s+)?(?<type>.+?)\s+(?<name>\w+)\s*(?:=\s*(?<def>.+))?$", RegexOptions.Singleline)]
    private static partial Regex Parameter();
    [GeneratedRegex(@"^include\s+""(?<path>[^""]+)""$")]
    private static partial Regex Include();
    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    /// <summary>Tolerant top-level XS scanner: functions, prototypes, classes, globals, rules, includes. Bodies are skipped.</summary>
    internal static Parsed Parse(string source, int firstLine = 1)
    {
        var text = StripComments(source);
        var result = new Parsed([], [], [], [], []);
        var buffer = new StringBuilder();
        var start = -1;
        var line = firstLine;
        var parens = 0; // Inside a parameter list, braces/semicolons belong to lambda defaults.
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '#' && buffer.ToString().Trim().Length == 0)
            {
                // Preprocessor directive (#if/#define/#endif): skip line.
                while (i < text.Length && text[i] != '\n') i++;
                line++;
                continue;
            }
            if (c == '"')
            {
                var end = StringEnd(text, i);
                buffer.Append(text, i, end - i + 1);
                i = end;
                continue;
            }
            if (c == '\n') line++;
            if (start < 0 && !char.IsWhiteSpace(c)) start = line;
            if (c == '(') parens++;
            else if (c == ')') parens = Math.Max(0, parens - 1);
            if (parens > 0)
                buffer.Append(c);
            else if (c == ';')
            {
                Statement(Normalize(buffer), start, result);
                buffer.Clear();
                start = -1;
                parens = 0;
            }
            else if (c == '{' && HasTopLevelAssignment(buffer))
            {
                // Lambda initializer of a global (extern void() g = []() { ... };): keep it in the statement.
                var close = BlockEnd(text, i);
                buffer.Append(text, i, close - i + 1);
                line += text[i..close].Count(ch => ch == '\n');
                i = close;
            }
            else if (c == '{')
            {
                var close = BlockEnd(text, i);
                var body = text[(i + 1)..close];
                Block(Normalize(buffer), body, start, line, result);
                line += body.Count(ch => ch == '\n');
                i = close;
                buffer.Clear();
                start = -1;
            }
            else
                buffer.Append(c);
        }
        return result;
    }

    static bool HasTopLevelAssignment(StringBuilder b)
    {
        var depth = 0;
        for (var i = 0; i < b.Length; i++)
        {
            if (b[i] is '(' or '[') depth++;
            else if (b[i] is ')' or ']') depth--;
            else if (b[i] == '=' && depth == 0 && (i + 1 >= b.Length || b[i + 1] != '=')
                && (i == 0 || b[i - 1] is not ('=' or '!' or '<' or '>'))) return true;
        }
        return false;
    }

    static string Normalize(StringBuilder b) => Spaces().Replace(b.ToString().Trim(), " ").Replace("( ", "(").Replace(" )", ")");

    static void Statement(string s, int line, Parsed result)
    {
        if (s.Length == 0) return;
        var include = Include().Match(s);
        if (include.Success)
        {
            result.Includes.Add(include.Groups["path"].Value);
            return;
        }
        if (FunctionHeader().Match(s) is { Success: true } f)
        {
            result.Functions.Add(MakeFunction(f, line, prototype: true));
            return;
        }
        var d = Declaration().Match(s);
        if (!d.Success) return;
        var mods = d.Groups["mods"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var isConst = mods.Contains("const");
        var value = d.Groups["value"].Success ? d.Groups["value"].Value.Trim() : null;
        // Constant initializers are part of the interface; mutable global initial values are omitted.
        result.Globals.Add(new Global(d.Groups["name"].Value, d.Groups["type"].Value, isConst ? true : null,
            mods.Contains("extern") ? true : null, isConst && value is { Length: <= 160 } ? value : null, "", "", line, ""));
    }

    static void Block(string header, string body, int line, int braceLine, Parsed result)
    {
        if (header.StartsWith("rule ", StringComparison.Ordinal))
        {
            var parts = header.Split(' ', 3);
            result.Rules.Add(new Rule(parts[1], parts.Length > 2 ? parts[2] : "", "", "", line));
            return;
        }
        if (header.StartsWith("class ", StringComparison.Ordinal))
        {
            // Class bodies hold member declarations and methods; parse them like a file, keeping absolute lines.
            var className = header[6..].Trim();
            var inner = Parse(body, braceLine);
            var members = inner.Globals.Select(g => new Member(g.Type, g.Name)).ToArray();
            result.Functions.AddRange(inner.Functions.Select(f => f with { Class = className }));
            result.Classes.Add(new ClassDef(className, members, "", "", line, ""));
            return;
        }
        if (FunctionHeader().Match(header) is { Success: true } f)
            result.Functions.Add(MakeFunction(f, line, prototype: false));
    }

    static Function MakeFunction(Match f, int line, bool prototype)
    {
        var mods = f.Groups["mods"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var parameters = SplitTop(f.Groups["params"].Value, ',').Select(p => p.Trim()).Where(p => p.Length > 0).Select(p =>
        {
            var m = Parameter().Match(p);
            return m.Success
                ? new Param(m.Groups["type"].Value.Trim(), m.Groups["name"].Value,
                    m.Groups["def"].Success ? m.Groups["def"].Value.Trim() : null, m.Groups["ref"].Success ? true : null)
                : new Param(p, "");
        }).ToArray();
        var ret = f.Groups["ret"].Value;
        var name = f.Groups["name"].Value;
        var modifiers = mods.Length > 0 ? mods : null;
        return new Function(name, ret, parameters, Signature(ret, name, parameters, modifiers), modifiers,
            prototype ? true : null, "", "", line, "");
    }

    static IEnumerable<string> SplitTop(string s, char separator)
    {
        var depth = 0;
        var startAt = 0;
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '"') { i = StringEnd(s, i); continue; }
            if (s[i] is '(' or '[' or '{') depth++;
            else if (s[i] is ')' or ']' or '}') depth--;
            else if (s[i] == separator && depth == 0)
            {
                yield return s[startAt..i];
                startAt = i + 1;
            }
        }
        yield return s[startAt..];
    }

    static int StringEnd(string s, int open)
    {
        for (var i = open + 1; i < s.Length; i++)
        {
            if (s[i] == '\\') i++;
            else if (s[i] == '"' || s[i] == '\n') return i;
        }
        return s.Length - 1;
    }

    static int BlockEnd(string s, int open)
    {
        var depth = 0;
        for (var i = open; i < s.Length; i++)
        {
            if (s[i] == '"') i = StringEnd(s, i);
            else if (s[i] == '{') depth++;
            else if (s[i] == '}' && --depth == 0) return i;
        }
        return s.Length - 1;
    }

    /// <summary>Replaces // and /* */ comments with spaces, keeping newlines and string literals.</summary>
    static string StripComments(string s)
    {
        var b = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '"')
            {
                var end = StringEnd(s, i);
                b.Append(s, i, end - i + 1);
                i = end;
            }
            else if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                while (i < s.Length && s[i] != '\n') i++;
                if (i < s.Length) b.Append('\n');
            }
            else if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                var end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? s.Length - 1 : end + 1;
                for (var j = i; j <= end; j++) b.Append(s[j] == '\n' ? '\n' : ' ');
                i = end;
            }
            else
                b.Append(s[i]);
        }
        return b.ToString();
    }
}
