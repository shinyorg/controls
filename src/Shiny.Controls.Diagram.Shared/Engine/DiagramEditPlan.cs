namespace Shiny.Controls.Diagramming;

/// <summary>What an edit did.</summary>
public enum DiagramEditKind
{
    /// <summary>One or more nodes were dragged.</summary>
    Move,

    /// <summary>A node was added.</summary>
    AddNode,

    /// <summary>A node was deleted, along with the connections that touched it.</summary>
    RemoveNode,

    /// <summary>A connection was drawn.</summary>
    AddConnection,

    /// <summary>A connection was deleted.</summary>
    RemoveConnection,

    /// <summary>A connection's end was dragged onto a different node or port.</summary>
    Reconnect
}


/// <summary>
/// One editing gesture, recorded so it can be vetoed before it happens and undone after.
/// </summary>
/// <remarks>
/// <para>
/// The pattern is <c>GanttSchedulePlan</c>'s, and for the same reason. A gesture builds a plan
/// describing everything it is about to do - which for deleting one node is that node <i>and</i> the
/// four connections that touched it - and the plan is raised for cancellation with the whole
/// consequence in view rather than after the fact. Because it recorded the old values instead of
/// trying to recompute them, <see cref="Revert"/> puts everything back including the cascade, and
/// undo is a <c>Stack&lt;DiagramEditPlan&gt;</c> and nothing else.
/// </para>
/// <para>
/// A plan holds the collections it edited, so reverting a delete can put the node back where it was
/// in the list rather than on the end - which matters, because supply order is what the layouts break
/// ties by, and an undo that reordered the source would move unrelated nodes.
/// </para>
/// </remarks>
public sealed class DiagramEditPlan
{
    readonly List<NodeMove> moves = [];
    readonly List<NodeChange> nodeChanges = [];
    readonly List<ConnectionChange> connectionChanges = [];
    readonly List<Reconnection> reconnections = [];

    readonly IList<DiagramNode>? nodeCollection;
    readonly IList<DiagramConnection>? connectionCollection;

    /// <summary>A node's position before and after a drag.</summary>
    /// <param name="Node">The node that moved.</param>
    /// <param name="OldX">Where it was.</param>
    /// <param name="OldY">Where it was.</param>
    /// <param name="OldPinned">Whether it was already pinned before the drag.</param>
    /// <param name="NewX">Where it is going.</param>
    /// <param name="NewY">Where it is going.</param>
    public readonly record struct NodeMove(
        DiagramNode Node,
        double OldX,
        double OldY,
        bool OldPinned,
        double NewX,
        double NewY
    );

    readonly record struct NodeChange(DiagramNode Node, int Index, bool Added);

    readonly record struct ConnectionChange(DiagramConnection Connection, int Index, bool Added);

    readonly record struct Reconnection(
        DiagramConnection Connection,
        string OldSourceId,
        string OldTargetId,
        DiagramPort OldSourcePort,
        DiagramPort OldTargetPort
    );

    /// <summary>What the gesture did.</summary>
    public DiagramEditKind Kind { get; }

    /// <summary>Every node the gesture moved, with where it came from.</summary>
    public IReadOnlyList<NodeMove> Moves => this.moves;

    /// <summary>Every node the gesture added or removed.</summary>
    public IReadOnlyList<DiagramNode> AffectedNodes
    {
        get
        {
            var list = new List<DiagramNode>(this.nodeChanges.Count + this.moves.Count);

            foreach (var change in this.nodeChanges)
                list.Add(change.Node);

            foreach (var move in this.moves)
                list.Add(move.Node);

            return list;
        }
    }

    /// <summary>Every connection the gesture added, removed or reconnected.</summary>
    public IReadOnlyList<DiagramConnection> AffectedConnections
    {
        get
        {
            var list = new List<DiagramConnection>(this.connectionChanges.Count + this.reconnections.Count);

            foreach (var change in this.connectionChanges)
                list.Add(change.Connection);

            foreach (var reconnection in this.reconnections)
                list.Add(reconnection.Connection);

            return list;
        }
    }

    /// <summary>True when the plan would change nothing, so there is no point raising or pushing it.</summary>
    public bool IsEmpty =>
        this.moves.Count == 0 &&
        this.nodeChanges.Count == 0 &&
        this.connectionChanges.Count == 0 &&
        this.reconnections.Count == 0;

    /// <summary>Creates a plan.</summary>
    /// <param name="kind">What the gesture is doing.</param>
    /// <param name="nodes">The collection nodes live in, so a removal can be undone into its old slot.</param>
    /// <param name="connections">The collection connections live in.</param>
    public DiagramEditPlan(
        DiagramEditKind kind,
        IList<DiagramNode>? nodes = null,
        IList<DiagramConnection>? connections = null
    )
    {
        this.Kind = kind;
        this.nodeCollection = nodes;
        this.connectionCollection = connections;
    }

    /// <summary>Records a node's position before a drag moves it.</summary>
    /// <param name="node">The node about to move.</param>
    /// <param name="newX">Where it is going.</param>
    /// <param name="newY">Where it is going.</param>
    public void RecordMove(DiagramNode node, double newX, double newY) =>
        this.moves.Add(new NodeMove(node, node.X, node.Y, node.IsPinned, newX, newY));

    /// <summary>Records a node being added.</summary>
    /// <param name="node">The node added.</param>
    /// <param name="index">Where in the collection it went.</param>
    public void RecordNodeAdded(DiagramNode node, int index) =>
        this.nodeChanges.Add(new NodeChange(node, index, true));

    /// <summary>Records a node being removed.</summary>
    /// <param name="node">The node removed.</param>
    /// <param name="index">Where in the collection it was.</param>
    public void RecordNodeRemoved(DiagramNode node, int index) =>
        this.nodeChanges.Add(new NodeChange(node, index, false));

    /// <summary>Records a connection being added.</summary>
    /// <param name="connection">The connection added.</param>
    /// <param name="index">Where in the collection it went.</param>
    public void RecordConnectionAdded(DiagramConnection connection, int index) =>
        this.connectionChanges.Add(new ConnectionChange(connection, index, true));

    /// <summary>Records a connection being removed.</summary>
    /// <param name="connection">The connection removed.</param>
    /// <param name="index">Where in the collection it was.</param>
    public void RecordConnectionRemoved(DiagramConnection connection, int index) =>
        this.connectionChanges.Add(new ConnectionChange(connection, index, false));

    /// <summary>Records a connection's ends before one of them is dragged elsewhere.</summary>
    /// <param name="connection">The connection about to be rerouted.</param>
    public void RecordReconnect(DiagramConnection connection) =>
        this.reconnections.Add(new Reconnection(
            connection,
            connection.SourceId,
            connection.TargetId,
            connection.SourcePort,
            connection.TargetPort
        ));

    /// <summary>
    /// Applies the moves the plan recorded. Adds, removals and reconnections are applied by the
    /// caller as it records them; only moves are deferred, so a drag can be vetoed before anything
    /// visibly jumps.
    /// </summary>
    public void ApplyMoves()
    {
        foreach (var move in this.moves)
        {
            move.Node.X = move.NewX;
            move.Node.Y = move.NewY;

            // Dragging a node pins it. An auto-layout that snapped it back on the next rebuild would
            // make dragging pointless, which is the whole reason IsPinned exists.
            move.Node.IsPinned = true;
        }
    }

    /// <summary>Puts everything the gesture did back the way it was.</summary>
    public void Revert()
    {
        foreach (var move in this.moves)
        {
            move.Node.X = move.OldX;
            move.Node.Y = move.OldY;
            move.Node.IsPinned = move.OldPinned;
        }

        // Reversed, so a gesture that removed two nodes puts them back in ascending index order and
        // the recorded slots still mean what they meant when they were taken.
        for (var i = this.nodeChanges.Count - 1; i >= 0; i--)
        {
            var change = this.nodeChanges[i];

            if (this.nodeCollection is null)
                continue;

            if (change.Added)
                this.nodeCollection.Remove(change.Node);
            else
                this.nodeCollection.Insert(Math.Clamp(change.Index, 0, this.nodeCollection.Count), change.Node);
        }

        for (var i = this.connectionChanges.Count - 1; i >= 0; i--)
        {
            var change = this.connectionChanges[i];

            if (this.connectionCollection is null)
                continue;

            if (change.Added)
            {
                this.connectionCollection.Remove(change.Connection);
            }
            else
            {
                this.connectionCollection.Insert(
                    Math.Clamp(change.Index, 0, this.connectionCollection.Count),
                    change.Connection
                );
            }
        }

        foreach (var reconnection in this.reconnections)
        {
            reconnection.Connection.SourceId = reconnection.OldSourceId;
            reconnection.Connection.TargetId = reconnection.OldTargetId;
            reconnection.Connection.SourcePort = reconnection.OldSourcePort;
            reconnection.Connection.TargetPort = reconnection.OldTargetPort;
        }
    }
}
