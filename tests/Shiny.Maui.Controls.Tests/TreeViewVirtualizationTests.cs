using Microsoft.Maui.Controls;
using Shiny.Maui.Controls.Tree;
using Shiny.Maui.Controls.Tree.Internal;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// IsVirtualized builds only the rows in and around the viewport, places each at its index in the
/// flattened order, and keeps the rows it already built when an expand or collapse only moves them.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class TreeViewVirtualizationTests
{
    record Node(string Name, List<Node> Children);

    static TreeView BuildTree(int roots, int childrenEach, out List<Node> items)
    {
        new Application();
        items = Enumerable.Range(0, roots)
            .Select(r => new Node($"root{r}", Enumerable.Range(0, childrenEach).Select(c => new Node($"root{r}.{c}", new())).ToList()))
            .ToList();

        return new TreeView
        {
            IsVirtualized = true,
            RowHeight = 40,
            ChildrenSelector = o => ((Node)o).Children,
            HasChildrenSelector = o => ((Node)o).Children.Count > 0,
            ItemsSource = items
        };
    }

    static List<TreeNodeView> Rows(TreeView tree) => tree.RowViews.OrderBy(r => r.RowIndex).ToList();

    [Fact]
    public void OnlyTheViewportIsRealized()
    {
        var tree = BuildTree(10_000, 0, out _);

        // 400px viewport / 40px rows = 10 rows, +6 overscan below (none above at the top).
        tree.RealizeVisibleRows(0, 400);

        var rows = Rows(tree);
        rows.Count.ShouldBe(17);
        rows[0].RowIndex.ShouldBe(0);
        rows[^1].RowIndex.ShouldBe(16);
        tree.rowLayout.Children.Count.ShouldBe(0);
        tree.virtualPanel.ExtentHeight.ShouldBe(10_000 * 40);
    }

    [Fact]
    public void ScrollingMovesTheWindow()
    {
        var tree = BuildTree(10_000, 0, out var items);

        tree.RealizeVisibleRows(40 * 5000, 400);

        var rows = Rows(tree);
        rows.Count.ShouldBe(23); // 10 visible + 6 overscan each side + the partial row
        rows[0].RowIndex.ShouldBe(4994);
        rows[0].Node.Item.ShouldBe(items[4994]);
        rows[^1].RowIndex.ShouldBe(5016);
    }

    [Fact]
    public void ExpandKeepsRealizedRowsAndShiftsTheOnesBelow()
    {
        var tree = BuildTree(100, 3, out var items);
        tree.RealizeVisibleRows(0, 400);

        var before = Rows(tree).ToDictionary(r => r.Node.Item);
        tree.Expand(items[1]);
        tree.RealizeVisibleRows(0, 400);

        var after = Rows(tree);
        // root0 stays put, root1's children slot in at 2..4, root2 moves down to 5.
        after.Select(r => ((Node)r.Node.Item).Name).Take(6)
            .ShouldBe(new[] { "root0", "root1", "root1.0", "root1.1", "root1.2", "root2" });
        after.Single(r => r.Node.Item == items[2]).RowIndex.ShouldBe(5);

        // Rows for nodes still in range are the same instances, not rebuilt.
        after.Single(r => r.Node.Item == items[0]).ShouldBeSameAs(before[items[0]]);
        after.Single(r => r.Node.Item == items[2]).ShouldBeSameAs(before[items[2]]);
    }

    [Fact]
    public void ExpandAllOnALargeTreeStillOnlyBuildsTheWindow()
    {
        var tree = BuildTree(1000, 50, out _);
        tree.ExpandAll();
        tree.RealizeVisibleRows(0, 400);

        tree.virtualPanel.RowCount.ShouldBe(1000 * 51);
        Rows(tree).Count.ShouldBe(17);
    }

    [Fact]
    public void TurningVirtualizationOffRendersEveryRow()
    {
        var tree = BuildTree(50, 0, out _);
        tree.RealizeVisibleRows(0, 400);

        tree.IsVirtualized = false;

        tree.virtualPanel.Children.Count.ShouldBe(0);
        tree.rowLayout.Children.Count.ShouldBe(50);
    }

    [Fact]
    public void SelectionStillReachesARowThatScrolledBackIn()
    {
        var tree = BuildTree(1000, 0, out var items);
        tree.SelectionMode = TreeSelectionMode.Multiple;
        tree.RealizeVisibleRows(0, 400);

        tree.SetBranchSelected(items[900], true);
        tree.RealizeVisibleRows(40 * 895, 400);

        tree.FindNode(items[900])!.IsSelected.ShouldBeTrue();
        Rows(tree).Single(r => r.Node.Item == items[900]).Node.IsSelected.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0, 400, 40, 100, 0, 0, 10)]
    [InlineData(400, 400, 40, 100, 2, 8, 22)]
    [InlineData(10_000, 400, 40, 100, 2, 88, 99)]   // offset past the end: clamps to the last screenful
    [InlineData(0, 400, 40, 0, 2, 0, -1)]           // empty
    [InlineData(0, double.PositiveInfinity, 40, 100, 0, 0, 30)] // unbounded: one screenful, not all
    public void VisibleRange(double scrollY, double viewport, double rowHeight, int count, int overscan, int first, int last)
    {
        VirtualRowPanel.VisibleRange(scrollY, viewport, rowHeight, count, overscan).ShouldBe((first, last));
    }
}
