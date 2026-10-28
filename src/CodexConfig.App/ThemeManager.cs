using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace CodexConfig.App;

internal static class ThemeManager
{
    public static bool IsDark { get; private set; }
    public static void ApplySystemTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        Apply(key?.GetValue("AppsUseLightTheme") is int value && value == 0);
    }
    internal static void Apply(bool dark)
    {
        IsDark = dark;
        var colors = new Dictionary<string, string>
        {
            ["Ink"] = dark ? "#E8EDF3" : "#202A36", ["Muted"] = dark ? "#A8B4C3" : "#596779",
            ["Accent"] = dark ? "#9BBEFF" : "#245CC5", ["AccentOn"] = dark ? "#172233" : "#FFFFFF",
            ["AccentSoft"] = dark ? "#273951" : "#EAF1FE", ["Line"] = dark ? "#38424E" : "#DDE3EB",
            ["Canvas"] = dark ? "#171B22" : "#F5F7FB", ["Sidebar"] = dark ? "#1C212A" : "#FFFFFF",
            ["Surface"] = dark ? "#202630" : "#FFFFFF", ["Success"] = dark ? "#89D7B4" : "#267252",
            ["SuccessSoft"] = dark ? "#243E36" : "#E8F5EE",
            ["Field"] = dark ? "#202225" : "#FFFFFF", ["Hover"] = dark ? "#2B2E34" : "#EDF0F4",
            ["Error"] = dark ? "#FF9C9C" : "#B53636", ["Code"] = dark ? "#191B1E" : "#F6F7F9",
            ["ScrollThumbBrush"] = dark ? "#616875" : "#BFC5CE"
        };
        foreach (var (name, color) in colors)
            Application.Current.Resources[name] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        foreach (Window window in Application.Current.Windows) UpdateTitleBar(window);
    }
    internal static void UpdateTitleBar(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var value = IsDark ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, 20, ref value, sizeof(int));
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
