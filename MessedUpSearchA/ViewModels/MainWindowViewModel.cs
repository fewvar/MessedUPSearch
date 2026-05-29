using CommunityToolkit.Mvvm.ComponentModel;

namespace MessedUpSearchA.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly BeatsViewModel _beatsVm = new();
    private readonly ArtistsViewModel _artistsVm = new();

    public BeatsViewModel BeatsVm => _beatsVm;
    public ArtistsViewModel ArtistsVm => _artistsVm;

    [ObservableProperty]
    private ViewModelBase _currentViewModel;

    [ObservableProperty]
    private bool? _isBeatsSelected = true;

    [ObservableProperty]
    private bool _isCrmOpen;

    [ObservableProperty]
    private bool _isSettingsOpen;

    [ObservableProperty]
    private string _parserStatus = "Idle";

    public string AppVersion => "v0.1";

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
