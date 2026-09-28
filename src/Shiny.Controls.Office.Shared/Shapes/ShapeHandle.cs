namespace Shiny.Controls.Office.Shapes;

/// <summary>Which grab handle a drag started on.</summary>
/// <remarks>
/// Shared by the slide editor, which drags shapes around a slide, and the document editor, which
/// resizes inline objects in a text flow. The document side never reports <see cref="Body"/> for a
/// move — an inline object has no free position to move it to — but the eight resize handles mean
/// exactly the same thing in both, and a host that draws them for one can draw them for the other.
/// </remarks>
public enum ShapeHandle
{
    None,
    Body,
    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left,

    /// <summary>The round grip above a selected slide shape that turns it. Slides only.</summary>
    Rotate
}
