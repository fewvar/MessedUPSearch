using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MessedUpSearchA.Services.Parsing;

public class ParserService
{
    private readonly IReadOnlyList<IArtistSource> _sources;
    private readonly GeniusLookup? _genius;

    public ParserService(IReadOnlyList<IArtistSource> sources, GeniusLookup? genius = null)
    {
        _sources = sources;
        _genius = genius;
    }

    public async Task<ParserRunResult> RunAsync(
        ArtistSearchQuery query,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var result = new ParserRunResult();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var sourceIndex = 0;
        var sourceTotal = _sources.Count;

        foreach (var source in _sources)
        {
            ct.ThrowIfCancellationRequested();
            sourceIndex++;

            var collectedHere = 0;
            var tag = $"[{sourceIndex}/{sourceTotal}] {source.Platform}";

            try
            {
                progress?.Report($"{tag}: ищу артистов…");
                var found = await source.SearchArtistsAsync(query, ct);

                var checkedCount = 0;

                foreach (var artist in found)
                {
                    ct.ThrowIfCancellationRequested();

                    if (collectedHere >= query.MaxArtists)
                        break;

                    checkedCount++;

                    if (string.IsNullOrWhiteSpace(artist.SourceUrl) || !seenUrls.Add(artist.SourceUrl))
                        continue;

                    progress?.Report(
                        $"{tag}: {collectedHere}/{query.MaxArtists} собрано — смотрю {artist.Nickname} " +
                        $"({checkedCount}/{found.Count})…");

                    var tracks = await source.GetTracksAsync(artist, query.TracksPerArtist, ct);

                    artist.Tracks.Clear();
                    artist.Tracks.AddRange(tracks);
                    ApplyTrackAggregates(artist);
                    ApplyLanguage(artist);

                    if (!Passes(artist, query, source.SupportsPlayCountFilter))
                        continue;

                    if (_genius is { IsConfigured: true })
                    {
                        progress?.Report($"{tag}: Genius — {artist.Nickname}…");
                        await ApplyGeniusAsync(artist, ct);
                    }

                    result.Candidates.Add(artist);
                    collectedHere++;
                }

                progress?.Report($"{tag}: готово, {collectedHere}");
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
                progress?.Report($"{tag}: отвалилась — {ex.Message}");
            }
        }

        return result;
    }

    private async Task ApplyGeniusAsync(ArtistCandidate artist, CancellationToken ct)
    {
        if (_genius is null || string.IsNullOrWhiteSpace(artist.Nickname))
            return;

        GeniusArtistInfo? info;

        try
        {
            info = await _genius.LookupAsync(artist.Nickname, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Genius сбойнул — у кандидата и так уже есть данные с основной площадки, не роняем его.
            return;
        }

        if (info is null)
            return;

        // Genius всегда главный по соцсетям — перезаписывает то, что уже нашлось на исходной площадке.
        if (!string.IsNullOrWhiteSpace(info.InstagramHandle))
            artist.IgLink = "https://instagram.com/" + info.InstagramHandle.TrimStart('@');

        // Genius добивает только то, что не определилось по площадке.
        if (string.IsNullOrWhiteSpace(artist.Language))
        {
            var language = ArtistLanguageGuesser.Guess(info.Description);
            if (!string.IsNullOrWhiteSpace(language))
                artist.Language = language;
        }
    }

    /// <summary>
    /// Язык по всему тексту, что есть у кандидата: описание профиля и названия
    /// треков. Названия оказались сильнее описания — их пишут всегда, а
    /// описание у большинства пустое.
    /// </summary>
    private static void ApplyLanguage(ArtistCandidate artist)
    {
        if (!string.IsNullOrWhiteSpace(artist.Language))
            return;

        var titles = string.Join(" ", artist.Tracks.Select(t => t.Title));
        var language = ArtistLanguageGuesser.Guess(artist.Description, titles);

        if (!string.IsNullOrWhiteSpace(language))
            artist.Language = language;
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
