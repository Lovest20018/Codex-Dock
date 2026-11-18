using CodexConfig.Core;

namespace CodexConfig.Application;

public sealed class NativeDesktopApplyRuntime(DesktopEnhancementRuntime enhancements, Action<Action> dispatch) : IDesktopApplyRuntime
{
    public bool IsEnhancementRunning => enhancements.Session is { IsRunning: true };
    public Task<IReadOnlyList<CodexDesktopTarget>> FindTargetsAsync(CancellationToken cancellation)
        => Task.Run(() => new CodexDesktop().FindTargets(), cancellation);
    public bool Activate(CodexDesktopTarget target) => new CodexDesktop().Activate(target);
    public Task<DesktopEnhancementPreparation> PrepareEnhancementsAsync(CodexDesktopTarget target, ConfigSnapshot saved, CancellationToken cancellation)
        => enhancements.PrepareAsync(target, saved, cancellation);
    public async Task PrepareGatewayAsync(ConfigSnapshot saved, CancellationToken cancellation)
    {
        if (saved.Bridge is not null) await BridgeProcess.EnsureAsync(saved.Home, saved.Bridge, cancellation: cancellation);
    }
    public Task<CodexDesktopTarget> RestartAsync(CodexDesktopTarget target, ConfigSnapshot saved, Action validateSaved, int? port,
        Action<CodexDesktopPhase> progress, bool forceConfirmed, CancellationToken cancellation)
        => Task.Run(() => new CodexDesktop().RestartAsync(target, saved.Home, saved.AuthApiBaseUrl, () => dispatch(validateSaved), cancellation,
            port, phase => dispatch(() => progress(phase)), forceConfirmed), cancellation);
    public async Task StopEnhancementsAsync() => await enhancements.DisposeAsync();
    public Task AttachEnhancementsAsync(CodexDesktopTarget target, ConfigSnapshot saved, DesktopSpeedPlan plan, int port, CancellationToken cancellation)
        => enhancements.AttachAsync(target, saved, plan, port, cancellation);
}
