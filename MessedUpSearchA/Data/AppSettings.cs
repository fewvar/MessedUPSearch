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

    /// <summary>Плёночное зерно фона и световое пятно за курсором.</summary>
    public bool BackgroundEffects { get; set; } = true;

    /// <summary>Тихие звуки на 4 события: анализ готов, отправлен, ★, парсер закончил.</summary>
    public bool Sounds { get; set; } = true;

    /// <summary>Громкость плеера, 0..1.</summary>
    public double PlayerVolume { get; set; } = 0.8;

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
