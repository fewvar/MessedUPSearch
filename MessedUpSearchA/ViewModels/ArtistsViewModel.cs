using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MessedUpSearchA.Models;

namespace MessedUpSearchA.ViewModels;

public partial class ArtistsViewModel : ViewModelBase
{
    public ObservableCollection<Artist> Artists { get; } = new()
    {
        new Artist
        {
            Nickname = "KXSHDAMI",
            AiGenreTags = "Rage, Plugg",
            TotalPlays = 8120,
            IgLink = "@kxshdami",
            SpotifyLink = "23000",
            Language = "ENG",
            AvatarColor = "#E74C3C",
            IsFavorite = true,
            IsRedFlagged = false
        },
        new Artist
        {
            Nickname = "NIGHTTEARS",
            AiGenreTags = "Sad, Cloud",
            TotalPlays = 6890,
            IgLink = "@night.tears",
            SpotifyLink = "9400",
            Language = "RU",
            AvatarColor = "#5DADE2",
            IsFavorite = false,
            IsRedFlagged = true
        }
    };
}
