using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessedUpSearchA.Data;
using MessedUpSearchA.Services.Parsing;
using MessedUpSearchA.Services.Parsing.Sources;

namespace MessedUpSearchA.ViewModels;

public partial class ParserViewModel : ViewModelBase
{
    public ObservableCollection<ParserResultItem> Results { get; } = new();

    public ObservableCollection<SourceToggle> SourceToggles { get; } = new()
    {
        new SourceToggle("AUDIUS", () => new AudiusSource()),
        new SourceToggle("SOUNDCLOUD", () => new SoundCloudSource()),
        new SourceToggle("BANDCAMP", () => new BandcampSource()),
        new SourceToggle("LAST.FM", () => new LastFmSource(AppSettings.Load().LastFmApiKey)),
        new SourceToggle("JAMENDO", () => new JamendoSource(AppSettings.Load().JamendoClientId))
    };

    public IReadOnlyList<string> GenreOptions { get; } =
        new[] { "Rage", "Plugg", "Jerk", "Cloud", "Phonk", "Trap", "Dark", "Ambient", "Sad", "Emo" };

    [ObservableProperty] private string _genreTag = string.Empty;
    [ObservableProperty] private string _playsMin = string.Empty;
    [ObservableProperty] private string _playsMax = string.Empty;
    [ObservableProperty] private string _freshDays = string.Empty;
    [ObservableProperty] private string _country = string.Empty;
    [ObservableProperty] private string _maxArtists = "12";
    [ObservableProperty] private string _tracksPerArtist = "10";

    [ObservableProperty] private bool _isResultsStep;
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private bool _isImporting;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string _failuresText = string.Empty;

    private CancellationTokenSource? _cts;

    public ParserViewModel()
    {
        foreach (var toggle in SourceToggles)
            toggle.PropertyChanged += OnSourceToggleChanged;
    }

    public bool HasFailures => !string.IsNullOrWhiteSpace(FailuresText);

    public bool IsCriteriaStep => !IsResultsStep;

    public bool IsResultsEmpty => IsResultsStep && Results.Count == 0;

    public bool HasEnabledSource => SourceToggles.Any(s => s.IsEnabled);

    public bool CanSearch => HasEnabledSource && !IsSearching;

    public int SelectedCount => Results.Count(r => r.IsSelected);

    public bool HasSelection => SelectedCount > 0 && !IsImporting;

    public string ImportButtonLabel => SelectedCount == 0
        ? "НИЧЕГО НЕ ВЫБРАНО"
        : $"ДОБАВИТЬ {SelectedCount} В БАЗУ";

    public string FoundLabel => $"Найдено {Results.Count}";

    partial void OnIsResultsStepChanged(bool value)
    {
        OnPropertyChanged(nameof(IsCriteriaStep));
        OnPropertyChanged(nameof(IsResultsEmpty));
    }

    partial void OnFailuresTextChanged(string value) => OnPropertyChanged(nameof(HasFailures));

    partial void OnIsSearchingChanged(bool value) => OnPropertyChanged(nameof(CanSearch));

    private void OnSourceToggleChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SourceToggle.IsEnabled))
        {
            OnPropertyChanged(nameof(HasEnabledSource));
            OnPropertyChanged(nameof(CanSearch));
        }
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (IsSearching)
            return;

        if (!HasEnabledSource)
        {
            StatusText = "Выбери хотя бы одну площадку";
            return;
        }

        IsSearching = true;
        FailuresText = string.Empty;
        StatusText = "Запускаю…";
        ClearResults();

        _cts = new CancellationTokenSource();

        try
        {
            var genius = new GeniusLookup(AppSettings.Load().GeniusAccessToken);
            var service = new ParserService(BuildSources(), genius);
            var progress = new Progress<string>(message => StatusText = message);

            var run = await Task.Run(
                () => service.RunAsync(BuildQuery(), progress, _cts.Token),
                _cts.Token);

            var known = LoadKnownUrls();

            foreach (var candidate in run.Candidates)
            {
                var item = new ParserResultItem
                {
                    Candidate = candidate,
                    AlreadyInDb = known.Contains(candidate.SourceUrl)
                };
                item.PropertyChanged += OnItemChanged;
                Results.Add(item);
            }

            if (run.HasFailures)
                FailuresText = string.Join("\n", run.Failures.Select(f => f.Line));

            StatusText = run.Summary;
            IsResultsStep = true;
        }
        catch (OperationCanceledException)
        {
            StatusText = "Отменено";
        }
        catch (Exception ex)
        {
            StatusText = "Не получилось";
            FailuresText = ex.Message;
            IsResultsStep = true;
        }
        finally
        {
            IsSearching = false;
            _cts?.Dispose();
            _cts = null;
            RaiseSelectionChanged();
            OnPropertyChanged(nameof(FoundLabel));
            OnPropertyChanged(nameof(IsResultsEmpty));
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private async Task ImportAsync()
    {
        var chosen = Results.Where(r => r.IsSelected).Select(r => r.Candidate).ToList();

        if (chosen.Count == 0 || IsImporting)
            return;

        IsImporting = true;
        StatusText = $"Сохраняю {chosen.Count}…";

        try
        {
            var folder = AppSettings.Load().ResolveMediaFolder();
            var service = new ArtistImportService(folder);

            var outcome = await Task.Run(() => service.ImportAsync(chosen));

            StatusText = outcome.Summary;

            FailuresText = outcome.Problems.Count > 0
                ? string.Join("\n", outcome.Problems)
                : string.Empty;

            foreach (var item in Results.Where(r => r.IsSelected).ToList())
                item.MarkImported();

            ImportDone?.Invoke();
        }
        catch (Exception ex)
        {
            StatusText = "Сохранить не вышло";
            FailuresText = ex.Message;
        }
        finally
        {
            IsImporting = false;
            RaiseSelectionChanged();
        }
    }

    public event Action? ImportDone;

    [RelayCommand]
    private void BackToCriteria()
    {
        IsResultsStep = false;
        StatusText = string.Empty;
    }

    [RelayCommand]
    private void SelectAll() => SetAll(true);

    [RelayCommand]
    private void SelectNone() => SetAll(false);

    private void SetAll(bool selected)
    {
        foreach (var item in Results)
            item.IsSelected = selected;
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ParserResultItem.IsSelected))
            RaiseSelectionChanged();
    }

    private void RaiseSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(ImportButtonLabel));
    }

    private void ClearResults()
    {
        foreach (var item in Results)
            item.PropertyChanged -= OnItemChanged;

        Results.Clear();
    }

    private IReadOnlyList<IArtistSource> BuildSources() =>
        SourceToggles.Where(s => s.IsEnabled).Select(s => s.Create()).ToList();

    private ArtistSearchQuery BuildQuery() => new()
    {
        GenreTag = GenreTag.Trim(),
        PlaysMin = ParseNullableInt(PlaysMin),
        PlaysMax = ParseNullableInt(PlaysMax),
        FreshWithinDays = ParseNullableInt(FreshDays),
        Country = Country.Trim(),
        MaxArtists = Math.Clamp(ParseNullableInt(MaxArtists) ?? 12, 1, 50),
        TracksPerArtist = Math.Clamp(ParseNullableInt(TracksPerArtist) ?? 10, 1, 50)
    };

    private static int? ParseNullableInt(string raw) =>
        int.TryParse(raw?.Trim(), out var value) ? value : null;

    private static HashSet<string> LoadKnownUrls()
    {
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var db = new AppDbContext();

        foreach (var url in db.Artists.Select(a => a.SourceUrl).ToList())
            if (!string.IsNullOrWhiteSpace(url))
                known.Add(url);

        foreach (var url in db.Artists.Select(a => a.ScLink).ToList())
            if (!string.IsNullOrWhiteSpace(url))
                known.Add(url);

        return known;
    }
}
