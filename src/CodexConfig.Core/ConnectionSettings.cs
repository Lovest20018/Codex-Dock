using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodexConfig.Core;

// Keep the stored numeric values used by existing encrypted gateway connections.
public enum ConnectionKind { CodexRelay = 0, ResponsesApi = 1 }
public enum AuthenticationMode { ApiKey = 1, OfficialWithApi = 2 }
public enum ConnectionMode { Direct, OfficialWithApi }
public sealed record AuthenticationDecision(AuthenticationMode Mode, string ModelBaseUrl, string Reason);

public sealed record ConnectionSettings(string BaseUrl, string ApiKey, string Model,
    bool FastMode = true, bool ContextManagement = true, bool WebSearch = true)
{
    public ConnectionKind Kind { get; init; } = ConnectionKind.CodexRelay;
    public bool NeedsApiKeyMigration { get; init; }
    public ConnectionMode Mode { get; init; }
    public bool UseSystemProxy { get; init; }
    public ConnectionDetails Details { get; init; } = new();
    public string ProviderId { get; init; } = "openai";
    public string ProviderName { get; init; } = "";
    public string CredentialSource { get; init; } = "";
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
        if (Kind == ConnectionKind.CodexRelay && path.Length == 0) path = "/backend-api/codex";
        var key = ApiKey.Trim();
        if (key.Length == 0) throw new ArgumentException("请填写 API Key。");
        if (key.Any(char.IsWhiteSpace) || key.Any(char.IsControl)) throw new ArgumentException("API Key 中不能包含空格或换行。");
        var model = Model.Trim();
        if (model.Length == 0 || model.Any(char.IsControl)) throw new ArgumentException("请填写模型 ID，或先获取模型目录。");
        var origin = Kind == ConnectionKind.CodexRelay ? KrillLines.Find(uri.AbsoluteUri)?.Origin : null;
        return this with { BaseUrl = (origin ?? uri.GetLeftPart(UriPartial.Authority)) + path, ApiKey = key, Model = model };
    }

    public string Fingerprint()
    {
        var settings = Normalize();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{settings.Kind}\0{settings.BaseUrl}\0{settings.ApiKey}\0{settings.Mode}\0{settings.UseSystemProxy}\0{settings.Details.HttpOrigin}")));
    }

    public AuthenticationDecision Decide()
    {
        var normalized = Normalize();
        return new(AuthenticationMode.ApiKey, normalized.BaseUrl, "使用 API Key 认证；保存不代表服务已验证。");
    }

    public string RenderAuth(bool masked = false)
    {
        if (Mode == ConnectionMode.OfficialWithApi) throw new InvalidOperationException("混合连接由官方登录凭据和独立模型连接组成，不能生成 API Key 登录文件。");
        var key = masked ? "••••••••" : Normalize().ApiKey;
        var fields = new { auth_mode = "apikey", OPENAI_API_KEY = key };
        return JsonSerializer.Serialize(fields, new JsonSerializerOptions { WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n";
    }
}

public sealed record ConfigSnapshot(string Home, byte[]? Config, byte[]? Auth, string? AuthApiBaseUrl)
{
    public byte[]? Bridge { get; init; }
    public byte[]? Details { get; init; }
    public byte[]? CatalogIndex { get; init; }
    public CatalogReference? Catalog { get; init; }
    public bool SameContents(ConfigSnapshot other) => string.Equals(Home, other.Home, StringComparison.OrdinalIgnoreCase) &&
        AuthApiBaseUrl == other.AuthApiBaseUrl && Same(Config, other.Config) && Same(Auth, other.Auth) && Same(Bridge, other.Bridge) && Same(Details, other.Details) &&
        Same(CatalogIndex, other.CatalogIndex) && (Catalog is null ? other.Catalog is null : Catalog.Same(other.Catalog));

    private static bool Same(byte[]? left, byte[]? right) => left is null ? right is null : right is not null && left.AsSpan().SequenceEqual(right);
}
public sealed record ConfigPlan(ConfigSnapshot Before, byte[] Config, byte[]? Auth, string? AuthApiBaseUrl,
    string ConfigPreview, string AuthPreview, IReadOnlyList<string> Notes,
    AuthenticationMode Mode = AuthenticationMode.ApiKey, string AuthenticationReason = "")
{
    public byte[]? Bridge { get; init; }
    public byte[]? Details { get; init; }
    public ManagedCatalogUpdate? CatalogUpdate { get; init; }
}
