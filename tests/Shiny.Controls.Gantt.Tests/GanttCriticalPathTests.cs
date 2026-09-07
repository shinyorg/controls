namespace Shiny.Controls.Gantt.Tests;

public class GanttCriticalPathTests
{
    /// <summary>
    /// A diamond: A feeds both B (two days) and C (four days), and both feed D. The long arm through
    /// C is critical; B has two days of slack. It is the smallest plan where a wrong answer is
    /// obvious, which is why every assertion here is built on it.
    /// </summary>
    static GanttModel Diamond(GanttModelOptions? options = null)
    {
        var tasks = new[]
        {
            Plan.Task("a", 0, 1),
            Plan.Task("b", 1, 3),
            Plan.Task("c", 1, 5),
            Plan.Task("d", 5, 6)
        };
        var links = new[]
        {
            Plan.Link("a", "b"), Plan.Link("a", "c"),
            Plan.Link("b", "d"), Plan.Link("c", "d")
        };
        return GanttModel.Build(tasks, links, options);
    }


    [Fact]
    public void TheLongArmIsCriticalAndTheShortOneIsNot()
    {
        var model = Diamond();

        model["a"]!.IsCritical.ShouldBeTrue();
        model["c"]!.IsCritical.ShouldBeTrue();
        model["d"]!.IsCritical.ShouldBeTrue();
        model["b"]!.IsCritical.ShouldBeFalse();
    }


    [Fact]
    public void SlackIsTheGapToTheLatestFinish()
    {
        var model = Diamond();

        model["b"]!.TotalSlack.ShouldBe(TimeSpan.FromDays(2));
        model["c"]!.TotalSlack.ShouldBe(TimeSpan.Zero);
    }


    [Fact]
    public void ALinkIsCriticalOnlyWhenBothEndsAre()
    {
        // Highlighting a->b would draw a red arrow across slack that genuinely exists.
        var model = Diamond();

        model.Dependencies.Single(x => x.PredecessorId == "a" && x.SuccessorId == "c").IsCritical.ShouldBeTrue();
        model.Dependencies.Single(x => x.PredecessorId == "a" && x.SuccessorId == "b").IsCritical.ShouldBeFalse();
        model.Dependencies.Single(x => x.PredecessorId == "b" && x.SuccessorId == "d").IsCritical.ShouldBeFalse();
    }


    [Fact]
    public void ATrailingTaskWithNoSuccessorsGetsSlackAgainstTheProjectFinish()
    {
        var model = GanttModel.Build([Plan.Task("a", 0, 2), Plan.Task("late", 0, 9)]);

        model["late"]!.IsCritical.ShouldBeTrue();
        model["a"]!.TotalSlack.ShouldBe(TimeSpan.FromDays(7));
    }


    [Fact]
    public void TheThresholdWidensWhatCountsAsCritical()
    {
        var model = Diamond(new GanttModelOptions { CriticalSlackThreshold = TimeSpan.FromDays(2) });

        model["b"]!.IsCritical.ShouldBeTrue();
    }


    [Fact]
    public void ItCanBeTurnedOff()
    {
        var model = Diamond(new GanttModelOptions { ComputeCriticalPath = false });

        model.AllTasks.ShouldAllBe(x => !x.IsCritical);
        model.Dependencies.ShouldAllBe(x => !x.IsCritical);
    }


    [Fact]
    public void LatestFinishIsExposedForTooltips()
    {
        var model = Diamond();

        model.TryGetLatestFinish("b", out var lf).ShouldBeTrue();
        lf.Offset().ShouldBe(5);
    }


    [Fact]
    public void ABrokenLinkYieldsNegativeSlack()
    {
        // The plan already violates its own dependency; the answer is "minus a day of slack", not
        // a clamp to zero, because that is the number a planner needs to see.
        var model = GanttModel.Build(
            [Plan.Task("a", 0, 3), Plan.Task("b", 2, 4)],
            [Plan.Link("a", "b")]
        );

        model["a"]!.TotalSlack.ShouldBe(TimeSpan.FromDays(-1));
        model["a"]!.IsCritical.ShouldBeTrue();
    }


    [Fact]
    public void TasksOnACycleAreSkippedRatherThanHanging()
    {
        var model = GanttModel.Build(
            [Plan.Task("a", 0, 2), Plan.Task("b", 2, 4), Plan.Task("safe", 0, 1)],
            [Plan.Link("a", "b"), Plan.Link("b", "a")]
        );

        model["a"]!.IsCritical.ShouldBeFalse();
        model.TryGetLatestFinish("a", out _).ShouldBeFalse();
        model.TryGetLatestFinish("safe", out _).ShouldBeTrue();
    }


    [Fact]
    public void AMilestoneParticipatesWithZeroDuration()
    {
        var model = GanttModel.Build(
            [Plan.Task("a", 0, 3), Plan.Milestone("ship", 3)],
            [Plan.Link("a", "ship")]
        );

        model["ship"]!.IsCritical.ShouldBeTrue();
        model["a"]!.IsCritical.ShouldBeTrue();
    }


    [Fact]
    public void SlackIsMeasuredInWorkingTime()
    {
        // A finishes Tuesday and the project runs to Sunday: five calendar days of gap, but only
        // the four weekdays Tue-Fri are slack a planner could actually spend.
        var calendar = GanttCalendar.StandardDays;
        var model = GanttModel.Build(
            [Plan.Task("a", 0, 1), Plan.Task("long", 0, 6)],
            null,
            new GanttModelOptions { Calendar = calendar }
        );

        model["a"]!.TotalSlack.ShouldBe(TimeSpan.FromDays(4));
    }


    [Fact]
    public void LagIsHonouredWhenComputingSlack()
    {
        var model = GanttModel.Build(
            [Plan.Task("a", 0, 2), Plan.Task("b", 5, 7)],
            [Plan.Link("a", "b", lagDays: 3)]
        );

        // A must finish 3 days before B starts, and it does exactly — no slack.
        model["a"]!.TotalSlack.ShouldBe(TimeSpan.Zero);
        model["a"]!.IsCritical.ShouldBeTrue();
    }
}
