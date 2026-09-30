using System;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using MessedUpSearchA.Motion;
using MessedUpSearchA.ViewModels;

namespace MessedUpSearchA;

/// <summary>
/// Микродетали главного окна (tmp/plans/polish-details-v1.0.1.md):
///  - вход при запуске: контент проявляется и чуть поднимается за 0.5 с, звезда делает четверть оборота;
///  - звезда-логотип вращается, пока идёт анализ или парсер, и останавливается на ближайшей четверти
///    оборота (звезда четырёхлучевая — рывка не видно);
/// Всё движущееся выключается при «Уменьшить движение».
/// </summary>
public partial class MainWindow
{
    private const double SpinDegreesPerSecond = 45;     // оборот за 8 с — спокойно, не «загрузка»

    private readonly Stopwatch _spinClock = new();
    private DispatcherTimer? _spinTimer;
    private double _angle;
    private bool _spinning;

    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;

    private void InitializeEffects()
    {
        Logo.RenderTransformOrigin = RelativePoint.Center;
        Logo.RenderTransform = new RotateTransform(0);

        Opened += (_, _) => PlayEntrance();
        DataContextChanged += (_, _) =>
        {
            if (Vm is { } vm)
                vm.PropertyChanged += OnVmPropertyChanged;
        };
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsBusy))
            SetSpinning(Vm!.IsBusy);
    }

    // ---------- вход ----------

    private void PlayEntrance()
    {
        if (Motion.Motion.Reduced)
            return;

        var (duration, easing) = Motion.Motion.Spring(0.35);
        Body.Transitions = null;
        Body.Opacity = 0;
        Body.RenderTransform = TransformOperations.Parse("translateY(10px)");
        Body.Transitions =
        [
            new Avalonia.Animation.DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromSeconds(0.3), Easing = new Avalonia.Animation.Easings.CubicEaseOut() },
            new Avalonia.Animation.TransformOperationsTransition { Property = RenderTransformProperty, Duration = duration, Easing = easing }
        ];
        Body.Opacity = 1;
        Body.RenderTransform = TransformOperations.Parse("translateY(0px)");

        SettleTo(90, 0.5);
    }

    // ---------- звезда ----------

    private void SetSpinning(bool spinning)
    {
        if (spinning == _spinning)
            return;
        _spinning = spinning;

        if (spinning && !Motion.Motion.Reduced)
        {
            _spinClock.Restart();
            var from = _angle;
            _spinTimer?.Stop();
            _spinTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) =>
            {
                // Равноускоренный разгон за полсекунды — не дёргается с места.
                const double ramp = 0.5;
                var t = _spinClock.Elapsed.TotalSeconds;
                var travelled = t < ramp ? t * t / (2 * ramp) : t - ramp / 2;
                SetAngle(from + SpinDegreesPerSecond * travelled);
            });
            _spinTimer.Start();
        }
        else if (!spinning)
        {
            _spinTimer?.Stop();
            SettleTo(Math.Ceiling(_angle / 90 + 0.001) * 90, 0.45);
        }
    }

    /// <summary>Довернуть звезду до угла пружиной (ощущаемая длительность seconds).</summary>
    private void SettleTo(double target, double seconds)
    {
        if (Motion.Motion.Reduced)
        {
            SetAngle(target % 360);
            return;
        }

        var from = _angle;
        var easing = new SpringEasing();
        var duration = SpringEasing.DurationFor(seconds).TotalSeconds;
        var clock = Stopwatch.StartNew();
        _spinTimer?.Stop();
        _spinTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (s, _) =>
        {
            var p = Math.Min(1, clock.Elapsed.TotalSeconds / duration);
            SetAngle(from + (target - from) * easing.Ease(p));
            if (p >= 1)
            {
                ((DispatcherTimer)s!).Stop();
                _angle %= 360;
            }
        });
        _spinTimer.Start();
    }

    private void SetAngle(double angle)
    {
        _angle = angle;
        if (Logo.RenderTransform is RotateTransform rotate)
            rotate.Angle = angle;
    }
}
