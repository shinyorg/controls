namespace Shiny.Controls.Diagramming;

/// <summary>
/// The outline a node is drawn with.
/// </summary>
/// <remarks>
/// These are the flowchart vocabulary rather than a general shape library: each one means something
/// to a reader of a flowchart or a decision tree, which is why there is a <see cref="Diamond"/> and a
/// <see cref="Parallelogram"/> but no star. A node whose shape is not enough carries a template
/// instead.
/// </remarks>
public enum DiagramNodeShape
{
    /// <summary>A plain box - a process step, and the default.</summary>
    Rectangle,

    /// <summary>A box with rounded corners.</summary>
    RoundedRectangle,

    /// <summary>A fully rounded box - by convention the start or end of a flow.</summary>
    Stadium,

    /// <summary>A circle, sized from the larger of the two dimensions.</summary>
    Circle,

    /// <summary>An ellipse filling the node box.</summary>
    Ellipse,

    /// <summary>A decision - the shape that makes a decision tree readable.</summary>
    Diamond,

    /// <summary>A slanted box - input or output.</summary>
    Parallelogram,

    /// <summary>A six-sided box - preparation.</summary>
    Hexagon,

    /// <summary>A cylinder - a database or other store.</summary>
    Cylinder,

    /// <summary>A box with a wavy bottom edge - a document.</summary>
    Document,

    /// <summary>A triangle pointing up - a merge or a summary.</summary>
    Triangle
}


/// <summary>
/// Which side of a node a connection attaches to.
/// </summary>
public enum DiagramPort
{
    /// <summary>Pick the side facing the other end - the default, and what auto-layout wants.</summary>
    Auto,

    /// <summary>The middle of the top edge.</summary>
    Top,

    /// <summary>The middle of the right edge.</summary>
    Right,

    /// <summary>The middle of the bottom edge.</summary>
    Bottom,

    /// <summary>The middle of the left edge.</summary>
    Left
}


/// <summary>How the line between two nodes is shaped.</summary>
public enum DiagramConnectionRouter
{
    /// <summary>A straight line between the two anchor points.</summary>
    Straight,

    /// <summary>Right-angled elbows - the org-chart look, and the default for tree layouts.</summary>
    Orthogonal,

    /// <summary>A cubic bezier curve, with control points pulled along the port normals.</summary>
    Bezier
}


/// <summary>What is drawn at the end of a connection.</summary>
public enum DiagramConnectionCap
{
    /// <summary>Nothing.</summary>
    None,

    /// <summary>An open V.</summary>
    Arrow,

    /// <summary>A solid triangle - the default at the target end.</summary>
    FilledArrow,

    /// <summary>A small filled circle.</summary>
    Circle,

    /// <summary>A small filled diamond.</summary>
    Diamond
}


/// <summary>How a connection's line is stroked.</summary>
public enum DiagramConnectionStroke
{
    /// <summary>An unbroken line.</summary>
    Solid,

    /// <summary>A dashed line.</summary>
    Dashed,

    /// <summary>A dotted line.</summary>
    Dotted
}


/// <summary>Which auto-layout arranges the graph.</summary>
public enum DiagramLayoutKind
{
    /// <summary>
    /// None - every node keeps the X/Y it was given.
    /// </summary>
    /// <remarks>
    /// This is the mode a hand-authored diagram, or one loaded from JSON with saved positions, wants.
    /// It is deliberately not the default: a graph handed over with no coordinates at all would
    /// otherwise stack every node at the origin.
    /// </remarks>
    None,

    /// <summary>Tidy hierarchy - an org chart or a decision tree. The default.</summary>
    Tree,

    /// <summary>Layered directed graph (Sugiyama) - a flowchart that rejoins rather than a strict tree.</summary>
    Layered,

    /// <summary>Root in the middle, children fanned to both sides.</summary>
    MindMap,

    /// <summary>Root in the middle, descendants on rings around it.</summary>
    Radial,

    /// <summary>Physics relaxation, for a graph with no hierarchy to speak of.</summary>
    ForceDirected
}


/// <summary>Which way a hierarchy or layered layout grows.</summary>
public enum DiagramDirection
{
    /// <summary>Root at the top, children below. The default.</summary>
    TopToBottom,

    /// <summary>Root at the bottom, children above.</summary>
    BottomToTop,

    /// <summary>Root at the left, children to the right.</summary>
    LeftToRight,

    /// <summary>Root at the right, children to the left.</summary>
    RightToLeft
}


/// <summary>A variation on the tree layout's child arrangement.</summary>
public enum DiagramTreeStyle
{
    /// <summary>Children spread evenly across the axis. The default.</summary>
    Normal,

    /// <summary>
    /// Children stack along the growth axis and indent, the way a file tree does.
    /// </summary>
    /// <remarks>
    /// This is the arrangement that keeps a deep, narrow org chart on screen: a manager with twelve
    /// reports is twelve node-widths across in <see cref="Normal"/> and one node-width across here.
    /// </remarks>
    TipOver
}


/// <summary>What a validation issue is about.</summary>
public enum DiagramIssueKind
{
    /// <summary>Two nodes or two connections claim the same id; the later one is ignored.</summary>
    DuplicateId,

    /// <summary>A connection names a node that is not in the graph; the connection is dropped.</summary>
    DanglingConnection,

    /// <summary>A node's parent chain loops back on itself; the node is treated as a root.</summary>
    ParentCycle,

    /// <summary>A connection joins a node to itself.</summary>
    SelfConnection,

    /// <summary>The connection graph contains a cycle, which a layered layout has to break to proceed.</summary>
    ConnectionCycle
}
