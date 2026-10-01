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

    /// <summary>Шаблон последней рассылки — открывается первым в следующий раз.</summary>
    public int LastPitchTemplateId { get; set; }

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
