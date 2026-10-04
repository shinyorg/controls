using System.Windows.Input;
using Shiny.Controls.RangePickers;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls;

/// <summary>
/// A field showing a date range ("Mar 3 – 9, 2026") that opens a <see cref="DateRangeCalendar"/> in a
/// popup. Edits are a draft until Apply, or apply themselves with <see cref="AutoApply"/>.
/// </summary>
public partial class DateRangePicker : RangePickerBase
{
    DateRangeCalendar? calendar;

    public DateRangePicker() : base("📅")
    {
        StyleGuard.MarkReady(this, typeof(DateRangePicker));
        this.RefreshText();
    }

    /// <summary>The committed range, or null.</summary>
    public DateRange? Range => this.StartDate is { } s && this.EndDate is { } e
        ? new DateRange(DateOnly.FromDateTime(s), DateOnly.FromDateTime(e))
        : null;

    /// <summary>Raised when a range is applied, or cleared (<see cref="DateRangeSelectedEventArgs.Range"/> null).</summary>
    public event EventHandler<DateRangeSelectedEventArgs>? RangeSelected;

    /// <summary>The calendar inside the open popup. Test seam.</summary>
    internal DateRangeCalendar? PopupCalendar => this.calendar;

    protected override string DefaultPlaceholder => "Select dates";

    protected override double PopupMaxWidth => this.Months > 1 ? 400 * this.Months : 400;

    protected override bool CanApply => this.calendar is { } c && (c.Range is not null || (c.StartDate is null && c.EndDate is null));


    protected override void OnOpening()
    {
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
            Months = this.Months,
            Culture = this.Culture,
            StartDate = this.StartDate,
            EndDate = this.EndDate
        };
        this.calendar.PropertyChanged += this.OnDraftChanged;
        this.calendar.RangeSelected += this.OnDraftCompleted;
    }

    protected override View CreatePopupContent() => this.calendar!;

    void OnDraftChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DateRangeCalendar.StartDate) or nameof(DateRangeCalendar.EndDate))
            this.RefreshPopupState();
    }

    void OnDraftCompleted(object? sender, DateRangeSelectedEventArgs e)
    {
        if (this.AutoApply && e.Range is not null)
            this.Dispatcher.Dispatch(this.Apply);
    }

    protected override void CommitDraft()
    {
        this.StartDate = this.calendar?.Range?.Start.ToDateTime(TimeOnly.MinValue);
        this.EndDate = this.calendar?.Range?.End.ToDateTime(TimeOnly.MinValue);

        var range = this.Range;
        this.RangeSelected?.Invoke(this, new DateRangeSelectedEventArgs(range));
        if (range is not null && this.RangeSelectedCommand?.CanExecute(range) == true)
            this.RangeSelectedCommand.Execute(range);
    }

    protected override void OnClearDraft() => this.calendar?.Clear();

    protected override void OnClosed()
    {
        if (this.calendar is null)
            return;

        this.calendar.PropertyChanged -= this.OnDraftChanged;
        this.calendar.RangeSelected -= this.OnDraftCompleted;
        this.calendar = null;
    }

    protected override string? FormatValue()
        => this.Range is { } r ? RangeFormatter.Format(r, this.EffectiveCulture, this.Format, this.Separator) : null;

    protected override string? DraftSummary()
    {
        if (this.calendar?.Range is { } r)
            return RangeFormatter.Format(r, this.EffectiveCulture, this.Format, this.Separator);

        if (this.calendar?.StartDate is { } start)
            return RangeFormatter.Format(new DateRange(DateOnly.FromDateTime(start), DateOnly.FromDateTime(start)), this.EffectiveCulture, this.Format) + this.Separator + "…";

        return null;
    }
}
