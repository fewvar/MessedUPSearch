namespace MessedUpSearchA.ViewModels;

/// <summary>Строка в блоке «на кого похож бит».</summary>
public class SimilarArtistItem
{
    public string Artist { get; init; } = string.Empty;

    public int Percent { get; init; }

    public string PercentLabel => $"{Percent}%";

    /// <summary>Ширина полоски в списке — рисуем её вместо диаграммы.</summary>
    public double BarWidth => Percent * 1.6;
}
