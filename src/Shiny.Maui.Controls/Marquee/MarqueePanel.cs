using Microsoft.Maui.Layouts;

namespace Shiny.Maui.Controls;

/// <summary>
/// The moving run of items. Its own bounds are the bounding box of every item it holds, so when it is
/// translated the items move with it rather than out of it - Android clips a child to its parent, and a
/// run arranged in a viewport-sized box would lose everything that started outside the viewport.
/// </summary>
sealed class MarqueeTrack : Layout
{
    /// <summary>Where each child goes, relative to the track's own origin. Written by the panel's arrange.</summary>
    internal readonly List<Rect> Placements = [];

    internal Size Extent { get; set; }

    sealed class Manager(MarqueeTrack track) : ILayoutManager
    {
        // The panel has already measured every child; the track's size is the box around them.
        public Size Measure(double widthConstraint, double heightConstraint) => track.Extent;

        public Size ArrangeChildren(Rect bounds)
        {
            for (var i = 0; i < track.Children.Count && i < track.Placements.Count; i++)
                track.Children[i].Arrange(track.Placements[i]);

            return bounds.Size;
        }
    }

    protected override ILayoutManager CreateLayoutManager() => new Manager(this);
}


/// <summary>The clipped viewport: lays the track out along the axis, sizes itself, keeps the fade on top.</summary>
sealed class MarqueePanel : Layout
{
    readonly Marquee owner;

    public MarqueePanel(Marquee owner)
    {
        this.owner = owner;
        this.IsClippedToBounds = true;
    }

    protected override ILayoutManager CreateLayoutManager() => new Manager(this);

    sealed class Manager(MarqueePanel panel) : ILayoutManager
    {
        public Size Measure(double widthConstraint, double heightConstraint) => panel.owner.MeasurePanel(widthConstraint, heightConstraint);
        public Size ArrangeChildren(Rect bounds) => panel.owner.ArrangePanel(bounds);
    }
}
