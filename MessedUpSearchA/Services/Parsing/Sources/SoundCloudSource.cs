using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MessedUpSearchA.Services.Parsing.Sources;

public class SoundCloudSource : IArtistSource, IAudioResolver
{
    private const string Api = "https://api-v2.soundcloud.com";
    private const int PageSize = 50;
    private const int MaxPages = 6;

    private static readonly Regex Instagram =
        new(@"instagram\.com/([A-Za-z0-9._]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex Spotify =
        new(@"open\.spotify\.com/artist/([A-Za-z0-9]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly RateLimiter _limiter = new(60);
    private readonly SoundCloudClientIdProvider _clientIds;
    private readonly Func<string, CancellationToken, Task<string>> _fetch;

    public SoundCloudSource(
        Func<string, CancellationToken, Task<string>>? fetch = null,
        SoundCloudClientIdProvider? clientIds = null)
    {
        _fetch = fetch ?? ParsingHttp.GetStringAsync;
        _clientIds = clientIds ?? new SoundCloudClientIdProvider(fetch);
    }

    public string Platform => "SoundCloud";

    public bool SupportsPlayCountFilter => true;

    public async Task<IReadOnlyList<ArtistCandidate>> SearchArtistsAsync(
        ArtistSearchQuery query, CancellationToken ct)
    {
        var artists = new List<ArtistCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Поиска артистов по жанру у SoundCloud нет: /search/users ищет по нику.
        // Поэтому идём через треки — жанр там живёт в свободных тегах.
        var term = string.IsNullOrWhiteSpace(query.GenreTag) ? "type beat" : query.GenreTag.Trim();

        var searchCap = query.HasPostSearchFilter ? int.MaxValue : query.MaxArtists;

        for (var page = 0; page < MaxPages && artists.Count < searchCap; page++)
        {
            ct.ThrowIfCancellationRequested();

            var url = $"{Api}/search/tracks?q={Uri.EscapeDataString(term)}" +
                      $"&limit={PageSize}&offset={page * PageSize}";

            using var doc = await GetJsonAsync(url, ct);

            if (!doc.RootElement.TryGetProperty("collection", out var collection) ||
                collection.ValueKind != JsonValueKind.Array)
            {
                break;
            }

            var received = collection.GetArrayLength();
            if (received == 0)
                break;

            foreach (var track in collection.EnumerateArray())
            {
                var user = track.Obj("user");
                if (user is null)
                    continue;

                var profile = user.Value.Str("permalink_url");
                if (string.IsNullOrWhiteSpace(profile) || !seen.Add(profile))
                    continue;

                artists.Add(MapArtist(user.Value, profile));

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
        if (string.IsNullOrWhiteSpace(artist.SourceId))
            return Array.Empty<TrackInfo>();

        var url = $"{Api}/users/{artist.SourceId}/tracks?limit={Math.Clamp(limit, 1, 100)}";

        using var doc = await GetJsonAsync(url, ct);

        var tracks = new List<TrackInfo>();

        if (!doc.RootElement.TryGetProperty("collection", out var collection) ||
            collection.ValueKind != JsonValueKind.Array)
        {
            return tracks;
        }

        foreach (var track in collection.EnumerateArray())
        {
            tracks.Add(new TrackInfo
            {
                Title = track.Str("title"),
                Url = track.Str("permalink_url"),
                SourceId = track.NumberAsString("id"),
                PlayCount = track.Int("playback_count"),
                ReleasedAt = NormalizeDate(track.Str("created_at")),
                Tags = ParseTagList(track.Str("tag_list"), track.Str("genre")),
                IsDownloadable = track.Bool("downloadable")
            });
        }

        return tracks;
    }

    /// <summary>
    /// Звук трека через неофициальный api-v2. Берём progressive mp3, если его нет —
    /// HLS с mp3-сегментами (их можно склеить подряд). AAC-варианты не берём: наш
    /// декодер их не читает. Сниппеты GO+ отбрасываем — 30 секунд из середины
    /// чужого платного трека не то же самое, что трек.
    /// </summary>
    public async Task<ResolvedAudio?> ResolveAsync(string trackId, string trackUrl, CancellationToken ct)
    {
        string url;
        if (!string.IsNullOrWhiteSpace(trackId))
            url = $"{Api}/tracks/{Uri.EscapeDataString(trackId)}";
        else if (!string.IsNullOrWhiteSpace(trackUrl))
            url = $"{Api}/resolve?url={Uri.EscapeDataString(trackUrl)}";
        else
            return null;

        using var doc = await GetJsonAsync(url, ct);
        var track = doc.RootElement;

        if (track.Str("policy") == "SNIP" || track.Str("kind") != "track")
            return null;

        var media = track.Obj("media");
        if (media is null ||
            !media.Value.TryGetProperty("transcodings", out var transcodings) ||
            transcodings.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        JsonElement? progressive = null, hls = null;

        foreach (var transcoding in transcodings.EnumerateArray())
        {
            if (transcoding.Bool("snipped"))
                continue;

            var format = transcoding.Obj("format");
            if (format is null || format.Value.Str("mime_type") != "audio/mpeg")
                continue;

            var protocol = format.Value.Str("protocol");
            if (protocol == "progressive")
                progressive ??= transcoding;
            else if (protocol == "hls")
                hls ??= transcoding;
        }

        var chosen = progressive ?? hls;
        if (chosen is null)
            return null;

        var authorization = track.Str("track_authorization");
        var resolveUrl = chosen.Value.Str("url") +
                         (string.IsNullOrWhiteSpace(authorization)
                             ? string.Empty
                             : "?track_authorization=" + Uri.EscapeDataString(authorization));

        using var resolved = await GetJsonAsync(resolveUrl, ct);
        var mediaUrl = resolved.RootElement.Str("url");
        if (string.IsNullOrWhiteSpace(mediaUrl))
            return null;

        if (progressive is not null)
            return new ResolvedAudio { Urls = [mediaUrl], AudioKind = AudioKinds.Full };

        // HLS: плейлист — это список сегментов, строки без решётки. Ссылки в нём абсолютные.
        await _limiter.WaitAsync(ct);
        var playlist = await _fetch(mediaUrl, ct);

        var segments = playlist
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith('#'))
            .Select(line => Uri.TryCreate(new Uri(mediaUrl), line, out var absolute) ? absolute.ToString() : line)
            .ToList();

        return segments.Count == 0
            ? null
            : new ResolvedAudio { Urls = segments, AudioKind = AudioKinds.Full };
    }

    /// <summary>Описание профиля — из него подсказываются контакты.</summary>
    public async Task<string> GetBioAsync(string profileUrl, CancellationToken ct)
    {
        using var doc = await GetJsonAsync($"{Api}/resolve?url={Uri.EscapeDataString(profileUrl)}", ct);
        return doc.RootElement.Str("kind") == "user" ? doc.RootElement.Str("description") : string.Empty;
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
    {
        await _limiter.WaitAsync(ct);

        var clientId = await _clientIds.GetAsync(ct);

        try
        {
            return JsonDocument.Parse(await _fetch(WithClientId(url, clientId), ct));
        }
        catch (Exception ex) when (IsAuthFailure(ex))
        {

            // client_id протух — тянем свежий и пробуем ещё раз.
            var refreshed = await _clientIds.RefreshAsync(ct);
            return JsonDocument.Parse(await _fetch(WithClientId(url, refreshed), ct));
        }
    }

    private static bool IsAuthFailure(Exception ex) =>
        ex.Message.Contains("401") || ex.Message.Contains("403");

    private static string WithClientId(string url, string clientId) =>
        url + (url.Contains('?') ? "&" : "?") + "client_id=" + clientId;

    private ArtistCandidate MapArtist(JsonElement user, string profileUrl)
    {
        var description = user.Str("description");
        var city = user.Str("city");
        var country = user.Str("country_code");

        return new ArtistCandidate
        {
            Platform = Platform,
            SourceId = user.Int("id").ToString(CultureInfo.InvariantCulture),
            SourceUrl = profileUrl,
            Nickname = user.Str("username"),
            AvatarUrl = UpscaleAvatar(user.Str("avatar_url")),
            Description = description,
            Country = string.IsNullOrWhiteSpace(country) ? city : country,
            City = city,
            Followers = user.Int("followers_count"),
            IgLink = MatchLink(Instagram, description, "https://instagram.com/"),
            SpotifyLink = MatchLink(Spotify, description, "https://open.spotify.com/artist/")
        };
    }

    private static string MatchLink(Regex pattern, string text, string prefix)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var match = pattern.Match(text);
        return match.Success ? prefix + match.Groups[1].Value : string.Empty;
    }

    private static string UpscaleAvatar(string url) =>
        string.IsNullOrWhiteSpace(url) ? string.Empty : url.Replace("-large.jpg", "-t500x500.jpg");

    /// <summary>
    /// SoundCloud отдаёт теги одной строкой через пробел, многословные — в кавычках:
    /// шансон "dimebag plugg" "benzo gang" trap
    /// </summary>
    internal static string ParseTagList(string tagList, string genre = "")
    {
        var tags = new List<string>();

        if (!string.IsNullOrWhiteSpace(genre))
            tags.Add(genre.Trim());

        var buffer = new StringBuilder();
        var inQuotes = false;

        foreach (var symbol in tagList ?? string.Empty)
        {
            if (symbol == '"')
            {
                inQuotes = !inQuotes;

                if (!inQuotes)
                {
                    Flush(tags, buffer);
                }

                continue;
            }

            if (symbol == ' ' && !inQuotes)
            {
                Flush(tags, buffer);
                continue;
            }

            buffer.Append(symbol);
        }

        Flush(tags, buffer);

        return string.Join(", ", tags);
    }

    private static void Flush(List<string> tags, StringBuilder buffer)
    {
        var value = buffer.ToString().Trim();
        buffer.Clear();

        if (value.Length > 0 && !tags.Contains(value, StringComparer.OrdinalIgnoreCase))
            tags.Add(value);
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
