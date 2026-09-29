using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MessedUpSearchA.Services.Parsing.Sources;

/// <summary>
/// Deezer как запасной источник звука: только 30-секундные превью, зато без ключей
/// и почти для любого артиста, который хоть раз выходил на стримингах.
///
/// Трек ищется не через /search?q=artist:"..." — на «yeat» он отдаёт чужие треки
/// со словом в названии. Сначала ищем артиста, потом берём его топ.
/// </summary>
public class DeezerSource : IAudioResolver
{
    private const string Api = "https://api.deezer.com";

    private readonly RateLimiter _limiter = new(40);
    private readonly Func<string, CancellationToken, Task<string>> _fetch;

    public DeezerSource(Func<string, CancellationToken, Task<string>>? fetch = null)
        => _fetch = fetch ?? ParsingHttp.GetStringAsync;

    public string Platform => "Deezer";

    /// <summary>
    /// Топ-треки артиста с превью. Однофамильцев полно (два «Yeat»: 228 тысяч фанатов
    /// и 6), поэтому берём только точное совпадение имени и из них — самого известного.
    /// </summary>
    public async Task<IReadOnlyList<TrackInfo>> FindArtistTracksAsync(
        string nickname, int limit, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(nickname))
            return [];

        using var search = await GetJsonAsync(
            $"{Api}/search/artist?q={Uri.EscapeDataString(nickname.Trim())}&limit=10", ct);

        var wanted = Normalize(nickname);
        long? artistId = null;
        var bestFans = -1;

        foreach (var artist in Data(search))
        {
            if (Normalize(artist.Str("name")) != wanted)
                continue;

            var fans = artist.Int("nb_fan");
            if (fans <= bestFans)
                continue;

            bestFans = fans;
            artistId = artist.TryGetProperty("id", out var id) && id.TryGetInt64(out var value) ? value : null;
        }

        if (artistId is null)
            return [];

        using var top = await GetJsonAsync($"{Api}/artist/{artistId}/top?limit={Math.Clamp(limit, 1, 50)}", ct);

        var tracks = new List<TrackInfo>();

        foreach (var track in Data(top))
        {
            if (string.IsNullOrWhiteSpace(track.Str("preview")))
                continue;

            tracks.Add(new TrackInfo
            {
                Title = track.Str("title"),
                Url = track.Str("link"),
                SourceId = track.NumberAsString("id"),
                // Прослушиваний Deezer не отдаёт; rank — его оценка популярности, для сортировки годится.
                PlayCount = track.Int("rank")
            });
        }

        return tracks;
    }

    public async Task<ResolvedAudio?> ResolveAsync(string trackId, string trackUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(trackId))
            return null;

        // Ссылка на превью подписана и живёт недолго — берём свежую прямо перед скачиванием.
        using var doc = await GetJsonAsync($"{Api}/track/{trackId}", ct);

        var preview = doc.RootElement.Str("preview");
        return string.IsNullOrWhiteSpace(preview)
            ? null
            : new ResolvedAudio { Urls = [preview], AudioKind = AudioKinds.Preview };
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
    {
        await _limiter.WaitAsync(ct);
        var doc = JsonDocument.Parse(await _fetch(url, ct));

        // Deezer отвечает 200 даже на ошибки и кладёт их в тело.
        if (doc.RootElement.TryGetProperty("error", out var error))
        {
            doc.Dispose();
            throw new InvalidOperationException("Deezer: " + error.Str("message"));
        }

        return doc;
    }

    private static IEnumerable<JsonElement> Data(JsonDocument doc) =>
        doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
            ? data.EnumerateArray()
            : [];

    private static string Normalize(string raw) =>
        new string((raw ?? string.Empty).ToLower(CultureInfo.InvariantCulture).Where(char.IsLetterOrDigit).ToArray());
}
