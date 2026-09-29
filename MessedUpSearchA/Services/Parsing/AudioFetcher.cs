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

    /// <summary>
    /// Мегабайт из середины трека вместо всего файла: это 60 с на 128 кбит/с и 25 с на 320.
    /// Замер: 30-секундные превью дают то же качество, что полные треки,
    /// а трафика и декодирования в 4–6 раз меньше.
    /// </summary>
    public const long SliceBytes = 1024 * 1024;

    /// <summary>Откуда брать кусок: треть трека — уже не интро, но ещё не аутро.</summary>
    public const double SliceOffset = 0.35;

    /// <summary>
    /// Кусок трека вместо целого. Одна ссылка — Range из середины файла (декодер сам
    /// найдёт ближайший заголовок mp3-кадра). HLS — несколько сегментов подряд с той же доли.
    /// </summary>
    public static async Task<string> DownloadSliceAsync(ResolvedAudio audio, string tempDirectory, CancellationToken ct)
    {
        if (audio.Urls.Count == 0)
            throw new ArgumentException("нет ссылок на звук", nameof(audio));

        // Превью и так короткие — качаем как есть.
        if (audio.AudioKind == AudioKinds.Preview)
            return await DownloadAsync(audio, tempDirectory, ct);

        Directory.CreateDirectory(tempDirectory);
        var path = Path.Combine(tempDirectory, Guid.NewGuid().ToString("N") + ".mp3");

        try
        {
            await using var file = File.Create(path);

            if (audio.Urls.Count == 1)
            {
                await ParsingHttp.CopyRangeAsync(audio.Urls[0], file, SliceOffset, SliceBytes, ct);
                await file.DisposeAsync();

                // Кусок начинается с обрывка кадра — ищем настоящее начало, см. Mp3Sync.
                if (!Mp3Sync.TrimToFirstFrame(path))
                    throw new InvalidDataException("в куске трека не нашлось целых mp3-кадров");
            }
            else
            {
                // Сегменты HLS по ~10 с: берём подряд с той же доли, пока не наберём мегабайт.
                long left = SliceBytes;
                for (var i = (int)(audio.Urls.Count * SliceOffset); i < audio.Urls.Count && left > 0; i++)
                    left -= await ParsingHttp.CopyToAsync(audio.Urls[i], file, MaxBytes, ct);
            }

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
