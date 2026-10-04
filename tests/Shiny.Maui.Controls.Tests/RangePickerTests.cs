using Microsoft.Maui.Controls;
using Shiny.Controls.RangePickers;
using Shiny.Maui.Controls.Infrastructure;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// The MAUI hosts over the shared range engine: that taps reach the selector and the bound
/// properties, that a picker's draft only lands on Apply, and that the time columns follow the start.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class RangePickerTests
{
    public RangePickerTests()
    {
        TestDispatcherProvider.Install();
        _ = new Application();
    }

    static DateTime D(int day) => new(2031, 3, day);

    static RangeDayCell Cell(DateRangeCalendar calendar, DateTime date)
        => calendar.Cells.First(c => c.Date == DateOnly.FromDateTime(date) && !c.State.HasFlag(CalendarDayState.OutsideMonth));


    [Fact]
    public void TheCalendarDrawsSixWeeksPerMonth()
    {
        var calendar = new DateRangeCalendar { DisplayMonth = D(1), Months = 2 };

        calendar.Cells.Count.ShouldBe(84);
    }


    [Fact]
    public void TwoTapsPickARangeAndRaiseIt()
    {
        var calendar = new DateRangeCalendar { DisplayMonth = D(1) };
        DateRange? raised = null;
        calendar.RangeSelected += (_, e) => raised = e.Range;

        Cell(calendar, D(3)).SimulateTap();
        calendar.StartDate.ShouldBe(D(3));
        calendar.EndDate.ShouldBeNull();
        raised.ShouldBeNull();

        Cell(calendar, D(9)).SimulateTap();
        calendar.EndDate.ShouldBe(D(9));
        raised.ShouldBe(new DateRange(new DateOnly(2031, 3, 3), new DateOnly(2031, 3, 9)));
        Cell(calendar, D(5)).State.HasFlag(CalendarDayState.InRange).ShouldBeTrue();
    }


    [Fact]
    public void BoundDatesDriveTheSelection()
    {
        var calendar = new DateRangeCalendar { StartDate = D(10), EndDate = D(12) };

        calendar.Range.ShouldBe(new DateRange(new DateOnly(2031, 3, 10), new DateOnly(2031, 3, 12)));
        calendar.DisplayMonth.ShouldBe(D(1));
    }


    [Fact]
    public void ConstraintsReachTheSelector()
    {
        var calendar = new DateRangeCalendar { DisplayMonth = D(1), MinDate = D(5), MaxDays = 3 };

        Cell(calendar, D(4)).SimulateTap();
        calendar.StartDate.ShouldBeNull();

        Cell(calendar, D(6)).SimulateTap();
        Cell(calendar, D(10)).State.HasFlag(CalendarDayState.Unavailable).ShouldBeTrue();
    }


    [Fact]
    public void APresetSelectsItsRange()
    {
        var calendar = new DateRangeCalendar { Presets = [DateRangePresets.Last7Days] };

        calendar.ApplyPreset(DateRangePresets.Last7Days).ShouldBeTrue();

        calendar.EndDate.ShouldBe(DateTime.Today);
        calendar.StartDate.ShouldBe(DateTime.Today.AddDays(-6));
    }


    [Fact]
    public void ThePickerOnlyCommitsOnApply()
    {
        var picker = new DateRangePicker();
        _ = new ContentPage { Content = new VerticalStackLayout { Children = { picker } } };

        picker.Open();
        picker.IsOpen.ShouldBeTrue();
        var calendar = picker.PopupCalendar.ShouldNotBeNull();
        calendar.StartDate = D(3);
        calendar.EndDate = D(5);

        picker.StartDate.ShouldBeNull();
        picker.Apply();

        picker.IsOpen.ShouldBeFalse();
        picker.StartDate.ShouldBe(D(3));
        picker.EndDate.ShouldBe(D(5));
    }


    [Fact]
    public void CancelLeavesTheValueAlone()
    {
        var picker = new DateRangePicker { StartDate = D(1), EndDate = D(2) };
        _ = new ContentPage { Content = new VerticalStackLayout { Children = { picker } } };

        picker.Open();
        picker.PopupCalendar!.Clear();
        picker.Cancel();

        picker.StartDate.ShouldBe(D(1));
        picker.EndDate.ShouldBe(D(2));
    }


    [Fact]
    public void AHalfPickedRangeCannotBeApplied()
    {
        var picker = new DateRangePicker();
        _ = new ContentPage { Content = new VerticalStackLayout { Children = { picker } } };

        picker.Open();
        picker.PopupCalendar!.StartDate = D(3);
        picker.Apply();

        picker.IsOpen.ShouldBeTrue();
        picker.StartDate.ShouldBeNull();
    }


    [Fact]
    public void TheTimePickerKeepsTheLengthWhenTheStartMoves()
    {
        var picker = new TimeRangePicker { StartTime = TimeSpan.FromHours(9), EndTime = TimeSpan.FromHours(10.5) };
        _ = new ContentPage { Content = new VerticalStackLayout { Children = { picker } } };

        picker.Open();
        picker.SelectStart(new TimeOnly(13, 0));

        picker.DraftEnd.ShouldBe(new TimeOnly(14, 30));
        picker.Apply();
        picker.Range.ShouldBe(new TimeRange(new TimeOnly(13, 0), new TimeOnly(14, 30)));
    }


    [Fact]
    public void TheDateTimePickerRefusesAnEndBeforeTheStart()
    {
        var picker = new DateTimeRangePicker();
        _ = new ContentPage { Content = new VerticalStackLayout { Children = { picker } } };

        picker.Open();
        picker.PopupCalendar!.StartDate = D(3);
        picker.PopupCalendar!.EndDate = D(3);
        picker.SetDraftTimes(new TimeOnly(9, 0), new TimeOnly(17, 0));
        picker.DraftRange.ShouldBe(new DateTimeRange(new DateTime(2031, 3, 3, 9, 0, 0), new DateTime(2031, 3, 3, 17, 0, 0)));

        picker.Apply();
        picker.StartDateTime.ShouldBe(new DateTime(2031, 3, 3, 9, 0, 0));
        picker.EndDateTime.ShouldBe(new DateTime(2031, 3, 3, 17, 0, 0));
    }


    [Fact]
    public void TheFieldShowsTheCompactRange()
    {
        var picker = new DateRangePicker
        {
            Culture = System.Globalization.CultureInfo.GetCultureInfo("en-US"),
            StartDate = D(3),
            EndDate = D(9)
        };

        var text = picker.GetVisualTreeDescendants().OfType<Label>().First().Text;
        text.ShouldBe("Mar 3 – 9, 2031");
    }


    [Fact]
    public void ThePopupLivesInThePageOverlay()
    {
        var picker = new DateRangePicker();
        var page = new ContentPage { Content = new VerticalStackLayout { Children = { picker } } };

        picker.Open();

        PageOverlay.GetOrCreateRoot(page).Children.OfType<PageOverlay.RangePickerLayer>().Single().Children.Count.ShouldBe(1);
        picker.Cancel();
        PageOverlay.GetOrCreateRoot(page).Children.OfType<PageOverlay.RangePickerLayer>().Single().Children.ShouldBeEmpty();
    }
}
