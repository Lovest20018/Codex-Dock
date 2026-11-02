using System.Globalization;
using System.Text.Json;
using Tomlyn.Model;

namespace CodexConfig.Core;

public sealed record CatalogEntry
{
    public string Path { get; init; } = "";
    public string Name { get; init; } = "";
    public string Chinese { get; init; } = "";
    public string Description { get; init; } = "";
    public string Category { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Stage { get; init; } = "";
    public bool Common { get; init; }
    public bool Deprecated { get; init; }
    public string[] Platforms { get; init; } = [];
    public string[] Types { get; init; } = [];
    public string[] EnumValues { get; init; } = [];
    public string[] ItemTypes { get; init; } = [];
    public string[] ItemEnumValues { get; init; } = [];
    public string[] RequiredProperties { get; init; } = [];
    public string? DefinitionPath { get; init; }
    public string HelpPath => DefinitionPath ?? Path;
    public JsonElement Default { get; init; }
    public double? Minimum { get; init; }
    public double? Maximum { get; init; }
    public int? MinLength { get; init; }
    public int? MaxLength { get; init; }
    public bool Dynamic => Path.Contains('<');
    public bool SupportedPlatform => Platforms.Length == 0 || Platforms.Contains("windows");
    public bool ManagedConnection => ConfigSchema.ConnectionPaths.Contains(Path);
    public bool CanEdit => SupportedPlatform && Stage != "removed" && !ManagedConnection && Kind is not ("table" or "map" or "recursive");
    public string Lifecycle => Stage switch { "under_development" => "开发中", "experimental" => "实验性", "deprecated" => "已弃用", "removed" => "已移除", "stable" => "稳定", _ => Deprecated ? "已弃用" : "配置项" };
    public string Label => string.IsNullOrEmpty(Name) || Name == Path ? Path : Name + "  ·  " + Path;
}

public sealed record CatalogCategory(string Id, string Name);

public sealed class ConfigSchema
{
    public IReadOnlyList<CatalogEntry> Entries { get; }
    public static ConfigSchema Instance { get; } = new();
    public string SchemaVersion { get; }
    public string VerifiedRuntime { get; }
    public string SourceUrl { get; }
    public string SourceHash { get; }
    public string RetrievedAt { get; }
    public IReadOnlyList<CatalogCategory> Categories { get; }
    public static readonly HashSet<string> ConnectionPaths = new(StringComparer.Ordinal)
    { "model", "model_provider", "openai_base_url", "chatgpt_base_url", "web_search", "features.fast_mode", "features.context_management" };
    public static readonly HashSet<string> PreferencePaths = new(StringComparer.Ordinal)
    { "web_search", "features.fast_mode", "features.context_management" };

    private ConfigSchema()
    {
        using var stream = typeof(ConfigSchema).Assembly.GetManifestResourceStream("CodexConfig.Core.config-schema.json")!;
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        SchemaVersion = root.TryGetProperty("schema_version", out var schema) ? schema.GetString() ?? "local" : "local";
        var provenance = root.TryGetProperty("provenance", out var source) ? source : default;
        string Meta(string name) => provenance.ValueKind == JsonValueKind.Object && provenance.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";
        VerifiedRuntime = Meta("verified_runtime"); SourceUrl = Meta("source"); SourceHash = Meta("sha256"); RetrievedAt = Meta("retrieved_at");
        Categories = root.GetProperty("categories").EnumerateArray().Select(c => new CatalogCategory(c.GetProperty("id").GetString()!, c.GetProperty("name").GetString()!)).ToArray();
        Entries = root.GetProperty("entries").EnumerateArray().Select(ReadEntry).ToArray();
    }

    private static CatalogEntry ReadEntry(JsonElement entry)
    {
        string S(string name) => entry.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";
        double? N(string name) => entry.TryGetProperty(name, out var value) && value.TryGetDouble(out var n) ? n : null;
        return new()
        {
            Path = S("path"), Name = S("name"), Chinese = S("chinese"), Description = S("description"), Category = S("category"), Kind = S("kind"), Stage = S("stage"),
            Common = entry.TryGetProperty("common", out var common) && common.GetBoolean(),
            Deprecated = entry.TryGetProperty("deprecated_hint", out var deprecated) && deprecated.GetBoolean(),
            Platforms = entry.GetProperty("platforms").EnumerateArray().Select(x => x.GetString()!).ToArray(),
            Types = entry.TryGetProperty("types", out var types) ? types.EnumerateArray().Select(x => x.GetString()!).ToArray() : [],
            EnumValues = entry.TryGetProperty("enum_values", out var values) ? values.EnumerateArray().Select(x => x.GetProperty("value").GetString()!).ToArray() : [],
            ItemTypes = entry.TryGetProperty("item_types", out var itemTypes) ? itemTypes.EnumerateArray().Select(x => x.GetString()!).ToArray() : [],
            ItemEnumValues = entry.TryGetProperty("item_enum_values", out var itemValues) ? itemValues.EnumerateArray().Select(x => x.GetString()!).ToArray() : [],
            RequiredProperties = entry.TryGetProperty("required_properties", out var required) ? required.EnumerateArray().Select(x => x.GetString()!).ToArray() : [],
            Default = entry.TryGetProperty("default", out var fallback) ? fallback.Clone() : default,
            Minimum = N("minimum"), Maximum = N("maximum"), MinLength = (int?)N("min_length"), MaxLength = (int?)N("max_length")
        };
    }

    public CatalogEntry? Find(string path)
    {
        if (Entries.FirstOrDefault(e => e.Path == path) is { } exact) return exact;
        var parts = TomlDocument.ParsePath(path);
        return Entries.FirstOrDefault(e => e.Path == TomlDocument.FormatPath(parts)) ?? Entries.FirstOrDefault(e =>
        {
            if (!e.Dynamic) return false;
            var template = e.Path.Split('.');
            return template.Length == parts.Length && template.Zip(parts).All(pair => pair.First == "<name>" || pair.First == pair.Second);
        });
    }

    public IEnumerable<CatalogEntry> UnlistedEntries(TomlTable model, IEnumerable<string> drafts) =>
        TomlDocument.ValuePaths(model).Concat(drafts.Where(ValidPath)).Distinct(StringComparer.Ordinal).Where(path => Find(path) is null)
            .Select(path => new CatalogEntry { Path = path, Name = path, Kind = TomlDocument.ValueAt(model, path) is TomlTableArray ? "recursive" : "raw", Category = "unlisted",
                Chinese = "本地目录未收录的配置。使用 TOML 值编辑，例如字符串需加引号；只校验语法，实际支持情况由 Codex 决定。" });

    public IEnumerable<CatalogEntry> ConfiguredEntries(TomlTable model, IEnumerable<string> drafts) =>
        TomlDocument.ValuePaths(model).Concat(drafts.Where(ValidPath)).Distinct(StringComparer.Ordinal).Select(path =>
            Find(path) is { } entry ? entry with { Path = path, DefinitionPath = entry.Path } : new CatalogEntry
            {
                Path = path, Name = path, Kind = TomlDocument.ValueAt(model, path) is TomlTableArray ? "recursive" : "raw",
                Category = "unlisted", Chinese = "未收录的设置；仅检查 TOML 语法，实际支持由当前客户端决定。"
            });

    public IEnumerable<CatalogEntry> ExpandedEntries(TomlTable model)
    {
        foreach (var entry in Entries)
        {
            if (!entry.Dynamic) { yield return entry; continue; }
            foreach (var parts in Expand(entry.Path.Split('.'), 0, model, []))
            {
                var path = string.Join('.', parts.Select(part => part == "<name>" ? part : TomlDocument.FormatPath([part])));
                yield return entry with { Path = path, DefinitionPath = entry.Path };
            }
        }

        IEnumerable<string[]> Expand(string[] template, int index, object? value, string[] prefix)
        {
            if (index == template.Length) { yield return prefix; yield break; }
            var part = template[index];
            if (part != "<name>")
            {
                foreach (var path in Expand(template, index + 1, (value as TomlTable)?.GetValueOrDefault(part), [.. prefix, part])) yield return path;
                yield break;
            }
            var names = (value as TomlTable)?.Keys.ToList() ?? [];
            if (prefix.SequenceEqual(new[] { "model_providers" }) && model.GetValueOrDefault("model_provider") is string active && !names.Contains(active)) names.Add(active);
            if (names.Count == 0) { yield return [.. prefix, .. template.Skip(index)]; yield break; }
            foreach (var name in names)
                foreach (var path in Expand(template, index + 1, (value as TomlTable)?.GetValueOrDefault(name), [.. prefix, name])) yield return path;
        }
    }

    private static bool ValidPath(string path)
    {
        try { _ = TomlDocument.ParsePath(path); return true; }
        catch (ArgumentException) { return false; }
    }

    public void Validate(string path, string? repr, bool allowPreferences = false)
    {
        _ = TomlDocument.ParsePath(path);
        var entry = Find(path);
        if (repr is null && entry is { SupportedPlatform: true, ManagedConnection: false } && entry.Kind is not ("table" or "map" or "recursive")) return;
        if (entry is not null && !entry.CanEdit && !(allowPreferences && PreferencePaths.Contains(entry.Path) && entry.SupportedPlatform && entry.Stage != "removed"))
            throw new ArgumentException($"{path} 由连接页管理、属于其他平台、已移除或是父表，请编辑对应子项。");
        if (repr is null) return;
        var model = TomlDocument.ParseModel("value = " + repr);
        if (model.Count != 1 || !model.TryGetValue("value", out var value)) throw new ArgumentException("请输入单个 TOML 值。");
        if (entry is null) return;
        var valid = entry.Kind switch
        {
            "boolean" => value is bool,
            "string" => value is string,
            "enum" => value is string choice && entry.EnumValues.Contains(choice, StringComparer.Ordinal) || entry.Path == "approval_policy" && value is TomlTable,
            "integer" => value is long,
            "number" => value is long or double,
            "array" => value is TomlArray or TomlTableArray,
            "bool_or_table" => value is bool or TomlTable,
            "union" => entry.Types.Any(type => type switch { "string" => value is string, "boolean" => value is bool, "integer" => value is long, "number" => value is long or double, "array" => value is TomlArray or TomlTableArray, "object" => value is TomlTable, _ => false }),
            _ => false
        };
        if (!valid) throw new ArgumentException($"{path} 需要 {entry.Kind} 类型" + (entry.EnumValues.Length > 0 ? "，可选值：" + string.Join(", ", entry.EnumValues) : "。"));
        if (value is TomlArray array)
        {
            foreach (var item in array)
            {
                if (entry.ItemTypes.Length > 0 && !entry.ItemTypes.Any(type => MatchesType(item, type)))
                    throw new ArgumentException($"{path} 的列表元素类型必须为 {string.Join(" / ", entry.ItemTypes)}。");
                if (entry.ItemEnumValues.Length > 0 && (item is not string textItem || !entry.ItemEnumValues.Contains(textItem, StringComparer.Ordinal)))
                    throw new ArgumentException($"{path} 的列表元素不在允许值中。");
            }
        }
        if (value is TomlTable table)
            ValidateTable(path, table);
        if (value is string text && (text.Length < (entry.MinLength ?? 0) || text.Length > (entry.MaxLength ?? int.MaxValue)))
            throw new ArgumentException($"{path} 的文本长度不符合配置目录要求。");
        if (value is long or double)
        {
            var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            if (!double.IsFinite(number) || number < (entry.Minimum ?? double.NegativeInfinity) || number > (entry.Maximum ?? double.PositiveInfinity))
                throw new ArgumentException($"{path} 超出允许范围：{entry.Minimum?.ToString(CultureInfo.InvariantCulture) ?? "不限"} 至 {entry.Maximum?.ToString(CultureInfo.InvariantCulture) ?? "不限"}。");
        }
    }

    private void ValidateTable(string path, TomlTable table)
    {
        if (Find(path) is { } parent)
            foreach (var required in parent.RequiredProperties)
                if (!table.ContainsKey(required)) throw new ArgumentException(path + " 缺少必填项 " + required + "。");
        foreach (var (key, value) in table)
        {
            var parts = TomlDocument.ParsePath(path).Append(key).ToArray();
            var full = TomlDocument.FormatPath(parts);
            if (Find(full) is { } child && child.Path != path)
            {
                if (child.Kind is "table" or "map" && value is TomlTable nested) ValidateTable(full, nested);
                else Validate(full, Literal(value), allowPreferences: true);
            }
            else if (Find(path) is not null)
                throw new ArgumentException("结构化值包含当前目录未支持的子项，请分别添加已核实的配置路径。");
        }
    }

    private static bool MatchesType(object? value, string type) => type switch
    {
        "string" => value is string, "boolean" => value is bool, "integer" => value is long,
        "number" => value is long or double, "object" => value is TomlTable,
        "array" => value is TomlArray or TomlTableArray, "null" => value is null, _ => false
    };

    public static string Literal(object? value) => value switch
    {
        null => "未设置（由 Codex 决定）", bool flag => flag ? "true" : "false", string text => TomlDocument.Quote(text),
        TomlTable map => "{ " + string.Join(", ", map.Select(x => JsonSerializer.Serialize(x.Key) + " = " + Literal(x.Value))) + " }",
        TomlArray list => "[" + string.Join(", ", list.Select(Literal)) + "]",
        TomlTableArray list => "[" + string.Join(", ", list.Select(Literal)) + "]",
        DateTime date => date.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture) + (date.Kind == DateTimeKind.Utc ? "Z" : ""),
        DateTimeOffset date => date.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz", CultureInfo.InvariantCulture),
        IFormattable number => number.ToString(null, CultureInfo.InvariantCulture), _ => value.ToString() ?? ""
    };
}
