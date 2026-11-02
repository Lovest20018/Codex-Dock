using System.Text.Json;

namespace CodexConfig.Core;

public sealed record ModelCapabilities(string Model, string[] ReasoningLevels, string[] ServiceTiers,
    long? ContextWindow, DateTimeOffset UpdatedAt, string SourcePath)
{
    public static ModelCapabilities? Read(string home, TomlDocument document, string? selectedModel = null)
    {
        if (document.String("model_catalog_json") is not { Length: > 0 } source) return null;
        try
        {
            var path = Path.GetFullPath(source, Path.GetFullPath(home));
            if (!File.Exists(path) || new FileInfo(path).Length > 1024 * 1024) return null;
            using var json = JsonDocument.Parse(File.ReadAllBytes(path));
            if (!json.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array) return null;
            var model = selectedModel ?? document.String("model");
            foreach (var entry in models.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("slug", out var slug) || slug.GetString() != model) continue;
                string[] Values(string key, string property) => entry.TryGetProperty(key, out var items) && items.ValueKind == JsonValueKind.Array
                    ? items.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
                        .Select(item => item.GetProperty(property).GetString()!).Where(value => value.Length is > 0 and <= 100 && !value.Any(char.IsControl)).Distinct(StringComparer.Ordinal).ToArray() : [];
                return new(model!, Values("supported_reasoning_levels", "effort"), Values("service_tiers", "id"),
                    entry.TryGetProperty("context_window", out var window) && window.TryGetInt64(out var tokens) ? tokens : null,
                    new DateTimeOffset(File.GetLastWriteTimeUtc(path)), path);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException or NotSupportedException) { }
        return null;
    }

    public bool SupportsTier(string tier) => ServiceTiers.Contains(tier, StringComparer.Ordinal) ||
        tier == "fast" && ServiceTiers.Contains("priority", StringComparer.Ordinal);
}
