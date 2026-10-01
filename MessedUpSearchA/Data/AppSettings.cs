using System;
using System.IO;
using System.Text.Json;

namespace MessedUpSearchA.Data;

public class AppSettings
{

    public int ReminderDays { get; set; } = 3;

    public string MediaFolder { get; set; } = string.Empty;

    public string LastFmApiKey { get; set; } = string.Empty;

    public string JamendoClientId { get; set; } = string.Empty;

    public string GeniusAccessToken { get; set; } = string.Empty;

    /// <summary>"English" или "Russian". Пустое значение — английский.</summary>
    public string Language { get; set; } = "English";

    /// <summary>Писать ли прогоны парсера в app.log рядом с базой.</summary>
    public bool EnableParserLogs { get; set; } = true;

    /// <summary>Анимации интерфейса. Если в системе включено «Уменьшить движение», они упрощаются всё равно.</summary>
    public bool Animations { get; set; } = true;

    /// <summary>Тихие звуки на 4 события: анализ готов, отправлен, ★, парсер закончил.</summary>
    public bool Sounds { get; set; } = true;

    /// <summary>Громкость плеера, 0..1.</summary>
    public double PlayerVolume { get; set; } = 0.8;

    // Почта для рассылки. Пароль — в системном хранилище (SecretStore), не здесь.
    public string MailAddress { get; set; } = string.Empty;
    public string MailSenderName { get; set; } = string.Empty;
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 465;
    public string ImapHost { get; set; } = string.Empty;
    public int ImapPort { get; set; } = 993;

    /// <summary>Сколько писем в сутки, не больше. Почтовые сервисы режут массовые рассылки с личных ящиков.</summary>
    public int MailDailyLimit { get; set; } = 50;

    // Нейросеть для «Оживить»: свой ключ, OpenAI-совместимый провайдер. Ключ — в связке ключей.
    public string LlmProvider { get; set; } = "Groq";
    public string LlmBaseUrl { get; set; } = string.Empty;
    public string LlmModel { get; set; } = string.Empty;

    /// <summary>Нет ответа столько дней — предложить фоллоу-ап.</summary>
    public int FollowUpDays { get; set; } = 7;

    // Докуда дочитаны входящие: следующая проверка берёт только новые письма.
    // UidValidity сменился (ящик пересоздан) — читаем заново с даты первой рассылки.
    public uint ImapLastUid { get; set; }
    public uint ImapUidValidity { get; set; }

    // «Сегодня».
    /// <summary>Когда «Сегодня» открывали последний раз — от этого момента считаются «новые ответы».</summary>
    public string TodaySeenAt { get; set; } = string.Empty;
    /// <summary>Показывать «Сегодня» при первом запуске за день.</summary>
    public bool ShowTodayOnStart { get; set; } = true;
    public string TodayShownDate { get; set; } = string.Empty;
    /// <summary>Совет нейросети кэшируется на день: не тратить запросы на каждое открытие.</summary>
    public string CoachDate { get; set; } = string.Empty;
    public string CoachText { get; set; } = string.Empty;
    /// <summary>Тост «сейчас твоё время» — не чаще раза в день.</summary>
    public bool YourTimeToast { get; set; } = true;
    public string YourTimeToastDate { get; set; } = string.Empty;

    // Сессии в DAW и фоновый режим — только с согласия (страница при первом запуске 1.1).
    public bool DawConsentAsked { get; set; }
    public bool DawTracking { get; set; }

    /// <summary>Шаблон последней рассылки — открывается первым в следующий раз.</summary>
    public int LastPitchTemplateId { get; set; }
    public int LastFollowUpTemplateId { get; set; }

    public static string DefaultMediaFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Downloads", "MessedUpSearch");

    public string ResolveMediaFolder() =>
        string.IsNullOrWhiteSpace(MediaFolder) ? DefaultMediaFolder : MediaFolder;

    private static string FilePath => AppPaths.SettingsFile;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch
        {

        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {

        }
    }
}
