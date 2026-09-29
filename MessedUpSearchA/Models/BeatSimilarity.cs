namespace MessedUpSearchA.Models;

/// <summary>
/// Результат анализа бита: на кого из базы он похож. Одна строка — один артист.
/// Считается секундами, поэтому храним, а не пересчитываем при каждом открытии карточки.
/// </summary>
public class BeatSimilarity
{
    public int Id { get; set; }

    public int BeatId { get; set; }
    public Beat? Beat { get; set; }

    public string Artist { get; set; } = string.Empty;

    /// <summary>
    /// Артист из нашей базы, если совпадение пришло из его треков. Для 32 артистов
    /// из artist_index.bin пусто — у них нет карточки, только имя.
    /// </summary>
    public int? ArtistId { get; set; }

    /// <summary>Позиция в выдаче, 1 — самый похожий.</summary>
    public int Rank { get; set; }

    /// <summary>Оценка 0..100 для показа.</summary>
    public int Percent { get; set; }

    /// <summary>Сырая косинусная близость — на случай, если шкалу процентов пересчитаем.</summary>
    public double Similarity { get; set; }

    public string ComputedAt { get; set; } = string.Empty;

    /// <summary>"Target" — кому предложить, "Reference" — «звучит как» (крупные артисты).</summary>
    public string Kind { get; set; } = "Target";

    // Для артистов большого индекса, которых ещё нет в базе: как импортировать и что сыграть.
    public string Platform { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public string AvatarUrl { get; set; } = string.Empty;
    public int Plays { get; set; }
    public string TopTrackId { get; set; } = string.Empty;
    public string TopTrackUrl { get; set; } = string.Empty;
    public string TopTrackTitle { get; set; } = string.Empty;
}
