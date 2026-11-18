using CodexConfig.Application;
using CodexConfig.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly WorkspaceShellViewModel viewModel;
    private readonly WinUiLaunchOptions options;
    private readonly ConfigStore store;
    private readonly CancellationTokenSource lifetime = new();
    private readonly NativeWindowSizing windowSizing;
    private readonly DesktopEnhancementRuntime desktopEnhancements;
    private readonly IDesktopApplyRuntime applicationRuntime;
    private readonly DesktopApplicationService desktopApplication;
    private readonly ConfigurationSaveService configurationSave;
    private readonly ConfigurationRecoveryService configurationRecovery;
    private readonly OfficialLoginService officialLoginReader;
    private readonly ModelCatalogClient modelCatalogClient = new();
    private ModelDiscoveryService modelDiscovery;
    private readonly ConnectionDraftViewModel connectionDraft;
    private readonly AutomaticEnhancementService automaticEnhancements;
    private readonly GatewayManagementService gatewayManagement;
    private readonly MobileConnectionService mobileConnection;
    private readonly MobileSetupDraftViewModel mobileDraft = new();
    private readonly IMobileConnectionRuntime mobileRuntime;
    private readonly IGatewayManagementRuntime gatewayRuntime;
    private bool initialized, updatingControls;
    private string? automaticStatusHome;
    private bool automaticStatusEnabled;
    private string shellError = "", shellErrorTitle = "操作未完成";

    internal MainWindow(WinUiLaunchOptions options)
    {
        this.options = options;
        InitializeComponent();
        windowSizing = new(WinRT.Interop.WindowNative.GetWindowHandle(this));
        var bounds = windowSizing.InitialBounds(); AppWindow.MoveAndResize(new(bounds.X, bounds.Y, bounds.Width, bounds.Height));
        InitializeWorkspaceKeyboard();
        var output = options.SmokeOutput;
        IUserEnvironment environment = output is null ? new WindowsUserEnvironment() : new IsolatedEnvironment();
        store = new ConfigStore(environment, output is null ? null : _ => null);
        savedConfigurationPreview = new(new NativeConfigurationPreviewRuntime(environment));
        workspaceDirectory = new(output is null ? new NativeWorkspaceDirectoryRuntime() : new SmokeWorkspaceDirectoryRuntime(this));
        viewModel = new(store, options.Home,
            output is null ? null : Path.Combine(output, "connection-preferences"),
            output is null ? null : Path.Combine(output, "speed-preferences"),
            output is null ? null : Path.Combine(output, "automatic-preferences"));
        desktopEnhancements = new(store, viewModel.EnhancementPreferences, () => viewModel.Home, () => closing);
        applicationRuntime = output is null ? new NativeDesktopApplyRuntime(desktopEnhancements, Dispatch) : new SmokeDesktopApplyRuntime(viewModel.EnhancementPreferences, store, options.Home);
        var operations = new WorkspaceApplyCoordinator(viewModel.Session);
        desktopApplication = new(store, operations, viewModel.EnhancementPreferences, applicationRuntime);
        configurationSave = new(store, operations);
        configurationRecovery = new(store, operations);
        profileManagement = new(new ProfileStore(), operations);
        startupRegistration = output is null ? new WindowsStartupRegistration() : new MemoryStartupRegistration();
        startupSettings = new(startupRegistration, Environment.ProcessPath!);
        templateLibrary = new(new TemplateRepository(output is null ? TemplateRepository.DefaultDirectory : Path.Combine(output, "template-library")));
        officialLoginReader = new(store);
        modelDiscovery = output is null ? new(modelCatalogClient) : new((_, _) => Task.FromResult(new ModelCatalogResult(["preview-model", "preview-model-two"], 1)));
        gatewayRuntime = output is null ? new NativeGatewayManagementRuntime(store) : new SmokeGatewayRuntime();
        gatewayManagement = new(gatewayRuntime, operations, output is null ? null : Path.Combine(output, "gateway-preferences"));
        mobileRuntime = output is null ? new NativeMobileConnectionRuntime() : new SmokeMobileRuntime();
        mobileConnection = new(mobileRuntime, operations);
        updateDownloads = output is null ? new() : new(new SmokeUpdateRuntime(output));
        updateInstallation = new(operations, gatewayRuntime, new WinUiUpdateInstallationRuntime(this));
        mobileConnection.Changed += (_, _) => { if (!closing) RefreshView(); };
        mobileDraft.PropertyChanged += MobileDraftChanged;
        gatewayManagement.Changed += (_, _) => { if (!closing) RefreshView(); };
        connectionDraft = new(store);
        connectionDraft.PropertyChanged += ConnectionDraftChanged;
        InitializeModelBehavior();
        InitializeConfigurationCatalog();
        InitializeHelpTopics();
        automaticSourceExecutable = output is null ? CodexExecutableLocator.Find() ?? "" : null;
        SourceExecutableInput.Text = automaticSourceExecutable ?? Path.Combine(output!, "fake-codex.exe");
        SourceProjectInput.Text = output is null ? Environment.CurrentDirectory : output;
        profileDraft.PropertyChanged += (_, args) => { if (initialized && !loadingProfile && args.PropertyName == nameof(ProfileDraftViewModel.Revision)) RefreshView(); };
        automaticEnhancements = new(store, viewModel.EnhancementPreferences, output is null
            ? new NativeAutomaticEnhancementRuntime(desktopEnhancements) : new SmokeAutomaticRuntime(this));
        automaticEnhancements.Changed += (_, _) => { automaticStatus = automaticEnhancements.Status; if (!closing) RefreshView(); };
        viewModel.PropertyChanged += ViewModelChanged;
        Root.SizeChanged += (_, _) => { if (initialized && !closing) RefreshView(); };
        NavigateTo(WorkspaceNav);
        AppWindow.Closing += WindowClosing;
        Closed += (_, _) => { closing = true; windowSizing.Dispose(); runtimeTimer.Stop(); automaticEnhancements.Cancel(); lifetime.Cancel(); savedPreviewCancellation?.Cancel(); diagnosticCancellation?.Cancel(); updateCancellation?.Cancel(); modelDiscoveryCancellation?.Cancel(); connectionDraft.Dispose(); modelCatalogClient.Dispose(); tray?.Dispose(); tray = null; viewModel.PropertyChanged -= ViewModelChanged; };
    }

    private async void RootLoaded(object sender, RoutedEventArgs e)
    {
        if (initialized) return;
        initialized = true;
        LoadWorkspace(options.Home);
        RefreshView();
        WorkspaceSectionNav.Focus(FocusState.Programmatic);
        if (options.SmokeOutput is null)
        {
            InitializeTray();
            if (options.StartInTray || options.EnhancedLaunch) TryHideToTray();
        }
        StartRuntimeObservation();
        if (options.SmokeOutput is { } output)
        {
            try
            {
                var filter = Environment.GetEnvironmentVariable("CODEX_DOCK_WINUI_TEST_FILTER");
                if (string.IsNullOrEmpty(filter)) await RunSmokeAsync(output);
                else if (filter == "workspace-usability")
                {
                    var checks = new List<string>(); await RunWorkspaceUsabilitySmokeAsync(output, checks);
                    await WriteSmokeReportAsync(output, checks);
                }
                else if (filter == "ui-consolidation")
                {
                    var checks = new List<string>(); await RunWorkspaceConsolidationSmokeAsync(output, checks);
                    await WriteSmokeReportAsync(output, checks);
                }
                else if (filter == "task-flow")
                {
                    var checks = new List<string>(); await RunWorkspaceTaskFlowSmokeAsync(output, checks);
                    await WriteSmokeReportAsync(output, checks);
                }
                else if (filter == "quick-model")
                {
                    var checks = new List<string>(); await RunQuickModelSmokeAsync(output, checks);
                    await WriteSmokeReportAsync(output, checks);
                }
                else if (filter == "connection-setup")
                {
                    var checks = new List<string>(); await RunWorkspaceReadinessSmokeAsync(output, checks);
                    await WriteSmokeReportAsync(output, checks);
                }
                else if (filter == "review-fixes")
                {
                    var checks = new List<string>(); await RunWorkspaceReviewFixesSmokeAsync(output, checks);
                    await WriteSmokeReportAsync(output, checks);
                }
                else if (filter == "upstream-reuse")
                {
                    var checks = new List<string>(); await RunUpstreamReuseSmokeAsync(output, checks);
                    await WriteSmokeReportAsync(output, checks);
                }
                else if (filter == "native-acceptance") await RunNativeAcceptanceHostAsync(output);
                else if (filter == "installer-host")
                {
                    InitializeTray();
                    if (!TryHideToTray()) throw new InvalidOperationException("The installer fixture must run in the tray.");
                    File.WriteAllText(Path.Combine(output, "installer-host.ready"), options.Home);
                    return;
                }
                else if (filter == "environment-native")
                {
                    var checks = new List<string>(); await RunNativeEnvironmentSmokeAsync(output, checks);
                    await WriteSmokeReportAsync(output, checks, "Connected monitors with actual native DPI and work areas; isolated window movement; clipboard eligibility explicitly reported and non-fixture contents skipped; requested opening of the isolated fixture folder; no real model or desktop actions");
                }
                else throw new ArgumentException("Unknown CODEX_DOCK_WINUI_TEST_FILTER.");
                App.FinishSmoke(output);
            }
            catch (Exception error) { App.FinishSmoke(output, error); }
        }
        else
        {
            await ObserveDesktopTargetsAsync();
            await StartGatewaysOnLaunchAsync();
            await RefreshGatewaysAsync();
            if (pendingEnhancedHome is not null || options.EnhancedLaunch)
            {
                var requestedHome = pendingEnhancedHome ?? options.Home; pendingEnhancedHome = null;
                await RequestEnhancedLaunchAsync(requestedHome);
            }
            else await TryAutomaticEnhancementsAsync();
        }
    }

    private void ViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => RefreshView();
    private async void ReloadClicked(object sender, RoutedEventArgs e) => await ReloadWorkspaceAsync();

    private async void ChooseHomeClicked(object sender, RoutedEventArgs e)
    {
        if (ApplicationBusy || dialogOpen || closing) return;
        WorkspaceDirectoryButton.Flyout.Hide();
        dialogOpen = true; RefreshView();
        try
        {
            if (nativeAcceptanceInteractive) nativePickerOpens++;
            var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var folder = await picker.PickSingleFolderAsync();
            if (nativeAcceptanceInteractive && folder is null) nativePickerCancels++;
            dialogOpen = false;
            if (folder is not null)
            {
                if (options.SmokeOutput is { } testRoot && !Path.GetFullPath(folder.Path).StartsWith(Path.GetFullPath(testRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("隔离验收只能选择本次测试目录内的文件夹。");
                if (await SwitchHomeAsync(folder.Path) && nativeAcceptanceInteractive) nativePickerSelections++;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            SetShellError("未能选择配置目录", error.Message);
        }
        finally { dialogOpen = false; RefreshView(); }
    }

    private async Task<bool> SwitchHomeAsync(string home)
    {
        if (ManualApplicationBusy || dialogOpen || closing || !await ResolveProfileDraftAsync() || !await ResolveTemplateEditAsync() || !await ResolveConnectionDraftAsync() || !await ResolveMobileDraftAsync()) return false;
        await automaticEnhancements.CancelAndWaitAsync();
        await applicationRuntime.StopEnhancementsAsync();
        desktopApplication.Reset(); lastApplication = null;
        configurationResult = null;
        shellError = "";
        automaticEnhancements.ResetRetry(); automaticStatus = "已开启。等待核对上次接入的实例。";
        LoadWorkspace(home); RefreshView(); return true;
    }

    private void OpenEnhancementsClicked(object sender, RoutedEventArgs e) => NavigateTo(EnhancementsNav);
    private void ConnectionRecoveryChanged(object sender, RoutedEventArgs e)
    { if (!updatingControls && initialized && !ManualApplicationBusy && !dialogOpen && !closing && viewModel.SetConnectionRecovery(ConnectionRecoveryToggle.IsOn)) PreferenceSaved(); }
    private void SpeedChanged(object sender, RoutedEventArgs e)
    { if (!updatingControls && initialized && !ManualApplicationBusy && !dialogOpen && !closing && viewModel.SetSpeedCompatibility(SpeedToggle.IsOn)) PreferenceSaved(); }
    private async void AutomaticChanged(object sender, RoutedEventArgs e)
    {
        if (updatingControls || !initialized || ManualApplicationBusy || dialogOpen || closing || !viewModel.SetAutomaticReconnect(AutomaticToggle.IsOn)) return;
        PreferenceSaved(); await TryAutomaticEnhancementsAsync();
    }

    private void PreferenceSaved()
    { shellError = ""; automaticEnhancements.Cancel(); automaticEnhancements.ResetRetry(); desktopApplication.ClearEnhancementRetry(); lastApplication = null; RefreshView(); }

    private void SetShellError(string title, string message)
    { shellErrorTitle = title; shellError = message; RefreshView(); }

    private void RefreshView()
    {
        updatingControls = true;
        try
        {
            if (automaticStatusHome != viewModel.Home || automaticStatusEnabled != viewModel.AutomaticReconnectEnabled)
            {
                automaticStatusHome = viewModel.Home; automaticStatusEnabled = viewModel.AutomaticReconnectEnabled;
                automaticStatus = "已开启。成功应用一次后，Dock 会记住本次实例并自动接回。";
            }
            HomeText.Text = viewModel.Home;
            WorkspaceDirectoryLabel.Text = "配置目录：" + (Path.GetFileName(Path.TrimEndingDirectorySeparator(viewModel.Home)) is { Length: > 0 } name ? name : viewModel.Home);
            StatusText.Text = viewModel.NeedsConnectionSetup ? viewModel.SetupGuide.Message : viewModel.Status;
            Progress.Visibility = ApplicationBusy ? Visibility.Visible : Visibility.Collapsed;
            ErrorBar.Title = viewModel.Error.Length > 0 ? "操作未完成" : shellErrorTitle;
            ErrorBar.Message = viewModel.Error.Length > 0 ? viewModel.Error : shellError;
            ErrorBar.IsOpen = ErrorBar.Message.Length > 0;
            ErrorBar.Visibility = ErrorBar.IsOpen ? Visibility.Visible : Visibility.Collapsed;
            ConnectionText.Text = viewModel.ConnectionLabel; EndpointText.Text = viewModel.ServiceOrigin; ModelText.Text = "默认模型 " + viewModel.ModelLabel;
            ConnectionRecoveryToggle.IsOn = viewModel.ConnectionRecoveryEnabled;
            SpeedToggle.IsOn = viewModel.SpeedCompatibilityEnabled; AutomaticToggle.IsOn = viewModel.AutomaticReconnectEnabled;
            ConnectionRecoveryToggle.IsEnabled = SpeedToggle.IsEnabled = AutomaticToggle.IsEnabled = viewModel.PreferencesAvailable && !ManualApplicationBusy && !dialogOpen && !closing;
            EnhancementSummary.Text = viewModel.EnhancementSummary; EnhancementStatus.Text = viewModel.EnhancementStatus;
            PendingBar.IsOpen = viewModel.PreferencesChanged;
            AutomaticStatus.Text = !viewModel.AutomaticReconnectEnabled ? "已关闭。仍可手动应用客户端增强。" : automaticStatus;
            CreateEnhancedShortcutButton.IsEnabled = viewModel.PreferencesAvailable && viewModel.AutomaticReconnectEnabled &&
                (viewModel.ConnectionRecoveryEnabled || viewModel.SpeedCompatibilityEnabled) && !ApplicationBusy && !dialogOpen && !closing;
            BackgroundStatus.Text = backgroundStatus;
            RefreshConnectionControls();
            RefreshWorkspaceSetupControls();
            RefreshWorkspaceInformationControls();
            RefreshWindowActionControls();
            RefreshModelBehaviorControls();
            RefreshConfigurationCatalogControls();
            RefreshRecoveryControls();
            RefreshTemplateControls();
            RefreshStartupControls(); RefreshSourceControls(); RefreshProfileControls();
            RefreshFeatureControls();
            RefreshDiagnosticControls(); RefreshUpdateControls();
            RefreshGatewayControls();
            RefreshMobileControls();
            RefreshApplicationActions();
            RefreshConsolidatedShell();
        }
        finally { updatingControls = false; }
    }

    private sealed class IsolatedEnvironment : IUserEnvironment
    { public string? Read() => null; public void Write(string? value) => throw new InvalidOperationException("预览测试不能写入用户环境。"); }
}
