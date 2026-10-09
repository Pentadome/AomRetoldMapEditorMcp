using System.Numerics;
using System.Text.Json;

namespace AomMcp;

/// <summary>Saved-ID, caller-constrained work planning. Does not apply TR or prove runtime resource/path semantics.</summary>
internal static class StartupOrders
{
    internal sealed record Job(int EffectIndex, uint WorkerId, uint TargetId);
    internal sealed record Assignment(uint WorkerId, uint TargetId, double Distance, bool Preserved);
    internal sealed record Audit(Job[] Jobs, int[] UnsupportedEffects, uint[] InvalidWorkers, uint[] DuplicateWorkers, uint[] ConflictingWorkers, uint[] UnassignedWorkers);
    static readonly string[] Keys = ["SrcObject", "DstObject", "EventID"];
    static bool ObjectsValid(CampaignTriggers.Arg a) => a.ValueType == 4 && a.Objects is { Length: > 0 and <= 200 } && a.SelectionFlag is 0 or 1
        && a.Values.SequenceEqual(a.Objects.Select(o => o.UnitId.ToString(System.Globalization.CultureInfo.InvariantCulture)))
        && a.Objects.Select(o => o.UnitId).Distinct().Count() == a.Objects.Length && a.Objects.All(o => o.Player <= 12 && o.Proto.Length is > 0 and <= 128 && !o.Proto.Any(char.IsControl));
    internal static bool Work(CampaignTriggers.Element e)
    {
        if (e.Name != "Unit: Task" || e.Command.Trim() != "true" || e.Args.Length != 3 || !e.Args.Select(a => a.Key).SequenceEqual(Keys) || e.Extras is not { Length: 3 }
            || e.Args.Any(a => a.Magic != 1 || a.Trailer != 0) || e.Args[2].ValueFlag != 0 || e.Trailer is not { Length: 2 } || e.Trailer.Any(b => b != 0)) return false;
        var extras = e.Extras;
        return ObjectsValid(e.Args[0]) && ObjectsValid(e.Args[1]) && e.Args[1].Objects!.Length == 1
            && e.Args[2].ValueType == 8 && e.Args[2].Values.SequenceEqual((string[])["-1"])
            && extras[0].Expression == "trUnitSelectClear();" && extras[0].Flag == 0 && extras[0].Strings.Length == 0
            && extras[1].Expression == "trUnitSelectByID(%SrcObject%);" && extras[1].Flag == 1 && extras[1].Strings.SequenceEqual((string[])["SrcObject"])
            && extras[2].Expression == "trUnitDoWorkOnUnit(%DstObject%, %EventID%);" && extras[2].Flag == 0 && extras[2].Strings.Length == 0;
    }
    static bool Matches(CampaignTriggers.ObjectRef o, IReadOnlyDictionary<uint, CheckpointUnits.Unit> units) => units.TryGetValue(o.UnitId, out var u) && u.Player == o.Player && u.Proto is not null && u.Proto == o.Proto;
    internal static Audit Inspect(CampaignTriggers.Trigger trigger, IReadOnlyDictionary<uint, CheckpointUnits.Unit> units, HashSet<uint> selected, HashSet<int> removed)
    {
        var jobs = new List<Job>(); var unknown = new HashSet<int>(); var invalid = new HashSet<uint>();
        for (var i = 0; i < trigger.Effects.Length; i++)
        {
            var e = trigger.Effects[i]; if (removed.Contains(i)) continue;
            var relevant = e.Args.Any(a => a.Objects?.Any(o => selected.Contains(o.UnitId)) == true
                || a.Values.Any(v => uint.TryParse(v, out var id) && selected.Contains(id)));
            if (!Work(e))
            {
                if (relevant || e.Name.Contains("CodeSnippet", StringComparison.OrdinalIgnoreCase) || e.Args.Any(a => a.Key.Equals("codeSnippet", StringComparison.OrdinalIgnoreCase))) unknown.Add(i);
                continue;
            }
            var src = e.Args[0].Objects!; var dst = e.Args[1].Objects![0];
            foreach (var worker in src)
            {
                if (!Matches(worker, units) || !Matches(dst, units)) { if (selected.Contains(worker.UnitId)) invalid.Add(worker.UnitId); continue; }
                jobs.Add(new(i, worker.UnitId, dst.UnitId));
            }
        }
        // Unknown conditional code/selected-object expressions cannot establish worker idleness either.
        if (trigger.Conditions.Any(e => e.Args.Any(a => a.Key.Equals("codeSnippet", StringComparison.OrdinalIgnoreCase)
            || a.Objects?.Any(o => selected.Contains(o.UnitId)) == true))) unknown.Add(-1);
        var selectedJobs = jobs.Where(j => selected.Contains(j.WorkerId)).ToArray();
        var grouped = selectedJobs.GroupBy(j => j.WorkerId).ToArray();
        return new(jobs.ToArray(), unknown.Order().ToArray(), invalid.Order().ToArray(), grouped.Where(g => g.Count() > 1).Select(g => g.Key).Order().ToArray(),
            grouped.Where(g => g.Select(j => j.TargetId).Distinct().Count() > 1).Select(g => g.Key).Order().ToArray(), selected.Except(selectedJobs.Select(j => j.WorkerId)).Order().ToArray());
    }
    readonly record struct Cost(decimal Distance, BigInteger Tie) : IComparable<Cost>
    {
        public int CompareTo(Cost other) { var c = Distance.CompareTo(other.Distance); return c != 0 ? c : Tie.CompareTo(other.Tie); }
        public static Cost operator +(Cost a, Cost b) => new(a.Distance + b.Distance, a.Tie + b.Tie);
        public static Cost operator -(Cost a, Cost b) => new(a.Distance - b.Distance, a.Tie - b.Tie);
    }
    sealed class Edge(int to, int reverse, int capacity, Cost cost)
    {
        internal int To = to, Reverse = reverse, Capacity = capacity;
        internal Cost Cost = cost;
    }
    internal static double Distance(CheckpointUnits.Unit a, CheckpointUnits.Unit b) => Math.Sqrt(Math.Pow((double)a.X - b.X, 2) + Math.Pow((double)a.Z - b.Z, 2));
    internal static Assignment[] Match(CheckpointUnits.Unit[] workers, CheckpointUnits.Unit[] targets, int[] capacities, double maxDistance)
    {
        if (workers.Length > 200 || targets.Length > 200 || capacities.Length != targets.Length || capacities.Any(c => c is < 0 or > 200)
            || !double.IsFinite(maxDistance) || maxDistance <= 0 || maxDistance > 3_000_000) throw new ArgumentException("Assignment bounds invalid.");
        if (workers.Length == 0) return [];
        workers = workers.OrderBy(w => w.UnitId).ToArray();
        var targetPairs = targets.Select((t, i) => (Unit: t, Capacity: capacities[i])).OrderBy(t => t.Unit.UnitId).ToArray();
        targets = targetPairs.Select(t => t.Unit).ToArray(); capacities = targetPairs.Select(t => t.Capacity).ToArray();
        if (workers.Select(w => w.UnitId).Distinct().Count() != workers.Length || targets.Select(t => t.UnitId).Distinct().Count() != targets.Length || capacities.Sum() < workers.Length)
            throw new ArgumentException("Duplicate selections or insufficient target capacity.");
        var start = 0; var targetBase = 1 + workers.Length; var sink = targetBase + targets.Length;
        var graph = Enumerable.Range(0, sink + 1).Select(_ => new List<Edge>()).ToArray();
        void Add(int a, int b, int capacity, Cost cost)
        {
            graph[a].Add(new(b, graph[b].Count, capacity, cost)); graph[b].Add(new(a, graph[a].Count - 1, 0, new Cost(0, 0) - cost));
        }
        for (var i = 0; i < workers.Length; i++)
        {
            Add(start, i + 1, 1, new(0, 0)); var weight = BigInteger.Pow(targets.Length + 1, workers.Length - i - 1);
            for (var j = 0; j < targets.Length; j++)
            {
                var d = Distance(workers[i], targets[j]); if (d <= maxDistance && capacities[j] > 0) Add(i + 1, targetBase + j, 1, new((decimal)d, weight * j));
            }
        }
        for (var j = 0; j < targets.Length; j++) Add(targetBase + j, sink, capacities[j], new(0, 0));
        var potentials = new Cost[graph.Length]; var infinity = new Cost(1_000_000_000_000m, 0);
        for (var flow = 0; flow < workers.Length; flow++)
        {
            var costs = Enumerable.Repeat(infinity, graph.Length).ToArray(); var previous = new (int Node, int Edge)[graph.Length];
            var queue = new PriorityQueue<int, Cost>(); costs[start] = new(0, 0); queue.Enqueue(start, costs[start]);
            var relaxations = 0;
            while (queue.TryDequeue(out var node, out var queued))
            {
                if (queued.CompareTo(costs[node]) != 0) continue;
                for (var i = 0; i < graph[node].Count; i++)
                {
                    var e = graph[node][i]; if (e.Capacity == 0) continue;
                    var candidate = costs[node] + e.Cost + potentials[node] - potentials[e.To];
                    if (candidate.CompareTo(costs[e.To]) >= 0) continue;
                    if (++relaxations > 2_000_000) throw new InvalidDataException("Matching search exceeded bounded relaxations.");
                    costs[e.To] = candidate; previous[e.To] = (node, i); queue.Enqueue(e.To, candidate);
                }
            }
            if (costs[sink].CompareTo(infinity) == 0) throw new ArgumentException("No complete assignment within maxDistance/capacity; no partial write.");
            for (var i = 0; i < graph.Length; i++) if (costs[i].CompareTo(infinity) < 0) potentials[i] += costs[i];
            for (var node = sink; node != start;)
            {
                var (from, index) = previous[node]; var e = graph[from][index]; e.Capacity--; graph[node][e.Reverse].Capacity++; node = from;
            }
        }
        return workers.Select((w, i) =>
        {
            var e = graph[i + 1].Single(e => e.To >= targetBase && e.To < sink && e.Capacity == 0); var t = targets[e.To - targetBase];
            return new Assignment(w.UnitId, t.UnitId, Distance(w, t), false);
        }).ToArray();
    }
    internal static void SelfTest()
    {
        static CheckpointUnits.Unit Unit(uint id, string proto, float x = 0, float z = 0) => new(id, 1, proto, [proto], x, 0, z, null, "", "");
        static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);
        static object Tuple(CheckpointUnits.Unit u) => new { unitId = u.UnitId, player = u.Player, proto = u.Proto };
        static void Refuse(Action action)
        {
            try { action(); } catch (ArgumentException) { return; } catch (InvalidDataException) { return; }
            throw new InvalidOperationException("Unsafe/incomplete startup plan accepted.");
        }
        var farmers = Enumerable.Range(0, 44).Select(i => Unit((uint)(100 + i), "VillagerAztec", i * 2)).ToArray();
        var farms = Enumerable.Range(0, 44).Select(i => Unit((uint)(200 + i), "Farm", i * 2)).ToArray();
        var farming = Match(farmers, farms, Enumerable.Repeat(1, 44).ToArray(), 1);
        if (farming.Length != 44 || farming.Select(a => a.TargetId).Distinct().Count() != 44 || farming.Sum(a => a.Distance) != 0)
            throw new InvalidOperationException("44 farmers/distinct farms matching failed.");
        var cutters = Enumerable.Range(0, 13).Select(i => Unit((uint)(1000 + i), "VillagerAztec")).ToArray();
        var trees = Enumerable.Range(0, 4).Select(i => Unit((uint)(2000 + i), "TreePalmMexican")).ToArray();
        var wood = Match(cutters, trees, [4, 4, 4, 4], 10);
        if (wood.Length != 13 || wood.GroupBy(a => a.TargetId).Any(g => g.Count() > 4) || !wood.SequenceEqual(Match(cutters.Reverse().ToArray(), trees.Reverse().ToArray(), [4, 4, 4, 4], 10)))
            throw new InvalidOperationException("13 cutters/four trees deterministic capacity fixture failed.");
        var optimal = Match([Unit(1, "worker", 0), Unit(2, "worker", 1)], [Unit(10, "target", 1), Unit(20, "target", -10)], [1, 1], 20);
        if (optimal.Sum(a => a.Distance) != 10 || optimal[0].TargetId != 20) throw new InvalidOperationException("Greedy assignment substituted for global minimum.");
        var ties = Match([Unit(2, "worker"), Unit(1, "worker")], [Unit(20, "target", -1), Unit(10, "target", 1)], [1, 1], 20);
        if (ties[0].WorkerId != 1 || ties[0].TargetId != 10) throw new InvalidOperationException("Saved-ID lexicographic tie rule failed.");
        Refuse(() => Match(farmers, farms, new int[44], 1));
        Refuse(() => Match([Unit(1, "worker")], [Unit(10, "target", 100)], [1], 1));
        var baseDoc = CampaignTriggers.Parse(WorkflowFixtures.Trigger()); var t = baseDoc.Triggers[0]; var e = t.Effects[0];
        var all = farmers.Concat(farms).Concat([Unit(900, "VillagerAztec", 999), Unit(901, "Farm", 1000)]).ToArray(); var units = all.ToDictionary(u => u.UnitId);
        var selected = farmers.Select(u => u.UnitId).ToHashSet(); var audit = Inspect(t, units, selected, []);
        if (audit.Jobs.Length != 1 || audit.UnassignedWorkers.Length != 43 || !Work(e)) throw new InvalidOperationException("Reviewed work-pattern preservation failed.");
        if (Inspect(t with { Effects = [e with { Command = "unknown" }] }, units, selected, []).UnsupportedEffects.Length != 1)
            throw new InvalidOperationException("Unknown selected-worker work treated as idle.");
        if (Inspect(t with { Effects = [e, e] }, units, selected, []).DuplicateWorkers.Single() != 100) throw new InvalidOperationException("Duplicate worker jobs ignored.");
        var different = e with { Args = [e.Args[0], e.Args[1] with { Values = ["201"], Objects = [new(201, 1, "Farm")] }, e.Args[2]] };
        if (Inspect(t with { Effects = [e, different] }, units, selected, []).ConflictingWorkers.Single() != 100) throw new InvalidOperationException("Conflicting worker jobs ignored.");
        var unrelated = TriggerObjects.Retarget(baseDoc.Body, e, new Dictionary<string, CampaignTriggers.ObjectRef[]> { ["SrcObject"] = [new(900, 1, "VillagerAztec")], ["DstObject"] = [new(901, 1, "Farm")] });
        var composed = CampaignTriggers.Parse(TriggerObjects.ReplaceElements(baseDoc, t, t.Conditions.Select(c => baseDoc.Body.AsSpan(c.Start, c.End - c.Start).ToArray()).ToArray(),
            [baseDoc.Body.AsSpan(e.Start, e.End - e.Start).ToArray(), unrelated]));
        composed = CampaignTriggers.Parse(TriggerEdits.Transform(composed, Json(new { operation = "clone", triggerId = 704, expectedName = "Startup", newId = 705, newName = "Unrelated" })).Output);
        var directory = Path.Combine(Path.GetTempPath(), "aom-orders-fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var tr = Path.Combine(directory, "source.trg"); var scene = Path.Combine(directory, "synthetic.mythscn"); var output = Path.Combine(directory, "planned.trg");
            File.WriteAllBytes(tr, composed.Original); File.WriteAllBytes(scene, WorkflowFixtures.Scene(all));
            var args = new Dictionary<string, object> { ["operation"] = "plan", ["path"] = tr, ["expectedSha256"] = Layout.Hash(tr), ["scenarioPath"] = scene, ["expectedScenarioSha256"] = Layout.Hash(scene),
                ["triggerId"] = 704, ["expectedName"] = "Startup", ["workers"] = farmers.Select(Tuple).ToArray(), ["targets"] = farms.Select(Tuple).ToArray(),
                ["player"] = 1, ["targetPlayer"] = 1, ["workerProtos"] = (string[])["VillagerAztec"], ["targetProtos"] = (string[])["Farm"], ["job"] = "farm", ["maxDistance"] = 1, ["templateEffectIndex"] = 0 };
            var preview = Json(Execute(Json(args))); if (preview.GetProperty("assignments").GetArrayLength() != 43 || preview.GetProperty("preserved").GetArrayLength() != 1 || File.Exists(output))
                throw new InvalidOperationException("Startup plan lost preserved jobs or wrote preview output.");
            args["operation"] = "write"; args["outputPath"] = output; args["confirmWrite"] = true; var result = Json(Execute(Json(args)));
            var written = CampaignTriggers.Read(output); if (written.Triggers[0].Effects.Length != 45 || Layout.Hash(tr) != (string)args["expectedSha256"] || Layout.Hash(output) != result.GetProperty("sha256").GetString())
                throw new InvalidOperationException("Startup new-file generation/readback/preservation failed.");
            Refuse(() => Execute(Json(args)));
        }
        finally { Directory.Delete(directory, true); }
    }

    internal static void Preflight(JsonElement args)
    {
        Catalog.ValidateObject(args, ["operation", "path", "expectedSha256", "scenarioPath", "expectedScenarioSha256", "triggerId", "expectedName", "workers", "targets",
            "player", "targetPlayer", "workerProtos", "targetProtos", "job", "treeCapacity", "maxDistance", "templateEffectIndex", "replaceEffects", "outputPath", "confirmWrite"]);
        var op = args.GetProperty("operation").GetString(); if (op is not "audit" and not "plan" and not "write") throw new ArgumentException("Startup operation: audit/plan/write.");
        _ = TriggerObjects.ReadObjects(args.GetProperty("workers")); _ = TriggerObjects.ReadObjects(args.GetProperty("targets"));
        _ = EditorFiles.LocalPath(args.GetProperty("path").GetString()!); _ = EditorFiles.LocalPath(args.GetProperty("scenarioPath").GetString()!);
        foreach (var key in new[] { "expectedSha256", "expectedScenarioSha256" })
            if (args.GetProperty(key).GetString() is not { Length: 64 } sha || !sha.All(Uri.IsHexDigit)) throw new ArgumentException("Pinned TR/checkpoint hashes required.");
        if (args.GetProperty("job").GetString() is not "farm" and not "wood" || args.GetProperty("player").GetInt32() is < 0 or > 12 || args.GetProperty("targetPlayer").GetInt32() is < 0 or > 12)
            throw new ArgumentException("Explicit job/owners required.");
        if (op != "audit" && args.GetProperty("templateEffectIndex").GetInt32() < 0) throw new ArgumentException("Reviewed task template required for plan/write.");
        if (args.GetProperty("job").GetString() == "wood" && args.GetProperty("treeCapacity").GetInt32() is < 1 or > 200) throw new ArgumentException("Explicit bounded treeCapacity required.");
        foreach (var key in new[] { "workerProtos", "targetProtos" })
        {
            var values = args.GetProperty(key);
            if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() is < 1 or > 200 || values.EnumerateArray().Any(p => p.GetString() is not { Length: > 0 and <= 128 } name || name.Any(char.IsControl))) throw new ArgumentException("Explicit bounded prototype pools required.");
        }
        if (op == "write") { EditorFiles.Confirm(args, "confirmWrite"); _ = EditorFiles.ApprovedNewPath(args.GetProperty("outputPath").GetString()!, ".trg"); }
        else if (args.TryGetProperty("outputPath", out _) || args.TryGetProperty("confirmWrite", out _)) throw new ArgumentException("Only write can accept output/confirmation.");
        var distance = args.GetProperty("maxDistance").GetDouble(); if (!double.IsFinite(distance) || distance <= 0 || distance > 3_000_000) throw new ArgumentException("Explicit positive maxDistance required.");
    }
    internal static object Execute(JsonElement args)
    {
        Preflight(args); var document = CampaignTriggers.Read(args.GetProperty("path").GetString()!); CheckpointDocument.RequireHash(document.Sha256, args.GetProperty("expectedSha256").GetString());
        var checkpoint = CheckpointDocument.Read(args.GetProperty("scenarioPath").GetString()!, args.GetProperty("expectedScenarioSha256").GetString());
        var units = CheckpointUnits.Read(checkpoint).Units.ToDictionary(u => u.UnitId);
        var trigger = document.Triggers.SingleOrDefault(t => t.Id == args.GetProperty("triggerId").GetUInt32());
        if (trigger is null || trigger.Name != args.GetProperty("expectedName").GetString()) throw new ArgumentException("Startup trigger identity mismatch.");
        var workerRefs = TriggerObjects.ReadObjects(args.GetProperty("workers")); var targetRefs = TriggerObjects.ReadObjects(args.GetProperty("targets"));
        void Check(CampaignTriggers.ObjectRef[] refs, string ownerKey, string protoKey)
        {
            var owner = args.GetProperty(ownerKey).GetUInt32(); var protos = args.GetProperty(protoKey).EnumerateArray().Select(p => p.GetString()!).ToHashSet(StringComparer.Ordinal);
            if (protos.Count is < 1 or > 200 || refs.Any(o => o.Player != owner || !protos.Contains(o.Proto) || !Matches(o, units))) throw new ArgumentException("Selection violates saved identity/owner/prototype constraints.");
        }
        Check(workerRefs, "player", "workerProtos"); Check(targetRefs, "targetPlayer", "targetProtos");
        if (workerRefs.Select(o => o.UnitId).Intersect(targetRefs.Select(o => o.UnitId)).Any()) throw new ArgumentException("Worker/target pools overlap.");
        var selected = workerRefs.Select(o => o.UnitId).ToHashSet(); var removed = new HashSet<int>();
        if (args.TryGetProperty("replaceEffects", out var replacements))
        {
            if (replacements.GetArrayLength() is < 1 or > 200) throw new ArgumentException("replaceEffects requires 1..200 reviewed effects.");
            foreach (var r in replacements.EnumerateArray())
            {
                Catalog.ValidateObject(r, ["effectIndex", "expectedSha256"]); var index = r.GetProperty("effectIndex").GetInt32();
                if (index < 0 || index >= trigger.Effects.Length || !removed.Add(index)) throw new ArgumentException("Replacement effect index invalid/duplicate.");
                var e = trigger.Effects[index]; if (!Work(e) || e.Args[0].Objects!.Any(o => !selected.Contains(o.UnitId))) throw new ArgumentException("Only reviewed work effects exclusively involving selected workers may be replaced.");
                var expected = r.GetProperty("expectedSha256").GetString(); if (expected is null) throw new ArgumentException("Reviewed effect SHA required.");
                CheckpointDocument.RequireHash(CheckpointDocument.Hash(document.Body.AsSpan(e.Start, e.End - e.Start)), expected);
            }
        }
        var audit = Inspect(trigger, units, selected, removed); var op = args.GetProperty("operation").GetString();
        if (op == "audit") return new { operation = op, sourceSha256 = document.Sha256, checkpointSha256 = checkpoint.Sha256, audit,
            limitation = "Caller-designated startup trigger; conditions/timing and indirect XS references are not evaluated." };
        if (audit.UnsupportedEffects.Length > 0 || audit.InvalidWorkers.Length > 0 || audit.DuplicateWorkers.Length > 0) throw new InvalidDataException("Unknown/invalid/duplicate selected-worker jobs block planning. Audit and explicitly review replacement effects.");
        var capacity = args.GetProperty("job").GetString() == "farm" ? 1 : args.GetProperty("treeCapacity").GetInt32();
        if (args.GetProperty("job").GetString() == "farm" && (targetRefs.Any(t => t.Proto != "Farm" || t.Player != args.GetProperty("player").GetUInt32())))
            throw new ArgumentException("Farm requires exact Farm prototype/same owner/one worker.");
        var pending = audit.UnassignedWorkers.Select(id => units[id]).ToArray(); var targets = targetRefs.Select(t => units[t.UnitId]).ToArray();
        var capacities = targets.Select(t => capacity - audit.Jobs.Where(j => j.TargetId == t.UnitId).Select(j => j.WorkerId).Distinct().Count()).ToArray();
        if (capacities.Any(c => c < 0)) throw new InvalidDataException("Existing jobs already exceed requested target capacity.");
        var assignments = Match(pending, targets, capacities, args.GetProperty("maxDistance").GetDouble());
        var preserved = audit.Jobs.Where(j => selected.Contains(j.WorkerId)).Select(j => new Assignment(j.WorkerId, j.TargetId, Distance(units[j.WorkerId], units[j.TargetId]), true)).ToArray();
        var templateIndex = args.GetProperty("templateEffectIndex").GetInt32();
        if (templateIndex >= trigger.Effects.Length || !Work(trigger.Effects[templateIndex])) throw new InvalidDataException("Reviewed Unit: Task template required.");
        var template = trigger.Effects[templateIndex];
        var tasks = assignments.Select(a => TriggerObjects.Retarget(document.Body, template, new Dictionary<string, CampaignTriggers.ObjectRef[]>
        {
            ["SrcObject"] = [new(a.WorkerId, units[a.WorkerId].Player, units[a.WorkerId].Proto!)],
            ["DstObject"] = [new(a.TargetId, units[a.TargetId].Player, units[a.TargetId].Proto!)],
        })).ToArray();
        var effects = trigger.Effects.Where((_, i) => !removed.Contains(i)).Select(e => document.Body.AsSpan(e.Start, e.End - e.Start).ToArray()).Concat(tasks).ToArray();
        var output = TriggerObjects.ReplaceElements(document, trigger, trigger.Conditions.Select(e => document.Body.AsSpan(e.Start, e.End - e.Start).ToArray()).ToArray(), effects);
        var generated = CampaignTriggers.Parse(output).Triggers.Single(t => t.Id == trigger.Id).Effects.Skip(effects.Length - assignments.Length).ToArray();
        for (var i = 0; i < assignments.Length; i++)
            if (!Work(generated[i]) || generated[i].Args[0].Objects is not { Length: 1 } || generated[i].Args[0].Objects![0].UnitId != assignments[i].WorkerId
                || generated[i].Args[1].Objects![0].UnitId != assignments[i].TargetId || !Matches(generated[i].Args[0].Objects![0], units) || !Matches(generated[i].Args[1].Objects![0], units))
                throw new InvalidDataException("Generated startup task identity/pattern semantic readback failed.");
        string? written = null; var sha256 = CheckpointDocument.Hash(output);
        if (op == "write")
        {
            written = EditorFiles.ApprovedNewPath(args.GetProperty("outputPath").GetString()!, ".trg");
            try { using (var file = new FileStream(written, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(output); file.Flush(true); } CheckpointDocument.RequireHash(Layout.Hash(written), sha256); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { throw new WorkflowFailure("STARTUP_WRITE_OUTCOME_UNKNOWN", "write", e.Message, false, true, "Stop and inspect retained output; no automatic retry or cleanup.", written, written, e); }
        }
        return new { operation = op, path = written, sourceSha256 = document.Sha256, checkpointSha256 = checkpoint.Sha256, sha256, audit, assignments, preserved, removedEffects = removed.Order().ToArray(),
            totalDistance = assignments.Sum(a => a.Distance), costMetric = "Minimum sum of decimal-converted Euclidean world-XZ distances; equal costs use lexicographic saved worker/target IDs.",
            limitation = "Saved IDs only; revalidate live identities before guarded import. Target resource categories are caller-declared prototype constraints. No pathfinding, reachability, startup timing, compilation or runtime proof." };
    }
}
