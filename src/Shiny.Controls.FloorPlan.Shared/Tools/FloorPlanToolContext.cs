using SkiaSharp;

namespace Shiny.Controls.FloorPlan;

/// <summary>Everything a tool is allowed to touch.</summary>
public class FloorPlanToolContext
{
    public required FloorPlanDocument Document { get; init; }
    public required FloorPlanCamera Camera { get; init; }
    public required FloorPlanEditorState State { get; init; }
    public required FloorPlanHitTester HitTester { get; init; }

    /// <summary>Ask the host to repaint. Cheap, and safe to call on every pointer move.</summary>
    public required Action Invalidate { get; init; }

    /// <summary>Tells the engine a tool put something on the plan, so it can raise its own event.</summary>
    public Action<FloorPlanElement>? Added { get; init; }

    /// <summary>The point snapped to the document grid, or unchanged when snapping is off.</summary>
    public SKPoint SnapToGrid(SKPoint point)
    {
        var grid = this.Document.GridSize;
        if (!this.State.SnapToGrid || grid <= 0)
            return point;

        return new SKPoint(
            MathF.Round(point.X / grid) * grid,
            MathF.Round(point.Y / grid) * grid
        );
    }

    /// <summary>Hit-tests at the camera's current zoom, so handle targets are the right size.</summary>
    public FloorPlanHitResult? HitTest(SKPoint world) =>
        this.HitTester.HitTest(this.Document, world.X, world.Y, this.State, this.Camera.Zoom);

    /// <summary>Adds an element and selects it - what every create tool does on release.</summary>
    public void AddAndSelect(FloorPlanElement element)
    {
        this.Document.Elements.Add(element);
        this.State.Select(element);
        this.Added?.Invoke(element);
        this.Invalidate();
    }
}
