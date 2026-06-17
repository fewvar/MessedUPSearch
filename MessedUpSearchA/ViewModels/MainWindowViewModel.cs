using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessedUpSearchA.Data;
using MessedUpSearchA.Models;

namespace MessedUpSearchA.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly AppSettings _settings = AppSettings.Load();

    private readonly BeatsViewModel _beatsVm = new();
    private readonly ArtistsViewModel _artistsVm = new();

    public BeatsViewModel BeatsVm => _beatsVm;
    public ArtistsViewModel ArtistsVm => _artistsVm;

    public ObservableCollection<CrmEntry> CrmEntries { get; } = new();

    public ObservableCollection<ReminderEntry> Reminders { get; } = new();

    [ObservableProperty] private bool _isCrmEmpty = true;
    [ObservableProperty] private bool _isCrmOpen;
    [ObservableProperty] private bool _isSettingsOpen;
    [ObservableProperty] private bool _isReminderOpen;

    [ObservableProperty] private ViewModelBase _currentViewModel;
    [ObservableProperty] private bool? _isBeatsSelected = true;
    [ObservableProperty] private string _parserStatus = "Idle";

    [ObservableProperty] private int _reminderDays;

    public string AppVersion => "v0.1";

    public MainWindowViewModel()
    {
        _currentViewModel = _beatsVm;
        _reminderDays = Math.Clamp(_settings.ReminderDays, 1, 14);

        LoadReminders();
        IsReminderOpen = Reminders.Count > 0;
    }

    partial void OnIsBeatsSelectedChanged(bool? value)
    {
        CurrentViewModel = value == true ? _beatsVm : _artistsVm;
    }

    partial void OnReminderDaysChanged(int value)
    {
        _settings.ReminderDays = Math.Clamp(value, 1, 14);
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

    public void CloseOverlays()
    {
        IsCrmOpen = false;
        IsSettingsOpen = false;
    }
}
