using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MessedUpSearchA.Services.Ml;

/// <summary>
/// Большой индекс доступных артистов (artists_index_v4.bin, ~7 МБ): где лежит,
/// откуда качается, как не перечитывать его при каждом анализе.
///
/// Собирается краулером из ml/crawler у разработчика и едет ассетом релиза: обновляется
/// чаще приложения. Индекса нет и скачать не вышло — не беда: остаётся «звучит как».
/// </summary>
public static class IndexStore
{
    public const string FileName = "artists_index_v4.bin";

    private static readonly string DownloadUrl =
        $"https://github.com/fewvar/MessedUPSearch/releases/download/v1.0.1/{FileName}";

    private static readonly object Gate = new();
    private static TargetIndex? _cached;
    private static DateTime _cachedStamp;

    public static string IndexPath => Path.Combine(MlAssets.DownloadDirectory, FileName);

    public static bool IsReady() => File.Exists(IndexPath);

    /// <summary>Загруженный индекс или null. Перечитывается, только если файл поменялся.</summary>
    public static TargetIndex? TryLoad()
    {
        if (!IsReady())
            return null;

        lock (Gate)
        {
            var stamp = File.GetLastWriteTimeUtc(IndexPath);
            if (_cached is not null && stamp == _cachedStamp)
                return _cached;

            try
            {
                _cached = TargetIndex.Load(IndexPath);
                _cachedStamp = stamp;
                return _cached;
            }
            catch (Exception ex)
            {
                AppLog.Write($"индекс артистов: не читается — {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>Качает индекс. Файл проверяется чтением до того, как встанет на место.</summary>
    public static async Task DownloadAsync(CancellationToken ct = default)
    {
        var temp = IndexPath + ".part";

        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        using (var response = await client.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var target = File.Create(temp);
            await source.CopyToAsync(target, ct);
        }

        try
        {
            TargetIndex.Load(temp);
        }
        catch
        {
            File.Delete(temp);
            throw;
        }

        File.Move(temp, IndexPath, overwrite: true);
    }
}
