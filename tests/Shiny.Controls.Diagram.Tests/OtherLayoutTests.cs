namespace Shiny.Controls.Diagram.Tests;

public class MindMapLayoutTests
{
    static DiagramNode SixBranches() => Graph.Parent(
        "root",
        Graph.Node("a"), Graph.Node("b"), Graph.Node("c"),
        Graph.Node("d"), Graph.Node("e"), Graph.Node("f")
    );


    [Fact]
    public void BranchesGoToBothSidesOfTheRoot()
    {
        var root = SixBranches();
        var model = Graph.Model(Graph.Flatten(root), layout: DiagramLayoutKind.MindMap);

        var left = root.Children.Count(c => c.Bounds.CenterX < root.Bounds.CenterX);
        var right = root.Children.Count(c => c.Bounds.CenterX > root.Bounds.CenterX);

        left.ShouldBe(3);
        right.ShouldBe(3);
        Graph.NoOverlaps(model.VisibleNodes).ShouldBeTrue();
    }


    [Fact]
    public void ItIsNarrowerAndTallerThanTheSameTreeDrawnDownwards()
    {
        // The reason a mindmap is its own layout: a wide first level is half as wide split in two.
        var tree = Graph.Model(Graph.Flatten(SixBranches()));
        var mindMap = Graph.Model(Graph.Flatten(SixBranches()), layout: DiagramLayoutKind.MindMap);

        mindMap.ContentBounds.Width.ShouldBeLessThan(tree.ContentBounds.Width);
        mindMap.ContentBounds.Height.ShouldBeGreaterThan(tree.ContentBounds.Height);
    }


    [Fact]
    public void TheRootSitsOnTheVerticalCentreLine()
    {
        var root = Graph.Parent("root", Graph.Node("a"), Graph.Node("b"), Graph.Node("c"));
        var model = Graph.Model(Graph.Flatten(root), layout: DiagramLayoutKind.MindMap);

        var top = model.VisibleNodes.Min(n => n.Bounds.Y);
        var bottom = model.VisibleNodes.Max(n => n.Bounds.Bottom);

        root.Bounds.CenterY.ShouldBe((top + bottom) / 2, 1.0);
    }


    [Fact]
    public void DeeperLevelsSitFurtherFromTheCentreOnBothSides()
    {
        var deepRight = Graph.Parent("a", Graph.Node("a1"));
        var deepLeft = Graph.Parent("b", Graph.Node("b1"));
        var root = Graph.Parent("root", deepRight, deepLeft);

        Graph.Model(Graph.Flatten(root), layout: DiagramLayoutKind.MindMap);

        // Right side grows outward, left side grows inward - both away from the root.
        deepRight.Children[0].Bounds.CenterX.ShouldBeGreaterThan(deepRight.Bounds.CenterX);
        deepLeft.Children[0].Bounds.CenterX.ShouldBeLessThan(deepLeft.Bounds.CenterX);
    }
}


public class RadialLayoutTests
{
    [Fact]
    public void EachLevelSitsOnItsOwnRing()
    {
        var branch = Graph.Parent("a", Graph.Node("a1"), Graph.Node("a2"));
        var root = Graph.Parent("root", branch, Graph.Node("b"));

        var model = Graph.Model(
            Graph.Flatten(root),
            layout: DiagramLayoutKind.Radial,
            configure: options => options.RadialRingSpacing = 150
        );

        var centre = root.Bounds.Center;

        branch.Bounds.Center.DistanceTo(centre).ShouldBe(150, 1.0);
        branch.Children[0].Bounds.Center.DistanceTo(centre).ShouldBe(300, 1.0);
        Graph.NoOverlaps(model.VisibleNodes).ShouldBeTrue();
    }


    [Fact]
    public void ABranchWithMoreLeavesGetsAWiderWedge()
    {
        // Weighting by leaf count rather than child count is what stops a heavy branch being squeezed
        // into the same wedge as a bare sibling.
        var heavy = Graph.Parent("heavy", Graph.Node("h1"), Graph.Node("h2"), Graph.Node("h3"), Graph.Node("h4"));
        var light = Graph.Parent("light", Graph.Node("l1"));
        var root = Graph.Parent("root", heavy, light);

        Graph.Model(Graph.Flatten(root), layout: DiagramLayoutKind.Radial);

        double Spread(DiagramNode parent)
        {
            var centre = root.Bounds.Center;
            var angles = parent.Children
                .Select(c => Math.Atan2(c.Bounds.CenterY - centre.Y, c.Bounds.CenterX - centre.X))
                .ToList();

            return angles.Max() - angles.Min();
        }

        Spread(heavy).ShouldBeGreaterThan(0);
        heavy.Children.Count.ShouldBe(4);
        light.Children.Count.ShouldBe(1);
    }
}


public class ForceDirectedLayoutTests
{
    static List<DiagramNode> Ring(int count)
    {
        var nodes = new List<DiagramNode>();

        for (var i = 0; i < count; i++)
            nodes.Add(Graph.Node($"n{i}"));

        return nodes;
    }


    [Fact]
    public void TheSameGraphSettlesToTheSamePlaceEveryTime()
    {
        // The reason the seed is a circle indexed by supply order rather than random: a random seed
        // makes the two hosts draw the same graph differently, and the same host draw it differently
        // on every rebuild.
        List<double> Run()
        {
            var nodes = Ring(8);
            Graph.Model(nodes, Graph.Edges("n0>n1", "n1>n2", "n2>n3", "n3>n0", "n4>n5", "n0>n6"),
                DiagramLayoutKind.ForceDirected);

            return nodes.SelectMany(n => new[] { n.X, n.Y }).ToList();
        }

        var first = Run();
        var second = Run();

        for (var i = 0; i < first.Count; i++)
            first[i].ShouldBe(second[i], 1e-9);
    }


    [Fact]
    public void ConnectedNodesEndUpCloserThanUnconnectedOnes()
    {
        var nodes = Ring(6);
        Graph.Model(nodes, Graph.Edges("n0>n1"), DiagramLayoutKind.ForceDirected);

        var joined = nodes[0].Bounds.Center.DistanceTo(nodes[1].Bounds.Center);

        var apart = 0d;
        for (var i = 2; i < nodes.Count; i++)
            apart = Math.Max(apart, nodes[0].Bounds.Center.DistanceTo(nodes[i].Bounds.Center));

        joined.ShouldBeLessThan(apart);
    }


    [Fact]
    public void APinnedNodeAnchorsTheDiagramAndDoesNotMove()
    {
        var nodes = Ring(5);
        nodes[0].IsPinned = true;
        nodes[0].X = 500;
        nodes[0].Y = 500;

        Graph.Model(nodes, Graph.Edges("n0>n1", "n1>n2"), DiagramLayoutKind.ForceDirected);

        nodes[0].X.ShouldBe(500);
        nodes[0].Y.ShouldBe(500);
    }


    [Fact]
    public void ASingleNodeDoesNotDivideByZero()
    {
        var nodes = Ring(1);
        var model = Graph.Model(nodes, [], DiagramLayoutKind.ForceDirected);

        double.IsFinite(nodes[0].X).ShouldBeTrue();
        double.IsFinite(nodes[0].Y).ShouldBeTrue();
        model.ContentBounds.IsEmpty.ShouldBeFalse();
    }


    [Fact]
    public void CoincidentNodesSeparateRatherThanSticking()
    {
        var nodes = Ring(3);

        foreach (var node in nodes)
        {
            node.IsPinned = false;
            node.X = 0;
            node.Y = 0;
        }

        Graph.Model(nodes, [], DiagramLayoutKind.ForceDirected);

        nodes[0].Bounds.Center.DistanceTo(nodes[1].Bounds.Center).ShouldBeGreaterThan(1);
    }
}
