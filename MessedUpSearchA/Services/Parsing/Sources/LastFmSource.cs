using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;

namespace MessedUpSearchA.Services.Parsing.Sources;

/// <summary>
/// Требует бесплатный api_key (last.fm/api). Поля подтверждены живыми ответами API
/// (2026-07-31, через официальный demo-ключ Last.fm из их же документации — сюда он
/// намеренно не зашит, источник работает только с ключом самого пользователя).
/// Last.fm не отдаёт дату релиза трека вообще — фильтр "свежесть" для этой площадки
/// всегда игнорируется, как и у Bandcamp с прослушиваниями.
/// </summary>
public class LastFmSource : IArtistSource
{
    private const string Api = "https://ws.audioscrobbler.com/2.0/";
    private const int PageSize = 50;
    private const int MaxPages = 6;

    // У tag.getTopTracks нет "популярного по умолчанию" режима — нужен хоть какой-то тег.
    // Берём максимально широкий, чтобы не пустовало, когда пользователь ничего не задал.
    private const string FallbackTag = "hip hop";

    private readonly string _apiKey;
    private readonly RateLimiter _limiter = new(180);
    private readonly Func<string, CancellationToken, Task<string>> _fetch;

    public LastFmSource(string apiKey, Func<string, CancellationToken, Task<string>>? fetch = null)
    {
        _apiKey = apiKey ?? string.Empty;
        _fetch = fetch ?? ParsingHttp.GetStringAsync;
    }

    public string Platform => "Last.fm";

    public bool SupportsPlayCountFilter => true;

    public async Task<IReadOnlyList<ArtistCandidate>> SearchArtistsAsync(
        ArtistSearchQuery query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new InvalidOperationException("нет API-ключа, добавь в Settings → PARSER");

        var artists = new List<ArtistCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var searchCap = query.HasPostSearchFilter ? int.MaxValue : query.MaxArtists;

        var tag = string.IsNullOrWhiteSpace(query.GenreTag) ? FallbackTag : query.GenreTag.Trim();

        for (var page = 1; page <= MaxPages && artists.Count < searchCap; page++)
        {
            ct.ThrowIfCancellationRequested();
            await _limiter.WaitAsync(ct);

            var url = $"{Api}?method=tag.gettoptracks&tag={Uri.EscapeDataString(tag)}" +
                      $"&api_key={Uri.EscapeDataString(_apiKey)}&format=json&limit={PageSize}&page={page}";

            using var doc = JsonDocument.Parse(await _fetch(url, ct));

            var tracksObj = doc.RootElement.Obj("tracks");
            if (tracksObj is null || !tracksObj.Value.TryGetProperty("track", out var trackList) ||
                trackList.ValueKind != JsonValueKind.Array)
            {
                break;
            }

            var received = trackList.GetArrayLength();
            if (received == 0)
                break;

            foreach (var track in trackList.EnumerateArray())
            {
                var artistObj = track.Obj("artist");
                if (artistObj is null)
                    continue;

                var name = artistObj.Value.Str("name");
                if (string.IsNullOrWhiteSpace(name) || !seen.Add(name))
                    continue;

                ct.ThrowIfCancellationRequested();
                await _limiter.WaitAsync(ct);

                var profile = await FetchArtistInfoAsync(name, ct);
                artists.Add(MapArtist(name, profile));

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
        if (string.IsNullOrWhiteSpace(artist.Nickname) || string.IsNullOrWhiteSpace(_apiKey))
            return Array.Empty<TrackInfo>();

        await _limiter.WaitAsync(ct);

        var url = $"{Api}?method=artist.gettoptracks&artist={Uri.EscapeDataString(artist.Nickname)}" +
                  $"&api_key={Uri.EscapeDataString(_apiKey)}&format=json&limit={Math.Clamp(limit, 1, 50)}";

        using var doc = JsonDocument.Parse(await _fetch(url, ct));

        var topTracks = doc.RootElement.Obj("toptracks");
        if (topTracks is null || !topTracks.Value.TryGetProperty("track", out var trackList) ||
            trackList.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<TrackInfo>();
        }

        var tracks = new List<TrackInfo>();

        foreach (var t in trackList.EnumerateArray())
        {
            tracks.Add(new TrackInfo
            {
                Title = t.Str("name"),
                Url = t.Str("url"),
                PlayCount = t.Int("playcount"),
                ReleasedAt = string.Empty,
                Tags = string.Empty,
                IsDownloadable = false
            });
        }

        return tracks;
    }

    private async Task<JsonElement?> FetchArtistInfoAsync(string artistName, CancellationToken ct)
    {
        try
        {
            var url = $"{Api}?method=artist.getinfo&artist={Uri.EscapeDataString(artistName)}" +
                      $"&api_key={Uri.EscapeDataString(_apiKey)}&format=json";

            using var doc = JsonDocument.Parse(await _fetch(url, ct));
            return doc.RootElement.Obj("artist")?.Clone();
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

    private ArtistCandidate MapArtist(string name, JsonElement? profile)
    {
        var stats = profile?.Obj("stats");

        return new ArtistCandidate
        {
            Platform = Platform,
            SourceId = name,
            SourceUrl = profile?.Str("url") ?? string.Empty,
            Nickname = name,
            Followers = stats?.Int("listeners") ?? 0,
            TotalPlays = stats?.Int("playcount") ?? 0,
            Tags = ExtractTags(profile)
        };
    }

    private static string ExtractTags(JsonElement? profile)
    {
        if (profile is null || !profile.Value.TryGetProperty("tags", out var tagsObj) ||
            tagsObj.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        if (!tagsObj.TryGetProperty("tag", out var tagArray))
            return string.Empty;

        var list = new List<string>();

        // tag — массив {name,url} обычно, но старый XML->JSON мост Last.fm иногда
        // отдаёт единственный тег не массивом, а голым объектом.
        if (tagArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var tag in tagArray.EnumerateArray())
            {
                var name = tag.Str("name");
                if (!string.IsNullOrWhiteSpace(name))
                    list.Add(name);
            }
        }
        else if (tagArray.ValueKind == JsonValueKind.Object)
        {
            var name = tagArray.Str("name");
            if (!string.IsNullOrWhiteSpace(name))
                list.Add(name);
        }

        return string.Join(", ", list);
    }
}
