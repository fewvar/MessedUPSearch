namespace MessedUpSearchA.Models;

/// <summary>Ответ артиста, найденный во входящих (IMAP). По нему CRM ставит «ответил», статистика считает время до ответа.</summary>
public class IncomingReply
{
    public int Id { get; set; }
    public int? ArtistId { get; set; }

    /// <summary>На какое наше письмо ответ — по In-Reply-To, а если его нет, последнее письмо этому артисту.</summary>
    public int? OutgoingMailId { get; set; }

    public string MessageId { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;

    /// <summary>Первая строка ответа — видно в CRM без открытия почты.</summary>
    public string Snippet { get; set; } = string.Empty;

    /// <summary>yyyy-MM-dd HH:mm.</summary>
    public string ReceivedAt { get; set; } = string.Empty;
}
