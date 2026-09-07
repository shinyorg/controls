using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Shiny.Controls.Gantt;

/// <summary>
/// A link between two tasks, drawn as an arrow and enforced by the auto-scheduler.
/// </summary>
public class GanttDependency : INotifyPropertyChanged
{
    string predecessorId = string.Empty;
    string successorId = string.Empty;
    GanttDependencyType type = GanttDependencyType.FinishToStart;
    TimeSpan lag;
    string? color;
    bool isCritical;

    /// <summary>Creates an empty link, for a designer or a deserializer to fill in.</summary>
    public GanttDependency()
    {
    }

    /// <summary>Creates a link between two tasks.</summary>
    /// <param name="predecessorId">The <see cref="GanttTask.Id"/> the arrow leaves.</param>
    /// <param name="successorId">The <see cref="GanttTask.Id"/> the arrow points at.</param>
    /// <param name="type">Which pair of edges the link ties together.</param>
    /// <param name="lag">Delay after the link is satisfied. Negative values are lead time.</param>
    public GanttDependency(
        string predecessorId,
        string successorId,
        GanttDependencyType type = GanttDependencyType.FinishToStart,
        TimeSpan lag = default
    )
    {
        this.predecessorId = predecessorId;
        this.successorId = successorId;
        this.type = type;
        this.lag = lag;
    }

    /// <summary>The <see cref="GanttTask.Id"/> the arrow leaves.</summary>
    public string PredecessorId
    {
        get => this.predecessorId;
        set => this.Set(ref this.predecessorId, value);
    }

    /// <summary>The <see cref="GanttTask.Id"/> the arrow points at.</summary>
    public string SuccessorId
    {
        get => this.successorId;
        set => this.Set(ref this.successorId, value);
    }

    /// <summary>Which pair of edges the link ties together.</summary>
    public GanttDependencyType Type
    {
        get => this.type;
        set => this.Set(ref this.type, value);
    }

    /// <summary>
    /// Delay after the link is satisfied before the successor may proceed. Negative values are lead
    /// time and are legal — "start two days before the predecessor finishes" is an ordinary plan.
    /// Measured in working time when the control has a calendar, wall clock otherwise.
    /// </summary>
    public TimeSpan Lag
    {
        get => this.lag;
        set => this.Set(ref this.lag, value);
    }

    /// <summary>Arrow colour override, in the host's colour syntax. Null uses the control's default.</summary>
    public string? Color
    {
        get => this.color;
        set => this.Set(ref this.color, value);
    }

    /// <summary>True when both ends are critical and the link has no slack. Computed by <see cref="GanttModel"/>.</summary>
    public bool IsCritical
    {
        get => this.isCritical;
        internal set => this.Set(ref this.isCritical, value);
    }

    /// <summary>Raised when any of the link's properties change.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Assigns a backing field and raises <see cref="PropertyChanged"/> when the value actually differs.</summary>
    /// <typeparam name="T">The property's type.</typeparam>
    /// <param name="field">The backing field.</param>
    /// <param name="value">The value to assign.</param>
    /// <param name="propertyName">The property name, supplied by the compiler.</param>
    /// <returns>True when the field changed.</returns>
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName!));
        return true;
    }

    /// <summary>The link in one line, for logs and debugger display.</summary>
    public override string ToString() =>
        $"{this.PredecessorId} -{this.Type}{(this.Lag == TimeSpan.Zero ? "" : $"{this.Lag.TotalHours:+#;-#}h")}-> {this.SuccessorId}";
}
