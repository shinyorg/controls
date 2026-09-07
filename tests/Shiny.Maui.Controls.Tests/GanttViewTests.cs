using System.Collections.ObjectModel;
using Microsoft.Maui.Controls;
using Shiny.Controls.Gantt;
using Shiny.Maui.Controls.Gantt;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// Host-level cover for <see cref="GanttView"/>. The scheduling itself is tested exhaustively in
/// <c>Shiny.Controls.Gantt.Tests</c> against the shared engine, so nothing here re-asserts a date
/// cascade. What these do assert is the half that only exists in MAUI: that the control survives
/// construction, that its bindable properties actually reach the model, that hit testing maps a
/// coordinate onto the right gesture, and that a data change is observed rather than requiring the
/// consumer to call Rebuild by hand.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class GanttViewTests
{
    static readonly DateTimeOffset Monday = new(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);

    static DateTimeOffset Day(double offset) => Monday.AddDays(offset);

    static GanttTask Task(string id, double start, double end, string? parent = null) => new()
    {
        Id = id,
        Name = id,
        ParentId = parent,
        Start = Day(start),
        End = Day(end)
    };

    static GanttView Build(out ObservableCollection<GanttTask> tasks, out ObservableCollection<GanttDependency> links)
    {
        // Application.Current is process-wide; constructing one unconditionally rather than reusing
        // whatever a previous test left behind is what stops an implicit style leaking between them.
        _ = new Application();

        tasks =
        [
            Task("phase", 0, 5),
            Task("a", 0, 2, "phase"),
            Task("b", 2, 5, "phase"),
            Task("after", 5, 8)
        ];
        links = [new GanttDependency("b", "after")];

        return new GanttView
        {
            Tasks = tasks,
            Dependencies = links,
            StartDate = Monday,
            EndDate = Day(20),
            PixelsPerDay = 40,
            RowHeight = 40,
            BarHeight = 20
        };
    }


    [Fact]
    public void ConstructingAndBindingBuildsAPlan()
    {
        var view = Build(out _, out _);

        view.PlanModel.ShouldNotBeNull();
        view.PlanModel!.Rows.Count.ShouldBe(4);
        view.Metrics.ShouldNotBeNull();
    }


    [Fact]
    public void AnImplicitStyleDoesNotKillTheConstructor()
    {
        // MAUI applies an implicit style from StyleableElement's own constructor, before a single
        // line of the derived control's body has run. Every property callback here is behind
        // StyleGuard for that reason, and this is the test that proves it.
        var app = new Application();
        app.Resources.Add(new Style(typeof(GanttView))
        {
            Setters =
            {
                new Setter { Property = GanttView.RowHeightProperty, Value = 44d },
                new Setter { Property = GanttView.ShowCriticalPathProperty, Value = true },
                new Setter { Property = GanttView.TaskPaneWidthProperty, Value = 300d }
            }
        });

        var view = Should.NotThrow(() => new GanttView());
        view.RowHeight.ShouldBe(44);
    }


    [Fact]
    public void ARangeIsDerivedFromThePlanWhenNoneIsGiven()
    {
        _ = new Application();
        var view = new GanttView
        {
            Tasks = new ObservableCollection<GanttTask> { Task("a", 2, 6) },
            PaddingDays = 1
        };

        view.RangeStart.ShouldBe(Day(1));
        view.RangeEnd.ShouldBe(Day(7));
    }


    [Fact]
    public void APlanOfOneMilestoneStillGetsAWidth()
    {
        // Zero extent would give a zero-width timeline, which looks exactly like a control that
        // failed to bind.
        _ = new Application();
        var milestone = new GanttTask { Id = "m", Start = Day(3), End = Day(3), Kind = GanttTaskKind.Milestone };

        var view = new GanttView
        {
            Tasks = new ObservableCollection<GanttTask> { milestone },
            StartDate = Day(3),
            EndDate = Day(3)
        };

        view.RangeEnd.ShouldBeGreaterThan(view.RangeStart);
    }


    [Fact]
    public void AddingATaskRebuildsWithoutBeingAsked()
    {
        var view = Build(out var tasks, out _);
        tasks.Add(Task("extra", 9, 11));

        view.PlanModel!.Rows.Count.ShouldBe(5);
    }


    [Fact]
    public void EditingATaskInPlaceRebuilds()
    {
        // A view model that moves a date raises PropertyChanged and nothing else; without watching
        // each task the bar simply would not move.
        var view = Build(out var tasks, out _);
        tasks[3].Start = Day(12);
        tasks[3].End = Day(14);

        view.Metrics!.BarOf(tasks[3]).X.ShouldBe(480);
    }


    [Fact]
    public void CollapsingASummaryDropsItsChildrenFromTheRows()
    {
        var view = Build(out var tasks, out _);
        view.ToggleExpand(tasks[0]);

        view.PlanModel!.Rows.Select(x => x.Task.Id).ShouldBe(["phase", "after"]);
    }


    [Fact]
    public void ExpandAllAndCollapseAllReachEveryLevel()
    {
        var view = Build(out _, out _);

        view.CollapseAll();
        view.PlanModel!.Rows.Count.ShouldBe(2);

        view.ExpandAll();
        view.PlanModel!.Rows.Count.ShouldBe(4);
    }


    [Fact]
    public void HitTestingMapsCoordinatesOntoGestures()
    {
        var view = Build(out var tasks, out _);

        // Row 1 is task "a", days 0-2 at 40px/day, centred in a 40px row.
        view.FindHit(40, 60)!.Value.Target.ShouldBe(GanttDragTarget.Body);
        view.FindHit(2, 60)!.Value.Target.ShouldBe(GanttDragTarget.StartEdge);
        view.FindHit(78, 60)!.Value.Target.ShouldBe(GanttDragTarget.EndEdge);
        view.FindHit(40, 60)!.Value.Task.ShouldBe(tasks[1]);

        view.FindHit(600, 60).ShouldBeNull();
        view.FindHit(40, 5000).ShouldBeNull();
    }


    [Fact]
    public void ConnectorsAreOnlyOfferedWhenLinkEditingIsOn()
    {
        var view = Build(out _, out _);

        // Just past task "a"'s right edge at x=80.
        view.FindHit(86, 60).ShouldBeNull();

        view.AllowDependencyEdit = true;
        view.FindHit(86, 60)!.Value.Target.ShouldBe(GanttDragTarget.Connector);

        // Read-only outranks the individual permission.
        view.IsReadOnly = true;
        view.FindHit(86, 60).ShouldBeNull();
    }


    [Fact]
    public void SelectionIsSingleByDefaultAndTwoWay()
    {
        var view = Build(out var tasks, out _);
        var raised = 0;
        view.SelectionChanged += (_, _) => raised++;

        view.SelectTask(tasks[1]);

        view.SelectedTask.ShouldBe(tasks[1]);
        view.SelectedTasks.ShouldBe([tasks[1]]);
        raised.ShouldBe(1);

        view.SelectTask(tasks[2]);
        view.SelectedTasks.ShouldBe([tasks[2]]);
    }


    [Fact]
    public void MultipleSelectionTogglesRatherThanReplacing()
    {
        var view = Build(out var tasks, out _);
        view.SelectionMode = GanttSelectionMode.Multiple;

        view.SelectTask(tasks[1]);
        view.SelectTask(tasks[2]);
        view.SelectedTasks.Count.ShouldBe(2);

        view.SelectTask(tasks[1]);
        view.SelectedTasks.ShouldBe([tasks[2]]);
    }


    [Fact]
    public void SelectionModeNoneIgnoresTaps()
    {
        var view = Build(out var tasks, out _);
        view.SelectionMode = GanttSelectionMode.None;

        view.SelectTask(tasks[1]);
        view.SelectedTask.ShouldBeNull();
    }


    [Fact]
    public void TheCriticalPathIsOnlyComputedWhenAskedFor()
    {
        var view = Build(out var tasks, out _);
        view.PlanModel!.AllTasks.ShouldAllBe(x => !x.IsCritical);

        view.ShowCriticalPath = true;
        view.PlanModel!.AllTasks.ShouldContain(x => x.IsCritical);
    }


    [Fact]
    public void TheCalendarReachesTheModel()
    {
        var view = Build(out _, out _);
        view.Calendar = GanttCalendar.StandardDays;

        view.PlanModel!.Calendar.IsContinuous.ShouldBeFalse();
        view.PlanModel!.WorkingDurationOf(view.PlanModel.AllTasks.Single(x => x.Id == "after"))
            // Days 5-8 is Saturday to Tuesday: only the Monday is worked.
            .ShouldBe(TimeSpan.FromDays(1));
    }


    [Fact]
    public void ColumnsDefaultToASensibleSetAndOneCarriesTheHierarchy()
    {
        var view = Build(out _, out _);
        var columns = view.ResolveColumns();

        columns.Count.ShouldBe(3);
        columns.Count(x => x.ShowHierarchy).ShouldBe(1);
    }


    [Fact]
    public void AColumnSetWithNoHierarchyFlagGetsOne()
    {
        // A tree with no expanders is unusable, and silently so.
        var view = Build(out _, out _);
        view.Columns.Add(new GanttColumn { Field = GanttColumn.NameField });
        view.Columns.Add(new GanttColumn { Field = GanttColumn.StartField });

        view.ResolveColumns()[0].ShowHierarchy.ShouldBeTrue();
    }


    [Fact]
    public void ScheduleOptionsCarryTheControlsSettings()
    {
        var view = Build(out _, out _);
        view.CascadeMode = GanttCascadeMode.Strict;
        view.MinimumDuration = TimeSpan.FromHours(4);
        view.SnapMode = GanttSnapMode.WorkingTime;
        view.Calendar = GanttCalendar.StandardDays;

        var options = view.BuildScheduleOptions();

        options.Cascade.ShouldBe(GanttCascadeMode.Strict);
        options.MinimumDuration.ShouldBe(TimeSpan.FromHours(4));
        options.SnapToWorkingTime.ShouldBeTrue();
        options.Calendar.IsContinuous.ShouldBeFalse();
        options.MinDate.ShouldBe(Monday);
    }


    [Fact]
    public void SnappingFollowsTheEffectiveScale()
    {
        var view = Build(out _, out _);
        view.SnapMode = GanttSnapMode.Scale;
        view.TimeScale = GanttTimeScale.Day;

        view.Snap(Day(3).AddHours(20)).ShouldBe(Day(4));
    }


    [Fact]
    public void DisposeUnhooksWithoutThrowing()
    {
        var view = Build(out var tasks, out _);
        view.Dispose();

        // A change after disposal must be inert rather than resurrecting the control's handlers.
        Should.NotThrow(() => tasks.Add(Task("late", 20, 21)));
    }
}
