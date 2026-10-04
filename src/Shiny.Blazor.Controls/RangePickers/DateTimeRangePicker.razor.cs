using System.Globalization;
using Microsoft.AspNetCore.Components;
using Shiny.Controls.RangePickers;

namespace Shiny.Blazor.Controls;

/// <summary>
/// A field that opens a calendar plus a start and an end time. Picking one day gives a same-day range
/// ("Mar 3, 9:00 – 17:00"); picking two spans them. Apply stays disabled until the end is after the
/// start and the length is within <see cref="MinDuration"/> / <see cref="MaxDuration"/>. The Blazor
/// twin of MAUI's <c>DateTimeRangePicker</c>.
/// </summary>
/// <example>
/// <code>
/// &lt;DateTimeRangePicker @bind-StartDateTime="from" @bind-EndDateTime="to" Interval="TimeSpan.FromMinutes(30)" /&gt;
/// </code>
/// </example>
public partial class DateTimeRangePicker
{
    DateOnly? draftStartDate;
    DateOnly? draftEndDate;
    TimeOnly draftStartTime;
    TimeOnly draftEndTime;
    IReadOnlyList<TimeOnly> timeSlots = [];

    [Parameter] public DateTime? StartDateTime { get; set; }
    [Parameter] public EventCallback<DateTime?> StartDateTimeChanged { get; set; }

    [Parameter] public DateTime? EndDateTime { get; set; }
    [Parameter] public EventCallback<DateTime?> EndDateTimeChanged { get; set; }

    /// <summary>Raised when a range is applied or cleared.</summary>
    [Parameter] public EventCallback<DateTimeRange?> RangeChanged { get; set; }

    [Parameter] public DateOnly? MinDate { get; set; }
    [Parameter] public DateOnly? MaxDate { get; set; }
    [Parameter] public int? MinDays { get; set; }
    [Parameter] public int? MaxDays { get; set; }
    [Parameter] public IEnumerable<DateOnly>? DisabledDates { get; set; }
    [Parameter] public IEnumerable<DayOfWeek>? DisabledDaysOfWeek { get; set; }
    [Parameter] public Func<DateOnly, bool>? IsDateDisabled { get; set; }
    [Parameter] public bool AllowDisabledDatesInRange { get; set; }
    [Parameter] public DayOfWeek? FirstDayOfWeek { get; set; }

    /// <summary>The step between selectable times.</summary>
    [Parameter] public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>The shortest whole range, start to end.</summary>
    [Parameter] public TimeSpan? MinDuration { get; set; }

    /// <summary>The longest whole range, start to end.</summary>
    [Parameter] public TimeSpan? MaxDuration { get; set; }

    /// <summary>The start time offered when nothing is selected yet.</summary>
    [Parameter] public TimeOnly DefaultStartTime { get; set; } = new(9, 0);

    /// <summary>The end time offered when nothing is selected yet.</summary>
    [Parameter] public TimeOnly DefaultEndTime { get; set; } = new(17, 0);

    /// <summary>A date-time format for both ends. Null writes the compact form.</summary>
    [Parameter] public string? Format { get; set; }

    [Parameter] public string Separator { get; set; } = RangeFormatter.DefaultSeparator;
    [Parameter] public CultureInfo? Culture { get; set; }
    [Parameter] public DateOnly? Today { get; set; }

    [Parameter] public string Placeholder { get; set; } = "Select dates and times";
    [Parameter] public string? Title { get; set; }
    [Parameter] public string StartText { get; set; } = "Start";
    [Parameter] public string EndText { get; set; } = "End";
    [Parameter] public string ApplyText { get; set; } = "Apply";
    [Parameter] public string CancelText { get; set; } = "Cancel";
    [Parameter] public string ClearText { get; set; } = "Clear";
    [Parameter] public bool ShowClear { get; set; } = true;
    [Parameter] public bool Disabled { get; set; }

    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public EventCallback<bool> IsOpenChanged { get; set; }

    [Parameter] public string? Class { get; set; }
    [Parameter] public string? Style { get; set; }


    CultureInfo CultureOrCurrent => this.Culture ?? CultureInfo.CurrentCulture;

    /// <summary>A lone picked day is a same-day range.</summary>
    DateOnly? EffectiveEndDate => this.draftEndDate ?? this.draftStartDate;

    /// <summary>On a same-day range only times after the start can end it.</summary>
    IReadOnlyList<TimeOnly> EndTimeSlots => this.draftStartDate is not null && this.EffectiveEndDate == this.draftStartDate
        ? [.. this.timeSlots.Where(t => t > this.draftStartTime)]
        : this.timeSlots;

    string EndSlotsKey => this.EndTimeSlots is { Count: > 0 } slots ? $"{slots.Count}-{slots[0]:HHmm}" : "none";

    /// <summary>The draft as a range, or null while it is incomplete or breaks a rule.</summary>
    internal DateTimeRange? DraftRange
    {
        get
        {
            if (this.draftStartDate is not { } sd || this.EffectiveEndDate is not { } ed)
                return null;

            var start = sd.ToDateTime(this.draftStartTime);
            var end = ed.ToDateTime(this.draftEndTime);
            if (end <= start)
                return null;

            var duration = end - start;
            if (this.MinDuration is { } min && duration < min)
                return null;
            if (this.MaxDuration is { } max && duration > max)
                return null;

            return new DateTimeRange(start, end);
        }
    }

    string? DisplayText => this.StartDateTime is { } s && this.EndDateTime is { } e
        ? RangeFormatter.Format(new DateTimeRange(s, e), this.CultureOrCurrent, this.Format, this.Separator)
        : null;

    string? SummaryText => this.DraftRange is { } r
        ? $"{RangeFormatter.Format(r, this.CultureOrCurrent, this.Format, this.Separator)} · {RangeFormatter.FormatDuration(r.Duration)}"
        : null;


    protected override void OnParametersSet()
    {
        this.timeSlots = new TimeRangeConstraints { Interval = this.Interval }.AllSlots();

        if (!this.IsOpen)
            this.ResetDraft();
    }


    void ResetDraft()
    {
        if (this.StartDateTime is { } s && this.EndDateTime is { } e)
        {
            this.draftStartDate = DateOnly.FromDateTime(s);
            var endDate = DateOnly.FromDateTime(e);
            this.draftEndDate = endDate == this.draftStartDate ? null : endDate;
            this.draftStartTime = this.Snap(TimeOnly.FromDateTime(s));
            this.draftEndTime = this.Snap(TimeOnly.FromDateTime(e));
        }
        else
        {
            this.draftStartDate = null;
            this.draftEndDate = null;
            this.draftStartTime = this.Snap(this.DefaultStartTime);
            this.draftEndTime = this.Snap(this.DefaultEndTime);
        }
    }


    TimeOnly Snap(TimeOnly time) => new TimeRangeConstraints { Interval = this.Interval }.Snap(time);


    public Task OpenAsync()
    {
        this.ResetDraft();
        return this.SetOpenAsync(true);
    }


    Task ToggleAsync() => this.IsOpen ? this.CancelAsync() : this.OpenAsync();


    public Task CancelAsync()
    {
        this.ResetDraft();
        return this.SetOpenAsync(false);
    }


    public async Task ApplyAsync()
    {
        if (this.DraftRange is not { } range)
            return;

        await this.CommitAsync(range);
        await this.SetOpenAsync(false);
    }


    public async Task ClearAsync()
    {
        await this.CommitAsync(null);
        this.ResetDraft();
        await this.SetOpenAsync(false);
    }


    internal void OnDraftStartDate(DateOnly? value) => this.draftStartDate = value;

    internal void OnDraftEndDate(DateOnly? value)
    {
        this.draftEndDate = value;
        this.KeepEndAfterStart();
    }


    internal void SetStartTime(TimeOnly time)
    {
        this.draftStartTime = time;
        this.KeepEndAfterStart();
    }


    internal void SetEndTime(TimeOnly time) => this.draftEndTime = time;


    void OnStartTimeChanged(ChangeEventArgs e)
    {
        if (TimeOnly.TryParseExact(e.Value?.ToString(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
            this.SetStartTime(t);
    }


    void OnEndTimeChanged(ChangeEventArgs e)
    {
        if (TimeOnly.TryParseExact(e.Value?.ToString(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
            this.SetEndTime(t);
    }


    /// <summary>A same-day range whose end the new start overtook moves its end to the next slot.</summary>
    void KeepEndAfterStart()
    {
        if (this.EffectiveEndDate != this.draftStartDate || this.draftEndTime > this.draftStartTime)
            return;

        var later = this.EndTimeSlots;
        if (later.Count > 0)
            this.draftEndTime = later[0];
    }


    string FormatDate(DateOnly? date) => date is { } d
        ? RangeFormatter.Format(new DateRange(d, d), this.CultureOrCurrent)
        : "—";


    string FormatTime(TimeOnly time) => time.ToString(this.CultureOrCurrent.DateTimeFormat.ShortTimePattern, this.CultureOrCurrent);


    async Task CommitAsync(DateTimeRange? range)
    {
        this.StartDateTime = range?.Start;
        this.EndDateTime = range?.End;
        await this.StartDateTimeChanged.InvokeAsync(this.StartDateTime);
        await this.EndDateTimeChanged.InvokeAsync(this.EndDateTime);
        await this.RangeChanged.InvokeAsync(range);
    }


    async Task SetOpenAsync(bool open)
    {
        // Public methods are callable from a host's code as well as from our own buttons, so the
        // render is asked for here rather than left to the event dispatch - after the state changes,
        // or it renders the old state.
        if (this.IsOpen != open)
        {
            this.IsOpen = open;
            await this.IsOpenChanged.InvokeAsync(open);
        }
        this.StateHasChanged();
    }
}
