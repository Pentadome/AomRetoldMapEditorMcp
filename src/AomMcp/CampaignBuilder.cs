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
/// Preview-first, hash-pinned sources, new mod folder only; no cleanup/rollback, no game connection.
/// </summary>
internal static partial class CampaignBuilder
{
    const long MaxScenarioBytes = 512L * 1024 * 1024;
    static readonly string[] Keys = ["modsDirectory", "modName", "campaignId", "displayName", "description", "motifImage", "mapImage", "scenarios", "preview", "confirmWrite"];
    static readonly string[] ScenarioKeys = ["sourcePath", "expectedSha256", "fileName", "title", "premise", "cinematic", "backgroundImage", "loadImage", "pinImage", "mapImage", "pinX", "pinY"];
    [GeneratedRegex("^[a-z0-9_]{1,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex Id();
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9 _.-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex Name();
    // Resource paths in the XML: stock-style relative paths only.
    [GeneratedRegex(@"^[A-Za-z0-9_./\\-]{1,120}$", RegexOptions.CultureInvariant)]
    private static partial Regex Resource();

    internal sealed record Scenario(string Source, string Sha256, long Bytes, string Stem, string? Title, string? Premise, bool Cinematic,
        string? Background, string? Load, string? Pin, string? Map, double PinX, double PinY);
    internal sealed record Plan(string ModRoot, string CampaignDir, string Id, string Xml, string Strings, bool NonAsciiStrings, Scenario[] Scenarios);

    static string? Optional(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    static string Text(string? value, string key, int max)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > max || value.Any(c => c is '"' or '\r' or '\n' or '\0' || char.IsControl(c)))
            throw new ArgumentException(key + " must be 1.." + max + " characters without quotes or line breaks.");
        return value.Trim();
    }
    static string? Res(string? value, string key)
    {
        if (value is null) return null;
        if (!Resource().IsMatch(value) || value.Split('/', '\\').Any(s => s is "" or "." or ".."))
            throw new ArgumentException(key + " must be a relative game resource path (no traversal).");
        return value.Replace('/', '\\');
    }
    static string ModsDirectory(JsonElement args)
    {
        if (Optional(args, "modsDirectory") is { } supplied)
        {
            var dir = EditorFiles.LocalPath(supplied.TrimEnd('\\', '/') + "\\x");
            dir = Path.GetDirectoryName(dir)!;
            if (!Directory.Exists(dir) || !string.Equals(Path.GetFileName(dir), "local", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("modsDirectory must be an existing '...\\mods\\local' directory.");
            return dir;
        }
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Games", "Age of Mythology Retold");
        var candidates = Directory.Exists(root)
            ? Directory.GetDirectories(root).Select(d => Path.Combine(d, "mods", "local")).Where(Directory.Exists).ToArray() : [];
        if (candidates.Length != 1)
            throw new InvalidDataException("Cannot identify one profile mods\\local directory (found " + candidates.Length + "); supply modsDirectory.");
        return EditorFiles.LocalPath(candidates[0]);
    }

    internal static void Preflight(JsonElement args) => _ = Build(args);

    internal static Plan Build(JsonElement args)
    {
        Catalog.ValidateObject(args, Keys);
        var preview = !args.TryGetProperty("preview", out var p) || p.GetBoolean();
        if (!preview) EditorFiles.Confirm(args, "confirmWrite");
        else if (args.TryGetProperty("confirmWrite", out _)) throw new ArgumentException("Preview does not accept confirmWrite (no write).");
        var modName = Optional(args, "modName") ?? throw new ArgumentException("modName required.");
        if (!Name().IsMatch(modName) || modName.EndsWith('.') || modName.EndsWith(' ')) throw new ArgumentException("modName must be a plain folder name (letters, digits, space, _ . -).");
        var id = Optional(args, "campaignId") ?? throw new ArgumentException("campaignId required.");
        if (!Id().IsMatch(id)) throw new ArgumentException("campaignId must be 1..32 chars of a-z 0-9 _ (becomes the folder/XML stem and string-ID prefix).");
        var upper = id.ToUpperInvariant();
        var display = Text(Optional(args, "displayName"), "displayName", 80);
        var description = Text(Optional(args, "description") ?? display, "description", 600);
        var motif = Res(Optional(args, "motifImage"), "motifImage") ?? @"resources\front_end_demo\Foil_TNA.png";
        var campaignMap = Res(Optional(args, "mapImage"), "mapImage") ?? @"ui\campaign_map\map_new_atlantis";
        if (!args.TryGetProperty("scenarios", out var list) || list.ValueKind != JsonValueKind.Array || list.GetArrayLength() is < 1 or > 40)
            throw new ArgumentException("scenarios must list 1..40 entries.");

        var modsRoot = ModsDirectory(args);
        var modRoot = Path.GetFullPath(Path.Combine(modsRoot, modName));
        if (!modRoot.StartsWith(modsRoot.TrimEnd('\\') + '\\', StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("modName escaped mods\\local.");
        if (Directory.Exists(modRoot) || File.Exists(modRoot)) throw new ArgumentException("Mod folder already exists; this tool only creates NEW mods (never overwrites): " + modRoot);
        var campaignDir = Path.Combine(modRoot, "game", "campaign", id);

        var scenarios = new List<Scenario>(); var stems = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visible = 0;
        foreach (var e in list.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object) throw new ArgumentException("scenario entries must be objects.");
            Catalog.ValidateObject(e, ScenarioKeys);
            var source = EditorFiles.LocalPath(Optional(e, "sourcePath") ?? throw new ArgumentException("scenario.sourcePath required."));
            if (!source.EndsWith(".mythscn", StringComparison.OrdinalIgnoreCase) || !sources.Add(source)) throw new ArgumentException("scenario.sourcePath must be a distinct existing .mythscn: " + source);
            var info = new FileInfo(source);
            if (!info.Exists || info.Length is < 1 or > MaxScenarioBytes) throw new ArgumentException("Scenario missing/empty/over 512 MB: " + source);
            var expected = Optional(e, "expectedSha256");
            if (expected is not { Length: 64 } || !expected.All(Uri.IsHexDigit)) throw new ArgumentException("scenario.expectedSha256 (64 hex) required for " + source);
            var hash = Layout.Hash(source);
            CheckpointDocument.RequireHash(hash, expected);
            var stem = Optional(e, "fileName") ?? Path.GetFileNameWithoutExtension(source);
            if (stem.EndsWith(".mythscn", StringComparison.OrdinalIgnoreCase)) stem = stem[..^8];
            if (!Name().IsMatch(stem) || stem.EndsWith('.') || stem.EndsWith(' ') || !stems.Add(stem)) throw new ArgumentException("scenario.fileName must be a distinct plain name: " + stem);
            var cinematic = e.TryGetProperty("cinematic", out var c) && c.ValueKind == JsonValueKind.True;
            string? title = null, premise = null;
            if (!cinematic)
            {
                visible++;
                title = Text(Optional(e, "title"), "scenario.title", 80);
                premise = Text(Optional(e, "premise") ?? title, "scenario.premise", 600);
            }
            else if (Optional(e, "title") is not null || Optional(e, "premise") is not null) throw new ArgumentException("cinematic entries are hidden and take no title/premise.");
            double Pos(string key, double fallback) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number
                ? v.GetDouble() is var d && d is >= 0 and <= 1 ? d : throw new ArgumentException(key + " must be 0..1.") : fallback;
            scenarios.Add(new(source, hash, info.Length, stem, title, premise, cinematic,
                Res(Optional(e, "backgroundImage"), "backgroundImage"), Res(Optional(e, "loadImage"), "loadImage"), Res(Optional(e, "pinImage"), "pinImage"),
                Res(Optional(e, "mapImage"), "mapImage"), Pos("pinX", 0.5), Pos("pinY", 0.5)));
        }
        if (visible == 0) throw new ArgumentException("At least one non-cinematic scenario is required.");

        var xml = new StringBuilder(); var strings = new StringBuilder();
        xml.Append("<?xml version = \"1.0\" encoding = \"UTF-8\"?>\r\n<Campaign>\r\n");
        xml.Append(CultureInfo.InvariantCulture, $"    <DisplayNameStringID>STR_{upper}_MENU_CAMPAIGN</DisplayNameStringID>\r\n");
        xml.Append(CultureInfo.InvariantCulture, $"    <RolloverStringID>STR_{upper}_MENU_CAMPAIGN_DESC</RolloverStringID>\r\n");
        xml.Append(CultureInfo.InvariantCulture, $"    <MotifImage>{SecurityElement.Escape(motif)}</MotifImage>\r\n    <MapImage>{SecurityElement.Escape(campaignMap)}</MapImage>\r\n");
        strings.Append("Language = \"English\"\r\nIsRtl = \"False\"\r\n\r\n");
        strings.Append(CultureInfo.InvariantCulture, $"ID = \"STR_{upper}_MENU_CAMPAIGN\"   ;   Str = \"{display}\"\r\n");
        strings.Append(CultureInfo.InvariantCulture, $"ID = \"STR_{upper}_MENU_CAMPAIGN_DESC\"   ;   Str = \"{description}\"\r\n");
        string? previous = null; var number = 0;
        foreach (var s in scenarios)
        {
            var file = id + "\\" + s.Stem;
            xml.Append("\r\n    <CampaignNode>\r\n");
            xml.Append(CultureInfo.InvariantCulture, $"        <Filename>{SecurityElement.Escape(file)}</Filename>\r\n");
            if (s.Cinematic) xml.Append("        <Visible>0</Visible>\r\n        <Cinematic>1</Cinematic>\r\n");
            else
            {
                number++; var n = number.ToString("00", CultureInfo.InvariantCulture);
                if (previous is not null) xml.Append(CultureInfo.InvariantCulture, $"        <PrereqScenarioFile>{SecurityElement.Escape(previous)}</PrereqScenarioFile>\r\n");
                xml.Append(CultureInfo.InvariantCulture, $"        <DisplayNameStringID>STR_{upper}_MENU_TITLE_{n}</DisplayNameStringID>\r\n        <RolloverStringID>STR_{upper}_MENU_PREMISE_{n}</RolloverStringID>\r\n");
                var bg = s.Background ?? $@"resources\campaign\tna\screen{Math.Min(number, 5)}.png";
                xml.Append(CultureInfo.InvariantCulture, $"        <BackgroundImage>{SecurityElement.Escape(bg)}</BackgroundImage>\r\n        <LoadImage>{SecurityElement.Escape(s.Load ?? bg)}</LoadImage>\r\n");
                xml.Append(CultureInfo.InvariantCulture, $"        <PinImage>{SecurityElement.Escape(s.Pin ?? @"ui\campaign_map\pins\37_ALostPeople_512")}</PinImage>\r\n");
                xml.Append(CultureInfo.InvariantCulture, $"        <MapImageOverride>{SecurityElement.Escape(s.Map ?? campaignMap)}</MapImageOverride>\r\n");
                xml.Append(CultureInfo.InvariantCulture, $"        <PinXPosition>{s.PinX.ToString("0.###", CultureInfo.InvariantCulture)}</PinXPosition>\r\n        <PinYPosition>{s.PinY.ToString("0.###", CultureInfo.InvariantCulture)}</PinYPosition>\r\n        <PinZoom>1.0</PinZoom>\r\n");
                strings.Append(CultureInfo.InvariantCulture, $"\r\nID = \"STR_{upper}_MENU_TITLE_{n}\"   ;   Str = \"{s.Title}\"\r\n");
                strings.Append(CultureInfo.InvariantCulture, $"ID = \"STR_{upper}_MENU_PREMISE_{n}\"   ;   Str = \"{s.Premise}\"\r\n");
                previous = file;
            }
            xml.Append("    </CampaignNode>\r\n");
        }
        xml.Append("</Campaign>\r\n");
        var stringText = strings.ToString();
        return new(modRoot, campaignDir, id, xml.ToString(), stringText, stringText.Any(c => c > 127), scenarios.ToArray());
    }

    static void NewFile(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(bytes); file.Flush(true); }
        if (Layout.Hash(path) != CheckpointDocument.Hash(bytes)) throw new IOException("Readback hash mismatch: " + path);
    }

    internal static object Execute(JsonElement args)
    {
        var plan = Build(args);
        var preview = !args.TryGetProperty("preview", out var p) || p.GetBoolean();
        var xmlBytes = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(plan.Xml)).ToArray();
        var stringBytes = plan.NonAsciiStrings ? new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(plan.Strings)).ToArray() : Encoding.ASCII.GetBytes(plan.Strings);
        var xmlPath = Path.Combine(plan.CampaignDir, plan.Id + ".xml");
        var stringsPath = Path.Combine(plan.ModRoot, "game", "data", "strings", "english", "stringmods.txt");
        var files = plan.Scenarios.Select(s => new { source = s.Source, destination = Path.Combine(plan.CampaignDir, s.Stem + ".mythscn"), sha256 = s.Sha256, bytes = s.Bytes, cinematic = s.Cinematic, title = s.Title }).ToArray();
        const string limitation = "Layout copied from the Workshop 'Hero of Beausejour Retold' mod; the game must be restarted (mods load at startup) and enabled in the Mods menu. Campaign appearance/progression NOT verified by this tool. Scenario files are copied byte-for-byte, never edited.";
        if (preview)
            return new { preview = true, modRoot = plan.ModRoot, campaignXml = xmlPath, stringMods = stringsPath, scenarios = files, xml = plan.Xml, strings = plan.Strings, limitation };
        var written = new List<string>(); var phase = "write";
        try
        {
            foreach (var s in plan.Scenarios)
            {
                var destination = Path.Combine(plan.CampaignDir, s.Stem + ".mythscn");
                CheckpointDocument.RequireHash(Layout.Hash(s.Source), s.Sha256);
                Directory.CreateDirectory(plan.CampaignDir);
                using (var input = new FileStream(s.Source, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { written.Add(destination); input.CopyTo(output); output.Flush(true); }
                CheckpointDocument.RequireHash(Layout.Hash(destination), s.Sha256);
            }
            phase = "xml"; written.Add(xmlPath); NewFile(xmlPath, xmlBytes);
            phase = "strings"; written.Add(stringsPath); NewFile(stringsPath, stringBytes);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            throw new WorkflowFailure("CAMPAIGN_BUILD_OUTCOME_UNKNOWN", phase, e.Message, false, false,
                "Stop. Inspect the partially created mod folder; do not retry or clean up automatically. Remove it manually, then run a new preview.",
                plan.ModRoot, plan.ModRoot, e, written.ToArray());
        }
        return new { preview = false, modRoot = plan.ModRoot, campaignXml = xmlPath, campaignXmlSha256 = Layout.Hash(xmlPath), stringMods = stringsPath, stringModsSha256 = Layout.Hash(stringsPath),
            scenarios = files, limitation, nextAction = "Restart Age of Mythology Retold, enable the mod under Mods, then check Campaigns. Report string IDs/appearance problems from the game's log." };
    }
}
