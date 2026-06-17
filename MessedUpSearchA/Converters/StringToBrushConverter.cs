using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace MessedUpSearchA.Converters;

public class StringToBrushConverter : IValueConverter
{
    public static readonly StringToBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s && !string.IsNullOrWhiteSpace(s)
            && Color.TryParse(s, out var color))
        {
            return new SolidColorBrush(color);
        }
        return Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
