using System.Security.Cryptography;
using System.Text;

namespace AomMcp;

/// <summary>Read-only source-location evidence for computer-player AI personalities.</summary>
internal static class AiScripts
{
    /// <summary>Stock AI scripts shipped with the game; read-only include/personality fallback, never an install target.</summary>
    internal static string InstalledRoot(string exe) => EditorFiles.LocalPath(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(exe)!, "game", "ai")));
    /// <summary>Custom personalities: active profile Games\Age of Mythology Retold\&lt;id&gt;\Game\AI.</summary>
    internal static string ProfileRoot()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Games", "Age of Mythology Retold");
        var candidates = Directory.Exists(root) ? Directory.GetDirectories(root).Select(d => Path.Combine(d, "Game", "AI")).Where(Directory.Exists).ToArray() : [];
        if (candidates.Length != 1)
            throw new InvalidDataException("Cannot identify one active profile Game\\AI directory (Games\\Age of Mythology Retold\\<id>\\Game\\AI); found " + candidates.Length + ".");
        return EditorFiles.LocalPath(Path.GetFullPath(candidates[0]));
    }
    internal static string? SafeProfileRoot()
    {
        try { return ProfileRoot(); } catch (Exception e) when (e is ArgumentException or InvalidDataException or IOException) { return null; }
    }
    /// <summary>Lookup order: active profile Game\AI first, then stock INSTALLPATH\game\ai.</summary>
    internal static string[] SearchRoots(string exe) => SafeProfileRoot() is { } profile ? [profile, InstalledRoot(exe)] : [InstalledRoot(exe)];
    internal static string? Find(string[] roots, string relative) =>
        roots.Select(r => Contained(r, relative)).FirstOrDefault(File.Exists);
    internal static string Relative(string relative, bool appendExtension = false)
    {
        var name = relative.Replace('/', '\\');
        if (string.IsNullOrWhiteSpace(name) || Path.IsPathRooted(name) || name.Contains(':') || name.Split('\\').Any(s => s is "" or "." or ".."))
            throw new ArgumentException("AI path must be safe relative name under installed AI root.");
        if (appendExtension && !name.EndsWith(".xs", StringComparison.OrdinalIgnoreCase)) name += ".xs";
        if (!name.EndsWith(".xs", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("AI path requires .xs extension.");
        return name;
    }
    internal static string Contained(string root, string relative)
    {
        var path = EditorFiles.LocalPath(Path.GetFullPath(Path.Combine(root, relative)));
        if (!path.StartsWith(root.TrimEnd('\\') + '\\', StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("AI path escaped installed root.");
        return path;
    }
    internal static object Resolve(string exe, string relative)
    {
        var roots = SearchRoots(exe); var root = roots[0];
        if (string.IsNullOrWhiteSpace(relative))
            return new { aiPath = relative, installedAiRoot = root, searchRoots = roots, resolved = false,
                reason = "Player has no computer-player AI personality set.", waveStrategy = false,
                startAttacking = false, includeChecks = Array.Empty<object>() };
        var name = Relative(relative, appendExtension: true);
        if (Find(roots, name) is not { } path)
            return new { aiPath = relative, installedAiRoot = root, searchRoots = roots, resolved = false,
                reason = "Personality not found in active-profile Games\\Age of Mythology Retold\\<id>\\Game\\AI (custom) or INSTALLPATH\\game\\ai (stock). Profile <id>\\ai (without Game) is not used; triggers go in active-profile trigger directory.",
                waveStrategy = false, startAttacking = false, includeChecks = Array.Empty<object>() };
        var info = new FileInfo(path);
        if (info.Length > 1_000_000) throw new InvalidDataException("XS file exceeds read-only host bound.");
        var bytes = File.ReadAllBytes(path);
        string text; using (var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8)) text = reader.ReadToEnd();
        var checks = new List<object>();
        foreach (var line in text.Split('\n').Where(l => l.TrimStart().StartsWith("include ", StringComparison.Ordinal)).Take(100))
        {
            var first = line.IndexOf('"'); var end = first < 0 ? -1 : line.IndexOf('"', first + 1);
            if (end <= first) { checks.Add(new { include = line.Trim(), resolved = false }); continue; }
            var include = line[(first + 1)..end].Replace('/', '\\');
            var safe = !Path.IsPathRooted(include) && !include.Contains(':')
                && include.Split('\\').All(s => s is not "" and not "." and not "..");
            checks.Add(new { include, resolved = safe && Find(roots, include) is not null });
        }
        return new { aiPath = relative, installedAiRoot = root, searchRoots = roots, resolvedPath = path, resolved = true, reason = "Source present; XS compilation/runtime effect NOT verified.",
            waveStrategy = text.Contains("AttackWave", StringComparison.Ordinal) && text.Contains("scenarioAttackWaveStrategy", StringComparison.Ordinal),
            startAttacking = text.Contains("startAttacking", StringComparison.Ordinal), includeChecks = checks.ToArray(),
            sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) };
    }
}
