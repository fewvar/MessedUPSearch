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

    private void OnBeatRowClick(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: Beat beat } && DataContext is BeatsViewModel vm)
            vm.BeginEditBeat(beat);
    }
}
