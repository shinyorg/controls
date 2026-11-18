namespace Shiny.Controls.Keyframe;

/// <summary>
/// The built-in easing curves. Every entry is a plain <see cref="EasingFunction"/>, so callers
/// can freely substitute their own lambda anywhere one of these is accepted.
/// </summary>
public static class Easings
{
    /// <summary>No easing — progress passes through unchanged.</summary>
    public static readonly EasingFunction Linear = t => t;

    /// <summary>Holds the previous value until the segment completes, then snaps. Useful for
    /// discrete properties like visibility or sprite frames.</summary>
    public static readonly EasingFunction StepEnd = t => t >= 1d ? 1d : 0d;

    /// <summary>Holds the segment's starting value for its whole length and lands on the next key
    /// only when the segment ends. The same curve as <see cref="StepEnd"/>, under the name the
    /// keyframe authoring tools use for it.</summary>
    public static readonly EasingFunction Hold = StepEnd;

    /// <summary>Snaps to the new value immediately at the start of the segment.</summary>
    public static readonly EasingFunction StepStart = t => t <= 0d ? 0d : 1d;

    // --- Power curves -------------------------------------------------------------------

    /// <summary>Accelerates from rest (t²).</summary>
    public static readonly EasingFunction QuadIn = t => t * t;

    /// <summary>Decelerates to rest (t²).</summary>
    public static readonly EasingFunction QuadOut = t => 1d - (1d - t) * (1d - t);

    /// <summary>Accelerates then decelerates (t²).</summary>
    public static readonly EasingFunction QuadInOut = t =>
        t < 0.5d ? 2d * t * t : 1d - Pow(-2d * t + 2d, 2) / 2d;

    /// <summary>Accelerates from rest (t³).</summary>
    public static readonly EasingFunction CubicIn = t => t * t * t;

    /// <summary>Decelerates to rest (t³). The workhorse for UI that enters the screen.</summary>
    public static readonly EasingFunction CubicOut = t => 1d - Pow(1d - t, 3);

    /// <summary>Accelerates then decelerates (t³).</summary>
    public static readonly EasingFunction CubicInOut = t =>
        t < 0.5d ? 4d * t * t * t : 1d - Pow(-2d * t + 2d, 3) / 2d;

    /// <summary>Accelerates from rest (t⁴).</summary>
    public static readonly EasingFunction QuartIn = t => Pow(t, 4);

    /// <summary>Decelerates to rest (t⁴).</summary>
    public static readonly EasingFunction QuartOut = t => 1d - Pow(1d - t, 4);

    /// <summary>Accelerates then decelerates (t⁴).</summary>
    public static readonly EasingFunction QuartInOut = t =>
        t < 0.5d ? 8d * Pow(t, 4) : 1d - Pow(-2d * t + 2d, 4) / 2d;

    /// <summary>Accelerates from rest (t⁵).</summary>
    public static readonly EasingFunction QuintIn = t => Pow(t, 5);

    /// <summary>Decelerates to rest (t⁵).</summary>
    public static readonly EasingFunction QuintOut = t => 1d - Pow(1d - t, 5);

    /// <summary>Accelerates then decelerates (t⁵).</summary>
    public static readonly EasingFunction QuintInOut = t =>
        t < 0.5d ? 16d * Pow(t, 5) : 1d - Pow(-2d * t + 2d, 5) / 2d;

    // --- Trigonometric ------------------------------------------------------------------

    /// <summary>Gentle acceleration from rest.</summary>
    public static readonly EasingFunction SinIn = t => 1d - Math.Cos(t * Math.PI / 2d);

    /// <summary>Gentle deceleration to rest.</summary>
    public static readonly EasingFunction SinOut = t => Math.Sin(t * Math.PI / 2d);

    /// <summary>Gentle acceleration then deceleration.</summary>
    public static readonly EasingFunction SinInOut = t => -(Math.Cos(Math.PI * t) - 1d) / 2d;

    // --- Exponential --------------------------------------------------------------------

    /// <summary>Very slow start, very fast finish.</summary>
    public static readonly EasingFunction ExpoIn = t => t <= 0d ? 0d : Math.Pow(2d, 10d * t - 10d);

    /// <summary>Very fast start, very slow finish.</summary>
    public static readonly EasingFunction ExpoOut = t => t >= 1d ? 1d : 1d - Math.Pow(2d, -10d * t);

    /// <summary>Exponential acceleration then deceleration.</summary>
    public static readonly EasingFunction ExpoInOut = t =>
        t <= 0d ? 0d
        : t >= 1d ? 1d
        : t < 0.5d ? Math.Pow(2d, 20d * t - 10d) / 2d
        : (2d - Math.Pow(2d, -20d * t + 10d)) / 2d;

    // --- Circular -----------------------------------------------------------------------

    /// <summary>Follows a circular arc, accelerating.</summary>
    public static readonly EasingFunction CircIn = t => 1d - Math.Sqrt(Math.Max(0d, 1d - t * t));

    /// <summary>Follows a circular arc, decelerating.</summary>
    public static readonly EasingFunction CircOut = t => Math.Sqrt(Math.Max(0d, 1d - Pow(t - 1d, 2)));

    /// <summary>Follows a circular arc in both directions.</summary>
    public static readonly EasingFunction CircInOut = t =>
        t < 0.5d
            ? (1d - Math.Sqrt(Math.Max(0d, 1d - Pow(2d * t, 2)))) / 2d
            : (Math.Sqrt(Math.Max(0d, 1d - Pow(-2d * t + 2d, 2))) + 1d) / 2d;

    // --- Overshoot ----------------------------------------------------------------------

    // The 1.70158 constant is the standard Penner value: it produces roughly 10% overshoot.
    const double BackConstant = 1.70158d;
    const double BackInOutConstant = BackConstant * 1.525d;

    /// <summary>Pulls back before moving forward — "anticipation" in animation terms.</summary>
    public static readonly EasingFunction BackIn = t =>
        (BackConstant + 1d) * t * t * t - BackConstant * t * t;

    /// <summary>Overshoots the target then settles back.</summary>
    public static readonly EasingFunction BackOut = t =>
        1d + (BackConstant + 1d) * Pow(t - 1d, 3) + BackConstant * Pow(t - 1d, 2);

    /// <summary>Anticipates, then overshoots.</summary>
    public static readonly EasingFunction BackInOut = t =>
        t < 0.5d
            ? Pow(2d * t, 2) * ((BackInOutConstant + 1d) * 2d * t - BackInOutConstant) / 2d
            : (Pow(2d * t - 2d, 2) * ((BackInOutConstant + 1d) * (t * 2d - 2d) + BackInOutConstant) + 2d) / 2d;

    // --- Elastic ------------------------------------------------------------------------

    const double ElasticPeriod = 2d * Math.PI / 3d;
    const double ElasticInOutPeriod = 2d * Math.PI / 4.5d;

    /// <summary>Winds up with growing oscillation, then releases.</summary>
    public static readonly EasingFunction ElasticIn = t =>
        t <= 0d ? 0d
        : t >= 1d ? 1d
        : -Math.Pow(2d, 10d * t - 10d) * Math.Sin((t * 10d - 10.75d) * ElasticPeriod);

    /// <summary>Springs past the target and oscillates to rest.</summary>
    public static readonly EasingFunction ElasticOut = t =>
        t <= 0d ? 0d
        : t >= 1d ? 1d
        : Math.Pow(2d, -10d * t) * Math.Sin((t * 10d - 0.75d) * ElasticPeriod) + 1d;

    /// <summary>Oscillates at both ends.</summary>
    public static readonly EasingFunction ElasticInOut = t =>
        t <= 0d ? 0d
        : t >= 1d ? 1d
        : t < 0.5d
            ? -(Math.Pow(2d, 20d * t - 10d) * Math.Sin((20d * t - 11.125d) * ElasticInOutPeriod)) / 2d
            : Math.Pow(2d, -20d * t + 10d) * Math.Sin((20d * t - 11.125d) * ElasticInOutPeriod) / 2d + 1d;

    // --- Bounce -------------------------------------------------------------------------

    /// <summary>Bounces to rest at the target, like a dropped ball.</summary>
    public static readonly EasingFunction BounceOut = BounceOutCore;

    /// <summary>Reverse bounce — settles into motion.</summary>
    public static readonly EasingFunction BounceIn = t => 1d - BounceOutCore(1d - t);

    /// <summary>Bounces at both ends.</summary>
    public static readonly EasingFunction BounceInOut = t =>
        t < 0.5d
            ? (1d - BounceOutCore(1d - 2d * t)) / 2d
            : (1d + BounceOutCore(2d * t - 1d)) / 2d;

    static double BounceOutCore(double t)
    {
        const double n = 7.5625d;
        const double d = 2.75d;

        if (t < 1d / d)
            return n * t * t;

        if (t < 2d / d)
        {
            t -= 1.5d / d;
            return n * t * t + 0.75d;
        }

        if (t < 2.5d / d)
        {
            t -= 2.25d / d;
            return n * t * t + 0.9375d;
        }

        t -= 2.625d / d;
        return n * t * t + 0.984375d;
    }

    // --- CSS-named curves ---------------------------------------------------------------

    /// <summary>CSS <c>ease</c> — cubic-bezier(0.25, 0.1, 0.25, 1).</summary>
    public static readonly EasingFunction Ease = new CubicBezierEasing(0.25, 0.1, 0.25, 1.0).Ease;

    /// <summary>CSS <c>ease-in</c> — cubic-bezier(0.42, 0, 1, 1).</summary>
    public static readonly EasingFunction EaseIn = new CubicBezierEasing(0.42, 0.0, 1.0, 1.0).Ease;

    /// <summary>CSS <c>ease-out</c> — cubic-bezier(0, 0, 0.58, 1).</summary>
    public static readonly EasingFunction EaseOut = new CubicBezierEasing(0.0, 0.0, 0.58, 1.0).Ease;

    /// <summary>CSS <c>ease-in-out</c> — cubic-bezier(0.42, 0, 0.58, 1).</summary>
    public static readonly EasingFunction EaseInOut = new CubicBezierEasing(0.42, 0.0, 0.58, 1.0).Ease;

    /// <summary>Material Design's standard "emphasized" deceleration curve.</summary>
    public static readonly EasingFunction Emphasized = new CubicBezierEasing(0.2, 0.0, 0.0, 1.0).Ease;

    /// <summary>Builds a custom CSS-style cubic-bézier curve.</summary>
    public static EasingFunction CubicBezier(double x1, double y1, double x2, double y2)
        => new CubicBezierEasing(x1, y1, x2, y2).Ease;

    /// <summary>
    /// Builds a physically-parameterised spring curve, normalised so it starts at 0 and settles at 1.
    /// </summary>
    /// <param name="damping">Damping ratio. Below 1 oscillates, 1 is critically damped, above 1 is sluggish.</param>
    /// <param name="frequency">Undamped angular frequency — how fast it wants to move.</param>
    /// <remarks>
    /// This is a closed-form solution of the spring ODE rather than a numeric integration, so it is
    /// cheap enough to evaluate per frame and — importantly — is a pure function of <c>t</c>, which
    /// keeps it seekable and reversible like every other curve here.
    /// </remarks>
    public static EasingFunction Spring(double damping = 0.5d, double frequency = 10d)
    {
        var zeta = Math.Max(0d, damping);
        var omega = Math.Max(1e-4d, frequency);

        if (zeta < 1d)
        {
            // Underdamped — oscillates around the target before settling.
            var omegaD = omega * Math.Sqrt(1d - zeta * zeta);
            return t => t >= 1d
                ? 1d
                : 1d - Math.Exp(-zeta * omega * t) *
                    (Math.Cos(omegaD * t) + zeta * omega / omegaD * Math.Sin(omegaD * t));
        }

        if (Math.Abs(zeta - 1d) < 1e-6d)
        {
            // Critically damped — fastest approach with no overshoot.
            return t => t >= 1d ? 1d : 1d - Math.Exp(-omega * t) * (1d + omega * t);
        }

        // Overdamped — two real roots, no oscillation.
        var root = omega * Math.Sqrt(zeta * zeta - 1d);
        var r1 = -zeta * omega + root;
        var r2 = -zeta * omega - root;
        return t => t >= 1d
            ? 1d
            : 1d - (r2 * Math.Exp(r1 * t) - r1 * Math.Exp(r2 * t)) / (r2 - r1);
    }

    /// <summary>
    /// Builds a spring from a single "bounce" amount, the way SwiftUI and the design tools express
    /// one: 0 settles with no overshoot, 0.3 is a gentle settle, 0.7 is very bouncy.
    /// </summary>
    /// <param name="bounce">0 to just under 1. Converted to a damping ratio of <c>1 - bounce</c>.</param>
    /// <param name="frequency">Undamped angular frequency across the segment.</param>
    public static EasingFunction Bouncy(double bounce, double frequency = 12d)
        => Spring(1d - Math.Clamp(bounce, 0d, 0.95d), frequency);

    /// <summary>Reverses a curve in time: <c>Reverse(f)(t) == 1 - f(1 - t)</c>.</summary>
    public static EasingFunction Reverse(EasingFunction easing)
    {
        ArgumentNullException.ThrowIfNull(easing);
        return t => 1d - easing(1d - t);
    }

    /// <summary>
    /// Mirrors a curve about its midpoint, turning any "in" curve into the matching "in-out".
    /// </summary>
    public static EasingFunction Mirror(EasingFunction easing)
    {
        ArgumentNullException.ThrowIfNull(easing);
        return t => t < 0.5d
            ? easing(t * 2d) / 2d
            : 1d - easing((1d - t) * 2d) / 2d;
    }

    /// <summary>
    /// Quantises progress into <paramref name="steps"/> discrete jumps, matching CSS <c>steps()</c>.
    /// </summary>
    /// <param name="steps">Number of jumps. Must be at least 1.</param>
    /// <param name="jumpAtStart">When true the first jump happens at t=0 (CSS <c>jump-start</c>);
    /// otherwise the last jump happens at t=1 (CSS <c>jump-end</c>, the default).</param>
    public static EasingFunction Steps(int steps, bool jumpAtStart = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(steps, 1);
        return t =>
        {
            var clamped = Math.Clamp(t, 0d, 1d);
            var index = jumpAtStart
                ? Math.Ceiling(clamped * steps)
                : Math.Floor(clamped * steps);
            return Math.Clamp(index / steps, 0d, 1d);
        };
    }

    static double Pow(double value, int exponent)
    {
        var result = 1d;
        for (var i = 0; i < exponent; i++)
            result *= value;
        return result;
    }
}
