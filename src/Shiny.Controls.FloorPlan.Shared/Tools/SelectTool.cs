using SkiaSharp;

namespace Shiny.Controls.FloorPlan;

/// <summary>
/// Picks, moves, resizes and rubber-band selects. The default tool.
/// </summary>
public class SelectTool : IFloorPlanTool
{
    bool isDragging;
    bool isResizing;
    bool isRubberBanding;
    int resizeHandle = -1;
    SKPoint lastDragPos;
    FloorPlanElement? dragElement;
    SKPoint rubberBandStart;
    SKPoint rubberBandEnd;

    public string Name => "Select";

    public void OnActivated(FloorPlanToolContext context)
    {
    }

    public void OnDeactivated() => this.Reset();

    public void OnPointerPressed(FloorPlanToolContext context, FloorPlanPointerEventArgs e)
    {
        if (e.Button == FloorPlanPointerButton.Middle)
            return;

        var hit = context.HitTest(e.WorldPosition);

        if (hit is { IsHandle: true })
        {
            this.isResizing = true;
            this.resizeHandle = hit.HandleIndex;
            this.dragElement = hit.Element;
            this.lastDragPos = e.WorldPosition;
            return;
        }

        if (hit is not null && !hit.Element.IsLocked)
        {
            if (e.Shift)
                context.State.ToggleSelection(hit.Element);
            else if (!context.State.IsSelected(hit.Element))
                context.State.Select(hit.Element);

            this.isDragging = true;
            this.dragElement = hit.Element;
            this.lastDragPos = e.WorldPosition;
        }
        else
        {
            if (!e.Shift)
                context.State.ClearSelection();

            this.isRubberBanding = true;
            this.rubberBandStart = e.WorldPosition;
            this.rubberBandEnd = e.WorldPosition;
        }

        context.Invalidate();
    }

    public void OnPointerMoved(FloorPlanToolContext context, FloorPlanPointerEventArgs e)
    {
        if (this.isDragging && this.dragElement is not null)
        {
            var dx = e.WorldPosition.X - this.lastDragPos.X;
            var dy = e.WorldPosition.Y - this.lastDragPos.Y;

            foreach (var el in context.State.SelectedElements)
            {
                if (el.IsLocked)
                    continue;

                el.Transform.X += dx;
                el.Transform.Y += dy;
            }

            this.lastDragPos = e.WorldPosition;
            context.Invalidate();
            return;
        }

        if (this.isResizing && this.dragElement is not null)
        {
            this.ApplyResize(e.WorldPosition);
            this.lastDragPos = e.WorldPosition;
            context.Invalidate();
            return;
        }

        if (this.isRubberBanding)
        {
            this.rubberBandEnd = e.WorldPosition;
            context.Invalidate();
            return;
        }

        // Nothing in hand: report what the pointer is over. The state raises its own event only when
        // the answer actually changes, so this is free on a mouse that has not left an element.
        var hit = context.HitTest(e.WorldPosition);
        var hovered = hit is { IsHandle: false } ? hit.Element : null;
        if (!ReferenceEquals(context.State.HoveredElement, hovered))
        {
            context.State.HoveredElement = hovered;
            context.Invalidate();
        }
    }

    public void OnPointerReleased(FloorPlanToolContext context, FloorPlanPointerEventArgs e)
    {
        if (this.isRubberBanding)
            this.SelectElementsInBand(context, e.Shift);

        // Snapping is applied once, on release, rather than on every move: snapping mid-drag makes
        // the element jump ahead of the pointer, and a nudge smaller than one grid cell does nothing
        // at all.
        if (this.isDragging && context.State.SnapToGrid)
        {
            foreach (var el in context.State.SelectedElements)
            {
                if (el.IsLocked)
                    continue;

                var snapped = context.SnapToGrid(new SKPoint(el.Transform.X, el.Transform.Y));
                el.Transform.X = snapped.X;
                el.Transform.Y = snapped.Y;
            }
        }

        this.Reset();
        context.Invalidate();
    }

    public void Render(SKCanvas canvas, FloorPlanToolRenderContext context)
    {
        if (!this.isRubberBanding)
            return;

        var rect = Normalize(this.rubberBandStart, this.rubberBandEnd);

        using var fill = new SKPaint
        {
            Color = context.Theme.SelectionFill.ToSKColor(),
            Style = SKPaintStyle.Fill
        };
        using var stroke = new SKPaint
        {
            Color = context.Theme.Selection.WithAlpha(150).ToSKColor(),
            StrokeWidth = 1f / context.Camera.Zoom,
            Style = SKPaintStyle.Stroke
        };

        canvas.DrawRect(rect, fill);
        canvas.DrawRect(rect, stroke);
    }

    /// <summary>
    /// Resizes about the grabbed handle, moving the transform for the handles that pull the top-left
    /// corner with them.
    /// </summary>
    /// <remarks>
    /// The deltas are applied to the element's *current* bounds rather than accumulated from the
    /// press, so a resize that hits the minimum size and comes back out again tracks the pointer
    /// instead of staying stuck at the floor.
    /// </remarks>
    void ApplyResize(SKPoint worldPos)
    {
        if (this.dragElement is null)
            return;

        var dx = worldPos.X - this.lastDragPos.X;
        var dy = worldPos.Y - this.lastDragPos.Y;
        var t = this.dragElement.Transform;
        var bounds = this.dragElement.GetBounds();

        switch (this.resizeHandle)
        {
            case 0: // NW
                t.X += dx;
                t.Y += dy;
                SetSize(this.dragElement, bounds.Width - dx, bounds.Height - dy);
                break;
            case 1: // N
                t.Y += dy;
                SetSize(this.dragElement, bounds.Width, bounds.Height - dy);
                break;
            case 2: // NE
                t.Y += dy;
                SetSize(this.dragElement, bounds.Width + dx, bounds.Height - dy);
                break;
            case 3: // E
                SetSize(this.dragElement, bounds.Width + dx, bounds.Height);
                break;
            case 4: // SE
                SetSize(this.dragElement, bounds.Width + dx, bounds.Height + dy);
                break;
            case 5: // S
                SetSize(this.dragElement, bounds.Width, bounds.Height + dy);
                break;
            case 6: // SW
                t.X += dx;
                SetSize(this.dragElement, bounds.Width - dx, bounds.Height + dy);
                break;
            case 7: // W
                t.X += dx;
                SetSize(this.dragElement, bounds.Width - dx, bounds.Height);
                break;
        }
    }

    /// <summary>The minimum any element can be resized to, in plan units.</summary>
    public const float MinimumSize = 10f;

    static void SetSize(FloorPlanElement element, float width, float height)
    {
        width = MathF.Max(MinimumSize, width);
        height = MathF.Max(MinimumSize, height);

        switch (element)
        {
            case RoomElement room:
                room.Width = width;
                room.Height = height;
                break;
            case CubicleElement cubicle:
                cubicle.Width = width;
                cubicle.Height = height;
                break;
            case FurnitureElement furniture:
                furniture.Width = width;
                furniture.Height = height;
                break;
            case CustomElement custom:
                custom.Width = width;
                custom.Height = height;
                break;
            case DoorElement door:
                // A door is square by construction - its swing radius is its width - so one number
                // drives both and the arc stays a quarter circle.
                door.Width = MathF.Max(width, height);
                break;
            case OutletElement outlet:
                outlet.Size = MathF.Max(width, height);
                break;
        }
    }

    void SelectElementsInBand(FloorPlanToolContext context, bool add)
    {
        var rect = Normalize(this.rubberBandStart, this.rubberBandEnd);
        var band = new PlanRect(rect.Left, rect.Top, rect.Width, rect.Height);
        var caught = context.HitTester.HitTestArea(context.Document, band);

        if (add)
            context.State.SetSelection(context.State.SelectedElements.Concat(caught).Distinct());
        else
            context.State.SetSelection(caught);
    }

    static SKRect Normalize(SKPoint a, SKPoint b) => new(
        MathF.Min(a.X, b.X),
        MathF.Min(a.Y, b.Y),
        MathF.Max(a.X, b.X),
        MathF.Max(a.Y, b.Y)
    );

    void Reset()
    {
        this.isDragging = false;
        this.isResizing = false;
        this.isRubberBanding = false;
        this.resizeHandle = -1;
        this.dragElement = null;
    }
}
