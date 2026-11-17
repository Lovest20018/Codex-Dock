using System.Text;
using System.Text.Json;
using CodexConfig.Core;
using Tomlyn.Model;

namespace CodexConfig.Application;

public sealed record ConfigurationPreviewFile(byte[]? Bytes, bool Unreadable = false);
public sealed record ConfigurationPreviewFiles(ConfigurationPreviewFile Config, ConfigurationPreviewFile Auth, string? AuthApiBaseUrl, bool EnvironmentUnreadable = false);
public enum ConfigurationPreviewState { Ready, Missing, Unreadable }
public sealed record ConfigurationPreviewSection(ConfigurationPreviewState State, string Text, bool Truncated = false);
public sealed record ConfigurationPreviewSnapshot(string Home, DateTimeOffset ReadAt, ConfigurationPreviewSection Config, ConfigurationPreviewSection Auth, ConfigurationPreviewSection Environment);
public interface IConfigurationPreviewRuntime
{ Task<ConfigurationPreviewFiles> ReadAsync(string home, CancellationToken cancellation); }

public sealed class NativeConfigurationPreviewRuntime(IUserEnvironment environment) : IConfigurationPreviewRuntime
{
    public Task<ConfigurationPreviewFiles> ReadAsync(string home, CancellationToken cancellation) => Task.Run<ConfigurationPreviewFiles>(() =>
    {
        ConfigurationPreviewFile Read(string name)
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                var path = Path.Combine(home, name);
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length > 8 * 1024 * 1024 || File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) return new(null, true);
                var bytes = new byte[(int)stream.Length]; stream.ReadExactly(bytes); cancellation.ThrowIfCancellationRequested(); return new(bytes);
            }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return new(null); }
            catch (Exception error) when (WorkspaceStorageErrors.IsRecoverable(error)) { return new(null, true); }
        }
        var config = Read("config.toml"); var auth = Read("auth.json");
        try { return new(config, auth, environment.Read()); }
        catch (Exception error) when (WorkspaceStorageErrors.IsRecoverable(error) || error is System.Security.SecurityException) { return new(config, auth, null, true); }
    }, cancellation);
}

public sealed class ConfigurationPreviewService(IConfigurationPreviewRuntime runtime)
{
    public async Task<ConfigurationPreviewSnapshot> ReadAsync(string home, Func<string> currentHome, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested(); home = Normalize(home);
        var first = Freeze(await runtime.ReadAsync(home, cancellation)); cancellation.ThrowIfCancellationRequested();
        var result = await Task.Run(() => Project(home, first, cancellation), cancellation);
        var last = await runtime.ReadAsync(home, cancellation); cancellation.ThrowIfCancellationRequested();
        if (!string.Equals(home, Normalize(currentHome()), StringComparison.OrdinalIgnoreCase) || !Same(first, last))
            throw new OperationCanceledException("配置目录或文件在读取期间改变，请重新读取预览。");
        return result;
    }
    public static ConfigurationPreviewSnapshot Project(string home, ConfigurationPreviewFiles files, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        ConfigurationPreviewSection Config()
        {
            if (files.Config.Unreadable || files.Config.Bytes?.Length > 8 * 1024 * 1024) return Bad("config.toml 无法读取，或超过 8 MiB；原文件保持不变。");
            if (files.Config.Bytes is null) return Missing("尚无 config.toml；读取不会创建文件。");
            try
            {
                var document = new TomlDocument(files.Config.Bytes);
                object Mask(string path, object? value)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (TomlDocument.IsSensitivePath(path)) return "••••••••";
                    if (value is TomlTable table)
                    {
                        var result = new TomlTable();
                        foreach (var (key, child) in table) result[key] = Mask(TomlDocument.FormatPath(TomlDocument.ParsePath(path).Append(key)), child);
                        return result;
                    }
                    if (value is TomlArray array) { var result = new TomlArray(); foreach (var item in array) result.Add(Mask(path, item)); return result; }
                    if (value is TomlTableArray tables) { var result = new TomlTableArray(); foreach (var item in tables) result.Add((TomlTable)Mask(path, item)); return result; }
                    return value is string text ? ConfigReadClient.RedactValue(path, JsonSerializer.SerializeToElement(text))!.GetValue<string>() : value ?? "未设置";
                }
                var masked = new TomlTable();
                foreach (var (key, value) in document.Model) masked[key] = Mask(TomlDocument.FormatPath([key]), value);
                return Bound(Tomlyn.Toml.FromModel(masked));
            }
            catch (Exception error) when (WorkspaceStorageErrors.IsRecoverable(error)) { return Bad("config.toml 无法解析；请修复格式或编码后重新读取，原文件保持不变。"); }
        }
        ConfigurationPreviewSection Auth()
        {
            if (files.Auth.Unreadable || files.Auth.Bytes?.Length > 8 * 1024 * 1024) return Bad("auth.json 无法读取，或超过 8 MiB；原文件保持不变。");
            if (files.Auth.Bytes is null) return Missing("尚无 auth.json；系统凭据或其他认证方式未在本页读取。");
            try
            {
                var bytes = files.Auth.Bytes; using var json = JsonDocument.Parse(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? bytes.AsMemory(3) : bytes);
                if (json.RootElement.ValueKind != JsonValueKind.Object) return Bad("auth.json 不是 JSON 对象，内容不展示。");
                var summary = new Dictionary<string, object>(); var root = json.RootElement;
                if (root.TryGetProperty("auth_mode", out var mode)) summary["auth_mode"] = mode.ValueKind == JsonValueKind.String && mode.GetString() is "apikey" or "chatgpt" ? mode.GetString()! : "已设置（内容已隐藏）";
                foreach (var key in new[] { "OPENAI_API_KEY", "personal_access_token", "tokens" }) if (root.TryGetProperty(key, out _)) summary[key] = "已设置（内容已隐藏）";
                summary["other_fields_hidden"] = root.EnumerateObject().Count(property => property.Name is not ("auth_mode" or "OPENAI_API_KEY" or "personal_access_token" or "tokens"));
                return Bound(JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            }
            catch (JsonException) { return Bad("auth.json 无法解析，内容不展示；请修复后重新读取。"); }
        }
        ConfigurationPreviewSection Environment()
        {
            const string name = "CODEX_AUTHAPI_BASE_URL\n";
            if (files.EnvironmentUnreadable) return Bad(name + "当前 Windows 用户的变量无法读取。");
            if (files.AuthApiBaseUrl is null) return Missing(name + "（未设置）");
            var value = files.AuthApiBaseUrl;
            var text = value.Length <= 8192 && Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
                ? ConfigReadClient.RedactValue("CODEX_AUTHAPI_BASE_URL", JsonSerializer.SerializeToElement(value))!.GetValue<string>() : "已设置（内容已隐藏）";
            return Bound(name + text);
        }
        return new(Normalize(home), DateTimeOffset.Now, Config(), Auth(), Environment());
    }
    private static ConfigurationPreviewSection Bound(string value)
    { const int max = 1024 * 1024; return new(ConfigurationPreviewState.Ready, value.Length > max ? value[..max] + "\n…显示长度已达上限，剩余内容未展示。" : value, value.Length > max); }
    private static ConfigurationPreviewSection Missing(string message) => new(ConfigurationPreviewState.Missing, message);
    private static ConfigurationPreviewSection Bad(string message) => new(ConfigurationPreviewState.Unreadable, message);
    private static string Normalize(string home) => Path.IsPathFullyQualified(home) ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(home)) : throw new ArgumentException("请选择完整配置目录。");
    private static ConfigurationPreviewFiles Freeze(ConfigurationPreviewFiles files) => files with { Config = files.Config with { Bytes = files.Config.Bytes?.ToArray() }, Auth = files.Auth with { Bytes = files.Auth.Bytes?.ToArray() } };
    private static bool Same(ConfigurationPreviewFiles first, ConfigurationPreviewFiles last) => first.AuthApiBaseUrl == last.AuthApiBaseUrl && first.EnvironmentUnreadable == last.EnvironmentUnreadable &&
        first.Config.Unreadable == last.Config.Unreadable && first.Auth.Unreadable == last.Auth.Unreadable && ConfigurationSourceService.SameContent(first.Config.Bytes, last.Config.Bytes) && ConfigurationSourceService.SameContent(first.Auth.Bytes, last.Auth.Bytes);
}
