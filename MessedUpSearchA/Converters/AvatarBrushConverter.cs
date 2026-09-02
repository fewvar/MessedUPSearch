using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace MessedUpSearchA.Converters;

/// <summary>
/// Путь к скачанной аватарке -> кисть для заливки кружка в таблице.
///
/// Возвращает null, если файла нет: тогда сквозь прозрачную заливку виден
/// цветной кружок под ней. Так артисты, добавленные руками, и те, у кого файл
/// пропал, выглядят как раньше, а не дырой в таблице.
///
/// Картинки декодируются до 96 пикселей и кэшируются: в таблице сотни строк,
/// и держать полноразмерные фото в памяти незачем.
/// </summary>
public class AvatarBrushConverter : IValueConverter
{
    private const int DecodeWidth = 96;

    private static readonly Dictionary<string, ImageBrush?> Cache = new();
    private static readonly object Gate = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path))
            return null;

        lock (Gate)
        {
            if (Cache.TryGetValue(path, out var cached))
                return cached;

            var brush = Load(path);
            Cache[path] = brush;
            return brush;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static ImageBrush? Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            using var stream = File.OpenRead(path);
            var bitmap = Bitmap.DecodeToWidth(stream, DecodeWidth);

            return new ImageBrush(bitmap)
            {
                // Кружок квадратный, а фото — какое угодно: заполняем и обрезаем
                // лишнее, иначе портреты сплющит.
                Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center
            };
        }
        catch
        {
            // Битый или недокачанный файл не должен ронять отрисовку таблицы.
            return null;
        }
    }

    /// <summary>Сбросить кэш — после того как парсер перекачал аватарки.</summary>
    public static void Invalidate()
    {
        lock (Gate)
            Cache.Clear();
    }
}
