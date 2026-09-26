using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace MessedUpSearchA.Converters;

/// <summary>
/// true, когда все значения равны. Нужен строке таблицы, чтобы понять «это я играю?»:
/// id строки сравнивается с id бита в плеере.
/// </summary>
public class EqualsMultiConverter : IMultiValueConverter
{
    public static readonly EqualsMultiConverter Instance = new();

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2)
            return false;

        for (var i = 1; i < values.Count; i++)
        {
            if (!Equals(values[0], values[i]))
                return false;
        }

        return true;
    }
}

/// <summary>
/// [id строки, id бита в плеере, играет ли плеер] -> «строка сейчас звучит».
/// С параметром "invert" — наоборот: для значка ▶, который виден во всех остальных случаях.
/// </summary>
public class RowPlayingConverter : IMultiValueConverter
{
    public static readonly RowPlayingConverter Instance = new();

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var sounding = values.Count >= 3 &&
                       Equals(values[0], values[1]) &&
                       values[2] is true;

        return parameter as string == "invert" ? !sounding : sounding;
    }
}
