using System.Collections.ObjectModel;
using Microsoft.AspNetCore.Components;

namespace Shiny.Blazor.Controls.Diagram.Tests;

/// <summary>
/// Host-level cover for the Blazor <see cref="DiagramView"/>.
/// </summary>
/// <remarks>
/// <para>
/// The engine is tested exhaustively in <c>Shiny.Controls.Diagram.Tests</c>, so nothing here
/// re-asserts a layout. What these cover is the component's own contract: that parameters reach the
/// model, that the source is observed, and - the one that actually bit - that setting parameters
/// twice with the same values does not rebuild a second time.
/// </para>
/// <para>
/// Driven through <see cref="DiagramHost"/> - a real renderer that discards its output - because a
/// component needs a render handle before its lifecycle is legal, and everything under test happens
/// in the component rather than in the markup.
/// </para>
/// </remarks>
public class DiagramViewTests
{
    static async Task<(DiagramHost Host, DiagramView View)> BuildAsync(
        ObservableCollection<DiagramNode> nodes,
        ObservableCollection<DiagramConnection> connections,
        Action<Dictionary<string, object?>>? configure = null
    )
    {
        var host = DiagramHost.Create();

        var parameters = new Dictionary<string, object?>
        {
            [nameof(DiagramView.Nodes)] = nodes,
            [nameof(DiagramView.Connections)] = connections
        };

        configure?.Invoke(parameters);

        var view = await host.MountAsync<DiagramView>(ParameterView.FromDictionary(parameters));
        return (host, view);
    }

    static ObservableCollection<DiagramNode> Hierarchy()
    {
        var root = new DiagramNode("root", "Root");
        var a = new DiagramNode("a", "A");
        var b = new DiagramNode("b", "B");

        root.Children.Add(a);
        root.Children.Add(b);

        return [root, a, b];
    }


    [Fact]
    public async Task SettingParametersBuildsTheModel()
    {
        var nodes = Hierarchy();
        var (_, view) = await BuildAsync(nodes, []);

        view.Model.VisibleNodes.Count.ShouldBe(3);
        view.Model.Issues.ShouldBeEmpty();
        nodes[0].Y.ShouldBeLessThan(nodes[1].Y);
    }


    [Fact]
    public async Task AHierarchyWithNoConnectionsStillDrawsItsLinks()
    {
        var (_, view) = await BuildAsync(Hierarchy(), []);

        view.Model.VisibleConnections.Count.ShouldBe(2);
        view.Model.VisibleConnections.ShouldAllBe(c => c.IsImplicit);
    }


    [Fact]
    public async Task SettingTheSameParametersAgainDoesNotRebuild()
    {
        // The loop this guards against is not obvious: raising OnBuilt invokes an EventCallback,
        // which re-renders the handling component, which passes parameters down again, which lands
        // back in OnParametersSet. Rebuilding unconditionally there pegs the renderer at 100% and the
        // page never paints - it looks like the browser has died rather than like a feedback loop.
        var nodes = Hierarchy();
        var connections = new ObservableCollection<DiagramConnection>();
        var builds = 0;

        var host = DiagramHost.Create();
        var view = await host.MountAsync<DiagramView>(ParameterView.FromDictionary(
            new Dictionary<string, object?>
            {
                [nameof(DiagramView.Nodes)] = nodes,
                [nameof(DiagramView.Connections)] = connections
            }));

        var parameters = new Dictionary<string, object?>
        {
            [nameof(DiagramView.Nodes)] = nodes,
            [nameof(DiagramView.Connections)] = connections,
            [nameof(DiagramView.OnBuilt)] = EventCallback.Factory.Create<DiagramModel>(view, _ => builds++)
        };

        // Adding a callback is not a change the rebuild guard cares about, so this pass does not
        // rebuild and the counter starts from zero.
        await host.SetParametersAsync(view, ParameterView.FromDictionary(parameters));
        builds.ShouldBe(0);

        await host.SetParametersAsync(view, ParameterView.FromDictionary(parameters));
        builds.ShouldBe(0);

        // A parameter that actually changed does rebuild.
        parameters[nameof(DiagramView.LayoutKind)] = DiagramLayoutKind.MindMap;
        await host.SetParametersAsync(view, ParameterView.FromDictionary(parameters));
        builds.ShouldBe(1);
    }


    [Fact]
    public async Task ParametersReachTheEngine()
    {
        var (_, view) = await BuildAsync(Hierarchy(), [], p =>
        {
            p[nameof(DiagramView.LayoutKind)] = DiagramLayoutKind.Layered;
            p[nameof(DiagramView.Direction)] = DiagramDirection.LeftToRight;
            p[nameof(DiagramView.NodeSpacing)] = 77d;
            p[nameof(DiagramView.Router)] = DiagramConnectionRouter.Bezier;
        });

        view.Model.Layout.ShouldBe(DiagramLayoutKind.Layered);
        view.Model.Options.Direction.ShouldBe(DiagramDirection.LeftToRight);
        view.Model.Options.NodeSpacing.ShouldBe(77);
        view.Model.Router.ShouldBe(DiagramConnectionRouter.Bezier);
    }


    [Fact]
    public async Task AddingToTheSourceCollectionRebuilds()
    {
        var nodes = Hierarchy();
        var (_, view) = await BuildAsync(nodes, []);

        nodes.Add(new DiagramNode("c", "C") { ParentId = "root" });

        view.Model.VisibleNodes.ShouldContain(n => n.Id == "c");
    }


    [Fact]
    public async Task SelectionIsReportedAndFlaggedOnTheModel()
    {
        var nodes = Hierarchy();
        var (_, view) = await BuildAsync(nodes, []);

        view.Select([nodes[1]], [], false);

        view.Selection.Nodes.ShouldContain(nodes[1]);
        nodes[1].IsSelected.ShouldBeTrue();

        view.Select([], [], false);
        view.Selection.IsEmpty.ShouldBeTrue();
        nodes[1].IsSelected.ShouldBeFalse();
    }


    [Fact]
    public async Task DeletingASelectedNodeTakesItsConnectionsAndUndoesAsOne()
    {
        var nodes = Hierarchy();
        var connections = new ObservableCollection<DiagramConnection>
        {
            new("root", "a") { Id = "explicit" }
        };

        var (_, view) = await BuildAsync(nodes, connections);

        view.Select([nodes[1]], [], false);
        await view.DeleteSelectionAsync();

        nodes.ShouldNotContain(n => n.Id == "a");
        connections.ShouldBeEmpty();
        view.CanUndo.ShouldBeTrue();

        view.Undo();

        nodes.ShouldContain(n => n.Id == "a");
        connections.Count.ShouldBe(1);
    }


    [Fact]
    public async Task AVetoedEditIsRolledBack()
    {
        var nodes = Hierarchy();
        var connections = new ObservableCollection<DiagramConnection>
        {
            new("root", "a") { Id = "explicit" }
        };

        var host = DiagramHost.Create();
        var vetoed = false;

        var view = await host.MountAsync<DiagramView>(ParameterView.FromDictionary(
            new Dictionary<string, object?>
            {
                [nameof(DiagramView.Nodes)] = nodes,
                [nameof(DiagramView.Connections)] = connections,
                [nameof(DiagramView.OnEditing)] = EventCallback.Factory.Create<DiagramEditingEventArgs>(
                    new object(),
                    e =>
                    {
                        vetoed = true;
                        e.Cancel = true;
                    })
            }));

        view.Select([nodes[1]], [], false);
        await view.DeleteSelectionAsync();

        vetoed.ShouldBeTrue();
        nodes.ShouldContain(n => n.Id == "a");
        connections.Count.ShouldBe(1);
        view.CanUndo.ShouldBeFalse();
    }


    [Fact]
    public async Task DisposingUnhooksTheSource()
    {
        var nodes = Hierarchy();
        var (_, view) = await BuildAsync(nodes, []);

        await view.DisposeAsync();

        var before = view.Model.VisibleNodes.Count;
        nodes.Add(new DiagramNode("late", "Late"));

        view.Model.VisibleNodes.Count.ShouldBe(before);
    }


    [Fact]
    public async Task DisposingTwiceIsSafe()
    {
        var (_, view) = await BuildAsync(Hierarchy(), []);

        await view.DisposeAsync();
        await Should.NotThrowAsync(async () => await view.DisposeAsync());
    }
}
