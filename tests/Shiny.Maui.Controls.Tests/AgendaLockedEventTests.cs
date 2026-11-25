using Microsoft.Maui.Controls;
using Shiny.Maui.Controls.Scheduler;
using Shiny.Maui.Controls.Scheduler.Internal;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// Events the provider locks (<see cref="ISchedulerEventProvider.CanChangeEvent"/> returns false) used
/// to get the same grips and pan as every other event, so they looked draggable and pressing them
/// silently did nothing. The panel now asks at build time and leaves a locked event bare.
/// </summary>
/// <remarks>
/// Async for the same reason as <see cref="AgendaDragGestureTests"/>: the owner view starts a
/// fire-and-forget scroll when it loads, which a synchronous xUnit test would block on.
/// </remarks>
[Collection(ApplicationResourcesCollection.Name)]
public class AgendaLockedEventTests
{
    static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    static SchedulerEvent Event(string title, int startHour, int minutes) => new()
    {
        Title = title,
        Start = new DateTimeOffset(Today.ToDateTime(new TimeOnly(startHour, 0))),
        End = new DateTimeOffset(Today.ToDateTime(new TimeOnly(startHour, 0))).AddMinutes(minutes)
    };

    static AgendaTimelinePanel Panel(Func<SchedulerEvent, bool>? canChange, bool drag = true, bool resize = true)
    {
        TestDispatcherProvider.Install();
        _ = new Application();

        var owner = new SchedulerAgendaView { DragActivationDelay = TimeSpan.Zero, UseFeedback = false };
        return new AgendaTimelinePanel
        {
            DragController = new AgendaDragController(owner, new ScrollView()),
            AllowEventDrag = drag,
            AllowEventResize = resize,
            CanChangeEvent = canChange
        };
    }

    /// <summary>The views placed in the events layer, in build order (the time marker is off).</summary>
    static List<View> Placed(AgendaTimelinePanel panel) => panel.EventsLayer.Children.OfType<View>().ToList();

    static int GripCount(View placed) => placed is Grid host ? host.Children.Count - 1 : 0;

    static View Inner(View placed) => placed is Grid host ? (View)host.Children[0] : placed;

    static IEnumerable<Element> Descendants(Element root)
    {
        IEnumerable<Element> children = root switch
        {
            Layout layout => layout.Children.OfType<Element>(),
            Border border => border.Content is null ? [] : [border.Content],
            ContentView content => content.Content is null ? [] : [content.Content],
            _ => []
        };
        foreach (var child in children)
        {
            yield return child;
            foreach (var nested in Descendants(child))
                yield return nested;
        }
    }

    static bool HasLockGlyph(View view)
        => Descendants(view).OfType<Label>().Any(l => l.Text == AgendaTimelinePanel.LockGlyph);


    [Fact]
    public async Task ALockedEventGetsNoGripsAndNoPan()
    {
        await Task.CompletedTask;
        var locked = Event("Locked", 9, 120);
        var free = Event("Free", 13, 120);
        var panel = Panel(e => !ReferenceEquals(e, locked));

        panel.Build(Today, [locked, free], null, showTimeMarker: false);
        var placed = Placed(panel);

        var lockedView = placed[0];
        lockedView.ShouldNotBeOfType<Grid>("a locked event is placed bare, with no drag host around it");
        lockedView.GestureRecognizers.OfType<PanGestureRecognizer>().ShouldBeEmpty();
        lockedView.GestureRecognizers.OfType<TapGestureRecognizer>().Count().ShouldBe(1, "a locked event is still selectable");

        var freeView = placed[1];
        GripCount(freeView).ShouldBe(2);
        Inner(freeView).GestureRecognizers.OfType<PanGestureRecognizer>().Count().ShouldBe(1);
    }


    [Fact]
    public async Task NoProviderLocksEverything()
    {
        await Task.CompletedTask;
        var panel = Panel(canChange: null);

        panel.Build(Today, [Event("A", 9, 120)], null, showTimeMarker: false);

        var view = Placed(panel).Single();
        view.ShouldNotBeOfType<Grid>();
        SchedulerAgendaView.GetIsEventLocked(view).ShouldBeTrue();
    }


    [Fact]
    public async Task TheLockedAttachedPropertyIsSetOnlyOnLockedEvents()
    {
        await Task.CompletedTask;
        var locked = Event("Locked", 9, 60);
        var free = Event("Free", 13, 60);
        var panel = Panel(e => !ReferenceEquals(e, locked));

        panel.Build(Today, [locked, free], null, showTimeMarker: false);
        var placed = Placed(panel);

        SchedulerAgendaView.GetIsEventLocked(Inner(placed[0])).ShouldBeTrue();
        SchedulerAgendaView.GetIsEventLocked(Inner(placed[1])).ShouldBeFalse();
    }


    [Fact]
    public async Task ACustomTemplateGetsTheAttachedPropertyButNoBuiltInLock()
    {
        await Task.CompletedTask;
        var locked = Event("Locked", 9, 60);
        var panel = Panel(_ => false);
        panel.EventTemplate = new DataTemplate(() => new Label());

        panel.Build(Today, [locked], null, showTimeMarker: false);

        var view = Placed(panel).Single();
        view.ShouldBeOfType<Label>();
        SchedulerAgendaView.GetIsEventLocked(view).ShouldBeTrue();
        ((Label)view).Text.ShouldBeNull("the template draws its own lock state");
    }


    [Fact]
    public async Task TheDefaultViewShowsALockOnLockedEventsOnly()
    {
        await Task.CompletedTask;
        var locked = Event("Locked", 9, 60);
        var free = Event("Free", 13, 60);
        var panel = Panel(e => !ReferenceEquals(e, locked));

        panel.Build(Today, [locked, free], null, showTimeMarker: false);
        var placed = Placed(panel);

        HasLockGlyph(Inner(placed[0])).ShouldBeTrue();
        HasLockGlyph(Inner(placed[1])).ShouldBeFalse();
    }


    [Fact]
    public async Task ShowLockedIndicatorOffHidesTheLockButStillLocks()
    {
        await Task.CompletedTask;
        var panel = Panel(_ => false);
        panel.ShowLockedIndicator = false;

        panel.Build(Today, [Event("Locked", 9, 120)], null, showTimeMarker: false);

        var view = Placed(panel).Single();
        HasLockGlyph(view).ShouldBeFalse();
        view.ShouldNotBeOfType<Grid>();
        SchedulerAgendaView.GetIsEventLocked(view).ShouldBeTrue();
    }


    /// <summary>
    /// The docs promise a drag-disabled view's tree is what it always was - so with both flags off the
    /// provider is not even asked, nothing is marked locked and no lock is drawn.
    /// </summary>
    [Fact]
    public async Task DragDisabledNeverAsksTheProviderOrShowsALock()
    {
        await Task.CompletedTask;
        var asked = 0;
        var panel = Panel(_ => { asked++; return false; }, drag: false, resize: false);

        panel.Build(Today, [Event("A", 9, 120)], null, showTimeMarker: false);

        var view = Placed(panel).Single();
        asked.ShouldBe(0);
        view.ShouldBeOfType<Border>();
        HasLockGlyph(view).ShouldBeFalse();
        SchedulerAgendaView.GetIsEventLocked(view).ShouldBeFalse();
    }


    [Fact]
    public async Task MinResizableEventHeightDefaultsToFortyEight()
    {
        await Task.CompletedTask;
        new SchedulerAgendaView().MinResizableEventHeight.ShouldBe(48);
        new AgendaTimelinePanel().MinResizableEventHeight.ShouldBe(48);
    }


    /// <summary>At the default 60px slot: 45 minutes is 45px (move only), 60 minutes is 60px (grips).</summary>
    [Fact]
    public async Task EventsShorterThanTheDefaultMinimumAreMoveOnly()
    {
        await Task.CompletedTask;
        var panel = Panel(_ => true);

        panel.Build(Today, [Event("Short", 9, 45), Event("Tall", 13, 60)], null, showTimeMarker: false);
        var placed = Placed(panel);

        GripCount(placed[0]).ShouldBe(0);
        Inner(placed[0]).GestureRecognizers.OfType<PanGestureRecognizer>().Count().ShouldBe(1, "still movable");
        GripCount(placed[1]).ShouldBe(2);
    }


    [Fact]
    public async Task MinResizableEventHeightIsRespected()
    {
        await Task.CompletedTask;
        var panel = Panel(_ => true);
        panel.MinResizableEventHeight = 90;

        panel.Build(Today, [Event("Hour", 9, 60), Event("TwoHours", 13, 120)], null, showTimeMarker: false);
        var placed = Placed(panel);

        GripCount(placed[0]).ShouldBe(0, "60px is under the 90px minimum");
        GripCount(placed[1]).ShouldBe(2);

        panel.MinResizableEventHeight = 30;
        panel.Build(Today, [Event("HalfHour", 9, 30)], null, showTimeMarker: false);
        GripCount(Placed(panel).Single()).ShouldBe(2, "30px meets a 30px minimum");
    }


    [Fact]
    public async Task TheViewPassesItsSettingsToThePanels()
    {
        await Task.CompletedTask;
        TestDispatcherProvider.Install();
        _ = new Application();

        var view = new SchedulerAgendaView
        {
            AllowEventDrag = true,
            ShowLockedIndicator = false,
            MinResizableEventHeight = 72
        };

        var panel = view.PanelAt(0);
        panel.ShouldNotBeNull();
        panel.ShowLockedIndicator.ShouldBeFalse();
        panel.MinResizableEventHeight.ShouldBe(72);
        panel.CanChangeEvent.ShouldNotBeNull();
    }
}
