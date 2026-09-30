using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace MessedUpSearchA.Controls;

/// <summary>
/// Волна бита квадратными столбиками: высота квантуется по сетке в 2 px, без
/// сглаживания — пиксельная, а не «как в SoundCloud». Сыгранное рисуется ярко,
/// впереди — тускло. Клик или протяжка мышью — перемотка.
///
/// Столбики стоят на «мокром полу»: линия пола на 2/3 высоты, под ней отражение —
/// пиксельные клетки через строку (рябь), гаснущие к низу. В точке воспроизведения на
/// верхушке столбика горит яркий пиксель с мягким свечением — он тоже отражается.
/// </summary>
public class PixelWaveform : Control
{
    private const double Cell = 2;

    /// <summary>Ширина, которую реально заняли столбики, — по ней и считается клик.</summary>
    private double _contentWidth;

    public static readonly StyledProperty<float[]?> PeaksProperty =
        AvaloniaProperty.Register<PixelWaveform, float[]?>(nameof(Peaks));

    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<PixelWaveform, double>(nameof(Progress));

    public static readonly StyledProperty<IBrush?> PlayedBrushProperty =
        AvaloniaProperty.Register<PixelWaveform, IBrush?>(nameof(PlayedBrush), Brushes.White);

    public static readonly StyledProperty<IBrush?> RestBrushProperty =
        AvaloniaProperty.Register<PixelWaveform, IBrush?>(nameof(RestBrush), Brushes.Gray);

    /// <summary>Длительность трека в секундах: при наведении показываем время под курсором.</summary>
    public static readonly StyledProperty<double> DurationProperty =
        AvaloniaProperty.Register<PixelWaveform, double>(nameof(Duration));

    /// <summary>Выполняется с долей 0..1, куда кликнули.</summary>
    public static readonly StyledProperty<ICommand?> SeekCommandProperty =
        AvaloniaProperty.Register<PixelWaveform, ICommand?>(nameof(SeekCommand));

    static PixelWaveform()
    {
        AffectsRender<PixelWaveform>(PeaksProperty, ProgressProperty, PlayedBrushProperty, RestBrushProperty);
    }

    public PixelWaveform()
    {
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public float[]? Peaks
    {
        get => GetValue(PeaksProperty);
        set => SetValue(PeaksProperty, value);
    }

    public double Progress
    {
        get => GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public IBrush? PlayedBrush
    {
        get => GetValue(PlayedBrushProperty);
        set => SetValue(PlayedBrushProperty, value);
    }

    public IBrush? RestBrush
    {
        get => GetValue(RestBrushProperty);
        set => SetValue(RestBrushProperty, value);
    }

    public double Duration
    {
        get => GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    /// <summary>X курсора над волной или null — не наведено.</summary>
    private double? _hoverX;

    public ICommand? SeekCommand
    {
        get => GetValue(SeekCommandProperty);
        set => SetValue(SeekCommandProperty, value);
    }

    /// <summary>Где стоит «пол»: доля высоты сверху. Выше — столбики, ниже — отражение.</summary>
    private const double FloorRatio = 0.66;

    /// <summary>Насколько яркое отражение у самого пола; к низу гаснет до нуля.</summary>
    private const double ReflectionOpacity = 0.32;

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0 || height <= 0)
            return;

        var peaks = Peaks;
        var count = peaks?.Length ?? 0;

        // Пока волна считается — ровная полоса из минимальных столбиков.
        var bars = count > 0 ? count : (int)(width / (Cell * 2));

        // Шаг — целое число пикселей, иначе столбики поплывут и сольются.
        var step = Math.Max(Cell * 2, Math.Floor(width / bars));
        var barWidth = Math.Max(Cell, step - Cell);
        var drawn = Math.Min(bars, (int)(width / step));

        _contentWidth = drawn * step;
        var floor = Math.Floor(height * FloorRatio / Cell) * Cell;
        var depth = height - floor;
        var progressX = Progress * drawn * step;

        var played = ColorOf(PlayedBrush, Colors.White);
        var rest = ColorOf(RestBrush, Colors.Gray);
        var playheadBar = -1;
        var playheadTop = floor;

        for (var i = 0; i < drawn; i++)
        {
            var peak = count > 0 ? peaks![(int)((long)i * count / drawn)] : 0f;

            // Высота столбика кратна клетке: от 1 клетки до пола.
            var up = Math.Max(Cell, Math.Round(peak * floor / Cell) * Cell);
            var x = i * step;
            var isPlayed = x < progressX;
            var color = isPlayed ? played : rest;

            context.FillRectangle(new ImmutableSolidColorBrush(color), new Rect(x, floor - up, barWidth, up));

            // Отражение: та же высота, сжатая в глубину пола; клетки через строку, гаснут к низу.
            var down = Math.Min(depth, Math.Round(up * depth / floor / Cell) * Cell);
            for (var y = 0.0; y < down; y += Cell * 2)
            {
                var fade = ReflectionOpacity * (1 - y / depth);
                context.FillRectangle(new ImmutableSolidColorBrush(color, fade),
                    new Rect(x, floor + Cell + y, barWidth, Cell));
            }

            if (isPlayed && x + step >= progressX)
            {
                playheadBar = i;
                playheadTop = floor - up;
            }
        }

        // Точка воспроизведения: яркий пиксель на верхушке текущего столбика + мягкое свечение.
        if (Progress > 0 && playheadBar >= 0)
        {
            var x = playheadBar * step;
            var center = new Point(x + barWidth / 2, playheadTop + Cell / 2);
            var glow = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(110, played.R, played.G, played.B), 0),
                    new GradientStop(Color.FromArgb(0, played.R, played.G, played.B), 1)
                }
            };
            context.DrawEllipse(glow, null, center, 7, 7);
            context.FillRectangle(Brushes.White, new Rect(x, playheadTop - Cell, barWidth, Cell * 2));
            context.FillRectangle(new ImmutableSolidColorBrush(Colors.White, 0.35),
                new Rect(x, floor + Cell, barWidth, Cell));
        }

        RenderHover(context, floor);
    }

    /// <summary>
    /// Наведение: пунктирная пиксельная линия там, куда перемотаешь, и время рядом — в пустом месте над
    /// столбиками, с тёмной подложкой, чтобы читалось поверх волны.
    /// </summary>
    private void RenderHover(DrawingContext context, double floor)
    {
        if (_hoverX is not { } hx || _contentWidth <= 0)
            return;

        var x = Math.Floor(Math.Clamp(hx, 0, _contentWidth - 1) / Cell) * Cell;
        var line = new ImmutableSolidColorBrush(Colors.White, 0.55);
        for (var y = 0.0; y < floor; y += Cell * 2)
            context.FillRectangle(line, new Rect(x, y, Cell / 2, Cell));

        if (Duration <= 0)
            return;

        var seconds = Duration * Math.Clamp(hx / _contentWidth, 0, 1);
        var label = TimeSpan.FromSeconds(seconds).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");
        var text = new FormattedText(label, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("JetBrains Mono"), 10, Brushes.White);
        var boxX = x + 6 + text.Width + 8 > Bounds.Width ? x - text.Width - 12 : x + 6;
        var box = new Rect(boxX, 0, text.Width + 6, text.Height + 2);
        context.FillRectangle(new ImmutableSolidColorBrush(Color.FromArgb(220, 16, 16, 16)), box);
        context.DrawText(text, new Point(box.X + 3, box.Y + 1));
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _hoverX = e.GetPosition(this).X;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hoverX = null;
        InvalidateVisual();
    }

    private static Color ColorOf(IBrush? brush, Color fallback) =>
        brush is ISolidColorBrush solid ? Color.FromArgb((byte)(solid.Color.A * solid.Opacity), solid.Color.R, solid.Color.G, solid.Color.B) : fallback;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        e.Pointer.Capture(this);
        Seek(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _hoverX = e.GetPosition(this).X;
        InvalidateVisual();
        if (e.Pointer.Captured == this)
            Seek(e.GetPosition(this).X);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
    }

    private void Seek(double x)
    {
        var fraction = Math.Clamp(x / Math.Max(1, _contentWidth), 0, 1);
        if (SeekCommand?.CanExecute(fraction) == true)
            SeekCommand.Execute(fraction);
    }
}
