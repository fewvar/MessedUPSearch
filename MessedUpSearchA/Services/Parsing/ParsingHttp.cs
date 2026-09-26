using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
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

    public static async Task<string> PostJsonAsync(string url, string jsonBody, CancellationToken ct)
    {
        using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        using var response = await Client.PostAsync(url, content, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    public static async Task<string> GetStringWithBearerAsync(string url, string bearerToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        using var response = await Client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    public static async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
    {
        var body = await GetStringAsync(url, ct);
        return JsonDocument.Parse(body);
    }

    /// <summary>
    /// Дописывает ответ в поток, но не больше maxBytes: всё, что длиннее, бросает
    /// исключение. Защита от часовых миксов — декодированный час звука занимает
    /// сотни мегабайт памяти.
    /// </summary>
    public static async Task<long> CopyToAsync(
        string url, Stream destination, long maxBytes, CancellationToken ct)
    {
        using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength > maxBytes)
            throw new InvalidDataException($"файл больше {maxBytes / (1024 * 1024)} МБ");

        await using var source = await response.Content.ReadAsStreamAsync(ct);

        var buffer = new byte[81920];
        long total = 0;
        int read;

        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > maxBytes)
                throw new InvalidDataException($"файл больше {maxBytes / (1024 * 1024)} МБ");

            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
        }

        return total;
    }

    /// <summary>
    /// Кусок файла через Range: length байт начиная с доли offsetFraction от размера.
    /// Размер узнаём из Content-Range первого же ответа. Сервер Range не поддерживает
    /// (ответил 200) — берём просто первые length байт, дальше не тянем.
    /// </summary>
    public static async Task<long> CopyRangeAsync(
        string url, Stream destination, double offsetFraction, long length, CancellationToken ct)
    {
        long start = 0;

        // Узнать размер: просим один байт, в ответе «bytes 0-0/12345».
        using (var probe = new HttpRequestMessage(HttpMethod.Get, url))
        {
            probe.Headers.Range = new RangeHeaderValue(0, 0);
            using var response = await Client.SendAsync(probe, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentRange?.Length;
            if (total is > 0 && total > length * 2)
                start = (long)(total.Value * offsetFraction);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new RangeHeaderValue(start, start + length - 1);

        using var body = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        body.EnsureSuccessStatusCode();

        await using var source = await body.Content.ReadAsStreamAsync(ct);

        var buffer = new byte[81920];
        long copied = 0;

        while (copied < length)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, length - copied)), ct);
            if (read == 0)
                break;

            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
            copied += read;
        }

        return copied;
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

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            return number;

        // Last.fm отдаёт числовые поля (listeners, playcount) строками — подстраховка.
        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed))
            return parsed;

        return 0;
    }

    public static bool Bool(this JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    public static JsonElement? Obj(this JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : null;

    /// <summary>
    /// Число как строка без потери точности — некоторые ID у Bandcamp не влезают в int32.
    /// </summary>
    public static string NumberAsString(this JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
            return string.Empty;

        return value.ValueKind == JsonValueKind.Number ? value.GetRawText() : string.Empty;
    }
}
