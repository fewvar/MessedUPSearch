using System;
using System.IO;
using System.Text.Json;

namespace MessedUpSearchA.Data;

/// <summary>
/// Пользовательские настройки приложения. Хранятся в settings.json рядом с базой
/// (та же папка данных пользователя). Грузятся/сохраняются вручную — без EF.
/// </summary>
public class AppSettings
{
    /// <summary>Через сколько дней после привязки бита напоминать о неотправленном (1–14).</summary>
    public int ReminderDays { get; set; } = 3;

    private static string FilePath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MessedUpSearch");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "settings.json");
        }
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch
        {
            // битый файл — откатываемся на дефолты, не роняем приложение
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
            // не критично, если запись не удалась
        }
    }
}
