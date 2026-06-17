using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MessedUpSearchA.Models;
using MessedUpSearchA.ViewModels;

namespace MessedUpSearchA.Views;

public partial class ArtistsView : UserControl
{
    public ArtistsView()
    {
        InitializeComponent();
    }

    private void OnCrmClick(object? sender, RoutedEventArgs e)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        (owner?.DataContext as MainWindowViewModel)?.ShowCrm();
    }

    private void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        (owner?.DataContext as MainWindowViewModel)?.ShowSettings();
    }

    private void OnAddArtistClick(object? sender, RoutedEventArgs e)
    {
        (DataContext as ArtistsViewModel)?.BeginAddArtist();
    }

    private void OnArtistRowClick(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: Artist artist } && DataContext is ArtistsViewModel vm)
            vm.BeginEditArtist(artist);
    }

    private void OnToggleFavorite(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: Artist artist } && DataContext is ArtistsViewModel vm)
            vm.ToggleFavorite(artist);
        e.Handled = true;   // не открывать редактор строки
    }

    private void OnToggleRedFlag(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: Artist artist } && DataContext is ArtistsViewModel vm)
            vm.ToggleRedFlag(artist);
        e.Handled = true;
    }
}
