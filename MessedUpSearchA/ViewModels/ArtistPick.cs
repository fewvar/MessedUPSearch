using CommunityToolkit.Mvvm.ComponentModel;

namespace MessedUpSearchA.ViewModels;

public partial class ArtistPick : ObservableObject
{
    public int Id { get; init; }
    public string Nickname { get; init; } = string.Empty;

    [ObservableProperty] private bool _isSelected;
}
