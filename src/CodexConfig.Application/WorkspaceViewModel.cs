using System.Windows.Input;

namespace CodexConfig.Application;

// Small shell-facing adapter. Commands are intentionally UI-agnostic so WinUI and WPF can share it.
public sealed class WorkspaceViewModel : WorkspaceObservableObject
{
    private WorkspaceSession session;
    public WorkspaceSession Session { get => session; private set => Set(ref session, value); }
    public WorkspaceState State => Session.State;
    public string Home => State.Home;
    public WorkspacePhase Phase => State.Phase;
    public bool HasDraft => State.HasDraft;
    public bool IsBusy => State.Operation.IsBusy;
    public string Status => State.Operation.Message.Length > 0 ? State.Operation.Message : State.LastError ?? "";
    public ICommand ClearErrorCommand { get; private set; } = null!;

    public WorkspaceViewModel(string home)
    {
        session = new WorkspaceSession(home);
        AttachSession(session);
    }

    public WorkspaceViewModel(WorkspaceSession session)
    {
        this.session = session;
        AttachSession(session);
    }

    private void AttachSession(WorkspaceSession session)
    {
        ClearErrorCommand = new DelegateCommand(() => Session.WaitForUser("请核对当前工作区后继续。"), () => State.Phase == WorkspacePhase.Failed);
        Session.PropertyChanged += (_, _) =>
        {
            Changed(nameof(State)); Changed(nameof(Home)); Changed(nameof(Phase)); Changed(nameof(HasDraft)); Changed(nameof(IsBusy)); Changed(nameof(Status));
        };
    }

    public void ReplaceSession(string home)
    {
        Session = new WorkspaceSession(home); AttachSession(Session);
        Changed(nameof(Session));
    }

    private sealed class DelegateCommand(Action execute, Func<bool>? canExecute = null) : ICommand
    {
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
        public void Execute(object? parameter) => execute();
        public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
