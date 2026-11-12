using System.Windows;
using System.Windows.Controls;
using CodexConfig.Core;

namespace CodexConfig.App;

public partial class MainWindow
{
    private void ShowSessionDiagnostics(object sender, RoutedEventArgs e)
    {
        var directory = home;
        var content = new StackPanel { Margin = new(22) };
        var window = new Window { Owner = this, Title = "本机聊天参数", Width = 760, Height = 500, MinWidth = 560, MinHeight = 380,
            Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        content.Children.Add(new TextBlock { Text = "选择要核对的聊天", FontSize = 20, FontWeight = FontWeights.SemiBold });
        content.Children.Add(HelpContent.Note("只读取当前配置目录中的参数元数据。这里是客户端记录；实际服务档位与费用仍需对照服务后台。", new(0, 8, 0, 16)));
        var files = new ComboBox { DisplayMemberPath = "Name", Margin = new(0, 0, 0, 12) }; content.Children.Add(files);
        var read = new Button { Content = "读取所选聊天", HorizontalAlignment = HorizontalAlignment.Left }; content.Children.Add(read);
        var result = new TextBlock { Text = "尚未读取。请选择明确对应本次操作的聊天记录。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 16, 0, 0) }; content.Children.Add(result);
        try { files.ItemsSource = SessionDiagnostics.List(directory); }
        catch (Exception) { result.Text = "暂时无法列出本机聊天记录。"; }
        read.Click += async (_, _) =>
        {
            if (files.SelectedItem is not SessionDiagnosticFile file) return;
            read.IsEnabled = false;
            try
            {
                var observed = await Task.Run(() => SessionDiagnostics.Read(directory, file.Path));
                result.Text = "记录时间：" + (observed.ObservedAt?.LocalDateTime.ToString("g") ?? "未提供") +
                    "\n主模型：" + (observed.Model ?? "未观察到") + "\n推理强度：" + (observed.Reasoning ?? "未观察到") +
                    "\n速度档位：" + (observed.ServiceTier ?? "未观察到") + "\n审批方式：" + (observed.Reviewer ?? "未观察到") +
                    "\n实际审核模型：本记录未提供，不从配置值推断" +
                    (observed.ContainsConfigurationUpdate ? "\n已读取会话中的推理档位更新；顶层请求值可能保留初始基线。" : "") + "\n\n来源：" + observed.File;
            }
            catch (Exception) { result.Text = "暂时无法读取该聊天；请选择当前目录中的记录重试。"; }
            finally { read.IsEnabled = true; }
        };
        window.ShowDialog();
    }
}
