using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using CodexConfig.Core;

namespace CodexConfig.App;

public partial class MainWindow
{
    private async Task RunConfigurationReviewSmokeAsync(string output, List<string> checks)
    {
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        var previousHome = home; var previousSnapshot = store.Load(home);
        var width = Width; var height = Height;
        var fixture = Path.Combine(output, "review-fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(fixture);
        var configPath = Path.Combine(fixture, "config.toml");
        File.WriteAllText(configPath, "model='review-model'\nweb_search='cached'\n[features]\nfast_mode=false # retain this comment\n", new UTF8Encoding(false));
        var auth = Encoding.UTF8.GetBytes("{\"personal_access_token\":\"fake-review-secret\"}");
        File.WriteAllBytes(Path.Combine(fixture, "auth.json"), auth);
        try
        {
            home = fixture; Check(Reload(), "Review fixture loads"); ShowPage("preferences");
            PreviewClicked(PreviewButton, new());
            Check(!ConfirmWriteButton.IsVisible && ReviewTitle.Text == "查看当前配置", "Clean configuration preview is read-only"); CloseOverlay();
            var before = File.ReadAllBytes(configPath);
            ModelBox.Text = "review-next"; SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle();
            Check(PreviewOverlay.IsVisible && !MainShell.IsEnabled && reviewChangeCount == 1 && ConfirmWriteButton.IsEnabled, "Manual save opens a usable confirmation");
            Check(ReviewTargets.Text.Contains(configPath) && File.ReadAllBytes(configPath).SequenceEqual(before), "Confirmation identifies its file and does not write early");
            Check(FindVisual<TextBlock>(ReviewChanges).All(block => !block.Text.Contains("fake-review-secret")), "Structured review hides credentials");
            Capture(output, "review-confirm.png"); CloseOverlay();
            Check(ModelBox.Text == "review-next" && Dirty && File.ReadAllBytes(configPath).SequenceEqual(before), "Returning from manual confirmation retains edits");
            detailedChanges["agents.max_depth"] = "\"invalid\""; Editor.Configure(snapshot!.Config, detailedChanges); MarkDirty();
            SaveClicked(SaveButton, new());
            Check(reviewPlan is null && !ConfirmWriteButton.IsEnabled && ReviewError.IsVisible && File.ReadAllBytes(configPath).SequenceEqual(before), "Invalid settings cannot be confirmed"); CloseOverlay();
            detailedChanges.Remove("agents.max_depth"); Editor.Configure(snapshot!.Config, detailedChanges); MarkDirty();
            SaveClicked(SaveButton, new()); ConfirmWriteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle();
            Check(reviewSaved && ReviewSuccess.IsVisible && !Dirty && store.LatestBackup(home) is not null, "Confirm writes, backs up and rereads the file");
            Check(new TomlDocument(store.Load(home).Config).String("model") == "review-next", "Confirmed manual value persisted");
            Capture(output, "review-success.png"); CloseOverlay();
            detailedChanges["future.keep"] = "true"; Editor.Configure(snapshot!.Config, detailedChanges); MarkDirty();
            var template = TemplateRepository.New("子代理并发方案", [new("agents.max_concurrent_threads_per_session", "9"), new("agents.max_depth", "1"), new("features.multi_agent_v2.max_concurrent_threads_per_session", "9")]);
            TemplatesClicked(TemplatesNav, new()); TemplateEditor.ShowTemplate(template);
            TemplateEditor.StageButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(currentPage == "templates" && reviewChangeCount == 4 && !GlobalSaveBar.IsVisible && ConfirmWriteButton.IsEnabled, "Template and preceding edits share one confirmation");
            Width = 960; Height = 640; await Settle();
            foreach (var button in new[] { ConfirmWriteButton, ClosePreviewButton })
            {
                var bounds = button.TransformToAncestor(RootPanel).TransformBounds(new Rect(button.RenderSize));
                Check(bounds.Left >= 0 && bounds.Right <= RootPanel.ActualWidth && bounds.Bottom <= RootPanel.ActualHeight, "Confirmation actions fit the minimum window");
            }
            Capture(output, "review-confirm-compact.png"); ThemeManager.Apply(true); await Settle(); Capture(output, "review-confirm-dark.png"); ThemeManager.Apply(false);
            CloseOverlay();
            Check(detailedChanges.Count == 1 && detailedChanges["future.keep"] == "true" && TemplateEditor.Choices.Values.All(box => box.IsChecked == true), "Returning removes only the temporary template edits and retains selection");
            TemplateEditor.Choices["features.multi_agent_v2.max_concurrent_threads_per_session"].IsChecked = false;
            TemplateEditor.StageButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(reviewChangeCount == 3, "Changing selection removes a previously previewed field");
            ConfirmWriteClicked(ConfirmWriteButton, new()); CloseOverlay();
            var saved = new TomlDocument(store.Load(home).Config);
            Check(saved.Get("agents.max_depth") is 1L && saved.Get("agents.max_concurrent_threads_per_session") is 9L && saved.Get("features.multi_agent_v2.max_concurrent_threads_per_session") is null && saved.Get("future.keep") is true, "Only selected settings and preceding edits are persisted");
            Check(saved.Get("features.fast_mode") is false && saved.Text.Contains("# retain this comment") && File.ReadAllBytes(Path.Combine(home, "auth.json")).SequenceEqual(auth), "Confirmation retains unrelated settings, comments and login");
            ShowPage("catalog"); Editor.Filter("agents.max_depth"); ((TextBox)Editor.Input("agents.max_depth")).Text = "";
            SaveClicked(SaveButton, new()); await Settle();
            Check(FindVisual<TextBlock>(ReviewChanges).Any(block => block.Text.Contains("移除此项本地覆盖")), "Restore-default confirmation explains removal of the local override");
            ConfirmWriteClicked(ConfirmWriteButton, new()); CloseOverlay();
            Check(new TomlDocument(store.Load(home).Config).Get("agents.max_depth") is null, "Confirmed default removes just the selected override");
            ModelBox.Text = "must-not-overwrite"; SaveClicked(SaveButton, new());
            File.AppendAllText(configPath, "# external edit\n"); var external = File.ReadAllBytes(configPath);
            ConfirmWriteClicked(ConfirmWriteButton, new());
            Check(reviewWriteFailed && !ConfirmWriteButton.IsEnabled && ReviewError.IsVisible && File.ReadAllBytes(configPath).SequenceEqual(external), "Concurrent external edits block confirmed writes");
            PreviewTabClicked(ConfigTab, new());
            Check(!ConfirmWriteButton.IsEnabled && ReviewError.IsVisible, "Changing preview tabs cannot dismiss a failed write"); CloseOverlay();
            Check(Dirty && ModelBox.Text == "must-not-overwrite", "A failed write retains manual edits");
            checks.AddRange(["Manual save requires file confirmation", "Read-only preview cannot write", "Confirmation return retains manual edits", "Invalid configuration disables confirmation", "Confirmation reports backup and reread success", "Template confirmation preserves preceding drafts", "Template reselection excludes cancelled fields", "Configuration review redacts credentials", "Restore-default review explains removal", "Confirmed save rejects external edits", "Failed confirmation retains edits", "Minimum-size and dark configuration review"]);
        }
        finally
        {
            CloseOverlay(); home = previousHome; Width = width; Height = height; ThemeManager.Apply(false); Reload(previousSnapshot);
        }
    }
}
