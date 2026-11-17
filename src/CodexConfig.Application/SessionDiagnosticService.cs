using CodexConfig.Core;

namespace CodexConfig.Application;

public interface ISessionDiagnosticRuntime
{
    Task<IReadOnlyList<SessionDiagnosticFile>> ListAsync(string home, CancellationToken cancellation);
    Task<SessionObservation> ReadAsync(string home, string path, CancellationToken cancellation);
}
public sealed class NativeSessionDiagnosticRuntime : ISessionDiagnosticRuntime
{
    public Task<IReadOnlyList<SessionDiagnosticFile>> ListAsync(string home, CancellationToken cancellation)
        => Task.Run(() => SessionDiagnostics.List(home, cancellation), cancellation);
    public Task<SessionObservation> ReadAsync(string home, string path, CancellationToken cancellation)
        => Task.Run(() => SessionDiagnostics.Read(home, path, cancellation), cancellation);
}
public sealed class SessionDiagnosticListing
{
    public string Home { get; }
    public IReadOnlyList<SessionDiagnosticFile> Files { get; }
    internal SessionDiagnosticListing(string home, IReadOnlyList<SessionDiagnosticFile> files)
    { Home = home; Files = Array.AsReadOnly(files.ToArray()); }
}
public sealed class SessionDiagnosticService(ISessionDiagnosticRuntime? runtime = null)
{
    private readonly ISessionDiagnosticRuntime runtime = runtime ?? new NativeSessionDiagnosticRuntime();
    public async Task<SessionDiagnosticListing> ListAsync(string home, Func<string> currentHome, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested(); home = Normalize(home);
        var files = await runtime.ListAsync(home, cancellation);
        cancellation.ThrowIfCancellationRequested();
        if (!Same(home, Normalize(currentHome()))) throw new OperationCanceledException("配置目录已改变，请重新刷新。");
        return new(home, files.Where(file => InHome(home, file)).ToArray());
    }
    public async Task<SessionObservation> ReadAsync(SessionDiagnosticListing listing, SessionDiagnosticFile file,
        Func<(string Home, string? Path)> current, CancellationToken cancellation = default)
    {
        void Validate()
        {
            cancellation.ThrowIfCancellationRequested(); var value = current();
            if (!Same(listing.Home, Normalize(value.Home)) || value.Path is null || !Same(file.Path, value.Path))
                throw new OperationCanceledException("目录或所选聊天已改变，请重新读取。");
        }
        Validate();
        if (!listing.Files.Contains(file) || !InHome(listing.Home, file)) throw new InvalidDataException("请先刷新并选择当前目录中的聊天记录。");
        var result = await runtime.ReadAsync(listing.Home, file.Path, cancellation); Validate(); return result;
    }
    private static string Normalize(string home) => Path.IsPathFullyQualified(home) ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(home)) : throw new ArgumentException("请选择完整配置目录。");
    private static bool Same(string first, string second) => string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
    private static bool InHome(string home, SessionDiagnosticFile file)
    {
        var root = Path.GetFullPath(Path.Combine(home, "sessions")) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(file.Path).StartsWith(root, StringComparison.OrdinalIgnoreCase) && file.Name == Path.GetFileName(file.Path) &&
            file.Name.StartsWith("rollout-", StringComparison.Ordinal) && file.Name.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase);
    }
    public static string Describe(SessionObservation observed) =>
        "记录时间：" + (observed.ObservedAt?.LocalDateTime.ToString("g") ?? "未提供") +
        "\n主模型：" + (observed.Model ?? "未观察到") + "\n推理强度：" + (observed.Reasoning ?? "未观察到") +
        "\n速度档位：" + (observed.ServiceTier ?? "未观察到") + "\n审批方式：" + (observed.Reviewer ?? "未观察到") +
        "\n自动审核结果：" + (observed.ReviewStatus ?? "未观察到") + "\n实际审核模型：本记录未提供，不从配置值推断" +
        (observed.ContainsConfigurationUpdate ? "\n已读取会话中的推理档位更新；顶层请求值可能保留初始基线。" : "") + "\n\n来源：" + observed.File;
}
