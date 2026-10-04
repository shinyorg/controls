using System.Globalization;
using Microsoft.AspNetCore.Components;
using Shiny.Controls.RangePickers;

namespace Shiny.Blazor.Controls;

/// <summary>
/// A field that opens a range calendar. The calendar edits a draft; Apply commits it, Cancel (or a
/// click outside, or Escape) throws it away. The Blazor twin of MAUI's <c>DateRangePicker</c>.
/// </summary>
/// <example>
/// <code>
/// &lt;DateRangePicker @bind-StartDate="from" @bind-EndDate="to"
///                  Presets="@DateRangePresets.Reporting(DayOfWeek.Monday)" /&gt;
/// </code>
/// </example>
public partial class DateRangePicker
{
    DateOnly? draftStart;
    DateOnly? draftEnd;

    [Parameter] public DateOnly? StartDate { get; set; }
    [Parameter] public EventCallback<DateOnly?> StartDateChanged { get; set; }

    [Parameter] public DateOnly? EndDate { get; set; }
    [Parameter] public EventCallback<DateOnly?> EndDateChanged { get; set; }

    /// <summary>Raised when a range is applied or cleared.</summary>
    [Parameter] public EventCallback<DateRange?> RangeChanged { get; set; }

    [Parameter] public DateOnly? MinDate { get; set; }
    [Parameter] public DateOnly? MaxDate { get; set; }
    [Parameter] public int? MinDays { get; set; }
    [Parameter] public int? MaxDays { get; set; }
    [Parameter] public bool AllowSingleDay { get; set; } = true;
    [Parameter] public IEnumerable<DateOnly>? DisabledDates { get; set; }
    [Parameter] public IEnumerable<DayOfWeek>? DisabledDaysOfWeek { get; set; }
    [Parameter] public Func<DateOnly, bool>? IsDateDisabled { get; set; }
    [Parameter] public bool AllowDisabledDatesInRange { get; set; }
    [Parameter] public DayOfWeek? FirstDayOfWeek { get; set; }

    /// <summary>Months side by side in the popover. The second is hidden on screens narrower than 640px.</summary>
    [Parameter] public int Months { get; set; } = 2;

    [Parameter] public DateOnly? DisplayMonth { get; set; }
    [Parameter] public EventCallback<DateOnly?> DisplayMonthChanged { get; set; }

    [Parameter] public IReadOnlyList<DateRangePreset>? Presets { get; set; }
    [Parameter] public CultureInfo? Culture { get; set; }
    [Parameter] public DateOnly? Today { get; set; }

    [Parameter] public string Placeholder { get; set; } = "Select dates";

    /// <summary>A date format for both ends ("d"). Null writes the compact form ("Mar 3 – 9, 2026").</summary>
    [Parameter] public string? Format { get; set; }

    [Parameter] public string Separator { get; set; } = RangeFormatter.DefaultSeparator;

    [Parameter] public string? Title { get; set; }
    [Parameter] public string ApplyText { get; set; } = "Apply";
    [Parameter] public string CancelText { get; set; } = "Cancel";
    [Parameter] public string ClearText { get; set; } = "Clear";
    [Parameter] public bool ShowClear { get; set; } = true;

    /// <summary>Commit and close as soon as a range is complete or a preset is chosen - no Apply button.</summary>
    [Parameter] public bool AutoApply { get; set; }

    [Parameter] public bool Disabled { get; set; }

    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public EventCallback<bool> IsOpenChanged { get; set; }

    [Parameter] public string? Class { get; set; }
    [Parameter] public string? Style { get; set; }


    CultureInfo CultureOrCurrent => this.Culture ?? CultureInfo.CurrentCulture;

    internal DateOnly? DraftStart => this.draftStart;
    internal DateOnly? DraftEnd => this.draftEnd;

    string? DisplayText => this.StartDate is { } s
        ? RangeFormatter.Format(new DateRange(s, this.EndDate ?? s), this.CultureOrCurrent, this.Format, this.Separator)
        : null;

    string? SummaryText
    {
        get
        {
            if (this.draftStart is not { } s)
                return null;

            if (this.draftEnd is not { } e)
                return RangeFormatter.Format(new DateRange(s, s), this.CultureOrCurrent, this.Format, this.Separator) + Separator + "…";

            var range = new DateRange(s, e);
            var text = RangeFormatter.Format(range, this.CultureOrCurrent, this.Format, this.Separator);
            return range.Days == 1 ? text : $"{text} · {range.Days} days";
        }
    }


    protected override void OnParametersSet()
    {
        // Closed: the draft follows the committed value, so opening always starts from it.
        if (!this.IsOpen)
        {
            this.draftStart = this.StartDate;
            this.draftEnd = this.EndDate;
        }
    }


    /// <summary>Opens the popover with the committed range as the draft.</summary>
    public Task OpenAsync()
    {
        this.draftStart = this.StartDate;
        this.draftEnd = this.EndDate;
        return this.SetOpenAsync(true);
    }


    Task ToggleAsync() => this.IsOpen ? this.CancelAsync() : this.OpenAsync();


    /// <summary>Closes without committing.</summary>
    public Task CancelAsync()
    {
        this.draftStart = this.StartDate;
        this.draftEnd = this.EndDate;
        return this.SetOpenAsync(false);
    }


    /// <summary>Commits the draft and closes. A lone start commits as a single day when single days are allowed.</summary>
    public async Task ApplyAsync()
    {
        if (this.draftStart is not { } start)
            return;

        var end = this.draftEnd ?? start;
        if (this.draftEnd is null && (!this.AllowSingleDay || this.MinDays > 1))
            return;

        await this.CommitAsync(new DateRange(start, end));
        await this.SetOpenAsync(false);
    }


    /// <summary>Clears the committed range and closes.</summary>
    public async Task ClearAsync()
    {
        this.draftStart = null;
        this.draftEnd = null;
        await this.CommitAsync(null);
        await this.SetOpenAsync(false);
    }


    async Task CommitAsync(DateRange? range)
    {
        this.StartDate = range?.Start;
        this.EndDate = range?.End;
        await this.StartDateChanged.InvokeAsync(this.StartDate);
        await this.EndDateChanged.InvokeAsync(this.EndDate);
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


    internal void OnDraftStart(DateOnly? value) => this.draftStart = value;

    internal void OnDraftEnd(DateOnly? value) => this.draftEnd = value;


    async Task OnDraftRangeAsync(DateRange? range)
    {
        if (this.AutoApply && range is { } r)
        {
            await this.CommitAsync(r);
            await this.SetOpenAsync(false);
        }
    }
}
