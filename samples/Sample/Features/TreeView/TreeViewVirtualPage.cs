using System.Diagnostics;
using Shiny.Maui.Controls.Tree;

namespace Sample.Features.TreeView;

/// <summary>
/// 1,000 folders × 50 files: 51,000 rows once expanded, with IsVirtualized building only the rows
/// in and around the viewport.
/// </summary>
public class TreeViewVirtualPage : ContentPage
{
    const int Folders = 1000;
    const int FilesPerFolder = 50;

    record DemoNode(string Name, List<DemoNode>? Children)
    {
        public override string ToString() => (Children is null ? "📄 " : "📁 ") + Name;
    }

    readonly Shiny.Maui.Controls.Tree.TreeView tree;
    readonly Label status;

    public TreeViewVirtualPage()
    {
        Title = "Tree View · Virtualized";

        var roots = Enumerable.Range(1, Folders)
            .Select(f => new DemoNode(
                $"Folder {f:0000}",
                Enumerable.Range(1, FilesPerFolder).Select(i => new DemoNode($"file-{f:0000}-{i:00}.txt", null)).ToList()))
            .ToList();

        status = new Label { FontSize = 12, Padding = new Thickness(12, 6), AutomationId = "TreeVirtualStatus", Text = "Expand all, then scroll." };

        tree = new Shiny.Maui.Controls.Tree.TreeView
        {
            AutomationId = "TreeVirtual",
            IsVirtualized = true,
            RowHeight = 36,
            ShowGuideLines = true,
            ChildrenSelector = o => ((DemoNode)o).Children,
            HasChildrenSelector = o => ((DemoNode)o).Children is { Count: > 0 },
            ItemsSource = roots
        };
        tree.ItemSelected += (_, e) => status.Text = $"Selected {((DemoNode)e.Item).Name}";

        var expand = new Button { Text = "Expand all", FontSize = 12, Padding = new Thickness(10, 4), AutomationId = "TreeVirtualExpandAll" };
        expand.Clicked += (_, _) =>
        {
            var sw = Stopwatch.StartNew();
            tree.ExpandAll();
            status.Text = $"Expanded {Folders * (FilesPerFolder + 1):N0} rows in {sw.ElapsedMilliseconds} ms, {tree.RealizedRowCount} rows built";
        };
        var collapse = new Button { Text = "Collapse all", FontSize = 12, Padding = new Thickness(10, 4), AutomationId = "TreeVirtualCollapseAll" };
        collapse.Clicked += (_, _) => tree.CollapseAll();

        var grid = new Grid
        {
            RowDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) }
        };
        grid.Add(new HorizontalStackLayout { Spacing = 8, Padding = new Thickness(12, 8), Children = { expand, collapse } }, 0, 0);
        grid.Add(tree, 0, 1);
        grid.Add(status, 0, 2);
        Content = grid;
    }
}
