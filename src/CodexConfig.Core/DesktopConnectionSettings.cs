using System.Text.Json;

namespace CodexConfig.Core;

public sealed class DesktopConnectionSettings
{
    public string SettingsPath { get; }
    public DesktopConnectionSettings(string home, string? root = null) => SettingsPath = Path.Combine(
        Path.GetDirectoryName(new GatewayStartupStore(home, root).SettingsPath)!, "desktop-connections.json");
    public bool Load()
    {
        if (!File.Exists(SettingsPath)) return false;
        if (new FileInfo(SettingsPath).Length > 4096) throw new InvalidDataException("旧聊天接续设置文件过大。");
        using var document = JsonDocument.Parse(File.ReadAllBytes(SettingsPath));
        var value = document.RootElement.GetProperty("enabled");
        return value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : throw new InvalidDataException("无法读取旧聊天接续设置。");
    }
    public void Save(bool enabled)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var stage = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(stage, JsonSerializer.Serialize(new { enabled })); File.Move(stage, SettingsPath, true); }
        finally { if (File.Exists(stage)) File.Delete(stage); }
    }
}

public sealed record DesktopConnectionState(string Generation, string Provider, string? BaseUrl, bool RestartRequired, string? Reason);

public sealed class DesktopConnectionBackend
{
    private readonly ConfigStore store;
    private readonly ConfigSnapshot baseline;
    private readonly Func<bool> enabled;
    private readonly Func<bool> speedEnabled;
    public bool Enabled => enabled();
    public string Status { get; private set; } = "旧聊天接续已接入，等待聊天操作。";
    public int RestoredChats { get; private set; }
    public bool SpeedEligible
    {
        get { try { return speedEnabled() && DesktopSpeedSettings.Eligible(store, store.Load(baseline.Home)); } catch { return false; } }
    }
    public DesktopConnectionBackend(ConfigStore store, ConfigSnapshot baseline, Func<bool> enabled, Func<bool>? speedEnabled = null)
    { this.store = store; this.baseline = baseline; this.enabled = enabled; this.speedEnabled = speedEnabled ?? (() => new DesktopSpeedSettings(baseline.Home).Load()); }
    public DesktopConnectionState Inspect()
    {
        if (!Enabled) throw new InvalidOperationException("旧聊天接续已关闭。");
        if (store.HasPending(baseline.Home)) throw new IOException("配置有未完成的保存，请先处理备份恢复。");
        var current = store.Load(baseline.Home); var doc = new TomlDocument(current.Config);
        var previous = new TomlDocument(baseline.Config);
        var provider = doc.String("model_provider") ?? "openai";
        if (doc.String("profile") is not null) throw new InvalidOperationException("活动 Profile 的连接需要在客户端中单独核对，未自动续接。");
        var settings = store.ReadSettings(current);
        var identityChanged = !CodexApplySnapshot.SameAuth(baseline.Auth, current.Auth) || baseline.AuthApiBaseUrl != current.AuthApiBaseUrl ||
            new[] { "chatgpt_base_url", "cli_auth_credentials_store", "forced_login_method", "forced_chatgpt_workspace_id" }.Any(path => previous.String(path) != doc.String(path));
        var catalogChanged = baseline.Catalog is null ? current.Catalog is not null : !baseline.Catalog.Same(current.Catalog);
        var restart = identityChanged || catalogChanged;
        var reason = identityChanged ? "登录身份或直连凭据已改变，请使用统一应用入口重新打开 Codex。" :
            catalogChanged ? "模型目录已同步，当前内核需要重新加载。请使用统一应用入口重开 Codex。" : null;
        var route = doc.String("openai_base_url");
        var generation = ManagedModelCatalogStore.Hash(System.Text.Encoding.UTF8.GetBytes(provider + "\n" + settings.Fingerprint() + "\n" + route));
        if (restart) Status = reason!;
        return new(generation, provider, route, restart, reason);
    }
    internal object Handle(JsonElement request)
    {
        var operation = request.GetProperty("operation").GetString();
        if (operation == "inspect") return Inspect();
        if (operation == "prepare")
        {
            var state = Inspect();
            var generation = request.GetProperty("generation").GetString();
            if (state.RestartRequired || state.Generation != generation) throw new IOException(state.Reason ?? "连接在恢复时已改变，请重试。");
            var thread = request.GetProperty("threadId").GetString()!;
            var source = request.GetProperty("sourceProvider").GetString()!;
            var target = request.GetProperty("targetProvider").GetString()!;
            if (target != state.Provider) throw new IOException("目标供应商与当前保存连接不一致。");
            var result = ThreadProviderStore.Prepare(baseline.Home, thread, source, target, () =>
            {
                var current = Inspect();
                if (current.RestartRequired || current.Generation != generation) throw new IOException("连接在准备时已改变。");
            });
            return new { result.ChangedRows, result.Recovered };
        }
        if (operation == "report")
        {
            switch (request.GetProperty("phase").GetString())
            {
                case "deferred": Status = "聊天正在回复，仍使用原连接；空闲后的下一次发送会核对新连接。"; break;
                case "waiting": Status = "正在等待旧聊天卸载，再使用当前连接恢复…"; break;
                case "restored": RestoredChats++; Status = "已恢复 " + RestoredChats + " 个聊天到当前连接；历史与已选参数保留。"; break;
                case "blocked": Status = "聊天接续未完成，草稿与历史保留，请核对提示后重试。"; break;
            }
            return new { ok = true };
        }
        throw new InvalidOperationException("不支持的聊天接续操作。");
    }
}
