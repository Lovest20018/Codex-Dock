using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodexConfig.Core;

public enum AuthenticationChoice { Auto, Pat, ApiKey }
public enum ConnectionKind { PatRelay, ResponsesApi }
public enum AuthenticationMode { Pat, ApiKey, OfficialWithApi }
public enum ConnectionMode { Direct, OfficialWithApi }
public sealed record AuthenticationDecision(AuthenticationMode Mode, string ModelBaseUrl, string? ChatGptBaseUrl, string? AuthApiOrigin, string Reason);

public sealed record ConnectionSettings(string BaseUrl, string ApiKey, string Model,
    bool FastMode = true, bool ContextManagement = true, bool WebSearch = true)
{
    public AuthenticationChoice Authentication { get; init; } = AuthenticationChoice.Auto;
    public ConnectionKind Kind { get; init; } = ConnectionKind.PatRelay;
    public ConnectionMode Mode { get; init; }
    public bool UseSystemProxy { get; init; }
    public ConnectionDetails Details { get; init; } = new();
    public string? WebSearchMode { get; init; }
    // The settings UI edits these values explicitly, preserving absent defaults.
    public bool PreservePreferenceValues { get; init; }

    public ConnectionSettings Normalize()
    {
        if (!Uri.TryCreate(BaseUrl.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || string.IsNullOrEmpty(uri.Host))
            throw new ArgumentException("请输入完整的 http:// 或 https:// 地址。");
        if (uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("地址中不能包含账号密码、查询参数或 # 片段。");
        var path = uri.AbsolutePath.TrimEnd('/');
        foreach (var suffix in new[] { "/responses", "/models" })
            if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) { path = path[..^suffix.Length]; break; }
        if (Kind == ConnectionKind.PatRelay && path.Length == 0) path = "/backend-api/codex";
        var key = ApiKey.Trim();
        if (key.Length == 0) throw new ArgumentException("请填写第三方服务的 API Key。");
        if (key.Any(char.IsWhiteSpace) || key.Any(char.IsControl)) throw new ArgumentException("API Key 中不能包含空格或换行。");
        var model = Model.Trim();
        if (model.Length == 0 || model.Any(char.IsControl)) throw new ArgumentException("请填写模型 ID，或先获取模型目录。");
        var origin = Kind == ConnectionKind.PatRelay ? KrillLines.Find(uri.AbsoluteUri)?.Origin : null;
        return this with { BaseUrl = (origin ?? uri.GetLeftPart(UriPartial.Authority)) + path, ApiKey = key, Model = model };
    }

    public string Fingerprint()
    {
        var settings = Normalize();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{settings.Kind}\0{settings.BaseUrl}\0{settings.ApiKey}\0{settings.UseSystemProxy}\0{settings.Mode}")));
    }

    public AuthenticationDecision Decide()
    {
        var normalized = Normalize();
        var uri = new Uri(normalized.BaseUrl);
        var origin = uri.GetLeftPart(UriPartial.Authority);
        var relayPath = uri.AbsolutePath is "/backend-api/codex" or "/codex/v1";
        string? blocker = Kind == ConnectionKind.ResponsesApi ? "普通 Responses 服务仅使用 API Key。"
            : !relayPath ? "自定义模型入口使用 API Key。"
            : uri.AbsolutePath == "/codex/v1" ? "备用 /codex/v1 模型入口仅使用 API Key。"
            : uri.Scheme != "https" ? "PAT 要求 HTTPS 的 ChatGPT 后端，当前地址使用 API Key。"
            : null;
        if (Authentication == AuthenticationChoice.Pat && blocker is not null) throw new ArgumentException("无法固定使用 PAT：" + blocker);
        var usePat = Authentication != AuthenticationChoice.ApiKey && blocker is null;
        var modelUrl = normalized.BaseUrl;
        return usePat
            ? new(AuthenticationMode.Pat, modelUrl, origin + "/backend-api/", origin,
                "PAT · 保存不代表连接已验证。")
            : new(AuthenticationMode.ApiKey, modelUrl, null, null,
                Authentication == AuthenticationChoice.ApiKey ? "手动使用 API Key。" : blocker ?? "使用 API Key。");
    }

    public string RenderAuth(bool masked = false)
    {
        if (Mode == ConnectionMode.OfficialWithApi) throw new InvalidOperationException("混合连接由官方登录凭据和独立模型连接组成，不能生成 API Key 登录文件。");
        var key = masked ? "••••••••" : Normalize().ApiKey;
        object fields = Decide().Mode == AuthenticationMode.Pat
            ? new { personal_access_token = key, OPENAI_API_KEY = key }
            : new { auth_mode = "apikey", OPENAI_API_KEY = key };
        return JsonSerializer.Serialize(fields, new JsonSerializerOptions { WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n";
    }
}

public sealed record ConfigSnapshot(string Home, byte[]? Config, byte[]? Auth, string? AuthApiBaseUrl)
{
    public byte[]? Bridge { get; init; }
    public byte[]? Details { get; init; }
}
public sealed record ConfigPlan(ConfigSnapshot Before, byte[] Config, byte[]? Auth, string? AuthApiBaseUrl,
    string ConfigPreview, string AuthPreview, IReadOnlyList<string> Notes,
    AuthenticationMode Mode = AuthenticationMode.Pat, string AuthenticationReason = "")
{
    public byte[]? Bridge { get; init; }
    public byte[]? Details { get; init; }
}
