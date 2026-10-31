namespace Shiny.Controls.Gauge;

/// <summary>
/// Maps values onto the dial: a value range laid along an arc that starts at
/// <see cref="StartAngle"/> and turns clockwise through <see cref="SweepAngle"/>.
/// </summary>
/// <remarks>
/// Angles are compass degrees - 0 is 12 o'clock and they grow clockwise - because that is how
/// anyone describes a dial ("starts at 7:30, ends at 4:30" is -135 and +270). Each host converts to
/// its own drawing convention in exactly one place: SVG's <c>rotate()</c> already turns clockwise
/// from the needle drawn pointing up, and MAUI's <c>DrawArc</c> wants counter-clockwise from
/// 3 o'clock, which is <c>90 - angle</c>.
/// </remarks>
/// <param name="Minimum">The value at the start of the arc.</param>
/// <param name="Maximum">The value at the end of the arc.</param>
/// <param name="StartAngle">Compass angle of the minimum, in degrees.</param>
/// <param name="SweepAngle">Clockwise distance from the minimum to the maximum, in degrees (1-360).</param>
public readonly record struct GaugeScale(double Minimum, double Maximum, double StartAngle, double SweepAngle)
{
    /// <summary>The 270° dial most speedometers and tachometers use: 7:30 round to 4:30.</summary>
    public const double DefaultStartAngle = -135;

    /// <inheritdoc cref="DefaultStartAngle" />
    public const double DefaultSweepAngle = 270;

    /// <summary>
    /// The same scale with its numbers made safe to draw: a maximum that is not above the minimum
    /// gets a range of one, and the sweep is clamped to a single turn.
    /// </summary>
    /// <remarks>
    /// Both are states a binding passes through on its way to a valid one (Maximum is set before
    /// Minimum is raised, a sweep is typed digit by digit), so they are absorbed rather than thrown.
    /// </remarks>
    public GaugeScale Normalized()
    {
        var max = this.Maximum > this.Minimum ? this.Maximum : this.Minimum + 1;
        var sweep = Double.IsFinite(this.SweepAngle) ? Math.Clamp(this.SweepAngle, 1, 360) : DefaultSweepAngle;
        var start = Double.IsFinite(this.StartAngle) ? this.StartAngle : DefaultStartAngle;
        return new GaugeScale(this.Minimum, max, start, sweep);
    }


    /// <summary>The length of the value range.</summary>
    public double Range => this.Maximum - this.Minimum;

    /// <summary>Compass angle of the end of the arc.</summary>
    public double EndAngle => this.StartAngle + this.SweepAngle;


    /// <summary>Clamps a value into the scale.</summary>
    public double Clamp(double value)
        => Double.IsNaN(value) ? this.Minimum : Math.Clamp(value, this.Minimum, this.Maximum);


    /// <summary>
    /// How far along the arc a value sits, 0 at the minimum and 1 at the maximum. Not clamped, so a
    /// needle overshooting past a peg reports a fraction just outside the range.
    /// </summary>
    public double FractionOf(double value)
        => this.Range <= 0 ? 0 : (value - this.Minimum) / this.Range;


    /// <summary>The value that sits at a fraction of the arc.</summary>
    public double ValueAt(double fraction) => this.Minimum + fraction * this.Range;


    /// <summary>Compass angle of a value, in degrees. Not clamped.</summary>
    public double AngleOf(double value) => this.StartAngle + this.FractionOf(value) * this.SweepAngle;


    /// <summary>
    /// The point at a compass angle on a circle, in a y-down coordinate space (screen, SVG and MAUI
    /// canvases all agree on that).
    /// </summary>
    public static (double X, double Y) PointAt(double centerX, double centerY, double radius, double angle)
    {
        var radians = angle * Math.PI / 180;
        return (centerX + radius * Math.Sin(radians), centerY - radius * Math.Cos(radians));
    }
}
