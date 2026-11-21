using CodexConfig.Application;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexConfig.WinUI.Views;

public sealed partial class MobilePage : UserControl
{
    public MobilePage() => InitializeComponent();

    internal MainWindow? Host { private get; set; }

    private int deviceRevision = -1;

    internal void Render(MobileSetupDraftViewModel mobileDraft, MobileConnectionView? mobileView, MobileSetupStage stage,
        bool pairingPage, bool settingsPage, bool editable, bool usable, bool workerRunning, bool mobileRefreshing,
        bool canRefresh, bool mobileEntryFailed, bool hasPair, string mobilePairMessage, string mobileMessage, bool mobileMessageError)
    {
        foreach (var control in new Control[] { MobileUrlInput, MobilePortInput, MobileUsernameInput, MobilePasswordInput }) control.IsEnabled = editable;
        if (MobileUrlInput.Text != mobileDraft.PublicUrl) MobileUrlInput.Text = mobileDraft.PublicUrl;
        if (MobilePortInput.Text != mobileDraft.Port) MobilePortInput.Text = mobileDraft.Port;
        if (MobileUsernameInput.Text != mobileDraft.Username) MobileUsernameInput.Text = mobileDraft.Username;
        if (MobilePasswordInput.Password != mobileDraft.Password) MobilePasswordInput.Password = mobileDraft.Password;
        MobilePasswordHint.Text = mobileDraft.Settings?.Configured == true ? "密码已设置。留空沿用；更换密码会撤销旧设备授权。" : "设置 12 至 256 个字符的独立手机访问密码。";
        MobileDraftHint.Text = mobileDraft.HasDraft ? "有未保存的手机设置。" : "";
        MobileDraftHint.Visibility = mobileDraft.HasDraft ? Visibility.Visible : Visibility.Collapsed;
        MobileSettingsCard.Visibility = settingsPage || pairingPage && stage == MobileSetupStage.Entry ? Visibility.Visible : Visibility.Collapsed;
        MobileStepsPanel.Visibility = pairingPage && stage != MobileSetupStage.Complete ? Visibility.Visible : Visibility.Collapsed;
        MobilePairCard.Visibility = pairingPage && stage == MobileSetupStage.Pair ? Visibility.Visible : Visibility.Collapsed;
        MobileStatusCard.Visibility = pairingPage && stage == MobileSetupStage.Complete ? Visibility.Visible : Visibility.Collapsed;
        MobileAccessTools.Visibility = pairingPage ? Visibility.Visible : Visibility.Collapsed;
        RefreshMobileButton.IsEnabled = canRefresh;
        StartMobilePairingButton.Visibility = pairingPage && stage == MobileSetupStage.Start ? Visibility.Visible : Visibility.Collapsed;
        StartMobilePairingButton.IsEnabled = editable && !mobileRefreshing && stage == MobileSetupStage.Start;
        CheckMobileEntryButton.Visibility = pairingPage && stage == MobileSetupStage.Check ? Visibility.Visible : Visibility.Collapsed;
        CheckMobileEntryButton.IsEnabled = usable && stage == MobileSetupStage.Check;
        CreateMobilePairButton.IsEnabled = usable && stage == MobileSetupStage.Pair;
        OpenMobileManagerButton.IsEnabled = usable;
        PairAnotherDeviceButton.IsEnabled = usable;
        CheckMobileEntryButton.Content = mobileEntryFailed ? "重新检测" : "检测 HTTPS 入口";
        MobileEntryHelpButton.Visibility = pairingPage && stage == MobileSetupStage.Check && mobileEntryFailed ? Visibility.Visible : Visibility.Collapsed;
        CreateMobilePairButton.Content = hasPair || mobilePairMessage.Length > 0 ? "重新生成配对码" : "生成配对码";
        RevokeMobilePairButton.IsEnabled = usable && hasPair;
        RevokeMobilePairButton.Visibility = hasPair ? Visibility.Visible : Visibility.Collapsed;
        RenderSteps(stage, mobileMessageError, mobileEntryFailed);
        MobileRuntimeText.Text = mobileView is null ? "尚未读取手机设置。" : mobileView.Running is true ? mobileView.EntryChecked ? "网关运行中 · HTTPS 入口已检测通过" : "网关运行中 · 尚未检测 HTTPS 入口" :
            mobileView.Running is null ? "后台状态未确认，请在服务设置刷新。" : mobileView.Settings.Configured ? "手机服务已停止，可在服务设置启动。" : "尚未保存手机连接。";
        MobileEndpointText.Text = mobileView?.Settings.Configured == true ? "已保存入口：" + mobileView.Settings.PublicUrl + "\n本机监听端口：" + mobileView.Settings.Port : "";
        MobileOperationBar.IsOpen = mobileMessage.Length > 0; MobileOperationBar.Message = mobileMessage;
        MobileOperationBar.Visibility = MobileOperationBar.IsOpen ? Visibility.Visible : Visibility.Collapsed;
        MobileOperationBar.Severity = mobileMessageError ? InfoBarSeverity.Error : InfoBarSeverity.Success;
        MobilePairHint.Text = mobilePairMessage;
        MobilePairHint.Visibility = mobilePairMessage.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        MobileCodexReadinessText.Text = mobileView?.CodexReadiness switch
        {
            "ready" => "已有可供手机访问的 Codex。",
            "configure" => "尚无可供手机访问的 Codex。请选择并启用一个接入。",
            "checking" => "正在检查可供手机访问的 Codex…",
            _ => "Codex 接入状态待确认，可刷新或打开接入管理。"
        };
        MobileAccessStatus.Text = workerRunning ? MobileCodexReadinessText.Text : MobileRuntimeText.Text;
    }

    internal void RenderDeviceRows(int revision, MobileConnectionView? view, bool usable, Func<string?, string?, Task> changeDevice)
    {
        if (deviceRevision != revision)
        {
            BuildDeviceRows(view, changeDevice);
            deviceRevision = revision;
        }
        foreach (var button in MobileDevicesList.Children.OfType<StackPanel>().SelectMany(panel => panel.Children).OfType<Button>())
            button.IsEnabled = usable;
    }

    private void RenderSteps(MobileSetupStage stage, bool mobileMessageError, bool mobileEntryFailed)
    {
        void Step(TextBlock label, MobileSetupStage step, string title, string done)
        {
            label.Text = title + " · " + (stage == step ? "当前" : stage > step ? done : "待处理");
            label.FontWeight = stage == step ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
        }
        Step(MobileEntryStepText, MobileSetupStage.Entry, "1 填写入口", "已保存");
        Step(MobileStartStepText, MobileSetupStage.Start, "2 启动服务", "运行中");
        Step(MobileCheckStepText, MobileSetupStage.Check, "3 检测 HTTPS", "已通过");
        Step(MobilePairStepText, MobileSetupStage.Pair, "4 扫码", "已配对");
        MobileNextStepText.Text = stage switch
        {
            MobileSetupStage.Loading => mobileMessageError ? "可展开服务与访问，重新读取手机设置。" : "正在读取手机设置…",
            MobileSetupStage.Entry => "填写手机能访问的入口和登录信息。",
            MobileSetupStage.Start => "启动手机服务，使用已保存的访问设置。",
            MobileSetupStage.Check when mobileEntryFailed => "请检查手机访问地址与转发设置，修复后重新检测。",
            MobileSetupStage.Check => "检测 HTTPS 入口是否指向这台电脑。",
            MobileSetupStage.Pair => "HTTPS 入口已检测通过。",
            _ => ""
        };
    }

    private void BuildDeviceRows(MobileConnectionView? mobileView, Func<string?, string?, Task> changeDevice)
    {
        MobileDevicesList.Children.Clear();
        if (mobileView?.Running != true) { MobileDevicesList.Children.Add(new TextBlock { Text = "启动服务后可查看设备。", Opacity = 0.7 }); return; }
        foreach (var device in mobileView.Devices.Devices)
        {
            var row = new StackPanel { Spacing = 6 };
            row.Children.Add(new TextBlock { Text = device.Label + " · " + device.Ip + " · 最近 " + device.LastSeen.ToLocalTime().ToString("MM-dd HH:mm"), TextWrapping = TextWrapping.Wrap });
            var revoke = new Button { Content = "撤销设备", Tag = device.Id };
            revoke.Click += async (_, _) => await changeDevice(device.Id, null); row.Children.Add(revoke); MobileDevicesList.Children.Add(row);
        }
        foreach (var ip in mobileView.Devices.BlockedIps)
        {
            var row = new StackPanel { Spacing = 6 }; row.Children.Add(new TextBlock { Text = ip + " · 登录已限制", TextWrapping = TextWrapping.Wrap });
            var unblock = new Button { Content = "解除登录限制", Tag = ip }; unblock.Click += async (_, _) => await changeDevice(null, ip);
            row.Children.Add(unblock); MobileDevicesList.Children.Add(row);
        }
        if (MobileDevicesList.Children.Count == 0) MobileDevicesList.Children.Add(new TextBlock { Text = "尚无登录设备。", Opacity = 0.7 });
    }

    private void CheckMobileEntryClicked(object sender, RoutedEventArgs args) => Host?.CheckMobileEntryClicked(sender, args);

    private void CreateMobilePairClicked(object sender, RoutedEventArgs args) => Host?.CreateMobilePairClicked(sender, args);

    private void GatewayActionClicked(object sender, RoutedEventArgs args) => Host?.GatewayActionClicked(sender, args);

    private void GatewayAutomaticChanged(object sender, RoutedEventArgs args) => Host?.GatewayAutomaticChanged(sender, args);

    private void MobileEntryHelpClicked(object sender, RoutedEventArgs args) => Host?.MobileEntryHelpClicked(sender, args);

    private void MobilePasswordChanging(PasswordBox sender, PasswordBoxPasswordChangingEventArgs args) => Host?.MobilePasswordChanging(sender, args);

    private void MobileTextChanged(TextBox sender, TextBoxTextChangingEventArgs args) => Host?.MobileTextChanged(sender, args);

    private void OpenMobileManagerClicked(object sender, RoutedEventArgs args) => Host?.OpenMobileManagerClicked(sender, args);

    private void OpenMobileSettingsClicked(object sender, RoutedEventArgs args) => Host?.OpenMobileSettingsClicked(sender, args);

    private void OpenPairingPageClicked(object sender, RoutedEventArgs args) => Host?.OpenPairingPageClicked(sender, args);

    private void PairAnotherDeviceClicked(object sender, RoutedEventArgs args) => Host?.PairAnotherDeviceClicked(sender, args);

    private void RefreshGatewaysClicked(object sender, RoutedEventArgs args) => Host?.RefreshGatewaysClicked(sender, args);

    private void RefreshMobileClicked(object sender, RoutedEventArgs args) => Host?.RefreshMobileClicked(sender, args);

    private void RevokeMobilePairClicked(object sender, RoutedEventArgs args) => Host?.RevokeMobilePairClicked(sender, args);

    private void StartMobilePairingClicked(object sender, RoutedEventArgs args) => Host?.StartMobilePairingClicked(sender, args);

    private void ViewMobileDevicesClicked(object sender, RoutedEventArgs args) => Host?.ViewMobileDevicesClicked(sender, args);
}
