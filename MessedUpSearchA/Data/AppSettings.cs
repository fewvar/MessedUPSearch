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
