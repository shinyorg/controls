namespace Shiny.Controls.FloorPlan.Tests;

public class ToolTests
{
    [Fact]
    public void DrawRoom_AddsAndSelectsTheRoom()
    {
        // No grid, so this test is about the room and not about where snapping put it.
        var (engine, _) = Plan.Engine(Plan.Empty(gridSize: 0));
        engine.SetTool(new DrawRoomTool());

        Plan.Drag(engine, 100, 100, 300, 250);

        var room = engine.Document.Elements.OfType<RoomElement>().Single();
        room.Transform.X.ShouldBe(100);
        room.Width.ShouldBe(200);
        room.Height.ShouldBe(150);
        engine.State.SelectedElement.ShouldBe(room);
    }


    /// <summary>
    /// A click that never moved is a stray click, not a zero-sized room. Without the floor every
    /// mis-click litters the plan with rooms too small to see or select again.
    /// </summary>
    [Fact]
    public void DrawRoom_IgnoresADragTooSmallToBeARoom()
    {
        var (engine, _) = Plan.Engine();
        engine.SetTool(new DrawRoomTool());

        Plan.Drag(engine, 100, 100, 105, 105);

        engine.Document.Elements.ShouldBeEmpty();
    }


    [Fact]
    public void DrawRoom_SnapsToTheGrid()
    {
        var (engine, _) = Plan.Engine(Plan.Empty(gridSize: 50));
        engine.SetTool(new DrawRoomTool());

        Plan.Drag(engine, 107, 92, 313, 268);

        var room = engine.Document.Elements.OfType<RoomElement>().Single();
        room.Transform.X.ShouldBe(100);
        room.Transform.Y.ShouldBe(100);
        room.Width.ShouldBe(200);
        room.Height.ShouldBe(150);
    }


    /// <summary>
    /// Click-click, not click-drag: a run of walls is a long series of points, and holding a drag for
    /// each one is exhausting on a mouse and impossible on a phone.
    /// </summary>
    [Fact]
    public void DrawWall_TakesTwoClicks()
    {
        var (engine, _) = Plan.Engine();
        engine.SetTool(new DrawWallTool());

        Plan.Click(engine, 100, 100);
        engine.Document.Elements.ShouldBeEmpty();

        Plan.Click(engine, 300, 100);

        var wall = engine.Document.Elements.OfType<WallElement>().Single();
        wall.Start.ShouldBe(new PlanPoint(100, 100));
        wall.End.ShouldBe(new PlanPoint(300, 100));
    }


    [Fact]
    public void DrawWall_ChainsTheNextSegmentFromTheLastPoint()
    {
        var (engine, _) = Plan.Engine();
        engine.SetTool(new DrawWallTool());

        Plan.Click(engine, 100, 100);
        Plan.Click(engine, 300, 100);
        Plan.Click(engine, 300, 300);

        var walls = engine.Document.Elements.OfType<WallElement>().ToList();
        walls.Count.ShouldBe(2);
        walls[1].Start.ShouldBe(new PlanPoint(300, 100));
        walls[1].End.ShouldBe(new PlanPoint(300, 300));
    }


    [Fact]
    public void DrawWall_DoesNotDropAZeroLengthWall()
    {
        var (engine, _) = Plan.Engine();
        engine.SetTool(new DrawWallTool());

        Plan.Click(engine, 100, 100);
        Plan.Click(engine, 100, 100);

        engine.Document.Elements.ShouldBeEmpty();
    }


    [Fact]
    public void PlaceElement_DropsTheConfiguredKind()
    {
        var (engine, _) = Plan.Engine();
        engine.SetTool(new PlaceElementTool(FurnitureKind.Chair));

        Plan.Click(engine, 220, 140);

        var chair = engine.Document.Elements.OfType<FurnitureElement>().Single();
        chair.Kind.ShouldBe(FurnitureKind.Chair);
        chair.Transform.X.ShouldBe(220);
        chair.Transform.Y.ShouldBe(140);
    }


    [Fact]
    public void PlaceElement_PrefersAnExplicitFactory()
    {
        var (engine, _) = Plan.Engine();
        engine.SetTool(new PlaceElementTool(() => new CubicleElement { Occupant = "Ada" })
        {
            Kind = FloorPlanElementKind.Furniture
        });

        Plan.Click(engine, 100, 100);

        engine.Document.Elements.OfType<CubicleElement>().Single().Occupant.ShouldBe("Ada");
    }


    [Fact]
    public void PlaceElement_StopsAfterOneDropWhenRepeatIsOff()
    {
        var (engine, _) = Plan.Engine();
        engine.SetTool(new PlaceElementTool(FurnitureKind.Desk) { Repeat = false });

        Plan.Click(engine, 100, 100);

        engine.State.ActiveTool.ShouldBeOfType<SelectTool>();
    }


    [Fact]
    public void Select_DragsTheWholeSelection()
    {
        var (engine, _) = Plan.Engine();
        var a = Plan.Room(100, 100);
        var b = Plan.Room(300, 100);
        engine.Document.Elements.Add(a);
        engine.Document.Elements.Add(b);
        engine.State.SetSelection([a, b]);

        Plan.Drag(engine, 150, 150, 190, 190);

        a.Transform.X.ShouldBe(140);
        b.Transform.X.ShouldBe(340);
    }


    /// <summary>
    /// Snapping on release rather than on every move: snapping mid-drag makes the element jump ahead
    /// of the pointer, and a nudge smaller than one cell does nothing at all.
    /// </summary>
    [Fact]
    public void Select_SnapsOnRelease()
    {
        var (engine, _) = Plan.Engine(Plan.Empty(gridSize: 50));
        var room = Plan.Room(100, 100);
        engine.Document.Elements.Add(room);
        engine.State.Select(room);

        Plan.Drag(engine, 150, 150, 213, 150);

        room.Transform.X.ShouldBe(150);
    }


    [Fact]
    public void Select_RubberBandsOverEverythingItCovers()
    {
        var (engine, _) = Plan.Engine();
        var inside = Plan.Room(100, 100);
        var outside = Plan.Room(600, 600);
        engine.Document.Elements.Add(inside);
        engine.Document.Elements.Add(outside);

        Plan.Drag(engine, 50, 50, 400, 400);

        engine.State.SelectedElements.ShouldBe([inside]);
    }


    [Fact]
    public void Select_ResizesFromTheSouthEastHandle()
    {
        var (engine, _) = Plan.Engine(Plan.Empty(gridSize: 0));
        var room = Plan.Room(100, 100, 200, 200);
        engine.Document.Elements.Add(room);
        engine.State.Select(room);

        // Grab the SE corner and pull it out by 50 on each axis.
        Plan.Drag(engine, 300, 300, 350, 350);

        room.Width.ShouldBe(250);
        room.Height.ShouldBe(250);
        room.Transform.X.ShouldBe(100);
    }


    [Fact]
    public void Select_ResizingFromTheNorthWestMovesTheOrigin()
    {
        var (engine, _) = Plan.Engine(Plan.Empty(gridSize: 0));
        var room = Plan.Room(100, 100, 200, 200);
        engine.Document.Elements.Add(room);
        engine.State.Select(room);

        Plan.Drag(engine, 100, 100, 150, 150);

        room.Transform.X.ShouldBe(150);
        room.Transform.Y.ShouldBe(150);
        room.Width.ShouldBe(150);
        room.Height.ShouldBe(150);
    }


    [Fact]
    public void Select_WillNotResizeBelowTheMinimum()
    {
        var (engine, _) = Plan.Engine(Plan.Empty(gridSize: 0));
        var room = Plan.Room(100, 100, 40, 40);
        engine.Document.Elements.Add(room);
        engine.State.Select(room);

        Plan.Drag(engine, 140, 140, 20, 20);

        room.Width.ShouldBe(SelectTool.MinimumSize);
        room.Height.ShouldBe(SelectTool.MinimumSize);
    }


    [Fact]
    public void Select_WillNotDragALockedElement()
    {
        var (engine, _) = Plan.Engine();
        var locked = Plan.Room(100, 100);
        locked.IsLocked = true;
        engine.Document.Elements.Add(locked);

        Plan.Drag(engine, 150, 150, 250, 250);

        locked.Transform.X.ShouldBe(100);
        engine.State.SelectedElements.ShouldBeEmpty();
    }


    [Fact]
    public void Pan_MovesTheCameraInScreenSpace()
    {
        var (engine, _) = Plan.Engine();
        engine.SetTool(new PanTool());

        Plan.Drag(engine, 100, 100, 160, 130);

        engine.Camera.OffsetX.ShouldBe(60);
        engine.Camera.OffsetY.ShouldBe(30);
    }


    /// <summary>The middle button pans from any tool, the way every drawing application behaves.</summary>
    [Fact]
    public void MiddleButton_PansFromAnyTool()
    {
        var (engine, _) = Plan.Engine();
        engine.SetTool(new DrawRoomTool());

        engine.OnPointerPressed(100, 100, FloorPlanPointerButton.Middle);
        engine.OnPointerMoved(150, 100);
        engine.OnPointerReleased(150, 100, FloorPlanPointerButton.Middle);

        engine.Camera.OffsetX.ShouldBe(50);
        engine.Document.Elements.ShouldBeEmpty();
    }
}
