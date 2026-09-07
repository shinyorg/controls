using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Shiny.Controls.Diagramming;

/// <summary>
/// A line between two nodes.
/// </summary>
/// <remarks>
/// <para>
/// A connection is a free edge, deliberately independent of <see cref="DiagramNode.Parent"/>: a
/// decision tree whose branches rejoin, a flowchart with a loop back to an earlier step, and a plain
/// org chart are all the same control, and only the first two have edges the hierarchy cannot
/// express.
/// </para>
/// <para>
/// The route is computed by the engine and written to <see cref="Points"/>. Reading it is how a host
/// draws the line, and <see cref="Points"/> is always at least two points once the model has been
/// built - a connection whose ends could not be resolved is dropped with a
/// <see cref="DiagramIssueKind.DanglingConnection"/> issue rather than left half-routed.
/// </para>
/// </remarks>
public class DiagramConnection : INotifyPropertyChanged
{
    string id = Guid.NewGuid().ToString("N");
    string sourceId = string.Empty;
    string targetId = string.Empty;
    DiagramPort sourcePort = DiagramPort.Auto;
    DiagramPort targetPort = DiagramPort.Auto;
    string? text;
    DiagramConnectionRouter? router;
    DiagramConnectionCap startCap = DiagramConnectionCap.None;
    DiagramConnectionCap endCap = DiagramConnectionCap.FilledArrow;
    DiagramConnectionStroke strokeStyle = DiagramConnectionStroke.Solid;
    string? stroke;
    double? strokeThickness;
    bool isSelected;
    bool isVisible = true;
    bool canEdit = true;
    object? item;
    bool isHidden;
    bool isReversed;

    /// <summary>Stable identity. Defaults to a new GUID.</summary>
    public string Id
    {
        get => this.id;
        set => this.Set(ref this.id, value);
    }

    /// <summary>The <see cref="DiagramNode.Id"/> the line leaves.</summary>
    public string SourceId
    {
        get => this.sourceId;
        set => this.Set(ref this.sourceId, value);
    }

    /// <summary>The <see cref="DiagramNode.Id"/> the line arrives at.</summary>
    public string TargetId
    {
        get => this.targetId;
        set => this.Set(ref this.targetId, value);
    }

    /// <summary>Which side of the source the line leaves from. <see cref="DiagramPort.Auto"/> picks the facing side.</summary>
    public DiagramPort SourcePort
    {
        get => this.sourcePort;
        set => this.Set(ref this.sourcePort, value);
    }

    /// <summary>Which side of the target the line arrives at. <see cref="DiagramPort.Auto"/> picks the facing side.</summary>
    public DiagramPort TargetPort
    {
        get => this.targetPort;
        set => this.Set(ref this.targetPort, value);
    }

    /// <summary>
    /// A label drawn on the line - "Yes" and "No" on the two branches out of a decision.
    /// </summary>
    public string? Text
    {
        get => this.text;
        set => this.Set(ref this.text, value);
    }

    /// <summary>Router override. Null takes the control's connection defaults.</summary>
    public DiagramConnectionRouter? Router
    {
        get => this.router;
        set => this.Set(ref this.router, value);
    }

    /// <summary>What is drawn where the line leaves the source. Nothing, by default.</summary>
    public DiagramConnectionCap StartCap
    {
        get => this.startCap;
        set => this.Set(ref this.startCap, value);
    }

    /// <summary>What is drawn where the line meets the target. A filled arrow, by default.</summary>
    public DiagramConnectionCap EndCap
    {
        get => this.endCap;
        set => this.Set(ref this.endCap, value);
    }

    /// <summary>Whether the line is solid, dashed or dotted.</summary>
    public DiagramConnectionStroke StrokeStyle
    {
        get => this.strokeStyle;
        set => this.Set(ref this.strokeStyle, value);
    }

    /// <summary>Line colour override, as a hex string. Null takes the control's connection defaults.</summary>
    public string? Stroke
    {
        get => this.stroke;
        set => this.Set(ref this.stroke, value);
    }

    /// <summary>Line width override. Null takes the control's connection defaults.</summary>
    public double? StrokeThickness
    {
        get => this.strokeThickness;
        set => this.Set(ref this.strokeThickness, value);
    }

    /// <summary>Whether the connection is part of the current selection.</summary>
    public bool IsSelected
    {
        get => this.isSelected;
        set => this.Set(ref this.isSelected, value);
    }

    /// <summary>Whether the connection is drawn at all.</summary>
    public bool IsVisible
    {
        get => this.isVisible;
        set => this.Set(ref this.isVisible, value);
    }

    /// <summary>Whether this particular connection can be rerouted or deleted, when the control allows editing at all.</summary>
    public bool CanEdit
    {
        get => this.canEdit;
        set => this.Set(ref this.canEdit, value);
    }

    /// <summary>The consumer's own object, carried through untouched.</summary>
    public object? Item
    {
        get => this.item;
        set => this.Set(ref this.item, value);
    }

    /// <summary>
    /// True when either end is hidden behind a collapsed ancestor, so the line is not drawn. Written
    /// by the engine.
    /// </summary>
    public bool IsHidden
    {
        get => this.isHidden;
        internal set => this.Set(ref this.isHidden, value);
    }

    /// <summary>
    /// True when the layered layout had to reverse this edge to break a cycle. Written by the engine.
    /// </summary>
    /// <remarks>
    /// The line is still drawn source-to-target with its caps the right way round; this only says the
    /// layer assignment treated it backwards, which is worth surfacing because it is why an edge can
    /// appear to run against the layout's flow.
    /// </remarks>
    public bool IsReversed
    {
        get => this.isReversed;
        internal set => this.Set(ref this.isReversed, value);
    }

    /// <summary>
    /// The polyline or bezier the line follows, in diagram space. Written by the engine on every
    /// rebuild.
    /// </summary>
    /// <remarks>
    /// For <see cref="DiagramConnectionRouter.Straight"/> and
    /// <see cref="DiagramConnectionRouter.Orthogonal"/> these are polyline vertices. For
    /// <see cref="DiagramConnectionRouter.Bezier"/> they are start, two control points and end - four
    /// points, in that order - so a host draws a cubic without recomputing anything.
    /// </remarks>
    public IReadOnlyList<DiagramPoint> Points { get; internal set; } = [];

    /// <summary>Where <see cref="Text"/> is drawn. Written by the engine.</summary>
    public DiagramPoint LabelPosition { get; internal set; }

    /// <summary>
    /// Bend points a layout wants the route to pass through, between the two anchors.
    /// </summary>
    /// <remarks>
    /// This is how the layered layout hands its dummy-node chain to the router. An edge that spans
    /// four layers was routed around the nodes in between during layout, and throwing that away and
    /// drawing a straight line would put the line through them.
    /// </remarks>
    internal IReadOnlyList<DiagramPoint>? LayoutBends { get; set; }

    /// <summary>
    /// True when the engine created this line from a parent/child link rather than the consumer
    /// declaring it.
    /// </summary>
    /// <remarks>
    /// An implicit connection is drawn but is not in the consumer's collection, so it cannot be
    /// selected, rerouted or deleted - there is nothing to delete it from. Declaring the same edge
    /// explicitly replaces it.
    /// </remarks>
    public bool IsImplicit { get; internal init; }

    /// <summary>Creates a connection.</summary>
    public DiagramConnection()
    {
    }

    /// <summary>Creates a connection between two node ids.</summary>
    /// <param name="sourceId">The node the line leaves.</param>
    /// <param name="targetId">The node the line arrives at.</param>
    public DiagramConnection(string sourceId, string targetId)
    {
        this.sourceId = sourceId;
        this.targetId = targetId;
    }

    /// <summary>Creates a labelled connection between two node ids.</summary>
    /// <param name="sourceId">The node the line leaves.</param>
    /// <param name="targetId">The node the line arrives at.</param>
    /// <param name="text">The label drawn on the line.</param>
    public DiagramConnection(string sourceId, string targetId, string text) : this(sourceId, targetId) =>
        this.text = text;

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
        return true;
    }

    /// <summary>The connection's ends and id, for logs and debugger display.</summary>
    public override string ToString() => $"{this.SourceId} -> {this.TargetId} ({this.Id})";
}
