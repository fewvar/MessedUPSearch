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
using MessedUpSearchA.Services.Mail;
using MessedUpSearchA.Services.Llm;

namespace MessedUpSearchA.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly AppSettings _settings = AppSettings.Load();

    private readonly BeatsViewModel _beatsVm = new();
    private readonly ArtistsViewModel _artistsVm = new();
    private readonly ParserViewModel _parserVm = new();

    public BeatsViewModel BeatsVm => _beatsVm;
    public PlayerViewModel Player { get; }
    public ArtistsViewModel ArtistsVm => _artistsVm;
    public ParserViewModel ParserVm => _parserVm;

    /// <summary>Рассылка идёт в фоне — одна на приложение.</summary>
    public MailQueue Mail { get; }
    public MailViewModel MailVm { get; }
    public StatsViewModel StatsVm { get; } = new();
    public TodayViewModel TodayVm { get; }

    /// <summary>Сессии в DAW: пишет трекер раз в минуту, если есть согласие.</summary>
    public DawTracker? DawTracker { get; }

    /// <summary>Страница согласия на весь экран — при первом запуске 1.1.</summary>
    [ObservableProperty] private bool _isConsentOpen;
    [ObservableProperty] private bool _dawTracking;
    [ObservableProperty] private string _dawSummary = string.Empty;
    [ObservableProperty] private string _backgroundStatus = string.Empty;

    /// <summary>Фоновый режим включили/выключили — App ставит или убирает иконку в трее.</summary>
    public event Action<bool>? BackgroundModeChanged;

    [ObservableProperty] private bool _showTodayOnStart = true;
    [ObservableProperty] private bool _yourTimeToast = true;
    private Avalonia.Threading.DispatcherTimer? _minuteTimer;

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
    [ObservableProperty] private bool _animations = true;
    [ObservableProperty] private bool _sounds = true;

    // Почта. Пароль сюда не читается: он в связке ключей, поле только для ввода нового.
    [ObservableProperty] private string _mailAddress = string.Empty;
    [ObservableProperty] private string _mailSenderName = string.Empty;
    [ObservableProperty] private string _mailPassword = string.Empty;
    [ObservableProperty] private string _smtpHost = string.Empty;
    [ObservableProperty] private int _smtpPort = 465;
    [ObservableProperty] private string _imapHost = string.Empty;
    [ObservableProperty] private int _imapPort = 993;
    [ObservableProperty] private int _mailDailyLimit = 50;
    [ObservableProperty] private int _followUpDays = 7;

    /// <summary>Сколько фоллоу-апов пора отправить — кнопка в CRM.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasFollowUpsDue), nameof(FollowUpsLabel))]
    private int _followUpsDue;
    public bool HasFollowUpsDue => FollowUpsDue > 0;
    public string FollowUpsLabel => Localizer.Instance.Format("Crm.FollowUps", FollowUpsDue);

    [ObservableProperty] private bool _isCheckingReplies;
    private Avalonia.Threading.DispatcherTimer? _replyTimer;
    [ObservableProperty] private bool _hasSavedMailPassword;
    [ObservableProperty] private bool _isTestingMail;
    [ObservableProperty] private string _mailTestStatus = string.Empty;

    // Нейросеть. Ключ, как и пароль почты, только вводится — читается из связки ключей при запросе.
    public IReadOnlyList<string> LlmProviderOptions => LlmProviders.All;
    public ObservableCollection<string> LlmModels { get; } = new();
    [ObservableProperty] private string _llmProvider = LlmProviders.Groq;
    [ObservableProperty] private string _llmBaseUrl = string.Empty;
    [ObservableProperty] private string _llmModel = string.Empty;
    [ObservableProperty] private string _llmKey = string.Empty;
    [ObservableProperty] private bool _hasSavedLlmKey;
    [ObservableProperty] private bool _isTestingLlm;
    [ObservableProperty] private string _llmTestStatus = string.Empty;

    public bool IsCustomLlm => LlmProvider == LlmProviders.Custom;
    public string LlmPrivacyNote => Localizer.Instance.Format("Llm.Privacy",
        LlmProvider != LlmProviders.Custom ? LlmProvider
        : Uri.TryCreate(LlmBaseUrl.Trim(), UriKind.Absolute, out var uri) ? uri.Host : Localizer.Instance["Value.Custom"]);
    public string LlmKeyHint => Localizer.Instance[LlmProvider switch
    {
        LlmProviders.Groq => "Llm.KeyHintGroq",
        LlmProviders.OpenRouter => "Llm.KeyHintOpenRouter",
        _ => "Llm.KeyHintCustom"
    }];

    /// <summary>Где взять пароль приложения — под почту, которую ввели.</summary>
    public string MailHelp => Localizer.Instance[MailPresets.For(MailAddress)?.HelpKey ?? "Mail.HelpOther"];

    /// <summary>Идёт анализ бита или поиск парсера — звезда в шапке вращается.</summary>
    public bool IsBusy => _beatsVm.IsAnalyzing || _parserVm.IsSearching;

    /// <summary>Сообщение в футере (Toasts.Show); пустое — показываем статус парсера.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasToast))] private string _toast = string.Empty;
    public bool HasToast => Toast.Length > 0;
    private Avalonia.Threading.DispatcherTimer? _toastTimer;

    public IReadOnlyList<string> LanguageOptions { get; } = new[] { "English", "Русский" };

    public string AppVersion => "v1.1";

    /// <summary>Подпись в футере: «ожидание» или «идёт поиск» (окно парсера можно закрыть — поиск идёт дальше).</summary>
    public string ParserStatus =>
        Localizer.Instance.Format("Status.Parser", Localizer.Instance[_parserVm.IsSearching ? "Parser.Running" : "Parser.Idle"]);

    public bool IsParserRunning => _parserVm.IsSearching;

    /// <summary>"3 дн." — число и слово вместе, поэтому строка собирается в коде.</summary>
    public string ReminderDaysLabel => Localizer.Instance.Format("Settings.DaysShort", ReminderDays);

    public MainWindowViewModel()
    {
        _currentViewModel = _beatsVm;

        Player = new PlayerViewModel(_settings)
        {
            BeatListProvider = () => _beatsVm.Beats.ToList()
        };
        Player.PlayingBeatChanged += id => _beatsVm.PlayingBeatId = id;
        Player.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PlayerViewModel.IsPlaying))
                _beatsVm.IsPlayerPlaying = Player.IsPlaying;
        };
        _reminderDays = Math.Clamp(_settings.ReminderDays, 1, 14);

        _mediaFolder = _settings.ResolveMediaFolder();
        _lastFmApiKey = _settings.LastFmApiKey;
        _jamendoClientId = _settings.JamendoClientId;
        _geniusAccessToken = _settings.GeniusAccessToken;

        // Язык поднимаем до создания вкладок, иначе первый экран нарисуется
        // на английском и переключится только после ручного тычка в настройки.
        _language = string.IsNullOrWhiteSpace(_settings.Language) ? "English" : _settings.Language;
        ApplyLanguage(_language);

        _mailAddress = _settings.MailAddress;
        _mailSenderName = _settings.MailSenderName;
        _smtpHost = _settings.SmtpHost;
        _smtpPort = _settings.SmtpPort;
        _imapHost = _settings.ImapHost;
        _imapPort = _settings.ImapPort;
        _mailDailyLimit = Math.Clamp(_settings.MailDailyLimit, 1, 500);
        _followUpDays = Math.Clamp(_settings.FollowUpDays, 1, 60);
        _hasSavedMailPassword = _mailAddress.Length > 0 && SecretStore.Get(MailQueue.PasswordKey(_mailAddress)) is not null;

        _llmProvider = LlmProviders.All.Contains(_settings.LlmProvider) ? _settings.LlmProvider : LlmProviders.Groq;
        _llmBaseUrl = _settings.LlmBaseUrl;
        _llmModel = _settings.LlmModel;
        if (_llmModel.Length > 0)
            LlmModels.Add(_llmModel);
        _hasSavedLlmKey = SecretStore.Get(LlmProviders.KeyName(_llmProvider)) is not null;

        _dawTracking = _settings.DawTracking;
        DawTracker = new DawTracker(() => _settings);
        _isConsentOpen = !_settings.DawConsentAsked && !Avalonia.Controls.Design.IsDesignMode;

        _showTodayOnStart = _settings.ShowTodayOnStart;
        _yourTimeToast = _settings.YourTimeToast;
        TodayVm = new TodayViewModel(() => _settings, CreateLlmClient);
        TodayVm.OpenFollowUpsRequested += () => OpenFollowUpsCommand.Execute(null);
        TodayVm.OpenCrmRequested += ShowCrm;

        Mail = new MailQueue(() => _settings);
        MailVm = new MailViewModel(Mail, () => _settings, CreateLlmClient);
        Mail.MailSent += () =>
        {
            _artistsVm.LoadArtists();
            RefreshFollowUps();
        };
        MailVm.ContactsChanged += () => _artistsVm.LoadArtists();
        _beatsVm.MailRequested += (beatId, artistIds) => MailVm.Open(beatId, artistIds);

        _enableParserLogs = _settings.EnableParserLogs;
        _animations = _settings.Animations;
        _sounds = _settings.Sounds;
        Services.Audio.UiSounds.Enabled = _sounds;
        Motion.Motion.Initialize(_animations);
        AppLog.Enabled = _enableParserLogs;

        // Строки, собранные в коде, привязки сами не перечитают — обновляем руками.
        Localizer.Instance.LanguageChanged += () =>
        {
            OnPropertyChanged(nameof(ReminderDaysLabel));
            OnPropertyChanged(nameof(ParserStatus));
            OnPropertyChanged(nameof(MailHelp));
            OnPropertyChanged(nameof(LlmPrivacyNote));
            OnPropertyChanged(nameof(LlmKeyHint));
            OnPropertyChanged(nameof(FollowUpsLabel));
        };

        _beatsVm.ArtistImported += () =>
        {
            AvatarBrushConverter.Invalidate();
            _artistsVm.LoadArtists();
        };

        Toasts.Shown += text =>
        {
            Toast = text;
            _toastTimer ??= new Avalonia.Threading.DispatcherTimer(TimeSpan.FromSeconds(2.4), Avalonia.Threading.DispatcherPriority.Normal,
                (_, _) => { _toastTimer!.Stop(); Toast = string.Empty; });
            _toastTimer.Stop();
            _toastTimer.Start();
        };

        _parserVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(ParserViewModel.IsSearching))
                return;
            OnPropertyChanged(nameof(ParserStatus));
            OnPropertyChanged(nameof(IsParserRunning));
            OnPropertyChanged(nameof(IsBusy));
        };
        _beatsVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BeatsViewModel.IsAnalyzing))
                OnPropertyChanged(nameof(IsBusy));
        };
        _parserVm.ImportDone += () =>
        {
            // Парсер мог перекачать аватарки — старые картинки в кэше уже неверны.
            AvatarBrushConverter.Invalidate();
            _artistsVm.LoadArtists();
        };

        LoadReminders();
        RefreshFollowUps();

        // Первый запуск за день — «Сегодня» (напоминания там же, карточкой). Дальше — как раньше.
        // Пока открыта страница согласия, «Сегодня» откроется после ответа на неё.
        if (IsConsentOpen)
            IsReminderOpen = false;
        else if (ShowTodayOnStart && _settings.TodayShownDate != DateTime.Now.ToString("yyyy-MM-dd") &&
            !Avalonia.Controls.Design.IsDesignMode)
            ShowToday();
        else
            IsReminderOpen = Reminders.Count > 0;

        StartReplyChecks();
        StartMinuteTimer();
    }

    /// <summary>«Разрешить» на странице согласия: сессии в DAW + фоновый режим (трей, автозапуск).</summary>
    [RelayCommand]
    private void AllowDaw()
    {
        _settings.DawConsentAsked = true;
        DawTracking = true;
        IsConsentOpen = false;
        AfterConsent();
    }

    /// <summary>«Не сейчас»: ничего не пишется; включить можно в настройках.</summary>
    [RelayCommand]
    private void DeclineDaw()
    {
        _settings.DawConsentAsked = true;
        _settings.Save();
        IsConsentOpen = false;
        AfterConsent();
    }

    private void AfterConsent()
    {
        if (ShowTodayOnStart && _settings.TodayShownDate != DateTime.Now.ToString("yyyy-MM-dd"))
            ShowToday();
        else
            IsReminderOpen = Reminders.Count > 0;
    }

    partial void OnDawTrackingChanged(bool value)
    {
        _settings.DawTracking = value;
        _settings.Save();

        var problem = value ? Autostart.Enable() : string.Empty;
        if (!value)
            Autostart.Disable();
        BackgroundStatus = problem.Length > 0 ? Localizer.Instance[problem] : string.Empty;

        BackgroundModeChanged?.Invoke(value);
        RefreshDawSummary();
    }

    /// <summary>«Что собрано» в настройках: число сессий, часы и последние интервалы.</summary>
    public void RefreshDawSummary()
    {
        using var db = new AppDbContext();
        var sessions = db.DawSessions.OrderByDescending(s => s.StartedAt).ToList();
        if (sessions.Count == 0)
        {
            DawSummary = Localizer.Instance["Daw.Nothing"];
            return;
        }

        var minutes = sessions.Sum(s =>
            Services.Stats.StatsService.Parse(s.StartedAt) is { } a && Services.Stats.StatsService.Parse(s.EndedAt) is { } b
                ? (b - a).TotalMinutes + 1 : 0);
        var last = sessions.Take(5).Select(s => $"{s.App} {s.StartedAt}–{s.EndedAt[^5..]}");
        DawSummary = Localizer.Instance.Format("Daw.Summary", sessions.Count, Math.Round(minutes / 60, 1)) + "\n" + string.Join("\n", last);
    }

    [RelayCommand]
    private void EraseDaw()
    {
        DawTracker?.EraseAll();
        RefreshDawSummary();
        Toasts.Show(Localizer.Instance["Daw.Erased"]);
    }

    public void ShowToday()
    {
        IsReminderOpen = false;
        TodayVm.Open(Reminders.Sum(r => r.BeatNames.Count));
    }

    partial void OnShowTodayOnStartChanged(bool value) { _settings.ShowTodayOnStart = value; _settings.Save(); }
    partial void OnYourTimeToastChanged(bool value) { _settings.YourTimeToast = value; _settings.Save(); }

    /// <summary>Раз в минуту: «сейчас твоё время» (не чаще раза в день) и сессии в DAW.</summary>
    private void StartMinuteTimer()
    {
        if (Avalonia.Controls.Design.IsDesignMode)
            return;
        _minuteTimer = new Avalonia.Threading.DispatcherTimer(TimeSpan.FromMinutes(1), Avalonia.Threading.DispatcherPriority.Background,
            (_, _) => OnMinute());
        _minuteTimer.Start();
    }

    private int _hoursComputedAt = -1;
    private Services.Stats.ProductiveHours? _hours;

    private void OnMinute()
    {
        var now = DateTime.Now;
        DawTracker?.Tick(now);

        if (!_settings.YourTimeToast || _settings.YourTimeToastDate == now.ToString("yyyy-MM-dd"))
            return;

        // Часы пересчитываются раз в час — это запрос к базе и чтение дат файлов.
        if (_hoursComputedAt != now.Hour)
        {
            using var db = new AppDbContext();
            _hours = Services.Stats.TodayFacts.ProductiveHoursFor(db, _settings);
            _hoursComputedAt = now.Hour;
        }

        if (_hours is not { Known: true } hours || now.Hour != hours.Start)
            return;

        _settings.YourTimeToastDate = now.ToString("yyyy-MM-dd");
        _settings.Save();
        var window = $"{hours.Start:00}–{(hours.Start + hours.Length) % 24:00}";
        Toasts.Show(Localizer.Instance.Format("Today.YourTimeToast", window));
    }

    /// <summary>
    /// Ответы во входящих — раз в 5 минут, пока приложение открыто (первый раз через 15 с после
    /// запуска, чтобы не тормозить старт). Без настроенной почты не делает ничего.
    /// </summary>
    private void StartReplyChecks()
    {
        if (Avalonia.Controls.Design.IsDesignMode)
            return;

        Avalonia.Threading.DispatcherTimer.RunOnce(() => _ = CheckRepliesAsync(), TimeSpan.FromSeconds(15));
        _replyTimer = new Avalonia.Threading.DispatcherTimer(TimeSpan.FromMinutes(5), Avalonia.Threading.DispatcherPriority.Background,
            (_, _) => _ = CheckRepliesAsync());
        _replyTimer.Start();
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task CheckRepliesAsync()
    {
        if (IsCheckingReplies)
            return;

        IsCheckingReplies = true;
        try
        {
            var found = await ReplyChecker.CheckAsync(_settings);
            if (found.Count > 0)
            {
                var first = found[0];
                Toasts.Show(found.Count == 1
                    ? Localizer.Instance.Format("Crm.ReplyFrom", first.Nickname, first.Snippet)
                    : Localizer.Instance.Format("Crm.Replies", found.Count, string.Join(", ", found.Select(f => f.Nickname).Distinct())));
                Services.Audio.UiSounds.Play(Services.Audio.UiSound.AnalysisDone);
                _artistsVm.LoadArtists();
                if (IsCrmOpen)
                    LoadCrm();
            }
        }
        catch (Exception ex)
        {
            // Нет сети или почта не пускает IMAP — молча до следующей проверки, причина в логе.
            AppLog.Write($"проверка ответов: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            IsCheckingReplies = false;
            RefreshFollowUps();
        }
    }

    public void RefreshFollowUps()
    {
        using var db = new AppDbContext();
        FollowUpsDue = CrmRules.DueFollowUps(db, _settings.FollowUpDays).Count;
    }

    /// <summary>«Фоллоу-апы (N)» в CRM: окно рассылки со всеми, кому пора напомнить.</summary>
    [RelayCommand]
    private void OpenFollowUps()
    {
        using var db = new AppDbContext();
        var due = CrmRules.DueFollowUps(db, _settings.FollowUpDays);
        if (due.Count == 0)
            return;
        IsCrmOpen = false;
        MailVm.OpenFollowUps(due);
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

    partial void OnMailAddressChanged(string value)
    {
        var address = value.Trim();
        _settings.MailAddress = address;

        // Знакомый домен — серверы подставляются сами, вписывать руками не нужно.
        if (MailPresets.For(address) is { } preset)
        {
            SmtpHost = preset.SmtpHost;
            SmtpPort = preset.SmtpPort;
            ImapHost = preset.ImapHost;
            ImapPort = preset.ImapPort;
        }

        HasSavedMailPassword = address.Length > 0 && SecretStore.Get(MailQueue.PasswordKey(address)) is not null;
        MailTestStatus = string.Empty;
        OnPropertyChanged(nameof(MailHelp));
        _settings.Save();
    }

    partial void OnMailSenderNameChanged(string value) { _settings.MailSenderName = value.Trim(); _settings.Save(); }
    partial void OnSmtpHostChanged(string value) { _settings.SmtpHost = value.Trim(); _settings.Save(); }
    partial void OnSmtpPortChanged(int value) { _settings.SmtpPort = value; _settings.Save(); }
    partial void OnImapHostChanged(string value) { _settings.ImapHost = value.Trim(); _settings.Save(); }
    partial void OnImapPortChanged(int value) { _settings.ImapPort = value; _settings.Save(); }
    partial void OnMailDailyLimitChanged(int value) { _settings.MailDailyLimit = Math.Clamp(value, 1, 500); _settings.Save(); }

    partial void OnFollowUpDaysChanged(int value)
    {
        _settings.FollowUpDays = Math.Clamp(value, 1, 60);
        _settings.Save();
        RefreshFollowUps();
    }

    /// <summary>
    /// «Сохранить и проверить»: новый пароль (если ввели) — в связку ключей, затем тестовое письмо
    /// самому себе. Дошло — значит, и рассылка дойдёт.
    /// </summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task TestMailAsync()
    {
        var address = MailAddress.Trim();
        if (address.Length == 0 || SmtpHost.Trim().Length == 0)
        {
            MailTestStatus = Localizer.Instance["Mail.SetupFirst"];
            return;
        }

        if (MailPassword.Length > 0)
        {
            if (!SecretStore.Set(MailQueue.PasswordKey(address), MailPassword))
            {
                MailTestStatus = Localizer.Instance["Mail.KeychainFailed"];
                return;
            }
            MailPassword = string.Empty;
            HasSavedMailPassword = true;
        }

        var password = SecretStore.Get(MailQueue.PasswordKey(address));
        if (string.IsNullOrEmpty(password))
        {
            MailTestStatus = Localizer.Instance["Mail.NoPassword"];
            return;
        }

        IsTestingMail = true;
        MailTestStatus = Localizer.Instance["Mail.Testing"];
        try
        {
            var account = new MailAccount(address, MailQueue.SenderName(_settings), password, SmtpHost.Trim(), SmtpPort);
            await MailService.SendAsync(account, address, Localizer.Instance["Mail.TestSubject"], Localizer.Instance["Mail.TestBody"]);
            MailTestStatus = Localizer.Instance["Mail.TestOk"];
        }
        catch (MailSendException ex)
        {
            AppLog.Write($"проверка почты: {ex.Failure}: {ex.Message}");
            MailTestStatus = ex.Failure switch
            {
                MailFailure.Auth => Localizer.Instance["Mail.AuthFailed"],
                MailFailure.Connection => Localizer.Instance["Mail.ConnectionFailed"],
                _ => ex.Message
            };
        }
        finally
        {
            IsTestingMail = false;
        }
    }

    /// <summary>«Забыть пароль» — удалить запись из связки ключей.</summary>
    [RelayCommand]
    private void ForgetMailPassword()
    {
        SecretStore.Delete(MailQueue.PasswordKey(MailAddress));
        HasSavedMailPassword = false;
        MailTestStatus = string.Empty;
    }

    partial void OnLlmProviderChanged(string value)
    {
        _settings.LlmProvider = value;
        _settings.LlmModel = string.Empty;
        _settings.Save();
        LlmModel = string.Empty;
        LlmModels.Clear();
        LlmTestStatus = string.Empty;
        HasSavedLlmKey = SecretStore.Get(LlmProviders.KeyName(value)) is not null;
        OnPropertyChanged(nameof(IsCustomLlm));
        OnPropertyChanged(nameof(LlmPrivacyNote));
        OnPropertyChanged(nameof(LlmKeyHint));
    }

    partial void OnLlmBaseUrlChanged(string value)
    {
        _settings.LlmBaseUrl = value.Trim();
        _settings.Save();
        OnPropertyChanged(nameof(LlmPrivacyNote));
    }

    partial void OnLlmModelChanged(string value)
    {
        if (string.IsNullOrEmpty(value))
            return;
        _settings.LlmModel = value;
        _settings.Save();
    }

    /// <summary>Клиент по текущим настройкам; null — нейросеть не настроена.</summary>
    public LlmClient? CreateLlmClient()
    {
        var key = SecretStore.Get(LlmProviders.KeyName(_settings.LlmProvider)) ?? string.Empty;
        var baseUrl = LlmProviders.BaseUrl(_settings.LlmProvider, _settings.LlmBaseUrl);
        if (baseUrl.Length == 0 || _settings.LlmModel.Length == 0 ||
            (key.Length == 0 && _settings.LlmProvider != LlmProviders.Custom))
            return null;
        return new LlmClient(baseUrl, key, _settings.LlmModel);
    }

    /// <summary>«Сохранить и проверить»: ключ — в связку ключей, затем список моделей с провайдера.</summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task TestLlmAsync()
    {
        var keyName = LlmProviders.KeyName(LlmProvider);
        if (LlmKey.Length > 0)
        {
            if (!SecretStore.Set(keyName, LlmKey.Trim()))
            {
                LlmTestStatus = Localizer.Instance["Mail.KeychainFailed"];
                return;
            }
            LlmKey = string.Empty;
            HasSavedLlmKey = true;
        }

        var baseUrl = LlmProviders.BaseUrl(LlmProvider, LlmBaseUrl);
        if (baseUrl.Length == 0)
        {
            LlmTestStatus = Localizer.Instance["Llm.NoUrl"];
            return;
        }

        IsTestingLlm = true;
        LlmTestStatus = Localizer.Instance["Llm.Testing"];
        try
        {
            var models = await new LlmClient(baseUrl, SecretStore.Get(keyName) ?? string.Empty).ListModelsAsync();
            var keep = LlmModel;
            LlmModels.Clear();
            foreach (var m in models)
                LlmModels.Add(m);
            LlmModel = models.Contains(keep) ? keep : LlmProviders.PickDefault(models);
            LlmTestStatus = Localizer.Instance.Format("Llm.TestOk", models.Count);
        }
        catch (LlmException ex)
        {
            AppLog.Write($"нейросеть {LlmProvider}: {ex.Message}");
            LlmTestStatus = ex.IsAuth ? Localizer.Instance["Llm.AuthFailed"] : Localizer.Instance.Format("Llm.Failed", ex.Message);
        }
        finally
        {
            IsTestingLlm = false;
        }
    }

    [RelayCommand]
    private void ForgetLlmKey()
    {
        SecretStore.Delete(LlmProviders.KeyName(LlmProvider));
        HasSavedLlmKey = false;
        LlmTestStatus = string.Empty;
    }

    partial void OnEnableParserLogsChanged(bool value)
    {
        AppLog.Enabled = value;
        _settings.EnableParserLogs = value;
        _settings.Save();
    }

    partial void OnSoundsChanged(bool value)
    {
        Services.Audio.UiSounds.Enabled = value;
        _settings.Sounds = value;
        _settings.Save();
        if (value)
            Services.Audio.UiSounds.Play(Services.Audio.UiSound.Sent);   // сразу слышно, как звучит
    }

    partial void OnAnimationsChanged(bool value)
    {
        Motion.Motion.SetEnabled(value);
        _settings.Animations = value;
        _settings.Save();
    }

    /// <summary>В системе включено «Уменьшить движение» — галочка всё равно не вернёт полные анимации.</summary>
    public bool IsSystemReducedMotion => Motion.Motion.SystemReduced;

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
        RefreshFollowUps();
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

        var silent = CrmRules.SilentArtists(db);
        var replies = db.IncomingReplies.Where(r => r.ArtistId != null).ToList()
            .GroupBy(r => r.ArtistId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.ReceivedAt).Last());

        // «Молчуны» — в конец списка, ответившие — наверх.
        artists = artists
            .OrderBy(a => silent.Contains(a.Id))
            .ThenByDescending(a => replies.ContainsKey(a.Id))
            .ThenBy(a => a.Nickname)
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
                Beats = items,
                IsSilent = silent.Contains(a.Id),
                ReplyLine = replies.TryGetValue(a.Id, out var reply)
                    ? Localizer.Instance.Format("Crm.Replied", ShortDate(reply.ReceivedAt), reply.Snippet)
                    : string.Empty
            });
        }

        IsCrmEmpty = CrmEntries.Count == 0;
    }

    private static string ShortDate(string stored) =>
        DateTime.TryParse(stored, out var d) ? d.ToString("dd.MM") : stored;

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
                Services.Audio.UiSounds.Play(Services.Audio.UiSound.Sent);
                Toasts.Show(Localizer.Instance["Toast.Sent"]);
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

    public void ShowSettings()
    {
        RefreshDawSummary();
        IsSettingsOpen = true;
    }

    public void ShowParser() => IsParserOpen = true;

    public void CloseOverlays()
    {
        if (MailVm.IsOpen)
            MailVm.CloseCommand.Execute(null);
        IsCrmOpen = false;
        IsSettingsOpen = false;
        IsParserOpen = false;
    }
}
