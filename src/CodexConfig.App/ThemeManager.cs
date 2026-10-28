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
            ["Ink"] = dark ? "#EBEDF0" : "#20242B", ["Muted"] = dark ? "#A6ADB9" : "#69717D",
            ["Accent"] = dark ? "#94B4FF" : "#2F63D8", ["AccentOn"] = dark ? "#16233F" : "#FFFFFF",
            ["AccentSoft"] = dark ? "#29344A" : "#EAF0FC", ["Line"] = dark ? "#363A40" : "#E6E8EB",
            ["Canvas"] = dark ? "#202225" : "#FFFFFF", ["Sidebar"] = dark ? "#191B1E" : "#F6F7F9",
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
