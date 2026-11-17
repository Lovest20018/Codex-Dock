using CodexConfig.Core;

namespace CodexConfig.Application;

public sealed record ConfigurationSourceRequest(string Home, string Executable, string Project, string? Profile = null);
public interface IConfigurationSourceRuntime
{
    Task<ConfigurationReadResult> ReadAsync(ConfigurationSourceRequest request, byte[]? config, CancellationToken cancellation);
}
public sealed class NativeConfigurationSourceRuntime : IConfigurationSourceRuntime
{
    public Task<ConfigurationReadResult> ReadAsync(ConfigurationSourceRequest request, byte[]? config, CancellationToken cancellation) =>
        new ConfigReadClient().ReadAsync(request.Executable, request.Home, request.Project, request.Profile, config, cancellation);
}
public sealed class ConfigurationSourceService(IConfigurationSourceRuntime? runtime = null)
{
    private readonly IConfigurationSourceRuntime runtime = runtime ?? new NativeConfigurationSourceRuntime();
    public async Task<ConfigurationReadResult> ReadAsync(ConfigurationSourceRequest request, Func<ConfigurationSourceRequest> current, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        request = Normalize(request);
        var path = request.Profile is null ? Path.Combine(request.Home, "config.toml") : ProfileStore.FilePath(request.Home, request.Profile);
        var before = ReadFile(path); _ = new TomlDocument(before);
        var result = await runtime.ReadAsync(request, before?.ToArray(), cancellation);
        cancellation.ThrowIfCancellationRequested();
        if (request != Normalize(current()) || !SameContent(before, ReadFile(path)))
            throw new OperationCanceledException("读取条件或比较文件已改变，请重新读取配置来源。");
        return result;
    }
    private static ConfigurationSourceRequest Normalize(ConfigurationSourceRequest value)
    {
        var profile = string.IsNullOrWhiteSpace(value.Profile) ? null : value.Profile.Trim();
        if (profile is not null) ProfileStore.ValidateName(profile);
        if (!Path.IsPathFullyQualified(value.Home) || !Path.IsPathFullyQualified(value.Executable) || !Path.IsPathFullyQualified(value.Project)) throw new ArgumentException("请选择完整的本机执行端、配置目录和项目路径。");
        return new(Path.GetFullPath(value.Home), Path.GetFullPath(value.Executable), Path.GetFullPath(value.Project), profile);
    }
    private static byte[]? ReadFile(string path)
    { if (!File.Exists(path)) return null; if (new FileInfo(path).Length > 8 * 1024 * 1024) throw new IOException("配置文件过大。"); return File.ReadAllBytes(path); }
    internal static bool SameContent(byte[]? first, byte[]? second) => first is null ? second is null : second is not null && first.AsSpan().SequenceEqual(second);
}
