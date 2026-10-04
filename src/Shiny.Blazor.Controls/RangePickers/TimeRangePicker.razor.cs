using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Shiny.Controls.RangePickers;

namespace Shiny.Blazor.Controls;

/// <summary>
/// A field that opens two lists of times - start and end - on an interval grid. Moving the start
/// keeps the range's length, as calendar apps do. Ends are labelled with the length they make, and
/// with <see cref="AllowOvernight"/> an end may fall on the next day. The Blazor twin of MAUI's
/// <c>TimeRangePicker</c>.
/// </summary>
/// <example>
/// <code>
/// &lt;TimeRangePicker @bind-StartTime="from" @bind-EndTime="to" Interval="TimeSpan.FromMinutes(30)" /&gt;
/// </code>
/// </example>
public partial class TimeRangePicker
{
    static readonly TimeOnly Anchor = new(9, 0);

    readonly TimeRangeConstraints constraints = new();
    RangePickerField? field;
    IJSObjectReference? module;
    bool disposed;
    bool scrollPending;

    TimeOnly? draftStart;
    TimeOnly? draftEnd;
    IReadOnlyList<TimeOnly> startSlots = [];
    IReadOnlyList<TimeSlot> endSlots = [];
    TimeOnly? startAnchor;

    [Parameter] public TimeOnly? StartTime { get; set; }
    [Parameter] public EventCallback<TimeOnly?> StartTimeChanged { get; set; }

    [Parameter] public TimeOnly? EndTime { get; set; }
    [Parameter] public EventCallback<TimeOnly?> EndTimeChanged { get; set; }

    /// <summary>Raised when a range is applied or cleared.</summary>
    [Parameter] public EventCallback<TimeRange?> RangeChanged { get; set; }

    /// <summary>The step between selectable times.</summary>
    [Parameter] public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(15);

    [Parameter] public TimeOnly? MinTime { get; set; }
    [Parameter] public TimeOnly? MaxTime { get; set; }

    /// <summary>The shortest range. Defaults to one <see cref="Interval"/>.</summary>
    [Parameter] public TimeSpan? MinDuration { get; set; }

    [Parameter] public TimeSpan? MaxDuration { get; set; }

    /// <summary>Allow the end to be earlier than the start, meaning the next day (a night shift).</summary>
    [Parameter] public bool AllowOvernight { get; set; }

    /// <summary>A time format for both ends ("HH:mm"). Null uses the culture's short time.</summary>
    [Parameter] public string? Format { get; set; }

    [Parameter] public string Separator { get; set; } = RangeFormatter.DefaultSeparator;
    [Parameter] public CultureInfo? Culture { get; set; }

    [Parameter] public string Placeholder { get; set; } = "Select times";
    [Parameter] public string? Title { get; set; }
    [Parameter] public string StartText { get; set; } = "Start";
    [Parameter] public string EndText { get; set; } = "End";
    [Parameter] public string PickStartText { get; set; } = "Pick a start time";
    [Parameter] public string NextDayText { get; set; } = "next day";
    [Parameter] public string ApplyText { get; set; } = "Apply";
    [Parameter] public string CancelText { get; set; } = "Cancel";
    [Parameter] public string ClearText { get; set; } = "Clear";
    [Parameter] public bool ShowClear { get; set; } = true;
    [Parameter] public bool Disabled { get; set; }

    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public EventCallback<bool> IsOpenChanged { get; set; }

    [Parameter] public string? Class { get; set; }
    [Parameter] public string? Style { get; set; }


    internal TimeOnly? DraftStart => this.draftStart;
    internal TimeOnly? DraftEnd => this.draftEnd;
    internal IReadOnlyList<TimeOnly> StartSlots => this.startSlots;
    internal IReadOnlyList<TimeSlot> EndSlots => this.endSlots;

    CultureInfo CultureOrCurrent => this.Culture ?? CultureInfo.CurrentCulture;

    bool DraftIsValid => this.draftStart is { } s && this.draftEnd is { } e && this.constraints.IsValid(new TimeRange(s, e));

    string? DisplayText => this.StartTime is { } s && this.EndTime is { } e
        ? RangeFormatter.Format(new TimeRange(s, e), this.CultureOrCurrent, this.Format, this.Separator)
        : null;

    string? SummaryText => this.draftStart is { } s && this.draftEnd is { } e
        ? $"{RangeFormatter.Format(new TimeRange(s, e), this.CultureOrCurrent, this.Format, this.Separator)} · {RangeFormatter.FormatDuration(new TimeRange(s, e).Duration)}"
        : null;


    protected override void OnParametersSet()
    {
        this.constraints.Interval = this.Interval;
        this.constraints.MinTime = this.MinTime;
        this.constraints.MaxTime = this.MaxTime;
        this.constraints.MinDuration = this.MinDuration;
        this.constraints.MaxDuration = this.MaxDuration;
        this.constraints.AllowOvernight = this.AllowOvernight;

        this.startSlots = this.constraints.StartSlots();
        this.startAnchor = this.startSlots.Count == 0
            ? null
            : this.startSlots.MinBy(s => Math.Abs((s - Anchor).TotalMinutes is var d && d > 720 ? 1440 - d : d));

        if (!this.IsOpen)
        {
            this.draftStart = this.StartTime;
            this.draftEnd = this.EndTime;
        }
        this.RefreshEnds();
    }


    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!this.scrollPending || !this.IsOpen || this.field is null)
            return;

        this.scrollPending = false;
        try
        {
            if (this.module is null)
            {
                var loaded = await this.JS.InvokeAsync<IJSObjectReference>("import", "./_content/Shiny.Blazor.Controls/range-pickers.js");
                if (this.disposed) { await loaded.ReleaseLateAsync(); return; }
                this.module = loaded;
            }
            await this.module.InvokeVoidAsync("scrollSelectedIntoView", this.field.PopoverElement);
        }
        catch (JSDisconnectedException) { }
        catch (ObjectDisposedException) { }
        catch (TaskCanceledException) { }
        catch (JSException) { }
        catch (InvalidOperationException) { }
    }


    /// <summary>Opens the lists with the committed range as the draft.</summary>
    public Task OpenAsync()
    {
        this.draftStart = this.StartTime;
        this.draftEnd = this.EndTime;
        this.RefreshEnds();
        this.scrollPending = true;
        return this.SetOpenAsync(true);
    }


    Task ToggleAsync() => this.IsOpen ? this.CancelAsync() : this.OpenAsync();


    public Task CancelAsync()
    {
        this.draftStart = this.StartTime;
        this.draftEnd = this.EndTime;
        this.RefreshEnds();
        return this.SetOpenAsync(false);
    }


    public async Task ApplyAsync()
    {
        if (!this.DraftIsValid)
            return;

        await this.CommitAsync(new TimeRange(this.draftStart!.Value, this.draftEnd!.Value));
        await this.SetOpenAsync(false);
    }


    public async Task ClearAsync()
    {
        this.draftStart = null;
        this.draftEnd = null;
        this.RefreshEnds();
        await this.CommitAsync(null);
        await this.SetOpenAsync(false);
    }


    /// <summary>Picks a start time. The end moves with it, keeping the range's length where it can.</summary>
    internal void SelectStart(TimeOnly start)
    {
        TimeSpan? keep = this.draftStart is { } s && this.draftEnd is { } e ? new TimeRange(s, e).Duration : null;
        this.draftStart = start;
        this.draftEnd = this.constraints.EndFor(start, keep);
        this.RefreshEnds();
    }


    internal void SelectEnd(TimeOnly end) => this.draftEnd = end;


    void RefreshEnds()
        => this.endSlots = this.draftStart is { } s ? this.constraints.EndSlotsFor(s) : [];


    string FormatTime(TimeOnly time)
        => time.ToString(string.IsNullOrEmpty(this.Format) ? this.CultureOrCurrent.DateTimeFormat.ShortTimePattern : this.Format, this.CultureOrCurrent);


    async Task CommitAsync(TimeRange? range)
    {
        this.StartTime = range?.Start;
        this.EndTime = range?.End;
        await this.StartTimeChanged.InvokeAsync(this.StartTime);
        await this.EndTimeChanged.InvokeAsync(this.EndTime);
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


    public async ValueTask DisposeAsync()
    {
        this.disposed = true;
        if (this.module is null)
            return;

        try
        {
            await this.module.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
        catch (ObjectDisposedException) { }
        catch (TaskCanceledException) { }
        catch (JSException) { }
        GC.SuppressFinalize(this);
    }
}
