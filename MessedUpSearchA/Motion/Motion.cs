using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media.Transformation;
using Avalonia.Threading;

namespace MessedUpSearchA.Motion;

/// <summary>
/// Анимации приложения по принципам Apple (tmp/plans/animations-v1.0.1.md): пружины без отскока,
/// крупное — дольше, частое — короче, «Уменьшить движение» — только затухание.
///
/// <see cref="IsShownProperty"/> заменяет <c>IsVisible="{Binding IsXOpen}"</c> у окон-оверлеев:
/// при открытии фон проявляется, карточка (<c>Border.modal</c>) чуть увеличивается и поднимается;
/// при закрытии всё быстро гаснет, и только потом элемент прячется. Повторное открытие во время
/// закрытия подхватывает анимацию с текущего места. В XAML рядом ставить <c>IsVisible="False"</c> —
/// стартовое состояние «закрыто».
/// </summary>
public static class Motion
{
    /// <summary>Ощущаемая длительность появления окна (пружина, без отскока).</summary>
    private const double OpenSeconds = 0.35;
    private const double CloseSeconds = 0.15;
    private const double ReducedSeconds = 0.12;

    private static readonly TransformOperations CardClosed = TransformOperations.Parse("translateY(10px) scale(0.97)");
    private static readonly TransformOperations CardLeaving = TransformOperations.Parse("translateY(6px) scale(0.98)");
    private static readonly TransformOperations CardOpen = TransformOperations.Parse("translateY(0px) scale(1)");

    private static readonly ConditionalWeakTable<Control, DispatcherTimer> HideTimers = new();

    /// <summary>В системе включено «Уменьшить движение» (macOS) / выключены анимации (Windows).</summary>
    public static bool SystemReduced { get; private set; }

    /// <summary>Итог: системная настройка или галочка «Анимации» в приложении выключена.</summary>
    public static bool Reduced { get; private set; }

    public static event Action? ReducedChanged;

    public static readonly AttachedProperty<bool> IsShownProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsShown", typeof(Motion));

    public static bool GetIsShown(Control control) => control.GetValue(IsShownProperty);
    public static void SetIsShown(Control control, bool value) => control.SetValue(IsShownProperty, value);

    static Motion()
    {
        IsShownProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            if (e.NewValue is true)
                Show(control);
            else
                Hide(control);
        });
    }

    public static void Initialize(bool animationsEnabled)
    {
        SystemReduced = DetectSystemReducedMotion();
        SetEnabled(animationsEnabled);
    }

    public static void SetEnabled(bool animationsEnabled)
    {
        var reduced = SystemReduced || !animationsEnabled;
        if (reduced == Reduced)
            return;
        Reduced = reduced;
        ReducedChanged?.Invoke();
    }

    /// <summary>Пружина без отскока на ощущаемую длительность seconds (или короткое затухание при Reduced).</summary>
    public static (TimeSpan Duration, Easing Easing) Spring(double seconds, double bounce = 0) =>
        Reduced
            ? (TimeSpan.FromSeconds(ReducedSeconds), new CubicEaseOut())
            : (SpringEasing.DurationFor(seconds), new SpringEasing { Bounce = bounce });

    private static void Show(Control root)
    {
        if (HideTimers.TryGetValue(root, out var pending))
            pending.Stop();

        var card = FindCard(root);
        root.IsHitTestVisible = true;

        if (!root.IsVisible)
        {
            // Стартовая точка — без переходов, иначе элемент «влетит» из прошлого состояния.
            root.Transitions = null;
            root.Opacity = 0;
            if (card is not null)
            {
                card.Transitions = null;
                card.RenderTransform = Reduced ? CardOpen : CardClosed;
            }
            root.IsVisible = true;
        }

        var fade = Reduced ? ReducedSeconds : 0.22;
        root.Transitions = [new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromSeconds(fade), Easing = new CubicEaseOut() }];
        root.Opacity = 1;

        if (card is not null)
        {
            var (duration, easing) = Spring(OpenSeconds);
            card.Transitions = [new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = duration, Easing = easing }];
            card.RenderTransform = CardOpen;
        }
    }

    private static void Hide(Control root)
    {
        if (!root.IsVisible)
            return;

        var card = FindCard(root);
        var seconds = Reduced ? ReducedSeconds : CloseSeconds;
        var duration = TimeSpan.FromSeconds(seconds);

        // Закрывающееся окно не должно ловить клики: под ним уже главный экран.
        root.IsHitTestVisible = false;
        root.Transitions = [new DoubleTransition { Property = Visual.OpacityProperty, Duration = duration, Easing = new CubicEaseIn() }];
        root.Opacity = 0;

        if (card is not null && !Reduced)
        {
            card.Transitions = [new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = duration, Easing = new CubicEaseIn() }];
            card.RenderTransform = CardLeaving;
        }

        var timer = HideTimers.GetValue(root, _ => new DispatcherTimer());
        timer.Stop();
        timer.Interval = duration + TimeSpan.FromMilliseconds(20);
        timer.Tick -= OnHideTick;
        timer.Tick += OnHideTick;
        timer.Tag = root;
        timer.Start();
    }

    private static void OnHideTick(object? sender, EventArgs e)
    {
        if (sender is not DispatcherTimer { Tag: Control root } timer)
            return;
        timer.Stop();
        if (!GetIsShown(root))
            root.IsVisible = false;
    }

    private static Control? FindCard(Control root) =>
        root.GetLogicalDescendants().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("modal"));

    private static bool DetectSystemReducedMotion()
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                // Системные настройки -> Универсальный доступ -> Дисплей -> «Уменьшить движение».
                using var process = Process.Start(new ProcessStartInfo("defaults", "read com.apple.universalaccess reduceMotion")
                {
                    RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
                });
                if (process is null)
                    return false;
                var output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit(1000);
                return output == "1";
            }

            if (OperatingSystem.IsWindows())
            {
                // «Показывать анимацию в Windows» выключено -> уменьшаем движение.
                var enabled = 1;
                return SystemParametersInfo(SpiGetClientAreaAnimation, 0, ref enabled, 0) && enabled == 0;
            }
        }
        catch
        {
            // Не смогли прочитать — считаем, что анимации можно.
        }

        return false;
    }

    private const uint SpiGetClientAreaAnimation = 0x1042;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, ref int value, uint winIni);
}
