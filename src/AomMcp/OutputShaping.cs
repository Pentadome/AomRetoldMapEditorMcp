using System.Globalization;
using ModelContextProtocol.Protocol;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AomMcp;

/// <summary>
/// Output shaping for bulky read tools: `fields` (top-level key projection of the result rows), `saveTo` (write the full result
/// to a new .json file and return only a summary) and a `truncationHint` for large inline results.
/// Both arguments are removed before the tool's own validation, so tools never see them.
/// </summary>
internal sealed partial class Server
{
    const int HintBytes = 16 * 1024, MaxShapeFields = 20;

    // editor_prototypes/editor_technologies already define `fields` (XML child projection); they only gain saveTo and the hint.
    static readonly HashSet<string> ShapedTools = new(StringComparer.Ordinal)
    {
        "editor_trigger_list", "editor_xs_api", "editor_prototypes", "editor_technologies", "editor_units", "editor_players",
        "editor_xs_diagnostics", "editor_trigger_elements",
    };
    static readonly HashSet<string> OwnFields = new(StringComparer.Ordinal) { "editor_prototypes", "editor_technologies" };
    static readonly string[] RowKeys = ["entries", "triggers", "units", "players", "diagnostics"];

    static Tool AddShaping(Tool tool)
    {
        if (!ShapedTools.Contains(tool.Name)) return tool;
        var node = JsonNode.Parse(tool.InputSchema.GetRawText())!.AsObject();
        var properties = node["properties"]!.AsObject();
        properties["saveTo"] = JsonNode.Parse("""{"type":"string","description":"Absolute NEW .json path: write the full result there (no overwrite) and return only {savedTo, sha256, bytes, summary}."}""");
        if (!OwnFields.Contains(tool.Name))
            properties["fields"] = JsonNode.Parse($$"""{"type":"array","items":{"type":"string"},"minItems":1,"maxItems":{{MaxShapeFields}},"description":"Keep only these top-level keys of each result row (entries/triggers/units/players/diagnostics)."}""");
        tool.InputSchema = JsonDocument.Parse(node.ToJsonString()).RootElement.Clone();
        return tool;
    }

    static JsonElement StripShaping(string name, JsonElement args)
    {
        if (!ShapedTools.Contains(name) || args.ValueKind != JsonValueKind.Object) return args;
        var strip = OwnFields.Contains(name) ? new[] { "saveTo" } : ["saveTo", "fields"];
        if (!strip.Any(k => args.TryGetProperty(k, out _))) return args;
        var node = JsonNode.Parse(args.GetRawText())!.AsObject();
        foreach (var key in strip) node.Remove(key);
        return JsonDocument.Parse(node.ToJsonString()).RootElement.Clone();
    }

    static void PreflightShaping(string name, JsonElement args)
    {
        if (!ShapedTools.Contains(name)) return;
        if (!OwnFields.Contains(name) && args.TryGetProperty("fields", out var fields))
        {
            var list = fields.EnumerateArray().Select(f => f.GetString() ?? "").ToArray();
            if (list.Length is < 1 or > MaxShapeFields || list.Any(f => f.Length is 0 or > 64))
                throw new ArgumentException($"fields must be 1..{MaxShapeFields} non-empty key names.");
        }
        if (args.TryGetProperty("saveTo", out var save)) _ = EditorFiles.ApprovedNewPath(save.GetString()!, ".json");
    }

    object Invoke(string name, JsonElement args, Game? batchGame = null)
    {
        if (!ShapedTools.Contains(name)) return InvokeCore(name, args, batchGame);
        var inner = StripShaping(name, args);
        var result = InvokeCore(name, inner, batchGame);
        return ShapeResult(name, args, inner, result);
    }

    static object ShapeResult(string name, JsonElement args, JsonElement inner, object result)
    {
        var hasFields = !OwnFields.Contains(name) && args.TryGetProperty("fields", out _);
        var hasSave = args.TryGetProperty("saveTo", out var save);
        if (result is ImageResult) return result;
        var node = JsonSerializer.SerializeToNode(result, Json);
        if (node is not JsonObject root) return result;
        List<string>? unknown = null;
        if (hasFields)
        {
            var wanted = args.GetProperty("fields").EnumerateArray().Select(f => f.GetString()!).Distinct(StringComparer.Ordinal).ToArray();
            var rows = RowKeys.Select(k => root[k] as JsonArray).FirstOrDefault(a => a is not null);
            if (rows is not null)
            {
                var present = new HashSet<string>(StringComparer.Ordinal);
                for (var i = 0; i < rows.Count; i++)
                    if (rows[i] is JsonObject row)
                    {
                        foreach (var key in row.Select(p => p.Key).ToArray())
                        {
                            if (wanted.Contains(key)) present.Add(key);
                            else row.Remove(key);
                        }
                    }
                unknown = wanted.Where(w => !present.Contains(w)).ToList();
                if (unknown.Count > 0) root["unknownFields"] = JsonSerializer.SerializeToNode(unknown);
            }
        }
        if (hasSave)
        {
            var path = EditorFiles.ApprovedNewPath(save.GetString()!, ".json");
            var bytes = System.Text.Encoding.UTF8.GetBytes(root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) file.Write(bytes);
            var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
            if (Layout.Hash(path) != sha) throw new IOException("saveTo copy verification failed.");
            var summary = new JsonObject();
            foreach (var (key, value) in root)
                if (value is JsonArray array) summary[key + "Count"] = array.Count;
                else if (value is JsonValue) summary[key] = value.DeepClone();
            return new JsonObject { ["savedTo"] = path, ["sha256"] = sha, ["bytes"] = bytes.Length, ["summary"] = summary };
        }
        var size = System.Text.Encoding.UTF8.GetByteCount(root.ToJsonString());
        if (size > HintBytes)
        {
            var hints = new List<string>();
            if (RowKeys.Any(k => root[k] is JsonArray))
                hints.Add($"lower limit (now {(inner.TryGetProperty("limit", out var limit) ? limit.GetInt32().ToString(CultureInfo.InvariantCulture) : "default")})");
            if (!hasFields) hints.Add(OwnFields.Contains(name) ? "pass fields=[child element names] to get values instead of whole entries" : "pass fields=[row keys] to keep only the keys you need");
            hints.Add("pass saveTo=<new absolute .json path> to store the full result and read it with a file tool");
            root["truncationHint"] = new JsonObject { ["bytes"] = size, ["softLimitBytes"] = HintBytes, ["options"] = JsonSerializer.SerializeToNode(hints) };
        }
        return root;
    }
}
