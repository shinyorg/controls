namespace Shiny.Maui.Controls.Gantt;

/// <summary>Where a bar's label is drawn.</summary>
public enum GanttBarLabel
{
    /// <summary>No label; the task pane carries the names.</summary>
    None,

    /// <summary>Inside the bar, clipped to it. Falls back to <see cref="Right"/> when the bar is too narrow.</summary>
    Inside,

    /// <summary>To the right of the bar, in the gutter.</summary>
    Right,

    /// <summary>To the left of the bar.</summary>
    Left
}


/// <summary>How many bars can be selected at once.</summary>
public enum GanttSelectionMode
{
    None,
    Single,
    Multiple
}


/// <summary>Which part of a bar a pointer is over, and therefore what a drag would do.</summary>
public enum GanttDragTarget
{
    None,

    /// <summary>The body of the bar — a move.</summary>
    Body,

    /// <summary>The left edge grip.</summary>
    StartEdge,

    /// <summary>The right edge grip.</summary>
    EndEdge,

    /// <summary>The progress handle.</summary>
    Progress,

    /// <summary>A connector dot, which starts drawing a dependency.</summary>
    Connector
}
