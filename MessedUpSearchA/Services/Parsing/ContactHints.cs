using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MessedUpSearchA.Services.Parsing.Sources;

namespace MessedUpSearchA.Services.Parsing;

public enum ContactKind { Email, Telegram, Instagram }

public record ContactHint(ContactKind Kind, string Value);

/// <summary>
/// Контакты из описания профиля. Сами в карточку не пишутся: битмейкер видит подсказку
/// «нашли в профиле» и сохраняет сам — описание бывает чужим (менеджер, лейбл).
/// </summary>
public static partial class ContactHints
{
    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+(\.[A-Za-z0-9\-]+)*\.[A-Za-z]{2,}")]
    private static partial Regex EmailPattern();

    // «name (at) gmail (dot) com», «name [at] gmail.com» — так прячут почту от ботов.
    [GeneratedRegex(@"\s*[\(\[\{]\s*(at|собака)\s*[\)\]\}]\s*", RegexOptions.IgnoreCase)]
    private static partial Regex ObfuscatedAt();

    [GeneratedRegex(@"\s*[\(\[\{]\s*(dot|точка)\s*[\)\]\}]\s*", RegexOptions.IgnoreCase)]
    private static partial Regex ObfuscatedDot();

    [GeneratedRegex(@"(?:t\.me|telegram\.me)/([A-Za-z0-9_]{5,32})", RegexOptions.IgnoreCase)]
    private static partial Regex TelegramLink();

    [GeneratedRegex(@"\b(?:telegram|tg|тг|телеграм)(?:\s*[:\-–—]\s*@?|\s+@)([A-Za-z0-9_]{5,32})", RegexOptions.IgnoreCase)]
    private static partial Regex TelegramHandle();

    [GeneratedRegex(@"instagram\.com/([A-Za-z0-9._]{1,30})", RegexOptions.IgnoreCase)]
    private static partial Regex InstagramLink();

    // «ig: @nick», «Instagram: nick», «инст @nick»; без двоеточия — только с @, иначе ловит обычные слова.
    [GeneratedRegex(@"\b(?:instagram|insta|inst|ig|инст)(?:\s*[:\-–—]\s*@?|\s+@)([A-Za-z0-9._]{2,30})", RegexOptions.IgnoreCase)]
    private static partial Regex InstagramHandle();

    public static IReadOnlyList<ContactHint> Extract(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var hints = new List<ContactHint>();
        var plain = ObfuscatedDot().Replace(ObfuscatedAt().Replace(text, "@"), ".");

        foreach (Match m in EmailPattern().Matches(plain))
            hints.Add(new ContactHint(ContactKind.Email, m.Value.Trim('.').ToLowerInvariant()));

        foreach (Match m in TelegramLink().Matches(text).Concat(TelegramHandle().Matches(text)))
            if (IsHandle(m.Groups[1].Value))
                hints.Add(new ContactHint(ContactKind.Telegram, "@" + m.Groups[1].Value));

        foreach (Match m in InstagramLink().Matches(text).Concat(InstagramHandle().Matches(text)))
        {
            var handle = m.Groups[1].Value.TrimEnd('.');
            if (IsHandle(handle) && handle is not ("p" or "reel" or "stories"))
                hints.Add(new ContactHint(ContactKind.Instagram, "@" + handle));
        }

        return hints
            .DistinctBy(h => (h.Kind, h.Value.ToLowerInvariant()))
            .ToList();
    }

    /// <summary>«Instagram: https://…» — после двоеточия ссылка, а не ник; её поймает поиск ссылок.</summary>
    private static bool IsHandle(string value) =>
        !value.Equals("http", StringComparison.OrdinalIgnoreCase) &&
        !value.Equals("https", StringComparison.OrdinalIgnoreCase) &&
        !value.StartsWith("www.", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Свежее описание профиля по ссылке. Умеют SoundCloud и Audius — откуда приходит
    /// большая часть артистов; для остальных площадок null.
    /// </summary>
    public static async Task<string?> FetchBioAsync(string profileUrl, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(profileUrl.Trim(), UriKind.Absolute, out var uri))
            return null;

        var host = uri.Host.ToLowerInvariant();
        if (host.EndsWith("soundcloud.com"))
            return await new SoundCloudSource().GetBioAsync(uri.ToString(), ct);
        if (host.EndsWith("audius.co"))
            return await new AudiusSource().GetBioAsync(uri.ToString(), ct);

        return null;
    }
}
