using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessedUpSearchA.Data;
using MessedUpSearchA.Models;
using MessedUpSearchA.Services.Localization;
using MessedUpSearchA.Services.Ml;

namespace MessedUpSearchA.ViewModels;

public partial class ArtistsViewModel : ViewModelBase
{
    public ObservableCollection<Artist> Artists { get; } = new();

    public ObservableCollection<Beat> AssignedBeats { get; } = new();

    /// <summary>Мои биты, отсортированные по тому, насколько они подходят артисту.</summary>
    public ObservableCollection<SimilarBeatItem> SimilarBeats { get; } = new();

    public IReadOnlyList<string> CrmStatusOptions { get; } =
        new[] { "", "NO REPLY", "OK", "POSTED FREE" };

    public IReadOnlyList<string> TypeOptions { get; } =
        new[] { "ALL", "Rage", "Plugg", "Jerk", "Cloud", "Phonk", "Dark", "Ambient", "Trap", "Sad", "Emo" };
    // Список ровно тот, что умеет различать ArtistLanguageGuesser, плюс "Common"
    // для случаев, когда определить не удалось.
    public IReadOnlyList<string> LanguageOptions { get; } =
        new[] { "ALL", "English", "Russian", "Vietnamese", "Spanish", "French", "German",
                "Portuguese", "Italian", "Japanese", "Korean", "Chinese", "Arabic", "Common" };
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
    [ObservableProperty] private string _editorTitle = string.Empty;

    [ObservableProperty] private string _editNickname = string.Empty;
    [ObservableProperty] private string _editScLink = string.Empty;
    [ObservableProperty] private string _editGenreTags = string.Empty;
    [ObservableProperty] private string _editTotalPlays = string.Empty;
    [ObservableProperty] private string _editIgLink = string.Empty;
    [ObservableProperty] private string _editSpotifyLink = string.Empty;
    [ObservableProperty] private string _editLanguage = string.Empty;
    [ObservableProperty] private string _editCrmStatus = string.Empty;
    [ObservableProperty] private string _editNotes = string.Empty;

    [ObservableProperty] private bool _hasSimilarBeats;

    /// <summary>Модель обучена на конкретных 32 артистах — этого среди них может не быть.</summary>
    [ObservableProperty] private bool _isArtistKnownToModel;

    [ObservableProperty] private bool _isSimilarityAvailable;

    /// <summary>Только для показа: аватарку качает парсер, руками её не задать.</summary>
    [ObservableProperty] private string _editAvatarPath = string.Empty;
    [ObservableProperty] private string _editAvatarColor = "#888888";

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

    /// <summary>Старые записи могли храниться сокращённо — учитываем оба написания.</summary>
    private static string LangShort(string full) => full switch
    {
        "English"    => "ENG",
        "Russian"    => "RU",
        "French"     => "FR",
        "Spanish"    => "ES",
        "German"     => "DE",
        "Vietnamese" => "VI",
        "Portuguese" => "PT",
        "Italian"    => "IT",
        "Japanese"   => "JP",
        "Korean"     => "KR",
        "Chinese"    => "CN",
        "Arabic"     => "AR",
        _            => full
    };

    public void BeginAddArtist()
    {
        _editingId = 0;
        IsEditMode = false;
        EditorTitle = Localizer.Instance["ArtistEditor.Add"];
        EditNickname = string.Empty;
        EditScLink = string.Empty;
        EditGenreTags = string.Empty;
        EditTotalPlays = string.Empty;
        EditIgLink = string.Empty;
        EditSpotifyLink = string.Empty;
        EditLanguage = string.Empty;
        EditCrmStatus = string.Empty;
        EditNotes = string.Empty;
        EditAvatarPath = string.Empty;
        EditAvatarColor = "#888888";
        AssignedBeats.Clear();
        LoadSimilarBeats(0, string.Empty);
        IsEditorOpen = true;
    }

    public void BeginEditArtist(Artist artist)
    {
        _editingId = artist.Id;
        IsEditMode = true;
        EditorTitle = Localizer.Instance["ArtistEditor.Edit"];
        EditNickname = artist.Nickname;
        EditScLink = artist.ScLink ?? string.Empty;
        EditGenreTags = artist.AiGenreTags;
        EditTotalPlays = artist.TotalPlays == 0 ? string.Empty : artist.TotalPlays.ToString();
        EditIgLink = artist.IgLink;
        EditSpotifyLink = artist.SpotifyLink;
        EditLanguage = artist.Language;
        EditCrmStatus = artist.CrmStatus;
        EditNotes = artist.Notes;
        EditAvatarPath = artist.AvatarPath;
        EditAvatarColor = string.IsNullOrWhiteSpace(artist.AvatarColor) ? "#888888" : artist.AvatarColor;
        LoadAssignedBeats(artist.Id);
        LoadSimilarBeats(artist.Id, artist.Nickname);
        IsEditorOpen = true;
    }

    /// <summary>
    /// Разворачиваем выдачу анализа: бит знает, на кого он похож, а нам нужно
    /// обратное — какие биты подходят этому артисту.
    ///
    /// Связь идёт по имени, и это слабое место: модель знает своих 32 артистов
    /// (Yeat, Carti, nettspend...), а в базе лежат те, кого нашёл парсер. Имена
    /// сверяем нормализованно — без регистра, пробелов, точек и эмодзи, так что
    /// "playboi carty" и "Playboi Carty" совпадут, а "TuDrill🔥" найдётся как
    /// "tudrill". Разное написание одного артиста ("2Holis" против "2hollis")
    /// это не лечит — тут нужен уже словарь синонимов, а не нормализация.
    /// </summary>
    private void LoadSimilarBeats(int artistId, string nickname)
    {
        SimilarBeats.Clear();
        HasSimilarBeats = false;
        IsArtistKnownToModel = false;
        IsSimilarityAvailable = ModelStore.IsIndexReady();

        if (!IsSimilarityAvailable || string.IsNullOrWhiteSpace(nickname))
            return;

        var wanted = NormalizeName(nickname);
        // Модель знает артиста, если он в индексе по имени или если очередь уже послушала его треки.
        IsArtistKnownToModel = KnownArtists().Contains(wanted) ||
                               (artistId != 0 && LiveIndex.HasVectors(artistId));

        using var db = new AppDbContext();

        var matches = db.BeatSimilarities.ToList()
            .Where(s => (artistId != 0 && s.ArtistId == artistId) || NormalizeName(s.Artist) == wanted)
            .ToList();

        if (matches.Count == 0)
            return;

        var beatIds = matches.Select(m => m.BeatId).ToList();
        var beats = db.Beats.Where(b => beatIds.Contains(b.Id)).ToDictionary(b => b.Id);

        foreach (var match in matches.OrderByDescending(m => m.Percent))
        {
            if (!beats.TryGetValue(match.BeatId, out var beat))
                continue;

            SimilarBeats.Add(new SimilarBeatItem
            {
                BeatId = beat.Id,
                BeatName = beat.BeatName,
                Percent = match.Percent,
                StatusColor = string.IsNullOrWhiteSpace(beat.StatusColor) ? "#888888" : beat.StatusColor
            });
        }

        HasSimilarBeats = SimilarBeats.Count > 0;
    }

    private static HashSet<string>? _knownArtists;

    /// <summary>Имена из индекса модели. Читаются один раз — файл на диске не меняется.</summary>
    private static HashSet<string> KnownArtists()
    {
        if (_knownArtists is not null)
            return _knownArtists;

        try
        {
            _knownArtists = ArtistIndex.LoadNamesOnly(ModelStore.IndexPath)
                .Select(NormalizeName)
                .ToHashSet();
        }
        catch
        {
            _knownArtists = new HashSet<string>();
        }

        return _knownArtists;
    }

    private static string NormalizeName(string raw) =>
        new string((raw ?? string.Empty)
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());

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
