using MessedUpSearchA.Services.Parsing;
using MessedUpSearchA.Services.Parsing.Sources;

namespace Crawler;

/// <summary>
/// Поиск артистов: жанр × площадка через тот же ParserService, что в приложении,
/// с порогом прослушиваний. Останавливается, когда набрано target рэперов.
/// </summary>
public static class Discover
{
    /// <summary>
    /// Жанры из приложения, но в виде поисковых запросов: «ambient» или «sad» сами по себе
    /// находят что угодно, кроме рэпа, поэтому где надо — с уточнением.
    /// </summary>
    public static readonly string[] DefaultTerms =
        { "rage", "plugg", "jerk", "cloud rap", "phonk", "dark trap", "trap", "sad rap", "ambient rap" };

    public const int PlaysMin = 1_000;
    public const int PlaysMax = 300_000;

    public static async Task RunAsync(CrawlDb db, int target, string[] terms, CancellationToken ct)
    {
        IArtistSource[] sources = { new SoundCloudSource(), new AudiusSource() };

        // С запасом: часть отсечёт фильтр битмейкеров, часть окажется дублями между жанрами.
        var perCell = (int)Math.Ceiling(target * 1.6 / (terms.Length * sources.Length));

        foreach (var term in terms)
        {
            foreach (var source in sources)
            {
                var rappers = CountRappers(db);
                if (rappers >= target)
                {
                    Console.WriteLine($"набрано {rappers} рэперов — хватит");
                    return;
                }

                ct.ThrowIfCancellationRequested();
                Console.WriteLine($"\n== {term} / {source.Platform}: ищу до {perCell}");

                var query = new ArtistSearchQuery
                {
                    GenreTag = term,
                    PlaysMin = PlaysMin,
                    PlaysMax = PlaysMax,
                    MaxArtists = perCell,
                    TracksPerArtist = 10
                };

                var progress = new Progress<string>(line => Console.Write($"\r{Trim(line),-110}"));
                var run = await new ParserService(new[] { source }).RunAsync(query, progress, ct);
                Console.WriteLine();

                foreach (var failure in run.Failures)
                    Console.WriteLine($"   площадка отвалилась: {failure.Message}");

                var added = 0;
                var producers = 0;

                foreach (var artist in run.Candidates)
                {
                    var reason = ProducerFilter.Reason(artist);
                    if (Save(db, artist, term, reason))
                    {
                        added++;
                        if (reason.Length > 0)
                            producers++;
                    }
                }

                Console.WriteLine($"   новых {added}, из них битмейкеров {producers}; всего рэперов {CountRappers(db)}");
            }
        }
    }

    public static int CountRappers(CrawlDb db) =>
        db.Scalar<int>("SELECT COUNT(*) FROM artists WHERE producer_reason = ''");

    private static bool Save(CrawlDb db, ArtistCandidate artist, string genre, string producerReason)
    {
        if (db.Scalar<long>("SELECT COUNT(*) FROM artists WHERE source_url = $u", ("$u", artist.SourceUrl)) > 0)
            return false;

        using var transaction = db.Connection.BeginTransaction();

        var artistId = db.Scalar<long>("""
            INSERT INTO artists (platform, source_id, source_url, nickname, avatar_url, plays, followers, genre, producer_reason, found_at)
            VALUES ($p, $sid, $url, $nick, $avatar, $plays, $followers, $genre, $reason, $at)
            RETURNING id
            """,
            ("$p", artist.Platform), ("$sid", artist.SourceId), ("$url", artist.SourceUrl),
            ("$nick", artist.Nickname), ("$avatar", artist.AvatarUrl), ("$plays", artist.TotalPlays),
            ("$followers", artist.Followers), ("$genre", genre), ("$reason", producerReason),
            ("$at", DateTime.Now.ToString("yyyy-MM-dd HH:mm")));

        foreach (var track in artist.Tracks)
        {
            db.Execute("""
                INSERT INTO tracks (artist_id, source_track_id, url, title, plays)
                VALUES ($a, $sid, $url, $title, $plays)
                """,
                ("$a", artistId), ("$sid", track.SourceId), ("$url", track.Url),
                ("$title", track.Title), ("$plays", track.PlayCount));
        }

        transaction.Commit();
        return true;
    }

    private static string Trim(string line) => line.Length > 110 ? line[..107] + "…" : line;
}
