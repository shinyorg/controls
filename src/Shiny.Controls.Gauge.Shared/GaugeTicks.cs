namespace Shiny.Controls.Gauge;

/// <summary>One tick mark on the dial.</summary>
/// <param name="Value">The value the tick marks.</param>
/// <param name="IsMajor">Major ticks are longer and carry the labels; minor ticks subdivide them.</param>
public readonly record struct GaugeTick(double Value, bool IsMajor);


/// <summary>Works out where the tick marks go.</summary>
public static class GaugeTicks
{
    /// <summary>How many labelled ticks an automatic interval aims for.</summary>
    public const int TargetMajorCount = 10;

    /// <summary>
    /// The most ticks <see cref="Generate"/> will produce. A scale of 0-1,000,000 with an interval
    /// of 1 is a typo, not a request for a million line segments every frame.
    /// </summary>
    public const int MaximumTickCount = 1000;


    /// <summary>
    /// A "nice" major interval for a range - 1, 2 or 5 times a power of ten - whose number of labelled
    /// ticks comes closest to <paramref name="targetCount"/>.
    /// </summary>
    /// <remarks>
    /// The 1-2-5 series is what instruments are printed with: 0-8 gives 1, 0-240 gives 20 (not 25 or
    /// 30, which no speedometer has ever had) and 0-7000 gives 1000. The closest count wins rather
    /// than the next one up, which would turn 0-240 into a dial labelled every 50.
    /// </remarks>
    public static double NiceInterval(double range, int targetCount = TargetMajorCount)
    {
        if (!(range > 0) || !Double.IsFinite(range))
            return 1;

        var target = Math.Max(1, targetCount);
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(range / target)));

        var best = magnitude;
        var bestMiss = Double.MaxValue;
        foreach (var power in (ReadOnlySpan<double>)[0.1, 1, 10])
        {
            foreach (var step in (ReadOnlySpan<double>)[1, 2, 5])
            {
                var candidate = step * magnitude * power;
                var miss = Math.Abs(range / candidate - target);

                // Ties go to the wider interval: fewer labels crowd less.
                if (miss <= bestMiss)
                {
                    best = candidate;
                    bestMiss = miss;
                }
            }
        }
        return best;
    }


    /// <summary>
    /// The ticks for a scale, in ascending order. Major ticks fall on multiples of the interval
    /// counted from the minimum, and the minimum and maximum are always major so the dial has
    /// labelled ends even when the range is not a multiple of the interval.
    /// </summary>
    /// <param name="scale">The scale to tick.</param>
    /// <param name="majorInterval">Distance between labelled ticks; zero or less picks one with <see cref="NiceInterval"/>.</param>
    /// <param name="minorPerMajor">Unlabelled ticks between each pair of major ones; zero for none.</param>
    public static IReadOnlyList<GaugeTick> Generate(GaugeScale scale, double majorInterval, int minorPerMajor)
    {
        scale = scale.Normalized();
        var range = scale.Range;

        var interval = majorInterval > 0 && Double.IsFinite(majorInterval)
            ? majorInterval
            : NiceInterval(range);

        // Too fine an interval is widened rather than truncated, so the ticks still span the dial.
        while (range / interval > MaximumTickCount)
            interval *= 10;

        var minor = Math.Max(0, minorPerMajor);
        if ((range / interval) * (minor + 1) > MaximumTickCount)
            minor = 0;

        var step = interval / (minor + 1);

        // Within a hair of a tick counts as on it, so 0.1 + 0.2 lands on 0.3 and the last tick is
        // not dropped (or doubled) by floating point.
        var epsilon = step * 1e-6;

        var ticks = new List<GaugeTick>();
        var count = (int)Math.Floor(range / step + 1e-6);
        for (var i = 0; i <= count; i++)
        {
            var value = scale.Minimum + i * step;
            ticks.Add(new GaugeTick(value, i % (minor + 1) == 0));
        }

        var last = ticks[^1].Value;
        if (scale.Maximum - last > epsilon)
            ticks.Add(new GaugeTick(scale.Maximum, true));
        else
            ticks[^1] = new GaugeTick(scale.Maximum, true);

        return ticks;
    }
}
