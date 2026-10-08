using System.Globalization;
using AomMcp;

if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
    throw new PlatformNotSupportedException("Windows x64 required.");
// Default from inspected local Steam installation; --exe overrides different library locations.
var exe = @"C:\Program Files (x86)\Steam\steamapps\common\Age of Mythology Retold\AoMRT_s.exe";
// Native filename comes from native/build.cmd; project copies it beside managed host.
var bridge = Path.Combine(AppContext.BaseDirectory, "AomEditorBridge.dll");
string? layoutPath = null,
    ui = null,
    generate = null,
    buildXsApi = null;
int? pid = null;
var selfTest = false;
var localOnly = false;
var fullTools = false;
for (var i = 0; i < args.Length; i++)
{
    string Value() =>
        ++i < args.Length ? args[i] : throw new ArgumentException("Missing option value.");
    switch (args[i])
    {
        case "--exe":
            exe = Path.GetFullPath(Value());
            break;
        case "--bridge":
            bridge = Path.GetFullPath(Value());
            break;
        case "--layout":
            layoutPath = Path.GetFullPath(Value());
            break;
        case "--ui":
            ui = Path.GetFullPath(Value());
            break;
        case "--pid":
            pid = int.Parse(Value(), CultureInfo.InvariantCulture);
            break;
        case "--generate":
            generate = Path.GetFullPath(Value());
            break;
        case "--build-xs-api":
            // Maintainer-only: refresh shipped signature-only xs/xs_api.json from an installed game.
            buildXsApi = Path.GetFullPath(Value());
            break;
        case "--toolset":
            fullTools = Value() switch
            {
                "core" => false,
                "full" => true,
                _ => throw new ArgumentException("--toolset must be core or full."),
            };
            break;
        case "--self-test":
            selfTest = true;
            break;
        case "--self-test-local":
            selfTest = true;
            localOnly = true;
            break;
        case "--help":
            Console.WriteLine(
                "AomMcp [--exe path] [--layout json] [--bridge dll] [--ui extracted-xml-dir] [--pid number]\n  --toolset core|full: compact helpers/essential commands (default core), or all generated tools\n  --generate directory: write regenerated catalog and read-only discovered layout candidates\n  --self-test: tests including installed catalog coverage, no game edits\n  --self-test-local: managed/native checks without installed game or catalogs\nDefault: stdio MCP; open offline scenario editor first. Run build.ps1 to compile native bridge."
            );
            return;
        default:
            throw new ArgumentException("Unknown option " + args[i]);
    }
}
if (ui == null)
{
    for (DirectoryInfo? d = new(AppContext.BaseDirectory); d != null; d = d.Parent)
    {
        // Repository generator/build.ps1 convention: CryBar-decoded editor XML lives in generated/ui.
        var candidate = Path.Combine(d.FullName, "generated", "ui");
        if (Directory.Exists(candidate))
        {
            ui = candidate;
            break;
        }
    }
}
if (selfTest)
{
    Catalog.SelfTest();
    Ui.SelfTest();
    Server.OverviewSelfTest();
    ScreenProbe.SelfTest();
    UiLayouts.SelfTest();
    UiRead.SelfTest();
    LiveUnits.SelfTest();
    LiveWorld.SelfTest();
    EditorView.SelfTest();
    TriggerCodec.SelfTest();
    CampaignTriggers.SelfTest();
    CheckpointDocument.SelfTest();
    TriggerObjects.SelfTest();
    CheckpointUnits.SelfTest();
    ScenarioDiff.SelfTest();
    AiInstaller.SelfTest();
    PlayerSettings.SelfTest();
    StartupOrders.SelfTest();
    RuntimeProbes.SelfTest();
    RuntimeReport.SelfTest();
    RuntimeTelemetry.SelfTest();
    PlaytestWorkflow.SelfTest();
    ExportFormats.SelfTest();
    EditorFiles.SelfTest();
    ScenarioChecks.SelfTest();
    Formations.SelfTest();
    SceneGeometry.SelfTest();
    using var native = new Bridge(bridge);
    native.SelfTest();
    if (localOnly)
    {
        Console.WriteLine("Local self-tests passed; no installed game/catalog required. No game calls performed.");
        return;
    }
    var catalog = new Catalog(exe, ui);
    // Required native placement names from installed help/editor.con. 300 is a host regression floor,
    // below measured 434 exposed commands after unsafe loadScenario removal, not a game API count guarantee.
    if (
        !catalog.Commands.ContainsKey("uiSetProtoCursor")
        || !catalog.Commands.ContainsKey("uiPlaceAtPointer")
        || catalog.Commands.Count < 300
    )
        throw new InvalidOperationException("Catalog coverage self-test failed.");
    Console.WriteLine(
        $"Self-tests passed; {catalog.Commands.Count} command tools, {catalog.Actions.Count} UI actions. No game calls performed."
    );
    return;
}
if (buildXsApi != null)
{
    XsApi.Build(exe, buildXsApi);
    return;
}
if (generate != null)
{
    Generator.Generate(exe, generate, ui, pid);
    return;
}
// Host accepted-layout convention: executable SHA-256 filename; no stale-RVA/version-name fallback.
layoutPath ??= Path.Combine(AppContext.BaseDirectory, "layouts", Layout.Hash(exe) + ".json");
if (!File.Exists(layoutPath))
    throw new InvalidOperationException(
        "Unknown game build. Run --generate and review discovered layout; no stale offset fallback."
    );
using var server = new Server(exe, layoutPath, bridge, ui, pid, fullTools);
await server.RunAsync();
