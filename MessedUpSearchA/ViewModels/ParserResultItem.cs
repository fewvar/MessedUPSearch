using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using MessedUpSearchA.Services.Parsing;

namespace MessedUpSearchA.ViewModels;

public partial class ParserResultItem : ObservableObject
{
    [ObservableProperty] private bool _isSelected = true;
    [ObservableProperty] private bool _isImported;

    public ArtistCandidate Candidate { get; init; } = new();

    public bool AlreadyInDb { get; init; }

    public void MarkImported()
    {
        IsImported = true;
        IsSelected = false;
    }

    partial void OnIsImportedChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(HasStatus));
    }

    public string Nickname => Candidate.Nickname;
    public string Platform => Candidate.Platform;
    public string SourceUrl => Candidate.SourceUrl;

    public string PlaysLabel => Format(Candidate.TotalPlays);
    public string FollowersLabel => Format(Candidate.Followers);

    public string TagsLabel => string.IsNullOrWhiteSpace(Candidate.Tags) ? "—" : Candidate.Tags;

    public string CountryLabel => string.IsNullOrWhiteSpace(Candidate.Country) ? "—" : Candidate.Country;

    public string TracksLabel => Candidate.Tracks.Count switch
    {
        0 => "нет треков",
        1 => "1 трек",
        >= 2 and <= 4 => $"{Candidate.Tracks.Count} трека",
        _ => $"{Candidate.Tracks.Count} треков"
    };

    public string LastReleaseLabel =>
        string.IsNullOrWhiteSpace(Candidate.LastTrackDate) ? "—" : Candidate.LastTrackDate;

    public string StatusLabel => IsImported ? "СОХРАНЁН"
        : AlreadyInDb ? "УЖЕ В БАЗЕ"
        : string.Empty;

    public bool HasStatus => StatusLabel.Length > 0;

    private static string Format(int value) => value switch
    {
        0 => "—",
        < 1_000 => value.ToString(CultureInfo.InvariantCulture),
        < 1_000_000 => (value / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + "k",
        _ => (value / 1_000_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "M"
    };
}
