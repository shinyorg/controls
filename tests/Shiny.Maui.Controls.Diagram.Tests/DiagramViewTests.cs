using System.Collections.ObjectModel;

namespace Shiny.Maui.Controls.Diagram.Tests;

/// <summary>
/// Host-level cover for the MAUI <see cref="DiagramView"/>.
/// </summary>
/// <remarks>
/// The layouts, the routing and the geometry are tested exhaustively in
/// <c>Shiny.Controls.Diagram.Tests</c> against the shared engine, so nothing here re-asserts where a
/// node lands. What these assert is the half that only exists in MAUI: that the control survives
/// construction, that its bindable properties reach the model, that the source is observed rather
/// than needing a manual Rebuild, and that selection and undo behave from the public API.
/// </remarks>
public class DiagramViewTests
{
    static DiagramView Build(
        out ObservableCollection<DiagramNode> nodes,
        out ObservableCollection<DiagramConnection> connections
    )
    {
        // Application.Current is process-wide; constructing one unconditionally rather than reusing
        // whatever a previous test left behind is what stops an implicit style leaking between them.
        _ = new Application();

        var root = new DiagramNode("root", "Root");
        var a = new DiagramNode("a", "A");
        var b = new DiagramNode("b", "B");

        root.Children.Add(a);
        root.Children.Add(b);

        nodes = [root, a, b];
        connections = [];

        return new DiagramView
        {
            Nodes = nodes,
            Connections = connections
        };
    }


    [Fact]
    public void ConstructingAndBindingASourceBuildsTheModel()
    {
        var view = Build(out var nodes, out _);

        view.Model.VisibleNodes.Count.ShouldBe(3);
        view.Model.Issues.ShouldBeEmpty();

        // The hierarchy laid out, which is what proves the properties actually reached the engine.
        nodes[0].Y.ShouldBeLessThan(nodes[1].Y);
    }


    [Fact]
    public void AHierarchyWithNoConnectionsStillDrawsItsLinks()
    {
        var view = Build(out _, out _);

        view.Model.VisibleConnections.Count.ShouldBe(2);
        view.Model.VisibleConnections.ShouldAllBe(c => c.IsImplicit);
    }


    [Fact]
    public void AddingToTheSourceCollectionRebuildsWithoutBeingAsked()
    {
        var view = Build(out var nodes, out _);
        var before = view.Model.VisibleNodes.Count;

        nodes.Add(new DiagramNode("c", "C") { ParentId = "root" });

        view.Model.VisibleNodes.Count.ShouldBe(before + 1);
        view.Model.VisibleNodes.ShouldContain(n => n.Id == "c");
    }


    [Fact]
    public void ChangingANodePropertyRebuilds()
    {
        var view = Build(out var nodes, out _);

        nodes[1].IsVisible = false;

        view.Model.VisibleNodes.ShouldNotContain(nodes[1]);
    }


    [Fact]
    public void LayoutKindReachesTheEngine()
    {
        var view = Build(out _, out _);

        view.LayoutKind = DiagramLayoutKind.MindMap;
        view.Model.Layout.ShouldBe(DiagramLayoutKind.MindMap);

        view.Direction = DiagramDirection.LeftToRight;
        view.Model.Options.Direction.ShouldBe(DiagramDirection.LeftToRight);

        view.NodeSpacing = 99;
        view.Model.Options.NodeSpacing.ShouldBe(99);
    }


    [Fact]
    public void TheLayoutPropertyDoesNotShadowVisualElementsOwnLayout()
    {
        // The reason it is called LayoutKind. A property named Layout compiles with CS0108 and hides
        // VisualElement.Layout(Rect) - which is the class of collision that leaves a control looking
        // unwired rather than broken.
        var view = Build(out _, out _);

        // Referenced as a method group rather than invoked: resolving the name at all is the thing
        // under test - a property called Layout would make this line not compile. The method itself
        // is obsolete, which is beside the point here.
#pragma warning disable CS0618
        Action<Rect> layout = view.Layout;
#pragma warning restore CS0618

        layout.ShouldNotBeNull();
        view.LayoutKind.ShouldBe(DiagramLayoutKind.Tree);
    }


    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ZoomNeverReachesZero(double value)
    {
        // Every coordinate conversion divides by the zoom.
        var view = Build(out _, out _);

        view.Zoom = value;

        view.Zoom.ShouldBeGreaterThan(0);
    }


    [Fact]
    public void ZoomIsClampedToItsRange()
    {
        var view = Build(out _, out _);

        view.MinZoom = 0.5;
        view.MaxZoom = 2;

        view.Zoom = 100;
        view.Zoom.ShouldBe(2);

        view.Zoom = 0.01;
        view.Zoom.ShouldBe(0.5);
    }


    [Fact]
    public void NarrowingTheZoomRangeRecoercesTheCurrentZoom()
    {
        // coerceValue only runs when the coerced property is itself set, so a range that moves under
        // a valid zoom leaves it out of bounds unless the range handler re-applies it.
        var view = Build(out _, out _);

        view.Zoom = 3;
        view.MaxZoom = 1.5;

        view.Zoom.ShouldBe(1.5);
    }


    [Fact]
    public void SelectingRaisesTheEventAndSetsTheBindableNode()
    {
        var view = Build(out var nodes, out _);
        DiagramSelection? seen = null;
        view.SelectionChanged += (_, e) => seen = e.Selection;

        view.Select([nodes[1]], [], false);

        seen.ShouldNotBeNull();
        seen!.Nodes.ShouldContain(nodes[1]);
        view.SelectedNode.ShouldBeSameAs(nodes[1]);
        nodes[1].IsSelected.ShouldBeTrue();
    }


    [Fact]
    public void SettingSelectedNodeSelectsIt()
    {
        var view = Build(out var nodes, out _);

        view.SelectedNode = nodes[2];

        nodes[2].IsSelected.ShouldBeTrue();
        view.Selection.Nodes.ShouldContain(nodes[2]);
    }


    [Fact]
    public void DeletingASelectedNodeTakesItsConnectionsAndUndoesAsOne()
    {
        var view = Build(out var nodes, out var connections);
        connections.Add(new DiagramConnection("root", "a") { Id = "explicit" });
        view.Rebuild();

        view.Select([nodes[1]], [], false);
        view.DeleteSelection();

        nodes.ShouldNotContain(n => n.Id == "a");
        connections.ShouldBeEmpty();
        view.CanUndo.ShouldBeTrue();

        view.Undo();

        nodes.ShouldContain(n => n.Id == "a");
        connections.Count.ShouldBe(1);
    }


    [Fact]
    public void AVetoedEditIsRolledBack()
    {
        var view = Build(out var nodes, out var connections);
        connections.Add(new DiagramConnection("root", "a") { Id = "explicit" });
        view.Rebuild();

        view.Editing += (_, e) => e.Cancel = true;

        view.Select([nodes[1]], [], false);
        view.DeleteSelection();

        nodes.ShouldContain(n => n.Id == "a");
        connections.Count.ShouldBe(1);
        view.CanUndo.ShouldBeFalse();
    }


    [Fact]
    public void BuiltReportsValidationIssues()
    {
        var view = Build(out _, out var connections);
        DiagramModel? built = null;
        view.Built += (_, e) => built = e.Model;

        connections.Add(new DiagramConnection("root", "ghost"));

        built.ShouldNotBeNull();
        built!.Issues.ShouldContain(i => i.Kind == DiagramIssueKind.DanglingConnection);
    }


    [Fact]
    public void DisposingUnhooksTheSourceSoLaterChangesDoNothing()
    {
        var view = Build(out var nodes, out _);
        view.Dispose();

        var before = view.Model.VisibleNodes.Count;
        nodes.Add(new DiagramNode("late", "Late"));

        view.Model.VisibleNodes.Count.ShouldBe(before);
    }


    [Fact]
    public void DisposingTwiceIsSafe()
    {
        var view = Build(out _, out _);

        view.Dispose();
        Should.NotThrow(view.Dispose);
    }


    [Fact]
    public void ReplacingTheSourceDropsTheOldSubscription()
    {
        var view = Build(out var nodes, out _);
        var replacement = new ObservableCollection<DiagramNode> { new("only", "Only") };

        view.Nodes = replacement;
        view.Model.VisibleNodes.Count.ShouldBe(1);

        // The old collection must no longer be able to drive a rebuild.
        nodes.Add(new DiagramNode("stale", "Stale"));
        view.Model.VisibleNodes.Count.ShouldBe(1);
    }
}
