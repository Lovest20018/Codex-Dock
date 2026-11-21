using CodexConfig.Core;

namespace CodexConfig.WinUI;

internal sealed record WinUiLaunchOptions(string Home, bool EnhancedLaunch = false, bool StartInTray = false, bool VerifyStartup = false)
{
    internal string? IsolatedDataDirectory
    {
        get
        {
#if CODEX_DOCK_WINUI_SMOKE
            if (SmokeOutput is not null) return SmokeOutput;
#endif
            return VerifyStartup ? Home : null;
        }
    }

#if CODEX_DOCK_WINUI_SMOKE
    internal string? SmokeOutput { get; init; }
    internal bool InteractivePreview { get; init; }
#endif

    internal static WinUiLaunchOptions Parse(string[] arguments)
    {
#if CODEX_DOCK_UI_PREVIEW
        if (arguments.Length == 0)
            return CreateIsolated(Path.Combine(Path.GetTempPath(), "CodexDock-UI-Preview", Guid.NewGuid().ToString("N")), interactivePreview: true);
#endif
#if CODEX_DOCK_WINUI_SMOKE
        if (arguments is ["--smoke-test", var output]) return CreateIsolated(Path.GetFullPath(output));
#endif
        if (arguments is ["--verify-startup", var directory] && Path.IsPathFullyQualified(directory) && Directory.Exists(directory))
            return new(Path.GetFullPath(directory), StartInTray: true, VerifyStartup: true);

        string? selectedHome = null;
        var enhanced = false; var tray = false;
        for (var index = 0; index < arguments.Length; index++)
        {
            var argument = arguments[index];
            if (argument == "--enhanced-launch" && !enhanced) { enhanced = true; continue; }
            if (argument == "--start-in-tray" && !tray) { tray = true; continue; }
            if (argument != "--home" || index + 1 >= arguments.Length)
                throw new ArgumentException("支持 --home <配置目录>、--enhanced-launch 和 --start-in-tray。");
            var value = Path.GetFullPath(arguments[++index]);
            if (argument == "--home" && selectedHome is null) selectedHome = value;
            else throw new ArgumentException("启动参数重复。");
        }
        return new(selectedHome ?? ConfigStore.DefaultHome, enhanced, tray);
    }

#if CODEX_DOCK_WINUI_SMOKE
    private static WinUiLaunchOptions CreateIsolated(string output, bool interactivePreview = false)
    {
        var home = Path.Combine(output, interactivePreview ? "界面预览" : "workspace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(home);
        File.WriteAllText(Path.Combine(home, "config.toml"), "# WinUI isolated fixture\nmodel='preview-model'\nopenai_base_url='https://preview.invalid/v1'\n");
        File.WriteAllText(Path.Combine(home, "auth.json"), "{\"auth_mode\":\"apikey\",\"OPENAI_API_KEY\":\"fake-winui-test-key\"}");
        return new(home) { SmokeOutput = output, InteractivePreview = interactivePreview };
    }
#endif
}
