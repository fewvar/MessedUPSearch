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
