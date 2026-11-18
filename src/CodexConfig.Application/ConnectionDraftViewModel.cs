using CodexConfig.Core;
using System.Security.Cryptography;

namespace CodexConfig.Application;

// Draft values stay in memory until the shell presents a frozen save review.
public sealed class ConnectionDraftViewModel(ConfigStore store) : WorkspaceObservableObject, IDisposable
{
    private ConfigSnapshot? snapshot;
    private ConnectionSettings? baseline;
    private string baseUrl = "", apiKey = "", model = "", name = "", note = "", httpOrigin = "";
    private string gatewayHeaderTimeout = ConnectionDetails.DefaultHeaderTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
    private ConnectionKind kind = ConnectionKind.ResponsesApi;
    private ConnectionMode mode;
    private bool proxy, migrate;
    private byte[]? stagedOfficialAuth;
    private readonly Dictionary<string, string?> edits = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> invalidInputs = new(StringComparer.Ordinal), errors = new(StringComparer.Ordinal);
    private bool catalogSync, reviewChanged;
    private long loadGeneration;
    private string? catalogSource;
    private byte[]? serviceCatalog;
    private string? serviceCatalogFingerprint;
    public ConfigSnapshot? Snapshot => snapshot;
    public long Revision { get; private set; }
    public bool Available => baseline is not null;
    public string BaseUrl { get => baseUrl; set { if (Set(ref baseUrl, value)) { if (HttpConnectionPolicy.RequiredOrigin(value) != httpOrigin) httpOrigin = ""; ConnectionInputChanged(); } } }
    public string ApiKey { get => apiKey; set { if (Set(ref apiKey, value)) ConnectionInputChanged(); } }
    public string Model { get => model; set { if (Set(ref model, value)) DraftChanged(); } }
    public string Name { get => name; set { if (Set(ref name, value)) DraftChanged(); } }
    public string Note { get => note; set { if (Set(ref note, value)) DraftChanged(); } }
    public string GatewayHeaderTimeout { get => gatewayHeaderTimeout; set { if (Set(ref gatewayHeaderTimeout, value)) DraftChanged(); } }
    public ConnectionKind Kind { get => kind; set { if (!Enum.IsDefined(value)) throw new ArgumentException("连接协议无效。"); if (Set(ref kind, value)) ConnectionInputChanged(); } }
    public ConnectionMode Mode { get => mode; set { if (!Enum.IsDefined(value)) throw new ArgumentException("连接方式无效。"); if (Set(ref mode, value)) ConnectionInputChanged(); } }
    public bool UseSystemProxy { get => proxy; set { if (Set(ref proxy, value)) ConnectionInputChanged(); } }
    public bool MigrateLegacyPat { get => migrate; set { if (Set(ref migrate, value)) DraftChanged(); } }
    public bool HasOfficialLogin => OfficialCredentials.HasLogin(snapshot?.Auth) || OfficialCredentials.HasLogin(stagedOfficialAuth);
    public bool HasCurrentOfficialLogin => OfficialCredentials.HasLogin(snapshot?.Auth);
    public bool HasStagedOfficialLogin => stagedOfficialAuth is not null;
    public IReadOnlyDictionary<string, string?> Edits => new System.Collections.ObjectModel.ReadOnlyDictionary<string, string?>(edits);
    public IReadOnlyDictionary<string, string> FieldErrors => new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(errors);
    public IReadOnlyDictionary<string, string?> PendingFields => new System.Collections.ObjectModel.ReadOnlyDictionary<string, string?>(
        edits.Concat(invalidInputs.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)))
            .GroupBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal));
    public bool SynchronizeCatalog => catalogSync;
    public string? CatalogSource => catalogSource;
    public bool NeedsApiKeyMigration => baseline?.NeedsApiKeyMigration == true;
    public string CredentialSource => baseline?.CredentialSource ?? "尚未读取凭据";
    public string ProviderName => baseline?.ProviderName ?? "";
    public string? HttpRequiredOrigin => HttpConnectionPolicy.RequiredOrigin(BaseUrl);
    public bool HttpAuthorized
    {
        get => HttpRequiredOrigin is { } origin && httpOrigin == origin;
        set { var next = value ? HttpRequiredOrigin ?? "" : ""; if (Set(ref httpOrigin, next)) ConnectionInputChanged(); }
    }
    public bool ConnectionChanged => baseline is { } saved && (migrate || Mode == ConnectionMode.OfficialWithApi && stagedOfficialAuth is not null || BaseUrl != saved.BaseUrl || ApiKey != saved.ApiKey || Kind != saved.Kind || Mode != saved.Mode ||
        Mode == ConnectionMode.OfficialWithApi && UseSystemProxy != saved.UseSystemProxy);
    public bool DetailsChanged => baseline is { } saved && (Name != saved.Details.Name || Note.Replace("\r\n", "\n") != saved.Details.Note || httpOrigin != saved.Details.HttpOrigin ||
        Mode == ConnectionMode.OfficialWithApi && GatewayHeaderTimeout != saved.Details.GatewayHeaderTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public bool HasDraft => baseline is { } saved && (ConnectionChanged || Model != saved.Model || DetailsChanged || edits.Count > 0 || invalidInputs.Count > 0 || catalogSync);

    public void Load(ConfigSnapshot? value)
    {
        loadGeneration++;
        ClearStagedAuthentication(); ClearServiceCatalog(); edits.Clear(); invalidInputs.Clear(); errors.Clear(); catalogSync = reviewChanged = false; catalogSource = null;
        snapshot = null; baseline = null; baseUrl = apiKey = model = name = note = httpOrigin = "";
        gatewayHeaderTimeout = ConnectionDetails.DefaultHeaderTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        kind = ConnectionKind.ResponsesApi; mode = ConnectionMode.Direct; proxy = migrate = false;
        try
        {
            if (value is null) return;
            baseline = store.ReadSettings(value); snapshot = value;
            baseUrl = baseline.BaseUrl; apiKey = baseline.ApiKey; model = baseline.Model; name = baseline.Details.Name; note = baseline.Details.Note;
            httpOrigin = baseline.Details.HttpOrigin; kind = baseline.Kind; mode = baseline.Mode; proxy = baseline.UseSystemProxy;
            gatewayHeaderTimeout = baseline.Details.GatewayHeaderTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        finally { Revision++; NotifyAll(); }
    }
    public ConnectionSettings Settings()
    {
        if (snapshot is null || baseline is null) throw new InvalidOperationException("请先读取有效的连接配置。");
        var headerTimeout = baseline.Details.GatewayHeaderTimeoutSeconds;
        if (Mode == ConnectionMode.OfficialWithApi &&
            (!int.TryParse(GatewayHeaderTimeout, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out headerTimeout) || headerTimeout is < 60 or > 3600))
            throw new ArgumentException("模型网关等待响应头需为 60 至 3600 秒的整数。");
        return baseline with { BaseUrl = BaseUrl, ApiKey = ApiKey, Model = Model, Kind = Kind, Mode = Mode, UseSystemProxy = UseSystemProxy,
            Details = new(Name, Note, httpOrigin) { GatewayHeaderTimeoutSeconds = headerTimeout }, PreservePreferenceValues = true };
    }
    public ConfigurationPlanRequest Request()
    {
        if (errors.Count > 0) throw new InvalidOperationException("有配置项尚未填写正确，请在全部配置中检查行内提示。当前草稿已保留。");
        var settings = Settings();
        return new(snapshot!, settings, Edits: new Dictionary<string, string?>(edits), UpdateConnection: ConnectionChanged,
            UpdateModel: Model != baseline!.Model, UpdateDetails: DetailsChanged, OfficialAuth: Mode == ConnectionMode.OfficialWithApi ? stagedOfficialAuth?.ToArray() : null,
            SynchronizeCatalog: catalogSync, ReviewPolicyChanged: reviewChanged, CatalogSource: catalogSource,
            ServiceCatalog: serviceCatalog?.ToArray(), ServiceCatalogFingerprint: serviceCatalogFingerprint);
    }
    public TomlDocument Document()
    {
        var document = new TomlDocument(snapshot?.Config);
        foreach (var (path, value) in edits) { if (value is null) document.Remove(path); else document.SetRaw(path, value); }
        return document;
    }
    public void SetOverride(string path, string? value)
    {
        if (snapshot is null) throw new InvalidOperationException("请先读取有效配置。");
        path = TomlDocument.FormatPath(TomlDocument.ParsePath(path));
        ConfigSchema.Instance.Validate(path, value, true);
        if (value is not null) value = ConfigSchema.Literal(TomlDocument.ParseModel("value = " + value)["value"]);
        var projected = new TomlDocument(snapshot.Config);
        foreach (var (key, edit) in edits.Where(pair => pair.Key != path)) { if (edit is null) projected.Remove(key); else projected.SetRaw(key, edit); }
        if (value is null) projected.Remove(path); else projected.SetRaw(path, value);
        foreach (var (key, edit) in edits.Where(pair => pair.Key != path))
            if (edit is not null && (projected.Get(key) is not { } retained || ConfigSchema.Literal(retained) != edit))
                throw new ArgumentException("此字段与其他草稿的父子路径冲突，请先撤销相关字段后再编辑。");
        var cleared = invalidInputs.Remove(path); errors.Remove(path);
        var previous = new TomlDocument(snapshot.Config).Get(path) is { } saved ? ConfigSchema.Literal(saved) : null;
        if (previous == value) { if (edits.Remove(path) || cleared) DraftChanged(); return; }
        if (edits.TryGetValue(path, out var existing) && existing == value) { if (cleared) DraftChanged(); return; }
        if (path == "approvals_reviewer") ClearServiceCatalog();
        edits[path] = value; DraftChanged();
    }
    public string? FieldInput(string path) => invalidInputs.TryGetValue(path, out var invalid) ? invalid :
        Document().Get(path) is { } value ? ConfigSchema.Literal(value) : null;
    public void EditField(string path, string value)
    {
        if (snapshot is null) throw new InvalidOperationException("请先读取有效配置。");
        path = TomlDocument.FormatPath(TomlDocument.ParsePath(path));
        try { SetOverride(path, value); }
        catch (Exception error) when (error is ArgumentException or InvalidDataException or InvalidOperationException)
        {
            invalidInputs[path] = value;
            errors[path] = TomlDocument.IsSensitivePath(path) ? "取值格式不正确，请检查类型与 TOML 写法。" : error.Message;
            DraftChanged();
        }
    }
    public void UndoField(string path)
    {
        var changed = edits.Remove(path); changed |= invalidInputs.Remove(path); errors.Remove(path);
        if (changed) DraftChanged();
    }
    public TemplateDraftApplication StageTemplate(ConfigTemplate template, IEnumerable<string> selectedPaths)
    {
        if (snapshot is null) throw new InvalidOperationException("请先读取有效配置。");
        template = TemplateRepository.Validate(template);
        var candidate = new Dictionary<string, string?>(PendingFields, StringComparer.Ordinal);
        var application = template.StageSelected(new TomlDocument(snapshot.Config).Model, candidate, selectedPaths);
        AdoptFieldDraft(candidate); return new(snapshot, application, loadGeneration);
    }
    public TemplateUndoResult UndoTemplate(TemplateDraftApplication application)
    {
        if (!ReferenceEquals(snapshot, application.Snapshot) || loadGeneration != application.Generation) throw new InvalidOperationException("配置已重新读取，不能撤销之前工作区的方案。");
        var candidate = new Dictionary<string, string?>(PendingFields, StringComparer.Ordinal);
        var result = application.Application.Undo(candidate); AdoptFieldDraft(candidate); return result;
    }
    private void AdoptFieldDraft(Dictionary<string, string?> candidate)
    {
        edits.Clear(); invalidInputs.Clear(); errors.Clear(); var projected = new TomlDocument(snapshot?.Config);
        foreach (var (path, value) in candidate)
        {
            try
            {
                ConfigSchema.Instance.Validate(path, value, true);
                var next = new TomlDocument(projected.Bytes); if (value is null) next.Remove(path); else next.SetRaw(path, value);
                foreach (var (key, existing) in edits)
                    if (existing is not null && (next.Get(key) is not { } retained || ConfigSchema.Literal(retained) != existing)) throw new ArgumentException("方案与其他草稿的父子路径冲突，请在全部配置中核对。");
                projected = next; edits[path] = value;
            }
            catch (Exception error) when (error is ArgumentException or InvalidDataException or InvalidOperationException)
            { invalidInputs[path] = value ?? ""; errors[path] = TomlDocument.IsSensitivePath(path) ? "取值格式不正确，请检查类型与 TOML 写法。" : error.Message; }
        }
        DraftChanged();
    }
    public void SetDedicatedReviewer(bool enabled)
    {
        var document = Document();
        ClearServiceCatalog(); reviewChanged = enabled; catalogSync = false; catalogSource = null;
        SetOverride("approvals_reviewer", TomlDocument.Quote(enabled ? "auto_review" : "user"));
        if (enabled && document.Get("approval_policy") is null) SetOverride("approval_policy", TomlDocument.Quote("on-request"));
        DraftChanged();
    }
    public void StageCatalog(string? source = null)
    {
        if (snapshot is null) throw new InvalidOperationException("请先读取有效配置。");
        ClearServiceCatalog(); catalogSync = true; catalogSource = source is null ? null : Path.GetFullPath(source, snapshot.Home); DraftChanged();
    }
    public bool AdoptDiscoveredCatalog(ModelCatalogResult result, ConnectionSettings source)
    {
        if (snapshot is null) throw new InvalidOperationException("请先读取有效配置。");
        var fingerprint = (source with { Model = "model-catalog" }).Fingerprint();
        if (fingerprint != (Settings() with { Model = "model-catalog" }).Fingerprint())
            throw new InvalidOperationException("连接设置已改变，请重新获取模型。");
        var previousServiceDraft = serviceCatalog is not null;
        ClearServiceCatalog();
        // Explicit file imports keep their chosen source. Ordinary discovery and
        // user-review mode do not silently enable a managed reviewer.
        if (catalogSource is not null || Document().String("approvals_reviewer") != "auto_review" || result.NativeCatalog is null)
        { if (previousServiceDraft) DraftChanged(); return false; }
        ManagedModelCatalogStore.ValidateNative(result.NativeCatalog);
        serviceCatalog = result.NativeCatalog.ToArray(); serviceCatalogFingerprint = fingerprint;
        catalogSync = true; DraftChanged(); return true;
    }
    private void ClearServiceCatalog()
    {
        if (serviceCatalog is not null) catalogSync = false;
        serviceCatalog = null; serviceCatalogFingerprint = null;
    }
    private void ConnectionInputChanged() { ClearServiceCatalog(); DraftChanged(); }

    public void AdoptOfficialLogin(OfficialLoginRead read)
    {
        if (snapshot is null || !string.Equals(snapshot.Home, read.Snapshot.Home, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("配置目录已改变，未采用登录读取结果。");
        var nextBaseline = store.ReadSettings(read.Snapshot);
        ClearStagedAuthentication(); ClearServiceCatalog();
        snapshot = read.Snapshot; baseline = nextBaseline; stagedOfficialAuth = read.TakeStagedAuthentication();
        Revision++; NotifyAll();
    }
    private void ClearStagedAuthentication() { if (stagedOfficialAuth is { } auth) CryptographicOperations.ZeroMemory(auth); stagedOfficialAuth = null; }
    public void Dispose() { ClearStagedAuthentication(); ClearServiceCatalog(); }
    private void DraftChanged() { Revision++; Changed(nameof(HasDraft)); Changed(nameof(ConnectionChanged)); Changed(nameof(DetailsChanged)); Changed(nameof(HttpRequiredOrigin)); Changed(nameof(HttpAuthorized)); Changed(nameof(Revision)); }
    private void NotifyAll()
    {
        foreach (var property in new[] { nameof(BaseUrl), nameof(ApiKey), nameof(Model), nameof(Name), nameof(Note), nameof(GatewayHeaderTimeout), nameof(Kind), nameof(Mode), nameof(UseSystemProxy),
            nameof(MigrateLegacyPat), nameof(Available), nameof(HasOfficialLogin), nameof(HasCurrentOfficialLogin), nameof(HasStagedOfficialLogin), nameof(NeedsApiKeyMigration), nameof(CredentialSource), nameof(ProviderName),
            nameof(HasDraft), nameof(HttpRequiredOrigin), nameof(HttpAuthorized), nameof(Revision) }) Changed(property);
    }
}

public sealed class TemplateDraftApplication
{
    internal ConfigSnapshot Snapshot { get; }
    internal TemplateApplication Application { get; }
    internal long Generation { get; }
    public int Count => Application.Count;
    internal TemplateDraftApplication(ConfigSnapshot snapshot, TemplateApplication application, long generation) { Snapshot = snapshot; Application = application; Generation = generation; }
}
