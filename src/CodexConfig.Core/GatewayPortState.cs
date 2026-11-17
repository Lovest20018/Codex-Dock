using System.Net;
using System.Net.Sockets;

namespace CodexConfig.Core;

public static class GatewayPortState
{
    // A released local endpoint can be rebound exclusively. Failure to bind is
    // inconclusive: it may be another listener or an OS reservation/permission.
    public static bool IsReleased(int port)
    {
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        using var listener = new TcpListener(IPAddress.Loopback, port) { ExclusiveAddressUse = true };
        try { listener.Start(); return true; }
        catch (SocketException) { return false; }
    }
}
