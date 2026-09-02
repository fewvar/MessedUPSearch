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
        return new Binding($"[{Key}]")
        {
            Mode = BindingMode.OneWay,
            Source = Localizer.Instance
        };
    }
}
