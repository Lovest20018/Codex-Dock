using CodexConfig.Core;

namespace CodexConfig.Application;

public interface IExperimentalFeatureRuntime
{ Task<ExperimentalFeatureSnapshot> ReadAsync(ConfigurationSourceRequest request, CancellationToken cancellation); }
public sealed class NativeExperimentalFeatureRuntime : IExperimentalFeatureRuntime
{
    public Task<ExperimentalFeatureSnapshot> ReadAsync(ConfigurationSourceRequest request, CancellationToken cancellation) =>
        new ExperimentalFeatureClient().ReadAsync(request.Executable, request.Home, request.Project, request.Profile, cancellation);
}
public sealed record ExperimentalFeatureRead(ConfigurationSourceRequest Request, ExperimentalFeatureSnapshot Snapshot, byte[]? MainConfig, byte[]? ProfileConfig);

public sealed class ExperimentalFeatureService(IExperimentalFeatureRuntime? runtime = null)
{
    private readonly IExperimentalFeatureRuntime runtime = runtime ?? new NativeExperimentalFeatureRuntime();
    public async Task<ExperimentalFeatureRead> ReadAsync(ConfigurationSourceRequest request, Func<ConfigurationSourceRequest> current, CancellationToken cancellation = default)
    {
        request = Normalize(request); var main = Read(Path.Combine(request.Home, "config.toml"));
        var profile = request.Profile is null ? null : Read(ProfileStore.FilePath(request.Home, request.Profile));
        var result = await runtime.ReadAsync(request, cancellation); cancellation.ThrowIfCancellationRequested();
        if (request != Normalize(current()) || !Same(main, Read(Path.Combine(request.Home, "config.toml"))) ||
            request.Profile is not null && !Same(profile, Read(ProfileStore.FilePath(request.Home, request.Profile))))
            throw new OperationCanceledException("执行端、目录或已保存配置发生变化，请重新读取实验功能。");
        return new(request, result with { Features = Array.AsReadOnly(result.Features.ToArray()) }, main?.ToArray(), profile?.ToArray());
    }
    public static void Stage(ExperimentalFeatureRead read, string name, bool? enabled, ConnectionDraftViewModel draft, ConfigurationSourceRequest current)
    {
        if (read.Request != Normalize(current) || read.Request.Profile is not null || draft.Snapshot is not { } saved ||
            !string.Equals(saved.Home, read.Request.Home, StringComparison.OrdinalIgnoreCase) || !Same(read.MainConfig, saved.Config) ||
            !Same(read.MainConfig, Read(Path.Combine(read.Request.Home, "config.toml"))))
            throw new InvalidOperationException("实验功能读取条件或主配置已改变；请使用未选 Profile 的当前目录重新读取。");
        var feature = read.Snapshot.Features.SingleOrDefault(feature => feature.Name == name);
        if (feature?.CanStage != true || !ExperimentalFeatureClient.ValidName(name)) throw new ArgumentException("此功能的支持阶段未确认或已移除，未加入草稿。");
        if (draft.Document().Get(feature.Path) is { } existing && existing is not bool)
            throw new InvalidOperationException("此功能已有非布尔配置，请在配置草稿中检查，未覆盖现有结构。");
        draft.SetOverride(feature.Path, enabled is null ? null : enabled.Value ? "true" : "false");
    }
    private static ConfigurationSourceRequest Normalize(ConfigurationSourceRequest request)
    {
        if (!Path.IsPathFullyQualified(request.Home) || !Path.IsPathFullyQualified(request.Executable) || !Path.IsPathFullyQualified(request.Project)) throw new ArgumentException("请先明确选择本机执行端、配置目录和项目。");
        var profile = string.IsNullOrWhiteSpace(request.Profile) ? null : request.Profile.Trim(); if (profile is not null) ProfileStore.ValidateName(profile);
        return new(Path.GetFullPath(request.Home), Path.GetFullPath(request.Executable), Path.GetFullPath(request.Project), profile);
    }
    private static byte[]? Read(string path)
    { if (!File.Exists(path)) return null; if (new FileInfo(path).Length > 8 * 1024 * 1024) throw new IOException("配置文件超过读取上限。"); return File.ReadAllBytes(path); }
    private static bool Same(byte[]? first, byte[]? second) => first is null ? second is null : second is not null && first.AsSpan().SequenceEqual(second);
}
