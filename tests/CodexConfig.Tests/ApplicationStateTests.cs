using CodexConfig.Application;
using CodexConfig.Core;

internal static class ApplicationStateTests
{
    internal static void Register(List<(string Name, Func<Task> Run)> tests, string root)
    {
        tests.Add(("workspace application state has one explicit lifecycle and preserves the active snapshot", () =>
        {
            var home = Path.Combine(root, "workspace-state"); Directory.CreateDirectory(home);
            var session = new WorkspaceSession(home); var notifications = 0; session.PropertyChanged += (_, _) => notifications++;
            Assert(session.State.Phase == WorkspacePhase.Empty && !session.State.HasDraft && !session.State.Operation.IsBusy);
            session.BeginLoad(home); Assert(session.State.Phase == WorkspacePhase.Loading && session.State.Operation.Operation == WorkspaceOperation.LoadingConfiguration);
            var snapshot = new ConfigSnapshot(home, System.Text.Encoding.UTF8.GetBytes("model='fixture'"), null, null);
            session.Loaded(home, snapshot); session.MarkDraft(true);
            Assert(ReferenceEquals(session.State.Snapshot, snapshot) && session.State.Phase == WorkspacePhase.Draft && session.State.HasDraft);
            session.BeginOperation(WorkspaceOperation.ApplyingDesktop, "应用", "正在打开 Codex…");
            Assert(session.State.Phase == WorkspacePhase.Applying && session.State.Operation.IsBusy);
            session.CompleteOperation("完成", "已应用");
            Assert(session.State.Phase == WorkspacePhase.Applied && !session.State.Operation.IsBusy && session.State.LastAppliedAt is not null);
            Assert(notifications >= 5);
            return Task.CompletedTask;
        }));
        tests.Add(("workspace view model mirrors lifecycle state without owning file or window APIs", () =>
        {
            var home = Path.Combine(root, "workspace-view-model"); Directory.CreateDirectory(home);
            var viewModel = new WorkspaceViewModel(home); var changed = 0; viewModel.PropertyChanged += (_, _) => changed++;
            viewModel.Session.BeginLoad(home); viewModel.Session.Fail("fixture failure", WorkspaceOperation.LoadingConfiguration);
            Assert(viewModel.Phase == WorkspacePhase.Failed && viewModel.IsBusy == false && viewModel.Status == "fixture failure" && changed > 0);
            Assert(viewModel.ClearErrorCommand.CanExecute(null)); viewModel.ClearErrorCommand.Execute(null);
            Assert(viewModel.Phase == WorkspacePhase.WaitingForUser && viewModel.Status.Contains("核对"));
            return Task.CompletedTask;
        }));
        tests.Add(("workspace coordinator loads one snapshot and transitions the shared session atomically", () =>
        {
            var home = Path.Combine(root, "workspace-coordinator"); Directory.CreateDirectory(home);
            File.WriteAllText(Path.Combine(home, "config.toml"), "model='coordinator-fixture'\n");
            var coordinator = new WorkspaceCoordinator(new ConfigStore(new FixtureEnvironment()), home);
            var result = coordinator.Load(home);
            Assert(result.Settings.Model == "coordinator-fixture" && ReferenceEquals(coordinator.Session.State.Snapshot, result.Snapshot) && coordinator.Session.State.Phase == WorkspacePhase.Ready);
            coordinator.MarkDraft(true); Assert(coordinator.Session.State.HasDraft && coordinator.Session.State.Phase == WorkspacePhase.Draft);
            return Task.CompletedTask;
        }));
        tests.Add(("workspace operation runner serializes desktop work and closes its state", () =>
        {
            var home = Path.Combine(root, "workspace-operation"); Directory.CreateDirectory(home);
            var session = new WorkspaceSession(home); var runner = new WorkspaceOperationRunner(session);
            using var first = runner.TryBegin(WorkspaceOperation.ApplyingDesktop, "准备", "正在准备…")
                ?? throw new InvalidOperationException("First operation was rejected");
            Assert(runner.IsBusy && session.State.Operation.IsBusy && session.State.Operation.Operation == WorkspaceOperation.ApplyingDesktop);
            Assert(runner.TryBegin(WorkspaceOperation.ApplyingDesktop, "重复", "不应执行") is null);
            first.Report("执行", "正在执行…");
            Assert(session.State.Operation.Stage == "执行");
            first.Complete("完成", "已完成");
            Assert(!session.State.Operation.IsBusy && session.State.Phase == WorkspacePhase.Applied && runner.IsBusy);
            first.Dispose();
            Assert(!runner.IsBusy);
            using var second = runner.TryBegin(WorkspaceOperation.ApplyingDesktop, "失败", "正在失败…")
                ?? throw new InvalidOperationException("Released operation could not start");
            second.Fail("fixture failure");
            second.Dispose();
            Assert(session.State.Phase == WorkspacePhase.Failed && session.State.LastError == "fixture failure" && !runner.IsBusy);
            return Task.CompletedTask;
        }));
        tests.Add(("workspace apply coordinator uses the shared operation boundary", () =>
        {
            var home = Path.Combine(root, "workspace-apply-coordinator"); Directory.CreateDirectory(home);
            var session = new WorkspaceSession(home); var coordinator = new WorkspaceApplyCoordinator(session);
            using var lease = coordinator.TryBeginDesktopApplication();
            Assert(lease is not null && coordinator.IsBusy && session.State.Operation.Operation == WorkspaceOperation.ApplyingDesktop);
            Assert(coordinator.TryBeginDesktopApplication() is null);
            lease!.WaitForUser("需要核对"); lease.Dispose();
            Assert(!coordinator.IsBusy && session.State.Phase == WorkspacePhase.WaitingForUser && session.State.Operation.Message == "需要核对");
            return Task.CompletedTask;
        }));
    }
    private static void Assert(bool value) { if (!value) throw new InvalidOperationException("Application state regression"); }
    private sealed class FixtureEnvironment : IUserEnvironment { public string? Read() => null; public void Write(string? value) { } }
}
