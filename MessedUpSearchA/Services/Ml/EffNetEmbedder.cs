using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using MessedUpSearchA.Services.Localization;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace MessedUpSearchA.Services.Ml;

/// <summary>
/// Голова и всё, что к ней прилагается (effnet_head.bin, пишет ml/scripts/export_effnet.py):
/// окно Ханна и мел-матрица ровно из Essentia (C# не строит фильтры сам, а умножает —
/// так мел совпадает с эталоном), центр c0, проекция W (Half), центр c2.
///
/// Финальный вектор: unit( unit( unit(v - c0) · W ) - c2 ). Голова обучена на 652 андеграунд-
/// артистах «треки одного артиста рядом» и на линейке даёт +3–4 п.п. (ml/README.md).
///
/// Формат (little-endian): "MUSH", int версия = 1, int кадр (512), int полос (96), int размерность (1280),
/// float[кадр] окно, float[полос × (кадр/2+1)] мел, float[разм.] c0, Half[разм. × разм.] W, float[разм.] c2.
/// </summary>
public sealed class EffNetHead
{
    private const string Magic = "MUSH";

    public int FrameSize { get; private init; }
    public int Bands { get; private init; }
    public int Dimension { get; private init; }
    public float[] Window { get; private init; } = [];

    /// <summary>Мел по мощности: полосы × (кадр/2+1), построчно.</summary>
    public float[] Mel { get; private init; } = [];

    private float[] _c0 = [];
    private float[] _w = [];
    private float[] _c2 = [];

    public static EffNetHead Load(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path), Encoding.ASCII);
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != Magic || reader.ReadInt32() != 1)
            throw new InvalidDataException("effnet_head.bin: не тот формат");

        var frame = reader.ReadInt32();
        var bands = reader.ReadInt32();
        var dim = reader.ReadInt32();

        var head = new EffNetHead
        {
            FrameSize = frame, Bands = bands, Dimension = dim,
            Window = ReadFloats(reader, frame),
            Mel = ReadFloats(reader, bands * (frame / 2 + 1))
        };
        head._c0 = ReadFloats(reader, dim);
        head._w = new float[dim * dim];
        for (var i = 0; i < head._w.Length; i++)
            head._w[i] = (float)BitConverter.UInt16BitsToHalf(reader.ReadUInt16());
        head._c2 = ReadFloats(reader, dim);
        return head;
    }

    /// <summary>Сырой вектор EffNet (1280) -> финальный, в пространстве индексов v3.</summary>
    public float[] Project(float[] raw)
    {
        if (raw.Length != Dimension)
            throw new InvalidOperationException($"вектор из {raw.Length} чисел, голова ждёт {Dimension}");

        var u = new float[Dimension];
        for (var i = 0; i < Dimension; i++)
            u[i] = raw[i] - _c0[i];
        Normalize(u);

        // p = u · W, W хранится построчно (вход × выход): складываем строки с весами u[i].
        var p = new float[Dimension];
        var simd = Vector<float>.Count;
        for (var i = 0; i < Dimension; i++)
        {
            var weight = u[i];
            if (weight == 0f)
                continue;
            var row = i * Dimension;
            var j = 0;
            var scale = new Vector<float>(weight);
            for (; j <= Dimension - simd; j += simd)
                (new Vector<float>(p, j) + scale * new Vector<float>(_w, row + j)).CopyTo(p, j);
            for (; j < Dimension; j++)
                p[j] += weight * _w[row + j];
        }
        Normalize(p);

        for (var i = 0; i < Dimension; i++)
            p[i] -= _c2[i];
        Normalize(p);
        return p;
    }

    private static void Normalize(float[] v)
    {
        var norm = MathF.Sqrt(v.Sum(x => x * x));
        if (norm > 1e-9f)
            for (var i = 0; i < v.Length; i++)
                v[i] /= norm;
    }

    private static float[] ReadFloats(BinaryReader reader, int count)
    {
        var values = new float[count];
        for (var i = 0; i < count; i++)
            values[i] = reader.ReadSingle();
        return values;
    }
}

/// <summary>
/// Звук -> вектор Discogs-EffNet (Essentia, модель стилей Discogs 400, CC BY-NC-SA 4.0) -> голова.
///
/// Выбрана замером на всей галерее (682 артиста, ml/README.md): точнее MERT (top-5 среди 30
/// 52.7% против 41.7%) и в 11 раз легче — 18 МБ, поэтому едет внутри приложения.
///
/// Конвейер повторяет ml/scripts/embed_models.py (EffNet) и Essentia TensorflowInputMusiCNN:
///   16 кГц моно -> центральные 60 с -> кадры 512 с шагом 256 (Ханн) -> |FFT|² -> мел 96
///   -> log10(1 + 10000·x) -> патчи 128 кадров с шагом 62 -> ONNX 'embeddings' -> среднее -> голова.
/// Сверка с Python: ml/csharp-check (MlCheck vectors).
/// </summary>
public sealed class EffNetEmbedder : IDisposable
{
    public const int SampleRate = 16000;

    /// <summary>Пишется рядом с результатами. Сменится модель или голова — менять.</summary>
    public const string ModelVersion = "effnet-style-head1";

    private const int Hop = 256;
    private const int PatchFrames = 128;
    private const int PatchHop = 62;
    private const int MaxSeconds = 60;
    private const int MinSeconds = 3;

    private static readonly object SharedGate = new();
    private static EffNetEmbedder? _shared;

    private readonly InferenceSession _session;
    private readonly EffNetHead _head;

    public EffNetEmbedder(string modelPath, string headPath)
    {
        var options = new SessionOptions
        {
            // Сеть маленькая: бит считается за доли секунды; одно ядро оставляем интерфейсу.
            IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount - 1),
            InterOpNumThreads = 1,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
        };
        _session = new InferenceSession(modelPath, options);
        _head = EffNetHead.Load(headPath);
    }

    /// <summary>Общая сессия на приложение: файлы лежат в Assets/Models. Живёт до выхода.</summary>
    public static EffNetEmbedder GetShared()
    {
        lock (SharedGate)
            return _shared ??= new EffNetEmbedder(MlAssets.ModelPath, MlAssets.HeadPath);
    }

    public int Dimension => _head.Dimension;

    /// <summary>Файл -> финальный вектор (для поиска по индексам v3).</summary>
    public float[] EmbedFile(string path, CancellationToken ct = default) =>
        _head.Project(EmbedRaw(AudioDecoder.Decode(path, SampleRate, MaxSeconds), ct));

    public float[] Project(float[] raw) => _head.Project(raw);

    /// <summary>16 кГц моно -> сырой вектор EffNet (среднее по патчам).</summary>
    public float[] EmbedRaw(float[] samples, CancellationToken ct = default)
    {
        if (samples.Length < MinSeconds * SampleRate)
            throw new InvalidOperationException(Localizer.Instance["Analysis.TooShort"]);

        if (samples.Length > MaxSeconds * SampleRate)
        {
            var start = (samples.Length - MaxSeconds * SampleRate) / 2;
            samples = samples.AsSpan(start, MaxSeconds * SampleRate).ToArray();
        }

        var mel = MelFrames(samples);
        ct.ThrowIfCancellationRequested();

        var starts = new List<int>();
        for (var s = 0; s + PatchFrames <= mel.Count; s += PatchHop)
            starts.Add(s);

        var bands = _head.Bands;
        var batch = new float[starts.Count * PatchFrames * bands];
        for (var p = 0; p < starts.Count; p++)
            for (var f = 0; f < PatchFrames; f++)
                Array.Copy(mel[starts[p] + f], 0, batch, (p * PatchFrames + f) * bands, bands);

        var tensor = new DenseTensor<float>(batch, new[] { starts.Count, PatchFrames, bands });
        using var results = _session.Run([NamedOnnxValue.CreateFromTensor("melspectrogram", tensor)], ["embeddings"]);
        var output = results.First().AsTensor<float>();

        var dim = output.Dimensions[1];
        var mean = new float[dim];
        for (var p = 0; p < starts.Count; p++)
            for (var i = 0; i < dim; i++)
                mean[i] += output[p, i];
        for (var i = 0; i < dim; i++)
            mean[i] /= starts.Count;
        return mean;
    }

    /// <summary>
    /// Кадры как у Essentia FrameGenerator(startFromZero): кадр берётся, пока от его начала
    /// до конца звука больше половины кадра; хвост добивается нулями. Меньше 128 кадров —
    /// добиваем нулевыми кадрами (в Python — np.pad того же мела нулями).
    /// </summary>
    private List<float[]> MelFrames(float[] samples)
    {
        var size = _head.FrameSize;
        var bins = size / 2 + 1;
        var bands = _head.Bands;
        var frames = new List<float[]>();
        var re = new double[size];
        var im = new double[size];
        var power = new double[bins];

        for (var start = 0; samples.Length - start > Hop; start += Hop)
        {
            for (var i = 0; i < size; i++)
            {
                var k = start + i;
                re[i] = k < samples.Length ? samples[k] * _head.Window[i] : 0.0;
                im[i] = 0.0;
            }
            Fft(re, im);
            for (var b = 0; b < bins; b++)
                power[b] = re[b] * re[b] + im[b] * im[b];

            var bandsOut = new float[bands];
            for (var m = 0; m < bands; m++)
            {
                var sum = 0.0;
                var row = m * bins;
                for (var b = 0; b < bins; b++)
                    sum += _head.Mel[row + b] * power[b];
                bandsOut[m] = (float)Math.Log10(1 + 10000 * sum);
            }
            frames.Add(bandsOut);
        }

        while (frames.Count < PatchFrames)
            frames.Add(new float[bands]);
        return frames;
    }

    /// <summary>Комплексное БПФ по месту, radix-2 (размер — степень двойки).</summary>
    private static void Fft(double[] re, double[] im)
    {
        var n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
                j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        for (var len = 2; len <= n; len <<= 1)
        {
            var angle = -2 * Math.PI / len;
            var wr = Math.Cos(angle);
            var wi = Math.Sin(angle);
            for (var i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (var k = 0; k < len / 2; k++)
                {
                    var a = i + k;
                    var b = a + len / 2;
                    var tr = re[b] * cr - im[b] * ci;
                    var ti = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - tr;
                    im[b] = im[a] - ti;
                    re[a] += tr;
                    im[a] += ti;
                    var next = cr * wr - ci * wi;
                    ci = cr * wi + ci * wr;
                    cr = next;
                }
            }
        }
    }

    public void Dispose() => _session.Dispose();
}
