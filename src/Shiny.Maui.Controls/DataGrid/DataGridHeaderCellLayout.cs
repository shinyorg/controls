using Microsoft.Maui.Layouts;

namespace Shiny.Maui.Controls.DataGrid;

/// <summary>
/// One column header: the title (with its sort arrow) on the start side and the strip of affordance
/// glyphs - filter ▾, group ⊞, reorder ‹ › - on the end side.
/// </summary>
/// <remarks>
/// <para>
/// It replaces a Grid that split the cell 3* / 2*. That split reserved 40% of every header for the
/// glyphs whether they needed it or not, and one "▾" needs about ten points: in a narrow column the
/// title was left a sliver and ellipsized down to a bare "…" beside a mostly empty strip.
/// </para>
/// <para>
/// Here the glyphs take only what they measure, and the title takes the rest. The glyphs are still
/// capped so they cannot crowd the title out of a narrow column - the concern the star split existed
/// for - via <see cref="Split"/>: the title is guaranteed <see cref="TitleShare"/> of the room (or
/// everything it asks for, if that is less), and the glyphs lose off their end past that. Both
/// children are clipped, so whatever does not fit is cut rather than spilling into the next header.
/// </para>
/// <para>
/// The manager is a nested class for the reason <c>TagWrapLayout</c> gives: implementing
/// <see cref="ILayoutManager"/> on the layout itself would hide <c>VisualElement.Measure</c>.
/// </para>
/// </remarks>
class DataGridHeaderCellLayout : Layout
{
    /// <summary>The share of a too-narrow cell the title keeps however much the glyphs want.</summary>
    internal const double TitleShare = 0.6;

    sealed class HeaderCellLayoutManager(DataGridHeaderCellLayout view) : ILayoutManager
    {
        public Size Measure(double widthConstraint, double heightConstraint) => view.MeasureCell(widthConstraint, heightConstraint);
        public Size ArrangeChildren(Rect bounds) => view.ArrangeCell(bounds);
    }

    public DataGridHeaderCellLayout(View title, Layout? glyphs)
    {
        this.Title = title;
        this.Glyphs = glyphs;
        this.IsClippedToBounds = true;

        this.Add(title);
        if (glyphs is not null)
        {
            glyphs.IsClippedToBounds = true;
            this.Add(glyphs);
        }
    }

    protected override ILayoutManager CreateLayoutManager() => new HeaderCellLayoutManager(this);

    /// <summary>The title part - the label and its sort arrow.</summary>
    public View Title { get; }

    /// <summary>The affordance strip, or null when the column has none.</summary>
    public Layout? Glyphs { get; }

    /// <summary>Gap between the title and the glyphs.</summary>
    public double Spacing { get; set; } = 6;


    /// <summary>
    /// Divides <paramref name="available"/> between the title and the glyph strip. The glyphs get what
    /// they ask for unless that would push the title below <see cref="TitleShare"/> of the room (or
    /// below its own desired width, when that is smaller); the title gets everything else.
    /// </summary>
    internal static (double Title, double Glyphs) Split(double available, double titleDesired, double glyphsDesired, double spacing)
    {
        if (glyphsDesired <= 0)
            return (Math.Max(0, available), 0);

        var room = Math.Max(0, available - spacing);
        var titleFloor = Math.Min(titleDesired, room * TitleShare);
        var glyphs = Math.Max(0, Math.Min(glyphsDesired, room - titleFloor));
        return (room - glyphs, glyphs);
    }


    bool HasGlyphs => this.Glyphs is { IsVisible: true };

    // What each part asked for with no width limit, from the last measure. The title's own
    // DesiredSize cannot stand in for it at arrange time: after the re-measure at its allotted width
    // it reports the truncated width, which would shrink its floor on every pass.
    double titleNatural;
    double glyphsNatural;


    Size MeasureCell(double widthConstraint, double heightConstraint)
    {
        var padding = this.Padding;
        var innerHeight = double.IsInfinity(heightConstraint)
            ? heightConstraint
            : Math.Max(0, heightConstraint - padding.VerticalThickness);

        var glyphs = this.HasGlyphs
            ? ((IView)this.Glyphs!).Measure(double.PositiveInfinity, innerHeight)
            : Size.Zero;
        glyphsNatural = glyphs.Width;

        if (double.IsInfinity(widthConstraint))
        {
            // Unbounded - an Auto column, or a horizontal scroller. Nothing to share: both take what
            // they want, which is what makes an Auto column wide enough for its whole title.
            var title = ((IView)this.Title).Measure(double.PositiveInfinity, innerHeight);
            titleNatural = title.Width;
            var gap = glyphs.Width > 0 ? this.Spacing : 0;
            return new Size(
                title.Width + gap + glyphs.Width + padding.HorizontalThickness,
                Math.Max(title.Height, glyphs.Height) + padding.VerticalThickness
            );
        }

        var inner = Math.Max(0, widthConstraint - padding.HorizontalThickness);
        var titleDesired = ((IView)this.Title).Measure(double.PositiveInfinity, innerHeight);
        titleNatural = titleDesired.Width;
        var (titleWidth, glyphWidth) = Split(inner, titleDesired.Width, glyphs.Width, this.Spacing);

        // Re-measure the title at the width it will actually get so a truncating label reports the
        // height it will really have.
        var titleSize = ((IView)this.Title).Measure(titleWidth, innerHeight);
        return new Size(
            widthConstraint,
            Math.Max(titleSize.Height, glyphWidth > 0 ? glyphs.Height : 0) + padding.VerticalThickness
        );
    }


    Size ArrangeCell(Rect bounds)
    {
        var padding = this.Padding;
        var inner = Math.Max(0, bounds.Width - padding.HorizontalThickness);
        var height = Math.Max(0, bounds.Height - padding.VerticalThickness);

        var (titleWidth, glyphWidth) = Split(inner, titleNatural, this.HasGlyphs ? glyphsNatural : 0, this.Spacing);

        // The layout mirrors itself: in right-to-left the title sits on the right and the glyphs on
        // the left. The public FlowDirection can be MatchParent; the IView one is the effective value.
        var rtl = ((IView)this).FlowDirection == FlowDirection.RightToLeft;
        var left = bounds.X + padding.Left;
        var top = bounds.Y + padding.Top;

        var titleX = rtl ? left + inner - titleWidth : left;
        ((IView)this.Title).Arrange(new Rect(titleX, top, titleWidth, height));

        if (this.HasGlyphs)
        {
            var glyphX = rtl ? left : left + inner - glyphWidth;
            ((IView)this.Glyphs!).Arrange(new Rect(glyphX, top, glyphWidth, height));
        }

        return bounds.Size;
    }
}
