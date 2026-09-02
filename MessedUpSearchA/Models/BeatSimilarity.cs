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

    /// <summary>Позиция в выдаче, 1 — самый похожий.</summary>
    public int Rank { get; set; }

    /// <summary>Оценка 0..100 для показа.</summary>
    public int Percent { get; set; }

    /// <summary>Сырая косинусная близость — на случай, если шкалу процентов пересчитаем.</summary>
    public double Similarity { get; set; }

    public string ComputedAt { get; set; } = string.Empty;
}
