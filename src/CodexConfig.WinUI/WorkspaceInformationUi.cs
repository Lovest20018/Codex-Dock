using CodexConfig.Application;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private ConfigurationPreviewService savedConfigurationPreview;
    private readonly WorkspaceDirectoryService workspaceDirectory;
    private ConfigurationPreviewSnapshot? savedPreview;
    private CancellationTokenSource? savedPreviewCancellation;
    private bool readingSavedPreview, filteringHelp;
    private string savedPreviewMessage = "尚未读取已保存文件。", workspaceFileMessage = "";
    private readonly List<string> smokeCopiedTexts = [], smokeOpenedDirectories = [];
    private uint nativeClipboardCopySequence;
    private bool SavedPreviewCurrentHome => savedPreview is { } snapshot && string.Equals(snapshot.Home, Path.TrimEndingDirectorySeparator(Path.GetFullPath(viewModel.Home)), StringComparison.OrdinalIgnoreCase);
    private ConfigurationPreviewSection? SelectedPreviewSection => SavedPreviewSectionInput.SelectedIndex switch { 1 => savedPreview?.Auth, 2 => savedPreview?.Environment, _ => savedPreview?.Config };

    private void RefreshWorkspaceInformationControls()
    {
        if (ReadSavedPreviewButton is null) return;
        var editable = !ApplicationBusy && !dialogOpen && !closing;
        OpenSavedFilesButton.IsEnabled = OpenWorkspaceFolderButton.IsEnabled = CopyWorkspacePathButton.IsEnabled = editable;
        ReadSavedPreviewButton.IsEnabled = editable;
        CancelSavedPreviewButton.Visibility = readingSavedPreview ? Visibility.Visible : Visibility.Collapsed; CancelSavedPreviewButton.IsEnabled = readingSavedPreview;
        SavedPreviewSectionInput.IsEnabled = !readingSavedPreview && !closing;
        CopySavedPreviewButton.IsEnabled = editable && SavedPreviewCurrentHome && SelectedPreviewSection?.State == ConfigurationPreviewState.Ready;
        SavedPreviewStatus.Text = savedPreviewMessage;
        WorkspaceFileActionStatus.Text = workspaceFileMessage; WorkspaceFileActionStatus.Visibility = workspaceFileMessage.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        HelpTopicActionButton.IsEnabled = editable && HelpTopicsInput.SelectedItem is DockHelpTopic;
    }
    private void InvalidateSavedPreview()
    {
        savedPreviewCancellation?.Cancel(); savedPreview = null; savedPreviewMessage = "尚未读取当前已保存文件，或文件已在本窗口更改。请重新读取。";
        workspaceFileMessage = ""; if (SavedPreviewText is not null) { SavedPreviewText.Text = ""; SavedPreviewSectionStatus.Text = ""; }
    }
    private void RenderSavedPreview()
    {
        var section = SelectedPreviewSection;
        SavedPreviewText.Text = section?.Text ?? "尚未读取当前目录。";
        SavedPreviewSectionStatus.Text = section is null ? "" : section.State switch { ConfigurationPreviewState.Missing => "未设置或文件不存在", ConfigurationPreviewState.Unreadable => "未能读取此项；其他可读项仍可查看", _ => section.Truncated ? "脱敏快照 · 显示长度已达上限" : "脱敏只读快照" };
        RefreshView();
    }
    private async void OpenSavedFilesClicked(object sender, RoutedEventArgs args)
    { WorkspaceDirectoryButton.Flyout.Hide(); if (ReferenceEquals(currentPage, FilePreviewNav)) await ReadSavedConfigurationPreviewAsync(); else NavigateTo(FilePreviewNav); }
    private async void ReadSavedPreviewClicked(object sender, RoutedEventArgs args) => await ReadSavedConfigurationPreviewAsync();
    private void CancelSavedPreviewClicked(object sender, RoutedEventArgs args) => savedPreviewCancellation?.Cancel();
    private void SavedPreviewSectionChanged(object sender, SelectionChangedEventArgs args) { if (initialized) RenderSavedPreview(); }
    private async Task ReadSavedConfigurationPreviewAsync()
    {
        if (ManualApplicationBusy || dialogOpen || closing) return;
        InvalidateSavedPreview(); readingSavedPreview = true; savedPreviewMessage = "正在读取已保存文件…"; RefreshView();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); savedPreviewCancellation = cancellation;
        try
        {
            var result = await savedConfigurationPreview.ReadAsync(viewModel.Home, () => viewModel.Home, cancellation.Token);
            if (closing) return;
            savedPreview = result; savedPreviewMessage = "读取时间：" + result.ReadAt.LocalDateTime.ToString("g") + "\n目录：" + result.Home + "\n已保存文件快照 · 未保存草稿未参与 · 重新读取可更新结果。"; RenderSavedPreview();
        }
        catch (OperationCanceledException) { if (!closing) savedPreviewMessage = "读取已取消或目录/文件发生变化，请重新读取。"; }
        catch (Exception) { if (!closing) savedPreviewMessage = "暂时无法读取已保存文件，请检查目录与权限后重试。"; }
        finally { savedPreviewCancellation = null; readingSavedPreview = false; if (!closing) { RenderSavedPreview(); RefreshView(); } }
    }
    private void CopyWorkspaceText(string value)
    {
        if (options.SmokeOutput is not null && !nativeEnvironmentAcceptance) { smokeCopiedTexts.Add(value); return; }
        nativeClipboardCopySequence = NativeWorkspaceClipboard.Copy(value);
    }
    private void CopySavedPreviewClicked(object sender, RoutedEventArgs args)
    {
        if (ApplicationBusy || dialogOpen || closing || !SavedPreviewCurrentHome || SelectedPreviewSection is not { State: ConfigurationPreviewState.Ready } section) return;
        try { CopyWorkspaceText(section.Text); savedPreviewMessage = "所选脱敏内容已复制。快照读取时间：" + savedPreview!.ReadAt.LocalDateTime.ToString("g"); }
        catch (Exception) { savedPreviewMessage = "未能复制，请在只读文本框中选择所需的脱敏内容。"; }
        RefreshView();
    }
    private void OpenWorkspaceFolderClicked(object sender, RoutedEventArgs args)
    {
        if (ApplicationBusy || dialogOpen || closing) return;
        try { workspaceFileMessage = workspaceDirectory.Open(viewModel.Home, () => viewModel.Home) ? "已请求打开当前配置目录。" : "配置目录尚不存在；首次确认保存时会创建，当前未创建目录。"; }
        catch (Exception) { workspaceFileMessage = "未能打开当前目录，请核对路径与 Windows 文件管理器。"; }
        RefreshView();
    }
    private void CopyWorkspacePathClicked(object sender, RoutedEventArgs args)
    {
        if (ApplicationBusy || dialogOpen || closing) return;
        try { CopyWorkspaceText(workspaceDirectory.CurrentPath(viewModel.Home, () => viewModel.Home)); workspaceFileMessage = "当前配置目录路径已复制。"; }
        catch (Exception) { workspaceFileMessage = "未能复制路径，请在当前工作区中选择路径文本。"; }
        RefreshView();
    }
    private sealed class SmokeWorkspaceDirectoryRuntime(MainWindow window) : IWorkspaceDirectoryRuntime
    {
        public void Open(string path)
        {
            window.smokeOpenedDirectories.Add(path);
            if (window.nativeEnvironmentAcceptance)
            {
                if (window.options.SmokeOutput is not { } output || !Path.GetFullPath(path).StartsWith(Path.GetFullPath(output) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("原生目录验收只能打开测试输出中的文件夹。");
                new NativeWorkspaceDirectoryRuntime().Open(path);
            }
        }
    }

    private void InitializeHelpTopics()
    { filteringHelp = true; HelpTopicsInput.ItemsSource = DockHelpCatalog.Topics; HelpTopicsInput.SelectedIndex = 0; filteringHelp = false; RenderHelpTopic(); }
    private void HelpSearchChanged(TextBox sender, TextBoxTextChangingEventArgs args)
    {
        if (!initialized || filteringHelp) return;
        var current = HelpTopicsInput.SelectedItem as DockHelpTopic; var matches = DockHelpCatalog.Search(sender.Text);
        filteringHelp = true; HelpTopicsInput.ItemsSource = matches; HelpTopicsInput.SelectedItem = matches.FirstOrDefault(topic => topic.Id == current?.Id) ?? matches.FirstOrDefault(); filteringHelp = false; RenderHelpTopic();
    }
    private void HelpTopicChanged(object sender, SelectionChangedEventArgs args) { if (!filteringHelp) RenderHelpTopic(); }
    private void RenderHelpTopic()
    {
        if (HelpTopicTitle is null) return;
        var topic = HelpTopicsInput.SelectedItem as DockHelpTopic;
        HelpEmptyText.Visibility = topic is null ? Visibility.Visible : Visibility.Collapsed; HelpTopicCard.Visibility = topic is null ? Visibility.Collapsed : Visibility.Visible;
        HelpTopicTitle.Text = topic?.Title ?? ""; HelpTopicText.Text = topic?.Text ?? ""; HelpTopicActionButton.Content = topic?.Action ?? "";
    }
    private void HelpTopicActionClicked(object sender, RoutedEventArgs args)
    {
        if (ApplicationBusy || dialogOpen || closing || HelpTopicsInput.SelectedItem is not DockHelpTopic topic) return;
        NavigateTo(topic.Destination switch { "preview" => FilePreviewNav, "profiles" => ProfilesNav, "gateways" => GatewaysNav, "settings" => SettingsNav, "recovery" => RecoveryNav, "updates" => UpdatesNav, _ => ConnectionNav });
    }
}
