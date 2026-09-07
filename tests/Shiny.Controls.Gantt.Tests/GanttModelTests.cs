namespace Shiny.Controls.Gantt.Tests;

public class GanttModelTests
{
    [Fact]
    public void Build_AcceptsNulls()
    {
        var model = GanttModel.Build(null, null);

        model.AllTasks.ShouldBeEmpty();
        model.Rows.ShouldBeEmpty();
        model.Issues.ShouldBeEmpty();
    }


    [Fact]
    public void AFlatListIsReassembledIntoATree()
    {
        var model = GanttModel.Build([
            Plan.Task("phase", 0, 5),
            Plan.Task("a", 0, 2, parent: "phase"),
            Plan.Task("b", 2, 5, parent: "phase")
        ]);

        model.Roots.Count.ShouldBe(1);
        model.ChildrenOf("phase").Select(x => x.Id).ShouldBe(["a", "b"]);
        model["a"]!.Depth.ShouldBe(1);
        model["phase"]!.HasChildren.ShouldBeTrue();
    }


    [Fact]
    public void ANestedTreeIsAcceptedUnchanged()
    {
        var phase = Plan.Task("phase", 0, 5);
        phase.Children.Add(Plan.Task("a", 0, 2));
        phase.Children.Add(Plan.Task("b", 2, 5));

        var model = GanttModel.Build([phase]);

        model.AllTasks.Select(x => x.Id).ShouldBe(["phase", "a", "b"]);
        model.ChildrenOf("phase").Count.ShouldBe(2);
        model["a"]!.ParentId.ShouldBe("phase");
    }


    [Fact]
    public void NestingAndFlatParentIdsMixInOneSource()
    {
        var phase = Plan.Task("phase", 0, 5);
        phase.Children.Add(Plan.Task("nested", 0, 2));

        var model = GanttModel.Build([phase, Plan.Task("grafted", 2, 5, parent: "phase")]);

        model.ChildrenOf("phase").Select(x => x.Id).ShouldBe(["nested", "grafted"]);
    }


    [Fact]
    public void TheModelDoesNotReshapeTheConsumersOwnCollections()
    {
        // Both hosts bind to these collections. Re-parenting one mid-build would raise a change
        // notification into a layout pass that is already running.
        var phase = Plan.Task("phase", 0, 5);
        var child = Plan.Task("a", 0, 2, parent: "phase");

        GanttModel.Build([phase, child]);

        phase.Children.ShouldBeEmpty();
        child.Parent.ShouldBe(phase);
        phase.HasChildren.ShouldBeTrue();
    }


    [Fact]
    public void AParentIsPromotedToASummary()
    {
        var model = GanttModel.Build([Plan.Task("phase", 0, 5), Plan.Task("a", 0, 2, parent: "phase")]);

        model["phase"]!.Kind.ShouldBe(GanttTaskKind.Summary);
        model["a"]!.Kind.ShouldBe(GanttTaskKind.Task);
    }


    [Fact]
    public void AProjectRootKeepsItsKind()
    {
        var model = GanttModel.Build([
            Plan.Task("root", 0, 5, kind: GanttTaskKind.Project),
            Plan.Task("a", 0, 2, parent: "root")
        ]);

        model["root"]!.Kind.ShouldBe(GanttTaskKind.Project);
    }


    [Fact]
    public void ASummarySpansItsChildren()
    {
        var model = GanttModel.Build([
            Plan.Task("phase", 3, 4),
            Plan.Task("a", 0, 2, parent: "phase"),
            Plan.Task("b", 5, 9, parent: "phase")
        ]);

        model["phase"]!.Start.Offset().ShouldBe(0);
        model["phase"]!.End.Offset().ShouldBe(9);
    }


    [Fact]
    public void RollupsPropagateThroughThreeLevels()
    {
        var model = GanttModel.Build([
            Plan.Task("root", 0, 1),
            Plan.Task("phase", 0, 1, parent: "root"),
            Plan.Task("leaf", 4, 8, parent: "phase")
        ]);

        model["root"]!.Start.Offset().ShouldBe(4);
        model["root"]!.End.Offset().ShouldBe(8);
    }


    [Fact]
    public void SummaryProgressIsWeightedByDuration()
    {
        // A two-hour task at 100% must not drag a six-week one at 0% up to "half done".
        var model = GanttModel.Build([
            Plan.Task("phase", 0, 1),
            Plan.Task("tiny", 0, 0.0833, parent: "phase", progress: 1.0),
            Plan.Task("huge", 0, 30, parent: "phase", progress: 0.0)
        ]);

        model["phase"]!.Progress.ShouldBeLessThan(0.01);
    }


    [Fact]
    public void RollupCanBeTurnedOff()
    {
        var model = GanttModel.Build(
            [Plan.Task("phase", 3, 4), Plan.Task("a", 0, 9, parent: "phase")],
            null,
            new GanttModelOptions { RollUpSummaries = false }
        );

        model["phase"]!.Start.Offset().ShouldBe(3);
        model["phase"]!.End.Offset().ShouldBe(4);
    }


    [Fact]
    public void ACollapsedSummaryHidesItsSubtree()
    {
        var phase = Plan.Task("phase", 0, 5);
        phase.IsExpanded = false;

        var model = GanttModel.Build([phase, Plan.Task("a", 0, 2, parent: "phase")]);

        model.Rows.Select(x => x.Task.Id).ShouldBe(["phase"]);
        model.AllTasks.Count.ShouldBe(2);
        model["a"]!.IsVisible.ShouldBeFalse();
    }


    [Fact]
    public void RowsCarryDepthAndIndex()
    {
        var model = GanttModel.Build([
            Plan.Task("phase", 0, 5),
            Plan.Task("a", 0, 2, parent: "phase"),
            Plan.Task("b", 2, 5)
        ]);

        model.Rows.Select(x => (x.Task.Id, x.Index, x.Depth)).ShouldBe([("phase", 0, 0), ("a", 1, 1), ("b", 2, 0)]);
        model.Rows[0].HasChildren.ShouldBeTrue();
        model.RowIndexOf("b").ShouldBe(2);
    }


    [Fact]
    public void DuplicateIdsAreReportedAndTheSecondIsDropped()
    {
        var model = GanttModel.Build([Plan.Task("a", 0, 2), Plan.Task("a", 3, 5)]);

        model.AllTasks.Count.ShouldBe(1);
        model.Issues.ShouldContain(x => x.Code == GanttValidationCode.DuplicateId);
    }


    [Fact]
    public void AHierarchyCycleIsBrokenRatherThanHanging()
    {
        var a = Plan.Task("a", 0, 2, parent: "b");
        var b = Plan.Task("b", 0, 2, parent: "a");

        var model = GanttModel.Build([a, b]);

        model.Issues.ShouldContain(x => x.Code == GanttValidationCode.CircularHierarchy);
        model.Roots.ShouldNotBeEmpty();
    }


    [Fact]
    public void ADanglingDependencyIsDroppedAndReported()
    {
        var model = GanttModel.Build([Plan.Task("a", 0, 2)], [Plan.Link("a", "ghost")]);

        model.Dependencies.ShouldBeEmpty();
        model.Issues.ShouldContain(x => x.Code == GanttValidationCode.UnknownTaskReference);
    }


    [Fact]
    public void ADependencyCycleIsReportedAndItsTasksLeftOutOfTheOrder()
    {
        var model = GanttModel.Build(
            [Plan.Task("a", 0, 2), Plan.Task("b", 2, 4), Plan.Task("c", 4, 6)],
            [Plan.Link("a", "b"), Plan.Link("b", "c"), Plan.Link("c", "a")]
        );

        model.HasCycle.ShouldBeTrue();
        model.TopologicalOrder.ShouldBeEmpty();
        model.Issues.Count(x => x.Code == GanttValidationCode.CircularDependency).ShouldBe(3);
    }


    [Fact]
    public void TopologicalOrderPutsPredecessorsFirst()
    {
        var model = GanttModel.Build(
            [Plan.Task("c", 4, 6), Plan.Task("a", 0, 2), Plan.Task("b", 2, 4)],
            [Plan.Link("a", "b"), Plan.Link("b", "c")]
        );

        var order = model.TopologicalOrder.ToList();
        order.IndexOf("a").ShouldBeLessThan(order.IndexOf("b"));
        order.IndexOf("b").ShouldBeLessThan(order.IndexOf("c"));
    }


    [Fact]
    public void TheExtentCoversBaselinesAndDeadlines()
    {
        var task = Plan.Task("a", 2, 4);
        task.BaselineStart = Plan.Day(0);
        task.Deadline = Plan.Day(9);

        var model = GanttModel.Build([task]);

        model.Start.Offset().ShouldBe(0);
        model.End.Offset().ShouldBe(9);
    }


    [Fact]
    public void AMissedDeadlineIsReportedButNotBlocking()
    {
        var task = Plan.Task("a", 0, 5);
        task.Deadline = Plan.Day(3);

        var model = GanttModel.Build([task]);

        task.IsOverdue.ShouldBeTrue();
        var issue = model.Issues.Single(x => x.Code == GanttValidationCode.DeadlineExceeded);
        issue.IsBlocking.ShouldBeFalse();
    }


    [Fact]
    public void AMilestoneHasNoDuration()
    {
        var model = GanttModel.Build([Plan.Milestone("m", 3)]);

        model["m"]!.Duration.ShouldBe(TimeSpan.Zero);
        model.End.Offset().ShouldBe(3);
    }
}
