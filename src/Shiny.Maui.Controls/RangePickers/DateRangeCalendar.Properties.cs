using System.Globalization;
using System.Windows.Input;
using Shiny.Controls.RangePickers;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls;

public partial class DateRangeCalendar
{
    static void Sync(BindableObject b) => StyleGuard.WhenReady<DateRangeCalendar>(b, c => c.PullFromProperties());

    static void Constrain(BindableObject b) => StyleGuard.WhenReady<DateRangeCalendar>(b, c =>
    {
        c.RebuildConstraints();
        c.Refresh();
    });

    static void Redraw(BindableObject b) => StyleGuard.WhenReady<DateRangeCalendar>(b, c => c.Refresh());


    public static readonly BindableProperty StartDateProperty = BindableProperty.Create(
        nameof(StartDate), typeof(DateTime?), typeof(DateRangeCalendar), null, BindingMode.TwoWay,
        propertyChanged: (b, _, _) => Sync(b));

    /// <summary>The first day of the range. Set alone, it is a half-picked range waiting for its end.</summary>
    public DateTime? StartDate
    {
        get => (DateTime?)this.GetValue(StartDateProperty);
        set => this.SetValue(StartDateProperty, value);
    }

    public static readonly BindableProperty EndDateProperty = BindableProperty.Create(
        nameof(EndDate), typeof(DateTime?), typeof(DateRangeCalendar), null, BindingMode.TwoWay,
        propertyChanged: (b, _, _) => Sync(b));

    /// <summary>The last day of the range, inclusive.</summary>
    public DateTime? EndDate
    {
        get => (DateTime?)this.GetValue(EndDateProperty);
        set => this.SetValue(EndDateProperty, value);
    }

    public static readonly BindableProperty MinDateProperty = BindableProperty.Create(
        nameof(MinDate), typeof(DateTime?), typeof(DateRangeCalendar), null, propertyChanged: (b, _, _) => Constrain(b));

    /// <summary>The earliest selectable day.</summary>
    public DateTime? MinDate
    {
        get => (DateTime?)this.GetValue(MinDateProperty);
        set => this.SetValue(MinDateProperty, value);
    }

    public static readonly BindableProperty MaxDateProperty = BindableProperty.Create(
        nameof(MaxDate), typeof(DateTime?), typeof(DateRangeCalendar), null, propertyChanged: (b, _, _) => Constrain(b));

    /// <summary>The latest selectable day.</summary>
    public DateTime? MaxDate
    {
        get => (DateTime?)this.GetValue(MaxDateProperty);
        set => this.SetValue(MaxDateProperty, value);
    }

    public static readonly BindableProperty MinDaysProperty = BindableProperty.Create(
        nameof(MinDays), typeof(int?), typeof(DateRangeCalendar), null, propertyChanged: (b, _, _) => Constrain(b));

    /// <summary>The shortest range in days, counting both ends.</summary>
    public int? MinDays
    {
        get => (int?)this.GetValue(MinDaysProperty);
        set => this.SetValue(MinDaysProperty, value);
    }

    public static readonly BindableProperty MaxDaysProperty = BindableProperty.Create(
        nameof(MaxDays), typeof(int?), typeof(DateRangeCalendar), null, propertyChanged: (b, _, _) => Constrain(b));

    /// <summary>The longest range in days, counting both ends.</summary>
    public int? MaxDays
    {
        get => (int?)this.GetValue(MaxDaysProperty);
        set => this.SetValue(MaxDaysProperty, value);
    }

    public static readonly BindableProperty AllowSingleDayProperty = BindableProperty.Create(
        nameof(AllowSingleDay), typeof(bool), typeof(DateRangeCalendar), true, propertyChanged: (b, _, _) => Constrain(b));

    /// <summary>Whether a range may start and end on the same day.</summary>
    public bool AllowSingleDay
    {
        get => (bool)this.GetValue(AllowSingleDayProperty);
        set => this.SetValue(AllowSingleDayProperty, value);
    }

    public static readonly BindableProperty DisabledDatesProperty = BindableProperty.Create(
        nameof(DisabledDates), typeof(IList<DateTime>), typeof(DateRangeCalendar), null, propertyChanged: (b, _, _) => Constrain(b));

    /// <summary>Specific days that can never be picked.</summary>
    public IList<DateTime>? DisabledDates
    {
        get => (IList<DateTime>?)this.GetValue(DisabledDatesProperty);
        set => this.SetValue(DisabledDatesProperty, value);
    }

    public static readonly BindableProperty DisabledDaysOfWeekProperty = BindableProperty.Create(
        nameof(DisabledDaysOfWeek), typeof(IList<DayOfWeek>), typeof(DateRangeCalendar), null, propertyChanged: (b, _, _) => Constrain(b));

    /// <summary>Weekdays that can never be picked.</summary>
    public IList<DayOfWeek>? DisabledDaysOfWeek
    {
        get => (IList<DayOfWeek>?)this.GetValue(DisabledDaysOfWeekProperty);
        set => this.SetValue(DisabledDaysOfWeekProperty, value);
    }

    public static readonly BindableProperty IsDateDisabledProperty = BindableProperty.Create(
        nameof(IsDateDisabled), typeof(Func<DateOnly, bool>), typeof(DateRangeCalendar), null, propertyChanged: (b, _, _) => Constrain(b));

    /// <summary>Anything else that rules a day out.</summary>
    public Func<DateOnly, bool>? IsDateDisabled
    {
        get => (Func<DateOnly, bool>?)this.GetValue(IsDateDisabledProperty);
        set => this.SetValue(IsDateDisabledProperty, value);
    }

    public static readonly BindableProperty AllowDisabledDatesInRangeProperty = BindableProperty.Create(
        nameof(AllowDisabledDatesInRange), typeof(bool), typeof(DateRangeCalendar), false, propertyChanged: (b, _, _) => Constrain(b));

    /// <summary>Whether a range may run across disabled days. Off: a stay cannot straddle a booked night.</summary>
    public bool AllowDisabledDatesInRange
    {
        get => (bool)this.GetValue(AllowDisabledDatesInRangeProperty);
        set => this.SetValue(AllowDisabledDatesInRangeProperty, value);
    }

    public static readonly BindableProperty FirstDayOfWeekProperty = BindableProperty.Create(
        nameof(FirstDayOfWeek), typeof(DayOfWeek?), typeof(DateRangeCalendar), null, propertyChanged: (b, _, _) => Redraw(b));

    /// <summary>The first column. Null follows the culture.</summary>
    public DayOfWeek? FirstDayOfWeek
    {
        get => (DayOfWeek?)this.GetValue(FirstDayOfWeekProperty);
        set => this.SetValue(FirstDayOfWeekProperty, value);
    }

    public static readonly BindableProperty DisplayMonthProperty = BindableProperty.Create(
        nameof(DisplayMonth), typeof(DateTime), typeof(DateRangeCalendar), default(DateTime), BindingMode.TwoWay,
        coerceValue: (_, v) => v is DateTime d && d != default ? new DateTime(d.Year, d.Month, 1) : new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1),
        defaultValueCreator: _ => new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1),
        propertyChanged: (b, _, _) => Redraw(b));

    /// <summary>The first month on screen.</summary>
    public DateTime DisplayMonth
    {
        get => (DateTime)this.GetValue(DisplayMonthProperty);
        set => this.SetValue(DisplayMonthProperty, value);
    }

    public static readonly BindableProperty MonthsProperty = BindableProperty.Create(
        nameof(Months), typeof(int), typeof(DateRangeCalendar), 1,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady<DateRangeCalendar>(b, c =>
        {
            c.RebuildMonths();
            c.Refresh();
        }));

    /// <summary>How many months to show side by side. One suits a phone; two a tablet or desktop.</summary>
    public int Months
    {
        get => (int)this.GetValue(MonthsProperty);
        set => this.SetValue(MonthsProperty, value);
    }

    public static readonly BindableProperty ShowOutsideDaysProperty = BindableProperty.Create(
        nameof(ShowOutsideDays), typeof(bool), typeof(DateRangeCalendar), false, propertyChanged: (b, _, _) => Redraw(b));

    /// <summary>Show the neighbouring months' days in the grid padding. Off by default; they never carry range styling either way.</summary>
    public bool ShowOutsideDays
    {
        get => (bool)this.GetValue(ShowOutsideDaysProperty);
        set => this.SetValue(ShowOutsideDaysProperty, value);
    }

    public static readonly BindableProperty PresetsProperty = BindableProperty.Create(
        nameof(Presets), typeof(IList<DateRangePreset>), typeof(DateRangeCalendar), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady<DateRangeCalendar>(b, c => c.RebuildPresets()));

    /// <summary>Shortcut chips above the calendar - see <see cref="DateRangePresets"/>.</summary>
    public IList<DateRangePreset>? Presets
    {
        get => (IList<DateRangePreset>?)this.GetValue(PresetsProperty);
        set => this.SetValue(PresetsProperty, value);
    }

    public static readonly BindableProperty CultureProperty = BindableProperty.Create(
        nameof(Culture), typeof(CultureInfo), typeof(DateRangeCalendar), null, propertyChanged: (b, _, _) => Redraw(b));

    /// <summary>Month and day names, and the default first day of the week. Null follows the current culture.</summary>
    public CultureInfo? Culture
    {
        get => (CultureInfo?)this.GetValue(CultureProperty);
        set => this.SetValue(CultureProperty, value);
    }

    public static readonly BindableProperty RangeSelectedCommandProperty = BindableProperty.Create(
        nameof(RangeSelectedCommand), typeof(ICommand), typeof(DateRangeCalendar));

    /// <summary>Executed with the <see cref="DateRange"/> when a range is completed.</summary>
    public ICommand? RangeSelectedCommand
    {
        get => (ICommand?)this.GetValue(RangeSelectedCommandProperty);
        set => this.SetValue(RangeSelectedCommandProperty, value);
    }
}
