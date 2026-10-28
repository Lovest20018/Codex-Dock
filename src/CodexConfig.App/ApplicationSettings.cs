using System.Windows;
using CodexConfig.Core;

namespace CodexConfig.App;

public partial class MainWindow
{
    private bool updatingStartup;
    private readonly IStartupRegistration startup;
    internal bool StartInTray { get; init; }
    internal void ApplyStartupVisibility() { if (StartInTray && tray is not null) Hide(); }
    private void RefreshStartupSetting()
    {
        updatingStartup = true;
        try
        {
            var command = startup.Read();
            AutoStartBox.IsChecked = StartupCommand.Targets(command, Environment.ProcessPath!);
            AutoStartHint.Text = command is null ? "已关闭。" : AutoStartBox.IsChecked == true
                ? "已登记当前程序。若 Windows 已禁用此启动项，请在系统的“启动应用”设置中启用。"
                : "启动项指向另一位置的 Codex Dock。开启后将改用当前程序与配置目录。";
            AutoStartHint.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Muted");
        }
        catch (Exception) { AutoStartBox.IsEnabled = false; AutoStartHint.Text = "无法读取开机启动设置，请检查当前用户权限。"; }
        finally { updatingStartup = false; }
    }
    private void AutoStartChanged(object sender, RoutedEventArgs e)
    {
        if (initializing || updatingStartup) return;
        try
        {
            startup.Write(AutoStartBox.IsChecked == true ? StartupCommand.Create(Environment.ProcessPath!, home) : null);
            RefreshStartupSetting();
        }
        catch (Exception error)
        {
            RefreshStartupSetting();
            AutoStartHint.Text = error is ArgumentException ? error.Message : "开机启动设置未能保存，请检查当前用户权限。";
            AutoStartHint.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Error");
        }
    }

    private async Task RunApplicationSettingsSmokeAsync(string output, List<string> checks)
    {
        var realStartup = new WindowsStartupRegistration().Read();
        var originalHome = home;
        try
        {
            // Keep this command short even when the installer fixture uses long paths.
            home = System.IO.Path.Combine(System.IO.Path.GetPathRoot(home)!, "Dock startup fixture");
            var wasDirty = Dirty;
            ShowPage("app"); AutoStartBox.IsChecked = true;
            if (startup.Read() != StartupCommand.Create(Environment.ProcessPath!, home) || Dirty != wasDirty)
                throw new InvalidOperationException("Startup preference must apply immediately outside the configuration draft.");
            RefreshStartupSetting();
            if (AutoStartBox.IsChecked != true) throw new InvalidOperationException("Startup preference was not reloaded.");
            await Settle(); Capture(output, "application-startup.png");
            AutoStartBox.IsChecked = false;
            if (startup.Read() is not null) throw new InvalidOperationException("Startup registration was not removed.");
            if (new WindowsStartupRegistration().Read() != realStartup) throw new InvalidOperationException("UI test changed the user's startup registration.");
            checks.AddRange(["Startup toggle applies and reloads outside configuration draft", "Startup toggle disables its registration without changing real user Run entry"]);
        }
        finally { home = originalHome; }

        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var instanceName = "test-" + Guid.NewGuid().ToString("N");
        using (var primary = new DesktopInstance(() => signal.TrySetResult(), false, instanceName))
        {
            using (var quiet = new DesktopInstance(() => { }, true, instanceName))
                if (!primary.IsPrimary || quiet.IsPrimary || signal.Task.IsCompleted) throw new InvalidOperationException("Logon relaunch should keep the existing instance quiet.");
            using var manual = new DesktopInstance(() => { }, false, instanceName);
            await signal.Task.WaitAsync(TimeSpan.FromSeconds(3));
            if (manual.IsPrimary) throw new InvalidOperationException("Manual relaunch should restore the existing instance.");
        }
        checks.Add("Manual relaunch signals tray instance; logon relaunch stays quiet");

        var fixture = new MainWindow(store, home, output, environment) { smokeLifecycle = true, StartInTray = true };
        fixture.Loaded -= fixture.WindowLoaded; fixture.Show(); fixture.InitializeTray(); fixture.Reload(); fixture.ApplyStartupVisibility();
        if (fixture.tray is null || fixture.IsVisible) throw new InvalidOperationException("Logon startup should hide only after tray creation succeeds.");
        fixture.RestoreWindow(); fixture.RequestExit();
        for (var attempt = 0; attempt < 100 && !fixture.exitReady; attempt++) await Task.Delay(100);
        if (!fixture.exitReady) throw new InvalidOperationException("Startup tray fixture did not exit.");
        checks.Add("Logon startup hides to tray and restores through the normal window lifecycle");
    }

    private async Task RunConnectionDetailsSmokeAsync(string output, List<string> checks)
    {
        ShowPage("connection"); ConnectionPage.ScrollToTop();
        var before = store.Load(home);
        ConnectionNameBox.Text = "我的自定义连接"; ConnectionNoteBox.Text = "工作使用\r\n备用模型入口";
        ConnectionNoteExpander.IsExpanded = true; UpdatePreview();
        if (!Dirty || preview?.Details is null || !RenderChanges().Contains("连接名称与备注")) throw new InvalidOperationException("Connection labels missing from draft preview.");
        if (!SaveCurrent()) throw new InvalidOperationException("Connection label save failed.");
        var saved = store.Load(home);
        if (loadedSettings.Details.Name != "我的自定义连接" || loadedSettings.Details.Note != "工作使用\n备用模型入口" || Dirty ||
            !saved.Config!.SequenceEqual(before.Config!) || !saved.Auth!.SequenceEqual(before.Auth!) || !saved.Bridge!.SequenceEqual(before.Bridge!))
            throw new InvalidOperationException("Label-only save changed model routing or credentials.");
        ShowPage("overview"); await Settle(); Capture(output, "connection-named-overview.png");
        if (!FindVisual<System.Windows.Controls.TextBlock>(OverviewBody).Any(text => text.Text == "我的自定义连接")) throw new InvalidOperationException("Overview omitted saved connection name.");
        ShowPage("connection"); ConnectionPage.ScrollToTop(); await Settle(); Capture(output, "connection-named.png");
        ConnectionModeBox.SelectedIndex = (int)ConnectionMode.Direct;
        KindBox.SelectedIndex = (int)ConnectionKind.PatRelay; AuthenticationBox.SelectedIndex = (int)AuthenticationChoice.Pat;
        if (OfficialLoginPanel.Visibility != Visibility.Collapsed || !ConnectionModeHint.Text.Contains("无需登录 ChatGPT") || !SaveCurrent())
            throw new InvalidOperationException("Direct PAT mode requires no official login UI.");
        using (var auth = System.Text.Json.JsonDocument.Parse(store.Load(home).Auth!))
            if (!auth.RootElement.TryGetProperty("personal_access_token", out _) || auth.RootElement.TryGetProperty("tokens", out _) || store.Load(home).Bridge is not null)
                throw new InvalidOperationException("Direct PAT switch did not replace OAuth routing.");
        if (loadedSettings.Details.Name != "我的自定义连接") throw new InvalidOperationException("Mode switch lost labels.");
        ShowPage("connection"); ConnectionPage.ScrollToTop(); await Settle(); Capture(output, "connection-pat-named.png");
        store.Restore(store.Load(home)); Reload();
        checks.AddRange(["Name and multiline notes save and reload without rewriting credentials or routing", "Overview displays saved connection name", "Mixed mode switches to PAT without ChatGPT login and preserves labels"]);
    }
}
