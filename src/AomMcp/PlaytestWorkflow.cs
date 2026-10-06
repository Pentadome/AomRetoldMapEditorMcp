using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AomMcp;

/// <summary>Host-owned UI sessions. No native start/load; no unregistered UI profile can send input.</summary>
internal sealed class PlaytestWorkflow
{
    internal sealed record Pixel(int X, int Y, byte R, byte G, byte B);
    internal sealed record Step(int X, int Y, bool Editor, Pixel[] Gate);
    internal sealed record Profile(string Id, string ExeSha256, int Width, int Height, string Interface, string Language,
        string EvidenceSha256, Step[] Start, Step[] Quit, Pixel[] EditorGate, Pixel[] PlayingGate);
    internal sealed record Identity(uint Pid, uint Thread, long Window, long Base, long StartedTicks, string ExeSha256, int Width, int Height);
    internal sealed class Session(string token, string runId, Profile profile, Identity identity, string[] retainedPaths)
    {
        internal string Token { get; } = token;
        internal string RunId { get; } = runId;
        internal Profile Profile { get; } = profile;
        internal Identity Identity { get; } = identity;
        internal string[] RetainedPaths { get; } = retainedPaths;
        internal DateTimeOffset StartedAtUtc { get; } = DateTimeOffset.UtcNow;
        internal string State { get; set; } = "prepared";
    }
    // Independently observed ordinary Play/Paused-Quit/YES path; caller assertions cannot register profiles.
    internal const string ReviewedProfileHash = "df2311ab670edf906425f3864eec9d2493699ed0a74e923834d5dec291ae5c46";
    static readonly string[] ReviewedProfileHashes = [ReviewedProfileHash];
    static readonly JsonSerializerOptions ProfileJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    readonly HashSet<string> _runIds = new(StringComparer.Ordinal);
    Session? _session;
    internal static bool Available => ReviewedProfileHashes.Length != 0;
    internal static object Capabilities() => new { available = Available, profileCount = ReviewedProfileHashes.Length, nativeStartTest = false, nativeLoad = false,
        profile = "uilayouts/playtest-alt-en-2560x1440.json", expectedProfileSha256 = ReviewedProfileHash,
        reason = "One reviewed English alternative-UI 2560x1440 Player1/Standard normal Play/Paused-Quit/YES path. Exact pixels fail closed on other/loading/defeat/overlay states. No compiler/runtime/gameplay proof; existing editor guards unchanged." };
    internal static void Preflight(JsonElement args)
    {
        var operation = args.GetProperty("operation").GetString();
        Catalog.ValidateObject(args, operation switch {
            "preview" => ["operation", "profilePath", "expectedProfileSha256"],
            "start" => ["operation", "profilePath", "expectedProfileSha256", "runId", "expectedPid", "expectedWindowThread", "expectedExeSha256", "originalCheckpointPath", "expectedOriginalSha256", "disposableCheckpointPath", "expectedDisposableSha256", "triggerBackupPath", "expectedTriggerSha256", "confirmDisposableScene", "confirmPlaytest", "timeoutMs"],
            "inspect" => ["operation", "token"], "quit" => ["operation", "token", "confirmQuit", "timeoutMs"], _ => throw new ArgumentException("operation: preview/start/inspect/quit"),
        });
        if (args.TryGetProperty("timeoutMs", out var t) && t.GetInt32() is < 1000 or > 15_000) throw new ArgumentException("Transition wait 1000..15000 ms.");
        if (operation is "start" or "preview" && args.TryGetProperty("profilePath", out var p))
        { _ = EditorFiles.LocalPath(p.GetString()!); Hash(args.GetProperty("expectedProfileSha256").GetString()!); }
        if (operation == "start")
        {
            EditorFiles.Confirm(args, "confirmDisposableScene"); EditorFiles.Confirm(args, "confirmPlaytest");
            if (!RuntimeProbes.Token(args.GetProperty("runId").GetString()) || args.GetProperty("expectedPid").GetUInt32() == 0 || args.GetProperty("expectedWindowThread").GetUInt32() == 0) throw new ArgumentException("Explicit fresh run/PID/window thread required.");
            foreach (var key in new[] { "expectedExeSha256", "expectedOriginalSha256", "expectedDisposableSha256", "expectedTriggerSha256" }) Hash(args.GetProperty(key).GetString()!);
            foreach (var key in new[] { "originalCheckpointPath", "disposableCheckpointPath", "triggerBackupPath" }) _ = EditorFiles.LocalPath(args.GetProperty(key).GetString()!);
            _ = args.GetProperty("profilePath"); _ = args.GetProperty("expectedProfileSha256");
        }
        if (operation is "inspect" or "quit" && !RuntimeProbes.Token(args.GetProperty("token").GetString())) throw new ArgumentException("Host session token required.");
        if (operation == "quit") EditorFiles.Confirm(args, "confirmQuit");
    }
    static void Hash(string value) { if (value.Length != 64 || !value.All(Uri.IsHexDigit)) throw new ArgumentException("Pinned SHA-256 required."); }
    internal static void ValidateProfile(Profile profile)
    {
        Hash(profile.ExeSha256); Hash(profile.EvidenceSha256);
        if (!RuntimeProbes.Token(profile.Id) || profile.Width is < 320 or > 2560 || profile.Height is < 240 or > 1440 || string.IsNullOrWhiteSpace(profile.Interface) || string.IsNullOrWhiteSpace(profile.Language)
            || profile.Start is null || profile.Quit is null || profile.Start.Length is < 1 or > 8 || profile.Quit.Length is < 1 or > 8) throw new InvalidDataException("UI profile geometry/identity/step bounds invalid.");
        void Gate(Pixel[]? pixels)
        {
            if (pixels is null || pixels.Length is < 3 or > 32 || pixels.Select(p => (p.X, p.Y)).Distinct().Count() != pixels.Length || pixels.Any(p => p.X < 0 || p.Y < 0 || p.X >= profile.Width || p.Y >= profile.Height))
                throw new InvalidDataException("UI gates require 3..32 distinct in-client reviewed pixels.");
        }
        Gate(profile.EditorGate); Gate(profile.PlayingGate);
        foreach (var s in profile.Start.Concat(profile.Quit)) { if (s.X < 0 || s.Y < 0 || s.X >= profile.Width || s.Y >= profile.Height) throw new InvalidDataException("Click outside profile client."); Gate(s.Gate); }
        if (profile.Start.Any(s => !s.Editor) || profile.Quit.Any(s => s.Editor)) throw new InvalidDataException("Start editor-only; Quit playtest-only. No arbitrary input modes.");
    }
    static (Profile Profile, string Hash, bool Reviewed) ReadProfile(JsonElement args)
    {
        var path = EditorFiles.LocalPath(args.GetProperty("profilePath").GetString()!); var info = new FileInfo(path);
        if (!info.Exists || info.Length is < 1 or > 128_000) throw new InvalidDataException("UI profile byte bound invalid.");
        var bytes = File.ReadAllBytes(path); var hash = CheckpointDocument.Hash(bytes); CheckpointDocument.RequireHash(hash, args.GetProperty("expectedProfileSha256").GetString());
        var profile = JsonSerializer.Deserialize<Profile>(bytes, ProfileJson) ?? throw new InvalidDataException("Empty UI profile."); ValidateProfile(profile);
        return (profile, hash, ReviewedProfileHashes.Contains(hash, StringComparer.Ordinal));
    }
    internal void PreflightRequest(JsonElement args)
    {
        Preflight(args);
        var operation = args.GetProperty("operation").GetString();
        if (operation == "start" && !ReadProfile(args).Reviewed) throw Unavailable();
        if (operation is "inspect" or "quit")
        {
            var session = RequireToken(args.GetProperty("token").GetString()!);
            if (operation == "quit" && session.State != "playing") throw new ArgumentException("Unknown/inactive session cannot authorize Quit input.");
        }
    }
    static WorkflowFailure Unavailable() => new("PLAYTEST_UI_UNREVIEWED", "preflight", "Requested UI profile is not independently registered for this workflow.", false, false,
        "Do not guess coordinates or use native start/load. Obtain isolated live-research approval; use approved normal UI manually.");
    internal object Execute(JsonElement args, Func<Game> connect)
    {
        Preflight(args); var operation = args.GetProperty("operation").GetString();
        if (operation == "preview")
        {
            if (!args.TryGetProperty("profilePath", out _)) return Capabilities();
            var candidate = ReadProfile(args); return new { preview = true, candidate.Hash, candidate.Reviewed, candidate.Profile, inputSent = false, runtimeTelemetry = RuntimeTelemetry.Capabilities() };
        }
        if (operation == "start")
        {
            var candidate = ReadProfile(args); if (!candidate.Reviewed) throw Unavailable();
            if (_session is { State: not "returned" } || !_runIds.Add(args.GetProperty("runId").GetString()!)) throw new ArgumentException("Active/unknown session or reused run ID; no start retry.");
            var backups = Backups(args); using var game = connect(); var identity = Observe(game, candidate.Profile, true);
            if (identity.Pid != args.GetProperty("expectedPid").GetUInt32() || identity.Thread != args.GetProperty("expectedWindowThread").GetUInt32() || identity.ExeSha256 != args.GetProperty("expectedExeSha256").GetString()) throw new InvalidDataException("Fresh process/build/thread assertions failed.");
            VerifyScene(CheckpointUnits.Read(CheckpointDocument.Read(backups[1], args.GetProperty("expectedDisposableSha256").GetString())), LiveUnits.Read(game));
            RequirePixels(game, candidate.Profile, candidate.Profile.EditorGate);
            _session = new(Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)), args.GetProperty("runId").GetString()!, candidate.Profile, identity, backups);
            Transition(game, _session, candidate.Profile.Start, false, args); return Describe(_session);
        }
        var session = RequireToken(args.GetProperty("token").GetString()!);
        if (operation == "inspect" && session.State is not "playing" and not "returned") return Describe(session);
        if (operation == "quit" && session.State != "playing") throw new ArgumentException("Quit requires known active playing session; unknown outcomes need inspection/manual recovery, never automatic input.");
        Game current;
        try { current = connect(); } catch (Exception e) when (e is InvalidDataException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception) { session.State = "unknown"; throw Unknown(session, e); }
        using var connection = current;
        if (operation == "inspect")
        {
            try { CheckIdentity(session.Identity, Observe(current, session.Profile, session.State == "returned")); RequirePixels(current, session.Profile, session.State == "returned" ? session.Profile.EditorGate : session.Profile.PlayingGate); }
            catch (Exception e) when (e is InvalidDataException or InvalidOperationException) { session.State = "unknown"; throw Unknown(session, e); }
            return Describe(session);
        }
        Transition(current, session, session.Profile.Quit, true, args); return Describe(session);
    }
    Session RequireToken(string token) => _session is { } session && string.Equals(session.Token, token, StringComparison.Ordinal) ? session : throw new ArgumentException("Unknown/stale/cross-host playtest token; no input.");
    static object Describe(Session session) => new { session.Token, session.RunId, session.StartedAtUtc, session.State, session.Identity, retainedPaths = session.RetainedPaths,
        transitionVerified = session.State is "playing" or "returned", gameplayVerified = false, compilationVerified = false, runtimeTelemetry = RuntimeTelemetry.Capabilities(),
        limitation = "Mode + reviewed pixel gates only; not XS success, worker behavior, rebuilding or reload persistence. Quit does not load scenarios or restore viewport." };
    static string[] Backups(JsonElement args)
    {
        var paths = new[] { EditorFiles.LocalPath(args.GetProperty("originalCheckpointPath").GetString()!), EditorFiles.LocalPath(args.GetProperty("disposableCheckpointPath").GetString()!), EditorFiles.LocalPath(args.GetProperty("triggerBackupPath").GetString()!) };
        if (paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length) throw new ArgumentException("Original/disposable/checkpoint/TR paths must be distinct.");
        _ = CheckpointDocument.Read(paths[0], args.GetProperty("expectedOriginalSha256").GetString());
        var disposable = CheckpointDocument.Read(paths[1], args.GetProperty("expectedDisposableSha256").GetString());
        var info = new FileInfo(paths[2]); if (!info.Exists || info.Length > ExportFormats.MaxStoredBytes) throw new InvalidDataException("Trigger backup missing/oversized.");
        var bytes = File.ReadAllBytes(paths[2]); CheckpointDocument.RequireHash(CheckpointDocument.Hash(bytes), args.GetProperty("expectedTriggerSha256").GetString()); _ = ExportFormats.Trigger(bytes);
        foreach (var path in paths.Skip(1)) if (DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > TimeSpan.FromMinutes(2) || File.GetLastWriteTimeUtc(path) > DateTime.UtcNow.AddSeconds(5)) throw new InvalidDataException("Fresh game-written disposable checkpoint/TR backup required.");
        var embedded = disposable.Root.One("TR"); var a = CampaignTriggers.ParseScenarioSection(embedded.ToArray()); var b = CampaignTriggers.Parse(bytes);
        if (a.Triggers.Length != b.Triggers.Length || a.Groups.Length != b.Groups.Length || !a.Triggers.Zip(b.Triggers).All(pair => a.Body.AsSpan(pair.First.Start, pair.First.End - pair.First.Start).SequenceEqual(b.Body.AsSpan(pair.Second.Start, pair.Second.End - pair.Second.Start)))
            || !a.Groups.Zip(b.Groups).All(pair => a.Body.AsSpan(pair.First.Start, pair.First.End - pair.First.Start).SequenceEqual(b.Body.AsSpan(pair.Second.Start, pair.Second.End - pair.Second.Start))))
            throw new InvalidDataException("Checkpoint/export TR records/groups disagree; backup not proven current.");
        return paths;
    }
    internal static void VerifyScene(CheckpointUnits.Snapshot saved, LiveUnits.Unit[] live)
    {
        if (saved.UnresolvedPrototypes != 0 || saved.Units.Length != live.Length) throw new InvalidDataException("Disposable scene saved/live entity coverage differs.");
        var map = live.ToDictionary(u => u.UnitId);
        foreach (var u in saved.Units)
            if (u.UnitId > int.MaxValue || !map.TryGetValue((int)u.UnitId, out var v) || v.Player != u.Player || v.Proto != u.Proto
                || Math.Abs(v.Position.X - u.X) > .001 || Math.Abs(v.Position.Y - u.Y) > .001 || Math.Abs(v.Position.Z - u.Z) > .001)
                throw new InvalidDataException("Disposable saved/live entity tuple mismatch; IDs never assumed persistent. Player/TR proof additionally needs caller-confirmed game-written backups.");
    }
    static Identity Observe(Game game, Profile profile, bool editor)
    {
        if (game.Process.HasExited || game.Layout.ExeSha256 != profile.ExeSha256 || Win.GetWindowThreadProcessId(game.Window, out var pid) != game.Thread || pid != game.Pid || Win.GetForegroundWindow() != game.Window)
            throw new InvalidDataException("Playtest process/window/thread/build/foreground guard failed.");
        var size = Ui.ClientSize(game); if (size.Width != profile.Width || size.Height != profile.Height) throw new InvalidDataException("Playtest client/profile size mismatch.");
        var global = game.Pointer(game.Base + checked((int)game.Layout.EditorGlobalRva)); var flag = global == 0 ? (byte)255 : game.Read(global + checked((int)game.Layout.EditorFlagOffset), 1)[0];
        if (flag is not 0 and not 1 || (flag == 1) != editor) throw new InvalidDataException("Playtest editor-mode guard failed; menu/loading is not transition proof.");
        if (editor)
        {
            var contextEditor = game.Editor();
            if (unchecked((int)game.UInt(contextEditor + checked((int)game.Layout.ProtoOffset))) != -1) throw new InvalidDataException("Active placement cursor; clear manually before playtest. No UI input sent.");
        }
        var context = game.Pointer(game.Base + checked((int)game.Layout.ContextRva));
        if (context == 0 || game.UInt(context + checked((int)game.Layout.OwnerOffset)) != game.Thread || !game.Read(game.Base + checked((int)game.Layout.DispatcherRva), game.Layout.Prefix.Length).AsSpan().SequenceEqual(game.Layout.Prefix)) throw new InvalidDataException("Playtest owner/signature guard failed.");
        return new(game.Pid, game.Thread, (long)game.Window, (long)game.Base, game.Process.StartTime.ToUniversalTime().Ticks, game.Layout.ExeSha256, size.Width, size.Height);
    }
    internal static void CheckIdentity(Identity expected, Identity observed) { if (expected != observed) throw new InvalidDataException("Playtest PID/start-time/window/thread/module/build/size changed; no input."); }
    static void RequirePixels(Game game, Profile profile, Pixel[] gate)
    {
        var image = Ui.CapturePixels(game, profile.Width); if (image.Width != profile.Width || image.Height != profile.Height) throw new InvalidDataException("Playtest screenshot size changed.");
        foreach (var p in gate) { var i = (p.Y * image.Width + p.X) * 4; if (image.Bgra[i + 2] != p.R || image.Bgra[i + 1] != p.G || image.Bgra[i] != p.B) throw new InvalidDataException("Playtest pixel gate mismatch; no guessed UI input."); }
    }
    static void Click(Game game, Session session, Step step)
    {
        void Gate() { CheckIdentity(session.Identity, Observe(game, session.Profile, step.Editor)); RequirePixels(game, session.Profile, step.Gate); CheckIdentity(session.Identity, Observe(game, session.Profile, step.Editor)); }
        Gate(); var point = new Win.Point { X = step.X, Y = step.Y }; Win.Check(Win.ClientToScreen(game.Window, ref point), "Playtest ClientToScreen"); Win.Check(Win.SetCursorPos(point.X, point.Y), "Playtest cursor"); Thread.Sleep(80); Gate();
        // Narrow private input path only reached by registered profile + this host's known transition token.
        var down = new Win.Input { Type = 0, Mouse = new Win.Mouse { Flags = 2 } }; var up = new Win.Input { Type = 0, Mouse = new Win.Mouse { Flags = 4 } };
        try { Win.Check(Win.SendInput(1, [down], 40) == 1, "Playtest LEFTDOWN"); Thread.Sleep(60); }
        finally { Win.Check(Win.SendInput(1, [up], 40) == 1, "Playtest LEFTUP"); }
    }
    static WorkflowFailure Unknown(Session session, Exception e) => new("PLAYTEST_OUTCOME_UNKNOWN", "ui-transition", e.Message, false, true,
        "Stop. Inspect screenshot and retained backups. No retries, rollback, native start/load, or automatic Quit. Use separately approved manual recovery.", inner: e, retainedPaths: session.RetainedPaths);
    internal static void Advance(Session session, string next)
    {
        if ((session.State, next) is not (("prepared", "starting") or ("starting", "playing") or ("playing", "quitting") or ("quitting", "returned")))
            throw new InvalidOperationException("Invalid/repeated playtest transition; unknown sessions cannot resume input.");
        session.State = next;
    }
    internal static void WaitForGate(Action gate, int timeout, Func<long> elapsed, Action pause)
    {
        while (true)
        {
            try { gate(); return; }
            catch (InvalidDataException) when (elapsed() < timeout) { pause(); } // Read-only polling; no input replay.
        }
    }
    static void Transition(Game game, Session session, Step[] steps, bool returned, JsonElement args)
    {
        Advance(session, returned ? "quitting" : "starting");
        try
        {
            var timeout = args.TryGetProperty("timeoutMs", out var t) ? t.GetInt32() : 5000; var clock = Stopwatch.StartNew();
            foreach (var step in steps)
            {
                // UI updates are queued. Wait for the NEXT reviewed gate, never repeat the previous click.
                WaitForGate(() => { CheckIdentity(session.Identity, Observe(game, session.Profile, step.Editor)); RequirePixels(game, session.Profile, step.Gate); }, timeout, () => clock.ElapsedMilliseconds, () => Thread.Sleep(100));
                Click(game, session, step); Thread.Sleep(100);
            }
            WaitForGate(() => { CheckIdentity(session.Identity, Observe(game, session.Profile, returned)); RequirePixels(game, session.Profile, returned ? session.Profile.EditorGate : session.Profile.PlayingGate); CheckIdentity(session.Identity, Observe(game, session.Profile, returned)); }, timeout, () => clock.ElapsedMilliseconds, () => Thread.Sleep(100));
            Advance(session, returned ? "returned" : "playing");
        }
        catch (Exception e) when (e is InvalidDataException or InvalidOperationException or System.ComponentModel.Win32Exception or IOException) { session.State = "unknown"; throw Unknown(session, e); }
    }
    internal static void SelfTest()
    {
        var workflow = new PlaytestWorkflow(); var called = false;
        try { _ = workflow.Execute(JsonSerializer.SerializeToElement(new { operation = "quit", token = "foreign", confirmQuit = true }), () => { called = true; throw new InvalidOperationException(); }); throw new InvalidOperationException("Cross-host token accepted."); } catch (ArgumentException) { }
        if (called) throw new InvalidOperationException("Token refusal connected to game.");
        var identity = new Identity(1, 2, 3, 4, 5, new('a', 64), 2560, 1440); CheckIdentity(identity, identity);
        foreach (var wrong in new[] { identity with { Pid = 2 }, identity with { Thread = 3 }, identity with { StartedTicks = 6 }, identity with { Width = 1280 }, identity with { ExeSha256 = new('b', 64) } })
        { try { CheckIdentity(identity, wrong); throw new InvalidOperationException("Changed playtest identity accepted."); } catch (InvalidDataException) { } }
        if (!Available || ReviewedProfileHashes.Length != 1) throw new InvalidOperationException("Reviewed UI profile registration changed.");
        var attempts = 0; var pauses = 0; long elapsed = 0;
        WaitForGate(() => { if (++attempts < 3) throw new InvalidDataException("queued"); }, 1000, () => elapsed, () => { pauses++; elapsed += 100; });
        if (attempts != 3 || pauses != 2) throw new InvalidOperationException("Queued UI gate did not use read-only polling.");
        try { WaitForGate(() => throw new InvalidDataException("unexpected"), 100, () => elapsed, () => throw new InvalidOperationException("Expired gate polled again")); throw new InvalidOperationException("Expired gate accepted."); } catch (InvalidDataException) { }
        var reviewedPath = Path.Combine(AppContext.BaseDirectory, "uilayouts", "playtest-alt-en-2560x1440.json");
        var reviewed = ReadProfile(JsonSerializer.SerializeToElement(new { profilePath = reviewedPath, expectedProfileSha256 = ReviewedProfileHash }));
        if (!reviewed.Reviewed || reviewed.Profile.Start.Length != 2 || reviewed.Profile.Quit.Length != 3) throw new InvalidOperationException("Reviewed packaged profile missing/stale.");
        var evidencePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "playtest-ui-evidence.json");
        CheckpointDocument.RequireHash(Layout.Hash(evidencePath), reviewed.Profile.EvidenceSha256);
        Pixel[] pixels = [new(1, 1, 1, 2, 3), new(2, 2, 1, 2, 3), new(3, 3, 1, 2, 3)];
        var profile = new Profile("fixture", new('a', 64), 2560, 1440, "fixture-only", "en", new('b', 64), [new(5, 5, true, pixels)], [new(5, 5, false, pixels)], pixels, pixels); ValidateProfile(profile);
        var session = new Session("owned", "fixture", profile, identity, []); workflow._session = session;
        foreach (var state in new[] { "starting", "playing", "quitting", "returned" }) Advance(session, state);
        try { Advance(session, "quitting"); throw new InvalidOperationException("Repeated Quit accepted."); } catch (InvalidOperationException e) when (e.Message.StartsWith("Invalid/repeated", StringComparison.Ordinal)) { }
        session.State = "unknown";
        _ = workflow.Execute(JsonSerializer.SerializeToElement(new { operation = "inspect", token = "owned" }), () => { called = true; throw new InvalidOperationException(); });
        try { _ = workflow.Execute(JsonSerializer.SerializeToElement(new { operation = "quit", token = "owned", confirmQuit = true }), () => { called = true; throw new InvalidOperationException(); }); throw new InvalidOperationException("Unknown-outcome Quit accepted."); } catch (ArgumentException) { }
        if (called) throw new InvalidOperationException("Unknown-outcome inspection/Quit connected.");
        var savedUnit = new CheckpointUnits.Unit(701, 6, "Farm", ["Farm"], 10, 0, 20, null, new('a', 64), new('b', 64));
        var liveUnit = new LiveUnits.Unit(701, 1, "Farm", 6, new(10, 0, 20), 100, 100); VerifyScene(new([savedUnit], 0), [liveUnit]);
        try { VerifyScene(new([savedUnit], 0), [liveUnit with { UnitId = 702 }]); throw new InvalidOperationException("Stale saved identity accepted as live scene."); } catch (InvalidDataException) { }
        var directory = Path.Combine(Path.GetTempPath(), "aom-playtest-fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "profile.json"); File.WriteAllText(path, JsonSerializer.Serialize(profile, ProfileJson));
            var args = JsonSerializer.SerializeToElement(new { operation = "start", profilePath = path, expectedProfileSha256 = Layout.Hash(path), runId = "new_run", expectedPid = 1, expectedWindowThread = 2, expectedExeSha256 = new string('a', 64),
                originalCheckpointPath = Path.Combine(directory, "original.mythscn"), expectedOriginalSha256 = new string('a', 64), disposableCheckpointPath = Path.Combine(directory, "disposable.mythscn"), expectedDisposableSha256 = new string('a', 64),
                triggerBackupPath = Path.Combine(directory, "backup.trg"), expectedTriggerSha256 = new string('a', 64), confirmDisposableScene = true, confirmPlaytest = true });
            try { _ = new PlaytestWorkflow().Execute(args, () => { called = true; throw new InvalidOperationException(); }); throw new InvalidOperationException("Candidate profile enabled."); } catch (WorkflowFailure e) when (e.Code == "PLAYTEST_UI_UNREVIEWED") { }
            if (called) throw new InvalidOperationException("Unregistered profile opened game.");
        }
        finally { Directory.Delete(directory, true); }
    }
}
