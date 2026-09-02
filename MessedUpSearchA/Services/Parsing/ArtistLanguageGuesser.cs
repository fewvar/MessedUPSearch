using System;
using System.Collections.Generic;
using System.Linq;

namespace MessedUpSearchA.Services.Parsing;

/// <summary>
/// Язык артиста по тексту, который удалось собрать: описание профиля, названия
/// его треков, биография с Genius.
///
/// Раньше смотрели только в описание с Genius — у большинства артистов его нет,
/// и язык у всех оставался «Common». Названия треков оказались куда надёжнее:
/// у вьетнамского продюсера они выглядят как «BÙA YÊU», «TRÚC XINH», и это
/// видно сразу, без всякой лингвистики.
///
/// Порядок такой: сначала письменность (кириллица, вьетнамские диакритики,
/// иероглифы — однозначный сигнал), потом счётчик стоп-слов для латиницы.
/// Если ни то ни другое не сработало, возвращаем null и решение принимает
/// вызывающий — обычно по стране профиля.
/// </summary>
public static class ArtistLanguageGuesser
{
    private const int MinLength = 12;

    /// <summary>
    /// Порог для стоп-слов высокий намеренно. На двух совпадениях вьетнамские
    /// названия («GIU EM DI», «TINH EM LA DAI DUONG») уверенно определялись как
    /// португальские: короткие служебные слова у языков пересекаются, и на
    /// коротком тексте это ловится сплошь и рядом. Лучше не определить, чем
    /// показать неверный язык.
    /// </summary>
    private const int MinStopWordHits = 5;

    /// <summary>Иероглифам хватает и пары слов — там символ несёт куда больше.</summary>
    private const int MinLengthForScript = 4;

    /// <summary>Доля символов алфавита, ниже которой сигнал считается случайным.</summary>
    private const double ScriptThreshold = 0.04;

    private static readonly IReadOnlyDictionary<string, string[]> StopWords =
        new Dictionary<string, string[]>
        {
            ["English"] = new[] { "the", "and", "is", "of", "in", "to", "a", "with", "his", "her", "on", "for", "was", "are", "you", "my", "me" },
            ["French"] = new[] { "le", "la", "les", "et", "un", "une", "des", "de", "du", "est", "dans", "pour", "avec", "je", "tu", "moi" },
            ["Spanish"] = new[] { "el", "la", "los", "las", "y", "de", "que", "en", "un", "una", "con", "para", "es", "yo", "mi", "por" },
            ["German"] = new[] { "der", "die", "das", "und", "ist", "ein", "eine", "mit", "nicht", "von", "auf", "für", "ich", "du" },
            // У португальского и итальянского служебные слова слишком короткие
            // и общие («o», «a», «de», «di»), поэтому берём только те, что почти
            // не встречаются в других языках.
            ["Portuguese"] = new[] { "não", "você", "muito", "sempre", "coração", "amor", "meu", "minha", "estou", "tudo" },
            ["Italian"] = new[] { "che", "sono", "perché", "questo", "quando", "sempre", "cuore", "voglio", "niente" },
            ["Vietnamese"] = new[] { "em", "anh", "khong", "không", "người", "nguoi", "tình", "tinh", "yêu", "yeu", "đi", "một", "cho", "với" },
        };

    private static readonly char[] Separators =
        { ' ', '\t', '\n', '\r', '.', ',', '!', '?', ';', ':', '"', '\'', '(', ')', '[', ']', '-', '|', '/' };

    /// <summary>
    /// Символы, которые есть во вьетнамском и почти нигде больше. Обычные ú, à, ê
    /// сюда не входят намеренно — они встречаются в половине европейских языков.
    /// </summary>
    private const string VietnameseMarkers = "ăĂâÂđĐêÊôÔơƠưƯ";

    public static string? Guess(params string?[] sources)
    {
        var text = string.Join(" ", sources.Where(s => !string.IsNullOrWhiteSpace(s)));

        if (string.IsNullOrWhiteSpace(text))
            return null;

        // Письменность проверяем раньше и на более коротком тексте: три
        // иероглифа — это уже целая фраза, а три латинских слова ещё ничего.
        if (text.Trim().Length >= MinLengthForScript)
        {
            var byScript = GuessByScript(text);
            if (byScript is not null)
                return byScript;
        }

        return text.Trim().Length >= MinLength ? GuessByStopWords(text) : null;
    }

    /// <summary>Письменность — самый надёжный признак, если её заметно много.</summary>
    private static string? GuessByScript(string text)
    {
        var letters = text.Count(char.IsLetter);
        if (letters == 0)
            return null;

        double Share(Func<char, bool> match) => (double)text.Count(c => match(c)) / letters;

        if (Share(IsCyrillic) > ScriptThreshold)
            return "Russian";

        if (Share(c => VietnameseMarkers.Contains(c)) > ScriptThreshold)
            return "Vietnamese";

        if (Share(IsHangul) > ScriptThreshold)
            return "Korean";

        // Кана однозначно японская; общие с китайским иероглифы сами по себе
        // значат мало, поэтому смотрим именно на неё.
        if (Share(IsKana) > ScriptThreshold)
            return "Japanese";

        if (Share(IsHan) > ScriptThreshold)
            return "Chinese";

        if (Share(IsArabic) > ScriptThreshold)
            return "Arabic";

        return null;
    }

    private static string? GuessByStopWords(string text)
    {
        var words = text.ToLowerInvariant().Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return null;

        string? best = null;
        var bestScore = 0;

        foreach (var (language, stopWords) in StopWords)
        {
            var score = words.Count(w => stopWords.Contains(w));
            if (score > bestScore)
            {
                bestScore = score;
                best = language;
            }
        }

        return bestScore >= MinStopWordHits ? best : null;
    }

    private static bool IsCyrillic(char c) => c is >= 'Ѐ' and <= 'ӿ';
    private static bool IsHangul(char c) => c is >= '가' and <= '힣' or >= 'ᄀ' and <= 'ᇿ';
    private static bool IsKana(char c) => c is >= '぀' and <= 'ヿ';
    private static bool IsHan(char c) => c is >= '一' and <= '鿿';
    private static bool IsArabic(char c) => c is >= '؀' and <= 'ۿ';
}
