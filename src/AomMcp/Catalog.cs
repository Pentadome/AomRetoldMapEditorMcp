using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AomMcp;

/// <summary>Describes a parameter recovered from embedded native help.</summary>
/// <param name="Type">Documented native scalar or vector type.</param>
/// <param name="Name">Parameter name used in MCP arguments.</param>
internal sealed record Parameter(string Type, string Name);

/// <summary>Describes an exposed native editor command without claiming ABI return-value capture.</summary>
/// <param name="Name">Native command name.</param>
/// <param name="ReturnType">Return type described by native help.</param>
/// <param name="Parameters">Required typed arguments.</param>
/// <param name="Help">Embedded command description.</param>
internal sealed record Command(string Name, string ReturnType, Parameter[] Parameters, string Help);

/// <summary>Stores an original command expression from shipped editor controls or hotkeys.</summary>
/// <param name="Name">Unique action identifier.</param>
/// <param name="Label">Control label or hotkey description.</param>
/// <param name="Script">Shipped expression to dispatch unchanged.</param>
/// <param name="Source">Source XML or configuration filename.</param>
internal sealed record MenuAction(string Name, string Label, string Script, string Source);

/// <summary>Builds typed MCP command and action metadata from the installed game's editing sources.</summary>
internal sealed partial class Catalog
{
    /// <summary>Gets supported native commands keyed by exact command name.</summary>
    public Dictionary<string, Command> Commands { get; } = new(StringComparer.Ordinal);
    /// <summary>Gets shipped editor actions keyed by their unique identifiers.</summary>
    public Dictionary<string, MenuAction> Actions { get; } = new(StringComparer.Ordinal);
    /// <summary>Core editing commands included even without UI prefixes or source references.</summary>
    // Host allowlist of native names found in installed executable help and game/config/editor.con;
    // these core editor commands lack the usual ui/gadget prefix (research/MCP-COVERAGE.md).
    public static readonly HashSet<string> Extra = new(StringComparer.Ordinal)
    {
        "editMode",
        "undo",
        "redo",
        "saveScenario",
        "pause",
        "unpause",
        "renderSky",
        "renderFog",
        "renderWater",
        "renderTerrain",
        "renderUnits",
    };

    /// <summary>Scans executable help, editor hotkeys, and optional decoded editor XML.</summary>
    /// <param name="exe">Installed game executable path.</param>
    /// <param name="uiDirectory">Decoded editor XML directory, or null to omit XML actions.</param>
    public Catalog(string exe, string? uiDirectory = null)
    {
        if (uiDirectory != null && Directory.Exists(uiDirectory))
            LoadActions(uiDirectory);
        // Shipped editor hotkey file under the installation; decoded XML comes from UIDefaultEditor.bar.
        LoadHotkeys(Path.Combine(Path.GetDirectoryName(exe)!, "game", "config", "editor.con"));
        var referenced = Actions
            .Values.SelectMany(a => IdentifierRegex().Matches(a.Script).Select(m => m.Value))
            .ToHashSet(StringComparer.Ordinal);
        // Latin1 preserves one char per byte while finding embedded ASCII native help in the PE.
        var text = Encoding.Latin1.GetString(File.ReadAllBytes(exe));
        var regex = CommandHelpRegex();
        foreach (Match m in regex.Matches(text))
        {
            // CommandHelpRegex groups: 1=return type, 2=name, 3=parameter list, 4=help after "): ".
            // ui/gadget prefixes are observed engine UI namespaces; other names need source/allowlist evidence.
            var name = m.Groups[2].Value;
            if (
                !name.StartsWith("ui", StringComparison.Ordinal)
                && !name.StartsWith("gadget", StringComparison.Ordinal)
                && !Extra.Contains(name)
                && !referenced.Contains(name)
            )
                continue;
            if (RemovedCommand(name) || MultiplayerCommandRegex().IsMatch(name))
                continue;
            var args = m.Groups[3].Value.Trim();
            var parameters = new List<Parameter>();
            var supported = true;
            if (args.Length != 0)
                foreach (var arg in args.Split(','))
                {
                    var p = ParameterDeclarationRegex().Match(arg.Trim());
                    if (!p.Success)
                    {
                        supported = false;
                        break;
                    }
                    // ParameterDeclarationRegex groups: 1=native parameter type, 2=parameter name (embedded help).
                    parameters.Add(new(p.Groups[1].Value, p.Groups[2].Value));
                }
            if (supported)
                Commands.TryAdd(
                    name,
                    new(name, m.Groups[1].Value, parameters.ToArray(), m.Groups[4].Value.Trim())
                );
        }
    }

    void AddAction(string name, string label, string script, string source)
    {
        script = script.Trim();
        // Host limits: 8000-char shipped expressions fit beneath 16 KiB bridge capacity for ordinary
        // ASCII commands; byte length is rechecked by Bridge. NUL would terminate native char* early.
        if (script.Length == 0 || script.Length > 8000 || script.Contains('\0') || RemovedScript(script))
            return;
        // Host naming policy: MCP action IDs use ASCII letters/digits/underscore, max 90-char base;
        // duplicate IDs get suffixes starting at 2 because unsuffixed first occurrence already exists.
        var key = InvalidActionNameCharacterRegex().Replace(name, "_");
        if (key.Length > 90)
            key = key[..90];
        var unique = key;
        for (var suffix = 2; Actions.ContainsKey(unique); suffix++)
            unique = key + "_" + suffix;
        Actions[unique] = new(unique, label, script, source);
    }

    void LoadHotkeys(string path)
    {
        if (!File.Exists(path))
            return;
        // Shipped editor.con syntax: map("key", "context", "expression") => regex groups 1/2/3.
        // key_ is host action naming; escaped quotes/backslashes are decoded as written in that file.
        foreach (Match m in HotkeyBindingRegex().Matches(File.ReadAllText(path)))
            AddAction(
                "key_" + m.Groups[2].Value + "_" + m.Groups[1].Value,
                $"Hotkey {m.Groups[1].Value}; context {m.Groups[2].Value}",
                m.Groups[3].Value.Replace("\\\"", "\"").Replace(@"\\", "\\"),
                "editor.con"
            );
    }

    void LoadActions(string directory)
    {
        // XML field names come from shipped editor UI XML (generated/ui): command element/attribute,
        // nearest gadget's name, text, tooltip. Filename/index fallback is host naming, not an engine ID.
        foreach (
            var path in Directory.EnumerateFiles(directory, "*.xml", SearchOption.AllDirectories)
        )
        {
            XDocument doc;
            try
            {
                doc = XDocument.Load(path);
            }
            catch (System.Xml.XmlException)
            {
                continue;
            }
            var index = 0;
            foreach (
                var node in doc.Descendants()
                    .Where(e =>
                        e.Name.LocalName.Equals("command", StringComparison.OrdinalIgnoreCase)
                    )
            )
            {
                var gadget = node.Ancestors()
                    .FirstOrDefault(e =>
                        e.Name.LocalName.Equals("gadget", StringComparison.OrdinalIgnoreCase)
                    );
                var name =
                    gadget?.Attribute("name")?.Value
                    ?? $"{Path.GetFileNameWithoutExtension(path)}_{index}";
                var label =
                    gadget?.Attribute("text")?.Value ?? gadget?.Attribute("tooltip")?.Value ?? name;
                AddAction(name, label, node.Value, Path.GetFileName(path));
                index++;
            }
            foreach (var node in doc.Descendants())
            foreach (
                var attribute in node.Attributes()
                    .Where(a =>
                        a.Name.LocalName.Equals("command", StringComparison.OrdinalIgnoreCase)
                    )
            )
            {
                var name =
                    node.Attribute("name")?.Value
                    ?? $"{Path.GetFileNameWithoutExtension(path)}_item_{index}";
                AddAction(
                    name,
                    node.Attribute("text")?.Value ?? name,
                    attribute.Value,
                    Path.GetFileName(path)
                );
                index++;
            }
        }
    }

    /// <summary>Identifies native commands removed after observed unsafe live behavior.</summary>
    /// <param name="name">Native command identifier; comparison is case-insensitive.</param>
    /// <returns>True for direct scenario loading; normal uiScenarioLoad dialog remains available.</returns>
    // Live alternative-UI test on build 100.19.17020.0 crashed after native loadScenario;
    // the same checkpoint loaded successfully through normal Load Scenario UI after restart.
    // Keep native tool/actions excluded, not merely confirmation-gated. Root cause is unproven.
    // Evidence and recovery: research/ALTERNATIVE-UI.md.
    public static bool RemovedCommand(string name) =>
        name.Equals("loadScenario", StringComparison.OrdinalIgnoreCase);

    /// <summary>Refuses shipped actions referencing removed native command identifiers.</summary>
    /// <param name="script">Original shipped command expression.</param>
    /// <returns>True when an identifier references a removed command, even in a mixed expression.</returns>
    public static bool RemovedScript(string script) =>
        IdentifierRegex().Matches(script).Any(m => RemovedCommand(m.Value));

    /// <summary>Classifies command names requiring explicit data-loss or exit confirmation.</summary>
    /// <param name="name">Native command name.</param>
    /// <returns>True when the name matches a guarded lifecycle or data-loss action.</returns>
    // Host data-loss guard derived from observed native names/editor.con, not engine permission metadata.
    public static bool Confirmation(string name) =>
        DestructiveCommandRegex().IsMatch(name);

    /// <summary>Validates and escapes a string for a native command expression.</summary>
    /// <param name="value">Text up to 4096 characters with no control characters.</param>
    /// <returns>Double-quoted text with backslashes and quotes escaped.</returns>
    public static string Quote(string value)
    {
        // Host argument cap: 4096 chars. Native XS/console strings use double quotes and backslash
        // escapes; reject NUL/control chars so values cannot terminate or split the command expression.
        if (value.Length > 4096 || value.Any(c => c == '\0' || char.IsControl(c)))
            throw new ArgumentException(
                "String exceeds 4096 characters or contains control characters."
            );
        return "\"" + value.Replace(@"\", @"\\").Replace("\"", "\\\"") + "\"";
    }

    /// <summary>Validates required arguments and confirmations, then builds a typed command expression.</summary>
    /// <param name="command">Command metadata defining parameter names and types.</param>
    /// <param name="args">MCP argument object, including any required confirmation.</param>
    /// <returns>Expression ready for dispatch; no game operation is performed here.</returns>
    public static string Build(Command command, JsonElement args)
    {
        if (RemovedCommand(command.Name))
            throw new ArgumentException("Native loadScenario tool removed after game crash. Use editor Load Scenario UI.");
        ValidateObject(args, command.Parameters.Select(p => p.Name).Append("confirmDestructive"));
        if (
            Confirmation(command.Name)
            && (
                !args.TryGetProperty("confirmDestructive", out var confirm)
                || confirm.ValueKind != JsonValueKind.True
            )
        )
            throw new ArgumentException(
                "This action can overwrite/discard data or close game. confirmDestructive=true required."
            );
        var values = new List<string>();
        foreach (var p in command.Parameters)
        {
            if (!args.TryGetProperty(p.Name, out var v))
                throw new ArgumentException(
                    $"Missing parameter {p.Name}. Native help has no validated defaults; supply explicitly."
                );
            values.Add(Value(p.Type, v));
        }
        // Native name from embedded help/editor.con; slot 0=Gaia, 1..12=player slots
        // supported by Retold's editor. Same bound/default policy as Server.Place.
        if (command.Name == "uiSetPlacementPlayer")
        {
            var player = args.GetProperty(command.Parameters[0].Name).GetInt32();
            if (player is < 0 or > 12)
                throw new ArgumentException("Placement player must be 0..12.");
        }
        return command.Name + "(" + string.Join(",", values) + ")";
    }

    /// <summary>Requires a JSON object and refuses unknown argument names.</summary>
    /// <param name="args">Argument object to inspect.</param>
    /// <param name="allowed">Accepted property names; required values are checked by the caller.</param>
    public static void ValidateObject(JsonElement args, IEnumerable<string> allowed)
    {
        if (args.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Arguments must be an object.");
        var names = allowed.ToHashSet(StringComparer.Ordinal);
        foreach (var p in args.EnumerateObject())
            if (!names.Contains(p.Name))
                throw new ArgumentException($"Unknown argument {p.Name}.");
    }

    // Type names come from embedded native help. XS vector uses three x/y/z components and
    // xsVectorSet constructor (installed BANG_Documentation/XS documentation and syscalls.json).
    // Bool strings and (), comma delimiters are XS/console expression syntax, not MCP JSON types.
    static string Value(string type, JsonElement v) =>
        type switch
        {
            "string" when v.ValueKind == JsonValueKind.String => Quote(v.GetString()!),
            "bool" when v.ValueKind is JsonValueKind.True or JsonValueKind.False => v.GetBoolean()
                ? "true"
                : "false",
            "int" when v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) =>
                i.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "float" when v.ValueKind == JsonValueKind.Number => Number(v.GetDouble()),
            "vector" when v.ValueKind == JsonValueKind.Array && v.GetArrayLength() == 3 =>
                "xsVectorSet("
                    + string.Join(",", v.EnumerateArray().Select(x => Number(x.GetDouble())))
                    + ")",
            _ => throw new ArgumentException($"Expected {type}."),
        };

    static string Number(double value)
    {
        if (!double.IsFinite(value) || value < -float.MaxValue || value > float.MaxValue)
            throw new ArgumentException("Non-finite/out-of-range float.");
        // .NET "R" = round-trip float format; invariant decimal dot matches XS parsing, not OS locale.
        var single = (float)value;
        var text = single.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        // "R" switches to exponent form (1E-08, 1E+20); spell such values out as plain decimals instead.
        return text.Contains('E')
            ? ((double)single).ToString("0." + new string('#', 60), System.Globalization.CultureInfo.InvariantCulture)
            : text;
    }

    /// <summary>Builds a command's MCP input schema with required types and confirmation flags.</summary>
    /// <param name="c">Native command metadata.</param>
    /// <returns>A serializable JSON Schema object.</returns>
    public static object Schema(Command c)
    {
        // JSON Schema keyword/type names (properties/type/required/const/additionalProperties) are
        // standard schema vocabulary consumed by MCP; vector min/maxItems=3 enforces x/y/z.
        var properties = new Dictionary<string, object>();
        foreach (var p in c.Parameters)
            properties[p.Name] =
                p.Type == "vector"
                    ? new
                    {
                        type = "array",
                        items = new { type = "number" },
                        minItems = 3,
                        maxItems = 3,
                    }
                    : new Dictionary<string, object>
                    {
                        ["type"] = p.Type switch
                        {
                            "string" => "string",
                            "bool" => "boolean",
                            "int" => "integer",
                            _ => "number",
                        },
                    };
        var required = c.Parameters.Select(p => p.Name).ToList();
        if (Confirmation(c.Name))
        {
            properties["confirmDestructive"] = new { type = "boolean", @const = true };
            required.Add("confirmDestructive");
        }
        return new
        {
            type = "object",
            properties,
            required,
            additionalProperties = false,
        };
    }

    /// <summary>Enumerates MCP definitions for native commands and shipped actions.</summary>
    /// <param name="toolNames">Optional exact-name tool set; null exposes all generated definitions.</param>
    /// <returns>Tool names, descriptions, schemas, and annotations; not live verification results.</returns>
    public IEnumerable<object> Tools(IReadOnlySet<string>? toolNames = null)
    {
        // Host tool prefixes: editor_=typed native name, action_=original shipped expression;
        // Server strips seven chars. Annotation field names are defined by MCP ToolAnnotations.
        var gadgets = ShippedGadgetNames();
        foreach (var c in Commands.Values.Where(c => !RemovedCommand(c.Name)
            && (toolNames is null || toolNames.Contains("editor_" + c.Name)))
            .OrderBy(c => c.Name, StringComparer.Ordinal))
            yield return new
            {
                name = "editor_" + c.Name,
                description = c.Help + GadgetVisibilityNote(c.Name, gadgets)
                    + $" Native {c.ReturnType}; reports dispatcher return only, not a captured value or independently verified effect. Editor mode required.",
                inputSchema = Schema(c),
                annotations = new
                {
                    readOnlyHint = false,
                    destructiveHint = Confirmation(c.Name),
                    idempotentHint = false,
                    openWorldHint = false,
                },
            };
        foreach (var a in Actions.Values.Where(a => !RemovedScript(a.Script)
            && (toolNames is null || toolNames.Contains("action_" + a.Name)))
            .OrderBy(a => a.Name, StringComparer.Ordinal))
            yield return new
            {
                name = "action_" + a.Name,
                description = $"Shipped editor UI action: {a.Label}. Source {a.Source}. Executes its original command expression `{a.Script}`; dialog field values must be set first using editor UI input tools. Native return is not semantic verification.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        confirmDestructive = new { type = "boolean", @const = true },
                    },
                    required = new[] { "confirmDestructive" },
                    additionalProperties = false,
                },
                annotations = new
                {
                    readOnlyHint = false,
                    destructiveHint = true,
                    idempotentHint = false,
                    openWorldHint = false,
                },
            };
    }

    /// <summary>Lists gadget names that shipped action expressions show, hide or toggle.</summary>
    /// <returns>Distinct ordinal-sorted names; empty when decoded editor XML was not loaded.</returns>
    string[] ShippedGadgetNames() => Actions.Values
        .SelectMany(a => GadgetVisibilityCallRegex().Matches(a.Script).Select(m => m.Groups[1].Value))
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray();

    /// <summary>Explains gadget visibility commands in searchable terms; native help only says "makes real".</summary>
    /// <param name="name">Native command name.</param>
    /// <param name="gadgets">Gadget names referenced by shipped actions.</param>
    /// <returns>Description suffix starting with a space, or empty for other commands.</returns>
    static string GadgetVisibilityNote(string name, string[] gadgets)
    {
        // Meaning inferred from shipped editor XML: open buttons call gadgetReal, close buttons gadgetUnreal.
        var effect = name switch
        {
            "gadgetReal" or "gadgetRealIfNotMP" => "Shows/opens",
            "gadgetUnreal" => "Hides/closes",
            "gadgetToggle" or "gadgetToggleIfNotMP" => "Shows or hides (toggles open/closed)",
            _ => null,
        };
        if (effect is null)
            return "";
        var note = $" {effect} the named UI gadget (dialog, panel, menu or window); real = visible, un-real = hidden.";
        if (name.EndsWith("IfNotMP", StringComparison.Ordinal))
            note += " IfNotMP variant: name indicates it acts only outside multiplayer.";
        if (gadgets.Length != 0)
            note += " Gadget names used by shipped editor actions: " + string.Join(", ", gadgets) + ".";
        return note;
    }

    /// <summary>Checks escaping and confirmation classification without calling the game.</summary>
    public static void SelfTest()
    {
        // Deliberate test literals contain quote/backslash, NUL/newline, and safe/guarded native names.
        if (Quote("a\"b\\c") != "\"a\\\"b\\\\c\"")
            throw new InvalidOperationException("Escape test failed.");
        foreach (var s in new[] { "x\0y", "x\ny" })
        {
            try
            {
                Quote(s);
                throw new InvalidOperationException("Control-character test failed.");
            }
            catch (ArgumentException) { }
        }
        if (!Confirmation("saveScenario") || !Confirmation("uiLoadTriggers") || !Confirmation("uiSaveTriggers")
            || Confirmation("uiSetProtoCursor"))
            throw new InvalidOperationException("Confirmation classification test failed.");
        RemovedCommandSelfTest();
        // Synthetic gadget name; search relies on open/close wording and listed names in descriptions.
        if (!GadgetVisibilityNote("gadgetReal", ["FixtureDialog"]).Contains("Shows/opens", StringComparison.Ordinal)
            || !GadgetVisibilityNote("gadgetUnreal", ["FixtureDialog"]).Contains("FixtureDialog", StringComparison.Ordinal)
            || GadgetVisibilityNote("uiScenarioLoad", ["FixtureDialog"]).Length != 0)
            throw new InvalidOperationException("Gadget description fixture failed.");
        // Synthetic native-help fixture; no game/file operation. Import must not bypass helper confirmation.
        var loader = new Command("uiLoadTriggers", "void", [new("string", "filename")], "Synthetic fixture.");
        try
        {
            Build(loader, JsonSerializer.SerializeToElement(new { filename = "fixture" }));
            throw new InvalidOperationException("Native trigger confirmation fixture did not refuse.");
        }
        catch (ArgumentException) { }
        if (Build(loader, JsonSerializer.SerializeToElement(new { filename = "fixture", confirmDestructive = true })) != "uiLoadTriggers(\"fixture\")")
            throw new InvalidOperationException("Confirmed native trigger expression fixture failed.");
    }

    static void RemovedCommandSelfTest()
    {
        // Synthetic installed-help/UI/hotkey sources, never a game connection or real scenario load.
        var temporary = Path.Combine(Path.GetTempPath(), "aom-removed-command-" + Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(Path.Combine(temporary, "game", "config"));
        var ui = Path.Combine(temporary, "ui");
        Directory.CreateDirectory(ui);
        try
        {
            var exe = Path.Combine(temporary, "fixture.exe");
            File.WriteAllText(exe, "void loadScenario(string scenarioName): unsafe\0void uiScenarioLoad(): safe\0", Encoding.Latin1);
            File.WriteAllText(Path.Combine(temporary, "game", "config", "editor.con"),
                """map("L", "editor", "loadScenario(\"fixture\")")""");
            File.WriteAllText(Path.Combine(ui, "fixture.xml"),
                """<root><gadget name="Unsafe"><command>uiClearSelection(); LOADSCENARIO ("fixture")</command></gadget><gadget name="LoadButton"><command>uiScenarioLoad</command></gadget></root>""");
            var catalog = new Catalog(exe, ui);
            if (Extra.Contains("loadScenario") || catalog.Commands.ContainsKey("loadScenario")
                || !catalog.Commands.ContainsKey("uiScenarioLoad") || catalog.Actions.Count != 1
                || !catalog.Actions.ContainsKey("LoadButton"))
                throw new InvalidOperationException("Removed command source-filter fixture failed.");
            // Even stale/injected metadata cannot restore exposure or typed dispatch.
            var removed = new Command("loadScenario", "void", [new("string", "scenarioName")], "Removed fixture.");
            catalog.Commands.Add(removed.Name, removed);
            catalog.Actions.Add("LegacyLoad", new("LegacyLoad", "LegacyLoad", "loadScenario(\"fixture\")", "fixture.xml"));
            var tools = JsonSerializer.SerializeToElement(catalog.Tools());
            if (tools.EnumerateArray().Any(t => t.GetProperty("name").GetString() is "editor_loadScenario" or "action_LegacyLoad"))
                throw new InvalidOperationException("Removed command stale-metadata exposure fixture failed.");
            if (!tools.EnumerateArray().Any(t => t.GetProperty("name").GetString() == "action_LoadButton"
                    && t.GetProperty("description").GetString()!.Contains("`uiScenarioLoad`", StringComparison.Ordinal)))
                throw new InvalidOperationException("Action expression description fixture failed.");
            foreach (var name in new[] { "loadScenario", "LOADSCENARIO" })
            {
                try
                {
                    Build(removed with { Name = name }, JsonSerializer.SerializeToElement(new { scenarioName = "fixture", confirmDestructive = true }));
                    throw new InvalidOperationException("Removed command dispatch fixture did not refuse.");
                }
                catch (ArgumentException) { }
            }
        }
        finally { Directory.Delete(temporary, true); }
    }

    // Shipped expressions spell these calls gadgetReal/gadgetUnReal/gadgetUnreal/gadgetToggle("Name");
    // group 1 is the literal gadget name.
    [GeneratedRegex(@"\bgadget(?:Real|Unreal|Toggle)(?:IfNotMP)?\s*\(\s*""([^""\\]+)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GadgetVisibilityCallRegex();

    // Native/XS identifier spelling observed in shipped expressions (ASCII letters/digits/underscore).
    [GeneratedRegex(@"\b[A-Za-z_][A-Za-z_0-9]*")]
    private static partial Regex IdentifierRegex();

    // Embedded help format: "returnType name(params): help", NUL-delimited in executable.
    // 700 parameter chars / 1600 help chars are host scan caps, not documented engine limits.
    [GeneratedRegex(
        @"\b(void|bool|int|float|string|vector) ([A-Za-z_][A-Za-z_0-9]*)\(([^\x00]{0,700}?)\): ([^\x00]{0,1600})",
        RegexOptions.CultureInvariant
    )]
    private static partial Regex CommandHelpRegex();

    // Host offline-only name filter; these multiplayer-related prefixes occur in native UI help.
    [GeneratedRegex(
        "^ui(MP|Multiplayer|Online|Connect|Login|Lobby|SendChat)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex MultiplayerCommandRegex();

    [GeneratedRegex(@"^(string|float|int|bool|vector)\s+([A-Za-z_][A-Za-z_0-9]*)$")]
    private static partial Regex ParameterDeclarationRegex();
    [GeneratedRegex("""map\s*\(\s*"([^"]*)"\s*,\s*"([^"]*)"\s*,\s*"((?:\\.|[^"\\])*)"\s*\)""")]
    private static partial Regex HotkeyBindingRegex();
    // Host confirmation policy: names observed in native help/editor controls imply data loss or exit.
    // Live uiLoadTriggers replaces the whole set; uiSaveTriggers overwrites its profile-relative output.
    [GeneratedRegex("(saveScenario|LoadTriggers|SaveTriggers|NewScenario|NewMap|Quit|Exit|Restart|ResetMap|ResizeMap|DeleteAll)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DestructiveCommandRegex();
    [GeneratedRegex("[^A-Za-z0-9_]")]
    private static partial Regex InvalidActionNameCharacterRegex();
}
