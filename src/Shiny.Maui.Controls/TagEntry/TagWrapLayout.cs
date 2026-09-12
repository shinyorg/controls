using Microsoft.Maui.Layouts;

namespace Shiny.Maui.Controls;

/// <summary>
/// Lays children out in a row that wraps, with the last child stretching to fill whatever is left of
/// the line it lands on.
/// </summary>
/// <remarks>
/// <para>
/// It exists for <see cref="TagEntry"/>, whose box is a run of chips followed by an editor that has to
/// take the rest of the current line and drop to a line of its own when there is not enough of it left
/// to type in. Nothing already in the package does that: a <c>HorizontalStackLayout</c> does not wrap,
/// and the wrapping collection controls are virtualized surfaces for data rather than a handful of
/// self-sizing views. <see cref="ChipGroup"/> uses it too, with <see cref="FillTrailingChild"/> off —
/// a chip group's last chip is a chip like any other and has nothing to fill.
/// </para>
/// <para>
/// The manager is a nested class rather than the layout implementing <see cref="ILayoutManager"/>
/// itself, for the reason <c>FlyoutView</c> gives: <c>ILayoutManager.Measure</c> would hide
/// <c>VisualElement.Measure</c>, and a caller asking the view for its size would silently get a layout
/// pass instead.
/// </para>
/// </remarks>
class TagWrapLayout : Layout
{
    sealed class WrapLayoutManager(TagWrapLayout view) : ILayoutManager
    {
        public Size Measure(double widthConstraint, double heightConstraint) => view.MeasureWrap(widthConstraint);
        public Size ArrangeChildren(Rect bounds) => view.ArrangeWrap(bounds);
    }

    protected override ILayoutManager CreateLayoutManager() => new WrapLayoutManager(this);

    /// <summary>Gap between children on the same line.</summary>
    public double HorizontalSpacing { get; set; } = 6;

    /// <summary>Gap between lines.</summary>
    public double VerticalSpacing { get; set; } = 6;

    /// <summary>
    /// How narrow the trailing child may get before it is given a line of its own. Below this the editor
    /// is a sliver nobody can type into, which is worse than one more row of height.
    /// </summary>
    public double MinimumTrailingWidth { get; set; } = 90;

    /// <summary>Whether the last child stretches across the rest of its line.</summary>
    public bool FillTrailingChild { get; set; } = true;

    /// <summary>
    /// Whether a line that runs out of room wraps. Off, everything stays on one line however narrow the
    /// box is — the arrangement a horizontally scrolling row of chips needs, where the overflow is meant
    /// to be scrolled to rather than dropped underneath.
    /// </summary>
    public bool Wrap { get; set; } = true;


    /// <summary>One child's place in the flow, produced by the single pass both measure and arrange use.</summary>
    readonly record struct Placement(IView Child, Rect Bounds);

    List<Placement> Flow(double widthConstraint, out Size total)
    {
        var placements = new List<Placement>();
        var children = this.Children.Where(x => x.Visibility != Visibility.Collapsed).ToList();

        // An unbounded width is a real case - a group inside a horizontal ScrollView, or a measure pass
        // before the parent knows its own size. There is nothing to wrap against, so everything runs on
        // one line and the trailing child takes only what it asked for.
        var bounded = this.Wrap && !double.IsInfinity(widthConstraint) && widthConstraint > 0;

        double x = 0, y = 0, lineHeight = 0, widest = 0;

        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];
            var last = i == children.Count - 1;
            var available = bounded ? Math.Max(0, widthConstraint - x) : double.PositiveInfinity;
            var size = child.Measure(bounded ? widthConstraint : double.PositiveInfinity, double.PositiveInfinity);

            var width = size.Width;
            var wants = last && this.FillTrailingChild && bounded
                ? Math.Max(width, this.MinimumTrailingWidth)
                : width;

            // Wrap when this child does not fit the rest of the line, unless it is already at the start
            // of one - a single child wider than the box has nowhere better to go.
            if (bounded && x > 0 && wants > available)
            {
                x = 0;
                y += lineHeight + this.VerticalSpacing;
                lineHeight = 0;
                available = widthConstraint;
            }

            if (last && this.FillTrailingChild && bounded)
                width = Math.Max(width, available);

            placements.Add(new Placement(child, new Rect(x, y, width, size.Height)));

            lineHeight = Math.Max(lineHeight, size.Height);
            x += width + this.HorizontalSpacing;
            widest = Math.Max(widest, Math.Min(x - this.HorizontalSpacing, bounded ? widthConstraint : x));
        }

        total = new Size(widest, y + lineHeight);
        return placements;
    }


    Size MeasureWrap(double widthConstraint)
    {
        var horizontal = this.Padding.HorizontalThickness;
        var vertical = this.Padding.VerticalThickness;

        var inner = double.IsInfinity(widthConstraint) ? widthConstraint : Math.Max(0, widthConstraint - horizontal);
        this.Flow(inner, out var total);

        return new Size(total.Width + horizontal, total.Height + vertical);
    }


    Size ArrangeWrap(Rect bounds)
    {
        var inner = Math.Max(0, bounds.Width - this.Padding.HorizontalThickness);
        var placements = this.Flow(inner, out var total);

        foreach (var (child, rect) in placements)
        {
            child.Arrange(new Rect(
                rect.X + this.Padding.Left,
                rect.Y + this.Padding.Top,
                rect.Width,
                rect.Height
            ));
        }

        return new Size(bounds.Width, total.Height + this.Padding.VerticalThickness);
    }
}
