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

    [ObservableProperty]
    private bool _isCrmOpen;

    [ObservableProperty]
    private bool _isSettingsOpen;

    public MainWindowViewModel()
    {
        _currentViewModel = _beatsVm;
    }

    partial void OnIsBeatsSelectedChanged(bool? value)
    {
        CurrentViewModel = value == true ? _beatsVm : _artistsVm;
    }

    public void ShowCrm() => IsCrmOpen = true;
    public void ShowSettings() => IsSettingsOpen = true;
    public void CloseOverlays()
    {
        IsCrmOpen = false;
        IsSettingsOpen = false;
    }
}
