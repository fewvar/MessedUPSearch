using CommunityToolkit.Mvvm.ComponentModel;

namespace MessedUpSearchA.ViewModels;

/// <summary>Строка в блоке «кому предложить бит».</summary>
public partial class SimilarArtistItem : ObservableObject
{
    public string Artist { get; init; } = string.Empty;

    public int Percent { get; init; }

    public string PercentLabel => $"{Percent}%";

    /// <summary>Подсказка на ▶: какой трек сыграет — ближайший к биту по звуку.</summary>
    public string PlayTip => TopTrackTitle.Length > 0
        ? MessedUpSearchA.Services.Localization.Localizer.Instance.Format("BeatEditor.PlayNearest", TopTrackTitle)
        : MessedUpSearchA.Services.Localization.Localizer.Instance["BeatEditor.PlayTrack"];

    /// <summary>Первое место выдачи — его полоска ярче остальных.</summary>
    public bool IsLeader { get; set; }

    /// <summary>Ширина полоски в списке — рисуем её вместо диаграммы.</summary>
    public double BarWidth => Percent * 1.2;

    /// <summary>Номер артиста в базе. null — он только в большом индексе.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanImport))] private int? _artistId;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanImport))] private bool _isImporting;

    /// <summary>Только что добавлен кнопкой ＋ — на её месте ✓ со щелчком.</summary>
    [ObservableProperty] private bool _justImported;

    public string Platform { get; init; } = string.Empty;
    public string SourceId { get; init; } = string.Empty;
    public string SourceUrl { get; init; } = string.Empty;
    public string AvatarUrl { get; init; } = string.Empty;
    public int Plays { get; init; }
    public string TopTrackId { get; init; } = string.Empty;
    public string TopTrackUrl { get; init; } = string.Empty;
    public string TopTrackTitle { get; init; } = string.Empty;

    /// <summary>➕ — только тем, кого ещё нет в базе.</summary>
    public bool CanImport => ArtistId is null && !IsImporting && SourceUrl.Length > 0;

    public bool CanOpen => SourceUrl.Length > 0;

    public bool CanPlay => TopTrackUrl.Length > 0 || TopTrackId.Length > 0;
}
