using System.Linq;

namespace MessedUpSearchA.Converters;

/// <summary>
/// Артисты добавляются и парсером (уже полный URL), и руками (часто просто "@nick").
/// Приводит оба варианта к открываемой ссылке; пустая строка — нечего открывать.
/// </summary>
public static class SocialLinks
{
    public static string ResolveInstagram(string? raw)
    {
        var value = (raw ?? string.Empty).Trim();
        if (value.Length == 0)
            return string.Empty;

        if (LooksLikeUrl(value))
            return WithScheme(value);

        var handle = value.TrimStart('@');
        return IsPlausibleHandle(handle) ? $"https://instagram.com/{handle}" : string.Empty;
    }

    public static string ResolveSpotify(string? raw)
    {
        var value = (raw ?? string.Empty).Trim();
        if (value.Length == 0)
            return string.Empty;

        // В отличие от Instagram, вручную вбитый текст в это поле не всегда ник —
        // открываем только когда это уже похоже на настоящую ссылку.
        return LooksLikeUrl(value) && value.Contains("spotify.com") ? WithScheme(value) : string.Empty;
    }

    private static bool LooksLikeUrl(string value) =>
        !value.StartsWith('@') &&
        (value.StartsWith("http://") || value.StartsWith("https://") || value.Contains('.'));

    private static string WithScheme(string value) =>
        value.StartsWith("http://") || value.StartsWith("https://") ? value : "https://" + value;

    private static bool IsPlausibleHandle(string handle) =>
        handle.Length is > 0 and <= 30
        && !handle.Contains(' ')
        && !handle.All(char.IsDigit);
}
