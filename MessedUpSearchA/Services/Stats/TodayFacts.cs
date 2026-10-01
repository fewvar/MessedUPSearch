using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MessedUpSearchA.Data;
using MessedUpSearchA.Models;
using MessedUpSearchA.Services.Localization;
using MessedUpSearchA.Services.Mail;

namespace MessedUpSearchA.Services.Stats;

/// <summary>Откуда известны продуктивные часы.</summary>
public enum HoursSource { None, Daw, Files }

/// <summary>Продуктивные часы: сколько битов/минут в DAW пришлось на каждый час суток.</summary>
public record ProductiveHours(HoursSource Source, double[] ByHour, int Start, int Length, double Share, int Basis)
{
    public bool Known => Source != HoursSource.None;
    public bool IsNow(DateTime now) => Known && Contains(now.Hour);
    public bool Contains(int hour) => (hour - Start + 24) % 24 < Length;
}

/// <summary>Всё, что знает «Сегодня»: факты считает приложение, нейросеть только формулирует.</summary>
public record TodayReport(
    ProductiveHours Hours,
    int NewReplies, IReadOnlyList<string> NewReplyFrom,
    int FollowUpsDue,
    IReadOnlyList<(string Beat, int Days)> Unsent, int UnsentTotal,
    int MailLeft,
    IReadOnlyList<string> StyleFacts, int StyleBasis, int StyleNeeded,
    string? Diversity,
    string? BestSendTime)
{
    /// <summary>
    /// Факты строками. Советы (часы, стиль, время отправки) — для карточки «Сейчас»; дела добавляются
    /// только для нейросети — на экране они в своей карточке, дублировать незачем.
    /// </summary>
    public List<string> FactLines(DateTime now, bool withDeals)
    {
        var t = Localizer.Instance;
        var lines = new List<string>();

        if (Hours.Known)
        {
            var window = $"{Hours.Start:00}–{(Hours.Start + Hours.Length) % 24:00}";
            lines.Add(Hours.IsNow(now)
                ? t.Format("Today.NowYourTime", now.ToString("HH:mm"), window, Hours.Share)
                : t.Format("Today.YourTime", window, Hours.Share));
        }

        if (Diversity is not null)
            lines.Add(Diversity);
        lines.AddRange(StyleFacts);
        if (BestSendTime is not null)
            lines.Add(BestSendTime);
        if (!withDeals)
            return lines;
        if (Unsent.Count > 0)
            lines.Add(t.Format("Today.UnsentOldest", Unsent[0].Beat, Unsent[0].Days, UnsentTotal));
        if (FollowUpsDue > 0)
            lines.Add(t.Format("Today.FollowUps", FollowUpsDue));
        if (NewReplies > 0)
            lines.Add(t.Format("Today.NewReplies", NewReplies));
        return lines;
    }
}

public static class TodayFacts
{
    /// <summary>Советы по стилю — не раньше, чем наберётся столько питчей с исходом (на меньшем — шум).</summary>
    public const int StyleMinimum = 15;

    public static TodayReport Build(AppDbContext db, AppSettings settings, DateTime now)
    {
        var t = Localizer.Instance;
        var outcomes = StatsService.Outcomes(db);

        // Новые ответы — с прошлого открытия «Сегодня».
        var lastSeen = settings.TodaySeenAt;
        var newReplies = db.IncomingReplies.Where(r => string.Compare(r.ReceivedAt, lastSeen) > 0).ToList();
        var names = db.Artists.ToDictionary(a => a.Id, a => a.Nickname);

        // Биты, которые никуда не уходили: ни письмом, ни отметкой «отправлен».
        var sentBeatIds = db.OutgoingMails.Where(m => m.Status == MailStatuses.Sent && m.BeatId != null)
            .Select(m => m.BeatId!.Value)
            .Concat(db.SentBeatsLog.Where(s => s.IsSent).Select(s => s.BeatId))
            .ToHashSet();
        var unsent = db.Beats.Where(b => b.Status == "POTENTIAL").ToList()
            .Where(b => !sentBeatIds.Contains(b.Id))
            .Select(b => (b.BeatName, Days: DateTime.TryParse(b.Added, out var d) ? (now.Date - d.Date).Days : 0))
            .OrderByDescending(b => b.Days)
            .ToList();

        var (styleFacts, basis) = Style(outcomes, t);

        return new TodayReport(
            ProductiveHoursFor(db, settings),
            newReplies.Count,
            newReplies.Where(r => r.ArtistId != null).Select(r => names.GetValueOrDefault(r.ArtistId!.Value, "?")).Distinct().ToList(),
            CrmRules.DueFollowUps(db, settings.FollowUpDays).Count,
            unsent.Take(3).ToList(), unsent.Count,
            Math.Max(0, settings.MailDailyLimit - db.OutgoingMails.Count(m =>
                m.Status == MailStatuses.Sent && m.SentAt.StartsWith(now.ToString("yyyy-MM-dd")))),
            styleFacts, basis, Math.Max(0, StyleMinimum - basis),
            Diversity(db, now, t),
            BestSendTime(outcomes, t));
    }

    /// <summary>
    /// Главный сигнал — сессии в DAW (только с согласия). Без них — время файлов битов: момент
    /// экспорта из DAW. Если файлы скопированы пачкой (Telegram, перенос с диска), их время — время
    /// копирования, а не создания: такие пачки не считаем.
    /// </summary>
    public static ProductiveHours ProductiveHoursFor(AppDbContext db, AppSettings settings)
    {
        var byHour = new double[24];

        if (settings.DawTracking)
        {
            var sessions = db.DawSessions.ToList();
            foreach (var s in sessions)
            {
                if (StatsService.Parse(s.StartedAt) is not { } start || StatsService.Parse(s.EndedAt) is not { } end)
                    continue;
                for (var at = start; at < end; at = at.AddMinutes(1))
                    byHour[at.Hour] += 1;
            }

            if (sessions.Count >= 5)
                return Window(HoursSource.Daw, byHour, sessions.Count);
        }

        var times = db.Beats.Select(b => b.FilePath).ToList()
            .Where(File.Exists)
            .Select(File.GetLastWriteTime)
            .OrderBy(d => d)
            .ToList();

        var genuine = DropBulkCopies(times);
        if (genuine.Count < 8)
            return new ProductiveHours(HoursSource.None, byHour, 0, 0, 0, genuine.Count);

        foreach (var d in genuine)
            byHour[d.Hour] += 1;
        return Window(HoursSource.Files, byHour, genuine.Count);
    }

    /// <summary>Пачка — 5+ файлов в пределах 10 минут: так файлы копируют, а не экспортируют.</summary>
    public static List<DateTime> DropBulkCopies(List<DateTime> sorted)
    {
        var result = new List<DateTime>();
        var i = 0;
        while (i < sorted.Count)
        {
            var j = i;
            while (j + 1 < sorted.Count && sorted[j + 1] - sorted[i] < TimeSpan.FromMinutes(10))
                j++;
            if (j - i + 1 < 5)
                result.AddRange(sorted.Skip(i).Take(j - i + 1));
            i = j + 1;
        }
        return result;
    }

    /// <summary>Лучшее окно в 3 часа (через полночь тоже) и какая доля работы на него пришлась.</summary>
    private static ProductiveHours Window(HoursSource source, double[] byHour, int basis)
    {
        const int length = 3;
        var total = byHour.Sum();
        var best = Enumerable.Range(0, 24)
            .Select(h => (Start: h, Sum: Enumerable.Range(0, length).Sum(k => byHour[(h + k) % 24])))
            .MaxBy(w => w.Sum);
        return new ProductiveHours(source, byHour, best.Start, length, total == 0 ? 0 : best.Sum / total, basis);
    }

    /// <summary>Что «заходит»: жанры бита с лучшим и худшим процентом ответов (от 3 питчей в жанре).</summary>
    private static (List<string> Facts, int Basis) Style(List<PitchOutcome> outcomes, Localizer t)
    {
        var withOutcome = outcomes.Where(o => o.Answered || (DateTime.Now - o.SentAt).TotalDays >= 7).ToList();
        if (withOutcome.Count < StyleMinimum)
            return ([], withOutcome.Count);

        var genres = withOutcome.GroupBy(o => StatsService.FirstTag(o.Beat?.AiTags))
            .Where(g => g.Key != "—" && g.Count() >= 3)
            .Select(g => (Genre: g.Key, Rate: g.Count(o => o.Answered) / (double)g.Count(), Count: g.Count()))
            .OrderByDescending(g => g.Rate)
            .ToList();

        var facts = new List<string>();
        if (genres.Count >= 2 && genres[0].Rate > genres[^1].Rate)
        {
            var (best, worst) = (genres[0], genres[^1]);
            facts.Add(worst.Rate > 0
                ? t.Format("Today.GenreTimes", best.Genre, best.Rate / worst.Rate, worst.Genre)
                : t.Format("Today.GenreRates", best.Genre, best.Rate, worst.Genre, worst.Rate));
        }

        var bpm = withOutcome.Where(o => o.Beat is { Bpm: > 0 })
            .GroupBy(o => o.Beat!.Bpm / 10 * 10)
            .Where(g => g.Count() >= 3)
            .Select(g => (From: g.Key, Rate: g.Count(o => o.Answered) / (double)g.Count()))
            .OrderByDescending(g => g.Rate)
            .FirstOrDefault();
        if (bpm.Rate > 0)
            facts.Add(t.Format("Today.BestBpm", bpm.From, bpm.From + 9, bpm.Rate));

        return (facts, withOutcome.Count);
    }

    /// <summary>«Последние 5 битов — plugg»: когда за 2 недели один жанр занял почти всё.</summary>
    private static string? Diversity(AppDbContext db, DateTime now, Localizer t)
    {
        var since = now.AddDays(-14).ToString("yyyy-MM-dd");
        var recent = db.Beats.Where(b => string.Compare(b.Added, since) >= 0).ToList()
            .Select(b => StatsService.FirstTag(b.AiTags)).Where(g => g != "—").ToList();
        if (recent.Count < 4)
            return null;

        var top = recent.GroupBy(g => g).MaxBy(g => g.Count())!;
        return top.Count() >= recent.Count * 0.6 ? t.Format("Today.SameGenre", top.Count(), recent.Count, top.Key) : null;
    }

    /// <summary>Когда отправлять: окно в 4 часа с лучшим процентом ответов (от 5 питчей).</summary>
    private static string? BestSendTime(List<PitchOutcome> outcomes, Localizer t)
    {
        var buckets = outcomes.GroupBy(o => o.SentAt.Hour / 4)
            .Where(g => g.Count() >= 5)
            .Select(g => (From: g.Key * 4, Rate: g.Count(o => o.Answered) / (double)g.Count()))
            .OrderByDescending(g => g.Rate)
            .ToList();
        if (buckets.Count < 2 || buckets[0].Rate <= buckets[^1].Rate)
            return null;
        return t.Format("Today.BestSendTime", buckets[0].From, buckets[0].From + 4, buckets[0].Rate);
    }
}
