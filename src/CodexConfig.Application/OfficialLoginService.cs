using System.Security.Cryptography;
using CodexConfig.Core;

namespace CodexConfig.Application;

public interface IOfficialLoginRuntime
{
    Task<ConfigSnapshot> LoadAsync(string home, CancellationToken cancellation);
    Task<byte[]?> ReadBackupAsync(string home, CancellationToken cancellation);
}

public sealed class NativeOfficialLoginRuntime(ConfigStore store) : IOfficialLoginRuntime
{
    public Task<ConfigSnapshot> LoadAsync(string home, CancellationToken cancellation) => Task.Run(() => store.Load(home), cancellation);
    public Task<byte[]?> ReadBackupAsync(string home, CancellationToken cancellation) => Task.Run(() => store.ReadOfficialLoginBackup(home), cancellation);
}

// Authentication is held only for an explicit draft, never included in status text
// or written as a side effect of reading a current login or encrypted backup.
public sealed class OfficialLoginRead : IDisposable
{
    private byte[]? staged;
    private readonly bool requiresSave;
    public ConfigSnapshot Snapshot { get; }
    public bool FromCurrentFile { get; }
    public bool HasStagedAuthentication => staged is not null;
    public string Message => FromCurrentFile && requiresSave ? "已暂存当前目录的官方登录；确认保存后写入文件凭据，模型入口与未保存草稿已保留。"
        : FromCurrentFile ? "已读取当前官方登录，模型入口与未保存草稿已保留。"
        : "已从当前目录的加密备份暂存官方登录，确认保存后启用。有效期与刷新由 Codex 管理。";
    internal OfficialLoginRead(ConfigSnapshot snapshot, bool current, byte[]? staged)
    { Snapshot = snapshot; FromCurrentFile = current; this.staged = staged; requiresSave = staged is not null; }
    public byte[]? TakeStagedAuthentication() { var value = staged; staged = null; return value; }
    public void Dispose() { if (staged is { } bytes) CryptographicOperations.ZeroMemory(bytes); staged = null; }
}

public sealed class OfficialLoginService(ConfigStore store, IOfficialLoginRuntime? runtime = null)
{
    private readonly IOfficialLoginRuntime runtime = runtime ?? new NativeOfficialLoginRuntime(store);
    public async Task<OfficialLoginRead> ReadAsync(ConfigSnapshot before, bool fromBackup, Func<bool> isCurrent,
        CancellationToken cancellation = default)
    {
        void ValidateContext()
        {
            cancellation.ThrowIfCancellationRequested();
            if (!isCurrent()) throw new OperationCanceledException("工作区或草稿已改变，未采用本次登录读取结果。", cancellation);
            if (store.HasPending(before.Home)) throw new IOException("请先恢复未完成的保存，再读取官方登录。");
        }
        ValidateContext();
        var latest = await runtime.LoadAsync(before.Home, cancellation);
        ValidateContext();
        if (!string.Equals(before.Home, latest.Home, StringComparison.OrdinalIgnoreCase) ||
            !CodexApplySnapshot.Compare(before, latest with { Auth = before.Auth }).CanContinue)
            throw new IOException("配置或连接资料也已更改。请先核对外部修改并重新读取配置；当前草稿已保留。");
        _ = store.ReadSettings(latest);
        var live = OfficialCredentials.HasLogin(latest.Auth);
        byte[]? auth = null;
        try
        {
            auth = live ? OfficialCredentials.ForMixed(latest.Auth)
                : fromBackup ? await runtime.ReadBackupAsync(before.Home, cancellation) : null;
            ValidateContext();
            if (!OfficialCredentials.HasLogin(auth)) throw new InvalidOperationException(fromBackup
                ? "当前目录没有可用的官方登录备份。请先在 Codex 桌面登录 ChatGPT，再回来重新读取。"
                : "当前目录未检测到官方文件登录。请先在 Codex 桌面完成 ChatGPT 登录；已使用 API Key 时也可选择恢复本目录的官方登录备份。");
            // A backup read can be slow; do not adopt an older login over one
            // that the native client wrote while that read was pending.
            var current = await runtime.LoadAsync(before.Home, cancellation);
            ValidateContext();
            if (!latest.SameContents(current)) throw new IOException("读取期间本地登录或配置已改变，请重新读取；当前草稿已保留。");
            var needsStage = !live || new TomlDocument(latest.Config).String("cli_auth_credentials_store") is not (null or "file");
            var result = new OfficialLoginRead(latest, live, needsStage ? auth : null);
            if (needsStage) auth = null;
            return result;
        }
        finally { if (auth is not null) CryptographicOperations.ZeroMemory(auth); }
    }
}
