using System.Diagnostics;
using System.Globalization;
using MessedUpSearchA.Services.Ml;
using MessedUpSearchA.Services.Parsing;
using MessedUpSearchA.Services.Parsing.Sources;

if (args.Length >= 1 && args[0] == "online")
    return await Online(args[1..]);

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
