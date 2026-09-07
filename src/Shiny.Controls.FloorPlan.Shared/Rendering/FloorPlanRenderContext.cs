namespace Shiny.Controls.FloorPlan;

/// <summary>Everything a renderer needs beyond the element itself.</summary>
public class FloorPlanRenderContext
{
    public required FloorPlanCamera Camera { get; init; }
    public required FloorPlanEditorState State { get; init; }
    public required FloorPlanTheme Theme { get; init; }
    public float GridSize { get; init; }
    public bool IsSelected { get; init; }
    public bool IsHovered { get; init; }

    /// <summary>
    /// The element's own fill, or the theme's when the document did not specify one.
    /// </summary>
    /// <remarks>
    /// Hover and selection are folded in here rather than in each renderer, which is the only way
    /// seven renderers end up agreeing on what "hovered" looks like.
    /// </remarks>
    public PlanColor FillFor(FloorPlanElement element)
        => this.Emphasize(PlanColor.ParseOr(element.Style.FillColor, this.Theme.ElementFill), element);

    /// <summary>The element's own outline colour, or the theme's - selected always wins.</summary>
    public PlanColor StrokeFor(FloorPlanElement element)
    {
        if (this.IsSelected)
            return this.Theme.Selection;

        var stroke = PlanColor.ParseOr(element.Style.StrokeColor, this.Theme.ElementStroke);
        return this.IsHovered ? stroke.Fade(this.Theme.HoverFade) : stroke;
    }

    /// <summary>A material colour - furniture, a cubicle desk - with hover and selection applied.</summary>
    public PlanColor Emphasize(PlanColor color, FloorPlanElement element)
    {
        var alpha = (byte)Math.Clamp(color.A * element.Style.Opacity, 0, 255);
        var based = color.WithAlpha(alpha);

        return this.IsHovered && !this.IsSelected ? based.Fade(this.Theme.HoverFade) : based;
    }
}
