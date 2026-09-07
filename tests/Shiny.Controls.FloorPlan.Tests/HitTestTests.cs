namespace Shiny.Controls.FloorPlan.Tests;

public class HitTestTests
{
    static FloorPlanHitResult? HitTest(FloorPlanEngine engine, float x, float y) =>
        engine.HitTester.HitTest(engine.Document, x, y, engine.State, engine.Camera.Zoom);


    [Fact]
    public void FindsTheElementUnderThePoint()
    {
        var (engine, _) = Plan.Engine();
        var room = Plan.Room(100, 100, 200, 150);
        engine.Document.Elements.Add(room);

        HitTest(engine, 150, 150)!.Element.ShouldBe(room);
        HitTest(engine, 50, 50).ShouldBeNull();
    }


    [Fact]
    public void PrefersTheTopmostElement()
    {
        var (engine, _) = Plan.Engine();
        var under = Plan.Room(0, 0, 300, 300);
        var over = Plan.Room(0, 0, 300, 300);
        over.ZIndex = 5;
        engine.Document.Elements.Add(over);
        engine.Document.Elements.Add(under);

        HitTest(engine, 100, 100)!.Element.ShouldBe(over);
    }


    [Fact]
    public void SkipsHiddenElements()
    {
        var (engine, _) = Plan.Engine();
        var room = Plan.Room(0, 0, 300, 300);
        room.IsVisible = false;
        engine.Document.Elements.Add(room);

        HitTest(engine, 100, 100).ShouldBeNull();
    }


    /// <summary>
    /// A diagonal wall's bounding box is mostly empty room. Testing against the box means a click
    /// several plan-metres off the wall selects it, which is the sort of wrongness nobody can point
    /// at but everybody feels.
    /// </summary>
    [Fact]
    public void AWallIsTestedAgainstItsFootprintNotItsBoundingBox()
    {
        var (engine, _) = Plan.Engine();
        engine.Document.Elements.Add(new WallElement
        {
            Start = new PlanPoint(0, 0),
            End = new PlanPoint(400, 400),
            Thickness = 10
        });

        HitTest(engine, 200, 200).ShouldNotBeNull();  // on the line
        HitTest(engine, 380, 20).ShouldBeNull();      // inside the box, far from the wall
    }


    /// <summary>
    /// Handles are drawn in screen pixels, so their grab area has to shrink in plan units as the plan
    /// is zoomed in. Passing a constant zoom - which the prototype did - makes them impossible to
    /// grab when zoomed out and easy to grab by accident when zoomed in.
    /// </summary>
    [Fact]
    public void HandleTargetsScaleWithZoom()
    {
        var (engine, _) = Plan.Engine();
        var room = Plan.Room(100, 100, 200, 150);
        engine.Document.Elements.Add(room);
        engine.State.Select(room);

        // 12 plan units from the corner: inside an 8px grab area at 0.5x zoom, outside it at 4x.
        engine.Camera.Zoom = 0.5f;
        HitTest(engine, 112, 100)!.IsHandle.ShouldBeTrue();

        engine.Camera.Zoom = 4f;
        HitTest(engine, 112, 100)!.IsHandle.ShouldBeFalse();
    }


    /// <summary>
    /// Handles are not drawn in View mode, and something invisible should not be swallowing taps.
    /// </summary>
    [Fact]
    public void HandlesAreNotLiveInViewMode()
    {
        var (engine, _) = Plan.Engine();
        var room = Plan.Room(100, 100, 200, 150);
        engine.Document.Elements.Add(room);
        engine.State.Select(room);
        engine.State.Mode = FloorPlanEditorMode.View;

        HitTest(engine, 100, 100)!.IsHandle.ShouldBeFalse();
    }


    [Fact]
    public void HitTestArea_ReturnsEverythingItOverlaps()
    {
        var (engine, _) = Plan.Engine();
        var inside = Plan.Room(50, 50, 100, 100);
        var outside = Plan.Room(500, 500, 100, 100);
        engine.Document.Elements.Add(inside);
        engine.Document.Elements.Add(outside);

        var caught = engine.HitTester
            .HitTestArea(engine.Document, new PlanRect(0, 0, 300, 300))
            .ToList();

        caught.ShouldBe([inside]);
    }
}
