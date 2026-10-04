using System.Windows.Input;
using Shiny.Controls.RangePickers;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls;

public partial class TimeRangePicker
{
    static void Text(BindableObject b) => StyleGuard.WhenReady<TimeRangePicker>(b, p => p.RefreshText());

    public static readonly BindableProperty RangeSelectedCommandProperty = BindableProperty.Create(
        nameof(RangeSelectedCommand), typeof(ICommand), typeof(TimeRangePicker));

    /// <summary>Executed with the <see cref="TimeRange"/> when a range is applied.</summary>
    public ICommand? RangeSelectedCommand
    {
        get => (ICommand?)this.GetValue(RangeSelectedCommandProperty);
        set => this.SetValue(RangeSelectedCommandProperty, value);
    }

    public static readonly BindableProperty StartTimeProperty = BindableProperty.Create(
        nameof(StartTime), typeof(TimeSpan?), typeof(TimeRangePicker), null, BindingMode.TwoWay, propertyChanged: (b, _, _) => Text(b));

    /// <summary>The start of the applied range.</summary>
    public TimeSpan? StartTime
    {
        get => (TimeSpan?)this.GetValue(StartTimeProperty);
        set => this.SetValue(StartTimeProperty, value);
    }

    public static readonly BindableProperty EndTimeProperty = BindableProperty.Create(
        nameof(EndTime), typeof(TimeSpan?), typeof(TimeRangePicker), null, BindingMode.TwoWay, propertyChanged: (b, _, _) => Text(b));

    /// <summary>The end of the applied range. Earlier than StartTime means it runs past midnight.</summary>
    public TimeSpan? EndTime
    {
        get => (TimeSpan?)this.GetValue(EndTimeProperty);
        set => this.SetValue(EndTimeProperty, value);
    }

    public static readonly BindableProperty IntervalProperty = BindableProperty.Create(
        nameof(Interval), typeof(TimeSpan), typeof(TimeRangePicker), TimeSpan.FromMinutes(15));

    /// <summary>The step between selectable times.</summary>
    public TimeSpan Interval
    {
        get => (TimeSpan)this.GetValue(IntervalProperty);
        set => this.SetValue(IntervalProperty, value);
    }

    public static readonly BindableProperty MinTimeProperty = BindableProperty.Create(
        nameof(MinTime), typeof(TimeSpan?), typeof(TimeRangePicker), null);

    /// <summary>The earliest selectable time.</summary>
    public TimeSpan? MinTime
    {
        get => (TimeSpan?)this.GetValue(MinTimeProperty);
        set => this.SetValue(MinTimeProperty, value);
    }

    public static readonly BindableProperty MaxTimeProperty = BindableProperty.Create(
        nameof(MaxTime), typeof(TimeSpan?), typeof(TimeRangePicker), null);

    /// <summary>The latest selectable time.</summary>
    public TimeSpan? MaxTime
    {
        get => (TimeSpan?)this.GetValue(MaxTimeProperty);
        set => this.SetValue(MaxTimeProperty, value);
    }

    public static readonly BindableProperty MinDurationProperty = BindableProperty.Create(
        nameof(MinDuration), typeof(TimeSpan?), typeof(TimeRangePicker), null);

    /// <summary>The shortest range. Defaults to one Interval.</summary>
    public TimeSpan? MinDuration
    {
        get => (TimeSpan?)this.GetValue(MinDurationProperty);
        set => this.SetValue(MinDurationProperty, value);
    }

    public static readonly BindableProperty MaxDurationProperty = BindableProperty.Create(
        nameof(MaxDuration), typeof(TimeSpan?), typeof(TimeRangePicker), null);

    /// <summary>The longest range.</summary>
    public TimeSpan? MaxDuration
    {
        get => (TimeSpan?)this.GetValue(MaxDurationProperty);
        set => this.SetValue(MaxDurationProperty, value);
    }

    public static readonly BindableProperty AllowOvernightProperty = BindableProperty.Create(
        nameof(AllowOvernight), typeof(bool), typeof(TimeRangePicker), false);

    /// <summary>Whether the end may wrap past midnight (a 22:00 – 06:00 shift). Ignored when MinTime or MaxTime is set.</summary>
    public bool AllowOvernight
    {
        get => (bool)this.GetValue(AllowOvernightProperty);
        set => this.SetValue(AllowOvernightProperty, value);
    }

    public static readonly BindableProperty StartHeaderTextProperty = BindableProperty.Create(
        nameof(StartHeaderText), typeof(string), typeof(TimeRangePicker), "Start");

    /// <summary>The left column's heading.</summary>
    public string StartHeaderText
    {
        get => (string)this.GetValue(StartHeaderTextProperty);
        set => this.SetValue(StartHeaderTextProperty, value);
    }

    public static readonly BindableProperty EndHeaderTextProperty = BindableProperty.Create(
        nameof(EndHeaderText), typeof(string), typeof(TimeRangePicker), "End");

    /// <summary>The right column's heading.</summary>
    public string EndHeaderText
    {
        get => (string)this.GetValue(EndHeaderTextProperty);
        set => this.SetValue(EndHeaderTextProperty, value);
    }

    public static readonly BindableProperty PickStartHintTextProperty = BindableProperty.Create(
        nameof(PickStartHintText), typeof(string), typeof(TimeRangePicker), "Pick a start time");

    /// <summary>Shown in the end column until a start is picked.</summary>
    public string PickStartHintText
    {
        get => (string)this.GetValue(PickStartHintTextProperty);
        set => this.SetValue(PickStartHintTextProperty, value);
    }
}
