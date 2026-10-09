using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AomMcp;

/// <summary>Custom-only installation. No elevation, stock overwrites, compilation claims, retries or rollback.</summary>
internal static partial class AiInstaller
{
    const string Issuer = "AomMcp.editor_install_ai.v1";
    internal sealed record Receipt(string Issuer, string DestinationPath, string InstalledSha256,
        string? PreviousSha256, string StagingPath, string? BackupPath, DateTimeOffset AppliedUtc, bool Forced = false);
    internal sealed record Include(string Path, string Sha256, int Bytes);
    [GeneratedRegex("^\\s*include\\s+\"([^\"\\r\\n]+)\"\\s*;?\\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex IncludeLine();
    static bool Under(string path, string root) => path.StartsWith(root.TrimEnd('\\') + '\\', StringComparison.OrdinalIgnoreCase);
    static byte[] ReadXs(string path)
    {
        var info = new FileInfo(EditorFiles.LocalPath(path)); if (!info.Exists || info.Length is < 1 or > 1_000_000) throw new InvalidDataException("XS missing/empty/over 1 MB: " + path);
        return File.ReadAllBytes(info.FullName);
    }
    static string StripComments(string source)
    {
        var result = new StringBuilder(); var quoted = false; var block = false; var line = false;
        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i]; var next = i + 1 < source.Length ? source[i + 1] : '\0';
            if (line) { if (c == '\n') { line = false; result.Append(c); } else result.Append(' '); continue; }
            if (block) { if (c == '*' && next == '/') { block = false; result.Append("  "); i++; } else result.Append(c == '\n' ? c : ' '); continue; }
            if (!quoted && c == '/' && next is '/' or '*') { line = next == '/'; block = next == '*'; result.Append("  "); i++; continue; }
            result.Append(c);
            if (quoted && c == '\\' && next != '\0') { result.Append(next); i++; continue; }
            if (c == '"') quoted = !quoted;
        }
        if (block || quoted) throw new InvalidDataException("Unterminated XS comment/string; include analysis refused.");
        return result.ToString();
    }
    internal static Include[] Includes(string root, string virtualDestination, byte[] source)
    {
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var records = new List<Include>(); long bytes = 0;
        void Visit(string path, byte[] data, int depth)
        {
            if (depth > 32 || !active.Add(path)) throw new InvalidDataException("Cyclic/over-depth AI includes: " + path);
            if (!seen.Add(path)) { active.Remove(path); return; }
            bytes += data.Length; if (seen.Count > 128 || bytes > 8_000_000) throw new InvalidDataException("AI include graph exceeds file/byte bounds.");
            string text; try { text = new UTF8Encoding(false, true).GetString(data).TrimStart('\uFEFF'); } catch (DecoderFallbackException e) { throw new InvalidDataException("XS include analysis supports strict UTF-8 only.", e); }
            if (!path.Equals(virtualDestination, StringComparison.OrdinalIgnoreCase)) records.Add(new(path, CheckpointDocument.Hash(data), data.Length));
            foreach (var line in StripComments(text).Split('\n'))
            {
                var trimmed = line.TrimStart(); if (!trimmed.StartsWith("include", StringComparison.Ordinal) || trimmed.Length > 7 && (char.IsLetterOrDigit(trimmed[7]) || trimmed[7] == '_')) continue;
                var match = IncludeLine().Match(line); if (!match.Success) throw new InvalidDataException("Unreviewed XS include directive: " + line.Trim());
                var include = AiScripts.Contained(root, AiScripts.Relative(match.Groups[1].Value));
                if (active.Contains(include)) throw new InvalidDataException("Cyclic AI include: " + include);
                if (seen.Contains(include)) continue;
                if (seen.Count >= 128 || depth >= 32) throw new InvalidDataException("AI include graph exceeds file/depth bounds.");
                Visit(include, include.Equals(virtualDestination, StringComparison.OrdinalIgnoreCase) ? source : ReadXs(include), depth + 1);
            }
            active.Remove(path);
        }
        Visit(virtualDestination, source, 0); return records.ToArray();
    }
    static string RequiredHash(JsonElement args, string key)
    {
        if (!args.TryGetProperty(key, out var value) || value.GetString() is not { Length: 64 } sha || !sha.All(Uri.IsHexDigit))
            throw new ArgumentException("Required pinned SHA-256: " + key);
        return sha;
    }
    internal static void Preflight(JsonElement args)
    {
        Catalog.ValidateObject(args, ["sourcePath", "expectedSha256", "destination", "preview", "stagingPath", "receiptPath", "ownershipReceiptPath", "expectedReceiptSha256", "expectedInstalledSha256", "backupPath", "confirmWrite", "confirmDestructive", "force"]);
        _ = EditorFiles.LocalPath(args.GetProperty("sourcePath").GetString()!); _ = AiScripts.Relative(args.GetProperty("destination").GetString()!);
        _ = RequiredHash(args, "expectedSha256");
        if (!Preview(args))
        {
            EditorFiles.Confirm(args, "confirmWrite");
            _ = EditorFiles.ApprovedNewPath(args.GetProperty("stagingPath").GetString()!, ".xs");
            _ = EditorFiles.LocalPath(args.GetProperty("receiptPath").GetString()!);
            if (args.TryGetProperty("ownershipReceiptPath", out var receipt))
            {
                _ = EditorFiles.LocalPath(receipt.GetString()!); _ = RequiredHash(args, "expectedReceiptSha256"); _ = RequiredHash(args, "expectedInstalledSha256");
                EditorFiles.Confirm(args, "confirmDestructive"); _ = EditorFiles.ApprovedNewPath(args.GetProperty("backupPath").GetString()!, ".xs");
            }
            else if (Force(args))
            {
                _ = RequiredHash(args, "expectedInstalledSha256");
                EditorFiles.Confirm(args, "confirmDestructive"); _ = EditorFiles.ApprovedNewPath(args.GetProperty("backupPath").GetString()!, ".xs");
            }
        }
    }
    static bool Preview(JsonElement args) => !args.TryGetProperty("preview", out var p) || p.GetBoolean();
    static bool Force(JsonElement args) => args.TryGetProperty("force", out var f) && f.GetBoolean();
    static void NewBytes(string path, byte[] bytes)
    {
        using (var file = new FileStream(EditorFiles.LocalPath(path), FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(bytes); file.Flush(true); }
        if (Layout.Hash(EditorFiles.LocalPath(path)) != CheckpointDocument.Hash(bytes)) throw new IOException("Exclusive new-file readback mismatch: " + path);
    }
    internal static void SelfTest()
    {
        var directory = Path.Combine(Path.GetTempPath(), "aom-install-fixture-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(directory, "game", "ai"); Directory.CreateDirectory(root);
        var exe = Path.Combine(directory, "fake.exe"); var source = Path.Combine(directory, "source.xs");
        var managed = Path.Combine(root, "aom_mcp"); var destination = Path.Combine(managed, "fixture.xs");
        static JsonElement Json(object o) => JsonSerializer.SerializeToElement(o);
        static void Refuse(Action action)
        {
            try { action(); } catch (Exception e) when (e is ArgumentException or InvalidDataException or IOException or UnauthorizedAccessException) { return; }
            throw new InvalidOperationException("Unsafe AI install accepted.");
        }
        try
        {
            File.WriteAllText(source, "// fixture\ninclude \"common.xs\";\nvoid main() {}\n"); File.WriteAllText(Path.Combine(root, "common.xs"), "// common fixture\n");
            var args = new Dictionary<string, object> { ["sourcePath"] = source, ["expectedSha256"] = Layout.Hash(source), ["destination"] = "fixture.xs",
                ["stagingPath"] = Path.Combine(directory, "stage-one.xs"), ["receiptPath"] = Path.Combine(managed, ".receipts", "one.json") };
            var preview = Json(Execute(Json(args), exe));
            if (!preview.GetProperty("preview").GetBoolean() || Directory.Exists(managed) || File.Exists((string)args["stagingPath"])) throw new InvalidOperationException("AI preview wrote files.");
            Refuse(() => Execute(Json(new Dictionary<string, object>(args) { ["preview"] = false }), exe));
            args["preview"] = false; args["confirmWrite"] = true; var first = Json(Execute(Json(args), exe));
            if (Layout.Hash(destination) != (string)args["expectedSha256"] || !File.Exists((string)args["receiptPath"])) throw new InvalidOperationException("New AI/receipt readback failed.");
            var old = Layout.Hash(destination); var oldReceipt = (string)args["receiptPath"];
            File.WriteAllText(source, "// updated fixture\ninclude \"common.xs\";\nvoid main() {}\n");
            var update = new Dictionary<string, object>(args) { ["expectedSha256"] = Layout.Hash(source), ["stagingPath"] = Path.Combine(directory, "stage-two.xs"),
                ["receiptPath"] = Path.Combine(managed, ".receipts", "two.json"), ["ownershipReceiptPath"] = oldReceipt,
                ["expectedReceiptSha256"] = Layout.Hash(oldReceipt), ["expectedInstalledSha256"] = old, ["backupPath"] = Path.Combine(directory, "backup-two.xs") };
            Refuse(() => Execute(Json(update), exe));
            update["confirmDestructive"] = true;
            Refuse(() => Execute(Json(new Dictionary<string, object>(update) { ["expectedInstalledSha256"] = new string('0', 64) }), exe));
            Refuse(() => Execute(Json(new Dictionary<string, object>(update) { ["expectedReceiptSha256"] = new string('0', 64) }), exe));
            var second = Json(Execute(Json(update), exe));
            if (Layout.Hash((string)update["backupPath"]) != old || Layout.Hash(destination) != Layout.Hash(source) || !File.Exists(oldReceipt))
                throw new InvalidOperationException("Managed update lost backup/receipt or failed byte verification.");
            var third = new Dictionary<string, object>(update) { ["stagingPath"] = Path.Combine(directory, "stage-three.xs"), ["backupPath"] = Path.Combine(directory, "backup-three.xs"),
                ["receiptPath"] = Path.Combine(managed, ".receipts", "three.json"), ["ownershipReceiptPath"] = (string)update["receiptPath"],
                ["expectedReceiptSha256"] = Layout.Hash((string)update["receiptPath"]), ["expectedInstalledSha256"] = Layout.Hash(destination) };
            File.SetAttributes(destination, FileAttributes.ReadOnly);
            try { Refuse(() => Execute(Json(third), exe)); if (File.Exists((string)third["stagingPath"])) throw new InvalidOperationException("Permission refusal occurred after staging."); }
            finally { File.SetAttributes(destination, FileAttributes.Normal); }
            try
            {
                Execute(Json(third), exe, phase => { if (phase == "backed-up") throw new IOException("Injected backup-phase stop."); });
                throw new InvalidOperationException("Injected backup stop ignored.");
            }
            catch (WorkflowFailure e)
            {
                if (!e.OutcomeUnknown || Layout.Hash((string)third["backupPath"]) != Layout.Hash(destination) || Layout.Hash(destination) != Layout.Hash(source))
                    throw new InvalidOperationException("Backup-phase stop changed destination or lost verified backup.");
            }
            File.WriteAllText(Path.Combine(managed, "unregistered.xs"), "// not owned\n");
            Refuse(() => Execute(Json(new Dictionary<string, object>(args) { ["destination"] = "unregistered.xs", ["expectedSha256"] = Layout.Hash(source) }), exe));
            var unregistered = Path.Combine(managed, "unregistered.xs"); var unregisteredHash = Layout.Hash(unregistered);
            var forced = new Dictionary<string, object>(args) { ["destination"] = "unregistered.xs", ["expectedSha256"] = Layout.Hash(source), ["force"] = true,
                ["expectedInstalledSha256"] = unregisteredHash, ["stagingPath"] = Path.Combine(directory, "stage-forced.xs"),
                ["backupPath"] = Path.Combine(directory, "backup-forced.xs"), ["receiptPath"] = Path.Combine(managed, ".receipts", "forced.json") };
            Refuse(() => Execute(Json(forced), exe));
            forced["confirmDestructive"] = true;
            Refuse(() => Execute(Json(new Dictionary<string, object>(forced) { ["expectedInstalledSha256"] = new string('0', 64) }), exe));
            Refuse(() => Execute(Json(new Dictionary<string, object>(forced) { ["ownershipReceiptPath"] = oldReceipt, ["expectedReceiptSha256"] = Layout.Hash(oldReceipt) }), exe));
            Refuse(() => Execute(Json(new Dictionary<string, object>(forced) { ["destination"] = "absent.xs" }), exe));
            _ = Execute(Json(forced), exe);
            if (Layout.Hash((string)forced["backupPath"]) != unregisteredHash || Layout.Hash(unregistered) != Layout.Hash(source)
                || JsonSerializer.Deserialize<Receipt>(File.ReadAllBytes((string)forced["receiptPath"])) is not { Forced: true } forcedReceipt || forcedReceipt.PreviousSha256 != unregisteredHash)
                throw new InvalidOperationException("Forced update lost backup, bytes or forced receipt.");
            foreach (var unsafeName in new[] { "../stock.xs", "\\\\host\\file.xs", "C:\\stock.xs", "folder//name.xs", ".receipts/name.xs" })
                Refuse(() => Execute(Json(new Dictionary<string, object>(args) { ["destination"] = unsafeName }), exe));
            Refuse(() => Includes(root, destination, Encoding.UTF8.GetBytes("include \"missing.xs\";\n")));
            Refuse(() => Includes(root, destination, Encoding.UTF8.GetBytes("include \"aom_mcp/fixture.xs\";\n")));
            File.WriteAllText(Path.Combine(root, "cycle-a.xs"), "include \"cycle-b.xs\";\n"); File.WriteAllText(Path.Combine(root, "cycle-b.xs"), "include \"cycle-a.xs\";\n");
            Refuse(() => Includes(root, destination, Encoding.UTF8.GetBytes("include \"cycle-a.xs\";\n")));
            var partial = new Dictionary<string, object>(args) { ["destination"] = "partial.xs", ["expectedSha256"] = Layout.Hash(source),
                ["stagingPath"] = Path.Combine(directory, "stage-partial.xs"), ["receiptPath"] = Path.Combine(managed, ".receipts", "partial.json") };
            try
            {
                Execute(Json(partial), exe, phase => { if (phase == "partial-destination") throw new IOException("Injected partial writer failure."); });
                throw new InvalidOperationException("Partial install failure did not surface.");
            }
            catch (WorkflowFailure e)
            {
                if (!e.OutcomeUnknown || e.NativeDispatched || e.RetainedPaths.Length < 3 || Json(e.Details).GetProperty("retrySafe").GetBoolean()
                    || !File.Exists((string)partial["stagingPath"]) || !File.Exists(Path.Combine(managed, "partial.xs")) || File.Exists((string)partial["receiptPath"]))
                    throw new InvalidOperationException("Partial install paths/outcome not retained accurately.");
            }
            var brokenReceipt = new Dictionary<string, object>(partial) { ["destination"] = "receipt-failure.xs", ["stagingPath"] = Path.Combine(directory, "stage-receipt.xs"),
                ["receiptPath"] = Path.Combine(managed, ".receipts", "broken.json") };
            try
            {
                Execute(Json(brokenReceipt), exe, phase => { if (phase == "receipt-written") File.WriteAllText((string)brokenReceipt["receiptPath"], "{"); });
                throw new InvalidOperationException("Truncated receipt readback accepted.");
            }
            catch (WorkflowFailure e)
            {
                if (!e.OutcomeUnknown || e.Phase != "receipt" || !File.Exists((string)brokenReceipt["receiptPath"]) || Layout.Hash(Path.Combine(managed, "receipt-failure.xs")) != Layout.Hash(source))
                    throw new InvalidOperationException("Receipt failure lost installed/readback provenance.");
            }
            var external = Path.Combine(directory, "external"); Directory.CreateDirectory(external);
            try { Directory.CreateSymbolicLink(Path.Combine(managed, "link"), external); }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            { Console.WriteLine("SKIP AI reparse fixture: link creation unavailable; no elevation attempted."); }
            if (Directory.Exists(Path.Combine(managed, "link"))) Refuse(() => Execute(Json(new Dictionary<string, object>(args) { ["destination"] = "link/escape.xs" }), exe));
            if (first.GetProperty("runtimeVerified").GetBoolean() || second.GetProperty("compilationVerified").GetBoolean()) throw new InvalidOperationException("AI installation claimed runtime/compilation proof.");
        }
        finally { Directory.Delete(directory, true); } // Only this self-test's synthetic fake installation; production retains all artifacts.
    }

    internal static object Execute(JsonElement args, string exe, Action<string>? testFault = null)
    {
        Preflight(args); var root = AiScripts.InstalledRoot(exe); if (!Directory.Exists(root)) throw new ArgumentException("Installed AI root missing.");
        var managed = AiScripts.Contained(root, "aom_mcp"); var relative = AiScripts.Relative(args.GetProperty("destination").GetString()!);
        if (relative.Split('\\').Any(s => s.StartsWith('.'))) throw new ArgumentException("Reserved managed destination component.");
        var destination = AiScripts.Contained(managed, relative); var sourcePath = EditorFiles.LocalPath(args.GetProperty("sourcePath").GetString()!);
        if (!sourcePath.EndsWith(".xs", StringComparison.OrdinalIgnoreCase) || sourcePath.Equals(destination, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Source requires distinct .xs path.");
        var source = ReadXs(sourcePath); var sha = CheckpointDocument.Hash(source); CheckpointDocument.RequireHash(sha, args.GetProperty("expectedSha256").GetString());
        var dependencies = Includes(root, destination, source); var update = File.Exists(destination);
        if (Directory.Exists(destination)) throw new ArgumentException("Destination is directory.");
        string? oldHash = null, oldReceiptPath = null;
        var force = Force(args);
        if (update && force)
        {
            // Explicit override for managed files without a matching receipt; the current bytes stay hash-pinned and backed up.
            if (args.TryGetProperty("ownershipReceiptPath", out _) || args.TryGetProperty("expectedReceiptSha256", out _))
                throw new ArgumentException("force skips the ownership receipt; omit ownershipReceiptPath/expectedReceiptSha256.");
            _ = RequiredHash(args, "expectedInstalledSha256");
            if (new FileInfo(destination).Length > 1_000_000) throw new InvalidDataException("Installed AI exceeds byte bound.");
            oldHash = Layout.Hash(destination); CheckpointDocument.RequireHash(oldHash, args.GetProperty("expectedInstalledSha256").GetString());
            if ((File.GetAttributes(destination) & FileAttributes.ReadOnly) != 0) throw new UnauthorizedAccessException("Managed AI destination is read-only.");
            if (!Preview(args)) EditorFiles.Confirm(args, "confirmDestructive");
        }
        else if (update)
        {
            if (!args.TryGetProperty("ownershipReceiptPath", out var ownership)) throw new ArgumentException("Existing managed file lacks explicit ownership receipt; no update (force=true overrides).");
            _ = RequiredHash(args, "expectedReceiptSha256"); _ = RequiredHash(args, "expectedInstalledSha256");
            if (new FileInfo(destination).Length > 1_000_000) throw new InvalidDataException("Installed AI exceeds byte bound.");
            oldReceiptPath = EditorFiles.LocalPath(ownership.GetString()!);
            if (!Under(oldReceiptPath, Path.Combine(managed, ".receipts")) || new FileInfo(oldReceiptPath).Length > 100_000) throw new ArgumentException("Ownership receipt must be registered within managed .receipts.");
            CheckpointDocument.RequireHash(Layout.Hash(oldReceiptPath), args.GetProperty("expectedReceiptSha256").GetString());
            var receipt = JsonSerializer.Deserialize<Receipt>(File.ReadAllBytes(oldReceiptPath)) ?? throw new InvalidDataException("Ownership receipt missing.");
            oldHash = Layout.Hash(destination); CheckpointDocument.RequireHash(oldHash, args.GetProperty("expectedInstalledSha256").GetString());
            if (receipt.Issuer != Issuer || !string.Equals(receipt.DestinationPath, destination, StringComparison.OrdinalIgnoreCase) || receipt.InstalledSha256 != oldHash)
                throw new ArgumentException("Unregistered/changed personality; no update (force=true overrides).");
            if ((File.GetAttributes(destination) & FileAttributes.ReadOnly) != 0) throw new UnauthorizedAccessException("Managed AI destination is read-only.");
            if (!Preview(args)) EditorFiles.Confirm(args, "confirmDestructive");
        }
        else if (force || args.TryGetProperty("ownershipReceiptPath", out _) || args.TryGetProperty("expectedInstalledSha256", out _)) throw new ArgumentException("Update assertions supplied for absent destination.");
        string? staging = null, backup = null, receiptPath = null;
        if (args.TryGetProperty("stagingPath", out var stage)) staging = EditorFiles.ApprovedNewPath(stage.GetString()!, ".xs");
        if (args.TryGetProperty("backupPath", out var bp)) backup = EditorFiles.ApprovedNewPath(bp.GetString()!, ".xs");
        if (args.TryGetProperty("receiptPath", out var rp))
        {
            receiptPath = EditorFiles.LocalPath(rp.GetString()!);
            if (!Under(receiptPath, Path.Combine(managed, ".receipts")) || !receiptPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || File.Exists(receiptPath) || Directory.Exists(receiptPath))
                throw new ArgumentException("New receipt must be exclusive .json under managed .receipts.");
        }
        if (staging is not null && Under(staging, root) || backup is not null && Under(backup, root)) throw new ArgumentException("Staging/backups must be outside installed AI root.");
        var paths = new[] { sourcePath, destination, staging, backup, receiptPath }.Where(p => p is not null).Cast<string>().ToArray();
        if (paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length) throw new ArgumentException("Installer paths must be distinct.");
        if (Preview(args)) return new { preview = true, sourcePath, sourceSha256 = sha, destinationPath = destination, aiPath = "aom_mcp\\" + relative,
            update, force, expectedInstalledSha256 = oldHash, stagingPath = staging, backupPath = backup, receiptPath, includes = dependencies,
            permissions = "No permission-probing writes performed; OS access checked during confirmed apply.", compilationVerified = false, runtimeVerified = false };
        if (staging is null || receiptPath is null || update && backup is null) throw new ArgumentException("Apply requires stagingPath/receiptPath; update also requires new backupPath.");
        var phase = "stage"; var writesStarted = false;
        try
        {
            writesStarted = true; NewBytes(staging, source); testFault?.Invoke("staged");
            foreach (var dependency in dependencies) CheckpointDocument.RequireHash(Layout.Hash(EditorFiles.LocalPath(dependency.Path)), dependency.Sha256);
            CheckpointDocument.RequireHash(Layout.Hash(sourcePath), sha);
            _ = EditorFiles.LocalPath(destination); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            phase = "destination";
            if (update)
            {
                if (oldReceiptPath is not null) CheckpointDocument.RequireHash(Layout.Hash(oldReceiptPath), args.GetProperty("expectedReceiptSha256").GetString());
                using var file = new FileStream(EditorFiles.LocalPath(destination), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                if (file.Length > 1_000_000) throw new InvalidDataException("Installed AI changed beyond byte bound.");
                var old = new byte[(int)file.Length]; file.ReadExactly(old); CheckpointDocument.RequireHash(CheckpointDocument.Hash(old), oldHash);
                phase = "backup"; NewBytes(backup!, old); testFault?.Invoke("backed-up");
                phase = "destination"; file.Position = 0; file.Write(source.AsSpan(0, source.Length / 2)); testFault?.Invoke("partial-destination");
                file.Write(source.AsSpan(source.Length / 2)); file.SetLength(source.Length); file.Flush(true);
                file.Position = 0; var readback = new byte[source.Length]; file.ReadExactly(readback);
                if (!source.AsSpan().SequenceEqual(readback)) throw new IOException("Locked destination readback mismatch.");
            }
            else
            {
                using var file = new FileStream(EditorFiles.LocalPath(destination), FileMode.CreateNew, FileAccess.Write, FileShare.None);
                file.Write(source.AsSpan(0, source.Length / 2)); testFault?.Invoke("partial-destination"); file.Write(source.AsSpan(source.Length / 2)); file.Flush(true);
            }
            CheckpointDocument.RequireHash(Layout.Hash(EditorFiles.LocalPath(destination)), sha); testFault?.Invoke("destination-written");
            phase = "receipt"; _ = EditorFiles.LocalPath(receiptPath); Directory.CreateDirectory(Path.GetDirectoryName(receiptPath)!);
            var receipt = new Receipt(Issuer, destination, sha, oldHash, staging, backup, DateTimeOffset.UtcNow, update && force);
            NewBytes(receiptPath, JsonSerializer.SerializeToUtf8Bytes(receipt)); testFault?.Invoke("receipt-written");
            var verified = JsonSerializer.Deserialize<Receipt>(File.ReadAllBytes(receiptPath));
            if (verified != receipt) throw new IOException("Receipt semantic readback mismatch.");
            return new { preview = false, destinationPath = destination, sha256 = sha, aiPath = "aom_mcp\\" + relative, update, stagingPath = staging, backupPath = backup,
                receiptPath, receiptSha256 = Layout.Hash(receiptPath), includes = dependencies, compilationVerified = false, runtimeVerified = false,
                nextAction = "Bind managed AI separately with guarded player settings. Confirm compilation and fresh runtime evidence; no automatic apply/playtest." };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException or JsonException)
        {
            throw new WorkflowFailure("AI_INSTALL_OUTCOME_UNKNOWN", phase, e.Message, false, writesStarted,
                "Stop. Inspect retained staging, backup, destination and receipt. Do not retry, clean up, or roll back automatically.", staging, destination, e,
                [staging, destination, receiptPath, .. (backup is null ? Array.Empty<string>() : new[] { backup })]);
        }
    }
}
