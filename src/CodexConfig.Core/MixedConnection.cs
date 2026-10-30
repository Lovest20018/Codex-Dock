using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodexConfig.Core;

// Read-only compatibility with encrypted connections created by versions before 0.11.
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
    public string LocalBaseUrl => $"http://127.0.0.1:{Port}/v1";

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

    private static JsonDocument Parse(byte[] bytes) => JsonDocument.Parse(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? bytes.AsMemory(3) : bytes.AsMemory());
}
