using Windows.ApplicationModel.DataTransfer;
using System.Runtime.InteropServices;

namespace CodexConfig.WinUI;

internal static class NativeWorkspaceClipboard
{
    internal static uint Copy(string text)
    {
        var package = new DataPackage(); package.SetText(text);
        if (!Clipboard.SetContentWithOptions(package, new ClipboardContentOptions { IsAllowedInHistory = false, IsRoamable = false }))
            throw new IOException("剪贴板暂不可用，请重试。");
        return GetClipboardSequenceNumber();
    }
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
}
