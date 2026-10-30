using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace CodexConfig.Core;

public sealed record ModelCatalogResult(IReadOnlyList<string> Models, long Milliseconds);

public sealed class ModelCatalogClient : IDisposable
{
    private readonly HttpClient client;
    public ModelCatalogClient(HttpMessageHandler? handler = null)
    {
        client = handler is null ? new(new HttpClientHandler { AllowAutoRedirect = false }) : new(handler);
        client.Timeout = TimeSpan.FromSeconds(15);
    }

    // Fetch the chosen model endpoint only. This does not probe identity, fall
    // back to another route or supply evidence that changes authentication.
    public async Task<ModelCatalogResult> ReadModelsAsync(ConnectionSettings settings, CancellationToken cancellation = default)
    {
        settings = (settings with { Model = string.IsNullOrWhiteSpace(settings.Model) ? "model-catalog" : settings.Model }).Normalize();
        var clock = Stopwatch.StartNew();
        var answer = await GetAsync(settings, settings.BaseUrl + "/models", cancellation)
            ?? throw new InvalidDataException("未收到模型目录。");
        if (!answer.Ok) throw new HttpRequestException("获取模型失败：" + answer.Message);
        var models = new List<string>();
        if (answer.Json.ValueKind != JsonValueKind.Object ||
            !(answer.Json.TryGetProperty("models", out var entries) || answer.Json.TryGetProperty("data", out entries)) ||
            entries.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("模型接口未返回 models/data 数组，请检查服务地址。");
        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;
            foreach (var name in new[] { "slug", "id", "model" })
                if (entry.TryGetProperty(name, out var id) && id.ValueKind == JsonValueKind.String && id.GetString() is { Length: > 0 } model)
                { models.Add(model); break; }
        }
        return new(models.Distinct(StringComparer.Ordinal).ToArray(), clock.ElapsedMilliseconds);
    }

    private sealed record Endpoint(bool Ok, int Status, bool Intercepted, JsonElement Json, string Message);
    private async Task<Endpoint?> GetAsync(ConnectionSettings settings, string url, CancellationToken cancellation)
    {
        try { return await RequestAsync(settings, url, cancellation); }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { return new(false, 0, false, default, "连接超时。"); }
        catch (HttpRequestException) { return new(false, 0, false, default, "无法建立连接，请检查地址、证书与网络。"); }
    }

    private async Task<Endpoint?> RequestAsync(ConnectionSettings settings, string url, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        if (!response.IsSuccessStatusCode)
        {
            var message = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "API Key 无效、已过期或没有访问权限。",
                HttpStatusCode.NotFound => "接口不存在（HTTP 404）。",
                HttpStatusCode.TooManyRequests => "服务端限制了请求频率，请稍后重试。",
                _ => $"服务端返回 HTTP {(int)response.StatusCode}，请检查服务状态。"
            };
            return new(false, (int)response.StatusCode, response.Headers.Contains("l-block"), default, message);
        }
        if (response.Content.Headers.ContentLength > 2 * 1024 * 1024) throw new InvalidDataException("模型目录响应过大。");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellation);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(chunk, cancellation)) > 0)
        {
            if (buffer.Length + count > 2 * 1024 * 1024) throw new InvalidDataException("兼容接口响应过大。");
            await buffer.WriteAsync(chunk.AsMemory(0, count), cancellation);
        }
        try
        {
            using var parsed = JsonDocument.Parse(buffer.ToArray());
            var blocked = response.Headers.Contains("l-block");
            return new(!blocked, (int)response.StatusCode, blocked, parsed.RootElement.Clone(), blocked ? "线路边缘返回 l-block 拦截标记。" : "");
        }
        catch (JsonException) { return new(false, (int)response.StatusCode, true, default, "线路返回空内容或非 JSON，可能被边缘拦截。"); }
    }

    public void Dispose()
    {
        client.Dispose();
    }
}
