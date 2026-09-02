using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MessedUpSearchA.Services.Parsing.Sources;

/// <summary>
/// Требует бесплатный client_id (devportal.jamendo.com). Поля musicinfo/stats и точный
/// URL артиста подтверждены только частично документацией (2026-07-31, без живого ключа
/// проверить не удалось) — разбор написан защитно: если вложенность окажется другой,
/// TryGetProperty просто вернёт пусто/0, а не упадёт.
/// </summary>
public class JamendoSource : IArtistSource
{
    private const string Api = "https://api.jamendo.com/v3.0";
    private const int PageSize = 20;
    private const int MaxPages = 6;

    private readonly string _clientId;
    private readonly RateLimiter _limiter = new(60);
    private readonly Func<string, CancellationToken, Task<string>> _fetch;

    public JamendoSource(string clientId, Func<string, CancellationToken, Task<string>>? fetch = null)
    {
        _clientId = clientId ?? string.Empty;
        _fetch = fetch ?? ParsingHttp.GetStringAsync;
    }

    public string Platform => "Jamendo";

    public bool SupportsPlayCountFilter => true;

    public async Task<IReadOnlyList<ArtistCandidate>> SearchArtistsAsync(
        ArtistSearchQuery query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_clientId))
            throw new InvalidOperationException("нет client_id, добавь в Settings → PARSER");

        var artists = new List<ArtistCandidate>();
        var seenArtistIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var searchCap = query.HasPostSearchFilter ? int.MaxValue : query.MaxArtists;

        for (var page = 0; page < MaxPages && artists.Count < searchCap; page++)
        {
            ct.ThrowIfCancellationRequested();
            await _limiter.WaitAsync(ct);

            using var doc = JsonDocument.Parse(await _fetch(BuildTrackSearchUrl(query, page * PageSize), ct));

            if (!doc.RootElement.TryGetProperty("results", out var results) ||
                results.ValueKind != JsonValueKind.Array)
            {
                break;
            }

            var received = results.GetArrayLength();
            if (received == 0)
                break;

            foreach (var track in results.EnumerateArray())
            {
                var artistId = track.Str("artist_id");
                var artistName = track.Str("artist_name");

                if (string.IsNullOrWhiteSpace(artistId) || !seenArtistIds.Add(artistId))
                    continue;

                ct.ThrowIfCancellationRequested();
                await _limiter.WaitAsync(ct);

                var profile = await FetchArtistProfileAsync(artistId, ct);
                if (profile is null)
                    continue; // не получили стабильную ссылку на артиста — не гадаем URL, пропускаем

                artists.Add(MapArtist(profile.Value, artistId, artistName, track));

                if (artists.Count >= searchCap)
                    break;
            }

            if (received < PageSize)
                break;
        }

        return artists;
    }

    public async Task<IReadOnlyList<TrackInfo>> GetTracksAsync(
        ArtistCandidate artist, int limit, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(artist.SourceId) || string.IsNullOrWhiteSpace(_clientId))
            return Array.Empty<TrackInfo>();

        await _limiter.WaitAsync(ct);

        var url = $"{Api}/artists/tracks/?client_id={Uri.EscapeDataString(_clientId)}&format=json" +
                  $"&id={Uri.EscapeDataString(artist.SourceId)}&include=musicinfo+stats";

        using var doc = JsonDocument.Parse(await _fetch(url, ct));

        if (!doc.RootElement.TryGetProperty("results", out var results) ||
            results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0)
        {
            return Array.Empty<TrackInfo>();
        }

        var artistEntry = results[0];
        if (!artistEntry.TryGetProperty("tracks", out var trackList) || trackList.ValueKind != JsonValueKind.Array)
            return Array.Empty<TrackInfo>();

        var tracks = new List<TrackInfo>();

        foreach (var t in trackList.EnumerateArray())
        {
            if (tracks.Count >= limit)
                break;

            tracks.Add(new TrackInfo
            {
                Title = t.Str("name"),
                Url = t.Str("shareurl"),
                PlayCount = ExtractListens(t),
                ReleasedAt = NormalizeDate(t.Str("releasedate")),
                Tags = ExtractGenres(t),
                IsDownloadable = !string.IsNullOrWhiteSpace(t.Str("audiodownload"))
            });
        }

        return tracks;
    }

    private async Task<JsonElement?> FetchArtistProfileAsync(string artistId, CancellationToken ct)
    {
        try
        {
            var url = $"{Api}/artists/?client_id={Uri.EscapeDataString(_clientId)}&format=json" +
                      $"&id={Uri.EscapeDataString(artistId)}";

            using var doc = JsonDocument.Parse(await _fetch(url, ct));

            if (!doc.RootElement.TryGetProperty("results", out var results) ||
                results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0)
            {
                return null;
            }

            return results[0].Clone();
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

    private ArtistCandidate MapArtist(JsonElement profile, string artistId, string fallbackName, JsonElement track)
    {
        var url = profile.Str("shorturl");
        if (string.IsNullOrWhiteSpace(url))
            url = profile.Str("shareurl");

        var name = profile.Str("name");

        return new ArtistCandidate
        {
            Platform = Platform,
            SourceId = artistId,
            SourceUrl = url,
            Nickname = string.IsNullOrWhiteSpace(name) ? fallbackName : name,
            AvatarUrl = profile.Str("image"),
            Website = profile.Str("website"),
            LastTrackDate = NormalizeDate(track.Str("releasedate")),
            Tags = ExtractGenres(track)
        };
    }

    private string BuildTrackSearchUrl(ArtistSearchQuery query, int offset)
    {
        var url = $"{Api}/tracks/?client_id={Uri.EscapeDataString(_clientId)}&format=json" +
                  $"&include=musicinfo+stats&limit={PageSize}&offset={offset}";

        return string.IsNullOrWhiteSpace(query.GenreTag)
            ? url + "&order=popularity_total"
            : url + $"&tags={Uri.EscapeDataString(query.GenreTag.Trim().ToLowerInvariant())}";
    }

    private static int ExtractListens(JsonElement track) =>
        track.TryGetProperty("stats", out var stats) && stats.ValueKind == JsonValueKind.Object
            ? stats.Int("listens")
            : 0;

    private static string ExtractGenres(JsonElement track)
    {
        if (!track.TryGetProperty("musicinfo", out var musicinfo) || musicinfo.ValueKind != JsonValueKind.Object)
            return string.Empty;

        if (!musicinfo.TryGetProperty("tags", out var tagsObj) || tagsObj.ValueKind != JsonValueKind.Object)
            return string.Empty;

        if (!tagsObj.TryGetProperty("genres", out var genres) || genres.ValueKind != JsonValueKind.Array)
            return string.Empty;

        var list = new List<string>();
        foreach (var g in genres.EnumerateArray())
        {
            if (g.ValueKind != JsonValueKind.String)
                continue;

            var value = g.GetString();
            if (!string.IsNullOrWhiteSpace(value))
                list.Add(value);
        }

        return string.Join(", ", list);
    }

    private static string NormalizeDate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        return DateTime.TryParse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : string.Empty;
    }
}
