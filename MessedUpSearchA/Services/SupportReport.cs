using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace MessedUpSearchA.Services;

/// <summary>
/// «Сообщить о проблеме»: письмо в почтовой программе пользователя с версией, системой и
/// хвостом лога. Отправляет он сам — видит всё, что уходит, и может стереть лишнее.
/// </summary>
public static class SupportReport
{
    private const string Address = "vasiliydackevich2054@gmail.com";

    // Длинные mailto режутся: Windows отдаёт почтовой программе около 2 000 символов.
    private const int MaxUriLength = 1900;

    public static string BuildMailto(string version, string intro)
    {
        var subject = $"MessedUpSearch {version}";
        var head = $"{intro}\n\n\n\n---\nMessedUpSearch {version} · {RuntimeInformation.OSDescription} · {RuntimeInformation.OSArchitecture}\n";

        var log = LogTail(40);
        while (true)
        {
            var body = log.Length == 0 ? head : head + "\nlog:\n" + string.Join("\n", log);
            var uri = $"mailto:{Address}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}";
            if (uri.Length <= MaxUriLength || log.Length == 0)
                return uri;

            // Не влезло — отбрасываем самые старые строки лога.
            log = log[1..];
        }
    }

    /// <summary>Последние строки лога; домашняя папка заменена на «~», чтобы не светить имя пользователя.</summary>
    private static string[] LogTail(int lines)
    {
        try
        {
            if (!File.Exists(AppLog.FilePath))
                return [];

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return File.ReadLines(AppLog.FilePath, Encoding.UTF8)
                .TakeLast(lines)
                .Select(l => string.IsNullOrEmpty(home) ? l : l.Replace(home, "~"))
                .ToArray();
        }
        catch
        {
            return [];
        }
    }
}
