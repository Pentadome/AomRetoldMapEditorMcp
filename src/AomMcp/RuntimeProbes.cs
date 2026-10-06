using System.Globalization;
using System.Text;
using System.Text.Json;

namespace AomMcp;

/// <summary>Opt-in own-context XS rules. Query results are runtime KB identities, never saved/editor IDs.</summary>
internal static class RuntimeProbes
{
    internal static bool Token(string? value) => value is { Length: > 0 and <= 64 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
    static bool Preview(JsonElement args) => !args.TryGetProperty("preview", out var p) || p.GetBoolean();
    internal static void Preflight(JsonElement args)
    {
        Catalog.ValidateObject(args, ["runId", "player", "intervalSeconds", "selectors", "includePlans", "runtimeIdentityReviewed", "preview", "outputPath", "confirmWrite"]);
        if (!Token(args.GetProperty("runId").GetString()) || args.GetProperty("player").GetInt32() is < 1 or > 12
            || !args.GetProperty("runtimeIdentityReviewed").GetBoolean()) throw new ArgumentException("Run token, own player and reviewed runtime prototype/state IDs required.");
        if (args.TryGetProperty("intervalSeconds", out var interval) && interval.GetInt32() is < 1 or > 60) throw new ArgumentException("Probe interval must be 1..60 seconds.");
        var selectors = args.GetProperty("selectors"); if (selectors.ValueKind != JsonValueKind.Array || selectors.GetArrayLength() is < 1 or > 200) throw new ArgumentException("selectors requires 1..200 entries.");
        var keys = new HashSet<string>(StringComparer.Ordinal); var budget = 0;
        foreach (var s in selectors.EnumerateArray())
        {
            Catalog.ValidateObject(s, ["key", "kind", "runtimeProtoId", "runtimeStateId", "maxMatches", "runtimeUnitId", "savedIdAnnotation", "area"]);
            var key = s.GetProperty("key").GetString();
            if (!Token(key) || !keys.Add(key!) || s.GetProperty("kind").GetString() is not "worker" and not "defense"
                || s.GetProperty("runtimeProtoId").GetInt32() < 0 || s.GetProperty("runtimeStateId").GetInt32() < 0 || s.GetProperty("maxMatches").GetInt32() is < 1 or > 200)
                throw new ArgumentException("Invalid/duplicate runtime selector.");
            budget += s.GetProperty("maxMatches").GetInt32(); if (budget > 200) throw new ArgumentException("At most 200 unit observations per frame.");
            if (s.TryGetProperty("runtimeUnitId", out var id) && id.GetInt32() < 0) throw new ArgumentException("Runtime KB ID must be nonnegative.");
            if (s.TryGetProperty("area", out var area))
            {
                Catalog.ValidateObject(area, ["minX", "maxX", "minZ", "maxZ"]);
                var minX = area.GetProperty("minX").GetDouble(); var maxX = area.GetProperty("maxX").GetDouble(); var minZ = area.GetProperty("minZ").GetDouble(); var maxZ = area.GetProperty("maxZ").GetDouble();
                if (!double.IsFinite(minX + maxX + minZ + maxZ) || minX > maxX || minZ > maxZ || Math.Max(Math.Abs(minX), Math.Abs(maxX)) > 1_000_000 || Math.Max(Math.Abs(minZ), Math.Abs(maxZ)) > 1_000_000)
                    throw new ArgumentException("Runtime world-XZ area outside bounds.");
            }
        }
        if (!Preview(args)) { EditorFiles.Confirm(args, "confirmWrite"); _ = EditorFiles.ApprovedNewPath(args.GetProperty("outputPath").GetString()!, ".xs"); }
    }
    internal static string Generate(JsonElement args)
    {
        Preflight(args); var run = args.GetProperty("runId").GetString()!; var player = args.GetProperty("player").GetInt32();
        var interval = args.TryGetProperty("intervalSeconds", out var seconds) ? seconds.GetInt32() : 5;
        var suffix = CheckpointDocument.Hash(Encoding.UTF8.GetBytes(run))[..12]; var b = new StringBuilder();
        b.AppendLine("// Opt-in own-player AI rule; not a standalone personality. No context switching or saved-ID dispatch.");
        b.AppendLine("// aiEcho targets AI Debug Output. Compilation, capture transport and runtime effects are UNVERIFIED.");
        b.AppendLine("// runtimeProtoId/runtimeStateId must be independently reviewed in current KB; saved PT indexes are NOT runtime IDs.");
        b.AppendLine(CultureInfo.InvariantCulture, $"rule aom_mcp_probe_{suffix}\nactive\nminInterval {interval}\n{{");
        b.AppendLine("  int t = xsGetTime();\n  int cp = xsGetContextPlayer();");
        b.AppendLine(CultureInfo.InvariantCulture, $"  string p = \"AOMMCP1|{run}|\" + t + \"|\" + cp;\n  aiEcho(p + \"|BEGIN\");");
        b.AppendLine(CultureInfo.InvariantCulture, $"  if (cp != {player}) {{ aiEcho(p + \"|ERROR|context|wrong-player\"); }} else {{");
        var index = 0;
        foreach (var s in args.GetProperty("selectors").EnumerateArray())
        {
            var k = index++; var key = s.GetProperty("key").GetString()!; var proto = s.GetProperty("runtimeProtoId").GetInt32(); var state = s.GetProperty("runtimeStateId").GetInt32(); var max = s.GetProperty("maxMatches").GetInt32();
            if (s.TryGetProperty("savedIdAnnotation", out var saved)) b.AppendLine(CultureInfo.InvariantCulture, $"    // {key}: saved ID {saved.GetUInt32()} is annotation only; no identity equivalence claimed.");
            b.AppendLine(CultureInfo.InvariantCulture, $"    int q{k} = kbUnitQueryCreate(\"aom_mcp_{suffix}_{k}\");");
            b.AppendLine(CultureInfo.InvariantCulture, $"    kbUnitQuerySetPlayerID(q{k}, cp, true);\n    kbUnitQuerySetUnitType(q{k}, {proto});\n    kbUnitQuerySetState(q{k}, {state});\n    kbUnitQueryResetResults(q{k});");
            b.AppendLine(CultureInfo.InvariantCulture, $"    int n{k} = kbUnitQueryExecute(q{k});\n    int matches{k} = 0;");
            b.AppendLine(CultureInfo.InvariantCulture, $"    if (n{k} < 0 || n{k} > 200) {{ aiEcho(p + \"|ERROR|{key}|query-overflow\"); }} else {{");
            b.AppendLine(CultureInfo.InvariantCulture, $"      for (int i{k} = 0; i{k} < n{k}; i{k}++) {{\n        int u{k} = kbUnitQueryGetResult(q{k}, i{k});\n        vector pos{k} = kbUnitGetPosition(u{k});");
            var filter = $"u{k} >= 0 && kbUnitGetPlayerID(u{k}) == cp && kbUnitGetProtoUnitID(u{k}) == {proto} && kbUnitGetState(u{k}) == {state}";
            if (s.TryGetProperty("runtimeUnitId", out var uid)) filter += $" && u{k} == {uid.GetInt32()}";
            if (s.TryGetProperty("area", out var area)) foreach (var (field, component, op) in new[] { ("minX", "x", ">="), ("maxX", "x", "<="), ("minZ", "z", ">="), ("maxZ", "z", "<=") })
                filter += $" && pos{k}.{component} {op} {area.GetProperty(field).GetDouble().ToString("0.################", CultureInfo.InvariantCulture)}";
            b.AppendLine(CultureInfo.InvariantCulture, $"        if ({filter}) {{\n          matches{k} = matches{k} + 1;\n          int action{k} = kbUnitGetActionType(u{k});\n          int target{k} = kbUnitGetTargetUnitID(u{k});");
            b.AppendLine(CultureInfo.InvariantCulture, $"          if (kbUnitGetPlayerID(u{k}) != cp || kbUnitGetProtoUnitID(u{k}) != {proto} || kbUnitGetState(u{k}) != {state} || kbUnitGetActionType(u{k}) != action{k} || kbUnitGetTargetUnitID(u{k}) != target{k}) {{ aiEcho(p + \"|ERROR|{key}|unit-race\"); }}");
            b.AppendLine(CultureInfo.InvariantCulture, $"          aiEcho(p + \"|UNIT|{key}|\" + u{k} + \"|\" + {proto} + \"|\" + {state} + \"|\" + action{k} + \"|\" + target{k} + \"|\" + pos{k}.x + \"|\" + pos{k}.z);");
            b.AppendLine(CultureInfo.InvariantCulture, $"        }}\n      }}\n    }}\n    aiEcho(p + \"|PRESENCE|{key}|\" + matches{k});\n    if (matches{k} > {max}) {{ aiEcho(p + \"|ERROR|{key}|ambiguous-or-overflow\"); }}\n    kbUnitQueryDestroy(q{k});");
        }
        if (args.TryGetProperty("includePlans", out var plans) && plans.GetBoolean())
        {
            b.AppendLine("    int np = aiPlanGetActiveCount(false);\n    aiEcho(p + \"|PLAN_COUNT|\" + np);\n    if (np < 0 || np > 200) { aiEcho(p + \"|ERROR|plans|plan-overflow\"); } else {");
            b.AppendLine("      for (int pi = 0; pi < np; pi++) {\n        int plan = aiPlanGetIDByActiveIndex(pi, false);\n        int state = aiPlanGetState(plan);\n        if (plan < 0 || aiPlanGetIDByActiveIndex(pi, false) != plan || aiPlanGetState(plan) != state) { aiEcho(p + \"|ERROR|plans|plan-race\"); }\n        aiEcho(p + \"|PLAN|\" + plan + \"|\" + state);\n      }\n    }\n    if (aiPlanGetActiveCount(false) != np) { aiEcho(p + \"|ERROR|plans|plan-count-race\"); }");
        }
        b.AppendLine("  }\n  aiEcho(p + \"|END\");\n}"); return b.ToString();
    }
    internal static object Execute(JsonElement args)
    {
        var code = Generate(args); var bytes = Encoding.UTF8.GetBytes(code); var sha = CheckpointDocument.Hash(bytes); string? written = null;
        if (!Preview(args))
        {
            written = EditorFiles.ApprovedNewPath(args.GetProperty("outputPath").GetString()!, ".xs");
            try { using (var file = new FileStream(written, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(bytes); file.Flush(true); } CheckpointDocument.RequireHash(Layout.Hash(written), sha); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { throw new WorkflowFailure("PROBE_WRITE_OUTCOME_UNKNOWN", "write", e.Message, false, true, "Inspect retained new probe file; do not retry or clean up automatically.", written, written, e); }
        }
        return new { preview = Preview(args), path = written, sha256 = sha, runId = args.GetProperty("runId").GetString(), code,
            selectors = args.GetProperty("selectors").Clone(), identityDomain = "runtime-kb; fresh own-player queries; saved IDs annotate only", compilationVerified = false, runtimeVerified = false,
            captureTransport = "unavailable: aiEcho debug-window output only; no independently confirmed file sink",
            limitation = "Integrate opt-in rule separately in approved custom AI. Runtime prototype/state IDs and spatial associations need independent review. No AI installation, context switching, editor IDs, native getters or game input." };
    }
    internal static void SelfTest()
    {
        var args = JsonSerializer.SerializeToElement(new { runId = "fixture_run", player = 6, runtimeIdentityReviewed = true, includePlans = true,
            selectors = new[] { new { key = "workers", kind = "worker", runtimeProtoId = 12, runtimeStateId = 2, maxMatches = 44, savedIdAnnotation = 31332 } } });
        var code = Generate(args);
        if (code.Contains("xsSetContextPlayer", StringComparison.Ordinal) || code.Contains("kbUnitGetActionType(31332)", StringComparison.Ordinal)
            || !code.Contains("kbUnitQueryExecute", StringComparison.Ordinal) || !code.Contains("|END", StringComparison.Ordinal) || !code.Contains("aiPlanGetIDByActiveIndex(pi, false)", StringComparison.Ordinal))
            throw new InvalidOperationException("Probe context/identity/plan/frame safety fixture failed.");
        var directory = Path.Combine(Path.GetTempPath(), "aom-probe-fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "probe.xs"); var values = args.EnumerateObject().ToDictionary(p => p.Name, p => (object)p.Value.Clone()); values["outputPath"] = file;
            _ = Execute(JsonSerializer.SerializeToElement(values)); if (File.Exists(file)) throw new InvalidOperationException("Probe preview wrote output.");
            values["preview"] = false; values["confirmWrite"] = true; var result = JsonSerializer.SerializeToElement(Execute(JsonSerializer.SerializeToElement(values)));
            if (Layout.Hash(file) != result.GetProperty("sha256").GetString()) throw new InvalidOperationException("New probe output hash failed.");
            try { _ = Execute(JsonSerializer.SerializeToElement(values)); throw new InvalidOperationException("Probe overwrite accepted."); } catch (ArgumentException) { }
        }
        finally { Directory.Delete(directory, true); }
    }
}
