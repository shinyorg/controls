namespace Shiny.Controls.Diagram.Tests;

public class DiagramModelTests
{
    [Fact]
    public void AFlatParentIdSourceRebuildsTheSameTreeAsANestedOne()
    {
        // Both shapes are supported deliberately: rows come out of a database flat and out of a view
        // model nested, and forcing a conversion on the consumer is how a control ends up with two
        // half-supported paths.
        var nested = Graph.Parent("root", Graph.Node("a"), Graph.Node("b"));
        var nestedModel = Graph.Model(Graph.Flatten(nested));

        var flat = new List<DiagramNode>
        {
            Graph.Node("root"),
            new("a", "a") { ParentId = "root", Width = 100, Height = 50 },
            new("b", "b") { ParentId = "root", Width = 100, Height = 50 }
        };
        var flatModel = Graph.Model(flat);

        flatModel.Issues.ShouldBeEmpty();
        nestedModel.Issues.ShouldBeEmpty();

        for (var i = 0; i < 3; i++)
        {
            flat[i].X.ShouldBe(nestedModel.VisibleNodes[i].X, 0.01);
            flat[i].Y.ShouldBe(nestedModel.VisibleNodes[i].Y, 0.01);
        }
    }


    [Fact]
    public void ADuplicateIdIsReportedAndTheFirstNodeWins()
    {
        var nodes = new List<DiagramNode> { Graph.Node("a"), Graph.Node("a") };
        var model = Graph.Model(nodes);

        model.Issues.ShouldContain(i => i.Kind == DiagramIssueKind.DuplicateId);
        model.VisibleNodes.Count.ShouldBe(1);
        model.VisibleNodes[0].ShouldBeSameAs(nodes[0]);
    }


    [Fact]
    public void ADanglingConnectionIsReportedAndNotDrawn()
    {
        var model = Graph.Model([Graph.Node("a")], Graph.Edges("a>ghost"));

        model.Issues.ShouldContain(i =>
            i.Kind == DiagramIssueKind.DanglingConnection && i.Message.Contains("ghost"));

        model.VisibleConnections.ShouldBeEmpty();
    }


    [Fact]
    public void ValidationReportsRatherThanThrows()
    {
        // Every problem at once. A control that threw on any of these would be unusable against real
        // data, and one that silently dropped them would be worse.
        var a = Graph.Node("a");
        var b = Graph.Node("b");
        a.ParentId = "b";
        b.ParentId = "a";

        var model = Graph.Model([a, b, Graph.Node("b")], Graph.Edges("a>missing", "a>a"));

        model.Issues.Select(i => i.Kind).Distinct().ShouldContain(DiagramIssueKind.DuplicateId);
        model.Issues.Select(i => i.Kind).Distinct().ShouldContain(DiagramIssueKind.DanglingConnection);
        model.Issues.Select(i => i.Kind).Distinct().ShouldContain(DiagramIssueKind.SelfConnection);
        model.VisibleNodes.ShouldNotBeEmpty();
    }


    [Fact]
    public void AParentCycleIsCutAndReported()
    {
        var a = Graph.Node("a");
        var b = Graph.Node("b");
        a.ParentId = "b";
        b.ParentId = "a";

        var model = Graph.Model([a, b]);

        model.Issues.ShouldContain(i => i.Kind == DiagramIssueKind.ParentCycle);
        model.VisibleNodes.Count.ShouldBe(2);
    }


    [Fact]
    public void CollapsingANodeHidesItsWholeSubtreeAndTheConnectionsIntoIt()
    {
        var child = Graph.Node("child");
        var grandchild = Graph.Node("grandchild");
        child.Children.Add(grandchild);

        var root = Graph.Parent("root", child);
        var nodes = Graph.Flatten(root);

        var model = Graph.Model(nodes, Graph.Edges("root>child", "child>grandchild"));
        model.VisibleNodes.Count.ShouldBe(3);

        child.IsExpanded = false;
        model.Rebuild();

        model.VisibleNodes.ShouldNotContain(grandchild);
        model.VisibleNodes.ShouldContain(child);
        model.VisibleConnections.Count.ShouldBe(1);
        grandchild.IsHidden.ShouldBeTrue();
    }


    [Fact]
    public void ContentBoundsCoversTheRoutesAndNotJustTheNodes()
    {
        // A self-loop reaches outside every node box. A content size that missed it would clip the
        // drawing at exactly the edge a user needs to scroll to.
        var node = Graph.Node("a");
        var model = Graph.Model([node], Graph.Edges("a>a"), DiagramLayoutKind.None);

        var rightmost = model.VisibleConnections[0].Points.Max(p => p.X);
        model.ContentBounds.Width.ShouldBeGreaterThanOrEqualTo(rightmost);
    }


    [Fact]
    public void SwitchingLayoutsDiscardsThePreviousRunsBends()
    {
        // A layered run threads a long edge through dummies. Keeping those after a switch to Tree
        // leaves arrows detouring around nodes that are no longer where they were.
        var nodes = new List<DiagramNode>
        {
            Graph.Node("a"), Graph.Node("b"), Graph.Node("c"), Graph.Node("d")
        };

        var edges = Graph.Edges("a>b", "b>c", "c>d", "a>d");

        var model = new DiagramModel { Layout = DiagramLayoutKind.Layered };
        model.SetSource(nodes, edges);
        model.Rebuild();

        var longEdge = edges.Single(e => e.Id == "a>d");
        var bentPoints = longEdge.Points.Count;
        bentPoints.ShouldBeGreaterThan(2);

        model.Layout = DiagramLayoutKind.None;
        model.Rebuild();

        longEdge.Points.Count.ShouldBeLessThan(bentPoints);
    }


    [Fact]
    public void LayoutNoneLeavesEveryPositionAlone()
    {
        var node = Graph.Node("a");
        node.X = 123;
        node.Y = 456;

        Graph.Model([node], layout: DiagramLayoutKind.None);

        node.X.ShouldBe(123);
        node.Y.ShouldBe(456);
    }


    [Fact]
    public void LayoutNoneStillAppliesDefaultSizes()
    {
        // Without this a hand-placed diagram with no explicit sizes draws nothing at all.
        var node = new DiagramNode("a", "a");
        var model = Graph.Model([node], layout: DiagramLayoutKind.None);

        node.Width.ShouldBeGreaterThan(0);
        node.Height.ShouldBeGreaterThan(0);
        model.ContentBounds.IsEmpty.ShouldBeFalse();
    }


    [Fact]
    public void IssuesAreRebuiltRatherThanAccumulated()
    {
        var model = new DiagramModel { Layout = DiagramLayoutKind.Tree };
        model.SetSource([Graph.Node("a")], Graph.Edges("a>ghost"));
        model.Rebuild();
        model.Issues.Count.ShouldBe(1);

        model.Rebuild();
        model.Issues.Count.ShouldBe(1);

        model.SetSource([Graph.Node("a")], []);
        model.Rebuild();
        model.Issues.ShouldBeEmpty();
    }


    [Fact]
    public void AnEmptyDiagramBuildsToNothingWithoutThrowing()
    {
        var model = Graph.Model([], []);

        model.VisibleNodes.ShouldBeEmpty();
        model.ContentBounds.ShouldBe(DiagramRect.Empty);
        model.Issues.ShouldBeEmpty();
    }
}
