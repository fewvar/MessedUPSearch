using System.Globalization;
using MessedUpSearchA.Services.Ml;

namespace Crawler;

/// <summary>Оценка пилота, пробный запрос битом и экспорт индекса v2.</summary>
public static class Analysis
{
    private const int TopTracks = 3;

    /// <param name="Vectors">инструменталы (kind='inst'), центр — из references_v2.bin: по ним поиск</param>
    /// <param name="FullVectors">куски с голосом (kind='full'), центр — artist_index.bin: по ним фильтр голоса</param>
    private sealed record Artist(long Id, string Nickname, string Platform, string SourceId, string Url,
        string Avatar, string Genre, int Plays, List<float[]> Vectors, List<float[]> FullVectors)
    {
        /// <summary>Лучший из послушанных треков: его играет ▶ в выдаче приложения.</summary>
        public (string Id, string Url, string Title, int Plays) TopTrack { get; set; } = ("", "", "", -1);
    }

    /// <summary>Веса классификатора голоса; нет файла — фильтр не применяется.</summary>
    public static string VocalPath { get; set; } = string.Empty;

    /// <summary>Центр для инструменталов — общий с приложением (references_v2.bin).</summary>
    public static string ReferencesPath { get; set; } = string.Empty;

    /// <summary>Центр, которым учили фильтр голоса (векторы с голосом) — artist_index.bin.</summary>
    public static string FriendIndexPath { get; set; } = string.Empty;

    /// <summary>
    /// Рэперы с векторами. Если есть классификатор голоса — без тех, у кого в треках нет голоса
    /// (считается по кускам С голосом: разделённый инструментал голоса не содержит по определению).
    /// </summary>
    private static List<Artist> LoadArtists(CrawlDb db)
    {
        var all = LoadAllArtists(db);
        var vocal = VocalFilter.TryLoad(VocalPath);
        if (vocal is null)
            return all;

        var kept = all.Where(a => vocal.IsRapper(a.FullVectors)).ToList();
        Console.WriteLine($"фильтр голоса: оставлено {kept.Count} из {all.Count}");
        return kept;
    }

    private static List<Artist> LoadAllArtists(CrawlDb db)
    {
        var instCenter = TargetIndex.Load(ReferencesPath).Center;
        var fullCenter = ArtistIndex.Load(FriendIndexPath).Center;
        var artists = new Dictionary<long, Artist>();

        using var command = db.Command("""
            SELECT a.id, a.nickname, a.platform, a.source_id, a.source_url, a.avatar_url, a.genre, a.plays,
                   i.vector, f.vector, t.source_track_id, t.url, t.title, t.plays
            FROM embeddings i
            JOIN embeddings f ON f.track_id = i.track_id AND f.kind = 'full'
            JOIN tracks t ON t.id = i.track_id
            JOIN artists a ON a.id = t.artist_id
            WHERE i.kind = 'inst' AND a.producer_reason = ''
            """);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetInt64(0);
            if (!artists.TryGetValue(id, out var artist))
            {
                artists[id] = artist = new Artist(id, reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetInt32(7),
                    new List<float[]>(), new List<float[]>());
            }

            artist.Vectors.Add(MertEmbedder.Centered(MertEmbedder.FromBytes((byte[])reader[8]), instCenter));
            artist.FullVectors.Add(MertEmbedder.Centered(MertEmbedder.FromBytes((byte[])reader[9]), fullCenter));

            var trackPlays = reader.GetInt32(13);
            if (trackPlays > artist.TopTrack.Plays)
                artist.TopTrack = (reader.GetString(10), reader.GetString(11), reader.GetString(12), trackPlays);
        }

        return artists.Values.ToList();
    }

    /// <summary>
    /// Leave-one-track-out: каждый трек — запрос, остальные треки — база. Честнее, чем
    /// ничего, но мягче leave-one-album-out: соседние треки артиста могут быть с одного
    /// релиза. Поэтому цифру сравниваем со случайным угадыванием, а не с 75% старого замера.
    /// </summary>
    public static void Evaluate(CrawlDb db)
    {
        var artists = LoadArtists(db).Where(a => a.Vectors.Count >= 2).ToList();

        var tracks = artists.SelectMany((a, ai) => a.Vectors.Select((v, ti) => (Artist: ai, Track: ti, Vector: v))).ToList();
        Console.WriteLine($"артистов с 2+ векторами: {artists.Count}, треков: {tracks.Count}");
        if (artists.Count < 10)
            return;

        int top1 = 0, top5 = 0, top10 = 0;
        var perArtistHits = new int[artists.Count];

        foreach (var query in tracks)
        {
            var scores = new List<(int Artist, float Score)>(artists.Count);

            for (var a = 0; a < artists.Count; a++)
            {
                var sims = artists[a].Vectors
                    .Where((_, t) => a != query.Artist || t != query.Track)
                    .Select(v => Dot(query.Vector, v))
                    .OrderByDescending(s => s)
                    .Take(TopTracks)
                    .ToList();

                if (sims.Count > 0)
                    scores.Add((a, sims.Average()));
            }

            var rank = scores.OrderByDescending(s => s.Score).Select(s => s.Artist).ToList().IndexOf(query.Artist);
            if (rank == 0) { top1++; perArtistHits[query.Artist]++; }
            if (rank is >= 0 and < 5) top5++;
            if (rank is >= 0 and < 10) top10++;
        }

        double n = tracks.Count, k = artists.Count;
        Console.WriteLine($"\n            попадание   случайно");
        Console.WriteLine($"top-1       {top1 / n,8:P1}   {1 / k,8:P1}");
        Console.WriteLine($"top-5       {top5 / n,8:P1}   {5 / k,8:P1}");
        Console.WriteLine($"top-10      {top10 / n,8:P1}   {10 / k,8:P1}");

        var platforms = artists.GroupBy(a => a.Platform).Select(g => $"{g.Key} {g.Count()}");
        Console.WriteLine($"\nпо площадкам: {string.Join(", ", platforms)}");
    }

    /// <summary>
    /// Биты -> «звучит как» и топ-10 доступных артистов — тем же AnalyzeAll, что в приложении,
    /// поэтому выдача здесь ровно та, которую увидит пользователь.
    /// </summary>
    public static void Query(CrawlDb db, string modelPath, string indexPath, IEnumerable<string> beats, string? backgroundPath)
    {
        using var service = new BeatSimilarityService(modelPath, indexPath);
        var references = TargetIndex.Load(ReferencesPath);
        // Как в экспорте: артист с одним куском — это один случайный трек, в индекс не идёт.
        var full = BuildIndex(db, references.Center);
        var index = new TargetIndex { Center = full.Center, Artists = full.Artists.Where(a => a.Vectors.Length >= 2).ToList() };
        var hubs = backgroundPath is null ? null : HubCorrection.TryLoad(backgroundPath, references.Center);

        Console.WriteLine($"поправка на хабы: {(hubs is null ? "выключена" : "включена")}; в индексе {index.Artists.Count} артистов");

        foreach (var beat in beats)
        {
            var result = service.AnalyzeAll(beat, references, index, new Dictionary<string, int>(), hubs);

            Console.WriteLine($"\n== {Path.GetFileName(beat)}");
            Console.WriteLine($"   звучит как: {string.Join(" · ", result.References.Select(r => $"{r.Artist} {r.Percent}%"))}");

            foreach (var match in result.Targets)
            {
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "   {0,-24} {1,6:F3} {2,4}%  {3,9:N0} прослуш.  {4}",
                    Cut(match.Artist, 24), match.Similarity, match.Percent, match.Plays, match.SourceUrl));
            }
        }
    }

    private static TargetIndex BuildIndex(CrawlDb db, float[] center)
    {
        var artists = LoadArtists(db);
        return new TargetIndex
        {
            Center = center,
            Artists = artists.Select(a => new TargetArtist
            {
                Nickname = a.Nickname, Platform = a.Platform, SourceId = a.SourceId, SourceUrl = a.Url,
                AvatarUrl = a.Avatar, Genre = a.Genre, Plays = a.Plays, Vectors = a.Vectors.ToArray(),
                TopTrackId = a.TopTrack.Id, TopTrackUrl = a.TopTrack.Url, TopTrackTitle = a.TopTrack.Title
            }).ToList()
        };
    }

    public static void Export(CrawlDb db, string outPath, int minVectors)
    {
        var center = TargetIndex.Load(ReferencesPath).Center;
        var full = BuildIndex(db, center);
        var index = new TargetIndex
        {
            Center = center,
            Artists = full.Artists.Where(a => a.Vectors.Length >= minVectors).ToList()
        };

        index.Save(outPath);

        // Сразу читаем обратно — формат проверяется на месте, а не у пользователя.
        var check = TargetIndex.Load(outPath);
        var maxError = check.Artists.Zip(index.Artists)
            .SelectMany(p => p.First.Vectors.Zip(p.Second.Vectors))
            .SelectMany(p => p.First.Zip(p.Second, (x, y) => Math.Abs(x - y)))
            .DefaultIfEmpty(0).Max();

        Console.WriteLine($"{outPath}: {check.Artists.Count} артистов, " +
                          $"{new FileInfo(outPath).Length / 1024.0 / 1024:F1} МБ, погрешность Half {maxError:E1}");
    }

    private static float Dot(float[] a, float[] b)
    {
        var sum = 0f;
        for (var i = 0; i < a.Length; i++)
            sum += a[i] * b[i];
        return sum;
    }

    private static string Cut(string text, int length) => text.Length > length ? text[..(length - 1)] + "…" : text;
}
