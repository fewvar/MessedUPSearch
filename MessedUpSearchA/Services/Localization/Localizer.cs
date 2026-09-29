using System;
using System.Collections.Generic;

namespace MessedUpSearchA.Services.Localization;

public enum AppLanguage
{
    English,
    Russian
}

/// <summary>
/// Строки интерфейса на двух языках.
///
/// Сам по себе он только хранит текущий язык и отдаёт строки по ключу. За
/// обновление интерфейса отвечает <see cref="LocalizedString"/>: привязки идут
/// к нему, а не сюда. Попытка обойтись уведомлением «изменился Item[]» прямо
/// отсюда не сработала — Avalonia не перечитывает по нему живые привязки.
///
/// В разметке используется как {loc:Tr Beats.Add}.
/// </summary>
public class Localizer
{
    public static Localizer Instance { get; } = new();

    private AppLanguage _language = AppLanguage.English;

    private Localizer() { }

    /// <summary>Срабатывает после смены языка: на него подписаны LocalizedString и вьюмодели.</summary>
    public event Action? LanguageChanged;

    public AppLanguage Language
    {
        get => _language;
        set
        {
            if (_language == value)
                return;

            _language = value;
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

    /// <summary>Есть ли строка под этим ключом — в текущем языке или в английском.</summary>
    public bool TryGet(string key, out string value)
    {
        var table = _language == AppLanguage.Russian ? Ru : En;
        if (table.TryGetValue(key, out value!) || En.TryGetValue(key, out value!))
            return true;

        value = string.Empty;
        return false;
    }

    /// <summary>Культура для чисел и дат: «191 716» по-русски, «191,716» по-английски.</summary>
    public System.Globalization.CultureInfo Culture =>
        System.Globalization.CultureInfo.GetCultureInfo(_language == AppLanguage.Russian ? "ru-RU" : "en-US");

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
