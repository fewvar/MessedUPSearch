using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using MessedUpSearchA.Data;
using MessedUpSearchA.Models;
using MessedUpSearchA.Services.Localization;

namespace MessedUpSearchA.Services.Mail;

/// <summary>Одно письмо в очереди. Текст уже готов — ровно то, что было в предпросмотре.</summary>
public record MailJob(int ArtistId, int BeatId, int? TemplateId, string Kind, string To,
    string Subject, string Body, string? InReplyTo = null);

/// <summary>
/// Рассылка в фоне: окно рассылки можно закрыть, письма уходят дальше. Каждое письмо
/// отдельно, между ними 20–40 с, не больше лимита в сутки — так ящик не примут за спамера.
///
/// Живёт на UI-потоке: SMTP асинхронный, а свойства для футера меняются без диспетчера.
/// </summary>
public partial class MailQueue : ObservableObject
{
    /// <summary>Пауза между письмами, секунды. Меняется только в проверке (MlCheck mail).</summary>
    public int MinPauseSeconds { get; init; } = 20;
    public int MaxPauseSeconds { get; init; } = 40;

    private readonly Queue<MailJob> _jobs = new();
    private readonly Func<AppSettings> _settings;
    private CancellationTokenSource? _cts;

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private int _total;
    [ObservableProperty] private int _sent;
    [ObservableProperty] private int _failed;

    /// <summary>Строка для футера: «ПОЧТА: 3/10 · следующее через 27 с».</summary>
    [ObservableProperty] private string _status = string.Empty;

    /// <summary>Письмо ушло — CRM и таблицам пора перечитаться.</summary>
    public event Action? MailSent;

    public MailQueue(Func<AppSettings> settings) => _settings = settings;

    public static string PasswordKey(string address) => "smtp:" + address.Trim().ToLowerInvariant();

    /// <summary>Сколько ещё можно отправить сегодня.</summary>
    public int RemainingToday()
    {
        using var db = new AppDbContext();
        return Math.Max(0, _settings().MailDailyLimit - SentToday(db));
    }

    private static int SentToday(AppDbContext db)
    {
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        return db.OutgoingMails.Count(m => m.Status == MailStatuses.Sent && m.SentAt.StartsWith(today));
    }

    public void Enqueue(IEnumerable<MailJob> jobs)
    {
        foreach (var job in jobs)
            _jobs.Enqueue(job);

        Total = Sent + Failed + _jobs.Count;
        if (!IsRunning)
            _ = RunAsync();
    }

    public void Stop() => _cts?.Cancel();

    private async Task RunAsync()
    {
        IsRunning = true;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        string? stopReason = null;

        try
        {
            var settings = _settings();
            var password = SecretStore.Get(PasswordKey(settings.MailAddress));
            if (string.IsNullOrEmpty(password) || string.IsNullOrWhiteSpace(settings.SmtpHost))
            {
                stopReason = Localizer.Instance["Mail.NotConfigured"];
                return;
            }

            var account = new MailAccount(settings.MailAddress, SenderName(settings), password,
                settings.SmtpHost, settings.SmtpPort);

            while (_jobs.Count > 0)
            {
                ct.ThrowIfCancellationRequested();

                if (RemainingToday() == 0)
                {
                    stopReason = Localizer.Instance.Format("Mail.LimitReached", settings.MailDailyLimit);
                    return;
                }

                var job = _jobs.Dequeue();
                Status = Localizer.Instance.Format("Mail.Sending", Sent + Failed + 1, Total);

                try
                {
                    var messageId = await MailService.SendAsync(account, job.To, job.Subject, job.Body, job.InReplyTo, ct);
                    Record(job, messageId, null);
                    Sent++;
                    MailSent?.Invoke();
                }
                catch (MailSendException ex) when (ex.Failure is MailFailure.Recipient or MailFailure.Other)
                {
                    // Плохой адрес у одного артиста — не повод останавливать всю рассылку.
                    Record(job, string.Empty, ex.Message);
                    Failed++;
                    AppLog.Write($"рассылка: {job.To}: {ex.Message}");
                }
                catch (MailSendException ex)
                {
                    Record(job, string.Empty, ex.Message);
                    Failed++;
                    AppLog.Write($"рассылка остановлена: {ex.Failure}: {ex.Message}");
                    stopReason = Localizer.Instance[ex.Failure == MailFailure.Auth ? "Mail.AuthFailed" : "Mail.ConnectionFailed"];
                    return;
                }

                if (_jobs.Count == 0)
                    break;

                // Пауза с обратным отсчётом в футере.
                var pause = Random.Shared.Next(MinPauseSeconds, MaxPauseSeconds + 1);
                for (var left = pause; left > 0; left--)
                {
                    Status = Localizer.Instance.Format("Mail.Waiting", Sent + Failed, Total, left);
                    await Task.Delay(1000, ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            stopReason = Localizer.Instance["Mail.Stopped"];
        }
        catch (Exception ex)
        {
            AppLog.Write($"рассылка упала: {ex}");
            stopReason = ex.Message;
        }
        finally
        {
            var left = _jobs.Count;
            _jobs.Clear();
            IsRunning = false;
            Status = string.Empty;

            var summary = Localizer.Instance.Format("Mail.Done", Sent, Total);
            if (stopReason is not null)
                summary = left > 0 ? $"{stopReason} · {summary}" : stopReason;
            Toasts.Show(summary);
            if (Sent > 0)
                Audio.UiSounds.Play(Audio.UiSound.Sent);

            Sent = Failed = Total = 0;
            _cts.Dispose();
            _cts = null;
        }
    }

    public static string SenderName(AppSettings settings) =>
        string.IsNullOrWhiteSpace(settings.MailSenderName)
            ? settings.MailAddress.Split('@')[0]
            : settings.MailSenderName.Trim();

    /// <summary>
    /// Авто-отметки: письмо в журнал, связь «бит → артист» отмечена отправленной,
    /// артист попадает в CRM со статусом «не ответил», если статуса ещё не было.
    /// </summary>
    private static void Record(MailJob job, string messageId, string? error)
    {
        // Письмо уже ушло — сбой записи в базу не должен останавливать остальную рассылку.
        try
        {
            RecordCore(job, messageId, error);
        }
        catch (Exception ex)
        {
            AppLog.Write($"рассылка: не записал письмо {job.To} в базу: {ex.Message}");
        }
    }

    private static void RecordCore(MailJob job, string messageId, string? error)
    {
        using var db = new AppDbContext();
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        db.OutgoingMails.Add(new OutgoingMail
        {
            ArtistId = job.ArtistId,
            BeatId = job.BeatId,
            TemplateId = job.TemplateId,
            Kind = job.Kind,
            ToAddress = job.To,
            Subject = job.Subject,
            Body = job.Body,
            MessageId = messageId,
            SentAt = now,
            Status = error is null ? MailStatuses.Sent : MailStatuses.Failed,
            Error = error ?? string.Empty
        });

        if (error is null)
        {
            var link = db.SentBeatsLog.FirstOrDefault(s => s.ArtistId == job.ArtistId && s.BeatId == job.BeatId);
            if (link is null)
                db.SentBeatsLog.Add(new SentBeatsLog
                {
                    ArtistId = job.ArtistId, BeatId = job.BeatId, AssignedAt = now, IsSent = true, SentAt = now
                });
            else if (!link.IsSent)
            {
                link.IsSent = true;
                link.SentAt = now;
            }

            var artist = db.Artists.FirstOrDefault(a => a.Id == job.ArtistId);
            if (artist is not null && string.IsNullOrWhiteSpace(artist.CrmStatus))
                artist.CrmStatus = "NO REPLY";
        }

        db.SaveChanges();
    }
}
