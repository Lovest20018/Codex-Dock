using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodexConfig.Core;

public sealed record ReviewModelPolicy(string Model = "codex-auto-review");
public sealed record CatalogReference(string Path, byte[]? Content)
{
    public bool Same(CatalogReference? other) => other is not null && Path.Equals(other.Path, StringComparison.OrdinalIgnoreCase) &&
        (Content is null ? other.Content is null : other.Content is not null && Content.AsSpan().SequenceEqual(other.Content));
}
public sealed record ManagedCatalogIndex(int Version, string Model, string SourcePath, string SourceHash, string CatalogHash,
    string ConnectionId, DateTimeOffset UpdatedAt);
public sealed record ManagedCatalogUpdate(byte[] Index, string Path, byte[] Content, string SourcePath, string SourceHash);
public sealed record ManagedCatalogInfo(string? ReviewModel, string SourcePath, int ModelCount, bool Managed, bool HasUpdate, DateTimeOffset? UpdatedAt);

// Native metadata is retained verbatim; policy projection only pins the reviewer.
public static class ManagedModelCatalogStore
{
    public const string IndexFile = "codex-dock-catalog.json";
    public const int MaxBytes = 1024 * 1024;
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    public static string CatalogPath(string home, byte[] content) => System.IO.Path.Combine(home, "codex-dock-models", Hash(content).ToLowerInvariant() + ".json");
    public static CatalogReference? Capture(string home, byte[]? config)
    {
        try
        {
            var source = new TomlDocument(config).String("model_catalog_json");
            if (string.IsNullOrWhiteSpace(source)) return null;
            var path = System.IO.Path.GetFullPath(source, home);
            return new(path, Read(path));
        }
        catch (Exception error) when (error is ArgumentException or InvalidDataException) { return null; }
    }
    public static byte[]? Read(string path)
    {
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > MaxBytes) throw new InvalidDataException("模型目录超过 1 MiB，请核对来源。");
        return File.ReadAllBytes(path);
    }
    public static ManagedCatalogIndex? Index(byte[]? bytes) => bytes is null ? null :
        JsonSerializer.Deserialize<ManagedCatalogIndex>(bytes) is { Version: 1 } index ? index : throw new InvalidDataException("模型目录管理记录无效。");
    private static JsonArray Models(byte[] bytes)
    {
        var root = JsonNode.Parse(bytes)?.AsObject();
        var models = root?["models"]?.AsArray() ?? throw new InvalidDataException("需要包含原生能力信息的 models 目录，只有模型 ID 的列表不能用于固定审核。");
        if (models.Count is 0 or > 200) throw new InvalidDataException("模型目录条目数无效。");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in models)
        {
            if (item is not JsonObject entry || entry["slug"] is not JsonValue slug || !slug.TryGetValue<string>(out var id) ||
                string.IsNullOrWhiteSpace(id) || id.Length > 200 || id.Any(char.IsControl) || !ids.Add(id) || entry["supported_reasoning_levels"] is not JsonArray)
                throw new InvalidDataException("模型目录缺少原生元数据，或存在重复模型。");
        }
        return models;
    }
    public static ManagedCatalogInfo Inspect(ConfigSnapshot snapshot)
    {
        var index = Index(snapshot.CatalogIndex);
        var doc = new TomlDocument(snapshot.Config);
        var source = snapshot.Catalog?.Path ?? System.IO.Path.Combine(snapshot.Home, "models_cache.json");
        var bytes = snapshot.Catalog?.Content ?? Read(source);
        if (bytes is null) return new(null, source, 0, false, false, null);
        var models = Models(bytes);
        var parents = models.OfType<JsonObject>().Where(m => m["slug"]!.GetValue<string>() != "codex-auto-review").ToArray();
        var overrides = parents.Select(m => m["auto_review_model_override"]?.GetValue<string>()).Distinct().ToArray();
        var reviewer = overrides.Length == 1 ? overrides[0] : null;
        var managed = index is not null && snapshot.Catalog is not null && Hash(bytes) == index.CatalogHash &&
            CatalogPath(snapshot.Home, bytes).Equals(snapshot.Catalog.Path, StringComparison.OrdinalIgnoreCase);
        var update = managed && File.Exists(index!.SourcePath) && Hash(Read(index.SourcePath)!) != index.SourceHash;
        return new(reviewer, managed ? index!.SourcePath : source, models.Count, managed, update, managed ? index!.UpdatedAt : null);
    }
    public static ConfigPlan Prepare(ConfigPlan plan, ReviewModelPolicy policy, string? requestedSource = null)
    {
        var before = plan.Before;
        var doc = new TomlDocument(plan.Config);
        if (doc.String("profile") is not null) throw new InvalidOperationException("活动 Profile 可能覆盖模型目录；请先在全部配置中核对该覆盖。");
        if (doc.String("approvals_reviewer") == "auto_review" && doc.String("approval_policy") == "never")
            throw new InvalidOperationException("当前审批策略为 never，不会产生自动审核；请在全部配置中调整。");
        var index = Index(before.CatalogIndex);
        var info = Inspect(before);
        var source = System.IO.Path.GetFullPath(requestedSource ?? (info.Managed ? index!.SourcePath :
            doc.String("model_catalog_json") ?? System.IO.Path.Combine(before.Home, "models_cache.json")), before.Home);
        var original = Read(source) ?? throw new InvalidDataException("尚无可读取的原生模型目录。请导入当前服务的模型能力文件。");
        var models = Models(original).DeepClone().AsArray();
        if (!models.OfType<JsonObject>().Any(m => m["slug"]!.GetValue<string>() == doc.String("model"))) throw new InvalidDataException("所选主模型不在目录中，不能保证固定审核模型。");
        var reviewer = models.OfType<JsonObject>().SingleOrDefault(m => m["slug"]!.GetValue<string>() == policy.Model);
        if (reviewer is null || reviewer["supported_in_api"]?.GetValue<bool>() != true) throw new InvalidDataException("目录未声明可用于 API 的 " + policy.Model + "，请核对当前服务支持的原生目录。");
        foreach (var item in models.OfType<JsonObject>())
            if (item["slug"]!.GetValue<string>() != policy.Model) item["auto_review_model_override"] = policy.Model;
        var content = JsonSerializer.SerializeToUtf8Bytes(new JsonObject { ["models"] = models });
        if (content.Length > MaxBytes) throw new InvalidDataException("生成的模型目录超过原生上限。");
        var path = CatalogPath(before.Home, content);
        var connection = ConnectionId(doc, plan.Bridge);
        if (info.Managed && requestedSource is null && index!.ConnectionId != connection) throw new InvalidOperationException("模型服务已改变，请为当前服务重新选择模型目录后固定审核。");
        var sourceHash = Hash(original);
        var nextIndex = info.Managed && index!.CatalogHash == Hash(content) && index.SourceHash == sourceHash && index.ConnectionId == connection
            ? before.CatalogIndex! : JsonSerializer.SerializeToUtf8Bytes(new ManagedCatalogIndex(1, policy.Model, source, sourceHash, Hash(content), connection, DateTimeOffset.UtcNow));
        doc.Set("model_catalog_json", path);
        return plan with { Config = doc.Bytes, ConfigPreview = doc.Masked(), CatalogUpdate = new(nextIndex, path, content, source, sourceHash),
            Notes = plan.Notes.Concat(["固定审核模型为 " + policy.Model + "；审核失败会阻止执行。模型目录与管理记录同时备份，可一并恢复。"] ).ToArray() };
    }
    public static string ConnectionId(TomlDocument doc, byte[]? bridge = null)
    {
        var id = doc.String("model_provider") ?? "openai";
        var address = id == "openai" ? doc.String("openai_base_url") : doc.Get(TomlDocument.FormatPath(["model_providers", id, "base_url"])) as string;
        if (bridge is not null && MixedConnection.Read(bridge) is { } connection && connection.OwnsRoute(doc)) address = connection.BaseUrl + "\n" + connection.Kind;
        return Hash(System.Text.Encoding.UTF8.GetBytes(id + "\n" + address));
    }
    public static void ValidateCoverage(ConfigSnapshot before, TomlDocument after)
    {
        if (after.String("model_catalog_json") != new TomlDocument(before.Config).String("model_catalog_json"))
            throw new InvalidOperationException("固定审核已启用，请通过导入／同步能力目录更换来源。");
        var models = Models(before.Catalog?.Content ?? throw new InvalidDataException("受管模型目录缺失，请恢复或重新同步。"));
        if (!models.OfType<JsonObject>().Any(model => model["slug"]!.GetValue<string>() == after.String("model") &&
            (model["slug"]!.GetValue<string>() == "codex-auto-review" || model["auto_review_model_override"]?.GetValue<string>() == "codex-auto-review")))
            throw new InvalidOperationException("该主模型未包含专用审核配置，请同步包含它的能力目录。");
    }
    public static void EnsureSafeTarget(string home, string path, byte[] bytes)
    {
        if (!System.IO.Path.GetFullPath(path).Equals(CatalogPath(home, bytes), StringComparison.OrdinalIgnoreCase)) throw new IOException("受管模型目录路径不匹配。");
        foreach (var directory in new[] { home, System.IO.Path.GetDirectoryName(path)! })
            if (Directory.Exists(directory) && File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("模型目录路径包含链接，已停止写入。");
        if (File.Exists(path) && (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint) || Hash(File.ReadAllBytes(path)) != Hash(bytes)))
            throw new IOException("受管目录已被外部修改，已停止覆盖。");
    }
}
