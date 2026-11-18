using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodexConfig.Core;

// Translate the list shape only. Capabilities absent from the upstream are not enabled.
public static class NativeModelCatalog
{
    // Discovery may stage only metadata actually supplied in native form. An ID-only
    // list is still useful to the picker, but cannot establish reviewer capabilities.
    public static byte[]? DeclaredCatalog(JsonElement entries)
    {
        if (entries.ValueKind != JsonValueKind.Array) return null;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { models = entries });
        try { ManagedModelCatalogStore.ValidateNative(bytes); return bytes; }
        catch (Exception error) when (error is InvalidDataException or JsonException or InvalidOperationException or ArgumentException) { return null; }
    }

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
            if (!native) CopyDeclaredCapabilities(item, entry);
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

    private static void CopyDeclaredCapabilities(JsonElement source, JsonObject target)
    {
        foreach (var key in new[] { "supported_in_api", "support_verbosity", "supports_parallel_tool_calls", "supports_reasoning_summaries", "supports_reasoning_effort_updates", "supports_search_tool", "supports_image_detail_original" })
            if (source.TryGetProperty(key, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                target[key] = value.GetBoolean();
        if (source.TryGetProperty("context_window", out var window) && window.ValueKind == JsonValueKind.Number && window.TryGetInt64(out var tokens) && tokens is > 0 and <= 10_000_000_000L)
            target["context_window"] = tokens;
        if (source.TryGetProperty("input_modalities", out var modalities) && modalities.ValueKind == JsonValueKind.Array && modalities.GetArrayLength() <= 8 &&
            modalities.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String && item.GetString() is "text" or "image"))
            target["input_modalities"] = new JsonArray(modalities.EnumerateArray().Select(item => (JsonNode?)JsonValue.Create(item.GetString())).DistinctBy(item => item!.ToJsonString()).ToArray());
        CopyDescriptors("supported_reasoning_levels", "effort"); CopyDescriptors("service_tiers", "id");
        if (source.TryGetProperty("default_reasoning_level", out var reasoning) && SafeText(reasoning, 100) &&
            target["supported_reasoning_levels"] is JsonArray levels && levels.Any(level => level?["effort"]?.GetValue<string>() == reasoning.GetString()))
            target["default_reasoning_level"] = reasoning.GetString();

        void CopyDescriptors(string key, string id)
        {
            if (!source.TryGetProperty(key, out var values) || values.ValueKind != JsonValueKind.Array || values.GetArrayLength() > 32) return;
            var result = new JsonArray(); var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in values.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(id, out var identifier) || !SafeText(identifier, 100)) return;
                if (!seen.Add(identifier.GetString()!)) continue;
                var entry = new JsonObject { [id] = identifier.GetString() };
                foreach (var label in new[] { "name", "description" })
                    if (value.TryGetProperty(label, out var text) && SafeText(text, 1000)) entry[label] = text.GetString();
                // The native reasoning descriptor requires a string description,
                // even when the upstream supplied no usable display text.
                if (key == "supported_reasoning_levels" && !entry.ContainsKey("description")) entry["description"] = "";
                result.Add(entry);
            }
            target[key] = result;
        }
    }
    private static bool SafeText(JsonElement value, int limit) => value.ValueKind == JsonValueKind.String &&
        value.GetString() is { Length: > 0 } text && text.Length <= limit && !text.Any(char.IsControl);
}
