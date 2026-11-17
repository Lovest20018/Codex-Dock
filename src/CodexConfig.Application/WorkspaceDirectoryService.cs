using System.Diagnostics;

namespace CodexConfig.Application;

public interface IWorkspaceDirectoryRuntime { void Open(string path); }
public sealed class NativeWorkspaceDirectoryRuntime : IWorkspaceDirectoryRuntime
{
    public static ProcessStartInfo StartInfo(string path) => new("explorer.exe") { ArgumentList = { path }, UseShellExecute = false };
    public void Open(string path) => _ = Process.Start(StartInfo(path)) ?? throw new IOException("无法打开配置目录。");
}
public sealed class WorkspaceDirectoryService(IWorkspaceDirectoryRuntime runtime)
{
    public string CurrentPath(string home, Func<string> current)
    {
        if (!Path.IsPathFullyQualified(home)) throw new ArgumentException("请选择完整配置目录。");
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(home)); var latest = current();
        if (!Path.IsPathFullyQualified(latest) || !string.Equals(path, Path.TrimEndingDirectorySeparator(Path.GetFullPath(latest)), StringComparison.OrdinalIgnoreCase)) throw new OperationCanceledException("配置目录已改变，请重试。");
        return path;
    }
    public bool Open(string home, Func<string> current)
    {
        var path = CurrentPath(home, current); if (!Directory.Exists(path)) return false;
        _ = CurrentPath(path, current); runtime.Open(path); return true;
    }
}
