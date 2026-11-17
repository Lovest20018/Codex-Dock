using System.IO;
using System.Text.Json;
using System.Windows;
using CodexConfig.Core;

namespace CodexConfig.App;

public partial class MainWindow
{
    private async Task RunConnectionRecoveryUiSmokeAsync(string output)
    {
        Directory.CreateDirectory(output); var checks = new List<string>();
        await RunConnectionRecoveryChecksAsync(output, checks);
        await File.WriteAllTextAsync(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new { status = "PASS", tests = checks }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private async Task RunConnectionRecoveryChecksAsync(string output, List<string> checks)
    {
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks.Add(name); }
        var directory = Path.Combine(output, "recovery-home-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "config.toml"), "model='fixture-model'\nopenai_base_url='https://fixture.invalid/v1'\n");
        File.WriteAllText(Path.Combine(directory, "auth.json"), new ConnectionSettings("https://fixture.invalid/v1", "fake-recovery-key", "fixture-model").RenderAuth());
        var memory = new IsolatedGatewayEnvironment(); var fixtureStore = new ConfigStore(memory, _ => null);
        var window = new MainWindow(fixtureStore, directory, output, memory); window.Loaded -= window.WindowLoaded;
        try
        {
            window.Show(); window.ShowPage("enhancements"); await window.Settle();
            var before = fixtureStore.Load(directory);
            Check(window.ConnectionRecoveryBox.IsChecked == false && !window.ApplyDesktopEnhancementsButton.IsEnabled && !window.Dirty, "Old chat recovery defaults off on the shared client enhancement page");
            Check(window.EnhancementRuntimeHint.Text.Contains("无需启动模型网关") && !window.GatewaySummaryBar.IsVisible && window.EnhancementsNav.IsChecked == true,
                "Client enhancement guidance distinguishes Dock background runtime from model gateway services");
            window.ConnectionRecoveryBox.IsChecked = true; await window.Settle();
            Check(window.ConnectionPreferences().Load() && window.ApplyDesktopEnhancementsButton.IsEnabled && before.SameContents(fixtureStore.Load(directory)) && !window.Dirty, "Recovery opt-in saves separately without changing credentials or connection drafts");
            Check(!window.SpeedPreferences().Load() && window.ConnectionRecoveryStatus.Text.Contains("尚未应用"), "Old chat recovery applies independently of API speed compatibility");
            window.ModelBox.Text = "draft-model"; await window.Settle();
            Check(!window.ApplyDesktopEnhancementsButton.IsEnabled && window.ConnectionRecoveryStatus.Text.Contains("草稿"), "Unconfirmed drafts block the recovery application action");
            window.Reload();
            Check(window.ConnectionRecoveryBox.IsChecked == true && window.ApplyDesktopEnhancementsButton.IsEnabled, "Saved recovery preference survives configuration reload");
            var launches = 0; var attachments = 0; var target = new CodexDesktopTarget(Path.Combine(directory, "fixture-codex.exe"), []);
            window.smokeDesktopTarget = () => target; window.smokeDesktopConfirm = _ => true;
            window.smokeDesktopRestart = (_, _, _, validate, _) => { validate(); launches++; return Task.CompletedTask; };
            window.smokeSpeedPrepare = (_, _) => Task.FromResult<(DesktopSpeedPlan?, int?)>((new([], new Dictionary<string, int>()) { SpeedEnabled = false, ConnectionsEnabled = true }, 12345));
            window.smokeSpeedAttach = () => { attachments++; return Task.CompletedTask; };
            window.OpenDesktopApplicationPanel(); await window.RestartCodexFromReviewAsync();
            Check(launches == 1 && attachments == 1 && window.desktopRestarted && before.SameContents(fixtureStore.Load(directory)), "Recovery uses the shared confirmed restart flow without rewriting saved configuration");
            Check(window.DesktopApplyStageText.Text.Contains("可继续旧聊天") && window.SpeedCompatibilityStatus.Text == "已关闭。", "Recovery success reports its own state without claiming that API speed was enabled");
            window.CloseOverlay(); window.ConnectionRecoveryBox.IsChecked = false; await window.Settle();
            Check(!window.ConnectionPreferences().Load() && window.ConnectionRecoveryStatus.Text == "已关闭。", "Turning recovery off reports the saved setting immediately");
            window.ConnectionRecoveryBox.IsChecked = true;
            window.home = Path.Combine(directory, "other"); Directory.CreateDirectory(window.home); window.Reload();
            Check(window.ConnectionRecoveryBox.IsChecked == false, "Recovery is independently scoped to each configuration directory");
            window.home = directory; window.Reload(); window.ShowPage("enhancements");
            window.Width = 960; window.Height = 640; window.ConnectionRecoveryPanel.BringIntoView();
            await window.Settle(); window.Capture(output, "connection-recovery-compact.png");
            Check(window.ConnectionRecoveryBox.IsVisible && window.ApplyDesktopEnhancementsButton.IsVisible, "Recovery controls and the shared application action remain available at the compact window size");
            window.EnhancementsScroll.ScrollToTop(); await window.Settle(); window.Capture(output, "client-enhancements-compact.png");
            ThemeManager.Apply(true); window.RootPanel.LayoutTransform = new System.Windows.Media.ScaleTransform(1.25, 1.25);
            await window.Settle(); window.ConnectionRecoveryPanel.BringIntoView(); await window.Settle(); window.Capture(output, "connection-recovery-dark-scaled.png");
            var applyBounds = window.ApplyDesktopEnhancementsButton.TransformToAncestor(window.RootPanel).TransformBounds(new Rect(window.ApplyDesktopEnhancementsButton.RenderSize));
            Check(applyBounds.Bottom <= window.RootPanel.ActualHeight + 1 && applyBounds.Right <= window.RootPanel.ActualWidth + 1 && !window.GlobalSaveBar.IsVisible,
                "The shared enhancement action stays in view at compact size and scaling without a duplicate save bar");
            ThemeManager.Apply(false); window.RootPanel.LayoutTransform = System.Windows.Media.Transform.Identity;
            window.EnhancementStartupClicked(window, new()); await window.Settle();
            Check(window.currentPage == "app" && window.AutoStartBox.IsVisible, "Client enhancement page links to Dock tray startup without enabling a gateway");
            window.SpeedSettingsClicked(window, new()); await window.Settle();
            Check(window.currentPage == "enhancements" && window.EnhancementsNav.IsChecked == true, "Existing feature links open the unified client enhancement page");
            File.WriteAllText(window.ConnectionPreferences().SettingsPath, "{\"enabled\":\"invalid\"}"); window.RefreshConnectionRecovery();
            Check(window.ConnectionRecoveryStatus.Text.Contains("无法读取"), "Damaged recovery settings produce a readable failure state");
        }
        finally { ThemeManager.Apply(false); window.Close(); }
    }
}
