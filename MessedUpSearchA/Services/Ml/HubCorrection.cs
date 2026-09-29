using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MessedUpSearchA.Services.Ml;

/// <summary>
/// Поправка на «хабы» — артистов, похожих на любой бит. Без неё один артист с сотней треков
/// (три похожих найдутся всегда) стоит в топ-5 у большинства битов. Из оценки артиста
/// вычитается его средняя оценка по фоновым битам (877 type beat'ов, background_v3.bin):
/// остаётся «насколько этот бит похож на артиста сильнее, чем бит вообще».
///
/// Формат: int битов, int размерность, затем биты × размерность × Half — векторы уже финальные
/// (EffNet + голова, ml/scripts/export_effnet.py).
/// </summary>
public sealed class HubCorrection
{
    public const string FileName = "background_v3.bin";

    /// <summary>
    /// Шкалы процентов для поправленной оценки (EffNet + голова, 29.09.2026). Перцентили по 877 type
    /// beat'ам (10/50/90): ориентиры — 1-е место 0.14/0.25/0.38, 3-е 0.07/0.17/0.28; андеграунд 652 —
    /// 1-е 0.26/0.36/0.51, 10-е 0.18/0.25/0.38. Голова училась на андеграунде и собирает его плотнее,
    /// поэтому шкалы две: в обоих списках типичное первое место ~65%, последнее ~40–45%.
    /// Это подгонка под восприятие, а не вероятность.
    /// </summary>
    private const float ReferenceLow = -0.01f;
    private const float ReferenceHigh = 0.39f;
    private const float TargetLow = 0.086f;
    private const float TargetHigh = 0.506f;

    private static readonly ConcurrentDictionary<string, float> Cache = new();

    private readonly float[][] _background;

    private HubCorrection(float[][] background) => _background = background;

    public static HubCorrection? TryLoad(string path, int dimension)
    {
        if (!File.Exists(path))
            return null;

        using var reader = new BinaryReader(File.OpenRead(path));
        var count = reader.ReadInt32();
        var fileDimension = reader.ReadInt32();
        if (count <= 0 || fileDimension != dimension)
            return null;

        var background = new float[count][];
        for (var i = 0; i < count; i++)
        {
            var vector = new float[dimension];
            for (var d = 0; d < dimension; d++)
                vector[d] = (float)BitConverter.UInt16BitsToHalf(reader.ReadUInt16());
            background[i] = vector;
        }

        return new HubCorrection(background);
    }

    /// <summary>
    /// Средняя оценка артиста по фону — тем же способом, что и по биту (top-N треков).
    /// Для артистов индекса не меняется, поэтому кэшируется.
    /// </summary>
    public float Bias(string key, IReadOnlyList<float[]> vectors, Func<float[], IReadOnlyList<float[]>, float> score) =>
        Cache.GetOrAdd($"{key}|{vectors.Count}", _ => _background.Average(b => score(b, vectors)));

    public static int ToPercent(float adjusted, bool reference)
    {
        var (low, high) = reference ? (ReferenceLow, ReferenceHigh) : (TargetLow, TargetHigh);
        return (int)Math.Round(Math.Clamp((adjusted - low) / (high - low), 0f, 1f) * 100);
    }
}
