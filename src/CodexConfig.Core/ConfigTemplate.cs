using Tomlyn.Model;

namespace CodexConfig.Core;

public sealed record TemplateSetting(string Path, string Value, bool SelectByDefault = true);
public sealed record TemplateDifference(TemplateSetting Setting, string? FileValue, string? CurrentValue, bool HasDraft)
{
    public bool Same => ConfigTemplate.Equivalent(CurrentValue, Setting.Value);
    public bool SelectedByDefault => Setting.SelectByDefault && !Same && !HasDraft;
    public string State => Same ? "值相同 · 无需修改" : HasDraft ? "已有草稿 · 默认保留" : FileValue is null ? "新增配置" : "替换现值";
}

public sealed record TemplateUndoResult(int Restored, int Preserved);
public sealed class TemplateApplication
{
    internal sealed record Edit(string Path, bool HadDraft, string? Previous, bool HasStagedDraft, string? Staged);
    private readonly IReadOnlyList<Edit> edits;
    internal TemplateApplication(IReadOnlyList<Edit> edits) => this.edits = edits;
    public int Count => edits.Count;
    private bool undone;

    public TemplateUndoResult Undo(Dictionary<string, string?> draft)
    {
        if (undone) return new(0, 0);
        var restored = 0; var preserved = 0;
        foreach (var edit in edits)
        {
            // A later edit or per-field undo must not be replaced by this undo.
            var hasCurrent = draft.TryGetValue(edit.Path, out var current);
            if (hasCurrent != edit.HasStagedDraft || hasCurrent && current != edit.Staged) { preserved++; continue; }
            if (edit.HadDraft) draft[edit.Path] = edit.Previous;
            else draft.Remove(edit.Path);
            restored++;
        }
        undone = true;
        return new(restored, preserved);
    }
}

public sealed record ConfigTemplate(string Id, string Name, string SourceName, string SourceUrl, string Description, IReadOnlyList<TemplateSetting> Settings)
{
    public IReadOnlyList<TemplateDifference> BuildDiff(TomlTable original, IReadOnlyDictionary<string, string?> draft) => Settings.Select(setting =>
    {
        var fileValue = TomlDocument.ValueAt(original, setting.Path) is { } value ? ConfigSchema.Literal(value) : null;
        var hasDraft = draft.TryGetValue(setting.Path, out var pending);
        return new TemplateDifference(setting, fileValue, hasDraft ? pending : fileValue, hasDraft);
    }).ToArray();

    public TemplateApplication StageSelected(TomlTable original, Dictionary<string, string?> draft, IEnumerable<string> selectedPaths)
    {
        var paths = selectedPaths.ToHashSet(StringComparer.Ordinal);
        if (paths.Any(path => !Settings.Any(setting => setting.Path == path))) throw new ArgumentException("所选项目不属于此模板。");
        var selected = BuildDiff(original, draft).Where(diff => paths.Contains(diff.Setting.Path) && !diff.Same).ToArray();
        foreach (var diff in selected) ConfigSchema.Instance.Validate(diff.Setting.Path, diff.Setting.Value, allowPreferences: true);
        var edits = new List<TemplateApplication.Edit>();
        foreach (var diff in selected)
        {
            var hadDraft = draft.TryGetValue(diff.Setting.Path, out var previous);
            var needsDraft = !Equivalent(diff.FileValue, diff.Setting.Value);
            edits.Add(new(diff.Setting.Path, hadDraft, previous, needsDraft, needsDraft ? diff.Setting.Value : null));
            if (needsDraft) draft[diff.Setting.Path] = diff.Setting.Value;
            else draft.Remove(diff.Setting.Path);
        }
        return new(edits);
    }

    internal static bool Equivalent(string? first, string? second)
    {
        if (first is null || second is null) return first == second;
        try { return ConfigSchema.Literal(TomlDocument.ParseModel("value = " + first)["value"]) == ConfigSchema.Literal(TomlDocument.ParseModel("value = " + second)["value"]); }
        catch (Exception) { return first == second; }
    }
}

public static class BuiltInTemplates
{
    // Preserve provenance of the historical community recipe; no upstream or backend request.
    public static ConfigTemplate KrillCommunity { get; } = new(
        "krill-community", "Krill 社区方案", "Krill / Superba",
        "https://github.com/krill-lab/superba/blob/f78d77a2849aceb8dffc1f27a83869ffceab7e90/crates/superba-core/src/codex.rs",
        "7 项社区设置，含 3 项旧实验功能，按需选择。",
        Array.AsReadOnly(new[] {
            new TemplateSetting("web_search", "\"live\""),
            new TemplateSetting("features.fast_mode", "true"),
            new TemplateSetting("analytics.enabled", "false"),
            new TemplateSetting("features.context_management", "true", false),
            new TemplateSetting("features.token_budget.enabled", "true", false),
            new TemplateSetting("features.token_budget.use_history_notes_extension", "true", false),
            new TemplateSetting("suppress_unstable_features_warning", "true", false)
        }));
    public static IReadOnlyList<ConfigTemplate> All { get; } = Array.AsReadOnly(new[] { KrillCommunity });
}
