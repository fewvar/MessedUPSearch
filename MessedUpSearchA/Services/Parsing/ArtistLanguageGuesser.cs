using System;
using System.Collections.Generic;
using System.Linq;

namespace MessedUpSearchA.Services.Parsing;

/// <summary>
/// У Genius нет поля языка ни у артиста, ни у песни (проверено 2026-07-31 по исходникам
/// клиентов-обёрток API). Грубая эвристика по описанию артиста вместо честного детектора:
/// кириллица -> Russian, иначе — счётчик стоп-слов. На коротком/пустом тексте не угадывает,
/// просто возвращает null — тогда в дело идёт старый GuessLanguage по стране.
/// </summary>
public static class ArtistLanguageGuesser
{
    private const int MinLength = 15;
    private const int MinStopWordHits = 2;

    private static readonly IReadOnlyDictionary<string, string[]> StopWords =
        new Dictionary<string, string[]>
        {
            ["English"] = new[] { "the", "and", "is", "of", "in", "to", "a", "with", "his", "her", "on", "for", "was", "are" },
            ["French"] = new[] { "le", "la", "les", "et", "un", "une", "des", "de", "du", "est", "dans", "pour", "avec" },
            ["Spanish"] = new[] { "el", "la", "los", "las", "y", "de", "que", "en", "un", "una", "con", "para", "es" },
            ["German"] = new[] { "der", "die", "das", "und", "ist", "ein", "eine", "mit", "nicht", "von", "auf", "für" }
        };

    private static readonly char[] Separators =
        { ' ', '\t', '\n', '\r', '.', ',', '!', '?', ';', ':', '"', '\'', '(', ')' };

    public static string? Guess(string description)
    {
        if (string.IsNullOrWhiteSpace(description) || description.Trim().Length < MinLength)
            return null;

        if (ContainsCyrillic(description))
            return "Russian";

        var words = description.ToLowerInvariant().Split(Separators, StringSplitOptions.RemoveEmptyEntries);
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

    private static bool ContainsCyrillic(string text) => text.Any(c => c is >= 'Ѐ' and <= 'ӿ');
}
