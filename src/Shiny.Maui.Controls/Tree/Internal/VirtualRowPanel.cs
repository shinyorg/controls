using Microsoft.Maui.Layouts;

namespace Shiny.Maui.Controls.Tree.Internal;

/// <summary>
/// The scroll content of a virtualized <see cref="TreeView"/>: as tall as every visible row
/// together (<see cref="RowCount"/> × <see cref="RowHeight"/>), but holding only the rows in and
/// around the viewport, each placed at its index in the flattened order.
/// </summary>
internal class VirtualRowPanel : Layout
{
    public double RowHeight { get; set; } = 40;

    int rowCount;
    public int RowCount
    {
        get => rowCount;
        set
        {
            if (rowCount == value)
                return;
            rowCount = value;
            InvalidateMeasure();
        }
    }

    public double ExtentHeight => RowCount * RowHeight;

    protected override ILayoutManager CreateLayoutManager() => new Manager(this);

    class Manager(VirtualRowPanel panel) : ILayoutManager
    {
        public Size Measure(double widthConstraint, double heightConstraint)
        {
            var width = 0d;
            foreach (var child in panel.Children)
            {
                var size = child.Measure(widthConstraint, panel.RowHeight);
                width = Math.Max(width, size.Width);
            }
            if (!double.IsInfinity(widthConstraint))
                width = widthConstraint;
            return new Size(width, panel.ExtentHeight);
        }

        public Size ArrangeChildren(Rect bounds)
        {
            foreach (var child in panel.Children)
            {
                var index = child is TreeNodeView row ? row.RowIndex : 0;
                child.Arrange(new Rect(bounds.X, bounds.Y + (index * panel.RowHeight), bounds.Width, panel.RowHeight));
            }
            return new Size(bounds.Width, panel.ExtentHeight);
        }
    }

    /// <summary>
    /// The [first, last] flattened indexes to realize for a viewport, padded by
    /// <paramref name="overscan"/> rows each side. Returns (0, -1) when there is nothing to show.
    /// </summary>
    public static (int First, int Last) VisibleRange(double scrollY, double viewportHeight, double rowHeight, int rowCount, int overscan)
    {
        if (rowCount <= 0 || rowHeight <= 0)
            return (0, -1);

        // No usable viewport yet (not laid out, or an unbounded parent): realize one screenful
        // worth of rows rather than all of them.
        if (double.IsNaN(viewportHeight) || double.IsInfinity(viewportHeight) || viewportHeight <= 0)
            viewportHeight = rowHeight * 30;

        // Clamp to the extent: a collapse shrinks the content before the scroller pulls its offset
        // back, and an offset past the end would leave the viewport blank until the next scroll.
        scrollY = Math.Clamp(scrollY, 0, Math.Max(0, (rowCount * rowHeight) - viewportHeight));

        var first = (int)Math.Floor(scrollY / rowHeight) - overscan;
        var last = (int)Math.Ceiling((scrollY + viewportHeight) / rowHeight) + overscan;
        first = Math.Clamp(first, 0, rowCount - 1);
        last = Math.Clamp(last, 0, rowCount - 1);
        return (first, last);
    }
}
