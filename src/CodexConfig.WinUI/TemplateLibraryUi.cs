using System.Text.Json;
using CodexConfig.Application;
using CodexConfig.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private readonly TemplateLibraryService templateLibrary;
    private TemplateLibrarySnapshot? templateLibrarySnapshot;
    private ConfigTemplate? selectedTemplate, editingTemplate;
    private TemplateDraftApplication? templateApplication;
    private bool creatingTemplate, buildingTemplate;
    private string templateEditBaseline = "";
    private long templateDiffRevision;
    private readonly List<(TextBox Path, TextBox Value, CheckBox Default, Button Remove)> templateRows = [];
    private readonly Dictionary<string, CheckBox> templateChoices = new(StringComparer.Ordinal);
    private readonly List<Control> templateActions = [];
    private Button? stageTemplateButton;
    private bool HasTemplateDraft => editingTemplate is not null && TemplateEditFingerprint() != templateEditBaseline;
    private bool CanEditTemplates => initialized && !updatingControls && !buildingTemplate && !ManualApplicationBusy && !dialogOpen && !closing;

    private void EnsureTemplateLibrary()
    {
        if (templateLibrarySnapshot is null) ReadTemplateLibrary();
        else if (editingTemplate is null && selectedTemplate is not null) RenderTemplateDetails(selectedTemplate);
    }
    private void ReadTemplateLibrary()
    {
        try { templateLibrarySnapshot = templateLibrary.Read(); RenderTemplateList(); TemplatesPanel.TemplateStatusText.Text = "选择差异只加入配置草稿；保存方案只更新本机方案库。"; }
        catch (Exception error) { TemplatesPanel.TemplateStatusText.Text = "方案库无法读取，原文件已保留：" + error.Message; templateLibrarySnapshot = null; TemplatesPanel.TemplateList.Children.Clear(); }
        RefreshView();
    }
    private void RefreshTemplateControls()
    {
        if (TemplatesPanel.TemplateList is null) return;
        var editable = !ManualApplicationBusy && !dialogOpen && !closing;
        TemplatesPanel.NewTemplateButton.IsEnabled = TemplatesPanel.CaptureTemplateButton.IsEnabled = TemplatesPanel.RestoreBuiltInsButton.IsEnabled = editable && templateLibrarySnapshot is not null;
        TemplatesPanel.CaptureTemplateButton.IsEnabled &= connectionDraft.Available && connectionDraft.FieldErrors.Count == 0;
        TemplatesPanel.RefreshTemplatesButton.IsEnabled = editable;
        foreach (var control in templateActions) control.IsEnabled = editable && templateLibrarySnapshot is not null;
        foreach (var choice in templateChoices.Values) choice.IsEnabled = editable && choice.Tag is true;
        foreach (var (path, value, selected, remove) in templateRows) path.IsEnabled = value.IsEnabled = selected.IsEnabled = remove.IsEnabled = editable;
        TemplatesPanel.TemplateNameInput.IsEnabled = TemplatesPanel.TemplateDescriptionInput.IsEnabled = TemplatesPanel.AddTemplateRowButton.IsEnabled = SaveTemplateButton.IsEnabled = CancelTemplateButton.IsEnabled = editable;
        TemplateEditFooter.Visibility = ReferenceEquals(currentPage, TemplatesNav) && editingTemplate is not null ? Visibility.Visible : Visibility.Collapsed;
        TemplateEditFooterStatus.Text = HasTemplateDraft ? "方案有未保存修改，保存到本机方案库后生效。" : "保存方案只更新本机方案库。";
        ViewConfigurationDraftButton.Visibility = connectionDraft.HasDraft ? Visibility.Visible : Visibility.Collapsed; ViewConfigurationDraftButton.IsEnabled = editable;
        if (editingTemplate is not null) TemplatesPanel.TemplateStatusText.Text = HasTemplateDraft ? "正在编辑方案，修改尚未保存。" : "正在编辑方案。Codex 配置草稿单独保留。";
        if (stageTemplateButton is not null) stageTemplateButton.IsEnabled = editable && connectionDraft.Available && !HasPendingRecovery && templateDiffRevision == connectionDraft.Revision && templateChoices.Values.Any(choice => choice.IsChecked == true && choice.IsEnabled);
        TemplatesPanel.UndoTemplateButton.Visibility = templateApplication is null ? Visibility.Collapsed : Visibility.Visible;
        TemplatesPanel.UndoTemplateButton.IsEnabled = editable && connectionDraft.Available;
    }
    private static TextBlock TemplateText(string value, bool muted = false) => new() { Text = value, TextWrapping = TextWrapping.Wrap, Opacity = muted ? 0.7 : 1 };
    private Button TemplateAction(string label, Func<Task> action)
    {
        var button = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Left };
        button.Click += async (_, _) => { if (CanEditTemplates) await action(); }; templateActions.Add(button); return button;
    }
    private void RenderTemplateList()
    {
        selectedTemplate = editingTemplate = null; stageTemplateButton = null; templateChoices.Clear(); templateActions.Clear(); templateRows.Clear();
        TemplatesPanel.TemplateList.Visibility = Visibility.Visible; TemplatesPanel.TemplateDetails.Visibility = TemplatesPanel.TemplateEditPanel.Visibility = Visibility.Collapsed;
        TemplatesPanel.TemplateLibraryActions.Visibility = Visibility.Visible; TemplatesPanel.TemplateList.Children.Clear();
        foreach (var template in templateLibrarySnapshot?.Templates ?? [])
        {
            var body = new StackPanel { Spacing = 10 };
            body.Children.Add(new TextBlock { Text = template.Name, FontSize = 18, TextWrapping = TextWrapping.Wrap });
            body.Children.Add(TemplateText(template.Settings.Count + " 项 · " + (TemplateRepository.IsBuiltIn(template.Id) ? BuiltInTemplates.IsCustomized(template) ? "本地已编辑的内置方案" : "内置方案" : "我的方案"), true));
            if (template.Description.Length > 0) body.Children.Add(TemplateText(template.Description, true));
            body.Children.Add(TemplateAction("选择差异", () => { RenderTemplateDetails(template); return Task.CompletedTask; }));
            var commands = new ActionWrapPanel();
            commands.Children.Add(TemplateAction("编辑", () => { BeginTemplateEdit(template, false); return Task.CompletedTask; }));
            commands.Children.Add(TemplateAction("复制", () => { BeginTemplateEdit(TemplateRepository.New(template.Name.Length > 56 ? template.Name[..56] + " 副本" : template.Name + " 副本", template.Settings) with { Description = template.Description }, true); return Task.CompletedTask; }));
            commands.Children.Add(TemplateAction("删除", () => DeleteTemplateAsync(template))); body.Children.Add(commands);
            if (BuiltInTemplates.IsCustomized(template) && BuiltInTemplates.All.FirstOrDefault(item => item.Id == template.Id) is { } builtIn)
                body.Children.Add(TemplateAction("查看本版内置方案", () => { RenderTemplateDetails(builtIn); return Task.CompletedTask; }));
            TemplatesPanel.TemplateList.Children.Add(new Border { Padding = new(20), CornerRadius = new(8), BorderThickness = new(1), BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(40, 128, 128, 128)), Child = body });
        }
        if (templateLibrarySnapshot?.Templates.Count == 0) TemplatesPanel.TemplateList.Children.Add(TemplateText("方案库为空，可新建方案或恢复内置方案。"));
    }
    private void RenderTemplateDetails(ConfigTemplate template)
    {
        selectedTemplate = template; editingTemplate = null; templateChoices.Clear(); templateActions.Clear(); TemplatesPanel.TemplateDetails.Children.Clear();
        TemplatesPanel.TemplateList.Visibility = TemplatesPanel.TemplateEditPanel.Visibility = TemplatesPanel.TemplateLibraryActions.Visibility = Visibility.Collapsed; TemplatesPanel.TemplateDetails.Visibility = Visibility.Visible;
        templateDiffRevision = connectionDraft.Revision;
        TemplatesPanel.TemplateDetails.Children.Add(new TextBlock { Text = template.Name, FontSize = 20, TextWrapping = TextWrapping.Wrap });
        TemplatesPanel.TemplateDetails.Children.Add(TemplateText(template.Description, true));
        TemplatesPanel.TemplateDetails.Children.Add(TemplateText("来源：" + template.SourceName + (template.SourceUrl.Length > 0 ? "\n" + template.SourceUrl : ""), true));
        TemplatesPanel.TemplateDetails.Children.Add(TemplateText("已有草稿、历史项和相同值默认保留。手动勾选差异后，先加入配置草稿，再通过下方按钮确认保存。", true));
        if (connectionDraft.Snapshot is { } snapshot)
            foreach (var diff in template.BuildDiff(new TomlDocument(snapshot.Config).Model, connectionDraft.PendingFields))
            {
                var body = new StackPanel { Spacing = 6 }; var choice = new CheckBox { Content = ConfigHelp.Name(diff.Setting.Path) + " · " + diff.State,
                    IsChecked = diff.SelectedByDefault, IsEnabled = !diff.Same, Tag = !diff.Same };
                templateChoices[diff.Setting.Path] = choice; body.Children.Add(choice);
                body.Children.Add(TemplateText(diff.Setting.Path, true));
                body.Children.Add(TemplateText("本文件：" + TemplateValue(diff.Setting.Path, diff.FileValue) +
                    (diff.HasDraft ? "\n当前草稿：" + TemplateValue(diff.Setting.Path, diff.CurrentValue) : "") + "\n方案值：" + TemplateValue(diff.Setting.Path, diff.Setting.Value), true));
                if (ConfigHelp.Find(diff.Setting.Path)?.VerificationStatus == "historical") body.Children.Add(TemplateText("历史设置，当前客户端或服务是否支持需核对。", true));
                choice.Checked += (_, _) => RefreshView(); choice.Unchecked += (_, _) => RefreshView();
                TemplatesPanel.TemplateDetails.Children.Add(new Border { Padding = new(12), Child = body });
            }
        else TemplatesPanel.TemplateDetails.Children.Add(TemplateText("尚未读取有效的连接配置；可先管理方案库，再切换工作区使用。"));
        stageTemplateButton = TemplateAction("加入配置草稿", () => { StageSelectedTemplate(); return Task.CompletedTask; }); TemplatesPanel.TemplateDetails.Children.Add(stageTemplateButton);
        TemplatesPanel.TemplateDetails.Children.Add(TemplateAction("重新核对差异", () => { RenderTemplateDetails(template); return Task.CompletedTask; }));
        TemplatesPanel.TemplateDetails.Children.Add(TemplateAction("返回方案库", () => { RenderTemplateList(); return Task.CompletedTask; })); RefreshView();
    }
    private static string TemplateValue(string path, string? value)
    {
        if (value is null) return "未设置 · 跟随 Codex";
        try { return ConfigurationCatalogService.Display(path, TomlDocument.ParseModel("value = " + value)["value"]); }
        catch (Exception) { return "输入尚未通过校验"; }
    }
    private void StageSelectedTemplate()
    {
        if (!CanEditConnection || selectedTemplate is null) return;
        if (templateDiffRevision != connectionDraft.Revision) { TemplatesPanel.TemplateStatusText.Text = "配置草稿已改变，请重新核对差异后再选择。"; return; }
        try
        {
            var selected = templateChoices.Where(pair => pair.Value.IsChecked == true && pair.Value.IsEnabled).Select(pair => pair.Key).ToArray();
            templateApplication = connectionDraft.StageTemplate(selectedTemplate, selected);
            TemplatesPanel.TemplateStatusText.Text = "已将 " + templateApplication.Count + " 项加入配置草稿，尚未写入文件。"; RenderTemplateDetails(selectedTemplate);
        }
        catch (Exception error) { TemplatesPanel.TemplateStatusText.Text = "方案尚未加入：" + error.Message; }
        RefreshView();
    }
    internal void UndoTemplateClicked(object sender, RoutedEventArgs args)
    {
        if (!CanEditConnection || templateApplication is null) return;
        try
        {
            var result = connectionDraft.UndoTemplate(templateApplication); templateApplication = null;
            TemplatesPanel.TemplateStatusText.Text = "已撤销 " + result.Restored + " 项方案修改；保留之后另行编辑的 " + result.Preserved + " 项。";
            if (selectedTemplate is not null) RenderTemplateDetails(selectedTemplate);
        }
        catch (Exception error) { TemplatesPanel.TemplateStatusText.Text = error.Message; }
        RefreshView();
    }
    private void BeginTemplateEdit(ConfigTemplate template, bool create)
    {
        buildingTemplate = true;
        try
        {
            editingTemplate = template; selectedTemplate = null; creatingTemplate = create; templateRows.Clear(); templateActions.Clear(); TemplatesPanel.TemplateEditRows.Children.Clear();
            TemplatesPanel.TemplateList.Visibility = TemplatesPanel.TemplateDetails.Visibility = TemplatesPanel.TemplateLibraryActions.Visibility = Visibility.Collapsed; TemplatesPanel.TemplateEditPanel.Visibility = Visibility.Visible;
            TemplatesPanel.TemplateNameInput.Text = template.Name; TemplatesPanel.TemplateDescriptionInput.Text = template.Description; TemplatesPanel.TemplateEditError.Text = "";
            foreach (var setting in template.Settings) AddTemplateEditRow(setting);
            if (templateRows.Count == 0) AddTemplateEditRow(new("", ""));
            templateEditBaseline = TemplateEditFingerprint();
        }
        finally { buildingTemplate = false; }
        RefreshView();
    }
    private void AddTemplateEditRow(TemplateSetting setting)
    {
        var path = new TextBox { Header = "配置字段", Text = setting.Path, MaxLength = 1000 };
        var value = new TextBox { Header = "TOML 值", Text = setting.Value, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 160, MaxLength = 65536 };
        var selected = new CheckBox { Content = "应用时默认选择此项", IsChecked = setting.SelectByDefault };
        var remove = new Button { Content = "移除此字段", HorizontalAlignment = HorizontalAlignment.Left };
        var body = new StackPanel { Spacing = 10 }; body.Children.Add(path); body.Children.Add(value); body.Children.Add(selected); body.Children.Add(remove);
        var border = new Border { Padding = new(12), Child = body }; TemplatesPanel.TemplateEditRows.Children.Add(border); templateRows.Add((path, value, selected, remove));
        path.TextChanging += TemplateEditTextChanged; value.TextChanging += TemplateEditTextChanged;
        selected.Checked += (_, _) => { if (!buildingTemplate) RefreshView(); }; selected.Unchecked += (_, _) => { if (!buildingTemplate) RefreshView(); };
        remove.Click += (_, _) => { if (CanEditTemplates) { templateRows.RemoveAll(row => ReferenceEquals(row.Path, path)); TemplatesPanel.TemplateEditRows.Children.Remove(border); RefreshView(); } };
    }
    private string TemplateEditFingerprint() => JsonSerializer.Serialize(new { Name = TemplatesPanel.TemplateNameInput.Text, Description = TemplatesPanel.TemplateDescriptionInput.Text,
        Rows = templateRows.Select(row => new { Path = row.Path.Text, Value = row.Value.Text, Default = row.Default.IsChecked == true }) });
    internal void TemplateEditTextChanged(TextBox sender, TextBoxTextChangingEventArgs args) { if (CanEditTemplates) RefreshView(); }
    internal void NewTemplateClicked(object sender, RoutedEventArgs args) { if (CanEditTemplates) BeginTemplateEdit(TemplateRepository.New("", []), true); }
    internal void CaptureTemplateClicked(object sender, RoutedEventArgs args)
    { if (CanEditTemplates) try { BeginTemplateEdit(TemplateRepository.New("我的设置", TemplateLibraryService.Capture(connectionDraft)), true); } catch (Exception error) { TemplatesPanel.TemplateStatusText.Text = error.Message; } }
    internal void AddTemplateRowClicked(object sender, RoutedEventArgs args) { if (CanEditTemplates && templateRows.Count < 300) { AddTemplateEditRow(new("", "")); RefreshView(); } }
    private void SaveTemplateClicked(object sender, RoutedEventArgs args) { if (CanEditTemplates) SaveTemplateEdit(); }
    private bool SaveTemplateEdit()
    {
        if (editingTemplate is null || ManualApplicationBusy || dialogOpen || closing) return false;
        try
        {
            var saved = templateLibrary.Save(editingTemplate with { Name = TemplatesPanel.TemplateNameInput.Text, Description = TemplatesPanel.TemplateDescriptionInput.Text,
                Settings = templateRows.Select(row => new TemplateSetting(row.Path.Text.Trim(), row.Value.Text.Trim(), row.Default.IsChecked == true)).ToArray() }, creatingTemplate);
            templateLibrarySnapshot = templateLibrary.Read(); editingTemplate = null; RenderTemplateDetails(saved); TemplatesPanel.TemplateStatusText.Text = "方案已保存，Codex 配置保持不变。"; return true;
        }
        catch (Exception error) { TemplatesPanel.TemplateEditError.Text = error is InvalidDataException ? "取值或方案库格式不正确，请检查；当前编辑与原文件已保留。" : error.Message; return false; }
    }
    private async void CancelTemplateClicked(object sender, RoutedEventArgs args) { if (await ResolveTemplateEditAsync()) { RenderTemplateList(); RefreshView(); } }
    internal async void RefreshTemplatesClicked(object sender, RoutedEventArgs args) { if (await ResolveTemplateEditAsync()) ReadTemplateLibrary(); }
    private async Task<bool> ResolveTemplateEditAsync()
    {
        if (editingTemplate is null) return true;
        if (!HasTemplateDraft) { RenderTemplateList(); return true; }
        if (ManualApplicationBusy || dialogOpen || closing) return false;
        var dialog = new ContentDialog { Title = "方案有未保存修改", Content = "可保存到本机方案库，或放弃修改后继续。Codex 配置草稿会保留。",
            PrimaryButtonText = "保存方案", SecondaryButtonText = "放弃修改", CloseButtonText = "继续编辑", DefaultButton = ContentDialogButton.Close, XamlRoot = Root.XamlRoot };
        var result = await ShowDialogAsync(dialog);
        if (result == ContentDialogResult.Primary) return SaveTemplateEdit();
        if (result != ContentDialogResult.Secondary) return false;
        RenderTemplateList(); return true;
    }
    private async Task DeleteTemplateAsync(ConfigTemplate template)
    {
        if (!await ConfirmDialogAsync("删除方案", "删除“" + template.Name + "”？仅更新本机方案库，配置草稿保持不变。", "删除方案")) return;
        try { templateLibrary.Delete(template.Id); templateLibrarySnapshot = templateLibrary.Read(); RenderTemplateList(); TemplatesPanel.TemplateStatusText.Text = "方案已删除。"; }
        catch (Exception error) { TemplatesPanel.TemplateStatusText.Text = error.Message; }
        RefreshView();
    }
    internal async void RestoreBuiltInsClicked(object sender, RoutedEventArgs args)
    {
        if (!CanEditTemplates || !await ConfirmDialogAsync("恢复内置方案", "恢复本版内置方案及其默认取值，保留你的自建方案。Codex 配置保持不变。", "恢复内置方案")) return;
        try { templateLibrary.RestoreBuiltIns(); templateLibrarySnapshot = templateLibrary.Read(); RenderTemplateList(); TemplatesPanel.TemplateStatusText.Text = "内置方案已恢复。"; }
        catch (Exception error) { TemplatesPanel.TemplateStatusText.Text = error.Message; }
        RefreshView();
    }
}
