using System;
using System.Collections.Generic;
using System.Linq;
using MessedUpSearchA.Data;
using MessedUpSearchA.Models;

namespace MessedUpSearchA.Services.Mail;

/// <summary>Кому пора напомнить: бит ушёл письмом, ответа нет N дней, фоллоу-апа ещё не было.</summary>
public record FollowUpDue(int ArtistId, int BeatId, OutgoingMail Pitch);

/// <summary>
/// Правила живой CRM. Считаются по журналу писем и ответов — ничего не хранится отдельно,
/// поэтому ручная смена статуса или удаление ответа сразу меняют картину.
/// </summary>
public static class CrmRules
{
    /// <summary>Столько писем без единого ответа — артист «не отвечает».</summary>
    public const int SilentAfter = 3;

    /// <summary>Эти статусы ставят руками, когда договорились где-то ещё (директ, телеграм) — им не напоминаем.</summary>
    private static readonly HashSet<string> Settled = ["OK", "POSTED FREE"];

    public static List<FollowUpDue> DueFollowUps(AppDbContext db, int days)
    {
        var cutoff = DateTime.Now.AddDays(-days).ToString("yyyy-MM-dd HH:mm");
        var mails = db.OutgoingMails.Where(m => m.Status == MailStatuses.Sent && m.ArtistId != null && m.BeatId != null).ToList();
        var replied = LastReplyByArtist(db);
        var settled = db.Artists.Where(a => Settled.Contains(a.CrmStatus)).Select(a => a.Id).ToHashSet();

        var followedUp = mails.Where(m => m.Kind == MailKinds.FollowUp)
            .Select(m => (m.ArtistId!.Value, m.BeatId!.Value)).ToHashSet();

        return mails
            .Where(m => m.Kind == MailKinds.Pitch && string.CompareOrdinal(m.SentAt, cutoff) <= 0)
            .GroupBy(m => (m.ArtistId!.Value, m.BeatId!.Value))
            .Select(g => g.OrderBy(m => m.SentAt).Last())
            .Where(m => !followedUp.Contains((m.ArtistId!.Value, m.BeatId!.Value)))
            .Where(m => !settled.Contains(m.ArtistId!.Value))
            .Where(m => !replied.TryGetValue(m.ArtistId!.Value, out var last) || string.CompareOrdinal(last, m.SentAt) < 0)
            .Select(m => new FollowUpDue(m.ArtistId!.Value, m.BeatId!.Value, m))
            .ToList();
    }

    /// <summary>«Молчуны»: 3+ писем и ни одного ответа. В рассылках — без галочки и в конце списка.</summary>
    public static HashSet<int> SilentArtists(AppDbContext db)
    {
        var withReplies = db.IncomingReplies.Where(r => r.ArtistId != null).Select(r => r.ArtistId!.Value).ToHashSet();
        var settled = db.Artists.Where(a => Settled.Contains(a.CrmStatus) || a.CrmStatus == "REPLIED")
            .Select(a => a.Id).ToHashSet();

        return db.OutgoingMails.Where(m => m.Status == MailStatuses.Sent && m.ArtistId != null)
            .GroupBy(m => m.ArtistId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToList()
            .Where(x => x.Count >= SilentAfter && !withReplies.Contains(x.Id) && !settled.Contains(x.Id))
            .Select(x => x.Id)
            .ToHashSet();
    }

    public static Dictionary<int, string> LastReplyByArtist(AppDbContext db) =>
        db.IncomingReplies.Where(r => r.ArtistId != null).ToList()
            .GroupBy(r => r.ArtistId!.Value)
            .ToDictionary(g => g.Key, g => g.Max(r => r.ReceivedAt)!);
}
