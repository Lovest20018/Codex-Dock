using System.Text.Json;
using CodexConfig.Application;
using CodexConfig.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Net;
using System.Net.Sockets;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private async Task RunMobileConnectionSmokeAsync(string output, List<string> checks)
    {
        void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); checks.Add(message); File.AppendAllText(Path.Combine(output, "progress.log"), "PASS " + message + Environment.NewLine); }
        var runtime = (SmokeMobileRuntime)mobileRuntime;
        var originalHome = viewModel.Home; var original = store.Load(originalHome);
        var fixture = Path.Combine(output, "mobile-ui-home"); Directory.CreateDirectory(fixture);
        File.WriteAllText(Path.Combine(fixture, "config.toml"), "model='mobile-ui-fixture'\n");
        await SwitchHomeAsync(fixture); NavigateTo(MobileSettingsNav); await RefreshMobileAsync();
        Check(MobilePanel.Visibility == Visibility.Visible && PageTitle.Text == "手机访问" && MobilePortInput.Text == "13090" && MobilePasswordInput.Password.Length == 0 &&
            !mobileDraft.HasDraft && runtime.Trace.Count == 0 && mobileDraft.Settings?.Configured == false,
            "Windows phone page reads isolated defaults without starting a worker or exposing a password");
        await CaptureAsync(output, "mobile-light.png");
        MobileUrlInput.Text = "https://phone-fixture.invalid"; MobileUsernameInput.Text = "fixture-user"; MobilePasswordInput.Password = "fake-ui-phone-password";
        await Task.Delay(50);
        NavigateTo(ModelsNav); BehaviorModelInput.Text = "independent-model-draft"; await Task.Delay(50);
        NavigateTo(MobileSettingsNav); await RefreshMobileAsync();
        Check(mobileDraft.HasDraft && mobileDraft.Password == "fake-ui-phone-password" && connectionDraft.HasDraft && connectionDraft.Model == "independent-model-draft" && runtime.Trace.Count == 0,
            "Phone and model drafts survive navigation and status refresh independently without being saved");
        MobileUrlInput.Text = "http://phone-fixture.invalid"; await Task.Delay(40);
        Check(!await SaveMobileAsync() && MobileOperationBar.IsOpen && MobileOperationBar.Message.Contains("HTTPS") && runtime.Trace.Count == 0,
            "Invalid phone entry fails before confirmation and never creates a background service");
        MobileUrlInput.Text = "https://phone-fixture.invalid"; await Task.Delay(40);
        smokeDialog = async dialog =>
        {
            var content = ((TextBlock)((ScrollViewer)dialog.Content).Content).Text;
            Check(content.Contains("fixture-user") && content.Contains("13090") && !content.Contains("fake-ui-phone-password") && !MobileUrlInput.IsEnabled,
                "Phone save confirmation names the saved entry and account masks the password and locks competing edits");
            var shown = dialog.ShowAsync(); try { await CaptureAsync(output, "mobile-save-review.png", dialog); } finally { dialog.Hide(); await shown; }
            return ContentDialogResult.None;
        };
        Check(!await SaveMobileAsync() && mobileDraft.Password.Length > 0 && runtime.Trace.Count == 0, "Cancelling phone save preserves every draft field and writes no settings");
        smokeDialog = _ => Task.FromResult(ContentDialogResult.Primary);
        Check(await SaveMobileAsync() && runtime.Trace.Take(2).SequenceEqual(new[] { "save", "start" }) && !mobileDraft.HasDraft &&
            MobilePasswordInput.Password.Length == 0 && connectionDraft.Model == "independent-model-draft" && connectionDraft.HasDraft,
            "Confirmed phone save starts only its own worker clears the password and preserves the unsaved model draft");
        var settings = mobileDraft.Settings!;
        var opens = 0; smokeOpenMobileManager = address => { Check(address.Host == "127.0.0.1" && address.Port == settings.ControlPort, "Enrollment manager uses the exact saved loopback control endpoint"); opens++; };
        await RunMobileControlAsync(async (saved, eligible) =>
        {
            var result = await mobileConnection.ManagerAsync(saved, eligible, lifetime.Token); AcceptMobileResult(result); smokeOpenMobileManager(result.Value!);
        });
        Check(opens == 1 && runtime.Trace.Contains("manager"), "Phone page obtains a local enrollment ticket without opening a real browser during isolation");
        await RunMobileControlAsync(async (saved, eligible) => AcceptMobileResult(await mobileConnection.CheckEntryAsync(saved, eligible, lifetime.Token)));
        Check(mobileView?.EntryChecked == true && MobileRuntimeText.Text.Contains("已检测通过"), "Explicit HTTPS detection updates the saved gateway status independently of its draft");
        NavigateTo(MobileNav);
        runtime.PairImage = Convert.FromBase64String(FixturePairPng); runtime.Devices.Add(new("fixture-device", "安卓 App", "192.0.2.21", DateTimeOffset.UtcNow)); runtime.Blocks.Add("192.0.2.22");
        await GenerateMobilePairAsync();
        Check(mobilePair is not null && MobilePairImage.Source is not null && MobilePairImage.Visibility == Visibility.Visible && RevokeMobilePairButton.IsEnabled,
            "Windows renders the locally returned PNG pairing image and keeps its secret URL out of page text");
        PageScroll.ChangeView(null, double.MaxValue, null, true); await CaptureAsync(output, "mobile-pair-devices.png");
        await RevokeMobilePairAsync(); Check(mobilePair is null && MobilePairImage.Visibility == Visibility.Collapsed && runtime.PairState == "revoked", "Revoking the displayed pairing grant removes it from the Windows page");
        await GenerateMobilePairAsync(); runtime.PairState = "used"; gatewayTicks = 3; await MobilePairTickAsync();
        Check(mobilePair is null && MobilePairHint.Text.Contains("配对成功") && MobileDevicesList.Children.Count == 2, "A consumed pair clears the image and refreshes the device and login-block list");
        NavigateTo(MobileDevicesNav);
        smokeDialog = _ => Task.FromResult(ContentDialogResult.None); var actions = runtime.Trace.Count;
        await ChangeMobileDeviceAsync("fixture-device", null); Check(runtime.Trace.Count == actions && runtime.Devices.Count == 1, "Cancelling device revocation leaves the login authorization unchanged");
        smokeDialog = _ => Task.FromResult(ContentDialogResult.Primary);
        await ChangeMobileDeviceAsync("fixture-device", null); await ChangeMobileDeviceAsync(null, "192.0.2.22");
        Check(runtime.Devices.Count == 0 && runtime.Blocks.Count == 0 && MobileDevicesList.Children.OfType<TextBlock>().Any(text => text.Text.Contains("尚无")), "Confirmed revoke and unblock actions use the latest saved device membership");
        NavigateTo(MobileSettingsNav);
        MobileUrlInput.Text = "https://unsaved-phone-fixture.invalid"; await Task.Delay(40);
        Check(!CreateMobilePairButton.IsEnabled && !CheckMobileEntryButton.IsEnabled && mobilePair is null, "A phone draft hides its stale pair and blocks commands against the prior saved entry");
        DiscardConnectionClicked(DiscardConnectionButton, new());
        smokeDialog = _ => Task.FromResult(ContentDialogResult.None);
        Check(!await SwitchHomeAsync(Path.Combine(output, "cancelled-mobile-home")) && mobileDraft.HasDraft && viewModel.Home == fixture,
            "Cancelling a workspace change keeps the phone draft and existing saved gateway identity");
        Check(!await StopBeforeCloseAsync() && !closing && mobileDraft.HasDraft, "Explicit exit cannot bypass an unsaved phone-settings choice");
        DiscardMobileClicked(DiscardMobileButton, new()); MobilePortInput.Text = "13091"; await Task.Delay(40);
        runtime.StartFailure = true; smokeDialog = _ => Task.FromResult(ContentDialogResult.Primary);
        Check(!await SaveMobileAsync() && !mobileDraft.HasDraft && mobileDraft.Password.Length == 0 && mobileMessage.Contains("设置已保存") && mobileDraft.Settings!.Port == 13091,
            "A failed start reports that phone settings were saved and clears the password without claiming a running worker");
        runtime.StartFailure = false; runtime.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var starting = SaveMobileAsync();
        Check(ManualApplicationBusy && !ChooseHomeButton.IsEnabled && !ApplyButton.IsEnabled && !MobileUrlInput.IsEnabled,
            "Phone startup holds the shared operation lease and prevents duplicate save home or desktop operations");
        runtime.Gate.TrySetResult(); await starting; runtime.Gate = null;
        var savedBeforeExternal = mobileDraft.Settings!;
        runtime.Settings[fixture] = savedBeforeExternal with { Revision = "external-change" };
        MobileUsernameInput.Text = "new-account"; MobilePasswordInput.Password = "fake-new-ui-password"; await Task.Delay(40);
        var savedCount = runtime.Trace.Count(item => item == "save");
        Check(!await SaveMobileAsync() && mobileDraft.HasDraft && mobileMessage.Contains("手机设置已") && runtime.Trace.Count(item => item == "save") == savedCount && runtime.Settings[fixture].Revision == "external-change",
            "External phone settings changes block a reviewed overwrite and preserve the local draft");
        DiscardMobileClicked(DiscardMobileButton, new()); await RefreshMobileAsync(reloadDraft: true);
        Root.RequestedTheme = ElementTheme.Dark; AppWindow.Resize(new(900, 760)); await Task.Delay(120); Root.UpdateLayout(); PageScroll.ChangeView(null, 0, null, true);
        await CaptureAsync(output, "mobile-compact-dark.png");
        Check(MobileUrlInput.ActualWidth > 250 && MobileUrlInput.TransformToVisual(Root).TransformPoint(new()).X + MobileUrlInput.ActualWidth <= Root.ActualWidth,
            "Phone setup fields remain inside the compact dark Windows layout");
        Root.RequestedTheme = ElementTheme.Light;
        await SwitchHomeAsync(originalHome); await RefreshMobileAsync();
        Check(mobileDraft.Password.Length == 0 && !mobileDraft.HasDraft && mobileDraft.Settings?.Configured == false && original.SameContents(store.Load(originalHome)),
            "Changing the workspace clears private phone fields and pairing state without changing Codex files");
        smokeDialog = null; smokeOpenMobileManager = null; NavigateTo(WorkspaceNav);
        await RunNativeMobileConnectionSmokeAsync(output, Check);
    }

    private async Task RunNativeMobileConnectionSmokeAsync(string output, Action<bool, string> check)
    {
        var home = Path.Combine(output, "native-mobile-setup"); Directory.CreateDirectory(home); File.WriteAllText(Path.Combine(home, "config.toml"), "model='unchanged-mobile-native'\n");
        var root = Path.Combine(output, "native-mobile-setup-state"); var manager = new MobileGatewayManager(home, root);
        var service = new MobileConnectionService(new NativeMobileConnectionRuntime(root), new(new WorkspaceSession(home)));
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var before = store.Load(home);
        try
        {
            var review = await service.PrepareAsync(home, new("https://mobile-native-fixture.invalid", "fixture", "fake-mobile-native-password", port));
            check(!File.Exists(manager.SettingsPath) && !review.Description.Contains("fake-mobile-native-password"), "Native phone review performs no write and hides the independent password");
            var result = await service.SaveAndStartAsync(review, () => true);
            check(result.Succeeded && result.SettingsSaved && await manager.StatusAsync() is not null, "Delivered Windows preview saves encrypted phone settings and starts its bundled worker through the shared use case");
            var view = await service.ReadAsync(home); check(view.Running == true && view.Devices.Devices.Count == 0 && before.SameContents(store.Load(home)), "Native phone status and device list are authenticated and leave Codex routing untouched");
            var link = await service.ManagerAsync(result.Value!, () => true); check(link.Succeeded && link.Value?.Port == manager.Load()!.ControlPort, "Native enrollment returns the exact local control port and a bounded one-use fragment");
        }
        finally { await manager.StopAsync(); }
        check(await manager.StatusAsync() is null && before.SameContents(store.Load(home)), "Native phone setup verification releases its test worker and preserves all model files");
    }

    private const string FixturePairPng = "iVBORw0KGgoAAAANSUhEUgAAAQ4AAAEOCAAAAABd2qZ5AAADLklEQVR42u3dO3YDIQwAQN3/0qRNkzx9gF0nQ+XCXsM0eiChjWV8G4EABw4cOHDgwIEDBw4cOHDgwIEDBw4cBg4cOMYckR7fv//758x//fSE/H/l54wDBw4cOO5zZJbx01R+f1p+SVW4/Jxx4MCBA8dTHPngml92PsBnmHpzxoEDBw4cb+bI/PbcgnHgwIEDx9/jmB8d9jZgOHDgwIHjEzmq38kEy0y47aWdXnFWigMHDhw4rhQ03Pn8MfUdOHDgwPHPOHojiiOfcMonul5Uk44DBw4cOBLHcNXQ20s4VZ+TOTTcXISNAwcOHDgGgTZfZJAJnNXC7l4wriaccODAgQPHHY5dRQz50rp84UI+NbUh7YQDBw4cOLZm8KtLimJzgd4BX2+bhwMHDhw4nuUYPTq91OrFnvnFVBw4cODA8QaO3jZvkgSaFDT0QjgOHDhw4DjNEcX2AT2aNW6dU50hDhw4cOB4imPvRHc10KmGfBw4cODA8QaOtalYbd7cOb8li3GTUBw4cODAcZOjmnA6sRnrJaIKJeA4cODAgeMwRybcrq2NmGNTc89otVrDgQMHDhznOKJ4dWfXxdHJTCZXjHDgwIEDxx2OXaEuzxSt60O9MnEcOHDgwHGTo9qwbB78qgeRkb4OtPmsFAcOHDhwbOXIt+zsYa0DLUGP9P7BgQMHDhwH0k6r1Xpg16XQKh8OHDhw4Hgzx4mX00zSWqtYqI0DBw4cOO5zVBfWaw1QLaeLVpvpDYeDOHDgwIFjK8febdsaNACdFMzF3k7YOHDgwIFj/IrNctDqHc8NmhHgwIEDB45P4aimc+LAS2ii+EKaePZ1NThw4MCBYzz2buom14pGG04cOHDgwHGMo1cAPUkCVZt4zplw4MCBA8d9jnn7zsm2alL8jQMHDhw43szRC67nNoTzsm8cOHDgwPEpHNWk0aTEIbPUONegEwcOHDhwHObIT663zVvFy6IPb+Fw4MCBA8f4cHDSAHoNWgxUwzwOHDhw4HiWIw40x4xiy+YoNoCOm2/vwYEDBw4cx4qw/8bAgQMHDhw4cODAgQMHDhw4cODAgQMHDgMHDhzp8QWEis1NiIrEiAAAAABJRU5ErkJggg==";

    private sealed class SmokeMobileRuntime : IMobileConnectionRuntime
    {
        internal readonly Dictionary<string, MobileConnectionSettings> Settings = new(StringComparer.OrdinalIgnoreCase);
        internal readonly List<string> Trace = [];
        internal bool Running, Checked, StartFailure;
        internal TaskCompletionSource? Gate;
        internal TaskCompletionSource? StatusGate;
        internal bool StatusFailure;
        internal readonly List<MobileDevice> Devices = [];
        internal readonly List<string> Blocks = [];
        internal string PairState = "active", ManagerAddress = "";
        internal byte[] PairImage = [];
        internal int PairGeneration;
        public Task<MobileConnectionSettings> LoadAsync(string home, CancellationToken cancellation)
            => Task.FromResult(Settings.TryGetValue(home, out var saved) ? saved : new(home, false, "", "codex", 13090, 0, "missing"));
        public Task<MobileConnectionSettings> SaveAsync(string home, MobileSetupInput input, string expectedRevision, CancellationToken cancellation)
        {
            var previous = Settings.GetValueOrDefault(home);
            if ((previous?.Revision ?? "missing") != expectedRevision) throw new IOException("fixture settings changed");
            Trace.Add("save");
            var saved = new MobileConnectionSettings(home, true, input.PublicUrl, input.Username, input.Port, 32194, Guid.NewGuid().ToString("N"));
            if (input.Password.Length > 0 || previous?.PublicUrl != input.PublicUrl || previous?.Username != input.Username) Devices.Clear();
            Settings[home] = saved; Running = false; return Task.FromResult(saved);
        }
        public async Task StartAsync(string home, CancellationToken cancellation)
        {
            Trace.Add("start"); if (Gate is { } gate) await gate.Task.WaitAsync(cancellation);
            if (StartFailure) throw new IOException("模拟手机后台启动失败。"); Running = true;
        }
        public async Task<MobileGatewayStatus?> StatusAsync(string home, CancellationToken cancellation)
        {
            var saved = await LoadAsync(home, cancellation);
            var status = Running && saved.Configured ? new MobileGatewayStatus("codex-dock-mobile", 4, "fixture", saved.PublicUrl, saved.Port, Checked) : null;
            if (StatusGate is { } gate) await gate.Task.WaitAsync(cancellation);
            if (StatusFailure) throw new IOException("模拟手机状态读取失败。");
            return status;
        }
        public Task<JsonDocument> ControlAsync(string home, string action, object body, CancellationToken cancellation)
        {
            Trace.Add(action); var value = JsonSerializer.SerializeToElement(body);
            if (action == "check") { Checked = true; return Task.FromResult(JsonDocument.Parse("{\"ok\":true}")); }
            if (action == "manager") return Task.FromResult(JsonSerializer.SerializeToDocument(new { url = ManagerAddress.Length > 0 ? ManagerAddress : "http://127.0.0.1:32194/manager/#fixture-ticket-only-abcdefghijklmnop" }));
            if (action == "pair")
            {
                var command = value.GetProperty("action").GetString();
                if (command == "create") { PairState = "active"; PairGeneration++; return Task.FromResult(JsonSerializer.SerializeToDocument(new { id = "fixture-pair-" + PairGeneration,
                    image = Convert.ToBase64String(PairImage), expires = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds() })); }
                if (command == "revoke") PairState = "revoked";
                return Task.FromResult(JsonSerializer.SerializeToDocument(new { state = PairState }));
            }
            if (action == "devices")
            {
                var command = value.GetProperty("action").GetString();
                if (command == "revoke") Devices.RemoveAll(device => device.Id == value.GetProperty("id").GetString());
                if (command == "unblock") Blocks.Remove(value.GetProperty("ip").GetString()!);
                return Task.FromResult(JsonSerializer.SerializeToDocument(new { sessions = Devices.Select(device => new { id = device.Id, userAgent = device.Label == "安卓 App" ? "Fixture-Android" : "FixtureBrowser",
                    ip = device.Ip, lastSeen = device.LastSeen.ToUnixTimeSeconds() }), autoBlocks = Blocks.Select(ip => new { ip }) }));
            }
            throw new InvalidOperationException("隔离手机运行时不支持此操作。");
        }
    }
}
