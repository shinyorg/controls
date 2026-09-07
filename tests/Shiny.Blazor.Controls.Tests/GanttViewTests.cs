using System.Collections.ObjectModel;
using Shiny.Blazor.Controls.Gantt;
using Shiny.Controls.Gantt;
using Shouldly;
using Xunit;

namespace Shiny.Blazor.Controls.Tests;

/// <summary>
/// Host-level cover for the Blazor <see cref="GanttView"/>.
/// </summary>
/// <remarks>
/// The scheduling is tested exhaustively against the shared engine in
/// <c>Shiny.Controls.Gantt.Tests</c>, so nothing here re-asserts a cascade. These cover the half that
/// only exists in Blazor: that parameters reach the model, that the markup helpers emit CSS a browser
/// will actually parse, and — the load-bearing one — that the two hosts agree. The parity tests build
/// the same plan through both this component's helpers and the shared metrics, because a bar that
/// lands two pixels apart on the two hosts is exactly the kind of divergence neither host's own tests
/// would ever notice.
/// </remarks>
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

    static GanttView Build(out ObservableCollection<GanttTask> tasks)
    {
        tasks =
        [
            Task("phase", 0, 5),
            Task("a", 0, 2, "phase"),
            Task("b", 2, 5, "phase"),
            Task("after", 5, 8)
        ];

        var view = new GanttView
        {
            Tasks = tasks,
            Dependencies = new ObservableCollection<GanttDependency> { new("b", "after") },
            StartDate = Monday,
            EndDate = Day(20),
            PixelsPerDay = 40,
            RowHeight = 40,
            BarHeight = 20
        };
        view.Rebuild();
        return view;
    }


    [Fact]
    public void RebuildResolvesThePlan()
    {
        var view = Build(out _);

        view.Model.Rows.Count.ShouldBe(4);
        view.Bars.Count.ShouldBe(4);
        view.EffectivePixelsPerDay.ShouldBe(40);
    }


    [Fact]
    public void ARangeIsDerivedFromThePlanWhenNoneIsGiven()
    {
        var view = new GanttView
        {
            Tasks = new ObservableCollection<GanttTask> { Task("a", 2, 6) },
            PaddingDays = 1
        };
        view.Rebuild();

        view.RangeStart.ShouldBe(Day(1));
        view.RangeEnd.ShouldBe(Day(7));
    }


    [Fact]
    public void APlanOfOneMilestoneStillGetsAWidth()
    {
        var milestone = new GanttTask { Id = "m", Start = Day(3), End = Day(3), Kind = GanttTaskKind.Milestone };
        var view = new GanttView
        {
            Tasks = new ObservableCollection<GanttTask> { milestone },
            StartDate = Day(3),
            EndDate = Day(3)
        };
        view.Rebuild();

        view.RangeEnd.ShouldBeGreaterThan(view.RangeStart);
    }


    [Fact]
    public void CollapsingASummaryDropsItsChildrenFromTheRows()
    {
        var view = Build(out var tasks);
        tasks[0].IsExpanded = false;
        view.Rebuild();

        view.Model.Rows.Select(x => x.Task.Id).ShouldBe(["phase", "after"]);
        view.Bars.Select(x => x.Task.Id).ShouldBe(["phase", "after"]);
    }


    [Fact]
    public void HitTestingMapsCoordinatesOntoGestures()
    {
        var view = Build(out var tasks);

        view.FindHit(40, 60)!.Value.Target.ShouldBe(GanttDragTarget.Body);
        view.FindHit(2, 60)!.Value.Target.ShouldBe(GanttDragTarget.StartEdge);
        view.FindHit(78, 60)!.Value.Target.ShouldBe(GanttDragTarget.EndEdge);
        view.FindHit(40, 60)!.Value.Task.ShouldBe(tasks[1]);
        view.FindHit(600, 60).ShouldBeNull();
    }


    [Fact]
    public void ConnectorsAreOnlyOfferedOnTheSelectedBarWithLinkEditingOn()
    {
        var view = Build(out var tasks);
        view.AllowDependencyEdit = true;

        // Not selected yet, so the dots are not drawn and must not be hit either.
        view.FindHit(86, 60).ShouldBeNull();

        view.SelectTask(tasks[1]);
        view.FindHit(86, 60)!.Value.Target.ShouldBe(GanttDragTarget.Connector);
    }


    [Fact]
    public void SelectionIsSingleByDefault()
    {
        var view = Build(out var tasks);

        view.SelectTask(tasks[1]);
        view.SelectedTask.ShouldBe(tasks[1]);

        view.SelectTask(tasks[2]);
        view.SelectedTasks.ShouldBe([tasks[2]]);
    }


    [Fact]
    public void MultipleSelectionToggles()
    {
        var view = Build(out var tasks);
        view.SelectionMode = GanttSelectionMode.Multiple;

        view.SelectTask(tasks[1]);
        view.SelectTask(tasks[2]);
        view.SelectedTasks.Count.ShouldBe(2);

        view.SelectTask(tasks[1]);
        view.SelectedTasks.ShouldBe([tasks[2]]);
    }


    [Fact]
    public void CssNumbersAreInvariant()
    {
        // A German thread would otherwise emit "12,5px", which a browser silently drops — leaving a
        // bar at its default position with nothing in the console to explain why.
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            GanttView.Css(12.5).ShouldBe("12.5");
            GanttView.Box(new GanttRect(1.5, 2.25, 3, 4)).ShouldBe("left:1.5px;top:2.25px;width:3px;height:4px");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }


    [Fact]
    public void RoutePointsAreEmittedAsAnSvgPointList()
    {
        var route = new GanttPoint[] { new(0, 1), new(2, 3), new(4.5, 5) };
        GanttView.Points(route).ShouldBe("0,1 2,3 4.5,5");
    }


    [Fact]
    public void TheArrowHeadPointsAlongTheFinalSegment()
    {
        var rightwards = GanttView.ArrowPoints([new GanttPoint(0, 10), new GanttPoint(20, 10)]);
        rightwards.ShouldBe("20,10 15,5 15,15");

        var leftwards = GanttView.ArrowPoints([new GanttPoint(20, 10), new GanttPoint(0, 10)]);
        leftwards.ShouldBe("0,10 5,5 5,15");
    }


    [Fact]
    public void ColumnsDefaultToASensibleSetAndOneCarriesTheHierarchy()
    {
        var view = Build(out _);
        var columns = typeof(GanttView)
            .GetField("resolvedColumns", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(view) as IReadOnlyList<GanttColumnDefinition>;

        columns!.Count.ShouldBe(3);
        columns.Count(x => x.ShowHierarchy).ShouldBe(1);
    }


    [Fact]
    public void ScheduleOptionsCarryTheComponentsSettings()
    {
        var view = Build(out _);
        view.CascadeMode = GanttCascadeMode.Strict;
        view.MinimumDuration = TimeSpan.FromHours(4);
        view.SnapMode = GanttSnapMode.WorkingTime;
        view.Calendar = GanttCalendar.StandardDays;

        var options = view.BuildScheduleOptions();

        options.Cascade.ShouldBe(GanttCascadeMode.Strict);
        options.MinimumDuration.ShouldBe(TimeSpan.FromHours(4));
        options.SnapToWorkingTime.ShouldBeTrue();
        options.Calendar.IsContinuous.ShouldBeFalse();
    }


    // =============================================================================================
    // Parity with MAUI
    // =============================================================================================

    [Fact]
    public void BarRectanglesMatchTheSharedMetricsExactly()
    {
        // The component must not do any positioning of its own — every rectangle it renders has to be
        // the one the shared engine computed, or the two hosts drift.
        var view = Build(out _);
        var metrics = new GanttLayoutMetrics(
            view.Model, view.RangeStart, view.EffectivePixelsPerDay,
            new GanttRowMetrics(40, 20, view.SummaryBarHeight, view.MilestoneSize, Math.Max(2, 20 / 4d))
        );

        foreach (var bar in view.Bars)
            bar.Rect.ShouldBe(metrics.BarOf(bar.Task));
    }


    [Fact]
    public void TheResolvedScaleMatchesTheSharedResolver()
    {
        var view = Build(out _);
        view.TimeScale = GanttTimeScale.Auto;
        view.PixelsPerDay = 800;
        view.Rebuild();

        view.EffectiveScale.ShouldBe(GanttGeometry.ResolveScale(800));
    }


    [Fact]
    public void SnappingFollowsTheEffectiveScale()
    {
        var view = Build(out _);
        view.SnapMode = GanttSnapMode.Scale;
        view.TimeScale = GanttTimeScale.Day;
        view.Rebuild();

        view.Snap(Day(3).AddHours(20)).ShouldBe(Day(4));
    }
}
