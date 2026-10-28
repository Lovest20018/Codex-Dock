using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using CodexConfig.Core;

namespace CodexConfig.App;

internal static class BridgeProcess
{
    public static async Task<BridgeStatus?> StatusAsync(MixedConnection connection)
    {
        try
        {
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(1) };
            using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{connection.Port}/codex-config/health");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.LocalToken);
            using var response = await client.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            return bytes.Length < 4096 ? JsonSerializer.Deserialize<BridgeStatus>(bytes) : null;
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException) { return null; }
    }

    public static async Task<BridgeStatus> EnsureAsync(string home, byte[] bytes)
    {
        var connection = MixedConnection.Read(bytes);
        if (await StatusAsync(connection).ConfigureAwait(false) is { } existing) return existing;
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = AppContext.BaseDirectory };
        start.ArgumentList.Add("--model-bridge"); start.ArgumentList.Add(Path.GetFullPath(home));
        using var process = Process.Start(start) ?? throw new IOException("无法启动本地模型连接服务。");
        for (var i = 0; i < 40; i++)
        {
            if (await StatusAsync(connection).ConfigureAwait(false) is { } status) return status;
            if (process.HasExited) break;
            await Task.Delay(100).ConfigureAwait(false);
        }
        throw new IOException("本地模型连接服务未能启动。请检查端口占用，或恢复上次配置。");
    }

    public static async Task StopAsync(byte[] bytes)
    {
        var connection = MixedConnection.Read(bytes);
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(3) };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{connection.Port}/codex-config/stop");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.LocalToken);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        for (var i = 0; i < 50; i++)
        {
            if (await StatusAsync(connection) is null) return;
            await Task.Delay(100);
        }
        throw new IOException("本地连接尚未停止，请稍后重试。");
    }

    public static async Task RunAsync(string home)
    {
        home = Path.GetFullPath(home);
        var path = Path.Combine(home, MixedConnection.FileName);
        var initial = MixedConnection.Read(await File.ReadAllBytesAsync(path));
        MixedConnection ReadConnection()
        {
            var current = MixedConnection.Read(File.ReadAllBytes(path));
            if (current.Port != initial.Port || current.LocalToken != initial.LocalToken) throw new IOException("Connection changed.");
            return current;
        }
        byte[]? ReadAuth()
        {
            var auth = Path.Combine(home, "auth.json");
            return File.Exists(auth) && new FileInfo(auth).Length < 2 * 1024 * 1024 ? File.ReadAllBytes(auth) : null;
        }
        await using var bridge = new ModelBridge(ReadConnection, ReadAuth);
        await bridge.StartAsync();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        var failures = 0;
        while (await timer.WaitForNextTickAsync())
        {
            if (bridge.StopRequested) break;
            if (!File.Exists(path)) break;
            try
            {
                _ = ReadConnection();
                var config = new TomlDocument(await File.ReadAllBytesAsync(Path.Combine(home, "config.toml")));
                if (config.String("model_provider") != MixedConnection.Provider) break;
                failures = 0;
            }
            catch (Exception) { if (++failures >= 3) break; }
        }
    }
}
