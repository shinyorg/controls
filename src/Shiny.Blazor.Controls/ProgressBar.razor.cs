using Microsoft.AspNetCore.Components;
using Shiny.Blazor.Controls.Theming;

namespace Shiny.Blazor.Controls;

public partial class ProgressBar : IDisposable
{
    Timer? pulseTimer;
    bool isPulsing;
    int pulseKey;

    // Value
    [Parameter] public double Value { get; set; }
    [Parameter] public EventCallback<double> ValueChanged { get; set; }
    [Parameter] public double Minimum { get; set; } = 0;
    [Parameter] public double Maximum { get; set; } = 100;

    // Appearance
    //
    // Each default below is also declared in ProgressBar.razor.css. The style properties emit a
    // declaration only where the parameter differs from it, so an app stylesheet targeting
    // .shiny-pb-track or .shiny-pb-fill actually wins - an inline style never loses to a selector.
    internal const string DefaultTrackColor = "var(--shiny-color-surface-container-highest, #D7DCE1)";
    internal const string DefaultBarColor = "var(--shiny-color-primary, #0055D9)";
    internal const string DefaultCornerRadius = "var(--shiny-shape-corner-extra-small, 4px)";
    internal const string DefaultTextColor = "var(--shiny-color-on-primary, #FFFFFF)";
    internal const double DefaultTrackHeight = 8;

    [Parameter] public string TrackColor { get; set; } = DefaultTrackColor;
    [Parameter] public string BarColor { get; set; } = DefaultBarColor;
    [Parameter] public double TrackHeight { get; set; } = DefaultTrackHeight;
    [Parameter] public string CornerRadius { get; set; } = DefaultCornerRadius;

    // Gradient
    [Parameter] public bool UseGradient { get; set; }
    [Parameter] public string GradientStartColor { get; set; } = "var(--shiny-color-primary, #0055D9)";
    [Parameter] public string GradientEndColor { get; set; } = "var(--shiny-color-tertiary, #6A4CAD)";

    // Pulse (Vista-style shimmer sweep)
    [Parameter] public bool PulseEnabled { get; set; }
    [Parameter] public bool PulseOnValueChange { get; set; } = true;
    [Parameter] public TimeSpan PulseInterval { get; set; } = TimeSpan.Zero;
    [Parameter] public string PulseColor { get; set; } = "rgba(255,255,255,0.4)";
    [Parameter] public double PulseLength { get; set; } = 0.4;
    [Parameter] public int PulseSpeed { get; set; } = 800;

    // Text
    [Parameter] public bool ShowText { get; set; }
    [Parameter] public string TextFormat { get; set; } = "{0:0}%";
    [Parameter] public string TextColor { get; set; } = DefaultTextColor;
    /// <summary>Overlay label size in px. The default, <c>-1</c>, follows the theme type scale.</summary>
    [Parameter] public double FontSize { get; set; } = -1;

    // Fill animation
    /// <summary>
    /// Whether a change to <see cref="Value"/> slides the fill to its new width instead of snapping.
    /// Applies in both directions, so a value that drops drains rather than jumping backwards.
    /// </summary>
    [Parameter] public bool AnimateProgress { get; set; } = true;

    /// <summary>Length of the fill slide in milliseconds. Zero or less snaps.</summary>
    [Parameter] public int ProgressAnimationDuration { get; set; } = 250;

    /// <summary>CSS timing function for the fill slide.</summary>
    [Parameter] public string ProgressAnimationEasing { get; set; } = "cubic-bezier(0.33, 1, 0.68, 1)";

    // Segments
    /// <summary>
    /// Splits the bar into this many separate steps with a gap between them. <c>0</c> or <c>1</c> draws
    /// the continuous bar. The fill runs across the steps in order, so a value part-way through a step
    /// lights that step partially - set <see cref="Maximum"/> to the segment count for whole steps.
    /// </summary>
    /// <remarks>
    /// Indeterminate mode and the pulse sheen always use the continuous bar. A gradient is spread
    /// across the steps, each step taking the colour at its centre.
    /// </remarks>
    [Parameter] public int Segments { get; set; }

    /// <summary>Gap between segments in px.</summary>
    [Parameter] public double SegmentSpacing { get; set; } = 4;

    // Indeterminate
    [Parameter] public bool IsIndeterminate { get; set; }

    // General
    [Parameter] public string? CssClass { get; set; }
    [Parameter(CaptureUnmatchedValues = true)]
    public IDictionary<string, object>? AdditionalAttributes { get; set; }

    double Percentage => Maximum > Minimum
        ? Math.Clamp((Value - Minimum) / (Maximum - Minimum), 0, 1) * 100
        : 0;

    string FormattedText => string.Format(TextFormat, Percentage);

    string RootStyle => "";

    string TrackStyle =>
        StyleDefaults.Override("height", TrackHeight, DefaultTrackHeight) +
        StyleDefaults.Override("border-radius", CornerRadius, DefaultCornerRadius) +
        StyleDefaults.Override("background", TrackColor, DefaultTrackColor);

    string FillStyle
    {
        get
        {
            var width = IsIndeterminate ? "30%" : $"{Percentage}%";

            // A gradient is never the stylesheet's default, so it always inlines; a plain bar colour
            // only does when it is not the one the stylesheet already paints.
            var bg = UseGradient
                ? $"background: linear-gradient(to right, {GradientStartColor}, {GradientEndColor});"
                : StyleDefaults.Override("background", BarColor, DefaultBarColor);
            var lengthPct = Math.Clamp(PulseLength, 0.05, 1.0) * 100;
            var pulseVars = $"--pulse-color: {PulseColor}; --pulse-speed: {PulseSpeed}ms; --pulse-length: {lengthPct}%;";

            var duration = AnimateProgress && ProgressAnimationDuration > 0 ? ProgressAnimationDuration : 0;
            var fillVars = $"--fill-duration: {duration}ms; --fill-easing: {ProgressAnimationEasing};";

            return $"width: {width}; {bg}"
                + StyleDefaults.Override("border-radius", CornerRadius, DefaultCornerRadius)
                + $" {pulseVars} {fillVars}";
        }
    }

    bool IsSegmented => Segments > 1 && !IsIndeterminate;

    string SegmentedTrackStyle =>
        StyleDefaults.Override("height", TrackHeight, DefaultTrackHeight) +
        $"gap: {Css(Math.Max(SegmentSpacing, 0))}px;";

    string SegmentStyle =>
        StyleDefaults.Override("border-radius", CornerRadius, DefaultCornerRadius) +
        StyleDefaults.Override("background", TrackColor, DefaultTrackColor);

    /// <summary>How much of segment <paramref name="index"/> is lit, from 0 to 1.</summary>
    internal double SegmentFill(int index)
        => Math.Clamp(Percentage / 100 * Segments - index, 0, 1);

    string SegmentFillStyle(int index)
    {
        string bg;
        if (UseGradient)
        {
            // Each step takes the gradient's colour at its own centre, so the run of steps reads as one
            // gradient without every step repeating the whole ramp.
            var endWeight = Segments > 1 ? (index + 0.5) / Segments * 100 : 0;
            bg = $"background: color-mix(in srgb, {GradientStartColor}, {GradientEndColor} {Css(endWeight)}%);";
        }
        else
        {
            bg = StyleDefaults.Override("background", BarColor, DefaultBarColor);
        }

        var duration = AnimateProgress && ProgressAnimationDuration > 0 ? ProgressAnimationDuration : 0;
        return $"width: {Css(SegmentFill(index) * 100)}%; {bg}"
            + StyleDefaults.Override("border-radius", CornerRadius, DefaultCornerRadius)
            + $" --fill-duration: {duration}ms; --fill-easing: {ProgressAnimationEasing};";
    }

    static string Css(double value) => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    string TextStyle =>
        StyleDefaults.Override("color", TextColor, DefaultTextColor) +
        (FontSize >= 0 ? $"font-size: {FontSize}px;" : "");

    string PulseClass => isPulsing ? "shiny-pb-pulse" : "";

    string IndeterminateClass => IsIndeterminate ? "shiny-pb--indeterminate" : "";

    public override async Task SetParametersAsync(ParameterView parameters)
    {
        var oldValue = Value;
        var oldPulseInterval = PulseInterval;
        var oldPulseEnabled = PulseEnabled;

        await base.SetParametersAsync(parameters);

        if (PulseEnabled && PulseOnValueChange && Math.Abs(Value - oldValue) > double.Epsilon && oldValue != 0)
            TriggerPulse();

        if (PulseEnabled != oldPulseEnabled || PulseInterval != oldPulseInterval)
            ConfigurePulseTimer();
    }

    void TriggerPulse()
    {
        isPulsing = false;
        pulseKey++;
        var capturedKey = pulseKey;
        _ = Task.Run(async () =>
        {
            await Task.Yield();
            if (capturedKey != pulseKey) return;
            isPulsing = true;
            await InvokeAsync(StateHasChanged);

            await Task.Delay(PulseSpeed + 100);
            if (capturedKey != pulseKey) return;
            isPulsing = false;
            await InvokeAsync(StateHasChanged);
        });
    }

    void ConfigurePulseTimer()
    {
        StopPulseTimer();

        if (!PulseEnabled || PulseInterval <= TimeSpan.Zero)
            return;

        pulseTimer = new Timer(_ =>
        {
            isPulsing = true;
            InvokeAsync(StateHasChanged);

            Task.Delay(PulseSpeed + 100).ContinueWith(_ =>
            {
                isPulsing = false;
                InvokeAsync(StateHasChanged);
            });
        }, null, PulseInterval, PulseInterval);
    }

    void StopPulseTimer()
    {
        pulseTimer?.Dispose();
        pulseTimer = null;
    }

    public void Dispose()
    {
        StopPulseTimer();
        GC.SuppressFinalize(this);
    }
}
