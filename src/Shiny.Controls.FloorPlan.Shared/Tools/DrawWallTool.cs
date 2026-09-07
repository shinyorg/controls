using SkiaSharp;

namespace Shiny.Controls.FloorPlan;

/// <summary>
/// Two clicks make a wall: one for each end.
/// </summary>
/// <remarks>
/// Click-click rather than click-drag, and deliberately so - drawing a run of walls means placing a
/// long series of points, and holding a drag for each one is exhausting on a mouse and impossible on
/// a phone. <see cref="Chained"/> keeps the last end as the next start, which is what makes a room
/// outline one gesture per corner.
/// </remarks>
public class DrawWallTool : IFloorPlanTool
{
    bool hasStart;
    SKPoint start;
    SKPoint current;

    public string Name => "Draw Wall";

    public float Thickness { get; set; } = 10f;

    /// <summary>When true (the default) the wall just drawn hands its end point to the next one.</summary>
    public bool Chained { get; set; } = true;

    public void OnActivated(FloorPlanToolContext context)
    {
    }

    public void OnDeactivated() => this.hasStart = false;

    public void OnPointerPressed(FloorPlanToolContext context, FloorPlanPointerEventArgs e)
    {
        var snapped = context.SnapToGrid(e.WorldPosition);

        // Right-click ends a run without dropping a zero-length wall on the plan.
        if (e.Button == FloorPlanPointerButton.Right)
        {
            this.hasStart = false;
            context.Invalidate();
            return;
        }

        if (!this.hasStart)
        {
            this.hasStart = true;
            this.start = snapped;
            this.current = snapped;
            return;
        }

        if (snapped != this.start)
        {
            context.AddAndSelect(new WallElement
            {
                Start = new PlanPoint(this.start.X, this.start.Y),
                End = new PlanPoint(snapped.X, snapped.Y),
                Thickness = this.Thickness
            });
        }

        this.hasStart = this.Chained;
        this.start = snapped;
        this.current = snapped;
        context.Invalidate();
    }

    public void OnPointerMoved(FloorPlanToolContext context, FloorPlanPointerEventArgs e)
    {
        if (!this.hasStart)
            return;

        this.current = context.SnapToGrid(e.WorldPosition);
        context.Invalidate();
    }

    public void OnPointerReleased(FloorPlanToolContext context, FloorPlanPointerEventArgs e)
    {
    }

    public void Render(SKCanvas canvas, FloorPlanToolRenderContext context)
    {
        if (!this.hasStart)
            return;

        var dash = new[] { 6f / context.Camera.Zoom, 4f / context.Camera.Zoom };

        using var paint = new SKPaint
        {
            Color = context.Theme.Preview.WithAlpha(150).ToSKColor(),
            StrokeWidth = this.Thickness,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            PathEffect = SKPathEffect.CreateDash(dash, 0)
        };
        canvas.DrawLine(this.start.X, this.start.Y, this.current.X, this.current.Y, paint);
    }
}
