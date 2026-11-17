using System.ComponentModel;
using System.Diagnostics;
using CodexConfig.Application;
using CodexConfig.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private bool mobileAction, mobileLoading, mobileRefreshing;
    private Task mobileReadTask = Task.CompletedTask;
    private string? mobileHome;
    private string mobileMessage = "";
    private bool mobileMessageError;
    private MobileConnectionView? mobileView;
    private MobilePairGrant? mobilePair;
    private int mobileViewRevision, renderedMobileRevision = -1;
    private string mobilePairMessage = "先保存并启动，再检测 HTTPS 入口。";
    private Action<Uri>? smokeOpenMobileManager;
    private void OpenMobileClicked(object sender, RoutedEventArgs args) => NavigateTo(mobileDraft.Settings?.Configured == true ? MobileNav : MobileSettingsNav);
    private void OpenPairingPageClicked(object sender, RoutedEventArgs args)
    { if (!ApplicationBusy && !dialogOpen && !closing) NavigateTo(MobileNav, revealStart: true); }

    private void ResetMobileHome()
    {
        if (mobileHome == viewModel.Home) return;
        mobileHome = viewModel.Home; mobileLoading = true;
        try { mobileDraft.Load(null); }
        finally { mobileLoading = false; }
        mobileView = null; mobileMessage = ""; mobileMessageError = false; ClearMobilePair(); mobileViewRevision++;
    }
    private void MobileDraftChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(MobileSetupDraftViewModel.Revision) || mobileLoading || closing) return;
        ClearMobilePair(); mobileMessage = ""; RefreshView();
    }
    private Task RefreshMobileAsync(bool reloadDraft = false)
    {
        if (closing || mobileRefreshing || mobileAction) return Task.CompletedTask;
        return mobileReadTask = ReadMobileAsync(reloadDraft);
    }
    private async Task RefreshMobileAfterGatewayAsync(string home)
    {
        // A status read started before the command can still describe the old worker.
        await mobileReadTask;
        if (closing || home != viewModel.Home) return;
        mobileView = null; mobileViewRevision++; RefreshView();
        await RefreshMobileAsync();
    }
    private async Task ReadMobileAsync(bool reloadDraft)
    {
        var home = viewModel.Home; mobileRefreshing = true;
        try
        {
            var current = await mobileConnection.ReadAsync(home, lifetime.Token);
            if (closing || home != viewModel.Home) return;
            if (reloadDraft || mobileDraft.Settings is null)
            {
                mobileLoading = true; try { mobileDraft.Load(current.Settings); } finally { mobileLoading = false; }
            }
            else if (mobileDraft.Settings.Revision != current.Settings.Revision)
            {
                ClearMobilePair();
                if (mobileDraft.HasDraft) { mobileMessage = "手机设置已被其他窗口修改，当前草稿已保留。请重新读取并核对后保存。"; mobileMessageError = true; }
                else { mobileLoading = true; try { mobileDraft.Load(current.Settings); } finally { mobileLoading = false; } }
            }
            mobileView = current; mobileViewRevision++;
            if (current.Running != true) ClearMobilePair();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!closing && home == viewModel.Home) { mobileMessage = error.Message; mobileMessageError = true; } }
        finally
        {
            mobileRefreshing = false;
            if (!closing && home == viewModel.Home) RefreshView();
            else if (!closing && IsPhonePage) await RefreshMobileAsync();
        }
    }

    private void RefreshMobileControls()
    {
        if (MobilePanel is null) return;
        var editable = mobileDraft.Settings is not null && !ManualApplicationBusy && !dialogOpen && !closing;
        foreach (var control in new Control[] { MobileUrlInput, MobilePortInput, MobileUsernameInput, MobilePasswordInput }) control.IsEnabled = editable;
        if (MobileUrlInput.Text != mobileDraft.PublicUrl) MobileUrlInput.Text = mobileDraft.PublicUrl;
        if (MobilePortInput.Text != mobileDraft.Port) MobilePortInput.Text = mobileDraft.Port;
        if (MobileUsernameInput.Text != mobileDraft.Username) MobileUsernameInput.Text = mobileDraft.Username;
        if (MobilePasswordInput.Password != mobileDraft.Password) MobilePasswordInput.Password = mobileDraft.Password;
        MobilePasswordHint.Text = mobileDraft.Settings?.Configured == true ? "密码已设置。留空沿用；更换密码会撤销旧设备授权。" : "设置 12 至 256 个字符的独立手机访问密码。";
        MobileDraftHint.Text = mobileDraft.HasDraft ? "有未保存的手机设置。页面切换会保留；保存后单独生效，不写 Codex 模型配置。" : "手机设置单独保存，与连接配置草稿相互独立。";
        MobileFooter.Visibility = currentPage == MobileSettingsNav ? Visibility.Visible : Visibility.Collapsed;
        SaveMobileButton.IsEnabled = editable && (mobileDraft.HasDraft || mobileDraft.Settings?.Configured == true);
        DiscardMobileButton.IsEnabled = editable && mobileDraft.HasDraft;
        RefreshMobileButton.IsEnabled = !ManualApplicationBusy && !dialogOpen && !closing && !mobileRefreshing;
        var usable = editable && !mobileDraft.HasDraft && mobileView?.Running == true && mobileDraft.Settings?.Revision == mobileView.Settings.Revision;
        CheckMobileEntryButton.IsEnabled = OpenMobileManagerButton.IsEnabled = CreateMobilePairButton.IsEnabled = usable;
        RevokeMobilePairButton.IsEnabled = usable && mobilePair is not null;
        ContinueMobilePairButton.Visibility = usable ? Visibility.Visible : Visibility.Collapsed;
        ContinueMobilePairButton.IsEnabled = usable;
        MobileNextStepText.Text = mobileDraft.HasDraft ? "先确认服务设置中的修改，再检测或配对。" :
            mobileDraft.Settings?.Configured != true ? "第一步：设置手机入口、账号和密码，并保存启动。" :
            mobileView?.Running != true ? "先在服务设置中启动手机服务；状态未确认时刷新核对。" :
            mobileView.EntryChecked ? "入口已检测通过，可以生成配对码；已登录设备在“授权设备”中管理。" :
            "下一步：检测 HTTPS 入口，确认通过后生成配对码。";
        MobileSetupLinkButton.Content = mobileDraft.HasDraft ? "查看未保存的服务设置" : mobileDraft.Settings?.Configured != true ? "设置手机服务" : "服务设置与启停";
        MobileRuntimeText.Text = mobileView is null ? "尚未读取手机设置。" : mobileView.Running is true ? mobileView.EntryChecked ? "网关运行中 · HTTPS 入口已检测通过" : "网关运行中 · 尚未检测 HTTPS 入口" :
            mobileView.Running is null ? "后台状态未确认，请在服务设置刷新。" : mobileView.Settings.Configured ? "手机服务已停止，可在服务设置启动。" : "尚未保存手机连接。";
        MobileEndpointText.Text = mobileView?.Settings.Configured == true ? "已保存入口：" + mobileView.Settings.PublicUrl + "\n本机监听端口：" + mobileView.Settings.Port : "";
        MobileOperationBar.IsOpen = mobileMessage.Length > 0; MobileOperationBar.Message = mobileMessage;
        MobileOperationBar.Severity = mobileMessageError ? InfoBarSeverity.Error : InfoBarSeverity.Success;
        MobilePairHint.Text = mobilePairMessage;
        if (renderedMobileRevision != mobileViewRevision) { RenderMobileDeviceRows(); renderedMobileRevision = mobileViewRevision; }
        foreach (var button in MobileDevicesList.Children.OfType<StackPanel>().SelectMany(panel => panel.Children).OfType<Button>()) button.IsEnabled = usable;
    }
    private void MobileTextChanged(TextBox sender, TextBoxTextChangingEventArgs args)
    {
        if (!initialized || updatingControls || mobileLoading || ManualApplicationBusy || dialogOpen || closing) return;
        if (ReferenceEquals(sender, MobileUrlInput)) mobileDraft.PublicUrl = MobileUrlInput.Text;
        if (ReferenceEquals(sender, MobilePortInput)) mobileDraft.Port = MobilePortInput.Text;
        if (ReferenceEquals(sender, MobileUsernameInput)) mobileDraft.Username = MobileUsernameInput.Text;
    }
    private void MobilePasswordChanging(PasswordBox sender, PasswordBoxPasswordChangingEventArgs args)
    { if (initialized && !updatingControls && !mobileLoading && !ManualApplicationBusy && !dialogOpen && !closing) mobileDraft.Password = sender.Password; }
    private async void RefreshMobileClicked(object sender, RoutedEventArgs args)
    { if (await ResolveMobileDraftAsync()) await RefreshMobileAsync(reloadDraft: true); }
    private async void SaveMobileClicked(object sender, RoutedEventArgs args) => await SaveMobileAsync();
    private void DiscardMobileClicked(object sender, RoutedEventArgs args)
    {
        if (ManualApplicationBusy || dialogOpen || closing) return;
        mobileLoading = true; try { mobileDraft.Load(mobileView?.Settings ?? mobileDraft.Settings); } finally { mobileLoading = false; }
        mobileMessage = ""; ClearMobilePair(); RefreshView();
    }
    private async Task<bool> SaveMobileAsync()
    {
        if (ManualApplicationBusy || dialogOpen || closing) return false;
        var home = viewModel.Home; var revision = mobileDraft.Revision;
        mobileAction = true; mobileMessage = ""; RefreshView();
        try
        {
            await automaticEnhancements.CancelAndWaitAsync();
            var review = await mobileConnection.PrepareAsync(home, mobileDraft.Input(), lifetime.Token);
            if (mobileDraft.Settings is { } baseline && baseline.Revision != review.Before.Revision) throw new IOException("手机设置已改变，请重新读取后再保存；当前草稿已保留。");
            if (review.HasChanges && !await ConfirmDialogAsync("保存并启动手机连接", review.Description, "确认保存并启动")) return false;
            var result = await mobileConnection.SaveAndStartAsync(review, () => !closing && home == viewModel.Home && revision == mobileDraft.Revision, lifetime.Token);
            mobileMessage = result.Message; mobileMessageError = !result.Succeeded;
            if (result.SettingsSaved || result.Succeeded)
            {
                var saved = result.Value ?? await mobileConnection.LoadSettingsAsync(home, lifetime.Token);
                mobileLoading = true; try { mobileDraft.Load(saved); } finally { mobileLoading = false; }
                ClearMobilePair();
            }
            return result.Succeeded;
        }
        catch (Exception error) { mobileMessage = error.Message; mobileMessageError = true; return false; }
        finally { mobileAction = false; if (!closing && home == viewModel.Home) { await RefreshMobileAsync(); await RefreshGatewaysAsync(); RefreshView(); } }
    }
    private async Task<bool> ResolveMobileDraftAsync()
    {
        if (!mobileDraft.HasDraft) return true;
        if (ManualApplicationBusy || dialogOpen || closing) return false;
        var dialog = new ContentDialog { Title = "有未保存的手机设置", Content = "保存后会启动手机网关。也可放弃本次修改或继续编辑。", PrimaryButtonText = "查看并保存",
            SecondaryButtonText = "放弃修改", CloseButtonText = "继续编辑", DefaultButton = ContentDialogButton.Close, XamlRoot = Root.XamlRoot };
        var choice = await ShowDialogAsync(dialog);
        if (choice == ContentDialogResult.Primary) return await SaveMobileAsync();
        if (choice != ContentDialogResult.Secondary) return false;
        DiscardMobileClicked(DiscardMobileButton, new()); return true;
    }

    private async Task RunMobileControlAsync(Func<MobileConnectionSettings, Func<bool>, Task> action)
    {
        if (ManualApplicationBusy || dialogOpen || closing || mobileDraft.HasDraft || mobileView?.Running != true) return;
        var expected = mobileView.Settings; mobileAction = true; RefreshView();
        try { await action(expected, () => !closing && viewModel.Home == expected.Home && !mobileDraft.HasDraft); }
        catch (Exception error) { mobileMessage = error.Message; mobileMessageError = true; }
        finally { mobileAction = false; if (!closing && viewModel.Home == expected.Home) { await RefreshMobileAsync(); RefreshView(); } }
    }
    private void AcceptMobileResult<T>(MobileActionResult<T> result)
    { mobileMessage = result.Message; mobileMessageError = !result.Succeeded; if (!result.Succeeded) throw new IOException(result.Message); }
    private async void CheckMobileEntryClicked(object sender, RoutedEventArgs args)
        => await RunMobileControlAsync(async (saved, eligible) => AcceptMobileResult(await mobileConnection.CheckEntryAsync(saved, eligible, lifetime.Token)));
    private async void OpenMobileManagerClicked(object sender, RoutedEventArgs args)
        => await RunMobileControlAsync(async (saved, eligible) =>
        {
            var result = await mobileConnection.ManagerAsync(saved, eligible, lifetime.Token); AcceptMobileResult(result);
            if (result.Value is not { } address) throw new IOException("未返回本机接入管理地址。");
            if (options.SmokeOutput is not null) (smokeOpenMobileManager ?? throw new InvalidOperationException("隔离检查不能打开真实浏览器。"))(address);
            else Process.Start(new ProcessStartInfo(address.AbsoluteUri) { UseShellExecute = true });
            mobileMessage = "已在浏览器打开本机 Codex 接入管理。";
        });
    private async void CreateMobilePairClicked(object sender, RoutedEventArgs args) => await GenerateMobilePairAsync();
    private async Task GenerateMobilePairAsync() => await RunMobileControlAsync(async (saved, eligible) =>
    {
        var result = await mobileConnection.CreatePairAsync(saved, eligible, lifetime.Token); AcceptMobileResult(result);
        var grant = result.Value ?? throw new IOException("未返回配对码。");
        using var memory = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(memory.GetOutputStreamAt(0))) { writer.WriteBytes(grant.Image); await writer.StoreAsync(); }
        memory.Seek(0); var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(memory);
        mobilePair = grant; MobilePairImage.Source = bitmap; MobilePairImage.Visibility = Visibility.Visible;
        mobilePairMessage = "使用安卓 App 扫描。有效至 " + grant.Expires.ToLocalTime().ToString("HH:mm:ss") + "，仅可使用一次。";
    });
    private async void RevokeMobilePairClicked(object sender, RoutedEventArgs args) => await RevokeMobilePairAsync();
    private async Task RevokeMobilePairAsync()
    {
        if (mobilePair is not { } pair) return;
        await RunMobileControlAsync(async (saved, eligible) =>
        { AcceptMobileResult(await mobileConnection.RevokePairAsync(saved, pair.Id, eligible, lifetime.Token)); ClearMobilePair("配对码已撤销。"); });
    }
    private void ClearMobilePair(string? message = null)
    { mobilePair = null; MobilePairImage.Source = null; MobilePairImage.Visibility = Visibility.Collapsed; mobilePairMessage = message ?? "先保存并启动，再检测 HTTPS 入口。"; }
    private async Task MobilePairTickAsync()
    {
        if (mobilePair is not { } pair || ManualApplicationBusy || dialogOpen || closing) return;
        if (pair.Expires <= DateTimeOffset.UtcNow) { ClearMobilePair("配对码已过期，请重新生成。"); RefreshView(); return; }
        if (gatewayTicks % 3 != 0) return;
        await RunMobileControlAsync(async (saved, eligible) =>
        {
            var result = await mobileConnection.PairStateAsync(saved, pair.Id, eligible, lifetime.Token);
            if (mobilePair?.Id != pair.Id) return;
            if (!result.Succeeded || result.Value != "active") ClearMobilePair(result.Value == "used" ? "配对成功，可在“授权设备”管理登录设备。" : "配对码已失效，请重新生成。");
        });
    }
    private void RenderMobileDeviceRows()
    {
        MobileDevicesList.Children.Clear();
        if (mobileView?.Running != true) { MobileDevicesList.Children.Add(new TextBlock { Text = "启动服务后可查看设备。", Opacity = 0.7 }); return; }
        foreach (var device in mobileView.Devices.Devices)
        {
            var row = new StackPanel { Spacing = 6 };
            row.Children.Add(new TextBlock { Text = device.Label + " · " + device.Ip + " · 最近 " + device.LastSeen.ToLocalTime().ToString("MM-dd HH:mm"), TextWrapping = TextWrapping.Wrap });
            var revoke = new Button { Content = "撤销设备", Tag = device.Id };
            revoke.Click += async (_, _) => await ChangeMobileDeviceAsync(device.Id, null); row.Children.Add(revoke); MobileDevicesList.Children.Add(row);
        }
        foreach (var ip in mobileView.Devices.BlockedIps)
        {
            var row = new StackPanel { Spacing = 6 }; row.Children.Add(new TextBlock { Text = ip + " · 登录已限制", TextWrapping = TextWrapping.Wrap });
            var unblock = new Button { Content = "解除登录限制", Tag = ip }; unblock.Click += async (_, _) => await ChangeMobileDeviceAsync(null, ip);
            row.Children.Add(unblock); MobileDevicesList.Children.Add(row);
        }
        if (MobileDevicesList.Children.Count == 0) MobileDevicesList.Children.Add(new TextBlock { Text = "尚无登录设备。", Opacity = 0.7 });
    }
    private async Task ChangeMobileDeviceAsync(string? id, string? ip)
    {
        if (ManualApplicationBusy || dialogOpen || closing) return;
        var device = mobileView?.Devices.Devices.SingleOrDefault(candidate => candidate.Id == id);
        if (id is not null && device is null) return;
        if (!await ConfirmDialogAsync(id is not null ? "撤销设备授权" : "解除登录限制", device is not null ? device.Label + " · " + device.Ip + "\n撤销后，此设备需要重新登录。" :
            "解除 " + ip + " 的登录限制后，该地址可以再次尝试登录。", id is not null ? "撤销设备" : "解除限制")) return;
        await RunMobileControlAsync(async (saved, eligible) => AcceptMobileResult(id is not null
            ? await mobileConnection.RevokeDeviceAsync(saved, id, eligible, lifetime.Token) : await mobileConnection.UnblockAsync(saved, ip!, eligible, lifetime.Token)));
    }
}
