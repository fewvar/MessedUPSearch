using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MessedUpSearchA.Services.Parsing;

public class ParserService
{
    private readonly IReadOnlyList<IArtistSource> _sources;

    public ParserService(IReadOnlyList<IArtistSource> sources) => _sources = sources;

    public async Task<ParserRunResult> RunAsync(
        ArtistSearchQuery query,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var result = new ParserRunResult();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in _sources)
        {
            ct.ThrowIfCancellationRequested();

            var collectedHere = 0;

            try
            {
                progress?.Report($"{source.Platform}: ищу артистов…");
                var found = await source.SearchArtistsAsync(query, ct);

                foreach (var artist in found)
                {
                    ct.ThrowIfCancellationRequested();

                    if (collectedHere >= query.MaxArtists)
                        break;

                    if (string.IsNullOrWhiteSpace(artist.SourceUrl) || !seenUrls.Add(artist.SourceUrl))
                        continue;

                    progress?.Report($"{source.Platform}: {artist.Nickname} — треки…");
                    var tracks = await source.GetTracksAsync(artist, query.TracksPerArtist, ct);

                    artist.Tracks.Clear();
                    artist.Tracks.AddRange(tracks);
                    ApplyTrackAggregates(artist);

                    if (!Passes(artist, query, source.SupportsPlayCountFilter))
                        continue;

                    result.Candidates.Add(artist);
                    collectedHere++;
                }

                progress?.Report($"{source.Platform}: готово, {collectedHere}");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {

                result.Failures.Add(new SourceFailure
                {
                    Platform = source.Platform,
                    Message = ex.Message,
                    CollectedBefore = collectedHere
                });
                progress?.Report($"{source.Platform}: отвалилась — {ex.Message}");
            }
        }

        return result;
    }

    private static void ApplyTrackAggregates(ArtistCandidate artist)
    {
        if (artist.Tracks.Count == 0)
            return;

        if (artist.TotalPlays == 0)
            artist.TotalPlays = artist.Tracks.Sum(t => t.PlayCount);

        if (string.IsNullOrWhiteSpace(artist.LastTrackDate))
        {
            var latest = artist.Tracks
                .Select(t => DateTime.TryParse(t.ReleasedAt, out var d) ? d : (DateTime?)null)
                .Where(d => d.HasValue)
                .OrderByDescending(d => d!.Value)
                .FirstOrDefault();

            if (latest.HasValue)
                artist.LastTrackDate = latest.Value.ToString("yyyy-MM-dd HH:mm");
        }

        if (string.IsNullOrWhiteSpace(artist.Tags))
        {
            var tags = artist.Tracks
                .SelectMany(t => t.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => g.Key);

            artist.Tags = string.Join(", ", tags);
        }
    }

    private static bool Passes(ArtistCandidate artist, ArtistSearchQuery query, bool sourceHasPlayCounts)
    {

        if (sourceHasPlayCounts)
        {
            if (query.PlaysMin.HasValue && artist.TotalPlays < query.PlaysMin.Value)
                return false;
            if (query.PlaysMax.HasValue && artist.TotalPlays > query.PlaysMax.Value)
                return false;
        }

        if (query.FreshWithinDays.HasValue && DateTime.TryParse(artist.LastTrackDate, out var last))
        {
            if (last < DateTime.Now.AddDays(-query.FreshWithinDays.Value))
                return false;
        }

        if (!string.IsNullOrWhiteSpace(query.Country) &&
            !string.IsNullOrWhiteSpace(artist.Country) &&
            !artist.Country.Contains(query.Country, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }
}
