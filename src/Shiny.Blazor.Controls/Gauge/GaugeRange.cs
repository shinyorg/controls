namespace Shiny.Blazor.Controls;

/// <summary>
/// A coloured band on a <see cref="Gauge"/> dial - the green "normal" stretch of a temperature
/// gauge, or the redline on a tachometer.
/// </summary>
/// <remarks>
/// With <see cref="Gauge.ColorValueArcByRange"/> on, the value arc also takes the colour of whichever
/// band the needle is in, so the whole arc turns red as the needle crosses into the redline.
/// </remarks>
/// <param name="Start">The value the band starts at.</param>
/// <param name="End">The value the band ends at.</param>
/// <param name="Color">Any CSS colour, including a <c>var(--…)</c> token.</param>
public sealed record GaugeRange(double Start, double End, string Color)
{
    /// <summary>Whether a value falls inside the band, whichever way round Start and End were given.</summary>
    public bool Contains(double value)
        => value >= Math.Min(this.Start, this.End) && value <= Math.Max(this.Start, this.End);
}
