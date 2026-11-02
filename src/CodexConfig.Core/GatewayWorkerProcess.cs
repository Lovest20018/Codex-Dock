using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace CodexConfig.Core;

// Capture a process handle while the authenticated control listener still exists.
// Waiting on this handle cannot target a subsequently reused PID. Never kills processes.
internal static class GatewayWorkerProcess
{
    internal static Process? Capture(int port)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var processId = ListenerPid(port);
        if (processId == Environment.ProcessId) return null; // In-process regression servers have no separate worker.
        if (processId is null) throw new IOException("无法确认网关后台进程，请刷新状态后重试。");
        Process? process = null;
        try
        {
            process = Process.GetProcessById(processId.Value);
            _ = process.Handle;
            if (!process.HasExited && ListenerPid(port) == processId) return process;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        process?.Dispose(); throw new IOException("无法确认网关后台进程，请刷新状态后重试。");
    }

    internal static async Task WaitStoppedAsync(Process? process, Func<bool> released, string message, CancellationToken cancellation)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(15))
        {
            cancellation.ThrowIfCancellationRequested();
            if (released() && (process is null || process.HasExited)) return;
            await Task.Delay(100, cancellation).ConfigureAwait(false);
        }
        throw new IOException(message);
    }

    internal static bool PortFree(int port)
    {
        using var listener = new TcpListener(IPAddress.Loopback, port) { ExclusiveAddressUse = true };
        try { listener.Start(); return true; }
        catch (SocketException) { return false; }
    }

    private static int? ListenerPid(int port)
    {
        var size = 0;
        _ = GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2, 3, 0);
        for (var attempt = 0; attempt < 3 && size is > 0 and <= 4 * 1024 * 1024; attempt++)
        {
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                var result = GetExtendedTcpTable(buffer, ref size, false, 2, 3, 0);
                if (result == 122) continue;
                if (result != 0) return null;
                var count = Marshal.ReadInt32(buffer);
                if (count < 0 || count > (size - 4) / 24) return null;
                for (var row = 0; row < count; row++)
                {
                    var offset = 4 + row * 24;
                    var networkPort = Marshal.ReadInt32(buffer, offset + 8);
                    var localPort = ((networkPort & 255) << 8) | ((networkPort >> 8) & 255);
                    if (localPort == port && Marshal.ReadInt32(buffer, offset + 4) == 0x0100007f)
                        return Marshal.ReadInt32(buffer, offset + 20);
                }
                return null;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        return null;
    }

    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int family, int tableClass, uint reserved);
}
