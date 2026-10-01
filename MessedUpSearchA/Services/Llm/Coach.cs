using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MessedUpSearchA.Services.Llm;

/// <summary>
/// Совет на сегодня живым языком. Нейросеть получает только факты, посчитанные приложением
/// (часы, жанры, проценты, названия битов) — без почт и имён артистов — и не выходит за рамки
/// битмейкинга, отправок и ответов.
/// </summary>
public static class Coach
{
    private const string Instructions = """
        You are a short, friendly coach for a beatmaker inside a beat-pitching app.
        Turn the facts below into 2-3 sentences of advice for right now.
        Strict rules:
        - Only beatmaking, sending beats to artists, follow-ups and replies. Nothing about life, health, sleep, money or mood.
        - Use only the facts given. Do not invent numbers, genres, beats or reasons.
        - Concrete: name the genre, the beat, the time window from the facts.
        - Write in the language requested. No emoji, no markdown, no greeting.
        """;

    public static async Task<string> AdviseAsync(LlmClient client, IReadOnlyList<string> facts, string language,
        CancellationToken ct = default)
    {
        var user = $"Language: {language}\nFacts:\n- " + string.Join("\n- ", facts);
        var answer = (await client.CompleteAsync(Instructions, user, 0.6, ct)).Trim();

        // Модели иногда оборачивают ответ в кавычки или добавляют markdown — снимаем.
        answer = answer.Trim('"', '«', '»').Replace("**", "");
        return answer.Length > 600 ? answer[..600] : answer;
    }
}
