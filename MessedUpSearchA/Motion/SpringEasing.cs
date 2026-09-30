using System;
using Avalonia.Animation.Easings;

namespace MessedUpSearchA.Motion;

/// <summary>
/// Затухающая пружина в духе Apple (WWDC23 «Animate with springs»): задаётся не кривой, а ощущаемой
/// длительностью D и отскоком bounce. ω₀ = 2π/D, ζ = 1 − bounce; при bounce = 0 затухание критическое:
///   x(t) = 1 − e^(−ω₀t)(1 + ω₀t).
///
/// Easing в Avalonia получает прогресс 0..1 от Duration анимации, поэтому Duration = D · <see cref="Span"/>:
/// к 1.5·D пружина досаживается до 99.9%, хвост незаметен. Удобнее брать длительность из <see cref="DurationFor"/>.
/// </summary>
public sealed class SpringEasing : Easing
{
    public const double Span = 1.5;

    /// <summary>0 — без отскока (по умолчанию у Apple); 0.3 — лёгкий щелчок.</summary>
    public double Bounce { get; set; }

    public static TimeSpan DurationFor(double perceptualSeconds) => TimeSpan.FromSeconds(perceptualSeconds * Span);

    public override double Ease(double progress)
    {
        if (progress <= 0)
            return 0;
        if (progress >= 1)
            return 1;

        const double w0 = 2 * Math.PI;              // в единицах D
        var zeta = 1 - Bounce;
        var t = progress * Span;

        if (zeta >= 0.999)
            return 1 - Math.Exp(-w0 * t) * (1 + w0 * t);

        var wd = w0 * Math.Sqrt(1 - zeta * zeta);
        return 1 - Math.Exp(-zeta * w0 * t) * (Math.Cos(wd * t) + zeta * w0 / wd * Math.Sin(wd * t));
    }
}
