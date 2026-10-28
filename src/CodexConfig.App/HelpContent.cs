using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using CodexConfig.Core;

namespace CodexConfig.App;

internal static class HelpContent
{
    internal static TextBlock Note(string text, Thickness? margin = null)
    {
        var block = new TextBlock { Text = text, Margin = margin ?? new Thickness(0, 5, 0, 0), TextWrapping = TextWrapping.Wrap };
        block.SetResourceReference(FrameworkElement.StyleProperty, "Helper");
        return block;
    }

    internal static FrameworkElement Details(string path, CatalogEntry? entry = null)
    {
        var body = new StackPanel();
        var technical = new StackPanel();
        var help = ConfigHelp.Find(path);
        technical.Children.Add(new TextBlock { Text = path, FontFamily = new("Consolas"), FontSize = 12, TextWrapping = TextWrapping.Wrap });
        if (help is not null)
        {
            Add(body, "怎么选", help.ValueHelp); Add(body, "建议", help.Usage); Add(body, "注意", help.Limitations);
            Add(technical, "作用", help.Effect); Add(technical, "默认值", help.DefaultBehavior);
            if (help.RelatedKeys.Length > 0) Add(technical, "相关设置", string.Join("、", help.RelatedKeys.Select(ConfigHelp.Name)));
            var status = help.VerificationStatus switch { "verified" => "已核对官方文档", "local" => "本工具的连接规则", _ => "历史目录说明 · 当前版本待验证" };
            Add(technical, "说明来源", status + " · " + help.VerifiedAt);
            foreach (var source in help.Sources) technical.Children.Add(SourceLink(source.Title, source.Url));
        }
        else
        {
            Add(body, "说明", entry?.Chinese ?? "暂无说明，请确认 Codex 支持此设置。");
            Add(body, "注意", entry?.Category == "unlisted" ? "未收录的字段只检查 TOML 写法。" : "此说明来自旧版字段表，当前版本可能有变化。");
            if (entry is not null)
            {
                Add(technical, "字段状态", entry.Lifecycle + " · " + entry.Kind);
                Add(technical, "旧版默认值", entry.Default.ValueKind is System.Text.Json.JsonValueKind.Undefined or System.Text.Json.JsonValueKind.Null ? "未知或由模型决定" : entry.Default.GetRawText());
                if (!string.IsNullOrWhiteSpace(entry.Description)) technical.Children.Add(new Expander { Header = "英文原文", Content = Note(entry.Description), Margin = new(0, 8, 0, 0) });
            }
        }
        Add(technical, "跟随默认", "删除本文件中的设置，让 Codex 使用默认值。项目配置、会话或组织策略可能覆盖此处设置，实际运行值未检测。");
        body.Children.Add(new Expander { Header = "字段与来源", Content = technical, Margin = new(0, 10, 0, 0) });
        return body;

        static void Add(Panel panel, string name, string? text) { if (!string.IsNullOrWhiteSpace(text)) panel.Children.Add(Note(name + "：" + text, new(0, 8, 0, 0))); }
    }

    internal static TextBlock SourceLink(string title, string url)
    {
        var block = Note("", new(0, 8, 0, 0));
        var link = new Hyperlink(new Run(title)) { NavigateUri = new Uri(url) };
        link.SetResourceReference(TextElement.ForegroundProperty, "Accent");
        link.RequestNavigate += (_, e) =>
        {
            if (e.Uri.Scheme == Uri.UriSchemeHttps)
            {
                try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
                catch (Exception) { MessageBox.Show("无法打开浏览器。文档地址：" + e.Uri.AbsoluteUri, "配置说明"); }
            }
            e.Handled = true;
        };
        block.Inlines.Add(link); return block;
    }
}
