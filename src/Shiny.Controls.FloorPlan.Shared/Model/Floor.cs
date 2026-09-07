namespace Shiny.Controls.FloorPlan;

/// <summary>One level of a <see cref="Building"/>, and the plan drawn on it.</summary>
public class Floor
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;

    /// <summary>Storey number. Ground is conventionally zero; basements are negative.</summary>
    public int Level { get; set; }

    public FloorPlanDocument Plan { get; set; } = new();
}
