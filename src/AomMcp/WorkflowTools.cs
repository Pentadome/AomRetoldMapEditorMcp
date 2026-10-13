using System.Text.Json;

namespace AomMcp;

internal sealed partial class Server
{
    readonly PlaytestWorkflow _playtest = new();
    static readonly HashSet<string> WorkflowNames = new(StringComparer.Ordinal)
    {
        "editor_unit_notes",
        "editor_scenario_diff",
        "editor_install_ai",
        "editor_startup_orders",
        "editor_runtime_probe",
        "editor_runtime_report",
        "editor_playtest",
        "editor_build_campaign",
    };

    static bool WorkflowSafeInspection(string name, JsonElement args) =>
        name is "editor_unit_notes" or "editor_scenario_diff" or "editor_runtime_report"
        || name is "editor_install_ai" or "editor_runtime_probe" or "editor_build_campaign"
            && (!args.TryGetProperty("preview", out var p) || p.GetBoolean())
        || name == "editor_startup_orders" && args.GetProperty("operation").GetString() != "write"
        || name == "editor_playtest"
            && args.GetProperty("operation").GetString() is "preview" or "inspect";

    void PreflightWorkflow(string name, JsonElement args)
    {
        switch (name)
        {
            case "editor_unit_notes":
                CheckpointUnits.Preflight(args);
                break;
            case "editor_scenario_diff":
                ScenarioDiff.Preflight(args);
                break;
            case "editor_install_ai":
                AiInstaller.Preflight(args);
                break;
            case "editor_build_campaign":
                CampaignBuilder.Preflight(args, exe);
                break;
            case "editor_startup_orders":
                StartupOrders.Preflight(args);
                break;
            case "editor_runtime_probe":
                RuntimeProbes.Preflight(args);
                break;
            case "editor_runtime_report":
                RuntimeReport.Preflight(args);
                break;
            case "editor_playtest":
                _playtest.PreflightRequest(args);
                break;
        }
    }

    object InvokeWorkflow(string name, JsonElement args) =>
        name switch
        {
            "editor_unit_notes" => CheckpointUnits.Query(args, () => new Game(exe, _layout, pid)),
            "editor_scenario_diff" => ScenarioDiff.Query(args),
            "editor_install_ai" => AiInstaller.Execute(args, exe),
            "editor_build_campaign" => CampaignBuilder.Execute(args, exe),
            "editor_startup_orders" => StartupOrders.Execute(args),
            "editor_runtime_probe" => RuntimeProbes.Execute(args),
            "editor_runtime_report" => RuntimeReport.Execute(args),
            // Preview/profile/token failures never connect. Registered transitions lazily acquire query/read-only connection.
            "editor_playtest" => _playtest.Execute(args, () => new Game(exe, _layout, pid)),
            _ => throw new ArgumentException("Unknown workflow tool."),
        };

    static object WString() => new { type = "string" };

    static object WInt(int min = 0, int max = int.MaxValue) =>
        new
        {
            type = "integer",
            minimum = min,
            maximum = max,
        };

    static object WBool() => new { type = "boolean" };

    static object WArray(object item, int min = 1, int max = 200) =>
        new
        {
            type = "array",
            items = item,
            minItems = min,
            maxItems = max,
        };

    static object WEnum(params string[] values) => new { type = "string", @enum = values };

    static object WObject(Dictionary<string, object> props, params string[] required) =>
        new
        {
            type = "object",
            properties = props,
            required,
            additionalProperties = false,
        };

    static Dictionary<string, object> WPaths(params string[] names) =>
        names.ToDictionary(n => n, _ => WString());

    static void WPaging(Dictionary<string, object> props)
    {
        props["offset"] = WInt();
        props["limit"] = WInt(1, 200);
    }

    static object WArea() =>
        WObject(
            new()
            {
                ["minX"] = new
                {
                    type = "number",
                    minimum = -1_000_000,
                    maximum = 1_000_000,
                },
                ["maxX"] = new
                {
                    type = "number",
                    minimum = -1_000_000,
                    maximum = 1_000_000,
                },
                ["minZ"] = new
                {
                    type = "number",
                    minimum = -1_000_000,
                    maximum = 1_000_000,
                },
                ["maxZ"] = new
                {
                    type = "number",
                    minimum = -1_000_000,
                    maximum = 1_000_000,
                },
            },
            "minX",
            "maxX",
            "minZ",
            "maxZ"
        );

    static object WTuples() =>
        WArray(
            WObject(
                new()
                {
                    ["unitId"] = WInt(),
                    ["player"] = WInt(0, 12),
                    ["proto"] = WString(),
                },
                "unitId",
                "player",
                "proto"
            )
        );

    /// <summary>Workflow tools also exposed in the core toolset.</summary>
    static readonly HashSet<string> CoreWorkflowNames = new(StringComparer.Ordinal)
    {
        "editor_unit_notes",
    };

    static object UnitNotesSpec()
    {
        var notes = WPaths("scenarioPath", "expectedSha256", "proto", "noteContains");
        WPaging(notes);
        notes["unitIds"] = WArray(WInt());
        notes["player"] = WInt(0, 12);
        notes["hasNote"] = WBool();
        notes["area"] = WArea();
        notes["liveMatch"] = WBool();
        return Spec(
            "editor_unit_notes",
            "Saved checkpoint entities/notes (how users hand coordinates to the agent): exact saved IDs/proto/player, note substring/hasNote and world-XZ area; offset/limit1..200. scenarioPath=\"latest\" = newest save in the active profile scenario folder (resolvedFrom/modifiedUtc echoed); expectedSha256 optional (hash echoed for later pins). noteGroups (whole filtered set) merges case/space/typo variants of a note. liveMatch=true reads current editor units and joins by ID + XZ distance <= 0.5 on the page (needs the editor; evidence, not identity proof). Reviewed PT/Z1/H1 layout only; partial opaque records and ambiguous prototypes explicit. Saved IDs are NOT runtime KB IDs. No scenario writes.",
            notes,
            ["scenarioPath"],
            true
        );
    }

    static IEnumerable<object> WorkflowExtras()
    {
        var diff = WPaths("beforePath", "expectedBeforeSha256", "afterPath", "expectedAfterSha256");
        WPaging(diff);
        diff["assertions"] = WArray(
            WObject(
                new()
                {
                    ["scope"] = WEnum("players", "triggers", "groups", "entities", "raw"),
                    ["policy"] = WEnum("preserve", "allowChanges"),
                    ["keys"] = WArray(WString()),
                    ["fields"] = WArray(WString()),
                },
                "scope",
                "policy"
            )
        );
        yield return Spec(
            "editor_scenario_diff",
            "FULL ONLY. Read-only semantic checkpoint comparison and ordered repeated-section/raw hashes. One side may be \"latest\" (newest profile save); expected*Sha256 pins optional, observed hashes echoed. All preserve/allowChanges assertions evaluated on full dataset BEFORE paging; explicit reviewed fields required for semantic assertions. Partial/unsupported decoding cannot pass universal unchanged claims. No game connection or direct .mythscn edits.",
            diff,
            ["beforePath", "afterPath"],
            true
        );
        var ai = WPaths(
            "sourcePath",
            "expectedSha256",
            "destination",
            "stagingPath",
            "receiptPath",
            "ownershipReceiptPath",
            "expectedReceiptSha256",
            "expectedInstalledSha256",
            "backupPath"
        );
        ai["preview"] = WBool();
        ai["confirmWrite"] = WBool();
        ai["confirmDestructive"] = WBool();
        ai["force"] = WBool();
        ai["destinationRoot"] = WEnum("aom_mcp", "campaign");
        ai["lineEndings"] = WEnum("preserve", "crlf", "lf");
        yield return Spec(
            "editor_install_ai",
            "FULL ONLY. Preview-first hash-pinned custom XS installation restricted to active-profile <Games>/Age of Mythology Retold/<id>/Game/AI/aom_mcp (default) or, with destinationRoot=campaign, <id>/Game/AI/campaign/<subdir>/<file>.xs; includes resolve from profile Game/AI, then stock INSTALLPATH/game/ai (replacing an existing campaign file is the force flow: force=true + expectedInstalledSha256 + confirmDestructive + external backupPath; receipts always go under aom_mcp/.receipts). lineEndings preserve (default; follows existing destination, else source)|crlf|lf; the installed hash may differ from expectedSha256 of the source and is reported as installedSha256. Destination relative INSIDE the root. New files exclusive; updates need ownershipReceiptPath + expectedReceiptSha256 + expectedInstalledSha256 + confirmDestructive and fresh external backupPath; force=true skips the receipt (omit ownershipReceiptPath/expectedReceiptSha256) but still needs expectedInstalledSha256, confirmDestructive, backupPath. Apply requires confirmWrite, new external stagingPath and new receiptPath under aom_mcp/.receipts. Bounded recursive root-relative includes, traversal/reparse/stock refusals, byte readback, retained unknown outcomes. No elevation, automatic binding/compile/playtest/rollback/cleanup/retry; preview writes nothing. XS syscalls/library signatures: editor_xs_api.",
            ai,
            ["sourcePath", "expectedSha256", "destination"]
        );
        var scenarioEntry = WObject(
            new()
            {
                ["sourcePath"] = WString(),
                ["expectedSha256"] = WString(),
                ["fileName"] = WString(),
                ["title"] = WString(),
                ["premise"] = WString(),
                ["cinematic"] = WBool(),
                ["backgroundImage"] = WString(),
                ["loadImage"] = WString(),
                ["pinImage"] = WString(),
                ["mapImage"] = WString(),
                ["pinX"] = new
                {
                    type = "number",
                    minimum = 0,
                    maximum = 1,
                },
                ["pinY"] = new
                {
                    type = "number",
                    minimum = 0,
                    maximum = 1,
                },
            },
            "sourcePath"
        );
        var campaign = WPaths(
            "modsDirectory",
            "modName",
            "campaignId",
            "displayName",
            "description",
            "motifImage",
            "mapImage",
            "backupDirectory"
        );
        campaign["scenarios"] = WArray(scenarioEntry, 1, 40);
        campaign["preview"] = WBool();
        campaign["confirmWrite"] = WBool();
        campaign["confirmDestructive"] = WBool();
        campaign["operation"] = WEnum("create", "update");
        campaign["bundleXs"] = WBool();
        campaign["allowIncomplete"] = WBool();
        campaign["expectedInstalled"] = WArray(
            WObject(new() { ["path"] = WString(), ["sha256"] = WString() }, "path", "sha256"),
            1,
            500
        );
        yield return Spec(
            "editor_build_campaign",
            "FULL ONLY. Packages saved scenarios (game-written .mythscn) as a self-contained custom-campaign local mod <profile>/mods/local/<modName>: game/campaign/<campaignId>/<campaignId>.xml (ordered CampaignNodes; each visible scenario requires the previous; cinematic=true = hidden node), byte-identical scenario copies, game/data/strings/<lang>/stringmods.txt, and (bundleXs, default true) every profile-only XS the scenarios need: player AI personalities + TR XS Scripts with include closures, mirrored as game/ai/<rel> and trigger/<rel>. Preview (default) reports files with create/replace/unchanged, per-scenario dependencies (profile/mod/stock/missing), selfContained, missingFromMod, unmanaged extra files and ready applyArgs (pinned). sourcePath=\"latest\" (preview only) picks the newest saved scenario; modName/campaignId/displayName default from its folder. Apply: preview=false+confirmWrite, every scenario expectedSha256; refuses MOD_NOT_SELF_CONTAINED unless allowIncomplete. operation=create needs a NEW mod folder; operation=update rewrites only planned files: replacing needs expectedInstalled {path,sha256} from preview + confirmDestructive + new backupDirectory outside the mod; never deletes. Mods load at startup (restart + enable mod); campaign appearance and XS compile/runtime not verified. See editor_help topic campaign.",
            campaign,
            ["scenarios"]
        );
        var startup = WPaths(
            "path",
            "expectedSha256",
            "scenarioPath",
            "expectedScenarioSha256",
            "expectedName",
            "outputPath"
        );
        startup["operation"] = WEnum("audit", "plan", "write");
        startup["triggerId"] = WInt();
        startup["workers"] = WTuples();
        startup["targets"] = WTuples();
        startup["player"] = WInt(0, 12);
        startup["targetPlayer"] = WInt(0, 12);
        startup["workerProtos"] = WArray(WString());
        startup["targetProtos"] = WArray(WString());
        startup["job"] = WEnum("farm", "wood");
        startup["treeCapacity"] = WInt(1, 200);
        startup["maxDistance"] = new
        {
            type = "number",
            minimum = 0,
            maximum = 3_000_000,
        };
        startup["templateEffectIndex"] = WInt();
        startup["confirmWrite"] = WBool();
        startup["replaceEffects"] = WArray(
            WObject(
                new() { ["effectIndex"] = WInt(), ["expectedSha256"] = WString() },
                "effectIndex",
                "expectedSha256"
            )
        );
        yield return Spec(
            "editor_startup_orders",
            "FULL ONLY. Explicit caller-designated startup trigger and saved worker/target identity tuples, pinned TR/checkpoint, owner/prototype pools and maxDistance. Audit jobs; preserve valid work, block unknown/conflicting/duplicate tasks. Farm same-owner exact Farm/capacity1; wood explicit treeCapacity. Global minimum-distance world-XZ assignment with deterministic ID tie-breaking, not greedy/pathfinding. Optional effect-hash-pinned replacement of reviewed selected-worker tasks. write needs templateEffectIndex/confirmWrite and exclusive new outputPath .trg via shared codec; never live apply, auto-detect startup timing, or claim runtime success.",
            startup,
            [
                "operation",
                "path",
                "expectedSha256",
                "scenarioPath",
                "expectedScenarioSha256",
                "triggerId",
                "expectedName",
                "workers",
                "targets",
                "player",
                "targetPlayer",
                "workerProtos",
                "targetProtos",
                "job",
                "maxDistance",
            ]
        );
        var selectors = WObject(
            new()
            {
                ["key"] = WString(),
                ["kind"] = WEnum("worker", "defense"),
                ["runtimeProtoId"] = WInt(),
                ["runtimeStateId"] = WInt(),
                ["maxMatches"] = WInt(1, 200),
                ["runtimeUnitId"] = WInt(),
                ["savedIdAnnotation"] = WInt(),
                ["area"] = WArea(),
            },
            "key",
            "kind",
            "runtimeProtoId",
            "runtimeStateId",
            "maxMatches"
        );
        var probe = WPaths("runId", "outputPath");
        probe["player"] = WInt(1, 12);
        probe["intervalSeconds"] = WInt(1, 60);
        probe["selectors"] = WArray(selectors);
        probe["includePlans"] = WBool();
        probe["runtimeIdentityReviewed"] = WBool();
        probe["preview"] = WBool();
        probe["confirmWrite"] = WBool();
        yield return Spec(
            "editor_runtime_probe",
            "FULL ONLY. Opt-in own-player XS AI rule generation, preview defaults true/new .xs write needs confirmWrite. Caller-reviewed runtime prototype/state IDs, fresh own-context KB queries, bounded observations/unique run tags, unit action/target/presence and optional own active-plan states. No context switching, saved/editor-ID dispatch, auto-installation or game input. Saved IDs annotate only; spatial matches do NOT prove identity. aiEcho is debug-window output only: compiler, capture-file transport and runtime effects UNVERIFIED. Rule is NOT standalone personality; integration separately authorized. XS syscalls/library signatures: editor_xs_api.",
            probe,
            ["runId", "player", "selectors", "runtimeIdentityReviewed"]
        );
        var checks = WObject(
            new()
            {
                ["check"] = WString(),
                ["key"] = WString(),
                ["runtimeUnitId"] = WInt(),
                ["expectedActions"] = WArray(WInt()),
                ["expectedTargetKbId"] = WInt(-1),
                ["minimum"] = WInt(0, 200),
                ["maximum"] = WInt(0, 200),
                ["planId"] = WInt(),
                ["expectedStates"] = WArray(WInt()),
            },
            "check"
        );
        var report = WPaths(
            "evidencePath",
            "expectedSha256",
            "runId",
            "runStartedAtUtc",
            "capturedAtUtc"
        );
        WPaging(report);
        report["player"] = WInt(1, 12);
        report["maxAgeSeconds"] = WInt(1, 3600);
        report["minGameTime"] = WInt(0, 864_000);
        report["maxGameTime"] = WInt(0, 864_000);
        report["assertions"] = WArray(checks);
        yield return Spec(
            "editor_runtime_report",
            "FULL ONLY. Strict UTF-8 supplied AOMMCP1 transcript, expected hash, run/player, explicit UTC capture/start timestamps and fresh game-time window. Rejects mixed/wrong-run/player, stale, truncated, duplicate or malformed frames; race/ambiguity/overflow prevents checks passing. workerAction/workerTarget use explicit runtime KB IDs; presence min/max, planState numeric expectedStates, planCount min/max. All assertions BEFORE unit paging. compilation/memory checks unsupported; supplied provenance is NOT authenticated engine evidence. No absence-of-error compilation claim or saved/editor-ID substitution.",
            report,
            [
                "evidencePath",
                "expectedSha256",
                "runId",
                "player",
                "runStartedAtUtc",
                "capturedAtUtc",
                "minGameTime",
                "maxGameTime",
                "assertions",
            ],
            true
        );
        var play = WPaths(
            "profilePath",
            "expectedProfileSha256",
            "runId",
            "expectedExeSha256",
            "originalCheckpointPath",
            "expectedOriginalSha256",
            "disposableCheckpointPath",
            "expectedDisposableSha256",
            "triggerBackupPath",
            "expectedTriggerSha256",
            "token"
        );
        play["operation"] = WEnum("preview", "start", "inspect", "quit");
        play["expectedPid"] = WInt(1);
        play["expectedWindowThread"] = WInt(1);
        play["timeoutMs"] = WInt(1000, 15_000);
        play["confirmDisposableScene"] = WBool();
        play["confirmPlaytest"] = WBool();
        play["confirmQuit"] = WBool();
        yield return Spec(
            "editor_playtest",
            "FULL ONLY. Fail-closed host-owned UI Play/Quit sessions; preview metadata needs no game. Start requires registered pinned build/language/interface/client pixel profile, explicit PID/thread/build, fresh disposable checkpoint/TR and retained original checkpoint/hash, confirmDisposableScene + confirmPlaytest. Saved/live tuples verified, not assumed persistent. Inspect/quit need THIS host token; only known playing token + confirmQuit authorizes narrow outside-editor Quit. Mode/pixel checks, bounded reads, no input retry/rollback/cleanup/load/native start. Registered profiles: uilayouts/playtest-{alt,normal}-en-{2560x1440,1920x1080}.json (English, Player1/Standard); other clients/languages refused before connection/input. Telemetry/compiler/gameplay/reload evidence unavailable, not complete verification.",
            play,
            ["operation"]
        );
    }
}
