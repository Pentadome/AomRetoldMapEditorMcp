using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace AomMcp;

/// <summary>Guards approved local files and verifies game-writer outputs independently of acknowledgements.</summary>
internal static class EditorFiles
{
    // Host writer-completion budget/size ceilings; not engine limits. Poll only reads, never native retries.
    const int WriteWaitMs = 5000, PollMs = 25, MaxSnapshotBytes = 64_000_000, MaxDecodedBytes = 256_000_000;
    // Recovered TR header: two-byte magic + DWORD payload length + DWORD version. l33t: magic + DWORD decoded length.
    const int TrHeaderSize = 10, ScenarioHeaderSize = 8, TrSizeOffset = 2, ScenarioSizeOffset = 4;
    // Live game-writer outputs and tracked BaseDefense.mythscn both carry four opaque bytes after zlib EOF.
    // Preserve, do not interpret as Adler-32 or claim their game-specific integrity semantics are verified.
    const int ScenarioTrailerSize = 4;
    // Windows filename specification: reserved DOS device aliases, even when followed by an extension.
    static readonly string[] DeviceNames = ["CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$"];

    internal static string LocalPath(string path)
    {
        // Windows drive-root spelling is letter, colon, backslash (indices zero/one/two; minimum three chars).
        if (path.StartsWith("//", StringComparison.Ordinal) || path.StartsWith(@"\\", StringComparison.Ordinal))
            throw new ArgumentException("Explicit drive-qualified local path required; no device/UNC/alternate streams.");
        path = path.Replace('/', '\\');
        if (!Path.IsPathFullyQualified(path) || path.Length < 3 || path[1] != ':'
            || path[2] != '\\' || path[2..].Contains(':'))
            throw new ArgumentException("Explicit drive-qualified local path required; no device/UNC/alternate streams.");
        // Validate raw spelling before Windows/GetFullPath normalizes trailing dots/spaces in directory components.
        foreach (var component in path[3..].Split('\\')) // Skip Windows drive root, inspect every filename/directory component.
        {
            var stem = component.Split('.')[0].TrimEnd(' ');
            // Windows also reserves COM/LPT1..9 and superscript 1/2/3 aliases.
            var numberedDevice = stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && "123456789¹²³".Contains(stem[^1]);
            if (component.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || component.EndsWith(' ') || component.EndsWith('.')
                || DeviceNames.Contains(stem, StringComparer.OrdinalIgnoreCase) || numberedDevice)
                throw new ArgumentException("Invalid/reserved local filename.");
        }
        path = Path.GetFullPath(path);
        // Native writers follow filesystem links. Refuse redirected ancestors, including junctions.
        for (DirectoryInfo? d = new(Path.GetDirectoryName(path)!); d != null; d = d.Parent)
            if (d.Exists && (d.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("Redirected output/profile directory refused.");
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new ArgumentException("Redirected input/output file refused.");
        return path;
    }

    internal static string ApprovedNewPath(string path, string extension)
    {
        path = LocalPath(path);
        if (!path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
            || !Directory.Exists(Path.GetDirectoryName(path)) || File.Exists(path) || Directory.Exists(path))
            throw new ArgumentException("Output must be new file with requested extension in existing directory; never silently overwrite.");
        return path;
    }

    // Native commands resolve basenames under the active account's trigger/scenario directory.
    // A unique installed profile is a convenience, not proof of active account. Explicit directory must match game.
    internal static string ProfileDirectory(JsonElement args, string kind)
    {
        if (args.TryGetProperty("profileDirectory", out var supplied))
        {
            var directory = LocalPath(supplied.GetString()!.TrimEnd('\\') + "\\profile-path-check");
            directory = Path.GetDirectoryName(directory)!;
            if (!Directory.Exists(directory) || !string.Equals(Path.GetFileName(directory), kind, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("profileDirectory must be existing active game's " + kind + " directory.");
            return directory;
        }
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Games", "Age of Mythology Retold");
        var candidates = Directory.Exists(root) ? Directory.GetDirectories(root).Select(d => Path.Combine(d, kind)).Where(Directory.Exists).ToArray() : [];
        if (candidates.Length != 1)
            throw new InvalidDataException("Cannot identify one active " + kind + " directory; supply profileDirectory explicitly.");
        _ = LocalPath(Path.Combine(candidates[0], "profile-path-check"));
        return candidates[0];
    }

    static string Scratch(string directory, string purpose, string extension) => Path.Combine(directory,
        "AomMcp-" + purpose + "-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + extension);

    // Verified uiLoadTriggers native formatter at accepted-build RVA4347c48 is "%hs.trg";
    // uiSaveTriggers uses "%ls.trg". Live exports/checkpoints with suffixes produced .trg.trg
    // and .mythscn.mythscn respectively. These profile commands require bare filename stems.
    internal static string NativeProfileStem(string path) => Path.GetFileNameWithoutExtension(path);

    internal static void Confirm(JsonElement args, string key)
    {
        if (!args.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.True)
            throw new ArgumentException(key + "=true required.");
    }

    static readonly string[] ApplyHashes = ["expectedSha256", "expectedLiveSha256"];

    internal static void PreflightTriggers(JsonElement args)
    {
        var operation = args.GetProperty("operation").GetString();
        var path = LocalPath(args.GetProperty("path").GetString()!);
        // Conditional field sets prevent ignored edit parameters from masquerading as applied edits.
        string[] allowed = operation switch
        {
            "inspect" or "validate" => ["operation", "path"],
            "patch" => ["operation", "path", "outputPath", "name", "active", "loop", "code", "confirmWrite"],
            "export" => ["operation", "path", "profileDirectory", "confirmWrite"],
            "apply" => ["operation", "path", "profileDirectory", "expectedSha256", "expectedLiveSha256", "expectedLivePath", "confirmDestructive"],
            _ => throw new ArgumentException("Unknown trigger operation."),
        };
        Catalog.ValidateObject(args, allowed);
        if (operation == "export")
        {
            Confirm(args, "confirmWrite");
            ApprovedNewPath(path, ".trg");
            _ = ProfileDirectory(args, "trigger"); // Native output uses fresh GUID staging, never caller-named basename.
        }
        else
        {
            if (operation == "apply") _ = CampaignTriggers.Read(path);
            else TriggerCodec.Parse(TriggerCodec.ReadFile(path));
            if (operation == "patch")
            {
                Confirm(args, "confirmWrite");
                if (!args.TryGetProperty("outputPath", out var output)) throw new ArgumentException("Patch requires outputPath.");
                ApprovedNewPath(output.GetString()!, ".trg");
                if (!args.TryGetProperty("name", out _) && !args.TryGetProperty("active", out _)
                    && !args.TryGetProperty("loop", out _) && !args.TryGetProperty("code", out _))
                    throw new ArgumentException("Patch requires at least one edited field.");
            }
            if (operation == "apply")
            {
                Confirm(args, "confirmDestructive");
                _ = ProfileDirectory(args, "trigger");
                foreach (var key in ApplyHashes)
                    if (!args.TryGetProperty(key, out var hash) || hash.GetString() is not { Length: 64 } sha || !sha.All(Uri.IsHexDigit))
                        throw new ArgumentException(key + " must be 64-digit SHA-256 from inspected files.");
                var source = LocalPath(args.GetProperty("expectedLivePath").GetString()!);
                if (source.Equals(path, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("expectedLivePath must be separate game-exported original.");
                _ = CampaignTriggers.Read(source);
                if (!Layout.Hash(path).Equals(args.GetProperty("expectedSha256").GetString(), StringComparison.OrdinalIgnoreCase)
                    || !Layout.Hash(source).Equals(args.GetProperty("expectedLiveSha256").GetString(), StringComparison.OrdinalIgnoreCase))
                    throw new WorkflowFailure("SOURCE_CHANGED", "preflight", "Prepared/original TR SHA-256 differs from expected value.", false, false,
                        "Inspect both exports again before attempting apply; no native command dispatched.");
            }
        }
    }

    internal static void PreflightCheckpoint(JsonElement args)
    {
        Confirm(args, "confirmWrite");
        ApprovedNewPath(args.GetProperty("path").GetString()!, ".mythscn");
        _ = ProfileDirectory(args, "scenario");
    }

    internal static void PreflightRecovery(JsonElement args)
    {
        Catalog.ValidateObject(args, ["operation", "stagedPath", "outputPath", "expectedSha256", "profileDirectory", "confirmWrite"]);
        var operation = args.GetProperty("operation").GetString();
        if (operation is not "inspect" and not "recover") throw new ArgumentException("operation: inspect/recover.");
        var staged = LocalPath(args.GetProperty("stagedPath").GetString()!);
        var file = Path.GetFileName(staged);
        var kind = file.StartsWith("AomMcp-checkpoint-", StringComparison.Ordinal) && file.EndsWith(".mythscn", StringComparison.OrdinalIgnoreCase)
            ? "scenario" : file.StartsWith("AomMcp-trigger-export-", StringComparison.Ordinal) && file.EndsWith(".trg", StringComparison.OrdinalIgnoreCase)
                ? "trigger" : throw new ArgumentException("Only named game-writer checkpoint/trigger-export staging files can be recovered.");
        if (!string.Equals(Path.GetDirectoryName(staged), ProfileDirectory(args, kind), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Staging must be directly inside active profile " + kind + " directory.");
        if (operation == "inspect")
        {
            Catalog.ValidateObject(args, ["operation", "stagedPath", "profileDirectory"]);
            return;
        }
        Confirm(args, "confirmWrite");
        if (!args.TryGetProperty("outputPath", out var output) || !args.TryGetProperty("expectedSha256", out var expected))
            throw new ArgumentException("Recover requires outputPath and expectedSha256 from inspect.");
        ApprovedNewPath(output.GetString()!, kind == "scenario" ? ".mythscn" : ".trg");
        if (expected.GetString() is not { Length: 64 } sha || !sha.All(Uri.IsHexDigit))
            throw new ArgumentException("expectedSha256 must be 64 hex digits.");
    }

    internal static object Recover(JsonElement args)
    {
        PreflightRecovery(args);
        var staged = LocalPath(args.GetProperty("stagedPath").GetString()!);
        var scenario = staged.EndsWith(".mythscn", StringComparison.OrdinalIgnoreCase);
        byte[] bytes;
        try
        {
            var info = new FileInfo(staged);
            if (info.Length <= 0 || info.Length > MaxSnapshotBytes)
                throw new IOException("Staging file empty or exceeds host bound.");
            bytes = File.ReadAllBytes(staged);
            Thread.Sleep(PollMs);
            if (!bytes.AsSpan().SequenceEqual(File.ReadAllBytes(staged)))
                throw new IOException("Staging file still changing; no recovery attempted.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new WorkflowFailure("STAGING_UNAVAILABLE", "inspect", e.Message, false, true,
                "Wait for writer, then inspect same staging file. Do not dispatch native export again.", staged, inner: e);
        }
        var check = scenario ? ExportFormats.Scenario(bytes) : ExportFormats.Trigger(bytes);
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (args.GetProperty("operation").GetString() == "inspect")
            return new { stagedPath = staged, sha256, sizeBytes = bytes.Length, fileVerified = check.Valid,
                check.Format, check.Reason, check.SuffixBytes, nativeDispatched = false,
                nextAction = check.Valid ? "Recover to a NEW path with expectedSha256 and confirmWrite=true; no native retry."
                    : "Staging not structurally verified; do not recover/import or repeat native operation blindly." };
        if (!check.Valid) throw new WorkflowFailure("STAGING_INVALID", "verify", check.Reason, false, true,
            "Inspect staging and game state; no file was copied and no native command dispatched.", staged);
        if (!string.Equals(sha256, args.GetProperty("expectedSha256").GetString(), StringComparison.OrdinalIgnoreCase))
            throw new WorkflowFailure("STAGING_CHANGED", "verify", "Staging SHA-256 differs from inspected value.", false, true,
                "Inspect staging again; no file was copied.", staged);
        var output = ApprovedNewPath(args.GetProperty("outputPath").GetString()!, scenario ? ".mythscn" : ".trg");
        var copied = CopyNew(bytes, output);
        return new { path = output, stagedPath = staged, sha256 = copied, fileVerified = true,
            nativeDispatched = false, nextAction = "Use recovered file as an export snapshot; semantic scenario/editor state not proven." };
    }

    static bool Complete(byte[] bytes, bool scenario) =>
        scenario ? ExportFormats.Scenario(bytes).Valid : ExportFormats.Trigger(bytes).Valid;

    static byte[] Export(string path, bool scenario, Action<string, JsonElement> native, Action guard)
    {
        ApprovedNewPath(path, scenario ? ".mythscn" : ".trg");
        // Reserve a unique file. Game writer may refuse an existing reservation; then verification times out,
        // never retries or touches user-named originals. Explicit saves/imports are not validation probes.
        using (new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        try
        {
            native(scenario ? "saveScenario" : "uiSaveTriggers", scenario
                ? JsonSerializer.SerializeToElement(new { fname = NativeProfileStem(path), confirmDestructive = true })
                : JsonSerializer.SerializeToElement(new { filename = NativeProfileStem(path), confirmDestructive = true }));
        }
        catch (Exception e)
        {
            // Game.Focus throws before any input/native dispatch; empty host reservation is safe to remove.
            if (e is FocusRefusedException && new FileInfo(path).Length == 0)
            {
                File.Delete(path);
                throw new WorkflowFailure("FOCUS_NOT_GRANTED", "focus", e.Message, false, false,
                    "Bring editor window foreground manually, inspect state, then call again; nothing was dispatched.", inner: e);
            }
            throw new WorkflowFailure("EXPORT_DISPATCH_UNCERTAIN", "dispatch", e.Message, true, true,
                "Inspect staging and live editor state; do not retry native export.", path, inner: e);
        }
        var timer = Stopwatch.StartNew();
        byte[]? previous = null;
        var lastReason = "Game-writer staging file still empty.";
        while (timer.ElapsedMilliseconds < WriteWaitMs)
        {
            try { guard(); }
            catch (Exception e)
            {
                throw new WorkflowFailure("EXPORT_GUARD_CHANGED", "verify", e.Message, true, true,
                    "Inspect staging and game state; no native retry.", path, inner: e);
            }
            try
            {
                var length = new FileInfo(path).Length;
                if (length > 0 && length <= MaxSnapshotBytes)
                {
                    using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (file.Length > MaxSnapshotBytes) throw new IOException("Game output exceeds host file bound.");
                    var bytes = new byte[checked((int)file.Length)];
                    file.ReadExactly(bytes);
                    var check = scenario ? ExportFormats.Scenario(bytes) : ExportFormats.Trigger(bytes);
                    lastReason = check.Reason;
                    if (previous is not null && bytes.AsSpan().SequenceEqual(previous) && check.Valid) return bytes;
                    previous = bytes;
                }
            }
            catch (IOException) { /* Retry reads only; writer may hold file or compression may be incomplete. */ }
            Thread.Sleep(PollMs);
        }
        throw new WorkflowFailure("EXPORT_NOT_VERIFIED", "verify", "Game export not verified: " + lastReason,
            true, true, "Inspect staging with editor_export_recovery; do not retry native export.", path);
    }

    static string CopyNew(byte[] bytes, string output)
    {
        using (var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None)) file.Write(bytes);
        var expected = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (Layout.Hash(output) != expected) throw new IOException("Game output copy verification failed.");
        return expected;
    }

    /// <summary>Exports or explicitly replaces triggers with original backup and verified serialized round-trip.</summary>
    /// <param name="game">Guarded editor connection.</param>
    /// <param name="args">Preflighted operation/path/confirmation/profile arguments.</param>
    /// <param name="native">Existing typed guarded dispatcher path.</param>
    /// <returns>Verified paths/equality, never proof of XS compilation or effect success.</returns>
    public static object Triggers(Game game, JsonElement args, Action<string, JsonElement> native)
    {
        PreflightTriggers(args);
        var path = LocalPath(args.GetProperty("path").GetString()!);
        void Guard() => _ = game.Editor();
        if (args.GetProperty("operation").GetString() == "export")
        {
            var staging = Scratch(ProfileDirectory(args, "trigger"), "trigger-export", ".trg");
            try
            {
                var bytes = Export(staging, false, native, Guard);
                var sha256 = CopyNew(bytes, path);
                return new { path, stagedPath = staging, sha256, fileVerified = true,
                    limitation = "Game-written snapshot copied to approved new destination; no scene trigger changes requested. Profile must match active account; staging retained. Native exporter never receives caller-named basename." };
            }
            catch (WorkflowFailure e)
            {
                throw new WorkflowFailure(e.Code, e.Phase, e.Message, e.NativeDispatched, e.OutcomeUnknown,
                    e.NextAction, staging, path, e);
            }
            catch (Exception e)
            {
                throw new WorkflowFailure("EXPORT_COPY_UNCERTAIN", "copy", e.Message, true, true,
                    "Inspect staging/destination before any other operation; no native retry.", staging, path, e);
            }
        }
        var directory = ProfileDirectory(args, "trigger");
        var desired = TriggerCodec.ReadFile(path);
        _ = CampaignTriggers.Parse(desired);
        var expectedLive = TriggerCodec.ReadFile(args.GetProperty("expectedLivePath").GetString()!);
        if (!Convert.ToHexStringLower(SHA256.HashData(desired)).Equals(args.GetProperty("expectedSha256").GetString(), StringComparison.OrdinalIgnoreCase)
            || !Convert.ToHexStringLower(SHA256.HashData(expectedLive)).Equals(args.GetProperty("expectedLiveSha256").GetString(), StringComparison.OrdinalIgnoreCase))
            throw new WorkflowFailure("SOURCE_CHANGED", "preflight", "Prepared TR changed since preview.", false, false,
                "Inspect/preview TR again. No game command dispatched.", destinationPath: path);
        var backup = Scratch(directory, "before-trigger-import", ".trg");
        var staged = Scratch(directory, "trigger-import", ".trg");
        var roundtrip = Scratch(directory, "trigger-roundtrip", ".trg");
        var importDispatched = false;
        try
        {
            var original = Export(backup, false, native, Guard);
            if (!CampaignTriggers.EquivalentGameExport(expectedLive, original, out var liveCheck))
                throw new WorkflowFailure("LIVE_SOURCE_CHANGED", "backup", "Game-written original differs from reviewed live export: " + liveCheck,
                    true, false, "Backup retained; inspect live editor/exports. No import dispatched.", backup, path);
            using (var file = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None)) file.Write(desired);
            importDispatched = true;
            native("uiLoadTriggers", JsonSerializer.SerializeToElement(new { filename = NativeProfileStem(staged), confirmDestructive = true }));
            var observed = Export(roundtrip, false, native, Guard);
            if (!CampaignTriggers.EquivalentGameExport(desired, observed, out var semanticCheck))
                throw new WorkflowFailure("ROUNDTRIP_MISMATCH", "verify", semanticCheck, true, true,
                    "Review staged, backup and observed files. Do not retry/rollback automatically.", backup, roundtrip);
            return new { inputPath = path, backupPath = backup, stagedPath = staged, roundTripPath = roundtrip,
                roundTripVerified = true, semanticCheck, sha256 = Layout.Hash(roundtrip),
                triggerCount = CampaignTriggers.Parse(observed).Triggers.Length,
                limitation = "Whole trigger set replaced. Backup retained; XS compile/effects and scenario persistence unverified. No automatic rollback/retry." };
        }
        catch (WorkflowFailure) { throw; }
        catch (Exception e)
        {
            throw new WorkflowFailure(importDispatched ? "IMPORT_OUTCOME_UNKNOWN" : "BACKUP_FAILED",
                importDispatched ? "import/verify" : "backup", e.Message, true, importDispatched,
                importDispatched ? "Inspect game/staging and backup; no automatic rollback or retry."
                    : "Original import not dispatched. Inspect backup/staging before continuing; no retry.", backup, roundtrip, e);
        }
    }

    /// <summary>Saves through game writer to a unique profile staging file, then copies verified bytes to approved new destination.</summary>
    /// <param name="game">Guarded editor connection.</param>
    /// <param name="args">Approved new path, active scenario directory and explicit write confirmation.</param>
    /// <param name="native">Typed guarded native dispatcher.</param>
    /// <returns>Destination, retained game-written staging file, hash and structural verification limits.</returns>
    public static object Checkpoint(Game game, JsonElement args, Action<string, JsonElement> native)
    {
        PreflightCheckpoint(args);
        var output = ApprovedNewPath(args.GetProperty("path").GetString()!, ".mythscn");
        var staged = Scratch(ProfileDirectory(args, "scenario"), "checkpoint", ".mythscn");
        try
        {
            var bytes = Export(staged, true, native, () => _ = game.Editor());
            var sha256 = CopyNew(bytes, output);
            return new { path = output, stagedPath = staged, sha256, fileVerified = true, overwrite = false,
                limitation = "Game-written l33t/zlib snapshot with decoded length and zlib Adler-32 verified, not semantic reload validation. Four-byte game trailer preserved but not interpreted/validated. Staging file retained. Writer may change editor save-name/dirty state. No existing destination ever overwritten." };
        }
        catch (WorkflowFailure e)
        {
            throw new WorkflowFailure(e.Code, e.Phase, e.Message, e.NativeDispatched, e.OutcomeUnknown,
                e.NextAction, staged, output, e);
        }
        catch (Exception e)
        {
            throw new WorkflowFailure("CHECKPOINT_COPY_UNCERTAIN", "copy", e.Message, true, true,
                "Inspect staging/destination before any other operation; no native retry.", staged, output, e);
        }
    }

    /// <summary>Checks local path/refusal rules and bounded snapshot verification without contacting game.</summary>
    public static void SelfTest()
    {
        // Synthetic l33t fixture follows observed four-byte magic, DWORD decoded length, zlib stream and opaque trailer.
        using var memory = new MemoryStream();
        memory.Write("l33t"u8); memory.Write(BitConverter.GetBytes(3)); // Three synthetic payload bytes, not engine metadata.
        using (var compressed = new ZLibStream(memory, CompressionMode.Compress, true)) compressed.Write("abc"u8);
        memory.Write("tail"u8); // Four arbitrary synthetic trailer bytes, no guessed checksum algorithm.
        if (!Complete(memory.ToArray(), true)) throw new InvalidOperationException("Checkpoint codec fixture failed.");
        for (var missing = 1; missing <= ScenarioTrailerSize + sizeof(uint); missing++)
        {
            // Removing exactly the four optional trailer bytes leaves a valid no-trailer variant.
            if (missing != ScenarioTrailerSize && Complete(memory.ToArray()[..^missing], true))
                throw new InvalidOperationException("Truncated checkpoint fixture did not refuse.");
        }
        var temporary = Path.Combine(Path.GetTempPath(), "aom-writer-test-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(temporary);
        try
        {
            var target = Path.Combine(temporary, "fixture.mythscn");
            var calls = 0;
            var result = Export(target, true, (command, args) =>
            {
                calls++;
                Confirm(args, "confirmDestructive"); // Helper already has caller approval for its own fresh staging file.
                var stem = args.GetProperty("fname").GetString()!;
                if (command != "saveScenario" || stem != "fixture")
                    throw new InvalidOperationException("Writer routing fixture failed.");
                // Simulate observed native append; passing fixture.mythscn creates fixture.mythscn.mythscn.
                File.WriteAllBytes(Path.Combine(temporary, stem + ".mythscn"), memory.ToArray());
            }, () => { });
            if (calls != 1 || !result.AsSpan().SequenceEqual(memory.ToArray()) || File.Exists(target + ".mythscn"))
                throw new InvalidOperationException("Checkpoint append-extension regression failed.");
            try { ApprovedNewPath(target, ".mythscn"); } catch (ArgumentException) { calls = 0; }
            if (calls != 0) throw new InvalidOperationException("Existing checkpoint fixture did not refuse.");
            var trigger = Path.Combine(temporary, "fixture.trg");
            var desired = TriggerCodec.Serialize(new("fixture", false, false, ""));
            calls = 0;
            var triggerResult = Export(trigger, false, (command, args) =>
            {
                calls++;
                Confirm(args, "confirmDestructive"); // Native raw export can overwrite files; helper must retain its guard.
                var stem = args.GetProperty("filename").GetString()!;
                if (command != "uiSaveTriggers" || stem != "fixture" || NativeProfileStem(trigger) != stem)
                    throw new InvalidOperationException("Native trigger stem fixture failed.");
                // Simulate observed native appending behavior. Passing fixture.trg would create fixture.trg.trg.
                File.WriteAllBytes(Path.Combine(temporary, stem + ".trg"), desired);
            }, () => { });
            if (calls != 1 || !triggerResult.AsSpan().SequenceEqual(desired) || File.Exists(trigger + ".trg"))
                throw new InvalidOperationException("Trigger append-extension regression failed.");
            calls = 0;
            try
            {
                Export(Path.Combine(temporary, "unknown.trg"), false, (_, _) =>
                {
                    calls++;
                    throw new InvalidOperationException("Simulated dispatch uncertainty.");
                }, () => { });
                throw new InvalidOperationException("Uncertain dispatch fixture did not refuse.");
            }
            catch (WorkflowFailure e) when (e.Code == "EXPORT_DISPATCH_UNCERTAIN")
            {
                if (calls != 1 || e.Phase != "dispatch" || !e.NativeDispatched || !e.OutcomeUnknown
                    || e.StagingPath is null || !e.NextAction.Contains("do not retry", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Unknown dispatch lacks recovery evidence.");
            }
            var focusPath = Path.Combine(temporary, "focus.trg");
            try
            {
                Export(focusPath, false, (_, _) => throw new FocusRefusedException(), () => { });
                throw new InvalidOperationException("Focus fixture did not refuse.");
            }
            catch (WorkflowFailure e) when (e.Code == "FOCUS_NOT_GRANTED")
            {
                if (e.NativeDispatched || e.OutcomeUnknown || File.Exists(focusPath))
                    throw new InvalidOperationException("Focus refusal misreported dispatch or kept reservation.");
            }
        }
        finally { Directory.Delete(temporary, true); }
        var corrupt = memory.ToArray(); corrupt[ScenarioSizeOffset] = 4; // Deliberately wrong synthetic length.
        if (Complete(corrupt, true)) throw new InvalidOperationException("Checkpoint length fixture did not refuse.");
        if (LocalPath("C:/safe-name.trg") != @"C:\safe-name.trg")
            throw new InvalidOperationException("Forward-slash local path fixture failed.");
        foreach (var path in new[] { @"relative.trg", @"\\server\file.trg", "//server/file.trg", @"C:\file.trg:stream", @"C:\CON.trg", @"C:\COM1.trg", @"C:\folder.\name.trg" })
        {
            try { LocalPath(path); } catch (ArgumentException) { continue; }
            throw new InvalidOperationException("Local path fixture did not refuse.");
        }
    }
}
