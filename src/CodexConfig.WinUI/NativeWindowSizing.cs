using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CodexConfig.WinUI;

internal sealed class NativeWindowSizing : IDisposable
{
    private readonly IntPtr window;
    private readonly Subclass callback;
    private bool disposed;
    internal NativeWindowSizing(IntPtr window)
    {
        this.window = window; callback = WindowMessage;
        if (!SetWindowSubclass(window, callback, 74, 0)) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法设置窗口尺寸范围。");
    }
    internal (int X, int Y, int Width, int Height) InitialBounds()
    {
        var work = WorkArea(); var scale = Math.Max(96, GetDpiForWindow(window)) / 96.0;
        var width = Math.Min(work.Right - work.Left, (int)Math.Ceiling(1120 * scale));
        var height = Math.Min(work.Bottom - work.Top, (int)Math.Ceiling(820 * scale));
        return (work.Left + Math.Max(0, (work.Right - work.Left - width) / 2), work.Top + Math.Max(0, (work.Bottom - work.Top - height) / 2), width, height);
    }
    private NativeRect WorkArea()
    {
        var monitor = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(window, 2), ref monitor)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return monitor.Work;
    }
    internal uint CurrentDpi => Math.Max(96, GetDpiForWindow(window));
    internal (int X, int Y, int Width, int Height) CurrentWorkArea()
    { var work = WorkArea(); return (work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top); }
    private IntPtr WindowMessage(IntPtr hwnd, uint message, nuint wparam, nint lparam, nuint id, nuint data)
    {
        var result = DefSubclassProc(hwnd, message, wparam, lparam);
        if (disposed || message != 0x24 || lparam == IntPtr.Zero) return result;
        try
        {
            var info = Marshal.PtrToStructure<MinMaxInfo>(lparam); var work = WorkArea();
            var scale = Math.Max(96, GetDpiForWindow(hwnd)) / 96.0;
            info.Minimum.X = Math.Min(work.Right - work.Left, (int)Math.Ceiling(720 * scale));
            info.Minimum.Y = Math.Min(work.Bottom - work.Top, (int)Math.Ceiling(540 * scale));
            Marshal.StructureToPtr(info, lparam, false);
        }
        catch { } // A display change must never unwind into the window procedure.
        return result;
    }
    internal (int Width, int Height) QueryMinimum()
    {
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<MinMaxInfo>());
        try { Marshal.StructureToPtr(new MinMaxInfo(), pointer, false); SendMessage(window, 0x24, 0, pointer); var info = Marshal.PtrToStructure<MinMaxInfo>(pointer); return (info.Minimum.X, info.Minimum.Y); }
        finally { Marshal.FreeHGlobal(pointer); }
    }
    public void Dispose() { if (!disposed) { disposed = true; RemoveWindowSubclass(window, callback, 74); } }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo { public Point Reserved, MaximumSize, MaximumPosition, Minimum, Maximum; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public uint Size; public NativeRect Monitor, Work; public uint Flags; }
    private delegate IntPtr Subclass(IntPtr hwnd, uint message, nuint wparam, nint lparam, nuint id, nuint data);
    [DllImport("comctl32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowSubclass(IntPtr window, Subclass callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveWindowSubclass(IntPtr window, Subclass callback, nuint id);
    [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr window, uint message, nuint wparam, nint lparam);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, nuint wparam, nint lparam);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
