using System;
using System.Globalization;
using Avalonia.Data.Converters;
using MessedUpSearchA.Services.Localization;

namespace MessedUpSearchA.Converters;

/// <summary>
/// Значения, которые хранятся в базе и фильтрах по-английски («ALL», «All time»,
/// «Common», «Russian»), показываем на языке интерфейса. Сами данные не трогаем —
/// по ним фильтруется и сравнивается. Ключ перевода — «Value.» + значение;
/// перевода нет — показываем как есть (жанры, статусы битов).
/// </summary>
public class DataLabelConverter : IValueConverter
{
    public static readonly DataLabelConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // В узкой колонке таблицы «Не определён» обрезается до «Не опр…» — там прочерк.
        if (parameter as string == "short" && value as string == "Common")
            return "—";

        return value is string text && Localizer.Instance.TryGet("Value." + text, out var label) ? label : value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Число по-человечески: разряды по локали интерфейса, 0 — «—» (значит «не знаем», а не «ноль»).</summary>
public class CountConverter : IValueConverter
{
    public static readonly CountConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            int number when number > 0 => number.ToString("N0", Localizer.Instance.Culture),
            long number when number > 0 => number.ToString("N0", Localizer.Instance.Culture),
            int or long => "—",
            _ => value
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
