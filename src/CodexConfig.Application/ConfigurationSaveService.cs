using System.Text.Json;
using CodexConfig.Core;

namespace CodexConfig.Application;

public sealed record ConfigurationPlanRequest(ConfigSnapshot Before, ConnectionSettings Settings,
    IReadOnlyDictionary<string, string?>? Edits = null, bool UpdateConnection = true, bool UpdateModel = false,
    bool UpdateDetails = false, byte[]? OfficialAuth = null, bool SynchronizeCatalog = false, bool ReviewPolicyChanged = false, string? CatalogSource = null, byte[]? ServiceCatalog = null, string? ServiceCatalogFingerprint = null);
public sealed record ConfigurationChange(string Name, string Path, string Previous, string Next);
public enum ConfigurationSaveOutcome { Saved, NoChanges, Busy, Cancelled, Conflict, Failed }
public sealed record ConfigurationSaveResult(ConfigurationSaveOutcome Outcome, string Message, ConfigSnapshot? Saved = null, string? BackupPath = null)
{
    public bool Succeeded => Outcome is ConfigurationSaveOutcome.Saved or ConfigurationSaveOutcome.NoChanges;
    public bool WasWritten => BackupPath is not null;
}

// Holds an immutable copy of the reviewed bytes. The visible review contains no credentials.
public sealed class ConfigurationSaveReview
{
    internal ConfigPlan Plan { get; }
    private int attempted;
    internal bool TryConsume() => Interlocked.CompareExchange(ref attempted, 1, 0) == 0;
    public string Home => Plan.Before.Home;
    public IReadOnlyList<ConfigurationChange> Changes { get; }
    public IReadOnlyList<string> Targets { get; }
    public IReadOnlyList<string> Notes { get; }
    public bool HasChanges => Changes.Count > 0;
    public string Description => string.Join("\n\n", Changes.Select(change => change.Name + "\n" + change.Path +
        "\n本地现值：" + change.Previous + "\n修改为：" + change.Next));

    internal ConfigurationSaveReview(ConfigPlan plan, List<ConfigurationChange> changes, List<string> targets)
    { Plan = plan; Changes = changes.AsReadOnly(); Targets = targets.Distinct(StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly(); Notes = plan.Notes; }
}

public sealed class ConfigurationSaveService(ConfigStore store, WorkspaceApplyCoordinator operations)
{
    public ConfigPlan PreparePlan(ConfigurationPlanRequest request)
    {
        if (store.HasPending(request.Before.Home)) throw new InvalidOperationException("请先恢复未完成的保存，再核对修改。");
        var plan = store.Plan(request.Before, request.Settings, request.Edits, request.UpdateConnection, request.UpdateModel, request.UpdateDetails, request.OfficialAuth);
        var doc = new TomlDocument(plan.Config);
        if (request.ServiceCatalog is not null && (request.CatalogSource is not null || request.Edits?.ContainsKey("model_catalog_json") == true))
            throw new InvalidOperationException("已有明确的能力目录来源草稿，请保留一种来源后重新确认。");
        if (request.ServiceCatalog is not null && request.ServiceCatalogFingerprint != (request.Settings with { Model = "model-catalog" }).Fingerprint())
            throw new InvalidOperationException("连接设置已改变，请重新获取当前服务的模型能力。");
        if (request.ServiceCatalog is not null && !request.UpdateConnection &&
            ManagedModelCatalogStore.ConnectionId(doc, plan.Bridge) != ManagedModelCatalogStore.ConnectionId(new TomlDocument(request.Before.Config), request.Before.Bridge))
            throw new InvalidOperationException("本次修改包含新的模型服务，请先核对连接，再读取该服务的模型资料。");
        if (request.SynchronizeCatalog || request.ReviewPolicyChanged && doc.String("approvals_reviewer") == "auto_review")
            return ManagedModelCatalogStore.Prepare(plan, new(), request.CatalogSource, request.ServiceCatalog);
        if (request.Before.CatalogIndex is not null && ManagedModelCatalogStore.Inspect(request.Before).Managed && doc.String("approvals_reviewer") == "auto_review")
        {
            if (ManagedModelCatalogStore.ConnectionId(doc, plan.Bridge) != ManagedModelCatalogStore.ConnectionId(new TomlDocument(request.Before.Config), request.Before.Bridge))
                throw new InvalidOperationException("模型服务已改变，请导入当前服务的能力目录后确认保存。");
            ManagedModelCatalogStore.ValidateCoverage(request.Before, doc);
        }
        return plan;
    }

    public ConfigurationSaveReview PrepareReview(ConfigurationPlanRequest request) => Review(PreparePlan(request));

    public ConfigurationSaveReview Review(ConfigPlan source)
    {
        var plan = Freeze(source);
        var before = new TomlDocument(plan.Before.Config).Model; var after = new TomlDocument(plan.Config).Model;
        var changes = new List<ConfigurationChange>(); var targets = new List<string>();
        string Display(string path, object? value) => value is null ? "未设置 · 跟随其他配置层或 Codex 默认"
            : ConfigReadClient.SafeValue(path, JsonSerializer.SerializeToElement(value));
        foreach (var path in TomlDocument.ValuePaths(before).Concat(TomlDocument.ValuePaths(after)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var previous = TomlDocument.ValueAt(before, path); var next = TomlDocument.ValueAt(after, path);
            if (ConfigSchema.Literal(previous) == ConfigSchema.Literal(next)) continue;
            changes.Add(new(ConfigHelp.Name(path), path, Display(path, previous), next is null ? "移除此项本地覆盖，跟随其他配置层或 Codex 默认" : Display(path, next)));
        }
        if (changes.Count > 0) targets.Add(Path.Combine(plan.Before.Home, "config.toml"));
        void FileChange(byte[]? previous, byte[]? next, string name, string file)
        {
            if (Same(previous, next)) return;
            targets.Add(Path.Combine(plan.Before.Home, file));
            changes.Add(new(name, file, previous is null ? "本地未设置" : "已设置（内容已隐藏）", next is null ? "移除本地文件" : "更新本地文件（内容已隐藏）"));
        }
        FileChange(plan.Before.Auth, plan.Auth, !Same(plan.Before.Auth, plan.Auth) && plan.Mode == AuthenticationMode.ApiKey && store.ReadSettings(plan.Before).NeedsApiKeyMigration
            ? "认证文件 · 旧 PAT → API Key" : "认证文件", "auth.json");
        FileChange(plan.Before.Bridge, plan.Bridge, "加密模型连接", MixedConnection.FileName);
        FileChange(plan.Before.Details, plan.Details, "连接资料、网关等待与 HTTP 确认", ConnectionDetails.FileName);
        if (plan.CatalogUpdate is { } catalog && (!Same(plan.Before.CatalogIndex, catalog.Index) || plan.Before.Catalog is not { } old ||
            old.Path != catalog.Path || !Same(old.Content, catalog.Content)))
        {
            targets.Add(catalog.Path); targets.Add(Path.Combine(plan.Before.Home, ManagedModelCatalogStore.IndexFile));
            changes.Add(new("专用审核与模型目录", "Dock 管理的能力目录", "现有目录与审核策略", "固定 codex-auto-review；目录与策略同时备份，审核失败停止执行"));
        }
        var previousHttp = ConnectionDetails.Read(plan.Before.Details).HttpOrigin;
        var nextHttp = ConnectionDetails.Read(plan.Details).HttpOrigin;
        if (previousHttp != nextHttp)
            changes.Add(new("HTTP 明文连接确认", "Dock 站点确认", previousHttp.Length == 0 ? "未确认" : previousHttp, nextHttp.Length == 0 ? "撤销确认" : nextHttp));
        var previousWait = ConnectionDetails.Read(plan.Before.Details).GatewayHeaderTimeoutSeconds;
        var nextWait = ConnectionDetails.Read(plan.Details).GatewayHeaderTimeoutSeconds;
        if (previousWait != nextWait)
            changes.Add(new("模型网关等待响应头", "Dock 网关 · 响应头等待", previousWait + " 秒", nextWait + " 秒（仅经本网关的 HTTP 请求）"));
        if (plan.Before.AuthApiBaseUrl != plan.AuthApiBaseUrl)
        {
            targets.Add("用户环境变量 CODEX_AUTHAPI_BASE_URL");
            changes.Add(new("用户环境变量", "CODEX_AUTHAPI_BASE_URL", Display("CODEX_AUTHAPI_BASE_URL", plan.Before.AuthApiBaseUrl),
                plan.AuthApiBaseUrl is null ? "移除本地覆盖" : Display("CODEX_AUTHAPI_BASE_URL", plan.AuthApiBaseUrl)));
        }
        return new(plan, changes, targets);
    }

    public ConfigurationSaveResult Save(ConfigurationSaveReview review, ConfigSnapshot activeSnapshot, Func<string> activeHome,
        Action validateContext, CancellationToken cancellation = default)
    {
        using var operation = operations.TryBeginConfigurationSave();
        if (operation is null) return new(ConfigurationSaveOutcome.Busy, "正在处理其他操作，请完成后再保存。");
        string? backup = null;
        try
        {
            void Validate()
            {
                cancellation.ThrowIfCancellationRequested(); validateContext();
                if (!string.Equals(review.Home, activeHome(), StringComparison.OrdinalIgnoreCase) || !review.Plan.Before.SameContents(activeSnapshot))
                    throw new IOException("配置已重新读取或目录已改变，请返回调整后重新确认。");
            }
            Validate();
            if (!review.TryConsume()) throw new InvalidOperationException("此保存确认已使用，请重新查看修改后再保存。");
            if (store.HasPending(review.Home)) throw new InvalidOperationException("发现未完成的保存，请先恢复再确认。");
            CodexApplySnapshot.RequireCompatible(review.Plan.Before, store.Load(review.Home));
            if (!review.HasChanges)
            { operation.WaitForUser("没有需要保存的修改。"); return new(ConfigurationSaveOutcome.NoChanges, "没有需要保存的修改。", activeSnapshot); }
            Validate();
            backup = store.Apply(review.Plan);
            var saved = store.Load(review.Home); var plan = review.Plan;
            var expected = new ConfigSnapshot(plan.Before.Home, plan.Config, plan.Auth, plan.AuthApiBaseUrl)
            { Bridge = plan.Bridge, Details = plan.Details, CatalogIndex = plan.CatalogUpdate?.Index ?? plan.Before.CatalogIndex,
                Catalog = plan.CatalogUpdate is { } update ? new(update.Path, update.Content) : ManagedModelCatalogStore.Capture(review.Home, plan.Config) };
            var verification = CodexApplySnapshot.Compare(expected, saved);
            if (!verification.CanContinue) throw new IOException("保存后的本地设置再次改变：" + string.Join("、", verification.Changes) + "。请重新读取并核对；当前修改已保留。");
            Validate(); operations.ConfigurationSaved(saved);
            const string message = "本地配置已保存并核对，原内容已加密备份。请再应用到 Codex，并在聊天中核对。";
            operation.Complete("配置已保存", message, needsRestart: true);
            return new(ConfigurationSaveOutcome.Saved, message, saved, backup);
        }
        catch (OperationCanceledException)
        { operation.WaitForUser("保存已取消，草稿已保留。"); return new(ConfigurationSaveOutcome.Cancelled, "保存已取消，草稿已保留。", BackupPath: backup); }
        catch (CodexApplyConflictException error)
        { operation.Fail(error.Message); return new(ConfigurationSaveOutcome.Conflict, error.Message, BackupPath: backup); }
        catch (Exception error)
        { operation.Fail(error.Message); return new(ConfigurationSaveOutcome.Failed, error.Message, BackupPath: backup); }
    }

    private static bool Same(byte[]? left, byte[]? right) => left is null ? right is null : right is not null && left.AsSpan().SequenceEqual(right);
    private static ConfigPlan Freeze(ConfigPlan plan)
    {
        byte[]? Copy(byte[]? bytes) => bytes?.ToArray();
        var before = plan.Before with { Config = Copy(plan.Before.Config), Auth = Copy(plan.Before.Auth), Bridge = Copy(plan.Before.Bridge), Details = Copy(plan.Before.Details),
            CatalogIndex = Copy(plan.Before.CatalogIndex), Catalog = plan.Before.Catalog is { } catalog ? catalog with { Content = Copy(catalog.Content) } : null };
        return plan with { Before = before, Config = plan.Config.ToArray(), Auth = Copy(plan.Auth), Bridge = Copy(plan.Bridge), Details = Copy(plan.Details),
            Notes = plan.Notes.ToList().AsReadOnly(), CatalogUpdate = plan.CatalogUpdate is { } update ? update with
            { Index = update.Index.ToArray(), Content = update.Content.ToArray() } : null };
    }
}
