using System.Globalization;
using Microsoft.AspNetCore.Components;

using Shiny.Blazor.Controls.Theming;

namespace Shiny.Blazor.Controls;

/// <summary>
/// The thin determinate/indeterminate line that runs across the top or bottom of the window while
/// something is loading.
/// </summary>
/// <remarks>
/// Distinct from <see cref="ProgressBar"/>, which is an inline element that fills the slot you gave
/// it in a flex or grid layout. This one is chrome: it pins itself to a viewport edge, sits above the
/// page at its own z-index, and takes itself out of hit-testing so it never eats a click along a
/// whole edge of the window.
/// </remarks>
public partial class ProgressLine
{
    /// <summary>Progress from 0 to 100.</summary>
    [Parameter] public double Value { get; set; }

    [Parameter] public double Minimum { get; set; } = 0;

    [Parameter] public double Maximum { get; set; } = 100;

    [Parameter] public bool IsIndeterminate { get; set; }

    /// <inheritdoc cref="ProgressLinePosition"/>
    [Parameter] public ProgressLinePosition Position { get; set; } = ProgressLinePosition.Top;

    /// <inheritdoc cref="ProgressLineAnchor"/>
    [Parameter] public ProgressLineAnchor Anchor { get; set; } = ProgressLineAnchor.Viewport;

    /// <summary>Fill color. Any CSS color; defaults to the theme primary token.</summary>
    // Mirrored in ProgressLine.razor.css; only a caller-chosen value inlines. See StyleDefaults.
    internal const string DefaultBarColor = "var(--shiny-color-primary, #0055D9)";
    internal const string DefaultGradientStartColor = "var(--shiny-color-primary, #0055D9)";
    internal const string DefaultGradientEndColor = "var(--shiny-color-tertiary, #6A4CAD)";

    [Parameter] public string BarColor { get; set; } = DefaultBarColor;

    /// <summary>
    /// The unfilled remainder. Transparent by default, unlike <see cref="ProgressBar"/> — a rail
    /// spanning the whole window reads as a border that appeared for no reason.
    /// </summary>
    [Parameter] public string TrackColor { get; set; } = "transparent";

    /// <summary>Thickness in px.</summary>
    [Parameter] public double LineHeight { get; set; } = 3;

    /// <summary>Corner radius of the fill. Square by default, so the line meets the window edges.</summary>
    [Parameter] public string CornerRadius { get; set; } = "0";

    [Parameter] public bool UseGradient { get; set; }

    [Parameter] public string GradientStartColor { get; set; } = DefaultGradientStartColor;

    [Parameter] public string GradientEndColor { get; set; } = DefaultGradientEndColor;

    /// <summary>Runs the shimmer sheen along the fill for as long as the line is up.</summary>
    [Parameter] public bool PulseEnabled { get; set; }

    [Parameter] public string PulseColor { get; set; } = "rgba(255,255,255,0.6)";

    [Parameter] public double PulseLength { get; set; } = 0.4;

    [Parameter] public int PulseSpeed { get; set; } = 800;

    /// <summary>
    /// Whether a change to <see cref="Value"/> slides the fill to its new width instead of snapping.
    /// Applies in both directions, so a value that drops drains rather than jumping backwards.
    /// </summary>
    [Parameter] public bool AnimateProgress { get; set; } = true;

    /// <summary>Length of the fill slide in milliseconds. Zero or less snaps.</summary>
    [Parameter] public int ProgressAnimationDuration { get; set; } = 250;

    /// <summary>CSS timing function for the fill slide.</summary>
    [Parameter] public string ProgressAnimationEasing { get; set; } = "cubic-bezier(0.33, 1, 0.68, 1)";

    /// <summary>
    /// Extra distance from the edge, as a CSS length. Stacks on top of the safe-area inset and any
    /// <c>--shiny-progressline-offset</c> an ancestor has set.
    /// </summary>
    [Parameter] public string Offset { get; set; } = "0px";

    /// <summary>
    /// Whether to clear the notch and the home indicator via <c>env(safe-area-inset-*)</c>. Only
    /// meaningful in a viewport-anchored, display-mode-fullscreen context (a PWA, a MAUI
    /// <c>BlazorWebView</c>); a no-op in an ordinary browser tab, where the inset is zero.
    /// </summary>
    [Parameter] public bool RespectSafeArea { get; set; } = true;

    /// <summary>Fades the line out when set false, rather than removing it outright.</summary>
    [Parameter] public bool IsActive { get; set; } = true;

    /// <summary>Length of the <see cref="IsActive"/> fade in milliseconds.</summary>
    [Parameter] public int FadeDuration { get; set; } = 200;

    [Parameter] public string? CssClass { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IDictionary<string, object>? AdditionalAttributes { get; set; }

    double Percentage => this.Maximum > this.Minimum
        ? Math.Clamp((this.Value - this.Minimum) / (this.Maximum - this.Minimum), 0, 1) * 100
        : 0;

    string AriaValueNow => this.IsIndeterminate
        ? string.Empty
        : this.Percentage.ToString("0", CultureInfo.InvariantCulture);

    string PositionClass => this.Position == ProgressLinePosition.Bottom ? "shiny-pl--bottom" : "shiny-pl--top";

    string AnchorClass => this.Anchor == ProgressLineAnchor.Container ? "shiny-pl--container" : "";

    string StateClass => string.Join(' ', this.States());

    IEnumerable<string> States()
    {
        if (this.IsIndeterminate)
            yield return "shiny-pl--indeterminate";

        if (this.PulseEnabled)
            yield return "shiny-pl--pulse";

        if (!this.IsActive)
            yield return "shiny-pl--hiding";
    }

    /// <summary>
    /// Everything the caller splatted except <c>style</c>, which is folded into <see cref="RootStyle"/>
    /// instead.
    /// </summary>
    /// <remarks>
    /// Splatting after a literal <c>style</c> attribute replaces it outright rather than merging, and
    /// this component's style attribute is where every custom property it needs is declared — so a
    /// caller passing <c>style="..."</c> would silently strip the colors, the height and the offset.
    /// </remarks>
    IDictionary<string, object>? PassThroughAttributes
    {
        get
        {
            if (this.AdditionalAttributes is null || !this.AdditionalAttributes.ContainsKey("style"))
                return this.AdditionalAttributes;

            return this.AdditionalAttributes
                .Where(kv => !string.Equals(kv.Key, "style", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
        }
    }

    string RootStyle
    {
        get
        {
            // A gradient is never the stylesheet's own value, so it always writes; a plain bar colour
            // only does when it is not the one --pl-bar already falls back to.
            var fill = this.UseGradient
                ? $"--pl-bar: linear-gradient(to right, {this.GradientStartColor}, {this.GradientEndColor}); "
                : StyleDefaults.Override("--pl-bar", this.BarColor, DefaultBarColor);

            var duration = this.AnimateProgress && this.ProgressAnimationDuration > 0
                ? this.ProgressAnimationDuration
                : 0;

            var viewport = this.Anchor == ProgressLineAnchor.Viewport && this.RespectSafeArea;
            var safeTop = viewport ? "env(safe-area-inset-top, 0px)" : "0px";
            var safeBottom = viewport ? "env(safe-area-inset-bottom, 0px)" : "0px";

            var pulseLength = Math.Clamp(this.PulseLength, 0.05, 1.0) * 100;

            var style =
                $"--pl-progress: {this.Percentage.ToString("0.###", CultureInfo.InvariantCulture)}%; " +
                fill +
                $"--pl-track: {this.TrackColor}; " +
                $"--pl-height: {this.LineHeight.ToString(CultureInfo.InvariantCulture)}px; " +
                $"--pl-radius: {this.CornerRadius}; " +
                $"--pl-offset: {this.Offset}; " +
                $"--pl-safe-top: {safeTop}; " +
                $"--pl-safe-bottom: {safeBottom}; " +
                $"--pl-duration: {duration}ms; " +
                $"--pl-easing: {this.ProgressAnimationEasing}; " +
                $"--pl-fade: {this.FadeDuration}ms; " +
                $"--pl-pulse-color: {this.PulseColor}; " +
                $"--pl-pulse-length: {pulseLength.ToString("0.###", CultureInfo.InvariantCulture)}%; " +
                $"--pl-pulse-speed: {this.PulseSpeed}ms;";

            if (this.AdditionalAttributes?.TryGetValue("style", out var caller) == true && caller is not null)
                style = $"{style} {caller}";

            return style;
        }
    }
}
