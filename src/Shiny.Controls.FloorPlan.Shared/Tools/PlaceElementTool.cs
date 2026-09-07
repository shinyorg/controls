using SkiaSharp;

namespace Shiny.Controls.FloorPlan;

/// <summary>Which kind of element <see cref="PlaceElementTool"/> drops.</summary>
public enum FloorPlanElementKind
{
    Room,
    Door,
    Cubicle,
    Outlet,
    Furniture,
    Custom
}

/// <summary>
/// Drops a new element wherever you click, showing a ghost of it under the pointer first.
/// </summary>
/// <remarks>
/// Two ways to configure it. <see cref="Kind"/> and its companions cover the built-in elements and
/// are settable from XAML or a Blazor parameter; <see cref="Factory"/> takes over completely when set,
/// which is how an app places an element type of its own or one carrying pre-filled metadata.
/// </remarks>
public class PlaceElementTool : IFloorPlanTool
{
    SKPoint previewPos;
    bool hasPreview;
    FloorPlanElement? ghost;

    public PlaceElementTool()
    {
    }

    public PlaceElementTool(Func<FloorPlanElement> factory) => this.Factory = factory;

    public PlaceElementTool(FurnitureKind furniture)
    {
        this.Kind = FloorPlanElementKind.Furniture;
        this.FurnitureKind = furniture;
    }

    public PlaceElementTool(OutletType outlet)
    {
        this.Kind = FloorPlanElementKind.Outlet;
        this.OutletType = outlet;
    }

    public string Name => this.Kind switch
    {
        FloorPlanElementKind.Furniture => $"Place {this.FurnitureKind}",
        FloorPlanElementKind.Outlet => $"Place {this.OutletType} Outlet",
        _ => $"Place {this.Kind}"
    };

    /// <summary>Builds the element to drop. Overrides every other property on this tool.</summary>
    public Func<FloorPlanElement>? Factory { get; set; }

    public FloorPlanElementKind Kind { get; set; } = FloorPlanElementKind.Cubicle;

    public FurnitureKind FurnitureKind { get; set; } = FurnitureKind.Desk;

    public OutletType OutletType { get; set; } = OutletType.Standard;

    public DoorType DoorType { get; set; } = DoorType.Single;

    /// <summary>The stencil id used when <see cref="Kind"/> is <see cref="FloorPlanElementKind.Custom"/>.</summary>
    public string? ShapeDefinitionId { get; set; }

    /// <summary>Keeps the tool active after a drop, so a row of desks is one selection and many clicks.</summary>
    public bool Repeat { get; set; } = true;

    public void OnActivated(FloorPlanToolContext context)
    {
        this.hasPreview = false;
        this.ghost = null;
    }

    public void OnDeactivated()
    {
        this.hasPreview = false;
        this.ghost = null;
    }

    public void OnPointerPressed(FloorPlanToolContext context, FloorPlanPointerEventArgs e)
    {
        var snapped = context.SnapToGrid(e.WorldPosition);
        var element = this.Create();
        element.Transform.X = snapped.X;
        element.Transform.Y = snapped.Y;

        context.AddAndSelect(element);

        if (!this.Repeat)
            context.State.ActiveTool = new SelectTool();
    }

    public void OnPointerMoved(FloorPlanToolContext context, FloorPlanPointerEventArgs e)
    {
        this.previewPos = context.SnapToGrid(e.WorldPosition);
        this.hasPreview = true;

        // Rebuilt only when the configuration changed underneath us; moving the ghost is just a
        // transform write, and allocating an element per pointer move would churn hard on a drag.
        this.ghost ??= this.Create();
        this.ghost.Transform.X = this.previewPos.X;
        this.ghost.Transform.Y = this.previewPos.Y;

        context.Invalidate();
    }

    public void OnPointerReleased(FloorPlanToolContext context, FloorPlanPointerEventArgs e)
    {
    }

    public void Render(SKCanvas canvas, FloorPlanToolRenderContext context)
    {
        if (!this.hasPreview || this.ghost is null)
            return;

        // Drawn into a translucent layer rather than by fading each paint: the renderers own their
        // own colours, and a ghost has to read as one faint object rather than as a stack of them.
        canvas.SaveLayer(new SKPaint { Color = SKColors.White.WithAlpha(110) });
        context.DrawElement(canvas, this.ghost);
        canvas.Restore();

        var bounds = this.ghost.GetBounds();
        var dash = 4f / context.Camera.Zoom;

        using var outline = new SKPaint
        {
            Color = context.Theme.Preview.ToSKColor(),
            StrokeWidth = 1f / context.Camera.Zoom,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            PathEffect = SKPathEffect.CreateDash([dash, dash], 0)
        };
        canvas.DrawRect(new SKRect(bounds.X, bounds.Y, bounds.Right, bounds.Bottom), outline);
    }

    /// <summary>Invalidates the cached ghost after changing the tool's configuration.</summary>
    public void ResetGhost() => this.ghost = null;

    FloorPlanElement Create()
    {
        if (this.Factory is { } factory)
            return factory();

        return this.Kind switch
        {
            FloorPlanElementKind.Room => new RoomElement { Label = "Room" },
            FloorPlanElementKind.Door => new DoorElement { DoorType = this.DoorType },
            FloorPlanElementKind.Outlet => new OutletElement { OutletType = this.OutletType },
            FloorPlanElementKind.Furniture => new FurnitureElement
            {
                Kind = this.FurnitureKind,
                // A chair is square; everything else keeps the type's own default footprint.
                Width = this.FurnitureKind == FurnitureKind.Chair ? 30 : 60,
                Height = this.FurnitureKind == FurnitureKind.Chair ? 30 : 30
            },
            FloorPlanElementKind.Custom => new CustomElement { ShapeDefinitionId = this.ShapeDefinitionId ?? String.Empty },
            _ => new CubicleElement()
        };
    }
}
