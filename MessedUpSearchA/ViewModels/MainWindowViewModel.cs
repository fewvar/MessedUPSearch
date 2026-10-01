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
    [ObservableProperty] private bool _hasSavedMailPassword;
    [ObservableProperty] private bool _isTestingMail;
    [ObservableProperty] private string _mailTestStatus = string.Empty;

    /// <summary>Где взять пароль приложения — под почту, которую ввели.</summary>
    public string MailHelp => Localizer.Instance[MailPresets.For(MailAddress)?.HelpKey ?? "Mail.HelpOther"];

    /// <summary>Идёт анализ бита или поиск парсера — звезда в шапке вращается.</summary>
    public bool IsBusy => _beatsVm.IsAnalyzing || _parserVm.IsSearching;

    /// <summary>Сообщение в футере (Toasts.Show); пустое — показываем статус парсера.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasToast))] private string _toast = string.Empty;
    public bool HasToast => Toast.Length > 0;
    private Avalonia.Threading.DispatcherTimer? _toastTimer;

    public IReadOnlyList<string> LanguageOptions { get; } = new[] { "English", "Русский" };

    public string AppVersion => "v1.0.1";

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
        _hasSavedMailPassword = _mailAddress.Length > 0 && SecretStore.Get(MailQueue.PasswordKey(_mailAddress)) is not null;

        Mail = new MailQueue(() => _settings);
        MailVm = new MailViewModel(Mail, () => _settings);
        Mail.MailSent += () => _artistsVm.LoadArtists();
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

    public void ShowSettings() => IsSettingsOpen = true;

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
