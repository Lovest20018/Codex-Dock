using System.IO;
using System.Text.Json;
using System.Windows;
using CodexConfig.Core;

namespace CodexConfig.App;

public partial class MainWindow
{
    private async Task RunDesktopSpeedUiSmokeAsync(string output)
    {
        Directory.CreateDirectory(output); var checks = new List<string>();
        await RunDesktopSpeedChecksAsync(output, checks);
        await File.WriteAllTextAsync(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new { status = "PASS", tests = checks }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private async Task RunDesktopSpeedChecksAsync(string output, List<string> checks)
    {
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
        var directory = Path.Combine(output, "speed-home-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "config.toml"), "model='speed-model'\nopenai_base_url='https://relay.invalid/backend-api/codex'\n");
        File.WriteAllText(Path.Combine(directory, "auth.json"), new ConnectionSettings("https://relay.invalid", "fake-speed-ui-key", "speed-model").RenderAuth());
        var memory = new IsolatedGatewayEnvironment(); var fixtureStore = new ConfigStore(memory, _ => null);
        var fixture = new MainWindow(fixtureStore, directory, output, memory); fixture.Loaded -= fixture.WindowLoaded;
        try
        {
            fixture.Show(); fixture.ShowPage("preferences"); await fixture.Settle(); var before = fixtureStore.Load(directory);
            Check(fixture.SpeedCompatibilityBox.IsChecked == false && !fixture.ApplySpeedCompatibilityButton.IsEnabled && !fixture.Dirty, "Speed compatibility defaults off without changing the connection");
            fixture.SpeedCompatibilityBox.IsChecked = true; await fixture.Settle();
            Check(fixture.SpeedPreferences().Load() && fixture.ApplySpeedCompatibilityButton.IsEnabled && !fixture.Dirty && before.SameContents(fixtureStore.Load(directory)), "Speed opt-in saves outside the configuration draft and offers explicit application");
            fixture.SetDesktopRestartStatus("Codex 已打开，正在连接并加载速度兼容…");
            Check(fixture.SpeedCompatibilityStatus.Text.Contains("正在连接并加载") && fixture.SpeedCompatibilityCard.IsVisible,
                "Desktop startup progress is visible on the speed card without opening a review");
            fixture.SetDesktopRestartStatus("Codex 已重新打开，速度兼容已应用。");
            Check(fixture.SpeedCompatibilityStatus.Text.Contains("已应用"), "Successful application feedback remains visible on the speed card");
            fixture.speedFeedback = null;
            fixture.ModelBox.Text = "draft-speed-model";
            Check(!fixture.ApplySpeedCompatibilityButton.IsEnabled && fixture.SpeedCompatibilityStatus.Text.Contains("草稿"), "Unconfirmed drafts block desktop speed application immediately");
            fixture.Reload();
            Check(fixture.SpeedCompatibilityBox.IsChecked == true && fixture.ApplySpeedCompatibilityButton.IsEnabled && !fixture.Dirty, "Speed opt-in reloads for the current configuration directory");
            fixture.smokeDesktopTarget = () => new(Path.Combine(directory, "fixture-codex.exe"), []);
            await fixture.RestartCodexFromReviewAsync(true);
            Check(fixture.SpeedCompatibilityStatus.Text.Contains("隔离界面检查") && fixture.speedSession is null && before.SameContents(fixtureStore.Load(directory)), "Unsupported or isolated application reports failure without claiming a patched desktop");
            Check(fixture.PreviewOverlay.IsVisible && fixture.DesktopApplyStageText.Text == "应用未完成", "Speed application exposes the shared result panel and preserves its failure stage");
            fixture.CloseOverlay();
            fixture.SpeedCompatibilityBox.IsChecked = false; await fixture.Settle();
            Check(!fixture.SpeedPreferences().Load() && fixture.SpeedCompatibilityStatus.Text == "已关闭。", "Turning compatibility off clears the pending application state");
            fixture.SpeedCompatibilityBox.IsChecked = true;
            var other = Path.Combine(directory, "other"); Directory.CreateDirectory(other); fixture.home = other; fixture.Reload();
            Check(fixture.SpeedCompatibilityBox.IsChecked == false && !fixture.ApplySpeedCompatibilityButton.IsEnabled, "Other configuration directories keep an independent default-off preference");
            fixture.home = directory; fixture.Reload();
            File.WriteAllText(Path.Combine(directory, "auth.json"), "{\"auth_mode\":\"chatgpt\",\"tokens\":{\"access_token\":\"fake-speed-official\"}}"); fixture.Reload();
            Check(fixture.SpeedCompatibilityBox.IsChecked == true && !fixture.ApplySpeedCompatibilityButton.IsEnabled && fixture.SpeedCompatibilityStatus.Text.Contains("不适用"), "Official authentication does not inherit local API speed permission");
            File.WriteAllBytes(Path.Combine(directory, "auth.json"), before.Auth!); fixture.Reload();
            fixture.Width = 960; fixture.Height = 640; fixture.ShowPage("preferences"); fixture.SpeedCompatibilityCard.BringIntoView();
            await fixture.Settle(); fixture.Capture(output, "desktop-speed-compact.png");
            Check(fixture.SpeedCompatibilityCard.IsVisible && fixture.ApplySpeedCompatibilityButton.IsEnabled, "Speed settings remain accessible at the minimum window size");
            File.WriteAllText(fixture.SpeedPreferences().SettingsPath, "{\"enabled\":\"broken\"}"); fixture.RefreshSpeedSettings();
            Check(!fixture.SpeedCompatibilityBox.IsEnabled && !fixture.ApplySpeedCompatibilityButton.IsEnabled && fixture.SpeedCompatibilityStatus.Text.Contains("无法读取"), "Invalid preference files fail closed and remain available for diagnosis");
        }
        finally { fixture.Close(); }
    }
}
