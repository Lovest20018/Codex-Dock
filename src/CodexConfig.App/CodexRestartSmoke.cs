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
            var changed = new TomlDocument(saved.Config); changed.Set("service_tier", "priority");
            File.WriteAllBytes(Path.Combine(fixture, "config.toml"), changed.Bytes); Reload();
            await RestartCodexFromReviewAsync();
            Check(launches == 0 && DesktopRestartStatus.Text.Contains("service_tier") && ReloadDesktopConfigurationButton.IsVisible,
                "Actual changes identify the setting and offer inline reload after a background refresh");
            await Settle(); Capture(output, "configuration-external-change.png");
            ReloadDesktopConfigurationClicked(this, new());
            Check(launches == 0 && DesktopConfigurationChanges.IsVisible && DesktopConfigurationChangesText.Text.Contains("priority") && RestartCodexButton.IsEnabled,
                "Reading latest files shows masked differences without launching or writing");
            await Settle(); Capture(output, "configuration-external-change-reviewed.png");
            File.WriteAllBytes(Path.Combine(fixture, "config.toml"), saved.Config!); Reload(saved); PrepareDesktopRestart();
            File.AppendAllText(Path.Combine(fixture, "config.toml"), "# harmless client formatting\n");
            smokeDesktopConfirm = _ => false;
            await RestartCodexFromReviewAsync();
            Check(launches == 0 && DesktopRestartStatus.Text.Contains("已取消") && !ReloadDesktopConfigurationButton.IsVisible,
                "Formatting alone does not create a stale-file conflict or write files");
            File.WriteAllBytes(Path.Combine(fixture, "config.toml"), saved.Config!); Reload(saved); PrepareDesktopRestart();
            smokeDesktopTarget = () => new(Path.Combine(fixture, "Codex.exe"), [new(123, 1, 456, Path.Combine(fixture, "Codex.exe"))]);
            smokeDesktopConfirm = _ => true;
            smokeDesktopRestart = (_, _, _, _, _) => throw new CodexDesktopExitTimeoutException("fixture desktop did not exit");
            await RestartCodexFromReviewAsync();
            Check(reviewSaved && RestartCodexButton.IsEnabled && ForceDesktopRestartButton.IsVisible && DesktopApplyStageText.Text.Contains("等待旧实例退出") && saved.SameContents(store.Load(fixture)),
                "A veto or tray timeout preserves files and offers a separately confirmed scoped fallback");
            await Settle(); Capture(output, "configuration-restart-failure.png");
            smokeDesktopConfirm = _ => false;
            await RestartCodexFromReviewAsync(forceTarget: blockedDesktopExit!.Value.Target);
            Check(launches == 0 && DesktopRestartStatus.Text.Contains("已取消"), "Declining scoped process termination never restarts or ends the desktop");
            smokeDesktopConfirm = _ => true;
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            smokeDesktopRestart = async (_, directory, authBase, validate, _) =>
            {
                Check(directory == fixture && authBase == saved.AuthApiBaseUrl, "Restart receives saved launch context");
                validate(); launches++; await release.Task;
            };
            var restart = RestartCodexFromReviewAsync();
            await RestartCodexFromReviewAsync();
            CloseOverlay();
            Check(desktopRestartBusy && PreviewOverlay.IsVisible && !ClosePreviewButton.IsEnabled && !RestartCodexButton.IsEnabled, "Pending restart blocks duplicate actions and closing its result");
            release.SetResult(); await restart;
            Check(launches == 1 && desktopRestarted && !RestartCodexButton.IsEnabled && DesktopRestartStatus.Text.Contains("新建聊天"), "Successful restart reports a new desktop process without claiming all session settings are active");
            await Settle(); Capture(output, "configuration-activation.png");
            checks.AddRange(["Saved result offers desktop restart", "Restart cancellation retains saved files", "Actual external changes identify paths after background reload", "External reload shows masked differences before another launch", "Formatting-only changes do not block applying", "Veto and tray timeout offer scoped fallback without automatically ending processes", "Declining the force fallback leaves the captured instance intact", "Restart blocks duplicate and closing actions while pending", "Restart success reports the desktop process and asks to verify a new chat"]);
        }
        finally { smokeDesktopTarget = null; smokeDesktopConfirm = null; smokeDesktopRestart = null; }
        await RunDesktopApplicationStagesSmokeAsync(output, checks);
    }

    private async Task RunDesktopApplicationStagesSmokeAsync(string output, List<string> checks)
    {
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
        var directory = Path.Combine(output, "staged-apply-home-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "config.toml"), "model='staged-model'\nopenai_base_url='https://relay.invalid/backend-api/codex'\n");
        File.WriteAllText(Path.Combine(directory, "auth.json"), new ConnectionSettings("https://relay.invalid", "fake-staged-ui-key", "staged-model").RenderAuth());
        var memory = new IsolatedGatewayEnvironment(); var isolatedStore = new ConfigStore(memory, _ => null);
        var window = new MainWindow(isolatedStore, directory, output, memory); window.Loaded -= window.WindowLoaded;
        var launches = 0; var attachments = 0;
        var target = new CodexDesktopTarget(Path.Combine(directory, "fixture-codex.exe"), [new(987, 1, 654, Path.Combine(directory, "fixture-codex.exe"))]);
        try
        {
            window.Show(); window.ShowPage("overview");
            window.smokeDesktopTarget = () => target;
            window.smokeDesktopConfirm = _ => true;
            window.smokeDesktopRestart = (_, _, _, validate, _) => { validate(); launches++; return Task.CompletedTask; };
            window.SpeedPreferences().Save(true); window.RefreshSpeedSettings();
            window.smokeSpeedPrepare = (_, _) => Task.FromResult<(DesktopSpeedPlan?, int?)>((new([], new Dictionary<string, int>()), 12345));
            window.smokeSpeedAttach = () => { attachments++; throw new IOException("fixture compatibility unavailable"); };
            var unchanged = isolatedStore.Load(directory);
            window.OpenDesktopApplicationPanel(); await window.RestartCodexFromReviewAsync();
            Check(launches == 1 && attachments == 1 && !window.desktopRestarted && window.pendingSpeedRetry is not null &&
                window.DesktopApplyStageText.Text.Contains("已打开") && window.RestartCodexButton.Content.ToString() == "重试速度兼容",
                "A failed enhancement preserves confirmed desktop startup and exposes enhancement-only retry");
            window.Width = 960; window.Height = 640;
            await window.Settle(); window.Capture(output, "configuration-opened-compatibility-pending.png");
            ThemeManager.Apply(true); window.RootPanel.LayoutTransform = new System.Windows.Media.ScaleTransform(1.25, 1.25);
            await window.Settle(); window.Capture(output, "configuration-opened-compatibility-dark-scaled.png");
            Check(window.RestartCodexButton.IsVisible && window.ReviewSuccess.IsVisible && window.ClosePreviewButton.IsVisible,
                "The shared application result remains usable at the minimum window size with dark theme and scaling");
            window.RootPanel.LayoutTransform = System.Windows.Media.Transform.Identity; ThemeManager.Apply(false);
            window.smokeSpeedAttach = () => { attachments++; return Task.CompletedTask; };
            await window.RestartCodexFromReviewAsync();
            Check(launches == 1 && attachments == 2 && window.desktopRestarted && window.pendingSpeedRetry is null && unchanged.SameContents(isolatedStore.Load(directory)),
                "Enhancement retry does not restart the desktop or rewrite saved configuration");
            window.CloseOverlay(); window.Reload(); window.SpeedPreferences().Save(false); window.RefreshSpeedSettings();
            window.OpenDesktopApplicationPanel();
            var identityAuth = System.Text.Encoding.UTF8.GetBytes("{\"auth_mode\":\"chatgpt\",\"tokens\":{\"access_token\":\"fake-access\",\"refresh_token\":\"fake-refresh\",\"id_token\":\"fake-id\"}}");
            var settings = new ConnectionSettings("https://mixed.invalid/v1", "fake-mixed-key", "staged-model") { Kind = ConnectionKind.ResponsesApi, Mode = ConnectionMode.OfficialWithApi };
            isolatedStore.Apply(isolatedStore.Plan(isolatedStore.Load(directory), settings, officialAuth: identityAuth));
            window.Reload(); window.PrepareDesktopRestart();
            var bridgeChecked = false;
            window.smokeDesktopGateway = _ => { bridgeChecked = true; throw new IOException("fixture gateway unavailable"); };
            await window.RestartCodexFromReviewAsync();
            Check(bridgeChecked && launches == 1 && window.DesktopRestartStatus.Text.Contains("gateway unavailable"),
                "Mixed gateway startup failure leaves the existing Codex instance untouched");
            window.smokeDesktopGateway = saved => { Check(saved.Home == directory && saved.Bridge is not null, "Mixed launch prepares the saved connection for the selected home"); return Task.CompletedTask; };
            window.smokeDesktopRestart = (_, _, _, validate, _) => { Check(bridgeChecked, "The model gateway is prepared before desktop shutdown"); validate(); launches++; return Task.CompletedTask; };
            await window.RestartCodexFromReviewAsync();
            Check(launches == 2 && window.desktopRestarted, "Mixed configuration applies through the same visible launch flow");
            await window.Settle(); window.Capture(output, "configuration-stages-mixed-complete.png");
        }
        finally { ThemeManager.Apply(false); window.Close(); }
    }
}
