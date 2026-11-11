namespace CodexConfig.Core;

// Consent is private Dock metadata, bound to a scheme/host/port rather than a path.
public static class HttpConnectionPolicy
{
    public static string? RequiredOrigin(string address) =>
        Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == "http" && !uri.IsLoopback && uri.Host.Length > 0 && uri.UserInfo.Length == 0
            ? uri.GetLeftPart(UriPartial.Authority) : null;

    public static bool IsAuthorized(string address, string? allowedOrigin) =>
        RequiredOrigin(address) is not { } origin || string.Equals(origin, allowedOrigin, StringComparison.Ordinal);

    public static void RequireAuthorization(string address, string? allowedOrigin)
    {
        if (!IsAuthorized(address, allowedOrigin))
            throw new InvalidOperationException("此 HTTP 站点尚未确认。请在服务地址下确认凭据和模型内容将以明文发送到该站点。");
    }

    public static bool CanUseProxy(string address, bool requested) =>
        requested && Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme == "https";
}
