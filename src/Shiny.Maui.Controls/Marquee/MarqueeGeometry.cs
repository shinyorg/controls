namespace Shiny.Maui.Controls;

/// <summary>
/// The arithmetic behind <see cref="Marquee"/>, kept free of views so it can be tested headless.
/// </summary>
/// <remarks>
/// Items are laid end to end along an <em>axis</em> - the unit vector at <c>Angle</c> degrees, in
/// screen coordinates (y down) - and the whole run slides back along it. At 0° the axis points right
/// and the content travels left; at 90° the axis points down and the content travels up.
/// </remarks>
static class MarqueeGeometry
{
    /// <summary>The direction items are laid out in. Content travels the opposite way unless reversed.</summary>
    public static (double X, double Y) Axis(double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180;
        var x = Math.Cos(radians);
        var y = Math.Sin(radians);

        // Snap the floating-point noise at the right angles, so 90° is exactly vertical.
        return (Math.Abs(x) < 1e-12 ? 0 : x, Math.Abs(y) < 1e-12 ? 0 : y);
    }


    /// <summary>
    /// How much of the axis one item takes up. An upright box at an angle covers the projection of both
    /// its sides; one rotated with the track covers just its width.
    /// </summary>
    public static double ExtentAlong(Size item, double angleDegrees, bool upright)
    {
        if (!upright)
            return item.Width;

        var (x, y) = Axis(angleDegrees);
        return Math.Abs(item.Width * x) + Math.Abs(item.Height * y);
    }


    /// <summary>How thick one item makes the band, measured across the axis.</summary>
    public static double ExtentAcross(Size item, double angleDegrees, bool upright)
    {
        if (!upright)
            return item.Height;

        var (x, y) = Axis(angleDegrees);
        return Math.Abs(item.Width * y) + Math.Abs(item.Height * x);
    }


    /// <summary>How long a line along the axis has to be to span a viewport of this size.</summary>
    public static double ViewportExtent(Size viewport, double angleDegrees)
    {
        var (x, y) = Axis(angleDegrees);
        return Math.Abs(viewport.Width * x) + Math.Abs(viewport.Height * y);
    }


    /// <summary>
    /// How many copies of the item set it takes to keep the viewport covered for a whole loop: the
    /// run is shifted back by up to one period, so it has to span the viewport plus a period, plus an
    /// item's worth at each end so nothing pops in at the edge. Never fewer than <paramref name="minimum"/>.
    /// </summary>
    public static int Copies(double period, double viewportExtent, double largestItemExtent, int minimum)
    {
        minimum = Math.Max(1, minimum);
        if (period <= 0 || double.IsNaN(period) || double.IsInfinity(viewportExtent) || double.IsNaN(viewportExtent))
            return minimum;

        var needed = (int)Math.Ceiling((viewportExtent + (2 * largestItemExtent)) / period) + 1;
        return Math.Max(minimum, needed);
    }


    /// <summary>
    /// Moves the scroll offset on by <paramref name="elapsed"/>, wrapped into [0, period) so the loop
    /// never runs out of numbers. <paramref name="speed"/> in units per second wins when it is positive;
    /// otherwise one period takes <paramref name="duration"/>.
    /// </summary>
    public static double Advance(double offset, double period, TimeSpan elapsed, TimeSpan duration, double speed, bool reverse)
    {
        if (period <= 0)
            return 0;

        var rate = speed > 0
            ? speed
            : duration > TimeSpan.Zero ? period / duration.TotalSeconds : 0;

        var step = rate * elapsed.TotalSeconds;
        return Wrap(offset + (reverse ? -step : step), period);
    }


    public static double Wrap(double value, double period)
    {
        if (period <= 0)
            return 0;

        var result = value % period;
        return result < 0 ? result + period : result;
    }


    /// <summary>
    /// The end points of a linear gradient running along the axis across a box, in the 0-1 coordinates
    /// a <see cref="LinearGradientBrush"/> uses. The larger component reaches the box edge.
    /// </summary>
    public static (Point Start, Point End) GradientPoints(double angleDegrees)
    {
        var (x, y) = Axis(angleDegrees);
        var k = 0.5 / Math.Max(Math.Abs(x), Math.Abs(y));
        return (new Point(0.5 - (x * k), 0.5 - (y * k)), new Point(0.5 + (x * k), 0.5 + (y * k)));
    }
}
