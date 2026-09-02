namespace MessedUpSearchA.ViewModels;

/// <summary>Строка в блоке «какие мои биты подойдут этому артисту».</summary>
public class SimilarBeatItem
{
    public int BeatId { get; init; }

    public string BeatName { get; init; } = string.Empty;

    public int Percent { get; init; }

    public string PercentLabel => $"{Percent}%";

    public double BarWidth => Percent * 1.6;

    /// <summary>Цвет статуса бита — чтобы сразу видеть, продан он или свободен.</summary>
    public string StatusColor { get; init; } = "#888888";
}
