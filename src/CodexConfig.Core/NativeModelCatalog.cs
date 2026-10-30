using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodexConfig.Core;

// Translate the list shape only. Capabilities absent from the upstream are not enabled.
public static class NativeModelCatalog
{
    public static byte[] Convert(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !(root.TryGetProperty("models", out var items) || root.TryGetProperty("data", out items)) || items.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("模型入口未返回有效的 models/data 数组。");
        var output = new JsonArray(); var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var id = new[] { "slug", "id", "model" }.Select(k => item.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
            if (id is null || id.Length > 200 || id.Any(char.IsControl) || !seen.Add(id)) continue;
            var native = item.TryGetProperty("slug", out _) && item.TryGetProperty("supported_reasoning_levels", out _);
            var entry = native ? JsonNode.Parse(item.GetRawText())!.AsObject() : new JsonObject();
            void Default(string key, JsonNode? value) { if (!entry.ContainsKey(key)) entry[key] = value; }
            Default("slug", JsonValue.Create(id)); Default("display_name", JsonValue.Create(id));
            Default("description", JsonValue.Create("来自当前模型入口；能力以实际调用为准。"));
            Default("base_instructions", JsonValue.Create("You are a coding assistant. Follow the user's instructions, use the available tools when needed, and explain changes clearly."));
            Default("default_reasoning_level", null); Default("supported_reasoning_levels", new JsonArray());
            Default("shell_type", JsonValue.Create("unified_exec")); Default("visibility", JsonValue.Create("list"));
            Default("supported_in_api", JsonValue.Create(true)); Default("priority", JsonValue.Create(output.Count + 1));
            Default("support_verbosity", JsonValue.Create(false)); Default("apply_patch_tool_type", JsonValue.Create("freeform"));
            Default("truncation_policy", new JsonObject { ["mode"] = "tokens", ["limit"] = 10000 });
            Default("experimental_supported_tools", new JsonArray()); Default("input_modalities", new JsonArray("text"));
            Default("service_tiers", new JsonArray()); Default("supports_parallel_tool_calls", JsonValue.Create(false));
            Default("supports_reasoning_summaries", JsonValue.Create(false)); Default("supports_search_tool", JsonValue.Create(false));
            output.Add(entry);
            if (output.Count >= 100) break;
        }
        var result = JsonSerializer.SerializeToUtf8Bytes(new JsonObject { ["models"] = output });
        if (result.Length > 1024 * 1024) throw new InvalidDataException("Codex 原生模型目录超过 1 MiB，请减少模型条目。");
        return result;
    }
}
