using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MessedUpSearchA.Services.Parsing.Sources;

/// <summary>
/// Неофициальный discover_web (снят с боевого запроса и JS-бандла бэндкампа, 2026-07-31 —
/// не задокументирован официально). Профиля/соцсетей артиста через API нет — только то,
/// что отдаёт discover прямо на релизе (band_name/band_location/band_image). Список треков —
/// best-effort скрейпинг data-tralbum со страницы конкретного альбома, который дал discover.
/// </summary>
public class BandcampSource : IArtistSource
{
    private const string Api = "https://bandcamp.com/api/discover/1/discover_web";
    private const int PageSize = 20;
    private const int MaxPages = 6;

    private static readonly Regex TralbumPattern =
        new(@"data-tralbum=""([^""]*)""", RegexOptions.Compiled);

    private readonly RateLimiter _limiter = new(40);
    private readonly Func<string, string, CancellationToken, Task<string>> _postJson;
    private readonly Func<string, CancellationToken, Task<string>> _fetchHtml;

    private readonly Dictionary<string, string> _albumUrlByArtist =
        new(StringComparer.OrdinalIgnoreCase);

    public BandcampSource(
        Func<string, string, CancellationToken, Task<string>>? postJson = null,
        Func<string, CancellationToken, Task<string>>? fetchHtml = null)
    {
        _postJson = postJson ?? ParsingHttp.PostJsonAsync;
        _fetchHtml = fetchHtml ?? ParsingHttp.GetStringAsync;
    }

    public string Platform => "Bandcamp";

    // Bandcamp прослушивания не отдаёт нигде — фильтр по плеям для этой площадки не работает.
    public bool SupportsPlayCountFilter => false;

    public async Task<IReadOnlyList<ArtistCandidate>> SearchArtistsAsync(
        ArtistSearchQuery query, CancellationToken ct)
    {
        var artists = new List<ArtistCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var searchCap = query.HasPostSearchFilter ? int.MaxValue : query.MaxArtists;

        var tag = NormalizeTag(query.GenreTag);
        string? cursor = null;

        for (var page = 0; page < MaxPages && artists.Count < searchCap; page++)
        {
            ct.ThrowIfCancellationRequested();
            await _limiter.WaitAsync(ct);

            var body = JsonSerializer.Serialize(new DiscoverRequest
            {
                TagNormNames = tag.Length == 0 ? Array.Empty<string>() : new[] { tag },
                Cursor = cursor,
                Size = PageSize
            });

            using var doc = JsonDocument.Parse(await _postJson(Api, body, ct));
            var root = doc.RootElement;

            if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                break;

            var received = results.GetArrayLength();
            if (received == 0)
                break;

            foreach (var item in results.EnumerateArray())
            {
                var bandUrl = CleanUrl(item.Str("band_url"));
                if (string.IsNullOrWhiteSpace(bandUrl) || !seen.Add(bandUrl))
                    continue;

                artists.Add(MapArtist(item, bandUrl));

                var albumUrl = CleanUrl(item.Str("item_url"));
                if (!string.IsNullOrWhiteSpace(albumUrl))
                    _albumUrlByArtist[bandUrl] = albumUrl;

                if (artists.Count >= searchCap)
                    break;
            }

            cursor = root.Str("cursor");
            if (string.IsNullOrWhiteSpace(cursor) || received < PageSize)
                break;
        }

        return artists;
    }

    public async Task<IReadOnlyList<TrackInfo>> GetTracksAsync(
        ArtistCandidate artist, int limit, CancellationToken ct)
    {
        if (!_albumUrlByArtist.TryGetValue(artist.SourceUrl, out var albumUrl) ||
            string.IsNullOrWhiteSpace(albumUrl))
        {
            return Array.Empty<TrackInfo>();
        }

        await _limiter.WaitAsync(ct);

        string html;
        try
        {
            html = await _fetchHtml(albumUrl, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Best-effort: страница не отдалась/поменяла структуру — не роняем прогон.
            return Array.Empty<TrackInfo>();
        }

        var match = TralbumPattern.Match(html);
        if (!match.Success)
            return Array.Empty<TrackInfo>();

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(WebUtility.HtmlDecode(match.Groups[1].Value));
        }
        catch
        {
            return Array.Empty<TrackInfo>();
        }

        using (doc)
        {
            var root = doc.RootElement;

            if (!root.TryGetProperty("trackinfo", out var trackinfo) ||
                trackinfo.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<TrackInfo>();
            }

            var albumDate = NormalizeBandcampDate(root.Str("album_release_date"));
            var baseUrl = artist.SourceUrl.TrimEnd('/');
            var tracks = new List<TrackInfo>();

            foreach (var t in trackinfo.EnumerateArray())
            {
                if (tracks.Count >= limit)
                    break;

                var titleLink = t.Str("title_link");

                tracks.Add(new TrackInfo
                {
                    Title = t.Str("title"),
                    Url = string.IsNullOrWhiteSpace(titleLink) ? string.Empty : baseUrl + titleLink,
                    PlayCount = 0,
                    ReleasedAt = albumDate,
                    Tags = string.Empty,
                    IsDownloadable = t.Bool("is_downloadable")
                });
            }

            return tracks;
        }
    }

    private ArtistCandidate MapArtist(JsonElement item, string bandUrl) => new()
    {
        Platform = Platform,
        SourceId = item.NumberAsString("band_id"),
        SourceUrl = bandUrl,
        Nickname = item.Str("band_name"),
        AvatarUrl = BuildImageUrl(item.Obj("band_image")?.NumberAsString("image_id") ?? string.Empty),
        Country = item.Str("band_location"),
        LastTrackDate = NormalizeBandcampDate(item.Str("release_date"))
    };

    private static string NormalizeTag(string genreTag) =>
        string.IsNullOrWhiteSpace(genreTag)
            ? string.Empty
            : genreTag.Trim().ToLowerInvariant().Replace(' ', '-');

    private static string CleanUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;

        var qIndex = url.IndexOf('?');
        return qIndex >= 0 ? url[..qIndex] : url;
    }

    private static string BuildImageUrl(string rawImageId)
    {
        if (string.IsNullOrWhiteSpace(rawImageId) || !long.TryParse(rawImageId, out var id))
            return string.Empty;

        return $"https://f4.bcbits.com/img/{id:D10}_10.jpg";
    }

    private static string NormalizeBandcampDate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var cleaned = raw.Replace(" UTC", string.Empty).Replace(" GMT", string.Empty).Trim();

        return DateTime.TryParse(cleaned, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : string.Empty;
    }

    private sealed class DiscoverRequest
    {
        [JsonPropertyName("category_id")] public int CategoryId { get; init; } = 0;
        [JsonPropertyName("tag_norm_names")] public string[] TagNormNames { get; init; } = Array.Empty<string>();
        [JsonPropertyName("geoname_id")] public int GeonameId { get; init; } = 0;
        [JsonPropertyName("slice")] public string Slice { get; init; } = "top";
        [JsonPropertyName("time_facet_id")] public int? TimeFacetId { get; init; }
        [JsonPropertyName("cursor")] public string? Cursor { get; init; }
        [JsonPropertyName("size")] public int Size { get; init; }
        [JsonPropertyName("include_result_types")] public string[] IncludeResultTypes { get; init; } = { "a" };
        [JsonPropertyName("followed_bands")] public bool FollowedBands { get; init; } = false;
    }
}
