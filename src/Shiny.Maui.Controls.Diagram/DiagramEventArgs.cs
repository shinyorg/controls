using Shiny.Controls.Diagramming;

namespace Shiny.Maui.Controls.Diagram;

/// <summary>What is currently selected on a diagram.</summary>
/// <param name="Nodes">The selected nodes.</param>
/// <param name="Connections">The selected connections.</param>
public sealed record DiagramSelection(
    IReadOnlyList<DiagramNode> Nodes,
    IReadOnlyList<DiagramConnection> Connections
)
{
    /// <summary>Nothing selected.</summary>
    public static readonly DiagramSelection Empty = new([], []);

    /// <summary>True when nothing is selected.</summary>
    public bool IsEmpty => this.Nodes.Count == 0 && this.Connections.Count == 0;
}


/// <summary>Raised when the selection changes.</summary>
/// <param name="selection">What is selected now.</param>
public sealed class DiagramSelectionEventArgs(DiagramSelection selection) : EventArgs
{
    /// <summary>What is selected now.</summary>
    public DiagramSelection Selection { get; } = selection;
}


/// <summary>Raised when a node is tapped.</summary>
/// <param name="node">The node tapped.</param>
public sealed class DiagramNodeEventArgs(DiagramNode node) : EventArgs
{
    /// <summary>The node tapped.</summary>
    public DiagramNode Node { get; } = node;
}


/// <summary>Raised when a connection is tapped.</summary>
/// <param name="connection">The connection tapped.</param>
public sealed class DiagramConnectionEventArgs(DiagramConnection connection) : EventArgs
{
    /// <summary>The connection tapped.</summary>
    public DiagramConnection Connection { get; } = connection;
}


/// <summary>
/// An edit about to be applied, and the chance to stop it.
/// </summary>
/// <remarks>
/// The plan describes the whole gesture, not the one thing that was touched - deleting a node lists
/// the connections going with it - so a handler decides with the full consequence in front of it.
/// </remarks>
/// <param name="plan">What the gesture is about to do.</param>
public sealed class DiagramEditingEventArgs(DiagramEditPlan plan) : EventArgs
{
    /// <summary>What the gesture is about to do.</summary>
    public DiagramEditPlan Plan { get; } = plan;

    /// <summary>Set to true to abandon the edit. Everything it had already applied is reverted.</summary>
    public bool Cancel { get; set; }
}


/// <summary>Raised after an edit has been applied.</summary>
/// <param name="plan">What the gesture did. Keep it to undo it later.</param>
public sealed class DiagramEditedEventArgs(DiagramEditPlan plan) : EventArgs
{
    /// <summary>What the gesture did.</summary>
    public DiagramEditPlan Plan { get; } = plan;
}


/// <summary>Raised after a rebuild, which is the only place validation issues are reported.</summary>
/// <param name="model">The graph as built.</param>
public sealed class DiagramBuiltEventArgs(DiagramModel model) : EventArgs
{
    /// <summary>The graph as built. Read <see cref="DiagramModel.Issues"/> from it.</summary>
    public DiagramModel Model { get; } = model;
}
