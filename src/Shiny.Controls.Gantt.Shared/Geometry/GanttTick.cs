namespace Shiny.Controls.Gantt;

/// <summary>One cell of a timeline header tier, already positioned.</summary>
/// <param name="Start">Inclusive start of the cell.</param>
/// <param name="End">Exclusive end of the cell.</param>
/// <param name="Label">The text to draw, formatted for the requested culture.</param>
/// <param name="X">Left edge in pixels, relative to the timeline origin.</param>
/// <param name="Width">Cell width in pixels.</param>
/// <param name="IsNonWorking">True when no work happens anywhere in the cell — a weekend or holiday column.</param>
/// <param name="IsToday">True when the cell contains the current instant.</param>
public readonly record struct GanttTick(
    DateTimeOffset Start,
    DateTimeOffset End,
    string Label,
    double X,
    double Width,
    bool IsNonWorking,
    bool IsToday
);
