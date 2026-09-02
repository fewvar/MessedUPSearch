using System;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace MessedUpSearchA.Services.Localization;

/// <summary>
/// Разметочное расширение: {loc:Tr Beats.Add} вместо длинной привязки к индексатору.
/// Возвращает именно Binding, а не готовую строку, — иначе текст замерз бы на
/// том языке, который стоял при загрузке окна.
/// </summary>
public class TrExtension : MarkupExtension
{
    public TrExtension() { }

    public TrExtension(string key) => Key = key;

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        // Привязываемся к Value отдельного объекта, а не к индексатору Localizer:
        // уведомление «изменился Item[]» Avalonia на живых привязках игнорирует,
        // и текст обновлялся только при пересоздании контрола.
        return new Binding(nameof(LocalizedString.Value))
        {
            Mode = BindingMode.OneWay,
            Source = LocalizedString.For(Key)
        };
    }
}
