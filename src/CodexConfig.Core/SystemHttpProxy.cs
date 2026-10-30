using System.Net;
using Microsoft.Win32;

namespace CodexConfig.Core;

// Resolve the current user's fixed HTTP proxy, without changing OS settings.
public sealed class SystemHttpProxy : IWebProxy
{
    public ICredentials? Credentials { get; set; }
    public bool IsBypassed(Uri host) => false;
    public Uri GetProxy(Uri destination) => Detect(destination.Scheme) ?? throw new InvalidOperationException("未检测到本机 HTTP 代理，请启用 Windows 系统代理后重试。");

    public static Uri? Detect(string scheme = "https")
    {
        using var settings = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
        if (settings?.GetValue("ProxyEnable") is int enabled && enabled != 0 &&
            Parse(settings.GetValue("ProxyServer") as string, scheme) is { } system) return system;
        var secure = scheme is "https" or "wss";
        return Parse(secure ? Environment.GetEnvironmentVariable("HTTPS_PROXY") ?? Environment.GetEnvironmentVariable("https_proxy") : null, scheme)
            ?? Parse(Environment.GetEnvironmentVariable("HTTP_PROXY") ?? Environment.GetEnvironmentVariable("http_proxy"), scheme);
    }

    public static Uri? Parse(string? server, string scheme = "https")
    {
        if (string.IsNullOrWhiteSpace(server)) return null;
        server = server.Trim();
        if (server.Contains('='))
        {
            var entries = server.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(entry => entry.Split('=', 2, StringSplitOptions.TrimEntries))
                .Where(parts => parts.Length == 2).ToArray();
            var protocol = scheme is "https" or "wss" ? "https" : "http";
            server = entries.FirstOrDefault(parts => parts[0].Equals(protocol, StringComparison.OrdinalIgnoreCase))?[1]
                ?? entries.FirstOrDefault(parts => parts[0].Equals("http", StringComparison.OrdinalIgnoreCase))?[1];
        }
        if (string.IsNullOrWhiteSpace(server)) return null;
        if (!server.Contains("://", StringComparison.Ordinal)) server = "http://" + server;
        if (!Uri.TryCreate(server, UriKind.Absolute, out var address) || address.Scheme is not ("http" or "https") ||
            address.Host.Length == 0 || address.UserInfo.Length > 0 || address.Query.Length > 0 || address.Fragment.Length > 0 || address.AbsolutePath != "/") return null;
        return address;
    }
}
