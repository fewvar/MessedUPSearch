using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MessedUpSearchA.Services.Ml;

namespace MessedUpSearchA.Services.Audio;

/// <summary>
/// Пики для пиксельной волны: файл делится на равные куски, у каждого берётся
/// максимум по модулю. Нормируем по самому громкому куску — иначе тихо сведённый
/// бит нарисуется плоской полоской.
///
/// Кэш в памяти по пути и времени изменения: переоткрыть тот же бит — мгновенно,
/// а перезаписанный файл посчитается заново.
/// </summary>
public static class WaveformService
{
    public const int Bars = 160;

    private static readonly ConcurrentDictionary<string, float[]> Cache = new();

    public static Task<float[]> GetPeaksAsync(string path, CancellationToken ct = default)
    {
        var key = $"{path}|{File.GetLastWriteTimeUtc(path).Ticks}";

        if (Cache.TryGetValue(key, out var cached))
            return Task.FromResult(cached);

        return Task.Run(() =>
        {
            var (samples, _) = AudioDecoder.DecodeNative(path);
            ct.ThrowIfCancellationRequested();

            var peaks = ToPeaks(samples, Bars);
            Cache[key] = peaks;
            return peaks;
        }, ct);
    }

    private static float[] ToPeaks(float[] samples, int bars)
    {
        var peaks = new float[bars];
        var chunk = Math.Max(1, samples.Length / bars);

        for (var bar = 0; bar < bars; bar++)
        {
            var start = bar * chunk;
            var end = Math.Min(samples.Length, start + chunk);

            var max = 0f;
            for (var i = start; i < end; i++)
                max = Math.Max(max, Math.Abs(samples[i]));

            peaks[bar] = max;
        }

        var loudest = 0f;
        foreach (var peak in peaks)
            loudest = Math.Max(loudest, peak);

        if (loudest > 1e-6f)
        {
            for (var bar = 0; bar < bars; bar++)
                peaks[bar] /= loudest;
        }

        return peaks;
    }
}
