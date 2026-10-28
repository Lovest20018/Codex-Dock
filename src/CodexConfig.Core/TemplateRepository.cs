using System.Text.Json;
using Tomlyn.Model;

namespace CodexConfig.Core;

// Templates belong to this editor, independently of the selected Codex home.
public sealed class TemplateRepository(string directory)
{
    private sealed record Library(int Version, ConfigTemplate[] Templates, string[] HiddenBuiltIns);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private string FilePath => Path.Combine(directory, "templates.json");
    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexConfig");
    public static bool IsBuiltIn(string id) => BuiltInTemplates.All.Any(template => template.Id == id);

    public IReadOnlyList<ConfigTemplate> Load()
    {
        var library = Read();
        return Merge(library).ToArray();
    }

    public ConfigTemplate Save(ConfigTemplate template, bool create = false)
    {
        template = Validate(template);
        Mutate(library =>
        {
            if (create && Merge(library).Any(item => item.Id == template.Id)) throw new ArgumentException("模板已存在，请重新打开后再试。");
            return library with
            {
                Templates = library.Templates.Where(item => item.Id != template.Id).Append(template).ToArray(),
                HiddenBuiltIns = library.HiddenBuiltIns.Where(id => id != template.Id).ToArray()
            };
        });
        return template;
    }

    public void Delete(string id) => Mutate(library => library with
    {
        Templates = library.Templates.Where(template => template.Id != id).ToArray(),
        HiddenBuiltIns = IsBuiltIn(id) ? library.HiddenBuiltIns.Append(id).Distinct(StringComparer.Ordinal).ToArray() : library.HiddenBuiltIns
    });

    public void RestoreBuiltIns() => Mutate(library => library with
    {
        Templates = library.Templates.Where(template => !IsBuiltIn(template.Id)).ToArray(), HiddenBuiltIns = []
    });

    public static ConfigTemplate New(string name, IEnumerable<TemplateSetting> settings) =>
        new("user-" + Guid.NewGuid().ToString("N"), name, "我的模板", "", "", settings.ToArray());

    public static ConfigTemplate Validate(ConfigTemplate template)
    {
        if (string.IsNullOrWhiteSpace(template.Id) || template.Id.Length > 100 || string.IsNullOrWhiteSpace(template.Name) || template.Name.Trim().Length > 60)
            throw new ArgumentException("请填写模板名称（最多 60 个字）。");
        if (template.Settings is null || template.Settings.Count is < 1 or > 300) throw new ArgumentException("模板需包含 1 至 300 项设置。");
        if ((template.Description?.Length ?? 0) > 240) throw new ArgumentException("模板简介最多 240 个字。");
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var settings = new List<TemplateSetting>();
        foreach (var setting in template.Settings)
        {
            if (setting is null || string.IsNullOrWhiteSpace(setting.Path) || string.IsNullOrWhiteSpace(setting.Value)) throw new ArgumentException("请填写配置字段和取值。");
            var path = TomlDocument.FormatPath(TomlDocument.ParsePath(setting.Path));
            if (!CanInclude(path)) throw new ArgumentException("模板只保存普通设置，请移除连接、密钥或不可编辑的字段。");
            if (!paths.Add(path)) throw new ArgumentException("同一字段只能添加一次：" + path);
            ConfigSchema.Instance.Validate(path, setting.Value, allowPreferences: true);
            var value = TomlDocument.ParseModel("value = " + setting.Value)["value"];
            if (HasPrivateData(path, value)) throw new ArgumentException("模板不能保存密钥、连接或认证字段，包括表格和数组中的这些字段。");
            settings.Add(setting with { Path = path, Value = ConfigSchema.Literal(value) });
        }
        // Catch overlapping parent/child keys before saving or staging a broken combination.
        var combined = new TomlDocument(null);
        foreach (var setting in settings) combined.SetRaw(setting.Path, setting.Value);
        foreach (var setting in settings)
            if (ConfigSchema.Literal(combined.Get(setting.Path)) != setting.Value) throw new ArgumentException("模板中的父字段和子字段互相冲突，请只保留一种写法。");
        // Provenance of an edited built-in recipe remains available.
        var builtIn = BuiltInTemplates.All.FirstOrDefault(item => item.Id == template.Id);
        return template with { Name = template.Name.Trim(), Description = template.Description?.Trim() ?? "", Settings = settings.ToArray(),
            SourceName = builtIn?.SourceName ?? "我的模板", SourceUrl = builtIn?.SourceUrl ?? "" };
    }

    public static bool CanInclude(string path)
    {
        var parts = TomlDocument.ParsePath(path);
        var canonical = TomlDocument.FormatPath(parts);
        if (IsPrivatePath(canonical)) return false;
        var entry = ConfigSchema.Instance.Find(canonical);
        return entry is null || entry.CanEdit || ConfigSchema.PreferencePaths.Contains(canonical) && entry.Stage != "removed";
    }

    public static IReadOnlyList<TemplateSetting> Capture(TomlTable original, IReadOnlyDictionary<string, string?> draft)
    {
        var values = new TomlDocument(System.Text.Encoding.UTF8.GetBytes(Tomlyn.Toml.FromModel(original)));
        foreach (var (path, value) in draft) { if (value is null) values.Remove(path); else values.SetRaw(path, value); }
        return TomlDocument.ValuePaths(values.Model).Where(CanInclude).Where(path => !HasPrivateData(path, TomlDocument.ValueAt(values.Model, path))).OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new TemplateSetting(path, ConfigSchema.Literal(TomlDocument.ValueAt(values.Model, path)), ConfigHelp.Find(path)?.VerificationStatus != "historical" && path != "suppress_unstable_features_warning")).ToArray();
    }

    private static bool IsPrivatePath(string path) => TomlDocument.IsSensitivePath(path) || TomlDocument.ParsePath(path).Any(part =>
        part is "model" or "model_provider" or "model_providers" or "openai_base_url" or "chatgpt_base_url" or "connection" or "auth" or "oauth");

    private static bool HasPrivateData(string path, object? value) => IsPrivatePath(path) || value switch
    {
        TomlTable table => table.Any(pair => HasPrivateData(TomlDocument.FormatPath(TomlDocument.ParsePath(path).Append(pair.Key)), pair.Value)),
        TomlArray array => array.Any(item => HasPrivateData(path, item)),
        TomlTableArray array => array.Any(item => HasPrivateData(path, item)),
        _ => false
    };

    private static IEnumerable<ConfigTemplate> Merge(Library library) => BuiltInTemplates.All
        .Where(template => !library.HiddenBuiltIns.Contains(template.Id, StringComparer.Ordinal) && !library.Templates.Any(item => item.Id == template.Id))
        .Concat(library.Templates);

    private Library Read()
    {
        if (!File.Exists(FilePath)) return new(1, [], []);
        try
        {
            if (new FileInfo(FilePath).Length > 4 * 1024 * 1024) throw new InvalidDataException();
            var library = JsonSerializer.Deserialize<Library>(File.ReadAllBytes(FilePath), JsonOptions);
            if (library is null || library.Version != 1 || library.Templates is null || library.HiddenBuiltIns is null || library.Templates.Length > 300 || library.Templates.Select(template => template.Id).Distinct(StringComparer.Ordinal).Count() != library.Templates.Length) throw new InvalidDataException();
            return library with { Templates = library.Templates.Select(Validate).ToArray() };
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidDataException or NullReferenceException)
        { throw new InvalidDataException("模板文件无法读取，原文件已保留。请检查：" + FilePath, error); }
    }

    private void Mutate(Func<Library, Library> change)
    {
        Directory.CreateDirectory(directory);
        using var gate = new FileStream(Path.Combine(directory, "templates.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var next = change(Read());
        if (next.Templates.Length > 300) throw new ArgumentException("最多保存 300 个模板。");
        var data = JsonSerializer.SerializeToUtf8Bytes(next, JsonOptions);
        if (data.Length > 4 * 1024 * 1024) throw new ArgumentException("模板内容过多，请减少设置或文本长度。");
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, data); File.Move(temporary, FilePath, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
