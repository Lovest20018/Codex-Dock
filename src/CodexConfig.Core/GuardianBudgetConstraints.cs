using Tomlyn.Model;

namespace CodexConfig.Core;

// Ported validation rules from openai/codex c2d5399, guardian-context/src/budget.rs.
// These constants describe that pinned runtime, not a model's usable context window.
internal static class GuardianBudgetConstraints
{
    private const string Root = "features.guardianv2.sync_elastic_sections";
    private static readonly string[] Sections = ["transcript_messages", "transcript_tools", "root_conversation_messages", "retained_user_instructions", "trusted_user_answers", "sender_user_messages", "planned_action", "previous_reviews", "trusted_tools", "trusted_skills", "transcript_images", "node_repl_evidence", "permission_context"];
    private static string Canonical(string name) => name switch
    {
        "root_messages" => "root_conversation_messages", "retained_instructions" => "retained_user_instructions",
        "user_answers" => "trusted_user_answers", "sender_messages" => "sender_user_messages",
        "tool_descriptions" or "planned_action_tool_descriptions" => "planned_action",
        "images" => "transcript_images", "repl_evidence" => "node_repl_evidence", _ => name
    };

    internal static IEnumerable<ConfigurationIssue> Inspect(TomlDocument document)
    {
        if (document.Get("features.guardianv2.sync_elastic_budgeting") is not true) return [];
        var errors = new List<ConfigurationIssue>();
        void Error(string path, string message) => errors.Add(new(path, message));
        if (document.Get(Root) is not TomlTable { Count: > 0 } sections)
            return [new(Root, "开启同步 Guardian 弹性预算时，需要非空的分区配额表。")];
        var minimums = Sections.ToDictionary(section => section, section => section switch { "transcript_messages" => 5000m, "transcript_tools" => 1000m, _ => 900m }, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, value) in sections)
        {
            var path = TomlDocument.FormatPath(["features", "guardianv2", "sync_elastic_sections", name]);
            var canonical = Canonical(name);
            if (!minimums.ContainsKey(canonical)) { Error(path, "当前核查内核不支持此 Guardian 分区名。"); continue; }
            if (!seen.Add(canonical)) { Error(path, "此分区通过名称或别名重复指定。"); continue; }
            if (value is not TomlTable table) { Error(path, "分区配额需要子表。"); continue; }
            if (table.Keys.Any(key => key is not ("min_total_section_tokens" or "max_section_entry_tokens" or "max_total_section_tokens")))
                Error(path, "分区配额包含当前核查内核不支持的字段。");
            if (table.GetValueOrDefault("min_total_section_tokens") is not long minimum || minimum <= 0 ||
                table.GetValueOrDefault("max_section_entry_tokens") is not long entry || entry <= 0)
            { Error(path, "分区需要正整数的 min_total_section_tokens 和 max_section_entry_tokens。"); continue; }
            minimums[canonical] = minimum;
            if (entry > minimum || entry > 9000)
                Error(path + ".max_section_entry_tokens", "单条上限不能超过分区最低配额或 9000 token。");
            if (table.TryGetValue("max_total_section_tokens", out var maximum) && (maximum is not long cap || cap < minimum))
                Error(path + ".max_total_section_tokens", "分区总上限需要整数，且不能低于最低配额。");
        }
        if (errors.Count == 0 && minimums.Values.Sum() is var total && total != 128000)
            Error(Root, $"计入未列出分区的默认配额后，最低配额总和必须为 128000 token；当前为 {total}。");
        return errors;
    }
}
