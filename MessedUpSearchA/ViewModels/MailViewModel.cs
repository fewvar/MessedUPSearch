using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessedUpSearchA.Data;
using MessedUpSearchA.Models;
using MessedUpSearchA.Services;
using MessedUpSearchA.Services.Llm;
using MessedUpSearchA.Services.Localization;
using MessedUpSearchA.Services.Mail;
using MessedUpSearchA.Services.Parsing;

namespace MessedUpSearchA.ViewModels;

/// <summary>
/// Окно рассылки одного бита: получатели -> шаблон -> предпросмотр -> в очередь.
/// Текст правится прямо здесь и сохраняется в шаблон: что видно в предпросмотре, то и уйдёт.
/// </summary>
public partial class MailViewModel : ViewModelBase
{
    /// <summary>Писали артисту недавно — второе письмо раньше этого срока похоже на спам.</summary>
    public const int SpamGuardDays = 7;

    private readonly MailQueue _queue;
    private readonly Func<AppSettings> _settings;
    private readonly Func<LlmClient?> _llm;
    private Beat? _beat;

    /// <summary>Фоллоу-апы: разные биты, ответы в старые переписки, шаблоны вида «фоллоу-ап».</summary>
    [ObservableProperty] private bool _isFollowUpMode;
    private string Kind => IsFollowUpMode ? MailKinds.FollowUp : MailKinds.Pitch;
    private System.Threading.CancellationTokenSource? _personalizeCts;

    [ObservableProperty] private bool _isPersonalizing;
    [ObservableProperty] private string _personalizeStatus = string.Empty;
    [ObservableProperty] private bool _hasPersonalized;

    public ObservableCollection<MailRecipientItem> Recipients { get; } = new();
    public ObservableCollection<MailTemplate> Templates { get; } = new();

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private string _beatTitle = string.Empty;

    [ObservableProperty] private MailTemplate? _selectedTemplate;
    [ObservableProperty] private string _editSubject = string.Empty;
    [ObservableProperty] private string _editBody = string.Empty;

    [ObservableProperty] private MailRecipientItem? _previewRecipient;
    [ObservableProperty] private string _previewSubject = string.Empty;
    [ObservableProperty] private string _previewBody = string.Empty;

    /// <summary>Что мешает отправить: нет почты в настройках, нет ссылки на бит, никто не выбран.</summary>
    [ObservableProperty] private string _problem = string.Empty;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private bool _canSend;

    public string PlaceholdersHint => string.Join(" ", MailTemplates.Placeholders);

    public MailViewModel(MailQueue queue, Func<AppSettings> settings, Func<LlmClient?> llm)
    {
        _queue = queue;
        _settings = settings;
        _llm = llm;
    }

    /// <summary>Открыть рассылку бита; preselect — кого отметить (остальные получатели — привязанные к биту).</summary>
    public void Open(int beatId, IReadOnlyCollection<int> artistIds)
    {
        using var db = new AppDbContext();
        _beat = db.Beats.FirstOrDefault(b => b.Id == beatId);
        if (_beat is null)
            return;

        IsFollowUpMode = false;
        LoadTemplates(db);
        BeatTitle = _beat.BeatName;
        LoadRecipients(db, artistIds);
        Show();
    }

    /// <summary>Фоллоу-апы тем, кто молчит дольше срока: по одному на бит, ответом в ту же переписку.</summary>
    public void OpenFollowUps(IReadOnlyCollection<FollowUpDue> due)
    {
        if (due.Count == 0)
            return;

        using var db = new AppDbContext();
        _beat = null;
        IsFollowUpMode = true;
        LoadTemplates(db);
        BeatTitle = Localizer.Instance.Format("Mail.FollowUpsTitle", due.Count);

        ClearRecipients();
        var artistIds = due.Select(d => d.ArtistId).ToList();
        var beatIds = due.Select(d => d.BeatId).ToList();
        var artists = db.Artists.Where(a => artistIds.Contains(a.Id)).ToDictionary(a => a.Id);
        var beats = db.Beats.Where(b => beatIds.Contains(b.Id)).ToDictionary(b => b.Id);
        var tracks = TopTracks(db, artistIds);

        foreach (var d in due.OrderBy(d => d.Pitch.SentAt))
        {
            if (!artists.TryGetValue(d.ArtistId, out var artist) || !beats.TryGetValue(d.BeatId, out var beat))
                continue;
            AddRecipient(new MailRecipientItem
            {
                ArtistId = artist.Id,
                Beat = beat,
                InReplyTo = d.Pitch.MessageId.Length > 0 ? d.Pitch.MessageId : null,
                BeatLine = Localizer.Instance.Format("Mail.FollowUpLine", beat.BeatName, ShortDate(d.Pitch.SentAt)),
                Nickname = artist.Nickname,
                AvatarPath = artist.AvatarPath,
                AvatarColor = string.IsNullOrWhiteSpace(artist.AvatarColor) ? "#888888" : artist.AvatarColor,
                // Адрес — тот же, на который ушло первое письмо: переписка должна продолжиться там же.
                Email = d.Pitch.ToAddress,
                IsSelected = true,
                Genre = artist.AiGenreTags,
                TopTrack = tracks.GetValueOrDefault(artist.Id, string.Empty)
            });
        }

        Show();
    }

    private void Show()
    {
        PersonalizeStatus = string.Empty;
        HasPersonalized = false;
        PreviewRecipient = Recipients.FirstOrDefault(r => r.IsSelected) ?? Recipients.FirstOrDefault();
        Validate();
        IsOpen = true;
    }

    private void LoadTemplates(AppDbContext db)
    {
        MailTemplates.EnsureSeeded(db);
        _suppressSave = true;
        Templates.Clear();
        foreach (var t in db.MailTemplates.Where(t => t.Kind == Kind).OrderBy(t => t.Id).ToList())
            Templates.Add(t);

        var lastId = IsFollowUpMode ? _settings().LastFollowUpTemplateId : _settings().LastPitchTemplateId;
        SelectedTemplate = Templates.FirstOrDefault(t => t.Id == lastId)
                           ?? Templates.FirstOrDefault(t => t.Name.Contains(Localizer.Instance.Language == AppLanguage.Russian ? "RU" : "EN"))
                           ?? Templates.FirstOrDefault();
        _suppressSave = false;
    }

    private static Dictionary<int, string> TopTracks(AppDbContext db, ICollection<int> artistIds) =>
        db.ArtistTracks.Where(t => artistIds.Contains(t.ArtistId)).ToList()
            .GroupBy(t => t.ArtistId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(t => t.PlayCount).First().Title);

    private void ClearRecipients()
    {
        foreach (var r in Recipients)
            r.PropertyChanged -= OnRecipientChanged;
        Recipients.Clear();
    }

    private void AddRecipient(MailRecipientItem item)
    {
        item.PropertyChanged += OnRecipientChanged;
        Recipients.Add(item);
    }

    private void LoadRecipients(AppDbContext db, IReadOnlyCollection<int> artistIds)
    {
        ClearRecipients();
        var silent = CrmRules.SilentArtists(db);
        var items = new List<MailRecipientItem>();

        var guardSince = DateTime.Now.AddDays(-SpamGuardDays).ToString("yyyy-MM-dd HH:mm");
        var sent = db.OutgoingMails.Where(m => m.Status == MailStatuses.Sent && m.ArtistId != null)
            .Select(m => new { m.ArtistId, m.BeatId, m.SentAt })
            .ToList();

        var topTracks = TopTracks(db, artistIds.ToList());

        foreach (var artist in db.Artists.Where(a => artistIds.Contains(a.Id)).OrderBy(a => a.Nickname).ToList())
        {
            var mine = sent.Where(m => m.ArtistId == artist.Id).ToList();
            var sameBeat = mine.Where(m => m.BeatId == _beat!.Id).Select(m => m.SentAt).Max();
            var last = mine.Select(m => m.SentAt).Max();

            var warning = string.Empty;
            if (string.IsNullOrWhiteSpace(artist.Email))
                warning = Localizer.Instance["Mail.NoEmail"];
            else if (sameBeat is not null)
                warning = Localizer.Instance.Format("Mail.AlreadySent", ShortDate(sameBeat));
            else if (last is not null && string.CompareOrdinal(last, guardSince) > 0)
                warning = Localizer.Instance.Format("Mail.RecentlyWritten", DaysAgo(last));
            else if (artist.IsRedFlagged)
                warning = Localizer.Instance["Mail.RedFlag"];
            else if (silent.Contains(artist.Id))
                warning = Localizer.Instance.Format("Mail.Silent", CrmRules.SilentAfter);

            var item = new MailRecipientItem
            {
                ArtistId = artist.Id,
                Beat = _beat!,
                Nickname = artist.Nickname,
                AvatarPath = artist.AvatarPath,
                AvatarColor = string.IsNullOrWhiteSpace(artist.AvatarColor) ? "#888888" : artist.AvatarColor,
                Email = artist.Email.Trim(),
                Warning = warning,
                IsSelected = warning.Length == 0,
                Genre = artist.AiGenreTags,
                TopTrack = topTracks.GetValueOrDefault(artist.Id, string.Empty),
                EmailHints = ContactHints.Extract(artist.Bio)
                    .Where(h => h.Kind == ContactKind.Email).Select(h => h.Value).ToList()
            };
            items.Add(item);
        }

        // «Молчуны» — в конец: им писать в последнюю очередь.
        foreach (var item in items.OrderBy(i => silent.Contains(i.ArtistId)))
            AddRecipient(item);
    }

    private void OnRecipientChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MailRecipientItem.IsSelected))
            Validate();
    }

    /// <summary>Почта из описания профиля -> в карточку артиста и сразу в получатели.</summary>
    public void UseEmailHint(MailRecipientItem item)
    {
        if (!item.HasEmailHint)
            return;

        using (var db = new AppDbContext())
        {
            var artist = db.Artists.FirstOrDefault(a => a.Id == item.ArtistId);
            if (artist is null)
                return;
            artist.Email = item.EmailHint;
            db.SaveChanges();
        }

        item.Email = item.EmailHint;
        item.Warning = string.Empty;
        item.IsSelected = true;
        ContactsChanged?.Invoke();
        Validate();
    }

    /// <summary>Почта артиста изменилась отсюда — вкладке артистов перечитать список.</summary>
    public event Action? ContactsChanged;

    partial void OnSelectedTemplateChanged(MailTemplate? oldValue, MailTemplate? newValue)
    {
        if (!_suppressSave)
            SaveTemplateEdits(oldValue);
        EditSubject = newValue?.Subject ?? string.Empty;
        EditBody = newValue?.Body ?? string.Empty;
    }

    // Шаблон поменяли — переписанные письма больше ему не соответствуют.
    partial void OnEditSubjectChanged(string value) { ResetPersonalization(); RenderPreview(); }
    partial void OnEditBodyChanged(string value) { ResetPersonalization(); RenderPreview(); }
    partial void OnPreviewRecipientChanged(MailRecipientItem? value) => RenderPreview();

    private void RenderPreview()
    {
        if (PreviewRecipient is null)
        {
            PreviewSubject = PreviewBody = string.Empty;
            return;
        }

        var (subject, body) = LetterFor(PreviewRecipient);
        PreviewSubject = subject;
        PreviewBody = body;
        Validate();
    }

    private void Validate()
    {
        var settings = _settings();
        var selected = Recipients.Count(r => r.IsSelected && r.HasEmail);
        var remaining = _queue.RemainingToday();

        Problem = string.Empty;
        if (string.IsNullOrWhiteSpace(settings.MailAddress) || string.IsNullOrWhiteSpace(settings.SmtpHost))
            Problem = Localizer.Instance["Mail.SetupFirst"];
        else if ((EditBody + EditSubject).Contains("{link}", StringComparison.OrdinalIgnoreCase) &&
                 Recipients.FirstOrDefault(r => r.IsSelected && string.IsNullOrWhiteSpace(r.Beat.ShareUrl)) is { } noLink)
            Problem = IsFollowUpMode
                ? Localizer.Instance.Format("Mail.NoLinkFor", noLink.Beat.BeatName)
                : Localizer.Instance["Mail.NoLink"];
        else if (string.IsNullOrWhiteSpace(EditBody))
            Problem = Localizer.Instance["Mail.EmptyBody"];
        else if (selected > remaining)
            Problem = Localizer.Instance.Format("Mail.OverLimit", remaining);

        Summary = Localizer.Instance.Format("Mail.Summary", selected, remaining);
        CanSend = Problem.Length == 0 && selected > 0;
    }

    /// <summary>Правки текста — в сам шаблон: в следующий раз он откроется таким же.</summary>
    private void SaveTemplateEdits(MailTemplate? template)
    {
        if (template is null || (template.Subject == EditSubject && template.Body == EditBody))
            return;

        template.Subject = EditSubject;
        template.Body = EditBody;
        using var db = new AppDbContext();
        var stored = db.MailTemplates.FirstOrDefault(t => t.Id == template.Id);
        if (stored is null)
            return;
        stored.Subject = EditSubject;
        stored.Body = EditBody;
        db.SaveChanges();
    }

    [RelayCommand]
    private void NewTemplate()
    {
        SaveTemplateEdits(SelectedTemplate);
        using var db = new AppDbContext();
        var template = new MailTemplate
        {
            Name = Localizer.Instance.Format("Mail.NewTemplateName", Templates.Count + 1),
            Kind = Kind,
            Subject = EditSubject,
            Body = EditBody,
            CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm")
        };
        db.MailTemplates.Add(template);
        db.SaveChanges();
        Templates.Add(template);
        SelectedTemplate = template;
    }

    [RelayCommand]
    private void DeleteTemplate()
    {
        if (SelectedTemplate is null || Templates.Count <= 1)
            return;

        using (var db = new AppDbContext())
        {
            var stored = db.MailTemplates.FirstOrDefault(t => t.Id == SelectedTemplate.Id);
            if (stored is not null)
            {
                db.MailTemplates.Remove(stored);
                db.SaveChanges();
            }
        }

        var gone = SelectedTemplate;
        _suppressSave = true;
        SelectedTemplate = Templates.FirstOrDefault(t => t != gone);
        _suppressSave = false;
        Templates.Remove(gone);
    }

    private bool _suppressSave;

    [RelayCommand]
    private void Send()
    {
        Validate();
        if (!CanSend)
            return;

        SaveTemplateEdits(SelectedTemplate);
        var settings = _settings();
        if (SelectedTemplate is not null)
        {
            if (IsFollowUpMode) settings.LastFollowUpTemplateId = SelectedTemplate.Id;
            else settings.LastPitchTemplateId = SelectedTemplate.Id;
            settings.Save();
        }

        var jobs = Recipients.Where(r => r.IsSelected && r.HasEmail).Select(r =>
        {
            var (subject, body) = LetterFor(r);
            return new MailJob(r.ArtistId, r.Beat.Id, SelectedTemplate?.Id, Kind, r.Email, subject, body, r.InReplyTo);
        }).ToList();

        _queue.Enqueue(jobs);
        Toasts.Show(Localizer.Instance.Format("Mail.Started", jobs.Count));
        IsOpen = false;
    }

    /// <summary>Письмо этому артисту: переписанное нейросетью, если есть, иначе шаблон.</summary>
    private (string Subject, string Body) LetterFor(MailRecipientItem r)
    {
        if (r.IsPersonalized)
            return (r.CustomSubject, r.CustomBody);

        var artist = new Artist { Nickname = r.Nickname };
        var me = MailQueue.SenderName(_settings());
        return (MailTemplates.Render(EditSubject, artist, r.Beat, me), MailTemplates.Render(EditBody, artist, r.Beat, me));
    }

    /// <summary>
    /// «Оживить»: каждому отмеченному — своё письмо, переписанное под него (ник, жанр, один трек).
    /// Всё видно в предпросмотре до отправки; не вышло у кого-то — ему уйдёт шаблон.
    /// </summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task PersonalizeAsync()
    {
        if (IsPersonalizing)
        {
            _personalizeCts?.Cancel();
            return;
        }

        if (_llm() is not { } client)
        {
            PersonalizeStatus = Localizer.Instance["Llm.SetupFirst"];
            return;
        }

        var targets = Recipients.Where(r => r.IsSelected && r.HasEmail).ToList();
        if (targets.Count == 0)
            return;

        IsPersonalizing = true;
        _personalizeCts = new System.Threading.CancellationTokenSource();
        int done = 0, failed = 0;
        try
        {
            foreach (var r in targets)
            {
                PersonalizeStatus = Localizer.Instance.Format("Llm.Progress", done + failed + 1, targets.Count);
                r.CustomSubject = r.CustomBody = string.Empty;
                var (subject, body) = LetterFor(r);
                try
                {
                    var result = await LetterPersonalizer.RewriteAsync(client,
                        new ArtistFacts(r.Nickname, r.Genre, r.TopTrack), subject, body, r.Beat.ShareUrl,
                        _personalizeCts.Token);
                    if (result is { } letter)
                    {
                        r.CustomSubject = letter.Subject;
                        r.CustomBody = letter.Body;
                        done++;
                    }
                    else
                        failed++;
                }
                catch (LlmException ex)
                {
                    AppLog.Write($"оживить {r.Nickname}: {ex.Message}");
                    failed++;
                    if (ex.IsAuth)
                    {
                        PersonalizeStatus = Localizer.Instance["Llm.AuthFailed"];
                        return;
                    }
                }

                if (r == PreviewRecipient)
                    RenderPreview();
            }

            PersonalizeStatus = failed == 0
                ? Localizer.Instance.Format("Llm.Done", done)
                : Localizer.Instance.Format("Llm.DonePartly", done, failed);
        }
        catch (OperationCanceledException)
        {
            PersonalizeStatus = Localizer.Instance.Format("Llm.Done", done);
        }
        finally
        {
            IsPersonalizing = false;
            HasPersonalized = Recipients.Any(r => r.IsPersonalized);
            _personalizeCts.Dispose();
            _personalizeCts = null;
            RenderPreview();
        }
    }

    /// <summary>Вернуть всем шаблонный текст.</summary>
    [RelayCommand]
    private void ResetPersonalization()
    {
        if (IsPersonalizing)
            return;
        foreach (var r in Recipients)
            r.CustomSubject = r.CustomBody = string.Empty;
        if (HasPersonalized)
            PersonalizeStatus = string.Empty;
        HasPersonalized = false;
    }

    [RelayCommand]
    private void Close()
    {
        _personalizeCts?.Cancel();
        SaveTemplateEdits(SelectedTemplate);
        IsOpen = false;
    }

    private static string ShortDate(string stored) =>
        DateTime.TryParse(stored, out var d) ? d.ToString("dd.MM") : stored;

    private static int DaysAgo(string stored) =>
        DateTime.TryParse(stored, out var d) ? Math.Max(0, (DateTime.Now.Date - d.Date).Days) : 0;
}
