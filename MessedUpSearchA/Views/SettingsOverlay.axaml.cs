using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MessedUpSearchA.ViewModels;

namespace MessedUpSearchA.Views;

public partial class SettingsOverlay : UserControl
{
    public SettingsOverlay()
    {
        InitializeComponent();
    }

    private void OnBackdropClick(object? sender, PointerPressedEventArgs e)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        (owner?.DataContext as MainWindowViewModel)?.CloseOverlays();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        (owner?.DataContext as MainWindowViewModel)?.CloseOverlays();
    }

    /// <summary>
    /// Открывает папку с данными: там база, настройки и лог. Раньше кнопка
    /// была нарисована, но обработчика у неё не было вообще.
    /// </summary>
    private void OnOpenDataFolderClick(object? sender, RoutedEventArgs e)
    {
        var folder = MessedUpSearchA.Data.AppPaths.DataDir;

        var launcher = TopLevel.GetTopLevel(this)?.Launcher;
        launcher?.LaunchDirectoryInfoAsync(new System.IO.DirectoryInfo(folder));
    }

    private void OnReportProblemClick(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if ((top as Window)?.DataContext is not MainWindowViewModel vm)
            return;

        var uri = MessedUpSearchA.Services.SupportReport.BuildMailto(
            vm.AppVersion, MessedUpSearchA.Services.Localization.Localizer.Instance["Settings.ReportIntro"]);
        top?.Launcher.LaunchUriAsync(new System.Uri(uri));
    }

    private async void OnChooseMediaFolderClick(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);

        if (top is null || (top as Window)?.DataContext is not MainWindowViewModel vm)
            return;

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Куда складывать аватарки",
            AllowMultiple = false
        });

        if (folders.Count == 0)
            return;

        vm.SetMediaFolder(folders[0].TryGetLocalPath() ?? folders[0].Path.LocalPath);
    }
}
