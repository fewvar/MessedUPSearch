using System;

namespace MessedUpSearchA.Services;

/// <summary>
/// Короткие сообщения в футере: «Отправлено ✓», «Добавлен в базу». Показать можно откуда угодно —
/// футер (MainWindowViewModel) подписан и сам уберёт сообщение через пару секунд.
/// </summary>
public static class Toasts
{
    public static event Action<string>? Shown;

    public static void Show(string text) => Shown?.Invoke(text);
}
