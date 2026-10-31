using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CodexConfig.Core;

namespace CodexConfig.App;

public partial class MainWindow
{
    private readonly TextBox mobileUrl = new() { MinWidth = 200, MaxLength = 2048 };
    private readonly TextBox mobileUsername = new() { Text = "codex", MaxLength = 200 };
    private readonly PasswordBox mobilePassword = new() { MaxLength = 256 };
    private readonly TextBox mobilePort = new() { Text = MobileGatewayManager.DefaultPort.ToString(), MaxLength = 5 };
    private readonly TextBlock mobileStatus = OverviewText("尚未设置手机连接。", "Muted");
    private readonly TextBlock mobileEndpoint = OverviewText($"HTTP 端口：{MobileGatewayManager.DefaultPort} · 接受端口映射和反向代理连接", "Muted", 12);
    private readonly TextBlock mobilePasswordHint = OverviewText("设置 12 至 256 个字符的独立密码。", "Muted", 12);
    private readonly StackPanel mobileDevices = new();
    private readonly Image mobileQr = new() { Width = 240, Height = 240, Stretch = Stretch.Uniform };
    private readonly TextBlock mobileQrHint = OverviewText("先保存并启动，再检测 HTTPS 入口和生成配对码。", "Muted", 12);
    private readonly DispatcherTimer mobileTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly List<Button> mobileActions = [];
    private string? mobileLoadedHome, mobilePairId;
    private DateTimeOffset mobilePairExpires;
    private int mobileTicks;
    private bool mobileBusy, mobileRefreshing;

    private MobileGatewayManager MobileManager() => new(home, smokeOutput is null ? null : Path.Combine(smokeOutput, "mobile-state"));

    private void InitializeMobilePage()
    {
        static void Field(Panel panel, string label, Control input)
        {
            var caption = OverviewText(label, size: 13); caption.Margin = new(0, 15, 0, 7); panel.Children.Add(caption);
            panel.Children.Add(input); System.Windows.Automation.AutomationProperties.SetName(input, label);
        }
        Button Action(Panel panel, string label, Func<Task> action, bool primary = false)
        {
            var button = new Button { Content = label, Margin = new(0, 0, 8, 8) };
            if (primary) button.SetResourceReference(StyleProperty, "PrimaryButton");
            button.Click += async (_, _) => await MobileActionAsync(action); panel.Children.Add(button); mobileActions.Add(button); return button;
        }
        var setup = new StackPanel();
        setup.Children.Add(OverviewText("连接入口", size: 18));
        var help = OverviewText("仅继续当前配置目录中的本机 Codex App 聊天。保持电脑和 Codex App 运行，手机沿用原聊天的模型和权限。", "Muted", 13);
        help.Margin = new(0, 9, 0, 0); setup.Children.Add(help);
        Field(setup, "HTTPS 域名", mobileUrl);
        setup.Children.Add(OverviewText("例如 https://codex.example.com。将已有 HTTPS 入口转发到本机端口，保留完整 Host（含外部端口）。", "Muted", 12));
        Field(setup, "本机监听端口", mobilePort); mobilePort.MaxWidth = 160; mobilePort.HorizontalAlignment = HorizontalAlignment.Left;
        setup.Children.Add(OverviewText("支持本机代理、群晖和路由端口映射，无需填写代理来源 IP。", "Muted", 12));
        Field(setup, "手机登录账号", mobileUsername);
        Field(setup, "手机访问密码", mobilePassword); mobilePasswordHint.Margin = new(0, 7, 0, 0); setup.Children.Add(mobilePasswordHint);
        var save = new WrapPanel { Margin = new(0, 20, 0, 0) };
        Action(save, "保存并启动", SaveAndStartMobileAsync, true); setup.Children.Add(save);
        setup.Children.Add(OverviewText("此页单独保存并立即生效。更改账号、密码或域名后，已登录设备需要重新登录。", "Muted", 12));
        MobileBody.Children.Add(OverviewSurface(setup, new(0, 0, 0, 16)));

        var runtime = new StackPanel(); runtime.Children.Add(OverviewText("后台服务", size: 18));
        mobileStatus.Margin = new(0, 10, 0, 9); runtime.Children.Add(mobileStatus); runtime.Children.Add(mobileEndpoint);
        var controls = new WrapPanel { Margin = new(0, 16, 0, 0) };
        Action(controls, "启动", async () => { await MobileManager().StartAsync(lifetime.Token); await RefreshMobilePageAsync(); });
        Action(controls, "停止", StopMobileGatewayAsync);
        Action(controls, "刷新状态", () => RefreshMobilePageAsync());
        Action(controls, "检测 HTTPS 入口", async () =>
        {
            using var result = await MobileManager().ControlAsync("check", new { }, lifetime.Token);
            mobileStatus.Text = "HTTPS 入口已确认指向这台电脑，可生成配对码。";
        });
        runtime.Children.Add(controls);
        runtime.Children.Add(OverviewText("关闭或退出 Dock 后服务继续运行；点击“停止”才停用手机访问。重启 Windows 后，打开此页并点击“启动”。", "Muted", 12));
        MobileBody.Children.Add(OverviewSurface(runtime, new(0, 0, 0, 16)));

        var pairing = new StackPanel(); pairing.Children.Add(OverviewText("安卓扫码登录", size: 18));
        pairing.Children.Add(OverviewText("使用上游 Codex Mobile Bridge 安卓 App 添加电脑并扫码，也可手动填写上方域名后用账号密码登录。", "Muted", 13));
        mobileQr.HorizontalAlignment = HorizontalAlignment.Left; mobileQr.Margin = new(0, 16, 0, 8); mobileQr.Visibility = Visibility.Collapsed;
        pairing.Children.Add(mobileQr); mobileQrHint.Margin = new(0, 12, 0, 12); pairing.Children.Add(mobileQrHint);
        var qrActions = new WrapPanel();
        Action(qrActions, "生成 / 刷新配对码", CreateMobilePairAsync);
        Action(qrActions, "撤销配对码", async () =>
        {
            if (mobilePairId is { } id) { using var result = await MobileManager().ControlAsync("pair", new { action = "revoke", id }, lifetime.Token); }
            ClearMobileQr(); mobileQrHint.Text = "配对码已撤销。";
        }); pairing.Children.Add(qrActions);
        pairing.Children.Add(OverviewText("二维码 5 分钟有效、仅可使用一次；刷新使旧码失效。仅在本机生成，未上传至二维码服务。", "Muted", 12));
        MobileBody.Children.Add(OverviewSurface(pairing, new(0, 0, 0, 16)));

        var devices = new StackPanel(); devices.Children.Add(OverviewText("登录设备", size: 18));
        devices.Children.Add(OverviewText("安卓授权跨服务重启保留。撤销设备后，其保存的登录状态将无法恢复。", "Muted", 13));
        mobileDevices.Margin = new(0, 10, 0, 0); devices.Children.Add(mobileDevices);
        MobileBody.Children.Add(OverviewSurface(devices, new(0)));
        mobileTimer.Tick += async (_, _) => await MobileTickAsync(); mobileTimer.Start();
        Closed += (_, _) => { mobileTimer.Stop(); ClearMobileQr(); };
    }

    private async Task MobileActionAsync(Func<Task> action)
    {
        if (mobileBusy) return;
        try { await RunMobileActionAsync(action); }
        catch (OperationCanceledException) { mobileStatus.Text = "操作已取消，请刷新状态。"; }
        catch (Exception error) { mobileStatus.Text = error.Message; }
    }

    private async Task RunMobileActionAsync(Func<Task> action)
    {
        if (mobileBusy || closing) throw new InvalidOperationException("手机网关正在处理操作，请稍后重试。");
        mobileBusy = true; foreach (var button in mobileActions) button.IsEnabled = false;
        mobileUrl.IsEnabled = mobileUsername.IsEnabled = mobilePort.IsEnabled = mobilePassword.IsEnabled = false;
        mobileStatus.Text = "正在处理…";
        try { await action(); }
        catch (Exception error) { mobileStatus.Text = error.Message; throw; }
        finally
        {
            mobileBusy = false; foreach (var button in mobileActions) button.IsEnabled = true;
            mobileUrl.IsEnabled = mobileUsername.IsEnabled = mobilePort.IsEnabled = mobilePassword.IsEnabled = true;
        }
    }

    private async Task StopMobileGatewayAsync()
    {
        var expectedHome = home;
        await MobileManager().StopAsync(lifetime.Token);
        if (expectedHome == home && !closing) { ClearMobileQr(); await RefreshMobilePageAsync(); }
    }

    private async Task SaveAndStartMobileAsync()
    {
        if (!int.TryParse(mobilePort.Text, out var port)) throw new ArgumentException("请输入有效的本机端口。");
        var manager = MobileManager();
        await manager.SaveAsync(mobileUrl.Text, mobileUsername.Text, mobilePassword.Password, port, lifetime.Token);
        mobilePassword.Clear(); ClearMobileQr();
        mobilePasswordHint.Text = "密码已设置；留空沿用。填写新密码将撤销已有设备授权。";
        await manager.StartAsync(lifetime.Token);
        await RefreshMobilePageAsync();
    }

    private async Task RefreshMobilePageAsync()
    {
        if (mobileRefreshing || closing) return;
        mobileRefreshing = true; var manager = MobileManager(); var requestedHome = home;
        try
        {
            var config = manager.Load();
            if (mobileLoadedHome != home)
            {
                ClearMobileQr(); mobilePassword.Clear(); mobileLoadedHome = home;
                mobileUrl.Text = config?.PublicUrl ?? ""; mobileUsername.Text = config?.Auth.Username ?? "codex";
                mobilePort.Text = (config?.Port ?? MobileGatewayManager.DefaultPort).ToString();
                mobilePasswordHint.Text = config is null ? "设置 12 至 256 个字符的独立密码。" : "密码已设置；留空沿用。填写新密码将撤销已有设备授权。";
            }
            var status = await manager.StatusAsync(lifetime.Token);
            if (requestedHome != home || closing) return;
            mobileEndpoint.Text = $"HTTP 端口：{config?.Port ?? MobileGatewayManager.DefaultPort} · 代理目标为电脑可达的 IP 和此端口 · 管理端口仅本机";
            mobileStatus.Text = status is null ? config is null ? "尚未设置手机连接。" : "服务已停止。点击启动后，检测 HTTPS 入口。" :
                status.Checked ? "服务运行中 · HTTPS 入口已检测通过" : "服务运行中 · 尚未检测 HTTPS 入口";
            mobileDevices.Children.Clear();
            if (status is null) { mobileDevices.Children.Add(OverviewText("启动服务后可查看和撤销设备。", "Muted", 13)); return; }
            using var result = await manager.ControlAsync("devices", new { action = "list" }, lifetime.Token);
            if (requestedHome != home || closing) return;
            RenderMobileDevices(result.RootElement);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (requestedHome == home) mobileStatus.Text = error.Message; }
        finally
        {
            mobileRefreshing = false;
            if (requestedHome != home && currentPage == "mobile" && !closing) _ = RefreshMobilePageAsync();
        }
    }

    private void RenderMobileDevices(JsonElement value)
    {
        mobileDevices.Children.Clear();
        foreach (var row in value.GetProperty("sessions").EnumerateArray())
        {
            var id = row.GetProperty("id").GetString()!;
            var agent = row.GetProperty("userAgent").GetString() ?? "";
            var lastSeen = DateTimeOffset.FromUnixTimeSeconds((long)row.GetProperty("lastSeen").GetDouble()).ToLocalTime().ToString("MM-dd HH:mm");
            var panel = new DockPanel { Margin = new(0, 6, 0, 8) };
            var revoke = new Button { Content = "撤销设备", Margin = new(12, 0, 0, 0) }; DockPanel.SetDock(revoke, Dock.Right); panel.Children.Add(revoke);
            panel.Children.Add(OverviewText($"{(agent.Contains("-Android") ? "安卓 App" : "浏览器")} · {row.GetProperty("ip").GetString()} · 最近 {lastSeen}", "Muted", 13));
            revoke.Click += async (_, _) => await MobileActionAsync(async () => { using var result = await MobileManager().ControlAsync("devices", new { action = "revoke", id }, lifetime.Token); await RefreshMobilePageAsync(); });
            mobileDevices.Children.Add(panel);
        }
        foreach (var row in value.GetProperty("autoBlocks").EnumerateArray())
        {
            var ip = row.GetProperty("ip").GetString()!;
            var panel = new DockPanel { Margin = new(0, 6, 0, 8) };
            var unblock = new Button { Content = "解除封禁", Margin = new(12, 0, 0, 0) }; DockPanel.SetDock(unblock, Dock.Right); panel.Children.Add(unblock);
            panel.Children.Add(OverviewText($"{ip} · 多次登录失败，已封禁", "Muted", 13));
            unblock.Click += async (_, _) => await MobileActionAsync(async () => { using var result = await MobileManager().ControlAsync("devices", new { action = "unblock", ip }, lifetime.Token); await RefreshMobilePageAsync(); });
            mobileDevices.Children.Add(panel);
        }
        if (mobileDevices.Children.Count == 0) mobileDevices.Children.Add(OverviewText("尚无登录设备。", "Muted", 13));
    }

    private async Task CreateMobilePairAsync()
    {
        using var result = await MobileManager().ControlAsync("pair", new { action = "create" }, lifetime.Token);
        var grant = result.RootElement; var bytes = Convert.FromBase64String(grant.GetProperty("image").GetString()!);
        using var stream = new MemoryStream(bytes); var bitmap = new BitmapImage();
        bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
        mobilePairId = grant.GetProperty("id").GetString(); mobilePairExpires = DateTimeOffset.FromUnixTimeSeconds((long)grant.GetProperty("expires").GetDouble());
        mobileQr.Source = bitmap; mobileQr.Visibility = Visibility.Visible; mobileQrHint.Text = "使用安卓 App 扫描二维码。有效期 5 分钟。";
        mobileStatus.Text = "HTTPS 入口已确认，配对码已生成。";
    }

    private void ClearMobileQr()
    {
        mobilePairId = null; mobileQr.Source = null; mobileQr.Visibility = Visibility.Collapsed;
        mobileQrHint.Text = "先启动服务，再生成配对码。";
    }

    private async Task MobileTickAsync()
    {
        if (currentPage != "mobile" || !IsVisible || mobileBusy || mobilePairId is null) return;
        if (mobilePairExpires <= DateTimeOffset.UtcNow) { ClearMobileQr(); mobileQrHint.Text = "配对码已过期，请重新生成。"; return; }
        if (++mobileTicks % 5 != 0) return;
        var id = mobilePairId; var requestedHome = home;
        try
        {
            using var result = await MobileManager().ControlAsync("pair", new { action = "status", id }, lifetime.Token);
            if (id != mobilePairId || requestedHome != home) return;
            var state = result.RootElement.GetProperty("state").GetString();
            if (state != "active") { ClearMobileQr(); mobileQrHint.Text = state == "used" ? "配对成功。可在下方查看和撤销设备。" : "配对码已失效，请重新生成。"; await RefreshMobilePageAsync(); }
        }
        catch { ClearMobileQr(); mobileQrHint.Text = "无法确认配对码状态，请刷新服务状态。"; }
    }
}
