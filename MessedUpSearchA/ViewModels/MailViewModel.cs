using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessedUpSearchA.Data;
using MessedUpSearchA.Models;
using MessedUpSearchA.Services;
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
    private Beat? _beat;

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

    public MailViewModel(MailQueue queue, Func<AppSettings> settings)
    {
        _queue = queue;
        _settings = settings;
    }

    /// <summary>Открыть рассылку бита; preselect — кого отметить (остальные получатели — привязанные к биту).</summary>
    public void Open(int beatId, IReadOnlyCollection<int> artistIds)
    {
        using var db = new AppDbContext();
        _beat = db.Beats.FirstOrDefault(b => b.Id == beatId);
        if (_beat is null)
            return;

        MailTemplates.EnsureSeeded(db);
        Templates.Clear();
        foreach (var t in db.MailTemplates.Where(t => t.Kind == MailKinds.Pitch).OrderBy(t => t.Id).ToList())
            Templates.Add(t);

        var lastId = _settings().LastPitchTemplateId;
        SelectedTemplate = Templates.FirstOrDefault(t => t.Id == lastId)
                           ?? Templates.FirstOrDefault(t => t.Name.Contains(Localizer.Instance.Language == AppLanguage.Russian ? "RU" : "EN"))
                           ?? Templates.FirstOrDefault();

        BeatTitle = _beat.BeatName;
        LoadRecipients(db, artistIds);
        PreviewRecipient = Recipients.FirstOrDefault(r => r.IsSelected) ?? Recipients.FirstOrDefault();
        Validate();
        IsOpen = true;
    }

    private void LoadRecipients(AppDbContext db, IReadOnlyCollection<int> artistIds)
    {
        foreach (var r in Recipients)
            r.PropertyChanged -= OnRecipientChanged;
        Recipients.Clear();

        var guardSince = DateTime.Now.AddDays(-SpamGuardDays).ToString("yyyy-MM-dd HH:mm");
        var sent = db.OutgoingMails.Where(m => m.Status == MailStatuses.Sent && m.ArtistId != null)
            .Select(m => new { m.ArtistId, m.BeatId, m.SentAt })
            .ToList();

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

            var item = new MailRecipientItem
            {
                ArtistId = artist.Id,
                Nickname = artist.Nickname,
                AvatarPath = artist.AvatarPath,
                AvatarColor = string.IsNullOrWhiteSpace(artist.AvatarColor) ? "#888888" : artist.AvatarColor,
                Email = artist.Email.Trim(),
                Warning = warning,
                IsSelected = warning.Length == 0,
                EmailHints = ContactHints.Extract(artist.Bio)
                    .Where(h => h.Kind == ContactKind.Email).Select(h => h.Value).ToList()
            };
            item.PropertyChanged += OnRecipientChanged;
            Recipients.Add(item);
        }
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

    partial void OnEditSubjectChanged(string value) => RenderPreview();
    partial void OnEditBodyChanged(string value) => RenderPreview();
    partial void OnPreviewRecipientChanged(MailRecipientItem? value) => RenderPreview();

    private void RenderPreview()
    {
        if (_beat is null || PreviewRecipient is null)
        {
            PreviewSubject = PreviewBody = string.Empty;
            return;
        }

        var artist = new Artist { Nickname = PreviewRecipient.Nickname };
        var me = MailQueue.SenderName(_settings());
        PreviewSubject = MailTemplates.Render(EditSubject, artist, _beat, me);
        PreviewBody = MailTemplates.Render(EditBody, artist, _beat, me);
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
        else if (_beat is not null && string.IsNullOrWhiteSpace(_beat.ShareUrl) &&
                 (EditBody + EditSubject).Contains("{link}", StringComparison.OrdinalIgnoreCase))
            Problem = Localizer.Instance["Mail.NoLink"];
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
            Kind = MailKinds.Pitch,
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
        if (!CanSend || _beat is null)
            return;

        SaveTemplateEdits(SelectedTemplate);
        var settings = _settings();
        if (SelectedTemplate is not null)
        {
            settings.LastPitchTemplateId = SelectedTemplate.Id;
            settings.Save();
        }

        var me = MailQueue.SenderName(settings);
        var jobs = Recipients.Where(r => r.IsSelected && r.HasEmail).Select(r =>
        {
            var artist = new Artist { Nickname = r.Nickname };
            return new MailJob(r.ArtistId, _beat.Id, SelectedTemplate?.Id, MailKinds.Pitch, r.Email,
                MailTemplates.Render(EditSubject, artist, _beat, me),
                MailTemplates.Render(EditBody, artist, _beat, me));
        }).ToList();

        _queue.Enqueue(jobs);
        Toasts.Show(Localizer.Instance.Format("Mail.Started", jobs.Count));
        IsOpen = false;
    }

    [RelayCommand]
    private void Close()
    {
        SaveTemplateEdits(SelectedTemplate);
        IsOpen = false;
    }

    private static string ShortDate(string stored) =>
        DateTime.TryParse(stored, out var d) ? d.ToString("dd.MM") : stored;

    private static int DaysAgo(string stored) =>
        DateTime.TryParse(stored, out var d) ? Math.Max(0, (DateTime.Now.Date - d.Date).Days) : 0;
}
