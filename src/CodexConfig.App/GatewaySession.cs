using CodexConfig.Core;

namespace CodexConfig.App;

// Serialize startup and shutdown so exiting cannot leave a late-starting child behind.
internal sealed class GatewaySession
{
    private readonly SemaphoreSlim gate = new(1);
    private readonly Dictionary<string, byte[]> connections = new(StringComparer.Ordinal);
    private bool exiting;
    public async Task ResumeAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try { exiting = false; } finally { gate.Release(); }
    }

    public async Task<BridgeStatus> EnsureAsync(string home, byte[] bytes)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (exiting) throw new InvalidOperationException("程序正在退出。");
            var connection = MixedConnection.Read(bytes);
            connections[connection.LocalToken] = bytes.ToArray();
            return await BridgeProcess.EnsureAsync(home, bytes).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    public async Task StopAsync(byte[] bytes)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (await BridgeProcess.StatusAsync(MixedConnection.Read(bytes)).ConfigureAwait(false) is not null)
                await BridgeProcess.StopAsync(bytes).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    public async Task StopAllAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            exiting = true;
            var failures = new List<Exception>();
            foreach (var bytes in connections.Values)
            {
                try
                {
                    if (await BridgeProcess.StatusAsync(MixedConnection.Read(bytes)).ConfigureAwait(false) is not null)
                        await BridgeProcess.StopAsync(bytes).ConfigureAwait(false);
                }
                catch (Exception error) { failures.Add(error); }
            }
            if (failures.Count > 0)
            {
                exiting = false;
                throw new InvalidOperationException("网关未能停止，请重试退出或先点击停止网关。", new AggregateException(failures));
            }
            connections.Clear();
        }
        finally { gate.Release(); }
    }
}
