using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using CodexConfig.Core;

namespace CodexConfig.App;

public partial class MainWindow
{
    private async Task RunTemplateApplicationSmokeAsync(string output, List<string> checks)
    {
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        var savedHome = home; var savedSnapshot = store.Load(home); var width = Width; var height = Height;
        var fixture = Path.Combine(output, "template-application-fixture"); Directory.CreateDirectory(fixture);
        var path = Path.Combine(fixture, "config.toml");
        File.WriteAllText(path, "model='template-test'\nweb_search='cached'\n[features]\nfast_mode=false\ncontext_management=true # keep\n[features.token_budget]\nenabled=true\nuse_history_notes_extension=true\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(fixture, "auth.json"), "{\"personal_access_token\":\"fake-template-auth\"}");
        var library = new TemplateRepository(Path.Combine(savedHome, "editor-templates"));
        var custom = library.Save(BuiltInTemplates.KrillCommunity with { Name = "本地 Krill", Settings = [new("web_search", "\"cached\"")] });
        try
        {
            home = fixture; Check(Reload(), "Template fixture loads"); ShowPage("templates");
            var before = store.Load(home);
            TemplateEditor.ShowTemplate(custom); await Settle();
            Check(TemplateEditor.PreviewBuiltInButton.IsVisible, "Customized template exposes current builtin");
            Check(FindVisual<TextBlock>(TemplateEditor).Any(block => block.Text.Contains("当前配置或草稿有 3 项")), "Legacy warning includes omitted existing settings");
            TemplateEditor.PreviewBuiltInButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle();
            Check(TemplateEditor.Current == BuiltInTemplates.KrillCommunity && library.Load().Single(item => item.Id == custom.Id).Name == custom.Name, "Builtin preview preserves custom library");
            detailedChanges["features.context_management"] = "false"; Editor.Configure(snapshot!.Config, detailedChanges);
            TemplateEditor.Configure(snapshot.Config, detailedChanges); TemplateEditor.ShowTemplate(BuiltInTemplates.KrillCommunity);
            Check(!TemplateEditor.Choices["features.context_management"].IsChecked!.Value && TemplateEditor.ChoiceResults["features.context_management"].Text.Contains("保留草稿：false"), "Unchecked selection explains retained false draft");
            TemplateEditor.SelectAllValuesButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(TemplateEditor.ChoiceResults["features.context_management"].Text.Contains("写入：true"), "All-values selection explicitly enables old context");
            TemplateEditor.StageButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(before.SameContents(store.Load(home)), "Template confirmation does not write early"); CloseOverlay();
            Check(detailedChanges["features.context_management"] == "false", "Cancel restores draft preceding all-values selection");
            UndoClicked(UndoButton, new());
            TemplateEditor.ShowTemplate(BuiltInTemplates.DisableLegacyContext); await Settle();
            Check(TemplateEditor.Choices.Values.All(box => box.IsChecked == true) && TemplateEditor.ChoiceResults.Values.All(block => block.Text.Contains("写入：false")), "Shutdown selects explicit false for all three enabled fields");
            Width = 960; Height = 640; await Settle(); Capture(output, "template-disable-legacy.png");
            TemplateEditor.StageButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle();
            Check(reviewChangeCount == 3 && ConfirmWriteButton.IsEnabled && ReviewTargets.Text.Contains(path), "Shutdown review targets exactly three values");
            Capture(output, "template-disable-review.png"); ConfirmWriteClicked(ConfirmWriteButton, new()); await Settle();
            Check(reviewSaved && store.LatestBackup(home) is not null, "Shutdown saves with backup"); CloseOverlay();
            var after = store.Load(home); var doc = new TomlDocument(after.Config);
            Check(BuiltInTemplates.DisableLegacyContext.Settings.All(setting => doc.Get(setting.Path) is false), "Confirmed shutdown persists false");
            Check(after.Auth!.SequenceEqual(before.Auth!) && doc.String("model") == "template-test" && doc.Get("features.fast_mode") is false && doc.Text.Contains("# keep"), "Shutdown preserves auth and unrelated settings");
            TemplateEditor.ShowTemplate(BuiltInTemplates.DisableLegacyContext);
            Check(!TemplateEditor.StageButton.IsEnabled && TemplateEditor.ChoiceResults.Values.All(block => block.Text.Contains("无需重复写入")), "Repeated shutdown reports equality");
            TemplateEditor.ShowTemplate(custom); await Settle(); Capture(output, "template-local-override.png");
            TemplateEditor.PreviewBuiltInButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle(); Capture(output, "template-upstream-values.png");
            ThemeManager.Apply(true); await Settle(); Capture(output, "template-upstream-values-dark.png");
            store.Restore(after); Check(before.SameContents(store.Load(home)), "Shutdown rollback restores exact files");
            checks.AddRange(["Customized template previews current builtin without overwriting library", "Old active fields remain visible when omitted by custom recipe", "Template checkbox reports retained or written values", "All-values selection and cancellation preserve preceding drafts", "Shutdown review saves three false values with backup", "Shutdown preserves credentials and unrelated configuration", "Repeated shutdown has no redundant writes", "Shutdown rollback restores original files"]);
        }
        finally
        {
            CloseOverlay(); library.Delete(custom.Id); library.RestoreBuiltIns();
            home = savedHome; Width = width; Height = height; ThemeManager.Apply(false); Reload(savedSnapshot);
        }
    }
}
