using System;
using System.IO;
using MessedUpSearchA.Data;

namespace MessedUpSearchA.Services;

/// <summary>
/// Простой файловый лог рядом с базой. Нужен ровно для одного: понять, почему
/// парсер вернул не то. Площадки меняют свои ответы без предупреждения, и
/// разбираться постфактум без записи прогонов невозможно.
///
/// Пишется, только если включено в настройках. Файл подрезается по размеру,
/// чтобы не разрастись за годы.
/// </summary>
public static class AppLog
{
    private const long MaxBytes = 2 * 1024 * 1024;

    private static readonly object Gate = new();

    /// <summary>Читается из настроек при старте и меняется галочкой в настройках.</summary>
    public static bool Enabled { get; set; }

    public static string FilePath => Path.Combine(AppPaths.DataDir, "app.log");

    public static void Write(string message)
    {
        if (!Enabled)
            return;

        try
        {
            lock (Gate)
            {
                Rotate();
                File.AppendAllText(FilePath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Лог — вспомогательная вещь: если писать не вышло (нет прав, диск
            // полон), приложение из-за этого падать не должно.
        }
    }

    /// <summary>Дорос до предела — оставляем вторую половину, начало отбрасываем.</summary>
    private static void Rotate()
    {
        var file = new FileInfo(FilePath);
        if (!file.Exists || file.Length < MaxBytes)
            return;

        var lines = File.ReadAllLines(FilePath);
        File.WriteAllLines(FilePath, lines[(lines.Length / 2)..]);
    }
}
