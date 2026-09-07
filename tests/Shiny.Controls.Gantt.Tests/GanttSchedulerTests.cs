namespace Shiny.Controls.Gantt.Tests;

public class GanttSchedulerTests
{
    static GanttScheduleOptions Continuous(GanttCascadeMode cascade = GanttCascadeMode.PushOnly) =>
        new() { Cascade = cascade, Calendar = GanttCalendar.Continuous };

    static GanttModel Chain(out GanttTask a, out GanttTask b, out GanttTask c)
    {
        a = Plan.Task("a", 0, 2);
        b = Plan.Task("b", 2, 4);
        c = Plan.Task("c", 4, 6);
        return GanttModel.Build([a, b, c], [Plan.Link("a", "b"), Plan.Link("b", "c")]);
    }


    // =============================================================================================
    // Move
    // =============================================================================================

    [Fact]
    public void Move_KeepsTheDuration()
    {
        var task = Plan.Task("a", 0, 3);
        var model = GanttModel.Build([task]);

        var plan = GanttScheduler.Move(model, task, Plan.Day(5), Continuous());

        plan.Changes[0].NewStart.Offset().ShouldBe(5);
        plan.Changes[0].NewEnd.Offset().ShouldBe(8);
    }


    [Fact]
    public void Move_DoesNotTouchTheTaskUntilApplied()
    {
        var task = Plan.Task("a", 0, 3);
        var model = GanttModel.Build([task]);

        var plan = GanttScheduler.Move(model, task, Plan.Day(5), Continuous());

        task.Start.Offset().ShouldBe(0);
        plan.Apply().ShouldBeTrue();
        task.Start.Offset().ShouldBe(5);
    }


    [Fact]
    public void Revert_PutsEverythingBack()
    {
        var model = Chain(out var a, out var b, out var c);

        var plan = GanttScheduler.Move(model, a, Plan.Day(4), Continuous());
        plan.Apply();

        a.Start.Offset().ShouldBe(4);
        b.Start.Offset().ShouldBe(6);

        plan.Revert().ShouldBeTrue();

        a.Start.Offset().ShouldBe(0);
        b.Start.Offset().ShouldBe(2);
        c.Start.Offset().ShouldBe(4);
    }


    [Fact]
    public void Apply_IsIdempotent()
    {
        var task = Plan.Task("a", 0, 3);
        var model = GanttModel.Build([task]);
        var plan = GanttScheduler.Move(model, task, Plan.Day(5), Continuous());

        plan.Apply().ShouldBeTrue();
        plan.Apply().ShouldBeFalse();
        task.Start.Offset().ShouldBe(5);
    }


    [Fact]
    public void Move_RespectsCanMove()
    {
        var task = Plan.Task("a", 0, 3);
        task.CanMove = false;
        var model = GanttModel.Build([task]);

        var plan = GanttScheduler.Move(model, task, Plan.Day(5), Continuous());

        plan.IsValid.ShouldBeFalse();
        plan.Apply().ShouldBeFalse();
        task.Start.Offset().ShouldBe(0);
    }


    // =============================================================================================
    // Cascade
    // =============================================================================================

    [Fact]
    public void PushOnly_PushesSuccessorsLater()
    {
        var model = Chain(out var a, out _, out _);

        var plan = GanttScheduler.Move(model, a, Plan.Day(5), Continuous());

        plan.Changes.Single(x => x.Task.Id == "b").NewStart.Offset().ShouldBe(7);
        plan.Changes.Single(x => x.Task.Id == "c").NewStart.Offset().ShouldBe(9);
    }


    [Fact]
    public void PushOnly_DoesNotPullSuccessorsEarlier()
    {
        // Moving A earlier leaves slack in front of B; pulling B back would be a surprise edit the
        // planner never asked for.
        var model = Chain(out var a, out _, out _);

        var plan = GanttScheduler.Move(model, a, Plan.Day(-3), Continuous());

        plan.Changes.ShouldNotContain(x => x.Task.Id == "b");
    }


    [Fact]
    public void Strict_PullsSuccessorsEarlierToo()
    {
        var model = Chain(out var a, out _, out _);

        var plan = GanttScheduler.Move(model, a, Plan.Day(-3), Continuous(GanttCascadeMode.Strict));

        plan.Changes.Single(x => x.Task.Id == "b").NewStart.Offset().ShouldBe(-1);
        plan.Changes.Single(x => x.Task.Id == "c").NewStart.Offset().ShouldBe(1);
    }


    [Fact]
    public void CascadeNone_MovesNothingElse()
    {
        var model = Chain(out var a, out _, out _);

        var plan = GanttScheduler.Move(model, a, Plan.Day(5), Continuous(GanttCascadeMode.None));

        plan.Changes.Count.ShouldBe(1);
        plan.Issues.ShouldContain(x => x.Code == GanttValidationCode.DependencyViolated);
    }


    [Fact]
    public void CascadedTasksAreFlagged()
    {
        var model = Chain(out var a, out _, out _);

        var plan = GanttScheduler.Move(model, a, Plan.Day(5), Continuous());

        plan.Changes[0].IsCascade.ShouldBeFalse();
        plan.Cascade.Select(x => x.Task.Id).ShouldBe(["b", "c"]);
    }


    [Fact]
    public void AManuallyScheduledTaskIsNotDraggedAlong()
    {
        var model = Chain(out var a, out var b, out _);
        b.ManuallyScheduled = true;

        var plan = GanttScheduler.Move(model, a, Plan.Day(5), Continuous());

        plan.Changes.ShouldNotContain(x => x.Task.Id == "b");
        plan.Issues.ShouldContain(x => x.Code == GanttValidationCode.DependencyViolated);
    }


    [Fact]
    public void ACycleIsLeftAloneRatherThanLoopingForever()
    {
        var a = Plan.Task("a", 0, 2);
        var b = Plan.Task("b", 2, 4);
        var model = GanttModel.Build([a, b], [Plan.Link("a", "b"), Plan.Link("b", "a")]);

        Should.CompleteIn(
            () => GanttScheduler.Move(model, a, Plan.Day(5), Continuous()),
            TimeSpan.FromSeconds(5)
        );
    }


    // =============================================================================================
    // Dependency types
    // =============================================================================================

    [Theory]
    [InlineData(GanttDependencyType.FinishToStart, 0, 7)]   // b starts when a finishes (day 5+2)
    [InlineData(GanttDependencyType.StartToStart, 0, 5)]    // b starts when a starts
    [InlineData(GanttDependencyType.FinishToFinish, 0, 5)]  // b finishes when a does, so starts 2 before
    [InlineData(GanttDependencyType.StartToFinish, 0, 3)]   // b finishes when a starts
    public void EachDependencyTypeBoundsTheRightEdge(GanttDependencyType type, double lag, double expectedStart)
    {
        var a = Plan.Task("a", 0, 2);
        var b = Plan.Task("b", 2, 4); // two days long
        var model = GanttModel.Build([a, b], [Plan.Link("a", "b", type, lag)]);

        var plan = GanttScheduler.Move(model, a, Plan.Day(5), Continuous(GanttCascadeMode.Strict));

        plan.Changes.Single(x => x.Task.Id == "b").NewStart.Offset().ShouldBe(expectedStart);
    }


    [Fact]
    public void PositiveLagDelaysTheSuccessor()
    {
        var a = Plan.Task("a", 0, 2);
        var b = Plan.Task("b", 2, 4);
        var model = GanttModel.Build([a, b], [Plan.Link("a", "b", lagDays: 3)]);

        var plan = GanttScheduler.Move(model, a, Plan.Day(5), Continuous());

        plan.Changes.Single(x => x.Task.Id == "b").NewStart.Offset().ShouldBe(10);
    }


    [Fact]
    public void NegativeLagIsLeadTimeAndIsLegal()
    {
        // "Start two days before the predecessor finishes" is an ordinary plan, not an error.
        var a = Plan.Task("a", 0, 4);
        var b = Plan.Task("b", 4, 6);
        var model = GanttModel.Build([a, b], [Plan.Link("a", "b", lagDays: -2)]);

        var plan = GanttScheduler.Move(model, a, Plan.Day(5), Continuous(GanttCascadeMode.Strict));

        plan.Changes.Single(x => x.Task.Id == "b").NewStart.Offset().ShouldBe(7);
    }


    // =============================================================================================
    // Summaries
    // =============================================================================================

    [Fact]
    public void MovingASummaryShiftsItsWholeSubtree()
    {
        var phase = Plan.Task("phase", 0, 5);
        var x = Plan.Task("x", 0, 2, parent: "phase");
        var y = Plan.Task("y", 3, 5, parent: "phase");
        var model = GanttModel.Build([phase, x, y]);

        var plan = GanttScheduler.Move(model, model["phase"]!, Plan.Day(10), Continuous());
        plan.Apply();

        // The internal shape of the phase is preserved: x still leads y by a day.
        x.Start.Offset().ShouldBe(10);
        y.Start.Offset().ShouldBe(13);
        phase.Start.Offset().ShouldBe(10);
        phase.End.Offset().ShouldBe(15);
    }


    [Fact]
    public void ASummaryCannotBeResizedByDefault()
    {
        var phase = Plan.Task("phase", 0, 5);
        var model = GanttModel.Build([phase, Plan.Task("x", 0, 5, parent: "phase")]);

        var plan = GanttScheduler.Resize(model, model["phase"]!, Plan.Day(0), Plan.Day(9), Continuous());

        plan.IsValid.ShouldBeFalse();
    }


    [Fact]
    public void MovingAChildRollsTheSummaryUpInTheSamePlan()
    {
        // Otherwise the child bar and the bracket above it disagree until the next rebuild.
        var phase = Plan.Task("phase", 0, 5);
        var x = Plan.Task("x", 0, 5, parent: "phase");
        var model = GanttModel.Build([phase, x]);

        var plan = GanttScheduler.Move(model, x, Plan.Day(4), Continuous());

        plan.Changes.Single(t => t.Task.Id == "phase").NewEnd.Offset().ShouldBe(9);
    }


    [Fact]
    public void ASummaryIsALegalPredecessor()
    {
        var phase = Plan.Task("phase", 0, 5);
        var x = Plan.Task("x", 0, 5, parent: "phase");
        var after = Plan.Task("after", 5, 7);
        var model = GanttModel.Build([phase, x, after], [Plan.Link("phase", "after")]);

        // Moving the child pushes the summary out, which must in turn push the task linked to it.
        var plan = GanttScheduler.Move(model, x, Plan.Day(3), Continuous());

        plan.Changes.Single(t => t.Task.Id == "after").NewStart.Offset().ShouldBe(8);
    }


    // =============================================================================================
    // Resize / progress
    // =============================================================================================

    [Fact]
    public void Resize_HoldsTheOppositeEdge()
    {
        var task = Plan.Task("a", 0, 3);
        var model = GanttModel.Build([task]);

        var plan = GanttScheduler.Resize(model, task, Plan.Day(0), Plan.Day(6), Continuous());

        plan.Kind.ShouldBe(GanttChangeKind.ResizeEnd);
        plan.Changes[0].NewStart.Offset().ShouldBe(0);
        plan.Changes[0].NewEnd.Offset().ShouldBe(6);
    }


    [Fact]
    public void Resize_HonoursTheMinimumDuration()
    {
        var task = Plan.Task("a", 0, 3);
        var model = GanttModel.Build([task]);
        var opts = Continuous();
        opts.MinimumDuration = TimeSpan.FromDays(1);

        var plan = GanttScheduler.Resize(model, task, Plan.Day(0), Plan.Day(0.1), opts);

        plan.Changes[0].NewEnd.Offset().ShouldBe(1);
    }


    [Fact]
    public void Resize_PastTheFarEdgeCollapsesRatherThanInverting()
    {
        var task = Plan.Task("a", 0, 3);
        var model = GanttModel.Build([task]);

        var plan = GanttScheduler.Resize(model, task, Plan.Day(0), Plan.Day(-4), Continuous());

        plan.Changes[0].NewEnd.ShouldBeGreaterThanOrEqualTo(plan.Changes[0].NewStart);
    }


    [Fact]
    public void SetProgress_ClampsAndDoesNotCascade()
    {
        var task = Plan.Task("a", 0, 3);

        var plan = GanttScheduler.SetProgress(task, 1.4);
        plan.Apply();

        task.Progress.ShouldBe(1.0);
        plan.Changes.Count.ShouldBe(1);
    }


    [Fact]
    public void SetProgress_RespectsCanChangeProgress()
    {
        var task = Plan.Task("a", 0, 3);
        task.CanChangeProgress = false;

        GanttScheduler.SetProgress(task, 0.5).IsValid.ShouldBeFalse();
    }


    // =============================================================================================
    // Constraints and bounds
    // =============================================================================================

    [Fact]
    public void MustStartOn_PinsTheTask()
    {
        var task = Plan.Task("a", 0, 3);
        task.Constraint = GanttConstraintType.MustStartOn;
        task.ConstraintDate = Plan.Day(0);
        var model = GanttModel.Build([task]);

        var plan = GanttScheduler.Move(model, task, Plan.Day(9), Continuous());

        plan.Changes[0].NewStart.Offset().ShouldBe(0);
        plan.Issues.ShouldContain(x => x.Code == GanttValidationCode.ConstraintViolated);
    }


    [Fact]
    public void StartNoEarlierThan_ClampsOnlyInOneDirection()
    {
        var task = Plan.Task("a", 5, 8);
        task.Constraint = GanttConstraintType.StartNoEarlierThan;
        task.ConstraintDate = Plan.Day(3);
        var model = GanttModel.Build([task]);

        GanttScheduler.Move(model, task, Plan.Day(0), Continuous()).Changes[0].NewStart.Offset().ShouldBe(3);
        GanttScheduler.Move(model, task, Plan.Day(9), Continuous()).Changes[0].NewStart.Offset().ShouldBe(9);
    }


    [Fact]
    public void FinishNoLaterThan_PullsTheWholeBarBack()
    {
        var task = Plan.Task("a", 0, 3);
        task.Constraint = GanttConstraintType.FinishNoLaterThan;
        task.ConstraintDate = Plan.Day(10);
        var model = GanttModel.Build([task]);

        var plan = GanttScheduler.Move(model, task, Plan.Day(20), Continuous());

        plan.Changes[0].NewEnd.Offset().ShouldBe(10);
        plan.Changes[0].NewStart.Offset().ShouldBe(7);
    }


    [Fact]
    public void ConstraintsCanBeTurnedOff()
    {
        var task = Plan.Task("a", 0, 3);
        task.Constraint = GanttConstraintType.MustStartOn;
        task.ConstraintDate = Plan.Day(0);
        var model = GanttModel.Build([task]);
        var opts = Continuous();
        opts.EnforceConstraints = false;

        GanttScheduler.Move(model, task, Plan.Day(9), opts).Changes[0].NewStart.Offset().ShouldBe(9);
    }


    [Fact]
    public void MinAndMaxDateClampThePlan()
    {
        var task = Plan.Task("a", 0, 3);
        var model = GanttModel.Build([task]);
        var opts = Continuous();
        opts.MinDate = Plan.Day(1);

        var plan = GanttScheduler.Move(model, task, Plan.Day(-5), opts);

        plan.Changes[0].NewStart.Offset().ShouldBe(1);
        plan.Issues.ShouldContain(x => x.Code == GanttValidationCode.OutOfRange);
    }


    // =============================================================================================
    // Working calendars
    // =============================================================================================

    [Fact]
    public void AMoveOntoAWeekendLandsOnMonday()
    {
        var task = Plan.Task("a", 0, 2);
        var model = GanttModel.Build([task], null, new GanttModelOptions { Calendar = GanttCalendar.StandardDays });
        var opts = new GanttScheduleOptions { Calendar = GanttCalendar.StandardDays };

        var plan = GanttScheduler.Move(model, task, Plan.Day(5), opts);

        plan.Changes[0].NewStart.Offset().ShouldBe(7);
    }


    [Fact]
    public void AMoveAcrossAWeekendKeepsTheWorkingDuration()
    {
        // Three working days stays three working days and simply gets wider.
        var task = Plan.Task("a", 0, 3);
        var model = GanttModel.Build([task], null, new GanttModelOptions { Calendar = GanttCalendar.StandardDays });
        var opts = new GanttScheduleOptions { Calendar = GanttCalendar.StandardDays };

        var plan = GanttScheduler.Move(model, task, Plan.Day(3), opts);

        plan.Changes[0].NewStart.Offset().ShouldBe(3);
        plan.Changes[0].NewEnd.Offset().ShouldBe(8); // Thu + Fri + Mon
    }


    [Fact]
    public void CascadeAcrossAWeekendUsesWorkingTime()
    {
        var a = Plan.Task("a", 0, 1);
        var b = Plan.Task("b", 1, 2);
        var calendar = GanttCalendar.StandardDays;
        var model = GanttModel.Build([a, b], [Plan.Link("a", "b")], new GanttModelOptions { Calendar = calendar });

        // Push A onto Friday; B must land on the following Monday, not on Saturday.
        var plan = GanttScheduler.Move(model, a, Plan.Day(4), new GanttScheduleOptions { Calendar = calendar });

        plan.Changes.Single(x => x.Task.Id == "b").NewStart.Offset().ShouldBe(7);
    }


    // =============================================================================================
    // Links
    // =============================================================================================

    [Fact]
    public void AddDependency_RefusesToCloseACycle()
    {
        var model = Chain(out _, out _, out _);

        var plan = GanttScheduler.AddDependency(model, Plan.Link("c", "a"), Continuous());

        plan.IsValid.ShouldBeFalse();
        plan.Issues.ShouldContain(x => x.Code == GanttValidationCode.CircularDependency && x.IsBlocking);
    }


    [Fact]
    public void AddDependency_RefusesASelfLink() =>
        GanttScheduler.AddDependency(Chain(out _, out _, out _), Plan.Link("a", "a"), Continuous()).IsValid.ShouldBeFalse();


    [Fact]
    public void AddDependency_PushesTheSuccessorIntoPlace()
    {
        var a = Plan.Task("a", 0, 5);
        var b = Plan.Task("b", 1, 3);
        var model = GanttModel.Build([a, b]);

        var plan = GanttScheduler.AddDependency(model, Plan.Link("a", "b"), Continuous());

        plan.IsValid.ShouldBeTrue();
        plan.Changes.Single(x => x.Task.Id == "b").NewStart.Offset().ShouldBe(5);
    }


    [Fact]
    public void WouldCreateCycle_WalksTheWholeGraph()
    {
        var model = Chain(out _, out _, out _);

        GanttScheduler.WouldCreateCycle(model, "c", "a").ShouldBeTrue();
        GanttScheduler.WouldCreateCycle(model, "a", "c").ShouldBeFalse();
    }


    [Fact]
    public void Reschedule_ReDerivesEveryTaskFromTheGraph()
    {
        var a = Plan.Task("a", 0, 2);
        var b = Plan.Task("b", 9, 11); // adrift, well past where its link puts it
        var model = GanttModel.Build([a, b], [Plan.Link("a", "b")]);

        var plan = GanttScheduler.Reschedule(model, Continuous(GanttCascadeMode.Strict));
        plan.Apply();

        b.Start.Offset().ShouldBe(2);
    }
}
