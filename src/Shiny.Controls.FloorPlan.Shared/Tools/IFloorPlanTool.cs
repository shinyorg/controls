using SkiaSharp;

namespace Shiny.Controls.FloorPlan;

/// <summary>
/// What the pointer does. One tool is active at a time, and the engine hands it every pointer event.
/// </summary>
/// <remarks>
/// Implement this to add a tool of your own - a dimension line, a stamp, a measuring tape - and set
/// it with <see cref="FloorPlanEngine.SetTool"/> or the host control's <c>ActiveTool</c>. Tools own
/// their own in-progress state and draw it through <see cref="Render"/>, above the plan but below
/// nothing: what a tool draws is a preview, and only <c>OnPointerReleased</c> should change the
/// document.
/// </remarks>
public interface IFloorPlanTool
{
    /// <summary>A short name, shown by hosts that display the active tool.</summary>
    string Name { get; }

    void OnActivated(FloorPlanToolContext context);

    void OnDeactivated();

    void OnPointerPressed(FloorPlanToolContext context, FloorPlanPointerEventArgs e);

    void OnPointerMoved(FloorPlanToolContext context, FloorPlanPointerEventArgs e);

    void OnPointerReleased(FloorPlanToolContext context, FloorPlanPointerEventArgs e);

    /// <summary>Draws the tool's preview, in plan coordinates - the camera transform is already applied.</summary>
    void Render(SKCanvas canvas, FloorPlanToolRenderContext context);
}

/// <summary>What a tool needs to draw its preview.</summary>
public class FloorPlanToolRenderContext
{
    public required FloorPlanCamera Camera { get; init; }
    public required FloorPlanTheme Theme { get; init; }

    /// <summary>
    /// Draws an element with its registered renderer, so a tool's ghost is the actual thing it is
    /// about to create rather than an approximation of it.
    /// </summary>
    /// <remarks>
    /// Supplied by the engine. A placement ghost drawn as a generic marker is the reason a user
    /// cannot tell a desk from a bookshelf until after they have dropped it.
    /// </remarks>
    public required Action<SKCanvas, FloorPlanElement> DrawElement { get; init; }
}
