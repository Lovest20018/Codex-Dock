using CodexConfig.Core;

namespace CodexConfig.Application;

public sealed record StartupRegistrationState(string Home, bool Enabled, bool HasOtherRegistration, string? ObservedCommand);
public sealed class StartupRegistrationReview
{
    internal string? Previous { get; }
    internal string? Next { get; }
    private int attempted;
    internal bool TryConsume() => Interlocked.CompareExchange(ref attempted, 1, 0) == 0;
    public string Home { get; }
    public bool Enabled => Next is not null;
    public bool ReplacesOther { get; }
    internal StartupRegistrationReview(string home, string? previous, string? next, bool replacesOther)
    { Home = home; Previous = previous; Next = next; ReplacesOther = replacesOther; }
}

// This preference is independent of model drafts. Only an observed registration
// may be replaced, and disabling never removes another checkout's registration.
public sealed class StartupRegistrationService(IStartupRegistration registration, string executable)
{
    private readonly object gate = new();
    public StartupRegistrationState Read(string home)
    {
        home = Path.GetFullPath(home); var command = registration.Read();
        var enabled = StartupCommand.Targets(command, executable, home);
        return new(home, enabled, command is not null && !enabled, command);
    }
    public StartupRegistrationReview Prepare(StartupRegistrationState observed, bool enabled)
    {
        if (!enabled && observed.HasOtherRegistration) throw new InvalidOperationException("启动项属于另一程序或目录，未删除。可先核对，再开启当前目录的启动项。");
        return new(observed.Home, observed.ObservedCommand, enabled ? StartupCommand.Create(executable, observed.Home) : null, observed.HasOtherRegistration);
    }
    public StartupRegistrationState Apply(StartupRegistrationReview review, Func<string> activeHome, Func<bool> isCurrent)
    {
        lock (gate)
        {
            if (!isCurrent() || !string.Equals(review.Home, Path.GetFullPath(activeHome()), StringComparison.OrdinalIgnoreCase)) throw new OperationCanceledException("工作目录或当前操作已改变，未修改开机启动。");
            if (!review.TryConsume()) throw new InvalidOperationException("此启动项确认已使用，请重新读取。");
            if (registration.Read() != review.Previous) throw new IOException("开机启动项已被其他窗口修改，请重新读取后核对。");
            if (review.Next != review.Previous) registration.Write(review.Next);
            var result = Read(review.Home);
            if (result.Enabled != review.Enabled || result.ObservedCommand != review.Next) throw new IOException("开机启动项在保存后再次改变，请重新读取核对。");
            return result;
        }
    }
}
