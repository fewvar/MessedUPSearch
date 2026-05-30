using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using MessedUpSearchA.Data;
using MessedUpSearchA.Models;

namespace MessedUpSearchA.ViewModels;

public partial class BeatsViewModel : ViewModelBase
{
    public ObservableCollection<Beat> Beats { get; } = new();

    public BeatsViewModel()
    {
        // В дизайнере Rider базы нет — показываем заглушку, чтобы XAML-превью не падало.
        if (Design.IsDesignMode)
        {
            Beats.Add(new Beat { BeatName = "PREVIEW", AiTags = "Rage", Bpm = 145, StatusColor = "#F1C40F" });
            return;
        }

        using var db = new AppDbContext();
        foreach (var beat in db.Beats.ToList())
            Beats.Add(beat);
    }
}
