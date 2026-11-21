using CodexConfig.Application;
using CodexConfig.Core;
using Microsoft.UI.Dispatching;
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
    private bool viewRefreshQueued, viewRefreshDirty;
    private string? automaticStatusHome;
    private bool automaticStatusEnabled;
    private string shellError = "", shellErrorTitle = "操作未完成";

    internal MainWindow(WinUiLaunchOptions options)
    {
        this.options = options;
        InitializeComponent();
        ConnectionPanel.Host = this;
        WorkspacePanel.Host = this;
        ModelsPanel.Host = this;
        AdvancedPanel.Host = this;
        CatalogPanel.Host = this;
        SourcesPanel.Host = this;
        ProfilesPanel.Host = this;
        SettingsPanel.Host = this;
        TemplatesPanel.Host = this;
        RecoveryPanel.Host = this;
        EnhancementsPanel.Host = this;
        GatewaysPanel.Host = this;
        MobilePanel.Host = this;
        HelpPanel.DestinationRequested += NavigateFromHelp;
        UpdatesPanel.Host = this;
        windowSizing = new(WinRT.Interop.WindowNative.GetWindowHandle(this));
        var bounds = windowSizing.InitialBounds(); AppWindow.MoveAndResize(new(bounds.X, bounds.Y, bounds.Width, bounds.Height));
        InitializeWorkspaceKeyboard();
        var output = options.IsolatedDataDirectory;
        IUserEnvironment environment = new WindowsUserEnvironment();
#if CODEX_DOCK_WINUI_SMOKE
        if (options.SmokeOutput is not null) environment = new IsolatedEnvironment();
#endif
        store = new ConfigStore(environment, output is null ? null : _ => null);
#if CODEX_DOCK_WINUI_SMOKE
        if (options.SmokeOutput is not null) workspaceDirectory = new(new SmokeWorkspaceDirectoryRuntime(this));
        else
#endif
        workspaceDirectory = new(new NativeWorkspaceDirectoryRuntime());
        viewModel = new(store, options.Home,
            output is null ? null : Path.Combine(output, "connection-preferences"),
            output is null ? null : Path.Combine(output, "speed-preferences"),
            output is null ? null : Path.Combine(output, "automatic-preferences"));
        var pageOperations = new WorkspacePageOperations(() => viewModel.Home,
            () => !ManualApplicationBusy && !dialogOpen && !closing, () => !ApplicationBusy && !dialogOpen && !closing,
            () => closing, () => initialized, lifetime.Token, busy, RefreshView);
        FilePreviewPanel.Connect(pageOperations, new(new NativeConfigurationPreviewRuntime(environment)), CopyWorkspaceText, ClearWorkspaceFileMessage, PageHelpClicked);
        DiagnosticsPanel.Connect(pageOperations, OpenSourcesClicked);
        desktopEnhancements = new(store, viewModel.EnhancementPreferences, () => viewModel.Home, () => closing);
#if CODEX_DOCK_WINUI_SMOKE
        if (options.SmokeOutput is not null) applicationRuntime = new SmokeDesktopApplyRuntime(viewModel.EnhancementPreferences, store, options.Home);
        else
#endif
        applicationRuntime = new NativeDesktopApplyRuntime(desktopEnhancements, Dispatch);
        var operations = new WorkspaceApplyCoordinator(viewModel.Session);
        desktopApplication = new(store, operations, viewModel.EnhancementPreferences, applicationRuntime);
        configurationSave = new(store, operations);
        configurationRecovery = new(store, operations);
        profileManagement = new(new ProfileStore(), operations);
#if CODEX_DOCK_WINUI_SMOKE
        if (options.SmokeOutput is not null) startupRegistration = new MemoryStartupRegistration();
        else
#endif
        startupRegistration = new WindowsStartupRegistration();
        startupSettings = new(startupRegistration, Environment.ProcessPath!);
        templateLibrary = new(new TemplateRepository(output is null ? TemplateRepository.DefaultDirectory : Path.Combine(output, "template-library")));
        officialLoginReader = new(store);
#if CODEX_DOCK_WINUI_SMOKE
        if (options.SmokeOutput is not null) modelDiscovery = new((_, _) => Task.FromResult(new ModelCatalogResult(["preview-model", "preview-model-two"], 1)));
        else
#endif
        modelDiscovery = new(modelCatalogClient);
#if CODEX_DOCK_WINUI_SMOKE
        if (options.SmokeOutput is not null) gatewayRuntime = new SmokeGatewayRuntime();
        else
#endif
        gatewayRuntime = new NativeGatewayManagementRuntime(store);
        gatewayManagement = new(gatewayRuntime, operations, output is null ? null : Path.Combine(output, "gateway-preferences"));
#if CODEX_DOCK_WINUI_SMOKE
        if (options.SmokeOutput is not null) mobileRuntime = new SmokeMobileRuntime();
        else
#endif
        mobileRuntime = new NativeMobileConnectionRuntime();
        mobileConnection = new(mobileRuntime, operations);
#if CODEX_DOCK_WINUI_SMOKE
        if (options.SmokeOutput is { } smokeOutput) updateDownloads = new(new SmokeUpdateRuntime(smokeOutput));
        else
#endif
        updateDownloads = new();
        updateInstallation = new(operations, gatewayRuntime, new WinUiUpdateInstallationRuntime(this));
        mobileConnection.Changed += (_, _) => QueueViewRefresh();
        mobileDraft.PropertyChanged += MobileDraftChanged;
        gatewayManagement.Changed += (_, _) => QueueViewRefresh();
        connectionDraft = new(store);
        connectionDraft.PropertyChanged += ConnectionDraftChanged;
        InitializeModelBehavior();
        InitializeConfigurationCatalog();
        automaticSourceExecutable = output is null ? CodexExecutableLocator.Find() ?? "" : "";
        SourcesPanel.SourceExecutableInput.Text = automaticSourceExecutable;
#if CODEX_DOCK_WINUI_SMOKE
        if (options.SmokeOutput is { } sourceFixture)
        {
            automaticSourceExecutable = null;
            SourcesPanel.SourceExecutableInput.Text = Path.Combine(sourceFixture, "fake-codex.exe");
        }
#endif
        SourcesPanel.SourceProjectInput.Text = output is null ? Environment.CurrentDirectory : output;
        profileDraft.PropertyChanged += (_, args) => { if (initialized && !loadingProfile && args.PropertyName == nameof(ProfileDraftViewModel.Revision)) RefreshView(); };
#if CODEX_DOCK_WINUI_SMOKE
        if (options.SmokeOutput is not null) automaticEnhancements = new(store, viewModel.EnhancementPreferences, new SmokeAutomaticRuntime(this));
        else
#endif
        automaticEnhancements = new(store, viewModel.EnhancementPreferences, new NativeAutomaticEnhancementRuntime(desktopEnhancements));
        automaticEnhancements.Changed += (_, _) => { automaticStatus = automaticEnhancements.Status; QueueViewRefresh(); };
        viewModel.PropertyChanged += ViewModelChanged;
        Root.SizeChanged += ShellSizeChanged;
        NavigateTo(WorkspaceNav);
        AppWindow.Closing += WindowClosing;
        Closed += (_, _) => { closing = true; windowSizing.Dispose(); runtimeTimer.Stop(); automaticEnhancements.Cancel(); lifetime.Cancel(); updateCancellation?.Cancel(); modelDiscoveryCancellation?.Cancel(); connectionDraft.Dispose(); modelCatalogClient.Dispose(); tray?.Dispose(); tray = null; viewModel.PropertyChanged -= ViewModelChanged; };
    }

    private async void RootLoaded(object sender, RoutedEventArgs e)
    {
        if (initialized) return;
        initialized = true;
#if CODEX_DOCK_WINUI_SMOKE
        if (options.InteractivePreview) backgroundStatus = "当前窗口使用临时演示配置。关闭窗口即可退出。";
#endif
        LoadWorkspace(options.Home);
        RefreshView();
        WorkspaceSectionNav.Focus(FocusState.Programmatic);
#if CODEX_DOCK_WINUI_SMOKE
        if (options.InteractivePreview) { Title = "Codex Dock · 界面预览"; return; }
        if (options.SmokeOutput is { } output)
        {
            await RunSmokeHostAsync(output);
            return;
        }
#endif
        InitializeTray();
        if (options.VerifyStartup)
        {
            if (viewModel.Snapshot is null || ErrorBar.IsOpen || !TryHideToTray())
                throw new InvalidOperationException("WinUI could not load the verification workspace and initialize the tray.");
            var handle = WinRT.Interop.WindowNative.GetWindowHandle(this).ToInt64();
            var receipt = Path.Combine(options.Home, "startup.ready.pending");
            File.WriteAllText(receipt, handle.ToString(System.Globalization.CultureInfo.InvariantCulture));
            File.Move(receipt, Path.Combine(options.Home, "startup.ready"));
            return;
        }
        if (options.StartInTray || options.EnhancedLaunch) TryHideToTray();
        StartRuntimeObservation();
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

    private void ViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => QueueViewRefresh();

    private void QueueViewRefresh()
    {
        if (!initialized || closing) return;
        viewRefreshDirty = true;
        if (viewRefreshQueued) return;
        viewRefreshQueued = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            viewRefreshQueued = false;
            if (viewRefreshDirty && !closing) RefreshView();
        });
    }

    internal async void ReloadClicked(object sender, RoutedEventArgs e) => await ReloadWorkspaceAsync();

    private async void ChooseHomeClicked(object sender, RoutedEventArgs e)
    {
        if (ApplicationBusy || dialogOpen || closing) return;
        WorkspaceDirectoryButton.Flyout.Hide();
        dialogOpen = true; RefreshView();
        try
        {
#if CODEX_DOCK_WINUI_SMOKE
            if (nativeAcceptanceInteractive) nativePickerOpens++;
#endif
            var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var folder = await picker.PickSingleFolderAsync();
#if CODEX_DOCK_WINUI_SMOKE
            if (nativeAcceptanceInteractive && folder is null) nativePickerCancels++;
#endif
            dialogOpen = false;
            if (folder is not null)
            {
#if CODEX_DOCK_WINUI_SMOKE
                if (options.SmokeOutput is { } testRoot && !Path.GetFullPath(folder.Path).StartsWith(Path.GetFullPath(testRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("隔离验收只能选择本次测试目录内的文件夹。");
#endif
#if CODEX_DOCK_WINUI_SMOKE
                if (await SwitchHomeAsync(folder.Path) && nativeAcceptanceInteractive) nativePickerSelections++;
#else
                await SwitchHomeAsync(folder.Path);
#endif
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

    internal void OpenEnhancementsClicked(object sender, RoutedEventArgs e) => NavigateTo(EnhancementsNav);
    private async void ErrorReloadClicked(object sender, RoutedEventArgs e)
    {
        if (ApplicationBusy || dialogOpen || closing) return;
        if (viewModel.ConfigurationError is not null || viewModel.PreferenceError is null)
        {
            await ReloadWorkspaceAsync();
            return;
        }
        viewModel.ReloadPreferences();
        RefreshView();
    }

    internal void ConnectionRecoveryChanged(object sender, RoutedEventArgs e)
    { if (!updatingControls && initialized && !ManualApplicationBusy && !dialogOpen && !closing && viewModel.SetConnectionRecovery(EnhancementsPanel.ConnectionRecoveryToggle.IsOn)) PreferenceSaved(); }
    internal void SpeedChanged(object sender, RoutedEventArgs e)
    { if (!updatingControls && initialized && !ManualApplicationBusy && !dialogOpen && !closing && viewModel.SetSpeedCompatibility(EnhancementsPanel.SpeedToggle.IsOn)) PreferenceSaved(); }
    internal async void AutomaticChanged(object sender, RoutedEventArgs e)
    {
        if (updatingControls || !initialized || ManualApplicationBusy || dialogOpen || closing || !viewModel.SetAutomaticReconnect(EnhancementsPanel.AutomaticToggle.IsOn)) return;
        PreferenceSaved(); await TryAutomaticEnhancementsAsync();
    }

    private void PreferenceSaved()
    { shellError = ""; automaticEnhancements.Cancel(); automaticEnhancements.ResetRetry(); desktopApplication.ClearEnhancementRetry(); lastApplication = null; RefreshView(); }

    private void SetShellError(string title, string message)
    { shellErrorTitle = title; shellError = message; RefreshView(); }

    private void RefreshView()
    {
        viewRefreshDirty = false;
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
            Progress.Visibility = ApplicationBusy ? Visibility.Visible : Visibility.Collapsed;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(Progress, CurrentOperationName ?? "操作进度");
            ErrorBar.Title = viewModel.ConfigurationError is not null ? "配置读取未完成" :
                viewModel.PreferenceError is not null ? "增强偏好需要处理" : shellErrorTitle;
            ErrorBar.Message = viewModel.Error.Length > 0 ? viewModel.Error : shellError;
            ErrorBar.IsOpen = ErrorBar.Message.Length > 0;
            ErrorBar.Visibility = ErrorBar.IsOpen ? Visibility.Visible : Visibility.Collapsed;
            ErrorReloadButton.Content = viewModel.ConfigurationError is null && viewModel.PreferenceError is not null ? "重新读取增强偏好" : "重新读取";
            ErrorReloadButton.IsEnabled = !ApplicationBusy && !dialogOpen && !closing;
            PendingRecoveryBar.IsOpen = HasPendingRecovery;
            PendingRecoveryBar.Visibility = HasPendingRecovery ? Visibility.Visible : Visibility.Collapsed;
            RefreshWorkspaceSetupControls();
            RefreshWorkspaceInformationControls();
            RefreshConfigurationFooter();
            RefreshApplicationActions();
            RefreshConsolidatedShell();
            RefreshActivePage();
        }
        finally { updatingControls = false; }
    }

    private void RefreshActivePage()
    {
        if (IsPhonePage)
        {
            RefreshMobileControls();
            if (currentPage == MobileSettingsNav) RefreshGatewayControls();
            return;
        }

        switch (currentPage.Tag)
        {
            case "workspace": RefreshWorkspaceHomeControls(); break;
            case "connection": RefreshConnectionControls(); break;
            case "models": RefreshModelBehaviorControls(); break;
            case "catalog": RefreshConfigurationCatalogControls(); break;
            case "templates": RefreshTemplateControls(); break;
            case "profiles": RefreshProfileControls(); break;
            case "enhancements": RefreshEnhancementControls(); break;
            case "gateways": RefreshGatewayControls(); break;
            case "sources": RefreshSourceControls(); RefreshFeatureControls(); break;
            case "diagnostics": DiagnosticsPanel.Refresh(); break;
            case "recovery": RefreshRecoveryControls(); break;
            case "settings": RefreshStartupControls(); RefreshWindowActionControls(); break;
            case "updates": RefreshUpdateControls(); break;
        }
    }

    private void RefreshEnhancementControls()
    {
        EnhancementsPanel.ConnectionRecoveryToggle.IsOn = viewModel.ConnectionRecoveryEnabled;
        EnhancementsPanel.SpeedToggle.IsOn = viewModel.SpeedCompatibilityEnabled; EnhancementsPanel.AutomaticToggle.IsOn = viewModel.AutomaticReconnectEnabled;
        EnhancementsPanel.ConnectionRecoveryToggle.IsEnabled = EnhancementsPanel.SpeedToggle.IsEnabled = EnhancementsPanel.AutomaticToggle.IsEnabled =
            viewModel.PreferencesAvailable && !ManualApplicationBusy && !dialogOpen && !closing;
        EnhancementsPanel.EnhancementStatus.Text = viewModel.EnhancementStatus;
        EnhancementsPanel.PendingBar.IsOpen = viewModel.PreferencesChanged;
        EnhancementsPanel.AutomaticStatus.Text = !viewModel.AutomaticReconnectEnabled ? "已关闭。仍可手动应用客户端增强。" : automaticStatus;
        EnhancementsPanel.CreateEnhancedShortcutButton.IsEnabled = viewModel.PreferencesAvailable && viewModel.AutomaticReconnectEnabled &&
            (viewModel.ConnectionRecoveryEnabled || viewModel.SpeedCompatibilityEnabled) && !ApplicationBusy && !dialogOpen && !closing;
        EnhancementsPanel.BackgroundStatus.Text = backgroundStatus;
    }

#if CODEX_DOCK_WINUI_SMOKE
    private sealed class IsolatedEnvironment : IUserEnvironment
    { public string? Read() => null; public void Write(string? value) => throw new InvalidOperationException("预览测试不能写入用户环境。"); }
#endif
}
