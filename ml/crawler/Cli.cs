using System.Diagnostics;
using MessedUpSearchA.Services.Ml;
using MessedUpSearchA.Services.Parsing;

namespace Crawler;

public static class Cli
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine(Usage);
            return 1;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            // Первый Ctrl+C — аккуратная остановка: текущий трек дописывается, база цела.
            e.Cancel = true;
            cts.Cancel();
            Console.WriteLine("\nостанавливаюсь, прогресс сохранён…");
        };

        var db = () => new CrawlDb(Option(args, "--db") ?? DefaultDb);

        // --no-vocal выключает фильтр голоса (например, чтобы сравнить выдачу с ним и без).
        Analysis.VocalPath = args.Contains("--no-vocal") ? string.Empty : Path.Combine(DataDir, "vocal_lr.json");
        Analysis.ReferencesPath = Path.Combine(AppModels, "references_v3.bin");
        Analysis.HeadPath = Path.Combine(AppModels, "effnet_head.bin");
        Analysis.FriendIndexPath = DefaultIndex;

        try
        {
            switch (args[0])
            {
                case "discover":
                {
                    using var store = db();
                    var terms = Option(args, "--terms")?.Split(',', StringSplitOptions.TrimEntries) ?? Discover.DefaultTerms;
                    await Discover.RunAsync(store, int.Parse(Option(args, "--target") ?? "200"), terms, cts.Token);
                    return 0;
                }
                case "listen" when args.Contains("--slices-only"):
                {
                    using var store = db();
                    await Listen.SlicesOnlyAsync(store, Option(args, "--queue") ?? Path.Combine(DataDir, "slices"),
                        int.Parse(Option(args, "--tracks") ?? "4"), cts.Token, Option(args, "--only-index"));
                    return 0;
                }
                case "reslice":
                {
                    using var store = db();
                    await Listen.ResliceAsync(store, Option(args, "--out") ?? Path.Combine(DataDir, "audio"), cts.Token);
                    return 0;
                }
                case "reset-listen":
                {
                    // Перейти на новый конвейер: всё, что слушал старый (C#, без разделения), —
                    // заново. Старые векторы остаются в таблице vectors.
                    using var store = db();
                    var reset = store.Execute("UPDATE tracks SET status = 'new', error = '' WHERE status IN ('done', 'sliced')");
                    Console.WriteLine($"сброшено треков: {reset}");
                    return 0;
                }
                case "listen":
                {
                    using var store = db();
                    await Listen.RunAsync(store, Option(args, "--model") ?? DefaultModel,
                        int.Parse(Option(args, "--tracks") ?? "4"), cts.Token,
                        int.Parse(Option(args, "--negatives") ?? "0"));
                    return 0;
                }
                case "evaluate":
                {
                    using var store = db();
                    Analysis.Evaluate(store);
                    return 0;
                }
                case "query":
                {
                    using var store = db();
                    Analysis.Query(store, Path.Combine(AppModels, "effnet_style.onnx"),
                        args.Skip(1).Where(a => !a.StartsWith("--") && File.Exists(a)),
                        args.Contains("--no-hubs") ? null : Path.Combine(AppModels, "background_v3.bin"));
                    return 0;
                }
                case "export":
                {
                    using var store = db();
                    Analysis.Export(store,
                        Option(args, "--out") ?? Path.Combine(DataDir, "artists_index_v3.bin"),
                        int.Parse(Option(args, "--min-vectors") ?? "2"));
                    return 0;
                }
                case "refilter":
                {
                    using var store = db();
                    Refilter(store);
                    return 0;
                }
                case "producers":
                {
                    using var store = db();
                    ShowProducers(store);
                    return 0;
                }
                case "typebeats":
                    await TypeBeats.RunAsync(Option(args, "--index") ?? DefaultIndex,
                        Path.Combine(Root, "ml", "scripts", "deezer_aliases.tsv"),
                        Option(args, "--out") ?? Path.Combine(DataDir, "typebeats"),
                        int.Parse(Option(args, "--per-artist") ?? "8"), cts.Token);
                    return 0;
                case "seed-refs":
                {
                    using var store = db();
                    await RefSeed.RunAsync(store, Analysis.ReferencesPath, Path.Combine(Root, "ml", "scripts", "deezer_aliases.tsv"),
                        int.Parse(Option(args, "--tracks") ?? "10"), cts.Token);
                    return 0;
                }
                case "slice-test":
                    return await SliceTestAsync(args[1], args[2..]);
                default:
                    return Fail($"неизвестная команда {args[0]}\n{Usage}");
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("остановлено, можно продолжить той же командой");
            return 130;
        }
    }

    private const string Usage = """
        команды (все опции необязательны):
          discover   [--target 200] [--terms rage,plugg,...]   найти рэперов
          listen     [--tracks 4] [--model путь]                послушать их треки
          listen     --negatives 150                            по треку от явных битмейкеров — для классификатора голоса
          listen     --slices-only [--tracks 4] [--only-index ф] куски в очередь для separate_embed.py (--only-index: только артисты индекса)
          reset-listen                                          переслушать всё новым конвейером (с разделением вокала)
          evaluate   [--index artist_index.bin]                 качество: leave-one-track-out
          query      бит.mp3 ...                                топ-10 для битов — проверить ушами
          producers                                             кого отсёк фильтр и почему
          refilter                                              пересчитать фильтр по найденным
          typebeats  [--per-artist 8]                           линейка: «<артист> type beat» по индексу друга
          export     [--out путь] [--min-vectors 2]             собрать artists_index_v3.bin (EffNet + голова)
          reslice    [--out ml/data/audio]                      сохранить куски уже посчитанных треков (для других моделей)
          seed-refs  [--tracks 10] --db другая.db               ориентиры как артисты SoundCloud (опыт с источником)
          общее:     [--db ml/data/crawl.db]
        """;

    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    private static string DataDir => Path.Combine(Root, "ml", "data");
    private static string DefaultDb => Path.Combine(DataDir, "crawl.db");
    private static string AppModels => Path.Combine(Root, "MessedUpSearchA", "Assets", "Models");

    /// <summary>Архив друга (MERT): центр для фильтра голоса и имена для typebeats. Живёт в краулере.</summary>
    private static string DefaultIndex => Path.Combine(Root, "ml", "crawler", "Mert", "artist_index.bin");

    /// <summary>MERT нужен краулеру только для старого listen и фильтра голоса.</summary>
    private static string DefaultModel => MertEmbedder.DefaultModelPath;

    private static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    /// <summary>Фильтр поменялся — пересчитать решения по уже найденным артистам.</summary>
    private static void Refilter(CrawlDb db)
    {
        var artists = new List<(long Id, string Nick)>();
        using (var command = db.Command("SELECT id, nickname FROM artists"))
        using (var reader = command.ExecuteReader())
            while (reader.Read())
                artists.Add((reader.GetInt64(0), reader.GetString(1)));

        var changed = 0;
        foreach (var (id, nick) in artists)
        {
            var titles = new List<string>();
            using (var command = db.Command("SELECT title FROM tracks WHERE artist_id = $a", ("$a", id)))
            using (var reader = command.ExecuteReader())
                while (reader.Read())
                    titles.Add(reader.GetString(0));

            var reason = ProducerFilter.Reason(nick, titles);
            changed += db.Execute("UPDATE artists SET producer_reason = $r WHERE id = $id AND producer_reason <> $r",
                ("$r", reason), ("$id", id));
        }

        Console.WriteLine($"пересчитано: {artists.Count}, поменялось: {changed}, рэперов теперь: {Discover.CountRappers(db)}");
    }

    private static void ShowProducers(CrawlDb db)
    {
        using var command = db.Command("SELECT nickname, producer_reason, genre FROM artists WHERE producer_reason <> '' ORDER BY genre");
        using var reader = command.ExecuteReader();
        while (reader.Read())
            Console.WriteLine($"{reader.GetString(2),-12} {reader.GetString(0),-30} {reader.GetString(1)}");

        Console.WriteLine($"\nрэперов: {Discover.CountRappers(db)}, битмейкеров: " +
                          db.Scalar<int>("SELECT COUNT(*) FROM artists WHERE producer_reason <> ''"));
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }

    /// <summary>Скачать кусок трека и убедиться, что декодер его читает.</summary>
    private static async Task<int> SliceTestAsync(string platform, string[] tracks)
    {
        var resolver = AudioResolvers.CreateAll()[platform];
        var temp = Path.Combine(Path.GetTempPath(), "crawler-slice");

        foreach (var track in tracks)
        {
            var isUrl = track.StartsWith("http", StringComparison.OrdinalIgnoreCase);
            var audio = await resolver.ResolveAsync(isUrl ? "" : track, isUrl ? track : "", CancellationToken.None);
            if (audio is null)
            {
                Console.WriteLine($"{track}: звука нет");
                continue;
            }

            var sw = Stopwatch.StartNew();
            var path = await AudioFetcher.DownloadSliceAsync(audio, temp, CancellationToken.None);
            var bytes = new FileInfo(path).Length;
            var download = sw.ElapsedMilliseconds;

            try
            {
                sw.Restart();
                var (samples, rate) = AudioDecoder.DecodeNative(path);
                Console.WriteLine($"{track}: {bytes / 1024} КБ за {download} мс, сегментов {audio.Urls.Count}, " +
                                  $"{samples.Length / (double)rate:F1} с звука, {rate} Гц, декод {sw.ElapsedMilliseconds} мс, " +
                                  $"пик {samples.Max(Math.Abs):F2}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{track}: {bytes / 1024} КБ, декодер упал: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                AudioFetcher.TryDelete(path);
            }
        }

        return 0;
    }
}
