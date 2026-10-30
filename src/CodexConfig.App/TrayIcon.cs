using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace CodexConfig.App;

internal sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon icon;
    private readonly Forms.ContextMenuStrip menu = new();
    private readonly Drawing.Icon image;
    public TrayIcon(Action show, Action exit)
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
        var quit = new Forms.ToolStripMenuItem("退出程序");
        quit.Click += (_, _) => exit();
        menu.Items.AddRange([open, new Forms.ToolStripSeparator(), quit]);
        icon = new Forms.NotifyIcon { Icon = image, Text = "Codex Dock", ContextMenuStrip = menu, Visible = true };
        icon.DoubleClick += (_, _) => show();
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose(); menu.Dispose(); image.Dispose();
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
}
