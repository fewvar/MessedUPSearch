using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MessedUpSearchA.Services.Ml;

/// <summary>
/// Поправка на «хабы» — артистов, похожих на любой бит. Без неё Yeat (153 трека в базе:
/// три похожих найдутся всегда) стоял в топ-5 у 66% битов линейки. Из оценки артиста
/// вычитается его средняя оценка по фоновым битам (248 type beat'ов, background_beats.bin):
/// остаётся «насколько этот бит похож на артиста сильнее, чем бит вообще».
///
/// Замер на линейке (ml/scripts/evaluate_beats.py): top-5 41.5% против 40.7% без поправки —
/// точность та же, а самый частый артист в топ-5 теперь у 32% битов, а не у 66%.
/// </summary>
public sealed class HubCorrection
{
    public const string FileName = "background_beats.bin";

    /// <summary>
    /// Шкала процентов для поправленной оценки. На линейке лучший артист для бита набирает
    /// 0.10–0.29 (10–90 перцентиль), поэтому 0 -> 0%, 0.30 -> 100%. Это подгонка под
    /// восприятие, как и старая шкала, а не вероятность.
    /// </summary>
    private const float PercentHigh = 0.30f;

    private static readonly ConcurrentDictionary<string, float> Cache = new();

    private readonly float[][] _background;

    private HubCorrection(float[][] background) => _background = background;

    public static HubCorrection? TryLoad(string path, float[] center)
    {
        if (!File.Exists(path))
            return null;

        using var reader = new BinaryReader(File.OpenRead(path));
        var count = reader.ReadInt32();
        var dimension = reader.ReadInt32();
        if (count <= 0 || dimension != center.Length)
            return null;

        var background = new float[count][];
        for (var i = 0; i < count; i++)
        {
            var raw = new float[dimension];
            for (var d = 0; d < dimension; d++)
                raw[d] = reader.ReadSingle();
            background[i] = MertEmbedder.Centered(raw, center);
        }

        return new HubCorrection(background);
    }

    /// <summary>
    /// Средняя оценка артиста по фону — тем же способом, что и по биту (top-N треков).
    /// Для артистов индекса не меняется, поэтому кэшируется; ключ включает число треков,
    /// чтобы артист из базы, у которого очередь дослушала новые треки, пересчитался.
    /// </summary>
    public float Bias(string key, IReadOnlyList<float[]> vectors, Func<float[], IReadOnlyList<float[]>, float> score) =>
        Cache.GetOrAdd($"{key}|{vectors.Count}", _ => _background.Average(b => score(b, vectors)));

    public static int ToPercent(float adjusted) =>
        (int)Math.Round(Math.Clamp(adjusted / PercentHigh, 0f, 1f) * 100);
}
