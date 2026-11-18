using CodexConfig.Application;
using CodexConfig.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace CodexConfig.WinUI;

public sealed partial class MainWindow
{
    private ConfigurationSourceService configurationSources = new();
    private ConfigurationReadResult? sourceResult;
    private CancellationTokenSource? sourceCancellation;
    private string? automaticSourceExecutable;
    private bool readingSource;
    private string sourceMessage = "尚未读取当前条件。只比较已保存的文件，主配置与 Profile 草稿均不参与。";
    private int sourcePage;
    private ConfigurationSourceRequest SourceRequest() => new(viewModel.Home, SourceExecutableInput.Text, SourceProjectInput.Text,
        string.IsNullOrWhiteSpace(SourceProfileInput.Text) ? null : SourceProfileInput.Text.Trim());
    private void RefreshSourceControls()
    {
        if (ReadSourceButton is null) return;
        var editable = !ManualApplicationBusy && !dialogOpen && !closing;
        SourceExecutableInput.IsEnabled = SourceProjectInput.IsEnabled = SourceProfileInput.IsEnabled = FindSourceExecutableButton.IsEnabled = ChooseSourceExecutableButton.IsEnabled = ChooseSourceProjectButton.IsEnabled = ReadSourceButton.IsEnabled = editable;
        CancelSourceButton.Visibility = readingSource ? Visibility.Visible : Visibility.Collapsed; CancelSourceButton.IsEnabled = readingSource;
        SourceStatusText.Text = readingSource ? "正在通过所选执行端读取本机配置层…" : sourceMessage;
    }
    private void InvalidateSource()
    { sourceResult = null; sourcePage = 0; sourceMessage = "读取条件或文件已改变，请重新读取。未保存草稿不参与本次比较。"; SourceRows.Children.Clear(); SourceLayersText.Text = SourceCountText.Text = ""; PreviousSourceButton.IsEnabled = NextSourceButton.IsEnabled = false; InvalidateFeatures(); }
    private void SourceConditionChanged(TextBox sender, TextBoxTextChangingEventArgs args)
    {
        if (!initialized || updatingControls) return;
        if (sender == SourceExecutableInput && sender.Text != automaticSourceExecutable) automaticSourceExecutable = null;
        InvalidateSource(); RefreshView();
    }
    private void RefreshAutomaticSourceExecutable(bool force = false)
    {
        if (!force && automaticSourceExecutable is null) return;
        automaticSourceExecutable = CodexExecutableLocator.Find() ?? "";
        SourceExecutableInput.Text = automaticSourceExecutable;
    }
    private void FindSourceExecutableClicked(object sender, RoutedEventArgs args)
    {
        if (ManualApplicationBusy || dialogOpen || closing) return;
        RefreshAutomaticSourceExecutable(force: true);
        sourceMessage = automaticSourceExecutable?.Length > 0 ? "已找到本机执行端，可读取配置来源。" : "没有找到本机执行端。可安装 Codex，或手动选择 codex.exe。";
        RefreshView();
    }
    private void SourceSearchChanged(TextBox sender, TextBoxTextChangingEventArgs args) { if (initialized && !updatingControls) { sourcePage = 0; RenderSourceRows(); } }
    private async void ReadSourceClicked(object sender, RoutedEventArgs args) => await ReadConfigurationSourcesAsync();
    private void CancelSourceClicked(object sender, RoutedEventArgs args) => sourceCancellation?.Cancel();
    private async Task ReadConfigurationSourcesAsync()
    {
        if (ManualApplicationBusy || dialogOpen || closing) return;
        RefreshAutomaticSourceExecutable();
        InvalidateSource(); readingSource = true; RefreshView();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); sourceCancellation = cancellation;
        try
        {
            await automaticEnhancements.CancelAndWaitAsync(); var request = SourceRequest();
            var result = await configurationSources.ReadAsync(request, SourceRequest, cancellation.Token);
            if (closing) return;
            sourceResult = result;
            sourceMessage = "本机执行端快照 · " + result.ReadAt.LocalDateTime.ToString("g") + "\n执行端：" + result.Executable +
                "\n内核报告：" + (result.RuntimeInfo.Length > 0 ? result.RuntimeInfo : "未提供版本标识") + "\n比较文件：" + (request.Profile is null ? "config.toml" : request.Profile + ".config.toml") +
                "\n主配置与 Profile 草稿未参与读取；此结果不代表已打开聊天的参数。";
            SourceLayersText.Text = string.Join("\n", result.Layers.Select(layer => layer.Source + " · " + layer.State)); RenderSourceRows();
        }
        catch (OperationCanceledException error) { sourceMessage = error.Message.Contains("条件") ? error.Message : "读取已取消或超时，生效来源未确认。"; }
        catch (NotSupportedException error) { sourceMessage = error.Message; }
        catch (Exception) { sourceMessage = "无法读取配置层。若已更新 Codex，可点“自动查找”或重新选择执行端。也可核对项目与 Profile。"; }
        finally { sourceCancellation = null; readingSource = false; if (!closing) RefreshView(); }
    }
    private void RenderSourceRows()
    {
        SourceRows.Children.Clear(); var search = SourceSearchInput.Text.Trim();
        var rows = sourceResult?.Rows.Where(row => (row.Path + " " + row.Source).Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray() ?? [];
        var pages = Math.Max(1, (rows.Length + 19) / 20); sourcePage = Math.Clamp(sourcePage, 0, pages - 1);
        foreach (var row in rows.Skip(sourcePage * 20).Take(20))
        {
            var body = new StackPanel { Spacing = 8 }; body.Children.Add(new TextBlock { Text = row.Path, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            body.Children.Add(new TextBlock { Text = "比较文件值：" + row.FileValue + "\n执行端读取值：" + row.EffectiveValue + "\n来源：" + row.Source + "\n作用范围：" + row.Scope, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, Opacity = 0.8 });
            SourceRows.Children.Add(new Border { Child = body, Padding = new(16), CornerRadius = new(8), Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(12, 128, 128, 128)) });
        }
        SourceCountText.Text = sourceResult is null ? "" : rows.Length + " 项 · 第 " + (sourcePage + 1) + " / " + pages + " 页";
        PreviousSourceButton.IsEnabled = sourceResult is not null && sourcePage > 0; NextSourceButton.IsEnabled = sourceResult is not null && sourcePage + 1 < pages;
    }
    private void PreviousSourceClicked(object sender, RoutedEventArgs args) { sourcePage--; RenderSourceRows(); }
    private void NextSourceClicked(object sender, RoutedEventArgs args) { sourcePage++; RenderSourceRows(); }
    private async void ChooseSourceExecutableClicked(object sender, RoutedEventArgs args)
    {
        if (ManualApplicationBusy || dialogOpen || closing) return;
        dialogOpen = true; RefreshView();
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder }; picker.FileTypeFilter.Add(".exe");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var file = await picker.PickSingleFileAsync();
            if (file is not null) { automaticSourceExecutable = null; SourceExecutableInput.Text = file.Path; InvalidateSource(); }
        }
        catch (Exception) { sourceMessage = "未选择执行端，请检查本机路径。"; }
        finally { dialogOpen = false; RefreshView(); }
    }
    private async void ChooseSourceProjectClicked(object sender, RoutedEventArgs args)
    {
        if (ManualApplicationBusy || dialogOpen || closing) return;
        dialogOpen = true; RefreshView();
        try
        {
            var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder }; picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var folder = await picker.PickSingleFolderAsync(); if (folder is not null) { SourceProjectInput.Text = folder.Path; InvalidateSource(); }
        }
        catch (Exception) { sourceMessage = "未选择项目目录，请检查本机路径。"; }
        finally { dialogOpen = false; RefreshView(); }
    }
}
