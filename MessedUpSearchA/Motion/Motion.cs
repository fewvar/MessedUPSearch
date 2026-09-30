using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
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

    /// <summary>
    /// Строка списка появляется по очереди: прозрачность + сдвиг 6 px снизу, задержка = номер строки × 25 мс.
    /// Ставится на корень шаблона элемента ItemsControl.
    /// </summary>
    public static readonly AttachedProperty<bool> AppearProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Appear", typeof(Motion));

    public static bool GetAppear(Control control) => control.GetValue(AppearProperty);
    public static void SetAppear(Control control, bool value) => control.SetValue(AppearProperty, value);

    /// <summary>
    /// Полоска «дорастает» от левого края (масштаб по X, без пересчёта раскладки), с задержкой строки.
    /// </summary>
    public static readonly AttachedProperty<bool> GrowXProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("GrowX", typeof(Motion));

    public static bool GetGrowX(Control control) => control.GetValue(GrowXProperty);
    public static void SetGrowX(Control control, bool value) => control.SetValue(GrowXProperty, value);

    /// <summary>Число «досчитывает» от 0 до значения вместе с полоской строки (TextBlock, формат «N%»).</summary>
    public static readonly AttachedProperty<int> CountToProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, int>("CountTo", typeof(Motion), -1);

    public static int GetCountTo(TextBlock control) => control.GetValue(CountToProperty);
    public static void SetCountTo(TextBlock control, int value) => control.SetValue(CountToProperty, value);

    /// <summary>Появился (IsVisible стал true) — щелчок <see cref="Pop"/>. Для ✓ после добавления в базу.</summary>
    public static readonly AttachedProperty<bool> PopWhenShownProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("PopWhenShown", typeof(Motion));

    public static bool GetPopWhenShown(Control control) => control.GetValue(PopWhenShownProperty);
    public static void SetPopWhenShown(Control control, bool value) => control.SetValue(PopWhenShownProperty, value);

    /// <summary>true — один раз вспыхнуть зелёным (точка статуса, только что ставшая SOLD).</summary>
    public static readonly AttachedProperty<bool> FlashProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Flash", typeof(Motion));

    public static bool GetFlash(Control control) => control.GetValue(FlashProperty);
    public static void SetFlash(Control control, bool value) => control.SetValue(FlashProperty, value);

    private const double RowStepSeconds = 0.025;
    private const double RowSeconds = 0.3;
    private const double BarSeconds = 0.5;

    private static readonly TransformOperations RowHidden = TransformOperations.Parse("translateY(6px)");
    private static readonly TransformOperations RowShown = TransformOperations.Parse("translateY(0px)");
    private static readonly TransformOperations BarEmpty = TransformOperations.Parse("scaleX(0)");
    private static readonly TransformOperations BarFull = TransformOperations.Parse("scaleX(1)");

    static Motion()
    {
        CountToProperty.Changed.AddClassHandler<TextBlock>((text, _) =>
        {
            text.AttachedToVisualTree -= OnCountAttached;
            text.AttachedToVisualTree += OnCountAttached;
            if (text.IsLoaded)
                StartCount(text);
        });
        PopWhenShownProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            if (e.NewValue is true)
                control.PropertyChanged += OnPopVisibility;
            else
                control.PropertyChanged -= OnPopVisibility;
        });
        FlashProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            control.AttachedToVisualTree -= OnFlashAttached;
            if (e.NewValue is not true)
                return;
            if (control.IsLoaded)
                Flash(control);
            else
                control.AttachedToVisualTree += OnFlashAttached;
        });
        AppearProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            control.AttachedToVisualTree -= OnAppearAttached;
            if (e.NewValue is true)
                control.AttachedToVisualTree += OnAppearAttached;
        });
        GrowXProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            control.AttachedToVisualTree -= OnGrowAttached;
            if (e.NewValue is true)
            {
                control.RenderTransformOrigin = new RelativePoint(0, 0.5, RelativeUnit.Relative);
                control.AttachedToVisualTree += OnGrowAttached;
            }
        });

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

        // Окно с карточкой: фон гаснет/проявляется, карточка двигается. Без карточки (панель плеера) —
        // двигается сам элемент.
        var card = FindCard(root) ?? root;
        root.IsHitTestVisible = true;

        if (!root.IsVisible)
        {
            // Стартовая точка — без переходов, иначе элемент «влетит» из прошлого состояния.
            root.Transitions = null;
            card.Transitions = null;
            root.Opacity = 0;
            card.RenderTransform = Reduced ? CardOpen : CardClosed;
            root.IsVisible = true;
        }

        var fade = TimeSpan.FromSeconds(Reduced ? ReducedSeconds : 0.22);
        var (duration, easing) = Spring(OpenSeconds);
        Animate(root, card,
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = fade, Easing = new CubicEaseOut() },
            new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = duration, Easing = easing });
        root.Opacity = 1;
        card.RenderTransform = CardOpen;
    }

    private static void Hide(Control root)
    {
        if (!root.IsVisible)
            return;

        var card = FindCard(root) ?? root;
        var duration = TimeSpan.FromSeconds(Reduced ? ReducedSeconds : CloseSeconds);

        // Закрывающееся окно не должно ловить клики: под ним уже главный экран.
        root.IsHitTestVisible = false;
        Animate(root, card,
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = duration, Easing = new CubicEaseIn() },
            new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = duration, Easing = new CubicEaseIn() });
        root.Opacity = 0;
        if (!Reduced)
            card.RenderTransform = CardLeaving;

        var timer = HideTimers.GetValue(root, _ => new DispatcherTimer());
        timer.Stop();
        timer.Interval = duration + TimeSpan.FromMilliseconds(20);
        timer.Tick -= OnHideTick;
        timer.Tick += OnHideTick;
        timer.Tag = root;
        timer.Start();
    }

    /// <summary>Прозрачность — на корне, движение — на карточке; если это один элемент, оба перехода вместе.</summary>
    private static void Animate(Control root, Control card, DoubleTransition fade, TransformOperationsTransition move)
    {
        if (ReferenceEquals(root, card))
        {
            root.Transitions = [fade, move];
            return;
        }
        root.Transitions = [fade];
        card.Transitions = [move];
    }

    private static void OnHideTick(object? sender, EventArgs e)
    {
        if (sender is not DispatcherTimer { Tag: Control root } timer)
            return;
        timer.Stop();
        if (!GetIsShown(root))
            root.IsVisible = false;
    }

    private static void OnAppearAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not Control row || Reduced)
            return;

        row.Transitions = null;
        row.Opacity = 0;
        row.RenderTransform = RowHidden;

        Delayed(RowIndex(row) * RowStepSeconds, () =>
        {
            var (duration, easing) = Spring(RowSeconds);
            row.Transitions =
            [
                new DoubleTransition { Property = Visual.OpacityProperty, Duration = duration, Easing = easing },
                new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = duration, Easing = easing }
            ];
            row.Opacity = 1;
            row.RenderTransform = RowShown;
        });
    }

    private static void OnGrowAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not Control bar || Reduced)
            return;

        bar.Transitions = null;
        bar.RenderTransform = BarEmpty;

        var row = bar.FindAncestorOfType<ContentPresenter>();
        Delayed((row is null ? 0 : RowIndex(row)) * RowStepSeconds + 0.06, () =>
        {
            var (duration, easing) = Spring(BarSeconds);
            bar.Transitions = [new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = duration, Easing = easing }];
            bar.RenderTransform = BarFull;
        });
    }

    private static void OnCountAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is TextBlock text)
            StartCount(text);
    }

    private static void StartCount(TextBlock text)
    {
        var target = GetCountTo(text);
        if (target < 0)
            return;
        if (Reduced)
        {
            text.Text = $"{target}%";
            return;
        }

        text.Text = "0%";
        var delay = RowIndex(text) * RowStepSeconds + 0.06;          // в такт с полоской строки
        var duration = SpringEasing.DurationFor(BarSeconds).TotalSeconds;
        var easing = new SpringEasing();
        var clock = Stopwatch.StartNew();
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (s, _) =>
        {
            var t = clock.Elapsed.TotalSeconds - delay;
            var progress = Math.Clamp(t / duration, 0, 1);
            text.Text = $"{(int)Math.Round(target * easing.Ease(progress))}%";
            if (progress >= 1)
                ((DispatcherTimer)s!).Stop();
        });
        timer.Start();
    }

    private static void OnPopVisibility(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Visual.IsVisibleProperty && e.NewValue is true && sender is Control control)
            Pop(control);
    }

    private static void OnFlashAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control control)
            Flash(control);
    }

    /// <summary>
    /// Вспышка: элемент подпрыгивает (1.7 -> 1 с отскоком) и светится зелёным, свечение гаснет за 0.9 с.
    /// При «Уменьшить движение» — только свечение, без масштаба.
    /// </summary>
    public static void Flash(Control control)
    {
        var glow = new DropShadowEffect { Color = Color.Parse("#2ECC71"), BlurRadius = 16, OffsetX = 0, OffsetY = 0, Opacity = 1 };
        control.Effect = glow;
        if (!Reduced)
        {
            control.RenderTransformOrigin = RelativePoint.Center;
            control.Transitions = null;
            control.RenderTransform = TransformOperations.Parse("scale(1.7)");
            var (duration, easing) = Spring(0.4, bounce: 0.3);
            control.Transitions = [new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = duration, Easing = easing }];
            control.RenderTransform = TransformOperations.Parse("scale(1)");
        }

        var clock = Stopwatch.StartNew();
        var fade = new DispatcherTimer(TimeSpan.FromMilliseconds(30), DispatcherPriority.Render, (s, _) =>
        {
            var t = clock.Elapsed.TotalSeconds / 0.9;
            glow.Opacity = Math.Max(0, 1 - t);
            if (t < 1)
                return;
            ((DispatcherTimer)s!).Stop();
            control.Effect = null;
        });
        fade.Start();
    }

    /// <summary>
    /// Короткий «щелчок» значка (★, 🚩): сжался и отпружинил назад с лёгким отскоком — единственное
    /// место с bounce, как договорились. При «Уменьшить движение» ничего не делает.
    /// </summary>
    public static void Pop(Control? icon)
    {
        if (icon is null || Reduced)
            return;

        icon.RenderTransformOrigin = RelativePoint.Center;
        icon.Transitions = null;
        icon.RenderTransform = TransformOperations.Parse("scale(0.8)");
        var (duration, easing) = Spring(0.3, bounce: 0.35);
        icon.Transitions = [new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = duration, Easing = easing }];
        icon.RenderTransform = TransformOperations.Parse("scale(1)");
    }

    /// <summary>Номер элемента в ItemsControl — по контейнеру, в котором лежит шаблон.</summary>
    private static int RowIndex(Control control)
    {
        var container = control as ContentPresenter ?? control.FindAncestorOfType<ContentPresenter>();
        var items = container?.FindAncestorOfType<ItemsControl>();
        return container is null || items is null ? 0 : Math.Max(0, items.IndexFromContainer(container));
    }

    private static void Delayed(double seconds, Action action)
    {
        if (seconds <= 0)
            action();
        else
            DispatcherTimer.RunOnce(action, TimeSpan.FromSeconds(seconds));
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
