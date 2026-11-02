using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodexConfig.Core;

public sealed record ConfigurationSourceRow(string Path, string FileValue, string EffectiveValue, string Source, string Scope);
public sealed record ConfigurationLayer(string Source, string State);
public sealed record ConfigurationReadResult(DateTimeOffset ReadAt, string Executable, string Cwd, string? Profile,
    IReadOnlyList<ConfigurationSourceRow> Rows, IReadOnlyList<ConfigurationLayer> Layers);

// A separate, short-lived local app-server. Never starts a thread, turn, login or tool.
public sealed class ConfigReadClient
{
    public static ProcessStartInfo StartInfo(string executable, string home, string cwd, string? profile = null)
    {
        if (!Path.IsPathFullyQualified(executable) || !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(executable))
            throw new ArgumentException("请选择本机原生 codex.exe。");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetFullPath(cwd) };
        if (!string.IsNullOrWhiteSpace(profile)) { ProfileStore.ValidateName(profile); start.ArgumentList.Add("--profile"); start.ArgumentList.Add(profile); }
        start.ArgumentList.Add("app-server"); start.ArgumentList.Add("--listen"); start.ArgumentList.Add("stdio://");
        start.Environment["CODEX_HOME"] = Path.GetFullPath(home);
        return start;
    }

    public async Task<ConfigurationReadResult> ReadAsync(string executable, string home, string cwd, string? profile, byte[]? localConfig, CancellationToken cancellation = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var process = Process.Start(StartInfo(executable, home, cwd, profile)) ?? throw new IOException("无法启动所选 Codex 执行端。");
        var stderr = Drain(process.StandardError);
        using var kill = timeout.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        try
        {
            async Task Send(object request) { await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(), timeout.Token); await process.StandardInput.FlushAsync(timeout.Token); }
            async Task<JsonElement> Result(int id)
            {
                for (var lines = 0; lines < 10000; lines++)
                {
                    var line = await process.StandardOutput.ReadLineAsync(timeout.Token);
                    if (line is null)
                    {
                        if (profile is not null && await stderr) throw new NotSupportedException("所选内核的 app-server 不支持 --profile；Profile 文件值可以编辑，其生效来源未确认。");
                        break;
                    }
                    if (line.Length > 16 * 1024 * 1024) break;
                    using var json = JsonDocument.Parse(line); var message = json.RootElement;
                    if (!message.TryGetProperty("id", out var value) || !value.TryGetInt32(out var responseId) || responseId != id) continue;
                    if (message.TryGetProperty("error", out _)) throw new InvalidOperationException("所选内核未能读取配置层，请检查项目信任、profile 和客户端版本。");
                    return message.GetProperty("result").Clone();
                }
                throw new InvalidDataException("执行端没有返回可读取的配置快照。");
            }
            await Send(new { id = 1, method = "initialize", @params = new { clientInfo = new { name = "codex_dock_config_reader", version = "1.0" }, capabilities = new { experimentalApi = true } } });
            await Result(1); await Send(new { method = "initialized" });
            await Send(new { id = 2, method = "config/read", @params = new { cwd = Path.GetFullPath(cwd), includeLayers = true } });
            return Parse(await Result(2), localConfig, executable, Path.GetFullPath(cwd), profile);
        }
        catch (JsonException) { throw new InvalidDataException("执行端返回的配置快照格式不受支持。"); }
        finally
        {
            if (!process.HasExited) process.Kill(true);
            await process.WaitForExitAsync(); await stderr;
        }
    }

    public static ConfigurationReadResult Parse(JsonElement response, byte[]? localConfig, string executable, string cwd, string? profile = null)
    {
        if (!response.TryGetProperty("config", out var config) || config.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("配置快照缺少 config 对象。");
        var local = new TomlDocument(localConfig);
        var effective = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        void Walk(JsonElement value, string[] prefix)
        {
            if (effective.Count > 10000) throw new InvalidDataException("配置快照超过展示上限。");
            if (value.ValueKind == JsonValueKind.Object && value.EnumerateObject().Any())
                foreach (var property in value.EnumerateObject()) Walk(property.Value, [.. prefix, property.Name]);
            else if (prefix.Length > 0) effective[TomlDocument.FormatPath(prefix)] = value;
        }
        Walk(config, []);
        response.TryGetProperty("origins", out var origins);
        string Origin(string path)
        {
            var parts = TomlDocument.ParsePath(path);
            for (var length = parts.Length; length > 0; length--)
                if (origins.ValueKind == JsonValueKind.Object && origins.TryGetProperty(TomlDocument.FormatPath(parts.Take(length)), out var metadata) && metadata.TryGetProperty("name", out var source))
                    return Source(source);
            return "来源未知 / 内核默认";
        }
        var rows = TomlDocument.ValuePaths(local.Model).Concat(effective.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(path =>
        {
            var file = local.Get(path);
            var localValue = file is null ? "未设置" : SafeValue(path, JsonSerializer.SerializeToElement(file));
            return new ConfigurationSourceRow(path, localValue, effective.TryGetValue(path, out var value) ? SafeValue(path, value) : "内核未返回 / 未知", Origin(path), ConfigScope.Describe(path));
        }).ToArray();
        var layers = response.TryGetProperty("layers", out var layerList) && layerList.ValueKind == JsonValueKind.Array
            ? layerList.EnumerateArray().Select(layer => new ConfigurationLayer(layer.TryGetProperty("name", out var name) ? Source(name) : "未知层",
                layer.TryGetProperty("disabledReason", out var reason) && reason.ValueKind == JsonValueKind.String ? "未采用（如项目未受信任）" : "已加载")).ToArray() : [];
        return new(DateTimeOffset.UtcNow, executable, cwd, profile, rows, layers);
    }

    private static string Source(JsonElement source)
    {
        string? Text(string key) => source.ValueKind == JsonValueKind.Object && source.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        var label = Text("type") switch
        {
            "packagedDefaults" => "安装包默认", "user" => "用户文件", "project" => "项目配置", "sessionFlags" => "本次读取进程参数",
            "system" or "mdm" or "enterpriseManaged" or "legacyManagedConfigTomlFromFile" or "legacyManagedConfigTomlFromMdm" => "组织 / 系统管理层", _ => "未知层"
        };
        var location = Text("file") ?? Text("dotCodexFolder");
        return label + (location is not null ? " · " + location : "") + (Text("profile") is { } profile ? " · profile " + profile : "");
    }

    public static string SafeValue(string path, JsonElement value)
    {
        JsonNode? Mask(string key, JsonElement item)
        {
            if (TomlDocument.IsSensitivePath(key)) return JsonValue.Create("••••••••");
            if (item.ValueKind == JsonValueKind.Object)
            {
                var result = new JsonObject();
                foreach (var property in item.EnumerateObject()) result[property.Name] = Mask(TomlDocument.FormatPath(TomlDocument.ParsePath(key).Append(property.Name)), property.Value);
                return result;
            }
            if (item.ValueKind == JsonValueKind.Array) return new JsonArray(item.EnumerateArray().Select(entry => Mask(key, entry)).ToArray());
            if (item.ValueKind == JsonValueKind.String && Uri.TryCreate(item.GetString(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && (uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0))
                return JsonValue.Create(uri.GetLeftPart(UriPartial.Authority).Replace(uri.UserInfo + "@", "") + uri.AbsolutePath + "（地址参数已隐藏）");
            return JsonNode.Parse(item.GetRawText());
        }
        var text = Mask(path, value)?.ToJsonString() ?? "null";
        return text.Length > 800 ? text[..800] + "…" : text;
    }

    private static async Task<bool> Drain(StreamReader reader)
    {
        const string marker = "--profile only applies to runtime commands";
        var buffer = new char[2048]; var tail = ""; var unsupportedProfile = false; int count;
        while ((count = await reader.ReadAsync(buffer)) > 0)
        {
            var text = tail + new string(buffer, 0, count); unsupportedProfile |= text.Contains(marker, StringComparison.Ordinal);
            tail = text.Length > marker.Length ? text[^marker.Length..] : text;
        }
        Array.Clear(buffer); return unsupportedProfile;
    }
}
