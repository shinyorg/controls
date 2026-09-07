namespace Shiny.Controls.FloorPlan;

public enum OutletType
{
    /// <summary>A wall socket.</summary>
    Standard,

    /// <summary>A floor box.</summary>
    Floor,

    /// <summary>A network drop.</summary>
    Data
}

/// <summary>A power or data outlet, drawn as a symbol centred on its position.</summary>
/// <remarks>
/// The only element anchored on its centre rather than its top-left. An outlet is a point on the
/// plan, not a box, and placing one should put the symbol under the pointer.
/// </remarks>
public class OutletElement : FloorPlanElement
{
    public OutletType OutletType { get; set; } = OutletType.Standard;

    /// <summary>Diameter of the drawn symbol.</summary>
    public float Size { get; set; } = 16;

    public override PlanRect GetBounds() =>
        new(this.Transform.X - this.Size / 2, this.Transform.Y - this.Size / 2, this.Size, this.Size);
}
