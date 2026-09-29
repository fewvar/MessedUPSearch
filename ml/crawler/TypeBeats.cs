using System.Globalization;
using System.Text;
using System.Text.Json;
using MessedUpSearchA.Services.Ml;
using MessedUpSearchA.Services.Parsing;
using MessedUpSearchA.Services.Parsing.Sources;

namespace Crawler;

/// <summary>
/// Линейка «голый бит -> артист» (шаг 0 плана качества): по каждому артисту индекса
/// друга ищем на SoundCloud «<ник> type beat» и сохраняем куски этих битов. Это
/// голые биты, уже подписанные артистом, — ровно то, что приходит в продукт на вход.
///
/// Метки шумные: битмейкеры ставят популярные ники ради поиска. Поэтому берём только
/// биты, где в названии один артист из индекса, и не больше двух от одного битмейкера.
/// Куски лежат в ml/data/typebeats (в .gitignore), манифест — typebeats.csv рядом.
/// </summary>
public static class TypeBeats
{
    private const string Api = "https://api-v2.soundcloud.com";

    public static async Task RunAsync(string indexPath, string aliasesPath, string outDir, int perArtist, CancellationToken ct)
    {
        var names = ArtistIndex.LoadNamesOnly(indexPath);
        var aliases = File.Exists(aliasesPath)
            ? File.ReadAllLines(aliasesPath).Select(l => l.Split('\t')).Where(p => p.Length == 2)
                .ToDictionary(p => p[0].Trim(), p => p[1].Trim(), StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>();

        // Как артиста ищут в названиях: основной ник и алиас, без пробелов и регистра.
        var searchName = names.ToDictionary(n => n, n => aliases.GetValueOrDefault(n, n));
        var needles = names.ToDictionary(n => n, n => new[] { Normalize(n), Normalize(searchName[n]) }.Distinct().ToArray());

        var clientIds = new SoundCloudClientIdProvider();
        var soundCloud = new SoundCloudSource();
        var temp = Path.Combine(Path.GetTempPath(), "crawler-typebeats");
        Directory.CreateDirectory(outDir);

        var manifestPath = Path.Combine(outDir, "typebeats.csv");
        var done = File.Exists(manifestPath)
            ? File.ReadAllLines(manifestPath).Skip(1).Select(l => l.Split(',')[1]).ToHashSet()
            : new HashSet<string>();

        await using var manifest = new StreamWriter(manifestPath, append: done.Count > 0, Encoding.UTF8);
        if (done.Count == 0)
            await manifest.WriteLineAsync("artist,track_id,uploader,title,path");

        foreach (var artist in names)
        {
            ct.ThrowIfCancellationRequested();

            var have = File.Exists(manifestPath) ? CountFor(outDir, artist) : 0;
            if (have >= perArtist)
                continue;

            var query = $"{searchName[artist]} type beat";
            var candidates = await SearchAsync(clientIds, query, ct);

            var others = needles.Where(p => p.Key != artist).SelectMany(p => p.Value).Where(n => n.Length >= 4).ToArray();
            var perUploader = new Dictionary<string, int>();
            var taken = have;

            foreach (var track in candidates)
            {
                if (taken >= perArtist)
                    break;

                var title = Normalize(track.Title);
                if (!title.Contains("typebeat") || !needles[artist].Any(title.Contains) || others.Any(title.Contains))
                    continue;
                if (track.Seconds is < 60 or > 400 || done.Contains(track.Id))
                    continue;
                if (perUploader.GetValueOrDefault(track.Uploader) >= 2)
                    continue;

                try
                {
                    var audio = await soundCloud.ResolveAsync(track.Id, "", ct);
                    if (audio is null)
                        continue;

                    var slice = await AudioFetcher.DownloadSliceAsync(audio, temp, ct);
                    var folder = Path.Combine(outDir, Safe(artist));
                    Directory.CreateDirectory(folder);
                    var target = Path.Combine(folder, track.Id + ".mp3");
                    File.Move(slice, target, overwrite: true);

                    await manifest.WriteLineAsync(string.Join(',', Csv(artist), track.Id, Csv(track.Uploader), Csv(track.Title), Csv(target)));
                    await manifest.FlushAsync(ct);

                    done.Add(track.Id);
                    perUploader[track.Uploader] = perUploader.GetValueOrDefault(track.Uploader) + 1;
                    taken++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Console.WriteLine($"   {artist}: {track.Title}: {ex.Message}");
                }
            }

            Console.WriteLine($"{artist,-20} {taken}/{perArtist}  (кандидатов {candidates.Count})");
        }
    }

    private sealed record Found(string Id, string Title, string Uploader, double Seconds);

    private static async Task<List<Found>> SearchAsync(SoundCloudClientIdProvider clientIds, string query, CancellationToken ct)
    {
        var found = new List<Found>();

        for (var page = 0; page < 3; page++)
        {
            await Task.Delay(1200, ct);   // тот же темп, что у парсера: ~50 запросов в минуту

            var clientId = await clientIds.GetAsync(ct);
            var url = $"{Api}/search/tracks?q={Uri.EscapeDataString(query)}&limit=50&offset={page * 50}&client_id={clientId}";

            string body;
            try
            {
                body = await ParsingHttp.GetStringAsync(url, ct);
            }
            catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            {
                await clientIds.RefreshAsync(ct);
                continue;
            }

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("collection", out var collection) || collection.GetArrayLength() == 0)
                break;

            foreach (var track in collection.EnumerateArray())
            {
                var user = track.Obj("user");
                found.Add(new Found(track.NumberAsString("id"), track.Str("title"),
                    user?.Str("username") ?? "", track.Int("duration") / 1000.0));
            }
        }

        return found;
    }

    private static int CountFor(string outDir, string artist)
    {
        var folder = Path.Combine(outDir, Safe(artist));
        return Directory.Exists(folder) ? Directory.GetFiles(folder, "*.mp3").Length : 0;
    }

    private static string Normalize(string text) =>
        new string(text.ToLower(CultureInfo.InvariantCulture).Where(char.IsLetterOrDigit).ToArray());

    private static string Safe(string text) =>
        new string(text.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

    private static string Csv(string text) => "\"" + text.Replace("\"", "\"\"") + "\"";
}
