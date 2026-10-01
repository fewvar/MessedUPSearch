using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessedUpSearchA.Data;
using MessedUpSearchA.Services;
using MessedUpSearchA.Services.Llm;
using MessedUpSearchA.Services.Localization;
using MessedUpSearchA.Services.Stats;

namespace MessedUpSearchA.ViewModels;

/// <summary>
/// «Сегодня» — коуч по битмейкингу и отправкам: продуктивные часы, что заходит, дела.
/// Факты считает приложение; с ключом нейросети сверху — совет из 2–3 фраз.
/// </summary>
public partial class TodayViewModel : ViewModelBase
{
    private readonly Func<AppSettings> _settings;
    private readonly Func<LlmClient?> _llm;
    private List<string> _llmFacts = new();

    [ObservableProperty] private bool _isOpen;

    [ObservableProperty] private string _coachText = string.Empty;
    [ObservableProperty] private bool _isCoachThinking;
    [ObservableProperty] private string _coachBasis = string.Empty;
    public ObservableCollection<string> Facts { get; } = new();
    [ObservableProperty] private bool _hasCoach;

    [ObservableProperty] private IReadOnlyList<double> _hours = new double[24];
    [ObservableProperty] private IReadOnlyList<bool> _hoursHighlight = new bool[24];
    [ObservableProperty] private string _hoursCaption = string.Empty;
    [ObservableProperty] private bool _hasHours;

    public ObservableCollection<string> Deals { get; } = new();
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasFollowUps))] private int _followUpsDue;
    public bool HasFollowUps => FollowUpsDue > 0;
    [ObservableProperty] private string _followUpsLabel = string.Empty;
    [ObservableProperty] private bool _hasReminders;
    [ObservableProperty] private string _remindersLabel = string.Empty;

    [ObservableProperty] private string _styleLine = string.Empty;

    /// <summary>Кнопки на карточке дел ведут в CRM / фоллоу-апы / к напоминаниям.</summary>
    public event Action? OpenFollowUpsRequested;
    public event Action? OpenCrmRequested;

    public TodayViewModel(Func<AppSettings> settings, Func<LlmClient?> llm)
    {
        _settings = settings;
        _llm = llm;
    }

    public void Open(int pendingReminders)
    {
        var settings = _settings();
        var now = DateTime.Now;
        TodayReport report;
        using (var db = new AppDbContext())
            report = TodayFacts.Build(db, settings, now);

        var t = Localizer.Instance;

        // Часы.
        HasHours = report.Hours.Known;
        Hours = report.Hours.ByHour;
        HoursHighlight = Enumerable.Range(0, 24).Select(report.Hours.Contains).ToArray();
        HoursCaption = report.Hours.Source switch
        {
            HoursSource.Daw => t.Format("Today.HoursFromDaw", report.Hours.Basis),
            HoursSource.Files => t.Format("Today.HoursFromFiles", report.Hours.Basis),
            _ => t.Format("Today.HoursUnknown", report.Hours.Basis)
        };

        // Дела.
        Deals.Clear();
        if (report.NewReplies > 0)
            Deals.Add(t.Format("Today.DealReplies", report.NewReplies, string.Join(", ", report.NewReplyFrom.Take(4))));
        foreach (var (beat, days) in report.Unsent)
            Deals.Add(t.Format("Today.DealUnsent", beat, days));
        if (report.UnsentTotal > report.Unsent.Count)
            Deals.Add(t.Format("Today.DealUnsentMore", report.UnsentTotal - report.Unsent.Count));
        Deals.Add(t.Format("Today.DealMailLeft", report.MailLeft));

        FollowUpsDue = report.FollowUpsDue;
        FollowUpsLabel = t.Format("Crm.FollowUps", report.FollowUpsDue);
        HasReminders = pendingReminders > 0;
        RemindersLabel = t.Format("Today.Reminders", pendingReminders);

        StyleLine = report.StyleNeeded > 0
            ? t.Format("Today.StyleLater", TodayFacts.StyleMinimum, report.StyleBasis)
            : string.Empty;

        // Советы списком — видны всегда; совет нейросети — сверху, если есть ключ (ей — и советы, и дела).
        var lines = report.FactLines(now, withDeals: false);
        Facts.Clear();
        foreach (var line in lines)
            Facts.Add(line);
        if (Facts.Count == 0)
            Facts.Add(t["Today.NothingYet"]);
        _llmFacts = report.FactLines(now, withDeals: true);
        CoachBasis = t.Format("Today.Basis", report.StyleBasis, report.Hours.Basis);

        settings.TodaySeenAt = now.ToString("yyyy-MM-dd HH:mm");
        settings.TodayShownDate = now.ToString("yyyy-MM-dd");
        settings.Save();

        IsOpen = true;
        _ = AdviseAsync(_llmFacts, force: false);
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task RefreshCoachAsync()
    {
        await AdviseAsync(_llmFacts, force: true);
    }

    private async System.Threading.Tasks.Task AdviseAsync(IReadOnlyList<string> facts, bool force)
    {
        var settings = _settings();
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        if (!force && settings.CoachDate == today && settings.CoachText.Length > 0)
        {
            CoachText = settings.CoachText;
            HasCoach = true;
            return;
        }

        if (facts.Count == 0 || _llm() is not { } client)
        {
            HasCoach = false;
            return;
        }

        IsCoachThinking = true;
        try
        {
            var language = Localizer.Instance.Language == AppLanguage.Russian ? "Russian" : "English";
            CoachText = await Coach.AdviseAsync(client, facts, language);
            HasCoach = CoachText.Length > 0;
            settings.CoachDate = today;
            settings.CoachText = CoachText;
            settings.Save();
        }
        catch (LlmException ex)
        {
            AppLog.Write($"совет «Сегодня»: {ex.Message}");
            HasCoach = false;
        }
        finally
        {
            IsCoachThinking = false;
        }
    }

    [RelayCommand]
    private void OpenFollowUps()
    {
        IsOpen = false;
        OpenFollowUpsRequested?.Invoke();
    }

    [RelayCommand]
    private void OpenCrm()
    {
        IsOpen = false;
        OpenCrmRequested?.Invoke();
    }

    [RelayCommand]
    private void Close() => IsOpen = false;
}
