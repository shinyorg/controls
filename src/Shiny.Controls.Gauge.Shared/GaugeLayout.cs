namespace Shiny.Controls.Gauge;

/// <summary>Where the dial sits inside the space it was given.</summary>
/// <param name="CenterX">The needle's pivot, x.</param>
/// <param name="CenterY">The needle's pivot, y.</param>
/// <param name="Radius">The outer edge of the dial.</param>
public readonly record struct GaugeLayout(double CenterX, double CenterY, double Radius)
{
    /// <summary>
    /// How far the readout sits from the pivot, as a fraction of the radius, opposite the middle of
    /// the arc. Shared so both hosts draw it in the same place and reserve the same room for it.
    /// </summary>
    public const double ReadoutDistance = 0.42;

    /// <summary>The space kept clear round the readout's anchor, as a fraction of the radius.</summary>
    public const double ReadoutAllowance = 0.2;

    /// <summary>The hub's own allowance, so the pivot is never drawn hard against an edge.</summary>
    public const double HubAllowance = 0.12;


    /// <summary>
    /// Fits a dial into a box as large as it will go.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A dial is not a circle unless its sweep is a full turn. A 180° gauge is a half disc, so
    /// centring a circle in the box wastes half of it; this fits the bounding box of what is actually
    /// drawn - the arc, the hub and, when <paramref name="reserveReadout"/> is set, the readout below
    /// it - which is what lets a semicircle gauge use the whole width of a wide, short box.
    /// </para>
    /// <para>
    /// <paramref name="padding"/> is taken off every side first, for anything drawn outside the
    /// radius (the outer half of a thick track).
    /// </para>
    /// <para>
    /// <paramref name="includeFace"/> fits the whole disc instead: a face is a full circle, so fitting
    /// only the arc would cut it off flat wherever the sweep leaves a gap.
    /// </para>
    /// </remarks>
    public static GaugeLayout Fit(double width, double height, double startAngle, double sweepAngle, double padding, bool reserveReadout, bool includeFace = false)
    {
        var w = Math.Max(0, width - padding * 2);
        var h = Math.Max(0, height - padding * 2);
        if (w <= 0 || h <= 0)
            return new GaugeLayout(width / 2, height / 2, 0);

        var sweep = Math.Clamp(sweepAngle, 1, 360);
        var (minX, minY, maxX, maxY) = UnitBounds(startAngle, sweep, reserveReadout, includeFace);

        var boundsWidth = maxX - minX;
        var boundsHeight = maxY - minY;
        var radius = Math.Min(w / boundsWidth, h / boundsHeight);

        // Centre the fitted bounds in the box, then find where the pivot landed inside them.
        var left = padding + (w - boundsWidth * radius) / 2;
        var top = padding + (h - boundsHeight * radius) / 2;
        return new GaugeLayout(left - minX * radius, top - minY * radius, radius);
    }


    /// <summary>
    /// The compass angle the readout sits at: straight opposite the middle of the arc, so a
    /// speedometer has it below the pivot and a gauge rotated onto its side has it beside it.
    /// </summary>
    public static double ReadoutAngle(double startAngle, double sweepAngle)
        => startAngle + sweepAngle / 2 + 180;


    /// <summary>
    /// The bounds of a unit-radius dial with its pivot at the origin, y down - what <see cref="Fit"/>
    /// scales into a box. A host that scales for itself (an SVG <c>viewBox</c>) can use it directly.
    /// </summary>
    public static (double MinX, double MinY, double MaxX, double MaxY) UnitBounds(double startAngle, double sweep, bool reserveReadout, bool includeFace = false)
    {
        var minX = -HubAllowance;
        var minY = -HubAllowance;
        var maxX = HubAllowance;
        var maxY = HubAllowance;

        void Include(double x, double y, double allowance = 0)
        {
            minX = Math.Min(minX, x - allowance);
            minY = Math.Min(minY, y - allowance);
            maxX = Math.Max(maxX, x + allowance);
            maxY = Math.Max(maxY, y + allowance);
        }

        // The arc's extremes are its two ends plus every compass point it passes through.
        var end = startAngle + sweep;
        IncludePoint(GaugeScale.PointAt(0, 0, 1, startAngle));
        IncludePoint(GaugeScale.PointAt(0, 0, 1, end));

        var firstCardinal = Math.Ceiling(startAngle / 90) * 90;
        for (var angle = firstCardinal; angle <= end + 1e-9; angle += 90)
            IncludePoint(GaugeScale.PointAt(0, 0, 1, angle));

        if (includeFace)
        {
            Include(-1, -1);
            Include(1, 1);
        }

        if (reserveReadout)
        {
            var anchor = GaugeScale.PointAt(0, 0, ReadoutDistance, ReadoutAngle(startAngle, sweep));
            Include(anchor.X, anchor.Y, ReadoutAllowance);
        }

        return (minX, minY, maxX, maxY);

        void IncludePoint((double X, double Y) point) => Include(point.X, point.Y);
    }
}
