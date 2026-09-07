using System.Collections.Specialized;
using Microsoft.Maui.Controls.Shapes;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls;

public partial class Slider
{
    /// <summary>One mark's views: the thing on the track, and the label beside it when there is one.</summary>
    sealed class MarkVisual
    {
        public required SliderMark Mark { get; init; }
        public required View Marker { get; init; }
        public View? Caption { get; init; }
    }


    // ---------------------------------------------------------------------------------------------
    // Collection plumbing
    // ---------------------------------------------------------------------------------------------

    void OnMarksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (SliderMark mark in e.OldItems)
                mark.Changed -= this.OnMarkChanged;
        }

        if (e.NewItems is not null)
        {
            foreach (SliderMark mark in e.NewItems)
                mark.Changed += this.OnMarkChanged;
        }

        foreach (var mark in this.marks)
            SetInheritedBindingContext(mark, this.BindingContext);

        this.RebuildMarks();
    }


    void OnMarkChanged(object? sender, EventArgs e) => this.RebuildMarks();


    /// <summary>Bands, then positions. Anything that changes how much room the slider needs goes through here.</summary>
    void Refresh()
    {
        this.UpdateLayoutRequests();
        this.UpdateVisuals();
    }


    // ---------------------------------------------------------------------------------------------
    // Building
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Recreates the mark views. This runs as the collection is populated — for XAML, before the first
    /// layout — because on net10.0-macos a child added after the page has laid out is never realized.
    /// </summary>
    void RebuildMarks()
    {
        foreach (var visual in this.markVisuals)
        {
            this.rootLayout.Children.Remove(visual.Marker);
            if (visual.Caption is not null)
                this.rootLayout.Children.Remove(visual.Caption);
        }
        this.markVisuals.Clear();

        // Marks are inserted above the track but below the thumb. Appending them would paint a tick
        // straight through the thumb, and snapping parks the thumb on a mark by definition.
        var index = this.rootLayout.Children.IndexOf(this.thumb);

        foreach (var mark in this.marks)
        {
            if (!mark.IsVisible)
                continue;

            var shape = mark.Shape ?? this.MarkShape;
            var marker = shape == SliderMarkShape.Line ? this.CreateLine(mark) : this.CreateDot(mark);

            View? caption = null;
            if (this.ShowMarkLabels && !string.IsNullOrWhiteSpace(mark.Text))
            {
                caption = shape == SliderMarkShape.Bubble
                    ? this.CreateBubble(mark)
                    : this.CreateCaption(mark);
            }

            // Marks are decoration: they must not swallow the drag or tap that the track is listening for.
            marker.InputTransparent = true;
            this.rootLayout.Children.Insert(index++, marker);

            if (caption is not null)
            {
                caption.InputTransparent = true;
                this.rootLayout.Children.Insert(index++, caption);
            }

            this.markVisuals.Add(new MarkVisual { Mark = mark, Marker = marker, Caption = caption });
        }

        this.Refresh();
    }


    View CreateDot(SliderMark mark)
    {
        var size = this.MarkSizeFor(mark);
        var dot = new Border
        {
            WidthRequest = size,
            HeightRequest = size,
            StrokeShape = new RoundRectangle { CornerRadius = size / 2 },
            Stroke = Colors.Transparent,
            StrokeThickness = 0,
            Padding = 0
        };
        this.ApplyMarkFill(dot, mark, ShinyThemeKeys.Color.Surface);
        return dot;
    }


    View CreateLine(SliderMark mark)
    {
        var thickness = Math.Max(2, this.MarkSizeFor(mark) / 3);
        var length = this.TrackHeight + 8;

        // BoxView: Color is set alongside Background because the macOS/AppKit handler paints from Color
        // only, which would otherwise leave every tick invisible there.
        var line = new BoxView
        {
            WidthRequest = this.IsVertical ? length : thickness,
            HeightRequest = this.IsVertical ? thickness : length
        };

        var fill = mark.Color ?? this.MarkColor;
        if (fill is not null)
        {
            line.Color = fill;
            line.Background = new SolidColorBrush(fill);
        }
        else
        {
            line.SetDynamicResource(BoxView.ColorProperty, ShinyThemeKeys.Color.Surface);
            line.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.Surface);
        }
        return line;
    }


    /// <summary>
    /// The labelled pill. It sits in the label band rather than on the track: snapping parks the thumb
    /// on a mark by definition, so a badge centred on the track would spend its life underneath one.
    /// The stop point itself is still the dot on the track.
    /// </summary>
    View CreateBubble(SliderMark mark)
    {
        var label = new Label
        {
            Text = mark.Text ?? string.Empty,
            FontSize = this.MarkFontSize,
            LineBreakMode = LineBreakMode.NoWrap,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };
        this.ApplyMarkTextColor(label, mark);

        var bubble = new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = (this.MarkFontSize + 10) / 2 },
            Stroke = Colors.Transparent,
            StrokeThickness = 0,
            Padding = new Thickness(8, 3),
            Content = label
        };
        this.ApplyMarkFill(bubble, mark, ShinyThemeKeys.Color.SurfaceVariant);

        // A bubble sizes to its own text, so its extent is only known once it has been laid out.
        bubble.SizeChanged += (_, _) => this.UpdateVisuals();
        return bubble;
    }


    View CreateCaption(SliderMark mark)
    {
        var caption = new Label
        {
            Text = mark.Text,
            FontSize = this.MarkFontSize,
            LineBreakMode = LineBreakMode.NoWrap,
            HorizontalTextAlignment = this.IsVertical ? TextAlignment.Start : TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };
        this.ApplyMarkTextColor(caption, mark);
        caption.SizeChanged += (_, _) => this.UpdateVisuals();
        return caption;
    }


    void ApplyMarkFill(Border border, SliderMark mark, string fallbackToken)
    {
        var fill = mark.Color ?? this.MarkColor;
        if (fill is not null)
            border.BackgroundColor = fill;
        else
            border.SetDynamicResource(VisualElement.BackgroundColorProperty, fallbackToken);
    }


    void ApplyMarkTextColor(Label label, SliderMark mark)
    {
        var color = mark.TextColor ?? this.MarkTextColor;
        if (color is not null)
            label.TextColor = color;
        else
            label.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
    }


    double MarkSizeFor(SliderMark mark) => mark.Size > 0 ? mark.Size : this.MarkSize;


    // ---------------------------------------------------------------------------------------------
    // Measurement
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A cheap width for a run of text. The bands have to be sized before anything is laid out, and
    /// feeding a measured size back into a size request is how a layout starts oscillating — so the
    /// bands are always estimated, and only the positions inside them use real sizes.
    /// </summary>
    static double EstimateTextWidth(string? text, double fontSize)
        => string.IsNullOrEmpty(text) ? 0 : (text!.Length * fontSize * 0.58) + 2;

    double EstimatedTooltipWidth() => EstimateTextWidth(this.FormatValue(this.Value), this.TooltipFontSize) + 20;

    double EstimatedTooltipHeight() => this.TooltipFontSize + 14;

    double EstimatedCaptionWidth(SliderMark mark) => EstimateTextWidth(mark.Text, this.MarkFontSize);

    double EstimatedCaptionHeight() => this.MarkFontSize + 6;

    double EstimatedBubbleWidth(SliderMark mark) => EstimateTextWidth(mark.Text, this.MarkFontSize) + 16;

    double EstimatedBubbleHeight() => this.MarkFontSize + 12;


    /// <summary>Room reserved for the tooltip: above the track when horizontal, to its left when vertical.</summary>
    double TooltipBand()
    {
        if (!this.ShowTooltip)
            return 0;

        var extent = this.IsVertical ? this.EstimatedTooltipWidth() : this.EstimatedTooltipHeight();
        return extent + TooltipGap;
    }


    /// <summary>Room reserved for the track itself — the widest of track, thumb and anything sitting on it.</summary>
    double TrackBand()
    {
        var band = Math.Max(this.ThumbAcross, this.TrackHeight);

        foreach (var visual in this.markVisuals)
        {
            band = (visual.Mark.Shape ?? this.MarkShape) == SliderMarkShape.Line
                ? Math.Max(band, this.TrackHeight + 8)
                : Math.Max(band, this.MarkSizeFor(visual.Mark));
        }
        return band;
    }


    /// <summary>Room reserved for the mark captions: below the track when horizontal, to its right when vertical.</summary>
    double MarkLabelBand()
    {
        double extent = 0;

        foreach (var visual in this.markVisuals)
        {
            if (visual.Caption is null)
                continue;

            var bubble = (visual.Mark.Shape ?? this.MarkShape) == SliderMarkShape.Bubble;
            extent = Math.Max(extent, this.IsVertical
                ? (bubble ? this.EstimatedBubbleWidth(visual.Mark) : this.EstimatedCaptionWidth(visual.Mark))
                : (bubble ? this.EstimatedBubbleHeight() : this.EstimatedCaptionHeight()));
        }
        return extent > 0 ? extent + MarkLabelGap : 0;
    }


    void UpdateLayoutRequests()
    {
        var total = this.TooltipBand() + this.TrackBand() + this.MarkLabelBand();

        if (this.IsVertical)
        {
            this.rootLayout.WidthRequest = total;
            this.rootLayout.HeightRequest = this.VerticalLength;
            this.rootLayout.HorizontalOptions = LayoutOptions.Start;
            this.rootLayout.VerticalOptions = LayoutOptions.Start;
        }
        else
        {
            this.rootLayout.WidthRequest = -1;
            this.rootLayout.HeightRequest = total;
            this.rootLayout.HorizontalOptions = LayoutOptions.Fill;
            this.rootLayout.VerticalOptions = LayoutOptions.Center;
        }
    }


    // ---------------------------------------------------------------------------------------------
    // Placement
    // ---------------------------------------------------------------------------------------------

    void LayoutMarks(double tooltipBand, double trackBand)
    {
        var range = this.Maximum - this.Minimum;

        foreach (var visual in this.markVisuals)
        {
            var percent = range > 0
                ? Math.Clamp((visual.Mark.Value - this.Minimum) / range, 0, 1)
                : 0;
            var center = this.CenterFor(percent);

            this.PlaceMarker(visual, center, tooltipBand, trackBand);

            if (visual.Caption is not null)
                this.PlaceCaption(visual, center, tooltipBand, trackBand);
        }
    }


    void PlaceMarker(MarkVisual visual, double center, double tooltipBand, double trackBand)
    {
        var shape = visual.Mark.Shape ?? this.MarkShape;
        var size = this.MarkSizeFor(visual.Mark);
        var along = shape == SliderMarkShape.Line ? Math.Max(2, size / 3) : size;
        var across = shape == SliderMarkShape.Line ? this.TrackHeight + 8 : size;

        AbsoluteLayout.SetLayoutBounds(visual.Marker, this.IsVertical
            ? new Rect(tooltipBand + ((trackBand - across) / 2), center - (along / 2), across, along)
            : new Rect(center - (along / 2), tooltipBand + ((trackBand - across) / 2), along, across));
    }


    void PlaceCaption(MarkVisual visual, double center, double tooltipBand, double trackBand)
    {
        var caption = visual.Caption!;
        var bubble = (visual.Mark.Shape ?? this.MarkShape) == SliderMarkShape.Bubble;

        var width = caption.Width > 0
            ? caption.Width
            : (bubble ? this.EstimatedBubbleWidth(visual.Mark) : this.EstimatedCaptionWidth(visual.Mark));
        var height = caption.Height > 0
            ? caption.Height
            : (bubble ? this.EstimatedBubbleHeight() : this.EstimatedCaptionHeight());

        if (this.IsVertical)
        {
            var y = Math.Clamp(center - (height / 2), 0, Math.Max(0, this.layoutHeight - height));
            AbsoluteLayout.SetLayoutBounds(caption, new Rect(tooltipBand + trackBand + MarkLabelGap, y, AbsoluteLayout.AutoSize, AbsoluteLayout.AutoSize));
        }
        else
        {
            var x = Math.Clamp(center - (width / 2), 0, Math.Max(0, this.layoutWidth - width));
            AbsoluteLayout.SetLayoutBounds(caption, new Rect(x, tooltipBand + trackBand + MarkLabelGap, AbsoluteLayout.AutoSize, AbsoluteLayout.AutoSize));
        }
    }
}
