using System.Text.Json;
using System.Text.RegularExpressions;

namespace AomMcp;

/// <summary>Read-only, explicitly partial checks of live objects and caller-supplied exported controller files.</summary>
internal static partial class ScenarioChecks
{
    internal sealed record Finding(string Severity, string Code, string Message, int? UnitId = null);
    static readonly JsonElement Empty = JsonDocument.Parse("{}").RootElement.Clone();
    // Host code-scan timeout, not XS compiler validation. Candidate matches can occur in comments/strings.
    [GeneratedRegex("\\bxs(?:Enable|Disable)Rule\\s*\\(\\s*\"([^\"]+)\"", RegexOptions.CultureInvariant, 100)]
    private static partial Regex LiteralRuleReferenceRegex();

    internal static Finding[] Evaluate(LiveUnits.Unit[] units, TriggerCodec.Controller? controller,
        int[] requiredIds, int[] townCenterPlayers, double? width, double? depth)
    {
        var findings = new List<Finding>();
        var ids = units.Select(u => u.UnitId).ToHashSet();
        foreach (var id in requiredIds.Distinct())
            if (!ids.Contains(id)) findings.Add(new("warning", "missing-required-unit", "Caller-declared required live ID not found. Reloaded IDs are not persistent save IDs.", id));
        foreach (var player in townCenterPlayers.Distinct())
            if (!units.Any(u => u.Player == player && string.Equals(u.Proto, "TownCenter", StringComparison.OrdinalIgnoreCase) && u.Health > 0))
                findings.Add(new("warning", "missing-town-center", "No living exact TownCenter prototype for caller-required player " + player + ". Variants/scripts may satisfy custom objectives."));
        foreach (var unit in units)
        {
            if (unit.Proto is null) findings.Add(new("information", "unresolved-prototype", "Player-local/base prototype name unresolved; not inferred from catalog order.", unit.UnitId));
            if (width.HasValue && depth.HasValue && (unit.Position.X < 0 || unit.Position.Z < 0 || unit.Position.X > width || unit.Position.Z > depth))
                findings.Add(new("warning", "outside-map", "Object X/Z outside measured map rectangle. Intentional off-map staging is possible.", unit.UnitId));
            if (unit.MaxHealth > 0 && unit.Health <= 0) findings.Add(new("information", "nonliving-object", "Object has nonpositive current health. Could be intentional corpse/decoration.", unit.UnitId));
        }
        if (units.Length == 0) findings.Add(new("information", "empty-scene", "Live object registry empty; not necessarily invalid for trigger-generated scenarios."));
        if (controller is not null)
        {
            foreach (var message in TriggerCodec.Findings(controller)) findings.Add(new("warning", "controller-config", message));
            // ponytail: literal-call heuristics only; add XS AST/compiler integration when independently available.
            // Exact outcome call names from shipped XS syscalls and verified defense controller, not guessed aliases.
            if (!controller.Code.Contains("trPlayerSetVictorious", StringComparison.Ordinal)
                && !controller.Code.Contains("trPlayerSetDefeated", StringComparison.Ordinal))
                findings.Add(new("information", "no-literal-outcome", "No literal win/defeat call found in supplied controller. Missing objective suspected, not proven; default victory/external rules may handle outcomes."));
            foreach (var reference in LiteralRuleReferenceRegex().Matches(controller.Code).Cast<Match>().Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal))
                if (reference != "_" + controller.Name)
                    findings.Add(new("information", "unresolved-rule-candidate", "Literal rule reference not this serialized controller: " + reference + ". May be external rule or text/comment; not proof of broken reference."));
        }
        return findings.ToArray();
    }

    /// <summary>Reads the verified single-controller shape; a valid TR export with zero triggers yields null instead of refusing.</summary>
    internal static TriggerCodec.Controller? LoadController(string path, out bool empty)
    {
        var bytes = TriggerCodec.ReadFile(path);
        empty = TriggerCodec.GeneralTriggerCount(bytes) == 0;
        return empty ? null : TriggerCodec.Parse(bytes);
    }

    /// <summary>Flags missing caller-declared references, suspicious object/configuration states and controller heuristics without fixes.</summary>
    /// <param name="game">Read-only guarded editor connection.</param>
    /// <param name="args">Optional exported triggerPath, required live IDs/players and bounded findings pagination.</param>
    /// <returns>Findings, explicit skipped checks, snapshot identity and no universal validity verdict.</returns>
    public static object Query(Game game, JsonElement args)
    {
        var units = LiveUnits.Read(game);
        var controller = args.TryGetProperty("triggerPath", out var path) ? LoadController(path.GetString()!, out _) : null;
        var triggersEmpty = path.ValueKind == JsonValueKind.String && controller is null;
        var required = args.TryGetProperty("requiredUnitIds", out var ids) ? ids.EnumerateArray().Select(v => v.GetInt32()).ToArray() : [];
        var players = args.TryGetProperty("requireTownCenterPlayers", out var p) ? p.EnumerateArray().Select(v => v.GetInt32()).ToArray() : [];
        double? width = null, depth = null;
        if (game.Layout.Map is not null)
        {
            var map = JsonSerializer.SerializeToElement(EditorView.MapInfo(game, Empty));
            width = map.GetProperty("dimensions").GetProperty("worldWidth").GetDouble();
            depth = map.GetProperty("dimensions").GetProperty("worldDepth").GetDouble();
        }
        var findings = Evaluate(units, controller, required, players, width, depth);
        var offset = args.TryGetProperty("offset", out var o) ? o.GetInt32() : 0;
        var limit = args.TryGetProperty("limit", out var l) ? l.GetInt32() : LiveUnits.DefaultLimit;
        var page = findings.Skip(offset).Take(limit).ToArray();
        return new { pid = game.Pid, buildHash = game.Layout.ExeSha256, capturedAtUtc = DateTime.UtcNow,
            totalObjects = units.Length, totalFindings = findings.Length, offset, limit,
            nextOffset = offset < findings.Length && page.Length < findings.Length - offset ? (int?)(offset + page.Length) : null,
            findings = page, automaticFixes = false, atomic = false, triggerSourceIsLive = false,
            skippedChecks = new[] {
                triggersEmpty ? "Supplied trigger export contains 0 triggers; no controller heuristics run (file is not proof of current scene trigger state)."
                    : controller is null ? "No exported triggerPath supplied; triggers not inspected." : "Only supplied verified controller shape inspected; file is not proof of current scene trigger state.",
                width is null ? "Map layout unavailable; object/map bounds not inspected." : "Map bounds checked against separately measured non-atomic snapshot.",
                "Objective UI, diplomacy, scenario modes, native trigger references in other shapes, XS compilation, dynamic/scenario-name unit references and external scripts not inspected.",
            }, limitation = "Partial diagnostics, not a valid/invalid scenario verdict. Only caller-declared full live IDs treated as required references; script literals never guessed as simulation IDs. Rule/outcome scans are textual candidates, not semantic validation." };
    }

    /// <summary>Runs synthetic finding checks without memory reads, scene changes or file writes.</summary>
    public static void SelfTest()
    {
        // Synthetic object: ID7/player1, outside a 10x10 fixture map, zero health. No recovered game offsets.
        var unit = new LiveUnits.Unit(7, 0, "Hoplite", 1, new(11, 0, 5), 0, 100);
        var controller = new TriggerCodec.Controller("fixture", false, true, "xsEnableRule(\"missing\"); 5 % 2;");
        var findings = Evaluate([unit], controller, [8], [1], 10, 10);
        foreach (var code in new[] { "missing-required-unit", "missing-town-center", "outside-map", "nonliving-object", "controller-config", "no-literal-outcome", "unresolved-rule-candidate" })
            if (!findings.Any(f => f.Code == code)) throw new InvalidOperationException("Scenario finding fixture failed: " + code);
        var outcome = controller with { Active = true, Code = "trPlayerSetVictorious(1);" }; // Known shipped function, synthetic player1.
        if (Evaluate([], outcome, [], [], null, null).Any(f => f.Code == "no-literal-outcome"))
            throw new InvalidOperationException("Scenario outcome-name fixture failed.");
        if (Evaluate([unit with { Health = 100, Position = new(5, 0, 5) }], null, [7], [], 10, 10).Length != 0)
            throw new InvalidOperationException("Scenario clean fixture failed.");
    }
}
