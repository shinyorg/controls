namespace Shiny.Controls.FloorPlan.Tests;

public class EngineTests
{
    [Fact]
    public void SettingADocument_ClearsTheSelection()
    {
        var (engine, _) = Plan.Engine();
        var room = Plan.Room(0, 0);
        engine.AddElement(room);
        engine.State.SelectedElement.ShouldBe(room);

        engine.Document = Plan.Empty();

        engine.State.SelectedElements.ShouldBeEmpty();
    }


    [Fact]
    public void ZoomToFit_CentresTheWholePlan()
    {
        var (engine, _) = Plan.Engine();

        engine.ZoomToFit(500, 400);

        // 500/1000 and 400/800 are both 0.5, times the 0.9 margin.
        engine.Camera.Zoom.ShouldBe(0.45f, 0.0001);
        engine.Camera.OffsetX.ShouldBe((500 - 1000 * 0.45f) / 2, 0.01);
        engine.Camera.OffsetY.ShouldBe((400 - 800 * 0.45f) / 2, 0.01);
    }


    /// <summary>
    /// A viewport that has not been laid out yet reports zero. Fitting to it would set the zoom to
    /// zero and leave a plan that can never be seen again, so the call has to be a no-op.
    /// </summary>
    [Fact]
    public void ZoomToFit_IgnoresAnUnmeasuredViewport()
    {
        var (engine, _) = Plan.Engine();

        engine.ZoomToFit(0, 0);

        engine.Camera.Zoom.ShouldBe(1f);
    }


    /// <summary>
    /// Zoom buttons wired straight to OnScroll(0, 0, ...) anchor on the top-left corner, which walks
    /// the plan off the surface one click at a time.
    /// </summary>
    [Fact]
    public void ZoomIn_AnchorsOnTheMiddleOfTheViewport()
    {
        var (engine, _) = Plan.Engine();
        var before = engine.Camera.ScreenToWorld(400, 300);

        engine.ZoomIn(800, 600);

        var after = engine.Camera.ScreenToWorld(400, 300);
        engine.Camera.Zoom.ShouldBeGreaterThan(1f);
        after.X.ShouldBe(before.X, 0.01);
        after.Y.ShouldBe(before.Y, 0.01);
    }


    [Fact]
    public void BringToFront_PutsTheSelectionOnTop()
    {
        var (engine, _) = Plan.Engine();
        var back = Plan.Room(0, 0);
        var front = Plan.Room(200, 0);
        engine.AddElement(back);
        engine.AddElement(front);

        engine.State.Select(back);
        engine.BringToFront();

        back.ZIndex.ShouldBeGreaterThan(front.ZIndex);
    }


    [Fact]
    public void SendToBack_PutsTheSelectionUnderneath()
    {
        var (engine, _) = Plan.Engine();
        var back = Plan.Room(0, 0);
        var front = Plan.Room(200, 0);
        engine.AddElement(back);
        engine.AddElement(front);

        engine.State.Select(front);
        engine.SendToBack();

        front.ZIndex.ShouldBeLessThan(back.ZIndex);
    }


    [Fact]
    public void DeleteSelected_RemovesAndRaises()
    {
        var (engine, _) = Plan.Engine();
        var room = Plan.Room(0, 0);
        engine.AddElement(room);

        IReadOnlyList<FloorPlanElement>? removed = null;
        engine.ElementsRemoved += x => removed = x;

        engine.DeleteSelected();

        engine.Document.Elements.ShouldBeEmpty();
        removed.ShouldNotBeNull();
        removed!.Single().ShouldBe(room);
    }


    /// <summary>
    /// A locked element is the one thing on the plan a user is telling us not to touch. Deleting it
    /// along with a rubber-band selection is exactly the accident locking exists to prevent.
    /// </summary>
    [Fact]
    public void DeleteSelected_LeavesLockedElementsAlone()
    {
        var (engine, _) = Plan.Engine();
        var locked = Plan.Room(0, 0);
        locked.IsLocked = true;
        engine.Document.Elements.Add(locked);

        engine.State.Select(locked);
        engine.DeleteSelected();

        engine.Document.Elements.ShouldContain(locked);
    }


    [Fact]
    public void ScrollTo_CentresAnElementWithoutChangingZoom()
    {
        var (engine, _) = Plan.Engine();
        engine.Camera.Zoom = 2f;
        var room = Plan.Room(100, 100, 200, 200);
        engine.Document.Elements.Add(room);

        engine.ScrollTo(room, 800, 600);

        var centre = engine.Camera.WorldToScreen(200, 200);
        engine.Camera.Zoom.ShouldBe(2f);
        centre.X.ShouldBe(400, 0.01);
        centre.Y.ShouldBe(300, 0.01);
    }
}
