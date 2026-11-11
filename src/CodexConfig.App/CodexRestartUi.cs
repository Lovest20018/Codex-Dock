using System.IO;
using System.Windows;
using CodexConfig.Core;

namespace CodexConfig.App;

public partial class MainWindow
{
    private bool desktopRestartBusy, desktopRestarted;
    private ConfigSnapshot? desktopRestartSnapshot;
    private Func<CodexDesktopTarget>? smokeDesktopTarget;
    private Func<CodexDesktopTarget, bool>? smokeDesktopConfirm;
    private Func<CodexDesktopTarget, string, string?, Action, CancellationToken, Task>? smokeDesktopRestart;

    private void PrepareDesktopRestart()
    {
        desktopRestarted = false;
        desktopRestartSnapshot = snapshot;
        RestartCodexButton.Content = "重启 Codex 并应用"; RestartCodexButton.IsEnabled = true;
        DesktopRestartStatus.Text = ""; DesktopRestartStatus.Visibility = Visibility.Collapsed;
    }

    private async void RestartCodexClicked(object sender, RoutedEventArgs e) => await RestartCodexFromReviewAsync();

    private async Task RestartCodexFromReviewAsync()
    {
        if (!reviewSaved || desktopRestartSnapshot is null || Dirty || closing || desktopRestartBusy || desktopRestarted) return;
        var saved = desktopRestartSnapshot;
        void ValidateSaved()
        {
            if (!saved.SameContents(store.Load(saved.Home)))
                throw new IOException("配置在保存后已被其他程序修改，请重新读取并核对后再重启 Codex。");
        }
        desktopRestartBusy = true;
        RestartCodexButton.IsEnabled = ClosePreviewButton.IsEnabled = ConfigurationSourcesButton.IsEnabled = false;
        DesktopRestartStatus.Visibility = Visibility.Visible;
        DesktopRestartStatus.Text = "正在查找 Codex 桌面端…";
        DesktopRestartStatus.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Muted");
        try
        {
            ValidateSaved();
            var desktop = new CodexDesktop();
            var target = smokeOutput is not null
                ? smokeDesktopTarget?.Invoke() ?? throw new InvalidOperationException("隔离检查不能操作真实 Codex 桌面端。")
                : await Task.Run(desktop.FindTarget, lifetime.Token);
            var confirmed = smokeOutput is not null ? smokeDesktopConfirm?.Invoke(target) == true : MessageBox.Show(this,
                (target.IsRunning ? "将请求 Codex 正常退出，再重新打开。请先完成正在运行的任务并保存编辑内容。若取消退出或程序未退出，Dock 会停止本次重启。\n\n" : "将打开 Codex 桌面端。\n\n") +
                "程序：\n" + target.Executable + "\n\n本次使用的配置目录：\n" + saved.Home + "\n\n重新打开后，请新建聊天核对配置。继续？",
                target.IsRunning ? "重启 Codex 并应用配置" : "打开 Codex 并应用配置", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
            if (!confirmed) { DesktopRestartStatus.Text = "已取消重启，配置仍已保存；可稍后手动重新打开 Codex。"; return; }
            DesktopRestartStatus.Text = target.IsRunning ? "正在等待 Codex 完全退出，然后重新打开…" : "正在使用已保存的配置打开 Codex…";
            if (smokeOutput is not null)
            {
                if (smokeDesktopRestart is null) throw new InvalidOperationException("隔离检查不能启动真实 Codex。");
                await smokeDesktopRestart(target, saved.Home, saved.AuthApiBaseUrl, ValidateSaved, lifetime.Token);
            }
            else await Task.Run(() => desktop.RestartAsync(target, saved.Home, saved.AuthApiBaseUrl, ValidateSaved, lifetime.Token), lifetime.Token);
            desktopRestarted = true;
            RestartCodexButton.Content = "已重新打开 Codex";
            DesktopRestartStatus.Text = "已确认 Codex 桌面进程重新打开。请新建聊天核对；项目、Profile 和组织要求仍可能覆盖此文件。";
            ReviewHint.Text = "本地配置已保存 · Codex 已重新打开 · 请在新聊天核对";
        }
        catch (OperationCanceledException) { DesktopRestartStatus.Text = "重启已取消；配置仍已保存，请手动打开 Codex 核对。"; }
        catch (Exception error)
        {
            DesktopRestartStatus.Text = "配置已保存，重启未完成：" + error.Message;
            DesktopRestartStatus.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Error");
        }
        finally
        {
            desktopRestartBusy = false;
            RestartCodexButton.IsEnabled = !desktopRestarted && !closing;
            ClosePreviewButton.IsEnabled = ConfigurationSourcesButton.IsEnabled = !closing;
        }
    }
}
