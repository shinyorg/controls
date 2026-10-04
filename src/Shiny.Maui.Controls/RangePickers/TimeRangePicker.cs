using Microsoft.Maui.Controls.Shapes;
using Shiny.Controls.RangePickers;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls;

/// <summary>Raised when a time range is applied, or cleared (<see cref="Range"/> null).</summary>
public class TimeRangeSelectedEventArgs(TimeRange? range) : EventArgs
{
    public TimeRange? Range { get; } = range;
}


/// <summary>
/// A field showing a time range ("9:00 AM – 5:30 PM") that opens two columns of times - start on the
/// left, end on the right with the length each would make. Moving the start keeps the range's length,
/// the way calendar apps do. With <see cref="AllowOvernight"/> the end may wrap past midnight.
/// </summary>
public partial class TimeRangePicker : RangePickerBase
{
    VerticalStackLayout? startColumn;
    VerticalStackLayout? endColumn;
    ScrollView? startScroll;
    ScrollView? endScroll;
    TimeOnly? draftStart;
    TimeOnly? draftEnd;

    public TimeRangePicker() : base("🕒")
    {
        StyleGuard.MarkReady(this, typeof(TimeRangePicker));
        this.RefreshText();
    }

    /// <summary>The committed range, or null.</summary>
    public TimeRange? Range => this.StartTime is { } s && this.EndTime is { } e
        ? new TimeRange(TimeOnly.FromTimeSpan(s), TimeOnly.FromTimeSpan(e))
        : null;

    public event EventHandler<TimeRangeSelectedEventArgs>? RangeSelected;

    internal TimeOnly? DraftStart => this.draftStart;

    internal TimeOnly? DraftEnd => this.draftEnd;

    protected override string DefaultPlaceholder => "Select times";

    protected override double PopupMaxWidth => 360;

    internal TimeRangeConstraints Constraints => new()
    {
        Interval = this.Interval,
        MinTime = this.MinTime is { } min ? TimeOnly.FromTimeSpan(min) : null,
        MaxTime = this.MaxTime is { } max ? TimeOnly.FromTimeSpan(max) : null,
        MinDuration = this.MinDuration,
        MaxDuration = this.MaxDuration,
        AllowOvernight = this.AllowOvernight
    };

    protected override bool CanApply => (this.draftStart, this.draftEnd) switch
    {
        (null, null) => true,
        ({ } s, { } e) => this.Constraints.IsValid(new TimeRange(s, e)),
        _ => false
    };


    /// <summary>Picks a start time in the open popup, moving the end to keep the range's length.</summary>
    public void SelectStart(TimeOnly start)
    {
        TimeSpan? keep = this.draftStart is { } s && this.draftEnd is { } e ? new TimeRange(s, e).Duration : null;
        this.draftStart = start;
        this.draftEnd = this.Constraints.EndFor(start, keep);
        this.RebuildColumns();
    }


    /// <summary>Picks an end time in the open popup.</summary>
    public void SelectEnd(TimeOnly end)
    {
        if (this.draftStart is null)
            return;

        this.draftEnd = end;
        this.RebuildColumns();
    }


    protected override void OnOpening()
    {
        this.draftStart = this.StartTime is { } s ? TimeOnly.FromTimeSpan(s) : null;
        this.draftEnd = this.EndTime is { } e ? TimeOnly.FromTimeSpan(e) : null;
    }


    protected override View CreatePopupContent()
    {
        this.startColumn = new VerticalStackLayout { Spacing = 2 };
        this.endColumn = new VerticalStackLayout { Spacing = 2 };
        this.startScroll = new ScrollView { Content = this.startColumn, HeightRequest = 280 };
        this.endScroll = new ScrollView { Content = this.endColumn, HeightRequest = 280 };

        var grid = new Grid
        {
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)],
            RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto)],
            ColumnSpacing = 12,
            RowSpacing = 6,
            // Two columns of short labels shrink-wrap to almost nothing; give the card a real width.
            WidthRequest = 300
        };
        grid.Add(Header(this.StartHeaderText), 0, 0);
        grid.Add(Header(this.EndHeaderText), 1, 0);
        grid.Add(this.startScroll, 0, 1);
        grid.Add(this.endScroll, 1, 1);

        this.RebuildColumns();

        // After the first layout pass: scroll each column to its selection, or the start column to now.
        this.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(80), this.ScrollToSelection);
        return grid;
    }


    void ScrollToSelection()
    {
        Scroll(this.startScroll, this.startColumn, this.draftStart ?? this.Constraints.Snap(TimeOnly.FromDateTime(DateTime.Now)));
        if (this.draftEnd is { } end)
            Scroll(this.endScroll, this.endColumn, end);

        static void Scroll(ScrollView? scroll, VerticalStackLayout? column, TimeOnly target)
        {
            if (scroll is null || column is null)
                return;

            var row = column.Children.OfType<View>().FirstOrDefault(v => v.BindingContext is TimeOnly t && t >= target);
            if (row is not null)
                _ = scroll.ScrollToAsync(row, ScrollToPosition.Center, false);
        }
    }


    void RebuildColumns()
    {
        this.RefreshPopupState();
        if (this.startColumn is null || this.endColumn is null)
            return;

        var constraints = this.Constraints;
        var culture = this.EffectiveCulture;
        var format = string.IsNullOrEmpty(this.Format) ? culture.DateTimeFormat.ShortTimePattern : this.Format;

        this.startColumn.Children.Clear();
        foreach (var slot in constraints.StartSlots())
            this.startColumn.Add(this.Row(slot, slot.ToString(format, culture), null, slot == this.draftStart, () => this.SelectStart(slot)));

        this.endColumn.Children.Clear();
        if (this.draftStart is { } start)
        {
            foreach (var end in constraints.EndSlotsFor(start))
            {
                var time = end.Time;
                this.endColumn.Add(this.Row(
                    time,
                    time.ToString(format, culture),
                    end.Duration is { } d ? RangeFormatter.FormatDuration(d) : null,
                    time == this.draftEnd,
                    () => this.SelectEnd(time)
                ));
            }
        }
        else
        {
            var hint = new Label { Text = this.PickStartHintText, Margin = new Thickness(8) }
                .WithFontSize(ShinyThemeKeys.Type.BodySmallSize);
            hint.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
            this.endColumn.Add(hint);
        }
    }


    View Row(TimeOnly time, string text, string? detail, bool selected, Action select)
    {
        var label = new Label { Text = text, VerticalTextAlignment = TextAlignment.Center }
            .WithFontSize(ShinyThemeKeys.Type.BodyMediumSize);
        var row = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)] };
        row.Add(label, 0, 0);

        Label? detailLabel = null;
        if (detail is not null)
        {
            detailLabel = new Label { Text = detail, VerticalTextAlignment = TextAlignment.Center }
                .WithFontSize(ShinyThemeKeys.Type.LabelSmallSize);
            row.Add(detailLabel, 1, 0);
        }

        var border = new Border
        {
            Padding = new Thickness(10, 8),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerSmallRadius),
            Content = row,
            BindingContext = time
        };

        if (selected)
        {
            RangePickerPaint.Fill(border, ShinyThemeKeys.Color.Primary);
            label.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnPrimary);
            detailLabel?.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnPrimary);
            label.FontAttributes = FontAttributes.Bold;
        }
        else
        {
            RangePickerPaint.Fill(border, null);
            label.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurface);
            detailLabel?.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
        }

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => select();
        border.GestureRecognizers.Add(tap);
        SemanticProperties.SetDescription(border, detail is null ? text : $"{text}, {detail}");
        return border;
    }


    static Label Header(string text)
    {
        var label = new Label { Text = text, Margin = new Thickness(4, 0) }.WithFontSize(ShinyThemeKeys.Type.LabelLargeSize);
        label.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
        return label;
    }


    protected override void CommitDraft()
    {
        this.StartTime = this.draftStart?.ToTimeSpan();
        this.EndTime = this.draftEnd?.ToTimeSpan();

        var range = this.Range;
        this.RangeSelected?.Invoke(this, new TimeRangeSelectedEventArgs(range));
        if (range is not null && this.RangeSelectedCommand?.CanExecute(range) == true)
            this.RangeSelectedCommand.Execute(range);
    }

    protected override void OnClearDraft()
    {
        this.draftStart = null;
        this.draftEnd = null;
        this.RebuildColumns();
    }

    protected override void OnClosed()
    {
        this.startColumn = this.endColumn = null;
        this.startScroll = this.endScroll = null;
    }

    protected override string? FormatValue()
        => this.Range is { } r ? RangeFormatter.Format(r, this.EffectiveCulture, this.Format, this.Separator) : null;

    protected override string? DraftSummary()
    {
        if (this.draftStart is { } s && this.draftEnd is { } e)
        {
            var range = new TimeRange(s, e);
            return $"{RangeFormatter.Format(range, this.EffectiveCulture, this.Format, this.Separator)} ({RangeFormatter.FormatDuration(range.Duration)})";
        }
        return null;
    }
}
