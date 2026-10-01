namespace MessedUpSearchA.Models;

/// <summary>
/// Журнал событий для статистики и «Сегодня» — только локально. Письма и ответы живут в своих
/// таблицах (OutgoingMails, IncomingReplies), здесь — то, чего там нет: новые биты, смена статуса, анализ.
/// </summary>
public class ActivityEvent
{
    public int Id { get; set; }

    /// <summary>ActivityKinds.*</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>yyyy-MM-dd HH:mm.</summary>
    public string At { get; set; } = string.Empty;

    public int? BeatId { get; set; }
    public int? ArtistId { get; set; }

    /// <summary>Например, новый статус бита.</summary>
    public string Value { get; set; } = string.Empty;
}

public static class ActivityKinds
{
    public const string BeatAdded = "beat_added";
    public const string BeatStatus = "beat_status";
    public const string Analysis = "analysis";
}

/// <summary>Интервал, когда DAW была на переднем плане. Пишется только с согласия.</summary>
public class DawSession
{
    public int Id { get; set; }
    public string App { get; set; } = string.Empty;
    public string StartedAt { get; set; } = string.Empty;
    public string EndedAt { get; set; } = string.Empty;
}
