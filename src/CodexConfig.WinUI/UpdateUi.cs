using System.Diagnostics;
using CodexConfig.Application;
using CodexConfig.Core;
using Microsoft.UI.Xaml;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private static Version CurrentVersion => typeof(App).Assembly.GetName().Version!;
    private UpdateDownloadService updateDownloads;
    private readonly UpdateInstallationService updateInstallation;
    private ReleaseUpdate? availableUpdate;
    private VerifiedInstaller? downloadedInstaller;
    private CancellationTokenSource? updateCancellation;
    private bool installingUpdate, downloadingUpdate;
    private string updateMessage = "尚未检查。";
    private int updatePercent;
    private Action<VerifiedInstaller>? smokeInstallerLaunch;

    private void RefreshUpdateControls()
    {
        if (CheckUpdateButton is null) return;
        var editable = !ManualApplicationBusy && !dialogOpen && !closing && updateCancellation is null;
        UpdateVersionText.Text = "当前程序版本 " + CurrentVersion.ToString(3) + " · WinUI 3";
        UpdateStatusText.Text = updateMessage;
        CheckUpdateButton.IsEnabled = editable;
        DownloadUpdateButton.Visibility = availableUpdate is not null && downloadedInstaller is null ? Visibility.Visible : Visibility.Collapsed;
        DownloadUpdateButton.IsEnabled = editable;
        InstallUpdateButton.Visibility = downloadedInstaller is null ? Visibility.Collapsed : Visibility.Visible;
        InstallUpdateButton.IsEnabled = editable && !HasPendingRecovery;
        CancelUpdateButton.Visibility = updateCancellation is null ? Visibility.Collapsed : Visibility.Visible;
        CancelUpdateButton.IsEnabled = updateCancellation is not null;
        UpdateProgress.Visibility = downloadingUpdate ? Visibility.Visible : Visibility.Collapsed;
        UpdateProgress.Value = updatePercent;
    }
    private async void CheckUpdateClicked(object sender, RoutedEventArgs args) => await CheckForUpdatesAsync();
    private async Task CheckForUpdatesAsync()
    {
        if (ManualApplicationBusy || dialogOpen || closing || updateCancellation is not null) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); updateCancellation = cancellation;
        availableUpdate = null; downloadedInstaller = null; updateMessage = "正在检查 GitHub 正式版本…"; RefreshView();
        try
        {
            var update = await updateDownloads.CheckAsync(CurrentVersion, cancellation.Token);
            if (closing) return;
            availableUpdate = update;
            updateMessage = update is null ? "当前版本已不低于最新正式版，或尚无可用正式发布。" : "发现正式版 " + update.Version + "，可下载并核对安装。";
        }
        catch (Exception error) { if (!closing) updateMessage = UpdateError(error); }
        finally { updateCancellation = null; if (!closing) RefreshView(); }
    }
    private async void DownloadUpdateClicked(object sender, RoutedEventArgs args) => await DownloadUpdateAsync();
    private async Task DownloadUpdateAsync()
    {
        if (ManualApplicationBusy || dialogOpen || closing || updateCancellation is not null || availableUpdate is not { } update) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); updateCancellation = cancellation;
        downloadedInstaller = null; downloadingUpdate = true; updatePercent = 0; updateMessage = "正在下载安装器…"; RefreshView();
        try
        {
            var directory = Path.Combine(options.SmokeOutput ?? Path.GetTempPath(), "CodexDock-updates", Guid.NewGuid().ToString("N"));
            var installer = await updateDownloads.DownloadAsync(update, directory, new Progress<int>(percent =>
            {
                if (closing || !ReferenceEquals(updateCancellation, cancellation) || cancellation.IsCancellationRequested) return;
                updatePercent = percent; updateMessage = "正在下载正式版 " + update.Version + " · " + percent + "%"; RefreshView();
            }), cancellation.Token);
            if (closing) return;
            downloadedInstaller = installer;
            updateMessage = "下载及 SHA-256 校验完成。安装前将核对草稿，停止本窗口增强及当前目录的两个网关。";
        }
        catch (Exception error) { if (!closing) updateMessage = UpdateError(error); }
        finally { updateCancellation = null; downloadingUpdate = false; if (!closing) RefreshView(); }
    }
    private void CancelUpdateClicked(object sender, RoutedEventArgs args) => updateCancellation?.Cancel();
    private async void InstallUpdateClicked(object sender, RoutedEventArgs args) => await InstallUpdateAsync();
    private async Task InstallUpdateAsync()
    {
        if (ManualApplicationBusy || dialogOpen || closing || updateCancellation is not null || HasPendingRecovery || downloadedInstaller is not { } installer) return;
        if (!await ResolveProfileDraftAsync() || !await ResolveTemplateEditAsync() || !await ResolveConnectionDraftAsync() || !await ResolveMobileDraftAsync()) return;
        var home = viewModel.Home; var revision = connectionDraft.Revision;
        installingUpdate = true; RefreshView();
        try
        {
            if (!await ConfirmDialogAsync("安装更新并退出", "将启动已校验的正式版安装器（版本 " + (availableUpdate?.Version.ToString() ?? "已下载") + "），覆盖安装当前 Codex Dock。\n\n配置目录：" + home +
                "\n\n安装前会停止本窗口增强及此目录的手机、模型网关，手机连接和网关请求将中断。确认两个网关均停止后，再启动安装器并退出程序。", "安装并退出")) return;
            RefreshView();
            await updateInstallation.InstallAsync(installer, home, () => !closing && viewModel.Home == home && ReferenceEquals(downloadedInstaller, installer) &&
                connectionDraft.Revision == revision && !connectionDraft.HasDraft && !profileDraft.HasDraft && !HasTemplateDraft && !mobileDraft.HasDraft && !HasPendingRecovery, lifetime.Token);
            closing = true; runtimeTimer.Stop(); lifetime.Cancel(); tray?.Dispose(); tray = null; allowClose = true; Close();
        }
        catch (Exception error)
        {
            updateMessage = error is OperationCanceledException ? "安装已取消，请刷新网关状态后重试。" : "安装器未能启动。" + (error is IOException or InvalidDataException ? " " + error.Message : " 请核对后台状态后重试。");
            RestoreWindow();
        }
        finally { installingUpdate = false; if (!closing) RefreshView(); }
    }
    private sealed class WinUiUpdateInstallationRuntime(MainWindow window) : IUpdateInstallationRuntime
    {
        public Task WaitForAutomaticAsync(CancellationToken cancellation) => window.automaticEnhancements.CancelAndWaitAsync();
        public async Task StopEnhancementsAsync(CancellationToken cancellation)
        { cancellation.ThrowIfCancellationRequested(); await window.applicationRuntime.StopEnhancementsAsync(); }
        public void Launch(VerifiedInstaller installer)
        {
            if (window.options.SmokeOutput is not null)
            {
                if (window.smokeInstallerLaunch is null) throw new InvalidOperationException("隔离检查不能启动真实安装器。");
                window.smokeInstallerLaunch(installer);
            }
            else _ = Process.Start(new ProcessStartInfo(installer.Path) { UseShellExecute = true }) ?? throw new IOException("无法启动更新安装器。");
        }
    }
    private void ReleasePageClicked(object sender, RoutedEventArgs args)
    {
        if (options.SmokeOutput is not null) return;
        try { Process.Start(new ProcessStartInfo(availableUpdate?.Page.AbsoluteUri ?? GitHubUpdates.Repository + "/releases/latest") { UseShellExecute = true }); }
        catch (Exception) { updateMessage = "无法打开浏览器，请稍后重试 GitHub 发布页。"; RefreshView(); }
    }
    private static string UpdateError(Exception error) => error switch
    {
        OperationCanceledException => "检查或下载已取消，或网络连接超时。",
        InvalidDataException => error.Message,
        _ => "无法完成更新，请检查 GitHub 网络连接后重试，或打开发布页手动下载。"
    };
}
