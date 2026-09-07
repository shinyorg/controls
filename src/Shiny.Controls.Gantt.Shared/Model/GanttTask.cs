using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Shiny.Controls.Gantt;

/// <summary>
/// One row of a plan: a bar, a milestone, or a summary that rolls its children up.
/// </summary>
/// <remarks>
/// <para>
/// The hierarchy can be expressed either way round. Nest tasks in <see cref="Children"/> and the
/// parent link maintains itself; or hand the control a flat list where each task carries a
/// <see cref="ParentId"/> and <see cref="GanttModel"/> reassembles the tree. Mixing the two in one
/// source is fine — a task already nested keeps its nesting, and a flat task naming it as parent is
/// grafted on. Neither shape is "the" model, because plans arrive from a database as rows and from
/// a view model as a tree, and forcing a conversion on the consumer is how a control ends up with
/// two half-supported paths anyway.
/// </para>
/// <para>
/// Everything the engine computes — <see cref="IsCritical"/>, <see cref="TotalSlack"/>,
/// <see cref="Depth"/>, the rolled-up dates of a summary — is written back onto the task, so a
/// template binding to it updates without the consumer plumbing anything. Those properties have
/// internal setters: they are outputs, not inputs.
/// </para>
/// </remarks>
public class GanttTask : INotifyPropertyChanged
{
    string id = Guid.NewGuid().ToString("N");
    string? parentId;
    string name = string.Empty;
    DateTimeOffset start;
    DateTimeOffset end;
    double progress;
    GanttTaskKind kind = GanttTaskKind.Task;
    string? color;
    DateTimeOffset? baselineStart;
    DateTimeOffset? baselineEnd;
    DateTimeOffset? deadline;
    GanttConstraintType constraint = GanttConstraintType.None;
    DateTimeOffset? constraintDate;
    bool isExpanded = true;
    bool manuallyScheduled;
    bool canMove = true;
    bool canResize = true;
    bool canChangeProgress = true;
    bool isCritical;
    TimeSpan totalSlack;
    int depth;
    bool isVisible = true;
    object? item;
    string? resourceId;

    /// <summary>Stable identity, used by dependencies and by <see cref="ParentId"/>. Defaults to a new GUID.</summary>
    public string Id
    {
        get => this.id;
        set => this.Set(ref this.id, value);
    }

    /// <summary>
    /// The parent's <see cref="Id"/> for flat sources. Kept in sync automatically when the task is
    /// added to or removed from another task's <see cref="Children"/>.
    /// </summary>
    public string? ParentId
    {
        get => this.parentId;
        set => this.Set(ref this.parentId, value);
    }

    /// <summary>The label shown in the task pane and, space permitting, on the bar.</summary>
    public string Name
    {
        get => this.name;
        set => this.Set(ref this.name, value);
    }

    /// <summary>When the task begins. For a <see cref="GanttTaskKind.Milestone"/> this is the marker's date.</summary>
    public DateTimeOffset Start
    {
        get => this.start;
        set => this.Set(ref this.start, value);
    }

    /// <summary>
    /// When the task ends, exclusive — a one-day task on the 3rd runs from the 3rd 00:00 to the 4th
    /// 00:00. Ignored for a milestone, and recomputed on a summary from its children.
    /// </summary>
    public DateTimeOffset End
    {
        get => this.end;
        set => this.Set(ref this.end, value);
    }

    /// <summary>Completion from 0 to 1, drawn as the filled portion of the bar. Clamped on assignment.</summary>
    public double Progress
    {
        get => this.progress;
        set => this.Set(ref this.progress, double.IsNaN(value) ? 0d : Math.Clamp(value, 0d, 1d));
    }

    /// <summary>
    /// Task, milestone, summary or project. A task that gains children is promoted to
    /// <see cref="GanttTaskKind.Summary"/> by the model unless it is already a
    /// <see cref="GanttTaskKind.Project"/>.
    /// </summary>
    public GanttTaskKind Kind
    {
        get => this.kind;
        set => this.Set(ref this.kind, value);
    }

    /// <summary>
    /// Bar colour, as whatever string the host understands — a MAUI colour name or hex, or any CSS
    /// colour on Blazor. Null falls through to the control's palette, then the theme.
    /// </summary>
    public string? Color
    {
        get => this.color;
        set => this.Set(ref this.color, value);
    }

    /// <summary>The originally planned start, drawn as a thin bar under the live one when set.</summary>
    public DateTimeOffset? BaselineStart
    {
        get => this.baselineStart;
        set => this.Set(ref this.baselineStart, value);
    }

    /// <summary>The originally planned finish. Paired with <see cref="BaselineStart"/>.</summary>
    public DateTimeOffset? BaselineEnd
    {
        get => this.baselineEnd;
        set => this.Set(ref this.baselineEnd, value);
    }

    /// <summary>
    /// A date the task must not finish after, drawn as a marker. Missing it is reported as
    /// <see cref="GanttValidationCode.DeadlineExceeded"/> rather than blocked, because a slipped
    /// deadline is a fact about the plan, not an illegal edit.
    /// </summary>
    public DateTimeOffset? Deadline
    {
        get => this.deadline;
        set => this.Set(ref this.deadline, value);
    }

    /// <summary>A hard date restriction the auto-scheduler honours. See <see cref="GanttConstraintType"/>.</summary>
    public GanttConstraintType Constraint
    {
        get => this.constraint;
        set => this.Set(ref this.constraint, value);
    }

    /// <summary>The date <see cref="Constraint"/> refers to. Ignored when the constraint is <see cref="GanttConstraintType.None"/>.</summary>
    public DateTimeOffset? ConstraintDate
    {
        get => this.constraintDate;
        set => this.Set(ref this.constraintDate, value);
    }

    /// <summary>Whether a summary's children are shown. Two-way: the control writes it when the row is toggled.</summary>
    public bool IsExpanded
    {
        get => this.isExpanded;
        set => this.Set(ref this.isExpanded, value);
    }

    /// <summary>
    /// Opts the task out of automatic rescheduling: dependencies still draw and still report
    /// violations, but a predecessor moving will not drag this task along.
    /// </summary>
    public bool ManuallyScheduled
    {
        get => this.manuallyScheduled;
        set => this.Set(ref this.manuallyScheduled, value);
    }

    /// <summary>Whether the bar can be dragged along the timeline.</summary>
    public bool CanMove
    {
        get => this.canMove;
        set => this.Set(ref this.canMove, value);
    }

    /// <summary>Whether the bar's edges can be dragged.</summary>
    public bool CanResize
    {
        get => this.canResize;
        set => this.Set(ref this.canResize, value);
    }

    /// <summary>Whether the progress handle can be dragged.</summary>
    public bool CanChangeProgress
    {
        get => this.canChangeProgress;
        set => this.Set(ref this.canChangeProgress, value);
    }

    /// <summary>The consumer's own object for this row, for templates and event handlers to reach.</summary>
    public object? Item
    {
        get => this.item;
        set => this.Set(ref this.item, value);
    }

    /// <summary>
    /// The assigned resource, used for the resource label and for swimlane grouping. Free-form; the
    /// control never resolves it against a roster.
    /// </summary>
    public string? ResourceId
    {
        get => this.resourceId;
        set => this.Set(ref this.resourceId, value);
    }

    /// <summary>Additional assignees shown after the bar. Independent of <see cref="ResourceId"/>.</summary>
    public IList<string> Assignees { get; } = new List<string>();

    /// <summary>
    /// Values for custom task-pane columns, keyed by the column's <c>Field</c>. Kept as a bag rather
    /// than requiring a subclass so a plan loaded from JSON can carry columns the control never
    /// compiled against.
    /// </summary>
    public IDictionary<string, object?> Fields { get; } = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Nested children. Adding here sets the child's <see cref="ParentId"/>; removing clears it.
    /// </summary>
    public ObservableCollection<GanttTask> Children { get; }

    // ---------------------------------------------------------------------------------------------
    // Engine outputs. Internal setters: writing these from consumer code would be overwritten by the
    // next model rebuild, so the compiler says no rather than the behaviour being surprising.
    // ---------------------------------------------------------------------------------------------

    /// <summary>True when the task is on the critical path — zero total slack. Computed by <see cref="GanttModel"/>.</summary>
    public bool IsCritical
    {
        get => this.isCritical;
        internal set => this.Set(ref this.isCritical, value);
    }

    /// <summary>
    /// How far the task can slip before the project finish moves. Zero means critical. Computed by
    /// <see cref="GanttModel"/>.
    /// </summary>
    public TimeSpan TotalSlack
    {
        get => this.totalSlack;
        internal set => this.Set(ref this.totalSlack, value);
    }

    /// <summary>Indentation level; zero for a root. Computed by <see cref="GanttModel"/>.</summary>
    public int Depth
    {
        get => this.depth;
        internal set => this.Set(ref this.depth, value);
    }

    /// <summary>
    /// Whether every ancestor is expanded. Computed by <see cref="GanttModel"/>; the control uses it
    /// to decide which rows to realize.
    /// </summary>
    public bool IsVisible
    {
        get => this.isVisible;
        internal set => this.Set(ref this.isVisible, value);
    }

    /// <summary>The resolved parent, or null for a root. Computed by <see cref="GanttModel"/>.</summary>
    public GanttTask? Parent { get; internal set; }

    /// <summary>
    /// How many children the model resolved for this task. Non-zero only for a flat source, where
    /// the children name this task in <see cref="ParentId"/> but were never nested — the model
    /// refuses to reshape the consumer's own collections, so it records the count instead.
    /// </summary>
    internal int ResolvedChildCount { get; set; }

    /// <summary>
    /// True when the task has children, whatever its <see cref="Kind"/> says. Counts both nested
    /// children and, once the model has run, ones that only claimed it by <see cref="ParentId"/>.
    /// </summary>
    public bool HasChildren => this.Children.Count > 0 || this.ResolvedChildCount > 0;

    /// <summary>
    /// A summary or project — the two kinds whose dates are rolled up rather than set. A task with
    /// children is one even before the model has run.
    /// </summary>
    public bool IsRollup =>
        this.Kind is GanttTaskKind.Summary or GanttTaskKind.Project || this.HasChildren;

    /// <summary>Wall-clock span from <see cref="Start"/> to <see cref="End"/>. Zero for a milestone.</summary>
    public TimeSpan Duration =>
        this.Kind == GanttTaskKind.Milestone ? TimeSpan.Zero : this.End - this.Start;

    /// <summary>True when a <see cref="Deadline"/> is set and the task finishes after it.</summary>
    public bool IsOverdue =>
        this.Deadline is { } d && (this.Kind == GanttTaskKind.Milestone ? this.Start : this.End) > d;


    public GanttTask()
    {
        this.Children = new ObservableCollection<GanttTask>();
        this.Children.CollectionChanged += this.OnChildrenChanged;
    }


    /// <summary>Enumerates this task and everything under it, depth first, in row order.</summary>
    public IEnumerable<GanttTask> DescendantsAndSelf()
    {
        yield return this;
        foreach (var child in this.Children)
        {
            foreach (var d in child.DescendantsAndSelf())
                yield return d;
        }
    }


    /// <summary>Walks up to the root, nearest ancestor first.</summary>
    public IEnumerable<GanttTask> Ancestors()
    {
        // Bounded rather than while(true): a hierarchy cycle is reported as a validation code by the
        // model, and this must not hang before the model gets the chance to say so.
        var current = this.Parent;
        for (var i = 0; current is not null && i < 512; i++)
        {
            yield return current;
            current = current.Parent;
        }
    }


    /// <summary>Raised by the engine after a rebuild has finished writing its outputs onto this task.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged(string propertyName) =>
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        this.OnPropertyChanged(propertyName!);

        // Duration/IsRollup/IsOverdue are computed from these, and a template binding to them has no
        // other way to hear about it.
        switch (propertyName)
        {
            case nameof(this.Start):
            case nameof(this.End):
            case nameof(this.Kind):
                this.OnPropertyChanged(nameof(this.Duration));
                this.OnPropertyChanged(nameof(this.IsRollup));
                this.OnPropertyChanged(nameof(this.IsOverdue));
                break;

            case nameof(this.Deadline):
                this.OnPropertyChanged(nameof(this.IsOverdue));
                break;
        }
        return true;
    }


    void OnChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (GanttTask child in e.OldItems)
            {
                // Only detach if this is still the parent of record. A move between two parents
                // raises Add on the new one first in some collection implementations, and clearing
                // the link here would undo it.
                if (ReferenceEquals(child.Parent, this))
                {
                    child.Parent = null;
                    child.ParentId = null;
                }
            }
        }

        if (e.NewItems is not null)
        {
            foreach (GanttTask child in e.NewItems)
            {
                child.Parent = this;
                child.ParentId = this.Id;
            }
        }

        this.OnPropertyChanged(nameof(this.HasChildren));
        this.OnPropertyChanged(nameof(this.IsRollup));
    }


    public override string ToString() => $"{this.Name} ({this.Id})";
}
