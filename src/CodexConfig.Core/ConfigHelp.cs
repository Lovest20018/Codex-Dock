using System.Text.Json;

namespace CodexConfig.Core;

public sealed record HelpSource(string Title, string Url);
public sealed record ConfigHelpEntry(string Path, string Name, string Summary, string Effect, string ValueHelp, string Usage,
    string Limitations, string DefaultBehavior, string[] RelatedKeys, HelpSource[] Sources, string VerifiedAt, string VerificationStatus)
{
    public string SearchText => string.Join(" ", Name, Summary, Effect, ValueHelp, Usage, Limitations, string.Join(" ", RelatedKeys));
    public bool Verified => VerificationStatus == "verified";
}

public static class ConfigHelp
{
    public static IReadOnlyDictionary<string, ConfigHelpEntry> Entries { get; } = Load();
    private static IReadOnlyDictionary<string, ConfigHelpEntry> Load()
    {
        using var stream = typeof(ConfigHelp).Assembly.GetManifestResourceStream("CodexConfig.Core.config-help.json")!;
        var entries = JsonSerializer.Deserialize<ConfigHelpEntry[]>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, ConfigHelpEntry>(entries.ToDictionary(entry => entry.Path, StringComparer.Ordinal));
    }
    public static ConfigHelpEntry? Find(string path) => Entries.GetValueOrDefault(path);
    public static string Name(string path) => Find(path)?.Name ?? ConfigSchema.Instance.Find(path)?.Name ?? path;
    public static string Summary(CatalogEntry entry) => Find(entry.Path)?.Summary ??
        (entry.Category == "unlisted" ? "未收录的设置，可按 TOML 格式编辑。" :
            string.IsNullOrWhiteSpace(entry.Chinese) ? "暂无说明。" : Shorten(entry.Chinese));
    private static string Shorten(string text)
    {
        var end = text.IndexOf('。');
        return (end >= 0 ? text[..(end + 1)] : text) is { Length: > 60 } value ? value[..60] + "…" : end >= 0 ? text[..(end + 1)] : text;
    }
}
