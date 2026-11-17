using System.Text.Json;

namespace CodexConfig.Core;

public sealed record ConfigurationRecoveryChange(string Name, string Path, string Previous, string Next);
public sealed record ConfigurationRecoveryStatus(bool Interrupted, string? BackupPath)
{ public bool Available => Interrupted || BackupPath is not null; }
public sealed record ConfigurationRecoveryResult(ConfigSnapshot Saved, string? BackupPath);

// Only safe descriptions leave the frozen review. Mutable credential/config
// arrays stay inside Core and cannot be changed by a shell while confirming.
public sealed class ConfigurationRecoveryPlan
{
    internal ConfigSnapshot Before { get; }
    internal ConfigSnapshot Target { get; }
    internal string SourceHash { get; }
    internal string PendingHash { get; }
    internal string BaselineHash { get; }
    public string Home => Before.Home;
    public string SourcePath { get; }
    public bool Interrupted { get; }
    public DateTimeOffset CreatedAt { get; }
    public IReadOnlyList<ConfigurationRecoveryChange> Changes { get; }
    public IReadOnlyList<string> Targets { get; }
    internal ConfigurationRecoveryPlan(ConfigSnapshot before, ConfigSnapshot target, string source, string sourceHash, string pendingHash, string baselineHash,
        bool interrupted, DateTimeOffset createdAt, IReadOnlyList<ConfigurationRecoveryChange> changes, IReadOnlyList<string> targets)
    {
        Before = Freeze(before); Target = Freeze(target); SourcePath = source; SourceHash = sourceHash;
        PendingHash = pendingHash; BaselineHash = baselineHash; Interrupted = interrupted; CreatedAt = createdAt;
        Changes = Array.AsReadOnly(changes.ToArray()); Targets = Array.AsReadOnly(targets.ToArray());
    }
    private static ConfigSnapshot Freeze(ConfigSnapshot value) => value with
    { Config = value.Config?.ToArray(), Auth = value.Auth?.ToArray(), Bridge = value.Bridge?.ToArray(), Details = value.Details?.ToArray(),
        CatalogIndex = value.CatalogIndex?.ToArray(), Catalog = value.Catalog is { } catalog ? catalog with { Content = catalog.Content?.ToArray() } : null };
}

public sealed partial class ConfigStore
{
    public ConfigurationRecoveryStatus RecoveryStatus(string home) => new(HasPending(home), LatestBackup(home));

    public ConfigurationRecoveryPlan PrepareRecovery(string home)
    {
        home = Path.GetFullPath(home); var pending = ReadFile(PendingPath(home));
        var source = pending is null ? LatestBackup(home) ?? throw new InvalidOperationException("尚无可恢复的备份。") : PendingSource(home, pending);
        var encrypted = ReadFile(source) ?? throw new IOException("备份不存在，请重新读取。");
        var record = DecodeBackup(encrypted); var current = Load(home);
        var target = pending is null ? RestoreTarget(record, current) : InterruptedTarget(home, record, current);
        var changes = RecoveryChanges(current, target).ToList();
        var targets = new List<string>();
        if (changes.Any(change => change.Path.StartsWith("config.toml", StringComparison.Ordinal))) targets.Add(Path.Combine(home, "config.toml"));
        foreach (var change in changes.Where(change => !change.Path.StartsWith("config.toml", StringComparison.Ordinal)))
            targets.Add(change.Path == "CODEX_AUTHAPI_BASE_URL" ? "用户环境变量 CODEX_AUTHAPI_BASE_URL" : Path.IsPathFullyQualified(change.Path) ? change.Path : Path.Combine(home, change.Path));
        if (pending is not null)
        {
            changes.Add(new("未完成保存记录", "pending.json", "等待恢复", "恢复事务前状态并归档原记录"));
            targets.Add(PendingPath(home)); targets.Add(source + ".recovered");
            if (record.AfterTestBaselineHash is not null && Hash(ReadFile(TestBaselinePath(home))) != Hash(record.BeforeTestBaseline))
            { changes.Add(new("旧版测试基线", "pat-test-baseline.dat", "已设置（已隐藏）", "恢复事务前状态（内容已隐藏）")); targets.Add(TestBaselinePath(home)); }
        }
        return new(current, target, source, Hash(encrypted), Hash(pending), Hash(ReadFile(TestBaselinePath(home))),
            pending is not null, record.CreatedUtc, changes, targets.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public ConfigurationRecoveryResult ExecuteRecovery(ConfigurationRecoveryPlan review)
    {
        void ValidateSource()
        {
            var pending = ReadFile(PendingPath(review.Home));
            if (Hash(pending) != review.PendingHash || Hash(ReadFile(TestBaselinePath(review.Home))) != review.BaselineHash ||
                (pending is null ? LatestBackup(review.Home) : PendingSource(review.Home, pending)) != review.SourcePath ||
                Hash(ReadFile(review.SourcePath)) != review.SourceHash)
                throw new IOException("恢复记录在确认后发生变化，请重新查看恢复差异；当前文件已保留。");
        }
        ValidateSource();
        var encrypted = ReadFile(review.SourcePath) ?? throw new IOException("备份不存在。");
        if (Hash(encrypted) != review.SourceHash) throw new IOException("恢复备份已改变，请重新查看差异。");
        var record = DecodeBackup(encrypted);
        string? backup = null;
        if (review.Interrupted)
        {
            using var gate = Lock(review.Home); ValidateSource();
            var current = Load(review.Home); CodexApplySnapshot.RequireCompatible(review.Before, current);
            CodexApplySnapshot.RequireCompatible(review.Target, InterruptedTarget(review.Home, record, current));
            RestoreInterrupted(review.Home, record, review.Before);
            File.Delete(PendingPath(review.Home)); File.Move(review.SourcePath, review.SourcePath + ".recovered");
        }
        else
        {
            var current = Load(review.Home); CodexApplySnapshot.RequireCompatible(review.Before, current);
            var target = RestoreTarget(record, current); CodexApplySnapshot.RequireCompatible(review.Target, target);
            backup = Commit(current, target.Config, target.Auth, target.AuthApiBaseUrl, "restore", target.Bridge, target.Details,
                restoreCatalog: target.Catalog, catalogIndex: target.CatalogIndex, validateSource: ValidateSource);
        }
        var saved = Load(review.Home); CodexApplySnapshot.RequireCompatible(review.Target, saved);
        return new(saved, backup);
    }

    private static string PendingSource(string home, byte[] pending)
    {
        var name = System.Text.Encoding.UTF8.GetString(pending).Trim();
        if (Path.GetFileName(name) != name || !name.EndsWith(".backup", StringComparison.Ordinal)) throw new InvalidDataException("未完成保存的恢复记录无效。");
        return Path.Combine(DataDir(home), name);
    }
    private static IEnumerable<ConfigurationRecoveryChange> RecoveryChanges(ConfigSnapshot before, ConfigSnapshot after)
    {
        var original = new TomlDocument(before.Config).Model; var restored = new TomlDocument(after.Config).Model;
        string Display(string path, object? value) => value is null ? "未设置 · 跟随 Codex" : ConfigReadClient.SafeValue(path, JsonSerializer.SerializeToElement(value));
        foreach (var path in TomlDocument.ValuePaths(original).Concat(TomlDocument.ValuePaths(restored)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var previous = TomlDocument.ValueAt(original, path); var next = TomlDocument.ValueAt(restored, path);
            if (ConfigSchema.Literal(previous) != ConfigSchema.Literal(next)) yield return new(ConfigHelp.Name(path), "config.toml · " + path, Display(path, previous), Display(path, next));
        }
        string FileState(byte[]? value) => value is null ? "文件不存在" : "文件已设置（内容已隐藏）";
        foreach (var (path, previous, next) in new[] { ("auth.json", before.Auth, after.Auth), (MixedConnection.FileName, before.Bridge, after.Bridge),
            (ConnectionDetails.FileName, before.Details, after.Details), (ManagedModelCatalogStore.IndexFile, before.CatalogIndex, after.CatalogIndex) })
            if (Hash(previous) != Hash(next)) yield return new(path == "auth.json" ? "认证文件" : "相关配置文件", path, FileState(previous), FileState(next));
        if (Hash(before.Config) != Hash(after.Config) && !TomlDocument.ValuePaths(original).Concat(TomlDocument.ValuePaths(restored)).Distinct(StringComparer.Ordinal)
            .Any(path => ConfigSchema.Literal(TomlDocument.ValueAt(original, path)) != ConfigSchema.Literal(TomlDocument.ValueAt(restored, path))))
            yield return new("配置文件状态与格式", "config.toml", FileState(before.Config), FileState(after.Config));
        if (before.AuthApiBaseUrl != after.AuthApiBaseUrl) yield return new("账号服务环境变量", "CODEX_AUTHAPI_BASE_URL",
            Display("CODEX_AUTHAPI_BASE_URL", before.AuthApiBaseUrl), Display("CODEX_AUTHAPI_BASE_URL", after.AuthApiBaseUrl));
        if (after.Catalog is { Content: not null } catalog && (before.Catalog is null || !before.Catalog.Same(catalog) || !File.Exists(catalog.Path)))
            yield return new("模型能力目录", catalog.Path, "当前模型目录", "恢复备份所需的目录依赖，不覆盖外部修改");
    }
}
