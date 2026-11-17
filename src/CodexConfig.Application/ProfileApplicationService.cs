using CodexConfig.Core;

namespace CodexConfig.Application;

public sealed class ProfileSaveReview
{
    internal ProfilePlan? Plan { get; }
    internal ProfileRestorePlan? Restore { get; }
    private int attempted;
    internal bool TryConsume() => Interlocked.CompareExchange(ref attempted, 1, 0) == 0;
    public string Home { get; }
    public string Name { get; }
    public string Target => ProfileStore.FilePath(Home, Name);
    public string? Source => Restore?.Source;
    public bool IsRestore => Restore is not null;
    public bool HasChanges { get; }
    public string Preview => Restore?.Preview ?? Plan!.Preview;
    public IReadOnlyList<ConfigurationChange> Changes { get; }
    public IReadOnlyList<string> Warnings => Plan?.Warnings ?? [];
    internal ProfileSaveReview(ProfileSnapshot before, byte[]? after, ProfilePlan? plan, ProfileRestorePlan? restore)
    {
        Home = before.Home; Name = before.Name; Plan = plan; Restore = restore;
        var original = new TomlDocument(before.Config); var next = new TomlDocument(after);
        HasChanges = !ConfigurationSourceService.SameContent(before.Config, after);
        Changes = Array.AsReadOnly(TomlDocument.ValuePaths(original.Model).Concat(TomlDocument.ValuePaths(next.Model)).Distinct(StringComparer.Ordinal)
            .Where(path => ConfigSchema.Literal(original.Get(path)) != ConfigSchema.Literal(next.Get(path)))
            .Select(path => new ConfigurationChange(ConfigHelp.Name(path), path, ConfigurationCatalogService.Display(path, original.Get(path)), ConfigurationCatalogService.Display(path, next.Get(path)))).ToArray());
    }
}
public sealed record ProfileSaveResult(bool Succeeded, string Message, ProfileSnapshot? Saved = null, string? BackupPath = null);

public sealed class ProfileApplicationService(ProfileStore store, WorkspaceApplyCoordinator operations)
{
    public IReadOnlyList<string> List(string home) => store.List(home);
    public ProfileSnapshot Read(string home, string name) => store.Load(home, name);
    public bool CanRestore(string home, string name) => store.LatestBackup(home, name) is not null;
    public ProfileSaveReview PrepareReview(ProfileSnapshot before, IReadOnlyDictionary<string, string?> edits)
    {
        before = before with { Config = before.Config?.ToArray() };
        var plan = store.Plan(before, new Dictionary<string, string?>(edits, StringComparer.Ordinal));
        return new(before, plan.Config, plan, null);
    }
    public ProfileSaveReview PrepareRestore(ProfileSnapshot before)
    {
        var restore = store.PrepareRestore(before); return new(before, restore.Target, null, restore);
    }
    public ProfileSaveResult Save(ProfileSaveReview review, Func<(string Home, string? Name)> current, Action validate, CancellationToken cancellation = default)
    {
        using var operation = operations.TryBeginProfile();
        if (operation is null) return new(false, "正在处理其他操作，请完成后再保存 Profile。");
        try
        {
            cancellation.ThrowIfCancellationRequested(); validate(); var active = current();
            if (!string.Equals(active.Home, review.Home, StringComparison.OrdinalIgnoreCase) || active.Name != review.Name) throw new OperationCanceledException("Profile 或目录已改变，请重新查看差异。");
            if (!review.TryConsume()) throw new InvalidOperationException("此 Profile 确认已使用，请重新查看差异。");
            if (!review.IsRestore && !ConfigurationSourceService.SameContent(store.Load(review.Home, review.Name).Config, review.Plan!.Before.Config)) throw new IOException("Profile 已被其他程序修改，请重新读取并核对。");
            string? backup = null;
            if (review.IsRestore) backup = store.Restore(review.Restore!);
            else if (review.HasChanges) backup = store.Save(review.Plan!);
            var saved = store.Load(review.Home, review.Name);
            var desired = review.IsRestore ? review.Restore!.Target : review.Plan!.Config;
            if (!ConfigurationSourceService.SameContent(saved.Config, desired)) throw new IOException("Profile 保存后再次改变，请重新读取核对。");
            var message = "Profile 已" + (review.IsRestore ? "恢复" : "保存") + "并核对，主配置与当前聊天未切换。";
            operation.WaitForUser(message); return new(true, message, saved, backup);
        }
        catch (OperationCanceledException) { operation.WaitForUser("Profile 操作已取消，草稿已保留。"); return new(false, "Profile 操作已取消，草稿已保留。"); }
        catch (Exception error) { operation.Fail(error.Message); return new(false, error.Message); }
    }
}
