using System.ComponentModel;
using System.Diagnostics;
using CodexConfig.Application;
using CodexConfig.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;

namespace CodexConfig.WinUI;

internal enum MobileSetupStage { Loading, Entry, Start, Check, Pair, Complete }

public sealed partial class MainWindow
{
    private bool mobileLoading, mobileRefreshing, mobileEntryFailed, mobilePaired;
    private bool mobileAction => busy.IsActive(WindowOperation.MobileConnection);
    private bool pairingAnotherDevice;
    private Task mobileReadTask = Task.CompletedTask;
    private string? mobileHome;
    private string mobileMessage = "";
    private bool mobileMessageError;
    private MobileConnectionView? mobileView;
    private MobilePairGrant? mobilePair;
    private int mobileViewRevision;
    private string mobilePairMessage = "";
#if CODEX_DOCK_WINUI_SMOKE
    private Action<Uri>? smokeOpenMobileManager;
#endif
    internal void OpenMobileClicked(object sender, RoutedEventArgs args) => NavigateTo(MobileNav, revealStart: true);
    internal void OpenPairingPageClicked(object sender, RoutedEventArgs args)
    { if (!ApplicationBusy && !dialogOpen && !closing) NavigateTo(MobileNav, revealStart: true); }
    internal void ViewMobileDevicesClicked(object sender, RoutedEventArgs args) => NavigateTo(MobileDevicesNav, revealStart: true);
    internal void PairAnotherDeviceClicked(object sender, RoutedEventArgs args)
    {
        pairingAnotherDevice = true;
        mobilePaired = false;
        ClearMobilePair();
        RefreshView();
    }

    private void ResetMobileHome()
    {
        if (mobileHome == viewModel.Home) return;
        mobileHome = viewModel.Home; mobileLoading = true;
        try { mobileDraft.Load(null); }
        finally { mobileLoading = false; }
        mobileView = null; mobileMessage = ""; mobileMessageError = mobileEntryFailed = mobilePaired = pairingAnotherDevice = false;
        ClearMobilePair(); mobileViewRevision++;
    }
    private void MobileDraftChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(MobileSetupDraftViewModel.Revision) || mobileLoading || closing) return;
        mobileEntryFailed = mobilePaired = pairingAnotherDevice = false; ClearMobilePair(); mobileMessage = ""; RefreshView();
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
        mobileEntryFailed = false; mobileView = null; mobileViewRevision++; RefreshView();
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
                mobileEntryFailed = mobilePaired = pairingAnotherDevice = false;
                ClearMobilePair();
                if (mobileDraft.HasDraft) { mobileMessage = "手机设置已被其他窗口修改，当前草稿已保留。请重新读取并核对后保存。"; mobileMessageError = true; }
                else { mobileLoading = true; try { mobileDraft.Load(current.Settings); } finally { mobileLoading = false; } }
            }
            mobileView = current; mobileViewRevision++;
            if (current.Running != true || !current.EntryChecked)
            {
                mobilePaired = false;
                if (mobilePair is not null) ClearMobilePair();
            }
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

    private bool MobileWorkerRunning => mobileDraft.Settings?.Configured == true &&
        mobileView?.Running == true && mobileDraft.Settings.Revision == mobileView.Settings.Revision;

    private MobileSetupStage GetMobileSetupStage()
    {
        if (mobileDraft.Settings is null) return MobileSetupStage.Loading;
        if (!mobileDraft.Settings.Configured || mobileDraft.HasDraft) return MobileSetupStage.Entry;
        if (!MobileWorkerRunning) return MobileSetupStage.Start;
        if (mobileView?.EntryChecked != true || mobileEntryFailed) return MobileSetupStage.Check;
        if (mobilePair is null && !pairingAnotherDevice && (mobilePaired || mobileView.Devices.Devices.Any(device => device.Label == "安卓 App")))
            return MobileSetupStage.Complete;
        return MobileSetupStage.Pair;
    }

    private void RefreshMobileControls()
    {
        var canOperate = !ManualApplicationBusy && !dialogOpen && !closing;
        var editable = mobileDraft.Settings is not null && canOperate;
        var pairingPage = currentPage == MobileNav;
        var settingsPage = currentPage == MobileSettingsNav;
        var stage = GetMobileSetupStage();
        var workerRunning = MobileWorkerRunning;
        var usable = editable && workerRunning && !mobileDraft.HasDraft;
        MobilePanel.Render(mobileDraft, mobileView, stage, pairingPage, settingsPage, editable, usable, workerRunning,
            mobileRefreshing, canOperate && !mobileRefreshing, mobileEntryFailed, mobilePair is not null, mobilePairMessage, mobileMessage, mobileMessageError);
        if (currentPage == MobileDevicesNav) MobilePanel.RenderDeviceRows(mobileViewRevision, mobileView, usable, ChangeMobileDeviceAsync);
        MobileFooter.Visibility = settingsPage || pairingPage && stage == MobileSetupStage.Entry ? Visibility.Visible : Visibility.Collapsed;
        MobileFooterMessage.Text = pairingPage ? "核对后保存手机访问入口。" : "保存手机访问设置并启动服务。";
        SaveMobileButton.Content = pairingPage ? "查看并保存入口" : "查看设置并保存启动";
        SaveMobileButton.IsEnabled = editable && !mobileRefreshing && (mobileDraft.HasDraft || settingsPage && mobileDraft.Settings?.Configured == true);
        DiscardMobileButton.IsEnabled = editable && mobileDraft.HasDraft;
        ContinueMobilePairButton.Visibility = settingsPage && usable ? Visibility.Visible : Visibility.Collapsed;
        ContinueMobilePairButton.IsEnabled = usable;
    }

    internal void MobileTextChanged(TextBox sender, TextBoxTextChangingEventArgs args)
    {
        if (!initialized || updatingControls || mobileLoading || ManualApplicationBusy || dialogOpen || closing) return;
        if (ReferenceEquals(sender, MobilePanel.MobileUrlInput)) mobileDraft.PublicUrl = MobilePanel.MobileUrlInput.Text;
        if (ReferenceEquals(sender, MobilePanel.MobilePortInput)) mobileDraft.Port = MobilePanel.MobilePortInput.Text;
        if (ReferenceEquals(sender, MobilePanel.MobileUsernameInput)) mobileDraft.Username = MobilePanel.MobileUsernameInput.Text;
    }
    internal void MobilePasswordChanging(PasswordBox sender, PasswordBoxPasswordChangingEventArgs args)
    { if (initialized && !updatingControls && !mobileLoading && !ManualApplicationBusy && !dialogOpen && !closing) mobileDraft.Password = sender.Password; }
    internal async void RefreshMobileClicked(object sender, RoutedEventArgs args)
    { if (await ResolveMobileDraftAsync()) await RefreshMobileAsync(reloadDraft: true); }
    private async void SaveMobileClicked(object sender, RoutedEventArgs args) => await SaveMobileAsync(start: currentPage != MobileNav);
    internal async void StartMobilePairingClicked(object sender, RoutedEventArgs args) => await StartMobilePairingAsync();
    private async Task StartMobilePairingAsync()
    { if (GetMobileSetupStage() == MobileSetupStage.Start) await SaveMobileAsync(); }

    internal async void MobileEntryHelpClicked(object sender, RoutedEventArgs args)
    {
        if (dialogOpen || closing) return;
        var port = int.TryParse(mobileDraft.Port, out var enteredPort) && enteredPort is > 0 and <= 65535 ? enteredPort : MobileGatewayManager.DefaultPort;
        string instructions = $"请提供一个手机能访问、证书受手机信任的 HTTPS 地址。\n"
            + $"将这个地址的反向代理转发到 http://这台 Windows 电脑的内网 IP:{port}。保留原始 Host（含端口）和 Origin。\n"
            + "请核对代理到电脑的网络及防火墙通路。配置好后，把 HTTPS 地址填回 Codex Dock 手机连接页。";
        var dialog = new ContentDialog { Title = "准备手机访问地址", Content = new TextBlock { Text = instructions, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
            PrimaryButtonText = "复制准备说明", CloseButtonText = "返回", DefaultButton = ContentDialogButton.Close, XamlRoot = Root.XamlRoot };
        if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary) return;
        try
        {
            var package = new DataPackage(); package.SetText(instructions);
            Clipboard.SetContent(package);
        }
        catch (Exception)
        {
            mobileMessage = "未能复制准备说明，可以重新打开并选择文字。"; mobileMessageError = true; RefreshView();
        }
    }
    private void DiscardMobileClicked(object sender, RoutedEventArgs args)
    {
        if (ManualApplicationBusy || dialogOpen || closing) return;
        mobileLoading = true; try { mobileDraft.Load(mobileView?.Settings ?? mobileDraft.Settings); } finally { mobileLoading = false; }
        mobileMessage = ""; mobileMessageError = false; ClearMobilePair(); RefreshView();
    }
    private async Task<bool> SaveMobileAsync(bool start = true)
    {
        if (ManualApplicationBusy || dialogOpen || closing) return false;
        var home = viewModel.Home; var revision = mobileDraft.Revision;
        using var operation = busy.Begin(WindowOperation.MobileConnection, "正在处理手机连接…"); mobileMessage = ""; mobileMessageError = false; RefreshView();
        try
        {
            await automaticEnhancements.CancelAndWaitAsync();
            var review = await mobileConnection.PrepareAsync(home, mobileDraft.Input(), lifetime.Token);
            if (mobileDraft.Settings is { } baseline && baseline.Revision != review.Before.Revision) throw new IOException("手机设置已改变，请重新读取后再保存；当前草稿已保留。");
            if (review.HasChanges && !await ConfirmDialogAsync(start ? "保存并启动手机连接" : "核对手机访问入口",
                review.Describe(start), start ? "确认保存并启动" : "确认保存")) return false;
            bool Eligible() => !closing && home == viewModel.Home && revision == mobileDraft.Revision;
            var result = start ? await mobileConnection.SaveAndStartAsync(review, Eligible, lifetime.Token)
                : await mobileConnection.SaveAsync(review, Eligible, lifetime.Token);
            mobileMessage = result.Message; mobileMessageError = !result.Succeeded;
            if (result.SettingsSaved || result.Succeeded)
            {
                var saved = result.Value ?? await mobileConnection.LoadSettingsAsync(home, lifetime.Token);
                mobileLoading = true; try { mobileDraft.Load(saved); } finally { mobileLoading = false; }
                mobileEntryFailed = mobilePaired = pairingAnotherDevice = false; ClearMobilePair();
            }
            return result.Succeeded;
        }
        catch (Exception error) { mobileMessage = error.Message; mobileMessageError = true; return false; }
        finally { operation.Dispose(); if (!closing && home == viewModel.Home) { await RefreshMobileAsync(); await RefreshGatewaysAsync(); RefreshView(); } }
    }
    private async Task<bool> ResolveMobileDraftAsync()
    {
        if (!mobileDraft.HasDraft) return true;
        if (ManualApplicationBusy || dialogOpen || closing) return false;
        var dialog = new ContentDialog { Title = "有未保存的手机设置", Content = "可以核对后保存入口、放弃修改或继续编辑。", PrimaryButtonText = "查看并保存",
            SecondaryButtonText = "放弃修改", CloseButtonText = "继续编辑", DefaultButton = ContentDialogButton.Close, XamlRoot = Root.XamlRoot };
        var choice = await ShowDialogAsync(dialog);
        if (choice == ContentDialogResult.Primary) return await SaveMobileAsync(start: false);
        if (choice != ContentDialogResult.Secondary) return false;
        DiscardMobileClicked(DiscardMobileButton, new()); return true;
    }

    private async Task RunMobileControlAsync(Func<MobileConnectionSettings, Func<bool>, Task> action)
    {
        if (ManualApplicationBusy || dialogOpen || closing || mobileDraft.HasDraft || !MobileWorkerRunning || mobileView is null) return;
        var expected = mobileView.Settings; using var operation = busy.Begin(WindowOperation.MobileConnection, "正在处理手机连接…"); RefreshView();
        try { await action(expected, () => !closing && viewModel.Home == expected.Home && !mobileDraft.HasDraft); }
        catch (Exception error) { mobileMessage = error.Message; mobileMessageError = true; }
        finally { operation.Dispose(); if (!closing && viewModel.Home == expected.Home) { await RefreshMobileAsync(); RefreshView(); } }
    }
    private void AcceptMobileResult<T>(MobileActionResult<T> result)
    { mobileMessage = result.Message; mobileMessageError = !result.Succeeded; if (!result.Succeeded) throw new IOException(result.Message); }
    internal async void CheckMobileEntryClicked(object sender, RoutedEventArgs args) => await CheckMobileEntryAsync();
    private Task CheckMobileEntryAsync()
        => RunMobileControlAsync(async (saved, eligible) =>
        {
            var result = await mobileConnection.CheckEntryAsync(saved, eligible, lifetime.Token);
            mobileEntryFailed = !result.Succeeded;
            AcceptMobileResult(result);
        });
    internal async void OpenMobileManagerClicked(object sender, RoutedEventArgs args)
        => await RunMobileControlAsync(async (saved, eligible) =>
        {
            var result = await mobileConnection.ManagerAsync(saved, eligible, lifetime.Token); AcceptMobileResult(result);
            if (result.Value is not { } address) throw new IOException("未返回本机接入管理地址。");
#if CODEX_DOCK_WINUI_SMOKE
            if (options.SmokeOutput is not null) (smokeOpenMobileManager ?? throw new InvalidOperationException("隔离检查不能打开真实浏览器。"))(address);
            else
#endif
            Process.Start(new ProcessStartInfo(address.AbsoluteUri) { UseShellExecute = true });
            mobileMessage = "已在浏览器打开本机 Codex 接入管理。";
        });
    internal async void CreateMobilePairClicked(object sender, RoutedEventArgs args) => await GenerateMobilePairAsync();
    private Task GenerateMobilePairAsync() => GetMobileSetupStage() != MobileSetupStage.Pair ? Task.CompletedTask :
        RunMobileControlAsync(async (saved, eligible) =>
        {
            mobilePairMessage = "正在生成配对码…"; RefreshView();
            var result = await mobileConnection.CreatePairAsync(saved, eligible, lifetime.Token);
            mobileEntryFailed = !result.Succeeded;
            if (!result.Succeeded) mobilePairMessage = "未能生成配对码。检查入口后可以重试。";
            AcceptMobileResult(result);
            var grant = result.Value ?? throw new IOException("未返回配对码。");
            using var memory = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(memory.GetOutputStreamAt(0))) { writer.WriteBytes(grant.Image); await writer.StoreAsync(); }
            memory.Seek(0); var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(memory);
            mobilePair = grant; mobilePaired = false; mobileMessage = "";
            MobilePanel.MobilePairImage.Source = bitmap; MobilePanel.MobilePairImage.Visibility = Visibility.Visible;
            mobilePairMessage = "使用安卓 App 扫描。有效至 " + grant.Expires.ToLocalTime().ToString("HH:mm:ss") + "，仅可使用一次。";
            if (currentPage == MobileNav) PageScroll.ChangeView(null, 0, null, true);
        });
    internal async void RevokeMobilePairClicked(object sender, RoutedEventArgs args) => await RevokeMobilePairAsync();
    private async Task RevokeMobilePairAsync()
    {
        if (mobilePair is not { } pair) return;
        await RunMobileControlAsync(async (saved, eligible) =>
        { AcceptMobileResult(await mobileConnection.RevokePairAsync(saved, pair.Id, eligible, lifetime.Token)); ClearMobilePair("配对码已撤销。"); });
    }
    private void ClearMobilePair(string? message = null)
    { mobilePair = null; MobilePanel.MobilePairImage.Source = null; MobilePanel.MobilePairImage.Visibility = Visibility.Collapsed; mobilePairMessage = message ?? ""; }
    private async Task MobilePairTickAsync()
    {
        if (mobilePair is not { } pair || ManualApplicationBusy || dialogOpen || closing) return;
        if (pair.Expires <= DateTimeOffset.UtcNow) { ClearMobilePair("配对码已过期，请重新生成。"); RefreshView(); return; }
        if (gatewayTicks % 3 != 0) return;
        await RunMobileControlAsync(async (saved, eligible) =>
        {
            var result = await mobileConnection.PairStateAsync(saved, pair.Id, eligible, lifetime.Token);
            if (mobilePair?.Id != pair.Id) return;
            if (result.Succeeded && result.Value != "active")
            {
                mobilePaired = result.Value == "used";
                if (mobilePaired) pairingAnotherDevice = false;
                ClearMobilePair(mobilePaired ? "手机已配对。可在“授权设备”查看。" : "配对码已失效，请重新生成。");
            }
        });
    }
    private async Task ChangeMobileDeviceAsync(string? id, string? ip)
    {
        if (ManualApplicationBusy || dialogOpen || closing) return;
        var device = mobileView?.Devices.Devices.SingleOrDefault(candidate => candidate.Id == id);
        if (id is not null && device is null) return;
        if (!await ConfirmDialogAsync(id is not null ? "撤销设备授权" : "解除登录限制", device is not null ? device.Label + " · " + device.Ip + "\n撤销后，此设备需要重新登录。" :
            "解除 " + ip + " 的登录限制后，该地址可以再次尝试登录。", id is not null ? "撤销设备" : "解除限制")) return;
        await RunMobileControlAsync(async (saved, eligible) =>
        {
            var result = id is not null
                ? await mobileConnection.RevokeDeviceAsync(saved, id, eligible, lifetime.Token)
                : await mobileConnection.UnblockAsync(saved, ip!, eligible, lifetime.Token);
            AcceptMobileResult(result);
            if (id is not null && result.Value?.Devices.Any(device => device.Label == "安卓 App") != true) mobilePaired = false;
        });
    }
}
