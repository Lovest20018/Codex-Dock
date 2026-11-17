using System.Text.Json;
using CodexConfig.Core;
using Tomlyn.Model;

namespace CodexConfig.Application;

public sealed record ConfigurationCatalogQuery(string Scope = "configured", string Search = "", string Category = "all", bool Advanced = false, int Page = 0);
public sealed record ConfigurationCatalogPage(IReadOnlyList<CatalogEntry> Entries, int Count, int Page, int Pages);

// Browse the same native schema and local draft used by the ordinary save path.
// Querying never creates files or changes any draft value.
public sealed class ConfigurationCatalogService
{
    public const int PageSize = 20;
    public ConfigurationCatalogPage Query(ConnectionDraftViewModel draft, ConfigurationCatalogQuery query)
    {
        var schema = ConfigSchema.Instance;
        var original = new TomlDocument(draft.Snapshot?.Config).Model;
        var pending = draft.PendingFields;
        IEnumerable<CatalogEntry> entries = query.Scope is "configured" or "changed"
            ? schema.ConfiguredEntries(original, pending.Keys)
            : schema.ExpandedEntries(draft.Document().Model).Concat(schema.ConfiguredEntries(original, pending.Keys));
        entries = entries.Where(entry => entry.SupportedPlatform).DistinctBy(entry => entry.Path);
        if (query.Scope == "changed") entries = entries.Where(entry => pending.ContainsKey(entry.Path));
        var search = query.Search.Trim();
        if (search.Length > 0) entries = entries.Where(entry => (entry.Path + " " + ConfigHelp.Name(entry.HelpPath) + " " + ConfigHelp.Summary(entry)).Contains(search, StringComparison.OrdinalIgnoreCase));
        else if (query.Scope == "all" && !query.Advanced) entries = entries.Where(entry => entry.Stage is not ("removed" or "deprecated" or "under_development") && !entry.Deprecated && entry.Kind is not ("table" or "map" or "recursive"));
        if (query.Category != "all") entries = entries.Where(entry => entry.Category == query.Category);
        var all = entries.OrderBy(entry => entry.Path, StringComparer.Ordinal).ToArray();
        var pages = Math.Max(1, (all.Length + PageSize - 1) / PageSize); var page = Math.Clamp(query.Page, 0, pages - 1);
        return new(all.Skip(page * PageSize).Take(PageSize).ToArray(), all.Length, page, pages);
    }
    public static bool CanEdit(CatalogEntry entry) => !entry.Dynamic && (entry.CanEdit || ConfigSchema.PreferencePaths.Contains(entry.Path) && entry.Stage != "removed");
    public static bool Sensitive(string path, object? value)
    {
        if (TomlDocument.IsSensitivePath(path)) return true;
        return value switch
        {
            TomlTable table => table.Any(pair => Sensitive(TomlDocument.FormatPath(TomlDocument.ParsePath(path).Append(pair.Key)), pair.Value)),
            TomlArray list => list.Any(item => Sensitive(path, item)), TomlTableArray list => list.Any(item => Sensitive(path, item)), _ => false
        };
    }
    public static string Display(string path, object? value) => value is null ? "未设置 · 跟随其他配置层或 Codex 默认" :
        ConfigReadClient.SafeValue(path, JsonSerializer.SerializeToElement(value));
}
