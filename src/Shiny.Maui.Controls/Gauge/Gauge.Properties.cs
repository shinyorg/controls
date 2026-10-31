using Shiny.Controls.Gauge;

namespace Shiny.Maui.Controls;

public partial class Gauge
{
    // ---------------------------------------------------------------------------------------------
    // Value and scale
    // ---------------------------------------------------------------------------------------------

    /// <summary>The value the needle points at. It animates there; it is clamped to the scale.</summary>
    public static readonly BindableProperty ValueProperty = BindableProperty.Create(
        nameof(Value), typeof(double), typeof(Gauge), 0.0,
        propertyChanged: (b, _, n) => ((Gauge)b).OnValueChanged((double)n)
    );

    /// <inheritdoc cref="ValueProperty" />
    public double Value
    {
        get => (double)this.GetValue(ValueProperty);
        set => this.SetValue(ValueProperty, value);
    }


    /// <summary>The value at the start of the dial.</summary>
    public static readonly BindableProperty MinimumProperty = BindableProperty.Create(
        nameof(Minimum), typeof(double), typeof(Gauge), 0.0,
        propertyChanged: (b, _, _) => ((Gauge)b).OnScaleChanged()
    );

    /// <inheritdoc cref="MinimumProperty" />
    public double Minimum
    {
        get => (double)this.GetValue(MinimumProperty);
        set => this.SetValue(MinimumProperty, value);
    }


    /// <summary>The value at the end of the dial.</summary>
    public static readonly BindableProperty MaximumProperty = BindableProperty.Create(
        nameof(Maximum), typeof(double), typeof(Gauge), 100.0,
        propertyChanged: (b, _, _) => ((Gauge)b).OnScaleChanged()
    );

    /// <inheritdoc cref="MaximumProperty" />
    public double Maximum
    {
        get => (double)this.GetValue(MaximumProperty);
        set => this.SetValue(MaximumProperty, value);
    }


    /// <summary>
    /// Where the dial starts, in compass degrees: 0 is 12 o'clock and angles grow clockwise. The
    /// default, -135, is 7:30 - a classic speedometer.
    /// </summary>
    public static readonly BindableProperty StartAngleProperty = BindableProperty.Create(
        nameof(StartAngle), typeof(double), typeof(Gauge), GaugeScale.DefaultStartAngle,
        propertyChanged: (b, _, _) => ((Gauge)b).Invalidate()
    );

    /// <inheritdoc cref="StartAngleProperty" />
    public double StartAngle
    {
        get => (double)this.GetValue(StartAngleProperty);
        set => this.SetValue(StartAngleProperty, value);
    }


    /// <summary>
    /// How far the dial turns clockwise from <see cref="StartAngle"/>, in degrees. 270 is a
    /// speedometer; -90 with 180 is a semicircle; 360 is a full ring.
    /// </summary>
    public static readonly BindableProperty SweepAngleProperty = BindableProperty.Create(
        nameof(SweepAngle), typeof(double), typeof(Gauge), GaugeScale.DefaultSweepAngle,
        propertyChanged: (b, _, _) => ((Gauge)b).Invalidate()
    );

    /// <inheritdoc cref="SweepAngleProperty" />
    public double SweepAngle
    {
        get => (double)this.GetValue(SweepAngleProperty);
        set => this.SetValue(SweepAngleProperty, value);
    }


    // ---------------------------------------------------------------------------------------------
    // Animation
    // ---------------------------------------------------------------------------------------------

    /// <summary>How the needle travels to a new value. Spring by default.</summary>
    public static readonly BindableProperty AnimationProperty = BindableProperty.Create(
        nameof(Animation), typeof(GaugeAnimation), typeof(Gauge), GaugeAnimation.Spring,
        propertyChanged: (b, _, _) => ((Gauge)b).OnMotionSettingsChanged()
    );

    /// <inheritdoc cref="AnimationProperty" />
    public GaugeAnimation Animation
    {
        get => (GaugeAnimation)this.GetValue(AnimationProperty);
        set => this.SetValue(AnimationProperty, value);
    }


    /// <summary>
    /// For an ease, the length of the move; for a spring, its period - roughly how long the needle
    /// takes to arrive. Zero snaps.
    /// </summary>
    public static readonly BindableProperty AnimationDurationProperty = BindableProperty.Create(
        nameof(AnimationDuration), typeof(TimeSpan), typeof(Gauge), TimeSpan.FromMilliseconds(800),
        propertyChanged: (b, _, _) => ((Gauge)b).OnMotionSettingsChanged()
    );

    /// <inheritdoc cref="AnimationDurationProperty" />
    public TimeSpan AnimationDuration
    {
        get => (TimeSpan)this.GetValue(AnimationDurationProperty);
        set => this.SetValue(AnimationDurationProperty, value);
    }


    /// <summary>
    /// The spring's damping ratio. 1 arrives without overshooting; 0.5-0.7 is a lively analogue
    /// needle that swings past and settles back.
    /// </summary>
    public static readonly BindableProperty SpringDampingProperty = BindableProperty.Create(
        nameof(SpringDamping), typeof(double), typeof(Gauge), 0.65,
        propertyChanged: (b, _, _) => ((Gauge)b).OnMotionSettingsChanged()
    );

    /// <inheritdoc cref="SpringDampingProperty" />
    public double SpringDamping
    {
        get => (double)this.GetValue(SpringDampingProperty);
        set => this.SetValue(SpringDampingProperty, value);
    }


    /// <summary>
    /// The first time the gauge appears, swing the needle out to the maximum and back - the
    /// self-test a car's instrument cluster does when the ignition comes on.
    /// </summary>
    public static readonly BindableProperty SweepOnLoadProperty = BindableProperty.Create(
        nameof(SweepOnLoad), typeof(bool), typeof(Gauge), false
    );

    /// <inheritdoc cref="SweepOnLoadProperty" />
    public bool SweepOnLoad
    {
        get => (bool)this.GetValue(SweepOnLoadProperty);
        set => this.SetValue(SweepOnLoadProperty, value);
    }


    // ---------------------------------------------------------------------------------------------
    // Track, value arc and ranges
    // ---------------------------------------------------------------------------------------------

    /// <summary>Thickness of the outer track ring. Zero hides the track and the value arc.</summary>
    public static readonly BindableProperty TrackThicknessProperty = BindableProperty.Create(
        nameof(TrackThickness), typeof(double), typeof(Gauge), 10.0,
        propertyChanged: (b, _, _) => ((Gauge)b).Invalidate()
    );

    /// <inheritdoc cref="TrackThicknessProperty" />
    public double TrackThickness
    {
        get => (double)this.GetValue(TrackThicknessProperty);
        set => this.SetValue(TrackThicknessProperty, value);
    }


    /// <summary>The unfilled track. Null uses the theme's SurfaceContainerHighest token.</summary>
    public static readonly BindableProperty TrackColorProperty = BindableProperty.Create(
        nameof(TrackColor), typeof(Color), typeof(Gauge), null,
        propertyChanged: (b, _, _) => ((Gauge)b).Invalidate()
    );

    /// <inheritdoc cref="TrackColorProperty" />
    public Color? TrackColor
    {
        get => (Color?)this.GetValue(TrackColorProperty);
        set => this.SetValue(TrackColorProperty, value);
    }


    /// <summary>Whether the track fills from the minimum up to the needle.</summary>
    public static readonly BindableProperty ShowValueArcProperty = BindableProperty.Create(
        nameof(ShowValueArc), typeof(bool), typeof(Gauge), true,
        propertyChanged: (b, _, _) => ((Gauge)b).Invalidate()
    );

    /// <inheritdoc cref="ShowValueArcProperty" />
    public bool ShowValueArc
    {
        get => (bool)this.GetValue(ShowValueArcProperty);
        set => this.SetValue(ShowValueArcProperty, value);
    }


    /// <summary>The filled part of the track. Null uses the theme's Primary token.</summary>
    public static readonly BindableProperty ValueArcColorProperty = BindableProperty.Create(
        nameof(ValueArcColor), typeof(Color), typeof(Gauge), null,
        propertyChanged: (b, _, _) => ((Gauge)b).Invalidate()
    );

    /// <inheritdoc cref="ValueArcColorProperty" />
    public Color? ValueArcColor
    {
        get => (Color?)this.GetValue(ValueArcColorProperty);
        set => this.SetValue(ValueArcColorProperty, value);
    }


    /// <summary>
    /// Whether the value arc takes the colour of the <see cref="Ranges"/> band the needle is in, so
    /// it turns red as the needle crosses into the redline.
    /// </summary>
    public static readonly BindableProperty ColorValueArcByRangeProperty = BindableProperty.Create(
        nameof(ColorValueArcByRange), typeof(bool), typeof(Gauge), true,
        propertyChanged: (b, _, _) => ((Gauge)b).Invalidate()
    );

    /// <inheritdoc cref="ColorValueArcByRangeProperty" />
    public bool ColorValueArcByRange
    {
        get => (bool)this.GetValue(ColorValueArcByRangeProperty);
        set => this.SetValue(ColorValueArcByRangeProperty, value);
    }


    /// <summary>Thickness of the coloured range bands drawn inside the track.</summary>
    public static readonly BindableProperty RangeThicknessProperty = BindableProperty.Create(
        nameof(RangeThickness), typeof(double), typeof(Gauge), 4.0,
        propertyChanged: (b, _, _) => ((Gauge)b).Invalidate()
    );

    /// <inheritdoc cref="RangeThicknessProperty" />
    public double RangeThickness
    {
        get => (double)this.GetValue(RangeThicknessProperty);
        set => this.SetValue(RangeThicknessProperty, value);
    }


    // ---------------------------------------------------------------------------------------------
    // Ticks
    // ---------------------------------------------------------------------------------------------

    /// <summary>Whether tick marks are drawn.</summary>
    public static readonly BindableProperty ShowTicksProperty = BindableProperty.Create(
        nameof(ShowTicks), typeof(bool), typeof(Gauge), true,
        propertyChanged: (b, _, _) => ((Gauge)b).OnTicksChanged()
    );

    /// <inheritdoc cref="ShowTicksProperty" />
    public bool ShowTicks
    {
        get => (bool)this.GetValue(ShowTicksProperty);
        set => this.SetValue(ShowTicksProperty, value);
    }


    /// <summary>Distance between labelled ticks. Zero picks a 1-2-5 interval that gives about ten labels.</summary>
    public static readonly BindableProperty MajorTickIntervalProperty = BindableProperty.Create(
        nameof(MajorTickInterval), typeof(double), typeof(Gauge), 0.0,
        propertyChanged: (b, _, _) => ((Gauge)b).OnTicksChanged()
    );

    /// <inheritdoc cref="MajorTickIntervalProperty" />
    public double MajorTickInterval
    {
        get => (double)this.GetValue(MajorTickIntervalProperty);
        set => this.SetValue(MajorTickIntervalProperty, value);
    }


    /// <summary>Unlabelled ticks between each pair of labelled ones.</summary>
    public static readonly BindableProperty MinorTicksPerMajorProperty = BindableProperty.Create(
        nameof(MinorTicksPerMajor), typeof(int), typeof(Gauge), 4,
        propertyChanged: (b, _, _) => ((Gauge)b).OnTicksChanged()
    );

    /// <inheritdoc cref="MinorTicksPerMajorProperty" />
    public int MinorTicksPerMajor
    {
        get => (int)this.GetValue(MinorTicksPerMajorProperty);
        set => this.SetValue(MinorTicksPerMajorProperty, value);
    }


    /// <summary>Whether the labelled ticks carry their numbers.</summary>
    public static readonly BindableProperty ShowTickLabelsProperty = BindableProperty.Create(
        nameof(ShowTickLabels), typeof(bool), typeof(Gauge), true,
        propertyChanged: (b, _, _) => ((Gauge)b).Invalidate()
    );

    /// <inheritdoc cref="ShowTickLabelsProperty" />
    public bool ShowTickLabels
    {
        get => (bool)this.GetValue(ShowTickLabelsProperty);
        set => this.SetValue(ShowTickLabelsProperty, value);
    }


    /// <summary>Composite format for the tick labels, given the tick's value as <c>{0}</c>.</summary>
    public static readonly BindableProperty TickLabelFormatProperty = BindableProperty.Create(
        nameof(TickLabelFormat), typeof(string), typeof(Gauge), "{0:0.##}",
        propertyChanged: (b, _, _) => ((Gauge)b).Invalidate()
    );

    /// <inheritdoc cref="TickLabelFormatProperty" />
    public string TickLabelFormat
    {
        get => (string)this.GetValue(TickLabelFormatProperty);
        set => this.SetValue(TickLabelFormatProperty, value);
    }


    /// <summary>Tick marks and their labels. Null uses the theme's OnSurfaceVariant token.</summary>
    public static readonly BindableProperty TickColorProperty = BindableProperty.Create(
        nameof(TickColor), typeof(Color), typeof(Gauge), null,
        propertyChanged: (b, _, _) => ((Gauge)b).Invalidate()
    );

    /// <inheritdoc cref="TickColorProperty" />
    public Color? TickColor
    {
        get => (Color?)this.GetValue(TickColorProperty);
        set => this.SetValue(TickColorProperty, value);
    }


    // ---------------------------------------------------------------------------------------------
    // Needle
    // ---------------------------------------------------------------------------------------------

    /// <summary>The needle's shape. None leaves a ring-style gauge showing the value arc alone.</summary>
    public static readonly BindableProperty NeedleStyleProperty = BindableProperty.Create(
        nameof(NeedleStyle), typeof(GaugeNeedleStyle), typeof(Gauge), GaugeNeedleStyle.Tapered,
        propertyChanged: (b, _, _) => ((Gauge)b).Invalidate()
    );

    /// <inheritdoc cref="NeedleStyleProperty" />
    public GaugeNeedleStyle NeedleStyle
    {
        get => (GaugeNeedleStyle)this.GetValue(NeedleStyleProperty);
        set => this.SetValue(NeedleStyleProperty, value);
    }


    /// <summary>The needle. Null uses the theme's Primary token.</summary>
    public static readonly BindableProperty NeedleColorProperty = BindableProperty.Create(
        nameof(NeedleColor), typeof(Color), typeof(Gauge), null,
        propertyChanged: (b, _, _) => ((Gauge)b).Invalidate()
    );

    /// <inheritdoc cref="NeedleColorProperty" />
    public Color? NeedleColor
    {
        get => (Color?)this.GetValue(NeedleColorProperty);
        set => this.SetValue(NeedleColorProperty, value);
    }


    /// <summary>The needle's length as a fraction of the dial's radius.</summary>
    public static readonly BindableProperty NeedleLengthProperty = BindableProperty.Create(
        nameof(NeedleLength), typeof(double), typeof(Gauge), 0.8,
        propertyChanged: (b, _, _) => ((Gauge)b).Invalidate()
    );

    /// <inheritdoc cref="NeedleLengthProperty" />
    public double NeedleLength
    {
        get => (double)this.GetValue(NeedleLengthProperty);
        set => this.SetValue(NeedleLengthProperty, value);
    }


    /// <summary>The cap over the needle's pivot. Null uses the theme's OnSurface token.</summary>
    public static readonly BindableProperty HubColorProperty = BindableProperty.Create(
        nameof(HubColor), typeof(Color), typeof(Gauge), null,
        propertyChanged: (b, _, _) => ((Gauge)b).Invalidate()
    );

    /// <inheritdoc cref="HubColorProperty" />
    public Color? HubColor
    {
        get => (Color?)this.GetValue(HubColorProperty);
        set => this.SetValue(HubColorProperty, value);
    }


    // ---------------------------------------------------------------------------------------------
    // Face and text
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A disc painted behind the dial. Null (the default) leaves it transparent, so the gauge sits on
    /// whatever card or surface hosts it without drawing a second edge.
    /// </summary>
    public static readonly BindableProperty FaceColorProperty = BindableProperty.Create(
        nameof(FaceColor), typeof(Color), typeof(Gauge), null,
        propertyChanged: (b, _, _) => ((Gauge)b).Invalidate()
    );

    /// <inheritdoc cref="FaceColorProperty" />
    public Color? FaceColor
    {
        get => (Color?)this.GetValue(FaceColorProperty);
        set => this.SetValue(FaceColorProperty, value);
    }


    /// <summary>Whether the digital readout is drawn under the pivot. It counts along with the needle.</summary>
    public static readonly BindableProperty ShowReadoutProperty = BindableProperty.Create(
        nameof(ShowReadout), typeof(bool), typeof(Gauge), true,
        propertyChanged: (b, _, _) => ((Gauge)b).Invalidate()
    );

    /// <inheritdoc cref="ShowReadoutProperty" />
    public bool ShowReadout
    {
        get => (bool)this.GetValue(ShowReadoutProperty);
        set => this.SetValue(ShowReadoutProperty, value);
    }


    /// <summary>Decimal places in the readout.</summary>
    public static readonly BindableProperty ReadoutDecimalsProperty = BindableProperty.Create(
        nameof(ReadoutDecimals), typeof(int), typeof(Gauge), 0,
        propertyChanged: (b, _, _) => ((Gauge)b).Invalidate()
    );

    /// <inheritdoc cref="ReadoutDecimalsProperty" />
    public int ReadoutDecimals
    {
        get => (int)this.GetValue(ReadoutDecimalsProperty);
        set => this.SetValue(ReadoutDecimalsProperty, value);
    }


    /// <summary>The units under the readout - "km/h", "x1000 rpm".</summary>
    public static readonly BindableProperty UnitsProperty = BindableProperty.Create(
        nameof(Units), typeof(string), typeof(Gauge), null,
        propertyChanged: (b, _, _) => ((Gauge)b).OnTextChanged()
    );

    /// <inheritdoc cref="UnitsProperty" />
    public string? Units
    {
        get => (string?)this.GetValue(UnitsProperty);
        set => this.SetValue(UnitsProperty, value);
    }


    /// <summary>A caption drawn above the pivot - "RPM", "FUEL".</summary>
    public static readonly BindableProperty TitleProperty = BindableProperty.Create(
        nameof(Title), typeof(string), typeof(Gauge), null,
        propertyChanged: (b, _, _) => ((Gauge)b).OnTextChanged()
    );

    /// <inheritdoc cref="TitleProperty" />
    public string? Title
    {
        get => (string?)this.GetValue(TitleProperty);
        set => this.SetValue(TitleProperty, value);
    }


    /// <summary>The readout, units and title. Null uses the theme's OnSurface token.</summary>
    public static readonly BindableProperty TextColorProperty = BindableProperty.Create(
        nameof(TextColor), typeof(Color), typeof(Gauge), null,
        propertyChanged: (b, _, _) => ((Gauge)b).Invalidate()
    );

    /// <inheritdoc cref="TextColorProperty" />
    public Color? TextColor
    {
        get => (Color?)this.GetValue(TextColorProperty);
        set => this.SetValue(TextColorProperty, value);
    }


    // ---------------------------------------------------------------------------------------------
    // Peak hold
    // ---------------------------------------------------------------------------------------------

    /// <summary>Whether a marker stays at the highest value reached until <see cref="ResetPeak"/>.</summary>
    public static readonly BindableProperty ShowPeakProperty = BindableProperty.Create(
        nameof(ShowPeak), typeof(bool), typeof(Gauge), false,
        propertyChanged: (b, _, _) => ((Gauge)b).Invalidate()
    );

    /// <inheritdoc cref="ShowPeakProperty" />
    public bool ShowPeak
    {
        get => (bool)this.GetValue(ShowPeakProperty);
        set => this.SetValue(ShowPeakProperty, value);
    }


    /// <summary>The peak marker. Null uses the theme's Tertiary token.</summary>
    public static readonly BindableProperty PeakColorProperty = BindableProperty.Create(
        nameof(PeakColor), typeof(Color), typeof(Gauge), null,
        propertyChanged: (b, _, _) => ((Gauge)b).Invalidate()
    );

    /// <inheritdoc cref="PeakColorProperty" />
    public Color? PeakColor
    {
        get => (Color?)this.GetValue(PeakColorProperty);
        set => this.SetValue(PeakColorProperty, value);
    }


    static readonly BindablePropertyKey PeakValuePropertyKey = BindableProperty.CreateReadOnly(
        nameof(PeakValue), typeof(double), typeof(Gauge), 0.0,
        propertyChanged: (b, _, _) => ((Gauge)b).Invalidate()
    );

    /// <summary>
    /// The highest <see cref="Value"/> set since the gauge was created or <see cref="ResetPeak"/>
    /// was called. It tracks the readings, not the needle, so a spring's overshoot never counts.
    /// </summary>
    public static readonly BindableProperty PeakValueProperty = PeakValuePropertyKey.BindableProperty;

    /// <inheritdoc cref="PeakValueProperty" />
    public double PeakValue => (double)this.GetValue(PeakValueProperty);


    // ---------------------------------------------------------------------------------------------
    // Theme tokens, resolved on this element. The defaults are what draws before a theme merges.
    // ---------------------------------------------------------------------------------------------

    internal static readonly BindableProperty ThemePrimaryProperty = BindableProperty.Create(
        "ThemePrimary", typeof(Color), typeof(Gauge), Color.FromArgb("#0055D9"),
        propertyChanged: (b, _, _) => ((Gauge)b).Invalidate()
    );

    internal static readonly BindableProperty ThemeTrackProperty = BindableProperty.Create(
        "ThemeTrack", typeof(Color), typeof(Gauge), Color.FromArgb("#D7DCE1"),
        propertyChanged: (b, _, _) => ((Gauge)b).Invalidate()
    );

    internal static readonly BindableProperty ThemeOnSurfaceProperty = BindableProperty.Create(
        "ThemeOnSurface", typeof(Color), typeof(Gauge), Color.FromArgb("#161C23"),
        propertyChanged: (b, _, _) => ((Gauge)b).Invalidate()
    );

    internal static readonly BindableProperty ThemeOnSurfaceVariantProperty = BindableProperty.Create(
        "ThemeOnSurfaceVariant", typeof(Color), typeof(Gauge), Color.FromArgb("#3B475B"),
        propertyChanged: (b, _, _) => ((Gauge)b).Invalidate()
    );

    internal static readonly BindableProperty ThemeTertiaryProperty = BindableProperty.Create(
        "ThemeTertiary", typeof(Color), typeof(Gauge), Color.FromArgb("#6A4CAD"),
        propertyChanged: (b, _, _) => ((Gauge)b).Invalidate()
    );

    internal Color EffectiveTrackColor => this.TrackColor ?? (Color)this.GetValue(ThemeTrackProperty);
    internal Color EffectiveValueArcColor => this.ValueArcColor ?? (Color)this.GetValue(ThemePrimaryProperty);
    internal Color EffectiveNeedleColor => this.NeedleColor ?? (Color)this.GetValue(ThemePrimaryProperty);
    internal Color EffectiveHubColor => this.HubColor ?? (Color)this.GetValue(ThemeOnSurfaceProperty);
    internal Color EffectiveTickColor => this.TickColor ?? (Color)this.GetValue(ThemeOnSurfaceVariantProperty);
    internal Color EffectiveTextColor => this.TextColor ?? (Color)this.GetValue(ThemeOnSurfaceProperty);
    internal Color EffectivePeakColor => this.PeakColor ?? (Color)this.GetValue(ThemeTertiaryProperty);
}
