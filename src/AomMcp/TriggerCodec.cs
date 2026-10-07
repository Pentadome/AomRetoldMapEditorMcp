using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AomMcp;

/// <summary>Strict, lossless codec for the independently round-tripped TR controller shape.
/// Other trigger shapes refuse editing rather than guessing serialized parameter types.</summary>
internal static class TriggerCodec
{
    // Recovered game-exported TR v12 Always/CodeSnippet controller, research/defense-code-sample.trg.
    // Header: ASCII TR, payload length excluding 10-byte header, version12. UTF16 name length at42.
    const int HeaderSize = 10, SizeOffset = 2, VersionOffset = 6, NameLengthOffset = 42, NameStart = 46;
    // Native UTF16 code after parameter type72/version1; empty reference template code length at248.
    const int TemplateNameChars = 21, TemplateCodeLengthOffset = 248, CodeStartDelta = 4;
    // Host file/message safety ceilings, not engine maxima. Two bytes per UTF16 code unit.
    const int MaxBytes = 2_000_000, MaxCodeChars = 250000, MaxNameChars = 128, Utf16Size = 2;
    static readonly UnicodeEncoding Utf16 = new(false, false, true);
    internal sealed record Controller(string Name, bool Active, bool Loop, string Code);

    static byte[] Template()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "trigger-controller-template.trg");
        var bytes = File.ReadAllBytes(path);
        // Pin fixture itself; corruption/substitution must not silently redefine accepted serialization.
        if (Layout.Hash(path) != "78fc459576ae5ca4dc2d081a58eadbceb264aef400a45784f90cdce6686a4f49") throw new InvalidDataException("Trigger template changed; review serialization before editing.");
        return bytes;
    }

    internal static Controller Parse(byte[] bytes)
    {
        try { return ParseController(bytes); }
        catch (InvalidDataException e) when (ShapeHint(bytes) is { } hint) { throw new InvalidDataException(e.Message + " " + hint, e); }
    }

    /// <summary>Number of triggers when bytes are a general TR export (campaign codec), else null.</summary>
    internal static int? GeneralTriggerCount(byte[] bytes)
    {
        try { return CampaignTriggers.Parse(bytes).Triggers.Length; }
        catch (Exception e) when (e is InvalidDataException or ArgumentException or OverflowException) { return null; }
    }

    static string? ShapeHint(byte[] bytes) => GeneralTriggerCount(bytes) switch
    {
        0 => "File is a valid TR export with 0 triggers (empty trigger set); this tool only handles the single Always/CodeSnippet controller shape. Use editor_trigger_list for general exports.",
        1 => null, // Same shape family; original message is specific enough.
        { } n => $"File is a TR export with {n} triggers, not the single verified controller shape. Use editor_trigger_list / editor_trigger_edit.",
        null => null,
    };

    static Controller ParseController(byte[] bytes)
    {
        var template = Template();
        if (bytes.Length is < NameStart or > MaxBytes || bytes[0] != 'T' || bytes[1] != 'R'
            || BitConverter.ToInt32(bytes,SizeOffset) != bytes.Length - HeaderSize
            || BitConverter.ToInt32(bytes,VersionOffset) != 12) // Recovered serializer version, not a guessed universal TR ABI.
            throw new InvalidDataException("Unrecognized TR header/version/size; no edit/import attempted.");
        var nameLength = BitConverter.ToInt32(bytes,NameLengthOffset);
        if (nameLength is < 1 or > MaxNameChars || NameStart + nameLength * Utf16Size > bytes.Length)
            throw new InvalidDataException("Invalid trigger name bounds.");
        var name = Utf16.GetString(bytes,NameStart,nameLength * Utf16Size);
        var nameEnd = NameStart + nameLength * Utf16Size;
        // Active/loop are bytes immediately after native -1 sentinel; fixture-derived relative fields.
        if (nameEnd + 6 > bytes.Length) // Four-byte native sentinel plus two single-byte flags.
            throw new InvalidDataException("Truncated trigger flags.");
        var loop = bytes[nameEnd + 4]; var active = bytes[nameEnd + 5];
        if (loop > 1 || active > 1) throw new InvalidDataException("Invalid trigger flags.");
        var codeLengthOffset = TemplateCodeLengthOffset + (nameLength - TemplateNameChars) * Utf16Size;
        if (codeLengthOffset < 0 || codeLengthOffset + CodeStartDelta > bytes.Length) throw new InvalidDataException("Unsupported trigger record shape.");
        var codeChars = BitConverter.ToInt32(bytes,codeLengthOffset);
        if (codeChars is < 0 or > MaxCodeChars || codeLengthOffset + CodeStartDelta + codeChars * Utf16Size > bytes.Length)
            throw new InvalidDataException("Invalid trigger code bounds.");
        var code = Utf16.GetString(bytes,codeLengthOffset + CodeStartDelta,codeChars * Utf16Size);
        var result = new Controller(name,active != 0,loop != 0,code);
        if (!Serialize(result,template).AsSpan().SequenceEqual(bytes))
            throw new InvalidDataException("Unsupported TR shape. Only independently verified single Always/CodeSnippet controller edits supported; other shapes need codec review.");
        return result;
    }

    internal static byte[] Serialize(Controller controller, byte[]? template = null)
    {
        template ??= Template();
        if (controller.Name.Length is < 1 or > MaxNameChars || controller.Name.Any(char.IsControl)
            || controller.Code.Length > MaxCodeChars || controller.Code.Contains('\0'))
            throw new ArgumentException("Trigger name/code outside host bounds.");
        var name = Utf16.GetBytes(controller.Name);
        var code = Utf16.GetBytes(controller.Code);
        var templateNameEnd = NameStart + TemplateNameChars * Utf16Size;
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory,Encoding.UTF8,true);
        writer.Write(template,0,NameLengthOffset);
        writer.Write(controller.Name.Length); writer.Write(name);
        var body = template[templateNameEnd..TemplateCodeLengthOffset];
        body[4] = controller.Loop ? (byte)1 : (byte)0; // Relative loop flag, native -1 sentinel occupies first four bytes.
        body[5] = controller.Active ? (byte)1 : (byte)0; // Following byte is native active flag.
        writer.Write(body); writer.Write(controller.Code.Length); writer.Write(code);
        writer.Write(template,TemplateCodeLengthOffset + CodeStartDelta,template.Length - TemplateCodeLengthOffset - CodeStartDelta);
        var bytes = memory.ToArray();
        BitConverter.GetBytes(bytes.Length - HeaderSize).CopyTo(bytes,SizeOffset);
        return bytes;
    }

    internal static byte[] ReadFile(string path)
    {
        path = EditorFiles.LocalPath(path);
        if (!path.EndsWith(".trg",StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Expected bounded .trg file.");
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > MaxBytes) throw new ArgumentException("Expected bounded .trg file.");
        var bytes = new byte[checked((int)file.Length)];
        file.ReadExactly(bytes);
        return bytes;
    }

    internal static object Inspect(string path)
    {
        var bytes = ReadFile(path); var c = Parse(bytes);
        return new
        {
            path = Path.GetFullPath(path), sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            format = "TR v12 single verified controller", serializationRoundTripVerified = true,
            triggers = new[] { new { c.Name, c.Active, c.Loop,
                conditions = new[] { new { type = "Always" } },
                effects = new[] { new { type = "XS: Code Snippet", parameters = new { codeSnippet = c.Code } } },
            } },
            findings = Findings(c),
            limitation = "Lossless codec for verified controller shape. Not arbitrary legacy TR shapes, XS compiler validation or proof of trigger effects. Template expansion can change code before compilation.",
        };
    }

    internal static string[] Findings(Controller c)
    {
        var findings = new List<string>();
        if (!c.Active) findings.Add("Controller starts inactive.");
        if (string.IsNullOrWhiteSpace(c.Code)) findings.Add("Code Snippet empty.");
        if (c.Code.Contains('%')) findings.Add("Percent sign may be consumed by trigger-template expansion; this previously broke modulo expressions.");
        return findings.ToArray();
    }

    internal static object Patch(JsonElement args)
    {
        var source = args.GetProperty("path").GetString()!;
        var original = ReadFile(source);
        var current = Parse(original);
        var updated = new Controller(
            args.TryGetProperty("name",out var name) ? name.GetString()! : current.Name,
            args.TryGetProperty("active",out var active) ? active.GetBoolean() : current.Active,
            args.TryGetProperty("loop",out var loop) ? loop.GetBoolean() : current.Loop,
            args.TryGetProperty("code",out var code) ? code.GetString()!.Replace("\r\n","\n",StringComparison.Ordinal).Replace("\n","\r\n",StringComparison.Ordinal) : current.Code);
        var output = EditorFiles.ApprovedNewPath(args.GetProperty("outputPath").GetString()!, ".trg");
        if (!output.EndsWith(".trg",StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetFullPath(source),output,StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Trigger patch requires distinct new .trg outputPath; source never overwritten.");
        var bytes = Serialize(updated);
        if (Parse(bytes) != updated) throw new InvalidDataException("Trigger serialization round-trip failed; output not written.");
        using (var file = new FileStream(output,FileMode.CreateNew,FileAccess.Write,FileShare.None)) file.Write(bytes);
        return Inspect(output);
    }

    /// <summary>Checks published game-writer template and synthetic flag/name/code edits, no game calls.</summary>
    public static void SelfTest()
    {
        // Synthetic controller includes Unicode/surrogate pairs and a changed name; asserts UTF16 lengths.
        var value = new Controller("fixture_α",true,true,"trChatSend(1, \"fixture 😀\");\r\n");
        var bytes = Serialize(value);
        if (Parse(bytes) != value) throw new InvalidOperationException("TR codec fixture failed.");
        bytes[VersionOffset] = 99; // Deliberately unsupported fixture version; edits must refuse.
        try { Parse(bytes); } catch (InvalidDataException) { return; }
        throw new InvalidOperationException("TR unknown-version fixture did not refuse.");
    }
}
