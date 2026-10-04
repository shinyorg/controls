using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Shiny.Controls.RangePickers;

namespace Shiny.Blazor.Controls;

/// <summary>
/// An inline calendar for picking a range of days: the first click sets the start, the second the
/// end, and a third starts over. The Blazor twin of MAUI's <c>DateRangeCalendar</c>; the selection
/// rules are the shared engine's, so the two hosts pick identically.
/// </summary>
/// <example>
/// <code>
/// &lt;DateRangeCalendar @bind-StartDate="from" @bind-EndDate="to" Months="2"
///                    Presets="@DateRangePresets.Reporting(DayOfWeek.Monday)" /&gt;
/// </code>
/// </example>
public partial class DateRangeCalendar
{
    readonly DateRangeSelector selector = new();
    DateOnly displayMonth;
    DateOnly? lastDisplayParameter;
    bool initialised;

    // The StartDate/EndDate last received. Only a change is applied, so a host that listens to
    // RangeChanged without binding (and keeps passing null) does not wipe what the user is picking.
    DateOnly? lastStartParameter;
    DateOnly? lastEndParameter;

    // Set while our own Start/EndDateChanged callbacks run. The host re-renders us between the two,
    // with a new start and the old end, and applying that half-updated pair would pair the new start
    // with the previous range's end.
    bool reporting;

    DateOnly? focusedDate;
    bool pendingFocus;
    bool skipRender;
    ElementReference focusRef;

    /// <summary>The first day of the range, or the lone day picked so far.</summary>
    [Parameter] public DateOnly? StartDate { get; set; }
    [Parameter] public EventCallback<DateOnly?> StartDateChanged { get; set; }

    /// <summary>The last day of the range. Null while only a start is picked.</summary>
    [Parameter] public DateOnly? EndDate { get; set; }
    [Parameter] public EventCallback<DateOnly?> EndDateChanged { get; set; }

    /// <summary>Raised when a range is completed (both ends picked, or a preset chosen) or cleared.</summary>
    [Parameter] public EventCallback<DateRange?> RangeChanged { get; set; }

    /// <summary>The earliest selectable day.</summary>
    [Parameter] public DateOnly? MinDate { get; set; }

    /// <summary>The latest selectable day.</summary>
    [Parameter] public DateOnly? MaxDate { get; set; }

    /// <summary>The shortest range in days, counting both ends.</summary>
    [Parameter] public int? MinDays { get; set; }

    /// <summary>The longest range in days, counting both ends.</summary>
    [Parameter] public int? MaxDays { get; set; }

    /// <summary>Whether a range may start and end on the same day.</summary>
    [Parameter] public bool AllowSingleDay { get; set; } = true;

    /// <summary>Specific days that can never be picked.</summary>
    [Parameter] public IEnumerable<DateOnly>? DisabledDates { get; set; }

    /// <summary>Days of the week that can never be picked.</summary>
    [Parameter] public IEnumerable<DayOfWeek>? DisabledDaysOfWeek { get; set; }

    /// <summary>Anything else that rules a day out.</summary>
    [Parameter] public Func<DateOnly, bool>? IsDateDisabled { get; set; }

    /// <summary>Whether a range may run across disabled days. Off: a stay cannot straddle a booked night.</summary>
    [Parameter] public bool AllowDisabledDatesInRange { get; set; }

    /// <summary>The first column. Null follows <see cref="Culture"/>.</summary>
    [Parameter] public DayOfWeek? FirstDayOfWeek { get; set; }

    /// <summary>How many months to show side by side. They wrap on narrow widths.</summary>
    [Parameter] public int Months { get; set; } = 1;

    /// <summary>The first visible month (any day in it).</summary>
    [Parameter] public DateOnly? DisplayMonth { get; set; }
    [Parameter] public EventCallback<DateOnly?> DisplayMonthChanged { get; set; }

    /// <summary>Shortcuts listed beside the months. The one matching the current range is highlighted.</summary>
    [Parameter] public IReadOnlyList<DateRangePreset>? Presets { get; set; }

    /// <summary>Month names, day names and the first day of the week. Null uses the current culture.</summary>
    [Parameter] public CultureInfo? Culture { get; set; }

    /// <summary>Overrides today - for tests, or a host showing another time zone's day.</summary>
    [Parameter] public DateOnly? Today { get; set; }

    [Parameter] public string PreviousMonthText { get; set; } = "Previous month";

    [Parameter] public string NextMonthText { get; set; } = "Next month";

    [Parameter] public string? Class { get; set; }

    [Parameter] public string? Style { get; set; }


    internal DateRangeSelector Selector => this.selector;

    internal DateOnly VisibleMonth => this.displayMonth;

    CultureInfo CultureOrCurrent => this.Culture ?? CultureInfo.CurrentCulture;

    DayOfWeek FirstDay => this.FirstDayOfWeek ?? CalendarMath.FirstDayOfWeek(this.CultureOrCurrent);

    int MonthCount => Math.Clamp(this.Months, 1, 12);

    DateOnly TodayValue => this.Today ?? DateOnly.FromDateTime(DateTime.Today);


    protected override void OnParametersSet()
    {
        this.selector.Constraints = new DateRangeConstraints
        {
            MinDate = this.MinDate,
            MaxDate = this.MaxDate,
            MinDays = this.MinDays,
            MaxDays = this.MaxDays,
            AllowSingleDay = this.AllowSingleDay,
            DisabledDates = this.DisabledDates is null ? null : new HashSet<DateOnly>(this.DisabledDates),
            DisabledDaysOfWeek = this.DisabledDaysOfWeek is null ? null : new HashSet<DayOfWeek>(this.DisabledDaysOfWeek),
            IsDateDisabled = this.IsDateDisabled,
            AllowDisabledDatesInRange = this.AllowDisabledDatesInRange
        };
        this.selector.Today = this.TodayValue;

        var changed = this.StartDate != this.lastStartParameter || this.EndDate != this.lastEndParameter;
        this.lastStartParameter = this.StartDate;
        this.lastEndParameter = this.EndDate;

        if (!this.initialised || (changed && !this.reporting))
        {
            if (this.StartDate is { } s && this.EndDate is { } e)
                this.selector.SetRange(new DateRange(s, e));
            else
                this.selector.SetStart(this.StartDate ?? this.EndDate);
        }

        if (this.DisplayMonth is { } shown && shown != this.lastDisplayParameter)
        {
            this.displayMonth = CalendarMath.StartOfMonth(shown);
        }
        else if (!this.initialised)
        {
            this.displayMonth = CalendarMath.StartOfMonth(this.StartDate ?? this.EndDate ?? this.TodayValue);
        }
        this.lastDisplayParameter = this.DisplayMonth;
        this.initialised = true;
    }


    protected override bool ShouldRender()
    {
        if (!this.skipRender)
            return true;

        this.skipRender = false;
        return false;
    }


    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!this.pendingFocus)
            return;

        this.pendingFocus = false;
        try
        {
            await this.focusRef.FocusAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or Microsoft.JSInterop.JSException or ObjectDisposedException or TaskCanceledException)
        {
        }
    }


    /// <summary>Picks a day, exactly as a click on it would.</summary>
    public async Task TapAsync(DateOnly date)
    {
        var result = this.selector.Tap(date);
        if (result == DateRangeTapResult.Ignored)
            return;

        this.focusedDate = date;
        await this.ReportAsync(result == DateRangeTapResult.Completed);
        this.StateHasChanged();
    }


    /// <summary>Selects a preset's range and shows its first month.</summary>
    public async Task ApplyPresetAsync(DateRangePreset preset)
    {
        var range = preset.Resolve(this.TodayValue);
        this.selector.SetRange(range);
        this.focusedDate = range.Start;
        await this.ShowMonthAsync(range.Start);
        await this.ReportAsync(true);
        this.StateHasChanged();
    }


    /// <summary>Removes the selection.</summary>
    public async Task ClearAsync()
    {
        this.selector.Clear();
        await this.ReportAsync(true);
        this.StateHasChanged();
    }


    async Task ReportAsync(bool rangeEvent)
    {
        this.reporting = true;
        try
        {
            await this.StartDateChanged.InvokeAsync(this.selector.Start);
            await this.EndDateChanged.InvokeAsync(this.selector.End);
        }
        finally
        {
            this.reporting = false;
        }

        if (rangeEvent)
            await this.RangeChanged.InvokeAsync(this.selector.Range);
    }


    Task ShiftMonthAsync(int months) => this.ShowMonthAsync(this.displayMonth.AddMonths(months));


    async Task ShowMonthAsync(DateOnly date)
    {
        var first = CalendarMath.StartOfMonth(date);

        // A date already on screen in a later month of a multi-month view needs no paging.
        if (first >= this.displayMonth && first < this.displayMonth.AddMonths(this.MonthCount))
            return;

        this.displayMonth = first;
        this.lastDisplayParameter = first;
        await this.DisplayMonthChanged.InvokeAsync(first);
    }


    void OnHover(DateOnly date)
    {
        // Only a half-picked range previews anything, and on Blazor Server every unneeded render is a
        // round trip per day the pointer crosses.
        if (!this.selector.IsPickingEnd || this.selector.Hover == date)
        {
            this.skipRender = true;
            return;
        }
        this.selector.Hover = date;
    }


    void OnLeave()
    {
        if (this.selector.Hover is null)
        {
            this.skipRender = true;
            return;
        }
        this.selector.Hover = null;
    }


    async Task OnKeyDownAsync(KeyboardEventArgs e, DateOnly date)
    {
        DateOnly? target = e.Key switch
        {
            "ArrowLeft" => date.AddDays(-1),
            "ArrowRight" => date.AddDays(1),
            "ArrowUp" => date.AddDays(-7),
            "ArrowDown" => date.AddDays(7),
            "PageUp" => date.AddMonths(-1),
            "PageDown" => date.AddMonths(1),
            "Home" => CalendarMath.StartOfWeek(date, this.FirstDay),
            "End" => CalendarMath.StartOfWeek(date, this.FirstDay).AddDays(6),
            _ => null
        };

        if (target is not { } next)
        {
            // Enter and Space reach the button as a click; nothing else here is ours.
            this.skipRender = true;
            return;
        }

        this.focusedDate = next;
        this.pendingFocus = true;
        this.selector.Hover = this.selector.IsPickingEnd ? next : null;
        await this.ShowMonthAsync(next);
    }


    /// <summary>The one day in a month that takes Tab focus - roving tabindex, so Tab leaves the grid in one step.</summary>
    DateOnly? FocusDate(DateOnly month)
    {
        bool InMonth(DateOnly d) => d.Year == month.Year && d.Month == month.Month;

        if (this.focusedDate is { } f)
            return InMonth(f) ? f : null;

        if (this.selector.Start is { } s && InMonth(s))
            return s;

        if (InMonth(this.TodayValue))
            return this.TodayValue;

        // The first month of the view gets a focus stop even when nothing in it is special.
        return month == this.displayMonth ? month : null;
    }


    bool IsActivePreset(DateRangePreset preset)
        => this.selector.Range is { } range && preset.Resolve(this.TodayValue) == range;


    string DayLabel(DateOnly date) => date.ToString(this.CultureOrCurrent.DateTimeFormat.LongDatePattern, this.CultureOrCurrent);


    static string CellClass(CalendarDayState state)
    {
        var start = (state & CalendarDayState.RangeStart) != 0;
        var end = (state & CalendarDayState.RangeEnd) != 0;

        if (start && end)
            return "";
        if (start)
            return "band-start";
        if (end)
            return "band-end";
        if ((state & CalendarDayState.InRange) != 0)
            return "band";
        if ((state & CalendarDayState.PreviewEnd) != 0)
            return "preview-end";
        if ((state & CalendarDayState.Preview) != 0)
            return "preview";
        return "";
    }


    static string DayClass(CalendarDayState state)
    {
        var classes = new List<string>(4);
        if ((state & CalendarDayState.RangeStart) != 0) classes.Add("is-start");
        if ((state & CalendarDayState.RangeEnd) != 0) classes.Add("is-end");
        if ((state & CalendarDayState.InRange) != 0) classes.Add("is-in-range");
        if ((state & CalendarDayState.Today) != 0) classes.Add("is-today");
        if ((state & CalendarDayState.Disabled) != 0) classes.Add("is-disabled");
        if ((state & CalendarDayState.Unavailable) != 0) classes.Add("is-unavailable");
        if ((state & CalendarDayState.OutsideMonth) != 0) classes.Add("is-outside");
        if ((state & CalendarDayState.PreviewEnd) != 0) classes.Add("is-preview-end");
        return string.Join(' ', classes);
    }
}
