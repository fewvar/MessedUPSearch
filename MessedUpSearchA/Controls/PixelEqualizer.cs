using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace MessedUpSearchA.Controls;

/// <summary>
/// Маленький пиксельный эквалайзер — «модель слушает бит». Тот же язык, что у волны плеера:
/// столбики из клеток 2 px, без сглаживания. Таймер крутится, только пока контрол виден и
/// <see cref="IsActive"/> — в покое не тратит процессор. При «Уменьшить движение» стоит на месте.
/// </summary>
public class PixelEqualizer : Control
{
    private const double Cell = 2;
    private const int Bars = 5;

    private readonly double[] _levels = new double[Bars];
    private readonly double[] _targets = new double[Bars];
    private readonly Random _random = new();
    private DispatcherTimer? _timer;
    private bool _attached;

    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<PixelEqualizer, bool>(nameof(IsActive));

    public static readonly StyledProperty<IBrush?> BrushProperty =
        AvaloniaProperty.Register<PixelEqualizer, IBrush?>(nameof(Brush), Brushes.White);

    static PixelEqualizer()
    {
        AffectsRender<PixelEqualizer>(BrushProperty);
        IsActiveProperty.Changed.AddClassHandler<PixelEqualizer>((c, _) => c.UpdateTimer());
        IsVisibleProperty.Changed.AddClassHandler<PixelEqualizer>((c, _) => c.UpdateTimer());
    }

    public PixelEqualizer()
    {
        Width = Bars * Cell * 2;
        Height = 12;
        for (var i = 0; i < Bars; i++)
            _levels[i] = _targets[i] = 0.35 + 0.1 * (i % 3);
    }

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public IBrush? Brush
    {
        get => GetValue(BrushProperty);
        set => SetValue(BrushProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        UpdateTimer();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        _timer?.Stop();
    }

    private void UpdateTimer()
    {
        var run = IsActive && IsVisible && _attached && !Motion.Motion.Reduced;
        if (!run)
        {
            _timer?.Stop();
            return;
        }

        _timer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(70), DispatcherPriority.Render, (_, _) => Step());
        _timer.Start();
    }

    private void Step()
    {
        for (var i = 0; i < Bars; i++)
        {
            // Новая цель время от времени, к ней — быстро вверх, плавно вниз: как у настоящего индикатора.
            if (_random.NextDouble() < 0.35)
                _targets[i] = 0.2 + _random.NextDouble() * 0.8;
            var speed = _targets[i] > _levels[i] ? 0.6 : 0.25;
            _levels[i] += (_targets[i] - _levels[i]) * speed;
        }
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var brush = Brush ?? Brushes.White;
        var height = Bounds.Height;
        for (var i = 0; i < Bars; i++)
        {
            var h = Math.Max(Cell, Math.Round(_levels[i] * height / Cell) * Cell);
            context.FillRectangle(brush, new Rect(i * Cell * 2, height - h, Cell, h));
        }
    }
}
