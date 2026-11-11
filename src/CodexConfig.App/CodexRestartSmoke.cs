using System.IO;
using System.Text.Json;
using System.Windows;
using CodexConfig.Core;

namespace CodexConfig.App;

public partial class MainWindow
{
    private async Task RunDesktopRestartUiSmokeAsync(string output)
    {
        // This focused check starts with an already-saved plaintext fixture. Full smoke also verifies the encrypted save.
        Directory.CreateDirectory(output);
        Directory.CreateDirectory(home);
        File.WriteAllText(Path.Combine(home, "config.toml"), "model='saved-restart-fixture'\n");
        if (!Reload()) throw new InvalidOperationException("Saved restart fixture must load.");
        Width = 960; Height = 640;
        RenderSavedConfigurationReview(1, "本次写入目标：\n" + Path.Combine(home, "config.toml"));
        OpenOverlay(PreviewOverlay, ClosePreviewButton);
        var checks = new List<string>();
        await RunDesktopRestartReviewSmokeAsync(output, checks);
        ViewConfigurationSourcesClicked(this, new()); await Settle();
        if (currentPage != "catalog" || !configurationTools.IsExpanded || PreviewOverlay.IsVisible)
            throw new InvalidOperationException("Saved restart result must link to configuration sources.");
        checks.Add("Configuration sources are accessible from the saved result");
        await File.WriteAllTextAsync(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new { status = "PASS", scope = "desktop-restart UI only; encrypted save is covered by full smoke", tests = checks }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private async Task RunDesktopRestartReviewSmokeAsync(string output, List<string> checks)
    {
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        var saved = snapshot ?? throw new InvalidOperationException("Restart fixture must be saved.");
        var fixture = saved.Home; var launches = 0;
        try
        {
            await Settle(); Capture(output, "configuration-activation-ready.png");
            Check(ReviewSuccess.IsVisible && RestartCodexButton.IsEnabled, "Saved result presents the desktop restart action");
            smokeDesktopTarget = () => new(Path.Combine(fixture, "Codex.exe"), []);
            smokeDesktopConfirm = _ => false;
            smokeDesktopRestart = (_, directory, authBase, validate, _) =>
            {
                Check(directory == fixture && authBase == saved.AuthApiBaseUrl, "Restart uses the saved home and authentication environment");
                validate(); launches++; return Task.CompletedTask;
            };
            await RestartCodexFromReviewAsync();
            Check(launches == 0 && reviewSaved && RestartCodexButton.IsEnabled && DesktopRestartStatus.Text.Contains("已取消"), "Cancelling restart retains the saved configuration");
            smokeDesktopConfirm = _ => true;
            File.AppendAllText(Path.Combine(fixture, "config.toml"), "# external edit after save\n"); Reload();
            await RestartCodexFromReviewAsync();
            Check(launches == 0 && DesktopRestartStatus.Text.Contains("已被其他程序修改"), "Restart still compares the saved result after background reload changes the current snapshot");
            File.WriteAllBytes(Path.Combine(fixture, "config.toml"), saved.Config!); Reload(saved);
            smokeDesktopRestart = (_, _, _, _, _) => throw new TimeoutException("fixture desktop did not exit");
            await RestartCodexFromReviewAsync();
            Check(reviewSaved && RestartCodexButton.IsEnabled && DesktopRestartStatus.Text.Contains("重启未完成") && saved.SameContents(store.Load(fixture)), "Restart failure keeps the saved files and permits a retry");
            await Settle(); Capture(output, "configuration-restart-failure.png");
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            smokeDesktopRestart = async (_, directory, authBase, validate, _) =>
            {
                Check(directory == fixture && authBase == saved.AuthApiBaseUrl, "Restart receives saved launch context");
                validate(); launches++; await release.Task;
            };
            var restart = RestartCodexFromReviewAsync();
            CloseOverlay();
            Check(desktopRestartBusy && PreviewOverlay.IsVisible && !ClosePreviewButton.IsEnabled && !RestartCodexButton.IsEnabled, "Pending restart blocks duplicate actions and closing its result");
            release.SetResult(); await restart;
            Check(launches == 1 && desktopRestarted && !RestartCodexButton.IsEnabled && DesktopRestartStatus.Text.Contains("新建聊天"), "Successful restart reports a new desktop process without claiming all session settings are active");
            await Settle(); Capture(output, "configuration-activation.png");
            checks.AddRange(["Saved result offers desktop restart", "Restart cancellation retains saved files", "External changes after save block restart even after background reload", "Restart failure permits retry without changing files", "Restart blocks duplicate and closing actions while pending", "Restart success reports the desktop process and asks to verify a new chat"]);
        }
        finally { smokeDesktopTarget = null; smokeDesktopConfirm = null; smokeDesktopRestart = null; }
    }
}
