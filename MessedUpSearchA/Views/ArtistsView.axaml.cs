using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
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

    /// <summary>▶ у трека: плейлист — все треки из карточки, начиная с нажатого.</summary>
    private void OnPlayTrackClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: ArtistTrackItem item } ||
            DataContext is not ArtistsViewModel vm ||
            TopLevel.GetTopLevel(this) is not Window { DataContext: MainWindowViewModel main })
        {
            return;
        }

        var tracks = vm.Tracks.Select(t => t.Track).ToList();
        main.Player.PlayArtistTracks(tracks, tracks.IndexOf(item.Track), vm.EditingArtistName);
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
        {
            var becomesFavorite = !artist.IsFavorite;
            vm.ToggleFavorite(artist);
            PopAfterReload(vm, artist.Id, "FavoriteToggle");
            if (becomesFavorite)
                Services.Audio.UiSounds.Play(Services.Audio.UiSound.Favorite);
        }
        e.Handled = true;
    }

    private void OnToggleRedFlag(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: Artist artist } && DataContext is ArtistsViewModel vm)
        {
            vm.ToggleRedFlag(artist);
            PopAfterReload(vm, artist.Id, "FlagToggle");
        }
        e.Handled = true;
    }

    /// <summary>
    /// Переключение перезагружает список — щёлкнутый значок уже заменён новым. Ждём, пока новые строки
    /// разложатся, находим строку того же артиста и «щёлкаем» её значком.
    /// </summary>
    private void PopAfterReload(ArtistsViewModel vm, int artistId, string toggleName) =>
        Dispatcher.UIThread.Post(() =>
        {
            var item = vm.Artists.FirstOrDefault(a => a.Id == artistId);
            if (item is null || ArtistsList.ContainerFromItem(item) is not { } row)
                return;
            Motion.Motion.Pop(row.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Name == toggleName));
        }, DispatcherPriority.Loaded);

    private void OnSortHeaderClick(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { Tag: string column } && DataContext is ArtistsViewModel vm)
            vm.SortBy(column);
        e.Handled = true;
    }
}
