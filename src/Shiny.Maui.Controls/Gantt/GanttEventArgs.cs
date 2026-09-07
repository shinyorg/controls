using System.Windows.Input;
using Shiny.Controls.Gantt;

namespace Shiny.Maui.Controls.Gantt;

/// <summary>
/// Raised before a drag is committed, carrying the full consequence — including every task the
/// auto-scheduler would drag along — so a handler can veto it or amend it before anything moves.
/// </summary>
public class GanttTaskChangingEventArgs : EventArgs
{
    internal GanttTaskChangingEventArgs(GanttSchedulePlan plan) => this.Plan = plan;

    /// <summary>What would happen. Nothing has been applied yet.</summary>
    public GanttSchedulePlan Plan { get; }

    /// <summary>The task the user dragged.</summary>
    public GanttTask? Task => this.Plan.Task;

    /// <summary>Which gesture this was.</summary>
    public GanttChangeKind Kind => this.Plan.Kind;

    /// <summary>Set to true to abandon the change.</summary>
    public bool Cancel { get; set; }
}


/// <summary>Raised after a drag has been applied.</summary>
public class GanttTaskChangedEventArgs : EventArgs
{
    internal GanttTaskChangedEventArgs(GanttSchedulePlan plan) => this.Plan = plan;

    /// <summary>
    /// The applied plan. Keep it to implement undo — <see cref="GanttSchedulePlan.Revert"/> puts
    /// every task back, cascade included.
    /// </summary>
    public GanttSchedulePlan Plan { get; }

    public GanttTask? Task => this.Plan.Task;
    public GanttChangeKind Kind => this.Plan.Kind;
}


/// <summary>Raised when a bar or a row is tapped.</summary>
public class GanttTaskEventArgs(GanttTask task) : EventArgs
{
    public GanttTask Task { get; } = task;
}


/// <summary>Raised when a link is drawn or deleted.</summary>
public class GanttDependencyEventArgs(GanttDependency dependency) : EventArgs
{
    public GanttDependency Dependency { get; } = dependency;

    /// <summary>Set to true to refuse the link.</summary>
    public bool Cancel { get; set; }
}


/// <summary>A shaded band drawn behind the bars — a sprint, a freeze window, a release train.</summary>
public class GanttHighlightRange : BindableObject
{
    public static readonly BindableProperty StartProperty = BindableProperty.Create(
        nameof(Start), typeof(DateTimeOffset), typeof(GanttHighlightRange));

    public static readonly BindableProperty EndProperty = BindableProperty.Create(
        nameof(End), typeof(DateTimeOffset), typeof(GanttHighlightRange));

    public static readonly BindableProperty ColorProperty = BindableProperty.Create(
        nameof(Color), typeof(Color), typeof(GanttHighlightRange));

    public static readonly BindableProperty LabelProperty = BindableProperty.Create(
        nameof(Label), typeof(string), typeof(GanttHighlightRange));

    public DateTimeOffset Start
    {
        get => (DateTimeOffset)this.GetValue(StartProperty);
        set => this.SetValue(StartProperty, value);
    }

    public DateTimeOffset End
    {
        get => (DateTimeOffset)this.GetValue(EndProperty);
        set => this.SetValue(EndProperty, value);
    }

    /// <summary>Fill colour. Drawn at low opacity behind everything else.</summary>
    public Color? Color
    {
        get => (Color?)this.GetValue(ColorProperty);
        set => this.SetValue(ColorProperty, value);
    }

    /// <summary>Optional caption drawn in the header.</summary>
    public string? Label
    {
        get => (string?)this.GetValue(LabelProperty);
        set => this.SetValue(LabelProperty, value);
    }
}
