using System.Security.Principal;

namespace CodexConfig.App;

// Restore the tray instance on a manual launch; a second logon launch stays quiet.
internal sealed class DesktopInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly EventWaitHandle show;
    private readonly RegisteredWaitHandle? callback;
    public bool IsPrimary { get; }
    public DesktopInstance(Action restore, bool quiet, string? scope = null)
    {
        var name = @"Local\CodexDock." + (scope ?? WindowsIdentity.GetCurrent().User!.Value);
        show = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".show");
        mutex = new Mutex(false, name, out var created);
        IsPrimary = created;
        if (IsPrimary) callback = ThreadPool.RegisterWaitForSingleObject(show, (_, _) => restore(), null, Timeout.Infinite, false);
        else if (!quiet) show.Set();
    }
    public void Dispose() { callback?.Unregister(null); show.Dispose(); mutex.Dispose(); }
}
