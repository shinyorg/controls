using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using Shiny.Controls.Gauge;
using Shiny.Maui.Controls.MotionIcons;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls;

/// <summary>
/// A dial gauge - speedometer, tachometer, temperature or fuel - whose needle animates to each new
/// <see cref="Value"/> like a real instrument's: it accelerates, carries its momentum through a change
/// of reading, overshoots a little and settles, and stops against the pegs at either end.
/// </summary>
/// <remarks>
/// <para>
/// Drawn with Microsoft.Maui.Graphics, so it is one control on every platform. The scale, ticks,
/// layout and needle physics come from <c>Shiny.Controls.Gauge.Shared</c>, which the Blazor
/// <c>Gauge</c> uses too.
/// </para>
/// <para>
/// The frame clock is the one <see cref="MotionIcons"/> already share, and the gauge only listens to
/// it while the needle is actually moving. A dashboard of resting gauges costs no timer at all.
/// </para>
/// </remarks>
public partial class Gauge : GraphicsView
{
    readonly NeedleMotion motion = new();
    readonly ObservableCollection<GaugeRange> ranges = [];
    IReadOnlyList<GaugeTick>? ticks;
    MotionTicker? ticker;
    bool isSubscribed;
    bool hasAppeared;

    /// <summary>Creates a gauge.</summary>
    public Gauge()
    {
        this.Drawable = new GaugeDrawable(this);
        this.HeightRequest = 220;
        this.WidthRequest = 220;

        this.SetDynamicResource(ThemePrimaryProperty, ShinyThemeKeys.Color.Primary);
        this.SetDynamicResource(ThemeTrackProperty, ShinyThemeKeys.Color.SurfaceContainerHighest);
        this.SetDynamicResource(ThemeOnSurfaceProperty, ShinyThemeKeys.Color.OnSurface);
        this.SetDynamicResource(ThemeOnSurfaceVariantProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
        this.SetDynamicResource(ThemeTertiaryProperty, ShinyThemeKeys.Color.Tertiary);

        this.OnMotionSettingsChanged();

        // Subscribed here, not in a defaultValueCreator: a lazily created default never raises
        // propertyChanged, so a hook wired there would never run.
        this.ranges.CollectionChanged += this.OnRangesChanged;

        this.Loaded += this.OnGaugeLoaded;
        this.Unloaded += (_, _) => this.Unsubscribe();
        this.UpdateDescription();
    }


    /// <summary>
    /// The coloured bands on the dial - a green normal range, an amber warning, a red redline.
    /// </summary>
    public IList<GaugeRange> Ranges => this.ranges;

    /// <summary>Where the needle is drawn right now, which mid-animation is not <see cref="Value"/>.</summary>
    public double NeedleValue => this.motion.Position;

    /// <summary>Whether the needle is still on its way somewhere.</summary>
    public bool IsNeedleMoving => this.motion.IsMoving;

    /// <summary>Raised when the needle comes to rest on its value.</summary>
    public event EventHandler? NeedleSettled;


    /// <summary>
    /// Swings the needle out to the maximum and back to <see cref="Value"/> - the instrument
    /// cluster's ignition self-test, on demand.
    /// </summary>
    public void Sweep()
    {
        this.motion.Sweep(this.Value);
        this.SyncTicker();
        this.Invalidate();
    }


    /// <summary>Clears the peak marker down to the current value.</summary>
    public void ResetPeak() => this.SetValue(PeakValuePropertyKey, this.Clamp(this.Value));


    internal GaugeScale DialScale => new GaugeScale(this.Minimum, this.Maximum, this.StartAngle, this.SweepAngle).Normalized();

    internal IReadOnlyList<GaugeTick> Ticks
        => this.ticks ??= GaugeTicks.Generate(this.DialScale, this.MajorTickInterval, this.MinorTicksPerMajor);


    /// <summary>The readout text for a needle position.</summary>
    internal string FormatReadout(double value)
        => Math.Round(value, Math.Clamp(this.ReadoutDecimals, 0, 6))
            .ToString("F" + Math.Clamp(this.ReadoutDecimals, 0, 6), CultureInfo.CurrentCulture);


    internal string FormatTickLabel(double value)
    {
        try
        {
            return String.Format(CultureInfo.CurrentCulture, this.TickLabelFormat ?? "{0}", value);
        }
        catch (FormatException)
        {
            // A format typed half-way through a binding should not take the page down.
            return value.ToString(CultureInfo.CurrentCulture);
        }
    }


    /// <summary>The band a value is in. Later bands win where they overlap, as they draw on top.</summary>
    internal GaugeRange? RangeAt(double value)
    {
        for (var i = this.ranges.Count - 1; i >= 0; i--)
        {
            if (this.ranges[i].Contains(value))
                return this.ranges[i];
        }
        return null;
    }


    /// <summary>
    /// Moves the needle on by <paramref name="delta"/> without the frame clock - the test seam, since
    /// a headless test host has no dispatcher to drive one.
    /// </summary>
    internal void Advance(TimeSpan delta)
    {
        if (!this.motion.Step(delta.TotalSeconds))
            return;

        this.Invalidate();
        if (!this.motion.IsMoving)
        {
            this.Unsubscribe();
            this.UpdateDescription();
            this.NeedleSettled?.Invoke(this, EventArgs.Empty);
        }
    }


    /// <inheritdoc />
    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);

        if (propertyName == nameof(this.IsVisible))
            this.SyncTicker();
    }


    /// <inheritdoc />
    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();

        // The bands are not children of the gauge, so they inherit nothing on their own and a
        // binding on one would resolve against null.
        foreach (var range in this.ranges)
            SetInheritedBindingContext(range, this.BindingContext);
    }


    void OnGaugeLoaded(object? sender, EventArgs e)
    {
        if (!this.hasAppeared)
        {
            this.hasAppeared = true;

            // The needle has been resting at the minimum since construction; it rises to the value
            // now, where it can be seen doing it, rather than having arrived before the page showed.
            if (this.SweepOnLoad)
                this.motion.Sweep(this.Value);
        }
        this.SyncTicker();
    }


    void OnValueChanged(double value)
    {
        var clamped = this.Clamp(value);
        if (clamped > this.PeakValue)
            this.SetValue(PeakValuePropertyKey, clamped);

        this.motion.SetTarget(clamped);
        this.SyncTicker();
        this.UpdateDescription();
        this.Invalidate();
    }


    void OnScaleChanged()
    {
        this.ticks = null;
        this.motion.SetBounds(this.DialScale.Minimum, this.DialScale.Maximum);
        this.motion.SetTarget(this.Value);

        var peak = this.PeakValue;
        if (peak < this.DialScale.Minimum || peak > this.DialScale.Maximum)
            this.SetValue(PeakValuePropertyKey, this.Clamp(peak));

        this.SyncTicker();
        this.Invalidate();
    }


    void OnTicksChanged()
    {
        this.ticks = null;
        this.Invalidate();
    }


    void OnTextChanged()
    {
        this.UpdateDescription();
        this.Invalidate();
    }


    void OnMotionSettingsChanged()
    {
        this.motion.Mode = this.Animation;
        this.motion.Duration = this.AnimationDuration.TotalSeconds;
        this.motion.Damping = this.SpringDamping;

        // Turning animation off mid-flight lands the needle rather than freezing it part way.
        if (this.Animation == GaugeAnimation.None || this.AnimationDuration <= TimeSpan.Zero)
        {
            this.motion.Snap(this.motion.FinalTarget);
            this.Unsubscribe();
            this.Invalidate();
        }
    }


    void OnRangesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (GaugeRange range in e.OldItems)
                range.PropertyChanged -= this.OnRangePropertyChanged;
        }

        if (e.NewItems is not null)
        {
            foreach (GaugeRange range in e.NewItems)
            {
                range.PropertyChanged += this.OnRangePropertyChanged;
                SetInheritedBindingContext(range, this.BindingContext);
            }
        }

        this.Invalidate();
    }


    void OnRangePropertyChanged(object? sender, PropertyChangedEventArgs e) => this.Invalidate();


    void UpdateDescription()
    {
        var text = $"{this.Title} {this.FormatReadout(this.Clamp(this.Value))} {this.Units}".Trim();
        SemanticProperties.SetDescription(this, text);
    }


    double Clamp(double value) => this.DialScale.Clamp(value);


    /// <summary>Listens to the frame clock only while attached, visible and moving.</summary>
    void SyncTicker()
    {
        if (this.motion.IsMoving && this.IsLoaded && this.IsVisible)
            this.Subscribe();
        else
            this.Unsubscribe();
    }


    void Subscribe()
    {
        if (this.isSubscribed)
            return;

        this.ticker ??= MotionTicker.For(this);
        if (this.ticker is null)
            return;

        this.ticker.Tick += this.Advance;
        this.isSubscribed = true;
    }


    void Unsubscribe()
    {
        if (!this.isSubscribed || this.ticker is null)
            return;

        this.ticker.Tick -= this.Advance;
        this.isSubscribed = false;
    }
}
