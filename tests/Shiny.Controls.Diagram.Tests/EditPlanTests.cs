using System.Collections.ObjectModel;

namespace Shiny.Controls.Diagram.Tests;

public class EditPlanTests
{
    [Fact]
    public void AMoveAppliesAndRevertsIncludingThePin()
    {
        var node = Graph.Node("a");
        node.X = 10;
        node.Y = 20;

        var plan = new DiagramEditPlan(DiagramEditKind.Move);
        plan.RecordMove(node, 100, 200);

        // Nothing has moved yet: the plan exists so a handler can veto it with the whole consequence
        // in view, before anything visibly jumps.
        node.X.ShouldBe(10);

        plan.ApplyMoves();
        node.X.ShouldBe(100);
        node.Y.ShouldBe(200);

        // Dragging pins, or the next auto-layout would snap it straight back.
        node.IsPinned.ShouldBeTrue();

        plan.Revert();
        node.X.ShouldBe(10);
        node.Y.ShouldBe(20);
        node.IsPinned.ShouldBeFalse();
    }


    [Fact]
    public void DeletingANodeAndItsConnectionsRevertsAsOneGesture()
    {
        var nodes = new ObservableCollection<DiagramNode>
        {
            Graph.Node("a"), Graph.Node("b"), Graph.Node("c")
        };

        var connections = new ObservableCollection<DiagramConnection>(Graph.Edges("a>b", "b>c"));

        var plan = new DiagramEditPlan(DiagramEditKind.RemoveNode, nodes, connections);

        // Deleting b takes both connections with it - which is exactly the cascade a handler needs to
        // see before it decides whether to allow the delete.
        var doomed = connections.ToList();
        foreach (var connection in doomed)
        {
            plan.RecordConnectionRemoved(connection, connections.IndexOf(connection));
            connections.Remove(connection);
        }

        plan.RecordNodeRemoved(nodes[1], 1);
        nodes.RemoveAt(1);

        nodes.Count.ShouldBe(2);
        connections.ShouldBeEmpty();
        plan.AffectedConnections.Count.ShouldBe(2);

        plan.Revert();

        nodes.Count.ShouldBe(3);
        // Back in its old slot, not on the end: supply order is what the layouts break ties by, so an
        // undo that reordered the source would move unrelated nodes.
        nodes[1].Id.ShouldBe("b");
        connections.Count.ShouldBe(2);
        connections[0].Id.ShouldBe("a>b");
    }


    [Fact]
    public void DrawingAConnectionRevertsByRemovingIt()
    {
        var connections = new ObservableCollection<DiagramConnection>();
        var plan = new DiagramEditPlan(DiagramEditKind.AddConnection, null, connections);

        var drawn = new DiagramConnection("a", "b");
        connections.Add(drawn);
        plan.RecordConnectionAdded(drawn, 0);

        plan.Revert();
        connections.ShouldBeEmpty();
    }


    [Fact]
    public void ReconnectingRevertsBothEndsAndBothPorts()
    {
        var connection = new DiagramConnection("a", "b")
        {
            SourcePort = DiagramPort.Right,
            TargetPort = DiagramPort.Left
        };

        var plan = new DiagramEditPlan(DiagramEditKind.Reconnect);
        plan.RecordReconnect(connection);

        connection.TargetId = "c";
        connection.TargetPort = DiagramPort.Top;

        plan.Revert();

        connection.TargetId.ShouldBe("b");
        connection.TargetPort.ShouldBe(DiagramPort.Left);
        connection.SourcePort.ShouldBe(DiagramPort.Right);
    }


    [Fact]
    public void AnEmptyPlanKnowsItIsEmpty()
    {
        new DiagramEditPlan(DiagramEditKind.Move).IsEmpty.ShouldBeTrue();

        var plan = new DiagramEditPlan(DiagramEditKind.Move);
        plan.RecordMove(Graph.Node("a"), 1, 1);
        plan.IsEmpty.ShouldBeFalse();
    }


    [Fact]
    public void AStackOfPlansUndoesInOrder()
    {
        // The whole point of the plan shape: undo is a stack of them and nothing else.
        var node = Graph.Node("a");
        var undo = new Stack<DiagramEditPlan>();

        foreach (var position in new[] { 100d, 200d, 300d })
        {
            var plan = new DiagramEditPlan(DiagramEditKind.Move);
            plan.RecordMove(node, position, position);
            plan.ApplyMoves();
            undo.Push(plan);
        }

        node.X.ShouldBe(300);

        undo.Pop().Revert();
        node.X.ShouldBe(200);

        undo.Pop().Revert();
        node.X.ShouldBe(100);

        undo.Pop().Revert();
        node.X.ShouldBe(0);
        node.IsPinned.ShouldBeFalse();
    }
}


public class JsonTests
{
    [Fact]
    public void AGraphRoundTrips()
    {
        var nodes = new List<DiagramNode>
        {
            new("root", "Root") { Shape = DiagramNodeShape.Stadium, X = 10, Y = 20, Width = 120, Height = 40 },
            new("child", "Child") { ParentId = "root", Shape = DiagramNodeShape.Diamond, Fill = "#FF0000" }
        };

        var connections = new List<DiagramConnection>
        {
            new("root", "child", "Yes")
            {
                Id = "link",
                SourcePort = DiagramPort.Bottom,
                Router = DiagramConnectionRouter.Bezier,
                StrokeStyle = DiagramConnectionStroke.Dashed
            }
        };

        var (loadedNodes, loadedConnections) = DiagramJson.Load(DiagramJson.Save(nodes, connections));

        loadedNodes.Count.ShouldBe(2);
        loadedNodes[0].Text.ShouldBe("Root");
        loadedNodes[0].Shape.ShouldBe(DiagramNodeShape.Stadium);
        loadedNodes[0].X.ShouldBe(10);
        loadedNodes[1].ParentId.ShouldBe("root");
        loadedNodes[1].Fill.ShouldBe("#FF0000");

        loadedConnections.Count.ShouldBe(1);
        loadedConnections[0].Text.ShouldBe("Yes");
        loadedConnections[0].SourcePort.ShouldBe(DiagramPort.Bottom);
        loadedConnections[0].Router.ShouldBe(DiagramConnectionRouter.Bezier);
        loadedConnections[0].StrokeStyle.ShouldBe(DiagramConnectionStroke.Dashed);
    }


    [Fact]
    public void ALoadedGraphRebuildsItsHierarchyFromParentIds()
    {
        var nodes = new List<DiagramNode>
        {
            new("root", "Root"),
            new("a", "A") { ParentId = "root" },
            new("b", "B") { ParentId = "root" }
        };

        var (loaded, _) = DiagramJson.Load(DiagramJson.Save(nodes, []));

        var model = new DiagramModel();
        model.SetSource(loaded, []);
        model.Rebuild();

        model.Issues.ShouldBeEmpty();
        loaded[1].Parent.ShouldBeSameAs(loaded[0]);
        loaded[0].Y.ShouldBeLessThan(loaded[1].Y);
    }


    [Fact]
    public void AnEmptyDocumentLoadsToAnEmptyGraph()
    {
        var (nodes, connections) = DiagramJson.Load("{}");

        nodes.ShouldBeEmpty();
        connections.ShouldBeEmpty();
    }
}
