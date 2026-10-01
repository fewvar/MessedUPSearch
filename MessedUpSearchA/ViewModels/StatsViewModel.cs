using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessedUpSearchA.Data;
using MessedUpSearchA.Services.Localization;
using MessedUpSearchA.Services.Stats;

namespace MessedUpSearchA.ViewModels;

/// <summary>Блок статистики: заголовок и строки-полоски.</summary>
public class StatSection
{
    public string Title { get; init; } = string.Empty;
    public IReadOnlyList<StatRow> Rows { get; init; } = [];

    /// <summary>Для «через сколько отвечают» полоска — доля ответов, а не процент ответивших.</summary>
    public bool HasData => Rows.Any(r => r.Total > 0);
}

/// <summary>Окно «Статистика»: сводка и разрезы за период. Воронки нет — решение Васи.</summary>
public partial class StatsViewModel : ViewModelBase
{
    public static readonly int[] PeriodDays = [7, 30, 90, 0];

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private int _periodIndex = 1;

    public ObservableCollection<StatSection> Sections { get; } = new();

    [ObservableProperty] private string _pitches = "0";
    [ObservableProperty] private string _replyRate = "—";
    [ObservableProperty] private string _medianReply = "—";
    [ObservableProperty] private string _artists = "0";
    [ObservableProperty] private string _soldFree = "0 / 0";
    [ObservableProperty] private string _beatsAdded = "0";
    [ObservableProperty] private IReadOnlyList<double> _weekly = [];
    [ObservableProperty] private bool _isEmpty = true;

    public void Open()
    {
        Refresh();
        IsOpen = true;
    }

    [RelayCommand]
    private void SetPeriod(string index)
    {
        PeriodIndex = int.Parse(index);
        Refresh();
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    public bool Is7 => PeriodIndex == 0;
    public bool Is30 => PeriodIndex == 1;
    public bool Is90 => PeriodIndex == 2;
    public bool IsAll => PeriodIndex == 3;

    partial void OnPeriodIndexChanged(int value)
    {
        OnPropertyChanged(nameof(Is7));
        OnPropertyChanged(nameof(Is30));
        OnPropertyChanged(nameof(Is90));
        OnPropertyChanged(nameof(IsAll));
    }

    public void Refresh()
    {
        var days = PeriodDays[PeriodIndex];
        DateTime? since = days == 0 ? null : DateTime.Today.AddDays(-days + 1);

        using var db = new AppDbContext();
        var r = StatsService.Build(db, since, Localizer.Instance.Culture);
        var t = Localizer.Instance;

        Pitches = r.FollowUps > 0 ? $"{r.Pitches} + {r.FollowUps}" : r.Pitches.ToString();
        ReplyRate = r.Pitches == 0 ? "—" : r.ReplyRate.ToString("P0", Localizer.Instance.Culture);
        MedianReply = r.MedianReply is { } m ? Duration(m) : "—";
        Artists = r.Artists.ToString();
        SoldFree = $"{r.Sold} / {r.Free}";
        BeatsAdded = r.BeatsAdded.ToString();
        Weekly = r.WeeklySent;
        IsEmpty = r.Pitches == 0;

        Sections.Clear();
        Sections.Add(new StatSection { Title = t["Stats.ByTemplate"], Rows = r.ByTemplate });
        Sections.Add(new StatSection { Title = t["Stats.ByBeatGenre"], Rows = r.ByBeatGenre });
        Sections.Add(new StatSection { Title = t["Stats.ByWeekday"], Rows = r.ByWeekday });
        Sections.Add(new StatSection { Title = t["Stats.ByHours"], Rows = r.ByHours });
        Sections.Add(new StatSection { Title = t["Stats.ReplyTime"], Rows = r.ReplyTime });
        Sections.Add(new StatSection { Title = t["Stats.TopBeats"], Rows = r.TopBeats });
        Sections.Add(new StatSection { Title = t["Stats.ByArtistGenre"], Rows = r.ByArtistGenre });
        Sections.Add(new StatSection { Title = t["Stats.ByPlays"], Rows = r.ByPlays });
    }

    private static string Duration(TimeSpan t) =>
        t.TotalHours < 24
            ? Localizer.Instance.Format("Stats.Hours", Math.Max(1, (int)Math.Round(t.TotalHours)))
            : Localizer.Instance.Format("Stats.Days", Math.Round(t.TotalDays, 1));
}
