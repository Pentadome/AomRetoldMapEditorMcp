using System.Globalization;
using System.Text;
using System.Text.Json;

namespace AomMcp;

/// <summary>Run-bound supplied debug-transcript facts, never authenticated engine/compiler proof.</summary>
internal static class RuntimeReport
{
    internal sealed record Unit(string Key, int RuntimeKbId, int RuntimeProtoId, int RuntimeStateId, int Action, int TargetKbId, double X, double Z);
    internal sealed record Plan(int Id, int State);
    internal sealed record Frame(int GameTime, Unit[] Units, Dictionary<string, int> Presence, Plan[] Plans, bool PlansObserved, string[] Errors);
    internal static Frame[] Parse(string text, string runId, int player, int minTime, int maxTime)
    {
        var frames = new List<Frame>(); var units = new List<Unit>(); var plans = new List<Plan>(); var counts = new Dictionary<string, int>(StringComparer.Ordinal); var errors = new List<string>();
        int? time = null, planCount = null; var last = -1; var lines = 0;
        static int Int(string value, int min = 0, int max = int.MaxValue)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n < min || n > max) throw new InvalidDataException("Probe numeric field outside bounds."); return n;
        }
        foreach (var raw in text.Split('\n'))
        {
            if (++lines > 20_000 || raw.Length > 8192) throw new InvalidDataException("Debug transcript exceeds line/length bounds.");
            var index = raw.IndexOf("AOMMCP1|", StringComparison.Ordinal); if (index < 0) continue;
            var fields = raw[index..].TrimEnd('\r').Split('|');
            if (fields.Length < 5 || fields[1] != runId || Int(fields[3], 1, 12) != player) throw new InvalidDataException("Wrong-run/player or malformed probe evidence.");
            var tick = Int(fields[2], minTime, maxTime); var kind = fields[4];
            if (kind == "BEGIN")
            {
                if (fields.Length != 5 || time is not null || tick <= last || frames.Count >= 1000) throw new InvalidDataException("Duplicate/nonmonotonic/incomplete evidence frame.");
                time = tick; units.Clear(); plans.Clear(); counts.Clear(); errors.Clear(); planCount = null; continue;
            }
            if (time != tick) throw new InvalidDataException("Evidence outside matching BEGIN/END frame.");
            if (kind == "END")
            {
                if (fields.Length != 5 || units.Count > 200 || plans.Count > 200 || counts.Count > 200) throw new InvalidDataException("Evidence frame bounds invalid.");
                foreach (var g in units.GroupBy(u => u.Key)) if (!counts.TryGetValue(g.Key, out var n) || n != g.Count() || g.Select(u => u.RuntimeKbId).Distinct().Count() != g.Count())
                    throw new InvalidDataException("Missing/ambiguous unit-presence evidence.");
                if (counts.Any(c => c.Value != units.Count(u => u.Key == c.Key)) || planCount is not null && planCount != plans.Count || plans.Select(p => p.Id).Distinct().Count() != plans.Count)
                    throw new InvalidDataException("Truncated/ambiguous presence/plan evidence.");
                frames.Add(new(tick, units.ToArray(), new(counts, StringComparer.Ordinal), plans.ToArray(), planCount is not null, errors.ToArray())); time = null; last = tick; continue;
            }
            if (kind == "UNIT" && fields.Length == 13 && RuntimeProbes.Token(fields[5]))
            {
                if (!double.TryParse(fields[11], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) || !double.TryParse(fields[12], NumberStyles.Float, CultureInfo.InvariantCulture, out var z)
                    || !double.IsFinite(x) || !double.IsFinite(z) || Math.Abs(x) > 1_000_000 || Math.Abs(z) > 1_000_000) throw new InvalidDataException("Runtime positions invalid.");
                units.Add(new(fields[5], Int(fields[6]), Int(fields[7]), Int(fields[8]), Int(fields[9], -1), Int(fields[10], -1), x, z));
            }
            else if (kind == "PRESENCE" && fields.Length == 7 && RuntimeProbes.Token(fields[5]))
            { if (!counts.TryAdd(fields[5], Int(fields[6], 0, 200))) throw new InvalidDataException("Duplicate selector count."); }
            else if (kind == "PLAN_COUNT" && fields.Length == 6)
            { if (planCount is not null) throw new InvalidDataException("Duplicate plan count."); planCount = Int(fields[5], 0, 200); }
            else if (kind == "PLAN" && fields.Length == 7) plans.Add(new(Int(fields[5]), Int(fields[6], -1)));
            else if (kind == "ERROR" && fields.Length == 7 && RuntimeProbes.Token(fields[5]) && RuntimeProbes.Token(fields[6])) errors.Add(fields[5] + ":" + fields[6]);
            else throw new InvalidDataException("Unknown/malformed probe record; not silently ignored.");
            if (units.Count > 200 || plans.Count > 200 || errors.Count > 200) throw new InvalidDataException("Evidence record bound exceeded.");
        }
        if (time is not null || frames.Count == 0) throw new InvalidDataException("Missing complete fresh probe frames.");
        return frames.ToArray();
    }
    internal static void Preflight(JsonElement args)
    {
        Catalog.ValidateObject(args, ["evidencePath", "expectedSha256", "runId", "player", "runStartedAtUtc", "capturedAtUtc", "maxAgeSeconds", "minGameTime", "maxGameTime", "assertions", "offset", "limit"]);
        _ = EditorFiles.LocalPath(args.GetProperty("evidencePath").GetString()!);
        if (!RuntimeProbes.Token(args.GetProperty("runId").GetString()) || args.GetProperty("player").GetInt32() is < 1 or > 12
            || args.GetProperty("expectedSha256").GetString() is not { Length: 64 } sha || !sha.All(Uri.IsHexDigit)) throw new ArgumentException("Evidence hash, run and own player required.");
        var min = args.GetProperty("minGameTime").GetInt32(); var max = args.GetProperty("maxGameTime").GetInt32();
        if (min < 0 || max < min || max > 864_000) throw new ArgumentException("Explicit fresh game-time window invalid.");
        var rules = args.GetProperty("assertions"); if (rules.ValueKind != JsonValueKind.Array || rules.GetArrayLength() is < 1 or > 200) throw new ArgumentException("assertions requires 1..200 checks.");
        _ = Time(args, "runStartedAtUtc"); _ = Time(args, "capturedAtUtc");
        foreach (var r in rules.EnumerateArray())
        {
            var check = r.GetProperty("check").GetString();
            Catalog.ValidateObject(r, check switch {
                "workerAction" => ["check", "key", "runtimeUnitId", "expectedActions"], "workerTarget" => ["check", "key", "runtimeUnitId", "expectedTargetKbId"],
                "presence" => ["check", "key", "minimum", "maximum"], "planCount" => ["check", "minimum", "maximum"], "planState" => ["check", "planId", "expectedStates"], _ => ["check"],
            });
            if (string.IsNullOrWhiteSpace(check)) throw new ArgumentException("Assertion check required.");
            if (check is "workerAction" or "workerTarget" or "presence" && !RuntimeProbes.Token(r.GetProperty("key").GetString())) throw new ArgumentException("Assertion selector key required.");
            if (check is "workerAction" or "workerTarget" && r.GetProperty("runtimeUnitId").GetInt32() < 0 || check == "workerTarget" && r.GetProperty("expectedTargetKbId").GetInt32() < -1
                || check == "planState" && r.GetProperty("planId").GetInt32() < 0) throw new ArgumentException("Assertion requires explicit runtime KB/plan IDs.");
            if (check is "presence" or "planCount" && (r.GetProperty("minimum").GetInt32() < 0 || r.GetProperty("maximum").GetInt32() < r.GetProperty("minimum").GetInt32() || r.GetProperty("maximum").GetInt32() > 200))
                throw new ArgumentException("Assertion count range invalid.");
            if (check is "workerAction" or "planState")
            {
                var values = r.GetProperty(check == "workerAction" ? "expectedActions" : "expectedStates");
                if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() is < 1 or > 200 || values.EnumerateArray().Any(v => v.GetInt32() < 0)) throw new ArgumentException("Reviewed numeric action/state list required.");
            }
        }
    }
    static DateTimeOffset Time(JsonElement args, string key)
    {
        var text = args.GetProperty(key).GetString();
        if (text is null || !text.Contains('T') || !(text.EndsWith('Z') || text.EndsWith("+00:00", StringComparison.Ordinal))
            || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time)) throw new ArgumentException("Explicit UTC timestamp required: " + key); return time.ToUniversalTime();
    }
    internal static object Execute(JsonElement args)
    {
        Preflight(args); var path = EditorFiles.LocalPath(args.GetProperty("evidencePath").GetString()!); var info = new FileInfo(path);
        if (!info.Exists || info.Length is < 1 or > 8_000_000) throw new InvalidDataException("Evidence file outside byte bound.");
        var bytes = File.ReadAllBytes(path); var sha = CheckpointDocument.Hash(bytes); CheckpointDocument.RequireHash(sha, args.GetProperty("expectedSha256").GetString());
        var now = DateTimeOffset.UtcNow; var started = Time(args, "runStartedAtUtc"); var captured = Time(args, "capturedAtUtc"); var maxAge = args.TryGetProperty("maxAgeSeconds", out var age) ? age.GetInt32() : 120;
        if (captured < started || captured > now.AddSeconds(5) || (now - captured).TotalSeconds > maxAge || started > now
            || info.LastWriteTimeUtc < started.UtcDateTime.AddSeconds(-2) || info.LastWriteTimeUtc > now.UtcDateTime.AddSeconds(5)) throw new InvalidDataException("Stale/future/mismatched run-bound evidence timestamps.");
        string text; try { text = new UTF8Encoding(false, true).GetString(bytes); } catch (DecoderFallbackException e) { throw new InvalidDataException("Evidence requires strict UTF-8.", e); }
        var frames = Parse(text, args.GetProperty("runId").GetString()!, args.GetProperty("player").GetInt32(), args.GetProperty("minGameTime").GetInt32(), args.GetProperty("maxGameTime").GetInt32());
        return Report(frames, args, new { path, sha256 = sha, started, captured, fileLastWriteUtc = info.LastWriteTimeUtc });
    }
    internal static object Report(Frame[] frames, JsonElement args, object provenance)
    {
        var frame = frames[^1]; var checks = new List<object>();
        foreach (var a in args.GetProperty("assertions").EnumerateArray())
        {
            var check = a.GetProperty("check").GetString(); var status = "unsupported"; var reason = "No reviewed observation for requested check.";
            if (frame.Errors.Length > 0) reason = "Frame contains race/ambiguity/overflow errors.";
            else if (check is "workerAction" or "workerTarget")
            {
                var key = a.GetProperty("key").GetString(); var id = a.GetProperty("runtimeUnitId").GetInt32(); var matches = frame.Units.Where(u => u.Key == key && u.RuntimeKbId == id).ToArray();
                if (matches.Length != 1) reason = "Missing/ambiguous fresh runtime KB identity; saved IDs cannot substitute.";
                else if (check == "workerAction" && matches[0].Action < 0) reason = "Action code unavailable; no guessed enumeration.";
                else
                {
                    var pass = check == "workerTarget" ? matches[0].TargetKbId == a.GetProperty("expectedTargetKbId").GetInt32()
                        : a.GetProperty("expectedActions").EnumerateArray().Any(x => x.GetInt32() == matches[0].Action);
                    status = pass ? "pass" : "fail"; reason = "Compared fresh supplied transcript values; numeric codes not interpreted as activity names.";
                }
            }
            else if (check == "presence")
            {
                if (frame.Presence.TryGetValue(a.GetProperty("key").GetString()!, out var count))
                {
                    status = count >= a.GetProperty("minimum").GetInt32() && count <= a.GetProperty("maximum").GetInt32() ? "pass" : "fail";
                    reason = "KB query-state/prototype constraints caller-reviewed; count is not independent construction/rebuild proof.";
                }
                else reason = "No complete presence observation for selector.";
            }
            else if (check == "planCount" && frame.PlansObserved)
            { status = frame.Plans.Length >= a.GetProperty("minimum").GetInt32() && frame.Plans.Length <= a.GetProperty("maximum").GetInt32() ? "pass" : "fail"; reason = "Own-context active plans observed."; }
            else if (check == "planState" && frame.PlansObserved)
            {
                var plan = frame.Plans.SingleOrDefault(p => p.Id == a.GetProperty("planId").GetInt32());
                if (plan is not null && plan.State >= 0) { status = a.GetProperty("expectedStates").EnumerateArray().Any(x => x.GetInt32() == plan.State) ? "pass" : "fail"; reason = "Own-context numeric plan state; semantic enum mapping unverified."; }
            }
            checks.Add(new { check, status, reason });
        }
        var offset = args.TryGetProperty("offset", out var o) ? o.GetInt32() : 0; var limit = args.TryGetProperty("limit", out var l) ? l.GetInt32() : 20;
        return new { provenance, runId = args.GetProperty("runId").GetString(), identityDomain = "runtime-kb only", latestGameTime = frame.GameTime, frameCount = frames.Length,
            assertions = checks, errors = frame.Errors, total = frame.Units.Length, offset, limit,
            nextOffset = offset + limit < frame.Units.Length ? (int?)(offset + limit) : null, units = frame.Units.Skip(offset).Take(limit).ToArray(), frame.Presence, frame.Plans,
            engineTransportVerified = false, compilationVerified = false, runtimeMemoryVerified = false,
            unavailable = new[] { "No independently confirmed debug-output file transport/compiler channel.", "No corroborated runtime action/target/AI-plan memory layout." },
            limitation = "Assertions evaluate supplied, hash/time/run-bound transcript facts before paging. Caller-provided provenance is not authenticated engine evidence; no saved/editor-ID equivalence, gameplay success or reload-persistence claim." };
    }
    internal static void SelfTest()
    {
        const string prefix = "AOMMCP1|fixture|10|6|";
        var text = prefix + "BEGIN\n" + prefix + "UNIT|worker|701|12|2|9|801|10|20\n" + prefix + "PRESENCE|worker|1\n" + prefix + "PLAN_COUNT|1\n" + prefix + "PLAN|3|4\n" + prefix + "END\n";
        var frames = Parse(text, "fixture", 6, 10, 10);
        var args = JsonSerializer.SerializeToElement(new { runId = "fixture", offset = 999, limit = 1, assertions = new object[] {
            new { check = "workerTarget", key = "worker", runtimeUnitId = 701, expectedTargetKbId = 801 },
            new { check = "planState", planId = 3, expectedStates = (int[])[4] }, new { check = "compilation" },
        } });
        var result = JsonSerializer.SerializeToElement(Report(frames, args, "synthetic"));
        if (result.GetProperty("assertions")[0].GetProperty("status").GetString() != "pass" || result.GetProperty("assertions")[1].GetProperty("status").GetString() != "pass"
            || result.GetProperty("assertions")[2].GetProperty("status").GetString() != "unsupported" || result.GetProperty("units").GetArrayLength() != 0)
            throw new InvalidOperationException("Runtime evidence assertion/paging/proof-scope fixture failed.");
        foreach (var bad in new[] { text.Replace("fixture", "other", StringComparison.Ordinal), text.Replace("|6|", "|5|", StringComparison.Ordinal), text[..text.LastIndexOf(prefix + "END", StringComparison.Ordinal)], text + text })
        {
            try { _ = Parse(bad, "fixture", 6, 10, 10); throw new InvalidOperationException("Wrong-run/truncated/duplicate evidence accepted."); } catch (InvalidDataException) { }
        }
        try { _ = Parse(text, "fixture", 6, 11, 20); throw new InvalidOperationException("Stale game-time evidence accepted."); } catch (InvalidDataException) { }
        var race = Parse(text.Replace(prefix + "END", prefix + "ERROR|worker|unit-race\n" + prefix + "END", StringComparison.Ordinal), "fixture", 6, 10, 10);
        if (JsonSerializer.SerializeToElement(Report(race, args, "synthetic-race")).GetProperty("assertions")[0].GetProperty("status").GetString() != "unsupported")
            throw new InvalidOperationException("Racy runtime observations yielded pass.");
        var directory = Path.Combine(Path.GetTempPath(), "aom-evidence-fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "evidence.txt"); File.WriteAllText(path, text); var now = DateTimeOffset.UtcNow;
            var query = args.EnumerateObject().ToDictionary(p => p.Name, p => (object)p.Value.Clone());
            query["evidencePath"] = path; query["expectedSha256"] = Layout.Hash(path); query["player"] = 6; query["minGameTime"] = 10; query["maxGameTime"] = 10;
            query["runStartedAtUtc"] = now.AddSeconds(-10).ToString("O", CultureInfo.InvariantCulture); query["capturedAtUtc"] = now.AddSeconds(-1).ToString("O", CultureInfo.InvariantCulture); query["maxAgeSeconds"] = 60;
            _ = Execute(JsonSerializer.SerializeToElement(query));
            query["runStartedAtUtc"] = now.AddSeconds(-200).ToString("O", CultureInfo.InvariantCulture); query["capturedAtUtc"] = now.AddSeconds(-120).ToString("O", CultureInfo.InvariantCulture);
            try { _ = Execute(JsonSerializer.SerializeToElement(query)); throw new InvalidOperationException("Stale wall-time transcript accepted."); } catch (InvalidDataException) { }
        }
        finally { Directory.Delete(directory, true); }
    }
}
