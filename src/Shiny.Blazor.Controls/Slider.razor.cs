using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Shiny.Blazor.Controls;

public partial class Slider : IAsyncDisposable, ISliderMarkHost
{
    // Gap between the track and the tooltip, and between the track and the mark captions.
    const double TooltipGap = 6;
    const double MarkLabelGap = 4;

    [Inject] IJSRuntime JS { get; set; } = default!;

    readonly List<SliderMark> marks = new();

    ElementReference trackRef;
    IJSObjectReference? module;
    DotNetObjectReference<Slider>? selfRef;
    SliderOrientation initializedOrientation;
    bool hasRendered;
    bool marksDirty;

    // Parameters
    [Parameter] public double Value { get; set; }
    [Parameter] public EventCallback<double> ValueChanged { get; set; }
    [Parameter] public double Minimum { get; set; } = 0;
    [Parameter] public double Maximum { get; set; } = 100;
    [Parameter] public double Step { get; set; } = 1;
    [Parameter] public string ColdColor { get; set; } = "#3B82F6";
    [Parameter] public string HotColor { get; set; } = "#EF4444";
    [Parameter] public double TrackHeight { get; set; } = 8;
    [Parameter] public double ThumbSize { get; set; } = 24;
    [Parameter] public string ThumbColor { get; set; } = "var(--shiny-color-surface, #FFFFFF)";
    /// <summary>
    /// Thumb width in px. The default, <c>-1</c>, keeps the thumb square at <see cref="ThumbSize"/>. Set
    /// it when <see cref="ThumbTemplate"/> puts content in the thumb that is not square.
    /// </summary>
    [Parameter] public double ThumbWidth { get; set; } = -1;
    /// <summary>Thumb height in px. The default, <c>-1</c>, keeps the thumb square at <see cref="ThumbSize"/>.</summary>
    [Parameter] public double ThumbHeight { get; set; } = -1;
    /// <summary>
    /// Thumb corner radius, as any CSS length. The default, <c>null</c>, keeps the thumb fully rounded —
    /// a circle while it is square, a pill once it is not.
    /// </summary>
    [Parameter] public string? ThumbCornerRadius { get; set; }
    /// <summary>Inset, as any CSS padding value, between the thumb's border and its template content.</summary>
    [Parameter] public string? ThumbPadding { get; set; }
    /// <summary>
    /// Content drawn inside the thumb — an icon, a glyph, the value itself. The fragment is handed the
    /// current <see cref="Value"/>. The thumb does not grow to fit: size it with <see cref="ThumbSize"/>,
    /// or <see cref="ThumbWidth"/>/<see cref="ThumbHeight"/>.
    /// </summary>
    [Parameter] public RenderFragment<double>? ThumbTemplate { get; set; }
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

    /// <summary>Which way the slider runs. Vertical puts <see cref="Minimum"/> at the bottom.</summary>
    [Parameter] public SliderOrientation Orientation { get; set; } = SliderOrientation.Horizontal;

    /// <summary>
    /// How long the track is, in px, when <see cref="Orientation"/> is vertical. A vertical slider has no
    /// width to stretch into, so it has to be told how tall to be.
    /// </summary>
    [Parameter] public double VerticalLength { get; set; } = 220;

    /// <summary>The <see cref="SliderMark"/>s, as markup.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>
    /// Whether the thumb comes to rest on the nearest mark — what makes a mark a stop point rather than a
    /// label. Set it false to keep the marks purely as reference points and let <see cref="Step"/> govern
    /// the value. Has no effect while there are no marks.
    /// </summary>
    [Parameter] public bool SnapToMarks { get; set; } = true;

    /// <summary>The shape every mark uses unless it sets <see cref="SliderMark.Shape"/> itself.</summary>
    [Parameter] public SliderMarkShape MarkShape { get; set; } = SliderMarkShape.Dot;

    /// <summary>Dot diameter, or tick thickness, in px, for marks that do not set <see cref="SliderMark.Size"/>.</summary>
    [Parameter] public double MarkSize { get; set; } = 10;

    /// <summary>Fill for marks that do not set <see cref="SliderMark.Color"/>.</summary>
    [Parameter] public string MarkColor { get; set; } = "var(--shiny-color-surface, #FFFFFF)";

    /// <summary>Text colour for marks that do not set <see cref="SliderMark.TextColor"/>.</summary>
    [Parameter] public string MarkTextColor { get; set; } = "var(--shiny-color-on-surface-variant, #4B5563)";

    /// <summary>Mark label size in px.</summary>
    [Parameter] public double MarkFontSize { get; set; } = 11;

    /// <summary>Whether dot and tick marks show their <see cref="SliderMark.Text"/> as a caption. A bubble always shows its text.</summary>
    [Parameter] public bool ShowMarkLabels { get; set; } = true;

    [Parameter(CaptureUnmatchedValues = true)]
    public IDictionary<string, object>? AdditionalAttributes { get; set; }


    bool IsVertical => this.Orientation == SliderOrientation.Vertical;

    /// <summary>The marks the slider draws, and the only ones it will snap to.</summary>
    internal IEnumerable<SliderMark> VisibleMarks => this.marks.Where(m => m.IsVisible);

    double Percentage => Maximum > Minimum
        ? Math.Clamp((Value - Minimum) / (Maximum - Minimum), 0, 1) * 100
        : 0;

    double PercentFor(SliderMark mark) => Maximum > Minimum
        ? Math.Clamp((mark.Value - Minimum) / (Maximum - Minimum), 0, 1) * 100
        : 0;

    string BlendedColor => BlendColors(ColdColor, HotColor, Percentage / 100.0);

    internal string OrientationClass => IsVertical ? "shiny-gs-vertical" : "shiny-gs-horizontal";

    double ResolvedTooltipFontSize => TooltipFontSize >= 0 ? TooltipFontSize : 12;

    /// <summary>
    /// The thumb box. It is square at <see cref="ThumbSize"/> until <see cref="ThumbWidth"/> or
    /// <see cref="ThumbHeight"/> says otherwise, which is what lets a <see cref="ThumbTemplate"/> hold
    /// something wider than it is tall.
    /// </summary>
    internal double ResolvedThumbWidth => ThumbWidth > 0 ? ThumbWidth : ThumbSize;

    internal double ResolvedThumbHeight => ThumbHeight > 0 ? ThumbHeight : ThumbSize;

    /// <summary>The thumb's extent across the track — what every band offset has to clear.</summary>
    double ThumbAcross => IsVertical ? ResolvedThumbWidth : ResolvedThumbHeight;


    // ---------------------------------------------------------------------------------------------
    // Styles
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The bands the slider reserves around the track, handed to the stylesheet as custom properties so
    /// a rule there still owns the padding — an inline property the CSS also sets would be dead on arrival.
    /// </summary>
    internal string RootStyle
    {
        get
        {
            // The thumb is wider than the track and overhangs it on both sides, so everything the
            // stylesheet offsets from the track edge has to clear it or the thumb sits on the labels.
            var overhang = Math.Max(0, (ThumbAcross - TrackHeight) / 2);

            var tooltipBand = ShowTooltip
                ? (IsVertical ? EstimatedTooltipWidth() : ResolvedTooltipFontSize + 14) + TooltipGap + overhang
                : overhang;

            // Even with nothing to label, the band still has to hold the half of the thumb hanging past
            // the track — otherwise a tall custom thumb spills onto whatever is laid out next.
            var labelBand = HasCaptions()
                ? (IsVertical ? EstimatedCaptionWidth() : EstimatedCaptionHeight()) + MarkLabelGap + overhang
                : overhang;

            var style = $"--shiny-gs-tooltip-band: {N(tooltipBand)}px; --shiny-gs-label-band: {N(labelBand)}px;"
                + $" --shiny-gs-thumb-overhang: {N(overhang)}px;";

            if (!IsEnabled)
                style += " opacity: 0.5; pointer-events: none;";

            return style;
        }
    }

    internal string TrackStyle => IsVertical
        ? $"width: {N(TrackHeight)}px; height: {N(VerticalLength)}px; border-radius: {CornerRadius}; background: {BlendedColor};"
        : $"height: {N(TrackHeight)}px; border-radius: {CornerRadius}; background: {BlendedColor};";

    internal string ThumbStyle
    {
        get
        {
            var border = ThumbBorderWidth >= 0 ? $"{N(ThumbBorderWidth)}px" : "var(--shiny-border-medium, 2px)";

            // Fully rounded by default: a circle while the thumb is square, a pill once it is not. The
            // radius is written here rather than in the stylesheet because an inline property wins
            // outright, which would leave a rule there dead.
            var radius = string.IsNullOrWhiteSpace(ThumbCornerRadius)
                ? $"{N(Math.Min(ResolvedThumbWidth, ResolvedThumbHeight) / 2)}px"
                : ThumbCornerRadius;

            var common = $"width: {N(ResolvedThumbWidth)}px; height: {N(ResolvedThumbHeight)}px;"
                + $" border: {border} solid {BlendedColor}; border-radius: {radius}; background: {ThumbColor};";

            if (!string.IsNullOrWhiteSpace(ThumbPadding))
                common += $" padding: {ThumbPadding};";

            return IsVertical
                ? $"bottom: {N(Percentage)}%; {common}"
                : $"left: {N(Percentage)}%; {common}";
        }
    }

    internal string TooltipStyle
    {
        get
        {
            if (IsVertical)
                return $"bottom: {N(Percentage)}%;";

            // Shift the transform from -50% toward 0% at the left edge and -100% at the right so the badge
            // never overflows the track.
            var translatePct = -Percentage;
            return $"left: {N(Percentage)}%; transform: translateX({translatePct.ToString("0.#", CultureInfo.InvariantCulture)}%);";
        }
    }

    string TooltipBadgeStyle
    {
        get
        {
            var size = TooltipFontSize >= 0 ? $"{N(TooltipFontSize)}px" : "var(--shiny-type-body-small-size, 12px)";
            return $"background: {TooltipBackgroundColor}; color: {TooltipTextColor}; font-size: {size};";
        }
    }

    string TooltipPointerStyle => IsVertical
        ? $"border-left-color: {TooltipBackgroundColor};"
        : $"border-top-color: {TooltipBackgroundColor};";

    internal string MarkerStyle(SliderMark mark, SliderMarkShape shape)
    {
        // No edge clamping: the marker has to line up with the thumb, which itself overhangs the ends.
        var pct = N(PercentFor(mark));
        var color = mark.Color ?? MarkColor;
        var size = mark.Size > 0 ? mark.Size : MarkSize;

        var box = shape == SliderMarkShape.Line
            ? (IsVertical
                ? $"width: {N(TrackHeight + 8)}px; height: {N(Math.Max(2, size / 3))}px;"
                : $"width: {N(Math.Max(2, size / 3))}px; height: {N(TrackHeight + 8)}px;")
            : $"width: {N(size)}px; height: {N(size)}px; border-radius: 50%;";

        return IsVertical
            ? $"bottom: {pct}%; {box} background: {color};"
            : $"left: {pct}%; {box} background: {color};";
    }

    internal string MarkLabelStyle(SliderMark mark, SliderMarkShape shape)
    {
        var percent = PercentFor(mark);
        var pct = N(percent);

        var paint = $"color: {mark.TextColor ?? MarkTextColor}; font-size: {N(MarkFontSize)}px;";
        if (shape == SliderMarkShape.Bubble)
            paint += $" background: {mark.Color ?? MarkColor};";

        // Slide the transform from 0% at the low end to -100% at the high one, the way the tooltip does,
        // so the first and last label stay inside the track instead of hanging off it.
        return IsVertical
            ? $"bottom: {pct}%; transform: translateY({N(percent)}%); {paint}"
            : $"left: {pct}%; transform: translateX({N(-percent)}%); {paint}";
    }


    /// <summary>
    /// A cheap width for a run of text, in px. The bands are reserved before anything has been measured,
    /// and reading a laid-out size back into the padding that produced it is how a layout starts
    /// oscillating — so the bands are always estimated.
    /// </summary>
    static double EstimateTextWidth(string? text, double fontSize)
        => string.IsNullOrEmpty(text) ? 0 : (text!.Length * fontSize * 0.58) + 2;

    double EstimatedTooltipWidth() => EstimateTextWidth(FormatValue(Value), ResolvedTooltipFontSize) + 20;

    double EstimatedCaptionWidth()
    {
        double widest = 0;
        foreach (var mark in VisibleMarks)
        {
            var padding = (mark.Shape ?? MarkShape) == SliderMarkShape.Bubble ? 16 : 0;
            widest = Math.Max(widest, EstimateTextWidth(mark.Text, MarkFontSize) + padding);
        }
        return widest;
    }

    /// <summary>How tall the label band is: a bubble is the same caption with a pill around it.</summary>
    double EstimatedCaptionHeight()
        => MarkFontSize + (VisibleMarks.Any(m => (m.Shape ?? MarkShape) == SliderMarkShape.Bubble) ? 12 : 6);

    bool HasCaptions()
        => ShowMarkLabels && VisibleMarks.Any(m => !string.IsNullOrWhiteSpace(m.Text));


    string FormatValue(double val)
    {
        if (!string.IsNullOrEmpty(ValueFormat))
            return val.ToString(ValueFormat);
        return val % 1 == 0 ? val.ToString("0") : val.ToString("0.#");
    }

    /// <summary>CSS never reads the current culture, so every number written into a style has to be invariant.</summary>
    static string N(double value)
    {
        // Negative zero formats as "-0", which is valid but reads like a bug in the rendered markup.
        if (value == 0)
            value = 0;

        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }


    // ---------------------------------------------------------------------------------------------
    // Mark registration
    // ---------------------------------------------------------------------------------------------

    void ISliderMarkHost.RegisterMark(SliderMark mark)
    {
        if (!this.marks.Contains(mark))
            this.marks.Add(mark);

        this.Redraw();
    }

    void ISliderMarkHost.UnregisterMark(SliderMark mark)
    {
        this.marks.Remove(mark);
        this.Redraw();
    }

    void ISliderMarkHost.NotifyMarkChanged(SliderMark mark) => this.Redraw();


    /// <summary>
    /// Asks for another render, tolerating not having a handle yet. Marks register while the slider is
    /// mid-render — the parent's tree is built before any child is initialized — so the first batch
    /// arrives before there is anything to ask. Remember it and redraw once there is, or the marks never
    /// appear at all.
    /// </summary>
    void Redraw()
    {
        if (this.hasRendered)
            this.StateHasChanged();
        else
            this.marksDirty = true;
    }


    // ---------------------------------------------------------------------------------------------
    // Interaction
    // ---------------------------------------------------------------------------------------------

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            module = await JS.InvokeAsync<IJSObjectReference>(
                "import", "./_content/Shiny.Blazor.Controls/slider.js");
            selfRef = DotNetObjectReference.Create(this);
            initializedOrientation = Orientation;
            await module.InvokeVoidAsync("init", trackRef, selfRef, IsVertical);

            hasRendered = true;
            if (marksDirty)
            {
                marksDirty = false;
                StateHasChanged();
            }
        }
        else if (module is not null && initializedOrientation != Orientation)
        {
            // The drag maths depends on the axis, so the handler has to be told when it flips.
            initializedOrientation = Orientation;
            await module.InvokeVoidAsync("setOrientation", trackRef, IsVertical);
        }
    }

    async Task OnTrackClick(MouseEventArgs e)
    {
        if (!IsEnabled || module is null) return;

        var percent = await module.InvokeAsync<double>("getClickPercent", trackRef, e.ClientX, e.ClientY, IsVertical);
        await SetValueFromPercent(percent);
    }

    void OnThumbPointerDown(PointerEventArgs e)
    {
        // Drag is handled via JS
    }

    [JSInvokable]
    public async Task OnDragUpdate(double percent)
    {
        await SetValueFromPercent(percent);
    }

    async Task SetValueFromPercent(double percent)
    {
        percent = Math.Clamp(percent, 0, 1);
        var rawValue = Minimum + (percent * (Maximum - Minimum));

        var snapped = SnapToNearestMark(rawValue);
        if (snapped is double markValue)
        {
            rawValue = markValue;
        }
        else if (Step > 0)
        {
            rawValue = Math.Round(rawValue / Step) * Step;
        }

        rawValue = Math.Clamp(rawValue, Minimum, Maximum);

        if (Math.Abs(rawValue - Value) > double.Epsilon)
        {
            Value = rawValue;
            await ValueChanged.InvokeAsync(Value);
            Redraw();
        }
    }

    /// <summary>The mark the value comes to rest on, or null when marks are not snap targets.</summary>
    double? SnapToNearestMark(double rawValue)
    {
        if (!SnapToMarks)
            return null;

        double? best = null;
        var bestDistance = double.MaxValue;

        foreach (var mark in VisibleMarks)
        {
            var distance = Math.Abs(mark.Value - rawValue);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = mark.Value;
            }
        }
        return best;
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
