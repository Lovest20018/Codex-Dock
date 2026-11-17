using System.Security.Cryptography;
using System.Text.Json;
using CodexConfig.Core;

namespace CodexConfig.Application;

public enum WorkspaceSetupKind { Unreadable, NeedsConnection, ExistingConnection }
public sealed record WorkspaceSetupGuide(WorkspaceSetupKind Kind, string Title, string Message)
{
    public static WorkspaceSetupGuide Read(ConfigSnapshot snapshot, ConnectionSettings settings)
    {
        var document = new TomlDocument(snapshot.Config);
        if (settings.Mode == ConnectionMode.OfficialWithApi || settings.ApiKey.Length > 0)
            return new(WorkspaceSetupKind.ExistingConnection, "已读取模型连接", "本地设置已读取，实际服务与聊天参数请在 Codex 中核对。");
        if (OfficialCredentials.HasLogin(snapshot.Auth))
            return new(WorkspaceSetupKind.ExistingConnection, "已读取官方文件登录", "可保留现有官方登录和默认设置。登录有效期由 Codex 管理，尚未在线验证。");
        if (settings.ProviderId != "openai" || document.String("cli_auth_credentials_store") is not (null or "file"))
            return new(WorkspaceSetupKind.ExistingConnection, "使用客户端或供应商管理的认证", "保留现有认证方式；本窗口未执行外部认证命令，也未确认系统凭据与在线连接。");
        return new(WorkspaceSetupKind.NeedsConnection, "先设置连接", snapshot.Config is null && snapshot.Auth is null
            ? "此目录尚无连接文件。填写 API 服务地址、密钥和模型，查看差异后保存；若使用官方账号，可先在 Codex 登录同一目录，再重新读取。"
            : "已读取本地设置，尚未发现可用的模型凭据。可在连接页填写 API 服务，或在 Codex 完成官方文件登录后重新读取。现有文件会保留。");
    }
    public static WorkspaceSetupGuide Unreadable { get; } = new(WorkspaceSetupKind.Unreadable, "先处理配置读取问题",
        "当前目录未能完整读取，暂时不能编辑或应用。请核对目录、文件格式与当前 Windows 用户的加密数据；修复后重新读取，也可切换目录或查看已有恢复记录。原文件保持不变。");
}

internal static class WorkspaceStorageErrors
{
    internal static bool IsRecoverable(Exception error) => error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or
        CryptographicException or JsonException or KeyNotFoundException or InvalidOperationException;
    internal static string Describe(Exception error) => error switch
    {
        CryptographicException => "当前 Windows 用户无法解密此目录的连接数据。请核对配置目录或查看本目录的恢复记录。",
        UnauthorizedAccessException => "当前配置目录或文件无法读取，请核对访问权限后重试。",
        _ when error.Message.Contains("config.toml", StringComparison.OrdinalIgnoreCase) => "config.toml 无法完整读取，请核对 TOML 格式与 UTF-8 编码，修复后重新读取。",
        _ when error.Message.Contains("auth.json", StringComparison.OrdinalIgnoreCase) => "auth.json 无法完整读取，请核对 JSON 文件，或在 Codex 完成登录后重新读取。",
        _ => "配置或相关加密文件无法完整读取。请核对目录、文件格式及恢复记录后重试。"
    };
}
