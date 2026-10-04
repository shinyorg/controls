using System.Windows.Input;
using Shiny.Controls.RangePickers;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls;

public partial class DateRangePicker
{
    static void Text(BindableObject b) => StyleGuard.WhenReady<DateRangePicker>(b, p => p.RefreshText());

    public static readonly BindableProperty StartDateProperty = BindableProperty.Create(
        nameof(StartDate), typeof(DateTime?), typeof(DateRangePicker), null, BindingMode.TwoWay, propertyChanged: (b, _, _) => Text(b));

    /// <summary>The first day of the applied range.</summary>
    public DateTime? StartDate
    {
        get => (DateTime?)this.GetValue(StartDateProperty);
        set => this.SetValue(StartDateProperty, value);
    }

    public static readonly BindableProperty EndDateProperty = BindableProperty.Create(
        nameof(EndDate), typeof(DateTime?), typeof(DateRangePicker), null, BindingMode.TwoWay, propertyChanged: (b, _, _) => Text(b));

    /// <summary>The last day of the applied range, inclusive.</summary>
    public DateTime? EndDate
    {
        get => (DateTime?)this.GetValue(EndDateProperty);
        set => this.SetValue(EndDateProperty, value);
    }

    public static readonly BindableProperty RangeSelectedCommandProperty = BindableProperty.Create(
        nameof(RangeSelectedCommand), typeof(ICommand), typeof(DateRangePicker));

    /// <summary>Executed with the <see cref="DateRange"/> when a range is applied.</summary>
    public ICommand? RangeSelectedCommand
    {
        get => (ICommand?)this.GetValue(RangeSelectedCommandProperty);
        set => this.SetValue(RangeSelectedCommandProperty, value);
    }

    public static readonly BindableProperty MinDateProperty = BindableProperty.Create(
        nameof(MinDate), typeof(DateTime?), typeof(DateRangePicker), null);

    /// <summary>The earliest selectable day.</summary>
    public DateTime? MinDate
    {
        get => (DateTime?)this.GetValue(MinDateProperty);
        set => this.SetValue(MinDateProperty, value);
    }

    public static readonly BindableProperty MaxDateProperty = BindableProperty.Create(
        nameof(MaxDate), typeof(DateTime?), typeof(DateRangePicker), null);

    /// <summary>The latest selectable day.</summary>
    public DateTime? MaxDate
    {
        get => (DateTime?)this.GetValue(MaxDateProperty);
        set => this.SetValue(MaxDateProperty, value);
    }

    public static readonly BindableProperty MinDaysProperty = BindableProperty.Create(
        nameof(MinDays), typeof(int?), typeof(DateRangePicker), null);

    /// <summary>The shortest range in days, counting both ends.</summary>
    public int? MinDays
    {
        get => (int?)this.GetValue(MinDaysProperty);
        set => this.SetValue(MinDaysProperty, value);
    }

    public static readonly BindableProperty MaxDaysProperty = BindableProperty.Create(
        nameof(MaxDays), typeof(int?), typeof(DateRangePicker), null);

    /// <summary>The longest range in days, counting both ends.</summary>
    public int? MaxDays
    {
        get => (int?)this.GetValue(MaxDaysProperty);
        set => this.SetValue(MaxDaysProperty, value);
    }

    public static readonly BindableProperty AllowSingleDayProperty = BindableProperty.Create(
        nameof(AllowSingleDay), typeof(bool), typeof(DateRangePicker), true);

    /// <summary>Whether a range may start and end on the same day.</summary>
    public bool AllowSingleDay
    {
        get => (bool)this.GetValue(AllowSingleDayProperty);
        set => this.SetValue(AllowSingleDayProperty, value);
    }

    public static readonly BindableProperty DisabledDatesProperty = BindableProperty.Create(
        nameof(DisabledDates), typeof(IList<DateTime>), typeof(DateRangePicker), null);

    /// <summary>Specific days that can never be picked.</summary>
    public IList<DateTime>? DisabledDates
    {
        get => (IList<DateTime>?)this.GetValue(DisabledDatesProperty);
        set => this.SetValue(DisabledDatesProperty, value);
    }

    public static readonly BindableProperty DisabledDaysOfWeekProperty = BindableProperty.Create(
        nameof(DisabledDaysOfWeek), typeof(IList<DayOfWeek>), typeof(DateRangePicker), null);

    /// <summary>Weekdays that can never be picked.</summary>
    public IList<DayOfWeek>? DisabledDaysOfWeek
    {
        get => (IList<DayOfWeek>?)this.GetValue(DisabledDaysOfWeekProperty);
        set => this.SetValue(DisabledDaysOfWeekProperty, value);
    }

    public static readonly BindableProperty IsDateDisabledProperty = BindableProperty.Create(
        nameof(IsDateDisabled), typeof(Func<DateOnly, bool>), typeof(DateRangePicker), null);

    /// <summary>Anything else that rules a day out.</summary>
    public Func<DateOnly, bool>? IsDateDisabled
    {
        get => (Func<DateOnly, bool>?)this.GetValue(IsDateDisabledProperty);
        set => this.SetValue(IsDateDisabledProperty, value);
    }

    public static readonly BindableProperty AllowDisabledDatesInRangeProperty = BindableProperty.Create(
        nameof(AllowDisabledDatesInRange), typeof(bool), typeof(DateRangePicker), false);

    /// <summary>Whether a range may run across disabled days.</summary>
    public bool AllowDisabledDatesInRange
    {
        get => (bool)this.GetValue(AllowDisabledDatesInRangeProperty);
        set => this.SetValue(AllowDisabledDatesInRangeProperty, value);
    }

    public static readonly BindableProperty FirstDayOfWeekProperty = BindableProperty.Create(
        nameof(FirstDayOfWeek), typeof(DayOfWeek?), typeof(DateRangePicker), null);

    /// <summary>The first calendar column. Null follows the culture.</summary>
    public DayOfWeek? FirstDayOfWeek
    {
        get => (DayOfWeek?)this.GetValue(FirstDayOfWeekProperty);
        set => this.SetValue(FirstDayOfWeekProperty, value);
    }

    public static readonly BindableProperty PresetsProperty = BindableProperty.Create(
        nameof(Presets), typeof(IList<DateRangePreset>), typeof(DateRangePicker), null);

    /// <summary>Shortcut chips in the popup - see DateRangePresets.</summary>
    public IList<DateRangePreset>? Presets
    {
        get => (IList<DateRangePreset>?)this.GetValue(PresetsProperty);
        set => this.SetValue(PresetsProperty, value);
    }

    public static readonly BindableProperty MonthsProperty = BindableProperty.Create(
        nameof(Months), typeof(int), typeof(DateRangePicker), 1);

    /// <summary>Months shown side by side in the popup.</summary>
    public int Months
    {
        get => (int)this.GetValue(MonthsProperty);
        set => this.SetValue(MonthsProperty, value);
    }

    public static readonly BindableProperty AutoApplyProperty = BindableProperty.Create(
        nameof(AutoApply), typeof(bool), typeof(DateRangePicker), false);

    /// <summary>Apply and close as soon as a range is completed or a preset is chosen.</summary>
    public bool AutoApply
    {
        get => (bool)this.GetValue(AutoApplyProperty);
        set => this.SetValue(AutoApplyProperty, value);
    }
}
