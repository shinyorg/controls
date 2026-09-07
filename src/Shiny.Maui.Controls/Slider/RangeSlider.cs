using Microsoft.Maui.Layouts;
using Shiny.Maui.Controls.Themes;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls;

/// <summary>
/// A two-thumb range slider that selects a lower/upper value pair. Shares the gradient track,
/// blended thumb border, and floating tooltip styling of <see cref="Slider"/>, and adds
/// <see cref="MinimumRange"/> / <see cref="MaximumRange"/> gap constraints between the thumbs.
/// </summary>
public partial class RangeSlider : ContentView
{
    readonly BoxView trackBackground;
    readonly BoxView trackFill;
    readonly Border lowerThumb;
    readonly Border upperThumb;
    readonly Border lowerTooltipBadge;
    readonly Border upperTooltipBadge;
    readonly Label lowerTooltipLabel;
    readonly Label upperTooltipLabel;
    readonly ContentView lowerTooltipContainer;
    readonly ContentView upperTooltipContainer;
    readonly Grid tooltipRow;
    readonly AbsoluteLayout trackLayout;
    readonly Grid rootGrid;

    View? lowerThumbContent;
    View? upperThumbContent;

    double trackWidth;
    bool isDragging;
    bool draggingUpper;
    double dragStartThumbX;

    public RangeSlider()
    {
        lowerTooltipLabel = CreateTooltipLabel();
        upperTooltipLabel = CreateTooltipLabel();

        lowerTooltipBadge = CreateTooltipBadge(lowerTooltipLabel);
        upperTooltipBadge = CreateTooltipBadge(upperTooltipLabel);

        lowerTooltipContainer = new ContentView { Content = lowerTooltipBadge, HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.End };
        upperTooltipContainer = new ContentView { Content = upperTooltipBadge, HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.End };

        tooltipRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        tooltipRow.Add(lowerTooltipContainer);
        tooltipRow.Add(upperTooltipContainer);

        // Track background (inactive, neutral)
        trackBackground = new BoxView
        {
            HeightRequest = 8,
            CornerRadius = 4,
            VerticalOptions = LayoutOptions.Center
        };
        trackBackground.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceVariant);
        // Color is bound alongside BackgroundColor because the macOS/AppKit BoxView handler paints
        // from Color only and ignores the background brush, which left the track invisible there.
        trackBackground.SetDynamicResource(BoxView.ColorProperty, ShinyThemeKeys.Color.SurfaceVariant);

        // Active fill between the two thumbs (gradient)
        trackFill = new BoxView
        {
            HeightRequest = 8,
            CornerRadius = 4,
            VerticalOptions = LayoutOptions.Center
        };

        lowerThumb = CreateThumb();
        upperThumb = CreateThumb();

        trackLayout = new AbsoluteLayout
        {
            HeightRequest = TrackBandHeight,
            VerticalOptions = LayoutOptions.Center
        };

        AbsoluteLayout.SetLayoutBounds(trackBackground, new Rect(0, 0.5, 1, 8));
        AbsoluteLayout.SetLayoutFlags(trackBackground, AbsoluteLayoutFlags.PositionProportional | AbsoluteLayoutFlags.WidthProportional);

        AbsoluteLayout.SetLayoutBounds(trackFill, new Rect(0, 0.5, 0, 8));
        AbsoluteLayout.SetLayoutFlags(trackFill, AbsoluteLayoutFlags.YProportional);

        AbsoluteLayout.SetLayoutBounds(lowerThumb, new Rect(0, 0.5, 24, 24));
        AbsoluteLayout.SetLayoutFlags(lowerThumb, AbsoluteLayoutFlags.YProportional);

        AbsoluteLayout.SetLayoutBounds(upperThumb, new Rect(0, 0.5, 24, 24));
        AbsoluteLayout.SetLayoutFlags(upperThumb, AbsoluteLayoutFlags.YProportional);

        trackLayout.Children.Add(trackBackground);
        trackLayout.Children.Add(trackFill);
        trackLayout.Children.Add(lowerThumb);
        trackLayout.Children.Add(upperThumb);

        rootGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            },
            RowSpacing = 0
        };
        rootGrid.Add(tooltipRow, 0, 0);
        rootGrid.Add(trackLayout, 0, 1);

        Content = rootGrid;

        // Per-thumb drag
        AddThumbPan(lowerThumb, isUpper: false);
        AddThumbPan(upperThumb, isUpper: true);

        // Tap anywhere on the track moves the nearest thumb
        var tapGesture = new TapGestureRecognizer();
        tapGesture.Tapped += OnTrackTapped;
        trackLayout.GestureRecognizers.Add(tapGesture);

        UpdateVisuals();

        // Last line: replays any styled property that was applied before the
        // children existed. See StyleGuard.
        StyleGuard.MarkReady(this, typeof(RangeSlider));
    }

    /// <summary>
    /// The thumb box. It is square at <see cref="ThumbSize"/> until <see cref="ThumbWidth"/> or
    /// <see cref="ThumbHeight"/> says otherwise, which is what lets a thumb template hold something
    /// wider than it is tall.
    /// </summary>
    internal double ResolvedThumbWidth => this.ThumbWidth > 0 ? this.ThumbWidth : this.ThumbSize;

    internal double ResolvedThumbHeight => this.ThumbHeight > 0 ? this.ThumbHeight : this.ThumbSize;

    double ResolvedThumbCornerRadius => this.ThumbCornerRadius >= 0
        ? this.ThumbCornerRadius
        : Math.Min(this.ResolvedThumbWidth, this.ResolvedThumbHeight) / 2;

    /// <summary>How tall the track row has to be. A thumb bigger than the default grows it.</summary>
    double TrackBandHeight => Math.Max(32, Math.Max(this.ResolvedThumbHeight, this.TrackHeight));


    /// <summary>The row height, then the positions. Anything that changes how much room a thumb needs goes through here.</summary>
    void Refresh()
    {
        this.trackLayout.HeightRequest = this.TrackBandHeight;
        this.UpdateVisuals();
    }


    Label CreateTooltipLabel()
    {
        var label = new Label
        {
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            FontAttributes = FontAttributes.Bold
        }.WithFontSize(ShinyThemeKeys.Type.BodySmallSize);
        label.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
        return label;
    }

    Border CreateTooltipBadge(View content)
    {
        var badge = new Border
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerExtraSmallRadius),
            Stroke = Colors.Transparent,
            Padding = new Thickness(10, 4),
            Content = content,
            HorizontalOptions = LayoutOptions.Start
        };
        badge.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceVariant);
        return badge;
    }

    Border CreateThumb()
    {
        var thumb = new Border
        {
            WidthRequest = 24,
            HeightRequest = 24,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerMediumRadius),
            Stroke = ColdColor,
            Shadow = Slider.CreateThumbShadow(),
            Padding = 0,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center
        }.WithStrokeThickness(ShinyThemeKeys.Border.Thin);
        thumb.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.OnPrimary);
        return thumb;
    }

    void AddThumbPan(Border thumb, bool isUpper)
    {
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += (s, e) => OnThumbPan(e, isUpper);
        thumb.GestureRecognizers.Add(pan);
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width > 0)
        {
            trackWidth = width;
            UpdateVisuals();
        }
    }

    void OnThumbPan(PanUpdatedEventArgs e, bool isUpper)
    {
        if (!IsEnabled) return;

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                isDragging = true;
                draggingUpper = isUpper;
                dragStartThumbX = AbsoluteLayout.GetLayoutBounds(isUpper ? upperThumb : lowerThumb).X;
                break;

            case GestureStatus.Running:
                if (isDragging && trackWidth > 0)
                {
                    var currentX = dragStartThumbX + e.TotalX;
                    var percent = Math.Clamp(currentX / (trackWidth - ResolvedThumbWidth), 0, 1);
                    ApplyPercentToThumb(percent, draggingUpper);
                }
                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                isDragging = false;
                break;
        }
    }

    void OnTrackTapped(object? sender, TappedEventArgs e)
    {
        if (!IsEnabled || trackWidth <= 0) return;

        var point = e.GetPosition(trackLayout);
        if (point is null) return;

        var percent = Math.Clamp(point.Value.X / trackWidth, 0, 1);
        var tappedValue = PercentToValue(percent);

        // Move whichever thumb is nearer the tap.
        var toUpper = Math.Abs(tappedValue - UpperValue) < Math.Abs(tappedValue - LowerValue);
        ApplyPercentToThumb(percent, toUpper);
    }

    double PercentToValue(double percent)
    {
        var raw = Minimum + (percent * (Maximum - Minimum));
        if (Step > 0)
            raw = Math.Round(raw / Step) * Step;
        return Math.Clamp(raw, Minimum, Maximum);
    }

    void ApplyPercentToThumb(double percent, bool isUpper)
    {
        var value = PercentToValue(percent);
        var lower = LowerValue;
        var upper = UpperValue;

        if (isUpper)
        {
            // Cannot come closer than MinimumRange to the lower thumb.
            if (MinimumRange > 0)
                value = Math.Max(value, lower + MinimumRange);
            value = Math.Clamp(value, Minimum, Maximum);
            // Enforce MaximumRange by pushing the lower thumb up.
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

        var changed = Math.Abs(lower - LowerValue) > double.Epsilon || Math.Abs(upper - UpperValue) > double.Epsilon;
        if (!changed) return;

        LowerValue = lower;
        UpperValue = upper;

        var range = new SliderRange(lower, upper);
        RangeChangedCommand?.Execute(range);
        RangeChanged?.Invoke(this, range);
    }

    void UpdateVisuals()
    {
        if (trackWidth <= 0) return;

        var span = Maximum > Minimum ? Maximum - Minimum : 1;
        var lowerPercent = Math.Clamp((LowerValue - Minimum) / span, 0, 1);
        var upperPercent = Math.Clamp((UpperValue - Minimum) / span, 0, 1);
        if (upperPercent < lowerPercent)
            (lowerPercent, upperPercent) = (upperPercent, lowerPercent);

        var lowerColor = BlendColors(ColdColor, HotColor, lowerPercent);
        var upperColor = BlendColors(ColdColor, HotColor, upperPercent);

        trackBackground.HeightRequest = TrackHeight;
        trackBackground.CornerRadius = new CornerRadius(TrackHeight / 2);

        // Active fill spans from the lower thumb center to the upper thumb center.
        var lowerX = lowerPercent * (trackWidth - ResolvedThumbWidth) + (ResolvedThumbWidth / 2);
        var upperX = upperPercent * (trackWidth - ResolvedThumbWidth) + (ResolvedThumbWidth / 2);
        var fillWidth = Math.Max(0, upperX - lowerX);
        AbsoluteLayout.SetLayoutBounds(trackFill, new Rect(lowerX, 0.5, fillWidth, TrackHeight));
        trackFill.HeightRequest = TrackHeight;
        trackFill.CornerRadius = new CornerRadius(TrackHeight / 2);
        trackFill.Background = new LinearGradientBrush(
            new GradientStopCollection
            {
                new GradientStop(lowerColor, 0),
                new GradientStop(upperColor, 1)
            },
            new Point(0, 0.5),
            new Point(1, 0.5));

        // Thumbs
        PositionThumb(lowerThumb, lowerPercent, lowerColor);
        PositionThumb(upperThumb, upperPercent, upperColor);

        UpdateThumbContent();

        // Tooltips
        UpdateTooltips(lowerPercent, upperPercent, lowerColor, upperColor);
    }

    void PositionThumb(Border thumb, double percent, Color color)
    {
        var thumbX = percent * (trackWidth - ResolvedThumbWidth);
        AbsoluteLayout.SetLayoutBounds(thumb, new Rect(thumbX, 0.5, ResolvedThumbWidth, ResolvedThumbHeight));
        thumb.Stroke = color;
        if (thumb.StrokeShape is Microsoft.Maui.Controls.Shapes.RoundRectangle shape)
            shape.CornerRadius = ResolvedThumbCornerRadius;
        thumb.WidthRequest = ResolvedThumbWidth;
        thumb.HeightRequest = ResolvedThumbHeight;
        thumb.Padding = ThumbPadding;
    }


    /// <summary>
    /// Drops the realized templates so the next draw builds them again. Only a change of template comes
    /// through here — a change of value rebinds the views that are already there.
    /// </summary>
    void RebuildThumbContent()
    {
        this.lowerThumb.Content = null;
        this.upperThumb.Content = null;
        this.lowerThumbContent = null;
        this.upperThumbContent = null;

        // Built here rather than left to the next draw: UpdateVisuals does nothing until the track has
        // been given a width, and on net10.0-macos a view added after the page has laid out is never
        // realized. XAML sets the template before the first layout, so this is the moment to use it.
        this.UpdateThumbContent();
        this.UpdateVisuals();
    }


    /// <summary>
    /// Realizes each thumb's template once and rebinds it afterwards. Rebuilding per draw would throw the
    /// content away on every pixel of a drag, and on net10.0-macos a view added after the page has laid
    /// out is never realized at all — so the one that survives the drag is the one built first.
    /// </summary>
    void UpdateThumbContent()
    {
        this.lowerThumbContent = this.ApplyThumbContent(this.lowerThumb, this.lowerThumbContent, this.LowerThumbTemplate ?? this.ThumbTemplate, this.LowerValue);
        this.upperThumbContent = this.ApplyThumbContent(this.upperThumb, this.upperThumbContent, this.UpperThumbTemplate ?? this.ThumbTemplate, this.UpperValue);
    }


    View? ApplyThumbContent(Border thumb, View? existing, DataTemplate? template, double value)
    {
        if (template is null)
            return existing;

        if (existing is null && template.CreateContent() is View created)
        {
            // The pan lives on the thumb itself, so its content has to opt out of the gesture or it
            // swallows the drag before the Border ever sees it.
            created.InputTransparent = true;
            existing = created;
            thumb.Content = created;
        }

        if (existing is not null)
            existing.BindingContext = value;

        return existing;
    }

    void UpdateTooltips(double lowerPercent, double upperPercent, Color lowerColor, Color upperColor)
    {
        tooltipRow.IsVisible = ShowTooltip;
        if (!ShowTooltip) return;

        UpdateTooltip(lowerTooltipContainer, lowerTooltipBadge, lowerTooltipLabel, LowerValue, lowerPercent);
        UpdateTooltip(upperTooltipContainer, upperTooltipBadge, upperTooltipLabel, UpperValue, upperPercent);
    }

    void UpdateTooltip(ContentView container, Border badge, Label label, double value, double percent)
    {
        if (TooltipTemplate is not null)
        {
            container.Content = CreateTooltipFromTemplate(value);
        }
        else
        {
            if (container.Content != badge)
                container.Content = badge;

            label.Text = FormatValue(value);
            if (TooltipTextColor is Color ttText)
                label.TextColor = ttText;
            else
                label.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
            label.FontSize = TooltipFontSize;
            if (TooltipBackgroundColor is Color ttBg)
                badge.BackgroundColor = ttBg;
            else
                badge.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceVariant);
        }

        var content = container.Content;
        var tooltipWidth = content is { Width: > 0 } ? content.Width : 50;
        var thumbCenter = percent * (trackWidth - ResolvedThumbWidth) + (ResolvedThumbWidth / 2);
        var tooltipX = Math.Clamp(thumbCenter - (tooltipWidth / 2), 0, Math.Max(0, trackWidth - tooltipWidth));
        if (content is View v)
            v.TranslationX = tooltipX;
    }

    View? CreateTooltipFromTemplate(double value)
    {
        if (TooltipTemplate is null) return null;
        var content = TooltipTemplate.CreateContent();
        if (content is View view)
        {
            view.BindingContext = value;
            view.HorizontalOptions = LayoutOptions.Start;
            return view;
        }
        return null;
    }

    string FormatValue(double val)
    {
        if (!string.IsNullOrEmpty(ValueFormat))
            return val.ToString(ValueFormat);
        return val % 1 == 0 ? val.ToString("0") : val.ToString("0.#");
    }

    static Color BlendColors(Color color1, Color color2, double ratio)
    {
        ratio = Math.Clamp(ratio, 0, 1);
        var r = color1.Red + (color2.Red - color1.Red) * (float)ratio;
        var g = color1.Green + (color2.Green - color1.Green) * (float)ratio;
        var b = color1.Blue + (color2.Blue - color1.Blue) * (float)ratio;
        return new Color(r, g, b);
    }
}
