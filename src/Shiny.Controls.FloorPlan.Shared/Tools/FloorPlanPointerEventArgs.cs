using SkiaSharp;

namespace Shiny.Controls.FloorPlan;

public enum FloorPlanPointerButton
{
    Left,
    Middle,
    Right
}

/// <summary>One pointer event, in both coordinate spaces.</summary>
/// <remarks>
/// Both are supplied deliberately. A drag on the plan is a world-space delta - it has to survive the
/// camera moving underneath it - while a pan is a screen-space one, because the whole point of a pan
/// is that the plan follows the finger exactly.
/// </remarks>
public class FloorPlanPointerEventArgs
{
    public SKPoint WorldPosition { get; init; }
    public SKPoint ScreenPosition { get; init; }
    public FloorPlanPointerButton Button { get; init; }
    public bool Shift { get; init; }
    public bool Ctrl { get; init; }
}
