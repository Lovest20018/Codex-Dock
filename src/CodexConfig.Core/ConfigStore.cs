using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodexConfig.Core;

public sealed class ConfigStore(IUserEnvironment environment)
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
        { Bridge = ReadFile(Path.Combine(home, MixedConnection.FileName)) };
    }

    public ConnectionSettings ReadSettings(ConfigSnapshot snapshot)
    {
        var doc = new TomlDocument(snapshot.Config);
        if (doc.String("model_provider") == MixedConnection.Provider)
        {
            var connection = snapshot.Bridge is null ? throw new InvalidDataException("缺少混合连接文件，请从备份恢复或重新配置连接。") : MixedConnection.Read(snapshot.Bridge);
            return new(connection.BaseUrl, connection.ApiKey, doc.String("model") ?? "gpt-6.1-sol")
            { Mode = ConnectionMode.OfficialWithApi, Kind = connection.Kind, PreservePreferenceValues = true };
        }
        string key = "";
        var choice = AuthenticationChoice.Auto;
        if (snapshot.Auth is not null)
        {
            using var auth = ParseAuth(snapshot.Auth);
            var isKey = auth.RootElement.TryGetProperty("auth_mode", out var discriminator) && discriminator.ValueKind == JsonValueKind.String && discriminator.GetString() == "apikey";
            foreach (var field in isKey ? new[] { "OPENAI_API_KEY" } : new[] { "personal_access_token", "OPENAI_API_KEY" })
                if (auth.RootElement.TryGetProperty(field, out var token) && token.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(token.GetString()))
                { key = token.GetString()!; choice = field == "personal_access_token" ? AuthenticationChoice.Pat : AuthenticationChoice.ApiKey; break; }
        }
        var features = doc.Model.TryGetValue("features", out var value) ? value as Tomlyn.Model.TomlTable : null;
        bool Feature(string name) => features is null || !features.TryGetValue(name, out var enabled) || enabled is not false;
        var url = doc.String("openai_base_url") ?? doc.String("chatgpt_base_url") ?? snapshot.AuthApiBaseUrl ?? "";
        var relay = string.IsNullOrEmpty(url) || (Uri.TryCreate(url, UriKind.Absolute, out var address) && address.AbsolutePath.TrimEnd('/') is "/backend-api/codex" or "/codex/v1");
        return new(url, key, doc.String("model") ?? "gpt-6.1-sol", Feature("fast_mode"), Feature("context_management"), doc.String("web_search") != "disabled")
        { Authentication = choice, Kind = relay ? ConnectionKind.PatRelay : ConnectionKind.ResponsesApi, WebSearchMode = doc.String("web_search") };
    }

    public ConfigPlan Plan(ConfigSnapshot before, ConnectionSettings settings, IReadOnlyDictionary<string, string?>? edits = null, bool updateConnection = true, bool updateModel = false, byte[]? officialAuth = null)
    {
        var doc = new TomlDocument(before.Config);
        if (before.Auth is not null) using (ParseAuth(before.Auth)) { }
        var notes = new List<string>();
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
            if (before.Bridge is not null && new TomlDocument(before.Config).String("model_provider") == MixedConnection.Provider)
                ValidateMixedRoute(doc, MixedConnection.Read(before.Bridge));
            return new(before, doc.Bytes, before.Auth, before.AuthApiBaseUrl, doc.Masked(),
                before.Auth is null ? "尚无认证文件。" : "认证配置保持现值（内容已隐藏）。", ["仅保存配置项，认证配置保持现值。"])
            { Bridge = before.Bridge };
        }
        settings = settings.Normalize();
        if (settings.Mode == ConnectionMode.OfficialWithApi)
            return PlanMixed(before, settings, doc, officialAuth);
        foreach (var name in new[] { "profile", "forced_login_method", "forced_chatgpt_workspace_id" })
            if (doc.Get(name) is not null) notes.Add($"保留现有 {name}；它可能覆盖当前设置，请核查生效配置。");
        if (doc.String("cli_auth_credentials_store") is { } credentials && credentials != "file")
            notes.Add($"保留现有 cli_auth_credentials_store={credentials}；Codex 可能优先读取其他凭据存储，请核查实际认证。");
        var decision = settings.Decide();
        doc.Set("model", settings.Model);
        doc.Set("model_provider", "openai");
        doc.Set("openai_base_url", decision.ModelBaseUrl);
        if (before.Bridge is not null)
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
        return new(before, doc.Bytes, Encoding.UTF8.GetBytes(settings.RenderAuth()), decision.AuthApiOrigin ?? before.AuthApiBaseUrl,
            doc.Masked(), settings.RenderAuth(true), notes, decision.Mode, decision.Reason);
    }

    private static ConfigPlan PlanMixed(ConfigSnapshot before, ConnectionSettings settings, TomlDocument doc, byte[]? officialAuth)
    {
        ValidateMixedRestrictions(doc);
        if (before.Bridge is null && doc.Get("model_providers." + MixedConnection.Provider) is { } reserved && reserved is not Tomlyn.Model.TomlTable { Count: 0 })
            throw new InvalidOperationException("混合连接的保留供应商名称已被占用，请先更名。");
        var auth = OfficialCredentials.ForMixed(officialAuth ?? before.Auth);
        var previous = before.Bridge is null ? null : MixedConnection.Read(before.Bridge);
        var bridge = MixedConnection.Create(settings, previous);
        doc.Set("model", settings.Model);
        doc.Set("model_provider", MixedConnection.Provider);
        doc.Set("openai_base_url", bridge.LocalBaseUrl);
        doc.Remove("chatgpt_base_url");
        doc.Set("cli_auth_credentials_store", "file");
        var provider = "model_providers." + MixedConnection.Provider + ".";
        doc.Set(provider + "name", "Codex Config connection");
        doc.Set(provider + "base_url", bridge.LocalBaseUrl);
        doc.Set(provider + "wire_api", "responses");
        doc.Set(provider + "requires_openai_auth", true);
        doc.Set(provider + "experimental_bearer_token", bridge.LocalToken);
        doc.Set(provider + "supports_websockets", false);
        ValidateMixedRoute(doc, bridge);
        var notes = new List<string>
        {
            "保留官方 OAuth 登录；主对话与后台模型请求经本地连接服务转发到所选模型入口。",
            "Krill 凭据单独加密保存；本地连接服务替换认证头，不转发官方 OAuth Token。",
            "官方登录使用文件凭据存储；移除 ChatGPT 后端地址覆盖和 PAT 身份环境变量。",
            "Fast、自动审核、搜索与压缩仍由当前客户端、凭据和模型入口决定；模型目录不代表能力已验证。"
        };
        if (doc.String("forced_chatgpt_workspace_id") is not null) notes.Add("保留现有 forced_chatgpt_workspace_id；Codex 会检查账号是否属于指定工作区。");
        return new(before, doc.Bytes, auth, null, doc.Masked(), "官方登录凭据保留或更新（内容已隐藏）。\n模型访问凭据另行加密保存。", notes,
            AuthenticationMode.OfficialWithApi, "官方账号 + 独立模型连接") { Bridge = bridge.Protect() };
    }

    private static void ValidateMixedRestrictions(TomlDocument doc)
    {
        if (doc.String("profile") is { Length: > 0 })
            throw new InvalidOperationException("当前启用了 profile。请先在常用设置中移除 profile 覆盖后保存混合连接，避免模型请求走其他入口。");
        if (doc.String("forced_login_method") is { } restriction && restriction != "chatgpt")
            throw new InvalidOperationException("forced_login_method 限制了官方账号登录，请先调整此项。");
        if (doc.Get("model_providers.openai") is not null)
            throw new InvalidOperationException("当前自定义了内置 openai 供应商，请先移除此覆盖，避免后台模型请求走其他入口。");
    }

    private static void ValidateMixedRoute(TomlDocument doc, MixedConnection bridge)
    {
        ValidateMixedRestrictions(doc);
        var prefix = "model_providers." + MixedConnection.Provider + ".";
        if (doc.String("model_provider") != MixedConnection.Provider || doc.String("openai_base_url") != bridge.LocalBaseUrl ||
            doc.Get(prefix + "base_url") as string != bridge.LocalBaseUrl || doc.Get(prefix + "experimental_bearer_token") as string != bridge.LocalToken ||
            doc.Get(prefix + "requires_openai_auth") is not true || doc.Get(prefix + "supports_websockets") is not false ||
            doc.Get(prefix + "wire_api") as string != "responses" || doc.String("chatgpt_base_url") is not null || doc.String("cli_auth_credentials_store") != "file")
            throw new InvalidOperationException("混合连接的路由或认证方式被更改，请到服务连接页重新保存连接。");
        foreach (var name in new[] { "env_key", "env_http_headers", "http_headers", "auth" })
            if (doc.Get(prefix + name) is not null) throw new InvalidOperationException("混合连接使用独立的认证头，不能额外覆盖供应商认证。");
    }

    public string Apply(ConfigPlan plan) => Commit(plan.Before, plan.Config, plan.Auth, plan.AuthApiBaseUrl, "save", plan.Bridge);

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
            record.AfterBridgeHash is not null && Hash(current.Bridge) != record.AfterBridgeHash)
            throw new InvalidOperationException("配置或环境变量已被其他程序修改，不能直接覆盖恢复。请先检查源文件。");
        return Commit(current, record.BeforeConfig, record.BeforeAuth, record.BeforeEnv, "restore", record.AfterBridgeHash is null ? current.Bridge : record.BeforeBridge);
    }

    private string Commit(ConfigSnapshot before, byte[]? config, byte[]? auth, string? authApiUrl, string action, byte[]? bridge)
    {
        using var writeLock = Lock(before.Home);
        if (HasPending(before.Home)) throw new InvalidOperationException("发现未完成的保存，请先点击恢复未完成保存。");
        EnsureUnchanged(before);
        foreach (var file in new[] { "config.toml", "auth.json", MixedConnection.FileName })
        {
            var target = Path.Combine(before.Home, file);
            if (File.Exists(target) && File.GetAttributes(target).HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("配置文件是符号链接，请选择实际配置目录后再保存。");
        }
        var record = new Backup(DateTimeOffset.UtcNow, action, before.Config, before.Auth, before.AuthApiBaseUrl,
            Hash(config), Hash(auth), authApiUrl, BeforeBridge: before.Bridge, AfterBridgeHash: Hash(bridge));
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
            if (before.AuthApiBaseUrl != authApiUrl) environment.Write(authApiUrl);
            var after = Load(before.Home);
            if (Hash(after.Config) != record.AfterConfigHash || Hash(after.Auth) != record.AfterAuthHash || after.AuthApiBaseUrl != record.AfterEnv || Hash(after.Bridge) != record.AfterBridgeHash)
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
            (current.AuthApiBaseUrl != record.BeforeEnv && current.AuthApiBaseUrl != record.AfterEnv) ||
            (record.AfterTestBaselineHash is not null && !Expected(baselineCurrent, record.BeforeTestBaseline, record.AfterTestBaselineHash)))
            throw new IOException("恢复前发现其他程序修改了配置，已停止恢复以保留这些修改。");
        if (Hash(current.Config) != Hash(record.BeforeConfig)) AtomicWrite(Path.Combine(home, "config.toml"), record.BeforeConfig);
        if (Hash(current.Auth) != Hash(record.BeforeAuth)) AtomicWrite(Path.Combine(home, "auth.json"), record.BeforeAuth);
        if (record.AfterBridgeHash is not null && Hash(current.Bridge) != Hash(record.BeforeBridge)) AtomicWrite(Path.Combine(home, MixedConnection.FileName), record.BeforeBridge);
        if (environment.Read() != record.BeforeEnv) environment.Write(record.BeforeEnv);
        if (record.AfterTestBaselineHash is not null && Hash(baselineCurrent) != Hash(record.BeforeTestBaseline))
            AtomicWrite(TestBaselinePath(home), record.BeforeTestBaseline);
    }

    private void EnsureUnchanged(ConfigSnapshot snapshot)
    {
        var current = Load(snapshot.Home);
        if (Hash(current.Config) != Hash(snapshot.Config) || Hash(current.Auth) != Hash(snapshot.Auth) || current.AuthApiBaseUrl != snapshot.AuthApiBaseUrl || Hash(current.Bridge) != Hash(snapshot.Bridge))
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
        byte[]? BeforeBridge = null, string? AfterBridgeHash = null);

}

