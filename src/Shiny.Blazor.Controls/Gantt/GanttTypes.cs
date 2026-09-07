using Microsoft.AspNetCore.Components;
using Shiny.Controls.Gantt;

namespace Shiny.Blazor.Controls.Gantt;

/// <summary>Where a bar's label is drawn.</summary>
public enum GanttBarLabel
{
    None,
    Inside,
    Right,
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
    Body,
    StartEdge,
    EndEdge,
    Progress,
    Connector
}


/// <summary>
/// A column of the task pane. Mirrors MAUI's <c>GanttColumn</c>, down to sharing its field vocabulary
/// and formatting through <see cref="GanttFields"/>.
/// </summary>
public class GanttColumnDefinition
{
    /// <summary>Header text. Falls back to <see cref="Field"/>.</summary>
    public string? Header { get; set; }

    /// <summary>A <see cref="GanttFields"/> name, or a key into <see cref="GanttTask.Fields"/>.</summary>
    public string Field { get; set; } = GanttFields.Name;

    /// <summary>Fixed width in pixels.</summary>
    public double Width { get; set; } = 140;

    /// <summary>A standard .NET format string applied to the value.</summary>
    public string? Format { get; set; }

    /// <summary>CSS <c>text-align</c> value for the cell.</summary>
    public string Align { get; set; } = "left";

    /// <summary>Full control over the cell.</summary>
    public RenderFragment<GanttTask>? CellTemplate { get; set; }

    /// <summary>
    /// Whether this column carries the indent and the expander. Exactly one column should; the
    /// component turns it on for the first when nobody claims it.
    /// </summary>
    public bool ShowHierarchy { get; set; }

    public string TextFor(GanttTask task, System.Globalization.CultureInfo culture) =>
        GanttFields.TextOf(task, this.Field, this.Format, culture);
}


/// <summary>A shaded band drawn behind the bars — a sprint, a freeze window, a release train.</summary>
public class GanttHighlightRange
{
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset End { get; set; }

    /// <summary>Any CSS colour. Drawn at low opacity.</summary>
    public string? Color { get; set; }

    public string? Label { get; set; }
}


/// <summary>
/// Raised before a drag is committed, carrying the full consequence — including every task the
/// auto-scheduler would drag along — so a handler can veto it before anything moves.
/// </summary>
public class GanttTaskChangingArgs(GanttSchedulePlan plan)
{
    public GanttSchedulePlan Plan { get; } = plan;
    public GanttTask? Task => this.Plan.Task;
    public GanttChangeKind Kind => this.Plan.Kind;

    /// <summary>Set to true to abandon the change.</summary>
    public bool Cancel { get; set; }
}


/// <summary>Raised after a drag has been applied. Keep the plan to implement undo.</summary>
public class GanttTaskChangedArgs(GanttSchedulePlan plan)
{
    public GanttSchedulePlan Plan { get; } = plan;
    public GanttTask? Task => this.Plan.Task;
    public GanttChangeKind Kind => this.Plan.Kind;
}


/// <summary>Raised when a link is drawn or deleted.</summary>
public class GanttDependencyArgs(GanttDependency dependency)
{
    public GanttDependency Dependency { get; } = dependency;

    /// <summary>Set to true to refuse the link.</summary>
    public bool Cancel { get; set; }
}
