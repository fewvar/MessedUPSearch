using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MessedUpSearchA.Data;

namespace MessedUpSearchA.Services.Ml;

/// <summary>
/// Где лежит модель и как её получить.
///
/// Файл весит 208 МБ и в репозиторий не помещается — GitHub не принимает файлы
/// больше 100 МБ. Поэтому он висит отдельным ассетом на релизе, а приложение
/// качает его один раз при первом анализе и кладёт рядом с базой.
/// Сжать не вышло: квантизация роняет качество на треть (см. ml/scripts).
/// </summary>
public static class ModelStore
{
    public const string ModelFileName = "mert_layer5.onnx";
    public const string IndexFileName = "artist_index.bin";

    private const long ExpectedModelBytes = 207_503_434;

    private static readonly string DownloadUrl =
        $"https://github.com/fewvar/MessedUPSearch/releases/download/v0.6/{ModelFileName}";

    public static string ModelDirectory
    {
        get
        {
            var dir = Path.Combine(AppPaths.DataDir, "models");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string ModelPath => Path.Combine(ModelDirectory, ModelFileName);

    /// <summary>Индекс маленький и едет вместе с приложением.</summary>
    public static string IndexPath =>
        Path.Combine(AppContext.BaseDirectory, "Assets", "Models", IndexFileName);

    public static bool IsModelReady()
    {
        var file = new FileInfo(ModelPath);
        // Оборванная закачка оставляет файл нужного имени, но не того размера —
        // такой считаем отсутствующим, иначе ONNX Runtime упадёт при загрузке.
        return file.Exists && file.Length == ExpectedModelBytes;
    }

    public static bool IsIndexReady() => File.Exists(IndexPath);

    /// <summary>Фоновые биты для поправки на хабы — тоже едут с приложением.</summary>
    public static string BackgroundPath =>
        Path.Combine(AppContext.BaseDirectory, "Assets", "Models", HubCorrection.FileName);

    /// <summary>
    /// Качает модель, сообщая прогресс в долях. Файл пишется во временный и
    /// переименовывается только целиком — так оборванная закачка не выглядит удачной.
    /// </summary>
    public static async Task DownloadModelAsync(
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (IsModelReady())
            return;

        var target = ModelPath;
        var temp = target + ".part";

        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };

        using (var response = await client.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? ExpectedModelBytes;
            var buffer = new byte[81920];
            long received = 0;

            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var destination = File.Create(temp);

            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), ct);
                received += read;
                progress?.Report(Math.Clamp((double)received / total, 0, 1));
            }
        }

        var downloaded = new FileInfo(temp).Length;
        if (downloaded != ExpectedModelBytes)
        {
            File.Delete(temp);
            throw new InvalidDataException(
                $"скачалось {downloaded} байт вместо {ExpectedModelBytes} — файл повреждён");
        }

        File.Move(temp, target, overwrite: true);
    }
}
