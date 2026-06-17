using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessedUpSearchA.Data;
using MessedUpSearchA.Models;

namespace MessedUpSearchA.ViewModels;

public partial class ArtistsViewModel : ViewModelBase
{
    public ObservableCollection<Artist> Artists { get; } = new();

    public ObservableCollection<Beat> AssignedBeats { get; } = new();

    public IReadOnlyList<string> CrmStatusOptions { get; } =
        new[] { "", "NO REPLY", "OK", "POSTED FREE" };

    public IReadOnlyList<string> TypeOptions { get; } =
        new[] { "ALL", "Rage", "Plugg", "Jerk", "Cloud", "Phonk", "Dark", "Ambient", "Trap", "Sad", "Emo" };
    public IReadOnlyList<string> LanguageOptions { get; } =
        new[] { "ALL", "English", "Russian", "French", "Spanish", "German", "Unknown" };
    public IReadOnlyList<string> PeriodOptions { get; } =
        new[] { "All time", "Today", "This week", "This month", "This year" };

    [ObservableProperty] private string _filterType = "ALL";
    [ObservableProperty] private string _filterLanguage = "ALL";
    [ObservableProperty] private string _filterPeriod = "All time";
    [ObservableProperty] private string _playsMin = string.Empty;
    [ObservableProperty] private string _playsMax = string.Empty;
    [ObservableProperty] private string _searchText = string.Empty;

    [ObservableProperty] private bool _isPreviewVisible;

    private readonly List<Artist> _allArtists = new();

    partial void OnFilterTypeChanged(string value) => ApplyFilter();
    partial void OnFilterLanguageChanged(string value) => ApplyFilter();
    partial void OnFilterPeriodChanged(string value) => ApplyFilter();
    partial void OnPlaysMinChanged(string value) => ApplyFilter();
    partial void OnPlaysMaxChanged(string value) => ApplyFilter();
    partial void OnSearchTextChanged(string value) => ApplyFilter();

    [ObservableProperty] private bool _isEditorOpen;
    [ObservableProperty] private bool _isEditMode;
    [ObservableProperty] private string _editorTitle = "ADD ARTIST";

    [ObservableProperty] private string _editNickname = string.Empty;
    [ObservableProperty] private string _editScLink = string.Empty;
    [ObservableProperty] private string _editGenreTags = string.Empty;
    [ObservableProperty] private string _editTotalPlays = string.Empty;
    [ObservableProperty] private string _editIgLink = string.Empty;
    [ObservableProperty] private string _editSpotifyLink = string.Empty;
    [ObservableProperty] private string _editLanguage = string.Empty;
    [ObservableProperty] private string _editCrmStatus = string.Empty;
    [ObservableProperty] private string _editNotes = string.Empty;

    private int _editingId;

    public ArtistsViewModel()
    {

        if (Design.IsDesignMode)
        {
            Artists.Add(new Artist { Nickname = "PREVIEW", AiGenreTags = "Rage", TotalPlays = 8120, AvatarColor = "#E74C3C" });
            return;
        }

        LoadArtists();
    }

    public void LoadArtists()
    {
        using var db = new AppDbContext();
        _allArtists.Clear();
        _allArtists.AddRange(db.Artists.OrderByDescending(a => a.Id).ToList());
        IsPreviewVisible = _allArtists.Count == 0;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        const StringComparison oic = StringComparison.OrdinalIgnoreCase;
        IEnumerable<Artist> q = _allArtists;

        if (FilterType != "ALL")
            q = q.Where(a => (a.AiGenreTags ?? "").Contains(FilterType, oic));

        if (FilterLanguage != "ALL")
            q = q.Where(a => (a.Language ?? "").Contains(FilterLanguage, oic)
                          || (a.Language ?? "").Equals(LangShort(FilterLanguage), oic));

        if (int.TryParse(PlaysMin, out var min))
            q = q.Where(a => a.TotalPlays >= min);
        if (int.TryParse(PlaysMax, out var max))
            q = q.Where(a => a.TotalPlays <= max);

        var cutoff = BeatsViewModel.PeriodCutoff(FilterPeriod);
        if (cutoff is not null)
            q = q.Where(a => DateTime.TryParse(a.LastTrackDate, out var d) && d >= cutoff);

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var s = SearchText.Trim();
            q = q.Where(a => (a.Nickname ?? "").Contains(s, oic) || (a.AiGenreTags ?? "").Contains(s, oic));
        }

        Artists.Clear();
        foreach (var a in q)
            Artists.Add(a);
    }

    private static string LangShort(string full) => full switch
    {
        "English" => "ENG",
        "Russian" => "RU",
        "French"  => "FR",
        "Spanish" => "ES",
        "German"  => "DE",
        _         => full
    };

    public void BeginAddArtist()
    {
        _editingId = 0;
        IsEditMode = false;
        EditorTitle = "ADD ARTIST";
        EditNickname = string.Empty;
        EditScLink = string.Empty;
        EditGenreTags = string.Empty;
        EditTotalPlays = string.Empty;
        EditIgLink = string.Empty;
        EditSpotifyLink = string.Empty;
        EditLanguage = string.Empty;
        EditCrmStatus = string.Empty;
        EditNotes = string.Empty;
        AssignedBeats.Clear();
        IsEditorOpen = true;
    }

    public void BeginEditArtist(Artist artist)
    {
        _editingId = artist.Id;
        IsEditMode = true;
        EditorTitle = "EDIT ARTIST";
        EditNickname = artist.Nickname;
        EditScLink = artist.ScLink ?? string.Empty;
        EditGenreTags = artist.AiGenreTags;
        EditTotalPlays = artist.TotalPlays == 0 ? string.Empty : artist.TotalPlays.ToString();
        EditIgLink = artist.IgLink;
        EditSpotifyLink = artist.SpotifyLink;
        EditLanguage = artist.Language;
        EditCrmStatus = artist.CrmStatus;
        EditNotes = artist.Notes;
        LoadAssignedBeats(artist.Id);
        IsEditorOpen = true;
    }

    private void LoadAssignedBeats(int artistId)
    {
        AssignedBeats.Clear();
        using var db = new AppDbContext();
        var beatIds = db.SentBeatsLog.Where(s => s.ArtistId == artistId).Select(s => s.BeatId).ToList();
        foreach (var beat in db.Beats.Where(b => beatIds.Contains(b.Id)).OrderBy(b => b.BeatName).ToList())
            AssignedBeats.Add(beat);
    }

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(EditNickname))
            return;

        int.TryParse(EditTotalPlays, out var plays);

        using var db = new AppDbContext();

        Artist artist;
        if (_editingId == 0)
        {
            artist = new Artist
            {
                CreatedAt = DateTime.Now.ToString("yyyy-MM-dd"),
                AvatarColor = RandomColor()
            };
            db.Artists.Add(artist);
        }
        else
        {
            artist = db.Artists.First(a => a.Id == _editingId);
        }

        artist.Nickname = EditNickname.Trim();

        var link = EditScLink.Trim();
        artist.ScLink = string.IsNullOrWhiteSpace(link) ? null : link;
        artist.AiGenreTags = EditGenreTags.Trim();
        artist.TotalPlays = plays;
        artist.IgLink = EditIgLink.Trim();
        artist.SpotifyLink = EditSpotifyLink.Trim();
        artist.Language = EditLanguage.Trim();
        artist.CrmStatus = EditCrmStatus;
        artist.Notes = EditNotes.Trim();

        db.SaveChanges();

        IsEditorOpen = false;
        LoadArtists();
    }

    [RelayCommand]
    private void Cancel() => IsEditorOpen = false;

    [RelayCommand]
    private void Delete()
    {
        if (_editingId != 0)
        {
            using var db = new AppDbContext();
            var artist = db.Artists.FirstOrDefault(a => a.Id == _editingId);
            if (artist is not null)
            {
                db.Artists.Remove(artist);
                db.SaveChanges();
            }
        }

        IsEditorOpen = false;
        LoadArtists();
    }

    public void ToggleFavorite(Artist artist) => ToggleFlag(artist.Id, fav: true);

    public void ToggleRedFlag(Artist artist) => ToggleFlag(artist.Id, fav: false);

    private void ToggleFlag(int id, bool fav)
    {
        using var db = new AppDbContext();
        var artist = db.Artists.FirstOrDefault(a => a.Id == id);
        if (artist is null)
            return;

        if (fav) artist.IsFavorite = !artist.IsFavorite;
        else     artist.IsRedFlagged = !artist.IsRedFlagged;

        db.SaveChanges();
        LoadArtists();
    }

    private static string RandomColor()
    {
        string[] palette = { "#E74C3C", "#5DADE2", "#2ECC71", "#F1C40F", "#9B59B6", "#E67E22", "#1ABC9C" };
        return palette[Random.Shared.Next(palette.Length)];
    }
}
