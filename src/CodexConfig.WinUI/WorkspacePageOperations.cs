namespace CodexConfig.WinUI;

internal sealed record WorkspacePageOperations(
    Func<string> CurrentHome,
    Func<bool> CanBegin,
    Func<bool> CanInteract,
    Func<bool> IsClosing,
    Func<bool> IsInitialized,
    CancellationToken Lifetime,
    OperationTracker Busy,
    Action Refresh);
