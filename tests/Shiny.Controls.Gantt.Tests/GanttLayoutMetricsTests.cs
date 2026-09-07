namespace Shiny.Controls.Gantt.Tests;

public class GanttLayoutMetricsTests
{
    const double Ppd = 40;
    static readonly GanttRowMetrics Rows = new(RowHeight: 40, BarHeight: 20, SummaryHeight: 10, MilestoneSize: 16, BaselineHeight: 4);

    static GanttLayoutMetrics Metrics(GanttModel model) => new(model, Plan.Monday, Ppd, Rows);


    [Fact]
    public void ABarSpansItsDatesAndIsCentredInItsRow()
    {
        var model = GanttModel.Build([Plan.Task("a", 1, 3)]);
        var bar = Metrics(model).BarOf(model["a"]!);

        bar.X.ShouldBe(40);
        bar.Width.ShouldBe(80);
        bar.CenterY.ShouldBe(20);
        bar.Height.ShouldBe(20);
    }


    [Fact]
    public void ASubPixelTaskStillGetsAPixel()
    {
        // The difference between "this task is very short" and "this task is gone".
        var model = GanttModel.Build([Plan.Task("a", 0, 0.0001)]);
        Metrics(model).BarOf(model["a"]!).Width.ShouldBe(1);
    }


    [Fact]
    public void AMilestoneIsASquareCentredOnItsDate()
    {
        var model = GanttModel.Build([Plan.Milestone("m", 2)]);
        var bar = Metrics(model).BarOf(model["m"]!);

        bar.CenterX.ShouldBe(80);
        bar.Width.ShouldBe(16);
        bar.Height.ShouldBe(16);
    }


    [Fact]
    public void ASummaryUsesTheShorterBracketHeight()
    {
        var model = GanttModel.Build([Plan.Task("p", 0, 4), Plan.Task("c", 0, 4, parent: "p")]);
        Metrics(model).BarOf(model["p"]!).Height.ShouldBe(10);
    }


    [Fact]
    public void ProgressFillsAFractionOfTheBar()
    {
        var model = GanttModel.Build([Plan.Task("a", 0, 4, progress: 0.25)]);
        var metrics = Metrics(model);

        metrics.ProgressOf(model["a"]!).Width.ShouldBe(metrics.BarOf(model["a"]!).Width * 0.25);
    }


    [Fact]
    public void AMilestoneHasNoProgressFill()
    {
        var m = Plan.Milestone("m", 2);
        m.Progress = 0.5;
        var model = GanttModel.Build([m]);

        Metrics(model).ProgressOf(model["m"]!).IsEmpty.ShouldBeTrue();
    }


    [Fact]
    public void ABaselineSitsBelowTheLiveBarRatherThanBehindIt()
    {
        // Overlapping the two makes a task exactly on plan look like it has no baseline at all.
        var task = Plan.Task("a", 2, 4);
        task.BaselineStart = Plan.Day(1);
        task.BaselineEnd = Plan.Day(3);
        var model = GanttModel.Build([task]);
        var metrics = Metrics(model);

        var bar = metrics.BarOf(model["a"]!);
        var baseline = metrics.BaselineOf(model["a"]!);

        baseline.Y.ShouldBeGreaterThan(bar.Bottom - 0.001);
        baseline.X.ShouldBe(40);
        baseline.Width.ShouldBe(80);
    }


    [Fact]
    public void RowsMapBothWays()
    {
        var model = GanttModel.Build([Plan.Task("a", 0, 1), Plan.Task("b", 1, 2), Plan.Task("c", 2, 3)]);
        var metrics = Metrics(model);

        metrics.RowIndexOf("b").ShouldBe(1);
        metrics.RowAt(50).ShouldBe(1);
        metrics.RowAt(-5).ShouldBe(-1);
        metrics.RowAt(5000).ShouldBe(-1);
        metrics.ContentHeight.ShouldBe(120);
    }


    [Fact]
    public void ATaskInsideACollapsedSummaryHasNoRectangle()
    {
        var phase = Plan.Task("phase", 0, 5);
        phase.IsExpanded = false;
        var model = GanttModel.Build([phase, Plan.Task("a", 0, 2, parent: "phase")]);

        Metrics(model).BarOf(model["a"]!).IsEmpty.ShouldBeTrue();
    }


    // =============================================================================================
    // Dependency routing
    // =============================================================================================

    [Fact]
    public void AForwardLinkTakesTheThreeSegmentRoute()
    {
        var model = GanttModel.Build(
            [Plan.Task("a", 0, 2), Plan.Task("b", 4, 6)],
            [Plan.Link("a", "b")]
        );
        var route = Metrics(model).RouteOf(model.Dependencies[0]);

        route.Count.ShouldBe(4);
        route[0].X.ShouldBe(80);   // a's right edge
        route[^1].X.ShouldBe(160); // b's left edge
        route[0].Y.ShouldBe(20);
        route[^1].Y.ShouldBe(60);
    }


    [Fact]
    public void ABackwardLinkDetoursThroughTheGutter()
    {
        // Going straight at the target would draw the line back through the predecessor's own bar.
        var model = GanttModel.Build(
            [Plan.Task("a", 5, 8), Plan.Task("b", 0, 2)],
            [Plan.Link("a", "b")]
        );
        var route = Metrics(model).RouteOf(model.Dependencies[0]);

        route.Count.ShouldBe(6);
        route[2].Y.ShouldBe(40); // dropped into the gutter between rows 0 and 1
        route[3].Y.ShouldBe(40);
    }


    [Fact]
    public void RoutesAttachToTheEdgesTheirTypeNames()
    {
        var tasks = new[] { Plan.Task("a", 0, 2), Plan.Task("b", 4, 6) };
        var metrics = new Func<GanttDependencyType, IReadOnlyList<GanttPoint>>(type =>
        {
            var model = GanttModel.Build(tasks, [Plan.Link("a", "b", type)]);
            return Metrics(model).RouteOf(model.Dependencies[0]);
        });

        // Finish-to-* leaves a's right edge (x=80); start-to-* leaves its left (x=0).
        metrics(GanttDependencyType.FinishToStart)[0].X.ShouldBe(80);
        metrics(GanttDependencyType.StartToStart)[0].X.ShouldBe(0);

        // *-to-start arrives at b's left edge (x=160); *-to-finish at its right (x=240).
        metrics(GanttDependencyType.FinishToStart)[^1].X.ShouldBe(160);
        metrics(GanttDependencyType.FinishToFinish)[^1].X.ShouldBe(240);
    }


    [Fact]
    public void ARouteToAHiddenTaskIsEmpty()
    {
        var phase = Plan.Task("phase", 0, 5);
        phase.IsExpanded = false;
        var model = GanttModel.Build(
            [Plan.Task("a", 0, 2), phase, Plan.Task("hidden", 3, 5, parent: "phase")],
            [Plan.Link("a", "hidden")]
        );

        Metrics(model).RouteOf(model.Dependencies[0]).ShouldBeEmpty();
    }


    [Fact]
    public void EveryRouteSegmentIsAxisAligned()
    {
        // Elbow routing only: a diagonal means a bend was computed wrong.
        var model = GanttModel.Build(
            [Plan.Task("a", 0, 2), Plan.Task("b", 1, 3), Plan.Task("c", 6, 8)],
            [Plan.Link("a", "b"), Plan.Link("c", "a"), Plan.Link("b", "c", GanttDependencyType.StartToStart)]
        );
        var metrics = Metrics(model);

        foreach (var dep in model.Dependencies)
        {
            var route = metrics.RouteOf(dep);
            for (var i = 1; i < route.Count; i++)
            {
                var horizontal = Math.Abs(route[i].Y - route[i - 1].Y) < 0.001;
                var vertical = Math.Abs(route[i].X - route[i - 1].X) < 0.001;
                (horizontal || vertical).ShouldBeTrue($"{dep} segment {i} is diagonal");
            }
        }
    }


    // =============================================================================================
    // Hit testing
    // =============================================================================================

    [Fact]
    public void HitTest_DistinguishesEdgesFromTheBody()
    {
        var model = GanttModel.Build([Plan.Task("a", 0, 4)]);
        var metrics = Metrics(model);
        var task = model["a"]!;

        metrics.HitTest(task, 2, 20).ShouldBe(GanttHitTarget.StartEdge);
        metrics.HitTest(task, 80, 20).ShouldBe(GanttHitTarget.Body);
        metrics.HitTest(task, 158, 20).ShouldBe(GanttHitTarget.EndEdge);
        metrics.HitTest(task, 400, 20).ShouldBeNull();
    }


    [Fact]
    public void HitTest_FindsTheProgressHandle()
    {
        var model = GanttModel.Build([Plan.Task("a", 0, 4, progress: 0.5)]);
        Metrics(model).HitTest(model["a"]!, 80, 20).ShouldBe(GanttHitTarget.Progress);
    }


    [Fact]
    public void HitTest_GivesUpTheGripsOnAShortBar()
    {
        // A bar you cannot drag at all is worse than one you cannot resize; resizing is what zoom is for.
        var model = GanttModel.Build([Plan.Task("a", 0, 0.25)]);
        var task = model["a"]!;

        Metrics(model).HitTest(task, 1, 20).ShouldBe(GanttHitTarget.Body);
    }


    [Fact]
    public void HitTest_HasTouchSlop()
    {
        var model = GanttModel.Build([Plan.Task("a", 1, 3)]);
        // Two pixels above the top of a 20px bar centred at y=20 — still a hit for a finger.
        Metrics(model).HitTest(model["a"]!, 80, 8).ShouldNotBeNull();
    }


    [Fact]
    public void AMilestoneIsAllBody()
    {
        var model = GanttModel.Build([Plan.Milestone("m", 2)]);
        Metrics(model).HitTest(model["m"]!, 80, 20).ShouldBe(GanttHitTarget.Body);
    }
}
