using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MessedUpSearchA.Data;
using MessedUpSearchA.Models;
using MimeKit;

namespace MessedUpSearchA.Services.Mail;

/// <summary>Найденный ответ — для тоста «ответил X».</summary>
public record FoundReply(string Nickname, string Snippet);

/// <summary>
/// Ответы артистов во входящих (IMAP, только чтение — письма не помечаются прочитанными).
/// Ответом считается письмо, которое ссылается на наше (In-Reply-To / References), или письмо
/// с адреса артиста, которому мы уже писали. Остальная почта не читается и никуда не сохраняется.
/// </summary>
public static class ReplyChecker
{
    /// <summary>Старше этого во входящих не ищем: ответ через два месяца — уже не про этот бит.</summary>
    private const int LookBackDays = 60;

    public static async Task<IReadOnlyList<FoundReply>> CheckAsync(AppSettings settings, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(settings.ImapHost) || string.IsNullOrWhiteSpace(settings.MailAddress))
            return [];

        var password = SecretStore.Get(MailQueue.PasswordKey(settings.MailAddress));
        if (string.IsNullOrEmpty(password))
            return [];

        List<OutgoingMail> sent;
        Dictionary<int, Artist> artists;
        HashSet<string> known;
        using (var db = new AppDbContext())
        {
            var since = DateTime.Now.AddDays(-LookBackDays).ToString("yyyy-MM-dd");
            sent = db.OutgoingMails
                .Where(m => m.Status == MailStatuses.Sent && m.ArtistId != null && string.Compare(m.SentAt, since) >= 0)
                .ToList();
            if (sent.Count == 0)
                return [];

            var ids = sent.Select(m => m.ArtistId!.Value).Distinct().ToList();
            artists = db.Artists.Where(a => ids.Contains(a.Id)).ToDictionary(a => a.Id);
            known = db.IncomingReplies.Select(r => r.MessageId).ToHashSet();
        }

        var byMessageId = sent.Where(m => m.MessageId.Length > 0)
            .ToDictionary(m => Normalize(m.MessageId), m => m);
        var byAddress = sent.GroupBy(m => m.ToAddress.Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.SentAt).ToList());
        var me = settings.MailAddress.Trim().ToLowerInvariant();

        using var client = new ImapClient { Timeout = 30_000 };
        await client.ConnectAsync(settings.ImapHost, settings.ImapPort, MailService.SecurityFor(settings.ImapPort), ct);
        await client.AuthenticateAsync(settings.MailAddress, password, ct);

        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadOnly, ct);

        // Дальше последнего прочитанного — только новые; ящик пересоздан или первый раз — с даты первой рассылки.
        IList<UniqueId> uids;
        if (settings.ImapUidValidity == inbox.UidValidity && settings.ImapLastUid > 0)
        {
            var range = new UniqueIdRange(new UniqueId(settings.ImapLastUid + 1), UniqueId.MaxValue);
            uids = (await inbox.SearchAsync(SearchQuery.Uids(range), ct))
                .Where(u => u.Id > settings.ImapLastUid).ToList();
        }
        else
        {
            var first = sent.Min(m => DateTime.TryParse(m.SentAt, out var d) ? d : DateTime.Now);
            uids = await inbox.SearchAsync(SearchQuery.DeliveredAfter(first.AddDays(-1)), ct);
        }

        var found = new List<FoundReply>();
        if (uids.Count > 0)
        {
            var summaries = await inbox.FetchAsync(uids,
                MessageSummaryItems.Envelope | MessageSummaryItems.References | MessageSummaryItems.InternalDate, ct);

            foreach (var summary in summaries)
            {
                if (summary.Envelope is null)
                    continue;
                var from = summary.Envelope.From.Mailboxes.FirstOrDefault()?.Address?.Trim().ToLowerInvariant() ?? "";
                var messageId = summary.Envelope.MessageId ?? $"uid-{inbox.UidValidity}-{summary.UniqueId.Id}";
                if (from.Length == 0 || from == me || known.Contains(messageId))
                    continue;

                var received = (summary.InternalDate ?? summary.Envelope.Date ?? DateTimeOffset.Now).LocalDateTime;
                var original = Match(summary, from, received, byMessageId, byAddress);
                if (original?.ArtistId is not { } artistId || !artists.TryGetValue(artistId, out var artist))
                    continue;

                var message = await inbox.GetMessageAsync(summary.UniqueId, ct);
                var reply = new IncomingReply
                {
                    ArtistId = artistId,
                    OutgoingMailId = original.Id,
                    MessageId = messageId,
                    FromAddress = from,
                    Subject = summary.Envelope.Subject ?? string.Empty,
                    Snippet = Snippet(message),
                    ReceivedAt = received.ToString("yyyy-MM-dd HH:mm")
                };

                Save(reply);
                known.Add(messageId);
                found.Add(new FoundReply(artist.Nickname, reply.Snippet));
            }
        }

        settings.ImapUidValidity = inbox.UidValidity;
        if (uids.Count > 0)
            settings.ImapLastUid = Math.Max(settings.ImapLastUid, uids.Max(u => u.Id));
        settings.Save();

        await client.DisconnectAsync(true, ct);
        return found;
    }

    /// <summary>На какое наше письмо это ответ. Сначала по заголовкам, потом по адресу — последнее письмо до ответа.</summary>
    private static OutgoingMail? Match(IMessageSummary summary, string from, DateTime received,
        Dictionary<string, OutgoingMail> byMessageId, Dictionary<string, List<OutgoingMail>> byAddress)
    {
        var refs = new List<string>();
        if (summary.Envelope?.InReplyTo is { } inReplyTo)
            refs.Add(inReplyTo);
        if (summary.References is { } references)
            refs.AddRange(references);

        foreach (var id in refs)
            if (byMessageId.TryGetValue(Normalize(id), out var mail))
                return mail;

        if (!byAddress.TryGetValue(from, out var mails))
            return null;

        var stamp = received.ToString("yyyy-MM-dd HH:mm");
        return mails.LastOrDefault(m => string.CompareOrdinal(m.SentAt, stamp) <= 0);
    }

    private static string Normalize(string messageId) => messageId.Trim().Trim('<', '>').ToLowerInvariant();

    /// <summary>Первая живая строка ответа: без цитаты нашего письма и пустых строк.</summary>
    public static string Snippet(MimeMessage message)
    {
        var text = message.TextBody ?? HtmlToText(message.HtmlBody) ?? string.Empty;
        var line = text.Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 0 && !l.StartsWith('>'));

        if (line is null)
            return string.Empty;
        line = System.Text.RegularExpressions.Regex.Replace(System.Net.WebUtility.HtmlDecode(line), @"\s+", " ").Trim();
        return line.Length > 140 ? line[..140] + "…" : line;
    }

    private static string? HtmlToText(string? html) =>
        html is null ? null : System.Text.RegularExpressions.Regex.Replace(
            System.Text.RegularExpressions.Regex.Replace(html, "<(br|/p|/div)[^>]*>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
            "<[^>]+>", " ");

    /// <summary>Ответ — в журнал, артист в CRM — «REPLIED», если статус был пустым или «не ответил».</summary>
    private static void Save(IncomingReply reply)
    {
        using var db = new AppDbContext();
        db.IncomingReplies.Add(reply);
        var artist = db.Artists.FirstOrDefault(a => a.Id == reply.ArtistId);
        if (artist is not null && (string.IsNullOrWhiteSpace(artist.CrmStatus) || artist.CrmStatus == "NO REPLY"))
            artist.CrmStatus = "REPLIED";
        db.SaveChanges();
    }
}
