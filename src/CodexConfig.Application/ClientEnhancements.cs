using CodexConfig.Core;

namespace CodexConfig.Application;

public sealed record ClientEnhancementPreferences(
    bool ConnectionRecoveryEnabled,
    bool SpeedCompatibilityEnabled,
    bool AutomaticReconnectEnabled,
    bool HasRememberedInstance);

// The three enhancement preferences share one application boundary while retaining
// their existing per-home storage files. UI shells should call this service instead
// of constructing Core preference stores independently.
public sealed class ClientEnhancementPreferencesService
{
    private string home;
    private readonly string? connectionRoot, speedRoot, automaticRoot;
    public DesktopConnectionSettings ConnectionSettings => new(home, connectionRoot);
    public DesktopSpeedSettings SpeedSettings => new(home, speedRoot);
    public DesktopEnhancementSettings AutomaticSettings => new(home, automaticRoot);

    public ClientEnhancementPreferencesService(string home, string? connectionRoot = null, string? speedRoot = null, string? automaticRoot = null)
    { this.home = Path.GetFullPath(home); this.connectionRoot = connectionRoot; this.speedRoot = speedRoot; this.automaticRoot = automaticRoot; }

    public string Home => home;
    public void SetHome(string value) => home = Path.GetFullPath(value);
    public ClientEnhancementPreferences Load()
    {
        var automatic = AutomaticSettings.Load();
        return new(ConnectionSettings.Load(), SpeedSettings.Load(), automatic.Enabled, automatic.Binding is not null);
    }
    public void SetConnectionRecovery(bool enabled) => ConnectionSettings.Save(enabled);
    public void SetSpeedCompatibility(bool enabled) => SpeedSettings.Save(enabled);
    public void SetAutomaticReconnect(bool enabled) => AutomaticSettings.SetEnabled(enabled);
}

public sealed class ClientEnhancementsViewModel : WorkspaceObservableObject
{
    private readonly ClientEnhancementPreferencesService service;
    private ClientEnhancementPreferences preferences = new(false, false, false, false);
    public ClientEnhancementPreferences Preferences => preferences;
    public bool ConnectionRecoveryEnabled => preferences.ConnectionRecoveryEnabled;
    public bool SpeedCompatibilityEnabled => preferences.SpeedCompatibilityEnabled;
    public bool AutomaticReconnectEnabled => preferences.AutomaticReconnectEnabled;
    public bool HasRememberedInstance => preferences.HasRememberedInstance;
    public string Home => service.Home;

    public ClientEnhancementsViewModel(ClientEnhancementPreferencesService service) => this.service = service;
    public ClientEnhancementPreferences Reload() { preferences = service.Load(); Notify(); return preferences; }
    public void SetHome(string home) { service.SetHome(home); preferences = new(false, false, false, false); Notify(); }
    public void SetConnectionRecovery(bool enabled) { service.SetConnectionRecovery(enabled); Reload(); }
    public void SetSpeedCompatibility(bool enabled) { service.SetSpeedCompatibility(enabled); Reload(); }
    public void SetAutomaticReconnect(bool enabled) { service.SetAutomaticReconnect(enabled); Reload(); }
    public ClientEnhancementPreferencesService Storage => service;

    private void Notify()
    {
        Changed(nameof(Preferences)); Changed(nameof(ConnectionRecoveryEnabled)); Changed(nameof(SpeedCompatibilityEnabled));
        Changed(nameof(AutomaticReconnectEnabled)); Changed(nameof(HasRememberedInstance)); Changed(nameof(Home));
    }
}
