using CodexConfig.Core;

namespace CodexConfig.WinUI;

internal sealed record WinUiLaunchOptions(string Home, string? SmokeOutput, bool EnhancedLaunch = false, bool StartInTray = false, bool InteractivePreview = false)
{
    internal static WinUiLaunchOptions Parse(string[] arguments)
    {
#if CODEX_DOCK_UI_PREVIEW
        if (arguments.Length == 0)
            return CreateIsolated(Path.Combine(Path.GetTempPath(), "CodexDock-UI-Preview", Guid.NewGuid().ToString("N")), interactivePreview: true);
#endif
        string? selectedHome = null, smoke = null;
        var enhanced = false; var tray = false;
        for (var index = 0; index < arguments.Length; index++)
        {
            var argument = arguments[index];
            if (argument == "--enhanced-launch" && !enhanced) { enhanced = true; continue; }
            if (argument == "--start-in-tray" && !tray) { tray = true; continue; }
            if (argument is not ("--home" or "--smoke-test") || index + 1 >= arguments.Length)
                throw new ArgumentException("支持 --home <配置目录>、--enhanced-launch、--start-in-tray 或 --smoke-test <隔离测试目录>。");
            var value = Path.GetFullPath(arguments[++index]);
            if (argument == "--home" && selectedHome is null) selectedHome = value;
            else if (argument == "--smoke-test" && smoke is null) smoke = value;
            else throw new ArgumentException("启动参数重复。");
        }
        if (smoke is not null)
        {
            if (selectedHome is not null || enhanced || tray) throw new ArgumentException("隔离测试不接受外部配置目录或启动操作。");
            return CreateIsolated(smoke);
        }
        return new(selectedHome ?? ConfigStore.DefaultHome, null, enhanced, tray);
    }

    private static WinUiLaunchOptions CreateIsolated(string output, bool interactivePreview = false)
    {
        var home = Path.Combine(output, interactivePreview ? "界面预览" : "workspace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(home);
        File.WriteAllText(Path.Combine(home, "config.toml"), "# WinUI isolated fixture\nmodel='preview-model'\nopenai_base_url='https://preview.invalid/v1'\n");
        File.WriteAllText(Path.Combine(home, "auth.json"), "{\"auth_mode\":\"apikey\",\"OPENAI_API_KEY\":\"fake-winui-test-key\"}");
        return new(home, output, InteractivePreview: interactivePreview);
    }
}
