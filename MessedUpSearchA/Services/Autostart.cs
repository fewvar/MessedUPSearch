using System;
using System.IO;
using System.Security;

namespace MessedUpSearchA.Services;

/// <summary>
/// Автозапуск с системой — часть фонового режима (включается вместе с согласием на сессии в DAW).
/// Приложение стартует свёрнутым в трей: проверяет ответы, досылает рассылку, пишет сессии.
/// macOS — LaunchAgent в ~/Library/LaunchAgents, Windows — ключ Run текущего пользователя.
/// </summary>
public static class Autostart
{
    public const string BackgroundArg = "--background";
    private const string Label = "com.fewvar.messedupsearch";

    private static string MacPlist => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", Label + ".plist");

    /// <returns>Пусто — получилось; иначе причина для пользователя (ключ локализации).</returns>
    public static string Enable()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
            return "Background.NoPath";

        try
        {
            if (OperatingSystem.IsMacOS())
            {
                // Неподписанное приложение, запущенное прямо из «Загрузок», macOS запускает из
                // временной копии (App Translocation) — путь к ней после перезагрузки не живёт.
                if (exe.Contains("/AppTranslocation/"))
                    return "Background.MoveToApplications";

                Directory.CreateDirectory(Path.GetDirectoryName(MacPlist)!);
                File.WriteAllText(MacPlist, $"""
                    <?xml version="1.0" encoding="UTF-8"?>
                    <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                    <plist version="1.0">
                    <dict>
                        <key>Label</key><string>{Label}</string>
                        <key>ProgramArguments</key>
                        <array>
                            <string>{SecurityElement.Escape(exe)}</string>
                            <string>{BackgroundArg}</string>
                        </array>
                        <key>RunAtLoad</key><true/>
                    </dict>
                    </plist>
                    """);
                return string.Empty;
            }

            if (OperatingSystem.IsWindows())
            {
                using var run = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                run.SetValue("MessedUpSearch", $"\"{exe}\" {BackgroundArg}");
                return string.Empty;
            }
        }
        catch (Exception ex)
        {
            AppLog.Write($"автозапуск: {ex.Message}");
            return "Background.Failed";
        }

        return "Background.Unsupported";
    }

    public static void Disable()
    {
        try
        {
            if (OperatingSystem.IsMacOS() && File.Exists(MacPlist))
                File.Delete(MacPlist);
            else if (OperatingSystem.IsWindows())
            {
                using var run = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                run?.DeleteValue("MessedUpSearch", false);
            }
        }
        catch (Exception ex)
        {
            AppLog.Write($"автозапуск, выключение: {ex.Message}");
        }
    }

    public static bool IsEnabled()
    {
        if (OperatingSystem.IsMacOS())
            return File.Exists(MacPlist);
        if (OperatingSystem.IsWindows())
        {
            using var run = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            return run?.GetValue("MessedUpSearch") is not null;
        }
        return false;
    }
}
