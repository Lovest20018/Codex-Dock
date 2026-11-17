using System.Security.Principal;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;

namespace CodexConfig.Application;

// Restore the tray instance on a manual launch; a second logon launch stays quiet.
public sealed class DesktopInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly EventWaitHandle show;
    private readonly RegisteredWaitHandle? callback;
    private readonly string pipeName;
    private readonly CancellationTokenSource stopping = new();
    private readonly Task? requests;
    public bool IsPrimary { get; }
    public DesktopInstance(Action restore, bool quiet, string? scope = null, Action<string>? enhancedLaunch = null)
    {
        var name = @"Local\CodexDock." + (scope ?? WindowsIdentity.GetCurrent().User!.Value);
        pipeName = "CodexDock.Launch." + (scope ?? WindowsIdentity.GetCurrent().User!.Value);
        show = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".show");
        mutex = new Mutex(false, name, out var created);
        IsPrimary = created;
        if (IsPrimary) callback = ThreadPool.RegisterWaitForSingleObject(show, (_, _) => restore(), null, Timeout.Infinite, false);
        else if (!quiet) show.Set();
        if (IsPrimary && enhancedLaunch is not null) requests = ListenAsync(enhancedLaunch);
    }
    public async Task SendEnhancedLaunchAsync(string home)
    {
        if (!Path.IsPathFullyQualified(home) || home.Length > 4096) throw new ArgumentException("快捷方式配置目录无效。");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(timeout.Token);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { home = Path.GetFullPath(home) });
        await client.WriteAsync(BitConverter.GetBytes(bytes.Length), timeout.Token); await client.WriteAsync(bytes, timeout.Token);
    }
    private async Task ListenAsync(Action<string> launch)
    {
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(stopping.Token);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token); deadline.CancelAfter(TimeSpan.FromSeconds(2));
                var header = new byte[4]; await server.ReadExactlyAsync(header, deadline.Token); var length = BitConverter.ToInt32(header);
                if (length is < 2 or > 16384) continue;
                var bytes = new byte[length]; await server.ReadExactlyAsync(bytes, deadline.Token);
                using var document = JsonDocument.Parse(bytes);
                if (document.RootElement.GetProperty("home").GetString() is { Length: > 0 and <= 4096 } home && Path.IsPathFullyQualified(home)) launch(Path.GetFullPath(home));
            }
            catch (Exception error) when (error is IOException or OperationCanceledException or JsonException or ArgumentException or KeyNotFoundException or InvalidOperationException)
            { if (stopping.IsCancellationRequested) return; }
        }
    }
    public void Dispose() { stopping.Cancel(); callback?.Unregister(null); show.Dispose(); mutex.Dispose(); }
}
