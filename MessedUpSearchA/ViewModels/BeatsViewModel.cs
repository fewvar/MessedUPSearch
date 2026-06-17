using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessedUpSearchA.Data;
using MessedUpSearchA.Models;

namespace MessedUpSearchA.ViewModels;

public partial class BeatsViewModel : ViewModelBase
{
    public ObservableCollection<Beat> Beats { get; } = new();

    public ObservableCollection<ArtistPick> ArtistPicks { get; } = new();

    public IReadOnlyList<string> StatusOptions { get; } = new[] { "POTENTIAL", "FREE", "SOLD" };

    public IReadOnlyList<string> TypeOptions { get; } =
        new[] { "ALL", "Rage", "Plugg", "Jerk", "Cloud", "Phonk", "Dark", "Ambient", "Trap", "Sad" };
    public IReadOnlyList<string> KeyOptions { get; } = BuildKeyOptions();
    public IReadOnlyList<string> PeriodOptions { get; } =
        new[] { "All time", "Today", "This week", "This month", "This year" };

    [ObservableProperty] private string _filterType = "ALL";
    [ObservableProperty] private string _filterKey = "ALL";
    [ObservableProperty] private string _filterPeriod = "All time";
    [ObservableProperty] private string _bpmMin = string.Empty;
    [ObservableProperty] private string _bpmMax = string.Empty;
    [ObservableProperty] private string _searchText = string.Empty;

    [ObservableProperty] private bool _isPreviewVisible;

    private readonly List<Beat> _allBeats = new();

    partial void OnFilterTypeChanged(string value) => ApplyFilter();
    partial void OnFilterKeyChanged(string value) => ApplyFilter();
    partial void OnFilterPeriodChanged(string value) => ApplyFilter();
    partial void OnBpmMinChanged(string value) => ApplyFilter();
    partial void OnBpmMaxChanged(string value) => ApplyFilter();
    partial void OnSearchTextChanged(string value) => ApplyFilter();

    [ObservableProperty] private bool _isEditorOpen;
    [ObservableProperty] private bool _isEditMode;
    [ObservableProperty] private string _editorTitle = "ADD BEAT";

    [ObservableProperty] private string _editName = string.Empty;
    [ObservableProperty] private string _editTags = string.Empty;
    [ObservableProperty] private string _editBpm = string.Empty;
    [ObservableProperty] private string _editKey = string.Empty;
    [ObservableProperty] private string _editStatus = "POTENTIAL";
    [ObservableProperty] private string _editLicense = string.Empty;

    private int _editingId;
    private string _editFilePath = string.Empty;

    public BeatsViewModel()
    {

        if (Design.IsDesignMode)
        {
            Beats.Add(new Beat { BeatName = "PREVIEW", AiTags = "Rage", Bpm = 145, StatusColor = "#F1C40F" });
            return;
        }

        LoadBeats();
    }

    public void LoadBeats()
    {
        using var db = new AppDbContext();
        _allBeats.Clear();
        _allBeats.AddRange(db.Beats.OrderByDescending(b => b.Id).ToList());
        IsPreviewVisible = _allBeats.Count == 0;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        const StringComparison oic = StringComparison.OrdinalIgnoreCase;
        IEnumerable<Beat> q = _allBeats;

        if (FilterType != "ALL")
            q = q.Where(b => (b.AiTags ?? "").Contains(FilterType, oic));

        if (FilterKey != "ALL")
            q = q.Where(b => b.Key == FilterKey);

        if (int.TryParse(BpmMin, out var min))
            q = q.Where(b => b.Bpm >= min);
        if (int.TryParse(BpmMax, out var max))
            q = q.Where(b => b.Bpm <= max);

        var cutoff = PeriodCutoff(FilterPeriod);
        if (cutoff is not null)
            q = q.Where(b => DateTime.TryParse(b.Added, out var d) && d >= cutoff);

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var s = SearchText.Trim();
            q = q.Where(b => (b.BeatName ?? "").Contains(s, oic) || (b.AiTags ?? "").Contains(s, oic));
        }

        Beats.Clear();
        foreach (var b in q)
            Beats.Add(b);
    }

    internal static DateTime? PeriodCutoff(string period) => period switch
    {
        "Today"      => DateTime.Today,
        "This week"  => DateTime.Today.AddDays(-7),
        "This month" => DateTime.Today.AddMonths(-1),
        "This year"  => DateTime.Today.AddYears(-1),
        _            => null
    };

    private static string[] BuildKeyOptions()
    {
        string[] roots = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        var list = new List<string> { "ALL" };
        foreach (var r in roots) { list.Add($"{r} major"); list.Add($"{r} minor"); }
        return list.ToArray();
    }

    public int ImportFolder(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            return 0;

        var audioFiles = Directory.EnumerateFiles(folderPath)
            .Where(f =>
            {
                var ext = Path.GetExtension(f).ToLowerInvariant();
                return ext == ".wav" || ext == ".mp3";
            })
            .ToList();

        if (audioFiles.Count == 0)
            return 0;

        using var db = new AppDbContext();
        var existing = db.Beats.Select(b => b.FilePath).ToHashSet();
        var today = DateTime.Now.ToString("yyyy-MM-dd");

        var added = 0;
        foreach (var file in audioFiles)
        {
            if (existing.Contains(file))
                continue;

            db.Beats.Add(new Beat
            {
                BeatName = Path.GetFileNameWithoutExtension(file),
                FilePath = file,
                Added = today,
                Status = "POTENTIAL",
                StatusColor = StatusColorFor("POTENTIAL")
            });
            added++;
        }

        if (added > 0)
        {
            db.SaveChanges();
            LoadBeats();
        }

        return added;
    }

    public void BeginAddBeat(string filePath)
    {
        _editingId = 0;
        _editFilePath = filePath;
        IsEditMode = false;
        EditorTitle = "ADD BEAT";
        EditName = Path.GetFileNameWithoutExtension(filePath);
        EditTags = string.Empty;
        EditBpm = string.Empty;
        EditKey = string.Empty;
        EditStatus = "POTENTIAL";
        EditLicense = string.Empty;
        LoadArtistPicks(0);
        IsEditorOpen = true;
    }

    public void BeginEditBeat(Beat beat)
    {
        _editingId = beat.Id;
        _editFilePath = beat.FilePath;
        IsEditMode = true;
        EditorTitle = "EDIT BEAT";
        EditName = beat.BeatName;
        EditTags = beat.AiTags;
        EditBpm = beat.Bpm == 0 ? string.Empty : beat.Bpm.ToString();
        EditKey = beat.Key;
        EditStatus = string.IsNullOrWhiteSpace(beat.Status) ? "POTENTIAL" : beat.Status;
        EditLicense = beat.LicenseType;
        LoadArtistPicks(beat.Id);
        IsEditorOpen = true;
    }

    private void LoadArtistPicks(int beatId)
    {
        using var db = new AppDbContext();
        var assigned = beatId == 0
            ? new HashSet<int>()
            : db.SentBeatsLog.Where(s => s.BeatId == beatId).Select(s => s.ArtistId).ToHashSet();

        ArtistPicks.Clear();
        foreach (var a in db.Artists.OrderBy(a => a.Nickname).ToList())
            ArtistPicks.Add(new ArtistPick { Id = a.Id, Nickname = a.Nickname, IsSelected = assigned.Contains(a.Id) });
    }

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(EditName))
            return;

        int.TryParse(EditBpm, out var bpm);

        using var db = new AppDbContext();

        Beat beat;
        if (_editingId == 0)
        {
            beat = new Beat { Added = DateTime.Now.ToString("yyyy-MM-dd") };
            db.Beats.Add(beat);
        }
        else
        {
            beat = db.Beats.First(b => b.Id == _editingId);
        }

        beat.BeatName = EditName.Trim();
        beat.AiTags = EditTags.Trim();
        beat.Bpm = bpm;
        beat.Key = EditKey.Trim();
        beat.Status = EditStatus;
        beat.StatusColor = StatusColorFor(EditStatus);
        beat.IsSold = EditStatus == "SOLD";
        beat.LicenseType = EditStatus == "SOLD" ? EditLicense.Trim() : string.Empty;
        if (_editingId == 0)
            beat.FilePath = _editFilePath;

        db.SaveChanges();
        SyncArtistLinks(db, beat.Id);
        db.SaveChanges();

        IsEditorOpen = false;
        LoadBeats();
    }

    private void SyncArtistLinks(AppDbContext db, int beatId)
    {
        var selected = ArtistPicks.Where(p => p.IsSelected).Select(p => p.Id).ToHashSet();
        var existing = db.SentBeatsLog.Where(s => s.BeatId == beatId).ToList();

        foreach (var log in existing.Where(l => !selected.Contains(l.ArtistId)))
            db.SentBeatsLog.Remove(log);

        var alreadyLinked = existing.Select(l => l.ArtistId).ToHashSet();
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        foreach (var artistId in selected.Where(id => !alreadyLinked.Contains(id)))
            db.SentBeatsLog.Add(new SentBeatsLog
            {
                BeatId = beatId,
                ArtistId = artistId,
                AssignedAt = now,
                IsSent = false
            });
    }

    [RelayCommand]
    private void Cancel() => IsEditorOpen = false;

    [RelayCommand]
    private void Delete()
    {
        if (_editingId != 0)
        {
            using var db = new AppDbContext();
            var beat = db.Beats.FirstOrDefault(b => b.Id == _editingId);
            if (beat is not null)
            {
                db.Beats.Remove(beat);
                db.SaveChanges();
            }
        }

        IsEditorOpen = false;
        LoadBeats();
    }

    private static string StatusColorFor(string status) => status switch
    {
        "POTENTIAL" => "#F1C40F",
        "FREE"      => "#1A1A1A",
        "SOLD"      => "#2ECC71",
        _           => "#888888"
    };
}
