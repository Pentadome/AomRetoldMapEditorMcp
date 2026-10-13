using System.Globalization;
using System.Text;
using System.Text.Json;

namespace AomMcp;

/// <summary>
/// Scenario Triggers -> XS Scripts list. A standalone .trg carries no script list and uiLoadTriggers resets it, so
/// editor_triggers reports the last-known names (trigtemp.xs includes + newest saved scenario) for re-adding by hand.
/// </summary>
internal static class XsScripts
{
    internal const string Warning =
        "The .trg format carries no Triggers -> XS Scripts list, and importing triggers (apply / uiLoadTriggers) resets the scenario's list: "
        + "functions from those scripts stop compiling until the scripts are re-added. Check the list before playtesting.";
    internal const string ReAdd =
        "Re-add each lastKnown name: editor_xs_scripts operation=include (or apply with scripts=\"restore\"; normal UI only), "
        + "by hand in Triggers -> XS Scripts otherwise, "
        + "then save. Verify with editor_xs_check scenarioPath=\"latest\" after saving.";
    const long MaxTrigtempBytes = 2_000_000;

    /// <summary>Report for editor_triggers; never throws (a missing profile only adds a source error).</summary>
    internal static object Report(JsonElement args)
    {
        string? trigger = null;
        string? error = null;
        try
        {
            trigger = EditorFiles.ProfileDirectory(args, "trigger");
        }
        catch (Exception e)
            when (e
                    is ArgumentException
                        or InvalidDataException
                        or IOException
                        or UnauthorizedAccessException
            )
        {
            error = e.Message;
        }
        if (trigger is null)
            return new
            {
                warning = Warning,
                lastKnown = Array.Empty<object>(),
                sources = new[]
                {
                    new
                    {
                        kind = "profile",
                        path = (string?)null,
                        modifiedUtc = (string?)null,
                        count = 0,
                        error,
                    },
                },
                reAdd = ReAdd,
            };
        var (names, sources) = LastKnown(trigger);
        return new
        {
            warning = Warning,
            lastKnown = names,
            sources,
            reAdd = ReAdd,
        };
    }

    internal sealed record Source(
        string Kind,
        string? Path,
        string? ModifiedUtc,
        int Count,
        string? Error
    );

    internal sealed record Known(string Name, bool Embedded, string[] Sources);

    /// <summary>Union of include lines in trigger\trigtemp.xs (last trigger compile) and the TR scripts of the newest profile save.</summary>
    internal static (Known[] Names, Source[] Sources) LastKnown(string triggerDirectory)
    {
        var names = new Dictionary<string, (bool Embedded, List<string> From)>(
            StringComparer.OrdinalIgnoreCase
        );
        var sources = new List<Source>();
        void Add(string name, bool embedded, string from)
        {
            var key = name.Replace('/', '\\');
            if (!names.TryGetValue(key, out var entry))
                names[key] = entry = (embedded, []);
            if (!entry.From.Contains(from))
                entry.From.Add(from);
        }
        static string Utc(string path) =>
            File.GetLastWriteTimeUtc(path).ToString("O", CultureInfo.InvariantCulture);

        var trigtemp = Path.Combine(triggerDirectory, "trigtemp.xs");
        try
        {
            var info = new FileInfo(trigtemp);
            if (!info.Exists)
                sources.Add(
                    new(
                        "trigtemp.xs",
                        trigtemp,
                        null,
                        0,
                        "not present (no trigger compile in this profile yet)"
                    )
                );
            else if (info.Length > MaxTrigtempBytes)
                sources.Add(new("trigtemp.xs", trigtemp, Utc(trigtemp), 0, "over 2 MB; not read"));
            else
            {
                var includes = XsDiagnostics
                    .ParseTrigtemp(
                        Encoding.UTF8.GetString(File.ReadAllBytes(trigtemp)).TrimStart('\uFEFF')
                    )
                    .Includes;
                foreach (var i in includes)
                    Add(i, false, "trigtemp.xs");
                sources.Add(new("trigtemp.xs", trigtemp, Utc(trigtemp), includes.Length, null));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            sources.Add(new("trigtemp.xs", trigtemp, null, 0, e.Message));
        }

        var scenarioDirectory = Path.Combine(Path.GetDirectoryName(triggerDirectory)!, "scenario");
        string? latest = null;
        try
        {
            latest = ScenarioFiles.Latest(scenarioDirectory).FullName;
            var snapshot = ScenarioReader.Read(CheckpointDocument.Read(latest));
            var scripts = snapshot.TriggerSection is { } tr
                ? CampaignTriggers.ScenarioScripts(tr)
                : [];
            foreach (var s in scripts)
                Add(s.Name, s.Embedded, "newestScenario");
            sources.Add(new("newestScenario", latest, Utc(latest), scripts.Length, null));
        }
        catch (Exception e)
            when (e
                    is IOException
                        or InvalidDataException
                        or UnauthorizedAccessException
                        or ArgumentException
            )
        {
            sources.Add(
                new(
                    "newestScenario",
                    latest ?? scenarioDirectory,
                    latest is null ? null : Utc(latest),
                    0,
                    e.Message
                )
            );
        }
        return (
            names
                .Select(n => new Known(n.Key, n.Value.Embedded, n.Value.From.ToArray()))
                .OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            sources.ToArray()
        );
    }

    internal static void SelfTest()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "aom-xsscripts-" + Guid.NewGuid().ToString("N")
        );
        try
        {
            var trigger = Directory.CreateDirectory(Path.Combine(root, "trigger")).FullName;
            Directory.CreateDirectory(Path.Combine(root, "scenario"));
            File.WriteAllText(
                Path.Combine(trigger, "trigtemp.xs"),
                "include \"om10\\om10_diffbonus.xs\";\nvoid main() { }\n"
            );
            File.WriteAllBytes(
                Path.Combine(root, "scenario", "a.mythscn"),
                WorkflowFixtures.Checkpoint(scripts: ["om10/om10_diffbonus.xs", "other/b.xs"])
            );
            var (names, sources) = LastKnown(trigger);
            if (
                names.Length != 2
                || names.Single(n => n.Name == "om10\\om10_diffbonus.xs").Sources.Length != 2
                || names.Single(n => n.Name == @"other\b.xs").Sources.Single() != "newestScenario"
                || sources.Length != 2
                || sources.Any(s => s.Error is not null)
            )
                throw new InvalidOperationException(
                    "XsScripts.LastKnown self-test failed: "
                        + string.Join("; ", sources.Select(s => s.Error))
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
