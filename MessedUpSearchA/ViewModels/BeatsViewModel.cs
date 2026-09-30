using MessedUpSearchA.Services.Audio;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessedUpSearchA.Data;
using MessedUpSearchA.Models;
using MessedUpSearchA.Services;
using MessedUpSearchA.Services.Localization;
using MessedUpSearchA.Services.Parsing;
using MessedUpSearchA.Services.Ml;

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

    /// <summary>Какой бит сейчас в плеере — его строка подсвечивается. null — никакой.</summary>
    [ObservableProperty] private int? _playingBeatId;

    /// <summary>Играет ли плеер — чтобы в строке играющего бита был значок паузы, а не ▶.</summary>
    [ObservableProperty] private bool _isPlayerPlaying;

    private readonly List<Beat> _allBeats = new();

    /// <summary>Сортировка по заголовку: «Bpm:desc» / «Bpm:asc» / пусто (исходный порядок — новые сверху).</summary>
    [ObservableProperty] private string _sortState = string.Empty;

    /// <summary>Бит, который только что стал SOLD, — его точка статуса вспыхнет в таблице.</summary>
    [ObservableProperty] private int _justSoldBeatId;

    public void SortBy(string column)
    {
        SortState = TableSort.Next(SortState, column);
        ApplyFilter();
    }

    private static Func<Beat, object?>? SortKey(string column) => column switch
    {
        "Name" => b => b.BeatName,
        "Genre" => b => b.AiTags,
        "Bpm" => b => b.Bpm,
        "Key" => b => b.Key,
        "Added" => b => b.Added,
        "Status" => b => b.Status,
        _ => null
    };

    partial void OnFilterTypeChanged(string value) => ApplyFilter();
    partial void OnFilterKeyChanged(string value) => ApplyFilter();
    partial void OnFilterPeriodChanged(string value) => ApplyFilter();
    partial void OnBpmMinChanged(string value) => ApplyFilter();
    partial void OnBpmMaxChanged(string value) => ApplyFilter();
    partial void OnSearchTextChanged(string value) => ApplyFilter();

    [ObservableProperty] private bool _isEditorOpen;
    [ObservableProperty] private bool _isEditMode;
    [ObservableProperty] private string _editorTitle = string.Empty;

    [ObservableProperty] private string _editName = string.Empty;
    [ObservableProperty] private string _editTags = string.Empty;
    [ObservableProperty] private string _editBpm = string.Empty;
    [ObservableProperty] private string _editKey = string.Empty;
    [ObservableProperty] private string _editStatus = "POTENTIAL";
    [ObservableProperty] private string _editLicense = string.Empty;

    public ObservableCollection<SimilarArtistItem> SimilarArtists { get; } = new();

    [ObservableProperty] private bool _isAnalyzing;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowSimilarHint))] private string _analysisStatus = string.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowSimilarHint))] private bool _hasSimilarity;

    /// <summary>«Нажми АНАЛИЗ» — только пока нет ни результата, ни статуса («Слушаю бит…», ошибка).</summary>
    public bool ShowSimilarHint => !HasSimilarity && string.IsNullOrEmpty(AnalysisStatus);

    /// <summary>«звучит как: Osamason · Summrs» — крупные артисты-ориентиры, им бит не продашь.</summary>
    [ObservableProperty] private string _styleReferences = string.Empty;

    /// <summary>Артиста из выдачи добавили в базу — вкладке артистов пора перечитать список.</summary>
    public event Action? ArtistImported;

    /// <summary>Индекс не скачался — второй раз за сессию не пробуем, чтобы не тормозить каждый анализ.</summary>
    private static bool _indexDownloadFailed;

    private CancellationTokenSource? _analysisCts;

    /// <summary>
    /// Анализ нового бита нельзя записать сразу: у него ещё нет номера в базе,
    /// к которому привязывается результат. Держим его здесь и сохраняем вместе
    /// с самим битом — иначе пользователь видит выдачу на экране, а она пропадает.
    /// </summary>
    private SimilarityResult? _pendingSimilarity;

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

        q = TableSort.Apply(q, SortState, SortKey);

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
        EditorTitle = Localizer.Instance["BeatEditor.Add"];
        EditName = Path.GetFileNameWithoutExtension(filePath);
        EditTags = string.Empty;
        EditBpm = string.Empty;
        EditKey = string.Empty;
        EditStatus = "POTENTIAL";
        EditLicense = string.Empty;
        LoadArtistPicks(0);
        LoadSimilarity(0);
        IsEditorOpen = true;
    }

    public void BeginEditBeat(Beat beat)
    {
        _editingId = beat.Id;
        _editFilePath = beat.FilePath;
        IsEditMode = true;
        EditorTitle = Localizer.Instance["BeatEditor.Edit"];
        EditName = beat.BeatName;
        EditTags = beat.AiTags;
        EditBpm = beat.Bpm == 0 ? string.Empty : beat.Bpm.ToString();
        EditKey = beat.Key;
        EditStatus = string.IsNullOrWhiteSpace(beat.Status) ? "POTENTIAL" : beat.Status;
        EditLicense = beat.LicenseType;
        LoadArtistPicks(beat.Id);
        LoadSimilarity(beat.Id);
        IsEditorOpen = true;
    }

    /// <summary>
    /// Анализ идёт около секунды; модель едет с приложением. Результат пишем в базу и при
    /// следующем открытии карточки достаём оттуда. Качается только большой индекс артистов.
    /// </summary>
    [RelayCommand]
    private async Task AnalyzeSimilarityAsync()
    {
        if (IsAnalyzing)
            return;

        if (string.IsNullOrWhiteSpace(_editFilePath) || !File.Exists(_editFilePath))
        {
            AnalysisStatus = Localizer.Instance["Analysis.NoFile"];
            return;
        }

        if (!MlAssets.IsReady())
        {
            AnalysisStatus = Localizer.Instance["Analysis.NoIndex"];
            return;
        }

        IsAnalyzing = true;
        _analysisCts = new CancellationTokenSource();

        try
        {
            if (!IndexStore.IsReady() && !_indexDownloadFailed)
            {
                AnalysisStatus = Localizer.Instance["Analysis.DownloadingIndex"];
                try
                {
                    await IndexStore.DownloadAsync(_analysisCts.Token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Без большого индекса остаётся «звучит как» — не повод падать.
                    _indexDownloadFailed = true;
                    AppLog.Write($"индекс артистов не скачался: {ex.Message}");
                }
            }

            AnalysisStatus = Localizer.Instance["Analysis.Listening"];

            var path = _editFilePath;
            var ct = _analysisCts.Token;
            var result = await Task.Run(() =>
            {
                var embedder = EffNetEmbedder.GetShared();
                var references = TargetIndex.Load(MlAssets.ReferencesPath);
                var hubs = HubCorrection.TryLoad(MlAssets.BackgroundPath, embedder.Dimension);
                return new BeatSimilarityService(embedder)
                    .AnalyzeAll(path, references, IndexStore.TryLoad(), UserArtistIdsByUrl(), hubs, ct: ct);
            }, ct);

            if (_editingId == 0)
                _pendingSimilarity = result;   // бит ещё не сохранён — запишем при сохранении
            else
                SaveSimilarity(_editingId, result);
            ShowSimilarity(result);
            UiSounds.Play(UiSound.AnalysisDone);

            AnalysisStatus = string.Empty;
        }
        catch (OperationCanceledException)
        {
            AnalysisStatus = Localizer.Instance["Analysis.Cancelled"];
        }
        catch (Exception ex)
        {
            AnalysisStatus = Localizer.Instance.Format("Analysis.Failed", ex.Message);
        }
        finally
        {
            IsAnalyzing = false;
            _analysisCts?.Dispose();
            _analysisCts = null;
        }
    }

    [RelayCommand]
    private void CancelAnalysis() => _analysisCts?.Cancel();

    private static Dictionary<string, int> UserArtistIdsByUrl()
    {
        using var db = new AppDbContext();
        return db.Artists
            .Where(a => a.SourceUrl != null && a.SourceUrl != "")
            .Select(a => new { a.SourceUrl, a.Id })
            .ToList()
            .GroupBy(a => a.SourceUrl!)
            .ToDictionary(g => g.Key, g => g.First().Id);
    }

    private void SaveSimilarity(int beatId, SimilarityResult result)
    {
        var matches = result.Targets.Concat(result.References).ToList();
        if (beatId == 0 || matches.Count == 0)
            return;

        using var db = new AppDbContext();

        db.BeatSimilarities.RemoveRange(db.BeatSimilarities.Where(s => s.BeatId == beatId));

        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        for (var i = 0; i < matches.Count; i++)
        {
            db.BeatSimilarities.Add(new BeatSimilarity
            {
                BeatId = beatId,
                Artist = matches[i].Artist,
                ArtistId = matches[i].ArtistId,
                Kind = matches[i].Kind.ToString(),
                Platform = matches[i].Platform,
                SourceId = matches[i].SourceId,
                SourceUrl = matches[i].SourceUrl,
                AvatarUrl = matches[i].AvatarUrl,
                Plays = matches[i].Plays,
                TopTrackId = matches[i].TopTrackId,
                TopTrackUrl = matches[i].TopTrackUrl,
                TopTrackTitle = matches[i].TopTrackTitle,
                Rank = i + 1,
                Percent = matches[i].Percent,
                Similarity = matches[i].Similarity,
                ComputedAt = now
            });
        }

        db.SaveChanges();
    }

    private void LoadSimilarity(int beatId)
    {
        SimilarArtists.Clear();
        HasSimilarity = false;
        AnalysisStatus = string.Empty;
        _pendingSimilarity = null;
        StyleReferences = string.Empty;

        if (beatId == 0)
            return;

        using var db = new AppDbContext();
        var saved = db.BeatSimilarities
            .Where(s => s.BeatId == beatId)
            .OrderBy(s => s.Rank)
            .ToList();

        // Строки до v0.7 без Kind считаются целями — так их и показывали раньше.
        ShowSimilarity(
            saved.Where(s => s.Kind != nameof(MatchKind.Reference)).Select(ToItem),
            saved.Where(s => s.Kind == nameof(MatchKind.Reference)).Select(s => s.Artist));
    }

    private static SimilarArtistItem ToItem(BeatSimilarity s) => new()
    {
        Artist = s.Artist, Percent = s.Percent, ArtistId = s.ArtistId,
        Platform = s.Platform, SourceId = s.SourceId, SourceUrl = s.SourceUrl, AvatarUrl = s.AvatarUrl,
        Plays = s.Plays, TopTrackId = s.TopTrackId, TopTrackUrl = s.TopTrackUrl, TopTrackTitle = s.TopTrackTitle
    };

    private void ShowSimilarity(SimilarityResult result) =>
        ShowSimilarity(
            result.Targets.Select(m => new SimilarArtistItem
            {
                Artist = m.Artist, Percent = m.Percent, ArtistId = m.ArtistId,
                Platform = m.Platform, SourceId = m.SourceId, SourceUrl = m.SourceUrl, AvatarUrl = m.AvatarUrl,
                Plays = m.Plays, TopTrackId = m.TopTrackId, TopTrackUrl = m.TopTrackUrl, TopTrackTitle = m.TopTrackTitle
            }),
            result.References.Select(m => m.Artist));

    private void ShowSimilarity(IEnumerable<SimilarArtistItem> items, IEnumerable<string> references)
    {
        SimilarArtists.Clear();
        var first = true;
        foreach (var item in items)
        {
            item.IsLeader = first;
            first = false;
            SimilarArtists.Add(item);
        }

        var names = references.ToList();
        StyleReferences = names.Count > 0
            ? Localizer.Instance.Format("BeatEditor.SoundsLike", string.Join(" · ", names))
            : string.Empty;

        HasSimilarity = SimilarArtists.Count > 0 || names.Count > 0;
    }

    /// <summary>➕ у артиста из большого индекса: импорт тем же путём, что и парсер.</summary>
    public async Task ImportArtistAsync(SimilarArtistItem item)
    {
        if (!item.CanImport)
            return;

        item.IsImporting = true;
        try
        {
            var id = await IndexArtistImporter.ImportAsync(
                item.Platform, item.SourceId, item.SourceUrl, item.Artist, item.AvatarUrl);

            item.ArtistId = id;
            item.JustImported = true;
            Toasts.Show(Localizer.Instance.Format("Toast.Added", item.Artist));

            // Все выдачи, где он встречался, теперь ведут на карточку в базе.
            using (var db = new AppDbContext())
            {
                foreach (var row in db.BeatSimilarities.Where(s => s.SourceUrl == item.SourceUrl))
                    row.ArtistId = id;
                db.SaveChanges();
            }

            ArtistImported?.Invoke();
        }
        catch (Exception ex)
        {
            AnalysisStatus = Localizer.Instance.Format("Analysis.ImportFailed", item.Artist, ex.Message);
            AppLog.Write($"импорт из выдачи: {item.Artist}: {ex}");
        }
        finally
        {
            item.IsImporting = false;
        }
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

        var becomesSold = EditStatus == "SOLD" && !beat.IsSold;

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

        if (_pendingSimilarity is not null)
        {
            SaveSimilarity(beat.Id, _pendingSimilarity);
            _pendingSimilarity = null;
        }

        IsEditorOpen = false;
        LoadBeats();

        if (becomesSold)
        {
            JustSoldBeatId = beat.Id;
            // Сбросить, чтобы вспышка не повторялась при следующей перерисовке таблицы (фильтр, сортировка).
            Avalonia.Threading.DispatcherTimer.RunOnce(() => JustSoldBeatId = 0, TimeSpan.FromSeconds(1.5));
        }
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
