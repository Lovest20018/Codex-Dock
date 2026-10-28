using System.Diagnostics;
using System.IO;
using System.Windows;
using CodexConfig.Core;

namespace CodexConfig.App;

public partial class MainWindow
{
    private static Version CurrentVersion => typeof(App).Assembly.GetName().Version!;
    private ReleaseUpdate? availableUpdate;
    private VerifiedInstaller? downloadedInstaller, pendingInstaller;
    private CancellationTokenSource? updateCancellation;

    private async void CheckUpdateClicked(object sender, RoutedEventArgs e)
    {
        if (updateCancellation is not null) return;
        using var client = new GitHubUpdates();
        await CheckForUpdatesAsync(client);
    }
    private async Task CheckForUpdatesAsync(GitHubUpdates client)
    {
        updateCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        CheckUpdateButton.IsEnabled = false; DownloadUpdateButton.Visibility = InstallUpdateButton.Visibility = Visibility.Collapsed;
        availableUpdate = null; downloadedInstaller = null; UpdateStatusText.Text = "正在检查 GitHub 正式版本…";
        try
        {
            availableUpdate = await client.CheckAsync(CurrentVersion, updateCancellation.Token);
            if (closing) return;
            UpdateStatusText.Text = availableUpdate is null ? "当前已是最新正式版本，或尚未发布新版。" : $"发现新版本 {availableUpdate.Version}，可下载并安装。";
            DownloadUpdateButton.Visibility = availableUpdate is null ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (Exception error) { if (!closing) UpdateStatusText.Text = UpdateError(error); }
        finally { updateCancellation.Dispose(); updateCancellation = null; CheckUpdateButton.IsEnabled = true; }
    }
    private async void DownloadUpdateClicked(object sender, RoutedEventArgs e)
    {
        if (availableUpdate is null || updateCancellation is not null) return;
        var update = availableUpdate;
        updateCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        CheckUpdateButton.IsEnabled = DownloadUpdateButton.IsEnabled = false; CancelUpdateButton.Visibility = Visibility.Visible;
        try
        {
            using var client = new GitHubUpdates();
            var directory = Path.Combine(Path.GetTempPath(), "CodexDock-updates", Guid.NewGuid().ToString("N"));
            downloadedInstaller = await client.DownloadAsync(update, directory, new Progress<int>(percent =>
            { if (!closing) UpdateStatusText.Text = $"正在下载 {update.Version} · {percent}%"; }), updateCancellation.Token);
            if (!closing)
            {
                UpdateStatusText.Text = "下载及 SHA-256 校验完成。安装前将处理未保存修改，并停止网关。";
                InstallUpdateButton.Visibility = Visibility.Visible; DownloadUpdateButton.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception error) { if (!closing) UpdateStatusText.Text = UpdateError(error); }
        finally
        {
            updateCancellation.Dispose(); updateCancellation = null;
            CheckUpdateButton.IsEnabled = DownloadUpdateButton.IsEnabled = true; CancelUpdateButton.Visibility = Visibility.Collapsed;
        }
    }
    private void CancelUpdateClicked(object sender, RoutedEventArgs e) => updateCancellation?.Cancel();
    private void InstallUpdateClicked(object sender, RoutedEventArgs e)
    {
        if (busy || updateCancellation is not null || downloadedInstaller is null || !File.Exists(downloadedInstaller.Path)) return;
        pendingInstaller = downloadedInstaller; RequestExit();
        if (!closing) pendingInstaller = null;
    }
    private void ReleasePageClicked(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(availableUpdate?.Page.AbsoluteUri ?? GitHubUpdates.Repository + "/releases/latest") { UseShellExecute = true }); }
        catch (Exception) { UpdateStatusText.Text = "无法打开浏览器，请访问 github.com/Lovest20018/Codex-Dock/releases。"; }
    }
    private static string UpdateError(Exception error) => error switch
    {
        OperationCanceledException => "检查或下载已取消，或网络连接超时。",
        InvalidDataException => error.Message,
        _ => "无法完成更新，请检查 GitHub 网络连接后重试，或打开发布页手动下载。"
    };

    private async Task RunUpdateSmokeAsync(string output, List<string> checks)
    {
        using var latest = new GitHubUpdates(new UpdateFixtureHandler(false));
        ShowPage("app"); await CheckForUpdatesAsync(latest);
        if (availableUpdate is not null || DownloadUpdateButton.Visibility != Visibility.Collapsed || !CheckUpdateButton.IsEnabled)
            throw new InvalidOperationException("No-release update UI state is incorrect.");
        using var newer = new GitHubUpdates(new UpdateFixtureHandler(true));
        await CheckForUpdatesAsync(newer);
        if (availableUpdate is null || DownloadUpdateButton.Visibility != Visibility.Visible || InstallUpdateButton.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("New-release update UI state is incorrect.");
        await Settle(); Capture(output, "github-update.png");
        availableUpdate = null; DownloadUpdateButton.Visibility = Visibility.Collapsed;
        checks.Add("GitHub update UI handles no release and a newer stable release without network or execution");
    }
    private sealed class UpdateFixtureHandler(bool newer) : System.Net.Http.HttpMessageHandler
    {
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsoluteUri != GitHubUpdates.LatestApi) throw new InvalidOperationException("Unexpected update test request.");
            var content = "{\"tag_name\":\"v99.0.0\",\"draft\":false,\"prerelease\":false,\"assets\":[{\"name\":\"CodexDock-99.0.0-Setup-win-x64.exe\",\"size\":100,\"browser_download_url\":\"https://github.com/Lovest20018/Codex-Dock/releases/download/v99.0.0/CodexDock-99.0.0-Setup-win-x64.exe\"},{\"name\":\"SHA256SUMS-99.0.0.txt\",\"browser_download_url\":\"https://github.com/Lovest20018/Codex-Dock/releases/download/v99.0.0/SHA256SUMS-99.0.0.txt\"}]}";
            return Task.FromResult(new System.Net.Http.HttpResponseMessage(newer ? System.Net.HttpStatusCode.OK : System.Net.HttpStatusCode.NotFound) { Content = new System.Net.Http.StringContent(content) });
        }
    }
}
