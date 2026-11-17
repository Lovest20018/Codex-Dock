using CodexConfig.Core;

namespace CodexConfig.Application;

// The automatic path can only discover and resume an existing connection.
// Starting, closing and activating a desktop remain explicit shell actions.
public interface IAutomaticEnhancementRuntime
{
    bool IsRunning { get; }
    bool IsApplied { get; }
    bool HasFeatures(bool speed, bool connections);
    Task<IReadOnlyList<CodexDesktopTarget>> FindTargetsAsync(CancellationToken cancellation);
    Task<DesktopSpeedPlan> InspectAsync(CodexDesktopTarget target, bool speed, bool connections, CancellationToken cancellation);
    Task StopAsync();
    Task ResumeAsync(CodexDesktopTarget target, ConfigSnapshot saved, DesktopSpeedPlan plan, int port, CancellationToken cancellation);
}

public sealed class NativeAutomaticEnhancementRuntime(DesktopEnhancementRuntime runtime) : IAutomaticEnhancementRuntime
{
    public bool IsRunning => runtime.Session is { IsRunning: true };
    public bool IsApplied => runtime.Session is { IsApplied: true };
    public bool HasFeatures(bool speed, bool connections) => runtime.Session is { } session && session.HasSpeed == speed && session.HasConnections == connections;
    public async Task<IReadOnlyList<CodexDesktopTarget>> FindTargetsAsync(CancellationToken cancellation)
        => await Task.Run(() => new CodexDesktop().FindTargets(), cancellation);
    public Task<DesktopSpeedPlan> InspectAsync(CodexDesktopTarget target, bool speed, bool connections, CancellationToken cancellation)
        => Task.Run(() => DesktopSpeedPatch.Inspect(target.Executable, speed, connections), cancellation);
    public Task StopAsync() => runtime.DisposeAsync().AsTask();
    public Task ResumeAsync(CodexDesktopTarget target, ConfigSnapshot saved, DesktopSpeedPlan plan, int port, CancellationToken cancellation)
        => runtime.AttachAsync(target, saved, plan, port, cancellation, resumeOnly: true);
}

public sealed class AutomaticEnhancementService(ConfigStore store, ClientEnhancementPreferencesService preferences,
    IAutomaticEnhancementRuntime runtime, TimeProvider? clock = null)
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private CancellationTokenSource? cancellation;
    private DateTimeOffset retryAfter;
    public bool IsBusy { get; private set; }
    public Task? Pending { get; private set; }
    public string Status { get; private set; } = "已开启。等待核对上次接入的实例。";
    public event EventHandler? Changed;

    public void Cancel() => cancellation?.Cancel();
    public void ResetRetry() => retryAfter = DateTimeOffset.MinValue;
    public async Task CancelAndWaitAsync()
    { Cancel(); if (Pending is { } pending) await pending; }

    // Called on the shell dispatcher; reentrant refreshes observe the same task.
    public Task TryResumeAsync(ConfigSnapshot? saved, Func<bool> eligible, CancellationToken lifetime)
    {
        if (Pending is { IsCompleted: false }) return Pending;
        if (saved is null || !eligible() || time.GetUtcNow() < retryAfter) return Task.CompletedTask;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Pending = completion.Task;
        _ = CompleteAsync(saved, eligible, lifetime, completion);
        return Pending;
    }

    private async Task CompleteAsync(ConfigSnapshot saved, Func<bool> eligible, CancellationToken lifetime, TaskCompletionSource completion)
    {
        try { await ResumeCoreAsync(saved, eligible, lifetime); completion.SetResult(); }
        catch (Exception error) { completion.SetException(error); }
    }

    private async Task ResumeCoreAsync(ConfigSnapshot saved, Func<bool> eligible, CancellationToken lifetime)
    {
        CancellationTokenSource? attempt = null;
        var attaching = false;
        try
        {
            var settings = preferences.AutomaticSettings.Load();
            var selected = preferences.Load();
            if (!settings.Enabled || !selected.ConnectionRecoveryEnabled && !selected.SpeedCompatibilityEnabled) return;
            var speed = selected.SpeedCompatibilityEnabled && DesktopSpeedSettings.Eligible(store, saved);
            if (runtime.IsRunning)
            {
                SetStatus(runtime.HasFeatures(speed, selected.ConnectionRecoveryEnabled)
                    ? "增强正在运行。Dock 会在连接断开后自动核对并接回。"
                    : "增强选项已改变，请重新应用新的选择。");
                return;
            }
            if (settings.Binding is not { } binding)
            { SetStatus("等待首次应用。完成一次增强启动后，将记住本次实例。"); return; }
            IsBusy = true;
            using var deadline = attempt = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            cancellation = attempt; attempt.CancelAfter(TimeSpan.FromSeconds(12));
            SetStatus("正在核对上次接入的 Codex…");
            void Validate()
            {
                attempt.Token.ThrowIfCancellationRequested();
                if (!eligible() || !string.Equals(saved.Home, preferences.Home, StringComparison.OrdinalIgnoreCase))
                    throw new OperationCanceledException("配置目录或当前操作已改变。", attempt.Token);
                var currentPreferences = preferences.Load();
                if (!currentPreferences.AutomaticReconnectEnabled || currentPreferences.ConnectionRecoveryEnabled != selected.ConnectionRecoveryEnabled ||
                    currentPreferences.SpeedCompatibilityEnabled != selected.SpeedCompatibilityEnabled)
                    throw new OperationCanceledException("增强选择已改变。", attempt.Token);
            }
            Validate();
            if (store.HasPending(saved.Home)) throw new IOException("配置有未完成的保存，请先处理备份恢复。");
            var current = await Task.Run(() => store.Load(saved.Home), attempt.Token);
            if (DesktopEnhancementSettings.SnapshotHash(current) != binding.SnapshotHash)
                throw new DesktopEnhancementResumeException("保存的连接、身份或模型设置已改变，请重新应用。");
            var target = DesktopEnhancementSettings.Match(binding, await runtime.FindTargetsAsync(attempt.Token));
            if (target is null) throw new DesktopEnhancementResumeException("原实例已退出或改变。需要使用时，通过增强快捷方式打开 Codex；Dock 不会自动重开它。");
            Validate();
            var plan = await runtime.InspectAsync(target, speed, selected.ConnectionRecoveryEnabled, attempt.Token);
            if (DesktopSpeedSession.PlanHash(plan) != binding.PlanHash)
                throw new DesktopEnhancementResumeException("增强选项或客户端版本已改变，请重新应用。");
            Validate();
            if (store.HasPending(saved.Home) || DesktopEnhancementSettings.SnapshotHash(store.Load(saved.Home)) != binding.SnapshotHash)
                throw new DesktopEnhancementResumeException("核对期间配置已改变，请重新应用。");
            attaching = true;
            await runtime.StopAsync(); Validate();
            await runtime.ResumeAsync(target, current, plan, binding.Port, attempt.Token);
            SetStatus("已自动接回，Codex 页面保持不变。");
        }
        catch (OperationCanceledException)
        { SetStatus("自动接回已暂停，稍后再核对；Codex 保持运行。"); }
        catch (Exception error)
        { retryAfter = time.GetUtcNow().AddSeconds(20); SetStatus(error.Message); }
        finally
        {
            // Clear before publishing idle, so a toggle cannot cancel a disposed CTS.
            if (ReferenceEquals(cancellation, attempt)) cancellation = null;
            try { if (attaching && !runtime.IsApplied) await runtime.StopAsync(); }
            catch (Exception error) { retryAfter = time.GetUtcNow().AddSeconds(20); SetStatus("未能清理断开的增强连接：" + error.Message); }
            IsBusy = false; Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SetStatus(string status) { Status = status; Changed?.Invoke(this, EventArgs.Empty); }
}
