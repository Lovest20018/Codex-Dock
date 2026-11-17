using CodexConfig.Core;

namespace CodexConfig.Application;

public sealed class MobileSetupDraftViewModel : WorkspaceObservableObject
{
    private string url = "", username = "codex", port = MobileGatewayManager.DefaultPort.ToString(), password = "";
    public MobileConnectionSettings? Settings { get; private set; }
    public long Revision { get; private set; }
    public string PublicUrl { get => url; set { if (Set(ref url, value)) NotifyDraft(); } }
    public string Username { get => username; set { if (Set(ref username, value)) NotifyDraft(); } }
    public string Port { get => port; set { if (Set(ref port, value)) NotifyDraft(); } }
    public string Password { get => password; set { if (Set(ref password, value)) NotifyDraft(); } }
    public bool HasDraft => password.Length > 0 || url != (Settings?.PublicUrl ?? "") || username != (Settings?.Username ?? "codex") || port != (Settings?.Port ?? MobileGatewayManager.DefaultPort).ToString();
    public void Load(MobileConnectionSettings? saved)
    {
        Settings = saved; url = saved?.PublicUrl ?? ""; username = saved?.Username ?? "codex"; port = (saved?.Port ?? MobileGatewayManager.DefaultPort).ToString(); password = "";
        foreach (var property in new[] { nameof(PublicUrl), nameof(Username), nameof(Port), nameof(Password), nameof(Settings) }) Changed(property);
        NotifyDraft();
    }
    public MobileSetupInput Input()
    {
        if (!int.TryParse(Port, out var value)) throw new ArgumentException("请输入有效的本机监听端口。");
        return new(PublicUrl, Username, Password, value);
    }
    private void NotifyDraft() { Revision++; Changed(nameof(HasDraft)); Changed(nameof(Revision)); }
}
