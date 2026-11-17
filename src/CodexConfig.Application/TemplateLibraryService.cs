using CodexConfig.Core;

namespace CodexConfig.Application;

// Library writes are separate from Codex configuration saves. The repository
// checks the observed library revision while holding its own file lock.
public sealed class TemplateLibraryService(TemplateRepository repository)
{
    private TemplateLibrarySnapshot? snapshot;
    public TemplateLibrarySnapshot Read() => snapshot = repository.LoadSnapshot();
    public ConfigTemplate Save(ConfigTemplate template, bool create = false)
    {
        var saved = repository.Save(template, create, RequireSnapshot().Revision); Read(); return saved;
    }
    public void Delete(string id) { repository.Delete(id, RequireSnapshot().Revision); Read(); }
    public void RestoreBuiltIns() { repository.RestoreBuiltIns(RequireSnapshot().Revision); Read(); }
    public static IReadOnlyList<TemplateSetting> Capture(ConnectionDraftViewModel draft)
    {
        if (draft.Snapshot is null) throw new InvalidOperationException("请先读取有效配置。");
        if (draft.FieldErrors.Count > 0) throw new InvalidOperationException("请先修正全部配置中的输入，再生成方案。");
        return TemplateRepository.Capture(new TomlDocument(draft.Snapshot.Config).Model, draft.Edits);
    }
    private TemplateLibrarySnapshot RequireSnapshot() => snapshot ?? throw new InvalidOperationException("请先读取方案库。");
}
