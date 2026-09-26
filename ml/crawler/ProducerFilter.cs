using System.Text.RegularExpressions;
using MessedUpSearchA.Services.Parsing;

namespace Crawler;

/// <summary>
/// Индекс для «кому продать бит» должен состоять из тех, кто читает. Поиск по жанровому
/// тегу приводит ещё к пяти типам аккаунтов, и все они проверены глазами на пилоте:
///
///   битмейкеры     «rage type beat», «free for profit», свой ник в «(Prod. POT Cam)»
///   эдит-каналы    «SUPER SLOWED», «sped up», nightcore
///   миксы и радио  «Asian Archive Mix 10», «Dark Trap Mix»
///   перезаливы     «Daniel Allan - Take Me Under»: в названии чужой артист через дефис
///   ремиксеры/DJ   «skrillex - summit (7amr Remix)»
///   лейблы         «Coop Records», «BADMOUTH RECS»
///
/// «prod.» с ЧУЖИМ ником — признак рэпера: он так указывает, чей бит взял.
/// </summary>
public static partial class ProducerFilter
{
    [GeneratedRegex(@"type\s*beat|free\s*(for\s*)?profit|instrumental|\binstru\b|\bbeat\b|\[free\]|\(free\)|beat\s*tape",
        RegexOptions.IgnoreCase)]
    private static partial Regex BeatTitle();

    [GeneratedRegex(@"slowed|sped\s*up|reverb|nightcore|\bedit\b|\bmix\b|\bmixtape\s*mix\b|\bremix\b|\bset\b|\bpodcast\b|\bradio\b|\bepisode\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex EditTitle();

    [GeneratedRegex(@"beats\b|beatz|\bprod\b|producer|onthebeat|on\s*the\s*beat|instrumentals|records\b|\brecs\b|\blabel\b|music\s*group|\bmixes\b|archive|radio|slowed|\bedits\b|nightcore|\bdj\b|collective",
        RegexOptions.IgnoreCase)]
    private static partial Regex NotRapperNick();

    [GeneratedRegex(@"\(?\[?prod(?:uced)?\.?\s*(?:by\s*)?([^)\]]+)[)\]]?", RegexOptions.IgnoreCase)]
    private static partial Regex ProdCredit();

    /// <summary>Пустая строка — похож на рэпера. Иначе — почему нет (для разбора глазами).</summary>
    public static string Reason(ArtistCandidate artist) =>
        Reason(artist.Nickname, artist.Tracks.Select(t => t.Title).ToList());

    public static string Reason(string nickname, IReadOnlyList<string> titles)
    {
        var nick = NotRapperNick().Match(nickname);
        if (nick.Success)
            return $"ник: «{nick.Value}»";

        if (titles.Count == 0)
            return string.Empty;

        var half = titles.Count / 2.0;
        var own = Normalize(nickname);

        var beats = titles.Count(t => BeatTitle().IsMatch(t));
        if (beats > half)
            return $"биты: {beats}/{titles.Count}";

        var edits = titles.Count(t => EditTitle().IsMatch(t));
        if (edits > half)
            return $"эдиты/миксы/ремиксы: {edits}/{titles.Count}";

        // Свой ник в «prod.» — значит, это он делает биты, а не читает на чужих.
        var selfProduced = titles.Count(t => ProdCredit().Match(t) is { Success: true } m &&
                                              own.Length >= 3 && Normalize(m.Groups[1].Value).Contains(own));
        if (selfProduced > half)
            return $"сам себе продюсер: {selfProduced}/{titles.Count}";

        // «Кто-то - название», где «кто-то» — не он: перезалив чужих треков или лейбл.
        var foreign = titles.Count(t => IsForeignArtistTitle(t, own));
        if (foreign > half)
            return $"чужие артисты в названиях: {foreign}/{titles.Count}";

        return string.Empty;
    }

    private static bool IsForeignArtistTitle(string title, string own)
    {
        var dash = title.IndexOf(" - ", StringComparison.Ordinal);
        if (dash <= 0)
            return false;

        var left = Normalize(title[..dash]);
        return left.Length > 0 && own.Length > 0 && !left.Contains(own) && !own.Contains(left);
    }

    private static string Normalize(string text) =>
        new string(text.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
}
