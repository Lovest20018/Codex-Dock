using System.Windows;
using System.Windows.Controls;
using CodexConfig.Core;
using CodexConfig.Application;

namespace CodexConfig.App;

public partial class MainWindow
{
    private void ShowSessionDiagnostics(object sender, RoutedEventArgs e)
    {
        var directory = home;
        var service = new SessionDiagnosticService();
        using var lifetime = new CancellationTokenSource();
        SessionDiagnosticListing? listing = null;
        CancellationTokenSource? reading = null;
        var closed = false;
        var content = new StackPanel { Margin = new(22) };
        var window = new Window { Owner = this, Title = "本机聊天参数", Width = 760, Height = 500, MinWidth = 560, MinHeight = 380,
            Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        content.Children.Add(new TextBlock { Text = "选择要核对的聊天", FontSize = 20, FontWeight = FontWeights.SemiBold });
        content.Children.Add(HelpContent.Note("只读取当前配置目录中的参数元数据。这里是客户端记录；实际服务档位与费用仍需对照服务后台。", new(0, 8, 0, 16)));
        var files = new ComboBox { DisplayMemberPath = "Name", Margin = new(0, 0, 0, 12) }; content.Children.Add(files);
        var read = new Button { Content = "读取所选聊天", HorizontalAlignment = HorizontalAlignment.Left }; content.Children.Add(read);
        var result = new TextBlock { Text = "尚未读取。请选择明确对应本次操作的聊天记录。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 16, 0, 0) }; content.Children.Add(result);
        window.Closed += (_, _) => { closed = true; lifetime.Cancel(); reading?.Cancel(); };
        files.SelectionChanged += (_, _) => { reading?.Cancel(); result.Text = "尚未读取所选聊天。"; };
        window.Loaded += async (_, _) =>
        {
            try { listing = await service.ListAsync(directory, () => home, lifetime.Token); if (!closed) files.ItemsSource = listing.Files; }
            catch (Exception) { if (!closed) result.Text = "暂时无法列出本机聊天记录。"; }
        };
        read.Click += async (_, _) =>
        {
            if (files.SelectedItem is not SessionDiagnosticFile file || listing is null || reading is not null) return;
            read.IsEnabled = false;
            reading = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            try
            {
                var observed = await service.ReadAsync(listing, file, () => (home, (files.SelectedItem as SessionDiagnosticFile)?.Path), reading.Token);
                if (!closed) result.Text = SessionDiagnosticService.Describe(observed);
            }
            catch (OperationCanceledException) { if (!closed) result.Text = "读取已取消，请重新选择并读取。"; }
            catch (Exception) { if (!closed) result.Text = "暂时无法读取该聊天；请选择当前目录中的记录重试。"; }
            finally { reading.Dispose(); reading = null; if (!closed) read.IsEnabled = true; }
        };
        window.ShowDialog();
    }
}
