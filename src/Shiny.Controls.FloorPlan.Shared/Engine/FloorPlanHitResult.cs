namespace Shiny.Controls.FloorPlan;

/// <summary>What is under a plan point.</summary>
/// <param name="Element">The element hit.</param>
/// <param name="HandleIndex">The resize handle index, or -1 when the element's body was hit.</param>
public record FloorPlanHitResult(FloorPlanElement Element, int HandleIndex = -1)
{
    public bool IsHandle => this.HandleIndex >= 0;
}
