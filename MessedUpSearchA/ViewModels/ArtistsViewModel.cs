using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using MessedUpSearchA.Data;
using MessedUpSearchA.Models;

namespace MessedUpSearchA.ViewModels;

public partial class ArtistsViewModel : ViewModelBase
{
    public ObservableCollection<Artist> Artists { get; } = new();

    public ArtistsViewModel()
    {
        // В дизайнере Rider базы нет — показываем заглушку, чтобы XAML-превью не падало.
        if (Design.IsDesignMode)
        {
            Artists.Add(new Artist { Nickname = "PREVIEW", AiGenreTags = "Rage", TotalPlays = 8120, AvatarColor = "#E74C3C" });
            return;
        }

        using var db = new AppDbContext();
        foreach (var artist in db.Artists.ToList())
            Artists.Add(artist);
    }
}
