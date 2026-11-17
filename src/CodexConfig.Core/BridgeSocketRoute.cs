using System.Text.Json;

namespace CodexConfig.Core;

// Protect a stream already in flight, but never start another response against a
// route that was replaced. Unknown protocol messages remain busy conservatively.
internal sealed class BridgeSocketRoute(string fingerprint, Func<string> current)
{
    private int active;
    private int changed;
    private bool retired;
    private readonly object gate = new();
    internal bool IsIdle { get { lock (gate) return active == 0; } }
    internal bool Changed
    {
        get
        {
            if (Volatile.Read(ref changed) == 1) return true;
            try { if (current() == fingerprint) return false; }
            catch { } // Deleted, unreadable or revoked settings cannot authorize a new response.
            Volatile.Write(ref changed, 1); return true;
        }
    }
    internal bool BeginMessage()
    {
        lock (gate)
        {
            if (retired) return false;
            if (active == 0 && Changed) { retired = true; return false; }
            active++; return true;
        }
    }
    internal bool TryRetire() { lock (gate) { if (active != 0 || !Changed) return false; retired = true; return true; } }
    internal void ClientMessage(byte[] bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.TryGetProperty("type", out var type) && type.GetString() != "response.create") Complete();
        }
        catch (JsonException) { }
    }
    internal void ServerMessage(byte[] bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.TryGetProperty("type", out var type) && type.GetString() is "response.completed" or "response.failed" or "response.incomplete" or "error") Complete();
        }
        catch (JsonException) { }
    }
    private void Complete()
    {
        lock (gate) { if (active > 0) active--; }
    }
    internal static string Fingerprint(MixedConnection connection) => ManagedModelCatalogStore.Hash(System.Text.Encoding.UTF8.GetBytes(
        connection.BaseUrl + "\n" + connection.Kind + "\n" + connection.ApiKey + "\n" + connection.UseSystemProxy + "\n" + connection.HttpOrigin));
}
