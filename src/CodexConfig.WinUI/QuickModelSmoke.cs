using System.Text.Json;
using CodexConfig.Application;
using CodexConfig.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private async Task RunQuickModelSmokeAsync(string output, List<string> checks)
    {
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks.Add(message);
            File.AppendAllText(Path.Combine(output, "progress.log"), "PASS " + message + Environment.NewLine);
        }

        static byte[] Catalog(params string[] names) => JsonSerializer.SerializeToUtf8Bytes(new
        {
            models = names.Select(slug => new
            {
                slug, supported_in_api = true, visibility = "list", context_window = 128000,
                supported_reasoning_levels = new[] { new { effort = "medium", description = "Medium" } }
            })
        });
        static QuickModelDialog ModelPicker(ContentDialog dialog) => dialog as QuickModelDialog ??
            throw new InvalidOperationException("Expected the one-step model confirmation.");
        static void Select(QuickModelDialog picker, string model) =>
            picker.ModelChoices.Children.OfType<RadioButton>().Single(button => Equals(button.Tag, model)).IsChecked = true;
        static string Model(ConfigSnapshot snapshot) => new TomlDocument(snapshot.Config).String("model") ?? "";

        const string newModel = "gpt-6.2-sol";
        const string oldModel = "fixture-old-model";
        const string otherModel = "fixture-other-model";
        const string reviewModel = "codex-auto-review";
        var previous = viewModel.Home;
        var previousDiscovery = modelDiscovery;
        var runtime = (SmokeDesktopApplyRuntime)applicationRuntime;
        var previousTargets = runtime.Targets;
        var windowSize = AppWindow.Size;
        var theme = Root.RequestedTheme;
        var home = Path.Combine(output, "quick-model-home");
        Directory.CreateDirectory(home);
        var config = Path.Combine(home, "config.toml");
        File.WriteAllText(config, "model='" + oldModel + "'\nopenai_base_url='https://quick-model.invalid/v1'\n" +
            "approvals_reviewer='auto_review'\napproval_policy='on-request'\n");
        File.WriteAllText(Path.Combine(home, "auth.json"), "{\"auth_mode\":\"apikey\",\"OPENAI_API_KEY\":\"fake-quick-model-key\"}");
        var source = Path.Combine(home, "fixture-models.json");
        File.WriteAllBytes(source, Catalog(oldModel, otherModel, reviewModel));
        var before = store.Load(home);
        store.Apply(ManagedModelCatalogStore.Prepare(store.Plan(before, store.ReadSettings(before), updateConnection: false), new(), source));
        var initial = store.Load(home);
        var executable = Path.Combine(home, "Codex.exe");
        var desktop = new CodexDesktopTarget(executable, [new(720, 1, 134390198764749156, executable)]) { UserDataDirectory = Path.Combine(home, "default-instance") };
        runtime.Targets = [desktop];
        try
        {
            await SwitchHomeAsync(home);
            NavigateTo(WorkspaceNav);
            var restartCount = runtime.Restarts;
            var serviceReads = 0;
            string? serviceAddress = null;
            modelDiscovery = new((settings, _) =>
            {
                serviceReads++;
                serviceAddress = settings.BaseUrl;
                return Task.FromResult(new ModelCatalogResult([oldModel, otherModel, newModel, reviewModel], 1, Catalog(oldModel, otherModel, newModel, reviewModel)));
            });
            Check(QuickModelButton.IsEnabled && serviceReads == 0 && ModelText.Text.Contains(oldModel), "The saved model and the quick action are visible without contacting any service");

            smokeDialog = dialog =>
            {
                var picker = ModelPicker(dialog);
                Select(picker, newModel);
                Check(picker.IsPrimaryButtonEnabled && picker.IsSecondaryButtonEnabled && initial.SameContents(store.Load(home)) &&
                    !connectionDraft.HasDraft && runtime.Restarts == restartCount &&
                    picker.ModelChoices.Children.OfType<RadioButton>().All(item => !Equals(item.Tag, reviewModel)),
                    "Selecting a listed model previews save and restart without writing or selecting the dedicated reviewer as the main model");
                return Task.FromResult(ContentDialogResult.None);
            };
            await OpenQuickModelAsync();
            Check(connectionDraft.Model == newModel && QuickModelButton.Content.ToString() == "继续更换模型" &&
                serviceReads == 1 && serviceAddress == "https://quick-model.invalid/v1" &&
                initial.SameContents(store.Load(home)), "Closing the model picker keeps the draft and reads only the chosen service once");
            smokeDialog = dialog =>
            {
                var picker = ModelPicker(dialog);
                Check(picker.SelectedModel == newModel, "Reopening the model picker restores the previous selection");
                Select(picker, oldModel);
                return Task.FromResult(ContentDialogResult.None);
            };
            await OpenQuickModelAsync();
            Check(!connectionDraft.HasDraft && initial.SameContents(store.Load(home)), "Returning to the original model leaves no staged capability update");

            connectionDraft.SetOverride("approval_policy", null);
            var confirmations = 0;
            smokeDialog = async dialog =>
            {
                confirmations++;
                var picker = ModelPicker(dialog);
                Select(picker, newModel);
                var review = picker.Review;
                Check(review is not null && picker.PrimaryButtonText == "保存并重启 Codex" &&
                    review.Changes.Any(change => change.Path == "approval_policy") &&
                    review.Changes.Any(change => change.Path == "Dock 管理的能力目录") &&
                    !review.Description.Contains("fake-quick-model-key") && initial.SameContents(store.Load(home)),
                    "One confirmation presents the model, fixed reviewer data and other pending changes before any write");
                var shown = picker.ShowAsync();
                try { await CaptureAsync(output, "quick-model-confirmation.png", picker); }
                finally { picker.Hide(); await shown; }
                return ContentDialogResult.Primary;
            };
            await OpenQuickModelAsync();
            var applied = store.Load(home);
            Check(confirmations == 1 && lastApplication is { Outcome: DesktopApplyOutcome.Completed, DesktopOpened: true } &&
                runtime.Restarts == restartCount + 1 && runtime.LastForce == false && Model(applied) == newModel &&
                ManagedModelCatalogStore.Inspect(applied) is { Managed: true, ReviewModel: reviewModel } &&
                new TomlDocument(applied.Config).Get("approval_policy") is null &&
                initial.Auth!.SequenceEqual(applied.Auth!) && File.Exists(configurationResult?.BackupPath) && currentPage == WorkspaceNav,
                "Save and restart uses a single confirmation and an encrypted backup and preserves the independent reviewer and credentials");

            restartCount = runtime.Restarts;
            smokeDialog = dialog =>
            {
                Select(ModelPicker(dialog), otherModel);
                return Task.FromResult(ContentDialogResult.Secondary);
            };
            await OpenQuickModelAsync();
            Check(Model(store.Load(home)) == otherModel && quickModelAwaitingApply && runtime.Restarts == restartCount &&
                WorkspaceNextTitle.Text == "模型已保存，等待应用" && currentPage == WorkspaceNav,
                "Save only returns to the workbench with a clear pending application and never restarts Codex");
            smokeDialog = dialog =>
            {
                Check(dialog.Title.ToString() == "重启 Codex 并应用配置", "Resuming a saved change confirms the current live desktop");
                return Task.FromResult(ContentDialogResult.Primary);
            };
            await RunWorkspaceNextAsync();
            Check(lastApplication?.Outcome == DesktopApplyOutcome.Completed && runtime.Restarts == restartCount + 1 &&
                !quickModelAwaitingApply && currentPage == WorkspaceNav, "The workbench applies a saved model without taking the user to another page");

            restartCount = runtime.Restarts;
            runtime.RefuseExit = true;
            smokeDialog = dialog =>
            {
                Select(ModelPicker(dialog), newModel);
                return Task.FromResult(ContentDialogResult.Primary);
            };
            await OpenQuickModelAsync();
            Check(Model(store.Load(home)) == newModel && quickModelAwaitingApply && lastApplication?.Outcome == DesktopApplyOutcome.ExitBlocked &&
                QuickModelApplyDetailsButton.Visibility == Visibility.Visible && WorkspaceNextButton.Content.ToString() == "重试应用" &&
                runtime.Restarts == restartCount + 1 && !runtime.LastForce, "An exit failure keeps the new model saved and offers a normal retry and application details");
            runtime.RefuseExit = false;
            smokeDialog = _ => Task.FromResult(ContentDialogResult.Primary);
            await RunWorkspaceNextAsync();
            Check(runtime.Restarts == restartCount + 2 && lastApplication?.Outcome == DesktopApplyOutcome.Completed && Model(store.Load(home)) == newModel,
                "Retry resumes the desktop step using the saved model");

            restartCount = runtime.Restarts;
            var beforeMissing = store.Load(home);
            modelDiscovery = new((_, _) => Task.FromResult(new ModelCatalogResult(["fixture-id-only", otherModel], 1)));
            smokeDialog = dialog =>
            {
                var picker = ModelPicker(dialog);
                Select(picker, "fixture-id-only");
                Check(!picker.IsPrimaryButtonEnabled && !picker.IsSecondaryButtonEnabled && picker.Review is null && beforeMissing.SameContents(store.Load(home)),
                    "An ID-only model cannot bypass the saved dedicated reviewer");
                Select(picker, otherModel);
                Check(picker.IsSecondaryButtonEnabled && picker.Review?.Changes.All(change => change.Path == "model") == true,
                    "An ID-only result can still offer a model already covered by the saved catalog");
                return Task.FromResult(ContentDialogResult.None);
            };
            await OpenQuickModelAsync();
            LoadConnectionDraft(); viewModel.Session.MarkDraft(false);

            var current = runtime.Targets.Single();
            var second = desktop with { Processes = [new(721, 1, 134390198764749158, executable)], UserDataDirectory = Path.Combine(home, "second-instance") };
            runtime.Targets = [current, second];
            smokeDialog = dialog =>
            {
                var picker = ModelPicker(dialog);
                Select(picker, otherModel);
                Check(!picker.IsPrimaryButtonEnabled && picker.IsSecondaryButtonEnabled, "Multiple instances require a selection before restart but allow save only");
                picker.TargetList.SelectedIndex = 1;
                Check(picker.IsPrimaryButtonEnabled, "Selecting the second instance enables the one-step restart");
                runtime.Targets = [current];
                return Task.FromResult(ContentDialogResult.Primary);
            };
            await OpenQuickModelAsync();
            Check(runtime.Restarts == restartCount && Model(store.Load(home)) == otherModel && quickModelAwaitingApply && lastApplication?.Outcome == DesktopApplyOutcome.Failed,
                "A replaced target stops application after saving without restarting a different instance");

            var beforeConflict = store.Load(home);
            modelDiscovery = new((_, _) => Task.FromResult(new ModelCatalogResult([newModel, otherModel], 1)));
            smokeDialog = dialog =>
            {
                Select(ModelPicker(dialog), newModel);
                File.AppendAllText(config, "model_verbosity='low'\n");
                return Task.FromResult(ContentDialogResult.Primary);
            };
            await OpenQuickModelAsync();
            Check(configurationResult?.Outcome == ConfigurationSaveOutcome.Conflict && runtime.Restarts == restartCount &&
                Model(store.Load(home)) == Model(beforeConflict) && new TomlDocument(store.Load(home).Config).String("model_verbosity") == "low" &&
                connectionDraft.Model == newModel && connectionDraft.HasDraft, "An external save conflict retains the selected draft and never launches Codex");
            LoadWorkspace(home);

            var readStarted = new TaskCompletionSource();
            modelDiscovery = new(async (_, cancellation) =>
            {
                readStarted.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellation);
                return new([], 0);
            });
            smokeDialog = _ => throw new InvalidOperationException("A cancelled read cannot open a picker.");
            var pending = OpenQuickModelAsync();
            await readStarted.Task;
            Check(CancelQuickModelReadButton.Visibility == Visibility.Visible && !QuickModelButton.IsEnabled, "The workbench offers cancellation while the model list is loading");
            CancelQuickModelReadClicked(CancelQuickModelReadButton, new());
            await pending;
            Check(quickModelRead is null && QuickModelButton.IsEnabled && !connectionDraft.HasDraft, "Cancelling model discovery restores the workbench without a draft or a dialog");
            connectionDraft.BaseUrl = "https://unsaved-address.invalid/v1";
            await OpenQuickModelAsync();
            Check(currentPage == ConnectionNav && connectionDraft.HasDraft, "Changing the connection directs the user to its editor before contacting an unsaved address");
            LoadConnectionDraft(); viewModel.Session.MarkDraft(false);

            NavigateTo(WorkspaceNav);
            AppWindow.Resize(new(720, 540));
            Root.RequestedTheme = ElementTheme.Dark;
            runtime.Targets = [desktop with { Processes = [] }];
            modelDiscovery = new((_, _) => Task.FromException<ModelCatalogResult>(new HttpRequestException("连接超时。")));
            smokeDialog = async dialog =>
            {
                var picker = ModelPicker(dialog);
                Select(picker, newModel);
                Check(picker.PrimaryButtonText == "保存并打开 Codex" && picker.IsPrimaryButtonEnabled && picker.IsSecondaryButtonEnabled,
                    "A stopped Codex offers open and the saved model catalog remains selectable offline");
                var shown = picker.ShowAsync();
                try { await CaptureAsync(output, "quick-model-compact-dark.png", picker); }
                finally { picker.Hide(); await shown; }
                return ContentDialogResult.None;
            };
            await OpenQuickModelAsync();
        }
        finally
        {
            runtime.RefuseExit = false; runtime.Targets = previousTargets; modelDiscovery = previousDiscovery;
            LoadConnectionDraft(); viewModel.Session.MarkDraft(false);
            smokeDialog = _ => Task.FromResult(ContentDialogResult.Secondary);
            await SwitchHomeAsync(previous);
            smokeDialog = null;
            AppWindow.Resize(windowSize);
            Root.RequestedTheme = theme;
            NavigateTo(WorkspaceNav);
        }
    }
}
