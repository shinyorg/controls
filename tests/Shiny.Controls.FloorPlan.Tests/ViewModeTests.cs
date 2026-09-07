namespace Shiny.Controls.FloorPlan.Tests;

public class ViewModeTests
{
    static FloorPlanEngine Viewer(out CubicleElement cubicle)
    {
        var (engine, _) = Plan.Engine();
        cubicle = Plan.Cubicle(100, 100, "Ada");
        engine.Document.Elements.Add(cubicle);
        engine.State.Mode = FloorPlanEditorMode.View;
        return engine;
    }


    [Fact]
    public void ATapReportsTheElement()
    {
        var engine = Viewer(out var cubicle);
        FloorPlanElement? tapped = null;
        engine.ElementTapped += x => tapped = x;

        Plan.Click(engine, 140, 140);

        tapped.ShouldBe(cubicle);
    }


    /// <summary>
    /// A press in View mode is not yet either thing. It becomes a pan once it has moved far enough
    /// and a tap if it never does - without the threshold a seating chart is unusable on touch,
    /// because no finger holds perfectly still.
    /// </summary>
    [Fact]
    public void ADragPansAndDoesNotReportATap()
    {
        var engine = Viewer(out _);
        var tapped = 0;
        engine.ElementTapped += _ => tapped++;

        Plan.Drag(engine, 140, 140, 240, 140);

        tapped.ShouldBe(0);
        engine.Camera.OffsetX.ShouldBe(100);
    }


    [Fact]
    public void AWobbleUnderTheThresholdIsStillATap()
    {
        var engine = Viewer(out var cubicle);
        FloorPlanElement? tapped = null;
        engine.ElementTapped += x => tapped = x;

        Plan.Drag(engine, 140, 140, 142, 141);

        tapped.ShouldBe(cubicle);
    }


    /// <summary>Nothing is editable in View mode, so a drag over an element must not move it.</summary>
    [Fact]
    public void ElementsCannotBeMoved()
    {
        var engine = Viewer(out var cubicle);

        Plan.Drag(engine, 140, 140, 240, 240);

        cubicle.Transform.X.ShouldBe(100);
        cubicle.Transform.Y.ShouldBe(100);
    }


    [Fact]
    public void TappingEmptySpaceReportsNothing()
    {
        var engine = Viewer(out _);
        var tapped = 0;
        engine.ElementTapped += _ => tapped++;

        Plan.Click(engine, 600, 600);

        tapped.ShouldBe(0);
    }
}
