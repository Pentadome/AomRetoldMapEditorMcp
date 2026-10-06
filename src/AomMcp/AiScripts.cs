using System.Security.Cryptography;
using System.Text;

namespace AomMcp;

/// <summary>Read-only source-location evidence for computer-player AI personalities.</summary>
internal static class AiScripts
{
    internal static string InstalledRoot(string exe) => EditorFiles.LocalPath(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(exe)!, "game", "ai")));
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
        var root = InstalledRoot(exe);
        if (string.IsNullOrWhiteSpace(relative))
            return new { aiPath = relative, installedAiRoot = root, resolved = false,
                reason = "Player has no computer-player AI personality set.", waveStrategy = false,
                startAttacking = false, includeChecks = Array.Empty<object>() };
        var name = Relative(relative, appendExtension: true);
        var path = Contained(root, name);
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("AI personality escaped installed game AI root.");
        if (!File.Exists(path))
            return new { aiPath = relative, installedAiRoot = root, resolved = false,
                reason = "Personality not found in INSTALLPATH\\game\\ai. Active-profile Games\\Age of Mythology Retold\\<id>\\ai did NOT work; triggers go in active-profile trigger directory.",
                waveStrategy = false, startAttacking = false, includeChecks = Array.Empty<object>() };
        var info = new FileInfo(path);
        if (info.Length > 1_000_000) throw new InvalidDataException("XS file exceeds read-only host bound.");
        var text = File.ReadAllText(path, Encoding.UTF8);
        var checks = new List<object>();
        foreach (var line in text.Split('\n').Where(l => l.TrimStart().StartsWith("include ", StringComparison.Ordinal)).Take(100))
        {
            var first = line.IndexOf('"'); var end = first < 0 ? -1 : line.IndexOf('"', first + 1);
            if (end <= first) { checks.Add(new { include = line.Trim(), resolved = false }); continue; }
            var include = line[(first + 1)..end].Replace('/', '\\');
            var safe = !Path.IsPathRooted(include) && !include.Contains(':')
                && include.Split('\\').All(s => s is not "" and not "." and not "..");
            var includePath = safe ? Path.GetFullPath(Path.Combine(root, include)) : "";
            checks.Add(new { include, resolved = safe && includePath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && File.Exists(includePath) });
        }
        return new { aiPath = relative, installedAiRoot = root, resolved = true, reason = "Source present; XS compilation/runtime effect NOT verified.",
            waveStrategy = text.Contains("AttackWave", StringComparison.Ordinal) && text.Contains("scenarioAttackWaveStrategy", StringComparison.Ordinal),
            startAttacking = text.Contains("startAttacking", StringComparison.Ordinal), includeChecks = checks.ToArray(),
            sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))) };
    }
}
