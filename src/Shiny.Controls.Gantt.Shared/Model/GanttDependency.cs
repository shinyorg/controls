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

    public GanttDependency()
    {
    }

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

    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName!));
        return true;
    }

    public override string ToString() =>
        $"{this.PredecessorId} -{this.Type}{(this.Lag == TimeSpan.Zero ? "" : $"{this.Lag.TotalHours:+#;-#}h")}-> {this.SuccessorId}";
}
