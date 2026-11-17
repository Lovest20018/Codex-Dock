using CodexConfig.Core;

namespace CodexConfig.Application;

// One chosen endpoint only. Reads never write credentials, select a model or
// change authentication mode; shells retain the user's current model draft.
public sealed class ModelDiscoveryService
{
    private readonly Func<ConnectionSettings, CancellationToken, Task<ModelCatalogResult>> read;
    public ModelDiscoveryService(ModelCatalogClient client) => read = client.ReadModelsAsync;
    public ModelDiscoveryService(Func<ConnectionSettings, CancellationToken, Task<ModelCatalogResult>> read) => this.read = read;
    public async Task<ModelCatalogResult> ReadAsync(string home, ConnectionSettings settings,
        Func<(string Home, ConnectionSettings Settings)> current, CancellationToken cancellation = default)
    {
        settings = (settings with { Model = "model-catalog" }).Normalize();
        var fingerprint = settings.Fingerprint();
        var result = await read(settings, cancellation);
        cancellation.ThrowIfCancellationRequested();
        var latest = current();
        string? currentFingerprint;
        try { currentFingerprint = (latest.Settings with { Model = "model-catalog" }).Fingerprint(); }
        catch (ArgumentException) { currentFingerprint = null; }
        if (!string.Equals(home, latest.Home, StringComparison.OrdinalIgnoreCase) || fingerprint != currentFingerprint)
            throw new OperationCanceledException("连接设置已更改，请重新获取模型。");
        return result with { Models = result.Models.Where(value => !string.IsNullOrWhiteSpace(value) && value.Length <= 512 &&
            !value.Any(char.IsControl)).Distinct(StringComparer.Ordinal).ToArray() };
    }
}
