using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MessedUpSearchA.Services.Localization;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace MessedUpSearchA.Services.Ml;

/// <summary>
/// Звук -> сырой вектор MERT (слой 5, среднее по окнам). Больше ничего: ни центра,
/// ни сравнения — это общий кирпич и для анализа бита, и для треков из сети.
///
/// Сессия ONNX тяжёлая (модель 208 МБ, в памяти больше), поэтому на приложение
/// она одна — <see cref="GetShared"/>. Run у InferenceSession потокобезопасен.
/// </summary>
public sealed class MertEmbedder : IDisposable
{
    /// <summary>Пишется рядом с каждым вектором в базе. Сменится модель или слой — менять и это.</summary>
    public const string ModelVersion = "mert95m-l5";

    /// <summary>MERT ждёт 24 кГц. Из приложения убран (29.09.2026): тут он нужен только для фильтра голоса.</summary>
    public const int SampleRate = 24000;

    /// <summary>Модель, которую раньше качало приложение, — лежит там же.</summary>
    public static string DefaultModelPath =>
        System.IO.Path.Combine(MessedUpSearchA.Data.AppPaths.DataDir, "models", "mert_layer5.onnx");

    private const int WindowSamples = 10 * SampleRate;
    private const int MaxWindows = 6;      // 6 окон = минута звука, хватает и не тормозит

    private static readonly object SharedGate = new();
    private static MertEmbedder? _shared;

    private readonly InferenceSession _session;

    public MertEmbedder(string modelPath)
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
    }

    /// <summary>Общая сессия на приложение. Живёт до выхода, не диспозится.</summary>
    public static MertEmbedder GetShared(string modelPath)
    {
        lock (SharedGate)
            return _shared ??= new MertEmbedder(modelPath);
    }

    public float[] EmbedRaw(float[] samples, CancellationToken ct = default)
    {
        var windows = SliceWindows(samples);
        if (windows.Count == 0)
            throw new InvalidOperationException(Localizer.Instance["Analysis.TooShort"]);

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

        if (output.Dimensions.Length != 2)
            throw new InvalidOperationException(
                $"модель вернула форму [{string.Join(",", output.Dimensions.ToArray())}] вместо [окна, размерность]");

        var dimension = output.Dimensions[1];
        var accumulated = new float[dimension];

        for (var w = 0; w < windows.Count; w++)
        {
            for (var i = 0; i < dimension; i++)
                accumulated[i] += output[w, i];
        }

        for (var i = 0; i < dimension; i++)
            accumulated[i] /= windows.Count;

        return accumulated;
    }

    /// <summary>
    /// Вычесть центр базы и нормировать — точно как при построении индекса в Python.
    /// Без центра все векторы MERT смотрят примерно в одну сторону и косинус у всех под единицу.
    /// </summary>
    public static float[] Centered(float[] raw, float[] center)
    {
        if (raw.Length != center.Length)
            throw new InvalidOperationException(
                $"вектор из {raw.Length} чисел, а центр из {center.Length} — модель и индекс из разных сборок");

        var result = new float[raw.Length];
        var norm = 0f;

        for (var i = 0; i < raw.Length; i++)
        {
            result[i] = raw[i] - center[i];
            norm += result[i] * result[i];
        }

        norm = MathF.Sqrt(norm);
        if (norm > 1e-9f)
        {
            for (var i = 0; i < result.Length; i++)
                result[i] /= norm;
        }

        return result;
    }

    public static byte[] ToBytes(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    public static float[] FromBytes(byte[] bytes)
    {
        var vector = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, vector, 0, vector.Length * sizeof(float));
        return vector;
    }

    /// <summary>
    /// Берём окна равномерно по всему звуку. Только начало брать нельзя: там часто
    /// интро без бочки, и оно не показательно для звучания целиком.
    /// </summary>
    private static List<float[]> SliceWindows(float[] samples)
    {
        var windows = new List<float[]>();

        if (samples.Length < 3 * SampleRate)
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

    public void Dispose() => _session.Dispose();
}
