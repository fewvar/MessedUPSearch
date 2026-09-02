using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace MessedUpSearchA.Services.Ml;

public class ArtistMatch
{
    public string Artist { get; init; } = string.Empty;

    /// <summary>Сырая косинусная близость, от -1 до 1. Для отладки, не для показа.</summary>
    public float Similarity { get; init; }

    /// <summary>Растянутая под восприятие оценка 0..100 — вот её показываем.</summary>
    public int Percent { get; init; }
}

/// <summary>
/// Бит -> список артистов, на которых он похож по звучанию.
///
/// Порядок действий ровно тот же, что в Python при построении базы, иначе числа
/// не сойдутся: декодируем в 24 кГц моно -> режем на окна по 10 секунд ->
/// прогоняем через MERT -> усредняем окна -> вычитаем центр базы -> нормируем ->
/// считаем косинусы с треками и агрегируем по артистам.
/// </summary>
public class BeatSimilarityService : IDisposable
{
    private const int WindowSamples = 10 * AudioDecoder.TargetSampleRate;
    private const int MaxWindows = 6;      // 6 окон = минута звука, хватает и не тормозит
    private const int TopTracksPerArtist = 3;

    private readonly InferenceSession _session;
    private readonly ArtistIndex _index;
    private readonly Dictionary<int, List<int>> _tracksByArtist;

    public BeatSimilarityService(string modelPath, string indexPath)
    {
        var options = new SessionOptions
        {
            // Считаем на всех ядрах: анализ одного бита — это секунды, и пользователь
            // их ждёт. Оставляем одно ядро системе, чтобы интерфейс не подтормаживал.
            IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount - 1),
            InterOpNumThreads = 1,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
        };

        _session = new InferenceSession(modelPath, options);
        _index = ArtistIndex.Load(indexPath);
        _tracksByArtist = _index.GroupByArtist();
    }

    public IReadOnlyList<string> KnownArtists => _index.ArtistNames;

    public Task<IReadOnlyList<ArtistMatch>> AnalyzeAsync(
        string audioPath, int top = 5, CancellationToken ct = default)
        => Task.Run(() => Analyze(audioPath, top, ct), ct);

    public IReadOnlyList<ArtistMatch> Analyze(string audioPath, int top = 5, CancellationToken ct = default)
    {
        var samples = AudioDecoder.Decode(audioPath);
        var windows = SliceWindows(samples);

        if (windows.Count == 0)
            throw new InvalidOperationException("бит короче трёх секунд — нечего анализировать");

        var embedding = Embed(windows, ct);
        return Rank(embedding, top);
    }

    /// <summary>
    /// Берём окна равномерно по всему биту. Только начало брать нельзя: там часто
    /// интро без бочки, и оно не показательно для звучания целиком.
    /// </summary>
    private static List<float[]> SliceWindows(float[] samples)
    {
        var windows = new List<float[]>();

        if (samples.Length < 3 * AudioDecoder.TargetSampleRate)
            return windows;

        if (samples.Length <= WindowSamples)
        {
            var padded = new float[WindowSamples];
            Array.Copy(samples, padded, samples.Length);
            windows.Add(padded);
            return windows;
        }

        var count = Math.Min(MaxWindows, samples.Length / WindowSamples);
        var step = count > 1 ? (double)(samples.Length - WindowSamples) / (count - 1) : 0;

        for (var i = 0; i < count; i++)
        {
            var window = new float[WindowSamples];
            Array.Copy(samples, (int)(i * step), window, 0, WindowSamples);
            windows.Add(window);
        }

        return windows;
    }

    private float[] Embed(List<float[]> windows, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Все окна одним прогоном: по одному это выходило вдвое дольше — модель
        // распараллеливает батч сама, а мы платили за запуск графа каждый раз.
        var batch = new float[windows.Count * WindowSamples];
        for (var w = 0; w < windows.Count; w++)
            Array.Copy(windows[w], 0, batch, w * WindowSamples, WindowSamples);

        var tensor = new DenseTensor<float>(batch, new[] { windows.Count, WindowSamples });
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("waveform", tensor)
        };

        using var results = _session.Run(inputs);
        var output = results.First().AsTensor<float>();

        var dimension = _index.Dimension;
        if (output.Dimensions.Length != 2 || output.Dimensions[1] != dimension)
        {
            throw new InvalidOperationException(
                $"модель вернула форму [{string.Join(",", output.Dimensions.ToArray())}], " +
                $"а индекс ждёт {dimension} чисел — модель и индекс из разных сборок");
        }

        var accumulated = new float[dimension];

        for (var w = 0; w < windows.Count; w++)
        {
            for (var i = 0; i < dimension; i++)
                accumulated[i] += output[w, i];
        }

        for (var i = 0; i < accumulated.Length; i++)
            accumulated[i] /= windows.Count;

        // Центрирование и нормировка — точно как при построении базы.
        for (var i = 0; i < accumulated.Length; i++)
            accumulated[i] -= _index.Center[i];

        var norm = 0f;
        for (var i = 0; i < accumulated.Length; i++)
            norm += accumulated[i] * accumulated[i];

        norm = MathF.Sqrt(norm);
        if (norm > 1e-9f)
        {
            for (var i = 0; i < accumulated.Length; i++)
                accumulated[i] /= norm;
        }

        return accumulated;
    }

    private IReadOnlyList<ArtistMatch> Rank(float[] embedding, int top)
    {
        var scores = new List<(string Artist, float Score)>(_tracksByArtist.Count);

        foreach (var (artistId, trackIds) in _tracksByArtist)
        {
            // Оценка артиста — среднее по трём его ближайшим трекам. Один трек
            // слишком случаен, среднее по всем размывает: у Yeat 153 трека.
            var best = new float[TopTracksPerArtist];
            Array.Fill(best, float.NegativeInfinity);

            foreach (var trackId in trackIds)
            {
                var similarity = Dot(embedding, _index.Vectors[trackId]);

                for (var slot = 0; slot < best.Length; slot++)
                {
                    if (similarity <= best[slot])
                        continue;

                    for (var shift = best.Length - 1; shift > slot; shift--)
                        best[shift] = best[shift - 1];

                    best[slot] = similarity;
                    break;
                }
            }

            var taken = best.Where(v => !float.IsNegativeInfinity(v)).ToArray();
            if (taken.Length > 0)
                scores.Add((_index.ArtistNames[artistId], taken.Average()));
        }

        return scores
            .OrderByDescending(s => s.Score)
            .Take(top)
            .Select(s => new ArtistMatch
            {
                Artist = s.Artist,
                Similarity = s.Score,
                Percent = ToPercent(s.Score)
            })
            .ToArray();
    }

    /// <summary>
    /// Косинус в проценты. Честного способа нет: даже у чужих друг другу треков
    /// близость редко уходит сильно ниже нуля, а у похожих редко превышает 0.6.
    /// Поэтому растягиваем рабочий диапазон 0.05..0.65 на всю шкалу — это подгонка
    /// под восприятие, а не физическая величина.
    /// </summary>
    private static int ToPercent(float similarity)
    {
        const float low = 0.05f;
        const float high = 0.65f;

        var normalized = (similarity - low) / (high - low);
        return (int)Math.Round(Math.Clamp(normalized, 0f, 1f) * 100);
    }

    private static float Dot(float[] a, float[] b)
    {
        var sum = 0f;
        for (var i = 0; i < a.Length; i++)
            sum += a[i] * b[i];
        return sum;
    }

    public void Dispose() => _session.Dispose();
}
