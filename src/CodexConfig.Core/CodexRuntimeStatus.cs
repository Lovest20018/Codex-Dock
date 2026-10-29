using System.Diagnostics;

namespace CodexConfig.Core;

public sealed record CodexRuntimeStatus(bool Available, int ProcessCount)
{
    public string Summary => !Available ? "暂时无法读取进程状态" : ProcessCount > 0 ? $"运行中 · 发现 {ProcessCount} 个 Codex 进程" : "未检测到 Codex 进程";

    public static CodexRuntimeStatus Read()
    {
        try
        {
            var count = 0;
            foreach (var name in new[] { "codex", "codex-cli", "codex-app" })
                foreach (var process in Process.GetProcessesByName(name))
                    using (process) { count++; }
            return new(true, count);
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        { return new(false, 0); }
    }
}
