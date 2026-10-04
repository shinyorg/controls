using System.Globalization;
using Microsoft.Maui.Controls.Shapes;
using Shiny.Controls.RangePickers;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls;

/// <summary>
/// One day of a <see cref="DateRangeCalendar"/>: a circle for the day number over a band that joins
/// the days of a range. The band is two halves so the start day can show only its right half and the
/// end day only its left - which is what makes a range read as one shape rather than a row of pills.
/// </summary>
sealed class RangeDayCell : Grid
{
    const double CircleSize = 36;

    readonly BoxView bandLeft;
    readonly BoxView bandRight;
    readonly Border circle;
    readonly Label label;
    readonly TapGestureRecognizer tap;

    public RangeDayCell()
    {
        this.ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star)];
        this.HeightRequest = 40;
        this.MinimumWidthRequest = 36;

        // BoxView.Color, not BackgroundColor: AppKit paints a BoxView from Color only.
        this.bandLeft = new BoxView { HeightRequest = CircleSize, VerticalOptions = LayoutOptions.Center, IsVisible = false };
        this.bandRight = new BoxView { HeightRequest = CircleSize, VerticalOptions = LayoutOptions.Center, IsVisible = false };
        this.bandLeft.SetDynamicResource(BoxView.ColorProperty, ShinyThemeKeys.Color.PrimaryContainer);
        this.bandRight.SetDynamicResource(BoxView.ColorProperty, ShinyThemeKeys.Color.PrimaryContainer);

        this.label = new Label
        {
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        }.WithFontSize(ShinyThemeKeys.Type.BodyMediumSize);

        this.circle = new Border
        {
            WidthRequest = CircleSize,
            HeightRequest = CircleSize,
            StrokeShape = new RoundRectangle { CornerRadius = CircleSize / 2 },
            StrokeThickness = 0,
            Padding = 0,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Content = this.label
        };

        this.Add(this.bandLeft, 0, 0);
        this.Add(this.bandRight, 1, 0);
        this.Add(this.circle, 0, 0);
        Grid.SetColumnSpan(this.circle, 2);

        this.tap = new TapGestureRecognizer();
        this.tap.Tapped += (_, _) => this.Tapped?.Invoke(this, this.Date);
        this.GestureRecognizers.Add(this.tap);

        // Desktop hover drives the range preview; a no-op on touch.
        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) => this.Hovered?.Invoke(this, this.Date);
        this.GestureRecognizers.Add(pointer);
    }

    public DateOnly Date { get; private set; }

    public CalendarDayState State { get; private set; }

    public event EventHandler<DateOnly>? Tapped;

    public event EventHandler<DateOnly>? Hovered;

    /// <summary>Test seam: what a tap does, without a platform to deliver one.</summary>
    internal void SimulateTap() => this.Tapped?.Invoke(this, this.Date);


    public void Update(DateOnly date, CalendarDayState state, bool hasEnd, bool previewActive, bool showOutsideDays, CultureInfo culture)
    {
        this.Date = date;
        this.State = state;

        var outside = state.HasFlag(CalendarDayState.OutsideMonth);
        var hidden = outside && !showOutsideDays;
        this.label.Text = hidden ? string.Empty : date.Day.ToString(culture);
        this.InputTransparent = hidden;

        var start = state.HasFlag(CalendarDayState.RangeStart);
        var end = state.HasFlag(CalendarDayState.RangeEnd);
        var endpoint = start || end;
        var inRange = state.HasFlag(CalendarDayState.InRange);
        var preview = state.HasFlag(CalendarDayState.Preview);
        var previewEnd = state.HasFlag(CalendarDayState.PreviewEnd);
        var blocked = state.HasFlag(CalendarDayState.Disabled) || state.HasFlag(CalendarDayState.Unavailable);

        // A range shown across two month grids would otherwise draw its band twice - once in each
        // month's padding days. Outside days never carry range styling.
        if (outside)
        {
            endpoint = start = end = inRange = preview = previewEnd = false;
        }

        // The band: full under days strictly inside the range, half under an endpoint that has a
        // partner (the start's right half, the end's left half), translucent while only previewed.
        var startOnly = start && !end;
        var endOnly = end && !start;
        var solid = inRange || endOnly || (startOnly && hasEnd);
        var faint = preview || previewEnd || (startOnly && !hasEnd && previewActive);
        this.bandLeft.IsVisible = inRange || endOnly || preview || previewEnd;
        this.bandRight.IsVisible = inRange || preview || (startOnly && (hasEnd || previewActive));
        var bandOpacity = solid ? 1.0 : faint ? 0.5 : 1.0;
        this.bandLeft.Opacity = bandOpacity;
        this.bandRight.Opacity = bandOpacity;

        if (endpoint)
        {
            RangePickerPaint.Fill(this.circle, ShinyThemeKeys.Color.Primary);
            this.label.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnPrimary);
        }
        else
        {
            RangePickerPaint.Fill(this.circle, null);
            this.label.SetDynamicResource(
                Label.TextColorProperty,
                inRange || preview || previewEnd ? ShinyThemeKeys.Color.OnPrimaryContainer : ShinyThemeKeys.Color.OnSurface
            );
        }

        // Ring: today, or the day the pointer would end the range on.
        if ((state.HasFlag(CalendarDayState.Today) && !endpoint && !outside) || previewEnd)
        {
            this.circle.StrokeThickness = 1.5;
            this.circle.SetDynamicResource(Border.StrokeProperty, ShinyThemeKeys.Brush.Primary);
        }
        else
        {
            this.circle.StrokeThickness = 0;
        }

        this.label.FontAttributes = endpoint || state.HasFlag(CalendarDayState.Today) ? FontAttributes.Bold : FontAttributes.None;
        this.label.TextDecorations = state.HasFlag(CalendarDayState.Disabled) && !outside ? TextDecorations.Strikethrough : TextDecorations.None;
        this.label.Opacity = blocked ? 0.35 : outside ? 0.45 : 1.0;

        SemanticProperties.SetDescription(this, hidden ? string.Empty : Describe(date, state, culture));
    }


    static string Describe(DateOnly date, CalendarDayState state, CultureInfo culture)
    {
        var text = date.ToString(culture.DateTimeFormat.LongDatePattern, culture);
        if (state.HasFlag(CalendarDayState.RangeStart) && state.HasFlag(CalendarDayState.RangeEnd))
            text += ", selected";
        else if (state.HasFlag(CalendarDayState.RangeStart))
            text += ", range start";
        else if (state.HasFlag(CalendarDayState.RangeEnd))
            text += ", range end";
        else if (state.HasFlag(CalendarDayState.InRange))
            text += ", in range";

        if (state.HasFlag(CalendarDayState.Disabled) || state.HasFlag(CalendarDayState.Unavailable))
            text += ", unavailable";

        if (state.HasFlag(CalendarDayState.Today))
            text += ", today";

        return text;
    }
}
