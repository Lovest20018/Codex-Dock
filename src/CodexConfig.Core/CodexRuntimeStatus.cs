namespace CodexConfig.Core;

public sealed record CodexRuntimeStatus(bool Available, int ProcessCount)
{
    public IReadOnlyList<CodexDesktopProcess> DesktopProcesses { get; init; } = [];
    public string Summary => !Available ? "暂时无法读取进程状态" : ProcessCount > 0 ? $"运行中 · 发现 {ProcessCount} 个 Codex 进程" : "未检测到 Codex 进程";

    public static CodexRuntimeStatus Read()
    {
        try
        {
            var processes = new WindowsCodexDesktopPlatform().Running();
            return new(true, processes.Count) { DesktopProcesses = processes };
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException or IOException or UnauthorizedAccessException)
        { return new(false, 0); }
    }
}
