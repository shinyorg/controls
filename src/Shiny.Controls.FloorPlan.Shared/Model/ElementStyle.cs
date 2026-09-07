namespace Shiny.Controls.FloorPlan;

/// <summary>
/// How one element is painted, as part of the saved document.
/// </summary>
/// <remarks>
/// The colours are nullable and default to null, which means "whatever the current
/// <see cref="FloorPlanTheme"/> says". That is what lets the same saved plan read correctly in a
/// light app and a dark one: a document that hard-codes <c>#CCCCCC</c> for every room is a document
/// that can only ever be viewed on white. Set a colour explicitly and it is honoured exactly - an
/// authored plan whose meeting rooms are green stays green in both schemes.
/// </remarks>
public class ElementStyle
{
    /// <summary>Fill, as <c>#RRGGBB</c> or <c>#AARRGGBB</c>. Null takes the theme's element fill.</summary>
    public string? FillColor { get; set; }

    /// <summary>Outline, as <c>#RRGGBB</c> or <c>#AARRGGBB</c>. Null takes the theme's element stroke.</summary>
    public string? StrokeColor { get; set; }

    public float StrokeWidth { get; set; } = 2;

    public float Opacity { get; set; } = 1;
}
