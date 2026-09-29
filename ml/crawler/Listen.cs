using System.Diagnostics;
using System.Threading.Channels;
using MessedUpSearchA.Services.Ml;
using MessedUpSearchA.Services.Parsing;

namespace Crawler;

/// <summary>
/// Слушает треки найденных рэперов: кусок трека из середины -> MERT -> сырой вектор.
///
/// Два потока: один качает (упирается в сеть), другой считает (упирается в процессор),
/// между ними очередь на несколько файлов. Так время одного трека — максимум из двух,
/// а не сумма.
/// </summary>
public static class Listen
{
    private const int MinSeconds = 15;

    private sealed record Job(long TrackId, long ArtistId, string Platform, string SourceTrackId, string Url, string Nickname);

    private sealed record Downloaded(Job Job, string Path, string Kind);

    /// <param name="negatives">
    /// Больше нуля — слушаем не рэперов, а явных битмейкеров («биты: 10/10»), по одному треку:
    /// это примеры «звука без голоса» для классификатора (ml/scripts/train_vocal.py).
    /// </param>
    /// <summary>
    /// Новый конвейер: только скачать куски в папку-очередь. Звук забирает
    /// ml/scripts/separate_embed.py (Demucs + MERT на видеокарте) и удаляет сразу после расчёта.
    /// Файл появляется в очереди целиком (пишется как .part и переименовывается) —
    /// Python не схватит недокачанный. Если в очереди уже MaxQueued файлов, ждём:
    /// сеть быстрее видеокарты, и без этого папка разрослась бы на гигабайты.
    /// </summary>
    /// <param name="onlyIndexPath">если задан — только артисты этого индекса (докачка треков уже отобранным)</param>
    public static async Task SlicesOnlyAsync(CrawlDb db, string queueDir, int tracksPerArtist, CancellationToken ct,
        string? onlyIndexPath = null)
    {
        const int MaxQueued = 20;

        Directory.CreateDirectory(queueDir);
        File.Delete(Path.Combine(queueDir, ".done"));

        var jobs = PlanJobs(db, tracksPerArtist, countStatuses: "'sliced','embedded'");
        if (onlyIndexPath is not null)
        {
            var urls = TargetIndex.Load(onlyIndexPath).Artists.Select(a => a.SourceUrl).ToHashSet();
            var ids = new HashSet<long>();
            using (var command = db.Command("SELECT id, source_url FROM artists"))
            using (var reader = command.ExecuteReader())
                while (reader.Read())
                    if (urls.Contains(reader.GetString(1)))
                        ids.Add(reader.GetInt64(0));
            jobs = jobs.Where(j => ids.Contains(j.ArtistId)).ToList();
        }
        Console.WriteLine($"к скачиванию: {jobs.Count} кусков у {jobs.Select(j => j.ArtistId).Distinct().Count()} артистов");

        var resolvers = AudioResolvers.CreateAll();
        var temp = Path.Combine(Path.GetTempPath(), "crawler-slices");
        var clock = Stopwatch.StartNew();
        int queued = 0, failed = 0;

        try
        {
            foreach (var job in jobs)
            {
                while (Directory.GetFiles(queueDir, "*.mp3").Length >= MaxQueued)
                    await Task.Delay(1000, ct);

                try
                {
                    var audio = resolvers.TryGetValue(job.Platform, out var resolver)
                        ? await resolver.ResolveAsync(job.SourceTrackId, job.Url, ct)
                        : null;

                    if (audio is null)
                    {
                        Mark(db, job.TrackId, "noaudio", "звук недоступен");
                        failed++;
                        continue;
                    }

                    var slice = await AudioFetcher.DownloadSliceAsync(audio, temp, ct);
                    var part = Path.Combine(queueDir, $"{job.TrackId}.part");
                    File.Move(slice, part, overwrite: true);
                    File.Move(part, Path.Combine(queueDir, $"{job.TrackId}.mp3"), overwrite: true);

                    Mark(db, job.TrackId, "sliced", "");
                    queued++;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Mark(db, job.TrackId, "failed", ex.Message);
                    failed++;
                }

                Console.Write($"\r{queued + failed}/{jobs.Count}  в очереди {queued}, мимо {failed}  " +
                              $"{(queued + failed) / Math.Max(1, clock.Elapsed.TotalMinutes):F1} кусок/мин   ");
            }
        }
        finally
        {
            // Метка для Python: больше ничего не придёт, дочисти очередь и выходи.
            File.WriteAllText(Path.Combine(queueDir, ".done"), DateTime.Now.ToString("O"));
        }

        Console.WriteLine($"\nскачано за {clock.Elapsed.TotalMinutes:F1} мин: {queued}, мимо {failed}");
    }

    /// <summary>
    /// Заново скачать куски треков, у которых уже есть векторы, и СОХРАНИТЬ их (outDir/<track_id>.mp3):
    /// конвейер звук удалял, а для сравнения других моделей и дообучения нужен тот же звук.
    /// Кусок берётся с того же места (35%), статусы в базе не трогаются, готовые файлы пропускаются.
    /// </summary>
    public static async Task ResliceAsync(CrawlDb db, string outDir, CancellationToken ct)
    {
        Directory.CreateDirectory(outDir);

        using var command = db.Command("""
            SELECT t.id, a.platform, t.source_track_id, t.url
            FROM tracks t JOIN artists a ON a.id = t.artist_id
            WHERE a.producer_reason = '' AND t.status = 'embedded'
            ORDER BY a.id, t.plays DESC
            """);

        var jobs = new List<(long Id, string Platform, string SourceId, string Url)>();
        using (var reader = command.ExecuteReader())
            while (reader.Read())
                if (!File.Exists(Path.Combine(outDir, $"{reader.GetInt64(0)}.mp3")))
                    jobs.Add((reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));

        Console.WriteLine($"к скачиванию: {jobs.Count} кусков");

        var resolvers = AudioResolvers.CreateAll();
        var temp = Path.Combine(Path.GetTempPath(), "crawler-reslice");
        var clock = Stopwatch.StartNew();
        int saved = 0, failed = 0;

        foreach (var job in jobs)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var audio = resolvers.TryGetValue(job.Platform, out var resolver)
                    ? await resolver.ResolveAsync(job.SourceId, job.Url, ct)
                    : null;
                if (audio is null)
                {
                    failed++;
                    continue;
                }

                var slice = await AudioFetcher.DownloadSliceAsync(audio, temp, ct);
                File.Move(slice, Path.Combine(outDir, $"{job.Id}.mp3"), overwrite: true);
                saved++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
            }

            Console.Write($"\r{saved + failed}/{jobs.Count}  сохранено {saved}, мимо {failed}  " +
                          $"{(saved + failed) / Math.Max(1, clock.Elapsed.TotalMinutes):F1} кусок/мин   ");
        }

        Console.WriteLine($"\nготово за {clock.Elapsed.TotalMinutes:F1} мин: сохранено {saved}, мимо {failed}");
    }

    public static async Task RunAsync(CrawlDb db, string modelPath, int tracksPerArtist, CancellationToken ct, int negatives = 0)
    {
        var jobs = negatives > 0 ? PlanNegatives(db, negatives) : PlanJobs(db, tracksPerArtist);
        Console.WriteLine($"к прослушиванию: {jobs.Count} треков у {jobs.Select(j => j.ArtistId).Distinct().Count()} артистов");
        if (jobs.Count == 0)
            return;

        var resolvers = AudioResolvers.CreateAll();
        var embedder = new MertEmbedder(modelPath);
        var temp = Path.Combine(Path.GetTempPath(), "crawler-listen");

        var channel = Channel.CreateBounded<Downloaded>(4);
        var done = 0;
        var failed = 0;
        var clock = Stopwatch.StartNew();

        var producer = Task.Run(async () =>
        {
            try
            {
                foreach (var job in jobs)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var audio = resolvers.TryGetValue(job.Platform, out var resolver)
                            ? await resolver.ResolveAsync(job.SourceTrackId, job.Url, ct)
                            : null;

                        if (audio is null)
                        {
                            Mark(db, job.TrackId, "noaudio", "звук недоступен");
                            Interlocked.Increment(ref failed);
                            continue;
                        }

                        var path = await AudioFetcher.DownloadSliceAsync(audio, temp, ct);
                        await channel.Writer.WriteAsync(new Downloaded(job, path, audio.AudioKind), ct);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Mark(db, job.TrackId, "failed", ex.Message);
                        Interlocked.Increment(ref failed);
                    }
                }
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        }, ct);

        await foreach (var item in channel.Reader.ReadAllAsync(ct))
        {
            try
            {
                var samples = AudioDecoder.Decode(item.Path, MertEmbedder.SampleRate);
                var seconds = samples.Length / (double)MertEmbedder.SampleRate;

                if (seconds < MinSeconds)
                {
                    Mark(db, item.Job.TrackId, "failed", $"короче {MinSeconds} с");
                    Interlocked.Increment(ref failed);
                    continue;
                }

                var raw = embedder.EmbedRaw(samples, ct);

                db.Execute("INSERT OR REPLACE INTO vectors (track_id, vector, seconds, kind) VALUES ($t, $v, $s, $k)",
                    ("$t", item.Job.TrackId), ("$v", MertEmbedder.ToBytes(raw)),
                    ("$s", Math.Round(seconds, 1)), ("$k", item.Kind));
                Mark(db, item.Job.TrackId, "done", "");
                done++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Mark(db, item.Job.TrackId, "failed", ex.Message);
                Interlocked.Increment(ref failed);
            }
            finally
            {
                AudioFetcher.TryDelete(item.Path);
            }

            var processed = done + Volatile.Read(ref failed);
            var rate = processed / Math.Max(1, clock.Elapsed.TotalSeconds);
            var left = (jobs.Count - processed) / Math.Max(rate, 1e-6) / 60;
            Console.Write($"\r{processed}/{jobs.Count}  готово {done}, мимо {failed}  {rate * 60:F1} трек/мин  осталось ~{left:F0} мин   ");
        }

        await producer;
        Console.WriteLine($"\nготово за {clock.Elapsed.TotalMinutes:F1} мин: векторов {done}, мимо {failed}");
    }

    /// <summary>
    /// По каждому рэперу — самые прослушиваемые треки, которых ещё не слушали, столько,
    /// чтобы вместе с уже готовыми вышло tracksPerArtist. Упавшие не повторяем: на их
    /// место следующий запуск listen возьмёт следующий по прослушиваниям трек.
    /// </summary>
    private static List<Job> PlanJobs(CrawlDb db, int tracksPerArtist, string countStatuses = "'done'")
    {
        using var command = db.Command($"""
            SELECT t.id, a.id, a.platform, t.source_track_id, t.url, a.nickname,
                   (SELECT COUNT(*) FROM tracks d WHERE d.artist_id = a.id AND d.status IN ({countStatuses})) AS have
            FROM tracks t JOIN artists a ON a.id = t.artist_id
            WHERE a.producer_reason = '' AND t.status = 'new'
            ORDER BY a.id, t.plays DESC
            """);

        var jobs = new List<Job>();
        var taken = new Dictionary<long, int>();

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var artistId = reader.GetInt64(1);
            var have = reader.GetInt32(6);
            taken.TryGetValue(artistId, out var already);

            if (have + already >= tracksPerArtist)
                continue;

            taken[artistId] = already + 1;
            jobs.Add(new Job(reader.GetInt64(0), artistId, reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5)));
        }

        return jobs;
    }

    private static List<Job> PlanNegatives(CrawlDb db, int count)
    {
        using var command = db.Command("""
            SELECT t.id, a.id, a.platform, t.source_track_id, t.url, a.nickname
            FROM artists a JOIN tracks t ON t.id = (
                SELECT id FROM tracks x WHERE x.artist_id = a.id AND x.status = 'new' ORDER BY x.plays DESC LIMIT 1)
            WHERE a.producer_reason LIKE 'биты:%'
              AND NOT EXISTS (SELECT 1 FROM tracks d WHERE d.artist_id = a.id AND d.status = 'done')
            LIMIT $n
            """, ("$n", count));

        var jobs = new List<Job>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            jobs.Add(new Job(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5)));
        return jobs;
    }

    private static void Mark(CrawlDb db, long trackId, string status, string error) =>
        db.Execute("UPDATE tracks SET status = $s, error = $e WHERE id = $id",
            ("$s", status), ("$e", error.Length > 300 ? error[..300] : error), ("$id", trackId));
}
