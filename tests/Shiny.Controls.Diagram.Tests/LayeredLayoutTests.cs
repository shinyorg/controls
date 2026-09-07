namespace Shiny.Controls.Diagram.Tests;

public class LayeredLayoutTests
{
    static DiagramModel Layered(
        IEnumerable<DiagramNode> nodes,
        IEnumerable<DiagramConnection> connections,
        Action<DiagramLayoutOptions>? configure = null
    ) => Graph.Model(nodes, connections, DiagramLayoutKind.Layered, configure);


    [Fact]
    public void AChainGetsOneLayerPerStep()
    {
        var nodes = new List<DiagramNode> { Graph.Node("a"), Graph.Node("b"), Graph.Node("c") };
        Layered(nodes, Graph.Edges("a>b", "b>c"));

        nodes[0].Y.ShouldBeLessThan(nodes[1].Y);
        nodes[1].Y.ShouldBeLessThan(nodes[2].Y);
        nodes[0].Depth.ShouldBe(0);
        nodes[2].Depth.ShouldBe(2);
    }


    [Fact]
    public void ARejoiningBranchLandsBelowBothSides()
    {
        // The case a tree layout cannot draw: d has two parents, so one of the two edges into it
        // would have to be thrown away to make a tree.
        var nodes = new List<DiagramNode>
        {
            Graph.Node("a"), Graph.Node("b"), Graph.Node("c"), Graph.Node("d")
        };

        var model = Layered(nodes, Graph.Edges("a>b", "a>c", "b>d", "c>d"));

        nodes[3].Depth.ShouldBe(2);
        nodes[3].Y.ShouldBeGreaterThan(nodes[1].Y);
        nodes[3].Y.ShouldBeGreaterThan(nodes[2].Y);

        // Both edges survive, which is the whole reason to reach for this layout.
        model.VisibleConnections.Count.ShouldBe(4);
        Graph.NoOverlaps(model.VisibleNodes).ShouldBeTrue();
    }


    [Fact]
    public void ACycleIsBrokenRatherThanHanging()
    {
        var nodes = new List<DiagramNode> { Graph.Node("a"), Graph.Node("b"), Graph.Node("c") };
        var edges = Graph.Edges("a>b", "b>c", "c>a");

        var model = Layered(nodes, edges);

        model.VisibleNodes.Count.ShouldBe(3);
        Graph.NoOverlaps(model.VisibleNodes).ShouldBeTrue();

        // Exactly one edge has to be reversed to open a three-node loop.
        edges.Count(e => e.IsReversed).ShouldBe(1);
    }


    [Fact]
    public void ALongEdgeIsBentAroundTheNodesItSpans()
    {
        // a>d spans three layers. Drawn straight it would cut through b and c.
        var nodes = new List<DiagramNode>
        {
            Graph.Node("a"), Graph.Node("b"), Graph.Node("c"), Graph.Node("d")
        };

        var edges = Graph.Edges("a>b", "b>c", "c>d", "a>d");
        Layered(nodes, edges);

        var longEdge = edges.Single(e => e.Id == "a>d");

        // Two dummy layers between them means two bends, so four points rather than two.
        longEdge.Points.Count.ShouldBeGreaterThan(2);
    }


    [Fact]
    public void SelfConnectionsDoNotStallTheCycleDetector()
    {
        var nodes = new List<DiagramNode> { Graph.Node("a"), Graph.Node("b") };
        var model = Layered(nodes, Graph.Edges("a>a", "a>b"));

        model.VisibleNodes.Count.ShouldBe(2);
        model.Issues.ShouldContain(i => i.Kind == DiagramIssueKind.SelfConnection);
    }


    [Fact]
    public void DisconnectedIslandsAreStillPlaced()
    {
        var nodes = new List<DiagramNode>
        {
            Graph.Node("a"), Graph.Node("b"), Graph.Node("island")
        };

        var model = Layered(nodes, Graph.Edges("a>b"));

        Graph.NoOverlaps(model.VisibleNodes).ShouldBeTrue();
        nodes[2].Width.ShouldBeGreaterThan(0);
    }


    [Fact]
    public void OrderingIsStableAcrossRuns()
    {
        // The determinism the two hosts depend on. An unstable sort or a dictionary-order iteration
        // shows up here and nowhere else.
        List<double> Run()
        {
            var nodes = new List<DiagramNode>
            {
                Graph.Node("a"), Graph.Node("b"), Graph.Node("c"), Graph.Node("d"), Graph.Node("e")
            };

            Layered(nodes, Graph.Edges("a>b", "a>c", "b>d", "c>d", "d>e", "a>e"));
            return nodes.SelectMany(n => new[] { n.X, n.Y }).ToList();
        }

        Run().ShouldBe(Run());
    }


    [Theory]
    [InlineData(DiagramDirection.TopToBottom)]
    [InlineData(DiagramDirection.LeftToRight)]
    [InlineData(DiagramDirection.BottomToTop)]
    [InlineData(DiagramDirection.RightToLeft)]
    public void EveryDirectionKeepsNodesInsideTheMargin(DiagramDirection direction)
    {
        var nodes = new List<DiagramNode> { Graph.Node("a"), Graph.Node("b"), Graph.Node("c") };

        var model = Layered(
            nodes,
            Graph.Edges("a>b", "a>c"),
            options => options.Direction = direction
        );

        foreach (var node in model.VisibleNodes)
        {
            node.X.ShouldBeGreaterThanOrEqualTo(model.Options.Margin - 0.01);
            node.Y.ShouldBeGreaterThanOrEqualTo(model.Options.Margin - 0.01);
        }
    }
}
