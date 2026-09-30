using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;

namespace MessedUpSearchA.ViewModels;

/// <summary>
/// Сортировка таблицы по клику на заголовок: ▼ (от большего к меньшему) -> ▲ -> сброс к исходному порядку.
/// Состояние — строка «Столбец:desc» / «Столбец:asc» / пусто: её удобно отдать в XAML конвертеру стрелки.
/// Пустые значения (BPM 0, нет тональности) при любом направлении уходят в конец — иначе они забивают верх.
/// </summary>
public static class TableSort
{
    public static string Next(string state, string column)
    {
        var (current, descending) = Parse(state);
        if (current != column)
            return $"{column}:desc";
        return descending ? $"{column}:asc" : string.Empty;
    }

    public static (string Column, bool Descending) Parse(string state)
    {
        var parts = state.Split(':');
        return parts.Length == 2 ? (parts[0], parts[1] == "desc") : (string.Empty, false);
    }

    /// <summary>key(столбец) -> извлечение значения (число, строка или null = пусто) или null, если столбец не сортируется.</summary>
    public static IEnumerable<T> Apply<T>(IEnumerable<T> items, string state, Func<string, Func<T, object?>?> key)
    {
        var (column, descending) = Parse(state);
        if (column.Length == 0 || key(column) is not { } selector)
            return items;

        var list = items.ToList();
        var filled = list.Where(i => !IsEmpty(selector(i)));
        var empty = list.Where(i => IsEmpty(selector(i)));
        var sorted = descending
            ? filled.OrderByDescending(selector, ValueComparer.Instance)
            : filled.OrderBy(selector, ValueComparer.Instance);
        return sorted.Concat(empty);
    }

    private static bool IsEmpty(object? value) => value switch
    {
        null => true,
        string s => string.IsNullOrWhiteSpace(s) || s == "—",
        int n => n == 0,
        long n => n == 0,
        _ => false
    };

    private sealed class ValueComparer : IComparer<object?>
    {
        public static readonly ValueComparer Instance = new();

        public int Compare(object? a, object? b) => (a, b) switch
        {
            (string x, string y) => string.Compare(x, y, StringComparison.CurrentCultureIgnoreCase),
            (IComparable x, _) when b is not null && x.GetType() == b.GetType() => x.CompareTo(b),
            _ => 0
        };
    }
}

/// <summary>Стрелка у заголовка: ConverterParameter — имя столбца, значение — состояние сортировки.</summary>
public sealed class SortArrowConverter : IValueConverter
{
    public static readonly SortArrowConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var (column, descending) = TableSort.Parse(value as string ?? string.Empty);
        return column.Length > 0 && column == parameter as string ? (descending ? "▼" : "▲") : string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
