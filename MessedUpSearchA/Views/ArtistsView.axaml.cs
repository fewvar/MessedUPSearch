using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MessedUpSearchA.Converters;
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

    private void OnParserClick(object? sender, RoutedEventArgs e)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        (owner?.DataContext as MainWindowViewModel)?.ShowParser();
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

    private void OnIgLinkClick(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: Artist artist })
            OpenLink(SocialLinks.ResolveInstagram(artist.IgLink));
        e.Handled = true;
    }

    private void OnSpotifyLinkClick(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: Artist artist })
            OpenLink(SocialLinks.ResolveSpotify(artist.SpotifyLink));
        e.Handled = true;
    }

    private void OpenLink(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        var launcher = TopLevel.GetTopLevel(this)?.Launcher;
        launcher?.LaunchUriAsync(new Uri(url));
    }

    private void OnToggleFavorite(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: Artist artist } && DataContext is ArtistsViewModel vm)
            vm.ToggleFavorite(artist);
        e.Handled = true;
    }

    private void OnToggleRedFlag(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: Artist artist } && DataContext is ArtistsViewModel vm)
            vm.ToggleRedFlag(artist);
        e.Handled = true;
    }
}
