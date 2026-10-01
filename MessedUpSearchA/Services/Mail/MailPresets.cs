using System;
using System.Linq;

namespace MessedUpSearchA.Services.Mail;

/// <summary>Серверы популярных почт — чтобы битмейкеру не искать «SMTP-порт Яндекса».</summary>
public record MailPreset(string SmtpHost, int SmtpPort, string ImapHost, int ImapPort, string HelpKey);

public static class MailPresets
{
    private static readonly (string[] Domains, MailPreset Preset)[] Known =
    [
        (["gmail.com", "googlemail.com"], new("smtp.gmail.com", 465, "imap.gmail.com", 993, "Mail.HelpGmail")),
        (["yandex.ru", "ya.ru", "yandex.com", "yandex.by", "yandex.kz"], new("smtp.yandex.ru", 465, "imap.yandex.ru", 993, "Mail.HelpYandex")),
        (["mail.ru", "bk.ru", "inbox.ru", "list.ru", "internet.ru"], new("smtp.mail.ru", 465, "imap.mail.ru", 993, "Mail.HelpMailRu")),
        (["icloud.com", "me.com", "mac.com"], new("smtp.mail.me.com", 587, "imap.mail.me.com", 993, "Mail.HelpIcloud")),
        (["rambler.ru", "lenta.ru", "ro.ru"], new("smtp.rambler.ru", 465, "imap.rambler.ru", 993, "Mail.HelpOther")),
    ];

    /// <summary>null — домен незнакомый, серверы вписываются руками.</summary>
    public static MailPreset? For(string address)
    {
        var at = address.LastIndexOf('@');
        if (at < 0)
            return null;

        var domain = address[(at + 1)..].Trim().ToLowerInvariant();
        return Known.FirstOrDefault(k => k.Domains.Contains(domain)).Preset;
    }
}
