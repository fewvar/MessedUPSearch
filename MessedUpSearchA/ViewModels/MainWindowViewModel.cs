using CommunityToolkit.Mvvm.ComponentModel;

namespace MessedUpSearchA.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly BeatsViewModel _beatsVm = new();
    private readonly ArtistsViewModel _artistsVm = new();

    [ObservableProperty]
    private ViewModelBase _currentViewModel;

    [ObservableProperty]
    private bool? _isBeatsSelected = true;

    public MainWindowViewModel()
    {
        _currentViewModel = _beatsVm;
    }

    partial void OnIsBeatsSelectedChanged(bool? value)
    {
        CurrentViewModel = value == true ? _beatsVm : _artistsVm;
    }
}
