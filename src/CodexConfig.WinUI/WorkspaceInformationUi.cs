using CodexConfig.Application;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private readonly WorkspaceDirectoryService workspaceDirectory;
    private string workspaceFileMessage = "";
#if CODEX_DOCK_WINUI_SMOKE
    private readonly List<string> smokeCopiedTexts = [], smokeOpenedDirectories = [];
#endif
    private void RefreshWorkspaceInformationControls()
    {
        var editable = !ApplicationBusy && !dialogOpen && !closing;
        OpenSavedFilesButton.IsEnabled = OpenWorkspaceFolderButton.IsEnabled = CopyWorkspacePathButton.IsEnabled = editable;
        WorkspaceFileActionStatus.Text = workspaceFileMessage; WorkspaceFileActionStatus.Visibility = workspaceFileMessage.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (currentPage == HelpNav) HelpPanel.SetNavigationEnabled(editable);
        if (currentPage == FilePreviewNav) FilePreviewPanel.Refresh();
    }

    private void ClearWorkspaceFileMessage() => workspaceFileMessage = "";

    private void InvalidateSavedPreview() => FilePreviewPanel.Invalidate();

    private async void OpenSavedFilesClicked(object sender, RoutedEventArgs args)
    {
        WorkspaceDirectoryButton.Flyout.Hide();
        if (currentPage == FilePreviewNav) await FilePreviewPanel.ReadAsync();
        else NavigateTo(FilePreviewNav);
    }

    private void CopyWorkspaceText(string value)
    {
#if CODEX_DOCK_WINUI_SMOKE
        if (options.SmokeOutput is not null && !nativeEnvironmentAcceptance) { smokeCopiedTexts.Add(value); return; }
#endif
        NativeWorkspaceClipboard.Copy(value);
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
#if CODEX_DOCK_WINUI_SMOKE
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

#endif
    private void NavigateFromHelp(string destination)
    {
        if (ApplicationBusy || dialogOpen || closing) return;
        if (Pages.FirstOrDefault(page => page.Tag == destination) is { } page) NavigateTo(page);
    }

    internal async void PageHelpClicked(object sender, RoutedEventArgs args)
    {
        if (dialogOpen || closing || sender is not FrameworkElement { Tag: string id }) return;
        var topic = DockHelpCatalog.Topics.First(item => item.Id == id);
        await ShowDialogAsync(new ContentDialog
        {
            Title = topic.Title, CloseButtonText = "关闭", XamlRoot = Root.XamlRoot,
            Content = new ScrollViewer
            {
                MaxHeight = 320, Content = new TextBlock { Text = topic.Text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }
            }
        });
    }
}
