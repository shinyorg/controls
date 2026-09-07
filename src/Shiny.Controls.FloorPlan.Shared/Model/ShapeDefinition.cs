namespace Shiny.Controls.FloorPlan;

/// <summary>
/// A reusable stencil - an SVG path plus its natural size - that <see cref="CustomElement"/> instances
/// point at by id.
/// </summary>
/// <remarks>
/// Definitions live on the document rather than in a global registry so that a plan is
/// self-contained: hand someone the JSON and every shape in it still draws, with no shape library to
/// ship alongside it.
/// </remarks>
public class ShapeDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "Custom";

    /// <summary>SVG path data, in the definition's own coordinate space.</summary>
    /// <remarks>
    /// Parsed by Skia's own path parser, which - unlike a hand-rolled one - is unforgiving in the
    /// right way: it wants explicit commands and separated numbers. Implicit linetos and
    /// run-together decimals are the two things that silently truncate artwork.
    /// </remarks>
    public string SvgPath { get; set; } = string.Empty;

    public float BaseWidth { get; set; }
    public float BaseHeight { get; set; }
    public ElementStyle DefaultStyle { get; set; } = new();
}
