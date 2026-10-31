using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Shiny.Blazor.Controls.Theming;
using Shiny.Controls.Gauge;

namespace Shiny.Blazor.Controls;

/// <summary>
/// A dial gauge - speedometer, tachometer, temperature or fuel - whose needle animates to each new
/// <see cref="Value"/> like a real instrument's: it accelerates, carries its momentum through a change
/// of reading, overshoots a little and settles, and stops against the pegs at either end.
/// </summary>
/// <remarks>
/// <para>
/// The dial is SVG rendered by the component; the needle is moved by <c>gauge.js</c> on
/// <c>requestAnimationFrame</c>, running the same physics as the shared engine. Animating from .NET
/// would cost a render batch per frame, and on Blazor Server a network round trip per frame.
/// </para>
/// <para>
/// Without JavaScript (prerendering, static SSR) the needle is simply drawn at the value.
/// <c>prefers-reduced-motion</c> is honoured: the needle moves straight to each value.
/// </para>
/// </remarks>
public partial class Gauge
{
    internal const string DefaultSize = "220px";

    [Inject] IJSRuntime JS { get; set; } = default!;

    ElementReference rootRef;
    IJSObjectReference? module;
    DotNetObjectReference<Gauge>? selfRef;
    bool disposed;
    bool jsReady;
    bool frozenSet;
    double frozen;
    double peak;
    double lastValue = Double.NaN;
    GaugeJsConfig? lastSent;
    IReadOnlyList<GaugeTick>? ticks;
    (double, double, double, int) ticksKey;

    // ---------------------------------------------------------------------------------------------
    // Value and scale
    // ---------------------------------------------------------------------------------------------

    /// <summary>The value the needle points at. It animates there; it is clamped to the scale.</summary>
    [Parameter] public double Value { get; set; }

    /// <summary>The value at the start of the dial.</summary>
    [Parameter] public double Minimum { get; set; }

    /// <summary>The value at the end of the dial.</summary>
    [Parameter] public double Maximum { get; set; } = 100;

    /// <summary>
    /// Where the dial starts, in compass degrees: 0 is 12 o'clock and angles grow clockwise. The
    /// default, -135, is 7:30 - a classic speedometer.
    /// </summary>
    [Parameter] public double StartAngle { get; set; } = GaugeScale.DefaultStartAngle;

    /// <summary>How far the dial turns clockwise from <see cref="StartAngle"/>. 270 is a speedometer; -90 with 180 a semicircle.</summary>
    [Parameter] public double SweepAngle { get; set; } = GaugeScale.DefaultSweepAngle;

    // ---------------------------------------------------------------------------------------------
    // Animation
    // ---------------------------------------------------------------------------------------------

    /// <summary>How the needle travels to a new value. Spring by default.</summary>
    [Parameter] public GaugeAnimation Animation { get; set; } = GaugeAnimation.Spring;

    /// <summary>For an ease, the length of the move; for a spring, its period. Zero snaps.</summary>
    [Parameter] public TimeSpan AnimationDuration { get; set; } = TimeSpan.FromMilliseconds(800);

    /// <summary>The spring's damping ratio. 1 never overshoots; 0.5-0.7 is a lively analogue needle.</summary>
    [Parameter] public double SpringDamping { get; set; } = 0.65;

    /// <summary>The first time the gauge appears, swing the needle out to the maximum and back.</summary>
    [Parameter] public bool SweepOnLoad { get; set; }

    /// <summary>Raised when the needle comes to rest on its value.</summary>
    [Parameter] public EventCallback OnNeedleSettled { get; set; }

    // ---------------------------------------------------------------------------------------------
    // Track, value arc and ranges
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Thickness of the outer track ring, in dial units - the dial's radius is 100, so the default of
    /// 10 is a tenth of it whatever size the gauge is drawn at. Zero hides the track and value arc.
    /// </summary>
    [Parameter] public double TrackThickness { get; set; } = 10;

    /// <summary>The unfilled track. Null uses <c>--shiny-color-surface-container-highest</c>.</summary>
    [Parameter] public string? TrackColor { get; set; }

    /// <summary>Whether the track fills from the minimum up to the needle.</summary>
    [Parameter] public bool ShowValueArc { get; set; } = true;

    /// <summary>The filled part of the track. Null uses <c>--shiny-color-primary</c>.</summary>
    [Parameter] public string? ValueArcColor { get; set; }

    /// <summary>Whether the value arc takes the colour of the <see cref="Ranges"/> band the needle is in.</summary>
    [Parameter] public bool ColorValueArcByRange { get; set; } = true;

    /// <summary>The coloured bands on the dial - a green normal range, an amber warning, a red redline.</summary>
    [Parameter] public IReadOnlyList<GaugeRange>? Ranges { get; set; }

    /// <summary>Thickness of the range bands, in dial units.</summary>
    [Parameter] public double RangeThickness { get; set; } = 4;

    // ---------------------------------------------------------------------------------------------
    // Ticks
    // ---------------------------------------------------------------------------------------------

    /// <summary>Whether tick marks are drawn.</summary>
    [Parameter] public bool ShowTicks { get; set; } = true;

    /// <summary>Distance between labelled ticks. Zero picks a 1-2-5 interval that gives about ten labels.</summary>
    [Parameter] public double MajorTickInterval { get; set; }

    /// <summary>Unlabelled ticks between each pair of labelled ones.</summary>
    [Parameter] public int MinorTicksPerMajor { get; set; } = 4;

    /// <summary>Whether the labelled ticks carry their numbers.</summary>
    [Parameter] public bool ShowTickLabels { get; set; } = true;

    /// <summary>Composite format for the tick labels, given the tick's value as <c>{0}</c>.</summary>
    [Parameter] public string TickLabelFormat { get; set; } = "{0:0.##}";

    /// <summary>Tick marks and their labels. Null uses <c>--shiny-color-on-surface-variant</c>.</summary>
    [Parameter] public string? TickColor { get; set; }

    // ---------------------------------------------------------------------------------------------
    // Needle
    // ---------------------------------------------------------------------------------------------

    /// <summary>The needle's shape. None leaves a ring-style gauge showing the value arc alone.</summary>
    [Parameter] public GaugeNeedleStyle NeedleStyle { get; set; } = GaugeNeedleStyle.Tapered;

    /// <summary>The needle. Null uses <c>--shiny-color-primary</c>.</summary>
    [Parameter] public string? NeedleColor { get; set; }

    /// <summary>The needle's length as a fraction of the dial's radius.</summary>
    [Parameter] public double NeedleLength { get; set; } = 0.8;

    /// <summary>The cap over the needle's pivot. Null uses <c>--shiny-color-on-surface</c>.</summary>
    [Parameter] public string? HubColor { get; set; }

    // ---------------------------------------------------------------------------------------------
    // Face and text
    // ---------------------------------------------------------------------------------------------

    /// <summary>A disc painted behind the dial. Null leaves it transparent, so the host's own surface shows.</summary>
    [Parameter] public string? FaceColor { get; set; }

    /// <summary>Whether the digital readout is drawn under the pivot. It counts along with the needle.</summary>
    [Parameter] public bool ShowReadout { get; set; } = true;

    /// <summary>Decimal places in the readout.</summary>
    [Parameter] public int ReadoutDecimals { get; set; }

    /// <summary>The units under the readout - "km/h", "x1000 rpm".</summary>
    [Parameter] public string? Units { get; set; }

    /// <summary>A caption drawn above the pivot - "RPM", "FUEL". Also the meter's accessible name.</summary>
    [Parameter] public string? Title { get; set; }

    /// <summary>The readout, units and title. Null uses <c>--shiny-color-on-surface</c>.</summary>
    [Parameter] public string? TextColor { get; set; }

    // ---------------------------------------------------------------------------------------------
    // Peak hold
    // ---------------------------------------------------------------------------------------------

    /// <summary>Whether a marker stays at the highest value reached until <see cref="ResetPeak"/>.</summary>
    [Parameter] public bool ShowPeak { get; set; }

    /// <summary>The peak marker. Null uses <c>--shiny-color-tertiary</c>.</summary>
    [Parameter] public string? PeakColor { get; set; }

    // ---------------------------------------------------------------------------------------------
    // General
    // ---------------------------------------------------------------------------------------------

    /// <summary>The gauge's width as a CSS length; the height follows from the dial's shape.</summary>
    [Parameter] public string Size { get; set; } = DefaultSize;

    /// <summary>Extra classes on the root element.</summary>
    [Parameter] public string? CssClass { get; set; }

    /// <summary>Anything else, splatted onto the root element. <c>style</c> and <c>class</c> are appended, not replaced.</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public IDictionary<string, object>? AdditionalAttributes { get; set; }


    /// <summary>
    /// The highest <see cref="Value"/> set since the gauge was created or <see cref="ResetPeak"/> was
    /// called. It tracks the readings, not the needle, so a spring's overshoot never counts.
    /// </summary>
    public double PeakValue => this.peak;


    /// <summary>Clears the peak marker down to the current value.</summary>
    public void ResetPeak()
    {
        this.peak = this.Scale.Clamp(this.Value);
        this.StateHasChanged();
    }


    /// <summary>Swings the needle out to the maximum and back to <see cref="Value"/>, on demand.</summary>
    public async Task SweepAsync()
    {
        if (this.module is null || !this.jsReady)
            return;

        try
        {
            await this.module.InvokeVoidAsync("sweep", this.rootRef);
        }
        catch (JSDisconnectedException)
        {
        }
    }


    /// <summary>Called by gauge.js when the needle comes to rest.</summary>
    [JSInvokable]
    public Task NotifyNeedleSettled() => this.OnNeedleSettled.InvokeAsync();


    // ---------------------------------------------------------------------------------------------
    // Lifecycle
    // ---------------------------------------------------------------------------------------------

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        var clamped = this.Scale.Clamp(this.Value);

        if (!this.frozenSet)
        {
            // Interactive and animating: draw the needle resting on the minimum, so it rises to the
            // value once gauge.js is attached. Prerendered or static: draw it at the value - with no
            // script coming, that is the only picture there will ever be.
            this.frozen = this.RendererInfo.IsInteractive && this.Animates ? this.Scale.Minimum : clamped;
            this.frozenSet = this.RendererInfo.IsInteractive;
            this.peak = clamped;
        }

        if (!this.lastValue.Equals(clamped))
        {
            this.lastValue = clamped;
            if (clamped > this.peak)
                this.peak = clamped;
        }

        if (this.peak < this.Scale.Minimum || this.peak > this.Scale.Maximum)
            this.peak = this.Scale.Clamp(this.peak);
    }


    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            IJSObjectReference loaded;
            try
            {
                loaded = await this.JS.InvokeAsync<IJSObjectReference>("import", "./_content/Shiny.Blazor.Controls/gauge.js");
            }
            catch (JSDisconnectedException)
            {
                return;
            }

            if (this.disposed)
            {
                await loaded.ReleaseLateAsync();
                return;
            }

            this.module = loaded;
            if (this.OnNeedleSettled.HasDelegate)
                this.selfRef = DotNetObjectReference.Create(this);

            var config = this.BuildConfig();
            this.lastSent = config;
            await this.InitAsync(config);
            this.jsReady = true;
            return;
        }

        if (!this.jsReady || this.module is null)
            return;

        // Only when something gauge.js cares about moved - a re-render for a tick colour is not a
        // reason to cross the interop boundary.
        var next = this.BuildConfig();
        if (next.Equals(this.lastSent))
            return;

        this.lastSent = next;
        try
        {
            await this.module.InvokeVoidAsync("update", this.rootRef, next);
        }
        catch (JSDisconnectedException)
        {
        }
    }


    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(GaugeJsConfig))]
    async Task InitAsync(GaugeJsConfig config)
    {
        try
        {
            await this.module!.InvokeVoidAsync("init", this.rootRef, config, this.frozen, this.selfRef);
        }
        catch (JSDisconnectedException)
        {
        }
    }


    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        this.disposed = true;
        if (this.module is not null)
        {
            try
            {
                await this.module.InvokeVoidAsync("dispose", this.rootRef);
            }
            catch
            {
            }
            await this.module.ReleaseLateAsync();
        }
        this.selfRef?.Dispose();
    }


    // ---------------------------------------------------------------------------------------------
    // Geometry. Everything is round a pivot at (0,0) on a dial of radius 100.
    // ---------------------------------------------------------------------------------------------

    const double Radius = 100;
    const double Gap = 3.5;
    const double MajorTickLength = 9;
    const double MinorTickLength = 5;

    internal GaugeScale Scale => new GaugeScale(this.Minimum, this.Maximum, this.StartAngle, this.SweepAngle).Normalized();

    bool Animates => this.Animation != GaugeAnimation.None && this.AnimationDuration > TimeSpan.Zero;

    double Padding => Math.Max(2, this.TrackThickness / 2);

    double TrackRadius => Radius - this.TrackThickness / 2;

    double InnerRadius => Radius - this.TrackThickness - (this.TrackThickness > 0 ? Gap : 0);

    double RangeRadius => this.InnerRadius - this.RangeThickness / 2;

    bool HasRangeBand => this.Ranges is { Count: > 0 } && this.RangeThickness > 0;

    double TickRadius => this.HasRangeBand ? this.InnerRadius - this.RangeThickness - Gap : this.InnerRadius;

    double LabelRadius => this.TickRadius - (this.ShowTicks ? MajorTickLength : 0) - 11 * 0.95;

    double ReadoutAngle => GaugeLayout.ReadoutAngle(this.Scale.StartAngle, this.Scale.SweepAngle);

    double NeedleReach => Radius * Math.Clamp(this.NeedleLength, 0.1, 1.1);

    internal IReadOnlyList<GaugeTick> Ticks
    {
        get
        {
            var key = (this.Scale.Minimum, this.Scale.Maximum, this.MajorTickInterval, this.MinorTicksPerMajor);
            if (this.ticks is null || this.ticksKey != key)
            {
                this.ticks = GaugeTicks.Generate(this.Scale, this.MajorTickInterval, this.MinorTicksPerMajor);
                this.ticksKey = key;
            }
            return this.ticks;
        }
    }

    internal string ViewBox
    {
        get
        {
            var (minX, minY, maxX, maxY) = GaugeLayout.UnitBounds(this.Scale.StartAngle, this.Scale.SweepAngle, this.ShowReadout, !String.IsNullOrWhiteSpace(this.FaceColor));
            var pad = this.Padding;
            return $"{F(minX * Radius - pad)} {F(minY * Radius - pad)} {F((maxX - minX) * Radius + pad * 2)} {F((maxY - minY) * Radius + pad * 2)}";
        }
    }

    string TrackPath => Arc(this.TrackRadius, this.Scale.StartAngle, this.Scale.EndAngle);

    string NeedlePolygon
    {
        get
        {
            const double half = 3.5;
            const double tail = 14;
            return $"0,{F(-this.NeedleReach)} {F(half)},0 {F(half * 0.6)},{F(tail)} {F(-half * 0.6)},{F(tail)} {F(-half)},0";
        }
    }

    string FrozenArcStyle
    {
        get
        {
            var color = this.ArcColorAt(this.frozen);
            var style = color is null ? "" : $"stroke: {color};";
            if (this.frozen <= this.Scale.Minimum)
                style += "visibility: hidden;";
            return style;
        }
    }


    /// <summary>The arc colour at a needle position: the band it is in, else the explicit colour, else null for the stylesheet's.</summary>
    internal string? ArcColorAt(double position)
    {
        if (this.ColorValueArcByRange && this.Ranges is { Count: > 0 } ranges)
        {
            for (var i = ranges.Count - 1; i >= 0; i--)
            {
                if (ranges[i].Contains(position))
                    return ranges[i].Color;
            }
        }
        return String.IsNullOrWhiteSpace(this.ValueArcColor) ? null : this.ValueArcColor;
    }


    /// <summary>A clockwise SVG arc between two compass angles.</summary>
    internal static string Arc(double radius, double from, double to)
    {
        if (radius <= 0 || to - from <= 0.01)
            return "";

        var start = Point(radius, from);
        if (to - from >= 359.99)
        {
            // One arc command cannot start and end at the same point; two half turns can.
            var half = Point(radius, from + 180);
            return $"M {F(start.X)} {F(start.Y)} A {F(radius)} {F(radius)} 0 1 1 {F(half.X)} {F(half.Y)} A {F(radius)} {F(radius)} 0 1 1 {F(start.X)} {F(start.Y)}";
        }

        var end = Point(radius, to);
        var large = to - from > 180 ? 1 : 0;
        return $"M {F(start.X)} {F(start.Y)} A {F(radius)} {F(radius)} 0 {large} 1 {F(end.X)} {F(end.Y)}";
    }


    static (double X, double Y) Point(double radius, double angle) => GaugeScale.PointAt(0, 0, radius, angle);


    static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);


    // ---------------------------------------------------------------------------------------------
    // Text
    // ---------------------------------------------------------------------------------------------

    int Decimals => Math.Clamp(this.ReadoutDecimals, 0, 6);

    internal string FormatReadout(double value)
        => Math.Round(value, this.Decimals).ToString("F" + this.Decimals, CultureInfo.CurrentCulture);

    internal string FormatTickLabel(double value)
    {
        try
        {
            return String.Format(CultureInfo.CurrentCulture, this.TickLabelFormat ?? "{0}", value);
        }
        catch (FormatException)
        {
            return value.ToString(CultureInfo.CurrentCulture);
        }
    }

    string ValueText => $"{this.FormatReadout(this.Scale.Clamp(this.Value))} {this.Units}".Trim();


    // ---------------------------------------------------------------------------------------------
    // Root element
    // ---------------------------------------------------------------------------------------------

    string RootClass
    {
        get
        {
            var css = "shiny-gauge";
            if (!String.IsNullOrWhiteSpace(this.CssClass))
                css += " " + this.CssClass;
            if (this.AdditionalAttributes?.TryGetValue("class", out var extra) == true && extra is not null)
                css += " " + extra;
            return css;
        }
    }


    internal string RootStyle
    {
        get
        {
            var style = StyleDefaults.Override("width", this.Size, DefaultSize);
            style += Token("--shiny-gauge-track", this.TrackColor);
            style += Token("--shiny-gauge-arc", this.ValueArcColor);
            style += Token("--shiny-gauge-tick", this.TickColor);
            style += Token("--shiny-gauge-needle", this.NeedleColor);
            style += Token("--shiny-gauge-hub", this.HubColor);
            style += Token("--shiny-gauge-text", this.TextColor);
            style += Token("--shiny-gauge-peak", this.PeakColor);

            if (this.AdditionalAttributes?.TryGetValue("style", out var caller) == true && caller is not null)
                style += caller;

            return style;
        }
    }


    static string Token(string name, string? value)
        => String.IsNullOrWhiteSpace(value) ? "" : $"{name}: {value};";


    IReadOnlyDictionary<string, object>? Splatted
        => this.AdditionalAttributes?
            .Where(x => x.Key != "style" && x.Key != "class")
            .ToDictionary(x => x.Key, x => x.Value);


    // ---------------------------------------------------------------------------------------------
    // gauge.js
    // ---------------------------------------------------------------------------------------------

    internal GaugeJsConfig BuildConfig()
    {
        var scale = this.Scale;
        var ranges = this.ColorValueArcByRange ? this.Ranges : null;
        return new GaugeJsConfig
        {
            Minimum = scale.Minimum,
            Maximum = scale.Maximum,
            StartAngle = scale.StartAngle,
            SweepAngle = scale.SweepAngle,
            Value = scale.Clamp(this.Value),
            Mode = this.Animation switch
            {
                GaugeAnimation.Ease => "ease",
                GaugeAnimation.None => "none",
                _ => "spring"
            },
            Duration = this.AnimationDuration.TotalSeconds,
            Damping = this.SpringDamping,
            Decimals = this.Decimals,
            SweepOnLoad = this.SweepOnLoad,
            ArcColor = String.IsNullOrWhiteSpace(this.ValueArcColor) ? null : this.ValueArcColor,
            // Primitive arrays rather than an array of DTOs: the trimmer keeps a single object's
            // members but strips an array element type's (see the interop notes in the repo memory).
            RangeStarts = ranges?.Select(x => Math.Min(x.Start, x.End)).ToArray() ?? [],
            RangeEnds = ranges?.Select(x => Math.Max(x.Start, x.End)).ToArray() ?? [],
            RangeColors = ranges?.Select(x => x.Color).ToArray() ?? []
        };
    }
}


/// <summary>What gauge.js needs to move the needle. Named (never anonymous) so it survives trimming.</summary>
sealed class GaugeJsConfig : IEquatable<GaugeJsConfig>
{
    public double Minimum { get; init; }
    public double Maximum { get; init; }
    public double StartAngle { get; init; }
    public double SweepAngle { get; init; }
    public double Value { get; init; }
    public string Mode { get; init; } = "spring";
    public double Duration { get; init; }
    public double Damping { get; init; }
    public int Decimals { get; init; }
    public bool SweepOnLoad { get; init; }
    public string? ArcColor { get; init; }
    public double[] RangeStarts { get; init; } = [];
    public double[] RangeEnds { get; init; } = [];
    public string[] RangeColors { get; init; } = [];

    public bool Equals(GaugeJsConfig? other)
        => other is not null
            && this.Minimum.Equals(other.Minimum)
            && this.Maximum.Equals(other.Maximum)
            && this.StartAngle.Equals(other.StartAngle)
            && this.SweepAngle.Equals(other.SweepAngle)
            && this.Value.Equals(other.Value)
            && this.Mode == other.Mode
            && this.Duration.Equals(other.Duration)
            && this.Damping.Equals(other.Damping)
            && this.Decimals == other.Decimals
            && this.SweepOnLoad == other.SweepOnLoad
            && this.ArcColor == other.ArcColor
            && this.RangeStarts.AsSpan().SequenceEqual(other.RangeStarts)
            && this.RangeEnds.AsSpan().SequenceEqual(other.RangeEnds)
            && this.RangeColors.AsSpan().SequenceEqual(other.RangeColors);

    public override bool Equals(object? obj) => this.Equals(obj as GaugeJsConfig);

    public override int GetHashCode() => HashCode.Combine(this.Minimum, this.Maximum, this.Value, this.Mode);
}
