using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CodexConfig.Application;

public enum WorkspacePhase
{
    Empty,
    Loading,
    Ready,
    Draft,
    Applying,
    Applied,
    WaitingForUser,
    NeedsRestart,
    Failed
}

public enum WorkspaceOperation
{
    None,
    LoadingConfiguration,
    SavingConfiguration,
    RecoveringConfiguration,
    SavingProfile,
    ApplyingDesktop,
    AttachingEnhancements,
    ReconnectingEnhancements,
    StartingGateway,
    StoppingGateway,
    ManagingMobileConnection,
    InstallingUpdate
}

public sealed record WorkspaceOperationState(
    WorkspaceOperation Operation = WorkspaceOperation.None,
    string Stage = "",
    string Message = "",
    bool IsBusy = false,
    bool IsError = false);

public sealed record WorkspaceState(
    string Home,
    CodexConfig.Core.ConfigSnapshot? Snapshot,
    WorkspacePhase Phase,
    WorkspaceOperationState Operation,
    CodexConfig.Core.CodexRuntimeStatus? Runtime,
    bool HasDraft,
    bool ConnectionRecoveryEnabled,
    bool SpeedCompatibilityEnabled,
    bool AutomaticEnhancementsEnabled,
    string? LastError,
    DateTimeOffset? LastAppliedAt)
{
    public static WorkspaceState Empty(string home) => new(
        home, null, WorkspacePhase.Empty, new(), null, false, false, false, false, null, null);
}

// UI-independent session state shared by the current WPF shell and the future WinUI shell.
// It deliberately does not read files or call Windows APIs; those remain application services.
public sealed class WorkspaceSession : INotifyPropertyChanged
{
    private WorkspaceState state;
    public WorkspaceState State => state;
    public event PropertyChangedEventHandler? PropertyChanged;

    public WorkspaceSession(string home) => state = WorkspaceState.Empty(home);

    public void BeginLoad(string home)
        => Update(state with { Home = home, Snapshot = null, Runtime = null, HasDraft = false, LastAppliedAt = null,
            Phase = WorkspacePhase.Loading, Operation = new(WorkspaceOperation.LoadingConfiguration, "读取配置", "正在读取配置目录…", true), LastError = null });

    public void Loaded(string home, CodexConfig.Core.ConfigSnapshot snapshot)
        => Update(state with { Home = home, Snapshot = snapshot, Phase = WorkspacePhase.Ready, HasDraft = false, Operation = new(), LastError = null });

    public void MarkDraft(bool hasDraft)
        => Update(state with { HasDraft = hasDraft, Phase = hasDraft ? WorkspacePhase.Draft : state.Phase is WorkspacePhase.Applied ? WorkspacePhase.Applied : WorkspacePhase.Ready });

    public void BeginOperation(WorkspaceOperation operation, string stage, string message)
        => Update(state with { Phase = WorkspacePhase.Applying, Operation = new(operation, stage, message, true), LastError = null });

    public void CompleteOperation(string stage, string message, bool needsRestart = false)
        => Update(state with { Phase = needsRestart ? WorkspacePhase.NeedsRestart : WorkspacePhase.Applied,
            Operation = new(WorkspaceOperation.None, stage, message, false), LastAppliedAt = DateTimeOffset.Now, LastError = null });

    public void WaitForUser(string message)
        => Update(state with { Phase = WorkspacePhase.WaitingForUser, Operation = new(WorkspaceOperation.None, "等待操作", message, false), LastError = null });

    public void Fail(string message, WorkspaceOperation operation = WorkspaceOperation.None)
        => Update(state with { Phase = WorkspacePhase.Failed, Operation = new(operation, "未完成", message, false, true), LastError = message });

    public void SetRuntime(CodexConfig.Core.CodexRuntimeStatus? runtime)
        => Update(state with { Runtime = runtime });

    public void AdoptSnapshot(CodexConfig.Core.ConfigSnapshot snapshot)
    {
        if (!string.Equals(snapshot.Home, state.Home, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("工作区目录已改变。");
        Update(state with { Snapshot = snapshot });
    }

    public void ConfigurationSaved(CodexConfig.Core.ConfigSnapshot snapshot)
    {
        if (!string.Equals(snapshot.Home, state.Home, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("工作区目录已改变。");
        Update(state with { Snapshot = snapshot, HasDraft = false });
    }

    public void SetEnhancements(bool connectionRecovery, bool speedCompatibility, bool automatic)
        => Update(state with { ConnectionRecoveryEnabled = connectionRecovery, SpeedCompatibilityEnabled = speedCompatibility, AutomaticEnhancementsEnabled = automatic });

    public void SetHome(string home)
        => Update(state with { Home = home, Snapshot = null, Phase = WorkspacePhase.Empty, HasDraft = false, Operation = new(), LastError = null });

    private void Update(WorkspaceState next)
    {
        if (next == state) return;
        state = next;
        PropertyChanged?.Invoke(this, new(nameof(State)));
    }
}

public class WorkspaceObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; PropertyChanged?.Invoke(this, new(name)); return true;
    }
    protected void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
