namespace CodexConfig.Application;

// A small, UI-independent operation gate. A shell can hold the returned lease
// while it performs a long-running operation and report stages without owning
// the session lifecycle itself.
public sealed class WorkspaceOperationRunner
{
    private readonly WorkspaceSession session;
    private int active;

    public WorkspaceOperationRunner(WorkspaceSession session) => this.session = session;

    public bool IsBusy => Volatile.Read(ref active) != 0;

    public WorkspaceOperationLease? TryBegin(WorkspaceOperation operation, string stage, string message)
    {
        if (Interlocked.CompareExchange(ref active, 1, 0) != 0) return null;
        try
        {
            session.BeginOperation(operation, stage, message);
            return new(this, session, operation);
        }
        catch
        {
            Volatile.Write(ref active, 0);
            throw;
        }
    }

    internal void Release() => Volatile.Write(ref active, 0);
}

public sealed class WorkspaceOperationLease : IDisposable
{
    private readonly WorkspaceOperationRunner owner;
    private readonly WorkspaceSession session;
    private readonly WorkspaceOperation operation;
    private int disposed;
    private bool terminal;

    internal WorkspaceOperationLease(WorkspaceOperationRunner owner, WorkspaceSession session, WorkspaceOperation operation)
    {
        this.owner = owner; this.session = session; this.operation = operation;
    }

    public bool IsActive => Volatile.Read(ref disposed) == 0;
    public bool IsTerminal => terminal;

    public void Report(string stage, string message)
    {
        EnsureActive();
        session.BeginOperation(operation, stage, message);
    }

    public void Complete(string stage, string message, bool needsRestart = false)
    {
        EnsureActive();
        terminal = true;
        session.CompleteOperation(stage, message, needsRestart);
    }

    public void WaitForUser(string message)
    {
        EnsureActive();
        terminal = true;
        session.WaitForUser(message);
    }

    public void Fail(string message)
    {
        EnsureActive();
        terminal = true;
        session.Fail(message, operation);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        // A shell can exit an operation through an unexpected branch. Do not
        // leave the shared session looking busy after releasing the gate.
        try
        {
            if (!terminal) session.WaitForUser("操作已结束，请核对当前状态后继续。");
        }
        finally { owner.Release(); }
    }

    private void EnsureActive()
    {
        if (!IsActive) throw new ObjectDisposedException(nameof(WorkspaceOperationLease));
    }
}

// Desktop application is the first use case moved behind the application
// boundary. The same runner can later host save, gateway and mobile actions.
public sealed class WorkspaceApplyCoordinator
{
    private readonly WorkspaceOperationRunner runner;
    private readonly WorkspaceSession session;

    public WorkspaceApplyCoordinator(WorkspaceSession session) { this.session = session; runner = new(session); }

    public bool IsBusy => runner.IsBusy;

    public WorkspaceOperationLease? TryBeginDesktopApplication(string stage = "准备应用", string message = "正在准备应用…")
        => runner.TryBegin(WorkspaceOperation.ApplyingDesktop, stage, message);

    public WorkspaceOperationLease? TryBeginConfigurationSave()
        => runner.TryBegin(WorkspaceOperation.SavingConfiguration, "保存配置", "正在备份并写入已确认的修改…");

    public WorkspaceOperationLease? TryBeginConfigurationRecovery()
        => runner.TryBegin(WorkspaceOperation.RecoveringConfiguration, "恢复配置", "正在恢复已确认的配置与目录依赖…");
    public WorkspaceOperationLease? TryBeginProfile()
        => runner.TryBegin(WorkspaceOperation.SavingProfile, "Profile", "正在备份并写入已确认的 Profile…");

    public WorkspaceOperationLease? TryBeginGateway(bool stopping, string label)
        => runner.TryBegin(stopping ? WorkspaceOperation.StoppingGateway : WorkspaceOperation.StartingGateway,
            stopping ? "停止网关" : "启动网关", "正在处理" + label + "…");

    public WorkspaceOperationLease? TryBeginMobile(string message)
        => runner.TryBegin(WorkspaceOperation.ManagingMobileConnection, "手机连接", message);

    public WorkspaceOperationLease? TryBeginUpdateInstallation()
        => runner.TryBegin(WorkspaceOperation.InstallingUpdate, "准备安装", "正在重新核对安装器和后台进程…");

    internal void ConfigurationSaved(CodexConfig.Core.ConfigSnapshot saved) => session.ConfigurationSaved(saved);
}
