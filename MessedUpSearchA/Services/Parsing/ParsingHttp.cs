using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MessedUpSearchA.Services.Parsing;

public static class ParsingHttp
{
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");
        return client;
    }

    public static async Task<string> GetStringAsync(string url, CancellationToken ct)
    {
        using var response = await Client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    public static async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
    {
        var body = await GetStringAsync(url, ct);
        return JsonDocument.Parse(body);
    }

    public static async Task<byte[]> GetBytesAsync(string url, CancellationToken ct)
    {
        using var response = await Client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(ct);
    }
}

public class RateLimiter
{
    private readonly TimeSpan _minInterval;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Random _jitter = new();
    private DateTime _last = DateTime.MinValue;

    public RateLimiter(int requestsPerMinute) =>
        _minInterval = TimeSpan.FromMilliseconds(60_000.0 / Math.Max(1, requestsPerMinute));

    public async Task WaitAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var elapsed = DateTime.UtcNow - _last;
            var wait = _minInterval - elapsed;
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait + TimeSpan.FromMilliseconds(_jitter.Next(0, 250)), ct);

            _last = DateTime.UtcNow;
        }
        finally
        {
            _gate.Release();
        }
    }
}

public static class JsonExt
{
    public static string Str(this JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
            return string.Empty;

        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
    }

    public static int Int(this JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
            return 0;

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : 0;
    }

    public static bool Bool(this JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    public static JsonElement? Obj(this JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : null;
}
