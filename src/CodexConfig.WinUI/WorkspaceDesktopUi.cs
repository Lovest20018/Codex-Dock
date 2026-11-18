using CodexConfig.Application;
using CodexConfig.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private bool openingDesktop, observingDesktop;
    private int? runningDesktopCount;
    private (string Home, CodexDesktopTarget Target)? openedDesktop;
    private static string DesktopChoiceLabel(CodexDesktopTarget target) =>
        (target.UserDataDirectory is { Length: > 0 } directory ? "Codex · " + Path.GetFileName(Path.TrimEndingDirectorySeparator(directory)) : "Codex") +
        (target.MainProcess is { } process ? " · 进程 " + process.Id : " · 未运行");

    private void RecordOpenedDesktop(CodexDesktopTarget target, string home)
    {
        openedDesktop = (home, target);
        runningDesktopCount = 1;
        viewModel.Session.SetRuntime(new(true, target.Processes.Count) { DesktopProcesses = target.Processes });
    }

    private async Task ObserveDesktopTargetsAsync()
    {
        if (observingDesktop || ManualApplicationBusy || closing) return;
        observingDesktop = true;
        try
        {
            var (targets, _) = await FindDesktopTargetsAsync(lifetime.Token);
            if (closing || ManualApplicationBusy) return;
            var running = targets.Count(target => target.IsRunning);
            int? count = targets.Count > 0 ? running : null;
            if (runningDesktopCount != count) { runningDesktopCount = count; RefreshView(); }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally { observingDesktop = false; }
    }

    private async void OpenNativeLoginClicked(object sender, RoutedEventArgs args) => await OpenWorkspaceDesktopAsync(forLogin: true);

    private async Task OpenWorkspaceDesktopAsync(bool forLogin = false)
    {
        if (ApplicationBusy || dialogOpen || closing || HasPendingRecovery || viewModel.Snapshot is not { } saved) return;
        if (forLogin ? !ShowingNativeLogin : !viewModel.CanApplyConfiguration || connectionDraft.HasDraft) return;
        openingDesktop = true; shellError = ""; setupNativeStatus = ""; RefreshView();
        ReviewedDesktopChoice? launch = null;
        try
        {
            await automaticEnhancements.CancelAndWaitAsync();
            var (targets, error) = await FindDesktopTargetsAsync(lifetime.Token);
            if (targets.Count == 0) throw new IOException(error.Length > 0 ? error : "未找到 Codex 桌面端，请先安装 Codex。");
            var running = targets.Where(target => target.IsRunning).ToArray();
            runningDesktopCount = running.Length;
            lifetime.Token.ThrowIfCancellationRequested();
            if (running.Length > 0)
            {
                var matchesHome = openedDesktop is { } opened && string.Equals(opened.Home, saved.Home, StringComparison.OrdinalIgnoreCase)
                    ? running.FirstOrDefault(target => DesktopApplicationService.SameRoot(opened.Target, target)) : null;
                var defaultHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
                var singleDefaultWindow = running.Length == 1 && string.Equals(Path.TrimEndingDirectorySeparator(saved.Home), defaultHome, StringComparison.OrdinalIgnoreCase);
                if (forLogin && matchesHome is null && !singleDefaultWindow)
                    throw new IOException("无法确认正在运行的窗口使用此配置目录。请从这份资料原来的快捷方式登录，或退出 Codex 后再从这里打开。");
                var selected = matchesHome ?? (singleDefaultWindow ? running[0] : await ChooseForegroundTargetAsync(running));
                if (selected is null) return;
                if (!applicationRuntime.Activate(selected)) throw new IOException("暂时无法切到所选窗口，请从任务栏或托盘打开 Codex。");
                if (forLogin) setupNativeStatus = "已切到 Codex。完成登录后，回到此页重新读取。";
                return;
            }
            if (targets.Count != 1) throw new IOException("请先确定要使用的 Codex 安装位置。");
            if (forLogin)
            {
                void ValidateHome()
                {
                    if (closing || !string.Equals(viewModel.Home, saved.Home, StringComparison.OrdinalIgnoreCase) || store.HasPending(saved.Home))
                        throw new IOException("配置目录已变化或需要恢复，请重新核对。");
                    CodexApplySnapshot.RequireCompatible(saved, store.Load(saved.Home));
                }
                var openedTarget = await applicationRuntime.RestartAsync(targets[0], saved, ValidateHome, null, _ => { }, false, lifetime.Token);
                RecordOpenedDesktop(openedTarget, saved.Home);
                setupNativeStatus = "已打开 Codex。完成登录后，回到此页重新读取。";
            }
            else
            {
                if (!viewModel.PreferencesAvailable) throw new IOException("请先处理配置读取问题，再打开 Codex。");
                launch = new(saved.Home, targets[0], targets.Count, store.ReadSettings(saved).Mode == ConnectionMode.OfficialWithApi,
                    viewModel.SpeedCompatibilityEnabled, viewModel.ConnectionRecoveryEnabled);
            }
        }
        catch (OperationCanceledException) when (closing || lifetime.IsCancellationRequested) { }
        catch (Exception error) { if (!closing) SetShellError(forLogin ? "暂时无法打开登录" : "暂时无法打开 Codex", error.Message); }
        finally { openingDesktop = false; if (!closing) RefreshView(); }
        if (launch is not null) await ApplyDesktopAsync(modelChoice: launch);
    }

    private async Task<CodexDesktopTarget?> ChooseForegroundTargetAsync(IReadOnlyList<CodexDesktopTarget> targets)
    {
        var list = new ComboBox { Header = "正在运行的窗口", HorizontalAlignment = HorizontalAlignment.Stretch, DisplayMemberPath = "Label",
            ItemsSource = targets.Select(target => new ConnectionTarget(DesktopChoiceLabel(target), target)).ToArray() };
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(new TextBlock { Text = "选择要切换到的 Codex 窗口。", TextWrapping = TextWrapping.Wrap });
        body.Children.Add(list);
        var dialog = new ContentDialog { Title = "回到 Codex", Content = body, PrimaryButtonText = "切到窗口", CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close, IsPrimaryButtonEnabled = false, XamlRoot = Root.XamlRoot };
        list.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = list.SelectedIndex >= 0;
        return await ShowDialogAsync(dialog) == ContentDialogResult.Primary ? (list.SelectedItem as ConnectionTarget)?.Target : null;
    }
}
