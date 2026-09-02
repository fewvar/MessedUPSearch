using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace MessedUpSearchA.Services.Localization;

public enum AppLanguage
{
    English,
    Russian
}

/// <summary>
/// Строки интерфейса на двух языках.
///
/// Доступ идёт через индексатор, а не через сотню свойств: при смене языка
/// достаточно сказать «изменился Item[]», и Avalonia перечитает разом все
/// привязки на экране. Поэтому переключение видно сразу, без перезапуска окна.
///
/// В разметке используется как {loc:Tr Beats.Add}.
/// </summary>
public class Localizer : INotifyPropertyChanged
{
    public static Localizer Instance { get; } = new();

    private AppLanguage _language = AppLanguage.English;

    private Localizer() { }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Срабатывает после смены языка — для кода, который держит строки у себя.</summary>
    public event Action? LanguageChanged;

    public AppLanguage Language
    {
        get => _language;
        set
        {
            if (_language == value)
                return;

            _language = value;

            // "Item[]" — соглашение об «изменились все элементы индексатора».
            // Пустая строка следом — универсальное «изменилось вообще всё»: если
            // движок привязок не понимает первое, он точно поймёт второе.
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
            LanguageChanged?.Invoke();
        }
    }

    /// <summary>Неизвестный ключ возвращается как есть — так опечатка видна на экране, а не молчит.</summary>
    public string this[string key]
    {
        get
        {
            var table = _language == AppLanguage.Russian ? Ru : En;
            if (table.TryGetValue(key, out var value))
                return value;

            return En.TryGetValue(key, out var fallback) ? fallback : key;
        }
    }

    public string Get(string key) => this[key];

    /// <summary>Строка с подстановкой: Format("Parser.Found", 12).</summary>
    public string Format(string key, params object[] args)
    {
        try
        {
            return string.Format(this[key], args);
        }
        catch (FormatException)
        {
            return this[key];
        }
    }

    private static readonly Dictionary<string, string> En = LocalizationStrings.English;
    private static readonly Dictionary<string, string> Ru = LocalizationStrings.Russian;
}
