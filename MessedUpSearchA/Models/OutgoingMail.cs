namespace MessedUpSearchA.Models;

/// <summary>
/// Каждое письмо, ушедшее из приложения (или не ушедшее — Status = failed). По Message-ID
/// потом узнаются ответы, по датам — защита от спама, лимит в сутки и статистика.
/// </summary>
public class OutgoingMail
{
    public int Id { get; set; }

    /// <summary>Артиста или бит удалили — письмо остаётся в журнале для статистики.</summary>
    public int? ArtistId { get; set; }
    public int? BeatId { get; set; }
    public int? TemplateId { get; set; }

    /// <summary>MailKinds.Pitch / MailKinds.FollowUp.</summary>
    public string Kind { get; set; } = MailKinds.Pitch;

    public string ToAddress { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string MessageId { get; set; } = string.Empty;

    /// <summary>yyyy-MM-dd HH:mm, как остальные даты в базе.</summary>
    public string SentAt { get; set; } = string.Empty;

    public string Status { get; set; } = MailStatuses.Sent;
    public string Error { get; set; } = string.Empty;
}

public static class MailStatuses
{
    public const string Sent = "sent";
    public const string Failed = "failed";
}
