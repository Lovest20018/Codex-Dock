using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CodexConfig.Core;

namespace CodexConfig.App;

public partial class MainWindow
{
    private bool desktopRestartBusy, desktopRestarted;
    private ConfigSnapshot? desktopRestartSnapshot;
    private CodexDesktopTarget? lastAppliedDesktopTarget;
    private string? desktopApplicationHome;
    private string desktopApplicationStage = "等待应用", desktopApplicationMessage = "";
    private (CodexDesktopTarget Target, ConfigSnapshot Saved, DesktopSpeedPlan Plan, int Port)? pendingSpeedRetry;
    private (CodexDesktopTarget Target, ConfigSnapshot Saved)? blockedDesktopExit;
    private Func<CodexDesktopTarget>? smokeDesktopTarget;
    private Func<CodexDesktopTarget, bool>? smokeDesktopConfirm;
    private Func<CodexDesktopTarget, string, string?, Action, CancellationToken, Task>? smokeDesktopRestart;
    private Func<ConfigSnapshot, Task>? smokeDesktopGateway;

    private string DesktopApplicationAction => pendingSpeedRetry is { } retry && retry.Saved.Home == home ? "重试速度兼容" :
        codexRuntime is { Available: true, ProcessCount: 0 } ? "打开 Codex 并应用" :
        codexRuntime is { Available: true, ProcessCount: > 0 } ? "重启 Codex 并应用" : "打开或重启 Codex";

    private void SetDesktopRestartStatus(string text)
    {
        desktopApplicationHome = home; desktopApplicationMessage = text;
        DesktopRestartStatus.Text = text;
        DesktopRestartStatus.Visibility = Visibility.Visible;
        if (SpeedCompatibilityBox.IsChecked == true) { speedFeedback = text; RefreshSpeedSettings(); }
        overviewState = null; RefreshOverview();
    }

    private void SetDesktopApplicationStage(string stage, string text, bool error = false)
    {
        desktopApplicationStage = stage; DesktopApplyStageText.Text = stage;
        DesktopRestartStatus.SetResourceReference(TextBlock.ForegroundProperty, error ? "Error" : "Muted");
        SetDesktopRestartStatus(text);
    }

    private void PrepareDesktopRestart(bool preserveRetry = false)
    {
        desktopRestarted = false; desktopRestartSnapshot = snapshot;
        if (!preserveRetry || pendingSpeedRetry?.Saved.Home != home) pendingSpeedRetry = null;
        blockedDesktopExit = null;
        RestartCodexButton.Content = DesktopApplicationAction; RestartCodexButton.IsEnabled = true;
        desktopApplicationStage = "等待应用"; DesktopApplyStageText.Text = desktopApplicationStage;
        DesktopApplyProgress.Visibility = Visibility.Collapsed;
        DesktopApplyTargetDetails.IsExpanded = false;
        DesktopApplyContext.Text = "配置目录：" + home;
        DesktopRestartStatus.Text = ""; DesktopRestartStatus.Visibility = Visibility.Collapsed;
        ReloadDesktopConfigurationButton.Visibility = ForceDesktopRestartButton.Visibility = DesktopConfigurationChanges.Visibility = Visibility.Collapsed;
        DesktopConfigurationChangesText.Text = "";
    }

    private void OpenDesktopApplicationPanel()
    {
        reviewPlan = null; reviewTemplate = null; reviewSaved = true; reviewWriteFailed = false;
        ReviewTitle.Text = "应用 Codex 配置";
        ReviewSummary.Text = "使用已保存的配置 · 本次操作不写入配置";
        ReviewTargets.Text = "当前配置目录：\n" + home; ReviewTargets.Visibility = Visibility.Collapsed;
        ReviewSuccessMessage.Text = "Dock 会先核对配置与连接，再打开或重启 Codex。完成后在新聊天中核对。";
        SavedGatewaysButton.Visibility = Visibility.Collapsed;
        PrepareDesktopRestart(preserveRetry: true);
        ReviewSuccess.Visibility = Visibility.Visible;
        ReviewTabs.Visibility = ReviewScroll.Visibility = PreviewBox.Visibility = ReviewError.Visibility = ConfirmWriteButton.Visibility = Visibility.Collapsed;
        ClosePreviewButton.Content = "关闭"; ReviewHint.Text = "本地文件与聊天实际参数分别核对";
        OpenOverlay(PreviewOverlay, ClosePreviewButton);
    }

    private async void RestartCodexClicked(object sender, RoutedEventArgs e) => await RestartCodexFromReviewAsync();

    private async void ForceDesktopRestartClicked(object sender, RoutedEventArgs e)
    {
        if (blockedDesktopExit is not { } blocked || desktopRestartBusy || closing || Dirty) return;
        desktopRestartSnapshot = blocked.Saved;
        await RestartCodexFromReviewAsync(forceTarget: blocked.Target);
    }

    private async Task RestartCodexFromReviewAsync(bool fromSpeedSettings = false, CodexDesktopTarget? forceTarget = null)
    {
        if (closing || desktopRestartBusy) return;
        if (Dirty) { OpenConfigurationReview(); return; }
        if (fromSpeedSettings)
        {
            if (snapshot is null || !SpeedPreferences().Load() || !DesktopSpeedSettings.Eligible(store, snapshot)) return;
            OpenDesktopApplicationPanel();
        }
        if (!reviewSaved || desktopRestartSnapshot is null || desktopRestarted) return;
        var saved = desktopRestartSnapshot;
        var expectedHome = saved.Home;
        var desktopOpened = false;
        CodexDesktopTarget? requestedTarget = null;
        void ValidateSaved()
        {
            if (!string.Equals(home, expectedHome, StringComparison.OrdinalIgnoreCase)) throw new IOException("配置目录已改变，请重新核对应用目标。");
            var current = store.Load(expectedHome);
            CodexApplySnapshot.RequireCompatible(saved, current);
            // Use the latest bytes in memory, including refreshed login tokens. Nothing is written here.
            saved = current;
        }
        desktopRestartBusy = true;
        DesktopApplyProgress.Visibility = Visibility.Visible;
        ReloadDesktopConfigurationButton.Visibility = ForceDesktopRestartButton.Visibility = Visibility.Collapsed;
        RestartCodexButton.IsEnabled = ClosePreviewButton.IsEnabled = ConfigurationSourcesButton.IsEnabled = false;
        SavedGatewaysButton.IsEnabled = ReloadDesktopConfigurationButton.IsEnabled = false;
        ForceDesktopRestartButton.IsEnabled = false;
        RefreshSpeedSettings();
        SetDesktopApplicationStage("1 / 5 · 核对配置", "正在核对已保存的配置、模型目录和启动入口…");
        try
        {
            if (busy || officialLoginBusy || gatewayPolling || gatewayLaunchInProgress)
                throw new InvalidOperationException("连接或网关正在处理操作，请等待完成后再应用。");
            if (store.HasPending(expectedHome)) throw new InvalidOperationException("发现未完成的保存，请先到备份恢复处理。");
            ValidateSaved();
            var settings = store.ReadSettings(saved);
            if (saved.Catalog is { Content: null }) throw new IOException("模型能力目录不存在或无法读取，请先在“模型与行为”核对目录。");
            if (ManagedModelCatalogStore.Inspect(saved).Managed && new TomlDocument(saved.Config).String("approvals_reviewer") == "auto_review")
                ManagedModelCatalogStore.ValidateCoverage(saved, new TomlDocument(saved.Config));
            var desktop = new CodexDesktop();
            var retry = pendingSpeedRetry;
            var target = smokeOutput is not null
                ? smokeDesktopTarget?.Invoke() ?? throw new InvalidOperationException("隔离检查不能操作真实 Codex 桌面端。")
                : await Task.Run(desktop.FindTarget, lifetime.Token);
            if (forceTarget is not null && !SameDesktopRoot(forceTarget, target))
                throw new InvalidOperationException("此前未退出的 Codex 实例已改变，不能结束旧目标；请重新点击应用核对。");
            if (forceTarget is not null) target = forceTarget;
            requestedTarget = target;
            DesktopApplyContext.Text = "程序：" + target.Executable + "\n配置目录：" + expectedHome;
            if (retry is not null)
            {
                // Retrying a failed enhancement only refreshes the same confirmed instance.
                if (smokeOutput is null && !SameDesktopRoot(retry.Value.Target, target))
                {
                    pendingSpeedRetry = null;
                    throw new InvalidOperationException("上次打开的 Codex 实例已退出或改变。请再次点击应用，重新核对启动目标。");
                }
                if (!SpeedPreferences().Load() || !DesktopSpeedSettings.Eligible(store, saved))
                { pendingSpeedRetry = null; throw new InvalidOperationException("速度兼容已关闭或连接已改变，请重新应用当前配置。"); }
                CodexApplySnapshot.RequireCompatible(retry.Value.Saved, saved);
                desktopOpened = true;
                SetDesktopApplicationStage("5 / 5 · 加载速度兼容", "正在重试速度兼容，将刷新本次 Codex 页面…");
                await StopSpeedSessionAsync();
                await AttachSpeedCompatibilityAsync(target, saved, retry.Value.Plan, retry.Value.Port);
                pendingSpeedRetry = null;
                CompleteDesktopApplication(saved, target, speed: true);
                return;
            }
            var speed = await PrepareSpeedCompatibilityAsync(target, saved);
            var confirmed = smokeOutput is not null ? smokeDesktopConfirm?.Invoke(target) == true : MessageBox.Show(this,
                (forceTarget is not null ? "将结束下列已确认的 Codex 实例及其子进程，再重新打开。\n正在运行的任务会中断，未保存内容可能丢失。\n目标主进程：" +
                    string.Join("、", target.Processes.Where(process => !target.Processes.Any(parent => parent.Id == process.ParentId)).Select(process => process.Id)) + "\n\n" :
                    target.IsRunning ? "将请求 Codex 正常退出，再重新打开。请先完成正在运行的任务并保存编辑内容。若取消退出或程序未退出，Dock 会停止本次重启。\n\n" : "将打开 Codex 桌面端。\n\n") +
                "程序：\n" + target.Executable + "\n\n本次使用的配置目录：\n" + saved.Home +
                (settings.Mode == ConnectionMode.OfficialWithApi ? "\n\n将先检查并启动该连接的本机模型网关。" : "") +
                (speed.Plan is null ? "" : "\n\n将应用 API 速度兼容；请保持 Dock 运行。") + "\n\n重新打开后，请新建聊天核对配置。继续？",
                forceTarget is not null ? "结束该实例并重开" : target.IsRunning ? "重启 Codex 并应用配置" : "打开 Codex 并应用配置", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
            if (!confirmed) { SetDesktopApplicationStage("已取消", "已取消应用，配置仍已保存；可稍后继续。"); return; }
            ValidateSaved();
            SetDesktopApplicationStage("2 / 5 · 准备连接", settings.Mode == ConnectionMode.OfficialWithApi ? "正在确认本机模型网关可用，完成后再退出 Codex…" : "API Key 直连，无需启动本机模型网关。");
            await PrepareDesktopGatewayAsync(saved);
            ValidateSaved();
            CodexDesktopTarget opened;
            if (smokeOutput is not null)
            {
                if (smokeDesktopRestart is null) throw new InvalidOperationException("隔离检查不能启动真实 Codex。");
                SetDesktopApplicationStage("3 / 5 · 退出旧实例", "正在等待 Codex 完全退出…");
                await smokeDesktopRestart(target, saved.Home, saved.AuthApiBaseUrl, ValidateSaved, lifetime.Token);
                opened = target;
            }
            else
            {
                opened = await Task.Run(() => desktop.RestartAsync(target, saved.Home, saved.AuthApiBaseUrl, ValidateSaved, lifetime.Token, speed.Port,
                    phase => Dispatcher.Invoke(() => DesktopProcessProgress(phase, target.IsRunning)), forceConfirmed: forceTarget is not null), lifetime.Token);
            }
            desktopOpened = true;
            RecordDesktopApplication(saved, opened);
            await StopSpeedSessionAsync();
            if (speed.Plan is not null)
            {
                pendingSpeedRetry = (opened, saved, speed.Plan, speed.Port!.Value);
                SetDesktopApplicationStage("5 / 5 · 加载速度兼容", "Codex 已打开，正在连接并加载速度兼容…");
                await AttachSpeedCompatibilityAsync(opened, saved, speed.Plan, speed.Port.Value);
                pendingSpeedRetry = null;
            }
            CompleteDesktopApplication(saved, opened, speed.Plan is not null);
        }
        catch (CodexApplyConflictException error)
        {
            SetDesktopApplicationStage("等待核对外部变更", error.Message, error: true);
            ReloadDesktopConfigurationButton.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException)
        {
            SetDesktopApplicationStage("应用已取消", desktopOpened ? "Codex 已打开，后续应用已取消。请核对当前状态后重试。" : "应用已取消，配置仍已保存。", error: false);
        }
        catch (Exception error) when (error is CodexDesktopCloseException or CodexDesktopExitTimeoutException)
        {
            blockedDesktopExit = requestedTarget is { IsRunning: true } target ? (target, saved) : null;
            SetDesktopApplicationStage("等待旧实例退出", "配置已保存，Codex 尚未退出：" + error.Message, error: true);
            ForceDesktopRestartButton.Visibility = blockedDesktopExit is not null ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception error)
        {
            SetDesktopApplicationStage(desktopOpened ? "已打开 · 速度兼容未完成" : "应用未完成",
                (desktopOpened ? "Codex 已重新打开，速度兼容未完成：" : "配置已保存，应用未完成：") + error.Message, error: true);
            // A cancelled exit must not disconnect the compatibility session of the still-running app.
            if (desktopOpened || speedSession is { IsRunning: false }) await StopSpeedSessionAsync();
        }
        finally
        {
            desktopRestartBusy = false;
            DesktopApplyProgress.Visibility = Visibility.Collapsed;
            RestartCodexButton.Content = desktopRestarted ? "Codex 已打开" : DesktopApplicationAction;
            RestartCodexButton.IsEnabled = !desktopRestarted && !closing;
            ClosePreviewButton.IsEnabled = ConfigurationSourcesButton.IsEnabled = SavedGatewaysButton.IsEnabled = !closing;
            ReloadDesktopConfigurationButton.IsEnabled = !closing && !Dirty;
            ForceDesktopRestartButton.IsEnabled = !closing && !Dirty;
            if (fromSpeedSettings || SpeedCompatibilityBox.IsChecked == true) speedFeedback = DesktopRestartStatus.Text;
            overviewState = null; RefreshOverview(); RefreshSpeedSettings();
        }
    }

    private void DesktopProcessProgress(CodexDesktopPhase phase, bool wasRunning)
    {
        var (stage, message) = phase switch
        {
            CodexDesktopPhase.Preparing => ("2 / 5 · 核对启动入口", "正在核对 Codex 安装与启动入口…"),
            CodexDesktopPhase.Closing => ("3 / 5 · 退出旧实例", wasRunning ? "正在等待 Codex 完全退出，请完成其退出确认或未保存内容提示…" : "Codex 未运行，正在准备打开…"),
            CodexDesktopPhase.Starting => ("4 / 5 · 打开 Codex", "正在使用已保存的配置打开 Codex…"),
            _ => ("5 / 5 · 核对新进程", "已请求打开 Codex，正在核对新桌面进程…")
        };
        SetDesktopApplicationStage(stage, message);
    }

    private async Task PrepareDesktopGatewayAsync(ConfigSnapshot saved)
    {
        if (store.ReadSettings(saved).Mode != ConnectionMode.OfficialWithApi) return;
        if (smokeOutput is not null)
        {
            if (smokeDesktopGateway is null) throw new InvalidOperationException("隔离检查不能启动真实模型网关。");
            await smokeDesktopGateway(saved); return;
        }
        if (gatewayPolling) throw new InvalidOperationException("模型网关正在处理操作，请稍后重试。");
        var bytes = saved.Bridge ?? throw new InvalidOperationException("当前混合连接缺少已保存的模型网关设置。");
        var connection = MixedConnection.Read(bytes);
        HttpConnectionPolicy.RequireAuthorization(connection.BaseUrl, connection.HttpOrigin);
        gatewayPolling = true;
        try
        {
            gatewayStatus = await BridgeProcess.EnsureAsync(saved.Home, bytes, cancellation: lifetime.Token);
            stoppedGatewayHomes.Remove(saved.Home);
        }
        finally { gatewayPolling = false; }
    }

    private void RecordDesktopApplication(ConfigSnapshot saved, CodexDesktopTarget target)
    {
        lastAppliedSnapshot = saved; lastAppliedAt = DateTimeOffset.Now; lastAppliedDesktopTarget = target;
        if (smokeOutput is null) codexRuntime = new(true, target.Processes.Count) { DesktopProcesses = target.Processes };
        // No draft can be edited while the application panel owns the main shell.
        if (snapshot is not null && !snapshot.SameContents(saved)) Reload(saved);
        desktopRestartSnapshot = saved;
    }

    private void CompleteDesktopApplication(ConfigSnapshot saved, CodexDesktopTarget target, bool speed)
    {
        desktopRestarted = true; RecordDesktopApplication(saved, target);
        SetDesktopApplicationStage("已打开 · 请在新聊天核对", speed
            ? "Codex 已重新打开，速度兼容已应用。请保持 Dock 运行，并在新建聊天中核对速度档位。"
            : "已确认 Codex 桌面进程打开。请新建聊天核对；项目、Profile 和组织要求仍可能覆盖此文件。");
        ReviewHint.Text = "本地配置已保存 · Codex 已打开 · 请在新聊天核对";
    }

    private static bool SameDesktopRoot(CodexDesktopTarget expected, CodexDesktopTarget current)
    {
        var roots = expected.Processes.Where(process => !expected.Processes.Any(parent => parent.Id == process.ParentId)).ToArray();
        return roots.Length == 1 && current.Processes.Contains(roots[0]);
    }

    private void ReloadDesktopConfigurationClicked(object sender, RoutedEventArgs e)
    {
        if (desktopRestartBusy || closing || Dirty || desktopRestartSnapshot is null) return;
        try
        {
            var previous = desktopRestartSnapshot; var current = store.Load(home);
            _ = store.ReadSettings(current);
            var differences = CodexApplySnapshot.Compare(previous, current).Changes;
            var before = new TomlDocument(previous.Config).Model; var after = new TomlDocument(current.Config).Model;
            string Display(string path, object? value) => value is null ? "未设置" : ConfigReadClient.SafeValue(path, JsonSerializer.SerializeToElement(value));
            var text = differences.Select(change =>
            {
                const string prefix = "config.toml · ";
                if (!change.StartsWith(prefix, StringComparison.Ordinal)) return change + "（内容已隐藏）";
                var path = change[prefix.Length..];
                return path + "\n之前：" + Display(path, TomlDocument.ValueAt(before, path)) + "\n现在：" + Display(path, TomlDocument.ValueAt(after, path));
            });
            var summary = string.Join("\n\n", text);
            if (!Reload(current)) return;
            pendingSpeedRetry = null; PrepareDesktopRestart();
            reviewSaved = true;
            ReviewTitle.Text = "核对最新配置后应用";
            ReviewSummary.Text = "已读取最新本地配置 · 本次未写入文件";
            ReviewTargets.Text = "当前配置目录：\n" + home; ReviewTargets.Visibility = Visibility.Collapsed;
            ReviewSuccessMessage.Text = "以下变更来自保存后的本地文件。核对后点击应用，将使用当前文件；之前的保存备份仍可恢复。";
            DesktopConfigurationChangesText.Text = summary.Length > 0 ? summary : "仅格式或同一账号的登录刷新，设置值未改变。";
            DesktopConfigurationChanges.Visibility = Visibility.Visible; DesktopConfigurationChanges.IsExpanded = true;
            SetDesktopApplicationStage("最新配置已读取", "请核对本次外部变更，再点击应用。");
        }
        catch (Exception error) { SetDesktopApplicationStage("无法读取最新配置", error.Message, error: true); }
    }
}
