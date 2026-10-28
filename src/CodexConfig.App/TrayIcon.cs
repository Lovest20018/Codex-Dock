using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace CodexConfig.App;

internal sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon icon;
    private readonly Forms.ContextMenuStrip menu = new();
    private readonly Drawing.Icon image;
    private readonly Forms.ToolStripMenuItem status = new("本地网关：未配置") { Enabled = false };
    private readonly Forms.ToolStripMenuItem start = new("启动网关");
    private readonly Forms.ToolStripMenuItem stop = new("停止网关");

    public TrayIcon(Action show, Action startGateway, Action stopGateway, Action exit)
    {
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
        start.Click += (_, _) => startGateway();
        stop.Click += (_, _) => stopGateway();
        var quit = new Forms.ToolStripMenuItem("退出（停止网关）");
        quit.Click += (_, _) => exit();
        menu.Items.AddRange([open, new Forms.ToolStripSeparator(), status, start, stop, new Forms.ToolStripSeparator(), quit]);
        icon = new Forms.NotifyIcon { Icon = image, Text = "Codex Dock", ContextMenuStrip = menu, Visible = true };
        icon.DoubleClick += (_, _) => show();
    }

    public void Update(string state, bool canStart, bool canStop)
    {
        status.Text = "本地网关：" + state;
        icon.Text = "Codex Dock · 网关" + state;
        start.Enabled = canStart;
        stop.Enabled = canStop;
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose(); menu.Dispose(); image.Dispose();
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
}
