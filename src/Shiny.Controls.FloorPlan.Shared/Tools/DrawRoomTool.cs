using SkiaSharp;

namespace Shiny.Controls.FloorPlan;

/// <summary>Drags out a rectangular room.</summary>
public class DrawRoomTool : IFloorPlanTool
{
    bool isDrawing;
    SKPoint start;
    SKPoint current;

    public string Name => "Draw Room";

    /// <summary>Anything smaller than this on either axis is treated as a stray click, not a room.</summary>
    public float MinimumSize { get; set; } = 20f;

    /// <summary>The label given to a newly drawn room.</summary>
    public string? DefaultLabel { get; set; } = "Room";

    public void OnActivated(FloorPlanToolContext context)
    {
    }

    public void OnDeactivated() => this.isDrawing = false;

    public void OnPointerPressed(FloorPlanToolContext context, FloorPlanPointerEventArgs e)
    {
        this.isDrawing = true;
        this.start = context.SnapToGrid(e.WorldPosition);
        this.current = this.start;
    }

    public void OnPointerMoved(FloorPlanToolContext context, FloorPlanPointerEventArgs e)
    {
        if (!this.isDrawing)
            return;

        this.current = context.SnapToGrid(e.WorldPosition);
        context.Invalidate();
    }

    public void OnPointerReleased(FloorPlanToolContext context, FloorPlanPointerEventArgs e)
    {
        if (!this.isDrawing)
            return;

        this.isDrawing = false;

        var end = context.SnapToGrid(e.WorldPosition);
        var x = MathF.Min(this.start.X, end.X);
        var y = MathF.Min(this.start.Y, end.Y);
        var w = MathF.Abs(end.X - this.start.X);
        var h = MathF.Abs(end.Y - this.start.Y);

        if (w < this.MinimumSize || h < this.MinimumSize)
        {
            context.Invalidate();
            return;
        }

        context.AddAndSelect(new RoomElement
        {
            Transform = { X = x, Y = y },
            Width = w,
            Height = h,
            Label = this.DefaultLabel
        });
    }

    public void Render(SKCanvas canvas, FloorPlanToolRenderContext context)
    {
        if (!this.isDrawing)
            return;

        var rect = new SKRect(
            MathF.Min(this.start.X, this.current.X),
            MathF.Min(this.start.Y, this.current.Y),
            MathF.Max(this.start.X, this.current.X),
            MathF.Max(this.start.Y, this.current.Y)
        );
        var dash = new[] { 6f / context.Camera.Zoom, 4f / context.Camera.Zoom };

        using var fill = new SKPaint
        {
            Color = context.Theme.ElementFill.WithAlpha(120).ToSKColor(),
            Style = SKPaintStyle.Fill
        };
        using var stroke = new SKPaint
        {
            Color = context.Theme.Preview.ToSKColor(),
            StrokeWidth = 2f / context.Camera.Zoom,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            PathEffect = SKPathEffect.CreateDash(dash, 0)
        };

        canvas.DrawRect(rect, fill);
        canvas.DrawRect(rect, stroke);
    }
}
