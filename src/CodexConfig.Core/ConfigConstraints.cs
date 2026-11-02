using Tomlyn.Model;

namespace CodexConfig.Core;

public sealed record ConfigurationIssue(string Path, string Message, bool IsError = true);

// Validate only affected groups. Existing unrelated/unknown settings are preserved.
public static class ConfigConstraints
{
    public static IReadOnlyList<ConfigurationIssue> InspectChanges(TomlDocument before, TomlDocument after, ModelCapabilities? capabilities = null)
    {
        var changed = TomlDocument.ValuePaths(before.Model).Concat(TomlDocument.ValuePaths(after.Model)).Distinct(StringComparer.Ordinal)
            .Where(path => ConfigSchema.Literal(before.Get(path)) != ConfigSchema.Literal(after.Get(path)))
            .Select(TomlDocument.ParsePath).ToArray();
        var issues = new List<ConfigurationIssue>();
        bool Touched(params string[] roots) => roots.Any(root =>
        {
            var parts = TomlDocument.ParsePath(root);
            return changed.Any(path => path.Take(Math.Min(parts.Length, path.Length)).SequenceEqual(parts.Take(Math.Min(parts.Length, path.Length)), StringComparer.Ordinal));
        });
        void Error(string path, string message) => issues.Add(new(path, message));
        void Warn(string path, string message) => issues.Add(new(path, message, false));
        if (Touched("default_permissions", "sandbox_mode", "sandbox_workspace_write") && after.String("default_permissions") is { Length: > 0 } &&
            (after.Get("sandbox_mode") is not null || after.Get("sandbox_workspace_write") is not null))
            Error("default_permissions", "权限 profile 与 sandbox_mode / sandbox_workspace_write 不能同时设置，请选择一种权限配置方式。");

        if (after.Get("model_providers") is TomlTable providers)
            foreach (var (name, value) in providers)
            {
                if (value is not TomlTable provider) continue;
                var path = TomlDocument.FormatPath(["model_providers", name]);
                if (!Touched(path)) continue;
                if (name is "openai" or "ollama" or "lmstudio" or "amazon-bedrock")
                {
                    if (before.Get(path) is null) Error(path, "此名称是客户端内置供应商，不能用自定义表覆盖；请选择其他名称。");
                    else Warn(path, "此表使用内置供应商名称，请核对当前客户端是否采用其中的字段。");
                }
                if (provider.GetValueOrDefault("auth") is TomlTable &&
                    (provider.GetValueOrDefault("env_key") is string { Length: > 0 } || provider.GetValueOrDefault("experimental_bearer_token") is string { Length: > 0 } || provider.GetValueOrDefault("requires_openai_auth") is true))
                    Error(path + ".auth", "命令认证不能与环境变量、直接 Bearer Token 或官方账号认证同时配置。");
                if (provider.GetValueOrDefault("auth") is TomlTable auth && auth.GetValueOrDefault("command") is not string { Length: > 0 })
                    Error(path + ".auth.command", "命令认证需要非空 command；保存不会执行该命令。");
                if (provider.GetValueOrDefault("env_key") is string { Length: > 0 } && provider.GetValueOrDefault("experimental_bearer_token") is string { Length: > 0 })
                    Warn(path, "同时配置了环境变量与直接 Token，请确认客户端采用的认证来源。");
            }

        if (after.Get("mcp_servers") is TomlTable servers)
            foreach (var (name, value) in servers)
            {
                if (value is not TomlTable server) continue;
                var path = TomlDocument.FormatPath(["mcp_servers", name]);
                if (!Touched(path)) continue;
                var command = server.GetValueOrDefault("command") as string;
                var url = server.GetValueOrDefault("url") as string;
                if (!string.IsNullOrWhiteSpace(command) && !string.IsNullOrWhiteSpace(url))
                    Error(path, "MCP 服务请在命令与 HTTP URL 之间选择一种传输方式。");
                if (server.GetValueOrDefault("enabled") is not false && string.IsNullOrWhiteSpace(command) && string.IsNullOrWhiteSpace(url))
                    Error(path, "启用的 MCP 服务需要 command 或 URL。");
                if (!string.IsNullOrWhiteSpace(url) && (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || string.IsNullOrEmpty(uri.Host)))
                    Error(path + ".url", "MCP HTTP 地址需要完整的 http:// 或 https:// URL。");
                if (string.IsNullOrWhiteSpace(url) && (server.ContainsKey("http_headers") || server.ContainsKey("bearer_token_env_var") || server.ContainsKey("env_http_headers")))
                    Error(path, "MCP HTTP 认证和请求头需要 HTTP URL，不能用于命令传输。");
                foreach (var key in new[] { "startup_timeout_sec", "startup_timeout_ms", "tool_timeout_sec" })
                    if (server.TryGetValue(key, out var timeout) && (timeout is not (long or double) || !double.IsFinite(Convert.ToDouble(timeout)) || Convert.ToDouble(timeout) <= 0))
                        Error(path + "." + key, "超时必须为正数；startup_timeout_ms 使用毫秒，其他超时使用秒。");
                if (server.ContainsKey("startup_timeout_sec") && server.ContainsKey("startup_timeout_ms"))
                    Error(path, "启动超时请只填写秒或旧版毫秒中的一项。");
            }

        if (Touched("features.fast_mode", "features.ultrafast_mode", "service_tier"))
        {
            var tier = after.String("service_tier");
            if (tier is "fast" or "priority" && after.Get("features.fast_mode") is false)
                Error("service_tier", "选择 Fast 档位时不能同时关闭 features.fast_mode。");
            if (tier == "ultrafast" && after.Get("features.ultrafast_mode") is false)
                Error("service_tier", "选择 Ultrafast 档位时不能同时关闭对应功能。");
            if (tier is "fast" or "priority" or "ultrafast" && (capabilities is null || !capabilities.SupportsTier(tier)))
                Warn("service_tier", "当前模型目录未确认此服务档位；保存不代表上游支持或实际采用。");
        }
        if (Touched("model", "model_reasoning_effort") && capabilities is { ReasoningLevels.Length: > 0 } &&
            after.String("model_reasoning_effort") is { Length: > 0 } effort && !capabilities.ReasoningLevels.Contains(effort, StringComparer.Ordinal))
            Error("model_reasoning_effort", "推理强度不在当前模型目录支持的档位中，请重新选择或取消覆盖。");

        if (Touched("approval_policy") && after.Get("approval_policy") is TomlTable policy &&
            (policy.Count != 1 || policy.GetValueOrDefault("granular") is not TomlTable))
            Error("approval_policy", "细分审批策略需要 { granular = { ... } } 结构。");
        else if (Touched("approval_policy") && after.Get("approval_policy.granular") is TomlTable granular)
            foreach (var required in ConfigSchema.Instance.Find("approval_policy.granular")!.RequiredProperties)
                if (granular.GetValueOrDefault(required) is not bool) Error("approval_policy.granular." + required, "细分审批策略的必填项需要明确选择开启或关闭。");
        return issues;
    }

    public static string[] ValidateChanges(TomlDocument before, TomlDocument after, ModelCapabilities? capabilities = null)
    {
        var issues = InspectChanges(before, after, capabilities);
        var errors = issues.Where(issue => issue.IsError).ToArray();
        if (errors.Length > 0) throw new ArgumentException(string.Join("\n", errors.Select(issue => issue.Path + "：" + issue.Message)));
        return issues.Select(issue => issue.Message).Distinct().ToArray();
    }
}
