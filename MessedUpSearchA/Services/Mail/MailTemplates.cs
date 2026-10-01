using System;
using System.Collections.Generic;
using System.Linq;
using MessedUpSearchA.Data;
using MessedUpSearchA.Models;

namespace MessedUpSearchA.Services.Mail;

/// <summary>Готовые шаблоны и подстановка значений.</summary>
public static class MailTemplates
{
    public static readonly string[] Placeholders = ["{artist}", "{beat}", "{bpm}", "{key}", "{link}", "{my_name}"];

    /// <summary>Первый запуск рассылки — кладём по шаблону на вид и язык, дальше их правит сам битмейкер.</summary>
    public static void EnsureSeeded(AppDbContext db)
    {
        if (db.MailTemplates.Any())
            return;

        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        db.MailTemplates.AddRange(
            new MailTemplate
            {
                Name = "Питч (RU)", Kind = MailKinds.Pitch, CreatedAt = now,
                Subject = "бит для тебя — {beat}",
                Body = "Привет, {artist}!\n\nПослушал твои треки и сделал бит, который, по-моему, ляжет под твою подачу: " +
                       "{beat} ({bpm} BPM, {key}).\n\nПослушать: {link}\n\nЕсли зайдёт — напиши, обсудим условия. " +
                       "Если не твоё — тоже напиши, подстроюсь под то, что ищешь.\n\n{my_name}"
            },
            new MailTemplate
            {
                Name = "Pitch (EN)", Kind = MailKinds.Pitch, CreatedAt = now,
                Subject = "beat for you — {beat}",
                Body = "Hey {artist}!\n\nI've been listening to your tracks and made a beat I think fits your sound: " +
                       "{beat} ({bpm} BPM, {key}).\n\nListen: {link}\n\nIf you like it, let me know and we'll talk terms. " +
                       "If it's not your thing, tell me what you're looking for.\n\n{my_name}"
            },
            new MailTemplate
            {
                Name = "Фоллоу-ап (RU)", Kind = MailKinds.FollowUp, CreatedAt = now,
                Subject = "Re: бит для тебя — {beat}",
                Body = "Привет, {artist}! Напоминаю про {beat} — вдруг письмо потерялось: {link}\n\n" +
                       "Если не твоё — скажи, пришлю что-нибудь другое.\n\n{my_name}"
            },
            new MailTemplate
            {
                Name = "Follow-up (EN)", Kind = MailKinds.FollowUp, CreatedAt = now,
                Subject = "Re: beat for you — {beat}",
                Body = "Hey {artist}, just following up on {beat} in case it got buried: {link}\n\n" +
                       "Not your vibe? Tell me and I'll send something else.\n\n{my_name}"
            });
        db.SaveChanges();
    }

    public static string Render(string text, Artist artist, Beat beat, string myName)
    {
        var values = new Dictionary<string, string>
        {
            ["{artist}"] = artist.Nickname,
            ["{beat}"] = beat.BeatName,
            ["{bpm}"] = beat.Bpm > 0 ? beat.Bpm.ToString() : string.Empty,
            ["{key}"] = beat.Key,
            ["{link}"] = beat.ShareUrl,
            ["{my_name}"] = myName
        };

        foreach (var (placeholder, value) in values)
            text = text.Replace(placeholder, value, StringComparison.OrdinalIgnoreCase);

        // Без BPM и тональности «(, )» выглядит как баг — чистим пустые скобки и висячие запятые.
        text = text.Replace(" ( BPM, )", "").Replace("( BPM, )", "")
                   .Replace("( BPM, ", "(").Replace(", )", ")").Replace(" ()", "").Replace("()", "");
        return text;
    }
}
