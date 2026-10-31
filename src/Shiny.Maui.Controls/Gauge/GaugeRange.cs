namespace Shiny.Maui.Controls;

/// <summary>
/// A coloured band on a <see cref="Gauge"/> dial - the green "normal" stretch of an oil temperature
/// gauge, or the redline on a tachometer.
/// </summary>
/// <remarks>
/// Bands are drawn just inside the track. With <see cref="Gauge.ColorValueArcByRange"/> on, the
/// value arc also takes the colour of whichever band the needle is in, so the whole arc turns red
/// as the needle crosses into the redline.
/// </remarks>
public class GaugeRange : BindableObject
{
    /// <summary>The value the band starts at.</summary>
    public static readonly BindableProperty StartProperty = BindableProperty.Create(
        nameof(Start), typeof(double), typeof(GaugeRange), 0.0
    );

    /// <inheritdoc cref="StartProperty" />
    public double Start
    {
        get => (double)this.GetValue(StartProperty);
        set => this.SetValue(StartProperty, value);
    }


    /// <summary>The value the band ends at.</summary>
    public static readonly BindableProperty EndProperty = BindableProperty.Create(
        nameof(End), typeof(double), typeof(GaugeRange), 0.0
    );

    /// <inheritdoc cref="EndProperty" />
    public double End
    {
        get => (double)this.GetValue(EndProperty);
        set => this.SetValue(EndProperty, value);
    }


    /// <summary>The band's colour.</summary>
    public static readonly BindableProperty ColorProperty = BindableProperty.Create(
        nameof(Color), typeof(Color), typeof(GaugeRange), Colors.Red
    );

    /// <inheritdoc cref="ColorProperty" />
    public Color Color
    {
        get => (Color)this.GetValue(ColorProperty);
        set => this.SetValue(ColorProperty, value);
    }


    /// <summary>Whether a value falls inside the band, whichever way round Start and End were given.</summary>
    public bool Contains(double value)
        => value >= Math.Min(this.Start, this.End) && value <= Math.Max(this.Start, this.End);
}
