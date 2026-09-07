using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Shiny.Blazor.Controls;

public partial class RangeSlider : IAsyncDisposable
{
    [Inject] IJSRuntime JS { get; set; } = default!;

    ElementReference trackRef;
    IJSObjectReference? module;
    DotNetObjectReference<RangeSlider>? selfRef;

    // Parameters
    [Parameter] public double LowerValue { get; set; } = 0;
    [Parameter] public EventCallback<double> LowerValueChanged { get; set; }
    [Parameter] public double UpperValue { get; set; } = 100;
    [Parameter] public EventCallback<double> UpperValueChanged { get; set; }
    [Parameter] public EventCallback<(double Lower, double Upper)> RangeChanged { get; set; }
    [Parameter] public double Minimum { get; set; } = 0;
    [Parameter] public double Maximum { get; set; } = 100;
    [Parameter] public double Step { get; set; } = 1;
    /// <summary>Minimum distance the thumbs may be apart (hard stop). 0 disables the constraint.</summary>
    [Parameter] public double MinimumRange { get; set; } = 0;
    /// <summary>Maximum distance the thumbs may be apart. Dragging one thumb past this pushes the other. 0 disables the constraint.</summary>
    [Parameter] public double MaximumRange { get; set; } = 0;
    [Parameter] public string ColdColor { get; set; } = "#3B82F6";
    [Parameter] public string HotColor { get; set; } = "#EF4444";
    [Parameter] public double TrackHeight { get; set; } = 8;
    [Parameter] public double ThumbSize { get; set; } = 24;
    [Parameter] public string ThumbColor { get; set; } = "var(--shiny-color-surface, #FFFFFF)";
    /// <summary>
    /// Thumb width in px. The default, <c>-1</c>, keeps the thumbs square at <see cref="ThumbSize"/>. Set
    /// it when a thumb template puts content in them that is not square.
    /// </summary>
    [Parameter] public double ThumbWidth { get; set; } = -1;
    /// <summary>Thumb height in px. The default, <c>-1</c>, keeps the thumbs square at <see cref="ThumbSize"/>.</summary>
    [Parameter] public double ThumbHeight { get; set; } = -1;
    /// <summary>
    /// Thumb corner radius, as any CSS length. The default, <c>null</c>, keeps the thumbs fully rounded —
    /// a circle while they are square, a pill once they are not.
    /// </summary>
    [Parameter] public string? ThumbCornerRadius { get; set; }
    /// <summary>Inset, as any CSS padding value, between a thumb's border and its template content.</summary>
    [Parameter] public string? ThumbPadding { get; set; }
    /// <summary>
    /// Content drawn inside both thumbs — an icon, a glyph, the value itself. The fragment is handed that
    /// thumb's own value. <see cref="LowerThumbTemplate"/> and <see cref="UpperThumbTemplate"/> override
    /// it per thumb. The thumbs do not grow to fit: size them with <see cref="ThumbSize"/>, or
    /// <see cref="ThumbWidth"/>/<see cref="ThumbHeight"/>.
    /// </summary>
    [Parameter] public RenderFragment<double>? ThumbTemplate { get; set; }
    /// <summary>Content for the lower thumb only. Falls back to <see cref="ThumbTemplate"/> when null.</summary>
    [Parameter] public RenderFragment<double>? LowerThumbTemplate { get; set; }
    /// <summary>Content for the upper thumb only. Falls back to <see cref="ThumbTemplate"/> when null.</summary>
    [Parameter] public RenderFragment<double>? UpperThumbTemplate { get; set; }
    /// <summary>Thumb ring width in px. The default, <c>-1</c>, follows the theme border scale.</summary>
    [Parameter] public double ThumbBorderWidth { get; set; } = -1;
    [Parameter] public string CornerRadius { get; set; } = "var(--shiny-shape-corner-extra-small, 4px)";
    [Parameter] public bool ShowTooltip { get; set; } = true;
    [Parameter] public string TooltipBackgroundColor { get; set; } = "var(--shiny-color-inverse-surface, #1F2937)";
    [Parameter] public string TooltipTextColor { get; set; } = "var(--shiny-color-inverse-on-surface, #FFFFFF)";
    /// <summary>Tooltip label size in px. The default, <c>-1</c>, follows the theme type scale.</summary>
    [Parameter] public double TooltipFontSize { get; set; } = -1;
    [Parameter] public string? ValueFormat { get; set; }
    [Parameter] public RenderFragment<double>? TooltipTemplate { get; set; }
    [Parameter] public bool IsEnabled { get; set; } = true;
    [Parameter] public string? CssClass { get; set; }
    [Parameter(CaptureUnmatchedValues = true)]
    public IDictionary<string, object>? AdditionalAttributes { get; set; }

    double Span => Maximum > Minimum ? Maximum - Minimum : 1;
    double LowerPercentage => Math.Clamp((LowerValue - Minimum) / Span, 0, 1) * 100;
    double UpperPercentage => Math.Clamp((UpperValue - Minimum) / Span, 0, 1) * 100;

    string LowerColor => BlendColors(ColdColor, HotColor, LowerPercentage / 100.0);
    string UpperColor => BlendColors(ColdColor, HotColor, UpperPercentage / 100.0);

    /// <summary>
    /// The thumb box. It is square at <see cref="ThumbSize"/> until <see cref="ThumbWidth"/> or
    /// <see cref="ThumbHeight"/> says otherwise, which is what lets a thumb template hold something
    /// wider than it is tall.
    /// </summary>
    internal double ResolvedThumbWidth => ThumbWidth > 0 ? ThumbWidth : ThumbSize;

    internal double ResolvedThumbHeight => ThumbHeight > 0 ? ThumbHeight : ThumbSize;

    RenderFragment<double>? LowerThumbContent => LowerThumbTemplate ?? ThumbTemplate;

    RenderFragment<double>? UpperThumbContent => UpperThumbTemplate ?? ThumbTemplate;

    /// <summary>
    /// The half of the thumb that hangs past the track. The stylesheet turns it into padding at both ends
    /// of the control, so a tall custom thumb is not clipped and does not spill onto what follows it.
    /// </summary>
    internal string RootStyle
    {
        get
        {
            var overhang = Math.Max(0, (ResolvedThumbHeight - TrackHeight) / 2);
            var style = $"--shiny-rs-thumb-overhang: {N(overhang)}px;";

            if (!IsEnabled)
                style += " opacity: 0.5; pointer-events: none;";

            return style;
        }
    }

    string TrackStyle => $"height: {N(TrackHeight)}px; border-radius: {CornerRadius};";

    string TrackFillStyle
    {
        get
        {
            var left = Math.Min(LowerPercentage, UpperPercentage);
            var width = Math.Abs(UpperPercentage - LowerPercentage);
            return $"left: {N(left)}%; width: {N(width)}%; height: 100%; border-radius: {CornerRadius}; " +
                   $"background: linear-gradient(to right, {LowerColor}, {UpperColor});";
        }
    }

    internal string ThumbStyle(double percentage, string color)
    {
        var border = ThumbBorderWidth >= 0 ? $"{N(ThumbBorderWidth)}px" : "var(--shiny-border-medium, 2px)";

        // Fully rounded by default: a circle while the thumb is square, a pill once it is not. The radius
        // is written here rather than in the stylesheet because an inline property wins outright, which
        // would leave a rule there dead.
        var radius = string.IsNullOrWhiteSpace(ThumbCornerRadius)
            ? $"{N(Math.Min(ResolvedThumbWidth, ResolvedThumbHeight) / 2)}px"
            : ThumbCornerRadius;

        var style = $"left: {N(percentage)}%; width: {N(ResolvedThumbWidth)}px; height: {N(ResolvedThumbHeight)}px;"
            + $" border: {border} solid {color}; border-radius: {radius}; background: {ThumbColor};";

        if (!string.IsNullOrWhiteSpace(ThumbPadding))
            style += $" padding: {ThumbPadding};";

        return style;
    }

    // Shift transform from 0% at left edge to -100% at right edge to keep the tooltip on-track.
    string TooltipTransform(double percentage) => $"transform: translateX({N(-percentage)}%);";

    string TooltipBadgeStyle => $"background: {TooltipBackgroundColor}; color: {TooltipTextColor}; font-size: {(TooltipFontSize >= 0 ? $"{N(TooltipFontSize)}px" : "var(--shiny-type-body-small-size, 12px)")};";

    string TooltipPointerStyle => $"border-top-color: {TooltipBackgroundColor};";

    /// <summary>CSS never reads the current culture, so every number written into a style has to be invariant.</summary>
    static string N(double value)
    {
        // Negative zero formats as "-0", which is valid but reads like a bug in the rendered markup.
        if (value == 0)
            value = 0;

        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }


    string FormatValue(double val)
    {
        if (!string.IsNullOrEmpty(ValueFormat))
            return val.ToString(ValueFormat);
        return val % 1 == 0 ? val.ToString("0") : val.ToString("0.#");
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            module = await JS.InvokeAsync<IJSObjectReference>(
                "import", "./_content/Shiny.Blazor.Controls/rangeslider.js");
            selfRef = DotNetObjectReference.Create(this);
            await module.InvokeVoidAsync("init", trackRef, selfRef);
        }
    }

    async Task OnTrackClick(MouseEventArgs e)
    {
        if (!IsEnabled || module is null) return;

        var percent = await module.InvokeAsync<double>("getClickPercent", trackRef, e.ClientX);
        var value = PercentToValue(percent);
        // Move whichever thumb is nearer the click.
        var toUpper = Math.Abs(value - UpperValue) < Math.Abs(value - LowerValue);
        await ApplyPercentToThumb(percent, toUpper);
    }

    [JSInvokable]
    public Task OnDragUpdate(double percent, bool isUpper) => ApplyPercentToThumb(percent, isUpper);

    double PercentToValue(double percent)
    {
        percent = Math.Clamp(percent, 0, 1);
        var raw = Minimum + (percent * (Maximum - Minimum));
        if (Step > 0)
            raw = Math.Round(raw / Step) * Step;
        return Math.Clamp(raw, Minimum, Maximum);
    }

    async Task ApplyPercentToThumb(double percent, bool isUpper)
    {
        var value = PercentToValue(percent);
        var lower = LowerValue;
        var upper = UpperValue;

        if (isUpper)
        {
            if (MinimumRange > 0)
                value = Math.Max(value, lower + MinimumRange);
            value = Math.Clamp(value, Minimum, Maximum);
            if (MaximumRange > 0 && value - lower > MaximumRange)
                lower = Math.Max(Minimum, value - MaximumRange);
            upper = value;
        }
        else
        {
            if (MinimumRange > 0)
                value = Math.Min(value, upper - MinimumRange);
            value = Math.Clamp(value, Minimum, Maximum);
            if (MaximumRange > 0 && upper - value > MaximumRange)
                upper = Math.Min(Maximum, value + MaximumRange);
            lower = value;
        }

        var lowerChanged = Math.Abs(lower - LowerValue) > double.Epsilon;
        var upperChanged = Math.Abs(upper - UpperValue) > double.Epsilon;
        if (!lowerChanged && !upperChanged) return;

        LowerValue = lower;
        UpperValue = upper;

        if (lowerChanged) await LowerValueChanged.InvokeAsync(lower);
        if (upperChanged) await UpperValueChanged.InvokeAsync(upper);
        await RangeChanged.InvokeAsync((lower, upper));
        StateHasChanged();
    }

    static string BlendColors(string color1, string color2, double ratio)
    {
        ratio = Math.Clamp(ratio, 0, 1);
        var (r1, g1, b1) = ParseHex(color1);
        var (r2, g2, b2) = ParseHex(color2);

        var r = (int)(r1 + (r2 - r1) * ratio);
        var g = (int)(g1 + (g2 - g1) * ratio);
        var b = (int)(b1 + (b2 - b1) * ratio);

        return $"#{r:X2}{g:X2}{b:X2}";
    }

    static (int r, int g, int b) ParseHex(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 3)
            hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";

        return (
            Convert.ToInt32(hex[..2], 16),
            Convert.ToInt32(hex[2..4], 16),
            Convert.ToInt32(hex[4..6], 16)
        );
    }

    public async ValueTask DisposeAsync()
    {
        if (module is not null)
        {
            try { await module.InvokeVoidAsync("dispose", trackRef); } catch { }
            await module.DisposeAsync();
        }
        selfRef?.Dispose();
    }
}
