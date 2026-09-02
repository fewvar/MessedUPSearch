using System.Collections.Generic;
using System.ComponentModel;

namespace MessedUpSearchA.Services.Localization;

/// <summary>
/// Одна строка интерфейса как отдельный объект с обычным свойством <see cref="Value"/>.
///
/// Зачем так, а не привязка к индексатору Localizer напрямую: уведомление вида
/// «изменился Item[]» Avalonia на существующих привязках не подхватывает — текст
/// обновлялся только когда контрол пересоздавался (например, при переключении
/// вкладок). Обычное свойство обычного объекта обновляется гарантированно.
///
/// Объекты кэшируются по ключу, поэтому один и тот же ключ на разных экранах —
/// это один экземпляр, а не десять.
/// </summary>
public sealed class LocalizedString : INotifyPropertyChanged
{
    private static readonly Dictionary<string, LocalizedString> Cache = new();
    private static readonly object Gate = new();

    private readonly string _key;

    static LocalizedString()
    {
        Localizer.Instance.LanguageChanged += RefreshAll;
    }

    private LocalizedString(string key) => _key = key;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Value => Localizer.Instance[_key];

    public static LocalizedString For(string key)
    {
        lock (Gate)
        {
            if (!Cache.TryGetValue(key, out var existing))
                Cache[key] = existing = new LocalizedString(key);

            return existing;
        }
    }

    private static void RefreshAll()
    {
        LocalizedString[] all;

        lock (Gate)
        {
            all = new LocalizedString[Cache.Count];
            Cache.Values.CopyTo(all, 0);
        }

        foreach (var item in all)
            item.PropertyChanged?.Invoke(item, new PropertyChangedEventArgs(nameof(Value)));
    }
}
