using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace MessedUpSearchA.Controls;

/// <summary>
/// Горизонтальная полоска из квадратных клеток — доля 0..1. Заполненные клетки яркие,
/// остальные тусклые: как волна плеера, без сглаживания.
/// </summary>
public class PixelBar : Control
{
    private const double Cell = 4;
    private const double Gap = 1;

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<PixelBar, double>(nameof(Value));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<PixelBar, IBrush?>(nameof(Fill), Brushes.White);

    public static readonly StyledProperty<IBrush?> EmptyProperty =
        AvaloniaProperty.Register<PixelBar, IBrush?>(nameof(Empty), new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)));

    static PixelBar() => AffectsRender<PixelBar>(ValueProperty, FillProperty, EmptyProperty);

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public IBrush? Empty { get => GetValue(EmptyProperty); set => SetValue(EmptyProperty, value); }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 120 : availableSize.Width, Cell);

    public override void Render(DrawingContext context)
    {
        var count = (int)((Bounds.Width + Gap) / (Cell + Gap));
        var filled = (int)Math.Round(Math.Clamp(Value, 0, 1) * count);
        var y = Math.Floor((Bounds.Height - Cell) / 2);

        for (var i = 0; i < count; i++)
            context.FillRectangle(i < filled ? Fill! : Empty!, new Rect(i * (Cell + Gap), y, Cell, Cell));
    }
}

/// <summary>
/// Вертикальные столбики из клеток — по часам или дням недели. Самый высокий столбик —
/// на всю высоту; выделенные (Highlight) рисуются ярче — «твоё время».
/// </summary>
public class PixelColumns : Control
{
    private const double Cell = 4;
    private const double Gap = 1;

    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty =
        AvaloniaProperty.Register<PixelColumns, IReadOnlyList<double>?>(nameof(Values));

    public static readonly StyledProperty<IReadOnlyList<bool>?> HighlightProperty =
        AvaloniaProperty.Register<PixelColumns, IReadOnlyList<bool>?>(nameof(Highlight));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<PixelColumns, IBrush?>(nameof(Fill), new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)));

    public static readonly StyledProperty<IBrush?> AccentProperty =
        AvaloniaProperty.Register<PixelColumns, IBrush?>(nameof(Accent), Brushes.White);

    static PixelColumns() => AffectsRender<PixelColumns>(ValuesProperty, HighlightProperty, FillProperty, AccentProperty);

    public IReadOnlyList<double>? Values { get => GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public IReadOnlyList<bool>? Highlight { get => GetValue(HighlightProperty); set => SetValue(HighlightProperty, value); }
    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public IBrush? Accent { get => GetValue(AccentProperty); set => SetValue(AccentProperty, value); }

    private static readonly IBrush Floor = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));

    public override void Render(DrawingContext context)
    {
        var values = Values;
        if (values is null || values.Count == 0)
            return;

        var max = 0.0;
        foreach (var v in values)
            max = Math.Max(max, v);

        var slot = Bounds.Width / values.Count;
        var width = Math.Max(Cell, Math.Floor((slot - Gap) / Cell) * Cell);
        var rows = (int)((Bounds.Height + Gap) / (Cell + Gap));

        for (var i = 0; i < values.Count; i++)
        {
            var x = Math.Floor(i * slot + (slot - width) / 2);
            var height = max > 0 ? (int)Math.Round(values[i] / max * rows) : 0;
            // Пустой час — одна тусклая клетка на полу, чтобы шкала читалась.
            if (height == 0)
            {
                context.FillRectangle(Floor, new Rect(x, Bounds.Height - Cell, width, Cell));
                continue;
            }

            var brush = Highlight is { } h && i < h.Count && h[i] ? Accent! : Fill!;
            for (var r = 0; r < height; r++)
                context.FillRectangle(brush, new Rect(x, Bounds.Height - Cell - r * (Cell + Gap), width, Cell));
        }
    }
}
