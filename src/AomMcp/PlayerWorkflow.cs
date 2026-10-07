using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AomMcp;

/// <summary>Checkpoint-based player setting preconditions and verification. No unverified game UI input.</summary>
internal static class PlayerWorkflow
{
    static readonly string[] PreviewFields = ["operation", "scenarioPath", "expectedSha256", "player", "target", "expectedStance",
        "desiredStance", "direction", "expectedReverseStance", "desiredReverseStance", "expectedAiPath", "desiredAiPath", "verificationPath", "changes"];
    static readonly string[] ApplyFields = [.. PreviewFields, "confirmDestructive", "confirmIsolatedScene", "backupScenarioPath",
        "backupTriggerPath", "verificationDirectory", "scenarioProfileDirectory", "triggerProfileDirectory"];

    internal readonly record struct StanceChange(int From, int To, int Old, int Desired);

    static StanceChange[] Matrix(JsonElement args, ScenarioReader.Snapshot snapshot)
    {
        var list = args.GetProperty("changes");
        if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() is < 1 or > 64)
            throw new ArgumentException("changes requires 1..64 directed cells.");
        var seen = new HashSet<(int, int)>();
        return list.EnumerateArray().Select(entry =>
        {
            Catalog.ValidateObject(entry, ["player", "target", "expected", "desired"]);
            var from = entry.GetProperty("player").GetInt32(); var to = entry.GetProperty("target").GetInt32();
            var old = entry.GetProperty("expected").GetInt32(); var desired = entry.GetProperty("desired").GetInt32();
            if (from < 1 || to < 1 || from >= snapshot.Players.Length || to >= snapshot.Players.Length || from == to
                || !seen.Add((from, to)) || old is < 1 or > 3 || desired is < 1 or > 3 || old == desired)
                throw new ArgumentException("Invalid/duplicate diplomacy cell or transition.");
            if (snapshot.Players[from].Diplomacy[to] != old)
                throw new WorkflowFailure("STALE_PLAYER", "preflight", $"P{from}→P{to} differs from expected stance.", false, false,
                    "Inspect saved player matrix; no input sent.");
            return new StanceChange(from, to, old, desired);
        }).ToArray();
    }

    static object DiplomacyMatrix(JsonElement args, ScenarioReader.Snapshot snapshot)
    {
        if (args.TryGetProperty("player", out _) || args.TryGetProperty("target", out _) || args.TryGetProperty("direction", out _)
            || args.TryGetProperty("desiredAiPath", out _) || args.TryGetProperty("expectedAiPath", out _))
            throw new ArgumentException("Matrix changes cannot mix legacy stance/AI fields.");
        var changes = Matrix(args, snapshot);
        if (args.GetProperty("operation").GetString() == "verify")
        {
            var observed = ScenarioReader.Read(args.GetProperty("verificationPath").GetString()!);
            var mismatches = new List<string>();
            if (snapshot.Players.Length != observed.Players.Length || snapshot.TriggerSection is null || observed.TriggerSection is null
                || !snapshot.TriggerSection.AsSpan().SequenceEqual(observed.TriggerSection)) mismatches.Add("player count / TR section");
            else
                for (var i = 0; i < snapshot.Players.Length; i++)
                {
                    var before = snapshot.Players[i]; var after = observed.Players[i];
                    if (!SameFields(before, after) || before.Diplomacy.Length != after.Diplomacy.Length)
                    { mismatches.Add("P" + i + " non-diplomacy fields"); continue; }
                    for (var j = 0; j < before.Diplomacy.Length; j++)
                    {
                        var wanted = changes.FirstOrDefault(c => c.From == i && c.To == j);
                        var stance = wanted.From == i && wanted.To == j ? wanted.Desired : before.Diplomacy[j];
                        if (after.Diplomacy[j] != stance) mismatches.Add($"P{i}→P{j}");
                    }
                }
            return new { verified = mismatches.Count == 0, mismatches = mismatches.Take(100).ToArray(), changes,
                sourceSha256 = snapshot.Sha256, observedSha256 = observed.Sha256,
                limitation = "Game-written checkpoint only; no XS/runtime proof." };
        }
        return new { preview = true, changes, sourceSha256 = snapshot.Sha256,
            clicks = changes.Sum(c => { var state = c.Old; var n = 0; while (state != c.Desired) { state = NextStance(state); n++; } return n; }),
            limitation = "Alternative UI 2560×1440 only, pre-opened diplomacy dialog. Pixel gate before/after each click, one final checkpoint; no automatic retry." };
    }

    static bool SameFields(ScenarioReader.Player a, ScenarioReader.Player b) =>
        a.Id == b.Id && a.Name == b.Name && a.AiPath == b.AiPath && a.Control == b.Control
        && a.CivId == b.CivId && a.ColorId == b.ColorId && a.StartAge == b.StartAge && a.MaxAge == b.MaxAge
        && a.ClassicalGodId == b.ClassicalGodId && a.HeroicGodId == b.HeroicGodId && a.MythicGodId == b.MythicGodId
        && a.Pop == b.Pop && a.PopLimit == b.PopLimit && a.Food == b.Food && a.Wood == b.Wood
        && a.Gold == b.Gold && a.Favor == b.Favor && a.Visibility == b.Visibility && a.Handicap == b.Handicap;

    internal static object Diplomacy(JsonElement args, string exe)
    {
        var operation = args.GetProperty("operation").GetString();
        Catalog.ValidateObject(args, operation == "apply" ? ApplyFields : PreviewFields);
        if (operation is not "preview" and not "verify" and not "apply")
            throw new ArgumentException("Player workflow operation: preview/verify/apply.");
        var snapshot = ScenarioReader.Read(args.GetProperty("scenarioPath").GetString()!);
        if (!snapshot.Sha256.Equals(args.GetProperty("expectedSha256").GetString(), StringComparison.OrdinalIgnoreCase))
            throw new WorkflowFailure("SOURCE_CHANGED", "preflight", "Player checkpoint SHA-256 differs from preview source.", false, false,
                "Inspect new game-written checkpoint before using player settings.");
        if (args.TryGetProperty("changes", out _)) return DiplomacyMatrix(args, snapshot);
        var from = args.GetProperty("player").GetInt32();
        var to = args.GetProperty("target").GetInt32();
        if (from == to || from < 1 || to < 1 || from >= snapshot.Players.Length || to >= snapshot.Players.Length)
            throw new ArgumentException("Player/target must be distinct non-Gaia slots in checkpoint.");
        var expected = args.GetProperty("expectedStance").GetInt32();
        var desired = args.GetProperty("desiredStance").GetInt32();
        if (expected is < 0 or > 3 || desired is < 1 or > 3 || expected == desired)
            throw new ArgumentException("Distinct stance codes required: ally=1, enemy=2, neutral=3; 0 only for observed unset/self precondition.");
        var mutual = args.GetProperty("direction").GetString() switch
        {
            "oneWay" => false, "mutual" => true,
            _ => throw new ArgumentException("Direction must be oneWay or mutual; reverse stance never inferred."),
        };
        var player = snapshot.Players[from]; var other = snapshot.Players[to];
        if (player.Diplomacy[to] != expected)
            throw new WorkflowFailure("STALE_PLAYER", "preflight", "Saved directional stance differs from expectedStance.", false, false,
                "Inspect saved P6 stance before planning another change.");
        var oldReverse = -1; var newReverse = -1;
        if (mutual)
        {
            oldReverse = args.GetProperty("expectedReverseStance").GetInt32();
            newReverse = args.GetProperty("desiredReverseStance").GetInt32();
            if (other.Diplomacy[from] != oldReverse || oldReverse is < 0 or > 3 || newReverse is < 1 or > 3)
                throw new ArgumentException("Reverse stance precondition/desired value invalid; inspect both directions.");
        }
        else if (args.TryGetProperty("expectedReverseStance", out _) || args.TryGetProperty("desiredReverseStance", out _))
            throw new ArgumentException("Reverse stance fields require mutual direction.");
        var changeAi = args.TryGetProperty("desiredAiPath", out var desiredAi);
        var ai = changeAi ? desiredAi.GetString()! : player.AiPath;
        if (changeAi && (!args.TryGetProperty("expectedAiPath", out var oldAi)
            || oldAi.GetString() != player.AiPath || ai.Length > 200 || ai.Contains('\0')))
            throw new ArgumentException("AI path requires matching expectedAiPath and bounded desiredAiPath.");
        var aiResolution = AiScripts.Resolve(exe, ai);
        if (operation == "verify")
        {
            var checkedPath = args.GetProperty("verificationPath").GetString()!;
            var observed = ScenarioReader.Read(checkedPath);
            if (observed.Players.Length != snapshot.Players.Length || observed.Players[from].Name != player.Name
                || observed.Players[to].Name != other.Name)
                throw new WorkflowFailure("SCENE_MISMATCH", "verify", "Player identity/count differs in verification checkpoint.", false, false,
                    "Inspect scene identity manually; no changes applied by host.");
            var mismatches = new List<string>();
            for (var id = 0; id < snapshot.Players.Length; id++)
            {
                var before = snapshot.Players[id]; var after = observed.Players[id];
                if (before.Name != after.Name || before.Id != after.Id) mismatches.Add("player " + id + " identity");
                var wantedAi = id == from ? ai : before.AiPath;
                if (after.AiPath != wantedAi) mismatches.Add("player " + id + " P5 AI path");
                if (before.Diplomacy.Length != after.Diplomacy.Length) { mismatches.Add("player " + id + " P6 stance count"); continue; }
                for (var target = 0; target < before.Diplomacy.Length; target++)
                {
                    var wanted = id == from && target == to ? desired
                        : mutual && id == to && target == from ? newReverse : before.Diplomacy[target];
                    if (after.Diplomacy[target] != wanted) mismatches.Add("P" + id + "→P" + target + " stance");
                }
            }
            return new { verified = mismatches.Count == 0, mismatches = mismatches.Take(100).ToArray(),
                player = from, target = to, expectedStance = desired, actualStance = observed.Players[from].Diplomacy[to],
                expectedReverseStance = mutual ? (int?)newReverse : null, actualReverseStance = observed.Players[to].Diplomacy[from],
                expectedAiPath = ai, actualAiPath = observed.Players[from].AiPath, aiResolution,
                sourceSha256 = snapshot.Sha256, observed.Sha256,
                limitation = "Checkpoint PL fields only. Game AI compilation/runtime, trigger effects and world identity NOT verified." }; 
        }
        return new { preview = true, current = new { player = from, target = to, stance = expected, reverseStance = other.Diplomacy[from], aiPath = player.AiPath },
            requested = new { stance = desired, direction = mutual ? "mutual" : "oneWay", reverseStance = mutual ? (int?)newReverse : null, aiPath = ai },
            sourceSha256 = snapshot.Sha256, aiResolution, liveAutomationAvailable = !changeAi,
            limitation = "Only pinned 2560×1440 ALTERNATIVE UI diplomacy grid observed live. Apply requires pre-opened Players Settings + Diplomacy panel, isolated scene confirmation, game-writer checkpoint+trigger backups and screenshot pixel gates at every click; other UI layouts fail closed. AI Name text replacement FAILED live P5 verification; manual input + verify only.",
            aiLocation = "Computer-player .xs: INSTALLPATH\\game\\ai. Active-profile ai folder did NOT work; trigger scripts: active-profile trigger directory." };
    }

    /// <summary>Apply arguments naming new backups, verification directory and profile directories.</summary>
    internal static readonly string[] ApplyPathFields = ["backupScenarioPath", "backupTriggerPath", "verificationDirectory", "scenarioProfileDirectory", "triggerProfileDirectory"];

    internal static void PreflightApply(JsonElement args, string exe)
    {
        _ = Diplomacy(args, exe); // Includes saved source SHA, directional value and AI path preconditions.
        EditorFiles.Confirm(args, "confirmDestructive");
        EditorFiles.Confirm(args, "confirmIsolatedScene");
        if (args.TryGetProperty("desiredAiPath", out _))
            throw new WorkflowFailure("AI_TEXT_UNVERIFIED", "ui-preflight",
                "Alternative UI AI Name Unicode text entry returned acknowledgement but saved P5 AI path stayed unchanged in isolated-scene test. No input sent by this tool.",
                false, false, "Set AI Name manually in Players Settings; install personality under INSTALLPATH\\game\\ai (profile ai did not work). Export checkpoint and call verify.");
        var missing = ApplyPathFields
            .Where(f => !args.TryGetProperty(f, out var v) || v.ValueKind != JsonValueKind.String).ToArray();
        if (missing.Length > 0)
            throw new ArgumentException("apply requires new backup paths, existing verificationDirectory and profile directories; missing: " + string.Join(", ", missing) + ".");
        foreach (var (field, extension) in new[] { ("backupScenarioPath", ".mythscn"), ("backupTriggerPath", ".trg") })
            _ = EditorFiles.ApprovedNewPath(args.GetProperty(field).GetString()!, extension);
        if (args.GetProperty("scenarioPath").GetString()!.Equals(args.GetProperty("backupScenarioPath").GetString(), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Backup path must differ from source checkpoint.");
        var directory = EditorFiles.LocalPath(Path.Combine(args.GetProperty("verificationDirectory").GetString()!, "aom-verify-probe.mythscn"));
        if (!Directory.Exists(Path.GetDirectoryName(directory))) throw new ArgumentException("verificationDirectory must exist and be local.");
        _ = EditorFiles.ProfileDirectory(ProfileArgs(args.GetProperty("scenarioProfileDirectory").GetString()!), "scenario");
        _ = EditorFiles.ProfileDirectory(ProfileArgs(args.GetProperty("triggerProfileDirectory").GetString()!), "trigger");
    }

    static JsonElement ProfileArgs(string directory) => JsonSerializer.SerializeToElement(new { profileDirectory = directory });
    static int NextStance(int stance) => stance switch
    {
        2 => 3, 3 => 1, 1 => 2,
        _ => throw new InvalidDataException("Unreviewed diplomacy stance transition; no click."),
    };

    static void Compare(ScenarioReader.Snapshot before, ScenarioReader.Snapshot after,
        int[,] expectedStances, string backupPath, string observedPath)
    {
        if (before.Players.Length != after.Players.Length
            || before.TriggerSection is null || after.TriggerSection is null
            || !before.TriggerSection.AsSpan().SequenceEqual(after.TriggerSection))
            throw new WorkflowFailure("PLAYER_SCENE_CHANGED", "verify", "Saved player count/trigger section differs from source scene.",
                true, false, "Stop. Inspect backup and current scene; do not repeat input.", backupPath, observedPath);
        for (var i = 0; i < before.Players.Length; i++)
        {
            var a = before.Players[i]; var b = after.Players[i];
            if (!SameFields(a, b) || a.Diplomacy.Length != b.Diplomacy.Length)
                throw new WorkflowFailure("PLAYER_FIELDS_CHANGED", "verify", "Unrequested P1/P5/P6 player field changed.", true, false,
                    "Stop; compare game-written checkpoints. Do not repeat input.", backupPath, observedPath);
            for (var j = 0; j < b.Diplomacy.Length; j++)
                if (b.Diplomacy[j] != expectedStances[i, j])
                    throw new WorkflowFailure("PLAYER_STANCE_MISMATCH", "verify", $"P{i}→P{j} saved stance differs from expected state.", true, true,
                        "Stop; inspect current editor/backup. No automatic retry or rollback.", backupPath, observedPath);
        }
    }

    internal static object Apply(Game game, JsonElement args, string exe, Action<string, JsonElement> native)
    {
        if (args.TryGetProperty("changes", out _)) return ApplyMatrix(game, args, exe, native);
        PreflightApply(args, exe);
        var source = ScenarioReader.Read(args.GetProperty("scenarioPath").GetString()!);
        var from = args.GetProperty("player").GetInt32(); var to = args.GetProperty("target").GetInt32();
        var directions = new List<(int From, int To, int Old, int Desired)>
        {
            (from, to, args.GetProperty("expectedStance").GetInt32(), args.GetProperty("desiredStance").GetInt32()),
        };
        if (args.GetProperty("direction").GetString() == "mutual")
            directions.Add((to, from, args.GetProperty("expectedReverseStance").GetInt32(), args.GetProperty("desiredReverseStance").GetInt32()));
        var backupScenario = EditorFiles.ApprovedNewPath(args.GetProperty("backupScenarioPath").GetString()!, ".mythscn");
        var backupTrigger = EditorFiles.ApprovedNewPath(args.GetProperty("backupTriggerPath").GetString()!, ".trg");
        var verifyDir = Path.GetDirectoryName(EditorFiles.LocalPath(Path.Combine(args.GetProperty("verificationDirectory").GetString()!, "aom-verify-probe.mythscn")))!;
        var scenarioProfile = args.GetProperty("scenarioProfileDirectory").GetString()!;
        var triggerProfile = args.GetProperty("triggerProfileDirectory").GetString()!;
        game.Focus();
        ScreenProbe.RequireCell(ScreenProbe.Capture(game), from, to, directions[0].Old);
        var backupSaved = EditorFiles.Checkpoint(game,
            JsonSerializer.SerializeToElement(new { path = backupScenario, profileDirectory = scenarioProfile, confirmWrite = true }), native);
        _ = backupSaved;
        _ = EditorFiles.Triggers(game,
            JsonSerializer.SerializeToElement(new { operation = "export", path = backupTrigger, profileDirectory = triggerProfile, confirmWrite = true }), native);
        var baseline = ScenarioReader.Read(backupScenario);
        if (baseline.Players.Length != source.Players.Length || baseline.Players[0].Diplomacy.Length != source.Players[0].Diplomacy.Length)
            throw new WorkflowFailure("PLAYER_SCENE_CHANGED", "backup", "Backup player/stance count differs from source; no UI input.", true, false,
                "Inspect backup/scene before preparing another change.", backupScenario, backupTrigger);
        var expected = new int[baseline.Players.Length, baseline.Players[0].Diplomacy.Length];
        for (var i = 0; i < baseline.Players.Length; i++)
            for (var j = 0; j < baseline.Players[i].Diplomacy.Length; j++) expected[i, j] = source.Players[i].Diplomacy[j];
        Compare(source, baseline, expected, backupScenario, backupScenario);
        var checkpoints = new List<string>();
        var clicked = false;
        string? latest = null;
        try
        {
            foreach (var (owner, target, old, desired) in directions)
            {
                var state = old;
                var attempts = 0;
                while (state != desired)
                {
                    if (++attempts > 2) throw new InvalidDataException("Diplomacy transition exceeded two reviewed clicks.");
                    ScreenProbe.RequireCell(ScreenProbe.Capture(game), owner, target, state);
                    var next = NextStance(state);
                    var (x, y) = ScreenProbe.Cell(owner, target);
                    clicked = true; // Input outcome becomes uncertain from this point onward.
                    Ui.Click(game, x * 2, y * 2, "left");
                    Thread.Sleep(180);
                    ScreenProbe.RequireCell(ScreenProbe.Capture(game), owner, target, next);
                    latest = EditorFiles.ApprovedNewPath(Path.Combine(verifyDir,
                        "aom-player-verify-" + Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture) + ".mythscn"), ".mythscn");
                    _ = EditorFiles.Checkpoint(game,
                        JsonSerializer.SerializeToElement(new { path = latest, profileDirectory = scenarioProfile, confirmWrite = true }), native);
                    checkpoints.Add(latest);
                    expected[owner, target] = next;
                    Compare(source, ScenarioReader.Read(latest), expected, backupScenario, latest);
                    state = next;
                }
            }
            return new { verified = true, backupScenarioPath = backupScenario, backupTriggerPath = backupTrigger,
                checkpoints, finalCheckpointPath = latest, requestedDirections = directions.Count,
                limitation = "Verified game-written directional P6 fields after each click; no .mythscn edit. AI path/XS runtime, other scene state and scenario reload NOT proven. Backups retained." };
        }
        catch (Exception e)
        {
            throw new WorkflowFailure(clicked ? "PLAYER_UI_OUTCOME_UNKNOWN" : "PLAYER_UI_REFUSED", clicked ? "input/verify" : "ui-observe",
                e.Message, clicked, clicked, "STOP. Inspect latest checkpoint/game UI and backups; no retry, rollback or extra click.",
                backupScenario, latest ?? backupTrigger, e);
        }
    }

    static object ApplyMatrix(Game game, JsonElement args, string exe, Action<string, JsonElement> native)
    {
        PreflightApply(args, exe);
        var source = ScenarioReader.Read(args.GetProperty("scenarioPath").GetString()!);
        var changes = Matrix(args, source);
        var backupScenario = EditorFiles.ApprovedNewPath(args.GetProperty("backupScenarioPath").GetString()!, ".mythscn");
        var backupTrigger = EditorFiles.ApprovedNewPath(args.GetProperty("backupTriggerPath").GetString()!, ".trg");
        var verifyDir = Path.GetDirectoryName(EditorFiles.LocalPath(Path.Combine(args.GetProperty("verificationDirectory").GetString()!, "aom-verify-probe.mythscn")))!;
        var scenarioProfile = args.GetProperty("scenarioProfileDirectory").GetString()!;
        var triggerProfile = args.GetProperty("triggerProfileDirectory").GetString()!;
        game.Focus();
        ScreenProbe.RequireCell(ScreenProbe.Capture(game), changes[0].From, changes[0].To, changes[0].Old);
        _ = EditorFiles.Checkpoint(game,
            JsonSerializer.SerializeToElement(new { path = backupScenario, profileDirectory = scenarioProfile, confirmWrite = true }), native);
        _ = EditorFiles.Triggers(game,
            JsonSerializer.SerializeToElement(new { operation = "export", path = backupTrigger, profileDirectory = triggerProfile, confirmWrite = true }), native);
        var baseline = ScenarioReader.Read(backupScenario);
        var expected = new int[source.Players.Length, source.Players[0].Diplomacy.Length];
        for (var i = 0; i < source.Players.Length; i++)
            for (var j = 0; j < source.Players[i].Diplomacy.Length; j++) expected[i, j] = source.Players[i].Diplomacy[j];
        Compare(source, baseline, expected, backupScenario, backupScenario);
        var done = new List<object>();
        var clicked = false;
        string? latest = null;
        try
        {
            foreach (var change in changes)
            {
                var state = change.Old;
                var count = 0;
                while (state != change.Desired)
                {
                    if (++count > 2) throw new InvalidDataException("Diplomacy transition exceeds two reviewed clicks.");
                    ScreenProbe.RequireCell(ScreenProbe.Capture(game), change.From, change.To, state);
                    var next = NextStance(state);
                    var (x, y) = ScreenProbe.Cell(change.From, change.To);
                    clicked = true;
                    Ui.Click(game, x * 2, y * 2, "left");
                    Thread.Sleep(180);
                    ScreenProbe.RequireCell(ScreenProbe.Capture(game), change.From, change.To, next);
                    done.Add(new { change.From, change.To, before = state, after = next });
                    state = next;
                }
                expected[change.From, change.To] = change.Desired;
            }
            latest = EditorFiles.ApprovedNewPath(Path.Combine(verifyDir,
                "aom-player-matrix-verify-" + Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture) + ".mythscn"), ".mythscn");
            _ = EditorFiles.Checkpoint(game,
                JsonSerializer.SerializeToElement(new { path = latest, profileDirectory = scenarioProfile, confirmWrite = true }), native);
            Compare(source, ScenarioReader.Read(latest), expected, backupScenario, latest);
            return new { verified = true, backupScenarioPath = backupScenario, backupTriggerPath = backupTrigger,
                finalCheckpointPath = latest, completedClicks = done, requestedDirections = changes.Length,
                limitation = "Pixel-gated and final checkpoint verified; no scenario file edit, XS/runtime/reload proof or retry. Backups retained." };
        }
        catch (Exception e)
        {
            // Read-only game-writer diagnostic checkpoint after uncertain input; never click again.
            if (clicked && latest is null)
            {
                try
                {
                    latest = EditorFiles.ApprovedNewPath(Path.Combine(verifyDir,
                        "aom-player-matrix-diagnostic-" + Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture) + ".mythscn"), ".mythscn");
                    _ = EditorFiles.Checkpoint(game,
                        JsonSerializer.SerializeToElement(new { path = latest, profileDirectory = scenarioProfile, confirmWrite = true }), native);
                }
                catch { /* Preserve original failure; game writer may be unavailable. */ }
            }
            throw new WorkflowFailure(clicked ? "PLAYER_UI_OUTCOME_UNKNOWN" : "PLAYER_UI_REFUSED", clicked ? "input/verify" : "ui-observe",
                e.Message + " Completed pixel-observed clicks: " + JsonSerializer.Serialize(done), clicked, clicked,
                "STOP. Inspect diagnostic checkpoint and backup; no automatic retry/rollback.", backupScenario, latest ?? backupTrigger, e);
        }
    }

    internal static object StageAi(JsonElement args, string exe)
    {
        Catalog.ValidateObject(args, ["sourcePath", "expectedSha256", "outputPath", "preview", "confirmWrite"]);
        var source = EditorFiles.LocalPath(args.GetProperty("sourcePath").GetString()!);
        if (!source.EndsWith(".xs", StringComparison.OrdinalIgnoreCase) || new FileInfo(source).Length is < 1 or > 1_000_000)
            throw new ArgumentException("Source XS must be an existing nonempty file <=1MB.");
        var bytes = File.ReadAllBytes(source);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!hash.Equals(args.GetProperty("expectedSha256").GetString(), StringComparison.OrdinalIgnoreCase))
            throw new WorkflowFailure("SOURCE_CHANGED", "preflight", "XS source changed since expectedSha256.", false, false,
                "Re-hash/inspect XS source; no copy attempted.");
        var output = EditorFiles.ApprovedNewPath(args.GetProperty("outputPath").GetString()!, ".xs");
        var installed = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(exe)!, "game", "ai"));
        var profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Games", "Age of Mythology Retold");
        if (output.StartsWith(installed + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || output.StartsWith(profile + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("XS staging cannot write to installed game or nonworking active-profile ai directories.");
        var text = Encoding.UTF8.GetString(bytes);
        var includes = text.Split('\n').Where(l => l.TrimStart().StartsWith("include ", StringComparison.Ordinal))
            .Select(l => l.Trim()).Take(100).ToArray();
        if (includes.Any(l => l.Count(c => c == '"') != 2 || l.Contains("..", StringComparison.Ordinal)))
            throw new InvalidDataException("Unreviewed XS include path; no staging copy.");
        var preview = !args.TryGetProperty("preview", out var p) || p.GetBoolean();
        if (!preview)
        {
            EditorFiles.Confirm(args, "confirmWrite");
            using (var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None)) file.Write(bytes);
            if (Layout.Hash(output) != hash) throw new IOException("XS staged-file hash mismatch.");
        }
        else if (args.TryGetProperty("confirmWrite", out _))
            throw new ArgumentException("Preview does not accept confirmWrite (no write).");
        return new { preview, path = preview ? null : output, expectedOutputPath = output, sha256 = hash, includes,
            installedAiRoot = installed, gameResolved = false,
            limitation = "Staging copy is NOT usable AI installation. Manual reviewed install into INSTALLPATH\\game\\ai and game playtest required; active-profile ai did NOT work." };
    }
}
