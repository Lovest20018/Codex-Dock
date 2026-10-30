using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodexConfig.Core;

public sealed class ConfigStore(IUserEnvironment environment, Func<string, string?>? readEnvironment = null)
{
    private const int MaxBytes = 8 * 1024 * 1024;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CodexConfig.backup.v1");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string DefaultHome => Path.GetFullPath(
        Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } home ? home :
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"));

    public ConfigSnapshot Load(string home)
    {
        home = Path.GetFullPath(home);
        return new(home, ReadFile(Path.Combine(home, "config.toml")), ReadFile(Path.Combine(home, "auth.json")), environment.Read())
        { Bridge = ReadFile(Path.Combine(home, MixedConnection.FileName)), Details = ReadFile(Path.Combine(home, ConnectionDetails.FileName)) };
    }

    public ConnectionSettings ReadSettings(ConfigSnapshot snapshot)
    {
        var doc = new TomlDocument(snapshot.Config);
        var model = doc.Model;
        object? Value(Tomlyn.Model.TomlTable? table, string name) => table?.TryGetValue(name, out var entry) == true ? entry : null;
        string? Text(Tomlyn.Model.TomlTable? table, string name) => Value(table, name) as string;
        string? Variable(string name) => (readEnvironment ?? Environment.GetEnvironmentVariable)(name);
        var providerId = doc.String("model_provider") ?? "openai";
        var providers = Value(model, "model_providers") as Tomlyn.Model.TomlTable;
        var provider = Value(providers, providerId) as Tomlyn.Model.TomlTable;
        var providerName = Text(provider, "name") ?? (providerId == "openai" ? "OpenAI" : providerId);
        var details = ConnectionDetails.Read(snapshot.Details);
        var mightUseBridge = providerId == MixedConnection.Provider || providerId == "openai" &&
            Uri.TryCreate(doc.String("openai_base_url"), UriKind.Absolute, out var route) && route.IsLoopback;
        var savedBridge = snapshot.Bridge is null || !mightUseBridge ? null : MixedConnection.Read(snapshot.Bridge);
        if (doc.String("model_provider") == MixedConnection.Provider && savedBridge is null || savedBridge?.OwnsRoute(doc) is true)
        {
            var connection = savedBridge ?? throw new InvalidDataException("缺少混合连接文件，请从备份恢复或重新配置连接。");
            return new(connection.BaseUrl, connection.ApiKey, doc.String("model") ?? "gpt-6.1-sol")
            { Details = details, Mode = ConnectionMode.OfficialWithApi, Kind = connection.Kind, UseSystemProxy = connection.UseSystemProxy, PreservePreferenceValues = true,
                ProviderId = providerId, ProviderName = providerName, CredentialSource = "本地加密模型凭据" };
        }
        string key = "";
        string credentialSource = "未读取模型凭据";
        var choice = AuthenticationChoice.Auto;
        if (snapshot.Auth is not null)
        {
            using var auth = ParseAuth(snapshot.Auth);
            var isKey = auth.RootElement.TryGetProperty("auth_mode", out var discriminator) && discriminator.ValueKind == JsonValueKind.String && discriminator.GetString() == "apikey";
            var isOfficial = auth.RootElement.TryGetProperty("auth_mode", out var mode) && mode.ValueKind == JsonValueKind.String && mode.GetString() == "chatgpt";
            foreach (var field in isOfficial ? Array.Empty<string>() : isKey ? new[] { "OPENAI_API_KEY" } : new[] { "personal_access_token", "OPENAI_API_KEY" })
                if (auth.RootElement.TryGetProperty(field, out var token) && token.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(token.GetString()))
                { key = token.GetString()!; choice = field == "personal_access_token" ? AuthenticationChoice.Pat : AuthenticationChoice.ApiKey; credentialSource = "auth.json · " + (choice == AuthenticationChoice.Pat ? "PAT" : "API Key"); break; }
        }
        var environmentKey = Text(provider, "env_key");
        var bearer = Text(provider, "experimental_bearer_token");
        if (environmentKey is { Length: > 0 })
        {
            key = Variable(environmentKey) ?? ""; choice = AuthenticationChoice.ApiKey;
            credentialSource = "环境变量 " + environmentKey + (key.Length > 0 ? " · 已读取" : " · 当前进程未设置");
        }
        else if (bearer is { Length: > 0 })
        {
            key = bearer; choice = AuthenticationChoice.ApiKey; credentialSource = "供应商配置 · Bearer Token";
        }
        else if (providerId != "openai" && Value(provider, "requires_openai_auth") is not true)
        {
            key = ""; choice = AuthenticationChoice.Auto;
            credentialSource = provider?.ContainsKey("auth") == true ? "供应商外部认证命令 · 未执行" : "由供应商或客户端管理";
        }
        else if (key.Length == 0 && !OfficialCredentials.HasLogin(snapshot.Auth) && Variable("OPENAI_API_KEY") is { Length: > 0 } environmentToken)
        {
            key = environmentToken; choice = AuthenticationChoice.ApiKey; credentialSource = "环境变量 OPENAI_API_KEY · 已读取";
        }
        if (key.Length == 0 && string.IsNullOrEmpty(environmentKey) &&
            (providerId == "openai" || Value(provider, "requires_openai_auth") is true) && OfficialCredentials.HasLogin(snapshot.Auth))
            credentialSource = "官方账号登录凭据 · 未在线验证";
        var features = doc.Model.TryGetValue("features", out var value) ? value as Tomlyn.Model.TomlTable : null;
        bool Feature(string name) => features is null || !features.TryGetValue(name, out var enabled) || enabled is not false;
        var url = providerId == "openai" ? doc.String("openai_base_url") ?? Text(provider, "base_url") ?? Variable("OPENAI_BASE_URL") ?? doc.String("chatgpt_base_url") ?? snapshot.AuthApiBaseUrl ?? ""
            : Text(provider, "base_url") ?? "";
        var relay = providerId == "openai" && (string.IsNullOrEmpty(url) || (Uri.TryCreate(url, UriKind.Absolute, out var address) && address.AbsolutePath.TrimEnd('/') is "/backend-api/codex" or "/codex/v1"));
        return new(url, key, doc.String("model") ?? "gpt-6.1-sol", Feature("fast_mode"), Feature("context_management"), doc.String("web_search") != "disabled")
        { Details = details, Authentication = choice, Kind = relay ? ConnectionKind.PatRelay : ConnectionKind.ResponsesApi, WebSearchMode = doc.String("web_search"),
            ProviderId = providerId, ProviderName = providerName, CredentialSource = credentialSource };
    }

    public ConfigPlan Plan(ConfigSnapshot before, ConnectionSettings settings, IReadOnlyDictionary<string, string?>? edits = null, bool updateConnection = true, bool updateModel = false, bool updateDetails = false, byte[]? officialAuth = null)
    {
        var doc = new TomlDocument(before.Config);
        if (before.Auth is not null) using (ParseAuth(before.Auth)) { }
        var notes = new List<string>();
        var details = updateDetails ? settings.Details.Normalize().Protect() : before.Details;
        if (edits is not null)
            foreach (var (path, repr) in edits)
            {
                ConfigSchema.Instance.Validate(path, repr, !updateConnection || settings.PreservePreferenceValues);
                if (repr is null) doc.Remove(path); else doc.SetRaw(path, repr);
            }
        if (updateModel)
        {
            var model = settings.Model.Trim();
            if (model.Length == 0 || model.Any(char.IsControl)) throw new ArgumentException("请填写有效的模型 ID。");
            doc.Set("model", model);
        }
        if (!updateConnection)
        {
            if (before.Bridge is { } saved && MixedConnection.Read(saved).OwnsRoute(new TomlDocument(before.Config)))
            {
                var savedConnection = MixedConnection.Read(saved);
                if (doc.String("profile") is { Length: > 0 } || doc.String("cli_auth_credentials_store") is not (null or "file") ||
                    savedConnection.NativeRoute && doc.Get("features.enable_request_compression") is not false ||
                    !savedConnection.OwnsRoute(doc) || doc.String("forced_login_method") is { } forced && forced != "chatgpt")
                    throw new InvalidOperationException("这些设置会覆盖混合连接的登录或传输方式，请先切换到直连，或保留文件凭据和未压缩请求。");
            }
            return new(before, doc.Bytes, before.Auth, before.AuthApiBaseUrl, doc.Masked(),
                before.Auth is null ? "尚无认证文件。" : "认证配置保持现值（内容已隐藏）。", ["仅保存配置项，认证配置保持现值。"])
            { Bridge = before.Bridge, Details = details };
        }
        settings = settings.Normalize();
        if (settings.Mode == ConnectionMode.OfficialWithApi)
            return PlanMixed(before, settings, doc, officialAuth) with { Details = details };
        RestoreMixedPreferences(before, doc);
        foreach (var name in new[] { "profile", "forced_login_method", "forced_chatgpt_workspace_id" })
            if (doc.Get(name) is not null) notes.Add($"保留现有 {name}；它可能覆盖当前设置，请核查生效配置。");
        if (doc.String("cli_auth_credentials_store") is { } credentials && credentials != "file")
            notes.Add($"保留现有 cli_auth_credentials_store={credentials}；Codex 可能优先读取其他凭据存储，请核查实际认证。");
        var decision = settings.Decide();
        doc.Set("model", settings.Model);
        doc.Set("model_provider", "openai");
        doc.Set("openai_base_url", decision.ModelBaseUrl);
        if (before.Bridge is not null && MixedConnection.Read(before.Bridge) is { } owned &&
            doc.Get("model_providers." + MixedConnection.Provider + ".base_url") as string == owned.LocalBaseUrl &&
            doc.Get("model_providers." + MixedConnection.Provider + ".experimental_bearer_token") as string == owned.LocalToken)
            foreach (var path in TomlDocument.ValuePaths(doc.Model).Where(path => path.StartsWith("model_providers." + MixedConnection.Provider + ".", StringComparison.Ordinal)).ToArray()) doc.Remove(path);
        if (decision.ChatGptBaseUrl is not null) doc.Set("chatgpt_base_url", decision.ChatGptBaseUrl);
        var search = settings.WebSearchMode ?? (settings.WebSearch ? "live" : "disabled");
        if (!settings.PreservePreferenceValues)
        {
            if (search is not ("disabled" or "cached" or "indexed" or "live")) throw new ArgumentException("联网搜索模式必须为 disabled、cached、indexed 或 live。");
            doc.Set("web_search", search);
            doc.Set("features.fast_mode", settings.FastMode);
            doc.Set("features.context_management", settings.ContextManagement);
        }
        notes.Add(decision.Mode == AuthenticationMode.Pat ? "PAT 双字段；CODEX_AUTHAPI_BASE_URL 写入线路 origin，ChatGPT 后端使用 /backend-api/。" : "API Key 模式明确写入 auth_mode=apikey，不写 PAT；认证环境变量与 ChatGPT 后端保持现值。");
        notes.Add("原文件及用户环境变量纳入加密备份；不删除会话或修改客户端安装文件。");
        var preservedAuthOrigin = before.AuthApiBaseUrl;
        if (before.Bridge is { } previousBytes && MixedConnection.Read(previousBytes) is { NativeRoute: true } previousConnection && preservedAuthOrigin is null)
            preservedAuthOrigin = previousConnection.OriginalAuthApiBaseUrl;
        return new(before, doc.Bytes, Encoding.UTF8.GetBytes(settings.RenderAuth()), decision.AuthApiOrigin ?? preservedAuthOrigin,
            doc.Masked(), settings.RenderAuth(true), notes, decision.Mode, decision.Reason) { Details = details };
    }

    public string Apply(ConfigPlan plan) => Commit(plan.Before, plan.Config, plan.Auth, plan.AuthApiBaseUrl, "save", plan.Bridge, plan.Details);

    private static ConfigPlan PlanMixed(ConfigSnapshot before, ConnectionSettings settings, TomlDocument doc, byte[]? officialAuth)
    {
        if (doc.String("profile") is { Length: > 0 }) throw new InvalidOperationException("当前 profile 可能覆盖模型路由，请先移除 profile 覆盖后启用混合连接。");
        if (doc.String("forced_login_method") is { } forced && forced != "chatgpt") throw new InvalidOperationException("forced_login_method 限制了官方登录，请先调整此项。");
        if (doc.Get("model_providers.openai") is not null) throw new InvalidOperationException("检测到内置 openai 的自定义覆盖，请先处理该覆盖。");
        var storage = doc.String("cli_auth_credentials_store");
        if (storage is not (null or "file") && officialAuth is null) throw new InvalidOperationException("混合连接当前需要可验证的文件登录凭据；当前凭据存储为 " + storage + "。请使用原生登录或明确选择文件存储，原有登录不会被自动修改。");
        var auth = OfficialCredentials.ForMixed(officialAuth ?? before.Auth);
        var previous = before.Bridge is null ? null : MixedConnection.Read(before.Bridge);
        var connection = MixedConnection.Create(settings, previous);
        if (previous is null || !previous.NativeRoute)
        {
            foreach (var path in new[] { "chatgpt_base_url", "cli_auth_credentials_store", "features.enable_request_compression" })
                connection.OriginalValues[path] = doc.Get(path) is { } value ? ConfigSchema.Literal(value) : null;
            connection = new MixedConnection { BaseUrl = connection.BaseUrl, ApiKey = connection.ApiKey, Kind = connection.Kind, Port = connection.Port, LocalToken = connection.LocalToken,
                UseSystemProxy = connection.UseSystemProxy, NativeRoute = true, OriginalValues = connection.OriginalValues, OriginalAuthApiBaseUrl = before.AuthApiBaseUrl };
        }
        doc.Set("model", settings.Model); doc.Set("model_provider", "openai");
        doc.Set("openai_base_url", connection.NativeBaseUrl);
        doc.Remove("chatgpt_base_url"); doc.Set("cli_auth_credentials_store", "file");
        // Built-in OpenAI compresses by default; generic Responses providers need plain JSON.
        doc.Set("features.enable_request_compression", false);
        var p = "model_providers." + MixedConnection.Provider + ".";
        if (doc.Get("model_providers." + MixedConnection.Provider) is not null && (previous is null ||
            doc.Get(p + "base_url") as string != previous.LocalBaseUrl || doc.Get(p + "experimental_bearer_token") as string != previous.LocalToken))
            throw new InvalidOperationException("兼容 provider 已由其他程序配置，请先核对，保存不会覆盖它。");
        doc.Set(p + "name", "Codex Dock connection"); doc.Set(p + "base_url", connection.LocalBaseUrl);
        doc.Set(p + "wire_api", "responses"); doc.Set(p + "requires_openai_auth", true);
        doc.Set(p + "experimental_bearer_token", connection.LocalToken); doc.Set(p + "supports_websockets", true);
        return new(before, doc.Bytes, auth, null, doc.Masked(), "保留官方登录；模型凭据单独加密保存。", new[] {
            "官方账号身份保留，模型请求使用独立入口；不自动回退到官方推理额度。",
            "普通模型入口使用未压缩 JSON；官方刷新由原生 Codex 处理。",
            "关闭编辑器后，本地网关继续服务。保存后重新打开 Codex。"
        }, AuthenticationMode.OfficialWithApi, "官方账号 + 独立模型入口") { Bridge = connection.Protect() };
    }

    private static void RestoreMixedPreferences(ConfigSnapshot before, TomlDocument doc)
    {
        if (before.Bridge is null) return;
        var connection = MixedConnection.Read(before.Bridge);
        foreach (var (path, original) in connection.OriginalValues)
        {
            var ours = path switch { "features.enable_request_compression" => doc.Get(path) is false, "cli_auth_credentials_store" => doc.String(path) == "file", "chatgpt_base_url" => doc.Get(path) is null, _ => false };
            if (!ours) continue;
            if (original is null) doc.Remove(path); else doc.SetRaw(path, original);
        }
    }

    public string? LatestBackup(string home) => Directory.Exists(DataDir(home))
        ? Directory.EnumerateFiles(DataDir(home), "*.backup").OrderByDescending(Path.GetFileName, StringComparer.Ordinal).FirstOrDefault() : null;

    public bool HasPending(string home) => File.Exists(Path.Combine(DataDir(home), "pending.json"));

    public void Recover(string home)
    {
        using var writeLock = Lock(home);
        var pending = PendingPath(home);
        if (!File.Exists(pending)) return;
        var fileName = File.ReadAllText(pending).Trim();
        if (Path.GetFileName(fileName) != fileName || !fileName.EndsWith(".backup", StringComparison.Ordinal))
            throw new InvalidDataException("未完成保存的恢复记录无效。");
        var backup = ReadBackup(Path.Combine(DataDir(home), fileName));
        RestoreInterrupted(home, backup);
        File.Delete(pending);
        var recovered = Path.Combine(DataDir(home), fileName);
        File.Move(recovered, recovered + ".recovered");
    }

    public string Restore(ConfigSnapshot current)
    {
        if (HasPending(current.Home)) throw new InvalidOperationException("请先恢复未完成的保存。");
        var path = LatestBackup(current.Home) ?? throw new InvalidOperationException("尚无可恢复的备份。");
        var record = ReadBackup(path);
        if (Hash(current.Config) != record.AfterConfigHash || Hash(current.Auth) != record.AfterAuthHash || current.AuthApiBaseUrl != record.AfterEnv ||
            record.AfterBridgeHash is not null && Hash(current.Bridge) != record.AfterBridgeHash ||
            record.AfterDetailsHash is not null && Hash(current.Details) != record.AfterDetailsHash)
            throw new InvalidOperationException("配置或环境变量已被其他程序修改，不能直接覆盖恢复。请先检查源文件。");
        return Commit(current, record.BeforeConfig, record.BeforeAuth, record.BeforeEnv, "restore", record.AfterBridgeHash is null ? current.Bridge : record.BeforeBridge, record.AfterDetailsHash is null ? current.Details : record.BeforeDetails);
    }

    private string Commit(ConfigSnapshot before, byte[]? config, byte[]? auth, string? authApiUrl, string action, byte[]? bridge, byte[]? details)
    {
        using var writeLock = Lock(before.Home);
        if (HasPending(before.Home)) throw new InvalidOperationException("发现未完成的保存，请先点击恢复未完成保存。");
        EnsureUnchanged(before);
        foreach (var file in new[] { "config.toml", "auth.json", MixedConnection.FileName, ConnectionDetails.FileName })
        {
            var target = Path.Combine(before.Home, file);
            if (File.Exists(target) && File.GetAttributes(target).HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("配置文件是符号链接，请选择实际配置目录后再保存。");
        }
        var record = new Backup(DateTimeOffset.UtcNow, action, before.Config, before.Auth, before.AuthApiBaseUrl,
            Hash(config), Hash(auth), authApiUrl, BeforeBridge: before.Bridge, AfterBridgeHash: Hash(bridge), BeforeDetails: before.Details, AfterDetailsHash: Hash(details));
        var name = $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-fffffff}-{Guid.NewGuid():N}.backup";
        var path = Path.Combine(DataDir(before.Home), name);
        AtomicWrite(path, Protect(record));
        try { EnsureUnchanged(before); }
        catch { File.Move(path, path + ".cancelled"); throw; }
        AtomicWrite(PendingPath(before.Home), Encoding.UTF8.GetBytes(name));
        try
        {
            if (Hash(config) != Hash(before.Config)) AtomicWrite(Path.Combine(before.Home, "config.toml"), config);
            if (Hash(auth) != Hash(before.Auth)) AtomicWrite(Path.Combine(before.Home, "auth.json"), auth);
            if (Hash(bridge) != Hash(before.Bridge)) AtomicWrite(Path.Combine(before.Home, MixedConnection.FileName), bridge);
            if (Hash(details) != Hash(before.Details)) AtomicWrite(Path.Combine(before.Home, ConnectionDetails.FileName), details);
            if (before.AuthApiBaseUrl != authApiUrl) environment.Write(authApiUrl);
            var after = Load(before.Home);
            if (Hash(after.Config) != record.AfterConfigHash || Hash(after.Auth) != record.AfterAuthHash || after.AuthApiBaseUrl != record.AfterEnv || Hash(after.Bridge) != record.AfterBridgeHash || Hash(after.Details) != record.AfterDetailsHash)
                throw new IOException("保存后的内容校验未通过。");
            File.Delete(PendingPath(before.Home));
            return path;
        }
        catch (Exception error)
        {
            try { RestoreInterrupted(before.Home, record); File.Delete(PendingPath(before.Home)); }
            catch { throw new IOException("保存未完成，自动回滚也未完成。加密备份已保留，请使用恢复未完成保存。", error); }
            File.Move(path, path + ".rolledback");
            throw new IOException("保存失败，已回滚文件和环境变量。请检查文件权限。", error);
        }
    }

    private void RestoreInterrupted(string home, Backup record)
    {
        var current = Load(home);
        var baselineCurrent = record.AfterTestBaselineHash is not null ? ReadFile(TestBaselinePath(home)) : null;
        static bool Expected(byte[]? value, byte[]? before, string afterHash) => Hash(value) == Hash(before) || Hash(value) == afterHash;
        if (!Expected(current.Config, record.BeforeConfig, record.AfterConfigHash) ||
            !Expected(current.Auth, record.BeforeAuth, record.AfterAuthHash) ||
            (record.AfterBridgeHash is not null && !Expected(current.Bridge, record.BeforeBridge, record.AfterBridgeHash)) ||
            (record.AfterDetailsHash is not null && !Expected(current.Details, record.BeforeDetails, record.AfterDetailsHash)) ||
            (current.AuthApiBaseUrl != record.BeforeEnv && current.AuthApiBaseUrl != record.AfterEnv) ||
            (record.AfterTestBaselineHash is not null && !Expected(baselineCurrent, record.BeforeTestBaseline, record.AfterTestBaselineHash)))
            throw new IOException("恢复前发现其他程序修改了配置，已停止恢复以保留这些修改。");
        if (Hash(current.Config) != Hash(record.BeforeConfig)) AtomicWrite(Path.Combine(home, "config.toml"), record.BeforeConfig);
        if (Hash(current.Auth) != Hash(record.BeforeAuth)) AtomicWrite(Path.Combine(home, "auth.json"), record.BeforeAuth);
        if (record.AfterBridgeHash is not null && Hash(current.Bridge) != Hash(record.BeforeBridge)) AtomicWrite(Path.Combine(home, MixedConnection.FileName), record.BeforeBridge);
        if (record.AfterDetailsHash is not null && Hash(current.Details) != Hash(record.BeforeDetails)) AtomicWrite(Path.Combine(home, ConnectionDetails.FileName), record.BeforeDetails);
        if (environment.Read() != record.BeforeEnv) environment.Write(record.BeforeEnv);
        if (record.AfterTestBaselineHash is not null && Hash(baselineCurrent) != Hash(record.BeforeTestBaseline))
            AtomicWrite(TestBaselinePath(home), record.BeforeTestBaseline);
    }

    private void EnsureUnchanged(ConfigSnapshot snapshot)
    {
        var current = Load(snapshot.Home);
        if (Hash(current.Config) != Hash(snapshot.Config) || Hash(current.Auth) != Hash(snapshot.Auth) || current.AuthApiBaseUrl != snapshot.AuthApiBaseUrl || Hash(current.Bridge) != Hash(snapshot.Bridge) || Hash(current.Details) != Hash(snapshot.Details))
            throw new InvalidOperationException("文件或环境变量已被其他程序修改，请重新读取后再保存。");
    }

    private static FileStream Lock(string home)
    {
        Directory.CreateDirectory(DataDir(home));
        try { return new FileStream(Path.Combine(DataDir(home), "write.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new IOException("另一个配置窗口正在保存，请稍后再试。"); }
    }

    private static string DataDir(string home) => Path.Combine(home, "codex-config-backups");
    private static string PendingPath(string home) => Path.Combine(DataDir(home), "pending.json");
    private static string TestBaselinePath(string home) => Path.Combine(DataDir(home), "pat-test-baseline.dat");
    private static string Hash(byte[]? bytes) => bytes is null ? "absent" : Convert.ToHexString(SHA256.HashData(bytes));
    private static byte[]? ReadFile(string path)
    {
        if (!File.Exists(path)) return null;
        var info = new FileInfo(path);
        if (info.Length > MaxBytes) throw new IOException("配置文件过大，已停止读取。");
        return File.ReadAllBytes(path);
    }

    private static JsonDocument ParseAuth(byte[] bytes)
    {
        try
        {
            var content = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? bytes.AsMemory(3) : bytes.AsMemory();
            var doc = JsonDocument.Parse(content);
            if (doc.RootElement.ValueKind == JsonValueKind.Object) return doc;
            doc.Dispose();
        }
        catch (JsonException) { }
        throw new InvalidDataException("auth.json 不是有效的 JSON 对象，请先修复源文件。");
    }

    private static Backup ReadBackup(string path)
    {
        var encrypted = ReadFile(path) ?? throw new FileNotFoundException("备份不存在。");
        var bytes = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
        try { return JsonSerializer.Deserialize<Backup>(bytes) ?? throw new InvalidDataException("备份格式无效。"); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static byte[] Protect<T>(T value)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        try { return ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    private static void AtomicWrite(string path, byte[]? bytes)
    {
        if (File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("配置文件是符号链接，请选择实际配置目录后再保存。");
        if (bytes is null) { File.Delete(path); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var stage = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { stream.Write(bytes); stream.Flush(true); }
            File.Move(stage, path, overwrite: true);
        }
        finally { if (File.Exists(stage)) File.Delete(stage); }
    }

    private sealed record Backup(DateTimeOffset CreatedUtc, string Action, byte[]? BeforeConfig, byte[]? BeforeAuth,
        string? BeforeEnv, string AfterConfigHash, string AfterAuthHash, string? AfterEnv,
        byte[]? BeforeTestBaseline = null, string? AfterTestBaselineHash = null,
        byte[]? BeforeBridge = null, string? AfterBridgeHash = null,
        byte[]? BeforeDetails = null, string? AfterDetailsHash = null);

}

