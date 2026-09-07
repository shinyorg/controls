using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Shiny.Controls.Diagramming;

/// <summary>
/// One shape on the surface.
/// </summary>
/// <remarks>
/// <para>
/// The hierarchy can be expressed either way round, the same as <c>GanttTask</c>: nest nodes in
/// <see cref="Children"/> and the parent link maintains itself, or hand the control a flat list where
/// each node carries a <see cref="ParentId"/> and <see cref="DiagramModel"/> reassembles the tree.
/// Mixing the two in one source is fine. Org charts arrive from a database as rows and from a view
/// model as a tree, and forcing a conversion on the consumer is how a control ends up with two
/// half-supported paths anyway.
/// </para>
/// <para>
/// The parent/child hierarchy is what the <see cref="DiagramLayoutKind.Tree"/>,
/// <see cref="DiagramLayoutKind.MindMap"/> and <see cref="DiagramLayoutKind.Radial"/> layouts arrange
/// by, and a hierarchy with no connections of its own draws one automatically. It is deliberately
/// separate from <see cref="DiagramConnection"/>, which is a free edge between any two nodes:
/// <see cref="DiagramLayoutKind.Layered"/> and <see cref="DiagramLayoutKind.ForceDirected"/> arrange
/// by those instead, so a flowchart that rejoins does not have to be forced into a tree to be drawn.
/// </para>
/// <para>
/// <see cref="X"/> and <see cref="Y"/> are an input under <see cref="DiagramLayoutKind.None"/> and
/// for any node with <see cref="IsPinned"/> set; under every other layout they are an output the
/// engine writes back, so a template binding to a node updates without the consumer plumbing
/// anything.
/// </para>
/// </remarks>
public class DiagramNode : INotifyPropertyChanged
{
    string id = Guid.NewGuid().ToString("N");
    string? parentId;
    string text = string.Empty;
    DiagramNodeShape shape = DiagramNodeShape.Rectangle;
    double x;
    double y;
    double width;
    double height;
    bool isPinned;
    bool isSelected;
    bool isExpanded = true;
    bool isVisible = true;
    bool canMove = true;
    bool canConnect = true;
    string? fill;
    string? stroke;
    double? strokeThickness;
    double? cornerRadius;
    string? textColor;
    double? fontSize;
    object? item;
    int depth;
    bool isHidden;

    /// <summary>Stable identity, used by connections and by <see cref="ParentId"/>. Defaults to a new GUID.</summary>
    public string Id
    {
        get => this.id;
        set => this.Set(ref this.id, value);
    }

    /// <summary>
    /// The parent's <see cref="Id"/> for flat sources. Kept in sync automatically when the node is
    /// added to or removed from another node's <see cref="Children"/>.
    /// </summary>
    public string? ParentId
    {
        get => this.parentId;
        set => this.Set(ref this.parentId, value);
    }

    /// <summary>The label drawn inside the shape. Ignored when a node template is supplied.</summary>
    public string Text
    {
        get => this.text;
        set => this.Set(ref this.text, value);
    }

    /// <summary>The outline the node is drawn with.</summary>
    public DiagramNodeShape Shape
    {
        get => this.shape;
        set => this.Set(ref this.shape, value);
    }

    /// <summary>Left edge. An input when the node is pinned or the layout is <see cref="DiagramLayoutKind.None"/>; otherwise written by the layout.</summary>
    public double X
    {
        get => this.x;
        set => this.Set(ref this.x, value);
    }

    /// <summary>Top edge. An input when the node is pinned or the layout is <see cref="DiagramLayoutKind.None"/>; otherwise written by the layout.</summary>
    public double Y
    {
        get => this.y;
        set => this.Set(ref this.y, value);
    }

    /// <summary>Width. Zero means "use the control's default", which is the usual case.</summary>
    public double Width
    {
        get => this.width;
        set => this.Set(ref this.width, value);
    }

    /// <summary>Height. Zero means "use the control's default", which is the usual case.</summary>
    public double Height
    {
        get => this.height;
        set => this.Set(ref this.height, value);
    }

    /// <summary>
    /// Holds the node at its current <see cref="X"/>/<see cref="Y"/> through a re-layout.
    /// </summary>
    /// <remarks>
    /// Set automatically when a user drags the node, which is the point of it: an auto-layout that
    /// snapped a hand-placed node back the next time anything changed would make dragging useless.
    /// </remarks>
    public bool IsPinned
    {
        get => this.isPinned;
        set => this.Set(ref this.isPinned, value);
    }

    /// <summary>Whether the node is part of the current selection.</summary>
    public bool IsSelected
    {
        get => this.isSelected;
        set => this.Set(ref this.isSelected, value);
    }

    /// <summary>
    /// Whether the node's descendants are shown. Collapsing hides the whole subtree and the
    /// connections into it.
    /// </summary>
    public bool IsExpanded
    {
        get => this.isExpanded;
        set => this.Set(ref this.isExpanded, value);
    }

    /// <summary>Whether the node is drawn at all. A hidden node is skipped by layout as well.</summary>
    public bool IsVisible
    {
        get => this.isVisible;
        set => this.Set(ref this.isVisible, value);
    }

    /// <summary>Whether this particular node can be dragged, when the control allows dragging at all.</summary>
    public bool CanMove
    {
        get => this.canMove;
        set => this.Set(ref this.canMove, value);
    }

    /// <summary>Whether connections can be drawn from or to this node, when the control allows editing at all.</summary>
    public bool CanConnect
    {
        get => this.canConnect;
        set => this.Set(ref this.canConnect, value);
    }

    /// <summary>Fill colour override, as a hex string. Null takes the control's shape defaults.</summary>
    public string? Fill
    {
        get => this.fill;
        set => this.Set(ref this.fill, value);
    }

    /// <summary>Outline colour override, as a hex string. Null takes the control's shape defaults.</summary>
    public string? Stroke
    {
        get => this.stroke;
        set => this.Set(ref this.stroke, value);
    }

    /// <summary>Outline width override. Null takes the control's shape defaults.</summary>
    public double? StrokeThickness
    {
        get => this.strokeThickness;
        set => this.Set(ref this.strokeThickness, value);
    }

    /// <summary>Corner radius override, for the rounded shapes. Null takes the control's shape defaults.</summary>
    public double? CornerRadius
    {
        get => this.cornerRadius;
        set => this.Set(ref this.cornerRadius, value);
    }

    /// <summary>Label colour override, as a hex string. Null takes the control's shape defaults.</summary>
    public string? TextColor
    {
        get => this.textColor;
        set => this.Set(ref this.textColor, value);
    }

    /// <summary>Label size override. Null takes the control's shape defaults.</summary>
    public double? FontSize
    {
        get => this.fontSize;
        set => this.Set(ref this.fontSize, value);
    }

    /// <summary>
    /// The consumer's own object, carried through untouched.
    /// </summary>
    /// <remarks>
    /// Nodes are <see cref="DiagramNode"/> rather than a generic <c>TItem</c> for the same reason
    /// Gantt tasks are: the engine has to write layout results back onto them, and it cannot do that
    /// through an arbitrary type. Put your model here and bind a node template to it.
    /// </remarks>
    public object? Item
    {
        get => this.item;
        set => this.Set(ref this.item, value);
    }

    /// <summary>How deep in the hierarchy the node sits. A root is zero. Written by the engine.</summary>
    public int Depth
    {
        get => this.depth;
        internal set => this.Set(ref this.depth, value);
    }

    /// <summary>
    /// True when an ancestor is collapsed, so the node is not drawn even though
    /// <see cref="IsVisible"/> is set. Written by the engine.
    /// </summary>
    public bool IsHidden
    {
        get => this.isHidden;
        internal set => this.Set(ref this.isHidden, value);
    }

    /// <summary>The parent node, when the hierarchy was given as nesting or resolved from <see cref="ParentId"/>.</summary>
    public DiagramNode? Parent { get; internal set; }

    /// <summary>Nested children. Adding to this maintains <see cref="Parent"/> and <see cref="ParentId"/>.</summary>
    public ObservableCollection<DiagramNode> Children { get; }

    /// <summary>True when the node has children, whether or not they are shown.</summary>
    public bool HasChildren => this.Children.Count > 0;

    /// <summary>The box the node occupies, in diagram space.</summary>
    public DiagramRect Bounds => new(this.X, this.Y, this.Width, this.Height);

    /// <summary>Creates a node.</summary>
    public DiagramNode()
    {
        this.Children = [];
        // Subscribed in the constructor rather than through a defaultValueCreator: a lazily created
        // default never fires the property-changed callback, so a hook wired there never runs.
        this.Children.CollectionChanged += this.OnChildrenChanged;
    }

    /// <summary>Creates a node with a label.</summary>
    /// <param name="text">The label drawn inside the shape.</param>
    public DiagramNode(string text) : this() => this.text = text;

    /// <summary>Creates a node with an id and a label.</summary>
    /// <param name="id">Stable identity.</param>
    /// <param name="text">The label drawn inside the shape.</param>
    public DiagramNode(string id, string text) : this()
    {
        this.id = id;
        this.text = text;
    }

    /// <summary>
    /// Walks up the parent chain, this node first.
    /// </summary>
    /// <remarks>
    /// Bounded at 512 steps. A hierarchy assembled from <see cref="ParentId"/> values can be cyclic -
    /// real data routinely is - and an unbounded walk inside a layout pass hangs the UI thread with
    /// no stack to read.
    /// </remarks>
    public IEnumerable<DiagramNode> AncestorsAndSelf()
    {
        var current = this;
        for (var i = 0; current is not null && i < 512; i++)
        {
            yield return current;
            current = current.Parent;
        }
    }

    /// <summary>Raised when a property changes, including the ones the engine writes back.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raises <see cref="PropertyChanged"/> for one property.</summary>
    /// <param name="propertyName">The property that changed.</param>
    protected void OnPropertyChanged(string propertyName) =>
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

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
        this.OnPropertyChanged(propertyName!);

        // Bounds is computed from these four and a template binding to it has no other way to hear
        // about a drag.
        switch (propertyName)
        {
            case nameof(this.X):
            case nameof(this.Y):
            case nameof(this.Width):
            case nameof(this.Height):
                this.OnPropertyChanged(nameof(this.Bounds));
                break;
        }
        return true;
    }

    void OnChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (DiagramNode child in e.OldItems)
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
            foreach (DiagramNode child in e.NewItems)
            {
                child.Parent = this;
                child.ParentId = this.Id;
            }
        }

        this.OnPropertyChanged(nameof(this.HasChildren));
    }

    /// <summary>The node's label and id, for logs and debugger display.</summary>
    public override string ToString() => $"{this.Text} ({this.Id})";
}
