using System.Globalization;
using System.Text.Json;
using MessedUpSearchA.Services.Ml;
using MessedUpSearchA.Services.Parsing;
using MessedUpSearchA.Services.Parsing.Sources;

namespace Crawler;

/// <summary>
/// Опыт «разный источник или модель не различает» (tmp/plans/bpm-v0.7.md, этап В): ориентиры
/// из references_v2.bin (превью Deezer) заводятся в ОТДЕЛЬНУЮ базу как артисты SoundCloud,
/// чтобы дальше пройти ровно тот же конвейер, что андеграунд: listen --slices-only ‖ separate_embed.py.
///
/// Профиль ищется по нику (и алиасу): точное совпадение username/permalink без пробелов и регистра,
/// из совпавших — с наибольшим числом подписчиков. Треки — самые прослушиваемые, 60–400 с.
/// </summary>
public static class RefSeed
{
    private const string Api = "https://api-v2.soundcloud.com";

    public static async Task RunAsync(CrawlDb db, string referencesPath, string aliasesPath, int tracks, CancellationToken ct)
    {
        var aliases = File.Exists(aliasesPath)
            ? File.ReadAllLines(aliasesPath).Select(l => l.Split('\t')).Where(p => p.Length == 2)
                .ToDictionary(p => p[0].Trim(), p => p[1].Trim(), StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>();

        var clientIds = new SoundCloudClientIdProvider();

        foreach (var artist in TargetIndex.Load(referencesPath).Artists)
        {
            ct.ThrowIfCancellationRequested();
            var name = artist.Nickname;
            var query = aliases.GetValueOrDefault(name, name).Replace('_', ' ');
            var needles = new[] { Normalize(name), Normalize(query) }.Distinct().ToArray();

            var users = await GetAsync(clientIds, $"/search/users?q={Uri.EscapeDataString(query)}&limit=20", ct);
            var user = users?.EnumerateArray()
                .Where(u => needles.Contains(Normalize(u.Str("username"))) || needles.Contains(Normalize(u.Str("permalink"))))
                .OrderByDescending(u => u.Int("followers_count"))
                .Cast<JsonElement?>()
                .FirstOrDefault();

            if (user is not { } profile)
            {
                Console.WriteLine($"{name,-20} профиль не найден");
                continue;
            }

            var userId = profile.NumberAsString("id");
            var list = await GetAsync(clientIds, $"/users/{userId}/tracks?limit=100", ct);
            var picked = list?.EnumerateArray()
                .Where(t => t.Int("duration") / 1000.0 is >= 60 and <= 400)
                .OrderByDescending(t => t.Int("playback_count"))
                .Take(tracks)
                .ToList() ?? [];

            var url = profile.Str("permalink_url");
            if (db.Scalar<long>("SELECT COUNT(*) FROM artists WHERE source_url = $u", ("$u", url)) > 0)
                continue;

            var artistId = db.Scalar<long>("""
                INSERT INTO artists (platform, source_id, source_url, nickname, followers, genre, found_at)
                VALUES ('SoundCloud', $sid, $url, $nick, $f, 'reference', $at) RETURNING id
                """,
                ("$sid", userId), ("$url", url), ("$nick", name), ("$f", profile.Int("followers_count")),
                ("$at", DateTime.Now.ToString("yyyy-MM-dd HH:mm")));

            foreach (var t in picked)
                db.Execute("INSERT INTO tracks (artist_id, source_track_id, url, title, plays) VALUES ($a, $sid, $url, $title, $p)",
                    ("$a", artistId), ("$sid", t.NumberAsString("id")), ("$url", t.Str("permalink_url")),
                    ("$title", t.Str("title")), ("$p", t.Int("playback_count")));

            Console.WriteLine($"{name,-20} {profile.Str("username"),-24} подписчиков {profile.Int("followers_count"),9}  треков {picked.Count}");
        }
    }

    private static async Task<JsonElement?> GetAsync(SoundCloudClientIdProvider clientIds, string path, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await Task.Delay(1200, ct);
            var clientId = await clientIds.GetAsync(ct);
            try
            {
                var body = await ParsingHttp.GetStringAsync($"{Api}{path}&client_id={clientId}", ct);
                using var doc = JsonDocument.Parse(body);
                return doc.RootElement.TryGetProperty("collection", out var c) ? c.Clone() : null;
            }
            catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            {
                await clientIds.RefreshAsync(ct);
            }
        }
        return null;
    }

    private static string Normalize(string text) =>
        new string(text.ToLower(CultureInfo.InvariantCulture).Where(char.IsLetterOrDigit).ToArray());
}
