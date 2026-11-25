using Shiny.Blazor.Controls.Scheduler;
using Shouldly;
using Xunit;

namespace Shiny.Blazor.Controls.Tests;

/// <summary>
/// Locked events (<see cref="ISchedulerEventProvider.CanChangeEvent"/> returns false) get no grips, no
/// <c>data-draggable</c> and the <c>shiny-agenda-event--locked</c> class. The markup decisions are
/// methods on the component, so they are driven here without a renderer.
/// </summary>
public class SchedulerAgendaLockedEventTests
{
    sealed class Provider(Func<SchedulerEvent, bool> canChange) : ISchedulerEventProvider
    {
        public int Asked { get; private set; }

        public Task<IReadOnlyList<SchedulerEvent>> GetEvents(DateTimeOffset start, DateTimeOffset end)
            => Task.FromResult<IReadOnlyList<SchedulerEvent>>([]);

        public void OnEventSelected(SchedulerEvent selectedEvent) { }
        public bool CanCalendarSelect(DateOnly selectedDate) => true;
        public void OnCalendarDateSelected(DateOnly selectedDate) { }
        public void OnAgendaTimeSelected(DateTimeOffset selectedTime) { }
        public bool CanSelectAgendaTime(DateTimeOffset selectedTime) => true;

        public bool CanChangeEvent(SchedulerEvent evt)
        {
            this.Asked++;
            return canChange(evt);
        }
    }


    [Fact]
    public void ALockedEventIsLockedAndGetsNoGrips()
    {
        var locked = new SchedulerEvent { Title = "Locked" };
        var free = new SchedulerEvent { Title = "Free" };
        var view = new SchedulerAgendaView
        {
            AllowEventDrag = true,
            AllowEventResize = true,
            Provider = new Provider(e => !ReferenceEquals(e, locked))
        };

        view.IsEventLocked(locked).ShouldBeTrue();
        view.IsEventLocked(free).ShouldBeFalse();

        view.ShowsResizeGrips(locked: true, height: 120).ShouldBeFalse();
        view.ShowsResizeGrips(locked: false, height: 120).ShouldBeTrue();
    }


    [Fact]
    public void DragDisabledNeverAsksTheProvider()
    {
        var provider = new Provider(_ => false);
        var view = new SchedulerAgendaView { Provider = provider };

        view.IsEventLocked(new SchedulerEvent()).ShouldBeFalse();
        provider.Asked.ShouldBe(0);
    }


    [Fact]
    public void NoProviderLocksEverythingWhenDragIsOn()
    {
        var view = new SchedulerAgendaView { AllowEventDrag = true };

        view.IsEventLocked(new SchedulerEvent()).ShouldBeTrue();
    }


    [Fact]
    public void OnlyLockedEventsCarryTheLockedClass()
    {
        SchedulerAgendaView.EventCssClass(false).ShouldBe("shiny-agenda-event");
        SchedulerAgendaView.EventCssClass(true).ShouldBe("shiny-agenda-event shiny-agenda-event--locked");
    }


    [Fact]
    public void TheLockedIndicatorIsOnByDefault()
        => new SchedulerAgendaView().ShowLockedIndicator.ShouldBeTrue();


    [Fact]
    public void MinResizableEventHeightIsRespected()
    {
        var view = new SchedulerAgendaView { AllowEventResize = true };
        view.MinResizableEventHeight.ShouldBe(48);

        view.ShowsResizeGrips(false, 47).ShouldBeFalse();
        view.ShowsResizeGrips(false, 48).ShouldBeTrue();

        view.MinResizableEventHeight = 90;
        view.ShowsResizeGrips(false, 60).ShouldBeFalse();
        view.ShowsResizeGrips(false, 90).ShouldBeTrue();
    }


    [Fact]
    public void ResizeOffMeansNoGrips()
    {
        var view = new SchedulerAgendaView { AllowEventDrag = true };

        view.ShowsResizeGrips(false, 500).ShouldBeFalse();
    }
}
