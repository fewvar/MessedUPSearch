using System.Diagnostics;
using System.Globalization;
using MessedUpSearchA.Services.Ml;
using MessedUpSearchA.Services.Parsing;
using MessedUpSearchA.Services.Parsing.Sources;

if (args.Length >= 1 && args[0] == "online")
    return await Online(args[1..]);

if (args.Length >= 2 && args[0] == "play")
    return await Play(args[1]);

// Где уходит время на один файл: декодирование с ресемплингом или сама модель.
if (args.Length >= 3 && args[0] == "time")
{
    var sw = Stopwatch.StartNew();
    using var timed = new MertEmbedder(args[1]);
    Console.WriteLine($"LOAD\t{sw.ElapsedMilliseconds} мс");

    foreach (var path in args.Skip(2))
    {
        sw.Restart();
        AudioDecoder.DecodeNative(path);
        var native = sw.ElapsedMilliseconds;

        sw.Restart();
        var samples = AudioDecoder.Decode(path);
        var decode = sw.ElapsedMilliseconds;

        sw.Restart();
        timed.EmbedRaw(samples);
        Console.WriteLine($"FILE\t{Path.GetFileName(path)}\t{samples.Length / AudioDecoder.TargetSampleRate} с звука\t" +
                          $"без ресемплинга {native} мс\tс ресемплингом {decode} мс\tMERT {sw.ElapsedMilliseconds} мс");
    }

    return 0;
}

// Печатает эмбеддинг и топ-5 для каждого переданного файла — в том же формате,
// в каком их печатает Python-эталон, чтобы сравнение было построчным.
if (args.Length < 3)
{
    Console.Error.WriteLine("использование: MlCheck <модель.onnx> <индекс.bin> <аудио...>");
    Console.Error.WriteLine("           или: MlCheck online <модель.onnx> <индекс.bin> <папка> [треков на артиста] [алиасы.tsv]");
    return 1;
}

using var service = new BeatSimilarityService(args[0], args[1]);

foreach (var path in args.Skip(2))
{
    try
    {
        var samples = AudioDecoder.Decode(path);
        var matches = service.Analyze(path);

        Console.WriteLine($"FILE\t{Path.GetFileName(path)}");
        Console.WriteLine($"SAMPLES\t{samples.Length}");

        foreach (var match in matches)
        {
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "MATCH\t{0}\t{1:F6}\t{2}", match.Artist, match.Similarity, match.Percent));
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"ERROR\t{Path.GetFileName(path)}\t{ex.Message}");
    }
}

return 0;

// Замер «база из сети против базы друга»: для каждого артиста индекса берёт превью
// с Deezer, считает сырые векторы боевым кодом и пишет их в папку:
//   vectors.f32 — N×768 float32 подряд, meta.tsv — артист, трек, тип, секунды.
// Сравнение считает ml/scripts/evaluate_online.py.
static async Task<int> Online(string[] args)
{
    if (args.Length < 3)
    {
        Console.Error.WriteLine("использование: MlCheck online <модель.onnx> <индекс.bin> <папка> [треков на артиста] [алиасы.tsv]");
        return 1;
    }

    var (modelPath, indexPath, outDir) = (args[0], args[1], args[2]);
    var perArtist = args.Length > 3 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 10;

    // Папки в архиве друга названы как попало («2Holis»), Deezer таких не знает.
    var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    if (args.Length > 4)
    {
        foreach (var line in File.ReadAllLines(args[4]))
        {
            var parts = line.Split('\t');
            if (parts.Length == 2)
                aliases[parts[0].Trim()] = parts[1].Trim();
        }
    }

    Directory.CreateDirectory(outDir);
    var temp = Path.Combine(outDir, "tmp");

    var names = ArtistIndex.LoadNamesOnly(indexPath);
    using var embedder = new MertEmbedder(modelPath);
    var deezer = new DeezerSource();

    await using var vectors = File.Create(Path.Combine(outDir, "vectors.f32"));
    await using var meta = new StreamWriter(Path.Combine(outDir, "meta.tsv"));

    var embedTime = new Stopwatch();
    var embedded = 0;

    foreach (var name in names)
    {
        var query = aliases.GetValueOrDefault(name, name);
        IReadOnlyList<TrackInfo> tracks;

        try
        {
            tracks = await deezer.FindArtistTracksAsync(query, perArtist, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"MISS\t{name}\t{ex.Message}");
            continue;
        }

        if (tracks.Count == 0)
        {
            Console.WriteLine($"MISS\t{name}\tна Deezer не нашёлся по имени «{query}»");
            continue;
        }

        var got = 0;

        foreach (var track in tracks)
        {
            string? path = null;
            try
            {
                var audio = await deezer.ResolveAsync(track.SourceId, track.Url, CancellationToken.None);
                if (audio is null)
                    continue;

                path = await AudioFetcher.DownloadAsync(audio, temp, CancellationToken.None);
                var samples = AudioDecoder.Decode(path);

                embedTime.Start();
                var raw = embedder.EmbedRaw(samples);
                embedTime.Stop();

                await vectors.WriteAsync(MertEmbedder.ToBytes(raw));
                await meta.WriteLineAsync(string.Join('\t', name, track.Title.Replace('\t', ' '),
                    audio.AudioKind, (samples.Length / (double)AudioDecoder.TargetSampleRate).ToString("F1", CultureInfo.InvariantCulture)));

                embedded++;
                got++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR\t{name}\t{track.Title}\t{ex.Message}");
            }
            finally
            {
                if (path is not null)
                    AudioFetcher.TryDelete(path);
            }
        }

        Console.WriteLine($"OK\t{name}\t{got}");
    }

    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
        "DONE\t{0} треков\tMERT {1:F2} с на трек", embedded, embedded == 0 ? 0 : embedTime.Elapsed.TotalSeconds / embedded));
    return 0;
}

// Спайк плеера: проверка, что SoundFlow с нативом miniaudio живёт на этой машине.
static async Task<int> Play(string path)
{
    using var engine = new SoundFlow.Backends.MiniAudio.MiniAudioEngine();
    var format = SoundFlow.Structs.AudioFormat.DvdHq;
    using var device = engine.InitializePlaybackDevice(null, format);

    await using var file = File.OpenRead(path);
    using var provider = new SoundFlow.Providers.StreamDataProvider(engine, format, file);
    using var player = new SoundFlow.Components.SoundPlayer(engine, format, provider);

    var ended = new TaskCompletionSource();
    player.PlaybackEnded += (_, _) => ended.TrySetResult();

    device.MasterMixer.AddComponent(player);
    player.Volume = 0.3f;
    device.Start();
    player.Play();

    void Report(string what) =>
        Console.WriteLine($"{what,-12} state={player.State} time={player.Time:F2} dur={player.Duration:F2} vol={player.Volume}");

    await Task.Delay(1500); Report("играет");
    player.Seek(TimeSpan.FromSeconds(60), SeekOrigin.Begin);
    await Task.Delay(300); Report("seek 60");
    player.Volume = 0.3f;
    player.Pause(); var paused = player.Time;
    await Task.Delay(800); Report("пауза");
    Console.WriteLine($"пауза держит позицию: {Math.Abs(player.Time - paused) < 0.05}");
    player.Play();
    player.Seek(TimeSpan.FromSeconds(player.Duration - 2), SeekOrigin.Begin);
    var finished = await Task.WhenAny(ended.Task, Task.Delay(6000)) == ended.Task;
    Report("конец");
    Console.WriteLine($"PlaybackEnded пришёл: {finished}");

    device.Stop();
    return 0;
}
