using CodexConfig.Application;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexConfig.WinUI.Views;

public sealed partial class FilePreviewPage : UserControl
{
    private WorkspacePageOperations operations = null!;
    private CancellationTokenSource? currentRead;
    private Action<string> copy = null!;
    private Action clearDirectoryMessage = null!;
    private RoutedEventHandler? showHelp;
    internal ConfigurationPreviewService Service { get; set; } = null!;
    internal ConfigurationPreviewSnapshot? Snapshot { get; private set; }
    internal string Message { get; private set; } = "尚未读取已保存文件。";

    public FilePreviewPage() => InitializeComponent();

    internal void Connect(WorkspacePageOperations operations, ConfigurationPreviewService service,
        Action<string> copy, Action clearDirectoryMessage, RoutedEventHandler showHelp)
    {
        this.operations = operations;
        Service = service;
        this.copy = copy;
        this.clearDirectoryMessage = clearDirectoryMessage;
        this.showHelp = showHelp;
    }

    internal bool IsReading => operations.Busy.IsActive(WindowOperation.ReadSavedPreview);

    internal bool HasCurrentSnapshot => Snapshot is { } snapshot && string.Equals(snapshot.Home,
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(operations.CurrentHome())), StringComparison.OrdinalIgnoreCase);

    internal void Refresh() => SetAvailability(operations.CanInteract(), IsReading, operations.IsClosing(),
        HasCurrentSnapshot && SelectedSection?.State == ConfigurationPreviewState.Ready, Message);

    internal void Invalidate()
    {
        currentRead?.Cancel();
        Snapshot = null;
        Message = "尚未读取当前已保存文件，或文件已在本窗口更改。请重新读取。";
        clearDirectoryMessage();
        Clear();
    }

    internal async Task ReadAsync()
    {
        if (!operations.CanBegin()) return;
        Invalidate();
        using var operation = operations.Busy.Begin(WindowOperation.ReadSavedPreview, "正在读取已保存文件…");
        Message = "正在读取已保存文件…";
        operations.Refresh();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(operations.Lifetime);
        currentRead = cancellation;
        try
        {
            var result = await Service.ReadAsync(operations.CurrentHome(), operations.CurrentHome, cancellation.Token);
            if (operations.IsClosing()) return;
            Snapshot = result;
            Message = $"读取时间：{result.ReadAt.LocalDateTime:g}\n目录：{result.Home}\n已保存文件快照 · 未保存草稿未参与 · 重新读取可更新结果。";
        }
        catch (OperationCanceledException) { Message = "读取已取消或目录/文件发生变化，请重新读取。"; }
        catch (Exception) { Message = "暂时无法读取已保存文件，请检查目录与权限后重试。"; }
        finally
        {
            currentRead = null;
            operation.Dispose();
            if (!operations.IsClosing()) RenderSelection();
        }
    }

    private void RenderSelection()
    {
        Render(SelectedSection);
        operations.Refresh();
    }

    internal void CopySavedPreviewClicked(object sender, RoutedEventArgs args)
    {
        if (!operations.CanInteract() || !HasCurrentSnapshot || SelectedSection is not { State: ConfigurationPreviewState.Ready } section) return;
        try
        {
            copy(section.Text);
            Message = "所选脱敏内容已复制。快照读取时间：" + Snapshot!.ReadAt.LocalDateTime.ToString("g");
        }
        catch (Exception) { Message = "未能复制，请在只读文本框中选择所需的脱敏内容。"; }
        operations.Refresh();
    }

    internal void CancelSavedPreviewClicked(object sender, RoutedEventArgs args) => currentRead?.Cancel();
    private async void ReadSavedPreviewClicked(object sender, RoutedEventArgs args) => await ReadAsync();
    private void PageHelpClicked(object sender, RoutedEventArgs args) => showHelp?.Invoke(sender, args);
    private void SavedPreviewSectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (operations?.IsInitialized() == true) RenderSelection();
    }

    internal ConfigurationPreviewSection? SelectedSection =>
        SavedPreviewSectionInput.SelectedIndex switch { 1 => Snapshot?.Auth, 2 => Snapshot?.Environment, _ => Snapshot?.Config };

    private void SetAvailability(bool editable, bool reading, bool closing, bool canCopy, string message)
    {
        ReadSavedPreviewButton.IsEnabled = editable;
        CancelSavedPreviewButton.Visibility = reading ? Visibility.Visible : Visibility.Collapsed;
        CancelSavedPreviewButton.IsEnabled = reading;
        SavedPreviewSectionInput.IsEnabled = !reading && !closing;
        CopySavedPreviewButton.IsEnabled = editable && canCopy;
        SavedPreviewStatus.Text = message;
    }

    private void Clear()
    {
        SavedPreviewText.Text = "";
        SavedPreviewSectionStatus.Text = "";
    }

    private void Render(ConfigurationPreviewSection? section)
    {
        SavedPreviewText.Text = section?.Text ?? "尚未读取当前目录。";
        SavedPreviewSectionStatus.Text = section switch
        {
            null => "",
            { State: ConfigurationPreviewState.Missing } => "未设置或文件不存在",
            { State: ConfigurationPreviewState.Unreadable } => "未能读取此项；其他可读项仍可查看",
            { Truncated: true } => "脱敏快照 · 显示长度已达上限",
            _ => "脱敏只读快照"
        };
    }

}
