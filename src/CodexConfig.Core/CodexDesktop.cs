using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace CodexConfig.Core;

public sealed record CodexDesktopProcess(int Id, int ParentId, long Started, string Executable);
public sealed record CodexDesktopTarget(string Executable, IReadOnlyList<CodexDesktopProcess> Processes)
{
    public bool IsRunning => Processes.Count > 0;
}

internal interface ICodexDesktopPlatform
{
    IReadOnlyList<CodexDesktopProcess> Running();
    IReadOnlyList<string> Installed();
    void Close(CodexDesktopProcess process);
    void Start(ProcessStartInfo start);
}

// Restart only the identified desktop GUI. CLI sessions, Dock and services have separate lifecycles.
public sealed class CodexDesktop
{
    private readonly ICodexDesktopPlatform platform;
    private readonly TimeSpan exitTimeout, startTimeout;
    public CodexDesktop() : this(new WindowsCodexDesktopPlatform()) { }
    internal CodexDesktop(ICodexDesktopPlatform platform, TimeSpan? exitTimeout = null, TimeSpan? startTimeout = null)
    {
        this.platform = platform;
        this.exitTimeout = exitTimeout ?? TimeSpan.FromSeconds(25);
        this.startTimeout = startTimeout ?? TimeSpan.FromSeconds(15);
    }

    public CodexDesktopTarget FindTarget()
    {
        var running = platform.Running();
        var roots = running.Where(process => !running.Any(parent => parent.Id == process.ParentId && SamePath(parent.Executable, process.Executable))).ToArray();
        if (roots.Length > 1) throw new InvalidOperationException("检测到多个 Codex 桌面实例，请保留需要应用配置的一个实例后重试。");
        if (roots.Length == 1)
            return new(roots[0].Executable, running.Where(process => SamePath(process.Executable, roots[0].Executable)).ToArray());
        var installed = platform.Installed().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (installed.Length == 0) throw new FileNotFoundException("未找到 Codex 桌面端，请先安装并打开 Codex 后重试。");
        if (installed.Length > 1) throw new InvalidOperationException("检测到多个 Codex 安装位置，请先打开要使用的 Codex 后重试。");
        return new(installed[0], []);
    }

    public async Task RestartAsync(CodexDesktopTarget target, string home, string? authApiBaseUrl,
        Action? validateSaved = null, CancellationToken cancellation = default)
    {
        // Construct and validate the launch before asking the existing app to close.
        var start = CreateStartInfo(target.Executable, home, authApiBaseUrl);
        if (!File.Exists(start.FileName)) throw new FileNotFoundException("Codex 桌面程序已移动或更新，请重新打开后重试。");
        cancellation.ThrowIfCancellationRequested(); validateSaved?.Invoke();
        var current = platform.Running();
        if (current.Any(process => !target.Processes.Contains(process)))
            throw new InvalidOperationException("Codex 桌面实例已变化，请重新点击重启并确认目标。");
        if (!current.Any() && !platform.Installed().Any(path => SamePath(path, target.Executable)))
            throw new InvalidOperationException("无法重新核对 Codex 安装位置，请先手动打开后重试。");
        foreach (var root in current.Where(process => !current.Any(parent => parent.Id == process.ParentId)))
        {
            cancellation.ThrowIfCancellationRequested(); platform.Close(root);
        }
        var timer = Stopwatch.StartNew();
        while ((current = platform.Running()).Count > 0)
        {
            if (current.Any(process => !target.Processes.Contains(process)))
                throw new InvalidOperationException("退出期间出现了新的 Codex 桌面实例，已停止重启，请重新核对。");
            if (timer.Elapsed >= exitTimeout)
                throw new TimeoutException("Codex 尚未完全退出，可能已取消退出或仍驻留托盘。配置已保存；请在 Codex 中完成退出后重试。");
            await Task.Delay(100, cancellation).ConfigureAwait(false);
        }
        cancellation.ThrowIfCancellationRequested(); validateSaved?.Invoke();
        // Do not launch into a new single-instance owner that appeared while the old app was closing.
        if (platform.Running().Count > 0) throw new InvalidOperationException("Codex 已被重新打开，请核对当前实例后重试。");
        platform.Start(start);
        timer.Restart();
        while (!platform.Running().Any(process => SamePath(process.Executable, target.Executable)))
        {
            if (timer.Elapsed >= startTimeout)
                throw new IOException("已请求打开 Codex，但尚未确认桌面进程。配置已保存，请手动打开 Codex 并核对。");
            await Task.Delay(100, cancellation).ConfigureAwait(false);
        }
    }

    internal static ProcessStartInfo CreateStartInfo(string executable, string home, string? authApiBaseUrl)
    {
        if (!Path.IsPathFullyQualified(executable) || !Path.IsPathFullyQualified(home) || !Directory.Exists(home))
            throw new ArgumentException("Codex 程序和已保存的配置目录必须是有效的绝对路径。");
        var start = new ProcessStartInfo(Path.GetFullPath(executable))
        { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executable))!, CreateNoWindow = true };
        start.Environment["CODEX_HOME"] = Path.GetFullPath(home);
        if (string.IsNullOrEmpty(authApiBaseUrl)) start.Environment.Remove(WindowsUserEnvironment.Variable);
        else start.Environment[WindowsUserEnvironment.Variable] = authApiBaseUrl;
        return start;
    }

    internal static bool SamePath(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}

internal sealed class WindowsCodexDesktopPlatform(string? fixtureExecutable = null) : ICodexDesktopPlatform
{
    private const string PackageFamily = "OpenAI.Codex_2p2nqsd0c76g0";
    private readonly int session = Process.GetCurrentProcess().SessionId;
    private readonly HashSet<string> verifiedExecutables = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<CodexDesktopProcess> Running()
    {
        var result = new List<CodexDesktopProcess>();
        using var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取桌面进程列表。");
        var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
        if (!Process32First(snapshot, ref entry)) return result;
        do
        {
            if (fixtureExecutable is not null && entry.Id == Environment.ProcessId) continue;
            if (fixtureExecutable is not null ? !entry.Executable.Equals(Path.GetFileName(fixtureExecutable), StringComparison.OrdinalIgnoreCase)
                : !entry.Executable.Equals("Codex.exe", StringComparison.OrdinalIgnoreCase) && !entry.Executable.Equals("ChatGPT.exe", StringComparison.OrdinalIgnoreCase)) continue;
            if (!ProcessIdToSessionId(entry.Id, out var candidateSession) || candidateSession != session) continue;
            using var process = OpenProcess(0x1000, false, entry.Id);
            if (process.IsInvalid)
            {
                if (Marshal.GetLastWin32Error() == 5) throw new IOException("无法核对桌面进程，请以同一用户和权限运行 Codex 与 Dock。");
                continue;
            }
            var size = 32768; var path = new StringBuilder(size);
            if (!QueryFullProcessImageName(process, 0, path, ref size) || !GetProcessTimes(process, out var created, out _, out _, out _))
            {
                // QueryFullProcessImageName can return access denied for a process that exited after the snapshot.
                if (GetExitCodeProcess(process, out var code) && code != 259) continue;
                if (Marshal.GetLastWin32Error() == 5 || code == 259) throw new IOException("无法核对桌面程序路径或启动时间，请以同一用户和权限运行 Codex 与 Dock。");
                continue;
            }
            var executable = path.ToString();
            if (fixtureExecutable is not null ? CodexDesktop.SamePath(executable, fixtureExecutable)
                : IsDesktopExecutable(executable, Family(process))) verifiedExecutables.Add(executable);
            result.Add(new((int)entry.Id, (int)entry.ParentId, created, executable));
        } while (Process32Next(snapshot, ref entry));
        // Sandboxed renderer processes can omit package identity; include them only at an already verified GUI path.
        return result.Where(process => verifiedExecutables.Contains(process.Executable)).ToArray();
    }

    public IReadOnlyList<string> Installed()
    {
        if (fixtureExecutable is not null) return File.Exists(fixtureExecutable) ? [fixtureExecutable] : [];
        var result = verifiedExecutables.Where(File.Exists).Concat(KnownExecutables().Where(IsUnpackagedDesktop)).ToList();
        uint count = 0, length = 0;
        if (GetPackagesByPackageFamily(PackageFamily, ref count, IntPtr.Zero, ref length, IntPtr.Zero) == 122 && count is > 0 and < 100 && length < 1024 * 1024)
        {
            var names = Marshal.AllocHGlobal(checked((int)count * IntPtr.Size)); var buffer = Marshal.AllocHGlobal(checked((int)length * 2));
            try
            {
                if (GetPackagesByPackageFamily(PackageFamily, ref count, names, ref length, buffer) == 0)
                    for (var i = 0; i < count; i++)
                    {
                        var name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(names, i * IntPtr.Size));
                        if (name is null) continue;
                        uint pathLength = 32768; var path = new StringBuilder((int)pathLength);
                        if (GetPackagePathByFullName(name, ref pathLength, path) != 0) continue;
                        foreach (var file in new[] { "ChatGPT.exe", "Codex.exe" })
                        {
                            var executable = Path.Combine(path.ToString(), "app", file);
                            if (File.Exists(executable)) { result.Add(executable); break; }
                        }
                    }
            }
            finally { Marshal.FreeHGlobal(names); Marshal.FreeHGlobal(buffer); }
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static bool IsDesktopExecutable(string path, string? packageFamily) =>
        packageFamily == PackageFamily
            ? Path.GetFileName(Path.GetDirectoryName(path))?.Equals("app", StringComparison.OrdinalIgnoreCase) == true &&
                (Path.GetFileName(path).Equals("ChatGPT.exe", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(path).Equals("Codex.exe", StringComparison.OrdinalIgnoreCase))
            : IsUnpackagedDesktop(path);

    private static bool IsUnpackagedDesktop(string path)
    {
        if (!KnownExecutables().Any(candidate => CodexDesktop.SamePath(candidate, path)) || !File.Exists(path)) return false;
        try { return FileVersionInfo.GetVersionInfo(path).CompanyName?.Contains("OpenAI", StringComparison.OrdinalIgnoreCase) == true; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }

    private static IEnumerable<string> KnownExecutables()
    {
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) })
            foreach (var folder in new[] { "Codex", "OpenAI/Codex", "Programs/Codex", "Programs/OpenAI/Codex" })
                yield return Path.GetFullPath(Path.Combine(root, folder, "Codex.exe"));
    }

    public void Close(CodexDesktopProcess expected)
    {
        if (!Running().Contains(expected)) throw new InvalidOperationException("Codex 桌面实例已变化，请重新确认。");
        using var process = Process.GetProcessById(expected.Id);
        var window = process.MainWindowHandle;
        if (window == IntPtr.Zero)
            throw new InvalidOperationException("Codex 没有可关闭的主窗口。请先在 Codex 托盘或菜单中完成退出，再重试。");
        if (!IsWindowEnabled(window)) throw new InvalidOperationException("Codex 正在显示对话框，请先完成或取消该对话框后再重试。");
        GetWindowThreadProcessId(window, out var owner);
        if (owner != expected.Id || !PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero))
            throw new IOException("无法请求 Codex 正常退出，请在 Codex 菜单中完成退出后重试。");
    }

    public void Start(ProcessStartInfo start)
    {
        if (!Installed().Any(path => CodexDesktop.SamePath(path, start.FileName)))
            throw new InvalidOperationException("Codex 安装位置已变化，请先手动打开后重试。");
        using var process = Process.Start(start) ?? throw new IOException("无法启动 Codex 桌面端，请手动打开后核对。");
    }

    private static string? Family(SafeProcessHandle process)
    {
        uint length = 512; var family = new StringBuilder((int)length);
        return GetPackageFamilyName(process, ref length, family) == 0 ? family.ToString() : null;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size, Usage, Id; public UIntPtr DefaultHeap; public uint ModuleId, Threads, ParentId; public int BasePriority; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Executable;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32FirstW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32First(SafeFileHandle snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32NextW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32Next(SafeFileHandle snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint id);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder path, ref int size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetProcessTimes(SafeProcessHandle process, out long created, out long exited, out long kernel, out long user);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ProcessIdToSessionId(uint id, out int session);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetPackageFamilyName(SafeProcessHandle process, ref uint length, StringBuilder family);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetPackagesByPackageFamily(string family, ref uint count, IntPtr names, ref uint length, IntPtr buffer);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetPackagePathByFullName(string name, ref uint length, StringBuilder path);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
}
