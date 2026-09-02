using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MessedUpSearchA.Services.Parsing;

public class GeniusArtistInfo
{
    public string InstagramHandle { get; init; } = string.Empty;
    public string TwitterHandle { get; init; } = string.Empty;
    public string FacebookHandle { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}

/// <summary>
/// Требует бесплатный Client Access Token (genius.com/api-clients). Проверено живым
/// запросом (2026-08-01, реальный токен пользователя). /search ищет песни, а не
/// артистов — топовый хит может быть чужим треком, где нужный артист просто зафичерен,
/// поэтому матчим по точному совпадению имени, а не берём первый хит.
/// </summary>
public class GeniusLookup
{
    private const string Api = "https://api.genius.com";

    private readonly string _accessToken;
    private readonly RateLimiter _limiter = new(60);
    private readonly Func<string, string, CancellationToken, Task<string>> _fetch;

    public GeniusLookup(
        string accessToken,
        Func<string, string, CancellationToken, Task<string>>? fetch = null)
    {
        _accessToken = accessToken ?? string.Empty;
        _fetch = fetch ?? ParsingHttp.GetStringWithBearerAsync;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_accessToken);

    public async Task<GeniusArtistInfo?> LookupAsync(string nickname, CancellationToken ct)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(nickname))
            return null;

        try
        {
            await _limiter.WaitAsync(ct);

            var searchUrl = $"{Api}/search?q={Uri.EscapeDataString(nickname)}";
            using var searchDoc = JsonDocument.Parse(await _fetch(searchUrl, _accessToken, ct));

            var response = searchDoc.RootElement.Obj("response");
            if (response is null || !response.Value.TryGetProperty("hits", out var hits) ||
                hits.ValueKind != JsonValueKind.Array || hits.GetArrayLength() == 0)
            {
                return null;
            }

            // /search ищет ПЕСНИ, не артистов — топовый хит может быть чужим треком,
            // где наш артист просто зафичерен (primary_artist тогда будет не тот).
            // Поэтому берём только хит, где primary_artist.name реально совпадает с ником.
            var artistId = FindMatchingArtistId(hits, nickname);
            if (string.IsNullOrWhiteSpace(artistId))
                return null;

            await _limiter.WaitAsync(ct);

            var artistUrl = $"{Api}/artists/{artistId}";
            using var artistDoc = JsonDocument.Parse(await _fetch(artistUrl, _accessToken, ct));

            var artist = artistDoc.RootElement.Obj("response")?.Obj("artist");
            if (artist is null)
                return null;

            return new GeniusArtistInfo
            {
                InstagramHandle = artist.Value.Str("instagram_name"),
                TwitterHandle = artist.Value.Str("twitter_name"),
                FacebookHandle = artist.Value.Str("facebook_name"),
                Description = ExtractDescription(artist.Value)
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static string FindMatchingArtistId(JsonElement hits, string nickname)
    {
        foreach (var hit in hits.EnumerateArray())
        {
            var primaryArtist = hit.Obj("result")?.Obj("primary_artist");
            if (primaryArtist is null)
                continue;

            var name = primaryArtist.Value.Str("name");
            if (string.Equals(name, nickname, StringComparison.OrdinalIgnoreCase))
                return primaryArtist.Value.NumberAsString("id");
        }

        return string.Empty;
    }

    /// <summary>
    /// artist.description — не готовый текст, а DOM-дерево ({tag, children: [...]}),
    /// где children вперемешку — строки и вложенные теги. Плоского "plain"-поля нет,
    /// вытаскиваем текст рекурсивным обходом строковых листьев.
    /// </summary>
    private static string ExtractDescription(JsonElement artist)
    {
        var description = artist.Obj("description");
        if (description is null || !description.Value.TryGetProperty("dom", out var dom))
            return string.Empty;

        var sb = new StringBuilder();
        AppendDomText(dom, sb);
        return sb.ToString().Trim();
    }

    private static void AppendDomText(JsonElement node, StringBuilder sb)
    {
        if (node.ValueKind == JsonValueKind.String)
        {
            sb.Append(node.GetString()).Append(' ');
            return;
        }

        if (node.ValueKind != JsonValueKind.Object)
            return;

        if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
                AppendDomText(child, sb);
        }
    }
}
