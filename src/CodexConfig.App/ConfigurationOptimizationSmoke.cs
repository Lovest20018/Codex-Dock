using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodexConfig.Core;

namespace CodexConfig.App;

public partial class MainWindow
{
    private async Task RunConfigurationOptimizationSmokeAsync(string output, List<string> checks)
    {
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        var fixture = Path.Combine(output, "configuration-fixture"); Directory.CreateDirectory(fixture);
        var catalog = Path.Combine(fixture, "models.json");
        File.WriteAllBytes(catalog, NativeModelCatalog.Convert(Encoding.UTF8.GetBytes("""
            {"models":[{"slug":"fixture-high","supported_reasoning_levels":[{"effort":"high","description":"Fixture high"},{"effort":"xhigh","description":"Fixture xhigh"}],"service_tiers":[{"id":"priority","name":"Fast","description":"Fixture only"}],"context_window":123456},{"slug":"fixture-low","supported_reasoning_levels":[{"effort":"low","description":"Fixture low"}],"service_tiers":[]}]}
            """)));
        var text = "model='fixture-high'\nmodel_provider='merchant.v1'\nmodel_catalog_json=" + TomlDocument.Quote(catalog) + "\n" +
            "[mcp_servers.'alpha.one']\ncommand='fixture-command'\nenabled=false\n[mcp_servers.second]\nurl='https://example.invalid/mcp'\n" +
            "[model_providers.'merchant.v1']\nname='Merchant'\nbase_url='https://example.invalid/v1'\nwire_api='responses'\n" +
            "[model_providers.'merchant.v1'.capabilities]\nremote_compaction='v2'\n[model_providers.second]\nname='Second'\n[future]\nflag=false\n";
        var config = Encoding.UTF8.GetBytes(text); File.WriteAllBytes(Path.Combine(fixture,"config.toml"), config);
        var edits = new Dictionary<string,string?>(); var editor = new SettingsEditor(); var tools = new ConfigurationTools(lifetime.Token);
        editor.SetHeader(tools); editor.Configure(config, edits); editor.SetContext(fixture, "fixture-high"); tools.Configure(fixture,config,"fixture-high");
        var window = new Window { Owner = this, Title = "配置优化隔离验证", Content = editor, Width = 960, Height = 800, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.SetResourceReference(BackgroundProperty,"Canvas");
        window.Show();
        try
        {
            editor.ShowConfigured(); window.UpdateLayout();
            Check(editor.Scope == "configured" && editor.VisibleCount == TomlDocument.ValuePaths(new TomlDocument(config).Model).Count(), "Configured view covers every explicit leaf");
            Check(((ComboBox)editor.Input("mcp_servers.\"alpha.one\".enabled")).SelectedItem is SettingsEditor.ValueChoice { Repr: "false" }, "Explicit false remains distinct from absent");
            editor.Filter("model_providers.<name>.capabilities.remote_compaction");
            Check(editor.VisibleCount == 2 && ((ComboBox)editor.Input("model_providers.\"merchant.v1\".capabilities.remote_compaction")).SelectedItem is SettingsEditor.ValueChoice { Repr: "\"v2\"" }, "Dynamic provider names automatically expand including a dotted id");
            editor.Filter("mcp_servers.\"alpha.one\".tool_timeout_sec"); ((TextBox)editor.Input("mcp_servers.\"alpha.one\".tool_timeout_sec")).Text = "-1";
            Check(editor.Errors.Count > 0, "Invalid MCP timeout appears before save");
            ((TextBox)editor.Input("mcp_servers.\"alpha.one\".tool_timeout_sec")).Text = "30";
            Check(editor.Errors.Count == 0 && edits.ContainsKey("mcp_servers.\"alpha.one\".tool_timeout_sec"), "MCP edit targets the quoted concrete service");
            editor.Filter("model_reasoning_effort"); var reasoning = (ComboBox)editor.Input("model_reasoning_effort");
            Check(reasoning.Items.Cast<SettingsEditor.ValueChoice>().Count() == 3 && reasoning.Items.Cast<SettingsEditor.ValueChoice>().All(item => item.Repr != "\"low\""), "Reasoning choices come from the selected model catalog");
            reasoning.SelectedItem = reasoning.Items.Cast<SettingsEditor.ValueChoice>().Single(item => item.Repr == "\"high\"");
            editor.SetContext(fixture,"fixture-low"); editor.Revalidate(); Check(editor.Errors.ContainsKey("model_reasoning_effort"), "Changing models exposes unsupported retained reasoning overrides");
            editor.SetContext(fixture,"fixture-high"); editor.Revalidate(); Check(editor.Errors.Count == 0, "Supported reasoning clears the validation error");
            editor.Filter("service_tier"); var tier = (ComboBox)editor.Input("service_tier");
            Check(tier.Items.Cast<SettingsEditor.ValueChoice>().Any(item => item.Repr == "\"priority\"") && tier.Items.Cast<SettingsEditor.ValueChoice>().All(item => item.Repr != "\"ultrafast\""), "Tier choices do not invent unsupported service modes");
            editor.Filter("approval_policy"); var policy = (ComboBox)editor.Input("approval_policy");
            policy.SelectedItem = policy.Items.Cast<SettingsEditor.ValueChoice>().Single(item => item.Repr == "$granular");
            Check(editor.Errors.Count > 0, "Granular approval requires explicit values for mandatory fields");
            foreach (var required in ConfigSchema.Instance.Find("approval_policy.granular")!.RequiredProperties)
            {
                var field = (ComboBox)editor.Input("approval_policy.granular." + required);
                field.SelectedItem = field.Items.Cast<SettingsEditor.ValueChoice>().Single(item => item.Repr == "false");
            }
            Check(editor.Errors.Count == 0 && edits["approval_policy"]!.Contains("mcp_elicitations = false"), "Granular form stages all required choices");
            window.UpdateLayout(); CaptureElement(window,output,"configuration-approval.png");
            checks.Add("Granular approval form validates mandatory choices before saving");
            editor.ShowChanged(); window.UpdateLayout(); CaptureElement(window, output, "configuration-changes.png");
            editor.ShowConfigured(); window.UpdateLayout(); CaptureElement(window, output, "configuration-configured.png");
            if (Environment.GetEnvironmentVariable("CODEX_CONFIG_TEST_CLI") is { Length: > 0 } cli)
            {
                tools.IsExpanded = true; await tools.ReadForSmokeAsync(cli,fixture); window.UpdateLayout();
                Check(tools.Snapshot is { Rows.Count: > 0 }, "Source UI reads the actual native config/read response without a turn");
                checks.Add("Source layer UI reads native config/read without starting a turn");
                CaptureElement(window,output,"configuration-sources.png");
            }
            Check(File.ReadAllBytes(Path.Combine(fixture,"config.toml")).SequenceEqual(config), "Editor and source reads do not persist drafts");
            foreach (var kind in new[] { 0, 1, 2 })
            {
                var instance = editor.CreateInstanceEditor(); instance.Show(); instance.UpdateLayout();
                try
                {
                    FindVisual<ComboBox>(instance).Single(control => System.Windows.Automation.AutomationProperties.GetName(control) == "类型").SelectedIndex = kind;
                    TextBox Field(string label) => FindVisual<TextBox>(instance).Single(control => System.Windows.Automation.AutomationProperties.GetName(control) == label);
                    Field("名称（点号会作为名称的一部分保存）").Text = new[] { "added.service", "added.provider", "review" }[kind];
                    if (kind == 0) Field("命令（保存时不会执行）").Text = "fixture-added-command";
                    else if (kind == 1) Field("模型入口 URL").Text = "https://added.invalid/v1";
                    else Field("说明").Text = "Fixture permission profile";
                    FindVisual<Button>(instance).Single(button => button.Content as string == "添加到草稿").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var expected = new[] { "mcp_servers.\"added.service\".command", "model_providers.\"added.provider\".base_url", "permissions.review.extends" }[kind];
                    Check(edits.ContainsKey(expected), "Structured instance form stages concrete fields: " + expected);
                    Check(!edits.ContainsKey("default_permissions"), "Adding permissions never activates or expands the default policy");
                }
                finally { instance.Close(); }
            }
            checks.Add("Structured MCP, provider and permission forms stage isolated concrete instances");
            checks.AddRange(["Configured view uses concrete MCP and provider instances including dotted names", "Explicit false differs from absent", "MCP group validation appears while editing", "Model catalog controls reasoning and service tier choices", "Unsupported reasoning survives model changes with an error"]);
        }
        finally { window.Close(); }
        var profileWindow = new ProfileWindow(fixture,"fixture-high", true) { Owner = this }; profileWindow.Show();
        try
        {
            profileWindow.UpdateLayout(); var profileEditor = FindVisual<SettingsEditor>(profileWindow).Single();
            profileEditor.Filter("model_reasoning_effort"); var input = (ComboBox)profileEditor.Input("model_reasoning_effort");
            input.SelectedItem = input.Items.Cast<SettingsEditor.ValueChoice>().Single(item => item.Repr == "\"high\"");
            FindVisual<Button>(profileWindow).Single(button => button.Content as string == "预览此 Profile").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var saveProfile = FindVisual<Button>(profileWindow).Single(button => button.Content as string == "保存此 Profile");
            Check(saveProfile.IsEnabled, "Profile preview enables its independent save action");
            profileWindow.UpdateLayout(); CaptureElement(profileWindow,output,"configuration-profile-preview.png");
            saveProfile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(new TomlDocument(new ProfileStore().Load(fixture,"daily").Config).String("model_reasoning_effort") == "high", "Profile saves to a new named file");
            Check(File.ReadAllBytes(Path.Combine(fixture,"config.toml")).SequenceEqual(config), "Profile UI leaves the base config unchanged");
            checks.AddRange(["Profile UI previews and independently saves ordinary settings", "New profile files preserve base configuration"]);
        }
        finally { profileWindow.Close(); }
    }
    private static void CaptureElement(FrameworkElement element, string output, string name)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32);
        bitmap.Render(element); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(output,name)); encoder.Save(file);
    }
}
