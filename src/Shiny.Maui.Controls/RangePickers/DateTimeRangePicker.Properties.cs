using System.Windows.Input;
using Shiny.Controls.RangePickers;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls;

public partial class DateTimeRangePicker
{
    static void Text(BindableObject b) => StyleGuard.WhenReady<DateTimeRangePicker>(b, p => p.RefreshText());

    public static readonly BindableProperty RangeSelectedCommandProperty = BindableProperty.Create(
        nameof(RangeSelectedCommand), typeof(ICommand), typeof(DateTimeRangePicker));

    /// <summary>Executed with the <see cref="DateTimeRange"/> when a range is applied.</summary>
    public ICommand? RangeSelectedCommand
    {
        get => (ICommand?)this.GetValue(RangeSelectedCommandProperty);
        set => this.SetValue(RangeSelectedCommandProperty, value);
    }

    public static readonly BindableProperty StartDateTimeProperty = BindableProperty.Create(
        nameof(StartDateTime), typeof(DateTime?), typeof(DateTimeRangePicker), null, BindingMode.TwoWay, propertyChanged: (b, _, _) => Text(b));

    /// <summary>The start of the applied range.</summary>
    public DateTime? StartDateTime
    {
        get => (DateTime?)this.GetValue(StartDateTimeProperty);
        set => this.SetValue(StartDateTimeProperty, value);
    }

    public static readonly BindableProperty EndDateTimeProperty = BindableProperty.Create(
        nameof(EndDateTime), typeof(DateTime?), typeof(DateTimeRangePicker), null, BindingMode.TwoWay, propertyChanged: (b, _, _) => Text(b));

    /// <summary>The end of the applied range.</summary>
    public DateTime? EndDateTime
    {
        get => (DateTime?)this.GetValue(EndDateTimeProperty);
        set => this.SetValue(EndDateTimeProperty, value);
    }

    public static readonly BindableProperty MinDateProperty = BindableProperty.Create(
        nameof(MinDate), typeof(DateTime?), typeof(DateTimeRangePicker), null);

    /// <summary>The earliest selectable day.</summary>
    public DateTime? MinDate
    {
        get => (DateTime?)this.GetValue(MinDateProperty);
        set => this.SetValue(MinDateProperty, value);
    }

    public static readonly BindableProperty MaxDateProperty = BindableProperty.Create(
        nameof(MaxDate), typeof(DateTime?), typeof(DateTimeRangePicker), null);

    /// <summary>The latest selectable day.</summary>
    public DateTime? MaxDate
    {
        get => (DateTime?)this.GetValue(MaxDateProperty);
        set => this.SetValue(MaxDateProperty, value);
    }

    public static readonly BindableProperty MinDaysProperty = BindableProperty.Create(
        nameof(MinDays), typeof(int?), typeof(DateTimeRangePicker), null);

    /// <summary>The shortest range in calendar days, counting both ends.</summary>
    public int? MinDays
    {
        get => (int?)this.GetValue(MinDaysProperty);
        set => this.SetValue(MinDaysProperty, value);
    }

    public static readonly BindableProperty MaxDaysProperty = BindableProperty.Create(
        nameof(MaxDays), typeof(int?), typeof(DateTimeRangePicker), null);

    /// <summary>The longest range in calendar days, counting both ends.</summary>
    public int? MaxDays
    {
        get => (int?)this.GetValue(MaxDaysProperty);
        set => this.SetValue(MaxDaysProperty, value);
    }

    public static readonly BindableProperty AllowSingleDayProperty = BindableProperty.Create(
        nameof(AllowSingleDay), typeof(bool), typeof(DateTimeRangePicker), true);

    /// <summary>Whether the range may start and end on the same day.</summary>
    public bool AllowSingleDay
    {
        get => (bool)this.GetValue(AllowSingleDayProperty);
        set => this.SetValue(AllowSingleDayProperty, value);
    }

    public static readonly BindableProperty DisabledDatesProperty = BindableProperty.Create(
        nameof(DisabledDates), typeof(IList<DateTime>), typeof(DateTimeRangePicker), null);

    /// <summary>Specific days that can never be picked.</summary>
    public IList<DateTime>? DisabledDates
    {
        get => (IList<DateTime>?)this.GetValue(DisabledDatesProperty);
        set => this.SetValue(DisabledDatesProperty, value);
    }

    public static readonly BindableProperty DisabledDaysOfWeekProperty = BindableProperty.Create(
        nameof(DisabledDaysOfWeek), typeof(IList<DayOfWeek>), typeof(DateTimeRangePicker), null);

    /// <summary>Weekdays that can never be picked.</summary>
    public IList<DayOfWeek>? DisabledDaysOfWeek
    {
        get => (IList<DayOfWeek>?)this.GetValue(DisabledDaysOfWeekProperty);
        set => this.SetValue(DisabledDaysOfWeekProperty, value);
    }

    public static readonly BindableProperty IsDateDisabledProperty = BindableProperty.Create(
        nameof(IsDateDisabled), typeof(Func<DateOnly, bool>), typeof(DateTimeRangePicker), null);

    /// <summary>Anything else that rules a day out.</summary>
    public Func<DateOnly, bool>? IsDateDisabled
    {
        get => (Func<DateOnly, bool>?)this.GetValue(IsDateDisabledProperty);
        set => this.SetValue(IsDateDisabledProperty, value);
    }

    public static readonly BindableProperty AllowDisabledDatesInRangeProperty = BindableProperty.Create(
        nameof(AllowDisabledDatesInRange), typeof(bool), typeof(DateTimeRangePicker), false);

    /// <summary>Whether the range may run across disabled days.</summary>
    public bool AllowDisabledDatesInRange
    {
        get => (bool)this.GetValue(AllowDisabledDatesInRangeProperty);
        set => this.SetValue(AllowDisabledDatesInRangeProperty, value);
    }

    public static readonly BindableProperty FirstDayOfWeekProperty = BindableProperty.Create(
        nameof(FirstDayOfWeek), typeof(DayOfWeek?), typeof(DateTimeRangePicker), null);

    /// <summary>The first calendar column. Null follows the culture.</summary>
    public DayOfWeek? FirstDayOfWeek
    {
        get => (DayOfWeek?)this.GetValue(FirstDayOfWeekProperty);
        set => this.SetValue(FirstDayOfWeekProperty, value);
    }

    public static readonly BindableProperty PresetsProperty = BindableProperty.Create(
        nameof(Presets), typeof(IList<DateRangePreset>), typeof(DateTimeRangePicker), null);

    /// <summary>Shortcut chips above the calendar.</summary>
    public IList<DateRangePreset>? Presets
    {
        get => (IList<DateRangePreset>?)this.GetValue(PresetsProperty);
        set => this.SetValue(PresetsProperty, value);
    }

    public static readonly BindableProperty IntervalProperty = BindableProperty.Create(
        nameof(Interval), typeof(TimeSpan), typeof(DateTimeRangePicker), TimeSpan.FromMinutes(15));

    /// <summary>The step between selectable times.</summary>
    public TimeSpan Interval
    {
        get => (TimeSpan)this.GetValue(IntervalProperty);
        set => this.SetValue(IntervalProperty, value);
    }

    public static readonly BindableProperty MinDurationProperty = BindableProperty.Create(
        nameof(MinDuration), typeof(TimeSpan?), typeof(DateTimeRangePicker), null);

    /// <summary>The shortest the whole range may be.</summary>
    public TimeSpan? MinDuration
    {
        get => (TimeSpan?)this.GetValue(MinDurationProperty);
        set => this.SetValue(MinDurationProperty, value);
    }

    public static readonly BindableProperty MaxDurationProperty = BindableProperty.Create(
        nameof(MaxDuration), typeof(TimeSpan?), typeof(DateTimeRangePicker), null);

    /// <summary>The longest the whole range may be.</summary>
    public TimeSpan? MaxDuration
    {
        get => (TimeSpan?)this.GetValue(MaxDurationProperty);
        set => this.SetValue(MaxDurationProperty, value);
    }

    public static readonly BindableProperty DefaultStartTimeProperty = BindableProperty.Create(
        nameof(DefaultStartTime), typeof(TimeSpan), typeof(DateTimeRangePicker), TimeSpan.FromHours(9));

    /// <summary>The start time offered when there is no value yet.</summary>
    public TimeSpan DefaultStartTime
    {
        get => (TimeSpan)this.GetValue(DefaultStartTimeProperty);
        set => this.SetValue(DefaultStartTimeProperty, value);
    }

    public static readonly BindableProperty DefaultEndTimeProperty = BindableProperty.Create(
        nameof(DefaultEndTime), typeof(TimeSpan), typeof(DateTimeRangePicker), TimeSpan.FromHours(17));

    /// <summary>The end time offered when there is no value yet.</summary>
    public TimeSpan DefaultEndTime
    {
        get => (TimeSpan)this.GetValue(DefaultEndTimeProperty);
        set => this.SetValue(DefaultEndTimeProperty, value);
    }

    public static readonly BindableProperty StartTimeTextProperty = BindableProperty.Create(
        nameof(StartTimeText), typeof(string), typeof(DateTimeRangePicker), "Start");

    /// <summary>The start row's caption.</summary>
    public string StartTimeText
    {
        get => (string)this.GetValue(StartTimeTextProperty);
        set => this.SetValue(StartTimeTextProperty, value);
    }

    public static readonly BindableProperty EndTimeTextProperty = BindableProperty.Create(
        nameof(EndTimeText), typeof(string), typeof(DateTimeRangePicker), "End");

    /// <summary>The end row's caption.</summary>
    public string EndTimeText
    {
        get => (string)this.GetValue(EndTimeTextProperty);
        set => this.SetValue(EndTimeTextProperty, value);
    }
}
