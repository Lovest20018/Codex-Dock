using CodexConfig.Core;

namespace CodexConfig.Application;

public interface IUpdateDownloadRuntime
{
    Task<ReleaseUpdate?> CheckAsync(Version current, CancellationToken cancellation);
    Task<VerifiedInstaller> DownloadAsync(ReleaseUpdate update, string directory, IProgress<int>? progress, CancellationToken cancellation);
}

// Core owns release provenance, redirects, size bounds and SHA-256 verification.
public sealed class GitHubUpdateRuntime(Func<GitHubUpdates>? createClient = null) : IUpdateDownloadRuntime
{
    public async Task<ReleaseUpdate?> CheckAsync(Version current, CancellationToken cancellation)
    { using var client = createClient?.Invoke() ?? new GitHubUpdates(); return await client.CheckAsync(current, cancellation); }
    public async Task<VerifiedInstaller> DownloadAsync(ReleaseUpdate update, string directory, IProgress<int>? progress, CancellationToken cancellation)
    { using var client = createClient?.Invoke() ?? new GitHubUpdates(); return await client.DownloadAsync(update, directory, progress, cancellation); }
}

public sealed class UpdateDownloadService(IUpdateDownloadRuntime? runtime = null)
{
    private readonly IUpdateDownloadRuntime runtime = runtime ?? new GitHubUpdateRuntime();
    private int active;
    public bool IsBusy => Volatile.Read(ref active) != 0;
    public Task<ReleaseUpdate?> CheckAsync(Version current, CancellationToken cancellation = default)
        => RunAsync(token => runtime.CheckAsync(current, token), cancellation);
    public Task<VerifiedInstaller> DownloadAsync(ReleaseUpdate update, string directory, IProgress<int>? progress = null, CancellationToken cancellation = default)
        => RunAsync(token => runtime.DownloadAsync(update, directory, progress, token), cancellation);
    private async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> run, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref active, 1, 0) != 0) throw new InvalidOperationException("正在检查或下载更新，请完成后重试。");
        try { var result = await run(cancellation); cancellation.ThrowIfCancellationRequested(); return result; }
        finally { Volatile.Write(ref active, 0); }
    }
}

public interface IUpdateInstallationRuntime
{
    Task WaitForAutomaticAsync(CancellationToken cancellation) => Task.CompletedTask;
    Task StopEnhancementsAsync(CancellationToken cancellation);
    void Launch(VerifiedInstaller installer);
}

public sealed class UpdateInstallationService(WorkspaceApplyCoordinator operations, IGatewayManagementRuntime gateways, IUpdateInstallationRuntime runtime)
{
    public async Task InstallAsync(VerifiedInstaller installer, string home, Func<bool> eligible, CancellationToken cancellation = default)
    {
        void Validate() { cancellation.ThrowIfCancellationRequested(); if (!eligible()) throw new OperationCanceledException("安装条件已改变，请重新核对。"); }
        Validate();
        // Verify before disturbing an active attachment, then let automatic
        // reconnect release its existing shared lease before taking our own.
        using var verified = installer.OpenVerified();
        await runtime.WaitForAutomaticAsync(cancellation); Validate();
        using var operation = operations.TryBeginUpdateInstallation() ?? throw new InvalidOperationException("正在处理其他操作，请完成后再安装。");
        try
        {
            Validate();
            var definitions = new List<GatewayDefinition>();
            foreach (var kind in new[] { GatewayKind.Model, GatewayKind.Mobile })
            { definitions.Add(await gateways.LoadAsync(kind, home, cancellation)); Validate(); }
            operation.Report("停止客户端增强", "正在停止本窗口提供的客户端增强…");
            await runtime.StopEnhancementsAsync(cancellation); Validate();
            operation.Report("停止后台网关", "正在停止手机和模型网关，并确认后台进程退出…");
            foreach (var definition in definitions)
            {
                if (!definition.Configured) continue;
                await gateways.StopAsync(definition, cancellation); Validate();
            }
            foreach (var definition in definitions)
            {
                var latest = await gateways.LoadAsync(definition.Kind, home, cancellation); Validate();
                if (latest.Revision != definition.Revision || latest.Configured != definition.Configured)
                    throw new IOException("停止后网关设置已改变，请刷新并重新核对安装。");
                if ((await gateways.ProbeAsync(latest, cancellation)).Running != false)
                    throw new IOException("尚未确认两个网关均已停止，安装器未启动。请刷新网关状态。");
                Validate();
            }
            runtime.Launch(installer);
            operation.Complete("安装器已启动", "已启动校验通过的正式版安装器，Dock 即将退出。");
        }
        catch (OperationCanceledException) { operation.WaitForUser("安装已取消，请核对网关状态后重试。"); throw; }
        catch (Exception error) { operation.Fail(error.Message); throw; }
    }
}
