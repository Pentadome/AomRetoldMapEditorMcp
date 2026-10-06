using System.Globalization;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AomMcp;

/// <summary>Serves serialized stdio MCP requests with typed arguments and guarded editor operations.</summary>
/// <param name="exe">Expected game executable path.</param>
/// <param name="layoutPath">Accepted hash-pinned layout JSON path.</param>
/// <param name="bridgePath">Compiled native bridge DLL path.</param>
/// <param name="uiDirectory">Optional decoded editor XML directory.</param>
/// <param name="pid">Explicit game process ID, or null for single-process discovery.</param>
/// <param name="fullTools">Expose all generated tools instead of compact core set.</param>
internal sealed partial class Server(
    string exe,
    string layoutPath,
    string bridgePath,
    string? uiDirectory,
    int? pid,
    bool fullTools = false
) : IDisposable
{
    readonly Catalog _catalog = new(exe, uiDirectory);
    readonly Layout _layout = Layout.Load(layoutPath, Layout.Hash(exe));
    readonly Bridge _bridge = new(bridgePath);
    GameDataCatalog? _gameData; // All gameplay/map catalogs share one lazy cache, never a game connection.
    bool _disposed;
    volatile bool _fullTools = fullTools; // Publish selected immutable tool snapshot to concurrent list requests.
    // Shipped game/config/editor.con binds Ctrl+N/L/S to new/load/save; ALT+F4 is Windows close.
    static readonly string[] DataLossKeys = ["N", "L", "S"];
    // Windows SDK VK_LBUTTON=1, VK_RBUTTON=2, VK_SHIFT/CONTROL/MENU=0x10/11/12.
    static readonly int[] HeldInputKeys = [1, 2, 0x10, 0x11, 0x12];
    // Host batch schema: step must identify an existing tool; modifiers are Ui.Press's public names.
    static readonly string[] StepRequired = ["name"];
    static readonly string[] AreaRequired = ["x", "z", "radius"]; // Host world-XZ circle filter fields.
    static readonly JsonElement Empty = JsonDocument.Parse("{}").RootElement.Clone();
    static readonly string[] Modifiers = ["CTRL", "SHIFT", "ALT"];
    static readonly string[] TriggerOperations = ["inspect", "validate", "patch", "export", "apply"]; // Host workflow vocabulary.
    static readonly string[] RecoveryOperations = ["inspect", "recover"];
    static readonly string[] TriggerEditOperations = ["patch", "clone"];
    static readonly string[] PlayerWorkflowOperations = ["preview", "verify", "apply"];
    static readonly string[] PlayerDirections = ["oneWay", "mutual"];
    static readonly string[] TriggerElementKinds = ["condition", "effect"];
    static readonly string[] TriggerDuplicateRequired = ["kind", "elementIndex"];
    static readonly string[] TriggerEditRequired = ["operation", "triggerId", "expectedName"];
    static readonly string[] TriggerLabelRequired = ["kind", "elementIndex", "expected", "value"];
    static readonly string[] TriggerArgRequired = ["key", "value"];
    static readonly string[] DiplomacyChangeRequired = ["player", "target", "expected", "desired"];
    static readonly string[] PlayerSettingChangeRequired = ["player", "field", "expected", "desired"];
    static readonly string[] PlayerSettingFields = ["name", "control", "aiPath", "civ", "color", "visibility", "food", "wood", "gold", "favor", "pop", "popLimit", "handicap", "startAge", "maxAge", "classicalGod", "heroicGod", "mythicGod"];
    static readonly string[] TriggerReplacementRequired = ["kind", "elementIndex", "parameter", "expected", "value"];
    static readonly string[] FormationShapes = ["rows", "ring"]; // Host geometry vocabulary.
    static readonly string[] ToolSets = ["core", "full"]; // Host surface vocabulary, not game permissions.
    // Host search bounds, not game/protocol limits.
    const int ToolSearchMaxQueryLength = 256;
    const int ToolSearchMaxLimit = 50;
    const int ToolSearchDefaultLimit = 10;
    // Host core-surface policy: helpers plus essential history/selection/camera/file commands.
    // Internal helper preparation still uses full catalog; this is not a permissions sandbox.
    static readonly HashSet<string> CoreNativeTools = new(StringComparer.Ordinal)
    {
        "editor_undo", "editor_redo", "editor_uiClearSelection", "editor_uiSelectType",
        "editor_uiLookAtAndSelectUnit", "editor_uiSetCameraStartLoc", "editor_saveScenario",
        "editor_uiLoadTriggers", "editor_uiSaveTriggers",
    };

    /// <summary>Releases the host bridge after all SDK handlers have stopped.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _bridge.Dispose();
    }

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Runs SDK stdio MCP until stdin EOF, then disposes the bridge.</summary>
    /// <remarks>Calls are serialized; cancellation never interrupts an in-flight native mutation.</remarks>
    public async Task RunAsync()
    {
        // Host safety policy: one available slot, capacity one; no concurrent input/native operations.
        using (this)
        using (var calls = new SemaphoreSlim(1, 1))
        {
            _ = CoreTools;
            _ = FullTools; // Build immutable snapshots before SDK dispatches concurrent requests.
            var options = new McpServerOptions
            {
                // Host identity/release version; not the MCP protocol version (negotiated by SDK).
                ServerInfo = new() { Name = "aom-retold-editor", Version = "0.1.0" },
                ServerInstructions = $"Initial tool set: {(_fullTools ? "full" : "core")}. Use editor_search_tools to find tools by name/description across the full catalog without switching modes. Use editor_toolset to inspect/switch core/full mid-session. Only currently listed tools are callable; refresh tools/list after tools/list_changed. Controls local offline scenario editor. Commands report native return, not semantic verification. Use screenshots/state to verify effects. Unknown game builds fail closed; regenerate layout/catalog after patches. Data-loss actions need explicit confirmation. Do not retry mutations after unknown-outcome timeouts.",
                Capabilities = new() { Tools = new() { ListChanged = true } },
                Handlers = new()
                {
                    ListToolsHandler = (request, _) => ValueTask.FromResult(List(request.Params)),
                    CallToolHandler = async (request, cancellationToken) =>
                    {
                        await calls.WaitAsync(cancellationToken);
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            // Once started, finish bounded operations/cleanup even if cancelled.
                            var previous = _fullTools;
                            var result = Call(request.Params.Name,
                                JsonSerializer.SerializeToElement(request.Params.Arguments, Json));
                            if (previous != _fullTools)
                                // Surface already changed; announce it even if caller cancels afterward.
                                await request.Server.SendNotificationAsync(NotificationMethods.ToolListChangedNotification, CancellationToken.None);
                            return result;
                        }
                        finally
                        {
                            calls.Release();
                        }
                    },
                    ListResourcesHandler = (_, _) => ValueTask.FromResult(new ListResourcesResult()),
                    ListPromptsHandler = (_, _) => ValueTask.FromResult(new ListPromptsResult()),
                },
            };
            options.Filters.Message.IncomingFilters.Add(next => (context, cancellationToken) =>
            {
                // Host safety ceiling: 2,000,000 serialized UTF-16 chars (nominal "2 MB" message cap),
                // not a protocol-mandated limit or an exact UTF-8 byte count.
                if (JsonSerializer.Serialize(context.JsonRpcMessage, McpJsonUtilities.DefaultOptions).Length > 2_000_000)
                    throw new McpProtocolException("Message exceeds 2 MB.", McpErrorCode.InvalidParams);
                return next(context, cancellationToken);
            });
            await using var transport = new StdioServerTransport(options);
            await using var server = McpServer.Create(transport, options);
            await server.RunAsync();
        }
    }

    Tool[] Tools => _fullTools ? FullTools : CoreTools;
    Tool[] CoreTools => field ??= Extras().Concat(_catalog.Tools(CoreNativeTools))
        .Select(t => JsonSerializer.SerializeToElement(t, Json).Deserialize<Tool>(Json)!).ToArray();
    Tool[] FullTools => field ??= Extras().Concat(WorkflowExtras()).Concat(_catalog.Tools())
        .Select(t => JsonSerializer.SerializeToElement(t, Json).Deserialize<Tool>(Json)!).ToArray();

    ListToolsResult List(ListToolsRequestParams? p)
    {
        var tools = Tools;
        var start = 0;
        // MCP permits omitted params; absent cursor means the first page.
        if (p?.Cursor is { } cursor
            && (!int.TryParse(cursor, NumberStyles.Integer, CultureInfo.InvariantCulture, out start)
                || start < 0 || start > tools.Length))
            throw new McpProtocolException("Invalid tools cursor.", McpErrorCode.InvalidParams);
        // Host paging choice: 100 tool definitions per response to avoid huge schema payloads.
        const int pageSize = 100;
        var page = tools.Skip(start).Take(pageSize).ToArray();
        return new()
        {
            Tools = page,
            NextCursor = start + page.Length < tools.Length
                ? (start + page.Length).ToString(CultureInfo.InvariantCulture) : null,
        };
    }

    /// <summary>Searches cached full tool metadata without changing the exposed surface.</summary>
    object SearchTools(JsonElement args)
    {
        var query = String(args, "query").Trim();
        var terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var offset = Int(args, "offset", 0);
        var limit = Int(args, "limit", ToolSearchDefaultLimit);
        var full = _fullTools;
        var coreNames = CoreTools.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        int Rank(Tool tool) => tool.Name.Equals(query, StringComparison.OrdinalIgnoreCase) ? 0
            : tool.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ? 1
            : terms.All(term => tool.Name.Contains(term, StringComparison.OrdinalIgnoreCase)) ? 2 : 3;
        var matches = FullTools.Where(t => terms.All(term =>
                t.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (t.Description ?? "").Contains(term, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(Rank).ThenBy(t => t.Name, StringComparer.Ordinal).ToArray();
        var tools = matches.Skip(offset).Take(limit).Select(t => new
        {
            name = t.Name,
            description = t.Description ?? "",
            available = full || coreNames.Contains(t.Name),
            requiredToolset = coreNames.Contains(t.Name) ? "core" : "full",
        }).ToArray();
        return new
        {
            toolset = full ? "full" : "core",
            query,
            total = matches.Length,
            offset,
            limit,
            nextOffset = offset < matches.Length && tools.Length < matches.Length - offset
                ? (int?)(offset + tools.Length) : null,
            tools,
            guidance = "Metadata only; available means exposed in the current set, not runtime readiness. Hidden results require standalone editor_toolset mode=full, then refresh tools/list without an old cursor. Search changes nothing and does not execute matches.",
        };
    }

    CallToolResult Call(string name, JsonElement args, Game? batchGame = null)
    {
        var passedPreflight = false;
        try
        {
            if (args.ValueKind == JsonValueKind.Null)
                args = Empty;
            if (name == "editor_batch")
                return Batch(args);
            Preflight(name, args);
            passedPreflight = true;
            var value = Invoke(name, args, batchGame);
            if (value is ImageResult image)
                return new()
                {
                    Content = [ImageContentBlock.FromBytes(image.Bytes, "image/png")], // Registered PNG media type (MCP image content).
                    IsError = false,
                };
            return new()
            {
                Content = [new TextContentBlock { Text = JsonSerializer.Serialize(value, Json) }],
                StructuredContent = JsonSerializer.SerializeToElement(value, Json),
                IsError = value is Formations.Result { StoppedOnError: true },
            };
        }
        catch (Exception e)
        {
            var noInput = e.Message.Contains("no input sent", StringComparison.OrdinalIgnoreCase);
            var safeInspection = passedPreflight && (noInput || WorkflowSafeInspection(name, args) || name is "editor_search_tools" or "editor_trigger_list" or "editor_players" or "editor_player_dependency_audit"
                || (name is "editor_set_diplomacy" or "editor_player_settings") && args.GetProperty("operation").GetString() != "apply"
                || (name is "editor_trigger_edit" or "editor_stage_ai")
                    && (!args.TryGetProperty("preview", out var preview) || preview.GetBoolean()));
            var failure = e is WorkflowFailure known ? known.Details : new
            {
                code = e switch
                {
                    ArgumentException => "INVALID_ARGUMENT",
                    _ when noInput => "FOCUS_NOT_GRANTED",
                    InvalidDataException => "UNSUPPORTED_OR_INVALID_DATA",
                    FileNotFoundException => "FILE_NOT_FOUND",
                    _ => "OPERATION_FAILED",
                },
                phase = noInput ? "focus" : safeInspection ? "inspect/preview" : passedPreflight ? "unknown" : "preflight",
                message = e.Message,
                nativeDispatched = (bool?)(passedPreflight && !safeInspection ? null : false),
                outcomeUnknown = (bool?)(passedPreflight && !safeInspection ? null : false),
                retrySafe = !passedPreflight || safeInspection,
                stagingPath = (string?)null,
                destinationPath = (string?)null,
                nextAction = noInput ? "Bring editor window foreground manually; no input sent. Inspect state before proceeding."
                    : safeInspection ? "Correct unsupported values/format; no native command or file write occurred."
                    : passedPreflight ? "Inspect current state before retrying; mutation may have occurred."
                    : "Fix arguments or unsupported format before retrying; no native operation ran.",
            };
            return new()
            {
                Content = [new TextContentBlock { Text = JsonSerializer.Serialize(failure, Json) }],
                StructuredContent = JsonSerializer.SerializeToElement(failure, Json),
                IsError = true,
            };
        }
    }

    CallToolResult Batch(JsonElement args)
    {
        Catalog.ValidateObject(args, ["steps"]);
        var steps = args.GetProperty("steps");
        // Host batch bounds: 1..32 steps, 0..2000 ms delay per step; limit work/settle time,
        // not native editor limits. Extras advertises the same JSON Schema constraints.
        if (steps.ValueKind != JsonValueKind.Array || steps.GetArrayLength() is < 1 or > 32)
            throw new ArgumentException("Batch requires 1..32 steps.");
        // Preflight every schema and native confirmation before any game connection/effect.
        foreach (var step in steps.EnumerateArray())
        {
            Catalog.ValidateObject(step, ["name", "arguments", "delayMs"]);
            var name = String(step, "name");
            if (name == "editor_toolset")
                throw new ArgumentException("editor_toolset must be standalone; batches preflight one tool set.");
            var delay = Int(step, "delayMs", 0);
            if (name == "editor_batch" || delay is < 0 or > 2000)
                throw new ArgumentException("Nested batches forbidden; delayMs must be 0..2000.");
            var a = step.TryGetProperty("arguments", out var arguments) ? arguments : Empty;
            Preflight(name, a);
        }
        using var game = steps.EnumerateArray().All(s => ConnectionFree(String(s, "name"),
            s.TryGetProperty("arguments", out var a) ? a : Empty))
            ? null : new Game(exe, _layout, pid);
        var content = new List<ContentBlock>();
        var results = new List<object>();
        var failed = false;
        foreach (var step in steps.EnumerateArray())
        {
            Thread.Sleep(Int(step, "delayMs", 0));
            var name = String(step, "name");
            var a = step.TryGetProperty("arguments", out var arguments) ? arguments : Empty;
            var result = Call(name, a, game);
            failed = result.IsError == true;
            results.Add(new
            {
                name,
                isError = failed,
                structuredContent = result.StructuredContent,
            });
            content.Add(new TextContentBlock { Text = $"Step {results.Count}: {name}" });
            content.AddRange(result.Content);
            if (failed)
                break; // Earlier effects remain. Never retry a failed/unknown-outcome mutation.
        }
        return new()
        {
            Content = content,
            StructuredContent = JsonSerializer.SerializeToElement(new
            {
                steps = results,
                attempted = results.Count,
                completed = results.Count - (failed ? 1 : 0),
                stoppedOnError = failed,
                atomic = false,
            }, Json),
            IsError = failed,
        };
    }

    static void ValidateSchema(JsonElement args, JsonElement schema)
    {
        // JSON Schema keywords (properties, required, type, enum, const, min/maxItems) are standard;
        // argument names in Extras are this host's API, not undocumented engine property names.
        var properties = schema.GetProperty("properties");
        Catalog.ValidateObject(args, properties.EnumerateObject().Select(p => p.Name));
        foreach (var required in schema.GetProperty("required").EnumerateArray())
            if (!args.TryGetProperty(required.GetString()!, out _))
                throw new ArgumentException("Missing " + required.GetString());
        foreach (var p in args.EnumerateObject())
            ValidateValue(p.Value, properties.GetProperty(p.Name));
    }

    static void ValidateValue(JsonElement value, JsonElement schema)
    {
        var valid = schema.GetProperty("type").GetString() switch
        {
            "string" => value.ValueKind == JsonValueKind.String,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _),
            "number" => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var n)
                && double.IsFinite(n),
            "array" => value.ValueKind == JsonValueKind.Array,
            "object" => value.ValueKind == JsonValueKind.Object,
            _ => false,
        };
        if (!valid || (schema.TryGetProperty("const", out var c) && !JsonElement.DeepEquals(value, c))
            || (schema.TryGetProperty("enum", out var choices)
                && !choices.EnumerateArray().Any(c => JsonElement.DeepEquals(value, c))))
            throw new ArgumentException("Invalid batch argument value.");
        if (value.ValueKind == JsonValueKind.Number
            && ((schema.TryGetProperty("minimum", out var lower) && value.GetDouble() < lower.GetDouble())
                || (schema.TryGetProperty("maximum", out var upper) && value.GetDouble() > upper.GetDouble())))
            throw new ArgumentException("Argument outside schema bounds.");
        if (value.ValueKind == JsonValueKind.Object)
        {
            ValidateSchema(value, schema);
            return;
        }
        if (value.ValueKind != JsonValueKind.Array)
            return;
        var length = value.GetArrayLength();
        if ((schema.TryGetProperty("minItems", out var min) && length < min.GetInt32())
            || (schema.TryGetProperty("maxItems", out var max) && length > max.GetInt32()))
            throw new ArgumentException("Invalid batch array length.");
        foreach (var item in value.EnumerateArray())
            ValidateValue(item, schema.GetProperty("items"));
    }

    static bool ConnectionFree(string name, JsonElement args) => WorkflowNames.Contains(name) || name is "editor_toolset" or "editor_search_tools" or "editor_catalog" or "editor_capabilities" or "editor_export_recovery" or "editor_trigger_list" or "editor_trigger_player_parity" or "editor_trigger_edit" or "editor_players" or "editor_player_dependency_audit" or "editor_stage_ai" or "editor_pantheon" or "editor_dependencies"
        || GameDataCatalog.ToolKinds.ContainsKey(name)
        || (name is "editor_set_diplomacy" or "editor_player_settings" && (args.GetProperty("operation").GetString() is "preview" or "verify"))
        || (name == "editor_triggers" && args.GetProperty("operation").GetString() is "inspect" or "validate" or "patch")
        || (name == "editor_place_formation" && Formations.Preview(args));

    void Preflight(string name, JsonElement args)
    {
        if (name.StartsWith("editor_", StringComparison.Ordinal) && Catalog.RemovedCommand(name[7..]))
            throw new ArgumentException("Native loadScenario tool removed after game crash. Use editor Load Scenario UI.");
        var spec = Tools.FirstOrDefault(t => t.Name == name)
            ?? throw new ArgumentException($"Unknown or unexposed tool: {name}. Use editor_toolset mode=full or --toolset full for expanded surface.");
        ValidateSchema(args, spec.InputSchema);
        if (name == "editor_search_tools")
        {
            var query = String(args, "query");
            if (query.Length > ToolSearchMaxQueryLength || string.IsNullOrWhiteSpace(query))
                throw new ArgumentException("query must be nonblank and at most 256 characters.");
        }
        if (name == "editor_map_info" && args.TryGetProperty("planeY", out _) && !args.TryGetProperty("screen", out _))
            throw new ArgumentException("planeY requires screen coordinates.");
        if (name.StartsWith("editor_", StringComparison.Ordinal)
            && _catalog.Commands.TryGetValue(name[7..], out var command)) // Host editor_ namespace has seven characters.
            _ = Catalog.Build(command, args);
        if (name == "editor_triggers") EditorFiles.PreflightTriggers(args);
        if (name == "editor_save_checkpoint") EditorFiles.PreflightCheckpoint(args);
        if (name == "editor_export_recovery") EditorFiles.PreflightRecovery(args);
        if (name == "editor_trigger_edit") TriggerEdits.Preflight(args);
        PreflightWorkflow(name, args);
        if (name == "editor_set_diplomacy" && args.GetProperty("operation").GetString() == "apply")
            PlayerWorkflow.PreflightApply(args, exe);
        if (name == "editor_place_formation")
        {
            _ = Formations.Plan(args);
            if (!Formations.Preview(args) && _layout.Units is null)
                throw new InvalidDataException("Formation observation requires reviewed live-unit layout; no placement requested.");
        }
        if (name == "editor_validate_scenario" && args.TryGetProperty("triggerPath", out var path))
            TriggerCodec.Parse(TriggerCodec.ReadFile(path.GetString()!));
    }

    object Invoke(string name, JsonElement args, Game? batchGame = null)
    {
        if (WorkflowNames.Contains(name)) return InvokeWorkflow(name, args);
        if (name == "editor_search_tools") return SearchTools(args);
        if (name == "editor_toolset")
        {
            var next = String(args, "mode", _fullTools ? "full" : "core") == "full";
            var changed = next != _fullTools;
            _fullTools = next;
            return new { toolset = next ? "full" : "core", exposedToolCount = Tools.Length, changed };
        }
        if (name == "editor_capabilities")
        {
            return new
            {
                hostVersion = typeof(Server).Assembly.GetName().Version?.ToString(),
                buildHash = _layout.ExeSha256,
                toolset = _fullTools ? "full" : "core",
                exposedToolCount = Tools.Length,
                nativeCommands = _catalog.Commands.Count,
                uiActions = _catalog.Actions.Count,
                uiMetadataAvailable = uiDirectory is not null && Directory.Exists(uiDirectory),
                metadataCoverage = "Command/UI counts show shipped coverage, not proof of semantic effects. Toolset resets when MCP host reconnects; call editor_toolset mode=full when needed.",
                triggerEditing = "editor_trigger_list reads bounded TR v12 exports with player/arg/references filters. editor_trigger_player_parity audits template→target gaps. editor_trigger_edit previews/writes NEW-file patch/clone or up to 64 distinct-trigger edits in one output (value/label replacements, condition/effect removal, duplicates); requires source SHA and expected names, verifies unrelated records. editor_triggers apply requires reviewed live export/hash; game round-trip compared semantically. XS compile/runtime effects unproven.",
                playerSettings = "editor_players/editor_player_dependency_audit read game-written checkpoints (stances 1 ally, 2 enemy, 3 neutral; 0 self/unset). editor_set_diplomacy changes batches directed cells with per-click RGB gates and one final checkpoint. editor_player_settings supports reviewed alternative-UI fields, including AI path via INSTALLPATH game\\ai file browser; startAge changes can reset minor gods, requiring observedOnly assertions. Apply is pinned to 2560x1440 alternative UI and requires backups; normal UI refuses. Direct AI-name text entry did not persist. Neither proves XS runtime.",
                scenarioEditing = "Never edit .mythscn directly. Use game editor, game-writer checkpoints and normal Load Scenario UI; native loadScenario disabled after crash.",
                aiScripts = "Computer-player .xs personality must be under INSTALLPATH\\game\\ai (or its subdirectory). Active-profile Games\\Age of Mythology Retold\\<id>\\ai did NOT work. Triggers belong in active-profile trigger directory; use filename stems for uiLoadTriggers/uiSaveTriggers.",
                exportRecovery = "On unknown export outcome, use editor_export_recovery inspect on reported staging path. Recover only to a new file with expectedSha256; no second native dispatch.",
                workflowTools = new { requiredToolset = "full", names = WorkflowNames.Order().ToArray(), savedIdentity = "Checkpoint IDs only; never assume live/runtime identity.",
                    scenarioDiff = "Partial semantic decoding plus ordered raw section hashes; all assertions before paging, no universal unchanged claim.",
                    aiInstallation = "Receipt-owned aom_mcp namespace only; preview first, source/old/receipt hashes, exclusive staging/backups/receipts. No binding/compile/runtime proof.",
                    startupOrders = "Explicit saved pools, reviewed task effects, preserve jobs, bounded deterministic minimum-distance XZ assignments; new TR only, no live apply.",
                    playtest = PlaytestWorkflow.Capabilities(), telemetry = RuntimeTelemetry.Capabilities(),
                    runtimeEvidence = "Hash/time/run/player-bound supplied debug transcripts only; capture transport/compiler proof unavailable." },
            };
        }
        if (name == "editor_export_recovery") return EditorFiles.Recover(args);
        if (name == "editor_trigger_list") return CampaignTriggers.Query(args);
        if (name == "editor_trigger_player_parity") return TriggerParity.Query(args);
        if (name == "editor_trigger_edit") return TriggerEdits.Edit(args);
        if (name == "editor_players") return ScenarioReader.Query(args);
        if (name == "editor_player_dependency_audit") return ScenarioAudit.Query(args, exe);
        if (name == "editor_set_diplomacy" && args.GetProperty("operation").GetString() != "apply")
            return PlayerWorkflow.Diplomacy(args, exe);
        if (name == "editor_player_settings" && args.GetProperty("operation").GetString() != "apply")
            return PlayerSettings.Execute(null, args, exe, _layout.ExeSha256, (_, _) => throw new InvalidOperationException("Read-only operation."));
        if (name == "editor_stage_ai") return PlayerWorkflow.StageAi(args, exe);
        if (name == "editor_place_formation" && Formations.Preview(args)) return Formations.PreviewResult(args);
        if (name == "editor_triggers" && ConnectionFree(name, args))
            return String(args, "operation") == "patch" ? TriggerCodec.Patch(args) : TriggerCodec.Inspect(String(args, "path"));
        if (name == "editor_dependencies")
            return (_gameData ??= new GameDataCatalog()).Dependencies(exe, _layout.ExeSha256, args);
        if (GameDataCatalog.ToolKinds.TryGetValue(name, out var kind))
        {
            ValidateSchema(args, Tools.First(t => t.Name == name).InputSchema);
            return (_gameData ??= new GameDataCatalog()).Query(exe, _layout.ExeSha256, kind, args);
        }
        if (name == "editor_pantheon")
        {
            Catalog.ValidateObject(args, ["pantheon"]);
            return (_gameData ??= new GameDataCatalog()).Lookup(exe, _layout.ExeSha256, String(args, "pantheon"));
        }
        if (name == "editor_catalog")
        {
            Catalog.ValidateObject(args, ["filter"]);
            var filter = String(args, "filter", "");
            return new
            {
                buildHash = _layout.ExeSha256,
                toolset = _fullTools ? "full" : "core",
                exposedToolCount = Tools.Length,
                commands = _catalog.Commands.Values.Where(c =>
                    !Catalog.RemovedCommand(c.Name) && (_fullTools || CoreNativeTools.Contains("editor_" + c.Name))
                    && c.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                ),
                menuActionCount = _fullTools ? _catalog.Actions.Count : 0,
                coverage = "Current tool set only; use editor_toolset mode=full for expanded surface. Generated documented commands/actions, not proof of live effects. Native return values are not captured.",
            };
        }
        if (name is "editor_units" or "editor_inspect_selection" or "editor_map_info" or "editor_validate_scenario")
        {
            ValidateSchema(args, Tools.First(t => t.Name == name).InputSchema);
            if ((name is "editor_units" or "editor_inspect_selection" or "editor_validate_scenario" && _layout.Units is null)
                || (name == "editor_inspect_selection" && _layout.Selection is null)
                || (name == "editor_map_info" && _layout.Map is null))
                throw new InvalidDataException("Live read layout unavailable for this build. Passive review required; offsets are never guessed.");
            if (name == "editor_map_info" && args.TryGetProperty("planeY", out _) && !args.TryGetProperty("screen", out _))
                throw new ArgumentException("planeY requires screen coordinates.");
            using var connected = batchGame is null ? new Game(exe, _layout, pid) : null;
            var live = batchGame ?? connected!;
            return name switch
            {
                "editor_inspect_selection" => EditorView.InspectSelection(live, args),
                "editor_map_info" => EditorView.MapInfo(live, args),
                "editor_validate_scenario" => ScenarioChecks.Query(live, args),
                _ => LiveUnits.Query(live, args),
            };
        }
        if (name == "editor_status")
        {
            Catalog.ValidateObject(args, []);
            try
            {
                using var connected = batchGame is null ? new Game(exe, _layout, pid) : null;
                return (batchGame ?? connected!).State();
            }
            catch (Exception e)
            {
                return new
                {
                    connected = false,
                    reason = e.Message,
                    configuredBuildHash = _layout.ExeSha256,
                };
            }
        }
        using var ownedGame = batchGame is null ? new Game(exe, _layout, pid) : null;
        var game = batchGame ?? ownedGame!;
        if (name == "editor_place_formation") return Formations.Place(game, args, a => Place(game, a));
        if (name == "editor_set_diplomacy")
            return PlayerWorkflow.Apply(game, args, exe, (command, a) =>
                _bridge.Execute(game, Catalog.Build(_catalog.Commands[command], a)));
        if (name == "editor_player_settings")
            return PlayerSettings.Execute(game, args, exe, _layout.ExeSha256, (command, a) =>
                _bridge.Execute(game, Catalog.Build(_catalog.Commands[command], a)));
        if (name == "editor_save_checkpoint")
            return EditorFiles.Checkpoint(game, args, (command, a) =>
                _bridge.Execute(game, Catalog.Build(_catalog.Commands[command], a)));
        if (name == "editor_triggers")
            return EditorFiles.Triggers(game, args, (command, a) =>
                _bridge.Execute(game, Catalog.Build(_catalog.Commands[command], a)));
        if (name == "editor_focus")
        {
            Catalog.ValidateObject(args, []);
            game.Focus();
            return game.State();
        }
        if (name == "editor_ui_read") return UiRead.Query(game, args, _layout.ExeSha256);
        if (name == "editor_screenshot")
        {
            Catalog.ValidateObject(args, ["maxWidth", "region", "scale"]);
            // Host default 1280 px is half the tested 2560-wide client; region uses full-resolution pixels.
            var region = args.TryGetProperty("region", out var rect) ? rect.EnumerateArray().Select(n => n.GetInt32()).ToArray() : null;
            return new ImageResult(Ui.Screenshot(game, Int(args, "maxWidth", 1280), region, Int(args, "scale", 1)));
        }
        if (name == "editor_mouse_move")
        {
            Catalog.ValidateObject(args, ["x", "y"]);
            game.Move(Int(args, "x"), Int(args, "y"));
            Thread.Sleep(120); // Host pointer/frame settle interval from live hover tests, not engine guarantee.
            return game.State();
        }
        if (name == "editor_mouse_click")
        {
            Catalog.ValidateObject(args, ["x", "y", "button"]);
            Ui.Click(game, Int(args, "x"), Int(args, "y"), String(args, "button", "left"));
            return new { inputSent = true, semanticSuccessVerified = false };
        }
        if (name == "editor_mouse_drag")
        {
            Catalog.ValidateObject(args, ["x1", "y1", "x2", "y2", "durationMs"]);
            Ui.Drag(
                game,
                Int(args, "x1"),
                Int(args, "y1"),
                Int(args, "x2"),
                Int(args, "y2"),
                Int(args, "durationMs", 500) // Host default half-second drag; Ui enforces 100..5000 ms.
            );
            return new { inputSent = true, semanticSuccessVerified = false };
        }
        if (name == "editor_mouse_wheel")
        {
            Catalog.ValidateObject(args, ["steps"]);
            Ui.Wheel(game, Int(args, "steps"));
            return new { inputSent = true };
        }
        if (name == "editor_key")
        {
            Catalog.ValidateObject(args, ["key", "modifiers", "confirmDestructive"]);
            var mods = args.TryGetProperty("modifiers", out var m)
                ? m.EnumerateArray().Select(v => v.GetString() ?? "").ToArray()
                : [];
            var key = String(args, "key");
            var dataLoss =
                (
                    key.Equals("F4", StringComparison.OrdinalIgnoreCase)
                    && mods.Contains("ALT", StringComparer.OrdinalIgnoreCase)
                )
                || (
                    DataLossKeys.Contains(key, StringComparer.OrdinalIgnoreCase)
                    && mods.Contains("CTRL", StringComparer.OrdinalIgnoreCase)
                );
            if (
                dataLoss
                && (
                    !args.TryGetProperty("confirmDestructive", out var confirmed)
                    || confirmed.ValueKind != JsonValueKind.True
                )
            )
                throw new ArgumentException(
                    "New/load/save/close hotkey requires confirmDestructive=true."
                );
            Ui.Press(game, key, mods);
            return new { inputSent = true, semanticSuccessVerified = false };
        }
        if (name == "editor_text")
        {
            Catalog.ValidateObject(args, ["text"]);
            Ui.Text(game, String(args, "text"));
            return new { inputSent = true };
        }
        if (name == "editor_place_unit")
            return Place(game, args);
        if (
            name.StartsWith("editor_", StringComparison.Ordinal)
            && _catalog.Commands.TryGetValue(name[7..], out var command)
        )
        {
            // Host tool namespaces editor_/action_ both have 7 chars (Catalog.Tools).
            // uiPlaceAtPointer/uiPaint names come from native help/editor.con and require map hover.
            var script = Catalog.Build(command, args);
            return _bridge.Execute(game, script, command.Name is "uiPlaceAtPointer" or "uiPaint");
        }
        if (
            name.StartsWith("action_", StringComparison.Ordinal)
            && _catalog.Actions.TryGetValue(name[7..], out var action)
        )
        {
            Catalog.ValidateObject(args, ["confirmDestructive"]);
            if (
                !args.TryGetProperty("confirmDestructive", out var c)
                || c.ValueKind != JsonValueKind.True
            )
                throw new ArgumentException(
                    "Shipped UI actions can change/discard data. confirmDestructive=true required; inspect action description and dialog state first."
                );
            return _bridge.Execute(game, action.Script);
        }
        throw new ArgumentException("Unknown tool. Use tools/list or editor_catalog.");
    }

    object Place(Game game, JsonElement args)
    {
        Catalog.ValidateObject(args, ["proto", "player", "x", "y"]);
        var proto = String(args, "proto");
        // Host default player 1; editor slot 0=Gaia, 1..12=players (same bound as Catalog.Build).
        var player = Int(args, "player", 1);
        if (player is < 0 or > 12)
            throw new ArgumentException("Player 0..12 required.");
        Win.Check(Win.GetClientRect(game.Window, out var rect), "GetClientRect");
        // Omitted coordinates default to client center, not scaled screenshot or world coordinates.
        int x = Int(args, "x", rect.Right / 2),
            y = Int(args, "y", rect.Bottom / 2);
        var quoted = Catalog.Quote(proto);
        if (string.IsNullOrWhiteSpace(proto))
            throw new ArgumentException("Empty proto name.");
        if (x < 0 || y < 0 || x >= rect.Right || y >= rect.Bottom)
            throw new ArgumentException("Placement point outside client.");
        var steps = new List<object>();
        try
        {
            // Native command/mode names from embedded help + shipped editor.con; verified cursor cleanup.
            // Object palette may remain open; this does not claim restoration of prior editor UI panels.
            steps.Add(_bridge.Execute(game, "uiClearCursor()"));
            WaitSelection(game, clear: true, player);
            steps.Add(_bridge.Execute(game, "editMode(\"PlaceUnit\")"));
            steps.Add(_bridge.Execute(game, $"uiSetPlacementPlayer({player})"));
            steps.Add(_bridge.Execute(game, $"uiSetProtoCursor({quoted},true)"));
            var (selected, owner) = WaitSelection(game, clear: false, player);
            game.Move(x, y);
            Thread.Sleep(150); // Host-chosen map-hover settle time before one-shot placement.
            // Windows GetAsyncKeyState high bit 0x8000 means currently held; low bit is not used.
            if (HeldInputKeys.Any(k => (Win.GetAsyncKeyState(k) & 0x8000) != 0))
                throw new InvalidOperationException(
                    "Mouse button/modifier held; no placement requested."
                );
            steps.Add(_bridge.Execute(game, "uiPlaceAtPointer(false)", true, selected, owner));
            return new
            {
                placementRequested = true,
                semanticSuccessVerified = false,
                proto,
                player,
                x,
                y,
                selectedProtoId = selected,
                steps,
                note = "One editor placement command returned. Verify real unit with screenshot after preview cleanup. Scenario not saved.",
            };
        }
        finally
        {
            _bridge.Execute(game, "uiClearCursor()");
            _bridge.Execute(game, "editMode(\"None\")");
            WaitSelection(game, clear: true, player);
        }
    }

    (uint Proto, uint Player) WaitSelection(Game game, bool clear, int player)
    {
        // Host cursor acknowledgement budget: 2 s, 25 ms polls (observed async editor-frame updates).
        // Recovered proto DWORD 0xffffffff = signed -1 means no cursor; Layout gives its byte offset.
        var deadline = Win.GetTickCount64() + 2000;
        do
        {
            var editor = game.Editor();
            uint proto = game.UInt(editor + (int)_layout.ProtoOffset),
                owner = game.UInt(editor + (int)_layout.PlayerOffset);
            if (clear ? proto == uint.MaxValue : proto != uint.MaxValue && owner == player)
                return (proto, owner);
            Thread.Sleep(25); // Cursor activation/clear can complete on a following editor frame.
        } while (Win.GetTickCount64() < deadline);
        throw new InvalidOperationException(
            clear
                ? "Cursor cleanup not confirmed; inspect editor before retry."
                : "Unit preparation not reflected in editor state; no placement requested."
        );
    }

    static int Int(JsonElement args, string name, int? fallback = null) =>
        args.TryGetProperty(name, out var value)
            ? value.GetInt32()
            : fallback ?? throw new ArgumentException("Missing " + name);

    static string String(JsonElement args, string name, string? fallback = null) =>
        args.TryGetProperty(name, out var value)
            ? value.GetString() ?? throw new ArgumentException(name + " must be string")
            : fallback ?? throw new ArgumentException("Missing " + name);

    /// <summary>Carries an encoded screenshot for MCP image transport.</summary>
    /// <param name="Bytes">PNG image bytes.</param>
    sealed record ImageResult(byte[] Bytes);

    static object Spec(
        string name,
        string description,
        Dictionary<string, object> properties,
        string[] required,
        bool readOnly = false
    ) =>
        new
        {
            name,
            description,
            inputSchema = new
            {
                type = "object",
                properties,
                required,
                additionalProperties = false,
            },
            annotations = new
            {
                readOnlyHint = readOnly,
                destructiveHint = !readOnly,
                idempotentHint = readOnly,
                openWorldHint = false,
            },
        };

    static Dictionary<string, object> Props(params (string Name, string Type)[] items) =>
        items.ToDictionary(p => p.Name, p => (object)new { type = p.Type });

    static IEnumerable<object> Extras()
    {
        // editor_* helper names/arguments are defined by this host, not native exports.
        // Numeric schema bounds mirror validation above/Ui; maxItems=3 allows CTRL/SHIFT/ALT only.
        yield return new
        {
            name = "editor_toolset",
            description = "Get or switch current core/full tool set mid-session; omit mode to inspect. Default core exposes core helpers plus essential history/selection/camera/file commands; full additionally exposes workflow helpers and all generated native/action tools. No game connection. Always available in both sets. Changes notify tools/list_changed; client must refresh tools/list. Standalone only, not allowed in batches. Surface selection is not a permissions sandbox; all editor guards/confirmations remain.",
            inputSchema = new
            {
                type = "object",
                properties = new { mode = new { type = "string", @enum = ToolSets } },
                required = Array.Empty<string>(),
                additionalProperties = false,
            },
            annotations = new { readOnlyHint = false, destructiveHint = false, idempotentHint = true, openWorldHint = false },
        };
        yield return Spec("editor_search_tools",
            "Find tools by name or description across the full host catalog, even in core mode. Case-insensitive whitespace-separated terms must all match; exact/name matches rank first, then descriptions, with stable name ordering. Returns compact names/descriptions, current availability and required toolset, not schemas. query must be nonblank, max 256 characters; offset>=0, limit=1..50 (default 10). Read-only, no game connection, mode switch or match execution. Hidden tools still require standalone editor_toolset mode=full and fresh tools/list.",
            new Dictionary<string, object>
            {
                ["query"] = new { type = "string", minLength = 1, maxLength = ToolSearchMaxQueryLength },
                ["offset"] = new { type = "integer", minimum = 0 },
                ["limit"] = new { type = "integer", minimum = 1, maximum = ToolSearchMaxLimit },
            }, ["query"], true);
        yield return Spec(
            "editor_batch",
            "Run 1..32 existing tool calls sequentially in one connection. Schema/native-confirmation preflight; every step retains guards. Optional delayMs before a step for queued UI effects. Stops on first error; earlier effects remain (NOT atomic), no retries. Put screenshot last; inspect native acknowledgements independently.",
            new Dictionary<string, object>
            {
                ["steps"] = new
                {
                    type = "array", minItems = 1, maxItems = 32,
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            name = new { type = "string" },
                            arguments = new { type = "object" },
                            delayMs = new { type = "integer", minimum = 0, maximum = 2000 },
                        },
                        required = StepRequired,
                        additionalProperties = false,
                    },
                },
            },
            ["steps"]
        );
        yield return Spec("editor_capabilities",
            "Read host/toolset metadata, file/AI/script locations and safe workflow limits without connecting to the game. Call after reconnect or before editing.",
            [], [], true);
        yield return Spec("editor_export_recovery",
            "Inspect or recover an existing game-writer staging file after a failed checkpoint/trigger export. No game connection and NO native retry. Inspect first; recover requires expectedSha256, fresh outputPath, confirmWrite=true. Staging must belong to active profile and match AomMcp staging prefix.",
            new Dictionary<string, object>
            {
                ["operation"] = new { type = "string", @enum = RecoveryOperations },
                ["stagedPath"] = new { type = "string" },
                ["outputPath"] = new { type = "string" },
                ["expectedSha256"] = new { type = "string" },
                ["profileDirectory"] = new { type = "string" },
                ["confirmWrite"] = new { type = "boolean" },
            }, ["operation", "stagedPath"]);
        yield return Spec("editor_trigger_list",
            "Read-only bounded search/page/detail over exported TR v12 triggers. Use triggerId for detail; optional player/arg/references filters return paged element-level matches (references includes incoming/outgoing Trigger:* EventID edges). Snapshot not live proof.",
            new Dictionary<string, object>
            {
                ["path"] = new { type = "string" },
                ["filter"] = new { type = "string" },
                ["player"] = new { type = "integer", minimum = 0, maximum = 12 },
                ["arg"] = new { type = "object", properties = new Dictionary<string, object>
                {
                    ["key"] = new { type = "string" }, ["value"] = new { type = "string" },
                }, required = TriggerArgRequired, additionalProperties = false },
                ["references"] = new { type = "integer", minimum = 0 },
                ["triggerId"] = new { type = "integer", minimum = 0 },
                ["offset"] = new { type = "integer", minimum = 0 },
                ["limit"] = new { type = "integer", minimum = 1, maximum = 200 },
            }, ["path"], true);
        yield return Spec("editor_trigger_player_parity",
            "Read-only heuristic template→target player parity audit on exported TR v12. Paged gaps/target-only/review hints and proposed editor_trigger_edit edits. No changes or runtime proof.",
            new Dictionary<string, object>
            {
                ["path"] = new { type = "string" },
                ["templatePlayer"] = new { type = "integer", minimum = 0, maximum = 12 },
                ["targetPlayer"] = new { type = "integer", minimum = 0, maximum = 12 },
                ["triggerIds"] = new { type = "array", minItems = 1, maxItems = 200, items = new { type = "integer", minimum = 0 } },
                ["filter"] = new { type = "string" },
                ["offset"] = new { type = "integer", minimum = 0 },
                ["limit"] = new { type = "integer", minimum = 1, maximum = 200 },
            }, ["path", "templatePlayer", "targetPlayer"], true);
        yield return Spec("editor_trigger_edit",
            "Preview-first patch/clone of reviewed TR v12 records in exported .trg. Single operation or edits array (1..64 distinct source triggers) applied in memory with one new output and grouped diff. expectedSha256/source triggerId/expectedName mandatory; clone needs unique newId/newName. Optional active/loop, removeEffects/removeConditions indices (at least one element of each kind remains), labels (printable ASCII, changes element Kind/display label), duplicates (byte-exact copies of original conditions/effects appended in order; max 32), replacements for reviewed single-value numeric Player/PlayerID/FromPlayerID/ToPlayerID/EventID/TechID/Count/Dist/Status/Value/Duration and string ProtoUnit/UnitType/Command/QVName/Op params with expected old value. Indexes use ORIGINAL numbering; duplicates are numbered after originals per kind (e.g. first effect duplicate = original effect count), so a replacement can retarget a copy before removals renumber the result. objectReplacements use complete expected/new objects tuples {unitId,player,proto}, selected by original kind/elementIndex or copyHandle. copyEffects (max 32) use handle/sourceTriggerId/expectedSourceName/effectIndex/beforeEffectIndex; source always immutable original export, insertion before original destination index (original count means end), equal positions preserve request order. Copies retain commands/expression extras/flags. preview=true default, no file or game change. preview=false requires new outputPath + confirmWrite=true; no scenario file edits. Refuses unknown references/group membership and validates original records remain byte-identical.",
            new Dictionary<string, object>
            {
                ["operation"] = new { type = "string", @enum = TriggerEditOperations },
                ["edits"] = new { type = "array", minItems = 1, maxItems = 64, items = new
                {
                    type = "object", properties = new Dictionary<string, object>
                    {
                        ["operation"] = new { type = "string", @enum = TriggerEditOperations },
                        ["triggerId"] = new { type = "integer", minimum = 0 },
                        ["expectedName"] = new { type = "string" },
                        ["newId"] = new { type = "integer", minimum = 0 },
                        ["newName"] = new { type = "string" },
                        ["active"] = new { type = "boolean" }, ["loop"] = new { type = "boolean" },
                        ["objectReplacements"] = TriggerObjects.ReplacementSchema(), ["copyEffects"] = TriggerObjects.CopySchema(),
                        ["removeEffects"] = new { type = "array", items = new { type = "integer", minimum = 0 }, maxItems = 200 },
                        ["removeConditions"] = new { type = "array", items = new { type = "integer", minimum = 0 }, maxItems = 200 },
                        ["labels"] = new { type = "array", maxItems = 200, items = new { type = "object", properties = new Dictionary<string, object>
                        {
                            ["kind"] = new { type = "string", @enum = TriggerElementKinds },
                            ["elementIndex"] = new { type = "integer", minimum = 0 },
                            ["expected"] = new { type = "string" }, ["value"] = new { type = "string" },
                        }, required = TriggerLabelRequired, additionalProperties = false } },
                        ["duplicates"] = new { type = "array", minItems = 1, maxItems = TriggerEdits.MaxDuplicates,
                            items = new { type = "object", properties = new Dictionary<string, object>
                            {
                                ["kind"] = new { type = "string", @enum = TriggerElementKinds },
                                ["elementIndex"] = new { type = "integer", minimum = 0 },
                            }, required = TriggerDuplicateRequired, additionalProperties = false } },
                        ["replacements"] = new { type = "array", maxItems = 200, items = new { type = "object", properties = new Dictionary<string, object>
                        {
                            ["kind"] = new { type = "string", @enum = TriggerElementKinds },
                            ["elementIndex"] = new { type = "integer", minimum = 0 },
                            ["parameter"] = new { type = "string" }, ["expected"] = new { type = "string" }, ["value"] = new { type = "string" },
                        }, required = TriggerReplacementRequired, additionalProperties = false } },
                    }, required = TriggerEditRequired, additionalProperties = false,
                } },
                ["path"] = new { type = "string" },
                ["triggerId"] = new { type = "integer", minimum = 0 },
                ["expectedSha256"] = new { type = "string" },
                ["expectedName"] = new { type = "string" },
                ["newId"] = new { type = "integer", minimum = 0 },
                ["newName"] = new { type = "string" },
                ["active"] = new { type = "boolean" },
                ["loop"] = new { type = "boolean" },
                ["objectReplacements"] = TriggerObjects.ReplacementSchema(), ["copyEffects"] = TriggerObjects.CopySchema(),
                ["removeEffects"] = new { type = "array", items = new { type = "integer", minimum = 0 }, maxItems = 200 },
                ["removeConditions"] = new { type = "array", items = new { type = "integer", minimum = 0 }, maxItems = 200 },
                ["labels"] = new { type = "array", maxItems = 200, items = new { type = "object", properties = new Dictionary<string, object>
                {
                    ["kind"] = new { type = "string", @enum = TriggerElementKinds },
                    ["elementIndex"] = new { type = "integer", minimum = 0 },
                    ["expected"] = new { type = "string" }, ["value"] = new { type = "string" },
                }, required = TriggerLabelRequired, additionalProperties = false } },
                ["duplicates"] = new { type = "array", items = new
                {
                    type = "object", properties = new Dictionary<string, object>
                    {
                        ["kind"] = new { type = "string", @enum = TriggerElementKinds },
                        ["elementIndex"] = new { type = "integer", minimum = 0 },
                    }, required = TriggerDuplicateRequired, additionalProperties = false,
                }, minItems = 1, maxItems = TriggerEdits.MaxDuplicates },
                ["replacements"] = new { type = "array", items = new
                {
                    type = "object", properties = new Dictionary<string, object>
                    {
                        ["kind"] = new { type = "string", @enum = TriggerElementKinds },
                        ["elementIndex"] = new { type = "integer", minimum = 0 },
                        ["parameter"] = new { type = "string" },
                        ["expected"] = new { type = "string" },
                        ["value"] = new { type = "string" },
                    }, required = TriggerReplacementRequired, additionalProperties = false,
                }, maxItems = 200 },
                ["preview"] = new { type = "boolean" },
                ["outputPath"] = new { type = "string" },
                ["confirmWrite"] = new { type = "boolean" },
            }, ["path", "expectedSha256"]);
        yield return Spec("editor_players",
            "Read-only players/AI, control, civ/color IDs, starting/max age, minor-god numeric IDs, population, resources, visibility/handicap and directional stances from GAME-WRITTEN .mythscn checkpoint. raw=true with one player returns bounded P1–P6 hex for research. Minor-god IDs are not source names; no live/runtime proof or edits.",
            new Dictionary<string, object>
            {
                ["path"] = new { type = "string" },
                ["player"] = new { type = "integer", minimum = 0, maximum = 12 },
                ["raw"] = new { type = "boolean" },
            }, ["path"], true);
        yield return Spec("editor_set_diplomacy",
            "Preview/verify directional stance and optional AI path from game-written checkpoints. Legacy oneWay/mutual or changes array of 1..64 directed cells; matrix apply uses RGB gates before/after each click, one final game-writer checkpoint, and diagnostic checkpoint on uncertain input. Pinned 2560×1440 alternative UI with pre-opened Players Settings + Diplomacy dialog, confirmed scene, fresh checkpoint+TR backups. AI Name text entry FAILED live P5 readback; desiredAiPath apply refuses before input. Manual AI path under INSTALLPATH\\game\\ai then verify; profile ai did not work. No .mythscn edits/retry/rollback.",
            new Dictionary<string, object>
            {
                ["operation"] = new { type = "string", @enum = PlayerWorkflowOperations },
                ["scenarioPath"] = new { type = "string" },
                ["expectedSha256"] = new { type = "string" },
                ["player"] = new { type = "integer", minimum = 1, maximum = 12 },
                ["target"] = new { type = "integer", minimum = 1, maximum = 12 },
                ["expectedStance"] = new { type = "integer", minimum = 0, maximum = 3 },
                ["desiredStance"] = new { type = "integer", minimum = 1, maximum = 3 },
                ["direction"] = new { type = "string", @enum = PlayerDirections },
                ["changes"] = new { type = "array", minItems = 1, maxItems = 64, items = new
                {
                    type = "object", properties = new Dictionary<string, object>
                    {
                        ["player"] = new { type = "integer", minimum = 1, maximum = 12 },
                        ["target"] = new { type = "integer", minimum = 1, maximum = 12 },
                        ["expected"] = new { type = "integer", minimum = 1, maximum = 3 },
                        ["desired"] = new { type = "integer", minimum = 1, maximum = 3 },
                    }, required = DiplomacyChangeRequired, additionalProperties = false,
                } },
                ["expectedReverseStance"] = new { type = "integer", minimum = 0, maximum = 3 },
                ["desiredReverseStance"] = new { type = "integer", minimum = 1, maximum = 3 },
                ["expectedAiPath"] = new { type = "string" },
                ["desiredAiPath"] = new { type = "string" },
                ["verificationPath"] = new { type = "string" },
                ["backupScenarioPath"] = new { type = "string" },
                ["backupTriggerPath"] = new { type = "string" },
                ["verificationDirectory"] = new { type = "string" },
                ["scenarioProfileDirectory"] = new { type = "string" },
                ["triggerProfileDirectory"] = new { type = "string" },
                ["confirmDestructive"] = new { type = "boolean" },
                ["confirmIsolatedScene"] = new { type = "boolean" },
            }, ["operation", "scenarioPath", "expectedSha256"]);
        yield return Spec("editor_player_settings",
            "Preview/verify/apply guarded Players Settings changes in observed 2560×1440 alternative UI only. Supports name, control, AI browser path, civ/color, visibility, resources, pop/limit, handicap, age bounds and minor gods; unreviewed dropdowns may refuse. Requires source SHA and expected/desired strings; civ/minor gods need UI labels, colors an exact RGB swatch. Age changes require observedOnly assertions of all 3 minor-god IDs (auto-reset). AI browser change must be final and installed under game\\ai. apply: pre-opened Players Settings, fresh scenario/TR backups, pixel/OCR gates, final checkpoint, diagnostic on uncertain outcome. No retry/save-over. Normal UI refuses.",
            new Dictionary<string, object>
            {
                ["operation"] = new { type = "string", @enum = PlayerWorkflowOperations },
                ["scenarioPath"] = new { type = "string" }, ["expectedSha256"] = new { type = "string" },
                ["changes"] = new { type = "array", minItems = 1, maxItems = 16, items = new
                {
                    type = "object", properties = new Dictionary<string, object>
                    {
                        ["player"] = new { type = "integer", minimum = 1, maximum = 12 },
                        ["field"] = new { type = "string", @enum = PlayerSettingFields },
                        ["expected"] = new { type = "string" }, ["desired"] = new { type = "string" },
                        ["expectedLabel"] = new { type = "string" }, ["desiredLabel"] = new { type = "string" },
                        ["swatchRgb"] = new { type = "string" }, ["scroll"] = new { type = "integer", minimum = 0, maximum = 8 },
                        ["observedOnly"] = new { type = "boolean" },
                    }, required = PlayerSettingChangeRequired, additionalProperties = false,
                } },
                ["verificationPath"] = new { type = "string" },
                ["backupScenarioPath"] = new { type = "string" }, ["backupTriggerPath"] = new { type = "string" },
                ["verificationDirectory"] = new { type = "string" },
                ["scenarioProfileDirectory"] = new { type = "string" }, ["triggerProfileDirectory"] = new { type = "string" },
                ["confirmDestructive"] = new { type = "boolean" }, ["confirmIsolatedScene"] = new { type = "boolean" },
            }, ["operation", "scenarioPath", "expectedSha256", "changes"]);
        yield return Spec("editor_stage_ai",
            "Preview/stage XS personality bytes by expected SHA-256 to new .xs path OUTSIDE installed game/profile directories. No game write; staged XS NOT a working personality. Manually install under INSTALLPATH\\game\\ai with reviewed permissions, resolve includes and playtest. Active-profile ai directory did not work. preview=true by default; preview=false needs confirmWrite=true.",
            new Dictionary<string, object>
            {
                ["sourcePath"] = new { type = "string" },
                ["expectedSha256"] = new { type = "string" },
                ["outputPath"] = new { type = "string" },
                ["preview"] = new { type = "boolean" },
                ["confirmWrite"] = new { type = "boolean" },
            }, ["sourcePath", "expectedSha256", "outputPath"]);
        yield return Spec("editor_player_dependency_audit",
            "Read-only audit joining exported .trg and game-written .mythscn: player references, tribute, AI startAttacking calls, victory conditions, AI path/strategy presence. Saved snapshots only; not proof of runtime waves. AI .xs personality MUST reside in INSTALLPATH\\game\\ai; profile ai folder did not work. Trigger exports belong in active-profile trigger folder.",
            new Dictionary<string, object>
            {
                ["scenarioPath"] = new { type = "string" },
                ["triggerPath"] = new { type = "string" },
                ["player"] = new { type = "integer", minimum = 0, maximum = 12 },
                ["offset"] = new { type = "integer", minimum = 0 },
                ["limit"] = new { type = "integer", minimum = 1, maximum = 200 },
            }, ["scenarioPath", "triggerPath", "player"], true);
        yield return Spec(
            "editor_status",
            "Read current editor state, build, thread and placement selection. No input sent.",
            [],
            [],
            true
        );
        // Same host page and reference-list bound as live-unit queries. IDs are current full live IDs, not scenario-name strings.
        yield return Spec("editor_save_checkpoint",
            "Save through native game writer to unique active-profile scenario staging file, verify stable l33t/zlib payload/decoded length, copy/hash-verify to caller-approved NEW absolute local .mythscn path. confirmWrite=true required. No overwrite option; original scenario files never silently overwritten. profileDirectory selects active scenario directory when ambiguous. Staging retained; native writer may change editor save-name/dirty state. Not semantic reload validation; never retry unknown outcomes.",
            Props(("path", "string"), ("profileDirectory", "string"), ("confirmWrite", "boolean")), ["path", "confirmWrite"]);
        yield return Spec("editor_place_formation",
            "Bounded 1..32 objects in rows/ring, centered on x/y FULL-RESOLUTION CLIENT PIXELS; spacingPixels is NOT world distance. Rows columns default ceil(sqrt(count)); ring spacing is neighbor chord before rounding, no columns allowed. preview defaults true: pure local plan/no game. preview=false requires confirmPlacement=true, checks whole plan against actual client before mutation, reuses single placement guards/cleanup and independently observes actual new IDs. Stops on first error with partial progress/no retry/rollback/save. Camera/UI hover affect world layout.",
            new Dictionary<string, object>
            {
                ["proto"] = new { type = "string" },
                ["player"] = new { type = "integer", minimum = 0, maximum = LiveUnits.MaxPlayer },
                ["shape"] = new { type = "string", @enum = FormationShapes },
                ["count"] = new { type = "integer", minimum = 1, maximum = Formations.MaxCount },
                ["spacingPixels"] = new { type = "integer", minimum = 1, maximum = Formations.MaxSpacing },
                ["x"] = new { type = "integer", minimum = 0, maximum = Formations.MaxPixel },
                ["y"] = new { type = "integer", minimum = 0, maximum = Formations.MaxPixel },
                ["columns"] = new { type = "integer", minimum = 1, maximum = Formations.MaxCount },
                ["preview"] = new { type = "boolean" },
                ["confirmPlacement"] = new { type = "boolean" },
            }, ["proto", "shape", "count", "spacingPixels", "x", "y"]);
        yield return Spec("editor_validate_scenario",
            "Read-only partial diagnostics: live objects/map bounds, missing caller-declared requiredUnitIds or requireTownCenterPlayers; optional exported triggerPath for verified controller flags/percent/outcome/rule-reference heuristics. File is NOT proven current scene state. Objective UI/diplomacy/modes/dynamic XS references not inspected; no universal validity verdict or automatic fixes. offset>=0, limit1..200/default100.",
            new Dictionary<string, object>
            {
                ["triggerPath"] = new { type = "string" },
                ["requiredUnitIds"] = new { type = "array", items = new { type = "integer", minimum = 0 }, maxItems = LiveUnits.MaxLimit },
                ["requireTownCenterPlayers"] = new { type = "array", items = new { type = "integer", minimum = 0, maximum = LiveUnits.MaxPlayer }, maxItems = LiveUnits.MaxPlayer },
                ["offset"] = new { type = "integer", minimum = 0 },
                ["limit"] = new { type = "integer", minimum = 1, maximum = LiveUnits.MaxLimit },
            }, [], true);
        yield return Spec("editor_dependencies",
            "Explain exact proto's static train/build links, positive Enable/CreateUnit/replacement tech effects/raw prerequisites, god starting units and shortest active/obtainable god-to-tech paths. Reuses cached shipped catalogs, no game. Abstract unit-type targets included; paths are potential, NOT evaluated prerequisites/exclusions or current-player trainability. Bounded relations: offset>=0, limit1..200/default50.",
            new Dictionary<string, object>
            {
                ["proto"] = new { type = "string" },
                ["offset"] = new { type = "integer", minimum = 0 },
                ["limit"] = new { type = "integer", minimum = 1, maximum = LiveUnits.MaxLimit },
            }, ["proto"], true);
        yield return Spec("editor_triggers",
            "Inspect/validate/legacy-patch strict TR v12 single Always/CodeSnippet controllers; editor_trigger_list/edit handles read-only campaign/preview/new-file patch/clone. export verifies game-writer staging; apply replaces whole trigger set only after expectedSha256 for prepared file AND expectedLivePath/expectedLiveSha256 of reviewed game export; fresh game-written backup must match its trigger/camera semantics (export basenames differ). confirmDestructive=true, guarded round-trip of body/cameras (export-basename metadata may differ). No XS effect/persistence proof; no retry/rollback.",
            new Dictionary<string, object>
            {
                ["operation"] = new { type = "string", @enum = TriggerOperations },
                ["path"] = new { type = "string" },
                ["outputPath"] = new { type = "string" },
                ["profileDirectory"] = new { type = "string" },
                ["name"] = new { type = "string" },
                ["active"] = new { type = "boolean" },
                ["loop"] = new { type = "boolean" },
                ["code"] = new { type = "string" },
                ["confirmWrite"] = new { type = "boolean" },
                ["confirmDestructive"] = new { type = "boolean" },
                ["expectedSha256"] = new { type = "string" },
                ["expectedLiveSha256"] = new { type = "string" },
                ["expectedLivePath"] = new { type = "string" },
            }, ["operation", "path"]);
        yield return Spec("editor_inspect_selection",
            "Read actual selection records and selected objects' live prototype/player/world position/current-max health. Not command acknowledgements; includes unresolved and non-unit selection kinds. Editor only; no focus/input/game calls. Requires reviewed unit+selection fields. Paging offset>=0, limit1..200/default100; changing selection refuses.",
            new Dictionary<string, object>
            {
                // Same host page policies as editor_units, not native selection capacity.
                ["offset"] = new { type = "integer", minimum = 0 },
                ["limit"] = new { type = "integer", minimum = 1, maximum = LiveUnits.MaxLimit },
            }, [], true);
        // World coordinate/radius safety policies shared with live units; two screen axes, three world axes.
        var worldNumber = new { type = "number", minimum = -LiveUnits.MaxCoordinate, maximum = LiveUnits.MaxCoordinate };
        yield return Spec("editor_map_info",
            "Read map tile/world dimensions, active camera and native render projection/full-resolution client viewport. Optional terrainAt [X,Z] returns native quantized node height; world [X,Y,Z] projects to client pixels/frustum visibility. screen [clientX,clientY] returns inverse ray; optional planeY gives explicit horizontal-plane intersection, not guessed terrain hit. Read-only editor access; changing/unreviewed layouts refuse.",
            new Dictionary<string, object>
            {
                ["terrainAt"] = new { type = "array", items = worldNumber, minItems = 2, maxItems = 2 },
                ["world"] = new { type = "array", items = worldNumber, minItems = 3, maxItems = 3 },
                ["screen"] = new { type = "array", items = worldNumber, minItems = 2, maxItems = 2 },
                ["planeY"] = worldNumber,
            }, [], true);
        // Host paging/radius bounds, not engine API limits; player indices follow tested 0=Gaia..12.
        yield return Spec(
            "editor_units",
            "Read live scenario objects: full unitId, runtime protoId/exact base proto name, player, world XYZ, current/max health. Editor only, read-only process access; no focus/input/game calls. Optional exact case-insensitive proto, player (0=Gaia..12), area {x,z,radius} in world units. offset>=0, limit 1..200 (default 100). IDs scoped to current object/scenario lifetime, not persistent save IDs. Registry/IDs rechecked, not atomic frame snapshot; races refuse. Player-local prototype names may be null, never guessed; proto filtering refuses if names unresolved. Requires independently reviewed build-specific unit layout/runtime signatures; generator does not guess these fields.",
            new Dictionary<string, object>
            {
                ["proto"] = new { type = "string" },
                ["player"] = new { type = "integer", minimum = 0, maximum = LiveUnits.MaxPlayer },
                ["offset"] = new { type = "integer", minimum = 0 },
                ["limit"] = new { type = "integer", minimum = 1, maximum = LiveUnits.MaxLimit },
                ["area"] = new
                {
                    type = "object",
                    properties = new
                    {
                        x = new { type = "number", minimum = -LiveUnits.MaxCoordinate, maximum = LiveUnits.MaxCoordinate },
                        z = new { type = "number", minimum = -LiveUnits.MaxCoordinate, maximum = LiveUnits.MaxCoordinate },
                        radius = new { type = "number", minimum = 0, maximum = LiveUnits.MaxCoordinate },
                    },
                    required = AreaRequired,
                    additionalProperties = false,
                },
            }, [], true
        );
        yield return Spec(
            "editor_catalog",
            "List generated editor command signatures and coverage limits; optional name filter.",
            Props(("filter", "string")),
            [],
            true
        );
        yield return Spec(
            "editor_pantheon",
            "Get exact unit/building proto names by pantheon (e.g. greeks -> VillagerGreek, MilitaryAcademy). Case-insensitive singular/plural culture names. Generated from shipped culture/start/tech metadata, not guessed names or IDs. Potential union across gods/ages, not current-player trainability; unresolved techs reported. No game connection. Run --generate generated if missing/stale.",
            Props(("pantheon", "string")),
            ["pantheon"],
            true
        );
        foreach (var (name, kind) in GameDataCatalog.ToolKinds)
        {
            var description = kind switch
            {
                "prototypes" => "List exact prototypes: units, buildings, trees, resource nodes, decorations, wildlife, effects and other objects. Filter category/unitType/pantheon; base costs, stats, resources and train/build links. Flags are source data, NOT live editor placement proof.",
                "gods" => "List major/minor gods and culture associations, starting units, age techs and direct unlock effects. Minor names are canonical age-tech IDs (e.g. ClassicalAgeAthena); labels are source string IDs, not translations.",
                "technologies" => "List technologies/upgrades, base costs, prerequisites, effects and potential culture associations. Not current-player researchability.",
                "godPowers" => "List shipped god-power definitions, types, costs, placement and created units. Potential culture links from tech grants; source-file variants retained. XML detail has full settings.",
                "terrainTypes" => "List terrain texture identifiers, UI labels/classes, parent terrain types (passability groups) and settings. Names retain shipped backslashes; no guessed numeric IDs.",
                "waterTypes" => "List lake/river/ocean water presets, exact names and settings. Optional XML detail includes rendering, terrain placement and other nested settings.",
                _ => throw new InvalidOperationException("Unknown game catalog kind."),
            };
            yield return Spec(name, description
                + " Read-only, no running game required. name=exact identifier; filter=name/label substring. offset>=0, limit=1..200 (default 50); includeDefinition=false by default. Missing/stale data: --generate generated, then restart MCP.",
                GameDataCatalog.Properties(kind), [], true);
        }
        yield return Spec("editor_focus", "Focus/restore game window. Editor only.", [], []);
        yield return Spec(
            "editor_screenshot",
            "Capture foreground game client PNG. maxWidth default 1280; region [x,y,w,h] uses full-resolution client pixels, optional scale 1..4 nearest (output max 1600×1600). Screenshots consume tokens; prefer small regions.",
            new Dictionary<string, object>
            {
                ["maxWidth"] = new { type = "integer", minimum = 320, maximum = 2560 },
                ["region"] = new { type = "array", minItems = 4, maxItems = 4, items = new { type = "integer" } },
                ["scale"] = new { type = "integer", minimum = 1, maximum = 4 },
            },
            []
        );
        yield return Spec("editor_ui_read",
            "Read bounded foreground game-client region with embedded PP-OCRv6 tiny model. Supply region [x,y,w,h] or named field preset for reviewed alternative 2560×1440 UI; optional scale 1..4. Returns OCR text/score/boxes, never proof of field state.",
            new Dictionary<string, object>
            {
                ["region"] = new { type = "array", minItems = 4, maxItems = 4, items = new { type = "integer" } },
                ["field"] = new { type = "string" },
                ["scale"] = new { type = "integer", minimum = 1, maximum = 4 },
            }, []);
        yield return Spec(
            "editor_mouse_move",
            "Move pointer to game-client pixel position; needed for pointer-dependent native tools.",
            Props(("x", "integer"), ("y", "integer")),
            ["x", "y"]
        );
        yield return Spec(
            "editor_mouse_click",
            "Click game-client pixel position. Covers editor controls/dialog buttons lacking a command API. button left/right/middle.",
            Props(("x", "integer"), ("y", "integer"), ("button", "string")),
            ["x", "y"]
        );
        yield return Spec(
            "editor_mouse_drag",
            "Left-button stroke/selection/drag within editor client; releases button in finally. durationMs 100..5000.",
            Props(
                ("x1", "integer"),
                ("y1", "integer"),
                ("x2", "integer"),
                ("y2", "integer"),
                ("durationMs", "integer")
            ),
            ["x1", "y1", "x2", "y2"]
        );
        yield return Spec(
            "editor_mouse_wheel",
            "Scroll/zoom at current game pointer; steps -20..20.",
            Props(("steps", "integer")),
            ["steps"]
        );
        var keys = Props(("key", "string"), ("confirmDestructive", "boolean"));
        keys["modifiers"] = new
        {
            type = "array",
            items = new { type = "string", @enum = Modifiers },
            maxItems = 3,
        };
        yield return Spec(
            "editor_key",
            "Send editor key/hotkey. Letters, digits, F1..F12, ESC, ENTER, TAB, arrows, DELETE/BACKSPACE, HOME/END/PGUP/PGDN.",
            keys,
            ["key"]
        );
        yield return Spec(
            "editor_text",
            "Type Unicode text into focused editor field. UI fallback for property/trigger fields; no clipboard modification.",
            Props(("text", "string")),
            ["text"]
        );
        yield return Spec(
            "editor_place_unit",
            "Native, one-shot unit placement using internal proto name and player. x/y game-client pixels default center. Cleans preview; does not save. Verify screenshot afterward.",
            Props(("proto", "string"), ("player", "integer"), ("x", "integer"), ("y", "integer")),
            ["proto"]
        );
    }
}
