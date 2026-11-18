using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CodexConfig.WinUI;

// Native adapter only: automatic reconnect and process ownership live in Application.
internal sealed class NativeTrayIcon : IDisposable
{
    private const uint CallbackMessage = 0x8000 + 73;
    private readonly IntPtr window, icon;
    private readonly Subclass callback;
    private readonly Action restore, enhancements, gateways, stopAndExit, exit, unavailable;
    private readonly Func<bool> installerExit;
    private readonly uint taskbarCreated, setupExitMessage;
    private bool disposed;
    internal bool IsAvailable { get; private set; }

    internal NativeTrayIcon(IntPtr window, string iconPath, Action restore, Action enhancements, Action gateways, Action stopAndExit,
        Action exit, Action unavailable, Func<bool> installerExit)
    {
        this.window = window; this.restore = restore; this.enhancements = enhancements; this.gateways = gateways; this.stopAndExit = stopAndExit; this.exit = exit; this.unavailable = unavailable;
        this.installerExit = installerExit;
        taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        setupExitMessage = RegisterWindowMessage("CodexDock.ExitForSetup.1");
        icon = LoadImage(IntPtr.Zero, iconPath, 1, 0, 0, 0x10 | 0x40);
        if (icon == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法载入托盘图标。");
        callback = WindowMessage;
        if (!SetWindowSubclass(window, callback, 73, 0)) { DestroyIcon(icon); throw new InvalidOperationException("无法创建托盘消息入口。"); }
        if (!(IsAvailable = Notify(0)))
        { RemoveWindowSubclass(window, callback, 73); DestroyIcon(icon); throw new InvalidOperationException("Windows 未能创建托盘图标，请保持窗口运行。"); }
    }

    private IntPtr WindowMessage(IntPtr hwnd, uint message, nuint wparam, nint lparam, nuint id, nuint data)
    {
        try
        {
            if (!disposed && message == taskbarCreated)
            { IsAvailable = Notify(0); if (!IsAvailable) unavailable(); }
            if (!disposed && setupExitMessage != 0 && message == setupExitMessage)
                return installerExit() ? new IntPtr(1) : IntPtr.Zero;
            if (!disposed && message == CallbackMessage)
            {
                var mouse = (uint)(long)lparam;
                if (mouse == 0x202) restore();
                else if (mouse == 0x205 || mouse == 0x7B) ShowMenu();
                return IntPtr.Zero;
            }
        }
        catch { try { unavailable(); } catch { } } // Never unwind into a native window procedure.
        return DefSubclassProc(hwnd, message, wparam, lparam);
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        try
        {
            AppendMenu(menu, 0, 1, "打开 Codex Dock");
            AppendMenu(menu, 0, 2, "运行与增强");
            AppendMenu(menu, 0, 4, "模型网关");
            AppendMenu(menu, 0x800, 0, null);
            AppendMenu(menu, 0, 5, "停止网关并退出");
            AppendMenu(menu, 0, 3, "退出 Codex Dock");
            GetCursorPos(out var point); SetForegroundWindow(window);
            var selected = TrackPopupMenu(menu, 0x100 | 0x2, point.X, point.Y, 0, window, IntPtr.Zero);
            PostMessage(window, 0, 0, 0);
            if (selected == 1) restore(); else if (selected == 2) enhancements(); else if (selected == 3) exit();
            else if (selected == 4) gateways(); else if (selected == 5) stopAndExit();
        }
        finally { DestroyMenu(menu); }
    }

    private bool Notify(uint operation)
    {
        var data = new NotifyIconData { Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = window, Id = 73,
            Flags = 1 | 2 | 4, Callback = CallbackMessage, Icon = icon, Tip = "Codex Dock", Info = "", InfoTitle = "" };
        return ShellNotifyIcon(operation, ref data);
    }
    internal static void RequestWindowClose(IntPtr window) => PostMessage(window, 0x10, 0, 0);
    internal static void RequestTrayOpen(IntPtr window) => PostMessage(window, CallbackMessage, 73, 0x202);
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; Notify(2); IsAvailable = false;
        RemoveWindowSubclass(window, callback, 73); DestroyIcon(icon);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size; public IntPtr Window; public uint Id, Flags, Callback; public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Timeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public IntPtr BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    private delegate IntPtr Subclass(IntPtr hwnd, uint message, nuint wparam, nint lparam, nuint id, nuint data);
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint operation, ref NotifyIconData data);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowSubclass(IntPtr hwnd, Subclass callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveWindowSubclass(IntPtr hwnd, Subclass callback, nuint id);
    [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint message, nuint wparam, nint lparam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int x, int y, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AppendMenu(IntPtr menu, uint flags, nuint id, string? label);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(IntPtr hwnd, uint message, nuint wparam, nint lparam);
}
