using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MessedUpSearchA.Services.Parsing.Sources;

public class AudiusSource : IArtistSource, IAudioResolver
{
    private const string Host = "https://api.audius.co";
    private const string AppName = "MessedUpSearch";
    private const string Web = "https://audius.co";

    private const int PageSize = 100;
    private const int MaxPages = 5;

    private readonly RateLimiter _limiter = new(120);

    private readonly Func<string, CancellationToken, Task<string>> _fetch;

    public AudiusSource(Func<string, CancellationToken, Task<string>>? fetch = null)
        => _fetch = fetch ?? ParsingHttp.GetStringAsync;

    public string Platform => "Audius";

    public bool SupportsPlayCountFilter => true;

    public async Task<IReadOnlyList<ArtistCandidate>> SearchArtistsAsync(
        ArtistSearchQuery query, CancellationToken ct)
    {
        var artists = new List<ArtistCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var searchCap = query.HasPostSearchFilter ? int.MaxValue : query.MaxArtists;

        for (var page = 0; page < MaxPages && artists.Count < searchCap; page++)
        {
            ct.ThrowIfCancellationRequested();
            await _limiter.WaitAsync(ct);

            using var doc = JsonDocument.Parse(await _fetch(BuildSearchUrl(query, page * PageSize), ct));

            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Array ||
                data.GetArrayLength() == 0)
            {
                break;
            }

            foreach (var track in data.EnumerateArray())
            {
                var user = track.Obj("user");
                if (user is null)
                    continue;

                var handle = user.Value.Str("handle");
                if (string.IsNullOrWhiteSpace(handle) || !seen.Add(handle))
                    continue;

                artists.Add(MapArtist(user.Value, handle));

                if (artists.Count >= searchCap)
                    break;
            }

            if (data.GetArrayLength() < PageSize)
                break;
        }

        return artists;
    }

    public async Task<IReadOnlyList<TrackInfo>> GetTracksAsync(
        ArtistCandidate artist, int limit, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(artist.SourceId))
            return Array.Empty<TrackInfo>();

        await _limiter.WaitAsync(ct);

        var url = $"{Host}/v1/users/{artist.SourceId}/tracks?limit={Math.Clamp(limit, 1, 100)}&app_name={AppName}";
        using var doc = JsonDocument.Parse(await _fetch(url, ct));

        var tracks = new List<TrackInfo>();

        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return tracks;

        foreach (var track in data.EnumerateArray())
        {
            var permalink = track.Str("permalink");

            tracks.Add(new TrackInfo
            {
                Title = track.Str("title"),
                Url = string.IsNullOrWhiteSpace(permalink) ? string.Empty : Web + permalink,
                SourceId = track.Str("id"),
                PlayCount = track.Int("play_count"),
                ReleasedAt = NormalizeDate(track.Str("release_date")),
                Tags = (track.Str("tags") ?? string.Empty).Replace(",", ", "),
                IsDownloadable = track.Bool("is_downloadable")
            });
        }

        return tracks;
    }

    /// <summary>
    /// Audius отдаёт полный трек открыто: /stream редиректит на mp3 в хранилище.
    /// Трекам, сохранённым до v0.7, id не досталось — достаём его по ссылке через /resolve.
    /// </summary>
    public async Task<ResolvedAudio?> ResolveAsync(string trackId, string trackUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(trackId))
        {
            if (string.IsNullOrWhiteSpace(trackUrl))
                return null;

            await _limiter.WaitAsync(ct);
            using var doc = JsonDocument.Parse(await _fetch(
                $"{Host}/v1/resolve?url={Uri.EscapeDataString(trackUrl)}&app_name={AppName}", ct));

            trackId = doc.RootElement.Obj("data")?.Str("id") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trackId))
                return null;
        }

        return new ResolvedAudio
        {
            Urls = [$"{Host}/v1/tracks/{Uri.EscapeDataString(trackId)}/stream?app_name={AppName}"],
            AudioKind = AudioKinds.Full
        };
    }

    /// <summary>Описание профиля плюс отдельные поля Audius с инстаграмом и сайтом.</summary>
    public async Task<string> GetBioAsync(string profileUrl, CancellationToken ct)
    {
        await _limiter.WaitAsync(ct);
        using var doc = JsonDocument.Parse(await _fetch(
            $"{Host}/v1/resolve?url={Uri.EscapeDataString(profileUrl)}&app_name={AppName}", ct));

        var user = doc.RootElement.Obj("data");
        if (user is null)
            return string.Empty;

        var instagram = user.Value.Str("instagram_handle");
        return string.Join("\n", user.Value.Str("bio"), user.Value.Str("website"),
            string.IsNullOrWhiteSpace(instagram) ? "" : "instagram: @" + instagram.TrimStart('@'));
    }

    private string BuildSearchUrl(ArtistSearchQuery query, int offset)
    {

        if (string.IsNullOrWhiteSpace(query.GenreTag))
            return $"{Host}/v1/tracks/trending?limit={PageSize}&offset={offset}&app_name={AppName}";

        var tag = Uri.EscapeDataString(query.GenreTag.Trim());
        return $"{Host}/v1/tracks/search?query={tag}&limit={PageSize}&offset={offset}&app_name={AppName}";
    }

    private ArtistCandidate MapArtist(JsonElement user, string handle)
    {
        var name = user.Str("name");

        return new ArtistCandidate
        {
            Platform = Platform,
            SourceId = user.Str("id"),
            SourceUrl = $"{Web}/{handle}",
            Nickname = string.IsNullOrWhiteSpace(name) ? handle : name,
            AvatarUrl = PickAvatar(user),
            Description = user.Str("bio"),
            Country = user.Str("location"),
            Followers = user.Int("follower_count"),
            IgLink = BuildSocialLink("https://instagram.com/", user.Str("instagram_handle")),
            Website = user.Str("website")
        };
    }

    private static string BuildSocialLink(string prefix, string rawHandle)
    {
        var handle = (rawHandle ?? string.Empty).Trim().TrimStart('@');

        if (handle.Length == 0)
            return string.Empty;

        foreach (var c in handle)
        {
            if (!char.IsLetterOrDigit(c) && c != '.' && c != '_' && c != '-')
                return string.Empty;
        }

        return prefix + handle;
    }

    private static string PickAvatar(JsonElement user)
    {
        var picture = user.Obj("profile_picture");
        if (picture is null)
            return string.Empty;

        foreach (var size in new[] { "1000x1000", "480x480", "150x150" })
        {
            var url = picture.Value.Str(size);
            if (!string.IsNullOrWhiteSpace(url))
                return url;
        }

        return string.Empty;
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
