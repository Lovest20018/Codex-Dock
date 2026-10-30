using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

namespace CodexConfig.Core;

// The worker is owned by the saved connection, not by an editor process.
public static class BridgeProcess
{
    public static async Task<BridgeStatus?> StatusAsync(MixedConnection connection, CancellationToken cancellation = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            cancellation = timeout.Token;
            using var client = LocalClient(TimeSpan.FromSeconds(1));
            using var request = Request(connection, HttpMethod.Get, "health");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 8192) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
            using var data = new MemoryStream(); var buffer = new byte[1024]; int count;
            while ((count = await stream.ReadAsync(buffer, cancellation).ConfigureAwait(false)) > 0)
            { if (data.Length + count > 8192) return null; data.Write(buffer, 0, count); }
            return JsonSerializer.Deserialize<BridgeStatus>(data.ToArray());
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException) { return null; }
    }

    public static async Task<BridgeStatus> EnsureAsync(string home, byte[] bytes, string? executable = null, CancellationToken cancellation = default)
    {
        var connection = MixedConnection.Read(bytes);
        if (await StatusAsync(connection, cancellation).ConfigureAwait(false) is { } existing)
        {
            if (existing.ProtocolVersion == ModelBridge.ProtocolVersion) return existing;
            await StopAsync(bytes, cancellation).ConfigureAwait(false);
        }
        var start = new ProcessStartInfo(executable ?? Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = AppContext.BaseDirectory };
        start.ArgumentList.Add("--model-bridge"); start.ArgumentList.Add(Path.GetFullPath(home));
        using var process = Process.Start(start) ?? throw new IOException("无法启动本地网关。");
        for (var i = 0; i < 60; i++)
        {
            if (await StatusAsync(connection, cancellation).ConfigureAwait(false) is { ProtocolVersion: ModelBridge.ProtocolVersion } status) return status;
            // A concurrent caller can hold the worker lock; allow its listener time to start.
            await Task.Delay(100, cancellation).ConfigureAwait(false);
        }
        throw new IOException("本地网关未能启动，请检查端口占用、官方登录和配置目录。");
    }

    public static async Task StopAsync(byte[] bytes, CancellationToken cancellation = default)
    {
        var connection = MixedConnection.Read(bytes);
        if (await StatusAsync(connection, cancellation).ConfigureAwait(false) is null) return;
        using var client = LocalClient(TimeSpan.FromSeconds(3));
        using var request = Request(connection, HttpMethod.Post, "stop");
        using var response = await client.SendAsync(request, cancellation).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        for (var i = 0; i < 60; i++)
        {
            if (await StatusAsync(connection, cancellation).ConfigureAwait(false) is null) return;
            await Task.Delay(100, cancellation).ConfigureAwait(false);
        }
        throw new IOException("网关尚未停止，请稍后重试。");
    }

    public static async Task<int> RunAsync(string home, CancellationToken cancellation = default)
    {
        home = Path.GetFullPath(home);
        // Do not create a home for a stale command, or fall back to the user's default.
        var path = Path.Combine(home, MixedConnection.FileName);
        if (!File.Exists(path)) return 2;
        FileStream workerLock;
        try { workerLock = new FileStream(Path.Combine(home, "codex-dock-gateway.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read, 1, FileOptions.DeleteOnClose); }
        catch (IOException) { return 2; }
        await using var ownedLock = workerLock;
        try
        {
            var initial = MixedConnection.Read(await File.ReadAllBytesAsync(path, cancellation).ConfigureAwait(false));
            MixedConnection ReadConnection()
            {
                var current = MixedConnection.Read(File.ReadAllBytes(path));
                if (current.Port != initial.Port || current.LocalToken != initial.LocalToken) throw new InvalidDataException("连接已切换。");
                return current;
            }
            byte[]? ReadAuth()
            {
                var auth = Path.Combine(home, "auth.json");
                return File.Exists(auth) && new FileInfo(auth).Length < 2 * 1024 * 1024 ? File.ReadAllBytes(auth) : null;
            }
            var config = new TomlDocument(await File.ReadAllBytesAsync(Path.Combine(home, "config.toml"), cancellation).ConfigureAwait(false));
            if (!initial.OwnsRoute(config)) return 2;
            await using var bridge = new ModelBridge(ReadConnection, ReadAuth);
            await bridge.StartAsync(cancellation).ConfigureAwait(false);
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1)); var failures = 0;
            while (await timer.WaitForNextTickAsync(cancellation).ConfigureAwait(false))
            {
                if (bridge.StopRequested || !File.Exists(path)) break;
                try
                {
                    var current = ReadConnection();
                    config = new TomlDocument(await File.ReadAllBytesAsync(Path.Combine(home, "config.toml"), cancellation).ConfigureAwait(false));
                    if (!current.OwnsRoute(config)) break;
                    failures = 0;
                }
                catch (Exception error) when (error is IOException or InvalidDataException or System.Security.Cryptography.CryptographicException)
                { if (++failures >= 3) break; }
            }
            return 0;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or OperationCanceledException)
        { return 2; }
    }

    private static HttpClient LocalClient(TimeSpan timeout) => new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = timeout };
    private static HttpRequestMessage Request(MixedConnection connection, HttpMethod method, string action)
    {
        var request = new HttpRequestMessage(method, $"http://127.0.0.1:{connection.Port}/codex-config/{action}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.LocalToken); return request;
    }
}
