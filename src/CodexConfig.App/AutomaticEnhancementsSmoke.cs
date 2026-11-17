using System.IO;
using System.Text.Json;
using System.Windows;
using CodexConfig.Core;
using CodexConfig.Application;

namespace CodexConfig.App;

public partial class MainWindow
{
    private async Task RunAutomaticEnhancementsSmokeAsync(string output)
    {
        var checks = new List<string>(); await RunAutomaticEnhancementsChecksAsync(output, checks);
        await File.WriteAllTextAsync(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new { status = "PASS", tests = checks }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private async Task RunAutomaticEnhancementsChecksAsync(string output, List<string> checks)
    {
        void Check(bool value, string text) { if (!value) throw new InvalidOperationException(text); checks.Add(text); }
        var directory = Path.Combine(output, "automatic-home"); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "config.toml"), "model='automatic-fixture'\n");
        var environment = new IsolatedGatewayEnvironment(); var isolated = new ConfigStore(environment, _ => null);
        var window = new MainWindow(isolated, directory, output, environment); window.Loaded -= window.WindowLoaded;
        var attaches = 0; var launches = 0;
        try
        {
            window.Show(); window.ShowPage("enhancements"); window.desktopInitializationReady = true; await window.Settle();
            var before = isolated.Load(directory);
            Check(window.AutomaticEnhancementsBox.IsChecked == false && !window.CreateEnhancedShortcutButton.IsEnabled, "Enhanced launch and automatic reconnect default off without touching the service configuration");
            window.ConnectionRecoveryBox.IsChecked = true;
            window.AutomaticEnhancementsBox.IsChecked = true; await window.Settle();
            Check(window.EnhancementPreferences().Load().Enabled && !window.Dirty && before.SameContents(isolated.Load(directory)) && window.CreateEnhancedShortcutButton.IsEnabled,
                "Automatic option persists separately and exposes the shortcut action after an enhancement is chosen");
            var preferences = window.EnhancementPreferences();
            preferences.Remember(new(Path.Combine(directory, "fixture.exe"), directory, 42, 100, 12345, DesktopEnhancementSettings.SnapshotHash(isolated.Load(directory)),
                DesktopSpeedSession.PlanHash(new([], new Dictionary<string, int>()) { SpeedEnabled = false, ConnectionsEnabled = true })));
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            window.smokeAutomaticAttach = async () => { attaches++; entered.TrySetResult(); return await release.Task; };
            var first = window.TryAutomaticEnhancementsAsync(); var second = window.TryAutomaticEnhancementsAsync();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check(attaches == 1 && ReferenceEquals(first, second) && !window.ApplyDesktopEnhancementsButton.IsEnabled, "Repeated refreshes share one reconnect operation and disable duplicate launch actions");
            release.SetResult(true); await first;
            Check(window.AutomaticEnhancementsStatus.Text.Contains("已自动接回") && launches == 0 && before.SameContents(isolated.Load(directory)), "Automatic reconnect reports attachment without restarting a desktop or rewriting configuration");
            window.smokeCreateEnhancedShortcut = (path, executable, savedHome) => { Check(savedHome == directory && Path.IsPathFullyQualified(executable), "Shortcut creation uses the active saved configuration directory"); File.WriteAllText(path, EnhancedLaunchShortcut.Arguments(savedHome)); };
            var shortcut = Path.Combine(directory, "fixture.lnk"); window.SaveEnhancedShortcut(shortcut);
            Check(File.ReadAllText(shortcut).Contains("--enhanced-launch") && window.AutomaticEnhancementsStatus.Text.Contains("已创建"), "Shortcut result explains the explicit enhanced launch action");
            window.ModelBox.Text = "unsaved-model"; await window.TryAutomaticEnhancementsAsync();
            Check(attaches == 1 && window.Dirty, "A configuration draft blocks automatic reconnect without discarding edits");
            window.Reload(); window.AutomaticEnhancementsBox.IsChecked = false; await window.Settle(); await window.TryAutomaticEnhancementsAsync();
            window.automaticEnhancements.Cancel();
            Check(!preferences.Load().Enabled && attaches == 1 && !window.autoEnhancementBusy, "Disabling automatic reconnect prevents future attempts and leaves no disposed cancellation source");
            await window.RequestEnhancedLaunchAsync(directory);
            Check(window.currentPage == "enhancements" && launches == 0 && !preferences.Load().Enabled, "An existing shortcut respects the disabled option and returns to its settings");
            window.AutomaticEnhancementsBox.IsChecked = true; await window.Settle();
            window.smokeAutomaticAttach = () => Task.FromResult(false);
            window.smokeDesktopTarget = () => new(Path.Combine(directory, "fixture.exe"), []);
            var confirmations = 0;
            window.smokeDesktopConfirm = _ => { confirmations++; return false; };
            window.smokeDesktopRestart = (_, _, _, validate, _) => { validate(); launches++; return Task.CompletedTask; };
            window.smokeSpeedPrepare = (_, _) => Task.FromResult<(DesktopSpeedPlan?, int?)>((new([], new Dictionary<string, int>()) { SpeedEnabled = false, ConnectionsEnabled = true }, 12345));
            window.smokeSpeedAttach = () => Task.CompletedTask;
            await window.RequestEnhancedLaunchAsync(directory);
            Check(launches == 1 && confirmations == 0 && window.desktopRestarted && before.SameContents(isolated.Load(directory)), "An explicit enhanced shortcut starts an absent desktop once without a redundant restart prompt");
            window.home = Path.Combine(directory, "other"); Directory.CreateDirectory(window.home); window.Reload();
            Check(window.AutomaticEnhancementsBox.IsChecked == false && window.EnhancementPreferences().Load().Binding is null, "Automatic options and remembered desktop remain isolated per configuration directory");
            var otherHome = window.home;
            window.home = directory; window.Reload();
            window.mobilePassword.Password = "private-fixture-password"; window.mobileUrl.Text = "https://old-fixture.invalid";
            await window.RequestEnhancedLaunchAsync(otherHome);
            Check(window.home == otherHome && window.mobilePassword.Password.Length == 0 && window.mobileUrl.Text.Length == 0 && launches == 1,
                "A shortcut for another directory clears stale phone fields and respects that directory's disabled option");
            window.SwitchConfigurationHome(directory);
            var preferenceBytes = File.ReadAllBytes(preferences.SettingsPath);
            File.WriteAllText(preferences.SettingsPath, "{\"Enabled\":\"invalid\"}");
            await window.RequestEnhancedLaunchAsync(directory);
            Check(window.StatusText.Text.Contains("增强启动未完成") && launches == 1, "Damaged automatic settings report an error without launching or terminating the application");
            File.WriteAllBytes(preferences.SettingsPath, preferenceBytes);
            window.automaticEnhancementStatus = "已开启。通过增强快捷方式打开 Codex；重开 Dock 后自动核对并接回。";
            window.home = directory; window.Reload(); window.ShowPage("enhancements"); window.Width = 960; window.Height = 640;
            window.EnhancementsScroll.ScrollToTop(); await window.Settle(); window.Capture(output, "automatic-enhancements-compact.png");
            ThemeManager.Apply(true); window.RootPanel.LayoutTransform = new System.Windows.Media.ScaleTransform(1.25, 1.25);
            await window.Settle(); window.Capture(output, "automatic-enhancements-dark-scaled.png");
            Check(window.ApplyDesktopEnhancementsButton.IsVisible && window.AutomaticEnhancementsBox.IsVisible, "Automatic options and the fixed launch action stay available in the compact scaled layout");
            var scope = "enhanced-" + Guid.NewGuid().ToString("N");
            var request = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (var primary = new DesktopInstance(() => { }, true, scope, value => request.TrySetResult(value)))
            using (var secondary = new DesktopInstance(() => { }, true, scope))
            {
                await secondary.SendEnhancedLaunchAsync(directory);
                Check(await request.Task.WaitAsync(TimeSpan.FromSeconds(3)) == directory && primary.IsPrimary && !secondary.IsPrimary,
                    "Enhanced shortcut forwards its directory to the existing Dock instance through a current-user pipe");
            }
        }
        finally { ThemeManager.Apply(false); window.Close(); }
    }
}
