using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MessedUpSearchA.Data;
using MessedUpSearchA.Models;

namespace MessedUpSearchA.Services.Stats;

/// <summary>Строка статистики: «rage — 4 из 10 ответили».</summary>
public record StatRow(string Label, int Hits, int Total)
{
    public double Rate => Total == 0 ? 0 : (double)Hits / Total;
    public string ValueText => Total == 0 ? "—" : string.Format(Localization.Localizer.Instance.Culture, "{0:P0} · {1}/{2}", Rate, Hits, Total);
}

/// <summary>Питч и его судьба: ответили ли и через сколько.</summary>
public record PitchOutcome(OutgoingMail Pitch, DateTime SentAt, Artist? Artist, Beat? Beat, string Template, TimeSpan? ReplyAfter)
{
    public bool Answered => ReplyAfter is not null;
}

public record StatsReport(
    int Pitches, int FollowUps, int Answered, int Artists, TimeSpan? MedianReply,
    int Sold, int Free, int BeatsAdded,
    IReadOnlyList<StatRow> ByTemplate,
    IReadOnlyList<StatRow> ByBeatGenre,
    IReadOnlyList<StatRow> ByWeekday,
    IReadOnlyList<StatRow> ByHours,
    IReadOnlyList<StatRow> ReplyTime,
    IReadOnlyList<StatRow> TopBeats,
    IReadOnlyList<StatRow> ByArtistGenre,
    IReadOnlyList<StatRow> ByPlays,
    IReadOnlyList<double> WeeklySent)
{
    public double ReplyRate => Pitches == 0 ? 0 : (double)Answered / Pitches;
}

/// <summary>
/// Статистика рассылки — всё из журнала писем, ответов и событий, локально.
/// Питч считается отвеченным, если артист ответил после него и до следующего питча ему же
/// (но не позже 45 дней): ответ на фоллоу-ап засчитывается исходному питчу.
/// </summary>
public static class StatsService
{
    private const int ReplyWindowDays = 45;

    public static List<PitchOutcome> Outcomes(AppDbContext db, DateTime? since = null)
    {
        var mails = db.OutgoingMails.Where(m => m.Status == MailStatuses.Sent).ToList();
        var replies = db.IncomingReplies.Where(r => r.ArtistId != null).ToList()
            .Select(r => (r.ArtistId!.Value, At: Parse(r.ReceivedAt)))
            .Where(r => r.At is not null)
            .GroupBy(r => r.Value)
            .ToDictionary(g => g.Key, g => g.Select(r => r.At!.Value).OrderBy(d => d).ToList());

        var artists = db.Artists.ToDictionary(a => a.Id);
        var beats = db.Beats.ToDictionary(b => b.Id);
        var templates = db.MailTemplates.ToDictionary(t => t.Id, t => t.Name);

        var pitches = mails.Where(m => m.Kind == MailKinds.Pitch)
            .Select(m => (Mail: m, At: Parse(m.SentAt)))
            .Where(p => p.At is not null)
            .OrderBy(p => p.At)
            .ToList();

        var result = new List<PitchOutcome>();
        foreach (var (mail, at) in pitches)
        {
            if (since is not null && at < since)
                continue;

            TimeSpan? after = null;
            if (mail.ArtistId is { } artistId && replies.TryGetValue(artistId, out var times))
            {
                var next = pitches.Where(p => p.Mail.ArtistId == artistId && p.At > at).Select(p => p.At).FirstOrDefault();
                var until = Min(next ?? DateTime.MaxValue, at!.Value.AddDays(ReplyWindowDays));
                var reply = times.FirstOrDefault(t => t >= at && t < until);
                if (reply != default)
                    after = reply - at!.Value;
            }

            result.Add(new PitchOutcome(mail, at!.Value,
                mail.ArtistId is { } a ? artists.GetValueOrDefault(a) : null,
                mail.BeatId is { } b ? beats.GetValueOrDefault(b) : null,
                mail.TemplateId is { } t && templates.TryGetValue(t, out var name) ? name : "—",
                after));
        }

        return result;
    }

    public static StatsReport Build(AppDbContext db, DateTime? since, CultureInfo culture)
    {
        var outcomes = Outcomes(db, since);
        var stamp = since?.ToString("yyyy-MM-dd HH:mm") ?? string.Empty;

        var followUps = db.OutgoingMails.Count(m => m.Status == MailStatuses.Sent && m.Kind == MailKinds.FollowUp &&
                                                    string.Compare(m.SentAt, stamp) >= 0);
        var events = db.ActivityEvents.Where(e => string.Compare(e.At, stamp) >= 0).ToList();

        var answeredTimes = outcomes.Where(o => o.Answered).Select(o => o.ReplyAfter!.Value).OrderBy(t => t).ToList();
        TimeSpan? median = answeredTimes.Count == 0 ? null : answeredTimes[answeredTimes.Count / 2];

        // Понедельник первым — так неделю читают в России и Европе.
        var days = Enumerable.Range(0, 7).Select(i => (DayOfWeek)((i + 1) % 7)).ToList();
        var byWeekday = days.Select(d => Row(culture.DateTimeFormat.GetAbbreviatedDayName(d),
            outcomes.Where(o => o.SentAt.DayOfWeek == d))).ToList();

        var byHours = Enumerable.Range(0, 6).Select(i => Row($"{i * 4:00}–{i * 4 + 4:00}",
            outcomes.Where(o => o.SentAt.Hour / 4 == i))).ToList();

        var answered = outcomes.Count(o => o.Answered);
        var d = Localization.Localizer.Instance["Stats.DayShort"];
        var replyTime = new[]
        {
            ($"< 1 {d}", 0.0, 1.0), ($"1–3 {d}", 1, 3), ($"3–7 {d}", 3, 7), ($"7+ {d}", 7, double.MaxValue)
        }.Select(b => new StatRow(b.Item1,
            outcomes.Count(o => o.ReplyAfter is { } t && t.TotalDays >= b.Item2 && t.TotalDays < b.Item3), answered)).ToList();

        var topBeats = outcomes.Where(o => o.Beat is not null)
            .GroupBy(o => o.Beat!.Id)
            .Select(g =>
            {
                var beat = g.First().Beat!;
                var status = beat.Status is "SOLD" or "FREE" ? $" · {beat.Status}" : string.Empty;
                return Row(beat.BeatName + status, g);
            })
            .OrderByDescending(r => r.Hits).ThenByDescending(r => r.Rate).ThenByDescending(r => r.Total)
            .Take(6).ToList();

        // Последние 12 недель — независимо от выбранного периода: видно, как идёт рассылка.
        var sentDates = db.OutgoingMails.Where(m => m.Status == MailStatuses.Sent).Select(m => m.SentAt).ToList()
            .Select(Parse).Where(d => d is not null).Select(d => d!.Value).ToList();
        var weekly = Enumerable.Range(0, 12).Select(w =>
        {
            var start = DateTime.Today.AddDays(-7 * (11 - w) - 6);
            return (double)sentDates.Count(d => d >= start && d < start.AddDays(7));
        }).ToList();

        return new StatsReport(
            outcomes.Count, followUps, answered,
            outcomes.Where(o => o.Artist is not null).Select(o => o.Artist!.Id).Distinct().Count(),
            median,
            events.Count(e => e.Kind == ActivityKinds.BeatStatus && e.Value == "SOLD"),
            events.Count(e => e.Kind == ActivityKinds.BeatStatus && e.Value == "FREE"),
            events.Count(e => e.Kind == ActivityKinds.BeatAdded),
            Group(outcomes, o => o.Template),
            Group(outcomes, o => FirstTag(o.Beat?.AiTags)),
            byWeekday, byHours, replyTime, topBeats,
            Group(outcomes, o => FirstTag(o.Artist?.AiGenreTags)),
            PlaysBuckets(outcomes),
            weekly);
    }

    /// <summary>Первый тег жанра: «rage, dark» → «rage». Без тега — «—».</summary>
    public static string FirstTag(string? tags)
    {
        var first = (tags ?? string.Empty).Split(',', ';', '/').Select(t => t.Trim().ToLowerInvariant())
            .FirstOrDefault(t => t.Length > 0);
        return first ?? "—";
    }

    private static List<StatRow> Group(IEnumerable<PitchOutcome> outcomes, Func<PitchOutcome, string> key) =>
        outcomes.GroupBy(key)
            .Select(g => Row(g.Key, g))
            .OrderByDescending(r => r.Total).ThenByDescending(r => r.Rate)
            .Take(8).ToList();

    private static List<StatRow> PlaysBuckets(List<PitchOutcome> outcomes)
    {
        var buckets = new (string Label, int Min, int Max)[]
        {
            ("< 5k", 0, 5_000), ("5–30k", 5_000, 30_000), ("30–100k", 30_000, 100_000), ("100k+", 100_000, int.MaxValue)
        };
        return buckets.Select(b => Row(b.Label,
            outcomes.Where(o => o.Artist is { TotalPlays: > 0 } a && a.TotalPlays >= b.Min && a.TotalPlays < b.Max))).ToList();
    }

    private static StatRow Row(string label, IEnumerable<PitchOutcome> outcomes)
    {
        var list = outcomes as ICollection<PitchOutcome> ?? outcomes.ToList();
        return new StatRow(label, list.Count(o => o.Answered), list.Count);
    }

    public static DateTime? Parse(string? stored) =>
        DateTime.TryParse(stored, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
}
