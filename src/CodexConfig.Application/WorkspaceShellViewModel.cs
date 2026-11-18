using CodexConfig.Core;

namespace CodexConfig.Application;

// Shell presentation and use cases shared without a dependency on either XAML
// framework. Loading never starts Codex, a gateway, or an enhancement session.
public sealed class WorkspaceShellViewModel : WorkspaceObservableObject
{
    private readonly WorkspaceCoordinator coordinator;
    private readonly ClientEnhancementsViewModel enhancements;
    private readonly ConfigStore store;
    private ConnectionSettings? connection;
    private bool configurationAvailable, preferencesAvailable, preferencesChanged;
    private string? configurationError, preferenceError;
    private WorkspaceSetupGuide setupGuide = WorkspaceSetupGuide.Unreadable;

    public WorkspaceSession Session => coordinator.Session;
    public ClientEnhancementPreferencesService EnhancementPreferences => enhancements.Storage;
    public ConfigSnapshot? Snapshot => Session.State.Snapshot;
    public string Home => Session.State.Home;
    public bool IsBusy => Session.State.Operation.IsBusy;
    public bool ConfigurationAvailable => configurationAvailable && !IsBusy;
    public bool PreferencesAvailable => configurationAvailable && preferencesAvailable && !IsBusy;
    public bool ConnectionRecoveryEnabled => enhancements.ConnectionRecoveryEnabled;
    public bool SpeedCompatibilityEnabled => enhancements.SpeedCompatibilityEnabled;
    public bool AutomaticReconnectEnabled => enhancements.AutomaticReconnectEnabled;
    public bool PreferencesChanged => preferencesChanged;
    public WorkspaceSetupGuide SetupGuide => setupGuide;
    public bool NeedsConnectionSetup => configurationAvailable && setupGuide.Kind == WorkspaceSetupKind.NeedsConnection;
    public bool CanApplyConfiguration => ConfigurationAvailable && !NeedsConnectionSetup;
    public string Error => configurationError ?? preferenceError ?? "";
    public string Status => !configurationAvailable ? "配置未读取，请检查后重试。" :
        preferenceError is not null ? "配置已读取，增强偏好需要处理。" :
        Session.State.HasDraft ? "本地配置已读取，修改尚未保存。" :
        Session.State.Phase == WorkspacePhase.NeedsRestart ? "本地配置已保存，等待应用到 Codex。" :
        NeedsConnectionSetup ? "尚未配置，可在连接页设置服务。" : "本地配置已读取，聊天实际连接仍需在 Codex 中核对。";
    public string ConnectionLabel => connection is null ? "尚未读取" : connection.Mode == ConnectionMode.OfficialWithApi ? "官方账号 + API 服务" :
        connection.NeedsApiKeyMigration ? "旧 PAT · 等待迁移" : connection.ApiKey.Length > 0 ? "API Key 直连" :
        OfficialCredentials.HasLogin(Session.State.Snapshot?.Auth) ? "官方账号" : setupGuide.Kind == WorkspaceSetupKind.ExistingConnection ? "客户端或供应商认证" : "待设置连接";
    // Addresses can contain query credentials even in old or manually edited
    // configuration files. Only render the service origin, never the raw URL.
    public string ServiceOrigin => connection is null ? "" : Uri.TryCreate(connection.BaseUrl, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http"
        ? uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.Unescaped) : string.IsNullOrWhiteSpace(connection.BaseUrl) && setupGuide.Kind == WorkspaceSetupKind.ExistingConnection
            ? "使用 Codex 或供应商的默认服务" : "尚未设置服务地址";
    public string ModelLabel => connection?.Model is { Length: > 0 } model ? model : "跟随 Codex";
    public string ProfileName => connection?.Details.Name is { Length: > 0 } name ? name : Path.GetFileName(Path.TrimEndingDirectorySeparator(Home));
    public string EnhancementSummary
    {
        get
        {
            if (!PreferencesAvailable) return "暂时无法读取";
            var enabled = new[] { ConnectionRecoveryEnabled, SpeedCompatibilityEnabled, AutomaticReconnectEnabled }.Count(value => value);
            return enabled == 0 ? "全部关闭" : enabled + " 项偏好已开启";
        }
    }
    public string EnhancementStatus => enhancements.HasRememberedInstance
        ? "已有增强实例记录。开启自动接回后，只恢复之前确认的实例。"
        : "尚无增强实例记录。选择需要的选项后，点击下方按钮应用。";

    public WorkspaceShellViewModel(ConfigStore store, string home, string? connectionRoot = null, string? speedRoot = null, string? automaticRoot = null)
    {
        this.store = store;
        coordinator = new(store, home);
        enhancements = new(new ClientEnhancementPreferencesService(home, connectionRoot, speedRoot, automaticRoot));
        Session.PropertyChanged += (_, _) => Notify();
    }

    public void Load(string home) => Load(home, null);
    public void Load(ConfigSnapshot source) => Load(source.Home, source);
    public void AdoptConfigurationRead(ConfigSnapshot source, bool hasDraft)
    {
        if (IsBusy || !string.Equals(Home, source.Home, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("工作区正在改变，未采用读取结果。");
        connection = store.ReadSettings(source); setupGuide = WorkspaceSetupGuide.Read(source, connection); Session.AdoptSnapshot(source); Session.MarkDraft(hasDraft); Notify();
    }
    private void Load(string home, ConfigSnapshot? source)
    {
        if (IsBusy) return;
        home = Path.GetFullPath(home);
        if (!string.Equals(Home, home, StringComparison.OrdinalIgnoreCase)) preferencesChanged = false;
        configurationAvailable = preferencesAvailable = false;
        configurationError = preferenceError = null; connection = null; setupGuide = WorkspaceSetupGuide.Unreadable;
        try
        {
            var loaded = source is null ? coordinator.Load(home) : coordinator.Load(source);
            connection = loaded.Settings; setupGuide = WorkspaceSetupGuide.Read(loaded.Snapshot, connection);
            configurationAvailable = true;
        }
        catch (Exception error) when (IsStorageError(error)) { configurationError = WorkspaceStorageErrors.Describe(error); }
        enhancements.SetHome(home);
        if (configurationAvailable)
        {
            try { enhancements.Reload(); preferencesAvailable = true; }
            catch (Exception error) when (IsStorageError(error))
            { preferenceError = "无法读取客户端增强设置。请检查设置文件内容与目录权限，修复后重新读取。"; }
        }
        SyncEnhancements(); Notify();
    }

    public void ApplicationFinished(DesktopApplyResult result)
    {
        if (!string.Equals(Home, result.Saved.Home, StringComparison.OrdinalIgnoreCase)) return;
        if (result.Outcome == DesktopApplyOutcome.Completed) preferencesChanged = false;
        if (result.DesktopOpened)
        {
            Session.AdoptSnapshot(result.Saved);
            if (result.Target is { } target) Session.SetRuntime(new(true, target.Processes.Count) { DesktopProcesses = target.Processes });
        }
        Notify();
    }

    public void ConfigurationSaved(ConfigSnapshot saved)
    {
        if (!string.Equals(Home, saved.Home, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("工作区目录已改变。");
        connection = store.ReadSettings(saved); setupGuide = WorkspaceSetupGuide.Read(saved, connection); configurationAvailable = true; configurationError = null;
        Session.AdoptSnapshot(saved); Notify();
    }

    public bool SetConnectionRecovery(bool enabled) => ChangePreference(() => enhancements.SetConnectionRecovery(enabled));
    public bool SetSpeedCompatibility(bool enabled) => ChangePreference(() => enhancements.SetSpeedCompatibility(enabled));
    public bool SetAutomaticReconnect(bool enabled) => ChangePreference(() => enhancements.SetAutomaticReconnect(enabled));

    private bool ChangePreference(Action save)
    {
        if (!PreferencesAvailable) return false;
        try
        {
            save(); preferencesChanged = true; preferenceError = null;
            SyncEnhancements(); Notify(); return true;
        }
        catch (Exception error) when (IsStorageError(error))
        {
            preferenceError = "未能确认增强偏好已保存。请检查设置文件与目录权限，再重新读取。"; preferencesAvailable = false;
            Notify(); return false;
        }
    }

    private void SyncEnhancements() => coordinator.Enhancements(preferencesAvailable && ConnectionRecoveryEnabled,
        preferencesAvailable && SpeedCompatibilityEnabled, preferencesAvailable && AutomaticReconnectEnabled);

    private static bool IsStorageError(Exception error) => WorkspaceStorageErrors.IsRecoverable(error);

    private void Notify()
    {
        foreach (var property in new[] { nameof(Home), nameof(IsBusy), nameof(ConfigurationAvailable), nameof(PreferencesAvailable), nameof(ConnectionRecoveryEnabled), nameof(SpeedCompatibilityEnabled),
            nameof(AutomaticReconnectEnabled), nameof(PreferencesChanged), nameof(Error), nameof(Status), nameof(ConnectionLabel), nameof(ServiceOrigin),
            nameof(ModelLabel), nameof(ProfileName), nameof(EnhancementSummary), nameof(EnhancementStatus), nameof(SetupGuide), nameof(NeedsConnectionSetup), nameof(CanApplyConfiguration) }) Changed(property);
    }
}
