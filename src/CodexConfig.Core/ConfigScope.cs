namespace CodexConfig.Core;

public static class ConfigScope
{
    public static string Describe(string path) => path.StartsWith("desktop.", StringComparison.Ordinal)
        ? "作用端：Codex 桌面客户端；重新打开客户端后核对。"
        : path.StartsWith("tui.", StringComparison.Ordinal)
        ? "作用端：Codex 终端界面；下次启动终端客户端时读取。"
        : "作用端：读取此配置的 Codex 执行端；在新聊天或重新启动后核对。当前聊天可能另有会话和组织策略。";
}
