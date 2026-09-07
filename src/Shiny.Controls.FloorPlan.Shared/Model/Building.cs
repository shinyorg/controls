namespace Shiny.Controls.FloorPlan;

/// <summary>
/// A stack of floors saved as one file.
/// </summary>
/// <remarks>
/// The control itself only ever draws a single <see cref="FloorPlanDocument"/>. This exists so that a
/// floor switcher has something to switch between, and so the whole building round-trips through one
/// call rather than one file per storey.
/// </remarks>
public class Building
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public List<Floor> Floors { get; set; } = new();
}
