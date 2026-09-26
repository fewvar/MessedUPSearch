using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessedUpSearchA.Data;
using MessedUpSearchA.Models;
using MessedUpSearchA.Converters;
using MessedUpSearchA.Services;
using MessedUpSearchA.Services.Localization;
using MessedUpSearchA.Services.Ml;
using Avalonia.Threading;

namespace MessedUpSearchA.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly AppSettings _settings = AppSettings.Load();

    private readonly BeatsViewModel _beatsVm = new();
    private readonly ArtistsViewModel _artistsVm = new();
    private readonly ParserViewModel _parserVm = new();

    public BeatsViewModel BeatsVm => _beatsVm;
    public ArtistsViewModel ArtistsVm => _artistsVm;
    public ParserViewModel ParserVm => _parserVm;

    public ObservableCollection<CrmEntry> CrmEntries { get; } = new();

    public ObservableCollection<ReminderEntry> Reminders { get; } = new();

    [ObservableProperty] private bool _isCrmEmpty = true;
    [ObservableProperty] private bool _isCrmOpen;
    [ObservableProperty] private bool _isSettingsOpen;
    [ObservableProperty] private bool _isReminderOpen;
    [ObservableProperty] private bool _isParserOpen;

    [ObservableProperty] private ViewModelBase _currentViewModel;
    [ObservableProperty] private bool? _isBeatsSelected = true;


    [ObservableProperty] private int _reminderDays;
    [ObservableProperty] private string _mediaFolder = string.Empty;
    [ObservableProperty] private string _lastFmApiKey = string.Empty;
    [ObservableProperty] private string _jamendoClientId = string.Empty;
    [ObservableProperty] private string _geniusAccessToken = string.Empty;
    [ObservableProperty] private string _language = "English";
    [ObservableProperty] private bool _enableParserLogs = true;

    public IReadOnlyList<string> LanguageOptions { get; } = new[] { "English", "Русский" };

    public string AppVersion => "v0.6";

    /// <summary>
    /// Подпись в футере. Парсер работает только по кнопке, фонового прогона нет,
    /// поэтому состояние всегда одно — «ожидание».
    /// </summary>
    public string ParserStatus =>
        Localizer.Instance.Format("Status.Parser", Localizer.Instance["Parser.Idle"]);

    /// <summary>"3 дн." — число и слово вместе, поэтому строка собирается в коде.</summary>
    public string ReminderDaysLabel => Localizer.Instance.Format("Settings.DaysShort", ReminderDays);

    public MainWindowViewModel()
    {
        _currentViewModel = _beatsVm;
        _reminderDays = Math.Clamp(_settings.ReminderDays, 1, 14);

        _mediaFolder = _settings.ResolveMediaFolder();
        _lastFmApiKey = _settings.LastFmApiKey;
        _jamendoClientId = _settings.JamendoClientId;
        _geniusAccessToken = _settings.GeniusAccessToken;

        // Язык поднимаем до создания вкладок, иначе первый экран нарисуется
        // на английском и переключится только после ручного тычка в настройки.
        _language = string.IsNullOrWhiteSpace(_settings.Language) ? "English" : _settings.Language;
        ApplyLanguage(_language);

        _enableParserLogs = _settings.EnableParserLogs;
        AppLog.Enabled = _enableParserLogs;

        // Строки, собранные в коде, привязки сами не перечитают — обновляем руками.
        Localizer.Instance.LanguageChanged += () =>
        {
            OnPropertyChanged(nameof(ReminderDaysLabel));
            OnPropertyChanged(nameof(ParserStatus));
            RefreshEmbeddingStatus();
        };

        TrackEmbeddingQueue.Instance.StateChanged += () =>
            Dispatcher.UIThread.Post(RefreshEmbeddingStatus);

        _parserVm.ImportDone += () =>
        {
            // Новые артисты — новые треки, которые надо послушать.
            TrackEmbeddingQueue.Instance.Kick();

            // Парсер мог перекачать аватарки — старые картинки в кэше уже неверны.
            AvatarBrushConverter.Invalidate();
            _artistsVm.LoadArtists();
        };

        LoadReminders();
        IsReminderOpen = Reminders.Count > 0;
    }

    [ObservableProperty] private string _embeddingStatus = string.Empty;
    [ObservableProperty] private bool _canPauseEmbedding;
    [ObservableProperty] private bool _canResumeEmbedding;

    /// <summary>Строка очереди в футере. Пока очереди нечего делать — пусто и не видно.</summary>
    private void RefreshEmbeddingStatus()
    {
        var queue = TrackEmbeddingQueue.Instance;
        var loc = Localizer.Instance;

        EmbeddingStatus = queue.State switch
        {
            EmbeddingQueueState.Listening => loc.Format("Status.EmbedListening", queue.Done, queue.Total),
            EmbeddingQueueState.DownloadingModel => loc.Format("Status.EmbedModel", queue.ModelProgress.ToString("P0")),
            EmbeddingQueueState.Paused => loc["Status.EmbedPaused"],
            EmbeddingQueueState.Failed => loc.Format("Status.EmbedFailed", queue.LastError),
            _ => string.Empty
        };

        CanPauseEmbedding = queue.State is EmbeddingQueueState.Listening or EmbeddingQueueState.DownloadingModel;
        CanResumeEmbedding = queue.State is EmbeddingQueueState.Paused or EmbeddingQueueState.Failed;
    }

    [RelayCommand]
    private void PauseEmbedding() => TrackEmbeddingQueue.Instance.Pause();

    [RelayCommand]
    private void ResumeEmbedding()
    {
        var queue = TrackEmbeddingQueue.Instance;
        if (queue.State == EmbeddingQueueState.Paused)
            queue.Resume();
        else
            queue.Kick();
    }

    partial void OnIsBeatsSelectedChanged(bool? value)
    {
        CurrentViewModel = value == true ? _beatsVm : _artistsVm;
    }

    partial void OnReminderDaysChanged(int value)
    {
        _settings.ReminderDays = Math.Clamp(value, 1, 14);
        _settings.Save();
        OnPropertyChanged(nameof(ReminderDaysLabel));
    }

    partial void OnLastFmApiKeyChanged(string value)
    {
        _settings.LastFmApiKey = value.Trim();
        _settings.Save();
    }

    partial void OnJamendoClientIdChanged(string value)
    {
        _settings.JamendoClientId = value.Trim();
        _settings.Save();
    }

    partial void OnGeniusAccessTokenChanged(string value)
    {
        _settings.GeniusAccessToken = value.Trim();
        _settings.Save();
    }

    partial void OnEnableParserLogsChanged(bool value)
    {
        AppLog.Enabled = value;
        _settings.EnableParserLogs = value;
        _settings.Save();
    }

    /// <summary>Путь к логу показываем в настройках — он же подсказка, где лежат данные.</summary>
    public string LogFilePath => AppLog.FilePath;

    partial void OnLanguageChanged(string value)
    {
        ApplyLanguage(value);
        _settings.Language = value;
        _settings.Save();
    }

    private static void ApplyLanguage(string value) =>
        Localizer.Instance.Language = value is "Русский" or "Russian"
            ? AppLanguage.Russian
            : AppLanguage.English;

    public void SetMediaFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return;

        MediaFolder = folder;
        _settings.MediaFolder = folder;
        _settings.Save();
    }

    public void ShowCrm()
    {
        LoadCrm();
        IsCrmOpen = true;
    }

    private void LoadCrm()
    {
        CrmEntries.Clear();

        using var db = new AppDbContext();
        var artists = db.Artists
            .Where(a => a.CrmStatus != "" && a.CrmStatus != null)
            .OrderBy(a => a.Nickname)
            .ToList();

        foreach (var a in artists)
        {
            var logs = db.SentBeatsLog.Where(s => s.ArtistId == a.Id).ToList();
            var beatById = db.Beats
                .Where(b => logs.Select(l => l.BeatId).Contains(b.Id))
                .ToDictionary(b => b.Id);

            var items = logs
                .Where(l => beatById.ContainsKey(l.BeatId))
                .Select(l => new CrmBeatItem
                {
                    LogId = l.Id,
                    BeatName = beatById[l.BeatId].BeatName,
                    StatusColor = beatById[l.BeatId].StatusColor,
                    IsSent = l.IsSent
                })
                .OrderBy(i => i.IsSent).ThenBy(i => i.BeatName)
                .ToList();

            CrmEntries.Add(new CrmEntry
            {
                Nickname = a.Nickname,
                CrmStatus = a.CrmStatus,
                Notes = a.Notes,
                Beats = items
            });
        }

        IsCrmEmpty = CrmEntries.Count == 0;
    }

    [RelayCommand]
    private void MarkSent(int logId)
    {
        using (var db = new AppDbContext())
        {
            var log = db.SentBeatsLog.FirstOrDefault(s => s.Id == logId);
            if (log is not null && !log.IsSent)
            {
                log.IsSent = true;
                log.SentAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                db.SaveChanges();
            }
        }

        LoadCrm();
        LoadReminders();
    }

    private void LoadReminders()
    {
        Reminders.Clear();

        using var db = new AppDbContext();
        var cutoff = DateTime.Now.AddDays(-ReminderDays);

        var pending = db.SentBeatsLog.Where(s => !s.IsSent).ToList()
            .Where(s => DateTime.TryParse(s.AssignedAt, out var d) && d <= cutoff)
            .ToList();

        if (pending.Count == 0)
            return;

        var artistById = db.Artists.ToDictionary(a => a.Id);
        var beatById = db.Beats.ToDictionary(b => b.Id);

        var groups = pending
            .Where(s => artistById.ContainsKey(s.ArtistId) && beatById.ContainsKey(s.BeatId))
            .GroupBy(s => s.ArtistId);

        foreach (var g in groups)
        {
            Reminders.Add(new ReminderEntry
            {
                Nickname = artistById[g.Key].Nickname,
                BeatNames = g.Select(s => beatById[s.BeatId].BeatName).OrderBy(n => n).ToList()
            });
        }
    }

    public void CloseReminders() => IsReminderOpen = false;

    public void OpenCrmFromReminder()
    {
        IsReminderOpen = false;
        ShowCrm();
    }

    public void ShowSettings() => IsSettingsOpen = true;

    public void ShowParser() => IsParserOpen = true;

    public void CloseOverlays()
    {
        IsCrmOpen = false;
        IsSettingsOpen = false;
        IsParserOpen = false;
    }
}
