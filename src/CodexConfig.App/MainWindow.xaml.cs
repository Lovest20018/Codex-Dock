using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CodexConfig.Core;
using Microsoft.Win32;

namespace CodexConfig.App;

public partial class MainWindow : Window
{
    private readonly ConfigStore store;
    private readonly string? smokeOutput;
    private readonly IUserEnvironment environment;
    private readonly ModelCatalogClient probe = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly DispatcherTimer previewTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly Dictionary<string, string?> detailedChanges = new(StringComparer.Ordinal);
    private string home;
    private string baselineFields = "";
    private string currentPage = "overview";
    private string currentTab = "changes";
    private TemplateApplication? lastTemplate;
    private ConnectionSettings loadedSettings = new("", "", "gpt-6.1-sol");
    private ConfigSnapshot? snapshot;
    private ConfigPlan? preview;
    private bool initializing = true;
    private bool busy;
    private bool closing;
    private byte[]? pendingOfficialAuth;
    private string? codexExecutable = CodexLoginClient.FindExecutable();
    private CancellationTokenSource? loginCancellation;
    private IInputElement? overlayFocus;
    private bool Dirty => pendingOfficialAuth is not null || detailedChanges.Count > 0 || Fields() != baselineFields;
    private bool ConnectionChanged => BaseUrlBox.Text != loadedSettings.BaseUrl || ApiKeyBox.Password != loadedSettings.ApiKey ||
        KindBox.SelectedIndex != (int)loadedSettings.Kind || AuthenticationBox.SelectedIndex != (int)loadedSettings.Authentication ||
        ConnectionModeBox.SelectedIndex != (int)loadedSettings.Mode || GatewayProxyBox.IsChecked != loadedSettings.UseSystemProxy || pendingOfficialAuth is not null;

    public MainWindow(ConfigStore store, string home, string? smokeOutput, IUserEnvironment environment, IStartupRegistration? startup = null)
    {
        this.store = store; this.home = home; this.smokeOutput = smokeOutput; this.environment = environment;
        this.startup = startup ?? (smokeOutput is null ? new WindowsStartupRegistration() : new MemoryStartupRegistration());
        InitializeComponent();
        LinePresetBox.ItemsSource = KrillLines.All.Select(line => line with { Name = line.Name[(line.Name.IndexOf('·') + 1)..].Trim() }).ToArray();
        ModelBox.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(FieldChanged));
        Editor.Changed += MarkDirty;
        Editor.ConnectionRequested += () => ShowPage("connection");
        Editor.SetMode(true);
        TemplateEditor.Initialize(new TemplateRepository(smokeOutput is null ? TemplateRepository.DefaultDirectory : Path.Combine(home, "editor-templates")), smokeOutput is not null);
        TemplateEditor.StatusChanged += SetStatus;
        TemplateEditor.ViewChanged += UpdateActions;
        TemplateEditor.StageRequested += StageTemplate;
        var connectionHelp = new StackPanel { Margin = new(0, 10, 0, 0) };
        foreach (var key in new[] { "connection.provider", "connection.address", "connection.credential", "connection.protocol", "model" })
            connectionHelp.Children.Add(new Expander { Header = ConfigHelp.Name(key), Content = HelpContent.Details(key), Margin = new(0, 0, 0, 10) });
        ConnectionHelp.Content = connectionHelp;
        previewTimer.Tick += (_, _) => { previewTimer.Stop(); UpdatePreview(); };
        PreviewKeyDown += HandleShortcut;
        SourceInitialized += (_, _) => ThemeManager.UpdateTitleBar(this);
        SizeChanged += (_, _) => PreviewDrawer.Width = Math.Min(490, Math.Max(320, RootPanel.ActualWidth - 90));
        InitializeDockLayout();
        SystemEvents.UserPreferenceChanged += SystemPreferenceChanged;
        InitializeGatewayUi();
        InitializeLocalCodexState();
        RefreshStartupSetting();
        AppVersionText.Text = "当前版本 " + CurrentVersion.ToString(3);
        initializing = false;
        // Populate the connection before Show can paint the window or tray setup can delay it.
        Reload();
        ShowPage("overview");
    }

    private async void WindowLoaded(object sender, RoutedEventArgs e)
    {
        if (smokeOutput is null)
        {
            await InitializeDesktopAsync();
        }
        else
        {
            try { await RunSmokeAsync(smokeOutput); Application.Current.Shutdown(0); }
            catch (Exception error)
            {
                Directory.CreateDirectory(smokeOutput);
                await File.WriteAllTextAsync(Path.Combine(smokeOutput, "failure.txt"), error.ToString());
                Application.Current.Shutdown(1);
            }
        }
    }

    private async Task InitializeDesktopAsync()
    {
        if (StartInTray)
        {
            // Logon startup must hide before the first render, after tray creation succeeds.
            InitializeTray();
            ApplyStartupVisibility();
        }
        // Let the populated window render before initializing optional background services.
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        if (closing) return;
        if (!StartInTray) InitializeTray();
        localStateTimer.Start();
        if (snapshot?.Bridge is not null && loadedSettings.Mode == ConnectionMode.OfficialWithApi) _ = RefreshBridgeAsync();
        await RefreshLocalCodexStateAsync();
    }

    private bool Reload(ConfigSnapshot? source = null)
    {
        initializing = true;
        ClearPendingLogin();
        snapshot = null;
        try
        {
            var loaded = source ?? store.Load(home);
            loadedSettings = store.ReadSettings(loaded);
            if (loaded.Config is null && loaded.Auth is null && loaded.AuthApiBaseUrl is null) loadedSettings = loadedSettings with { Kind = ConnectionKind.ResponsesApi };
            snapshot = loaded;
            externalConfigurationChanged = false; localStateError = "";
            HomePath.Text = home;
            CurrentDirectoryText.Text = home; CurrentDirectoryText.ToolTip = home;
            BaseUrlBox.Text = loadedSettings.BaseUrl;
            ConnectionNameBox.Text = loadedSettings.Details.Name; ConnectionNoteBox.Text = loadedSettings.Details.Note;
            ConnectionNoteExpander.IsExpanded = loadedSettings.Details.Note.Length > 0;
            ApiKeyBox.Password = loadedSettings.ApiKey; VisibleKeyBox.Text = loadedSettings.ApiKey;
            ModelBox.Text = loadedSettings.Model;
            KindBox.SelectedIndex = (int)loadedSettings.Kind; AuthenticationBox.SelectedIndex = (int)loadedSettings.Authentication;
            ConnectionModeBox.SelectedIndex = (int)loadedSettings.Mode;
            GatewayProxyBox.IsChecked = loadedSettings.UseSystemProxy;
            UpdateModeControls();
            SynchronizeLinePreset();
            detailedChanges.Clear();
            lastTemplate = null;
            Editor.Configure(snapshot.Config, detailedChanges);
            TemplateEditor.Configure(snapshot.Config, detailedChanges);
            baselineFields = Fields();
            var pending = store.HasPending(home);
            RestoreButton.Content = pending ? "恢复未完成的保存" : "恢复上次配置";
            RestoreButton.IsEnabled = pending || store.LatestBackup(home) is not null;
            BackupHint.Text = pending ? "发现未完成的保存，请先恢复后再编辑。" : RestoreButton.IsEnabled ? "已找到可恢复的本地备份。" : "首次保存配置后，将自动创建备份。";
            ConnectionError.Visibility = Visibility.Collapsed;
            SetStatus(pending ? "发现未完成的保存，请到备份与恢复页面处理。" : "配置已读取", pending);
            return true;
        }
        catch (Exception error) { snapshot = null; ShowError(error); return false; }
        finally
        {
            initializing = false; ConfigureGatewayView(); UpdateActions(); UpdatePreview();
            if (IsLoaded && smokeOutput is null && snapshot?.Bridge is not null && loadedSettings.Mode == ConnectionMode.OfficialWithApi) _ = RefreshBridgeAsync();
        }
    }

    private bool DetailsChanged => ConnectionNameBox.Text != loadedSettings.Details.Name || ConnectionNoteBox.Text.Replace("\r\n", "\n") != loadedSettings.Details.Note;
    private string Fields() => JsonSerializer.Serialize(new { url = BaseUrlBox.Text, key = ApiKeyBox.Password, model = ModelBox.Text, kind = KindBox.SelectedIndex, auth = AuthenticationBox.SelectedIndex, mode = ConnectionModeBox.SelectedIndex, proxy = GatewayProxyBox.IsChecked == true, details = new ConnectionDetails(ConnectionNameBox.Text, ConnectionNoteBox.Text.Replace("\r\n", "\n")) });
    private ConnectionSettings Settings(bool placeholder = false) => loadedSettings with
    {
        BaseUrl = placeholder && string.IsNullOrWhiteSpace(BaseUrlBox.Text) ? "https://relay.example.com" : BaseUrlBox.Text,
        ApiKey = placeholder && string.IsNullOrWhiteSpace(ApiKeyBox.Password) ? "preview-placeholder" : ApiKeyBox.Password,
        Model = ModelBox.Text,
        Kind = (ConnectionKind)Math.Max(0, KindBox.SelectedIndex), Authentication = (AuthenticationChoice)Math.Max(0, AuthenticationBox.SelectedIndex),
        Mode = (ConnectionMode)Math.Max(0, ConnectionModeBox.SelectedIndex),
        UseSystemProxy = GatewayProxyBox.IsChecked == true,
        Details = new(ConnectionNameBox.Text, ConnectionNoteBox.Text),
        PreservePreferenceValues = true
    };

    private void NavigationChanged(object sender, RoutedEventArgs e)
    {
        if (!initializing && sender is RadioButton { Tag: string page }) ShowPage(page);
    }
    private void ShowPage(string page)
    {
        if (page != currentPage && currentPage == "templates" && !TemplateEditor.ResolvePending())
        {
            var before = initializing; initializing = true; TemplatesNav.IsChecked = true; initializing = before; return;
        }
        if (page == "templates" && currentPage != "templates" && snapshot is not null) TemplateEditor.Configure(snapshot.Config, detailedChanges);
        var previousPage = currentPage;
        currentPage = page;
        ConnectionPage.Visibility = page == "connection" ? Visibility.Visible : Visibility.Collapsed;
        GatewayPage.Visibility = page == "gateway" ? Visibility.Visible : Visibility.Collapsed;
        Editor.Visibility = page is "preferences" or "catalog" ? Visibility.Visible : Visibility.Collapsed;
        ModelLayout.Visibility = Editor.Visibility;
        ModelControls.Visibility = page == "preferences" ? Visibility.Visible : Visibility.Collapsed;
        OverviewPage.Visibility = page == "overview" ? Visibility.Visible : Visibility.Collapsed;
        ApplicationPage.Visibility = page == "app" ? Visibility.Visible : Visibility.Collapsed;
        LibraryTabs.Visibility = page is "templates" or "backup" ? Visibility.Visible : Visibility.Collapsed;
        TemplateEditor.Margin = new Thickness(0, 52, 8, 0); BackupPage.Margin = new Thickness(0, 52, 0, 0);
        LibraryTemplatesTab.SetResourceReference(BackgroundProperty, page == "templates" ? "AccentSoft" : "Surface");
        LibraryTemplatesTab.SetResourceReference(ForegroundProperty, page == "templates" ? "Accent" : "Muted");
        LibraryBackupTab.SetResourceReference(BackgroundProperty, page == "backup" ? "AccentSoft" : "Surface");
        LibraryBackupTab.SetResourceReference(ForegroundProperty, page == "backup" ? "Accent" : "Muted");
        BackupPage.Visibility = page == "backup" ? Visibility.Visible : Visibility.Collapsed;
        TemplateEditor.Visibility = page == "templates" ? Visibility.Visible : Visibility.Collapsed;
        GlobalSaveBar.Visibility = Dirty && !(page == "templates" && TemplateEditor.IsEditing) ? Visibility.Visible : Visibility.Collapsed;
        if (page == "preferences" && previousPage != page) { Editor.Search.Text = ""; Editor.SetMode(true); }
        if (page == "catalog" && previousPage != page && Editor.Scope == "common") Editor.SetMode(false);
        RefreshOverview();
        var (title, subtitle) = page switch
        {
            "overview" => ("概览", "当前已保存的连接与本地运行状态。"),
            "preferences" => ("模型与行为", "模型、速度和常用行为，修改后统一保存。"), "catalog" => ("全部配置", "搜索完整字段、查看说明和未收录配置。"),
            "templates" or "backup" => ("模板与备份", "复用配置，或恢复上次保存前的状态。"),
            "app" => ("应用设置与帮助", "窗口、托盘与后台服务。"), "gateway" => ("网关与网络", "运行操作立即执行；网络修改保存后应用。"), _ => ("连接与账号", "官方身份与模型服务分别配置，保存时统一写入。")
        };
        PageTitle.Text = title; PageSubtitle.Text = subtitle;
        var nav = page switch { "overview" => OverviewNav, "preferences" => CatalogNav, "catalog" => AdvancedNav, "templates" or "backup" => TemplatesNav, "app" => AppNav, "gateway" => GatewayNav, _ => ConnectionNav };
        if (nav.IsChecked != true) { var before = initializing; initializing = true; nav.IsChecked = true; initializing = before; }
    }

    private void UpdateActions()
    {
        var usable = snapshot is not null && !store.HasPending(home);
        SaveButton.IsEnabled = usable && Dirty && !busy;
        UndoButton.IsEnabled = usable && Dirty && !busy;
        ProbeButton.IsEnabled = usable && !busy;
        OfficialLoginButton.IsEnabled = usable && !busy;
        SelectCodexButton.IsEnabled = usable && !busy;
        UpdateGatewayActions();
        ConnectionModeBox.IsEnabled = !busy;
        TemplatesNav.IsEnabled = usable && !busy;
        ServiceTemplateButton.IsEnabled = usable && !busy;
        UndoTemplateButton.Visibility = lastTemplate is { Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;
        UndoTemplateButton.IsEnabled = usable && !busy;
        Editor.IsEnabled = usable && !busy; TemplateEditor.IsEnabled = usable && !busy;
        DraftCountText.Text = Dirty ? $"有 {DraftCount()} 项未保存" : "所有修改已保存";
        GlobalSaveBar.Visibility = Dirty && !(currentPage == "templates" && TemplateEditor.IsEditing) ? Visibility.Visible : Visibility.Collapsed;
        RefreshOverview();
    }
    private void MarkDirty()
    {
        if (initializing || !IsLoaded) return;
        ConnectionError.Visibility = Visibility.Collapsed;
        UpdateActions(); SetStatus(Dirty ? "有未保存的修改" : "配置已读取");
        previewTimer.Stop(); previewTimer.Start();
    }
    private void TemplatesClicked(object sender, RoutedEventArgs e)
    {
        if (snapshot is null || store.HasPending(home)) return;
        if (currentPage == "templates" && !TemplateEditor.ResolvePending()) return;
        TemplateEditor.Configure(snapshot.Config, detailedChanges); ShowPage("templates");
    }
    private void StageTemplate(ConfigTemplate template, string[] paths)
    {
        if (snapshot is null || store.HasPending(home) || busy) return;
        try
        {
            var applied = template.StageSelected(new TomlDocument(snapshot.Config).Model, detailedChanges, paths);
            if (applied.Count == 0) { SetStatus("所选设置与当前值相同。"); return; }
            lastTemplate = applied; Editor.Configure(snapshot.Config, detailedChanges); ShowPage("catalog"); Editor.ShowChanged(); MarkDirty();
            SetStatus($"已应用 {applied.Count} 项，保存配置后生效。");
        }
        catch (Exception error) { ShowError(error); }
    }
    private void UndoTemplateClicked(object sender, RoutedEventArgs e)
    {
        if (lastTemplate is null || snapshot is null || busy || store.HasPending(home)) return;
        var result = lastTemplate.Undo(detailedChanges); lastTemplate = null;
        Editor.Configure(snapshot.Config, detailedChanges); MarkDirty();
        SetStatus($"已撤销 {result.Restored} 项模板设置。" + (result.Preserved > 0 ? $"保留 {result.Preserved} 项后续修改。" : ""));
    }
    private int DraftCount() => detailedChanges.Count + (DetailsChanged ? 1 : 0) +
        (pendingOfficialAuth is not null ? 1 : 0) + (ConnectionModeBox.SelectedIndex != (int)loadedSettings.Mode ? 1 : 0) + (GatewayProxyBox.IsChecked != loadedSettings.UseSystemProxy ? 1 : 0) +
        (BaseUrlBox.Text != loadedSettings.BaseUrl ? 1 : 0) + (ApiKeyBox.Password != loadedSettings.ApiKey ? 1 : 0) +
        (ModelBox.Text != loadedSettings.Model ? 1 : 0) + (KindBox.SelectedIndex != (int)loadedSettings.Kind ? 1 : 0) +
        (AuthenticationBox.SelectedIndex != (int)loadedSettings.Authentication ? 1 : 0);

    private void ServiceProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (initializing || LinePresetRow is null) return;
        LinePresetRow.Visibility = ProviderBox.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        ServiceTemplateButton.Visibility = LinePresetRow.Visibility;
        if (ProviderBox.SelectedIndex == 1)
        {
            if (LinePresetBox.SelectedIndex < 0) LinePresetBox.SelectedIndex = 0;
            else LinePresetChanged(LinePresetBox, e);
        }
        else MoreConnection.IsExpanded = true;
    }
    private void FieldChanged(object sender, TextChangedEventArgs e)
    {
        if (!initializing && ReferenceEquals(sender, BaseUrlBox)) SynchronizeLinePreset();
        MarkDirty();
    }
    private void ModelChanged(object sender, SelectionChangedEventArgs e) => MarkDirty();
    private void ConnectionSelectionChanged(object sender, SelectionChangedEventArgs e) { if (!initializing) UpdateCredentialHint(); MarkDirty(); }
    private void LinePresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (initializing || LinePresetBox.SelectedItem is not KrillLine line) return;
        initializing = true;
        BaseUrlBox.Text = line.Origin; KindBox.SelectedIndex = (int)ConnectionKind.PatRelay; AuthenticationBox.SelectedIndex = (int)AuthenticationChoice.Pat;
        ProviderBox.SelectedIndex = 1; LinePresetRow.Visibility = Visibility.Visible; ServiceTemplateButton.Visibility = Visibility.Visible;
        initializing = false; UpdateCredentialHint(); MarkDirty();
    }
    private void SynchronizeLinePreset()
    {
        var before = initializing; initializing = true;
        var line = KrillLines.Find(BaseUrlBox.Text);
        LinePresetBox.SelectedValue = line?.Id;
        ProviderBox.SelectedIndex = line is null ? 0 : 1;
        LinePresetRow.Visibility = line is null ? Visibility.Collapsed : Visibility.Visible;
        ServiceTemplateButton.Visibility = LinePresetRow.Visibility;
        initializing = before;
        UpdateCredentialHint();
    }
    private void UpdateCredentialHint()
    {
        if (ConnectionModeBox.SelectedIndex == (int)ConnectionMode.OfficialWithApi)
        {
            CredentialLabel.Text = "模型访问凭据";
            CredentialHint.Text = "仅用于模型请求，可填写入口支持的 Krill PAT 或 API Key。";
            return;
        }
        CredentialLabel.Text = AuthenticationBox.SelectedIndex switch { 1 => "PAT", 2 => "API Key", _ => "访问凭据" };
        var loginTokens = false;
        if (snapshot?.Auth is not null)
        {
            using var auth = JsonDocument.Parse(snapshot.Auth);
            loginTokens = auth.RootElement.TryGetProperty("tokens", out var tokens) && tokens.ValueKind == JsonValueKind.Object;
        }
        CredentialHint.Text = loginTokens && loadedSettings.ApiKey.Length == 0
            ? "已读取登录认证结构，修改普通设置无需填写。"
            : "修改服务连接时填写，普通设置无需填写。";
    }

    private void ConnectionModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (initializing || OfficialLoginPanel is null) return;
        if (ConnectionModeBox.SelectedIndex == 0) ClearPendingLogin();
        UpdateModeControls(); UpdateCredentialHint(); MarkDirty();
    }
    private void UpdateModeControls()
    {
        var mixed = ConnectionModeBox.SelectedIndex == (int)ConnectionMode.OfficialWithApi;
        OfficialLoginPanel.Visibility = mixed ? Visibility.Visible : Visibility.Collapsed;
        ConnectionModeHint.Text = mixed ? "官方账号负责登录，模型请求使用下方 PAT / API Key。" : "无需登录 ChatGPT，使用服务提供的 PAT 或 API Key。";
        AuthenticationRow.Visibility = mixed ? Visibility.Collapsed : Visibility.Visible;
        OfficialLoginStatus.Text = pendingOfficialAuth is not null ? "新登录已完成，保存配置后应用。" :
            OfficialCredentials.HasLogin(snapshot?.Auth) ? "已读取官方登录凭据，实际有效状态由 Codex 验证。" : "尚未读取完整的官方登录，请先在浏览器登录。";
        CodexExecutableHint.Text = codexExecutable is null ? "未在 PATH 找到 codex.exe，请选择 Codex 原生程序。" : "登录程序：" + codexExecutable;
    }
    private void ClearPendingLogin()
    {
        if (pendingOfficialAuth is not null) CryptographicOperations.ZeroMemory(pendingOfficialAuth);
        pendingOfficialAuth = null;
    }
    private void SelectCodexClicked(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Title = "选择 Codex 原生程序", Filter = "Codex 程序 (codex.exe)|codex.exe", CheckFileExists = true };
        if (picker.ShowDialog(this) == true) { codexExecutable = picker.FileName; UpdateModeControls(); }
    }
    private async void OfficialLoginClicked(object sender, RoutedEventArgs e)
    {
        if (busy || smokeOutput is not null) return;
        if (codexExecutable is null) { SelectCodexClicked(sender, e); if (codexExecutable is null) return; }
        busy = true; loginCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        CancelLoginButton.Visibility = Visibility.Visible; UpdateActions();
        OfficialLoginStatus.Text = "请在系统浏览器中完成官方登录，最长等待 5 分钟。";
        try
        {
            var bytes = await new CodexLoginClient().LoginAsync(codexExecutable,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexConfig", "login"), loginCancellation.Token);
            if (closing) { CryptographicOperations.ZeroMemory(bytes); return; }
            ClearPendingLogin(); pendingOfficialAuth = bytes; MarkDirty(); UpdateModeControls();
        }
        catch (OperationCanceledException) { if (!closing) OfficialLoginStatus.Text = "登录已取消或超时，原有登录未更改。"; }
        catch (Exception error) { if (!closing) { OfficialLoginStatus.Text = "登录未完成，原有登录未更改。"; ShowError(error); } }
        finally
        {
            loginCancellation.Dispose(); loginCancellation = null; busy = false;
            if (!closing) { CancelLoginButton.Visibility = Visibility.Collapsed; UpdateActions(); }
        }
    }
    private void CancelLoginClicked(object sender, RoutedEventArgs e) => loginCancellation?.Cancel();
    private void PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!initializing && ShowKeyBox.IsChecked != true) { initializing = true; VisibleKeyBox.Text = ApiKeyBox.Password; initializing = false; }
        MarkDirty();
    }
    private void VisibleKeyChanged(object sender, TextChangedEventArgs e)
    {
        if (!initializing && ShowKeyBox.IsChecked == true) { initializing = true; ApiKeyBox.Password = VisibleKeyBox.Text; initializing = false; }
        MarkDirty();
    }
    private void KeyVisibilityChanged(object sender, RoutedEventArgs e)
    {
        if (ApiKeyBox is null || VisibleKeyBox is null) return;
        var previous = initializing; initializing = true;
        VisibleKeyBox.Text = ApiKeyBox.Password;
        VisibleKeyBox.Visibility = ShowKeyBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ApiKeyBox.Visibility = ShowKeyBox.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;
        initializing = previous;
    }

    private void UpdatePreview()
    {
        if (snapshot is null) { PreviewBox.Text = "请先读取有效的配置目录。"; return; }
        try { preview = store.Plan(snapshot, Settings(placeholder: true), detailedChanges, updateConnection: ConnectionChanged, updateModel: ModelBox.Text != loadedSettings.Model, officialAuth: pendingOfficialAuth, updateDetails: DetailsChanged); RenderPreview(); }
        catch (Exception) { preview = null; PreviewBox.Text = "配置预览暂不可用，请检查字段格式与未保存的设置。"; }
    }
    private void RenderPreview()
    {
        if (preview is null) return;
        PreviewBox.Text = currentTab switch
        {
            "changes" => RenderChanges(),
            "auth" => preview.AuthPreview,
            "env" => "CODEX_AUTHAPI_BASE_URL\n" + (preview.AuthApiBaseUrl ?? "（未设置）"),
            _ => preview.ConfigPreview
        };
        foreach (var tab in new[] { ChangesTab, ConfigTab, AuthTab, EnvTab })
        {
            var selected = (string)tab.Tag == currentTab;
            tab.SetResourceReference(BackgroundProperty, selected ? "AccentSoft" : "Canvas");
            tab.SetResourceReference(ForegroundProperty, selected ? "Accent" : "Muted");
        }
    }
    private string RenderChanges()
    {
        if (snapshot is null || preview is null) return "请先读取有效配置。";
        var before = new TomlDocument(snapshot.Config).Model; var after = new TomlDocument(preview.Config).Model;
        var lines = new List<string>();
        string Display(string path, object? value) => value is null ? "未设置 / 跟随默认" : SettingsEditor.Sensitive(path) ? "••••••••（已隐藏）" : ConfigSchema.Literal(value);
        foreach (var path in TomlDocument.ValuePaths(before).Concat(TomlDocument.ValuePaths(after)).Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal))
        {
            var previous = TomlDocument.ValueAt(before, path); var next = TomlDocument.ValueAt(after, path);
            if (ConfigSchema.Literal(previous) == ConfigSchema.Literal(next)) continue;
            lines.Add(ConfigHelp.Name(path) + "\n" + path + "\n  " + Display(path, previous) + "\n→ " + Display(path, next));
        }
        if (!(snapshot.Auth ?? []).SequenceEqual(preview.Auth ?? [])) lines.Add("认证文件\n  将更新（凭据内容已隐藏）");
        if (!(snapshot.Bridge ?? []).SequenceEqual(preview.Bridge ?? [])) lines.Add("模型连接\n  将更新本地连接配置与加密模型凭据（内容已隐藏）");
        if (!(snapshot.Details ?? []).SequenceEqual(preview.Details ?? [])) lines.Add("连接名称与备注\n  将更新本工具的连接信息（内容已隐藏）");
        if (snapshot.AuthApiBaseUrl != preview.AuthApiBaseUrl) lines.Add("用户环境变量 CODEX_AUTHAPI_BASE_URL\n  " + (snapshot.AuthApiBaseUrl ?? "未设置") + "\n→ " + (preview.AuthApiBaseUrl ?? "移除"));
        return lines.Count == 0 ? "没有需要保存的更改。" : string.Join("\n\n", lines) + "\n\n保存后，重新打开 Codex 读取配置。";
    }
    private void PreviewTabClicked(object sender, RoutedEventArgs e) { currentTab = (string)((Button)sender).Tag; UpdatePreview(); }
    private void PreviewClicked(object sender, RoutedEventArgs e) { currentTab = "changes"; UpdatePreview(); OpenOverlay(PreviewOverlay, ClosePreviewButton); }
    private void LocationClicked(object sender, RoutedEventArgs e) { HomePath.Text = home; OpenOverlay(LocationOverlay, CloseLocationButton); }
    private void OpenOverlay(Grid overlay, Control focus)
    {
        overlayFocus = Keyboard.FocusedElement; MainShell.IsEnabled = false; overlay.Visibility = Visibility.Visible; focus.Focus();
    }
    private void CloseOverlayClicked(object sender, RoutedEventArgs e) => CloseOverlay();
    private void CloseOverlay()
    {
        PreviewOverlay.Visibility = LocationOverlay.Visibility = Visibility.Collapsed; MainShell.IsEnabled = true;
        if (overlayFocus is not null) Keyboard.Focus(overlayFocus);
    }

    private void SaveClicked(object sender, RoutedEventArgs e) => SaveCurrent();
    private bool SaveCurrent()
    {
        if (busy) return false;
        try
        {
            if (snapshot is null || store.HasPending(home)) throw new InvalidOperationException("请先读取配置或恢复未完成的保存。");
            Editor.Revalidate();
            if (Editor.Errors.Count > 0)
            {
                var first = Editor.Errors.First(); ShowPage("catalog"); Editor.Filter(ConfigSchema.Instance.Find(first.Key)?.Path ?? first.Key);
                throw new ArgumentException("有配置项尚未填写正确，请检查行内提示。");
            }
            var plan = store.Plan(snapshot, Settings(), detailedChanges, updateConnection: ConnectionChanged, updateModel: ModelBox.Text != loadedSettings.Model, officialAuth: pendingOfficialAuth, updateDetails: DetailsChanged);
            store.Apply(plan);
            if (smokeOutput is null && plan.Bridge is not null)
            {
                try { gateways.EnsureAsync(home, plan.Bridge).GetAwaiter().GetResult(); }
                catch (Exception error)
                {
                    store.Restore(store.Load(home));
                    throw new IOException("本地连接启动失败，已回滚本次保存。" + error.Message);
                }
            }
            if (!Reload()) return false;
            var note = plan.Notes.FirstOrDefault(n => n.StartsWith("保留现有", StringComparison.Ordinal));
            SetStatus("已保存，重新打开 Codex 后生效。" + (note is null ? "" : " " + note));
            return true;
        }
        catch (Exception error)
        {
            ShowError(error);
            if (currentPage == "connection") { ConnectionError.Text = error.Message; ConnectionError.Visibility = Visibility.Visible; }
            UpdateActions(); return false;
        }
    }
    private async void ProbeClicked(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        try
        {
            var settings = Settings();
            settings = (settings with { Model = string.IsNullOrWhiteSpace(settings.Model) ? "model-catalog" : settings.Model }).Normalize();
            var fingerprint = settings.Fingerprint();
            busy = true; ProbeButton.Content = "正在获取…"; UpdateActions(); ModelHint.Text = "正在获取模型目录…";
            var result = await probe.ReadModelsAsync(settings, lifetime.Token);
            if (closing) return;
            string? latest;
            try { latest = (Settings() with { Model = "model-catalog" }).Fingerprint(); } catch (ArgumentException) { latest = null; }
            if (latest != fingerprint) { ModelHint.Text = "连接设置已更改，请重新获取模型。"; return; }
            var selected = ModelBox.Text; initializing = true;
            ModelBox.ItemsSource = result.Models; ModelBox.Text = selected;
            initializing = false;
            ModelHint.Text = result.Models.Count > 0 ? $"已获取 {result.Models.Count} 个模型 · {result.Milliseconds} ms" : "模型目录为空，可手动填写模型名称。";
        }
        catch (OperationCanceledException) { if (!closing) ModelHint.Text = "获取已取消。"; }
        catch (Exception error) { if (!closing) { ModelHint.Text = error.Message; ShowError(error); } }
        finally { busy = false; initializing = false; if (!closing) { ProbeButton.Content = "获取模型"; UpdateActions(); } else probe.Dispose(); }
    }

    private bool ResolveUnsaved()
    {
        if (!TemplateEditor.ResolvePending()) return false;
        if (!Dirty || smokeOutput is not null) return true;
        var choice = MessageBox.Show(this, "有未保存的修改。是否先保存？\n\n“是”保存，“否”放弃，“取消”继续编辑。", "未保存的配置", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return choice == MessageBoxResult.No || choice == MessageBoxResult.Yes && SaveCurrent();
    }
    private void UndoClicked(object sender, RoutedEventArgs e)
    {
        if (smokeOutput is null && MessageBox.Show(this, "撤销所有页面中未保存的修改？", "撤销修改", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        Reload();
    }
    private void ChooseHomeClicked(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        if (!ResolveUnsaved()) return;
        var dialog = new OpenFolderDialog { Title = "选择 Codex 配置目录", InitialDirectory = Directory.Exists(home) ? home : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) };
        if (dialog.ShowDialog(this) == true) { home = dialog.FolderName; CloseOverlay(); Reload(); }
    }
    private void ReloadClicked(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        if (!ResolveUnsaved()) return;
        CloseOverlay(); Reload();
    }
    private void OpenFolderClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!Directory.Exists(home)) { SetStatus("配置目录尚不存在，首次保存时会自动创建。"); return; }
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { Path.GetFullPath(home) }, UseShellExecute = false });
        }
        catch (Exception error) { ShowError(error); }
    }
    private void RestoreClicked(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        if (!ResolveUnsaved()) return;
        if (smokeOutput is null && MessageBox.Show(this, "将恢复上次保存前的配置文件和相关用户环境变量。当前状态也会加密备份。继续？", "恢复配置", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            if (store.HasPending(home)) store.Recover(home); else store.Restore(store.Load(home));
            Reload(); SetStatus("已恢复，重新打开 Codex 后生效。");
        }
        catch (Exception error) { ShowError(error); }
    }
    private void HelpClicked(object sender, RoutedEventArgs e) => MessageBox.Show(this,
        "1. 调整设置，或在模板中选择一组设置。\n\n" +
        "2. 点击“保存配置”，重新打开 Codex 后生效。\n\n" +
        "3. 保存前会自动备份，可在“备份恢复”中还原。\n\n" +
        "Ctrl+S 保存 · Ctrl+F 搜索 · Esc 关闭面板\n\n" +
        "配置目录可在“配置位置”查看或更换。模板单独保存在本机。",
        "使用说明", MessageBoxButton.OK, MessageBoxImage.Information);
    private void SetStatus(string text, bool error = false)
    {
        StatusText.Text = text; StatusText.SetResourceReference(TextBlock.ForegroundProperty, error ? "Error" : "Muted");
        StatusText.Visibility = text is "配置已读取" or "有未保存的修改" ? Visibility.Collapsed : Visibility.Visible;
    }
    private void ShowError(Exception error) => SetStatus(error is InvalidDataException ? "配置数据格式不正确，请在配置位置检查源文件后重新读取。" : error.Message, true);
    private void HandleShortcut(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && (PreviewOverlay.IsVisible || LocationOverlay.IsVisible)) { CloseOverlay(); e.Handled = true; }
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        if (e.Key == Key.S && MainShell.IsEnabled && SaveButton.IsEnabled) { SaveCurrent(); e.Handled = true; }
        if (e.Key == Key.F && MainShell.IsEnabled) { ShowPage("catalog"); Editor.FocusSearch(); e.Handled = true; }
    }
    private void SystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (!closing) Dispatcher.BeginInvoke(() => { if (!closing) ThemeManager.ApplySystemTheme(); });
    }

    private async Task RunSmokeAsync(string output)
    {
        using (var process = Process.GetCurrentProcess())
        {
            if (process.ProcessName != "CodexDock" || typeof(App).Assembly.GetName().Name != "CodexDock")
                throw new InvalidOperationException("Application process and assembly must use the CodexDock name.");
        }
        Directory.CreateDirectory(output);
        var originalEnvironment = Environment.GetEnvironmentVariable(WindowsUserEnvironment.Variable, EnvironmentVariableTarget.User);
        var checks = new List<string>();
        void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        ThemeManager.Apply(false);
        Assert(!SaveButton.IsEnabled && PreviewOverlay.Visibility == Visibility.Collapsed, "Initial state");
        Assert(currentPage == "overview" && OverviewPage.IsVisible && GlobalSaveBar.Visibility == Visibility.Collapsed, "Dock default overview without draft bar");
        Assert(ProviderBox.SelectedIndex == 0 && LinePresetRow.Visibility == Visibility.Collapsed, "Blank home must not select a provider");
        await Settle(); Capture(output, "main-window.png");
        ShowPage("connection"); await Settle(); Capture(output, "connection-custom.png");
        checks.AddRange(["Neutral default common page", "Blank custom connection without provider selection"]);
        await RunLocalStateSmokeAsync(output, checks);
        ShowPage("preferences"); Editor.Search.Text = ""; Editor.SetMode(true);
        Editor.NewPath.Text = "future_config.answer"; Editor.NewValue.Text = "42";
        Editor.AddSetting.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(Editor.VisibleCount == 1 && detailedChanges.GetValueOrDefault("future_config.answer") == "42", "Add unlisted setting");
        Assert(SaveCurrent(), "Config-only save without credentials");
        Assert(store.Load(home).Auth is null && environment.Read() is null, "Config-only save preserves absent authentication");
        Editor.Filter("future_config.answer");
        ((TextBox)Editor.Input("future_config.answer")).Text = "43";
        Assert(SaveCurrent() && new TomlDocument(store.Load(home).Config).Get("future_config.answer") is 43L, "Edit unlisted setting");
        Editor.Filter(""); ShowPage("connection");
        checks.AddRange(["Add and edit unlisted TOML setting", "Config-only save without credentials"]);
        foreach (var line in KrillLines.All)
        {
            LinePresetBox.SelectedValue = line.Id;
            Assert(BaseUrlBox.Text == line.Origin && AuthenticationBox.SelectedIndex == (int)AuthenticationChoice.Pat, "Preset " + line.Id);
        }
        LinePresetBox.SelectedValue = KrillLines.Cdn.Id; ApiKeyBox.Password = "ah-fake-ui-smoke-key"; ModelBox.Text = "gpt-6.1-sol";
        ShowPage("preferences"); Editor.Search.Text = ""; Editor.SetMode(true);
        ModelBox.ApplyTemplate();
        Assert(ModelBox.Template.FindName("PART_EditableTextBox", ModelBox) is TextBox, "Editable model input");
        var editable = (TextBox)ModelBox.Template.FindName("PART_EditableTextBox", ModelBox);
        editable.Text = "typed-model"; Assert(ModelBox.Text == "typed-model", "Editable model binding"); editable.Text = "gpt-6.1-sol";
        checks.Add("Presets and editable model binding");
        ShowPage("preferences"); Editor.Search.Text = ""; Editor.SetMode(true);
        Editor.Search.Text = "model_context_window";
        await Task.Delay(240);
        Assert(Editor.VisibleCount == 1, "Common settings search");
        Editor.Search.Text = ""; await Task.Delay(240);
        checks.Add("Search within common settings");
        var reasoning = (ComboBox)Editor.Input("model_reasoning_effort");
        reasoning.SelectedItem = reasoning.Items.Cast<SettingsEditor.ValueChoice>().First(c => c.Repr == "\"high\"");
        ((TextBox)Editor.Input("model_context_window")).Text = "500000";
        Assert(detailedChanges.GetValueOrDefault("model_context_window") == "500000", "Shared draft");
        ShowPage("connection"); ShowPage("catalog"); Editor.Filter("model_context_window");
        Assert(Editor.VisibleCount == 1 && ((TextBox)Editor.Input("model_context_window")).Text == "500000", "Draft across navigation");
        ((TextBox)Editor.Input("model_context_window")).Text = "invalid-number";
        Assert(!SaveCurrent() && detailedChanges.ContainsKey("model_context_window"), "Invalid draft blocked and retained");
        ((TextBox)Editor.Input("model_context_window")).Text = "500000";
        Editor.Filter("no-such-setting-unique"); Assert(Editor.VisibleCount == 0, "Empty search");
        Editor.Filter(""); ShowPage("connection");
        UpdatePreview(); Assert(preview is not null && !preview.AuthPreview.Contains(ApiKeyBox.Password), "Masked preview");
        Assert(SaveCurrent(), "Save failed: " + StatusText.Text);
        var saved = store.Load(home); var document = new TomlDocument(saved.Config);
        Assert(document.Get("model_context_window") is long n && n == 500000 && document.String("model_reasoning_effort") == "high", "Unified save");
        Assert(document.Get("features.fast_mode") is null && document.Get("web_search") is null, "Absent defaults preserved");
        using (var auth = JsonDocument.Parse(saved.Auth!)) Assert(auth.RootElement.GetProperty("personal_access_token").GetString() == "ah-fake-ui-smoke-key", "Auth format");
        Assert(environment.Read() == KrillLines.Cdn.Origin && !SaveButton.IsEnabled, "Saved state");
        checks.AddRange(["Shared draft across pages", "Invalid draft retained and save blocked", "Search and empty state", "Unified settings save", "Absent preferences preserved", "Masked preview and authentication"]);
        await Settle(); Capture(output, "connection-krill.png");
        ShowPage("preferences"); Editor.Search.Text = ""; Editor.SetMode(true); await Settle(); Capture(output, "preferences.png");
        ShowPage("catalog"); Editor.Filter("model_context"); await Settle(); Capture(output, "detailed-settings.png");
        Editor.Filter(""); ShowPage("connection"); PreviewClicked(PreviewButton, new()); await Settle();
        AuthTab.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert(!PreviewBox.Text.Contains(ApiKeyBox.Password), "Auth drawer mask");
        ConfigTab.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Capture(output, "preview-drawer.png"); CloseOverlay();
        var width = Width; var height = Height;
        Width = 1024; Height = 720; await Settle(); Capture(output, "main-window-compact.png");
        ShowPage("preferences"); Editor.Search.Text = ""; Editor.SetMode(true); ModelBox.Text = "layout-draft";
        foreach (var factor in new[] { 1.25, 1.5 })
        {
            RootPanel.LayoutTransform = new ScaleTransform(factor, factor); await Settle();
            var saveBounds = SaveButton.TransformToAncestor(RootPanel).TransformBounds(new Rect(SaveButton.RenderSize));
            Assert(SaveButton.IsVisible && ProbeButton.IsVisible && saveBounds.Right <= RootPanel.ActualWidth + 1 && saveBounds.Bottom <= RootPanel.ActualHeight + 1, "Scaled layout");
            Capture(output, "main-window-scale-" + (int)(factor * 100) + ".png");
        }
        RootPanel.LayoutTransform = Transform.Identity; Width = width; Height = height; await Settle();
        ModelBox.Text = loadedSettings.Model;
        ThemeManager.Apply(true); await Settle(); Capture(output, "main-window-dark.png"); ThemeManager.Apply(false);
        checks.AddRange(["Preview drawer", "Compact window", "125% and 150% layout simulation", "System theme resources"]);
        ShowPage("preferences"); Editor.Search.Text = ""; Editor.SetMode(true);
        void SelectValue(string path, string? repr)
        {
            var box = (ComboBox)Editor.Input(path);
            box.SelectedItem = box.Items.Cast<SettingsEditor.ValueChoice>().First(c => c.Repr == repr);
        }
        SelectValue("features.fast_mode", "false"); SelectValue("web_search", "\"cached\"");
        Editor.Filter("features.context_management"); SelectValue("features.context_management", "true");
        Assert(SaveCurrent(), "Boolean and enum preference save");
        document = new TomlDocument(store.Load(home).Config);
        Assert(document.Get("features.fast_mode") is false && document.Get("features.context_management") is true && document.String("web_search") == "cached", "Typed preferences persisted");
        Editor.Filter("features.context_management"); SelectValue("features.context_management", null);
        ShowPage("preferences"); Editor.Search.Text = ""; Editor.SetMode(true); SelectValue("features.fast_mode", null); SelectValue("web_search", null);
        Assert(SaveCurrent(), "Preference defaults reset");
        ShowPage("catalog"); Editor.Filter("developer_instructions");
        var instruction = (TextBox)Editor.Input("developer_instructions");
        Assert(instruction.AcceptsReturn, "Long text editor"); instruction.Text = "中文指令\nsecond line";
        Editor.Filter("sandbox_workspace_write.writable_roots");
        ((TextBox)Editor.Input("sandbox_workspace_write.writable_roots")).Text = "[\"D:/example\"]";
        Assert(SaveCurrent(), "Long text and array save");
        document = new TomlDocument(store.Load(home).Config);
        Assert(document.String("developer_instructions") == "中文指令\nsecond line" && document.Get("sandbox_workspace_write.writable_roots") is Tomlyn.Model.TomlArray { Count: 1 }, "Long text and array persisted");
        checks.AddRange(["Boolean and enum preferences", "Preference reset removes overrides", "Multiline text and TOML array editing"]);
        using (var authBefore = JsonDocument.Parse(store.Load(home).Auth!))
            Assert(authBefore.RootElement.TryGetProperty("personal_access_token", out _), "PAT survives configuration-only edits");
        ModelBox.Text = "second-model"; Assert(SaveCurrent(), "Repeated save");
        RestoreClicked(RestoreButton, new());
        Assert(new TomlDocument(store.Load(home).Config).String("model") == "gpt-6.1-sol", "Generic restore");
        ShowPage("preferences"); Editor.Search.Text = ""; Editor.SetMode(true);
        var contextBox = (TextBox)Editor.Input("model_context_window"); contextBox.Text = "";
        Assert(SaveCurrent() && new TomlDocument(store.Load(home).Config).Get("model_context_window") is null, "Restore default removes override");
        ModelBox.Text = "unsaved-model"; UndoClicked(UndoButton, new()); Assert(ModelBox.Text == "gpt-6.1-sol" && !SaveButton.IsEnabled, "Undo draft");
        checks.AddRange(["Generic backup and restore", "Restore default removes override", "Undo all draft changes"]);
        var current = store.Load(home); File.AppendAllText(Path.Combine(home, "config.toml"), "# external change\n");
        ModelBox.Text = "must-not-overwrite"; Assert(!SaveCurrent(), "External edit conflict");
        Assert(File.ReadAllText(Path.Combine(home, "config.toml")).Contains("# external change"), "External edit retained");
        File.WriteAllBytes(Path.Combine(home, "config.toml"), current.Config!); Reload(); ShowPage("connection");
        detailedChanges["web_search"] = "\"cached\""; detailedChanges["future_config.other"] = "true";
        TemplatesClicked(TemplatesNav, new()); TemplateEditor.ShowTemplate(BuiltInTemplates.KrillCommunity);
        Assert(TemplateEditor.Choices.Count == 7 && TemplateEditor.Choices["web_search"].IsChecked == false, "Template conflicts default to preserving draft");
        Assert(TemplateEditor.Choices["features.context_management"].IsChecked == false && TemplateEditor.Choices["suppress_unstable_features_warning"].IsChecked == false, "Historical and suppress-warning options opt in");
        Assert(detailedChanges.Count == 2 && File.ReadAllBytes(Path.Combine(home, "config.toml")).SequenceEqual(current.Config!), "Template preview has no side effects");
        ShowPage("catalog"); Assert(detailedChanges["web_search"] == "\"cached\"", "Cancel template preserves draft");
        TemplatesClicked(TemplatesNav, new()); TemplateEditor.ShowTemplate(BuiltInTemplates.KrillCommunity);
        foreach (var box in TemplateEditor.Choices.Values) box.IsChecked = box.IsEnabled;
        await Settle(); Capture(output, "template-details.png");
        TemplateEditor.StageButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(lastTemplate?.Count == 7 && detailedChanges.Count == 8, "Selected template stages all seven");
        Editor.Filter("web_search"); SelectValue("web_search", "\"disabled\"");
        UndoTemplateClicked(UndoTemplateButton, new());
        Assert(detailedChanges.Count == 2 && detailedChanges["web_search"] == "\"disabled\"" && detailedChanges["future_config.other"] == "true", "Undo template preserves later and unrelated edits");
        UndoClicked(UndoButton, new());
        TemplatesClicked(TemplatesNav, new()); TemplateEditor.ShowTemplate(BuiltInTemplates.KrillCommunity);
        Assert(TemplateEditor.Choices.Values.Count(box => box.IsChecked == true) == 3, "Default template selects three supported non-conflicting options");
        Width = 960; Height = 640; await Settle();
        Assert(FindVisual<TextBlock>(TemplateEditor.StageButton).All(block => block.Foreground.ToString() == TemplateEditor.StageButton.Foreground.ToString()), "Primary button text contrast in light theme");
        var stageBounds = TemplateEditor.StageButton.TransformToAncestor(RootPanel).TransformBounds(new Rect(TemplateEditor.StageButton.RenderSize));
        Assert(stageBounds.Right <= RootPanel.ActualWidth && stageBounds.Bottom <= RootPanel.ActualHeight, "Template action fits minimum window");
        Capture(output, "template-compact.png");
        ThemeManager.Apply(true); await Settle();
        Assert(FindVisual<TextBlock>(TemplateEditor.StageButton).All(block => block.Foreground.ToString() == TemplateEditor.StageButton.Foreground.ToString()), "Primary button text contrast in dark theme");
        Capture(output, "template-dark.png"); ThemeManager.Apply(false);
        ShowPage("preferences"); Editor.Search.Text = ""; Editor.SetMode(true); await Settle(); Capture(output, "common-compact.png");
        Width = width; Height = height; ShowPage("templates"); await Settle();
        TemplateEditor.ShowTemplate(BuiltInTemplates.KrillCommunity);
        foreach (var box in TemplateEditor.Choices.Values) box.IsChecked = box.IsEnabled;
        TemplateEditor.StageButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(File.ReadAllBytes(Path.Combine(home, "config.toml")).SequenceEqual(current.Config!), "Template only stages a draft");
        PreviewClicked(PreviewButton, new()); Assert(currentTab == "changes" && PreviewBox.Text.Contains("web_search") && !PreviewBox.Text.Contains("ah-fake"), "Masked field differences");
        await Settle(); Capture(output, "changes-preview.png"); CloseOverlay();
        Assert(SaveCurrent(), "Template save");
        document = new TomlDocument(store.Load(home).Config);
        foreach (var setting in BuiltInTemplates.KrillCommunity.Settings) Assert(ConfigSchema.Literal(document.Get(setting.Path)) == setting.Value, "Template saved " + setting.Path);
        Assert(store.Load(home).Auth!.SequenceEqual(current.Auth!), "Template preserves authentication");
        TemplatesClicked(TemplatesNav, new()); TemplateEditor.ShowTemplate(BuiltInTemplates.KrillCommunity);
        Assert(!TemplateEditor.StageButton.IsEnabled && TemplateEditor.Choices.Values.All(box => !box.IsEnabled) && detailedChanges.Count == 0, "Repeated template creates no draft");
        checks.AddRange(["All seven template settings explicitly selected and persisted", "Template preview and cancel have no side effects", "Template conflicts and historical options opt in", "Undo template preserves later and unrelated edits", "Repeated template skips equal values", "Masked field differences", "Template preserves authentication", "Minimum 960x640 common and template layout", "Dark template details"]);
        // Exercise the actual template editor controls and local persistence in the isolated smoke home.
        var library = new TemplateRepository(Path.Combine(home, "editor-templates"));
        var configBeforeLibrary = File.ReadAllBytes(Path.Combine(home, "config.toml"));
        TemplateEditor.ShowList(); await Settle(); Capture(output, "template-list.png");
        TemplateEditor.NewTemplateButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle();
        Assert(TemplateEditor.IsEditing && GlobalSaveBar.Visibility == Visibility.Collapsed, "Template editor has one action bar");
        TemplateEditor.NameInput.Text = "日常设置";
        var templateField = FindVisual<ComboBox>(TemplateEditor).Single(box => System.Windows.Automation.AutomationProperties.GetName(box) == "模板配置字段");
        templateField.SelectedItem = templateField.Items.Cast<string>().Single(text => text.EndsWith(" · web_search", StringComparison.Ordinal));
        var templateValue = FindVisual<TextBox>(TemplateEditor).Single(box => System.Windows.Automation.AutomationProperties.GetName(box) == "模板配置取值");
        templateValue.Text = "'disabled'";
        await Settle(); Capture(output, "template-edit.png");
        TemplateEditor.SaveTemplateButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var custom = TemplateEditor.Current ?? throw new InvalidOperationException("New template was not saved");
        Assert(custom is { Name: "日常设置", Settings.Count: 1 } && custom.Settings[0].Path == "web_search", "New template saves friendly field selection");
        Assert(library.Load().Single(template => template.Id == custom.Id).Settings[0].Value == "\"disabled\"", "Template survives repository reopen");
        Assert(detailedChanges.Count == 0 && File.ReadAllBytes(Path.Combine(home, "config.toml")).SequenceEqual(configBeforeLibrary), "Saving template does not change Codex");
        TemplateEditor.BeginEdit(custom!); await Settle();
        TemplateEditor.NameInput.Text = "我的日常设置"; TemplateEditor.AddRowButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle();
        var invalidLibraryBefore = File.ReadAllBytes(Path.Combine(home, "editor-templates", "templates.json"));
        Assert(!TemplateEditor.SaveEdit() && TemplateEditor.IsEditing && TemplateEditor.ValidationMessage.Length > 0, "Invalid template row remains editable");
        Assert(File.ReadAllBytes(Path.Combine(home, "editor-templates", "templates.json")).SequenceEqual(invalidLibraryBefore), "Invalid template cannot replace library");
        FindVisual<Button>(TemplateEditor).Where(button => button.Content as string == "移除").Last().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(TemplateEditor.SaveEdit() && TemplateEditor.Current?.Name == "我的日常设置", "Template row removal and rename save");
        custom = TemplateEditor.Current!;
        TemplateEditor.CopyTemplate(custom); await Settle(); TemplateEditor.NameInput.Text = "日常设置副本";
        Assert(TemplateEditor.SaveEdit() && TemplateEditor.Current!.Id != custom.Id, "Copy creates independent template");
        var copied = TemplateEditor.Current!;
        TemplateEditor.DeleteTemplate(copied); Assert(library.Load().All(template => template.Id != copied.Id), "Delete custom template");
        TemplateEditor.DeleteTemplate(BuiltInTemplates.KrillCommunity); Assert(library.Load().All(template => template.Id != BuiltInTemplates.KrillCommunity.Id), "Remove built-in template");
        TemplateEditor.RestoreBuiltIns(); Assert(library.Load().Any(template => template.Id == custom.Id) && library.Load().Any(template => template.Id == BuiltInTemplates.KrillCommunity.Id), "Restore built-in preserves custom templates");
        TemplateEditor.BeginEdit(custom); TemplateEditor.NameInput.Text = "尚未保存的名称";
        TemplateEditor.PendingTestChoice = MessageBoxResult.Cancel; ShowPage("connection");
        Assert(currentPage == "templates" && TemplateEditor.IsEditing && TemplatesNav.IsChecked == true, "Cancel navigation retains template edit");
        TemplateEditor.PendingTestChoice = MessageBoxResult.No; ShowPage("connection"); ShowPage("templates");
        Assert(!TemplateEditor.IsEditing && library.Load().Single(template => template.Id == custom.Id).Name == custom.Name, "Discard template edit leaves stored version unchanged");
        detailedChanges["model_context_window"] = "123456"; TemplateEditor.Configure(snapshot!.Config, detailedChanges);
        TemplateEditor.CaptureTemplateButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle();
        TemplateEditor.NameInput.Text = "当前设置";
        Assert(TemplateEditor.SaveEdit() && TemplateEditor.Current!.Settings.Any(setting => setting.Path == "model_context_window" && setting.Value == "123456"), "Capture current settings includes draft");
        Assert(TemplateEditor.Current!.Settings.All(setting => TemplateRepository.CanInclude(setting.Path) && !setting.Value.Contains("ah-fake")), "Captured template excludes connection and credentials");
        TemplateEditor.DeleteTemplate(TemplateEditor.Current!); detailedChanges.Clear(); Editor.Configure(snapshot.Config, detailedChanges);
        TemplateEditor.BeginEdit(custom); await Settle(); Width = 960; Height = 640; await Settle();
        var templateSaveBounds = TemplateEditor.SaveTemplateButton.TransformToAncestor(RootPanel).TransformBounds(new Rect(TemplateEditor.SaveTemplateButton.RenderSize));
        Assert(templateSaveBounds.Right <= RootPanel.ActualWidth && templateSaveBounds.Bottom <= RootPanel.ActualHeight, "Template edit action fits minimum window");
        Capture(output, "template-edit-compact.png"); ThemeManager.Apply(true); await Settle(); Capture(output, "template-edit-dark.png"); ThemeManager.Apply(false);
        Width = width; Height = height; TemplateEditor.CancelEditButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle(); Capture(output, "template-list.png");
        checks.AddRange(["Create and reopen custom template", "Edit template name and remove rows", "Invalid template edits preserve library", "Copy and delete custom template", "Remove and restore built-in without deleting custom templates", "Template unsaved navigation cancel and discard", "Capture current settings includes draft and excludes credentials", "Template management leaves Codex files unchanged", "Template editor minimum size and dark theme"]);
        ShowPage("catalog"); Editor.Filter("");
        var toggleTimes = new List<long>();
        for (var i = 0; i < 6; i++)
        {
            var timer = Stopwatch.StartNew(); Editor.ShowAdvanced(i % 2 == 0); UpdateLayout(); timer.Stop();
            toggleTimes.Add(timer.ElapsedMilliseconds);
            Assert(Editor.RenderedCount <= 30 && timer.ElapsedMilliseconds < 1000, "Advanced toggle must render a bounded page promptly");
        }
        Editor.ShowAdvanced(true);
        Assert(Editor.VisibleCount > 900 && Editor.NextPage.IsEnabled, "Advanced entries remain accessible");
        Editor.NextPage.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(Editor.PreviousPage.IsEnabled && Editor.RenderedCount <= 30, "Next page");
        var editorPage = Editor.CurrentPage; ShowPage("connection"); ShowPage("catalog");
        Assert(Editor.CurrentPage == editorPage, "Navigation preserves pagination");
        Editor.PreviousPage.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Editor.Filter("model_context_window"); ((TextBox)Editor.Input("model_context_window")).Text = "600000";
        Editor.Filter(""); Editor.NextPage.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Editor.Filter("model_context_window");
        Assert(((TextBox)Editor.Input("model_context_window")).Text == "600000", "Draft survives pagination");
        UndoClicked(UndoButton, new());
        ShowPage("catalog"); Editor.Filter(""); await Settle(); Capture(output, "advanced-settings.png");
        checks.AddRange(["Advanced toggle bounded to 30 controls", "Pagination preserves draft edits"]);
        ShowPage("preferences"); Editor.Search.Text = ""; Editor.SetMode(true);
        Assert(ConfigHelp.Find("features.fast_mode")?.Effect.Contains("不等于") == true && ConfigHelp.Find("features.token_budget.enabled")?.Verified == false, "Accurate help metadata");
        var helpButton = FindVisual<Button>(Editor).Where(button => button.Content as string == "了解更多").ElementAt(2);
        helpButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle(); Capture(output, "setting-help.png");
        ((TextBox)Editor.Input("service_tier")).Text = "fast";
        ((TextBox)Editor.Input("service_tier")).Text = "";
        Assert(!detailedChanges.ContainsKey("service_tier"), "Blank service tier removes override instead of writing empty string");
        checks.AddRange(["Navigation preserves editor view", "Curated help and historical verification labels"]);
        current = store.Load(home);
        File.WriteAllText(Path.Combine(home, "config.toml"), "broken = [\n");
        Assert(!Reload() && !SaveButton.IsEnabled, "Invalid file disables save");
        File.WriteAllBytes(Path.Combine(home, "config.toml"), current.Config!); ReloadClicked(this, new());
        Assert(snapshot is not null && !SaveButton.IsEnabled, "Reload after source repair");
        // A separate fixture within this smoke home exercises login preservation, never a real account.
        var loginAuth = System.Text.Encoding.UTF8.GetBytes("{\"tokens\":{\"access_token\":\"fake-official-login-token\"},\"auth_mode\":\"chatgpt\"}");
        var officialConfig = System.Text.Encoding.UTF8.GetBytes("# login fixture\nmodel='existing-login-model'\n");
        File.WriteAllBytes(Path.Combine(home, "auth.json"), loginAuth); File.WriteAllBytes(Path.Combine(home, "config.toml"), officialConfig);
        environment.Write(null); Reload();
        Assert(!Dirty && ProviderBox.SelectedIndex == 0 && CredentialHint.Text.Contains("登录认证结构"), "Existing login status reads without staging changes");
        ShowPage("connection"); await Settle(); Capture(output, "connection-login.png");
        ShowPage("preferences"); Editor.Search.Text = ""; Editor.SetMode(true); SelectValue("analytics.enabled", "false");
        Assert(SaveCurrent() && store.Load(home).Auth!.SequenceEqual(loginAuth) && environment.Read() is null, "Config-only edit preserves login authentication");
        checks.AddRange(["Blank service tier follows default", "Existing login structure shown without rewriting", "Config-only UI save preserves login authentication"]);
        // Mixed mode stages login in memory; the smoke suite never opens a browser or starts a real bridge process.
        pendingOfficialAuth = System.Text.Encoding.UTF8.GetBytes("{\"auth_mode\":\"chatgpt\",\"tokens\":{\"id_token\":\"fake-id-token\",\"access_token\":\"fake-mixed-official-access\",\"refresh_token\":\"fake-refresh-token\"}}");
        var mixedAuth = pendingOfficialAuth.ToArray();
        ConnectionModeBox.SelectedIndex = (int)ConnectionMode.OfficialWithApi;
        BaseUrlBox.Text = "https://relay.example/backend-api/codex"; ApiKeyBox.Password = "fake-mixed-krill-pat";
        KindBox.SelectedIndex = (int)ConnectionKind.PatRelay;
        detailedChanges["service_tier"] = "\"fast\""; detailedChanges["features.fast_mode"] = "true";
        detailedChanges["approval_policy"] = "\"on-request\""; detailedChanges["approvals_reviewer"] = "\"auto_review\"";
        Editor.Configure(snapshot!.Config, detailedChanges); MarkDirty(); UpdateModeControls(); UpdatePreview();
        Assert(OfficialLoginPanel.Visibility == Visibility.Visible && AuthenticationRow.Visibility == Visibility.Collapsed && SaveButton.IsEnabled, "Mixed login controls");
        Assert(preview is not null && !preview.ConfigPreview.Contains("fake-mixed-krill-pat") && !RenderChanges().Contains("fake-mixed-official-access"), "Mixed preview masks both credentials");
        ShowPage("connection"); ConnectionPage.ScrollToTop(); await Settle(); Capture(output, "connection-mixed.png");
        Assert(SaveCurrent(), "Mixed connection save");
        var mixedSnapshot = store.Load(home);
        Assert(mixedSnapshot.Bridge is not null && mixedSnapshot.Auth!.SequenceEqual(mixedAuth) && environment.Read() is null, "Mixed identity stored separately");
        Assert(loadedSettings.Mode == ConnectionMode.OfficialWithApi && ApiKeyBox.Password == "fake-mixed-krill-pat" && !Dirty, "Mixed connection reload");
        ModelBox.Text = "mixed-model-after-edit";
        Assert(SaveCurrent() && store.Load(home).Bridge!.SequenceEqual(mixedSnapshot.Bridge!) && store.Load(home).Auth!.SequenceEqual(mixedAuth), "Mixed model-only save preserves encrypted connection and official login");
        ConnectionModeBox.SelectedIndex = 0; KindBox.SelectedIndex = (int)ConnectionKind.ResponsesApi;
        BaseUrlBox.Text = "https://api.example/v1"; ApiKeyBox.Password = "fake-direct-after-mixed";
        Assert(SaveCurrent() && store.Load(home).Bridge is null, "Explicit direct mode clears mixed connection");
        store.Restore(store.Load(home)); Reload();
        Assert(loadedSettings.Mode == ConnectionMode.OfficialWithApi && store.Load(home).Bridge!.SequenceEqual(mixedSnapshot.Bridge!) && store.Load(home).Auth!.SequenceEqual(mixedAuth), "UI restore returns to mixed identity and encrypted connection");
        await RunGatewaySmokeAsync(output, checks);
        checks.AddRange(["Mixed login draft and mode controls", "Mixed credential redaction", "Mixed save/reload and model-only preservation", "Mixed-to-direct switch and encrypted connection restore"]);
        ShowPage("overview"); await Settle(); Capture(output, "dock-overview.png");
        ThemeManager.Apply(true); await Settle(); Capture(output, "dock-overview-dark.png"); ThemeManager.Apply(false);
        ShowPage("app"); await Settle(); Capture(output, "dock-application.png");
        ShowPage("backup"); await Settle(); Capture(output, "dock-backup.png");
        ShowPage("preferences"); Editor.Search.Text = ""; Editor.SetMode(true); await Settle(); Capture(output, "dock-model.png");
        Assert(ModelControls.IsVisible && !ConnectionPage.IsVisible && !GlobalSaveBar.IsVisible, "Dedicated model page without clean draft bar");
        GatewayProxyBox.IsChecked = true; ShowPage("gateway");
        Assert(GlobalSaveBar.IsVisible && Dirty, "Network draft shows global save bar across pages");
        GatewayProxyBox.IsChecked = false;
        Assert(!Dirty && !GlobalSaveBar.IsVisible, "Reset network draft hides save bar");
        checks.AddRange(["Codex Dock overview and identity summary", "Dedicated model, backup and application pages", "Global draft bar appears only for changes"]);
        await RunConnectionDetailsSmokeAsync(output, checks);
        await RunApplicationSettingsSmokeAsync(output, checks);
        await RunUpdateSmokeAsync(output, checks);
        await RunLayoutReviewAsync(output, checks);
        Assert(Environment.GetEnvironmentVariable(WindowsUserEnvironment.Variable, EnvironmentVariableTarget.User) == originalEnvironment, "Real environment changed");
        checks.AddRange(["External change conflict protection", "Reload after invalid source repair", "Real user environment unchanged"]);
        await File.WriteAllTextAsync(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new
        {
            status = "PASS", executableName = Path.GetFileName(Environment.ProcessPath!),
            applicationAssemblySha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(typeof(App).Assembly.Location))).ToLowerInvariant(),
            executableSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Environment.ProcessPath!))).ToLowerInvariant(), tests = checks.Prepend("CodexDock process and assembly identity"),
            advancedToggleMilliseconds = toggleTimes
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static IEnumerable<T> FindVisual<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var nested in FindVisual<T>(child)) yield return nested;
        }
    }
    private async Task Settle() { await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); await Task.Delay(70); UpdateLayout(); }
    private void Capture(string output, string name)
    {
        var factor = RootPanel.LayoutTransform.Value.M11;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(RootPanel.ActualWidth * factor), (int)Math.Ceiling(RootPanel.ActualHeight * factor), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(RootPanel); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(output, name)); encoder.Save(file);
    }
}
