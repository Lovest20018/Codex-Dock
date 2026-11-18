using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Automation;

// This helper is compiled by Windows PowerShell before the new runtime is installed.
public static class InstallerProcesses
{
    private const uint LegacyTrayCallback = 0x8000 + 73;
    private const string SetupExitMessage = "CodexDock.ExitForSetup.1";

    public static bool IsCurrentUser(IntPtr process)
    {
        IntPtr token;
        if (!OpenProcessToken(process, 8, out token)) return false;
        try
        {
            using (var owner = new WindowsIdentity(token))
            using (var current = WindowsIdentity.GetCurrent())
                return owner.User.Equals(current.User);
        }
        finally { CloseHandle(token); }
    }

    public static string ModelHome(IntPtr process)
    {
        // ProcessCommandLineInformation returns a UNICODE_STRING followed by its buffer.
        int size;
        NtQueryInformationProcess(process, 60, IntPtr.Zero, 0, out size);
        if (size < Marshal.SizeOf(typeof(UnicodeString)) || size > 131072) return null;
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (NtQueryInformationProcess(process, 60, buffer, size, out size) < 0) return null;
            var command = (UnicodeString)Marshal.PtrToStructure(buffer, typeof(UnicodeString));
            var offset = command.Buffer.ToInt64() - buffer.ToInt64();
            if (command.Length == 0 || command.Length % 2 != 0 || command.Length > command.MaximumLength ||
                offset < Marshal.SizeOf(typeof(UnicodeString)) || offset > size - command.Length) return null;
            int count;
            var argv = CommandLineToArgvW(Marshal.PtrToStringUni(command.Buffer, command.Length / 2), out count);
            if (argv == IntPtr.Zero) return null;
            try
            {
                if (count != 3 || Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, IntPtr.Size)) != "--model-bridge") return null;
                return Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, 2 * IntPtr.Size));
            }
            finally { LocalFree(argv); }
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    public static bool RequestDesktopExit(Process process)
    {
        var deadline = Stopwatch.StartNew();
        while (!process.HasExited && deadline.ElapsedMilliseconds < 3000)
        {
            var window = DockWindow(process.Id);
            if (window == IntPtr.Zero) { Thread.Sleep(100); continue; }
            if (!OwnsWindow(window, process)) return false;
            AllowSetForegroundWindow((uint)process.Id);
            IntPtr response;
            var request = RegisterWindowMessage(SetupExitMessage);
            if (request != 0 && Send(window, request, IntPtr.Zero, IntPtr.Zero, out response) && response == new IntPtr(1))
                return true;
            return TryLegacyExit(window, process);
        }
        return process.HasExited;
    }

    private static IntPtr DockWindow(int processId)
    {
        IntPtr match = IntPtr.Zero;
        EnumWindows(delegate(IntPtr window, IntPtr ignored)
        {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (owner != (uint)processId || GetWindow(window, 4) != IntPtr.Zero) return true;
            var name = new StringBuilder(128);
            GetClassName(window, name, name.Capacity);
            if (name.ToString() != "WinUIDesktopWin32WindowClass") return true;
            var title = new StringBuilder(128);
            GetWindowText(window, title, title.Capacity);
            if (!title.ToString().StartsWith("Codex Dock", StringComparison.Ordinal)) return true;
            match = window;
            return false;
        }, IntPtr.Zero);
        return match;
    }

    private static bool OwnsWindow(IntPtr window, Process process)
    {
        uint owner;
        GetWindowThreadProcessId(window, out owner);
        return owner == (uint)process.Id && !process.HasExited;
    }

    private static bool TryLegacyExit(IntPtr window, Process process)
    {
        // WinUI 0.19.0–0.19.3 use the tray callback to restore and a separate
        // settings button for real exit. WM_CLOSE would hide the window again.
        var selectedSettings = false;
        var openedNavigation = false;
        var clock = Stopwatch.StartNew();
        IntPtr ignored;
        if (!Send(window, 0, IntPtr.Zero, IntPtr.Zero, out ignored)) return false;
        Send(window, LegacyTrayCallback, new IntPtr(73), new IntPtr(0x202), out ignored);
        if (!IsWindowVisible(window) || IsIconic(window)) ShowWindowAsync(window, 9);
        while (clock.ElapsedMilliseconds < 5000 && OwnsWindow(window, process))
        {
            try
            {
                var root = AutomationElement.FromHandle(window);
                var exit = Find(root, "ExitWindowButton");
                if (exit != null && OwnsWindow(window, process) && Invoke(exit)) return true;
                var settings = Find(root, "SettingsSectionNav");
                object selection;
                if (!selectedSettings && settings != null && settings.Current.IsEnabled &&
                    settings.TryGetCurrentPattern(SelectionItemPattern.Pattern, out selection))
                {
                    var choice = (SelectionItemPattern)selection;
                    if (!choice.Current.IsSelected) choice.Select();
                    selectedSettings = true;
                }
                else if (!openedNavigation && settings == null)
                    openedNavigation = Invoke(Find(root, "TogglePaneButton"));
            }
            catch (ElementNotAvailableException) { }
            catch (ElementNotEnabledException) { }
            catch (InvalidOperationException) { }
            Thread.Sleep(125);
        }
        return process.HasExited;
    }

    private static AutomationElement Find(AutomationElement root, string id)
    {
        return root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, id));
    }

    private static bool Invoke(AutomationElement element)
    {
        object action;
        if (element == null || !element.Current.IsEnabled ||
            !element.TryGetCurrentPattern(InvokePattern.Pattern, out action)) return false;
        ((InvokePattern)action).Invoke();
        return true;
    }

    private static bool Send(IntPtr window, uint message, IntPtr wparam, IntPtr lparam, out IntPtr response)
    {
        return SendMessageTimeout(window, message, wparam, lparam, 3, 500, out response) != IntPtr.Zero;
    }

    public static int ListenerOwner(int port)
    {
        int size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2, 3, 0);
        for (int attempt = 0; attempt < 3 && size > 0 && size <= 4 * 1024 * 1024; attempt++)
        {
            var table = Marshal.AllocHGlobal(size);
            try
            {
                uint result = GetExtendedTcpTable(table, ref size, false, 2, 3, 0);
                if (result == 122) continue;
                if (result != 0) return 0;
                int count = Marshal.ReadInt32(table);
                if (count < 0 || count > (size - 4) / 24) return 0;
                for (int row = 0; row < count; row++)
                {
                    int offset = 4 + row * 24;
                    int rawPort = Marshal.ReadInt32(table, offset + 8);
                    if ((((rawPort & 255) << 8) | ((rawPort >> 8) & 255)) == port && Marshal.ReadInt32(table, offset + 4) == 0x0100007f)
                        return Marshal.ReadInt32(table, offset + 20);
                }
                return 0;
            }
            finally { Marshal.FreeHGlobal(table); }
        }
        return 0;
    }

    [StructLayout(LayoutKind.Sequential)] private struct UnicodeString { public ushort Length, MaximumLength; public IntPtr Buffer; }
    private delegate bool EnumWindow(IntPtr window, IntPtr data);
    [DllImport("advapi32.dll")] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    [DllImport("ntdll.dll")] private static extern int NtQueryInformationProcess(IntPtr process, int information, IntPtr buffer, int size, out int needed);
    [DllImport("iphlpapi.dll")] private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int family, int tableClass, uint reserved);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CommandLineToArgvW(string command, out int count);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow callback, IntPtr data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint relation);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder title, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint process);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")] private static extern IntPtr SendMessageTimeout(IntPtr window, uint message,
        IntPtr wparam, IntPtr lparam, uint flags, uint timeout, out IntPtr response);
}
