namespace CodexConfig.Application;

public sealed record WorkspaceLoadResult(
    CodexConfig.Core.ConfigSnapshot Snapshot,
    CodexConfig.Core.ConnectionSettings Settings);

// Application use case boundary for workspace loading. File formats and Windows APIs stay in Core;
// shells receive one coherent result and one state transition.
public sealed class WorkspaceCoordinator
{
    private readonly CodexConfig.Core.ConfigStore store;
    public WorkspaceSession Session { get; }

    public WorkspaceCoordinator(CodexConfig.Core.ConfigStore store, string home)
    {
        this.store = store; Session = new WorkspaceSession(home);
    }

    public WorkspaceLoadResult Load(string home) => Load(home, null);
    public WorkspaceLoadResult Load(CodexConfig.Core.ConfigSnapshot snapshot) => Load(snapshot.Home, snapshot);
    private WorkspaceLoadResult Load(string home, CodexConfig.Core.ConfigSnapshot? source)
    {
        Session.BeginLoad(home);
        try
        {
            if (File.Exists(home)) throw new IOException("配置目录指向了文件，请选择实际目录。");
            var snapshot = source ?? store.Load(home);
            var settings = store.ReadSettings(snapshot);
            Session.Loaded(home, snapshot);
            return new(snapshot, settings);
        }
        catch (Exception error) when (WorkspaceStorageErrors.IsRecoverable(error))
        {
            Session.Fail(WorkspaceStorageErrors.Describe(error), WorkspaceOperation.LoadingConfiguration);
            throw;
        }
    }

    public void MarkDraft(bool hasDraft) => Session.MarkDraft(hasDraft);
    public void Runtime(CodexConfig.Core.CodexRuntimeStatus? runtime) => Session.SetRuntime(runtime);
    public void Enhancements(bool connectionRecovery, bool speedCompatibility, bool automatic)
        => Session.SetEnhancements(connectionRecovery, speedCompatibility, automatic);
}
