using CommunityToolkit.Mvvm.ComponentModel;

namespace MessedUpSearchA.ViewModels;

/// <summary>
/// Один артист в списке выбора при назначении бита (модалка бита).
/// IsSelected двусторонне привязан к чекбоксу.
/// </summary>
public partial class ArtistPick : ObservableObject
{
    public int Id { get; init; }
    public string Nickname { get; init; } = string.Empty;

    [ObservableProperty] private bool _isSelected;
}
