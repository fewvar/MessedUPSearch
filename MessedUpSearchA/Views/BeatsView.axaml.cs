using MessedUpSearchA.Data;
using System.Linq;
using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MessedUpSearchA.Models;
using MessedUpSearchA.ViewModels;

namespace MessedUpSearchA.Views;

public partial class BeatsView : UserControl
{
    public BeatsView()
    {
        InitializeComponent();
    }

    private void OnStatsClick(object? sender, RoutedEventArgs e) =>
        ((TopLevel.GetTopLevel(this) as Window)?.DataContext as MainWindowViewModel)?.StatsVm.Open();

    private void OnTodayClick(object? sender, RoutedEventArgs e) =>
        ((TopLevel.GetTopLevel(this) as Window)?.DataContext as MainWindowViewModel)?.ShowToday();

    private async void OnAddBeatClick(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null || DataContext is not BeatsViewModel vm)
            return;

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Выбери бит (WAV / MP3)",
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("Audio") { Patterns = new[] { "*.wav", "*.mp3" } }
            }
        });

        if (files.Count == 0)
            return;

        var path = files[0].TryGetLocalPath() ?? files[0].Path.LocalPath;
        vm.BeginAddBeat(path);
    }

    private async void OnImportFolderClick(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null || DataContext is not BeatsViewModel vm)
            return;

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Выбери папку с битами",
            AllowMultiple = false
        });

        if (folders.Count == 0)
            return;

        var path = folders[0].TryGetLocalPath() ?? folders[0].Path.LocalPath;
        vm.ImportFolder(path);
    }

    /// <summary>▶ в строке. Плеер живёт в главном окне — достаём его через окно.</summary>
    private void OnPlayBeatClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: Beat beat } &&
            TopLevel.GetTopLevel(this) is Window { DataContext: MainWindowViewModel main })
        {
            main.Player.PlayBeat(beat);
        }

        e.Handled = true;
    }

    /// <summary>
    /// ▶ у артиста из выдачи. Из большого индекса — его лучший трек (стримом),
    /// из базы — его треки, как в карточке артиста.
    /// </summary>
    private void OnPlaySimilarClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: SimilarArtistItem item } ||
            TopLevel.GetTopLevel(this) is not Window { DataContext: MainWindowViewModel main })
        {
            return;
        }

        // Первым — трек, ближайший к биту по звуку (его выбрал анализ): сразу слышно, почему артист в выдаче.
        // Если артист уже в базе — дальше его остальные треки, без повтора.
        var tracks = new List<ArtistTrack>();
        if (item.CanPlay)
        {
            tracks.Add(new ArtistTrack
            {
                Title = item.TopTrackTitle, SourcePlatform = item.Platform,
                SourceTrackId = item.TopTrackId, Url = item.TopTrackUrl
            });
        }

        if (item.ArtistId is { } id and > 0)
        {
            using var db = new AppDbContext();
            tracks.AddRange(db.ArtistTracks
                .Where(t => t.ArtistId == id && t.Url != item.TopTrackUrl)
                .OrderByDescending(t => t.PlayCount).Take(30).ToList());
        }

        if (tracks.Count > 0)
            main.Player.PlayArtistTracks(tracks, 0, item.Artist);
    }

    private void OnPickSimilarClick(object? sender, RoutedEventArgs e) =>
        (DataContext as BeatsViewModel)?.NotifyPickedChanged();

    private async void OnImportSimilarClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: SimilarArtistItem item } && DataContext is BeatsViewModel vm)
            await vm.ImportArtistAsync(item);
    }

    private async void OnOpenSimilarClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: SimilarArtistItem { CanOpen: true } item } &&
            TopLevel.GetTopLevel(this)?.Launcher is { } launcher &&
            Uri.TryCreate(item.SourceUrl, UriKind.Absolute, out var uri))
        {
            await launcher.LaunchUriAsync(uri);
        }
    }

    private void OnBeatRowClick(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: Beat beat } && DataContext is BeatsViewModel vm)
            vm.BeginEditBeat(beat);
    }

    private void OnSortHeaderClick(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { Tag: string column } && DataContext is BeatsViewModel vm)
            vm.SortBy(column);
        e.Handled = true;
    }
}
