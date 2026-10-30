using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodexConfig.Core;

// This file is private to this tool; Codex receives only a loopback address and a local token.
public sealed class MixedConnection
{
    public const string Provider = "codex_config_mixed";
    public const string FileName = "codex-config-connection.dat";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CodexConfig.mixed.v1");
    public string BaseUrl { get; init; } = "";
    public string ApiKey { get; init; } = "";
    public ConnectionKind Kind { get; init; }
    public string LocalToken { get; init; } = "";
    public int Port { get; init; }
    public bool UseSystemProxy { get; init; }
    public bool NativeRoute { get; init; }
    public Dictionary<string, string?> OriginalValues { get; init; } = new(StringComparer.Ordinal);
    public string? OriginalAuthApiBaseUrl { get; init; }
    public string LocalBaseUrl => $"http://127.0.0.1:{Port}/v1";
    public string NativeBaseUrl => $"http://127.0.0.1:{Port}/backend-api/codex";

    public bool OwnsRoute(TomlDocument doc) => doc.String("model_provider") == Provider &&
        doc.Get("model_providers." + Provider + ".base_url") as string == LocalBaseUrl &&
        doc.Get("model_providers." + Provider + ".experimental_bearer_token") as string == LocalToken ||
        NativeRoute && (doc.String("model_provider") is null or "openai") && doc.String("openai_base_url") == NativeBaseUrl;

    public static MixedConnection Create(ConnectionSettings settings, MixedConnection? previous = null)
    {
        settings = settings.Normalize();
        var port = previous?.Port ?? FreePort();
        var token = previous?.LocalToken ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        return new() { BaseUrl = settings.BaseUrl, ApiKey = settings.ApiKey, Kind = settings.Kind, Port = port, LocalToken = token, UseSystemProxy = settings.UseSystemProxy,
            NativeRoute = true, OriginalValues = previous?.OriginalValues ?? new(StringComparer.Ordinal), OriginalAuthApiBaseUrl = previous?.OriginalAuthApiBaseUrl };
    }

    public byte[] Protect()
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(this);
        try { return ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    public static MixedConnection Read(byte[] bytes)
    {
        var plain = ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.CurrentUser);
        try
        {
            var result = JsonSerializer.Deserialize<MixedConnection>(plain) ?? throw new InvalidDataException("本地连接数据无效。");
            if (result.Port is < 1024 or > 65535 || result.LocalToken.Length != 64 || !result.LocalToken.All(Uri.IsHexDigit))
                throw new InvalidDataException("本地连接数据无效。");
            _ = new ConnectionSettings(result.BaseUrl, result.ApiKey, "connection") { Kind = result.Kind }.Normalize();
            if (new Uri(result.BaseUrl).IsLoopback && new Uri(result.BaseUrl).Port == result.Port)
                throw new InvalidDataException("模型入口不能指向本地连接自身。");
            return result;
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}

public static class OfficialCredentials
{
    // Structure detection is deliberately not an online identity or entitlement check.
    public static bool HasLogin(byte[]? bytes)
    {
        if (bytes is null) return false;
        try
        {
            using var doc = Parse(bytes);
            var root = doc.RootElement;
            return root.TryGetProperty("tokens", out var tokens) && tokens.ValueKind == JsonValueKind.Object &&
                new[] { "access_token", "refresh_token", "id_token" }.All(name =>
                    tokens.TryGetProperty(name, out var token) && token.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(token.GetString())) &&
                (!root.TryGetProperty("auth_mode", out var mode) || mode.ValueKind == JsonValueKind.Null || mode.GetString() == "chatgpt");
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException) { return false; }
    }

    public static byte[] ForMixed(byte[]? bytes)
    {
        if (!HasLogin(bytes)) throw new InvalidOperationException("请先完成官方登录，再保存“官方账号 + API”连接。");
        using var document = Parse(bytes!);
        if (!document.RootElement.TryGetProperty("OPENAI_API_KEY", out _) && !document.RootElement.TryGetProperty("personal_access_token", out _))
            return bytes!.ToArray();
        var node = JsonNode.Parse(document.RootElement.GetRawText())!.AsObject();
        node.Remove("OPENAI_API_KEY"); node.Remove("personal_access_token");
        node["auth_mode"] = "chatgpt";
        return Encoding.UTF8.GetBytes(node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    public static bool MatchesAccessToken(byte[]? bytes, string token)
    {
        if (!HasLogin(bytes) || token.Length == 0) return false;
        try
        {
            using var doc = Parse(bytes!);
            return doc.RootElement.TryGetProperty("tokens", out var tokens) && tokens.ValueKind == JsonValueKind.Object &&
                tokens.TryGetProperty("access_token", out var access) && access.ValueKind == JsonValueKind.String &&
                CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(access.GetString()!), Encoding.UTF8.GetBytes(token));
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException) { return false; }
    }

    private static JsonDocument Parse(byte[] bytes) => JsonDocument.Parse(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? bytes.AsMemory(3) : bytes.AsMemory());
}
