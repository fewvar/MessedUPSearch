using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MessedUpSearchA.Models;

namespace MessedUpSearchA.ViewModels;

public partial class BeatsViewModel : ViewModelBase
{
    public ObservableCollection<Beat> Beats { get; } = new()
    {
        new Beat
        {
            BeatName = "VOODOO",
            AiTags = "Rage",
            Bpm = 145,
            Key = "C minor",
            Added = "2026-05-24",
            Status = "POTENTIAL",
            StatusColor = "#F1C40F"
        },
        new Beat
        {
            BeatName = "LATE NIGHT",
            AiTags = "Cloud, Sad",
            Bpm = 120,
            Key = "F# minor",
            Added = "2026-05-20",
            Status = "SOLD",
            StatusColor = "#2ECC71"
        }
    };
}
