namespace Shiny.Maui.Controls.Kanban;

/// <summary>
/// A coloured chip on a card - a tag, a component, a priority.
/// </summary>
/// <remarks>
/// <see cref="Color"/> is a string rather than a typed colour so this file is identical on both
/// hosts - it is mirrored by <c>Shiny.Blazor.Controls/Kanban/KanbanLabel.cs</c>. Null takes the
/// theme's secondary container.
/// </remarks>
public class KanbanLabel
{
    public KanbanLabel()
    {
    }

    public KanbanLabel(string text, string? color = null)
    {
        this.Text = text;
        this.Color = color;
    }

    /// <summary>The chip's text. An empty string renders the chip as a colour dot only.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>The chip's fill - <c>#2563eb</c>, <c>rebeccapurple</c>, anything the host parses.</summary>
    public string? Color { get; set; }
}
