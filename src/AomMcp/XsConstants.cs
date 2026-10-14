using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AomMcp;

/// <summary>
/// Local-only list of XS-style constant NAMES found as identifier strings in the game executable (never values, never shipped).
/// A name appearing here is not proof that XS code can reference it; see the per-entry status.
/// </summary>
internal static partial class XsConstants
{
    internal const string FileName = "xs_constants.json";

    // Host heuristics, not game rules: identifier shape, minimum prefix family size and word length.
    const int MinFamily = 3,
        MinFamilyWord = 3,
        MaxName = 64;

    internal sealed record Family(string Prefix, int Count);

    internal sealed record Document(
        string GameFileVersion,
        string ExeSha256,
        string Status,
        string Note,
        Family[] Families,
        string[] Names,
        string[]? SeenInShippedXs = null
    );

    [GeneratedRegex(@"^c[A-Z][A-Za-z0-9]{2,63}$")]
    private static partial Regex Shape();

    [GeneratedRegex(@"^(c[A-Z][a-z0-9]*)")]
    private static partial Regex Prefix();

    /// <summary>NUL/non-printable delimited ASCII runs in the image that look like constant identifiers.</summary>
    internal static string[] Scan(byte[] image)
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        var start = -1;
        for (var i = 0; i <= image.Length; i++)
        {
            var printable = i < image.Length && image[i] is >= 0x20 and <= 0x7e;
            if (printable)
            {
                if (start < 0)
                    start = i;
                continue;
            }
            if (start >= 0 && i - start is >= 3 and <= MaxName + 1)
            {
                var text = Encoding.ASCII.GetString(image, start, i - start);
                if (Shape().IsMatch(text))
                    found.Add(text);
            }
            start = -1;
        }
        return [.. found];
    }

    /// <summary>Keeps prefix families of at least 3 names whose family word has 3+ characters, minus names the shipped API already knows.</summary>
    internal static (string[] Names, Family[] Families) Select(
        IEnumerable<string> scanned,
        ISet<string> known
    )
    {
        var candidates = scanned.Where(n => !known.Contains(n)).ToArray();
        string? Family(string name) =>
            Prefix().Match(name) is { Success: true } m
            && m.Groups[1].Value.Length - 1 >= MinFamilyWord
                ? m.Groups[1].Value
                : null;
        var counts = candidates
            .Select(Family)
            .Where(f => f is not null)
            .GroupBy(f => f!, StringComparer.Ordinal)
            .Where(g => g.Count() >= MinFamily)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var names = candidates.Where(n => Family(n) is { } f && counts.ContainsKey(f)).ToArray();
        return (
            names,
            counts
                .OrderByDescending(c => c.Value)
                .ThenBy(c => c.Key, StringComparer.Ordinal)
                .Select(c => new Family(c.Key, c.Value))
                .ToArray()
        );
    }

    /// <summary>Names of the scanned constants that appear as identifiers in the installed game's own .xs scripts (read-only evidence).</summary>
    static string[] ShippedTokens(string exe, string[] names)
    {
        var game = Path.Combine(Path.GetDirectoryName(exe)!, "game");
        var wanted = new HashSet<string>(names, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sub in new[] { "ai", "random_maps" })
        {
            var root = Path.Combine(game, sub);
            if (!Directory.Exists(root))
                continue;
            foreach (
                var file in Directory.EnumerateFiles(root, "*.xs", SearchOption.AllDirectories)
            )
            {
                if (new FileInfo(file).Length > 4_000_000)
                    continue;
                foreach (
                    System.Text.RegularExpressions.Match m in Token()
                        .Matches(File.ReadAllText(file))
                )
                    if (wanted.Contains(m.Value))
                        seen.Add(m.Value);
            }
        }
        return seen.Order(StringComparer.Ordinal).ToArray();
    }

    [GeneratedRegex(@"\bc[A-Z][A-Za-z0-9]{2,63}\b")]
    private static partial Regex Token();

    internal static string Generate(string exe, string output)
    {
        var known = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var model = XsApi.LoadModel();
            foreach (
                var n in model
                    .Syscalls.Select(s => s.Name)
                    .Concat(model.Functions.Select(f => f.Name))
                    .Concat(model.Globals.Select(g => g.Name))
                    .Concat(model.Classes.Select(c => c.Name))
                    .Concat(model.AiPlanConstants.Select(p => p.Name))
            )
                known.Add(n);
        }
        catch (FileNotFoundException) { } // Shipped API missing: keep every scanned name.
        var (names, families) = Select(Scan(new PeImage(exe).Bytes), known);
        var seen = ShippedTokens(exe, names);
        var document = new Document(
            System.Diagnostics.FileVersionInfo.GetVersionInfo(exe).FileVersion ?? "",
            Layout.Hash(exe),
            "unverified",
            "Identifier strings found in the game executable (names only, no values). Not verified as visible to XS code until a live probe confirms them.",
            families,
            names,
            seen
        );
        var path = Path.Combine(output, FileName);
        File.WriteAllText(path, JsonSerializer.Serialize(document, XsApi.Json));
        return $"XS constants: {names.Length} names in {families.Length} families -> {path} (local only, not shipped).";
    }

    internal static Document? Load(string output)
    {
        var path = Path.Combine(output, FileName);
        return File.Exists(path)
            ? JsonSerializer.Deserialize<Document>(File.ReadAllText(path), XsApi.Json)
            : null;
    }

    internal static void SelfTest()
    {
        var image = Encoding.ASCII.GetBytes(
            "\0cModifyTypeA\0cModifyTypeB\0cModifyTypeC\0cXAB\0cXCD\0cXEF\0cPlanFoo\0cPlanBar\0cOnce\0noise\0cModifyKnown\0cModifyTypeA\0"
        );
        var scanned = Scan(image);
        var (names, families) = Select(scanned, new HashSet<string>(["cModifyKnown"]));
        if (
            scanned.Length != 10
            || !names.SequenceEqual(["cModifyTypeA", "cModifyTypeB", "cModifyTypeC"])
            || families.Single() != new Family("cModify", 3)
        )
            throw new InvalidOperationException(
                "XS constants scan fixture failed: "
                    + string.Join(",", scanned)
                    + " | "
                    + string.Join(",", names)
            );
    }
}
