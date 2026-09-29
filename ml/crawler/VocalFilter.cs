using System.Text.Json;

namespace Crawler;

/// <summary>
/// «Есть голос или чистый бит» по вектору трека: логистическая регрессия, обученная
/// ml/scripts/train_vocal.py (веса в ml/data/vocal_lr.json). Артист остаётся в индексе,
/// если средняя вероятность голоса по его трекам не ниже порога: битмейкеров, фонк
/// и электронику по названиям треков не поймать, а по звуку — видно.
/// </summary>
public sealed class VocalFilter
{
    private readonly float[] _weights;
    private readonly float _bias;

    public double Threshold { get; }

    private VocalFilter(float[] weights, float bias, double threshold)
    {
        _weights = weights;
        _bias = bias;
        Threshold = threshold;
    }

    public static VocalFilter? TryLoad(string path)
    {
        if (!File.Exists(path))
            return null;

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        var weights = root.GetProperty("w").EnumerateArray().Select(v => (float)v.GetDouble()).ToArray();
        return new VocalFilter(weights, (float)root.GetProperty("b").GetDouble(), root.GetProperty("threshold").GetDouble());
    }

    /// <summary>Вероятность голоса для уже центрированного и нормированного вектора.</summary>
    public double Probability(float[] vector)
    {
        var z = _bias;
        for (var i = 0; i < vector.Length; i++)
            z += _weights[i] * vector[i];
        return 1 / (1 + Math.Exp(-z));
    }

    public bool IsRapper(IEnumerable<float[]> vectors) =>
        vectors.Select(Probability).DefaultIfEmpty(0).Average() >= Threshold;
}
