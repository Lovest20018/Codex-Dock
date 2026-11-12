using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CodexConfig.Core;

namespace CodexConfig.App;

public partial class MainWindow
{
    private async Task RunModelBehaviorSmokeAsync(string output)
    {
        var checks = new List<string>(); await RunModelBehaviorChecksAsync(output, checks);
        await File.WriteAllTextAsync(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new { status = "PASS", tests = checks }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private async Task RunModelBehaviorChecksAsync(string output, List<string> checks)
    {
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
        Directory.CreateDirectory(output);
        var directory = Path.Combine(output, "model-home-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "models_cache.json");
        File.WriteAllText(source, JsonSerializer.Serialize(new { models = new[] { "gpt-6.1-sol", "gpt-6-astra", "codex-auto-review" }.Select(slug => new {
            slug, supported_in_api = true, base_instructions = "Offline UI fixture", supported_reasoning_levels = new[] { new { effort = "high", description = "High" }, new { effort = "max", description = "Max" } },
            service_tiers = new[] { new { id = "priority", name = "Fast" }, new { id = "ultrafast", name = "Ultra Fast" } }, supports_reasoning_effort_updates = true
        }) }));
        File.WriteAllText(Path.Combine(directory, "config.toml"), "# preserve\nmodel='gpt-6.1-sol'\nopenai_base_url='https://fixture.invalid/v1'\nmodel_reasoning_effort='max'\napproval_policy='on-request'\n");
        File.WriteAllText(Path.Combine(directory, "auth.json"), new ConnectionSettings("https://fixture.invalid", "fake-ui-model-key", "gpt-6.1-sol").RenderAuth());
        var memory = new IsolatedGatewayEnvironment(); var fixtureStore = new ConfigStore(memory, _ => null);
        var fixture = new MainWindow(fixtureStore, directory, output, memory); fixture.Loaded -= fixture.WindowLoaded;
        try
        {
            fixture.Show(); fixture.ShowPage("preferences"); await fixture.Settle();
            Check(fixture.modelSection.IsVisible && fixture.SpeedCompatibilityCard.IsVisible && fixture.reviewSection.IsVisible && !fixture.configurationTools.IsVisible, "Model, speed compatibility and reviewer share the common page without advanced source controls");
            var before = fixtureStore.Load(directory);
            fixture.dedicatedReview.IsChecked = true; await fixture.Settle();
            Check(fixture.Dirty && before.SameContents(fixtureStore.Load(directory)), "Selecting dedicated approval stages a draft without touching files");
            fixture.OpenConfigurationReview(); await fixture.Settle();
            Check(fixture.preview?.CatalogUpdate is not null && fixture.ConfirmWriteButton.IsEnabled && fixture.ReviewTargets.Text.Contains(ManagedModelCatalogStore.IndexFile), "Confirmation includes the native catalog and management policy");
            fixture.ConfirmWriteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await fixture.Settle();
            Check(fixture.reviewSaved && !fixture.Dirty && ManagedModelCatalogStore.Inspect(fixtureStore.Load(directory)) is { Managed: true, ReviewModel: "codex-auto-review" }, "Confirmed UI save creates a managed dedicated reviewer and encrypted backup");
            fixture.CloseOverlay();
            fixture.behaviorModel.ApplyTemplate();
            var modelInput = (TextBox)fixture.behaviorModel.Template.FindName("PART_EditableTextBox", fixture.behaviorModel);
            modelInput.Focus(); modelInput.Text = "gpt-6-astra";
            Check(fixture.Dirty && fixture.SaveButton.IsEnabled, "Uncommitted model text immediately participates in save and close guards");
            Check(new TomlDocument(fixture.CreateConfigurationPlan().Config).String("model") == "gpt-6-astra", "Immediate confirmation captures the typed model before the debounce expires");
            await Task.Delay(500); await fixture.Settle();
            Check(fixture.ModelBox.Text == "gpt-6-astra" && fixture.behaviorModel.Text == "gpt-6-astra" && fixture.Dirty, "Typing in the common model selector updates the shared draft without losing text");
            fixture.Reload();
            fixture.Width = 960; fixture.Height = 640; fixture.ShowPage("preferences"); await fixture.Settle(); fixture.Capture(output, "model-compact-light.png");
            fixture.SpeedCompatibilityCard.BringIntoView(); await fixture.Settle(); fixture.Capture(output, "speed-compact-light.png");
            fixture.reviewSection.BringIntoView(); await fixture.Settle(); fixture.Capture(output, "review-compact-light.png");
            Check(fixture.SpeedCompatibilityCard.Parent != fixture.ApplicationBody && fixture.catalogState.Text.Contains("Dock 管理"), "Compatibility lives beside the speed choice and catalog ownership is visible");
            File.AppendAllText(source, " "); fixture.RefreshModelBehavior();
            Check(fixture.catalogState.Text.Contains("有更新待同步") && !fixture.Dirty, "Source updates are detected without silently writing or creating drafts");
            fixture.StageCatalog(); await fixture.Settle();
            Check(fixture.Dirty && fixture.preview?.CatalogUpdate is not null, "Explicit catalog sync reprojects the dedicated review policy");
            fixture.dedicatedReview.IsChecked = false; fixture.StageCatalog(); await fixture.Settle();
            Check(new TomlDocument(fixture.preview!.Config).String("approvals_reviewer") == "user" && fixture.preview.CatalogUpdate is not null, "Catalog synchronization preserves a deliberate manual-approval choice");
            fixture.Reload(); fixture.ModelBox.Text = "unknown-model"; fixture.UpdatePreview();
            Check(fixture.preview is null && fixture.Dirty && !fixture.SaveCurrent(), "An uncovered model cannot silently lose its fixed reviewer");
            fixture.Reload(); fixture.ShowPage("overview"); await fixture.Settle(); fixture.Capture(output, "overview-compact-light.png");
            Check(FindVisual<TextBlock>(fixture.OverviewBody).Any(text => text.Text.Contains("当前进程是否加载未确认")), "Overview distinguishes a saved file from runtime adoption");
            ThemeManager.Apply(true); fixture.RootPanel.LayoutTransform = new ScaleTransform(1.25, 1.25);
            fixture.ShowPage("preferences"); fixture.reviewSection.BringIntoView(); await fixture.Settle(); fixture.Capture(output, "review-compact-dark-scaled.png");
            Check(fixture.reviewSection.IsVisible && fixture.GlobalSaveBar.IsVisible, "Reviewer settings and save actions remain accessible with dark theme and scaled layout");
            fixture.RootPanel.LayoutTransform = Transform.Identity;
            fixtureStore.Restore(fixtureStore.Load(directory)); fixture.Reload();
            Check(before.SameContents(fixtureStore.Load(directory)), "Restoring the first UI save also restores catalog policy and credentials");
        }
        finally { ThemeManager.Apply(false); fixture.Close(); }
    }
}
