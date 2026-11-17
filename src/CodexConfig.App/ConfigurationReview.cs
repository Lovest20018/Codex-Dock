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
        currentPage is not ("mobile" or "gateways") && !(currentPage == "enhancements" && !Dirty) && !(currentPage == "templates" && (TemplateEditor.IsEditing || TemplateEditor.Current is not null))
            ? Visibility.Visible : Visibility.Collapsed;

    private void OpenConfigurationReview()
    {
        if (closing || desktopRestartBusy || officialLoginBusy || snapshot is null || store.HasPending(home)) return;
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
        ReviewChanges.Children.Clear();
        if (ConnectionChanged && !EditingMixedConnection)
        {
            var compatibility = new StackPanel();
            compatibility.Children.Add(new TextBlock { Text = "Codex 桌面 Fast 入口", FontWeight = FontWeights.SemiBold });
            compatibility.Children.Add(HelpContent.Note(DirectFastCompatibilityHint.Text, new(0, 6, 0, 0)));
            var notice = new Border { Child = compatibility, Padding = new(14), CornerRadius = new(8), Margin = new(0, 0, 0, 8) };
            notice.SetResourceReference(Border.BackgroundProperty, "AccentSoft"); ReviewChanges.Children.Add(notice);
        }
        if (ConnectionModeBox.SelectedIndex != (int)loadedSettings.Mode)
        {
            var change = new StackPanel();
            change.Children.Add(new TextBlock { Text = "切换连接方式", FontWeight = FontWeights.SemiBold });
            change.Children.Add(HelpContent.Note(SavedConnectionLabel + " → " + (EditingMixedConnection ? MixedConnectionLabel : DirectConnectionLabel), new(0, 6, 0, 0)));
            change.Children.Add(HelpContent.Note(EditingMixedConnection
                ? "保存后保留官方账号登录。到“网关管理”启动本机模型网关后，模型请求通过该网关使用服务凭据。"
                : "保存后改用服务的 API Key 认证并直接连接模型；原官方登录会纳入加密备份。", new(0, 6, 0, 0)));
            var notice = new Border { Child = change, Padding = new(14), CornerRadius = new(8), Margin = new(0, 0, 0, 8) };
            notice.SetResourceReference(Border.BackgroundProperty, "AccentSoft"); ReviewChanges.Children.Add(notice);
        }
        var sharedReview = configurationSave.Review(preview);
        var targets = sharedReview.Targets;
        reviewChangeCount = 0;
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
        foreach (var change in sharedReview.Changes)
            Add(change.Name, change.Path, change.Previous, change.Next);
        var editable = Dirty;
        ReviewTitle.Text = editable ? "确认写入本地配置" : "查看当前配置";
        ReviewSummary.Text = editable ? $"待写入 {reviewChangeCount} 项 · " + (reviewSource is null ? "包含当前所有未保存的修改" : "来源：" + reviewSource + (reviewHadDraft ? "，包含此前未保存修改" : "")) : "当前本地配置 · 没有待写入的修改";
        ReviewTargets.Text = targets.Count > 0 ? "本次写入目标：\n" + string.Join("\n", targets) : "当前配置目录：\n" + before.Home;
        ReviewTargets.Visibility = Visibility.Visible;
        ReviewHint.Text = editable ? "尚未写入 · 确认时自动加密备份 · 保存后重新打开 Codex" : "只读预览 · 凭据已隐藏";
        ClosePreviewButton.Content = editable ? "返回调整" : "关闭";
        ConfirmWriteButton.Content = $"确认写入 {reviewChangeCount} 项";
        ConfirmWriteButton.Visibility = editable ? Visibility.Visible : Visibility.Collapsed;
        ConfirmWriteButton.IsEnabled = editable && reviewChangeCount > 0 && Editor.Errors.Count == 0 && !reviewWriteFailed && !closing && !officialLoginBusy;
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
        ReviewTargets.Visibility = Visibility.Visible;
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
        ReviewTitle.Text = "配置已保存"; ReviewSummary.Text = $"已保存 {count} 项修改 · 等待应用到 Codex"; ReviewTargets.Text = targets;
        ReviewTargets.Visibility = Visibility.Visible;
        ReviewSuccessMessage.Text = "本地配置已写入并核对，原内容已自动加密备份。\n点击下方按钮，统一打开或重启 Codex 并加载所需兼容。完成后请新建聊天核对。";
        SavedGatewaysButton.Visibility = Visibility.Collapsed;
        if (loadedSettings.Mode == ConnectionMode.OfficialWithApi)
            ReviewSuccessMessage.Text += "\nDock 会先检查并启动本机模型网关，再打开 Codex。";
        PrepareDesktopRestart();
        ReviewSuccess.Visibility = Visibility.Visible;
        ReviewTabs.Visibility = ReviewScroll.Visibility = PreviewBox.Visibility = ReviewError.Visibility = Visibility.Collapsed;
        ConfirmWriteButton.Visibility = Visibility.Collapsed; ClosePreviewButton.Content = "完成";
        ReviewHint.Text = "已保存本地文件 · 当前聊天是否采用尚未确认"; ClosePreviewButton.Focus();
    }
}
