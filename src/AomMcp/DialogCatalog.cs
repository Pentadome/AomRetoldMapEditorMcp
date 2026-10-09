using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace AomMcp;

/// <summary>One native call parsed from a shipped UI script; Arguments only when every literal matches the native signature.</summary>
internal sealed record DialogCall(string Command, string? Tool, Dictionary<string, object?>? Arguments, bool? RequiresConfirmation, string? Problem);

/// <summary>A shipped UI script and the native calls parsed from it.</summary>
internal sealed record DialogScript(string Script, DialogCall[] Calls, bool? Unparsed);

/// <summary>One menu entry flattened to a "Top > Sub" path.</summary>
internal sealed record DialogMenuEntry(string Path, string? Accelerator, string? ConfigIndex, DialogScript? Script);

/// <summary>One radioset choice.</summary>
internal sealed record DialogItem(string? Label, DialogScript Script);

/// <summary>One XML control inside a window, in document order.</summary>
internal sealed record DialogControl(
    int Index, string Name, string Type, string? Parent, string? Label, string? StringId, string? Title, string? Tooltip,
    double[]? Size1024, bool? DefaultHidden, bool? ReadOnly, string? ValueKind, string? ConfigIndex, string? SliderRange,
    int? States, bool? Dropdown, DialogScript[]? Scripts, DialogItem[]? Items, DialogMenuEntry[]? Menu);

/// <summary>A gadget name that shows (part of) this window, with live status for this UI.</summary>
internal sealed record DialogOpenName(string Name, string Target, string Link, string Status, string Close);

/// <summary>A control or menu entry whose script makes one of this window's open names real/toggled.</summary>
internal sealed record DialogOpener(string Window, string Control, string? Label, string Script);

/// <summary>One top-level gadget of a decoded editor layout file.</summary>
internal sealed record DialogWindow(
    string Ui, string Window, string File, string Type, string? Title, double[]? Size1024, bool? DefaultHidden, bool? Moveable,
    bool? CloseButton, bool? CloseOnEsc, DialogOpenName[] OpenNames, DialogOpener[] OpenedBy, DialogMenuEntry[]? Menu,
    DialogControl[] Controls);

internal sealed record DialogArchive(string Ui, string Archive, long Length, DateTime WriteTimeUtc);

internal sealed record DialogFile(string ExeSha256, long ExeLength, DialogArchive[] Archives, DialogWindow[] Windows, string[] Warnings);

/// <summary>Generates and queries the editor dialog catalog decoded from both shipped editor UI archives; no game connection.</summary>
internal sealed class DialogCatalog
{
    internal const string FileName = "dialogs.json";
    // Installed archives under game/ui (normal = UIDefaultEditor.bar, alternative = UIEditor.bar) and host output
    // directories; normal stays generated/ui because action_* tools are generated from it.
    internal static readonly (string Ui, string Archive, string Directory)[] Interfaces =
    [
        ("normal", "UIDefaultEditor.bar", "ui"),
        ("alternative", "UIEditor.bar", "ui-alternative"),
    ];
    internal static readonly string[] UiValues = ["both", "normal", "alternative"];
    // Host paging policy, same bounds as the other metadata catalogs.
    internal const int MaxLimit = 200;
    const int DefaultWindowLimit = 50;
    const int DefaultControlLimit = 100;
    const int MaxOpenersPerWindow = 12;

    // Engine wrapper gadgets (menu/button gadgetReal names) hosting decoded XML windows; the link is not in the XML.
    // Alternative UI (build 100.19.17020.0): gadgetUnreal(XML name) closed the window opened by the wrapper for every
    // pair below except MapElevationGadget (no visible window there), so those links are "live". Normal-UI links are
    // "xml" when the window's own scripts name the wrapper, else "name" (same XML window name; not observed).
    static readonly (string Wrapper, string? Normal, string? Alternative)[] WrapperWindows =
    [
        ("NewScenarioGadget", "NewScenarioDialog", "NewScenarioDialog"),
        ("PlayerEditDlg", "PlayerEdit", "PlayerEdit"),
        ("FakePlayerEditDlg", "FakePlayerEdit", "FakePlayerEdit"),
        ("AScenarioSummaryGadget", "ScenarioSummaryDialog", "ScenarioSummaryDialog"),
        ("VictoryConditionsGadget", "VictoryConditions", "VictoryConditions"),
        ("ObjectiveEditor", "objectivesDlg", "objectivesDlg"),
        ("ChapterEditor", "chaptersDlg", "chaptersDlg"),
        ("ForbiddenUnitsTechsDlg", "ForbiddenUnitsTechs", "ForbiddenUnitsTechs"),
        ("DiplomacyDialog", "PlayerDiplomacyDialog", "PlayerDiplomacyDialog"),
        ("AMapSizeDialog", "MapSizeDialog", "MapSettings"),
        ("MapElevationGadget", "MapElevationDialog", "MapElevationDialog"),
        ("GodPowersGadget", "GodPowersDlg", "GodPowersDlg"),
        ("DistanceCheckerGadget", "DistanceChecker", "DistanceChecker"),
        ("ObjectInfoPanel", "ObjectInfo", "ObjectInfo"),
        ("UnitRepDlg", "UnitRep", "UnitRep"),
        ("AdvancedTransformSettingsGadget", "AdvancedTransformSettings", "AdvancedTransformSettings"),
        ("AnimationEditorGadget", "animgadget", "animgadget"),
        ("GarrisonEditorGadget", "GarrisonEditor", "GarrisonEditor"),
        ("RelicEditorGadget", "RelicEditor", "RelicEditor"),
        ("ATriggerGadget", "trEditor", "trEditor"),
        ("ATrigGroupEditor", "TriggerGroupEditor", "TriggerGroupEditor"),
        ("ATriggerArmyGadget", "ArmyEditor", "ArmyEditor"),
        ("ScriptManager", "scriptsDlg", "scriptsDlg"),
        ("TriggerEchoWindow", "triggerEcho", "triggerEcho"),
        ("ACameraTrackGadget", "camEditor", "camEditor"),
        ("CameraStatesGadget", "CameraStates", "CameraStates"),
        ("PitchEditorGadget", "pitchEditor", "pitchEditor"),
        ("CameraStartDialog", "CameraStartDlg", "CameraStartDlg"),
        ("StringIdDialog", "StringIdDlg", "StringIdDlg"),
    ];
    static readonly HashSet<string> AlternativeNotLive = new(["MapElevationGadget"], StringComparer.Ordinal);

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    const string Limitation = "Static shipped editor UI XML (English fallback text after $$STR id$$), not live dialog state. size1024 is the source design-space rect [x1,y1,x2,y2] (1024-wide reference with engine anchoring), NOT client pixels. calls are what a button/menu/radio item runs; arguments are filled only when every literal matches the native signature (requiresConfirmation needs confirmDestructive). Value fields without scripts (text/number fields, checkboxes, lists, dropdowns) are read by engine dialog code on Apply/OK and have no native setter: use workflow helpers or checkpoint decoding. openNames status comes from live sweeps on build 100.19.17020.0 (opens/noVisibleEffect/shownByDefault/emptyShell/untested); a native acknowledgement is not proof.";

    readonly DialogFile _file;
    readonly Dictionary<DialogWindow, string> _search = [];

    DialogCatalog(DialogFile file) => _file = file;

    /// <summary>Loads generated dialog metadata without connecting to the game.</summary>
    /// <exception cref="FileNotFoundException">Dialog metadata has not been generated.</exception>
    internal static DialogCatalog Load()
    {
        string? file = null;
        // Prefer repository metadata so regeneration takes effect without another build (same policy as game_catalog.json).
        for (DirectoryInfo? d = new(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "generated", FileName);
            if (File.Exists(candidate))
            {
                file = candidate;
                break;
            }
        }
        file ??= Path.Combine(AppContext.BaseDirectory, FileName);
        if (!File.Exists(file))
            throw new FileNotFoundException("Dialog catalog metadata missing. Run editor_generate_catalog (or --generate generated).");
        return new(JsonSerializer.Deserialize<DialogFile>(File.ReadAllText(file), Json)
            ?? throw new InvalidDataException("Dialog catalog metadata empty."));
    }

    /// <summary>True when generated dialog metadata exists and matches the installed executable/UI archives.</summary>
    internal static bool IsFresh(string exe, string hash)
    {
        try
        {
            Load().CheckFresh(exe, hash);
            return true;
        }
        catch (Exception e) when (e is FileNotFoundException or WorkflowFailure or JsonException or InvalidDataException or NotSupportedException)
        {
            return false;
        }
    }

    void CheckFresh(string exe, string hash)
    {
        var image = new FileInfo(exe);
        // Same identity policy as GameDataCatalog: live executable hash, archive size/UTC write time.
        var stale = _file.ExeSha256 != hash || !image.Exists || image.Length != _file.ExeLength || _file.Archives.Length == 0
            || _file.Archives.Any(a =>
            {
                var archive = new FileInfo(ArchivePath(exe, a.Archive));
                return !archive.Exists || archive.Length != a.Length || archive.LastWriteTimeUtc != a.WriteTimeUtc;
            });
        if (stale)
            throw new WorkflowFailure("METADATA_STALE", "metadata", "Dialog catalog metadata stale (executable hash/length or editor UI archive size/time changed).", false, false,
                "Run editor_generate_catalog (or --generate generated). Metadata read only; no game connection or native command occurred.");
    }

    internal static string ArchivePath(string exe, string archive) =>
        Path.Combine(Path.GetDirectoryName(exe)!, "game", "ui", archive);

    /// <summary>Parses decoded editor XML of each available UI and writes dialogs.json.</summary>
    /// <param name="exe">Installed executable used to tag the metadata.</param>
    /// <param name="output">Metadata directory.</param>
    /// <param name="commands">Native command metadata used to resolve scripts to editor_* tools.</param>
    /// <param name="directories">Decoded XML directory per UI kind; missing UIs are skipped.</param>
    /// <returns>Number of windows written.</returns>
    internal static int Generate(string exe, string output, IReadOnlyDictionary<string, Command> commands, IReadOnlyDictionary<string, string> directories)
    {
        var windows = new List<DialogWindow>();
        var warnings = new List<string>();
        var archives = new List<DialogArchive>();
        foreach (var (ui, archive, _) in Interfaces)
        {
            if (!directories.TryGetValue(ui, out var directory) || !Directory.Exists(directory))
                continue;
            var info = new FileInfo(ArchivePath(exe, archive));
            archives.Add(new(ui, archive, info.Length, info.LastWriteTimeUtc));
            var documents = Directory.EnumerateFiles(directory, "*.xml").Order(StringComparer.Ordinal)
                .Select(path => (Path.GetFileName(path), XDocument.Load(path)));
            windows.AddRange(Build(ui, documents, commands, warnings));
        }
        if (archives.Count == 0)
            throw new InvalidDataException("No decoded editor UI XML available for the dialog catalog.");
        File.WriteAllText(Path.Combine(output, FileName), JsonSerializer.Serialize(
            new DialogFile(Layout.Hash(exe), new FileInfo(exe).Length, archives.ToArray(), windows.ToArray(), warnings.ToArray()), Json));
        return windows.Count;
    }

    static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    static string? Attr(XElement e, string name) => e.Attributes().FirstOrDefault(a => Same(a.Name.LocalName, name))?.Value;

    static bool? Flag(XElement e, string name) => Attr(e, name) is null ? null : true;

    static IEnumerable<XElement> Children(XElement e, string name) => e.Elements().Where(c => Same(c.Name.LocalName, name));

    // Shipped text form "$$STR_ID$$English fallback"; plain text has no string ID.
    static (string? Label, string? StringId) Text(string? raw)
    {
        raw = raw?.Trim();
        if (string.IsNullOrEmpty(raw))
            return (null, null);
        if (raw.StartsWith("$$", StringComparison.Ordinal))
        {
            var end = raw.IndexOf("$$", 2, StringComparison.Ordinal);
            if (end > 2)
            {
                var label = raw[(end + 2)..].Trim();
                return (label.Length == 0 ? null : label, raw[2..end]);
            }
        }
        return (raw, null);
    }

    static double[]? Rect(XElement e)
    {
        var parts = Attr(e, "size1024")?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts is not { Length: 4 })
            return null;
        var values = new double[4];
        for (var i = 0; i < 4; i++)
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
                return null;
        return values;
    }

    static string? DirectText(XElement e) =>
        string.Concat(e.Nodes().OfType<XText>().Select(t => t.Value)).Trim() is { Length: > 0 } text ? text : null;

    /// <summary>Builds windows for one UI from decoded layout documents (testable without installed archives).</summary>
    internal static List<DialogWindow> Build(string ui, IEnumerable<(string File, XDocument Document)> documents,
        IReadOnlyDictionary<string, Command> commands, List<string> warnings)
    {
        var lookup = new Dictionary<string, Command>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in commands.Values)
            lookup.TryAdd(c.Name, c);
        DialogScript Script(string script)
        {
            var (calls, parsed) = ParseScript(script, lookup);
            return new(script, calls, parsed ? null : true);
        }
        DialogMenuEntry[] Menu(XElement menu)
        {
            var entries = new List<DialogMenuEntry>();
            void Walk(XElement node, string prefix)
            {
                foreach (var sub in Children(node, "submenu"))
                {
                    if (Attr(sub, "separator") is not null)
                        continue;
                    var title = Text(Attr(sub, "title")).Label;
                    var path = title is null ? prefix : prefix.Length == 0 ? title : prefix + " > " + title;
                    var script = DirectText(sub);
                    var config = Attr(sub, "configindex");
                    if (script is not null || config is not null)
                        entries.Add(new(path, Attr(sub, "accelerator") is { Length: > 0 } a ? a : null, config, script is null ? null : Script(script)));
                    Walk(sub, path);
                }
            }
            Walk(menu, "");
            return entries.ToArray();
        }
        var windows = new List<(DialogWindow Window, XElement Element)>();
        foreach (var (file, document) in documents)
        {
            if (document.Root is not { } root || !Same(root.Name.LocalName, "layout"))
                continue;
            foreach (var top in Children(root, "gadget"))
            {
                var name = Attr(top, "name");
                if (string.IsNullOrEmpty(name))
                    continue;
                var controls = new List<DialogControl>();
                foreach (var g in top.Descendants().Where(d => Same(d.Name.LocalName, "gadget")))
                {
                    var (label, stringId) = Text(DirectText(g));
                    var type = Attr(g, "type") ?? "gadget";
                    var scripts = Children(g, "command").Select(c => c.Value.Trim()).Where(s => s.Length > 0)
                        .Concat(Attr(g, "command") is { Length: > 0 } attribute ? new[] { attribute } : Array.Empty<string>())
                        .Select(Script).ToArray();
                    var items = Children(g, "button").Where(b => Attr(b, "command") is { Length: > 0 })
                        .Select(b => new DialogItem(Text(DirectText(b)).Label, Script(Attr(b, "command")!))).ToArray();
                    var states = Children(g, "stateentry").Count();
                    var menu = Same(type, "menu") ? Menu(g) : [];
                    var parent = g.Parent is { } p && p != top && Same(p.Name.LocalName, "gadget") ? Attr(p, "name") : null;
                    controls.Add(new(controls.Count, Attr(g, "name") ?? "", type, parent, label, stringId,
                        Text(Attr(g, "titlebar")).Label, Text(Attr(g, "tooltiptext")).Label, Rect(g), Flag(g, "hidden"),
                        Flag(g, "fieldreadonly"),
                        Attr(g, "intfield") is not null ? "int" : Attr(g, "numberfield") is not null ? "number" : null,
                        Attr(g, "configindex"), Attr(g, "sliderminmax"), states == 0 ? null : states,
                        Children(g, "dropdown").Any() ? true : null, scripts.Length == 0 ? null : scripts,
                        items.Length == 0 ? null : items, menu.Length == 0 ? null : menu));
                }
                var windowMenu = Same(Attr(top, "type"), "menu") ? Menu(top) : [];
                windows.Add((new(ui, name, file, Attr(top, "type") ?? "gadget", Text(Attr(top, "titlebar")).Label, Rect(top),
                    Flag(top, "hidden"), Flag(top, "moveable"), Flag(top, "closebutton"), Flag(top, "closeonesc"), [], [],
                    windowMenu.Length == 0 ? null : windowMenu, controls.ToArray()), top));
            }
        }
        // Every gadgetReal/Toggle target named by any script of this UI, with the control that runs it.
        var openers = new List<(string Target, DialogOpener Opener)>();
        foreach (var (window, _) in windows)
        {
            void Add(string control, string? label, DialogScript script)
            {
                foreach (var call in script.Calls)
                    if (call.Command.StartsWith("gadgetReal", StringComparison.OrdinalIgnoreCase)
                        || call.Command.StartsWith("gadgetToggle", StringComparison.OrdinalIgnoreCase))
                        if (call.Arguments?.Values.FirstOrDefault() is string target)
                            openers.Add((target, new(window.Window, control, label, script.Script)));
            }
            foreach (var entry in window.Menu ?? [])
                if (entry.Script is { } s)
                    Add(window.Window, entry.Path, s);
            foreach (var c in window.Controls)
            {
                var label = c.Label ?? c.Tooltip ?? c.Title;
                foreach (var s in c.Scripts ?? [])
                    Add(c.Name, label, s);
                foreach (var item in c.Items ?? [])
                    Add(c.Name, item.Label ?? label, item.Script);
                foreach (var entry in c.Menu ?? [])
                    if (entry.Script is { } s)
                        Add(c.Name, entry.Path, s);
            }
        }
        // Live-tested names that are nested gadgets (e.g. PlayerEditMinorGodDlg in PlayerEdit) open part of a window.
        var tested = Catalog.VerifiedGadgets.Concat(Catalog.NormalOnlyGadgets).Concat(Catalog.AlternativeOnlyGadgets).Select(g => g.Name)
            .Concat(Catalog.NoVisibleEffectGadgets).Concat(Catalog.AlternativeNoVisibleEffectGadgets).Concat(Catalog.ShownByDefaultGadgets)
            .Append("PlayerDataDialog").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var alternative = ui == "alternative";
        // A gadget name resolves to a top-level window first; otherwise to the first nested panel (not a button or
        // field that merely shares the name, e.g. the alternative UpperBar's MapSettings button).
        (int Window, string Target)? Locate(string name)
        {
            for (var w = 0; w < windows.Count; w++)
                if (Same(windows[w].Window.Window, name))
                    return (w, windows[w].Window.Window);
            for (var w = 0; w < windows.Count; w++)
                if (windows[w].Window.Controls.FirstOrDefault(c => Same(c.Name, name) && c.Type is "gadget" or "dialog" or "palette") is { } c)
                    return (w, c.Name);
            return null;
        }
        var result = new List<DialogWindow>();
        for (var index = 0; index < windows.Count; index++)
        {
            var (window, element) = windows[index];
            var open = new List<DialogOpenName> { new(window.Window, window.Window, "self", Status(window.Window, ui), window.Window) };
            foreach (var nested in window.Controls.Select(c => c.Name).Where(n => n.Length > 0 && tested.Contains(n)).Distinct(StringComparer.OrdinalIgnoreCase))
                if (Locate(nested) is { } found && found.Window == index && !Same(found.Target, window.Window))
                    open.Add(new(found.Target, found.Target, "self", Status(found.Target, ui), found.Target));
            foreach (var (wrapper, normal, alt) in WrapperWindows)
            {
                if ((alternative ? alt : normal) is not { } name || Locate(name) is not { } located || located.Window != index)
                    continue;
                var target = located.Target;
                var link = alternative && !AlternativeNotLive.Contains(wrapper) ? "live"
                    : element.DescendantsAndSelf().SelectMany(e => e.Nodes().OfType<XText>().Select(t => t.Value).Concat(e.Attributes().Select(a => a.Value)))
                        .Any(text => text.Contains('"' + wrapper + '"', StringComparison.OrdinalIgnoreCase)) ? "xml" : "name";
                var close = alternative && Catalog.AlternativeCloseNames.TryGetValue(wrapper, out var inner) ? inner : wrapper;
                open.Add(new(wrapper, target, link, Status(wrapper, ui), close));
            }
            var openerNames = open.Select(o => o.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var openedBy = openers.Where(o => openerNames.Contains(o.Target)).Select(o => o.Opener).Take(MaxOpenersPerWindow).ToArray();
            result.Add(window with { OpenNames = open.ToArray(), OpenedBy = openedBy });
        }
        foreach (var (wrapper, normal, alt) in WrapperWindows)
            if ((alternative ? alt : normal) is { } target && Locate(target) is null)
                warnings.Add($"{ui}: wrapper {wrapper} maps to missing XML gadget {target}.");
        return result;
    }

    /// <summary>Live status of a gadget name for one UI from the gadget sweep tables in Catalog.</summary>
    internal static string Status(string name, string ui)
    {
        bool In(IEnumerable<string> names) => names.Contains(name, StringComparer.OrdinalIgnoreCase);
        var alternative = ui == "alternative";
        if (Same(name, "PlayerDataDialog"))
            return alternative ? "untested" : "emptyShell";
        if (In(Catalog.VerifiedGadgets.Select(g => g.Name)))
            return "opens";
        if (In(Catalog.NormalOnlyGadgets.Select(g => g.Name)))
            return alternative ? "noVisibleEffect" : "opens";
        if (In(Catalog.AlternativeOnlyGadgets.Select(g => g.Name)))
            return alternative ? "opens" : "noVisibleEffect";
        if (In(Catalog.NoVisibleEffectGadgets))
            return "noVisibleEffect";
        if (In(Catalog.AlternativeNoVisibleEffectGadgets))
            return alternative ? "noVisibleEffect" : "untested";
        if (In(Catalog.ShownByDefaultGadgets))
            return "shownByDefault";
        return "untested";
    }

    /// <summary>Parses a shipped UI script into calls; stops (Parsed=false) at syntax beyond name(literal, ...) sequences.</summary>
    internal static (DialogCall[] Calls, bool Parsed) ParseScript(string script, IReadOnlyDictionary<string, Command> commands)
    {
        var calls = new List<DialogCall>();
        var i = 0;
        while (true)
        {
            while (i < script.Length && (char.IsWhiteSpace(script[i]) || script[i] == ';'))
                i++;
            if (i >= script.Length)
                return (calls.ToArray(), true);
            if (!(char.IsAsciiLetter(script[i]) || script[i] == '_'))
                return (calls.ToArray(), false);
            var start = i;
            while (i < script.Length && (char.IsAsciiLetterOrDigit(script[i]) || script[i] == '_'))
                i++;
            var name = script[start..i];
            var j = i;
            while (j < script.Length && char.IsWhiteSpace(script[j]))
                j++;
            var raw = new List<string>();
            if (j < script.Length && script[j] == '(')
            {
                var text = new StringBuilder();
                var depth = 0;
                var quoted = false;
                var closed = false;
                var k = j + 1;
                for (; k < script.Length; k++)
                {
                    var c = script[k];
                    if (quoted)
                    {
                        text.Append(c);
                        if (c == '\\' && k + 1 < script.Length)
                            text.Append(script[++k]);
                        else if (c == '"')
                            quoted = false;
                        continue;
                    }
                    if (c == '"')
                        quoted = true;
                    else if (c == '(')
                        depth++;
                    else if (c == ')' && depth-- == 0)
                    {
                        closed = true;
                        break;
                    }
                    else if (c == ',' && depth == 0)
                    {
                        raw.Add(text.ToString().Trim());
                        text.Clear();
                        continue;
                    }
                    text.Append(c);
                }
                if (!closed)
                    return (calls.ToArray(), false);
                var last = text.ToString().Trim();
                if (raw.Count > 0 || last.Length > 0)
                    raw.Add(last);
                i = k + 1;
            }
            calls.Add(Resolve(name, raw, commands));
        }
    }

    static DialogCall Resolve(string name, List<string> raw, IReadOnlyDictionary<string, Command> commands)
    {
        if (Catalog.RemovedCommand(name))
            return new(name, null, null, null, "removedCommand");
        if (!commands.TryGetValue(name, out var command))
            return new(name, null, null, null, "unknownCommand");
        var tool = "editor_" + command.Name;
        bool? confirm = Catalog.Confirmation(command.Name) ? true : null;
        if (raw.Count != command.Parameters.Length)
            return new(command.Name, tool, null, confirm, "argumentCountMismatch");
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var k = 0; k < raw.Count; k++)
        {
            if (!Literal(command.Parameters[k].Type, raw[k], out var value))
                return new(command.Name, tool, null, confirm, "argumentsNotLiteral");
            arguments[command.Parameters[k].Name] = value;
        }
        return new(command.Name, tool, arguments, confirm, null);
    }

    // XS/console literal forms: double-quoted strings with backslash escapes, invariant numbers, true/false.
    // Shipped editor scripts also pass bare gadget names (gadgetUnreal(ShapesDLG)); the name is the string value.
    static bool Literal(string type, string raw, out object? value)
    {
        value = null;
        switch (type)
        {
            case "string" when raw.Length > 0 && (char.IsAsciiLetter(raw[0]) || raw[0] == '_')
                && raw.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-') && raw is not ("true" or "false"):
                value = raw;
                return true;
            case "string" when raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"':
                var body = raw[1..^1];
                var text = new StringBuilder();
                for (var i = 0; i < body.Length; i++)
                {
                    if (body[i] == '\\' && i + 1 < body.Length)
                        text.Append(body[++i]);
                    else if (body[i] == '"')
                        return false;
                    else
                        text.Append(body[i]);
                }
                value = text.ToString();
                return true;
            case "int" when int.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var i32):
                value = i32;
                return true;
            case "float" when double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number):
                value = number;
                return true;
            case "bool" when raw is "true" or "false":
                value = raw == "true";
                return true;
            default:
                return false;
        }
    }

    string SearchText(DialogWindow w)
    {
        if (_search.TryGetValue(w, out var text))
            return text;
        var parts = new List<string?> { w.Window, w.Title, w.File, w.Type };
        parts.AddRange(w.OpenNames.Select(o => o.Name));
        parts.AddRange(w.OpenedBy.Select(o => o.Label));
        parts.AddRange((w.Menu ?? []).SelectMany(m => new[] { m.Path, m.Script?.Script }));
        foreach (var c in w.Controls)
        {
            parts.AddRange([c.Name, c.Label, c.Title, c.Tooltip, c.ConfigIndex]);
            parts.AddRange((c.Scripts ?? []).Select(s => s.Script));
            parts.AddRange((c.Items ?? []).SelectMany(x => new[] { x.Label, x.Script.Script }));
            parts.AddRange((c.Menu ?? []).SelectMany(m => new[] { m.Path, m.Script?.Script }));
        }
        return _search[w] = string.Join('\n', parts.Where(p => !string.IsNullOrEmpty(p)));
    }

    static bool Matches(string text, string[] terms) => terms.All(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));

    static string? ControlText(DialogControl c) =>
        string.Join(" ", new[] { c.Label, c.Title, c.Tooltip }.Where(s => s is not null).Distinct()) is { Length: > 0 } s ? s : null;

    /// <summary>Lists/filters windows, or returns one window's controls by exact window/open name; metadata only.</summary>
    /// <param name="exe">Configured game executable path.</param>
    /// <param name="hash">Accepted executable hash.</param>
    /// <param name="args">Schema-validated ui/name/filter/offset/limit.</param>
    internal object Query(string exe, string hash, JsonElement args)
    {
        CheckFresh(exe, hash);
        string Arg(string key) => args.TryGetProperty(key, out var v) ? v.GetString()!.Trim() : "";
        var ui = Arg("ui") is { Length: > 0 } u ? u : "both";
        if (!UiValues.Contains(ui, StringComparer.Ordinal))
            throw new ArgumentException("ui must be both, normal or alternative.");
        var name = Arg("name");
        var terms = Arg("filter").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var offset = args.TryGetProperty("offset", out var o) ? o.GetInt32() : 0;
        var windows = _file.Windows.Where(w => ui == "both" || w.Ui == ui).ToArray();
        static int? Next(int offset, int count, int total) => offset < total && count < total - offset ? offset + count : null;
        var common = new
        {
            buildHash = hash,
            sources = _file.Archives.Select(a => new { a.Ui, archive = "game/ui/" + a.Archive }),
            limitation = Limitation,
        };
        if (name.Length > 0)
        {
            var limit = args.TryGetProperty("limit", out var l) ? l.GetInt32() : DefaultControlLimit;
            var matches = windows.Where(w => Same(w.Window, name) || w.OpenNames.Any(n => Same(n.Name, name))).ToArray();
            if (matches.Length == 0)
                throw new ArgumentException($"No editor window or open name '{name}'"
                    + (ui == "both" ? "" : $" in the {ui} UI")
                    + ". Engine-only gadgets (e.g. AGameMinimap, BrushMaskDialog) have no decoded XML window. Close names: "
                    + string.Join(", ", GameDataCatalog.Suggest(name, windows.SelectMany(w => w.OpenNames.Select(n => n.Name).Append(w.Window)).Distinct(StringComparer.OrdinalIgnoreCase))));
            return JsonSerializer.SerializeToElement(new
            {
                name,
                ui,
                windows = matches.Select(w =>
                {
                    var controls = w.Controls.Skip(offset).Take(limit).ToArray();
                    return new
                    {
                        w.Ui, w.Window, w.File, w.Type, w.Title, w.Size1024, w.DefaultHidden, w.Moveable, w.CloseButton, w.CloseOnEsc,
                        w.OpenNames, w.OpenedBy, w.Menu,
                        controlsTotal = w.Controls.Length,
                        offset,
                        limit,
                        nextOffset = Next(offset, controls.Length, w.Controls.Length),
                        controls,
                    };
                }).ToArray(),
                common.buildHash,
                common.sources,
                common.limitation,
            }, Json);
        }
        var pageLimit = args.TryGetProperty("limit", out var pl) ? pl.GetInt32() : DefaultWindowLimit;
        var filtered = terms.Length == 0 ? windows : windows.Where(w => Matches(SearchText(w), terms)).ToArray();
        var entries = filtered.Skip(offset).Take(pageLimit).Select(w => new
        {
            w.Ui,
            w.Window,
            w.Title,
            w.Type,
            w.File,
            w.DefaultHidden,
            openNames = w.OpenNames.Select(n => new { n.Name, n.Status, n.Close, n.Link }),
            openedBy = w.OpenedBy.Select(b => b.Label ?? b.Control).Distinct().Take(3).ToArray(),
            controls = w.Controls.Length,
            scriptedControls = w.Controls.Count(c => c.Scripts is not null || c.Items is not null || c.Menu is not null),
            matches = terms.Length == 0 ? null : w.Controls
                .Where(c => Matches(string.Join('\n', new[] { c.Name, c.Label, c.Title, c.Tooltip }.Where(s => s is not null)), terms)
                    || (c.Menu ?? []).Any(m => Matches(m.Path, terms)))
                .Select(c => ControlText(c) is { } t ? $"{t} ({c.Name})" : c.Name)
                .Concat((w.Menu ?? []).Where(m => Matches(m.Path, terms)).Select(m => m.Path)).Distinct().Take(5).ToArray(),
        }).ToArray();
        return JsonSerializer.SerializeToElement(new
        {
            ui,
            filter = terms.Length == 0 ? null : string.Join(' ', terms),
            total = filtered.Length,
            offset,
            limit = pageLimit,
            nextOffset = Next(offset, entries.Length, filtered.Length),
            entries,
            common.buildHash,
            common.sources,
            warnings = _file.Warnings,
            common.limitation,
        }, Json);
    }

    /// <summary>Script parsing, wrapper linking and status checks on synthetic XML; no installed game needed.</summary>
    internal static void SelfTest()
    {
        var commands = new Dictionary<string, Command>(StringComparer.Ordinal)
        {
            ["gadgetReal"] = new("gadgetReal", "void", [new("string", "name")], ""),
            ["gadgetUnreal"] = new("gadgetUnreal", "void", [new("string", "name")], ""),
            ["uiSaveScenarioPrompt"] = new("uiSaveScenarioPrompt", "void", [new("bool", "saveAs")], ""),
            ["uiSetTerrainDetailPaintMode"] = new("uiSetTerrainDetailPaintMode", "void", [new("int", "mode")], ""),
            ["uiNewScenario"] = new("uiNewScenario", "void", [], ""),
        };
        var lookup = new Dictionary<string, Command>(commands, StringComparer.OrdinalIgnoreCase);
        var (calls, parsed) = ParseScript("gadgetUnReal(\"A\\\"B\") ; uiSaveScenarioPrompt(true) uiSaveScenarioPrompt openX();", lookup);
        if (!parsed || calls.Length != 4
            || calls[0].Tool != "editor_gadgetUnreal" || (string?)calls[0].Arguments!["name"] != "A\"B"
            || calls[1].Arguments!["saveAs"] is not true
            || calls[2].Problem != "argumentCountMismatch" || calls[3].Problem != "unknownCommand")
            throw new InvalidOperationException("Dialog script parsing self-test failed.");
        if (ParseScript("gadgetReal(\"x\" + y)", lookup).Calls[0].Problem != "argumentsNotLiteral"
            || (string?)ParseScript("gadgetunReal(Shapes-DLG)", lookup).Calls[0].Arguments?["name"] != "Shapes-DLG"
            || ParseScript("a = b", lookup).Parsed || ParseScript("gadgetReal(\"x\"", lookup).Parsed
            || ParseScript("loadScenario(\"x\")", lookup).Calls[0].Problem != "removedCommand"
            || ParseScript("uiNewScenario()", lookup).Calls[0].RequiresConfirmation != true)
            throw new InvalidOperationException("Dialog script refusal self-test failed.");
        var xml = XDocument.Parse("""
            <layout>
              <gadget name="PlayerDiplomacyDialog" type="gadget" titlebar="$$STR_D$$Diplomacy" hidden="" closebutton="" size1024="140 50 860 600">
                <gadget name="P0P1" type="stateButton" size1024="1 2 3 4"><stateentry/><stateentry/></gadget>
                <gadget name="Mode" type="radioset"><button command="uiSetTerrainDetailPaintMode(1)">$$STR_W$$Wetness</button></gadget>
                <gadget name="Count" type="textField" numberfield="" configindex="cfg">$$STR_C$$Count<dropdown></dropdown></gadget>
                <gadget name="Close" type="button" tooltiptext="Close it">$$STR_X$$Close<command>gadgetUnReal("PlayerDiplomacyDialog")</command></gadget>
              </gadget>
              <gadget name="UpperBar" type="gadget"><gadget name="trEditor" type="button"><command>gadgetReal("ATriggerGadget")</command></gadget></gadget>
              <gadget name="EditorMenu" type="menu">
                <submenu title="$$STR_S$$Scenario"><submenu title="$$STR_D$$Diplomacy">gadgetReal("DiplomacyDialog")</submenu><submenu title="" separator=""></submenu></submenu>
              </gadget>
            </layout>
            """);
        var warnings = new List<string>();
        var alt = Build("alternative", [("ui_test.xml", xml)], commands, warnings);
        var normal = Build("normal", [("ui_test.xml", xml)], commands, []);
        var dialog = alt.Single(w => w.Window == "PlayerDiplomacyDialog");
        var wrapper = dialog.OpenNames.Single(n => n.Name == "DiplomacyDialog");
        var menu = alt.Single(w => w.Window == "EditorMenu").Menu!.Single();
        var count = dialog.Controls.Single(c => c.Name == "Count");
        if (dialog.Title != "Diplomacy" || dialog.DefaultHidden != true || dialog.Controls.Length != 4
            || dialog.Controls[0].States != 2 || dialog.Controls[1].Items![0].Script.Calls[0].Arguments!["mode"] is not 1
            || count.ValueKind != "number" || count.ConfigIndex != "cfg" || count.Dropdown != true || count.Label != "Count" || count.StringId != "STR_C"
            || wrapper.Link != "live" || wrapper.Close != "PlayerDiplomacyDialog" || wrapper.Status != "opens"
            || normal.Single(w => w.Window == "PlayerDiplomacyDialog").OpenNames.Single(n => n.Name == "DiplomacyDialog") is not { Link: "name", Close: "DiplomacyDialog" }
            || dialog.OpenNames[0].Status != "opens" || normal.Single(w => w.Window == "PlayerDiplomacyDialog").OpenNames[0].Status != "noVisibleEffect"
            || menu.Path != "Scenario > Diplomacy" || dialog.OpenedBy.Single().Label != "Scenario > Diplomacy"
            || !warnings.Any(w => w.Contains("maps to missing XML gadget trEditor", StringComparison.Ordinal)) // Only a button has that name.
            || alt.Single(w => w.Window == "UpperBar").OpenNames.Length != 1)
            throw new InvalidOperationException("Dialog catalog build self-test failed.");
        if (Status("TerraMode", "alternative") != "shownByDefault" || Status("PlayerDataDialog", "normal") != "emptyShell"
            || Status("MapElevationGadget", "alternative") != "noVisibleEffect" || Status("name-dialog", "alternative") != "opens")
            throw new InvalidOperationException("Dialog status self-test failed.");
    }
}
