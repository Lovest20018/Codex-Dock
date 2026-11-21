using System.Net;
using System.Text.Json;
using CodexConfig.Core;

namespace CodexConfig.Application;

public sealed record MobileConnectionSettings(string Home, bool Configured, string PublicUrl, string Username, int Port, int ControlPort, string Revision);
public sealed class MobileSetupInput(string publicUrl, string username, string password, int port)
{
    public string PublicUrl { get; } = publicUrl;
    public string Username { get; } = username;
    public string Password { get; } = password;
    public int Port { get; } = port;
    public override string ToString() => "手机连接输入（密码已隐藏）";
}
public sealed class MobileSetupReview
{
    private int consumed;
    internal MobileSetupInput Input { get; }
    internal MobileSetupReview(MobileConnectionSettings before, MobileSetupInput input, IReadOnlyList<string> changes)
    { Before = before; Input = input; Changes = Array.AsReadOnly(changes.ToArray()); }
    internal bool TryConsume() => Interlocked.CompareExchange(ref consumed, 1, 0) == 0;
    public MobileConnectionSettings Before { get; }
    public IReadOnlyList<string> Changes { get; }
    public bool HasChanges => Changes.Count > 0;
    public string Description => Describe(start: true);
    public string Describe(bool start) => "配置目录：" + Before.Home + "\n\n" + string.Join("\n", Changes) + "\n\n" +
        (start ? "保存后启动手机网关。" : "保存会停止旧设置对应的服务。下一步可以启动手机服务。") +
        "修改域名、账号或密码会使旧设备授权失效；只改端口会保留设备身份。";
}
public sealed record MobileDevice(string Id, string Label, string Ip, DateTimeOffset LastSeen);
public sealed record MobileDeviceList(IReadOnlyList<MobileDevice> Devices, IReadOnlyList<string> BlockedIps);
public sealed record MobileConnectionView(MobileConnectionSettings Settings, bool? Running, bool EntryChecked, MobileDeviceList Devices, string CodexReadiness = "unknown");
public sealed record MobilePairGrant(string Id, byte[] Image, DateTimeOffset Expires);
public sealed record MobileActionResult<T>(bool Succeeded, string Message, T? Value = default, bool SettingsSaved = false);

public interface IMobileConnectionRuntime
{
    Task<MobileConnectionSettings> LoadAsync(string home, CancellationToken cancellation);
    Task<MobileConnectionSettings> SaveAsync(string home, MobileSetupInput input, string expectedRevision, CancellationToken cancellation);
    Task StartAsync(string home, CancellationToken cancellation);
    Task<MobileGatewayStatus?> StatusAsync(string home, CancellationToken cancellation);
    Task<JsonDocument> ControlAsync(string home, string action, object body, CancellationToken cancellation);
}

public sealed class NativeMobileConnectionRuntime(string? root = null, string? executable = null, string? script = null) : IMobileConnectionRuntime
{
    private MobileGatewayManager Manager(string home) => new(home, root);
    public Task<MobileConnectionSettings> LoadAsync(string home, CancellationToken cancellation)
        => Task.Run(() => Read(home), cancellation);
    private MobileConnectionSettings Read(string home)
    {
        var manager = Manager(home);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var revision = manager.SettingsRevision(); var saved = manager.Load();
            if (revision == manager.SettingsRevision())
                return new(Path.GetFullPath(home), saved is not null, saved?.PublicUrl ?? "", saved?.Auth.Username ?? "codex",
                    saved?.Port ?? MobileGatewayManager.DefaultPort, saved?.ControlPort ?? 0, revision);
        }
        throw new IOException("手机设置正在被其他窗口更新，请稍后重新读取。");
    }
    public async Task<MobileConnectionSettings> SaveAsync(string home, MobileSetupInput input, string expectedRevision, CancellationToken cancellation)
    {
        await Manager(home).SaveAsync(input.PublicUrl, input.Username, input.Password, input.Port, cancellation, expectedRevision);
        return Read(home);
    }
    public async Task StartAsync(string home, CancellationToken cancellation) => await Manager(home).StartAsync(cancellation, executable, script);
    public Task<MobileGatewayStatus?> StatusAsync(string home, CancellationToken cancellation) => Manager(home).StatusAsync(cancellation);
    public Task<JsonDocument> ControlAsync(string home, string action, object body, CancellationToken cancellation) => Manager(home).ControlAsync(action, body, cancellation);
}

public sealed class MobileConnectionService(IMobileConnectionRuntime runtime, WorkspaceApplyCoordinator operations, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;
    public bool IsBusy { get; private set; }
    public event EventHandler? Changed;
    public Task<MobileConnectionSettings> LoadSettingsAsync(string home, CancellationToken cancellation = default) => runtime.LoadAsync(home, cancellation);

    public async Task<MobileSetupReview> PrepareAsync(string home, MobileSetupInput input, CancellationToken cancellation = default)
    {
        var url = MobileGatewayManager.NormalizePublicUrl(input.PublicUrl); var username = input.Username.Trim();
        if (username.Length is < 1 or > 200 || username.Any(char.IsControl)) throw new ArgumentException("账号需为 1 至 200 个字符。");
        if (input.Port is < 1024 or > 65535) throw new ArgumentException("本机端口需在 1024 至 65535 之间。");
        var before = await runtime.LoadAsync(home, cancellation);
        if (input.Password.Length == 0)
        { if (!before.Configured || before.Username != username) throw new ArgumentException("请设置 12 至 256 个字符的独立访问密码。"); }
        else if (input.Password.Length is < 12 or > 256 || input.Password.Any(char.IsControl)) throw new ArgumentException("请设置 12 至 256 个字符的独立访问密码。");
        var changes = new List<string>();
        if (!before.Configured || before.PublicUrl != url) changes.Add("HTTPS 入口：" + (before.Configured ? before.PublicUrl : "未设置") + " → " + url);
        if (!before.Configured || before.Port != input.Port) changes.Add("本机监听端口：" + (before.Configured ? before.Port.ToString() : "未设置") + " → " + input.Port);
        if (!before.Configured || before.Username != username) changes.Add("手机登录账号：" + (before.Configured ? before.Username : "未设置") + " → " + username);
        if (input.Password.Length > 0) changes.Add("手机访问密码：" + (before.Configured ? "更换密码，内容已隐藏" : "设置密码，内容已隐藏"));
        return new(before, new(url, username, input.Password, input.Port), changes.AsReadOnly());
    }

    public Task<MobileActionResult<MobileConnectionSettings>> SaveAndStartAsync(MobileSetupReview review, Func<bool> eligible, CancellationToken cancellation = default)
        => SaveAsync(review, eligible, start: true, cancellation);

    public Task<MobileActionResult<MobileConnectionSettings>> SaveAsync(MobileSetupReview review, Func<bool> eligible, CancellationToken cancellation = default)
        => SaveAsync(review, eligible, start: false, cancellation);

    private Task<MobileActionResult<MobileConnectionSettings>> SaveAsync(MobileSetupReview review, Func<bool> eligible, bool start, CancellationToken cancellation)
        => RunAsync(start ? "正在保存并启动手机连接…" : "正在保存手机访问设置…", async (validate, saved) =>
        {
            await RequireCurrentAsync(review.Before, cancellation); validate();
            if (!review.TryConsume()) throw new InvalidOperationException("此手机连接确认已使用，请重新查看设置。");
            var settings = review.Before;
            if (review.HasChanges) { settings = await runtime.SaveAsync(review.Before.Home, review.Input, review.Before.Revision, cancellation); saved(); }
            validate(); await RequireCurrentAsync(settings, cancellation); validate();
            if (!start) return settings;
            await runtime.StartAsync(review.Before.Home, cancellation); validate();
            await RequireCurrentAsync(settings, cancellation);
            if (await runtime.StatusAsync(settings.Home, cancellation) is null) throw new IOException("尚未确认手机服务运行，请刷新状态后重试启动。");
            return settings;
        }, eligible, cancellation, start ? "手机服务已启动。接下来检测 HTTPS 入口。" : "手机访问设置已保存。接下来可以启动手机服务。");

    public async Task<MobileConnectionView> ReadAsync(string home, CancellationToken cancellation = default)
    {
        var settings = await runtime.LoadAsync(home, cancellation); var status = settings.Configured ? await runtime.StatusAsync(home, cancellation) : null;
        await RequireCurrentAsync(settings, cancellation);
        if (status is null) return new(settings, settings.Configured && (!GatewayPortState.IsReleased(settings.Port) || !GatewayPortState.IsReleased(settings.ControlPort)) ? null : false, false, new([], []));
        using var devices = await runtime.ControlAsync(home, "devices", new { action = "list" }, cancellation);
        await RequireCurrentAsync(settings, cancellation);
        var root = devices.RootElement;
        var readiness = root.TryGetProperty("codexReadiness", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
        return new(settings, true, status.Checked, ParseDevices(root),
            readiness is "ready" or "configure" or "checking" ? readiness : "unknown");
    }

    public Task<MobileActionResult<string>> CheckEntryAsync(MobileConnectionSettings expected, Func<bool> eligible, CancellationToken cancellation = default)
        => ControlAsync("check", new { }, expected, eligible, _ => "已确认 HTTPS 入口指向当前电脑。", cancellation);
    public Task<MobileActionResult<MobilePairGrant>> CreatePairAsync(MobileConnectionSettings expected, Func<bool> eligible, CancellationToken cancellation = default)
        => ControlAsync("pair", new { action = "create" }, expected, eligible, root =>
        {
            var id = ReadId(root.GetProperty("id").GetString()); var imageText = root.GetProperty("image").GetString() ?? throw new InvalidDataException("配对码图片缺失。");
            if (imageText.Length > 350000) throw new InvalidDataException("配对码图片过大。");
            var image = Convert.FromBase64String(imageText);
            if (image.Length < 8 || !image.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) throw new InvalidDataException("配对码图片格式无效。");
            var expires = DateTimeOffset.FromUnixTimeSeconds((long)root.GetProperty("expires").GetDouble());
            if (expires <= time.GetUtcNow() || expires > time.GetUtcNow().AddMinutes(10)) throw new InvalidDataException("配对码有效期无效。");
            return new MobilePairGrant(id, image, expires);
        }, cancellation);
    public Task<MobileActionResult<string>> PairStateAsync(MobileConnectionSettings expected, string id, Func<bool> eligible, CancellationToken cancellation = default)
        => ControlAsync("pair", new { action = "status", id = ReadId(id) }, expected, eligible, root => root.GetProperty("state").GetString() is { } state &&
            state is "active" or "used" or "expired" or "revoked" ? state : throw new InvalidDataException("配对状态无效。"), cancellation);
    public Task<MobileActionResult<string>> RevokePairAsync(MobileConnectionSettings expected, string id, Func<bool> eligible, CancellationToken cancellation = default)
        => ControlAsync("pair", new { action = "revoke", id = ReadId(id) }, expected, eligible, _ => "配对码已撤销。", cancellation);
    public Task<MobileActionResult<Uri>> ManagerAsync(MobileConnectionSettings expected, Func<bool> eligible, CancellationToken cancellation = default)
        => ControlAsync("manager", new { }, expected, eligible, root =>
        {
            var text = root.GetProperty("url").GetString();
            if (!Uri.TryCreate(text, UriKind.Absolute, out var url) || url.Scheme != "http" || url.Host != "127.0.0.1" || url.Port != expected.ControlPort ||
                url.AbsolutePath != "/manager/" || url.UserInfo.Length > 0 || url.Query.Length > 0 || url.Fragment.Length is < 16 or > 256)
                throw new InvalidDataException("本机接入管理地址无效。");
            return url;
        }, cancellation);
    public Task<MobileActionResult<MobileDeviceList>> RevokeDeviceAsync(MobileConnectionSettings expected, string id, Func<bool> eligible, CancellationToken cancellation = default)
        => ChangeDevicesAsync(expected, ReadId(id), null, eligible, cancellation);
    public Task<MobileActionResult<MobileDeviceList>> UnblockAsync(MobileConnectionSettings expected, string ip, Func<bool> eligible, CancellationToken cancellation = default)
        => ChangeDevicesAsync(expected, null, ip, eligible, cancellation);

    private Task<MobileActionResult<MobileDeviceList>> ChangeDevicesAsync(MobileConnectionSettings expected, string? id, string? ip, Func<bool> eligible, CancellationToken cancellation)
        => RunAsync("正在更新手机设备授权…", async (validate, markSaved) =>
        {
            await RequireCurrentAsync(expected, cancellation); validate();
            using var current = await runtime.ControlAsync(expected.Home, "devices", new { action = "list" }, cancellation);
            var devices = ParseDevices(current.RootElement); validate();
            if (id is not null && !devices.Devices.Any(device => device.Id == id) || ip is not null && (!IPAddress.TryParse(ip, out _) || !devices.BlockedIps.Contains(ip)))
                throw new InvalidOperationException("设备列表已改变，请刷新后再操作。");
            await RequireCurrentAsync(expected, cancellation); validate();
            using var result = await runtime.ControlAsync(expected.Home, "devices", id is not null ? new { action = "revoke", id } : (object)new { action = "unblock", ip }, cancellation);
            validate(); await RequireCurrentAsync(expected, cancellation); return ParseDevices(result.RootElement);
        }, eligible, cancellation, "设备授权已更新。");

    private Task<MobileActionResult<T>> ControlAsync<T>(string action, object body, MobileConnectionSettings expected, Func<bool> eligible,
        Func<JsonElement, T> parse, CancellationToken cancellation)
        => RunAsync("正在处理手机连接…", async (validate, _) =>
        {
            await RequireCurrentAsync(expected, cancellation); validate();
            using var result = await runtime.ControlAsync(expected.Home, action, body, cancellation);
            validate(); await RequireCurrentAsync(expected, cancellation); return parse(result.RootElement);
        }, eligible, cancellation, "手机连接操作已完成。");
    private async Task RequireCurrentAsync(MobileConnectionSettings expected, CancellationToken cancellation)
    {
        var current = await runtime.LoadAsync(expected.Home, cancellation);
        if (current.Revision != expected.Revision || current.Home != expected.Home) throw new IOException("手机设置已改变，请重新读取并核对后继续。");
    }
    private async Task<MobileActionResult<T>> RunAsync<T>(string message, Func<Action, Action, Task<T>> execute, Func<bool> eligible, CancellationToken cancellation, string success)
    {
        using var operation = operations.TryBeginMobile(message);
        if (operation is null) return new(false, "正在处理其他操作，请完成后再试。");
        var settingsSaved = false; IsBusy = true; Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            void Validate() { cancellation.ThrowIfCancellationRequested(); if (!eligible()) throw new OperationCanceledException("目录或当前操作已改变。"); }
            Validate(); var value = await execute(Validate, () => settingsSaved = true); Validate();
            operation.WaitForUser(success); return new(true, success, value, settingsSaved);
        }
        catch (OperationCanceledException)
        { const string text = "手机连接操作已取消，请刷新核对当前状态。"; operation.WaitForUser(text); return new(false, text, SettingsSaved: settingsSaved); }
        catch (Exception error)
        { var text = (settingsSaved ? "设置已保存，后续操作未完成：" : "") + error.Message; operation.Fail(text); return new(false, text, SettingsSaved: settingsSaved); }
        finally { IsBusy = false; Changed?.Invoke(this, EventArgs.Empty); }
    }
    public static MobileDeviceList ParseDevices(JsonElement root)
    {
        var sessions = root.GetProperty("sessions").EnumerateArray().Take(257).ToArray(); var blocks = root.GetProperty("autoBlocks").EnumerateArray().Take(257).ToArray();
        if (sessions.Length > 256 || blocks.Length > 256) throw new InvalidDataException("设备列表条目过多，请在原界面核对。");
        return new(sessions.Select(row => new MobileDevice(ReadId(row.GetProperty("id").GetString()),
            row.GetProperty("userAgent").GetString()?.Contains("-Android", StringComparison.Ordinal) == true ? "安卓 App" : "浏览器",
            row.GetProperty("ip").GetString() ?? "未知地址", DateTimeOffset.FromUnixTimeSeconds((long)row.GetProperty("lastSeen").GetDouble()))).ToArray(),
            blocks.Select(row => row.GetProperty("ip").GetString() ?? throw new InvalidDataException("设备地址缺失。")).ToArray());
    }
    private static string ReadId(string? id) => id is { Length: > 0 and <= 128 } && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') ? id : throw new ArgumentException("手机授权标识无效。");
}
