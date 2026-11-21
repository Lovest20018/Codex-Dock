using CodexConfig.Application;
using CodexConfig.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexConfig.WinUI.Views;

public sealed partial class DiagnosticsPage : UserControl
{
    private WorkspacePageOperations operations = null!;
    private RoutedEventHandler? openSources;

    public DiagnosticsPage() => InitializeComponent();

    internal void Connect(WorkspacePageOperations operations, RoutedEventHandler openSources)
    {
        this.operations = operations;
        this.openSources = openSources;
    }

    internal SessionDiagnosticService Service { get; set; } = new();
    internal SessionDiagnosticListing? Listing { get; private set; }
    private CancellationTokenSource? diagnosticCancellation;
    internal bool IsReading => operations.Busy.IsActive(WindowOperation.ReadDiagnostics);
    private string diagnosticMessage = "尚未读取。刷新后请选择明确对应本次操作的聊天记录。";
    internal void Refresh()
    {
        if (DiagnosticFilesInput is null) return;
        var editable = operations.CanBegin();
        DiagnosticFilesInput.IsEnabled = RefreshDiagnosticsButton.IsEnabled = editable;
        ReadDiagnosticButton.IsEnabled = editable && Listing is not null && DiagnosticFilesInput.SelectedItem is SessionDiagnosticFile;
        CancelDiagnosticButton.Visibility = IsReading ? Visibility.Visible : Visibility.Collapsed;
        CancelDiagnosticButton.IsEnabled = IsReading;
        DiagnosticStatusText.Text = diagnosticMessage;
    }
    internal void ResetHome()
    {
        diagnosticCancellation?.Cancel(); Listing = null; DiagnosticFilesInput.ItemsSource = Array.Empty<SessionDiagnosticFile>();
        DiagnosticResultText.Text = "尚未读取所选聊天。"; diagnosticMessage = "尚未读取当前目录。刷新后请选择明确对应本次操作的聊天记录。";
    }
    private void DiagnosticSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        diagnosticCancellation?.Cancel(); DiagnosticResultText.Text = "尚未读取所选聊天。";
        if (operations?.IsInitialized() == true) operations.Refresh();
    }
    private async void RefreshDiagnosticsClicked(object sender, RoutedEventArgs args) => await ReadListAsync();
    internal async Task ReadListAsync()
    {
        if (!operations.CanBegin()) return;
        ResetHome(); using var operation = operations.Busy.Begin(WindowOperation.ReadDiagnostics, "正在读取聊天诊断…"); diagnosticMessage = "正在列出当前目录的近期聊天…"; operations.Refresh();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(operations.Lifetime); diagnosticCancellation = cancellation;
        try
        {
            var listing = await Service.ListAsync(operations.CurrentHome(), operations.CurrentHome, cancellation.Token);
            if (operations.IsClosing()) return;
            Listing = listing; DiagnosticFilesInput.ItemsSource = listing.Files;
            diagnosticMessage = listing.Files.Count == 0 ? "此目录未发现可读取的近期聊天记录。" : "已列出 " + listing.Files.Count + " 条近期记录。选择后点击读取。";
        }
        catch (OperationCanceledException) { diagnosticMessage = "列表读取已取消，请重新刷新。"; }
        catch (Exception) { diagnosticMessage = "暂时无法列出本机聊天，请检查当前目录和读取权限。"; }
        finally { diagnosticCancellation = null; operation.Dispose(); if (!operations.IsClosing()) operations.Refresh(); }
    }
    private async void ReadDiagnosticClicked(object sender, RoutedEventArgs args) => await ReadSelectionAsync();
    internal async Task ReadSelectionAsync()
    {
        if (!operations.CanBegin() || Listing is not { } listing || DiagnosticFilesInput.SelectedItem is not SessionDiagnosticFile file) return;
        using var operation = operations.Busy.Begin(WindowOperation.ReadDiagnostics, "正在读取聊天诊断…"); DiagnosticResultText.Text = "正在读取所选聊天的参数…"; diagnosticMessage = "正在读取参数元数据…"; operations.Refresh();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(operations.Lifetime); diagnosticCancellation = cancellation;
        try
        {
            var observed = await Service.ReadAsync(listing, file, () => (operations.CurrentHome(), (DiagnosticFilesInput.SelectedItem as SessionDiagnosticFile)?.Path), cancellation.Token);
            if (operations.IsClosing()) return;
            DiagnosticResultText.Text = SessionDiagnosticService.Describe(observed); diagnosticMessage = "已读取所选聊天的客户端参数记录。";
        }
        catch (OperationCanceledException) { if (!operations.IsClosing()) DiagnosticResultText.Text = diagnosticMessage = "读取已取消或选择已改变，请重新读取。"; }
        catch (Exception) { if (!operations.IsClosing()) DiagnosticResultText.Text = diagnosticMessage = "暂时无法读取所选聊天，请刷新列表后重试。"; }
        finally { diagnosticCancellation = null; operation.Dispose(); if (!operations.IsClosing()) operations.Refresh(); }
    }
    internal void CancelDiagnosticClicked(object sender, RoutedEventArgs args) => diagnosticCancellation?.Cancel();

    private void OpenSourcesClicked(object sender, RoutedEventArgs args) => openSources?.Invoke(sender, args);
}
