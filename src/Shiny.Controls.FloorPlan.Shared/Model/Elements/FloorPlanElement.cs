using System.Text.Json.Serialization;

namespace Shiny.Controls.FloorPlan;

/// <summary>
/// Anything that can sit on a plan.
/// </summary>
/// <remarks>
/// The <c>JsonDerivedType</c> discriminators are part of the saved format: renaming one breaks every
/// document already written. Add new element types with a new discriminator rather than reusing an
/// old one.
/// </remarks>
[JsonDerivedType(typeof(RoomElement), "room")]
[JsonDerivedType(typeof(WallElement), "wall")]
[JsonDerivedType(typeof(DoorElement), "door")]
[JsonDerivedType(typeof(CubicleElement), "cubicle")]
[JsonDerivedType(typeof(OutletElement), "outlet")]
[JsonDerivedType(typeof(FurnitureElement), "furniture")]
[JsonDerivedType(typeof(CustomElement), "custom")]
public abstract class FloorPlanElement
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>A human name. This is what a viewer reports when the element is tapped.</summary>
    public string Name { get; set; } = string.Empty;

    public PlanTransform Transform { get; set; } = new();

    public ElementStyle Style { get; set; } = new();

    /// <summary>When true the element cannot be selected, moved or resized.</summary>
    public bool IsLocked { get; set; }

    public bool IsVisible { get; set; } = true;

    /// <summary>Paint order. Higher draws later, so a higher value is on top.</summary>
    public int ZIndex { get; set; }

    /// <summary>
    /// Anything the app wants to hang off an element - a desk booking id, a room's capacity, an asset
    /// tag. Round-trips through the document, and the engine never looks at it.
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>The element's axis-aligned extents in plan coordinates.</summary>
    public abstract PlanRect GetBounds();
}
