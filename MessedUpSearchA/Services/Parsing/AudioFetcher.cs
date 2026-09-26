using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MessedUpSearchA.Services.Parsing;

/// <summary>
/// Качает звук во временный mp3. Файл живёт ровно до конца анализа: вызывающий
/// обязан удалить его в finally. На диске у пользователя музыка не копится.
/// </summary>
public static class AudioFetcher
{
    /// <summary>15 МБ mp3 на 128 кбит/с — это почти восемь минут. Длиннее — миксы и подкасты.</summary>
    public const long MaxBytes = 15 * 1024 * 1024;

    public static async Task<string> DownloadAsync(ResolvedAudio audio, string tempDirectory, CancellationToken ct)
    {
        if (audio.Urls.Count == 0)
            throw new ArgumentException("нет ссылок на звук", nameof(audio));

        Directory.CreateDirectory(tempDirectory);
        var path = Path.Combine(tempDirectory, Guid.NewGuid().ToString("N") + ".mp3");

        try
        {
            await using var file = File.Create(path);
            long left = MaxBytes;

            // HLS-сегменты — это куски одного mp3 потока, склеиваются как есть.
            foreach (var url in audio.Urls)
                left -= await ParsingHttp.CopyToAsync(url, file, left, ct);

            return path;
        }
        catch
        {
            TryDelete(path);
            throw;
        }
    }

    public static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Не удалилось сейчас — подметём при следующем запуске очереди.
        }
    }
}
