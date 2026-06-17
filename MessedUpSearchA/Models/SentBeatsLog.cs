namespace MessedUpSearchA.Models;

/// <summary>
/// Лог отправленных битов: какой бит какому артисту и когда был отправлен.
/// Связка многие-ко-многим между Artist и Beat (схема ТЗ §3, таблица SentBeats_Log).
/// </summary>
public class SentBeatsLog
{
    public int Id { get; set; }

    public int ArtistId { get; set; }
    public Artist? Artist { get; set; }

    public int BeatId { get; set; }
    public Beat? Beat { get; set; }

    public string SentAt { get; set; } = string.Empty;   // ISO datetime отправки
}
