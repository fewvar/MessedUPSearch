using System;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Transformation;
using Avalonia.Platform;
using Avalonia.Threading;
using MessedUpSearchA.Motion;
using MessedUpSearchA.ViewModels;

namespace MessedUpSearchA;

/// <summary>
/// Микродетали главного окна (tmp/plans/polish-details-v1.0.1.md):
///  - вход при запуске: контент проявляется и чуть поднимается за 0.5 с, звезда делает четверть оборота;
///  - звезда-логотип вращается, пока идёт анализ или парсер, и останавливается на ближайшей четверти
///    оборота (звезда четырёхлучевая — рывка не видно);
///  - «Эффекты фона» (галочка в настройках): плёночное зерно и световое пятно за курсором.
/// Всё движущееся выключается при «Уменьшить движение».
/// </summary>
public partial class MainWindow
{
    private const double SpinDegreesPerSecond = 45;     // оборот за 8 с — спокойно, не «загрузка»
    private const int GrainSize = 256;

    private readonly Stopwatch _spinClock = new();
    private DispatcherTimer? _spinTimer;
    private double _angle;
    private bool _spinning;

    private DispatcherTimer? _grainTimer;
    private readonly Random _random = new();
    private ImageBrush? _grain;
    private RadialGradientBrush? _spot;

    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;

    private void InitializeEffects()
    {
        Logo.RenderTransformOrigin = RelativePoint.Center;
        Logo.RenderTransform = new RotateTransform(0);

        Opened += (_, _) => PlayEntrance();
        DataContextChanged += (_, _) =>
        {
            if (Vm is { } vm)
            {
                vm.PropertyChanged += OnVmPropertyChanged;
                ApplyBackgroundEffects();
            }
        };
        Activated += (_, _) => ApplyBackgroundEffects();
        Deactivated += (_, _) => _grainTimer?.Stop();
        Motion.Motion.ReducedChanged += ApplyBackgroundEffects;

        AddHandler(PointerMovedEvent, OnWindowPointerMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        PointerExited += (_, _) => Spotlight.IsVisible = false;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsBusy))
            SetSpinning(Vm!.IsBusy);
        else if (e.PropertyName == nameof(MainWindowViewModel.BackgroundEffects))
            ApplyBackgroundEffects();
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

    // ---------- фон ----------

    private void ApplyBackgroundEffects()
    {
        var enabled = Vm?.BackgroundEffects ?? true;

        Grain.IsVisible = enabled;
        Spotlight.IsVisible = enabled && !Motion.Motion.Reduced && Spotlight.IsVisible;

        if (!enabled)
        {
            _grainTimer?.Stop();
            return;
        }

        _grain ??= CreateGrain();
        Grain.Fill = _grain;

        // Зерно «шевелится» только в активном окне и без «Уменьшить движение».
        if (Motion.Motion.Reduced || !IsActive)
        {
            _grainTimer?.Stop();
            return;
        }

        _grainTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(120), DispatcherPriority.Background, (_, _) =>
        {
            _grain!.DestinationRect = new RelativeRect(_random.Next(GrainSize), _random.Next(GrainSize),
                GrainSize, GrainSize, RelativeUnit.Absolute);
            Grain.InvalidateVisual();
        });
        _grainTimer.Start();
    }

    /// <summary>Шумовая плитка 256×256: светлые пиксели с низкой и разной прозрачностью.</summary>
    private ImageBrush CreateGrain()
    {
        var bitmap = new WriteableBitmap(new PixelSize(GrainSize, GrainSize), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var buffer = bitmap.Lock())
        {
            var pixels = new byte[GrainSize * GrainSize * 4];
            for (var i = 0; i < GrainSize * GrainSize; i++)
            {
                var alpha = (byte)(_random.NextDouble() < 0.5 ? 0 : _random.Next(8, 40));
                var value = (byte)(alpha);          // premultiplied: белый с прозрачностью alpha
                pixels[i * 4] = value;
                pixels[i * 4 + 1] = value;
                pixels[i * 4 + 2] = value;
                pixels[i * 4 + 3] = alpha;
            }
            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, buffer.Address, pixels.Length);
        }

        return new ImageBrush(bitmap)
        {
            TileMode = TileMode.Tile,
            Stretch = Stretch.None,
            DestinationRect = new RelativeRect(0, 0, GrainSize, GrainSize, RelativeUnit.Absolute)
        };
    }

    private void OnWindowPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!(Vm?.BackgroundEffects ?? true) || Motion.Motion.Reduced)
        {
            Spotlight.IsVisible = false;
            return;
        }

        var p = e.GetPosition(this);
        _spot ??= new RadialGradientBrush
        {
            RadiusX = new RelativeScalar(420, RelativeUnit.Absolute),
            RadiusY = new RelativeScalar(420, RelativeUnit.Absolute),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(18, 255, 255, 255), 0),
                new GradientStop(Color.FromArgb(0, 255, 255, 255), 1)
            }
        };
        _spot.Center = new RelativePoint(p.X, p.Y, RelativeUnit.Absolute);
        _spot.GradientOrigin = _spot.Center;
        Spotlight.Fill = _spot;
        Spotlight.IsVisible = true;
    }
}
