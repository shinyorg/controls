using Shiny.Controls.RangePickers;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls;

/// <summary>Raised when a date/time range is applied, or cleared (<see cref="Range"/> null).</summary>
public class DateTimeRangeSelectedEventArgs(DateTimeRange? range) : EventArgs
{
    public DateTimeRange? Range { get; } = range;
}


/// <summary>
/// A field showing a date/time range ("Mar 3, 9:00 AM – Mar 5, 2026, 5:00 PM") that opens a range
/// calendar with a start and end time under it. A range that starts and ends on one day only offers
/// end times after the start time.
/// </summary>
public partial class DateTimeRangePicker : RangePickerBase
{
    DateRangeCalendar? calendar;
    Picker? startPicker;
    Picker? endPicker;
    Label? startDateLabel;
    Label? endDateLabel;
    IReadOnlyList<TimeOnly> startSlots = [];
    IReadOnlyList<TimeOnly> endSlots = [];
    TimeOnly draftStartTime;
    TimeOnly draftEndTime;
    bool populating;

    public DateTimeRangePicker() : base("📅")
    {
        StyleGuard.MarkReady(this, typeof(DateTimeRangePicker));
        this.RefreshText();
    }

    public DateTimeRange? Range => this.StartDateTime is { } s && this.EndDateTime is { } e ? new DateTimeRange(s, e) : null;

    public event EventHandler<DateTimeRangeSelectedEventArgs>? RangeSelected;

    internal DateRangeCalendar? PopupCalendar => this.calendar;

    protected override string DefaultPlaceholder => "Select dates and times";


    /// <summary>The range the popup would apply right now, or null while it is incomplete.</summary>
    internal DateTimeRange? DraftRange
    {
        get
        {
            if (this.calendar?.Range is not { } dates)
                return null;

            var start = dates.Start.ToDateTime(this.draftStartTime);
            var end = dates.End.ToDateTime(this.draftEndTime);
            return end > start ? new DateTimeRange(start, end) : null;
        }
    }


    protected override bool CanApply
    {
        get
        {
            if (this.calendar is { StartDate: null, EndDate: null })
                return true;

            if (this.DraftRange is not { } range)
                return false;

            if (this.MinDuration is { } min && range.Duration < min)
                return false;

            return this.MaxDuration is not { } max || range.Duration <= max;
        }
    }


    /// <summary>Sets the draft times in the open popup. Test seam and code-driven entry.</summary>
    internal void SetDraftTimes(TimeOnly start, TimeOnly end)
    {
        this.draftStartTime = start;
        this.draftEndTime = end;
        this.RefreshTimes();
    }


    protected override void OnOpening()
    {
        this.draftStartTime = this.StartDateTime is { } s ? TimeOnly.FromDateTime(s) : TimeOnly.FromTimeSpan(this.DefaultStartTime);
        this.draftEndTime = this.EndDateTime is { } e ? TimeOnly.FromDateTime(e) : TimeOnly.FromTimeSpan(this.DefaultEndTime);

        this.calendar = new DateRangeCalendar
        {
            MinDate = this.MinDate,
            MaxDate = this.MaxDate,
            MinDays = this.MinDays,
            MaxDays = this.MaxDays,
            AllowSingleDay = this.AllowSingleDay,
            DisabledDates = this.DisabledDates,
            DisabledDaysOfWeek = this.DisabledDaysOfWeek,
            IsDateDisabled = this.IsDateDisabled,
            AllowDisabledDatesInRange = this.AllowDisabledDatesInRange,
            FirstDayOfWeek = this.FirstDayOfWeek,
            Presets = this.Presets,
            Culture = this.Culture,
            StartDate = this.StartDateTime?.Date,
            EndDate = this.EndDateTime?.Date
        };
        this.calendar.PropertyChanged += this.OnCalendarChanged;
    }


    protected override View CreatePopupContent()
    {
        this.startDateLabel = DateLabel();
        this.endDateLabel = DateLabel();
        this.startPicker = new Picker { Title = this.StartTimeText, MinimumWidthRequest = 120 };
        this.endPicker = new Picker { Title = this.EndTimeText, MinimumWidthRequest = 120 };
        this.startPicker.SelectedIndexChanged += this.OnStartPicked;
        this.endPicker.SelectedIndexChanged += this.OnEndPicked;

        var times = new Grid
        {
            ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)],
            RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto)],
            ColumnSpacing = 12,
            RowSpacing = 4,
            Margin = new Thickness(4, 12, 4, 0)
        };
        times.Add(Caption(this.StartTimeText), 0, 0);
        times.Add(this.startDateLabel, 1, 0);
        times.Add(this.startPicker, 2, 0);
        times.Add(Caption(this.EndTimeText), 0, 1);
        times.Add(this.endDateLabel, 1, 1);
        times.Add(this.endPicker, 2, 1);

        this.RefreshTimes();
        return new VerticalStackLayout { Children = { this.calendar!, times } };
    }


    void OnCalendarChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DateRangeCalendar.StartDate) or nameof(DateRangeCalendar.EndDate))
            this.RefreshTimes();
    }


    void OnStartPicked(object? sender, EventArgs e)
    {
        if (this.populating || this.startPicker is not { SelectedIndex: >= 0 } p || p.SelectedIndex >= this.startSlots.Count)
            return;

        // Keep the length on a one-day range, the same as the time range picker does.
        var oldLength = this.draftEndTime - this.draftStartTime;
        this.draftStartTime = this.startSlots[p.SelectedIndex];
        if (this.IsSameDay && oldLength > TimeSpan.Zero)
        {
            var moved = this.draftStartTime.Add(oldLength, out var wrapped);
            this.draftEndTime = wrapped == 0 ? moved : this.draftEndTime;
        }
        this.RefreshTimes();
    }


    void OnEndPicked(object? sender, EventArgs e)
    {
        if (this.populating || this.endPicker is not { SelectedIndex: >= 0 } p || p.SelectedIndex >= this.endSlots.Count)
            return;

        this.draftEndTime = this.endSlots[p.SelectedIndex];
        this.RefreshTimes();
    }


    bool IsSameDay => this.calendar?.Range is { IsSingleDay: true };


    void RefreshTimes()
    {
        this.RefreshPopupState();
        if (this.startPicker is null || this.endPicker is null || this.calendar is null)
            return;

        var culture = this.EffectiveCulture;
        var timeFormat = culture.DateTimeFormat.ShortTimePattern;
        var dateFormat = culture.DateTimeFormat.MonthDayPattern.Replace("MMMM", "MMM");
        var constraints = new TimeRangeConstraints { Interval = this.Interval };
        var all = constraints.AllSlots();

        this.startSlots = all;
        this.endSlots = this.IsSameDay ? [.. all.Where(t => t > this.draftStartTime)] : all;

        // Snap the draft onto the grid, and the end after the start on a one-day range.
        this.draftStartTime = constraints.Snap(this.draftStartTime);
        this.draftEndTime = constraints.Snap(this.draftEndTime);
        if (this.endSlots.Count > 0 && !this.endSlots.Contains(this.draftEndTime))
            this.draftEndTime = this.endSlots.FirstOrDefault(t => t > this.draftStartTime, this.endSlots[^1]);

        this.populating = true;
        try
        {
            Fill(this.startPicker, this.startSlots, this.draftStartTime, timeFormat, culture);
            Fill(this.endPicker, this.endSlots, this.draftEndTime, timeFormat, culture);
        }
        finally
        {
            this.populating = false;
        }

        var dates = this.calendar.Range;
        var start = this.calendar.StartDate;
        this.startDateLabel!.Text = start is { } s ? s.ToString(dateFormat, culture) : "—";
        this.endDateLabel!.Text = dates is { } r ? r.End.ToString(dateFormat, culture) : "—";

        this.RefreshPopupState();

        static void Fill(Picker picker, IReadOnlyList<TimeOnly> slots, TimeOnly selected, string format, System.Globalization.CultureInfo culture)
        {
            picker.ItemsSource = slots.Select(t => t.ToString(format, culture)).ToList();
            var index = -1;
            for (var i = 0; i < slots.Count; i++)
            {
                if (slots[i] == selected)
                {
                    index = i;
                    break;
                }
            }
            picker.SelectedIndex = index;
        }
    }


    static Label DateLabel()
    {
        var label = new Label { VerticalTextAlignment = TextAlignment.Center }.WithFontSize(ShinyThemeKeys.Type.BodyMediumSize);
        label.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurface);
        return label;
    }


    static Label Caption(string text)
    {
        var label = new Label { Text = text, VerticalTextAlignment = TextAlignment.Center }.WithFontSize(ShinyThemeKeys.Type.LabelLargeSize);
        label.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
        return label;
    }


    protected override void CommitDraft()
    {
        var range = this.DraftRange;
        this.StartDateTime = range?.Start;
        this.EndDateTime = range?.End;

        this.RangeSelected?.Invoke(this, new DateTimeRangeSelectedEventArgs(range));
        if (range is not null && this.RangeSelectedCommand?.CanExecute(range) == true)
            this.RangeSelectedCommand.Execute(range);
    }

    protected override void OnClearDraft()
    {
        this.calendar?.Clear();
        this.RefreshTimes();
    }

    protected override void OnClosed()
    {
        if (this.calendar is not null)
            this.calendar.PropertyChanged -= this.OnCalendarChanged;
        if (this.startPicker is not null)
            this.startPicker.SelectedIndexChanged -= this.OnStartPicked;
        if (this.endPicker is not null)
            this.endPicker.SelectedIndexChanged -= this.OnEndPicked;

        this.calendar = null;
        this.startPicker = this.endPicker = null;
        this.startDateLabel = this.endDateLabel = null;
    }

    protected override string? FormatValue()
        => this.Range is { } r ? RangeFormatter.Format(r, this.EffectiveCulture, this.Format, this.Separator) : null;

    protected override string? DraftSummary()
        => this.DraftRange is { } r ? RangeFormatter.Format(r, this.EffectiveCulture, this.Format, this.Separator) : null;
}
