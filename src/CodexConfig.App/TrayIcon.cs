using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace CodexConfig.App;

internal sealed record TrayGatewayState(bool? MobileRunning, bool? ModelRunning);

internal sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon icon;
    private readonly Forms.ContextMenuStrip menu = new();
    private readonly Drawing.Icon image;
    private readonly Func<Task<TrayGatewayState>> readGateways;
    private readonly bool notifications;
    internal Forms.ToolStripMenuItem StopMobileItem { get; } = new("停止手机网关（检查中…）") { Enabled = false };
    internal Forms.ToolStripMenuItem StopModelItem { get; } = new("停止模型网关（检查中…）") { Enabled = false };
    internal Forms.ToolStripMenuItem RestartMobileItem { get; } = new("重启手机网关（检查中…）") { Enabled = false };
    internal Forms.ToolStripMenuItem RestartModelItem { get; } = new("重启模型网关（检查中…）") { Enabled = false };
    internal bool GatewayActionInProgress { get; private set; }
    private bool disposed;
    private int statusRevision;

    public TrayIcon(Action show, Action exit, Action stopAndExit, Func<Task<TrayGatewayState>> readGateways,
        Func<Task> stopMobile, Func<Task> stopModel, Func<Task> restartMobile, Func<Task> restartModel, bool notifications = true)
    {
        this.readGateways = readGateways;
        this.notifications = notifications;
        using var registry = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        var darkTaskbar = registry?.GetValue("SystemUsesLightTheme") is not int theme || theme == 0;
        var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/tray-" + (darkTaskbar ? "light" : "dark") + ".png"));
        using var stream = resource.Stream;
        using var bitmap = new Drawing.Bitmap(stream);
        var handle = bitmap.GetHicon();
        try { using var borrowed = Drawing.Icon.FromHandle(handle); image = (Drawing.Icon)borrowed.Clone(); }
        finally { DestroyIcon(handle); }
        var open = new Forms.ToolStripMenuItem("打开 Codex Dock");
        open.Click += (_, _) => show();
        var quit = new Forms.ToolStripMenuItem("退出程序");
        quit.Click += (_, _) => exit();
        var stopQuit = new Forms.ToolStripMenuItem("停止网关并退出");
        stopQuit.Click += (_, _) => stopAndExit();
        menu.Items.AddRange([open, new Forms.ToolStripSeparator(), RestartMobileItem, StopMobileItem, RestartModelItem, StopModelItem, new Forms.ToolStripSeparator(), stopQuit, quit]);
        icon = new Forms.NotifyIcon { Icon = image, Text = "Codex Dock", ContextMenuStrip = menu, Visible = true };
        icon.DoubleClick += (_, _) => show();
        menu.Opening += async (_, _) => await RefreshGatewayActionsAsync();
        StopMobileItem.Click += async (_, _) => await StopGatewayAsync(stopMobile, "手机网关");
        StopModelItem.Click += async (_, _) => await StopGatewayAsync(stopModel, "模型网关");
        RestartMobileItem.Click += async (_, _) => await StopGatewayAsync(restartMobile, "手机网关", true);
        RestartModelItem.Click += async (_, _) => await StopGatewayAsync(restartModel, "模型网关", true);
    }

    internal async Task RefreshGatewayActionsAsync()
    {
        if (disposed || GatewayActionInProgress) return;
        var revision = ++statusRevision;
        SetState(StopMobileItem, "手机网关", null, checking: true);
        SetState(StopModelItem, "模型网关", null, checking: true);
        SetState(RestartMobileItem, "手机网关", null, checking: true, restart: true);
        SetState(RestartModelItem, "模型网关", null, checking: true, restart: true);
        TrayGatewayState state;
        try { state = await readGateways(); }
        catch { state = new(null, null); }
        if (disposed || GatewayActionInProgress || revision != statusRevision) return;
        SetState(StopMobileItem, "手机网关", state.MobileRunning);
        SetState(StopModelItem, "模型网关", state.ModelRunning);
        SetState(RestartMobileItem, "手机网关", state.MobileRunning, restart: true);
        SetState(RestartModelItem, "模型网关", state.ModelRunning, restart: true);
    }

    private static void SetState(Forms.ToolStripMenuItem item, string name, bool? running, bool checking = false, bool restart = false)
    {
        item.Enabled = running == true;
        item.Text = (restart ? "重启" : "停止") + name + (running == true ? "" : checking ? "（检查中…）" : running == false ? "（未运行）" : "（无法确认）");
    }

    private async Task StopGatewayAsync(Func<Task> stop, string name, bool restart = false)
    {
        if (disposed || GatewayActionInProgress) return;
        statusRevision++;
        GatewayActionInProgress = true;
        StopMobileItem.Enabled = StopModelItem.Enabled = false;
        RestartMobileItem.Enabled = RestartModelItem.Enabled = false;
        try
        {
            await stop();
            if (!disposed && notifications) icon.ShowBalloonTip(3000, "Codex Dock", name + (restart ? "已重启。" : "已停止。"), Forms.ToolTipIcon.Info);
        }
        catch (Exception)
        {
            if (!disposed && notifications) icon.ShowBalloonTip(5000, "Codex Dock", name + (restart ? "重启未完成" : "未能确认停止") + "，请打开 Dock 查看状态后重试。", Forms.ToolTipIcon.Warning);
        }
        finally { GatewayActionInProgress = false; await RefreshGatewayActionsAsync(); }
    }

    public void Dispose()
    {
        disposed = true;
        icon.Visible = false;
        icon.Dispose(); menu.Dispose(); image.Dispose();
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
}
