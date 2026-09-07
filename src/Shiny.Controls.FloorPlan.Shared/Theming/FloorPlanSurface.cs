namespace Shiny.Controls.FloorPlan;

/// <summary>
/// The neutral part of an app's theme, so a drawn plan sits on the same ground as the composed chrome
/// around it.
/// </summary>
/// <remarks>
/// Both hosts read the same seven tokens and both end here, which is what keeps a plan in a MAUI app
/// and the same plan in a Blazor app from being themed differently. Only neutrals are taken - see
/// <see cref="FloorPlanTheme"/> for why the selection and the furniture are left alone.
/// </remarks>
public readonly record struct FloorPlanSurface(
    PlanColor Surface,
    PlanColor OnSurface,
    PlanColor SurfaceContainer,
    PlanColor SurfaceContainerLow,
    PlanColor OnSurfaceVariant,
    PlanColor Outline,
    PlanColor OutlineVariant)
{
    /// <summary>Restates a theme's neutrals in the app's own.</summary>
    public FloorPlanTheme Apply(FloorPlanTheme baseline)
        => baseline with
        {
            Background = this.Surface,
            GridLine = this.OutlineVariant,
            PlanBorder = this.Outline,

            ElementFill = this.SurfaceContainer,
            ElementStroke = this.Outline,
            LabelText = this.OnSurface,

            // Both are "the ground showing through" rather than colours of their own: the door gap is
            // a hole in a wall and the handle's inside is a disc lying on the plan.
            DoorOpening = this.Surface,
            HandleFill = this.Surface,

            OutletFill = this.SurfaceContainerLow,
            OutletSymbol = this.OnSurfaceVariant
        };
}
