using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace MessedUpSearchA.Controls;

/// <summary>
/// Волна бита квадратными столбиками: высота квантуется по сетке в 2 px, без
/// сглаживания — пиксельная, а не «как в SoundCloud». Сыгранное рисуется ярко,
/// впереди — тускло. Клик или протяжка мышью — перемотка.
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

    public ICommand? SeekCommand
    {
        get => GetValue(SeekCommandProperty);
        set => SetValue(SeekCommandProperty, value);
    }

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
        var middle = Math.Floor(height / 2);
        var progressX = Progress * drawn * step;

        for (var i = 0; i < drawn; i++)
        {
            var peak = count > 0 ? peaks![(int)((long)i * count / drawn)] : 0f;

            // Высота половины столбика кратна клетке: от 1 клетки до половины контрола.
            var half = Math.Max(Cell, Math.Round(peak * middle / Cell) * Cell);
            var x = i * step;

            var brush = x < progressX ? PlayedBrush : RestBrush;
            context.FillRectangle(brush!, new Rect(x, middle - half, barWidth, half * 2));
        }
    }

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
