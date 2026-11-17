using CodexConfig.Core;

namespace CodexConfig.Application;

public sealed class ConfigurationRecoveryReview
{
    internal ConfigurationRecoveryPlan Plan { get; }
    private int attempted;
    internal bool TryConsume() => Interlocked.CompareExchange(ref attempted, 1, 0) == 0;
    public string Home => Plan.Home;
    public string SourcePath => Plan.SourcePath;
    public bool Interrupted => Plan.Interrupted;
    public DateTimeOffset CreatedAt => Plan.CreatedAt;
    public IReadOnlyList<ConfigurationRecoveryChange> Changes => Plan.Changes;
    public IReadOnlyList<string> Targets => Plan.Targets;
    internal ConfigurationRecoveryReview(ConfigurationRecoveryPlan plan) => Plan = plan;
}

public sealed class ConfigurationRecoveryService(ConfigStore store, WorkspaceApplyCoordinator operations)
{
    public ConfigurationRecoveryStatus Read(string home) => store.RecoveryStatus(home);
    public ConfigurationRecoveryReview PrepareReview(string home) => new(store.PrepareRecovery(home));
    public ConfigurationSaveResult Restore(ConfigurationRecoveryReview review, Func<string> activeHome, Action validateContext, CancellationToken cancellation = default)
    {
        using var operation = operations.TryBeginConfigurationRecovery();
        if (operation is null) return new(ConfigurationSaveOutcome.Busy, "正在处理其他操作，请完成后再恢复。");
        try
        {
            cancellation.ThrowIfCancellationRequested(); validateContext();
            if (!string.Equals(review.Home, activeHome(), StringComparison.OrdinalIgnoreCase)) throw new OperationCanceledException("工作目录已改变，请重新查看恢复差异。");
            if (!review.TryConsume()) throw new InvalidOperationException("此恢复确认已使用，请重新查看差异。");
            var result = store.ExecuteRecovery(review.Plan);
            operations.ConfigurationSaved(result.Saved);
            var message = review.Interrupted ? "未完成的保存已恢复并归档。请核对配置，再应用到 Codex。" : "已恢复上次保存前的配置，恢复前状态已加密备份。请再应用到 Codex。";
            operation.Complete("配置已恢复", message, needsRestart: true);
            return new(ConfigurationSaveOutcome.Saved, message, result.Saved, result.BackupPath);
        }
        catch (OperationCanceledException) { operation.WaitForUser("恢复已取消，当前文件已保留。"); return new(ConfigurationSaveOutcome.Cancelled, "恢复已取消，当前文件已保留。"); }
        catch (CodexApplyConflictException error) { operation.Fail(error.Message); return new(ConfigurationSaveOutcome.Conflict, error.Message); }
        catch (Exception error) { operation.Fail(error.Message); return new(ConfigurationSaveOutcome.Failed, error.Message); }
    }
}
