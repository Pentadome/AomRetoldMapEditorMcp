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
    const int WriteWaitMs = 5000,
        PollMs = 25,
        MaxSnapshotBytes = 64_000_000,
        MaxDecodedBytes = 256_000_000;

    // Recovered TR header: two-byte magic + DWORD payload length + DWORD version. l33t: magic + DWORD decoded length.
    const int TrHeaderSize = 10,
        ScenarioHeaderSize = 8,
        TrSizeOffset = 2,
        ScenarioSizeOffset = 4;

    // Live game-writer outputs and tracked BaseDefense.mythscn both carry four opaque bytes after zlib EOF.
    // Preserve, do not interpret as Adler-32 or claim their game-specific integrity semantics are verified.
    // Required for loading (2026-10-29 live: cut or recomputed-stream copies "failed to load"); not CRC-32/Adler/xxHash32/
    // Murmur3/MD5/SHA-1 of the stream, file or decoded payload, so host code cannot recreate it.
    const int ScenarioTrailerSize = 4;

    /// <summary>Self-test writer delay before appending the trailer: several <see cref="PollMs"/> polls see a stable no-trailer file.</summary>
    const int SelfTestTrailerDelayMs = 200;

    // Windows filename specification: reserved DOS device aliases, even when followed by an extension.
    static readonly string[] DeviceNames = ["CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$"];

    internal static string LocalPath(string path)
    {
        // Windows drive-root spelling is letter, colon, backslash (indices zero/one/two; minimum three chars).
        if (
            path.StartsWith("//", StringComparison.Ordinal)
            || path.StartsWith(@"\\", StringComparison.Ordinal)
        )
            throw new ArgumentException(
                "Explicit drive-qualified local path required; no device/UNC/alternate streams."
            );
        path = path.Replace('/', '\\');
        if (
            !Path.IsPathFullyQualified(path)
            || path.Length < 3
            || path[1] != ':'
            || path[2] != '\\'
            || path[2..].Contains(':')
        )
            throw new ArgumentException(
                "Explicit drive-qualified local path required; no device/UNC/alternate streams."
            );
        // Validate raw spelling before Windows/GetFullPath normalizes trailing dots/spaces in directory components.
        foreach (var component in path[3..].Split('\\')) // Skip Windows drive root, inspect every filename/directory component.
        {
            var stem = component.Split('.')[0].TrimEnd(' ');
            // Windows also reserves COM/LPT1..9 and superscript 1/2/3 aliases.
            var numberedDevice =
                stem.Length == 4
                && (
                    stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                    || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)
                )
                && "123456789¹²³".Contains(stem[^1]);
            if (
                component.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || component.EndsWith(' ')
                || component.EndsWith('.')
                || DeviceNames.Contains(stem, StringComparer.OrdinalIgnoreCase)
                || numberedDevice
            )
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

    /// <summary>
    /// Resolves outputPath "auto" to the first unused "&lt;stem&gt;.e&lt;N&gt;&lt;extension&gt;" beside the source (an existing ".eN" suffix is replaced);
    /// any other value is returned unchanged. The result still goes through <see cref="ApprovedNewPath"/>.
    /// </summary>
    internal static string ResolveOutput(string output, string sourcePath, string extension)
    {
        if (!output.Equals("auto", StringComparison.Ordinal))
            return output;
        var source = LocalPath(sourcePath);
        var stem = System.Text.RegularExpressions.Regex.Replace(
            Path.GetFileNameWithoutExtension(source),
            @"\.e\d+$",
            ""
        );
        var directory = Path.GetDirectoryName(source)!;
        for (var n = 1; n < 10_000; n++)
        {
            var candidate = Path.Combine(directory, stem + ".e" + n + extension);
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
                return candidate;
        }
        throw new ArgumentException(
            "No free .eN output name beside the source; supply an explicit outputPath."
        );
    }

    internal static string ApprovedNewPath(string path, string extension)
    {
        path = LocalPath(path);
        if (
            !path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
            || !Directory.Exists(Path.GetDirectoryName(path))
            || File.Exists(path)
            || Directory.Exists(path)
        )
            throw new ArgumentException(
                "Output must be new file with requested extension in existing directory; never silently overwrite."
            );
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
            if (
                !Directory.Exists(directory)
                || !string.Equals(
                    Path.GetFileName(directory),
                    kind,
                    StringComparison.OrdinalIgnoreCase
                )
            )
                throw new ArgumentException(
                    "profileDirectory must be existing active game's " + kind + " directory."
                );
            return directory;
        }
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Games",
            "Age of Mythology Retold"
        );
        var candidates = Directory.Exists(root)
            ? Directory
                .GetDirectories(root)
                .Select(d => Path.Combine(d, kind))
                .Where(Directory.Exists)
                .ToArray()
            : [];
        if (candidates.Length != 1)
            throw new InvalidDataException(
                "Cannot identify one active "
                    + kind
                    + " directory; supply profileDirectory explicitly."
            );
        _ = LocalPath(Path.Combine(candidates[0], "profile-path-check"));
        return candidates[0];
    }

    static string Scratch(string directory, string purpose, string extension) =>
        Path.Combine(
            directory,
            "AomMcp-"
                + purpose
                + "-"
                + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)
                + extension
        );

    /// <summary>Profile sibling folder (&lt;id&gt;\scenario_backup) holding every scenario backup a tool takes (user requirement).</summary>
    internal const string ScenarioBackupFolder = "scenario_backup";

    /// <summary>Profile sibling folder (&lt;id&gt;\trigger_backup) holding every trigger backup a tool takes (user requirement).</summary>
    internal const string TriggerBackupFolder = "trigger_backup";

    /// <summary>Profile sibling folder (&lt;id&gt;\ai_backup) holding replaced computer-player .xs files (user requirement).</summary>
    internal const string AiBackupFolder = "ai_backup";

    /// <summary>Profile sibling folder (&lt;id&gt;\campaign_backup) holding one subfolder per campaign mod update (user requirement).</summary>
    internal const string CampaignBackupFolder = "campaign_backup";

    /// <summary>Timestamp part of generated backup names; sortable, local time.</summary>
    const string BackupStampFormat = "yyyyMMdd-HHmmss";

    /// <summary>Profile home &lt;id&gt; of a profile kind directory such as &lt;id&gt;\scenario or &lt;id&gt;\trigger.</summary>
    /// <param name="profileKindDirectory">Validated profile scenario or trigger directory.</param>
    /// <returns>Parent profile directory.</returns>
    internal static string ProfileHomeOf(string profileKindDirectory) =>
        Path.GetDirectoryName(
            Path.GetFullPath(profileKindDirectory).TrimEnd(Path.DirectorySeparatorChar)
        )!;

    /// <summary>Backup folder &lt;id&gt;\&lt;folder&gt;, e.g. &lt;id&gt;\scenario_backup.</summary>
    /// <param name="home">Profile home &lt;id&gt;.</param>
    /// <param name="folder">One of the *BackupFolder constants.</param>
    /// <param name="create">Create the folder when missing (apply); preflight leaves the disk untouched.</param>
    /// <returns>Full local backup folder path.</returns>
    internal static string BackupDirectory(string home, string folder, bool create)
    {
        var directory = Path.GetDirectoryName(
            LocalPath(Path.Combine(home, folder, "backup-path-check"))
        )!;
        if (create)
            Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>Generated backup name AomMcp-&lt;purpose&gt;-&lt;timestamp&gt;-&lt;id&gt;&lt;extension&gt;.</summary>
    /// <param name="purpose">Short tag.</param>
    /// <param name="extension">File extension, or empty for a folder.</param>
    /// <returns>Unique file or folder name.</returns>
    static string BackupName(string purpose, string extension) =>
        "AomMcp-"
        + purpose
        + "-"
        + DateTime.Now.ToString(BackupStampFormat, CultureInfo.InvariantCulture)
        + "-"
        + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)
        + extension;

    /// <summary>
    /// Supplied backup name (bare name or full path) resolved inside <paramref name="directory"/>, or a generated one when omitted.
    /// </summary>
    /// <param name="args">Tool arguments possibly naming the backup.</param>
    /// <param name="field">Argument name.</param>
    /// <param name="directory">Backup folder.</param>
    /// <param name="generated">Name used when <paramref name="field"/> is omitted.</param>
    /// <returns>Full path directly inside <paramref name="directory"/>.</returns>
    static string BackupEntry(JsonElement args, string field, string directory, string generated)
    {
        if (
            !args.TryGetProperty(field, out var supplied)
            || supplied.ValueKind != JsonValueKind.String
        )
            return Path.Combine(directory, generated);
        var name = supplied
            .GetString()!
            .Replace('/', Path.DirectorySeparatorChar)
            .TrimEnd(Path.DirectorySeparatorChar);
        var path = LocalPath(
            name.Contains(Path.DirectorySeparatorChar) ? name : Path.Combine(directory, name)
        );
        if (
            !string.Equals(
                Path.GetDirectoryName(path),
                directory,
                StringComparison.OrdinalIgnoreCase
            )
        )
            throw new ArgumentException(
                field
                    + " must name an entry directly inside "
                    + directory
                    + " (or omit it for a generated name)."
            );
        return path;
    }

    /// <summary>
    /// New backup file inside &lt;id&gt;\<paramref name="folder"/>. Omitted <paramref name="field"/> generates
    /// AomMcp-&lt;purpose&gt;-&lt;timestamp&gt;-&lt;id&gt;&lt;extension&gt;; a supplied bare filename or full path must resolve inside that folder.
    /// </summary>
    /// <param name="args">Tool arguments possibly naming the backup.</param>
    /// <param name="field">Argument name, e.g. backupScenarioPath.</param>
    /// <param name="home">Profile home &lt;id&gt;.</param>
    /// <param name="folder">One of the *BackupFolder constants.</param>
    /// <param name="extension">Required extension (.mythscn/.trg/.xs).</param>
    /// <param name="purpose">Short tag in generated names.</param>
    /// <param name="create">Create the folder and require a new file (apply) or only validate the spelling (preflight).</param>
    /// <returns>Full path of the not-yet-existing backup file.</returns>
    internal static string BackupPath(
        JsonElement args,
        string field,
        string home,
        string folder,
        string extension,
        string purpose,
        bool create
    )
    {
        var directory = BackupDirectory(home, folder, create);
        var path = BackupEntry(args, field, directory, BackupName(purpose, extension));
        if (!create)
        {
            if (!path.EndsWith(extension, StringComparison.OrdinalIgnoreCase) || File.Exists(path))
                throw new ArgumentException(
                    field + " must be a new " + extension + " file; never silently overwrite."
                );
            return path;
        }
        return ApprovedNewPath(path, extension);
    }

    /// <summary>
    /// New backup subfolder inside &lt;id&gt;\<paramref name="folder"/> (not created here; the writer creates it with its first file).
    /// Omitted <paramref name="field"/> generates AomMcp-&lt;purpose&gt;-&lt;timestamp&gt;-&lt;id&gt;.
    /// </summary>
    /// <param name="args">Tool arguments possibly naming the folder.</param>
    /// <param name="field">Argument name, e.g. backupDirectory.</param>
    /// <param name="home">Profile home &lt;id&gt;.</param>
    /// <param name="folder">One of the *BackupFolder constants.</param>
    /// <param name="purpose">Short tag in generated names.</param>
    /// <returns>Full path of the not-yet-existing backup folder.</returns>
    internal static string BackupFolderPath(
        JsonElement args,
        string field,
        string home,
        string folder,
        string purpose
    )
    {
        var path = BackupEntry(
            args,
            field,
            BackupDirectory(home, folder, false),
            BackupName(purpose, "")
        );
        if (Directory.Exists(path) || File.Exists(path))
            throw new ArgumentException(field + " must be a NEW folder: " + path);
        return path;
    }

    /// <summary>Moves a verified game-written trigger backup out of the profile trigger folder into &lt;id&gt;\trigger_backup.</summary>
    /// <param name="written">Game-written backup in the profile trigger directory.</param>
    /// <param name="bytes">Verified bytes the game wrote.</param>
    /// <param name="triggerDirectory">Validated profile trigger directory.</param>
    /// <returns>New backup path; hash re-verified after the move.</returns>
    static string RetainTriggerBackup(string written, byte[] bytes, string triggerDirectory) =>
        RetainInBackup(written, bytes, triggerDirectory, TriggerBackupFolder, ".trg");

    /// <summary>Moves verified game-written checkpoint staging out of the profile scenario folder into &lt;id&gt;\scenario_backup
    /// (session 3: AomMcp-checkpoint-* files cluttered the scenario list).</summary>
    /// <param name="written">Game-written staging file in the profile scenario directory.</param>
    /// <param name="bytes">Verified bytes the game wrote.</param>
    /// <param name="scenarioDirectory">Validated profile scenario directory.</param>
    /// <returns>New staging path; hash re-verified after the move.</returns>
    static string RetainScenarioStaging(string written, byte[] bytes, string scenarioDirectory) =>
        RetainInBackup(written, bytes, scenarioDirectory, ScenarioBackupFolder, ".mythscn");

    /// <summary>Moves a verified file from a profile kind folder into its &lt;id&gt;\*_backup sibling, keeping the name.</summary>
    /// <param name="written">File inside <paramref name="kindDirectory"/>.</param>
    /// <param name="bytes">Verified bytes of the file.</param>
    /// <param name="kindDirectory">Validated profile scenario/trigger directory.</param>
    /// <param name="folder">One of the *BackupFolder constants.</param>
    /// <param name="extension">Required extension.</param>
    /// <returns>New path; hash re-verified after the move.</returns>
    static string RetainInBackup(
        string written,
        byte[] bytes,
        string kindDirectory,
        string folder,
        string extension
    )
    {
        var target = ApprovedNewPath(
            Path.Combine(
                BackupDirectory(ProfileHomeOf(kindDirectory), folder, true),
                Path.GetFileName(written)
            ),
            extension
        );
        File.Move(written, target);
        if (Layout.Hash(target) != Convert.ToHexStringLower(SHA256.HashData(bytes)))
            throw new IOException("Backup changed while moving to " + target + ".");
        return target;
    }

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

    /// <summary>editor_triggers apply scripts modes: restore (Script Manager re-include) or ignore (report lastKnown only).
    /// One is required when lastKnown is non-empty (plan A1).</summary>
    internal static readonly string[] TriggerScriptModes = ["restore", "ignore"];

    internal static void PreflightTriggers(JsonElement args)
    {
        var operation = args.GetProperty("operation").GetString();
        var path = LocalPath(args.GetProperty("path").GetString()!);
        // Conditional field sets prevent ignored edit parameters from masquerading as applied edits.
        string[] allowed = operation switch
        {
            "inspect" or "validate" => ["operation", "path"],
            "patch" =>
            [
                "operation",
                "path",
                "outputPath",
                "name",
                "active",
                "loop",
                "code",
                "confirmWrite",
                "expectedSha256",
            ],
            "create" => ["operation", "path", "name", "active", "loop", "code", "confirmWrite"],
            "export" => ["operation", "path", "profileDirectory", "confirmWrite"],
            "apply" =>
            [
                "operation",
                "path",
                "profileDirectory",
                "expectedSha256",
                "expectedLiveSha256",
                "expectedLivePath",
                "autoBackup",
                "expectedBasePath",
                "expectedBaseSha256",
                "confirmDestructive",
                "scripts",
            ],
            _ => throw new ArgumentException("Unknown trigger operation."),
        };
        Catalog.ValidateObject(args, allowed);
        if (operation == "export")
        {
            Confirm(args, "confirmWrite");
            ApprovedNewPath(path, ".trg");
            _ = ProfileDirectory(args, "trigger"); // Native output uses fresh GUID staging, never caller-named basename.
        }
        else if (operation == "create")
        {
            // Session 3: an empty live trigger set has no controller to inspect/patch; create starts one from the template.
            Confirm(args, "confirmWrite");
            ApprovedNewPath(path, ".trg");
        }
        else
        {
            if (operation == "apply")
                _ = CampaignTriggers.Read(path);
            else
                TriggerCodec.Parse(TriggerCodec.ReadFile(path));
            if (operation == "patch")
            {
                Confirm(args, "confirmWrite");
                if (!args.TryGetProperty("outputPath", out var output))
                    throw new ArgumentException("Patch requires outputPath.");
                ApprovedNewPath(output.GetString()!, ".trg");
                if (
                    !args.TryGetProperty("name", out _)
                    && !args.TryGetProperty("active", out _)
                    && !args.TryGetProperty("loop", out _)
                    && !args.TryGetProperty("code", out _)
                )
                    throw new ArgumentException("Patch requires at least one edited field.");
                if (
                    args.TryGetProperty("expectedSha256", out var expected)
                    && (expected.GetString() is not { Length: 64 } sha || !sha.All(Uri.IsHexDigit))
                )
                    throw new ArgumentException(
                        "expectedSha256 must be the 64-digit SHA-256 of path (from inspect)."
                    );
            }
            if (operation == "apply")
            {
                Confirm(args, "confirmDestructive");
                var triggerDirectory = ProfileDirectory(args, "trigger");
                if (!args.TryGetProperty("scripts", out _))
                {
                    var (known, _) = XsScripts.LastKnown(triggerDirectory);
                    if (known.Length > 0)
                        throw new ArgumentException(
                            $"Import resets the Triggers -> XS Scripts list and lastKnown is not empty ({string.Join(", ", known.Select(k => k.Name))}): "
                                + "pass scripts=\"restore\" (re-include through the Script Manager, normal or alternative UI; 2560x1440 and 1920x1080 live-verified, other supported sizes best effort) or scripts=\"ignore\" (re-add by hand)."
                        );
                }
                var auto =
                    args.TryGetProperty("autoBackup", out var autoFlag) && autoFlag.GetBoolean();
                if (
                    auto
                    && (
                        args.TryGetProperty("expectedLivePath", out _)
                        || args.TryGetProperty("expectedLiveSha256", out _)
                    )
                )
                    throw new ArgumentException(
                        "autoBackup replaces the reviewed live export: do not pass expectedLivePath/expectedLiveSha256 with it."
                    );
                if (
                    args.TryGetProperty("expectedBasePath", out var basePath)
                    != args.TryGetProperty("expectedBaseSha256", out var baseSha)
                )
                    throw new ArgumentException(
                        "expectedBasePath and expectedBaseSha256 go together (editor_trigger_edit basePath/baseSha256)."
                    );
                if (basePath.ValueKind != JsonValueKind.Undefined)
                {
                    if (!auto)
                        throw new ArgumentException(
                            "expectedBasePath/expectedBaseSha256 are the autoBackup lost-update check; without autoBackup use expectedLivePath/expectedLiveSha256."
                        );
                    var baseFile = LocalPath(basePath.GetString()!);
                    _ = CampaignTriggers.Read(baseFile);
                    if (baseSha.GetString() is not { Length: 64 } b || !b.All(Uri.IsHexDigit))
                        throw new ArgumentException(
                            "expectedBaseSha256 must be a 64-digit SHA-256."
                        );
                    if (!Layout.Hash(baseFile).Equals(b, StringComparison.OrdinalIgnoreCase))
                        throw new WorkflowFailure(
                            "SOURCE_CHANGED",
                            "preflight",
                            "Base TR SHA-256 differs from expectedBaseSha256.",
                            false,
                            false,
                            "Use basePath/baseSha256 from the editor_trigger_edit result; no native command dispatched."
                        );
                }
                if (auto)
                {
                    if (
                        args.GetProperty("expectedSha256").GetString()
                            is not { Length: 64 } prepared
                        || !prepared.All(Uri.IsHexDigit)
                    )
                        throw new ArgumentException(
                            "expectedSha256 must be 64-digit SHA-256 from inspected prepared file."
                        );
                    if (!Layout.Hash(path).Equals(prepared, StringComparison.OrdinalIgnoreCase))
                        throw new WorkflowFailure(
                            "SOURCE_CHANGED",
                            "preflight",
                            "Prepared TR SHA-256 differs from expected value.",
                            false,
                            false,
                            "Inspect the prepared file again before attempting apply; no native command dispatched."
                        );
                    return;
                }
                foreach (var key in ApplyHashes)
                    if (
                        !args.TryGetProperty(key, out var hash)
                        || hash.GetString() is not { Length: 64 } sha
                        || !sha.All(Uri.IsHexDigit)
                    )
                        throw new ArgumentException(
                            key + " must be 64-digit SHA-256 from inspected files."
                        );
                var source = LocalPath(args.GetProperty("expectedLivePath").GetString()!);
                if (source.Equals(path, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException(
                        "expectedLivePath must be separate game-exported original."
                    );
                _ = CampaignTriggers.Read(source);
                if (
                    !Layout
                        .Hash(path)
                        .Equals(
                            args.GetProperty("expectedSha256").GetString(),
                            StringComparison.OrdinalIgnoreCase
                        )
                    || !Layout
                        .Hash(source)
                        .Equals(
                            args.GetProperty("expectedLiveSha256").GetString(),
                            StringComparison.OrdinalIgnoreCase
                        )
                )
                    throw new WorkflowFailure(
                        "SOURCE_CHANGED",
                        "preflight",
                        "Prepared/original TR SHA-256 differs from expected value.",
                        false,
                        false,
                        "Inspect both exports again before attempting apply; no native command dispatched."
                    );
            }
        }
    }

    /// <summary>Extension of the side file the game writer creates next to a saved scenario (live 2026-10-25: empty .bak).</summary>
    const string WriterBackupExtension = ".bak";

    /// <summary>Purpose tag of the backup of a checkpoint replaced by overwrite=true (scenario_backup name).</summary>
    const string ReplacedCheckpointPurpose = "checkpoint-replaced";

    /// <summary>editor_save_checkpoint preflight: new destination, or overwrite=true + expectedSha256 of the existing one.</summary>
    /// <param name="args">Tool arguments.</param>
    /// <returns>Validated destination path.</returns>
    internal static string PreflightCheckpoint(JsonElement args)
    {
        Confirm(args, "confirmWrite");
        _ = ProfileDirectory(args, "scenario");
        var path = args.GetProperty("path").GetString()!;
        var overwrite =
            args.TryGetProperty("overwrite", out var flag) && flag.ValueKind == JsonValueKind.True;
        var hasHash = args.TryGetProperty("expectedSha256", out var expected);
        if (!overwrite)
        {
            if (hasHash)
                throw new ArgumentException(
                    "expectedSha256 is the overwrite=true lost-update check; omit it for a new destination."
                );
            return ApprovedNewPath(path, ".mythscn");
        }
        path = LocalPath(path);
        if (!path.EndsWith(".mythscn", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            throw new ArgumentException(
                "overwrite=true needs an existing .mythscn destination; use a new path without overwrite instead."
            );
        if (!hasHash || expected.GetString() is not { Length: 64 } sha || !sha.All(Uri.IsHexDigit))
            throw new ArgumentException(
                "overwrite=true requires expectedSha256 (64-digit SHA-256 of the current destination)."
            );
        RequireDestination(path, sha, false);
        return path;
    }

    /// <summary>Refuses with DESTINATION_CHANGED when the existing checkpoint differs from the reviewed hash.</summary>
    /// <param name="path">Existing destination.</param>
    /// <param name="sha256">Caller-reviewed SHA-256.</param>
    /// <param name="dispatched">Whether the game writer already ran (staging exists).</param>
    static void RequireDestination(string path, string sha256, bool dispatched)
    {
        if (!string.Equals(Layout.Hash(path), sha256, StringComparison.OrdinalIgnoreCase))
            throw new WorkflowFailure(
                "DESTINATION_CHANGED",
                dispatched ? "copy" : "preflight",
                "Destination SHA-256 differs from expectedSha256; destination untouched.",
                dispatched,
                false,
                "Hash the destination again (or pick a new path) before overwriting."
            );
    }

    internal static void PreflightRecovery(JsonElement args)
    {
        Catalog.ValidateObject(
            args,
            [
                "operation",
                "stagedPath",
                "outputPath",
                "expectedSha256",
                "profileDirectory",
                "confirmWrite",
            ]
        );
        var operation = args.GetProperty("operation").GetString();
        if (operation is not "inspect" and not "recover")
            throw new ArgumentException("operation: inspect/recover.");
        var staged = LocalPath(args.GetProperty("stagedPath").GetString()!);
        var file = Path.GetFileName(staged);
        var kind =
            file.StartsWith("AomMcp-checkpoint-", StringComparison.Ordinal)
            && file.EndsWith(".mythscn", StringComparison.OrdinalIgnoreCase)
                ? "scenario"
            : file.StartsWith("AomMcp-trigger-export-", StringComparison.Ordinal)
            && file.EndsWith(".trg", StringComparison.OrdinalIgnoreCase)
                ? "trigger"
            : throw new ArgumentException(
                "Only named game-writer checkpoint/trigger-export staging files can be recovered."
            );
        if (
            !string.Equals(
                Path.GetDirectoryName(staged),
                ProfileDirectory(args, kind),
                StringComparison.OrdinalIgnoreCase
            )
        )
            throw new ArgumentException(
                "Staging must be directly inside active profile " + kind + " directory."
            );
        if (operation == "inspect")
        {
            Catalog.ValidateObject(args, ["operation", "stagedPath", "profileDirectory"]);
            return;
        }
        Confirm(args, "confirmWrite");
        if (
            !args.TryGetProperty("outputPath", out var output)
            || !args.TryGetProperty("expectedSha256", out var expected)
        )
            throw new ArgumentException(
                "Recover requires outputPath and expectedSha256 from inspect."
            );
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
            throw new WorkflowFailure(
                "STAGING_UNAVAILABLE",
                "inspect",
                e.Message,
                false,
                true,
                "Wait for writer, then inspect same staging file. Do not dispatch native export again.",
                staged,
                inner: e
            );
        }
        var check = scenario ? ExportFormats.Scenario(bytes) : ExportFormats.Trigger(bytes);
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (args.GetProperty("operation").GetString() == "inspect")
            return new
            {
                stagedPath = staged,
                sha256,
                sizeBytes = bytes.Length,
                fileVerified = check.Valid,
                check.Format,
                check.Reason,
                check.SuffixBytes,
                nativeDispatched = false,
                nextAction = check.Valid
                    ? "Recover to a NEW path with expectedSha256 and confirmWrite=true; no native retry."
                    : "Staging not structurally verified; do not recover/import or repeat native operation blindly.",
            };
        if (!check.Valid)
            throw new WorkflowFailure(
                "STAGING_INVALID",
                "verify",
                check.Reason,
                false,
                true,
                "Inspect staging and game state; no file was copied and no native command dispatched.",
                staged
            );
        if (
            !string.Equals(
                sha256,
                args.GetProperty("expectedSha256").GetString(),
                StringComparison.OrdinalIgnoreCase
            )
        )
            throw new WorkflowFailure(
                "STAGING_CHANGED",
                "verify",
                "Staging SHA-256 differs from inspected value.",
                false,
                true,
                "Inspect staging again; no file was copied.",
                staged
            );
        var output = ApprovedNewPath(
            args.GetProperty("outputPath").GetString()!,
            scenario ? ".mythscn" : ".trg"
        );
        var copied = CopyNew(bytes, output);
        return new
        {
            path = output,
            stagedPath = staged,
            sha256 = copied,
            fileVerified = true,
            nativeDispatched = false,
            nextAction = "Use recovered file as an export snapshot; semantic scenario/editor state not proven.",
        };
    }

    static bool Complete(byte[] bytes, bool scenario) =>
        scenario ? ExportFormats.Scenario(bytes).Valid : ExportFormats.Trigger(bytes).Valid;

    static byte[] Export(
        string path,
        bool scenario,
        Action<string, JsonElement> native,
        Action guard
    )
    {
        ApprovedNewPath(path, scenario ? ".mythscn" : ".trg");
        // Reserve a unique file. Game writer may refuse an existing reservation; then verification times out,
        // never retries or touches user-named originals. Explicit saves/imports are not validation probes.
        using (new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        try
        {
            native(
                scenario ? "saveScenario" : "uiSaveTriggers",
                scenario
                    ? JsonSerializer.SerializeToElement(
                        new { fname = NativeProfileStem(path), confirmDestructive = true }
                    )
                    : JsonSerializer.SerializeToElement(
                        new { filename = NativeProfileStem(path), confirmDestructive = true }
                    )
            );
        }
        catch (Exception e)
        {
            // Game.Focus throws before any input/native dispatch; empty host reservation is safe to remove.
            if (e is FocusRefusedException && new FileInfo(path).Length == 0)
            {
                File.Delete(path);
                throw new WorkflowFailure(
                    "FOCUS_NOT_GRANTED",
                    "focus",
                    e.Message,
                    false,
                    false,
                    "Bring editor window foreground manually, inspect state, then call again; nothing was dispatched.",
                    inner: e
                );
            }
            throw new WorkflowFailure(
                "EXPORT_DISPATCH_UNCERTAIN",
                "dispatch",
                e.Message,
                true,
                true,
                "Inspect staging and live editor state; do not retry native export.",
                path,
                inner: e
            );
        }
        var timer = Stopwatch.StartNew();
        byte[]? previous = null;
        var lastReason = "Game-writer staging file still empty.";
        while (timer.ElapsedMilliseconds < WriteWaitMs)
        {
            try
            {
                guard();
            }
            catch (Exception e)
            {
                throw new WorkflowFailure(
                    "EXPORT_GUARD_CHANGED",
                    "verify",
                    e.Message,
                    true,
                    true,
                    "Inspect staging and game state; no native retry.",
                    path,
                    inner: e
                );
            }
            try
            {
                var length = new FileInfo(path).Length;
                if (length > 0 && length <= MaxSnapshotBytes)
                {
                    using var file = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read
                    );
                    if (file.Length > MaxSnapshotBytes)
                        throw new IOException("Game output exceeds host file bound.");
                    var bytes = new byte[checked((int)file.Length)];
                    file.ReadExactly(bytes);
                    var check = scenario
                        ? ExportFormats.Scenario(bytes)
                        : ExportFormats.Trigger(bytes);
                    lastReason = check.Reason;
                    // The game appends the trailer after the zlib stream; a stable no-trailer read is a write still in
                    // progress. 2026-10-29 live: a copy taken before the trailer (AomMcp_s4_verify) and the user's
                    // WakeIsland.mythscn with its 4 trailer bytes cut both "failed to load" in Load Scenario.
                    var trailed = !scenario || check.SuffixBytes == ScenarioTrailerSize;
                    if (check.Valid && !trailed)
                        lastReason =
                            "Zlib stream complete but the game trailer is not written yet (files without it fail to load).";
                    if (
                        previous is not null
                        && bytes.AsSpan().SequenceEqual(previous)
                        && check.Valid
                        && trailed
                    )
                        return bytes;
                    previous = bytes;
                }
            }
            catch (IOException)
            { /* Retry reads only; writer may hold file or compression may be incomplete. */
            }
            Thread.Sleep(PollMs);
        }
        throw new WorkflowFailure(
            "EXPORT_NOT_VERIFIED",
            "verify",
            "Game export not verified: " + lastReason,
            true,
            true,
            "Inspect staging with editor_export_recovery; do not retry native export.",
            path
        );
    }

    static string CopyNew(byte[] bytes, string output)
    {
        using (
            var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None)
        )
            file.Write(bytes);
        var expected = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (Layout.Hash(output) != expected)
            throw new IOException("Game output copy verification failed.");
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
                return new
                {
                    path,
                    stagedPath = staging,
                    sha256,
                    fileVerified = true,
                    limitation = "Game-written snapshot copied to approved new destination; no scene trigger changes requested. Profile must match active account; staging retained. Native exporter never receives caller-named basename.",
                };
            }
            catch (WorkflowFailure e)
            {
                throw new WorkflowFailure(
                    e.Code,
                    e.Phase,
                    e.Message,
                    e.NativeDispatched,
                    e.OutcomeUnknown,
                    e.NextAction,
                    staging,
                    path,
                    e
                );
            }
            catch (Exception e)
            {
                throw new WorkflowFailure(
                    "EXPORT_COPY_UNCERTAIN",
                    "copy",
                    e.Message,
                    true,
                    true,
                    "Inspect staging/destination before any other operation; no native retry.",
                    staging,
                    path,
                    e
                );
            }
        }
        var directory = ProfileDirectory(args, "trigger");
        var desired = TriggerCodec.ReadFile(path);
        _ = CampaignTriggers.Parse(desired);
        var auto = args.TryGetProperty("autoBackup", out var autoFlag) && autoFlag.GetBoolean();
        var expectedLive = auto
            ? []
            : TriggerCodec.ReadFile(args.GetProperty("expectedLivePath").GetString()!);
        var baseExport =
            auto && args.TryGetProperty("expectedBasePath", out var baseArg)
                ? TriggerCodec.ReadFile(baseArg.GetString()!)
                : null;
        if (
            baseExport is not null
            && !Convert
                .ToHexStringLower(SHA256.HashData(baseExport))
                .Equals(
                    args.GetProperty("expectedBaseSha256").GetString(),
                    StringComparison.OrdinalIgnoreCase
                )
        )
            throw new WorkflowFailure(
                "SOURCE_CHANGED",
                "preflight",
                "Base TR changed since preview.",
                false,
                false,
                "Inspect the base export again. No game command dispatched.",
                destinationPath: path
            );
        if (
            !Convert
                .ToHexStringLower(SHA256.HashData(desired))
                .Equals(
                    args.GetProperty("expectedSha256").GetString(),
                    StringComparison.OrdinalIgnoreCase
                )
            || (
                !auto
                && !Convert
                    .ToHexStringLower(SHA256.HashData(expectedLive))
                    .Equals(
                        args.GetProperty("expectedLiveSha256").GetString(),
                        StringComparison.OrdinalIgnoreCase
                    )
            )
        )
            throw new WorkflowFailure(
                "SOURCE_CHANGED",
                "preflight",
                "Prepared TR changed since preview.",
                false,
                false,
                "Inspect/preview TR again. No game command dispatched.",
                destinationPath: path
            );
        var backup = Scratch(directory, "before-trigger-import", ".trg");
        var staged = Scratch(directory, "trigger-import", ".trg");
        var roundtrip = Scratch(directory, "trigger-roundtrip", ".trg");
        var importDispatched = false;
        try
        {
            var original = Export(backup, false, native, Guard);
            backup = RetainTriggerBackup(backup, original, directory);
            var liveCheck =
                "autoBackup: game-written export taken immediately before import is the retained original (no separately reviewed live export).";
            var lostUpdateCheck = auto ? "skipped" : "expectedLive";
            if (baseExport is not null)
            {
                if (!CampaignTriggers.EquivalentGameExport(baseExport, original, out var baseCheck))
                    throw new WorkflowFailure(
                        "LIVE_SOURCE_CHANGED",
                        "backup",
                        "Live editor triggers changed since the base export the edits came from ("
                            + baseCheck
                            + "): "
                            + string.Join(
                                "; ",
                                CampaignTriggers.DifferingTriggers(baseExport, original)
                            ),
                        true,
                        false,
                        "Backup retained (it holds the newer live triggers); re-export, redo the edits on it, then apply. No import dispatched.",
                        backup,
                        path
                    );
                lostUpdateCheck = "verified";
                liveCheck =
                    "autoBackup: fresh game-written backup matches the base export ("
                    + baseCheck
                    + ")";
            }
            if (
                !auto
                && !CampaignTriggers.EquivalentGameExport(expectedLive, original, out liveCheck)
            )
                throw new WorkflowFailure(
                    "LIVE_SOURCE_CHANGED",
                    "backup",
                    "Game-written original differs from reviewed live export: " + liveCheck,
                    true,
                    false,
                    "Backup retained; inspect live editor/exports. No import dispatched.",
                    backup,
                    path
                );
            using (
                var file = new FileStream(
                    staged,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None
                )
            )
                file.Write(desired);
            importDispatched = true;
            native(
                "uiLoadTriggers",
                JsonSerializer.SerializeToElement(
                    new { filename = NativeProfileStem(staged), confirmDestructive = true }
                )
            );
            var observed = Export(roundtrip, false, native, Guard);
            if (!CampaignTriggers.EquivalentGameExport(desired, observed, out var semanticCheck))
                throw new WorkflowFailure(
                    "ROUNDTRIP_MISMATCH",
                    "verify",
                    semanticCheck,
                    true,
                    true,
                    "Review staged, backup and observed files. Do not retry/rollback automatically.",
                    backup,
                    roundtrip
                );
            return new
            {
                inputPath = path,
                backupPath = backup,
                backupSha256 = Layout.Hash(backup),
                autoBackup = auto,
                lostUpdateCheck,
                liveSourceCheck = liveCheck,
                stagedPath = staged,
                roundTripPath = roundtrip,
                roundTripVerified = true,
                semanticCheck,
                sha256 = Layout.Hash(roundtrip),
                triggerCount = CampaignTriggers.Parse(observed).Triggers.Length,
                xsScripts = XsScripts.Report(args),
                limitation = "Whole trigger set replaced; the Triggers->XS Scripts list was reset by the import (see xsScripts.reAdd). Backup retained; XS compile/effects and scenario persistence unverified. No automatic rollback/retry.",
            };
        }
        catch (WorkflowFailure)
        {
            throw;
        }
        catch (Exception e)
        {
            throw new WorkflowFailure(
                importDispatched ? "IMPORT_OUTCOME_UNKNOWN" : "BACKUP_FAILED",
                importDispatched ? "import/verify" : "backup",
                e.Message,
                true,
                importDispatched,
                importDispatched
                    ? "Inspect game/staging and backup; no automatic rollback or retry."
                    : "Original import not dispatched. Inspect backup/staging before continuing; no retry.",
                backup,
                roundtrip,
                e
            );
        }
    }

    /// <summary>Saves through game writer to a unique profile staging file, then copies verified bytes to approved new destination.</summary>
    /// <param name="game">Guarded editor connection.</param>
    /// <param name="args">Approved new path, active scenario directory and explicit write confirmation.</param>
    /// <param name="native">Typed guarded native dispatcher.</param>
    /// <returns>Destination, retained game-written staging file, hash and structural verification limits.</returns>
    public static object Checkpoint(Game game, JsonElement args, Action<string, JsonElement> native)
    {
        var output = PreflightCheckpoint(args);
        var overwrite = File.Exists(output);
        var scenarioDirectory = ProfileDirectory(args, "scenario");
        var staged = Scratch(scenarioDirectory, "checkpoint", ".mythscn");
        string? replaced = null;
        try
        {
            var bytes = Export(staged, true, native, () => _ = game.Editor());
            string? replacedSha256 = null;
            if (overwrite)
            {
                replacedSha256 = args.GetProperty("expectedSha256").GetString()!.ToLowerInvariant();
                RequireDestination(output, replacedSha256, true);
                var target = BackupPath(
                    args,
                    "",
                    ProfileHomeOf(scenarioDirectory),
                    ScenarioBackupFolder,
                    ".mythscn",
                    ReplacedCheckpointPurpose,
                    true
                );
                File.Move(output, target);
                replaced = target;
                if (Layout.Hash(target) != replacedSha256)
                    throw new IOException(
                        "Replaced checkpoint changed while moving to " + target + "."
                    );
            }
            var sha256 = CopyNew(bytes, output);
            string retained = staged,
                stagingNote;
            try
            {
                retained = RetainScenarioStaging(staged, bytes, scenarioDirectory);
                // The game writer also leaves an empty <stem>.bak (live 2026-10-25); the GUID stem is ours alone.
                var writerBackup = Path.ChangeExtension(staged, WriterBackupExtension);
                if (File.Exists(writerBackup))
                    File.Move(writerBackup, Path.ChangeExtension(retained, WriterBackupExtension));
                stagingNote =
                    "Staging moved to " + ScenarioBackupFolder + " after the verified copy.";
            }
            catch (IOException e)
            {
                stagingNote =
                    "Staging left in the scenario folder (move failed: " + e.Message + ").";
            }
            return new
            {
                path = output,
                stagedPath = retained,
                sha256,
                fileVerified = true,
                overwrite,
                replacedBackupPath = replaced,
                replacedSha256,
                staging = stagingNote,
                limitation = "Game-written l33t/zlib snapshot with decoded length and zlib Adler-32 verified, not semantic reload validation. Four-byte game trailer preserved but not interpreted/validated. Writer may change editor save-name/dirty state. An existing destination is only replaced with overwrite=true + matching expectedSha256, after moving it to "
                    + ScenarioBackupFolder
                    + ".",
            };
        }
        catch (WorkflowFailure e)
        {
            throw new WorkflowFailure(
                e.Code,
                e.Phase,
                e.Message,
                e.NativeDispatched,
                e.OutcomeUnknown,
                e.NextAction,
                staged,
                output,
                e
            );
        }
        catch (Exception e)
        {
            throw new WorkflowFailure(
                "CHECKPOINT_COPY_UNCERTAIN",
                "copy",
                e.Message,
                true,
                true,
                replaced is null
                    ? "Inspect staging/destination before any other operation; no native retry."
                    : "Inspect staging/destination before any other operation; no native retry. The replaced checkpoint was moved to "
                        + replaced
                        + ".",
                staged,
                output,
                e
            );
        }
    }

    /// <summary>Checks local path/refusal rules and bounded snapshot verification without contacting game.</summary>
    public static void SelfTest()
    {
        // Synthetic l33t fixture follows observed four-byte magic, DWORD decoded length, zlib stream and opaque trailer.
        using var memory = new MemoryStream();
        memory.Write("l33t"u8);
        memory.Write(BitConverter.GetBytes(3)); // Three synthetic payload bytes, not engine metadata.
        using (var compressed = new ZLibStream(memory, CompressionMode.Compress, true))
            compressed.Write("abc"u8);
        memory.Write("tail"u8); // Four arbitrary synthetic trailer bytes, no guessed checksum algorithm.
        if (!Complete(memory.ToArray(), true))
            throw new InvalidOperationException("Checkpoint codec fixture failed.");
        for (var missing = 1; missing <= ScenarioTrailerSize + sizeof(uint); missing++)
        {
            // Removing exactly the four optional trailer bytes leaves a valid no-trailer variant.
            if (missing != ScenarioTrailerSize && Complete(memory.ToArray()[..^missing], true))
                throw new InvalidOperationException("Truncated checkpoint fixture did not refuse.");
        }
        var temporary = Path.Combine(
            Path.GetTempPath(),
            "aom-writer-test-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)
        );
        Directory.CreateDirectory(temporary);
        try
        {
            var target = Path.Combine(temporary, "fixture.mythscn");
            var calls = 0;
            var result = Export(
                target,
                true,
                (command, args) =>
                {
                    calls++;
                    Confirm(args, "confirmDestructive"); // Helper already has caller approval for its own fresh staging file.
                    var stem = args.GetProperty("fname").GetString()!;
                    if (command != "saveScenario" || stem != "fixture")
                        throw new InvalidOperationException("Writer routing fixture failed.");
                    // Simulate observed native append; passing fixture.mythscn creates fixture.mythscn.mythscn.
                    File.WriteAllBytes(
                        Path.Combine(temporary, stem + ".mythscn"),
                        memory.ToArray()
                    );
                },
                () => { }
            );
            if (
                calls != 1
                || !result.AsSpan().SequenceEqual(memory.ToArray())
                || File.Exists(target + ".mythscn")
            )
                throw new InvalidOperationException(
                    "Checkpoint append-extension regression failed."
                );
            try
            {
                ApprovedNewPath(target, ".mythscn");
            }
            catch (ArgumentException)
            {
                calls = 0;
            }
            if (calls != 0)
                throw new InvalidOperationException("Existing checkpoint fixture did not refuse.");
            // Writer race: zlib stream first, trailer later. A stable no-trailer read must not be accepted.
            var raced = Path.Combine(temporary, "raced.mythscn");
            var full = memory.ToArray();
            Task? appender = null;
            var racedResult = Export(
                raced,
                true,
                (_, args) =>
                {
                    var file = Path.Combine(
                        temporary,
                        args.GetProperty("fname").GetString()! + ".mythscn"
                    );
                    File.WriteAllBytes(file, full[..^ScenarioTrailerSize]);
                    appender = Task.Run(() =>
                    {
                        Thread.Sleep(SelfTestTrailerDelayMs);
                        using var stream = new FileStream(
                            file,
                            FileMode.Append,
                            FileAccess.Write,
                            FileShare.Read
                        );
                        stream.Write(full.AsSpan(full.Length - ScenarioTrailerSize));
                    });
                },
                () => { }
            );
            appender?.Wait();
            if (!racedResult.AsSpan().SequenceEqual(full))
                throw new InvalidOperationException(
                    "Checkpoint export accepted a scenario before its game trailer."
                );
            var trigger = Path.Combine(temporary, "fixture.trg");
            var desired = TriggerCodec.Serialize(new("fixture", false, false, ""));
            calls = 0;
            var triggerResult = Export(
                trigger,
                false,
                (command, args) =>
                {
                    calls++;
                    Confirm(args, "confirmDestructive"); // Native raw export can overwrite files; helper must retain its guard.
                    var stem = args.GetProperty("filename").GetString()!;
                    if (
                        command != "uiSaveTriggers"
                        || stem != "fixture"
                        || NativeProfileStem(trigger) != stem
                    )
                        throw new InvalidOperationException("Native trigger stem fixture failed.");
                    // Simulate observed native appending behavior. Passing fixture.trg would create fixture.trg.trg.
                    File.WriteAllBytes(Path.Combine(temporary, stem + ".trg"), desired);
                },
                () => { }
            );
            if (
                calls != 1
                || !triggerResult.AsSpan().SequenceEqual(desired)
                || File.Exists(trigger + ".trg")
            )
                throw new InvalidOperationException("Trigger append-extension regression failed.");
            calls = 0;
            try
            {
                Export(
                    Path.Combine(temporary, "unknown.trg"),
                    false,
                    (_, _) =>
                    {
                        calls++;
                        throw new InvalidOperationException("Simulated dispatch uncertainty.");
                    },
                    () => { }
                );
                throw new InvalidOperationException("Uncertain dispatch fixture did not refuse.");
            }
            catch (WorkflowFailure e) when (e.Code == "EXPORT_DISPATCH_UNCERTAIN")
            {
                if (
                    calls != 1
                    || e.Phase != "dispatch"
                    || !e.NativeDispatched
                    || !e.OutcomeUnknown
                    || e.StagingPath is null
                    || !e.NextAction.Contains("do not retry", StringComparison.OrdinalIgnoreCase)
                )
                    throw new InvalidOperationException(
                        "Unknown dispatch lacks recovery evidence."
                    );
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
                    throw new InvalidOperationException(
                        "Focus refusal misreported dispatch or kept reservation."
                    );
            }
        }
        finally
        {
            Directory.Delete(temporary, true);
        }
        var corrupt = memory.ToArray();
        corrupt[ScenarioSizeOffset] = 4; // Deliberately wrong synthetic length.
        if (Complete(corrupt, true))
            throw new InvalidOperationException("Checkpoint length fixture did not refuse.");
        if (LocalPath("C:/safe-name.trg") != @"C:\safe-name.trg")
            throw new InvalidOperationException("Forward-slash local path fixture failed.");
        foreach (
            var path in new[]
            {
                @"relative.trg",
                @"\\server\file.trg",
                "//server/file.trg",
                @"C:\file.trg:stream",
                @"C:\CON.trg",
                @"C:\COM1.trg",
                @"C:\folder.\name.trg",
            }
        )
        {
            try
            {
                LocalPath(path);
            }
            catch (ArgumentException)
            {
                continue;
            }
            throw new InvalidOperationException("Local path fixture did not refuse.");
        }
    }
}
