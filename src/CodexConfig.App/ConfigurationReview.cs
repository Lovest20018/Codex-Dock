using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CodexConfig.Core;

namespace CodexConfig.App;

public partial class MainWindow
{
    private ConfigPlan? reviewPlan;
    private TemplateApplication? reviewTemplate;
    private string? reviewSource;
    private bool reviewHadDraft;
    private bool reviewSaved;
    private bool reviewWriteFailed;
    private int reviewChangeCount;

    private void UpdateSaveBarVisibility() => GlobalSaveBar.Visibility =
        currentPage != "mobile" && !(currentPage == "templates" && (TemplateEditor.IsEditing || TemplateEditor.Current is not null))
            ? Visibility.Visible : Visibility.Collapsed;

    private void OpenConfigurationReview()
    {
        if (closing || loginInProgress || snapshot is null || store.HasPending(home)) return;
        previewTimer.Stop(); reviewPlan = null; reviewSaved = false; reviewWriteFailed = false;
        currentTab = Dirty ? "changes" : "config";
        Editor.Revalidate(); UpdatePreview();
        if (Editor.Errors.Count > 0) RenderReviewError("有配置项尚未填写正确。请返回调整，检查行内提示。");
        else reviewPlan = preview;
        ReviewScroll.ScrollToTop();
        OpenOverlay(PreviewOverlay, ClosePreviewButton);
    }

    private void RenderConfigurationReview()
    {
        if (preview is null || reviewSaved) return;
        var before = preview.Before;
        var original = new TomlDocument(before.Config).Model;
        var updated = new TomlDocument(preview.Config).Model;
        ReviewChanges.Children.Clear();
        var targets = new List<string>();
        reviewChangeCount = 0;
        string Display(string path, object? value) => value is null ? "未设置 · 跟随其他配置层或 Codex 默认"
            : ConfigReadClient.SafeValue(path, JsonSerializer.SerializeToElement(value));
        void Add(string name, string path, string previous, string next)
        {
            reviewChangeCount++;
            var row = new StackPanel();
            row.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            row.Children.Add(HelpContent.Note(path, new(0, 4, 0, 8)));
            row.Children.Add(HelpContent.Note("本地现值：" + previous, new(0)));
            row.Children.Add(new TextBlock { Text = "修改为：" + next, TextWrapping = TextWrapping.Wrap, Margin = new(0, 5, 0, 0) });
            var border = new Border { Child = row, Padding = new(0, 12, 8, 12), BorderThickness = new(0, 0, 0, 1) };
            border.SetResourceReference(Border.BorderBrushProperty, "Line"); ReviewChanges.Children.Add(border);
        }
        foreach (var path in TomlDocument.ValuePaths(original).Concat(TomlDocument.ValuePaths(updated)).Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal))
        {
            var previous = TomlDocument.ValueAt(original, path); var next = TomlDocument.ValueAt(updated, path);
            if (ConfigSchema.Literal(previous) == ConfigSchema.Literal(next)) continue;
            Add(ConfigHelp.Name(path), path, Display(path, previous), next is null ? "移除此项本地覆盖，跟随其他配置层或 Codex 默认" : Display(path, next));
        }
        if (reviewChangeCount > 0) targets.Add(Path.Combine(before.Home, "config.toml"));
        void FileChange(byte[]? oldBytes, byte[]? newBytes, string name, string file)
        {
            if ((oldBytes ?? []).SequenceEqual(newBytes ?? [])) return;
            targets.Add(Path.Combine(before.Home, file));
            Add(name, file, oldBytes is null ? "本地未设置" : "已设置（内容已隐藏）", newBytes is null ? "移除本地文件" : "更新本地文件（内容已隐藏）");
        }
        FileChange(before.Auth, preview.Auth, "认证文件", "auth.json");
        FileChange(before.Bridge, preview.Bridge, "加密模型连接", MixedConnection.FileName);
        FileChange(before.Details, preview.Details, "连接资料与 HTTP 确认", ConnectionDetails.FileName);
        var previousHttp = ConnectionDetails.Read(before.Details).HttpOrigin;
        var nextHttp = ConnectionDetails.Read(preview.Details).HttpOrigin;
        if (previousHttp != nextHttp)
            Add("HTTP 明文连接确认", "Dock 站点确认", previousHttp.Length == 0 ? "未确认" : previousHttp, nextHttp.Length == 0 ? "撤销确认" : nextHttp);
        if (before.AuthApiBaseUrl != preview.AuthApiBaseUrl)
        {
            targets.Add("用户环境变量 CODEX_AUTHAPI_BASE_URL");
            Add("用户环境变量", "CODEX_AUTHAPI_BASE_URL", before.AuthApiBaseUrl ?? "未设置", preview.AuthApiBaseUrl ?? "移除本地覆盖");
        }
        var editable = Dirty;
        ReviewTitle.Text = editable ? "确认写入本地配置" : "查看当前配置";
        ReviewSummary.Text = editable ? $"待写入 {reviewChangeCount} 项 · " + (reviewSource is null ? "包含当前所有未保存的修改" : "来源：" + reviewSource + (reviewHadDraft ? "，包含此前未保存修改" : "")) : "当前本地配置 · 没有待写入的修改";
        ReviewTargets.Text = targets.Count > 0 ? "本次写入目标：\n" + string.Join("\n", targets) : "当前配置目录：\n" + before.Home;
        ReviewHint.Text = editable ? "尚未写入 · 确认时自动加密备份 · 保存后重新打开 Codex" : "只读预览 · 凭据已隐藏";
        ClosePreviewButton.Content = editable ? "返回调整" : "关闭";
        ConfirmWriteButton.Content = $"确认写入 {reviewChangeCount} 项";
        ConfirmWriteButton.Visibility = editable ? Visibility.Visible : Visibility.Collapsed;
        ConfirmWriteButton.IsEnabled = editable && reviewChangeCount > 0 && Editor.Errors.Count == 0 && !reviewWriteFailed && !closing && !loginInProgress;
        if (!reviewWriteFailed) ReviewError.Visibility = Visibility.Collapsed;
        ReviewTabs.Visibility = Visibility.Visible; ReviewSuccess.Visibility = Visibility.Collapsed;
        ReviewScroll.Visibility = currentTab == "changes" ? Visibility.Visible : Visibility.Collapsed;
        PreviewBox.Visibility = currentTab == "changes" ? Visibility.Collapsed : Visibility.Visible;
        if (preview.Notes.Count > 0)
            ReviewChanges.Children.Add(new Expander { Header = "保存说明", Content = HelpContent.Note(string.Join("\n", preview.Notes)), Margin = new(0, 14, 0, 0) });
    }

    private void RenderReviewError(string message)
    {
        ReviewTitle.Text = "请先调整配置";
        ReviewSummary.Text = "尚未写入本地文件，修改已保留。";
        ReviewTargets.Text = "当前配置：\n" + Path.Combine(home, "config.toml");
        ReviewError.Text = message; ReviewError.Visibility = Visibility.Visible;
        ReviewTabs.Visibility = ReviewScroll.Visibility = PreviewBox.Visibility = ReviewSuccess.Visibility = Visibility.Collapsed;
        ConfirmWriteButton.Visibility = Visibility.Visible; ConfirmWriteButton.IsEnabled = false;
        ClosePreviewButton.Content = "返回调整";
        ReviewHint.Text = "请返回调整后重新确认。";
    }

    private void CancelTemplateReview()
    {
        if (reviewTemplate is null) return;
        reviewTemplate.Undo(detailedChanges); reviewTemplate = null;
        if (snapshot is not null) Editor.Configure(snapshot.Config, detailedChanges);
        MarkDirty();
    }

    private void ConfirmWriteClicked(object sender, RoutedEventArgs e)
    {
        if (reviewPlan is null || reviewSaved || reviewWriteFailed || !ConfirmWriteButton.IsEnabled) return;
        var plan = reviewPlan; var count = reviewChangeCount; var targets = ReviewTargets.Text;
        ConfirmWriteButton.IsEnabled = false;
        if (!SaveCurrent(plan))
        {
            reviewWriteFailed = true;
            ReviewError.Text = SaveFeedback.Text + "\n请返回调整；如文件已被其他程序修改，请重新读取后再确认。";
            ReviewError.Visibility = Visibility.Visible; ConfirmWriteButton.IsEnabled = false;
            return;
        }
        RenderSavedConfigurationReview(count, targets);
    }

    private void RenderSavedConfigurationReview(int count, string targets)
    {
        reviewTemplate = null; reviewPlan = null; reviewSaved = true;
        ReviewTitle.Text = "写入完成"; ReviewSummary.Text = $"已保存 {count} 项修改"; ReviewTargets.Text = targets;
        ReviewSuccessMessage.Text = "本地文件已写入并重新读取核对，原内容已自动加密备份。\n可在下方重启 Codex 读取这份配置，再新建聊天核对。新字段需要相应内核版本；项目、Profile 或组织配置可能覆盖本文件。";
        PrepareDesktopRestart();
        ReviewSuccess.Visibility = Visibility.Visible;
        ReviewTabs.Visibility = ReviewScroll.Visibility = PreviewBox.Visibility = ReviewError.Visibility = Visibility.Collapsed;
        ConfirmWriteButton.Visibility = Visibility.Collapsed; ClosePreviewButton.Content = "完成";
        ReviewHint.Text = "已保存本地文件 · 当前聊天是否采用尚未确认"; ClosePreviewButton.Focus();
    }
}
