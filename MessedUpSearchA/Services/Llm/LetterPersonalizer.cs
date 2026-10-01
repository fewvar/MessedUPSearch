using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MessedUpSearchA.Services.Llm;

/// <summary>Что известно об артисте — ровно то, что можно упомянуть в письме.</summary>
public record ArtistFacts(string Nickname, string Genre, string TrackTitle);

/// <summary>
/// «Оживить»: переписать готовое письмо под артиста. Нейросеть получает только текст письма,
/// ник, жанр и название одного трека — ни почт, ни базы. Ответ проверяется: ссылка должна
/// остаться той же, длина — примерно той же; не прошло — письмо остаётся как было.
/// </summary>
public static class LetterPersonalizer
{
    private const string Instructions = """
        You help a beatmaker rewrite a short pitch email to a rapper so it reads personal, not mass-sent.
        Rules:
        - Keep the language of the original email (Russian stays Russian, English stays English).
        - Keep the meaning, the signature and every URL exactly as they are.
        - You may mention the artist's nickname, genre and the ONE track title given below. Nothing else about them.
        - Never invent facts: no made-up stats, opinions about other tracks, collabs, prices, deadlines or promises
          that are not in the original email.
        - Keep roughly the same length (±30%). No emoji unless the original has them. No markdown.
        - Vary wording and sentence structure so two emails to different artists don't look identical.
        Reply with JSON only: {"subject": "...", "body": "..."}
        """;

    /// <returns>Новые тема и текст; null — ответ не прошёл проверку (тогда письмо не трогаем).</returns>
    public static async Task<(string Subject, string Body)?> RewriteAsync(LlmClient client, ArtistFacts artist,
        string subject, string body, string link, CancellationToken ct = default)
    {
        var user = $"""
            Artist nickname: {artist.Nickname}
            Artist genre: {(artist.Genre.Length > 0 ? artist.Genre : "unknown")}
            One of their tracks: {(artist.TrackTitle.Length > 0 ? artist.TrackTitle : "unknown — don't mention tracks")}

            Original subject: {subject}
            Original email:
            {body}
            """;

        var answer = await client.CompleteAsync(Instructions, user, 0.9, ct);
        return Parse(answer, body, link);
    }

    public static (string Subject, string Body)? Parse(string answer, string originalBody, string link)
    {
        var start = answer.IndexOf('{');
        var end = answer.LastIndexOf('}');
        if (start < 0 || end <= start)
            return null;

        try
        {
            using var doc = JsonDocument.Parse(answer[start..(end + 1)]);
            var subject = doc.RootElement.GetProperty("subject").GetString()?.Trim() ?? string.Empty;
            var body = doc.RootElement.GetProperty("body").GetString()?.Trim() ?? string.Empty;

            if (subject.Length == 0 || body.Length == 0)
                return null;
            if (link.Length > 0 && originalBody.Contains(link) && !body.Contains(link))
                return null;
            if (body.Length < originalBody.Length * 0.5 || body.Length > originalBody.Length * 1.8)
                return null;

            return (subject, body);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return null;
        }
    }
}
