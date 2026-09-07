namespace Shiny.Controls.Diagram.Tests;

public class TreeLayoutTests
{
    [Fact]
    public void ParentIsCentredOverItsChildren()
    {
        var root = Graph.Parent("root", Graph.Node("a"), Graph.Node("b"), Graph.Node("c"));
        var model = Graph.Model(Graph.Flatten(root));

        var children = root.Children;
        var span = (children[0].Bounds.CenterX + children[^1].Bounds.CenterX) / 2;

        root.Bounds.CenterX.ShouldBe(span, 0.01);
        model.Issues.ShouldBeEmpty();
    }


    [Fact]
    public void SubtreesNeverOverlap()
    {
        // The case a naive per-level placement gets wrong: a wide subtree next to a narrow one.
        var left = Graph.Parent("left", Graph.Node("l1"), Graph.Node("l2"), Graph.Node("l3"), Graph.Node("l4"));
        var right = Graph.Parent("right", Graph.Node("r1"));
        var root = Graph.Parent("root", left, right);

        var model = Graph.Model(Graph.Flatten(root));

        Graph.NoOverlaps(model.VisibleNodes).ShouldBeTrue();
    }


    [Fact]
    public void IdenticalSubtreesAreDrawnIdentically()
    {
        // The tidiness property: two subtrees with the same shape get the same internal geometry
        // wherever they sit. This is what Buchheim buys over a simpler packing.
        DiagramNode Branch(string prefix) =>
            Graph.Parent(prefix, Graph.Node(prefix + "1"), Graph.Node(prefix + "2"));

        var a = Branch("a");
        var b = Branch("b");
        var root = Graph.Parent("root", a, b);

        Graph.Model(Graph.Flatten(root));

        var aSpread = a.Children[1].X - a.Children[0].X;
        var bSpread = b.Children[1].X - b.Children[0].X;

        aSpread.ShouldBe(bSpread, 0.01);
        (a.Children[0].X - a.X).ShouldBe(b.Children[0].X - b.X, 0.01);
    }


    [Theory]
    [InlineData(DiagramDirection.TopToBottom)]
    [InlineData(DiagramDirection.BottomToTop)]
    [InlineData(DiagramDirection.LeftToRight)]
    [InlineData(DiagramDirection.RightToLeft)]
    public void EveryDirectionPlacesEveryNodeInsideTheMargin(DiagramDirection direction)
    {
        var root = Graph.Parent("root", Graph.Parent("a", Graph.Node("a1")), Graph.Node("b"));

        var model = Graph.Model(
            Graph.Flatten(root),
            layout: DiagramLayoutKind.Tree,
            configure: options => options.Direction = direction
        );

        Graph.NoOverlaps(model.VisibleNodes).ShouldBeTrue();

        foreach (var node in model.VisibleNodes)
        {
            node.X.ShouldBeGreaterThanOrEqualTo(model.Options.Margin - 0.01);
            node.Y.ShouldBeGreaterThanOrEqualTo(model.Options.Margin - 0.01);
        }
    }


    [Fact]
    public void TopToBottomAndBottomToTopAreMirrorImages()
    {
        var down = Graph.Parent("root", Graph.Node("a"), Graph.Node("b"));
        var up = Graph.Parent("root", Graph.Node("a"), Graph.Node("b"));

        var downModel = Graph.Model(Graph.Flatten(down));
        var upModel = Graph.Model(
            Graph.Flatten(up),
            configure: options => options.Direction = DiagramDirection.BottomToTop
        );

        // The root is on top going down and on the bottom going up.
        down.Y.ShouldBeLessThan(down.Children[0].Y);
        up.Y.ShouldBeGreaterThan(up.Children[0].Y);

        downModel.ContentBounds.Height.ShouldBe(upModel.ContentBounds.Height, 0.01);
    }


    [Fact]
    public void AForestLaysEachTreeOutSideBySide()
    {
        var first = Graph.Parent("first", Graph.Node("f1"));
        var second = Graph.Parent("second", Graph.Node("s1"));

        var model = Graph.Model(Graph.Flatten(first, second));

        Graph.NoOverlaps(model.VisibleNodes).ShouldBeTrue();
        first.Bounds.Right.ShouldBeLessThan(second.Bounds.X);
    }


    [Fact]
    public void HierarchyIsDerivedFromConnectionsWhenNoParentLinksExist()
    {
        // The shape most people write first: a flat node list plus edges, and no ParentId anywhere.
        var nodes = new List<DiagramNode> { Graph.Node("root"), Graph.Node("a"), Graph.Node("b") };
        var model = Graph.Model(nodes, Graph.Edges("root>a", "root>b"));

        var root = nodes[0];
        root.Y.ShouldBeLessThan(nodes[1].Y);
        root.Bounds.CenterX.ShouldBe((nodes[1].Bounds.CenterX + nodes[2].Bounds.CenterX) / 2, 0.01);
    }


    [Fact]
    public void TipOverStacksChildrenInsteadOfSpreadingThem()
    {
        DiagramNode Build() => Graph.Parent(
            "root",
            Graph.Node("a"),
            Graph.Node("b"),
            Graph.Node("c"),
            Graph.Node("d")
        );

        var normal = Graph.Model(Graph.Flatten(Build()));
        var tipOver = Graph.Model(
            Graph.Flatten(Build()),
            configure: options => options.TreeStyle = DiagramTreeStyle.TipOver
        );

        // The whole point of tip-over: much narrower, and correspondingly taller.
        tipOver.ContentBounds.Width.ShouldBeLessThan(normal.ContentBounds.Width);
        tipOver.ContentBounds.Height.ShouldBeGreaterThan(normal.ContentBounds.Height);
        Graph.NoOverlaps(tipOver.VisibleNodes).ShouldBeTrue();
    }


    [Fact]
    public void ADeepChainDoesNotOverflowTheStack()
    {
        // The reason both walks run on explicit stacks. A recursive layout dies here, and a stack
        // overflow cannot be caught - it takes the process with it.
        var nodes = new List<DiagramNode>();
        DiagramNode? previous = null;

        for (var i = 0; i < 5000; i++)
        {
            var node = Graph.Node($"n{i}");

            previous?.Children.Add(node);
            previous = node;
            nodes.Add(node);
        }

        var model = Graph.Model(nodes);

        model.VisibleNodes.Count.ShouldBe(5000);
        nodes[^1].Y.ShouldBeGreaterThan(nodes[0].Y);
    }


    [Fact]
    public void APinnedNodeKeepsItsPositionThroughALayout()
    {
        var root = Graph.Parent("root", Graph.Node("a"), Graph.Node("b"));
        var pinned = root.Children[0];
        pinned.IsPinned = true;
        pinned.X = 999;
        pinned.Y = 888;

        Graph.Model(Graph.Flatten(root));

        pinned.X.ShouldBe(999);
        pinned.Y.ShouldBe(888);

        // Its space was still reserved, so its sibling did not close over the gap it left.
        root.Children[1].X.ShouldBeGreaterThan(0);
    }
}
