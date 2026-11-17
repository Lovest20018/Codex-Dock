using CodexConfig.Core;

namespace CodexConfig.Application;

public enum DesktopApplyOutcome { Completed, Cancelled, Conflict, ExitBlocked, Failed, Busy }
public sealed record DesktopApplyProgress(string Stage, string Message, bool IsError = false);
public sealed record DesktopEnhancementRetry(CodexDesktopTarget Target, ConfigSnapshot Saved, DesktopSpeedPlan Plan, int Port);
public sealed record DesktopExitRetry(CodexDesktopTarget Target, ConfigSnapshot Saved);
public sealed record DesktopApplyRequest(ConfigSnapshot Saved, bool ForceExit = false, bool EnhancedLaunch = false);
public sealed record DesktopApplyResult(DesktopApplyOutcome Outcome, ConfigSnapshot Saved, CodexDesktopTarget? Target,
    bool DesktopOpened, bool EnhancementsApplied, DesktopApplyProgress Progress);
public sealed record DesktopApplyConfirmation(CodexDesktopTarget Target, ConfigSnapshot Saved, bool ForceExit, bool HasEnhancements,
    bool NeedsGateway, int AvailableTargets)
{
    public string Title => ForceExit ? "结束所选实例并重开" : Target.IsRunning ? "重启 Codex 并应用配置" : "打开 Codex 并应用配置";
    public string Message =>
        (ForceExit ? "将结束本次所选的 Codex 实例及其子进程，再重新打开。正在运行的任务会中断，未保存内容可能丢失。\n\n" :
            Target.IsRunning ? "将请求 Codex 正常退出，再重新打开。请先完成正在运行的任务并保存编辑内容；未退出时停止本次应用。\n\n" : "将打开 Codex 桌面端。\n\n") +
        DesktopApplicationService.DescribeTarget(Target) + "\n\n本次配置目录：\n" + Saved.Home +
        (Target.AlsoExit.Count > 0 ? "\n\n以下 " + Target.ExitRoots.Count + " 个旧实例全部退出后，仅打开一次：\n" +
            string.Join("\n", new[] { Target }.Concat(Target.AlsoExit).Select(instance => "PID " + instance.MainProcess?.Id + " · " + (instance.UserDataDirectory ?? "资料目录未确认"))) :
            AvailableTargets > 1 ? "\n\n仅重启所选实例，其他 Codex 实例保持运行。" : "") +
        (NeedsGateway ? "\n\n将先检查并启动此连接的本机模型网关。" : "") +
        (HasEnhancements ? "\n\n将加载已开启的客户端增强。请保持当前 Dock 窗口或托盘运行。" : "") +
        "\n\n重新打开后，请核对聊天中的连接与参数。";
}

// Platform execution and human interaction are injected independently. Both
// shells use this sequence and the same retry/confirmation state.
public interface IDesktopApplyRuntime
{
    bool IsEnhancementRunning { get; }
    Task<IReadOnlyList<CodexDesktopTarget>> FindTargetsAsync(CancellationToken cancellation);
    Task<DesktopEnhancementPreparation> PrepareEnhancementsAsync(CodexDesktopTarget target, ConfigSnapshot saved, CancellationToken cancellation);
    Task PrepareGatewayAsync(ConfigSnapshot saved, CancellationToken cancellation);
    Task<CodexDesktopTarget> RestartAsync(CodexDesktopTarget target, ConfigSnapshot saved, Action validateSaved, int? port,
        Action<CodexDesktopPhase> progress, bool forceConfirmed, CancellationToken cancellation);
    Task StopEnhancementsAsync();
    Task AttachEnhancementsAsync(CodexDesktopTarget target, ConfigSnapshot saved, DesktopSpeedPlan plan, int port, CancellationToken cancellation);
}

public interface IDesktopApplyInteraction
{
    string ActiveHome { get; }
    void ValidateContext();
    Task<CodexDesktopTarget?> ChooseAsync(IReadOnlyList<CodexDesktopTarget> targets, CancellationToken cancellation);
    Task<bool> ConfirmAsync(DesktopApplyConfirmation confirmation, CancellationToken cancellation);
    void Report(DesktopApplyProgress progress);
    void TargetSelected(CodexDesktopTarget target, ConfigSnapshot saved);
    void DesktopOpened(CodexDesktopTarget target, ConfigSnapshot saved);
}

public sealed class DesktopApplicationService(ConfigStore store, WorkspaceApplyCoordinator coordinator,
    ClientEnhancementPreferencesService preferences, IDesktopApplyRuntime runtime)
{
    public bool IsBusy => coordinator.IsBusy;
    public DesktopEnhancementRetry? PendingRetry { get; private set; }
    public DesktopExitRetry? BlockedExit { get; private set; }

    public void Reset(bool preserveRetry = false)
    {
        if (IsBusy) return;
        if (!preserveRetry) PendingRetry = null;
        BlockedExit = null;
    }
    public void ClearEnhancementRetry() { if (!IsBusy) PendingRetry = null; }

    public async Task<DesktopApplyResult> ApplyAsync(DesktopApplyRequest request, IDesktopApplyInteraction interaction, CancellationToken cancellation = default)
    {
        var saved = request.Saved;
        var progress = new DesktopApplyProgress("1 / 5 · 核对配置", "正在核对已保存的配置、模型目录和启动入口…");
        using var operation = coordinator.TryBeginDesktopApplication(progress.Stage, progress.Message);
        if (operation is null) return new(DesktopApplyOutcome.Busy, saved, null, false, false, new("正在应用", "已有应用操作正在处理。"));
        var opened = false; var attached = false; var applyingConnections = false;
        (bool Speed, bool Connections)? expectedFeatures = null;
        CodexDesktopTarget? selected = null;
        void Report(string stage, string message, bool error = false)
        {
            progress = new(stage, message, error);
            if (error) operation.Fail(message); else operation.Report(stage, message);
            interaction.Report(progress);
        }
        void ValidateSaved()
        {
            cancellation.ThrowIfCancellationRequested();
            if (!string.Equals(interaction.ActiveHome, request.Saved.Home, StringComparison.OrdinalIgnoreCase))
                throw new IOException("配置目录已改变，请重新核对应用目标。");
            interaction.ValidateContext();
            var current = store.Load(request.Saved.Home);
            CodexApplySnapshot.RequireCompatible(saved, current);
            saved = current;
            if (expectedFeatures is { } expected)
            {
                var selectedPreferences = preferences.Load();
                if (expected.Connections != selectedPreferences.ConnectionRecoveryEnabled ||
                    expected.Speed != (selectedPreferences.SpeedCompatibilityEnabled && DesktopSpeedSettings.Eligible(store, saved)))
                    throw new InvalidOperationException("客户端增强选项已改变，请重新核对后应用。");
            }
        }
        DesktopApplyResult Result(DesktopApplyOutcome outcome) => new(outcome, saved, selected, opened, attached, progress);
        DesktopApplyResult Cancel(string message)
        {
            Report("已取消", message); operation.WaitForUser(message);
            return Result(DesktopApplyOutcome.Cancelled);
        }
        try
        {
            Report(progress.Stage, progress.Message);
            ValidateSaved();
            if (store.HasPending(saved.Home)) throw new InvalidOperationException("发现未完成的保存，请先到备份恢复处理。");
            var settings = store.ReadSettings(saved);
            if (saved.Catalog is { Content: null }) throw new IOException("模型能力目录不存在或无法读取，请先在“模型与行为”核对目录。");
            if (ManagedModelCatalogStore.Inspect(saved).Managed && new TomlDocument(saved.Config).String("approvals_reviewer") == "auto_review")
                ManagedModelCatalogStore.ValidateCoverage(saved, new TomlDocument(saved.Config));
            var retry = PendingRetry;
            var forced = request.ForceExit ? BlockedExit ?? throw new InvalidOperationException("没有此前确认且尚未退出的实例，不能直接结束进程。") : null;
            if (forced is not null) CodexApplySnapshot.RequireCompatible(forced.Saved, saved);
            var targets = await runtime.FindTargetsAsync(cancellation);
            cancellation.ThrowIfCancellationRequested();
            var previous = forced?.Target ?? retry?.Target;
            selected = forced is not null && targets.Any(candidate => candidate.MainProcess is { } root && forced.Target.ExitRoots.Any(old => SameProcess(old, root))) ? forced.Target :
                previous is not null ? targets.FirstOrDefault(candidate => SameRoot(previous, candidate)) :
                targets.Count == 1 ? targets[0] : await interaction.ChooseAsync(targets, cancellation);
            if (selected is null)
            {
                if (previous is not null) { PendingRetry = null; throw new InvalidOperationException("此前确认的 Codex 实例已退出或改变，请重新点击应用并选择目标。"); }
                return Cancel("已取消选择 Codex 实例，配置仍已保存。");
            }
            if (forced is null && !IsCapturedSelection(selected, targets)) throw new InvalidOperationException("所选实例不在本次核对列表中，请重新选择。");
            interaction.TargetSelected(selected, saved);
            if (retry is not null)
            {
                applyingConnections = retry.Plan.ConnectionsEnabled;
                var currentPreferences = preferences.Load();
                var speed = currentPreferences.SpeedCompatibilityEnabled && DesktopSpeedSettings.Eligible(store, saved);
                if (!SameRoot(retry.Target, selected) || retry.Plan.SpeedEnabled != speed || retry.Plan.ConnectionsEnabled != currentPreferences.ConnectionRecoveryEnabled)
                { PendingRetry = null; throw new InvalidOperationException("增强选项、连接或原实例已改变，请重新应用当前配置。"); }
                CodexApplySnapshot.RequireCompatible(retry.Saved, saved);
                expectedFeatures = (retry.Plan.SpeedEnabled, retry.Plan.ConnectionsEnabled);
                opened = true;
                Report("5 / 5 · 重试客户端增强", "正在重试同一实例的客户端增强，将刷新本次 Codex 页面…");
                ValidateSaved(); await runtime.StopEnhancementsAsync();
                await runtime.AttachEnhancementsAsync(selected, saved, retry.Plan, retry.Port, cancellation);
                PendingRetry = null; attached = true;
            }
            else
            {
                var preparation = await runtime.PrepareEnhancementsAsync(selected, saved, cancellation);
                expectedFeatures = (preparation.Plan?.SpeedEnabled == true, preparation.Plan?.ConnectionsEnabled == true);
                if (preparation.Plan is not null && preparation.Port is not (>= 1024 and <= 65535)) throw new InvalidOperationException("增强启动缺少有效的本机调试端口。");
                applyingConnections = preparation.Plan?.ConnectionsEnabled == true;
                var confirmation = new DesktopApplyConfirmation(selected, saved, request.ForceExit, preparation.Plan is not null,
                    settings.Mode == ConnectionMode.OfficialWithApi, targets.Count);
                if (!(request.EnhancedLaunch && !selected.IsRunning) && !await interaction.ConfirmAsync(confirmation, cancellation))
                    return Cancel("已取消应用，配置仍已保存；可稍后继续。");
                ValidateSaved(); BlockedExit = null;
                Report("2 / 5 · 准备连接", confirmation.NeedsGateway ? "正在确认本机模型网关可用，完成后再退出 Codex…" : "当前连接无需启动本机模型网关。");
                if (confirmation.NeedsGateway) await runtime.PrepareGatewayAsync(saved, cancellation);
                ValidateSaved();
                selected = await runtime.RestartAsync(selected, saved, ValidateSaved, preparation.Port,
                    phase => ProcessProgress(phase, confirmation.Target.IsRunning, Report), request.ForceExit, cancellation);
                opened = true; interaction.DesktopOpened(selected, saved);
                await runtime.StopEnhancementsAsync();
                if (preparation.Plan is { } plan)
                {
                    PendingRetry = new(selected, saved, plan, preparation.Port!.Value);
                    Report("5 / 5 · 加载客户端增强", "Codex 已打开，正在连接并加载已启用的客户端增强…");
                    ValidateSaved();
                    await runtime.AttachEnhancementsAsync(selected, saved, plan, preparation.Port.Value, cancellation);
                    PendingRetry = null; attached = true;
                }
            }
            var message = applyingConnections ? "Codex 已重新打开，旧聊天接续已接入。请保持 Dock 运行，聊天空闲时核对入口后继续。" :
                attached ? "Codex 已重新打开，客户端增强已接入。请保持 Dock 运行，并核对聊天中的连接与参数。" :
                "已确认 Codex 桌面进程打开。请新建聊天核对；项目、Profile 和组织要求仍可能覆盖此文件。";
            Report(applyingConnections ? "已打开 · 可继续旧聊天" : "已打开 · 请在新聊天核对", message);
            operation.Complete(progress.Stage, message);
            return Result(DesktopApplyOutcome.Completed);
        }
        catch (CodexApplyConflictException error)
        { Report("等待核对外部变更", error.Message, true); return Result(DesktopApplyOutcome.Conflict); }
        catch (OperationCanceledException)
        { return Cancel(opened ? "Codex 已打开，后续应用已取消。请核对当前状态后重试。" : "应用已取消，配置仍已保存。"); }
        catch (Exception error) when (error is CodexDesktopCloseException or CodexDesktopExitTimeoutException)
        {
            BlockedExit = selected is { IsRunning: true } ? new(selected, saved) : null;
            Report("等待旧实例退出", "配置已保存，Codex 尚未退出：" + error.Message, true);
            return Result(DesktopApplyOutcome.ExitBlocked);
        }
        catch (Exception error)
        {
            Report(opened ? applyingConnections ? "已打开 · 旧聊天接续未完成" : "已打开 · 速度兼容未完成" : "应用未完成",
                (opened ? applyingConnections ? "Codex 已重新打开，旧聊天接续未完成：" : "Codex 已重新打开，速度兼容未完成：" : "配置已保存，应用未完成：") + error.Message, true);
            return Result(DesktopApplyOutcome.Failed);
        }
        finally
        {
            if (!attached && (opened || !runtime.IsEnhancementRunning))
            {
                try { await runtime.StopEnhancementsAsync(); }
                catch (Exception) { /* Keep the original apply outcome even if transport cleanup fails. */ }
            }
        }
    }

    private static void ProcessProgress(CodexDesktopPhase phase, bool wasRunning, Action<string, string, bool> report)
    {
        var (stage, message) = phase switch
        {
            CodexDesktopPhase.Preparing => ("2 / 5 · 核对启动入口", "正在核对 Codex 安装与启动入口…"),
            CodexDesktopPhase.Closing => ("3 / 5 · 退出旧实例", wasRunning ? "正在等待 Codex 完全退出，请完成其退出确认或未保存内容提示…" : "Codex 未运行，正在准备打开…"),
            CodexDesktopPhase.Starting => ("4 / 5 · 打开 Codex", "正在使用已保存的配置打开 Codex…"),
            _ => ("5 / 5 · 核对新进程", "已请求打开 Codex，正在核对新桌面进程…")
        };
        report(stage, message, false);
    }

    public static bool SameRoot(CodexDesktopTarget expected, CodexDesktopTarget current) => expected.MainProcess is { } root && current.MainProcess is { } other && SameProcess(root, other);
    private static bool SameProcess(CodexDesktopProcess left, CodexDesktopProcess right) => left.Id == right.Id && left.Started == right.Started && string.Equals(left.Executable, right.Executable, StringComparison.OrdinalIgnoreCase);
    private static bool SameSelection(CodexDesktopTarget candidate, CodexDesktopTarget selected) =>
        string.Equals(candidate.Executable, selected.Executable, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(candidate.UserDataDirectory, selected.UserDataDirectory, StringComparison.OrdinalIgnoreCase) &&
        (ReferenceEquals(candidate, selected) || SameRoot(candidate, selected));
    private static bool IsCapturedSelection(CodexDesktopTarget selected, IReadOnlyList<CodexDesktopTarget> targets) =>
        targets.Any(candidate => SameSelection(candidate, selected)) &&
        selected.AlsoExit.All(other => targets.Any(candidate => SameSelection(candidate, other))) &&
        selected.ExitRoots.Select(root => root.Id).Distinct().Count() == selected.ExitRoots.Count;
    public static string DescribeTarget(CodexDesktopTarget target) => "程序：" + target.Executable +
        (target.MainProcess is { } root ? "\n主进程：" + root.Id + " · 启动于 " + DateTime.FromFileTimeUtc(root.Started).ToLocalTime().ToString("g") : "") +
        (target.UserDataDirectory is { } profile ? "\n资料目录：" + profile : "");

    public static string DescribeChanges(ConfigSnapshot previous, ConfigSnapshot current)
    {
        var before = new TomlDocument(previous.Config).Model; var after = new TomlDocument(current.Config).Model;
        string Display(string path, object? value) => value is null ? "未设置" : ConfigReadClient.SafeValue(path, System.Text.Json.JsonSerializer.SerializeToElement(value));
        return string.Join("\n\n", CodexApplySnapshot.Compare(previous, current).Changes.Select(change =>
        {
            const string prefix = "config.toml · ";
            if (!change.StartsWith(prefix, StringComparison.Ordinal)) return change + "（内容已隐藏）";
            var path = change[prefix.Length..];
            return path + "\n之前：" + Display(path, TomlDocument.ValueAt(before, path)) + "\n现在：" + Display(path, TomlDocument.ValueAt(after, path));
        }));
    }
}
