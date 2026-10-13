using System.Globalization;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AomMcp;

/// <summary>
/// Builds a local mod that shows saved scenarios as a custom campaign:
/// mods\local\&lt;mod&gt;\game\campaign\&lt;id&gt;\&lt;id&gt;.xml, byte-identical scenario copies and
/// game\data\strings\english\stringmods.txt. Layout mirrors the Workshop "Hero of Beausejour Retold" mod.
/// bundleXs copies profile-only XS the scenarios need, mirroring the profile layout at the mod root
/// (profile Game\AI\&lt;rel&gt; → game\ai\&lt;rel&gt;, profile trigger\&lt;rel&gt; → trigger\&lt;rel&gt;), as in the user's working om10 fix.
/// Preview-first, hash-pinned sources; create makes a NEW mod folder, update replaces only hash-pinned planned files
/// after an external backup and never deletes. No cleanup/rollback, no game connection.
/// </summary>
internal static partial class CampaignBuilder
{
    const long MaxScenarioBytes = 512L * 1024 * 1024;
    const string Latest = "latest";
    static readonly string[] Keys =
    [
        "modsDirectory",
        "modName",
        "campaignId",
        "displayName",
        "description",
        "motifImage",
        "mapImage",
        "scenarios",
        "preview",
        "confirmWrite",
        "operation",
        "bundleXs",
        "allowIncomplete",
        "expectedInstalled",
        "backupDirectory",
        "confirmDestructive",
    ];
    static readonly string[] ScenarioKeys =
    [
        "sourcePath",
        "expectedSha256",
        "fileName",
        "title",
        "premise",
        "cinematic",
        "backgroundImage",
        "loadImage",
        "pinImage",
        "mapImage",
        "pinX",
        "pinY",
    ];

    [GeneratedRegex("^[a-z0-9_]{1,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex Id();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9 _.-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex Name();

    // Resource paths in the XML: stock-style relative paths only.
    [GeneratedRegex(@"^[A-Za-z0-9_./\\-]{1,120}$", RegexOptions.CultureInvariant)]
    private static partial Regex Resource();

    internal sealed record Scenario(
        string Source,
        string Sha256,
        long Bytes,
        string Stem,
        string? Title,
        string? Premise,
        bool Cinematic,
        string? Background,
        string? Load,
        string? Pin,
        string? Map,
        double PinX,
        double PinY,
        bool FromLatest,
        DateTime ModifiedUtc
    );

    /// <summary>Relative is mod-root relative; Source (file copy) or Content (generated) supplies the bytes; Action create|replace|unchanged.</summary>
    internal sealed record PlannedFile(
        string Relative,
        string Kind,
        string? Source,
        byte[]? Content,
        string Sha256,
        long Bytes,
        string Action,
        string? InstalledSha256
    );

    internal sealed record Plan(
        string ModRoot,
        string CampaignDir,
        string Id,
        string Operation,
        bool Preview,
        string Xml,
        string Strings,
        PlannedFile[] Files,
        Scenario[] Scenarios,
        object[] Dependencies,
        bool? SelfContained,
        object[] MissingFromMod,
        string[] Unmanaged,
        string[] Warnings,
        string? BackupDirectory
    );

    static string? Optional(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    static bool Flag(JsonElement e, string key, bool fallback) =>
        e.TryGetProperty(key, out var v) ? v.ValueKind == JsonValueKind.True : fallback;

    static string Text(string? value, string key, int max)
    {
        if (
            string.IsNullOrWhiteSpace(value)
            || value.Length > max
            || value.Any(c => c is '"' or '\r' or '\n' or '\0' || char.IsControl(c))
        )
            throw new ArgumentException(
                key + " must be 1.." + max + " characters without quotes or line breaks."
            );
        return value.Trim();
    }

    static string? Res(string? value, string key)
    {
        if (value is null)
            return null;
        if (!Resource().IsMatch(value) || value.Split('/', '\\').Any(s => s is "" or "." or ".."))
            throw new ArgumentException(
                key + " must be a relative game resource path (no traversal)."
            );
        return value.Replace('/', '\\');
    }

    static string ModsDirectory(JsonElement args)
    {
        if (Optional(args, "modsDirectory") is { } supplied)
        {
            var dir = EditorFiles.LocalPath(supplied.TrimEnd('\\', '/') + "\\x");
            dir = Path.GetDirectoryName(dir)!;
            if (
                !Directory.Exists(dir)
                || !string.Equals(
                    Path.GetFileName(dir),
                    "local",
                    StringComparison.OrdinalIgnoreCase
                )
            )
                throw new ArgumentException(
                    "modsDirectory must be an existing '...\\mods\\local' directory."
                );
            return dir;
        }
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Games",
            "Age of Mythology Retold"
        );
        var candidates = Directory.Exists(root)
            ? Directory
                .GetDirectories(root)
                .Select(d => Path.Combine(d, "mods", "local"))
                .Where(Directory.Exists)
                .ToArray()
            : [];
        if (candidates.Length != 1)
            throw new InvalidDataException(
                "Cannot identify one profile mods\\local directory (found "
                    + candidates.Length
                    + "); supply modsDirectory."
            );
        return EditorFiles.LocalPath(candidates[0]);
    }

    /// <summary>Profile root &lt;id&gt; when modsDirectory is &lt;id&gt;\mods\local; otherwise null (no profile XS/latest lookup).</summary>
    static string? ProfileFor(string modsLocal)
    {
        var mods = Path.GetDirectoryName(modsLocal);
        return
            mods is not null
            && Path.GetFileName(mods).Equals("mods", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(mods)
            : null;
    }

    static string? Existing(string path) =>
        Directory.Exists(path) ? EditorFiles.LocalPath(Path.GetFullPath(path)) : null;

    static string Sanitize(string value, string pattern, int max) =>
        Regex.Replace(value, pattern, "_").Trim('_', ' ', '.') is var s && s.Length > max
            ? s[..max]
            : s;

    internal static void Preflight(JsonElement args, string exe) => _ = Build(args, exe);

    internal static Plan Build(JsonElement args, string exe)
    {
        Catalog.ValidateObject(args, Keys);
        var preview = !args.TryGetProperty("preview", out var p) || p.GetBoolean();
        if (!preview)
            EditorFiles.Confirm(args, "confirmWrite");
        else if (args.TryGetProperty("confirmWrite", out _))
            throw new ArgumentException("Preview does not accept confirmWrite (no write).");
        var operation = Optional(args, "operation") ?? "create";
        if (operation is not ("create" or "update"))
            throw new ArgumentException("operation must be create or update.");
        var bundleXs = Flag(args, "bundleXs", true);
        var allowIncomplete = Flag(args, "allowIncomplete", false);
        if (
            operation == "create"
            && (
                args.TryGetProperty("expectedInstalled", out _)
                || args.TryGetProperty("backupDirectory", out _)
                || args.TryGetProperty("confirmDestructive", out _)
            )
        )
            throw new ArgumentException(
                "expectedInstalled/backupDirectory/confirmDestructive belong to operation=update."
            );
        if (preview && args.TryGetProperty("confirmDestructive", out _))
            throw new ArgumentException("Preview does not accept confirmDestructive (no write).");
        if (
            !args.TryGetProperty("scenarios", out var list)
            || list.ValueKind != JsonValueKind.Array
            || list.GetArrayLength() is < 1 or > 40
        )
            throw new ArgumentException("scenarios must list 1..40 entries.");

        var modsRoot = ModsDirectory(args);
        var profile = ProfileFor(modsRoot);
        var warnings = new List<string>();
        if (profile is null)
            warnings.Add(
                "modsDirectory is not <profile>\\mods\\local: profile Game\\AI / trigger lookups and sourcePath=\"latest\" are unavailable."
            );

        var scenarios = new List<Scenario>();
        var stems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visible = 0;
        foreach (var e in list.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("scenario entries must be objects.");
            Catalog.ValidateObject(e, ScenarioKeys);
            var requested =
                Optional(e, "sourcePath")
                ?? throw new ArgumentException("scenario.sourcePath required.");
            var fromLatest = requested.Equals(Latest, StringComparison.OrdinalIgnoreCase);
            if (fromLatest && !preview)
                throw new ArgumentException(
                    "sourcePath=\"latest\" is preview-only; apply with the resolved sourcePath and expectedSha256 from the preview."
                );
            if (fromLatest && profile is null)
                throw new ArgumentException(
                    "sourcePath=\"latest\" needs modsDirectory under <profile>\\mods\\local."
                );
            var source = fromLatest
                ? ScenarioFiles.Latest(Path.Combine(profile!, "scenario")).FullName
                : EditorFiles.LocalPath(requested);
            if (
                !source.EndsWith(".mythscn", StringComparison.OrdinalIgnoreCase)
                || !sources.Add(source)
            )
                throw new ArgumentException(
                    "scenario.sourcePath must be a distinct existing .mythscn: " + source
                );
            var info = new FileInfo(source);
            if (!info.Exists || info.Length is < 1 or > MaxScenarioBytes)
                throw new ArgumentException("Scenario missing/empty/over 512 MB: " + source);
            var expected = Optional(e, "expectedSha256");
            if (expected is null && !preview)
                throw new ArgumentException(
                    "scenario.expectedSha256 (64 hex) required to apply " + source
                );
            if (expected is not null && (expected.Length != 64 || !expected.All(Uri.IsHexDigit)))
                throw new ArgumentException("scenario.expectedSha256 must be 64 hex: " + source);
            var hash = Layout.Hash(source);
            CheckpointDocument.RequireHash(hash, expected);
            var stem = Optional(e, "fileName") ?? Path.GetFileNameWithoutExtension(source);
            if (stem.EndsWith(".mythscn", StringComparison.OrdinalIgnoreCase))
                stem = stem[..^8];
            if (
                !Name().IsMatch(stem)
                || stem.EndsWith('.')
                || stem.EndsWith(' ')
                || !stems.Add(stem)
            )
                throw new ArgumentException(
                    "scenario.fileName must be a distinct plain name (letters, digits, space, _ . -): "
                        + stem
                );
            var cinematic =
                e.TryGetProperty("cinematic", out var c) && c.ValueKind == JsonValueKind.True;
            string? title = null,
                premise = null;
            if (!cinematic)
            {
                visible++;
                title = Text(Optional(e, "title") ?? stem, "scenario.title", 80);
                premise = Text(Optional(e, "premise") ?? title, "scenario.premise", 600);
            }
            else if (Optional(e, "title") is not null || Optional(e, "premise") is not null)
                throw new ArgumentException(
                    "cinematic entries are hidden and take no title/premise."
                );
            double Pos(string key, double fallback) =>
                e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number
                    ? v.GetDouble() is var d && d is >= 0 and <= 1
                        ? d
                        : throw new ArgumentException(key + " must be 0..1.")
                    : fallback;
            scenarios.Add(
                new(
                    source,
                    hash,
                    info.Length,
                    stem,
                    title,
                    premise,
                    cinematic,
                    Res(Optional(e, "backgroundImage"), "backgroundImage"),
                    Res(Optional(e, "loadImage"), "loadImage"),
                    Res(Optional(e, "pinImage"), "pinImage"),
                    Res(Optional(e, "mapImage"), "mapImage"),
                    Pos("pinX", 0.5),
                    Pos("pinY", 0.5),
                    fromLatest,
                    info.LastWriteTimeUtc
                )
            );
        }
        if (visible == 0)
            throw new ArgumentException("At least one non-cinematic scenario is required.");

        // Defaults: the first scenario's folder name (e.g. scenario\om10-betrayed\…), else its file stem.
        var first = scenarios[0].Source;
        var folder = Path.GetFileName(Path.GetDirectoryName(first)!);
        var fallbackName =
            profile is not null
            && Path.GetDirectoryName(first)!
                .Equals(Path.Combine(profile, "scenario"), StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileNameWithoutExtension(first)
                : folder;
        var modName = Optional(args, "modName") ?? Sanitize(fallbackName, "[^A-Za-z0-9 _.-]+", 64);
        if (!Name().IsMatch(modName) || modName.EndsWith('.') || modName.EndsWith(' '))
            throw new ArgumentException(
                "modName must be a plain folder name (letters, digits, space, _ . -): " + modName
            );
        var id =
            Optional(args, "campaignId") ?? Sanitize(modName.ToLowerInvariant(), "[^a-z0-9_]+", 32);
        if (!Id().IsMatch(id))
            throw new ArgumentException(
                "campaignId must be 1..32 chars of a-z 0-9 _ (becomes the folder/XML stem and string-ID prefix): "
                    + id
            );
        var upper = id.ToUpperInvariant();
        var display = Text(Optional(args, "displayName") ?? modName, "displayName", 80);
        var description = Text(Optional(args, "description") ?? display, "description", 600);
        var motif =
            Res(Optional(args, "motifImage"), "motifImage")
            ?? @"resources\front_end_demo\Foil_TNA.png";
        var campaignMap =
            Res(Optional(args, "mapImage"), "mapImage") ?? @"ui\campaign_map\map_new_atlantis";

        var modRoot = Path.GetFullPath(Path.Combine(modsRoot, modName));
        if (!modRoot.StartsWith(modsRoot.TrimEnd('\\') + '\\', StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("modName escaped mods\\local.");
        if (operation == "create" && (Directory.Exists(modRoot) || File.Exists(modRoot)))
            throw new ArgumentException(
                "Mod folder already exists; operation=create only makes NEW mods. Use operation=update to refresh it: "
                    + modRoot
            );
        if (
            operation == "update"
            && (
                !Directory.Exists(modRoot)
                || new DirectoryInfo(modRoot).Attributes.HasFlag(FileAttributes.ReparsePoint)
            )
        )
            throw new ArgumentException(
                "operation=update needs an existing (non-junction) mod folder: " + modRoot
            );
        var campaignDir = Path.Combine(modRoot, "game", "campaign", id);

        // XS dependencies of every scenario, resolved profile → existing mod (update) → stock install.
        var roots = new ScenarioDependencies.Roots(
            profile is null ? null : Existing(Path.Combine(profile, "Game", "AI")),
            profile is null ? null : Existing(Path.Combine(profile, "trigger")),
            operation == "update" ? Existing(Path.Combine(modRoot, "game", "ai")) : null,
            operation == "update" ? Existing(Path.Combine(modRoot, "trigger")) : null,
            Existing(Path.Combine(Path.GetDirectoryName(exe)!, "game", "ai")),
            ScenarioDependencies.StockTriggers(exe)
        );
        var dependencies = new List<object>();
        var missing = new List<object>();
        var bundled = new Dictionary<string, ScenarioDependencies.Node>(
            StringComparer.OrdinalIgnoreCase
        );
        var readable = true;
        foreach (var s in scenarios)
        {
            var document = CheckpointDocument.Read(s.Source, s.Sha256);
            var report = ScenarioDependencies.Analyze(document, roots);
            readable &= report.Readable;
            warnings.AddRange(report.Warnings.Select(w => s.Stem + ": " + w));
            foreach (var n in report.Nodes)
            {
                var target = (n.Kind == "ai" ? @"game\ai\" : @"trigger\") + n.Relative;
                string? problem = n.From switch
                {
                    "missing" => "not found in profile, mod or stock install",
                    "profile" when !bundleXs => roots.StockAi is not null
                    && n.Kind == "ai"
                    && File.Exists(AiScripts.Contained(roots.StockAi, n.Relative))
                        ? "profile-only copy; with only the mod enabled the game would load the different stock file"
                        : "profile-only; not in the mod or stock install",
                    _ => null,
                };
                if (n.From == "profile" && bundleXs)
                {
                    if (n.Sha256 is null)
                        throw new InvalidDataException("Cannot bundle XS over 1 MB: " + n.Path);
                    if (bundled.TryGetValue(target, out var prior) && prior.Sha256 != n.Sha256)
                        throw new InvalidDataException("Conflicting bundle sources for " + target);
                    bundled[target] = n;
                }
                if (problem is not null)
                    missing.Add(
                        new
                        {
                            scenario = s.Stem,
                            kind = n.Kind,
                            relative = n.Relative,
                            modPath = target,
                            via = n.Via,
                            problem,
                        }
                    );
            }
            dependencies.Add(
                new
                {
                    scenario = s.Stem,
                    readable = report.Readable,
                    reason = report.Reason,
                    players = report
                        .Players.Select(pl => new
                        {
                            player = pl.Player,
                            name = pl.Name,
                            control = pl.Control,
                            aiPath = pl.AiPath,
                        })
                        .ToArray(),
                    triggerScripts = report
                        .Scripts.Select(t => new
                        {
                            name = t.Name,
                            embedded = t.Embedded,
                            lines = t.Lines.Length,
                        })
                        .ToArray(),
                    files = report
                        .Nodes.Select(n => new
                        {
                            kind = n.Kind,
                            relative = n.Relative,
                            resolvedFrom = n.From,
                            path = n.Path,
                            sha256 = n.Sha256,
                            via = n.Via,
                            includes = n.Includes,
                        })
                        .ToArray(),
                }
            );
        }
        bool? selfContained = readable ? missing.Count == 0 : (missing.Count == 0 ? null : false);
        if (!readable)
            warnings.Add(
                "Some scenario XS dependencies could not be decoded; selfContained cannot be proven."
            );
        if (selfContained == false && !preview && !allowIncomplete)
            throw new WorkflowFailure(
                "MOD_NOT_SELF_CONTAINED",
                "preflight",
                $"{missing.Count} XS dependencies would not resolve with only this mod enabled (e.g. {JsonSerializer.Serialize(missing[0])}).",
                false,
                false,
                "Keep bundleXs=true (default) so profile XS is copied into the mod, restore missing files, or pass allowIncomplete=true to publish anyway. Nothing was written."
            );

        // Campaign XML and strings.
        var xml = new StringBuilder();
        var strings = new StringBuilder();
        xml.Append("<?xml version = \"1.0\" encoding = \"UTF-8\"?>\r\n<Campaign>\r\n");
        xml.Append(
            CultureInfo.InvariantCulture,
            $"    <DisplayNameStringID>STR_{upper}_MENU_CAMPAIGN</DisplayNameStringID>\r\n"
        );
        xml.Append(
            CultureInfo.InvariantCulture,
            $"    <RolloverStringID>STR_{upper}_MENU_CAMPAIGN_DESC</RolloverStringID>\r\n"
        );
        xml.Append(
            CultureInfo.InvariantCulture,
            $"    <MotifImage>{SecurityElement.Escape(motif)}</MotifImage>\r\n    <MapImage>{SecurityElement.Escape(campaignMap)}</MapImage>\r\n"
        );
        strings.Append("Language = \"English\"\r\nIsRtl = \"False\"\r\n\r\n");
        strings.Append(
            CultureInfo.InvariantCulture,
            $"ID = \"STR_{upper}_MENU_CAMPAIGN\"   ;   Str = \"{display}\"\r\n"
        );
        strings.Append(
            CultureInfo.InvariantCulture,
            $"ID = \"STR_{upper}_MENU_CAMPAIGN_DESC\"   ;   Str = \"{description}\"\r\n"
        );
        string? previous = null;
        var number = 0;
        foreach (var s in scenarios)
        {
            var file = id + "\\" + s.Stem;
            xml.Append("\r\n    <CampaignNode>\r\n");
            xml.Append(
                CultureInfo.InvariantCulture,
                $"        <Filename>{SecurityElement.Escape(file)}</Filename>\r\n"
            );
            if (s.Cinematic)
                xml.Append("        <Visible>0</Visible>\r\n        <Cinematic>1</Cinematic>\r\n");
            else
            {
                number++;
                var n = number.ToString("00", CultureInfo.InvariantCulture);
                if (previous is not null)
                    xml.Append(
                        CultureInfo.InvariantCulture,
                        $"        <PrereqScenarioFile>{SecurityElement.Escape(previous)}</PrereqScenarioFile>\r\n"
                    );
                xml.Append(
                    CultureInfo.InvariantCulture,
                    $"        <DisplayNameStringID>STR_{upper}_MENU_TITLE_{n}</DisplayNameStringID>\r\n        <RolloverStringID>STR_{upper}_MENU_PREMISE_{n}</RolloverStringID>\r\n"
                );
                var bg = s.Background ?? $@"resources\campaign\tna\screen{Math.Min(number, 5)}.png";
                xml.Append(
                    CultureInfo.InvariantCulture,
                    $"        <BackgroundImage>{SecurityElement.Escape(bg)}</BackgroundImage>\r\n        <LoadImage>{SecurityElement.Escape(s.Load ?? bg)}</LoadImage>\r\n"
                );
                xml.Append(
                    CultureInfo.InvariantCulture,
                    $"        <PinImage>{SecurityElement.Escape(s.Pin ?? @"ui\campaign_map\pins\37_ALostPeople_512")}</PinImage>\r\n"
                );
                xml.Append(
                    CultureInfo.InvariantCulture,
                    $"        <MapImageOverride>{SecurityElement.Escape(s.Map ?? campaignMap)}</MapImageOverride>\r\n"
                );
                xml.Append(
                    CultureInfo.InvariantCulture,
                    $"        <PinXPosition>{s.PinX.ToString("0.###", CultureInfo.InvariantCulture)}</PinXPosition>\r\n        <PinYPosition>{s.PinY.ToString("0.###", CultureInfo.InvariantCulture)}</PinYPosition>\r\n        <PinZoom>1.0</PinZoom>\r\n"
                );
                strings.Append(
                    CultureInfo.InvariantCulture,
                    $"\r\nID = \"STR_{upper}_MENU_TITLE_{n}\"   ;   Str = \"{s.Title}\"\r\n"
                );
                strings.Append(
                    CultureInfo.InvariantCulture,
                    $"ID = \"STR_{upper}_MENU_PREMISE_{n}\"   ;   Str = \"{s.Premise}\"\r\n"
                );
                previous = file;
            }
            xml.Append("    </CampaignNode>\r\n");
        }
        xml.Append("</Campaign>\r\n");
        var stringText = strings.ToString();
        var xmlText = xml.ToString();
        var xmlBytes = new UTF8Encoding(true)
            .GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(xmlText))
            .ToArray();
        var stringBytes = stringText.Any(ch => ch > 127)
            ? new UTF8Encoding(true)
                .GetPreamble()
                .Concat(Encoding.UTF8.GetBytes(stringText))
                .ToArray()
            : Encoding.ASCII.GetBytes(stringText);

        // Update reuses the mod's single existing stringmods.txt folder (e.g. strings\language) so IDs are not defined twice.
        var language = "english";
        if (operation == "update")
        {
            var stringsRoot = Path.Combine(modRoot, "game", "data", "strings");
            var existingStrings = Directory.Exists(stringsRoot)
                ? Directory.GetFiles(stringsRoot, "stringmods.txt", SearchOption.AllDirectories)
                : [];
            if (
                existingStrings.Length == 1
                && Path.GetDirectoryName(Path.GetDirectoryName(existingStrings[0])) is { } parent
                && parent.Equals(stringsRoot, StringComparison.OrdinalIgnoreCase)
            )
                language = Path.GetFileName(Path.GetDirectoryName(existingStrings[0])!);
            else if (existingStrings.Length > 1)
                warnings.Add(
                    "Mod has several stringmods.txt files; writing strings\\english\\stringmods.txt. Check for duplicate string IDs."
                );
        }

        var planned =
            new List<(
                string Relative,
                string Kind,
                string? Source,
                byte[]? Content,
                string Sha256,
                long Bytes
            )>();
        string CampaignRelative(string name) =>
            Path.GetRelativePath(modRoot, Path.Combine(campaignDir, name));
        foreach (var s in scenarios)
            planned.Add(
                (
                    CampaignRelative(s.Stem + ".mythscn"),
                    "scenario",
                    s.Source,
                    null,
                    s.Sha256,
                    s.Bytes
                )
            );
        planned.Add(
            (
                CampaignRelative(id + ".xml"),
                "campaignXml",
                null,
                xmlBytes,
                CheckpointDocument.Hash(xmlBytes),
                xmlBytes.Length
            )
        );
        planned.Add(
            (
                Path.Combine("game", "data", "strings", language, "stringmods.txt"),
                "stringMods",
                null,
                stringBytes,
                CheckpointDocument.Hash(stringBytes),
                stringBytes.Length
            )
        );
        foreach (var (target, n) in bundled.OrderBy(b => b.Key, StringComparer.OrdinalIgnoreCase))
            planned.Add(
                (target, n.Kind == "ai" ? "aiXs" : "triggerXs", n.Path, null, n.Sha256!, n.Bytes)
            );
        var duplicate = planned
            .GroupBy(f => f.Relative, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException("Two planned files share a destination: " + duplicate.Key);

        var files = new List<PlannedFile>();
        foreach (var f in planned)
        {
            var destination = AiScripts.Contained(modRoot, f.Relative);
            if (Directory.Exists(destination))
                throw new ArgumentException("Planned file path is a directory: " + destination);
            string action = "create";
            string? installed = null;
            if (File.Exists(destination))
            {
                if (new FileInfo(destination).Attributes.HasFlag(FileAttributes.ReparsePoint))
                    throw new ArgumentException(
                        "Refusing reparse-point destination: " + destination
                    );
                installed = Layout.Hash(destination);
                action = installed.Equals(f.Sha256, StringComparison.OrdinalIgnoreCase)
                    ? "unchanged"
                    : "replace";
            }
            files.Add(
                new(f.Relative, f.Kind, f.Source, f.Content, f.Sha256, f.Bytes, action, installed)
            );
        }

        var unmanaged = Array.Empty<string>();
        string? backupDirectory = null;
        if (operation == "update")
        {
            var plannedSet = files
                .Select(f => f.Relative)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
                MaxRecursionDepth = 16,
            };
            unmanaged = Directory
                .EnumerateFiles(modRoot, "*", options)
                .Take(2000)
                .Select(f => Path.GetRelativePath(modRoot, f))
                .Where(r => !plannedSet.Contains(r))
                .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var replaces = files.Where(f => f.Action == "replace").ToArray();
            if (!preview)
            {
                var pins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (args.TryGetProperty("expectedInstalled", out var pinList))
                    foreach (var pin in pinList.EnumerateArray())
                    {
                        var key = (pin.GetProperty("path").GetString() ?? "").Replace('/', '\\');
                        if (!pins.TryAdd(key, pin.GetProperty("sha256").GetString() ?? ""))
                            throw new ArgumentException("Duplicate expectedInstalled path: " + key);
                    }
                foreach (var r in replaces)
                    if (
                        !pins.TryGetValue(r.Relative, out var pinned)
                        || !pinned.Equals(r.InstalledSha256, StringComparison.OrdinalIgnoreCase)
                    )
                        throw new ArgumentException(
                            $"Installed file changed or not pinned: expectedInstalled needs {{path:\"{r.Relative}\", sha256:\"{r.InstalledSha256}\"}} (from a fresh preview)."
                        );
                var stale = pins
                    .Keys.Where(k =>
                        !replaces.Any(r => r.Relative.Equals(k, StringComparison.OrdinalIgnoreCase))
                    )
                    .ToArray();
                if (stale.Length > 0)
                    throw new ArgumentException(
                        "expectedInstalled lists files that are not replaced by this plan (re-run preview): "
                            + string.Join(", ", stale)
                    );
                if (replaces.Length > 0)
                {
                    EditorFiles.Confirm(args, "confirmDestructive");
                    var supplied =
                        Optional(args, "backupDirectory")
                        ?? throw new ArgumentException(
                            "backupDirectory (new folder outside the mod) required to replace installed files."
                        );
                    backupDirectory = Path.GetDirectoryName(
                        EditorFiles.LocalPath(supplied.TrimEnd('\\', '/') + "\\x")
                    )!;
                    if (
                        Directory.Exists(backupDirectory)
                        || File.Exists(backupDirectory)
                        || !Directory.Exists(Path.GetDirectoryName(backupDirectory))
                    )
                        throw new ArgumentException(
                            "backupDirectory must be a NEW folder in an existing parent: "
                                + backupDirectory
                        );
                    if (
                        (backupDirectory + '\\').StartsWith(
                            modRoot.TrimEnd('\\') + '\\',
                            StringComparison.OrdinalIgnoreCase
                        )
                        || (modRoot + '\\').StartsWith(
                            backupDirectory.TrimEnd('\\') + '\\',
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                        throw new ArgumentException(
                            "backupDirectory must be outside the mod folder (it would be published with the mod)."
                        );
                }
            }
        }
        return new(
            modRoot,
            campaignDir,
            id,
            operation,
            preview,
            xmlText,
            stringText,
            files.ToArray(),
            scenarios.ToArray(),
            dependencies.ToArray(),
            selfContained,
            missing.ToArray(),
            unmanaged,
            warnings.Distinct(StringComparer.Ordinal).ToArray(),
            backupDirectory
        );
    }

    static void WriteNew(string destination, PlannedFile f)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using (
            var output = new FileStream(
                destination,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None
            )
        )
        {
            if (f.Content is not null)
                output.Write(f.Content);
            else
            {
                CheckpointDocument.RequireHash(Layout.Hash(f.Source!), f.Sha256);
                using var input = new FileStream(
                    f.Source!,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read
                );
                input.CopyTo(output);
            }
            output.Flush(true);
        }
        if (!Layout.Hash(destination).Equals(f.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Readback hash mismatch: " + destination);
    }

    internal static object Execute(JsonElement args, string exe)
    {
        var plan = Build(args, exe);
        var files = plan
            .Files.Select(f => new
            {
                relative = f.Relative,
                kind = f.Kind,
                source = f.Source,
                destination = Path.Combine(plan.ModRoot, f.Relative),
                sha256 = f.Sha256,
                bytes = f.Bytes,
                action = f.Action,
                installedSha256 = f.InstalledSha256,
            })
            .ToArray();
        var scenarios = plan
            .Scenarios.Select(s => new
            {
                source = s.Source,
                sha256 = s.Sha256,
                modifiedUtc = s.ModifiedUtc,
                resolvedFromLatest = s.FromLatest,
                fileName = s.Stem,
                cinematic = s.Cinematic,
                title = s.Title,
            })
            .ToArray();
        const string limitation =
            "Layout from the Workshop 'Hero of Beausejour Retold' mod plus the user's working om10 fix (XS mirrored at the mod root: game\\ai\\…, trigger\\…). Mods load at startup: restart the game and enable the mod. Campaign appearance/progression and XS compile/runtime are NOT verified by this tool; the dependency check is file resolution only. Scenario and XS files are copied byte-for-byte, never edited. Bundled XS hashes are taken at apply time (not caller-pinned).";
        var nextSteps = new[]
        {
            plan.Preview
                ? "Review files/dependencies; then repeat with preview=false, confirmWrite=true and each scenario's sourcePath+expectedSha256 from this preview"
                    + (
                        plan.Operation == "update"
                            ? ", expectedInstalled for every replace and a new backupDirectory outside the mod."
                            : "."
                    )
                : "Restart Age of Mythology Retold (mods load at startup).",
            "Enable the mod under Mods. To test self-containment, temporarily move the profile's Game\\AI\\<folder> and trigger\\<folder> away and play the campaign with only this mod enabled.",
            "Publish/update through the in-game Mods → Publish flow; re-run this tool with operation=update after every scenario/XS change.",
        };
        var expectedInstalled = plan
            .Files.Where(f => f.Action == "replace")
            .Select(f => new { path = f.Relative, sha256 = f.InstalledSha256 })
            .ToArray();
        if (plan.Preview)
        {
            // Resolved, hash-pinned arguments for the apply call; the caller adds preview=false, confirmWrite (and confirmDestructive/backupDirectory for replaces).
            var applyArgs = JsonSerializer
                .Deserialize<Dictionary<string, JsonElement>>(args.GetRawText())!
                .ToDictionary(kv => kv.Key, kv => (object)kv.Value);
            applyArgs.Remove("preview");
            applyArgs["modName"] = Path.GetFileName(plan.ModRoot);
            applyArgs["campaignId"] = plan.Id;
            var entries = args.GetProperty("scenarios").EnumerateArray().ToArray();
            applyArgs["scenarios"] = entries
                .Select(
                    (e, i) =>
                    {
                        var entry = JsonSerializer
                            .Deserialize<Dictionary<string, JsonElement>>(e.GetRawText())!
                            .ToDictionary(kv => kv.Key, kv => (object)kv.Value);
                        entry["sourcePath"] = plan.Scenarios[i].Source;
                        entry["expectedSha256"] = plan.Scenarios[i].Sha256;
                        entry["fileName"] = plan.Scenarios[i].Stem;
                        return entry;
                    }
                )
                .ToArray();
            if (expectedInstalled.Length > 0)
                applyArgs["expectedInstalled"] = expectedInstalled;
            return new
            {
                preview = true,
                operation = plan.Operation,
                modRoot = plan.ModRoot,
                campaignId = plan.Id,
                selfContained = plan.SelfContained,
                missingFromMod = plan.MissingFromMod,
                files,
                scenarios,
                dependencies = plan.Dependencies,
                unmanaged = plan.Unmanaged,
                expectedInstalled,
                warnings = plan.Warnings,
                xml = plan.Xml,
                strings = plan.Strings,
                applyArgs,
                nextSteps,
                limitation,
            };
        }

        var written = new List<string>();
        var phase = "write";
        try
        {
            if (
                plan.Operation == "create"
                && (Directory.Exists(plan.ModRoot) || File.Exists(plan.ModRoot))
            )
                throw new IOException("Mod folder appeared since planning: " + plan.ModRoot);
            foreach (var f in plan.Files)
            {
                var destination = Path.Combine(plan.ModRoot, f.Relative);
                phase = f.Action + " " + f.Relative;
                switch (f.Action)
                {
                    case "unchanged":
                        continue;
                    case "create":
                        written.Add(destination);
                        WriteNew(destination, f);
                        break;
                    case "replace":
                        CheckpointDocument.RequireHash(Layout.Hash(destination), f.InstalledSha256);
                        var backup = Path.Combine(plan.BackupDirectory!, f.Relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                        File.Copy(destination, backup, overwrite: false);
                        written.Add(backup);
                        CheckpointDocument.RequireHash(Layout.Hash(backup), f.InstalledSha256);
                        var staging =
                            destination + ".aommcp-" + Guid.NewGuid().ToString("N") + ".tmp";
                        written.Add(staging);
                        WriteNew(staging, f);
                        File.Move(staging, destination, overwrite: true);
                        written.Remove(staging);
                        written.Add(destination);
                        if (
                            !Layout
                                .Hash(destination)
                                .Equals(f.Sha256, StringComparison.OrdinalIgnoreCase)
                        )
                            throw new IOException("Readback hash mismatch: " + destination);
                        break;
                }
            }
        }
        catch (Exception e)
            when (e
                    is IOException
                        or UnauthorizedAccessException
                        or InvalidDataException
                        or ArgumentException
            )
        {
            throw new WorkflowFailure(
                "CAMPAIGN_BUILD_OUTCOME_UNKNOWN",
                phase,
                e.Message,
                false,
                false,
                "Stop. Inspect the mod folder"
                    + (
                        plan.BackupDirectory is null
                            ? ""
                            : " and backupDirectory (original copies of replaced files)"
                    )
                    + "; do not retry or clean up automatically. Fix it manually, then run a new preview.",
                plan.BackupDirectory,
                plan.ModRoot,
                e,
                written.ToArray()
            );
        }
        return new
        {
            preview = false,
            operation = plan.Operation,
            modRoot = plan.ModRoot,
            campaignId = plan.Id,
            selfContained = plan.SelfContained,
            missingFromMod = plan.MissingFromMod,
            files,
            scenarios,
            unmanaged = plan.Unmanaged,
            backupDirectory = plan.BackupDirectory,
            warnings = plan.Warnings,
            limitation,
            nextSteps = nextSteps[1..],
        };
    }
}
