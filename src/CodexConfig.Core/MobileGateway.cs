using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexConfig.Core;

public sealed record MobileAuth(string Mode, string Username, string Salt, string Hash, int Iterations = 600000, int SessionHours = 12);
public sealed record MobileGatewayConfig(int Protocol, string PublicUrl, int Port, int ControlPort, string ControlToken, string InstanceId,
    MobileAuth Auth, string CodexHome, string DataDir);
public sealed record MobileGatewayStatus(string Service, int Protocol, string InstanceId, string PublicUrl, int Port, bool Checked);

// Mobile access is independent of model routing and never writes Codex settings.
public sealed partial class MobileGatewayManager
{
    public const int Protocol = 1;
    public const int DefaultPort = 13090;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CodexDock.mobile.v1");
    private readonly string home;
    public string StateDirectory { get; }
    public string SettingsPath => Path.Combine(StateDirectory, "mobile-settings.dat");

    public MobileGatewayManager(string codexHome, string? dataRoot = null)
    {
        home = Path.GetFullPath(codexHome);
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(home.TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant()))).ToLowerInvariant();
        StateDirectory = Path.Combine(dataRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexConfig", "mobile"), identity);
    }

    public static string NormalizePublicUrl(string value)
    {
        value = value.Trim();
        if (value.Length > 2048 || value.Any(char.IsControl) || !Uri.TryCreate(value, UriKind.Absolute, out var url) || url.Scheme != "https" ||
            url.UserInfo.Length > 0 || url.Query.Length > 0 || url.Fragment.Length > 0 || url.AbsolutePath != "/" || url.IsLoopback ||
            url.HostNameType != UriHostNameType.Dns || url.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            !DomainPattern().IsMatch(url.IdnHost) || url.Port is < 1 or > 65535)
            throw new ArgumentException("请输入 HTTPS 根域名，不支持路径、凭据、参数或片段。");
        return "https://" + url.IdnHost.ToLowerInvariant() + (url.IsDefaultPort ? "" : ":" + url.Port);
    }

    [GeneratedRegex(@"^(?=.{1,253}$)[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*$")]
    private static partial Regex DomainPattern();

    public MobileGatewayConfig? Load()
    {
        if (!File.Exists(SettingsPath)) return null;
        byte[]? plain = null;
        try
        {
            var info = new FileInfo(SettingsPath);
            if (info.Length > 65536) throw new InvalidDataException();
            plain = ProtectedData.Unprotect(File.ReadAllBytes(SettingsPath), Entropy, DataProtectionScope.CurrentUser);
            var config = JsonSerializer.Deserialize<MobileGatewayConfig>(plain, Json) ?? throw new InvalidDataException();
            if (config.Protocol != Protocol || NormalizePublicUrl(config.PublicUrl) != config.PublicUrl || config.Port is < 1024 or > 65535 ||
                config.ControlPort is < 1024 or > 65535 || config.Port == config.ControlPort || config.ControlToken.Length != 64 || config.InstanceId.Length != 32 ||
                !config.ControlToken.All(Uri.IsHexDigit) || !config.InstanceId.All(Uri.IsHexDigit) || config.Auth.Mode != "password" ||
                config.Auth.Iterations != 600000 || config.Auth.SessionHours != 12 || config.Auth.Salt.Length != 32 || config.Auth.Hash.Length != 64 ||
                !config.Auth.Salt.All(Uri.IsHexDigit) || !config.Auth.Hash.All(Uri.IsHexDigit) || string.IsNullOrWhiteSpace(config.Auth.Username) ||
                config.Auth.Username.Length > 200 || !string.Equals(config.CodexHome, home, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(config.DataDir, StateDirectory, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException();
            return config;
        }
        catch (Exception error) when (error is CryptographicException or JsonException or ArgumentException or InvalidDataException or NullReferenceException)
        { throw new InvalidDataException("手机连接设置无法读取。请保留现有数据，检查当前 Windows 用户和数据目录。", error); }
        finally { if (plain is not null) CryptographicOperations.ZeroMemory(plain); }
    }

    public async Task<MobileGatewayConfig> SaveAsync(string publicUrl, string username, string password, int port = DefaultPort, CancellationToken cancellation = default)
    {
        publicUrl = NormalizePublicUrl(publicUrl); username = username.Trim();
        if (username.Length is < 1 or > 200 || username.Any(char.IsControl)) throw new ArgumentException("账号需为 1 至 200 个字符。");
        if (port is < 1024 or > 65535) throw new ArgumentException("本机端口需在 1024 至 65535 之间。");
        if (!Directory.Exists(home)) throw new ArgumentException("请先选择已存在的 Codex 配置目录。");
        await using var managerLock = await LockAsync(cancellation).ConfigureAwait(false);
        var previous = Load();
        MobileAuth auth;
        if (password.Length == 0 && previous?.Auth.Username == username) auth = previous.Auth;
        else
        {
            if (password.Length is < 12 or > 256 || password.Any(char.IsControl)) throw new ArgumentException("请设置 12 至 256 个字符的独立访问密码。");
            var salt = RandomNumberGenerator.GetBytes(16);
            var hash = await Task.Run(() => Rfc2898DeriveBytes.Pbkdf2(password, salt, 600000, HashAlgorithmName.SHA256, 32), cancellation).ConfigureAwait(false);
            try { auth = new("password", username, Convert.ToHexString(salt).ToLowerInvariant(), Convert.ToHexString(hash).ToLowerInvariant()); }
            finally { CryptographicOperations.ZeroMemory(hash); }
        }
        if (previous is not null) await StopCoreAsync(previous, cancellation).ConfigureAwait(false);
        var controlPort = previous?.ControlPort ?? FreePort();
        while (controlPort == port) controlPort = FreePort();
        var config = new MobileGatewayConfig(Protocol, publicUrl, port, controlPort, previous?.ControlToken ?? RandomHex(32),
            previous?.InstanceId ?? RandomHex(16), auth, home, StateDirectory);
        // Changing the public entry invalidates the old domain's devices as well
        // as any outstanding QR. A password/account change is revoked by Auth.
        if (previous is not null && previous.PublicUrl != publicUrl)
        {
            var sessions = Path.Combine(StateDirectory, "auth-sessions.json");
            if (File.Exists(sessions)) File.Delete(sessions);
        }
        var plain = JsonSerializer.SerializeToUtf8Bytes(config, Json);
        var temporary = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
            await File.WriteAllBytesAsync(temporary, encrypted, cancellation).ConfigureAwait(false);
            File.Move(temporary, SettingsPath, true);
        }
        finally { CryptographicOperations.ZeroMemory(plain); if (File.Exists(temporary)) File.Delete(temporary); }
        return config;
    }

    public async Task<MobileGatewayStatus?> StatusAsync(CancellationToken cancellation = default)
    {
        var config = Load();
        return config is null ? null : await StatusCoreAsync(config, cancellation).ConfigureAwait(false);
    }

    private static async Task<MobileGatewayStatus?> StatusCoreAsync(MobileGatewayConfig config, CancellationToken cancellation)
    {
        try
        {
            using var result = await CallCoreAsync(config, "health", null, cancellation, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            var status = result.Deserialize<MobileGatewayStatus>(Json);
            return status is { Service: "codex-dock-mobile" } && status.InstanceId == config.InstanceId && status.PublicUrl == config.PublicUrl && status.Port == config.Port ? status : null;
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or InvalidDataException or JsonException) { return null; }
    }

    public async Task<MobileGatewayStatus> StartAsync(CancellationToken cancellation = default, string? workerExecutable = null, string? workerScript = null)
    {
        await using var managerLock = await LockAsync(cancellation).ConfigureAwait(false);
        var config = Load() ?? throw new InvalidOperationException("请先保存 HTTPS 域名和访问密码。");
        if (await StatusCoreAsync(config, cancellation).ConfigureAwait(false) is { } existing)
        {
            if (existing.Protocol == Protocol) return existing;
            await StopCoreAsync(config, cancellation).ConfigureAwait(false);
        }
        if (!PortsFree(config)) throw new IOException("手机服务端口已被占用，未启动其他进程。请先检查已有服务。");
        workerExecutable ??= Path.Combine(AppContext.BaseDirectory, "mobile", "CodexDock.Mobile.exe");
        if (!Path.IsPathFullyQualified(workerExecutable) || !File.Exists(workerExecutable) ||
            workerScript is not null && (!Path.IsPathFullyQualified(workerScript) || !File.Exists(workerScript)))
            throw new FileNotFoundException("没有找到手机后台服务，请使用包含 mobile 文件夹的完整安装包。");
        var start = new ProcessStartInfo(workerExecutable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            StandardInputEncoding = new UTF8Encoding(false), WorkingDirectory = Path.GetDirectoryName(workerScript ?? workerExecutable)!
        };
        if (workerScript is not null) { start.ArgumentList.Add("-B"); start.ArgumentList.Add(workerScript); }
        using var process = Process.Start(start) ?? throw new IOException("无法启动手机后台服务。");
        try
        {
            await process.StandardInput.WriteAsync(JsonSerializer.Serialize(config, Json).AsMemory(), cancellation).ConfigureAwait(false);
            process.StandardInput.Close();
            for (var i = 0; i < 80; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                if (await StatusCoreAsync(config, cancellation).ConfigureAwait(false) is { Protocol: Protocol } status) return status;
                if (process.HasExited) break;
                await Task.Delay(100, cancellation).ConfigureAwait(false);
            }
        }
        catch { if (!process.HasExited) process.Kill(); throw; }
        if (!process.HasExited) process.Kill();
        throw new IOException("手机后台服务未能启动，请检查端口和完整安装文件。");
    }

    public async Task StopAsync(CancellationToken cancellation = default)
    {
        await using var managerLock = await LockAsync(cancellation).ConfigureAwait(false);
        if (Load() is { } config) await StopCoreAsync(config, cancellation).ConfigureAwait(false);
    }

    private static async Task StopCoreAsync(MobileGatewayConfig config, CancellationToken cancellation)
    {
        if (await StatusCoreAsync(config, cancellation).ConfigureAwait(false) is null)
        {
            if (!PortsFree(config)) throw new IOException("无法核对已有手机服务，设置未覆盖。请检查端口或稍后重试。");
            return;
        }
        using var result = await CallCoreAsync(config, "stop", new { }, cancellation).ConfigureAwait(false);
        for (var i = 0; i < 80; i++)
        {
            if (PortsFree(config)) return;
            await Task.Delay(100, cancellation).ConfigureAwait(false);
        }
        throw new IOException("手机后台服务尚未完全停止，请稍后重试。");
    }

    public async Task<JsonDocument> ControlAsync(string action, object body, CancellationToken cancellation = default)
    {
        if (action is not ("check" or "pair" or "devices")) throw new ArgumentException("未知手机服务管理操作。");
        var config = Load() ?? throw new InvalidOperationException("请先保存手机连接设置。");
        return await CallCoreAsync(config, action, body, cancellation).ConfigureAwait(false);
    }

    private static async Task<JsonDocument> CallCoreAsync(MobileGatewayConfig config, string action, object? body, CancellationToken cancellation, TimeSpan? timeout = null)
    {
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = timeout ?? TimeSpan.FromSeconds(15) };
        using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, $"http://127.0.0.1:{config.ControlPort}/{action}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ControlToken);
        if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
        if (response.Content.Headers.ContentLength > 262144) throw new InvalidDataException("手机服务响应过大。");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        using var data = new MemoryStream(); var buffer = new byte[4096]; int count;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation); deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(15));
        while ((count = await stream.ReadAsync(buffer, deadline.Token).ConfigureAwait(false)) > 0)
        { if (data.Length + count > 262144) throw new InvalidDataException("手机服务响应过大。"); data.Write(buffer, 0, count); }
        var result = JsonDocument.Parse(data.ToArray());
        if (response.IsSuccessStatusCode) return result;
        var message = result.RootElement.TryGetProperty("error", out var error) ? error.GetString() : null; result.Dispose();
        throw new HttpRequestException(message ?? "手机服务请求失败。", null, response.StatusCode);
    }

    private async Task<FileStream> LockAsync(CancellationToken cancellation)
    {
        Directory.CreateDirectory(StateDirectory);
        for (var i = 0; ; i++)
        {
            try { return new FileStream(Path.Combine(StateDirectory, "manager.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (i < 100) { await Task.Delay(100, cancellation).ConfigureAwait(false); }
        }
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
    private static bool PortsFree(MobileGatewayConfig config)
    {
        var listeners = new List<TcpListener>();
        try
        {
            foreach (var port in new[] { config.Port, config.ControlPort })
            {
                var listener = new TcpListener(IPAddress.Loopback, port) { ExclusiveAddressUse = true }; listeners.Add(listener); listener.Start();
            }
            return true;
        }
        catch (SocketException) { return false; }
        finally { foreach (var listener in listeners) listener.Stop(); }
    }
    private static string RandomHex(int bytes) => Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();
}
