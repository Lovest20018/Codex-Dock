namespace CodexConfig.WinUI;

internal enum WindowOperation
{
    ApplyDesktop,
    OpenDesktop,
    ReviewConnection,
    SaveConfiguration,
    RestoreConfiguration,
    SaveProfile,
    RestoreClipboard,
    ReadSavedPreview,
    ReadSource,
    ReadFeatures,
    ReadDiagnostics,
    InstallUpdate,
    ReadOfficialLogin,
    DiscoverModels,
    ReadQuickModels,
    MobileConnection,
    OperateGateway,
    StartGateways,
    StopGatewaysForExit,
}

// The window starts and ends operations on its dispatcher thread.
internal sealed class OperationTracker
{
    private readonly List<Operation> active = [];

    public bool IsBusy => active.Count != 0;
    public string? CurrentName => IsBusy ? active[^1].Name : null;
    public bool IsActive(WindowOperation kind) => active.Exists(operation => operation.Kind == kind);
    public bool IsOnlyActive(WindowOperation kind) => active.Count == 1 && active[0].Kind == kind;

    public IDisposable Begin(WindowOperation kind, string name)
    {
        var operation = new Operation(this, kind, name);
        active.Add(operation);
        return operation;
    }

    private sealed class Operation(OperationTracker tracker, WindowOperation kind, string name) : IDisposable
    {
        public WindowOperation Kind { get; } = kind;
        public string Name { get; } = name;

        public void Dispose() => tracker.active.Remove(this);
    }
}
