namespace Shiny.Controls.Diagram.Tests;

public class RoutingTests
{
    static (DiagramConnection Connection, DiagramNode Source, DiagramNode Target) Pair(
        DiagramConnectionRouter router,
        double targetX = 300,
        double targetY = 0
    )
    {
        var source = new DiagramNode("a", "a") { X = 0, Y = 0, Width = 100, Height = 60 };
        var target = new DiagramNode("b", "b") { X = targetX, Y = targetY, Width = 100, Height = 60 };
        var connection = new DiagramConnection("a", "b");

        ConnectionRouter.Route(connection, source, target, router);
        return (connection, source, target);
    }


    [Fact]
    public void AStraightRouteIsTwoPointsOnTheTwoOutlines()
    {
        var (connection, source, target) = Pair(DiagramConnectionRouter.Straight);

        connection.Points.Count.ShouldBe(2);
        connection.Points[0].X.ShouldBe(source.Bounds.Right, 0.01);
        connection.Points[1].X.ShouldBe(target.Bounds.X, 0.01);
    }


    [Fact]
    public void ABezierIsStartTwoControlsAndEnd()
    {
        var (connection, _, _) = Pair(DiagramConnectionRouter.Bezier);

        // Four points, not a tessellation - so MAUI draws a real curve and Blazor emits one C command.
        connection.Points.Count.ShouldBe(4);
    }


    [Fact]
    public void ABezierPullsItsControlPointsOutOfThePortNormals()
    {
        var (connection, source, target) = Pair(DiagramConnectionRouter.Bezier);

        // Leaving the right edge, the first control point is further right than the anchor.
        connection.Points[1].X.ShouldBeGreaterThan(source.Bounds.Right);

        // Entering the left edge, the second is further left than the anchor.
        connection.Points[2].X.ShouldBeLessThan(target.Bounds.X);
    }


    [Fact]
    public void EveryOrthogonalSegmentIsAxisAligned()
    {
        var (connection, _, _) = Pair(DiagramConnectionRouter.Orthogonal, targetX: 300, targetY: 200);

        connection.Points.Count.ShouldBeGreaterThan(2);

        for (var i = 1; i < connection.Points.Count; i++)
        {
            var a = connection.Points[i - 1];
            var b = connection.Points[i];

            var horizontal = Math.Abs(a.Y - b.Y) < 0.01;
            var vertical = Math.Abs(a.X - b.X) < 0.01;

            (horizontal || vertical).ShouldBeTrue($"segment {i} runs diagonally from {a} to {b}");
        }
    }


    [Fact]
    public void AnOrthogonalRouteHasNoRepeatedPoints()
    {
        // A zero-length segment draws nothing but breaks cap direction and arc-length label placement.
        var (connection, _, _) = Pair(DiagramConnectionRouter.Orthogonal, targetX: 300, targetY: 0);

        for (var i = 1; i < connection.Points.Count; i++)
            connection.Points[i].DistanceTo(connection.Points[i - 1]).ShouldBeGreaterThan(1e-6);
    }


    [Fact]
    public void ASelfConnectionLoopsOffTheNodeRatherThanCollapsing()
    {
        var node = new DiagramNode("a", "a") { X = 0, Y = 0, Width = 100, Height = 60 };
        var connection = new DiagramConnection("a", "a");

        ConnectionRouter.Route(connection, node, node, DiagramConnectionRouter.Orthogonal);

        connection.Points.Count.ShouldBe(4);
        connection.Points.ShouldAllBe(p => p.X >= 100);
    }


    [Fact]
    public void TheLabelSitsAtTheArcLengthMidpointNotTheMiddleIndex()
    {
        // A route with one short stub and one long run. By index the label lands on the join; by arc
        // length it lands halfway along the line, which is where a reader looks for it.
        var points = new List<DiagramPoint>
        {
            new(0, 0),
            new(10, 0),
            new(210, 0)
        };

        var label = ConnectionRouter.LabelPosition(points, DiagramConnectionRouter.Orthogonal);

        label.X.ShouldBe(105, 0.01);
        label.Y.ShouldBe(0, 0.01);
    }


    [Fact]
    public void ACapPointsAlongTheLineAwayFromItsNode()
    {
        var (connection, _, _) = Pair(DiagramConnectionRouter.Straight);

        var end = ConnectionRouter.CapDirection(connection.Points, DiagramConnectionRouter.Straight, atEnd: true);
        end.X.ShouldBe(1, 0.01);

        var start = ConnectionRouter.CapDirection(connection.Points, DiagramConnectionRouter.Straight, atEnd: false);
        start.X.ShouldBe(-1, 0.01);
    }


    [Fact]
    public void ABeziersCapFollowsItsControlPointNotTheFarEnd()
    {
        var source = new DiagramNode("a", "a") { X = 0, Y = 0, Width = 100, Height = 60 };
        var target = new DiagramNode("b", "b") { X = 300, Y = 0, Width = 100, Height = 60 };
        var connection = new DiagramConnection("a", "b")
        {
            TargetPort = DiagramPort.Top
        };

        ConnectionRouter.Route(connection, source, target, DiagramConnectionRouter.Bezier);

        // Entering the top port, the curve arrives heading downward - which is what the arrowhead has
        // to point along. The straight line between the two nodes is horizontal, so a naive tangent
        // would draw the arrow sideways into the node's roof.
        var direction = ConnectionRouter.CapDirection(connection.Points, DiagramConnectionRouter.Bezier, atEnd: true);
        direction.Y.ShouldBeGreaterThan(0.5);
    }


    [Fact]
    public void ANamedPortOverridesTheFacingSide()
    {
        var source = new DiagramNode("a", "a") { X = 0, Y = 0, Width = 100, Height = 60 };
        var target = new DiagramNode("b", "b") { X = 300, Y = 0, Width = 100, Height = 60 };
        var connection = new DiagramConnection("a", "b") { SourcePort = DiagramPort.Left };

        ConnectionRouter.Route(connection, source, target, DiagramConnectionRouter.Straight);

        // Left, even though the target is to the right.
        connection.Points[0].X.ShouldBe(0, 0.01);
    }


    [Fact]
    public void HitTestingFindsAConnectionNearItsLineAndNotFarFromIt()
    {
        var (connection, _, _) = Pair(DiagramConnectionRouter.Straight);
        var list = new List<DiagramConnection> { connection };

        DiagramHitTester.ConnectionAt(list, new DiagramPoint(200, 30)).ShouldBe(connection);
        DiagramHitTester.ConnectionAt(list, new DiagramPoint(200, 120)).ShouldBeNull();
    }
}

public class RoutingAxisTests
{
    [Fact]
    public void ATreeLayoutSendsArrowsIntoTheTopsOfItsBoxes()
    {
        // The org-chart look. Left to itself the router picks the geometrically nearest edge, which
        // for a child down and well to the left is the parent's *side* - so the arrow leaves
        // sideways and arrives in the child's right-hand edge pointing backwards.
        var root = Graph.Parent("root", Graph.Node("a"), Graph.Node("b"), Graph.Node("c"));
        var model = Graph.Model(Graph.Flatten(root));

        model.VisibleConnections.Count.ShouldBe(3);

        foreach (var connection in model.VisibleConnections)
        {
            var start = connection.Points[0];
            var end = connection.Points[^1];

            // Leaves the parent's bottom edge and enters the child's top edge.
            start.Y.ShouldBe(root.Bounds.Bottom, 0.5);

            var child = model.VisibleNodes.Single(n => n.Id == connection.TargetId);
            end.Y.ShouldBe(child.Bounds.Y, 0.5);
        }
    }


    [Fact]
    public void ALeftToRightTreeUsesTheSideEdgesInstead()
    {
        var root = Graph.Parent("root", Graph.Node("a"));

        var model = Graph.Model(
            Graph.Flatten(root),
            configure: options => options.Direction = DiagramDirection.LeftToRight
        );

        var connection = model.VisibleConnections.Single();
        connection.Points[0].X.ShouldBe(root.Bounds.Right, 0.5);
        connection.Points[^1].X.ShouldBe(model.VisibleNodes[1].Bounds.X, 0.5);
    }


    [Fact]
    public void AMindMapLeavesTheRootOnWhicheverSideTheBranchIsOn()
    {
        var root = Graph.Parent("root", Graph.Node("a"), Graph.Node("b"));
        var model = Graph.Model(Graph.Flatten(root), layout: DiagramLayoutKind.MindMap);

        foreach (var connection in model.VisibleConnections)
        {
            var child = model.VisibleNodes.Single(n => n.Id == connection.TargetId);
            var start = connection.Points[0];

            // Right-hand branches leave the root's right edge, left-hand ones its left.
            var expected = child.Bounds.CenterX > root.Bounds.CenterX ? root.Bounds.Right : root.Bounds.X;
            start.X.ShouldBe(expected, 0.5);
        }
    }


    [Fact]
    public void ARadialLayoutKeepsTheNearestEdgeBecauseItHasNoAxis()
    {
        // Snapping to one of four sides on a wheel would send every spoke out sideways regardless of
        // where its ring neighbour actually is.
        var root = Graph.Parent("root", Graph.Node("a"), Graph.Node("b"), Graph.Node("c"), Graph.Node("d"));
        var model = Graph.Model(Graph.Flatten(root), layout: DiagramLayoutKind.Radial);

        var starts = model.VisibleConnections.Select(c => c.Points[0]).ToList();

        // Four spokes leaving in four different directions means four distinct exit points.
        starts.Select(p => (Math.Round(p.X, 1), Math.Round(p.Y, 1))).Distinct().Count().ShouldBe(4);
    }
}
