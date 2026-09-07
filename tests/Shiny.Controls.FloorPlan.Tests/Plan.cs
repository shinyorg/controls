namespace Shiny.Controls.FloorPlan.Tests;

/// <summary>
/// Builders for the small plans the tests drive. Everything here is deliberately unremarkable - the
/// interesting numbers belong in the test that cares about them.
/// </summary>
static class Plan
{
    public static FloorPlanDocument Empty(float gridSize = 20) => new()
    {
        Width = 1000,
        Height = 800,
        GridSize = gridSize
    };

    public static RoomElement Room(float x, float y, float w = 100, float h = 100, string? label = null) => new()
    {
        Transform = { X = x, Y = y },
        Width = w,
        Height = h,
        Label = label
    };

    public static CubicleElement Cubicle(float x, float y, string? occupant = null) => new()
    {
        Transform = { X = x, Y = y },
        Width = 80,
        Height = 80,
        Occupant = occupant
    };

    /// <summary>An engine over a fresh document, with a repaint counter attached.</summary>
    public static (FloorPlanEngine Engine, Func<int> Repaints) Engine(FloorPlanDocument? document = null)
    {
        var engine = new FloorPlanEngine { Document = document ?? Empty() };
        var count = 0;
        engine.InvalidateRequested += () => count++;
        return (engine, () => count);
    }

    /// <summary>Presses, moves and releases, the way a host's pointer pump would.</summary>
    public static void Drag(FloorPlanEngine engine, float fromX, float fromY, float toX, float toY, bool shift = false)
    {
        engine.OnPointerPressed(fromX, fromY, shift: shift);
        engine.OnPointerMoved(toX, toY, shift: shift);
        engine.OnPointerReleased(toX, toY, shift: shift);
    }

    /// <summary>A press and release at one point, with no movement between them.</summary>
    public static void Click(FloorPlanEngine engine, float x, float y, bool shift = false)
    {
        engine.OnPointerPressed(x, y, shift: shift);
        engine.OnPointerReleased(x, y, shift: shift);
    }
}
