using System.Globalization;

namespace Shiny.Controls.Office.Shell;

/// <summary>The unit a ruler is numbered in.</summary>
public enum OfficeRulerUnit
{
    Inches,
    Centimeters
}


/// <summary>How a tab stop aligns the text that reaches it.</summary>
public enum OfficeTabAlignment
{
    Left,
    Center,
    Right,
    Decimal
}


/// <summary>A tab stop, in points from the left margin.</summary>
public readonly record struct OfficeTabStop(double Position, OfficeTabAlignment Alignment = OfficeTabAlignment.Left);


/// <summary>The paragraph indents a ruler shows, in points — Word's own model.</summary>
/// <param name="Left">From the left margin to the paragraph's left edge (the hanging indent's position).</param>
/// <param name="FirstLine">From <paramref name="Left"/> to the first line's start. Negative is a hanging indent.</param>
/// <param name="Right">From the right margin in to the paragraph's right edge.</param>
public readonly record struct OfficeIndents(double Left = 0, double FirstLine = 0, double Right = 0)
{
    /// <summary>Where the first line starts, from the left margin.</summary>
    public double FirstLineStart => this.Left + this.FirstLine;
}


/// <summary>The handles on a horizontal ruler.</summary>
public enum OfficeRulerMarker
{
    None,

    /// <summary>The downward triangle on top: the first line's start.</summary>
    FirstLineIndent,

    /// <summary>The upward triangle underneath: every line after the first.</summary>
    HangingIndent,

    /// <summary>The box under the hanging triangle: both indents together.</summary>
    LeftIndent,

    /// <summary>The upward triangle on the right: the paragraph's right edge.</summary>
    RightIndent,

    /// <summary>A tab stop.</summary>
    TabStop,

    /// <summary>The boundary between the left margin's shading and the page.</summary>
    LeftMargin,

    /// <summary>The boundary between the page and the right margin's shading.</summary>
    RightMargin
}


/// <summary>One tick on the ruler.</summary>
/// <param name="X">Its position in view pixels.</param>
/// <param name="Height">0–1 of the ruler's height: 1 for a numbered whole unit, less for halves and smaller.</param>
/// <param name="Label">The number drawn at a whole unit, or null.</param>
public readonly record struct OfficeRulerTick(double X, double Height, string? Label);


/// <summary>What a pointer is over on the ruler.</summary>
public readonly record struct OfficeRulerHit(OfficeRulerMarker Marker, int TabIndex = -1);


/// <summary>
/// The horizontal ruler's geometry: where every tick, margin and indent marker is drawn, what a pointer
/// is over, and what dragging a marker does to the indents.
/// </summary>
/// <remarks>
/// <para>
/// Everything is held in points (1/72") — the unit the document model measures in — and converted to
/// view pixels only at the edges, through <see cref="Zoom"/> and <see cref="PixelsPerPoint"/>. The
/// hosts draw from the output and feed pointer positions back in, and never do arithmetic of their own.
/// </para>
/// <para>
/// The indent model is Word's. <see cref="OfficeIndents.Left"/> is where the wrapped lines start,
/// <see cref="OfficeIndents.FirstLine"/> is the first line's offset from it, so dragging the hanging
/// triangle moves the wrapped lines while the first line stays put — which means <c>FirstLine</c>
/// changes by the opposite amount. Dragging the box moves both, so only <c>Left</c> changes.
/// </para>
/// </remarks>
public sealed class OfficeRulerModel
{
    /// <summary>Points per inch.</summary>
    public const double PointsPerInch = 72;

    /// <summary>Points per centimetre.</summary>
    public const double PointsPerCentimeter = 72 / 2.54;

    /// <summary>The narrowest the text column can be squeezed to by dragging, in points.</summary>
    public const double MinimumTextWidth = 36;


    /// <summary>The page's width in points. Default US Letter, 612.</summary>
    public double PageWidth { get; set; } = 612;

    /// <summary>The left margin in points. Default 72 (1").</summary>
    public double LeftMargin { get; set; } = 72;

    /// <summary>The right margin in points. Default 72.</summary>
    public double RightMargin { get; set; } = 72;

    public OfficeIndents Indents { get; set; }

    public IReadOnlyList<OfficeTabStop> TabStops { get; set; } = [];

    public OfficeRulerUnit Unit { get; set; } = OfficeRulerUnit.Inches;

    /// <summary>The editor's zoom factor.</summary>
    public double Zoom { get; set; } = 1;

    /// <summary>View pixels per point at 100%. 96/72 for a screen that treats 1pt as 1/72 of a 96dpi inch.</summary>
    public double PixelsPerPoint { get; set; } = 96d / 72d;

    /// <summary>Where the page's left edge sits in the ruler's own coordinates, in view pixels — the editor's scroll and centring.</summary>
    public double PageOffset { get; set; }

    /// <summary>The distance a drag snaps to, in points. Default 1/16" (inches) or 0.25cm.</summary>
    public double? SnapIncrement { get; set; }

    /// <summary>The text column's width in points: the page less its margins.</summary>
    public double TextWidth => Math.Max(0, this.PageWidth - this.LeftMargin - this.RightMargin);

    double Scale => this.Zoom * this.PixelsPerPoint;

    double UnitPoints => this.Unit == OfficeRulerUnit.Inches ? PointsPerInch : PointsPerCentimeter;

    double Snap => this.SnapIncrement ?? (this.Unit == OfficeRulerUnit.Inches ? PointsPerInch / 16 : PointsPerCentimeter / 4);


    // ---------------------------------------------------------------------------------------------
    // Conversion
    // ---------------------------------------------------------------------------------------------

    /// <summary>View x for a distance in points from the page's left edge.</summary>
    public double PageToView(double points) => this.PageOffset + (points * this.Scale);

    /// <summary>Distance in points from the page's left edge for a view x.</summary>
    public double ViewToPage(double x) => (x - this.PageOffset) / this.Scale;

    /// <summary>View x for a distance in points from the left margin — how indents and tabs are measured.</summary>
    public double MarginToView(double points) => this.PageToView(this.LeftMargin + points);

    /// <summary>Distance in points from the left margin for a view x.</summary>
    public double ViewToMargin(double x) => this.ViewToPage(x) - this.LeftMargin;

    /// <summary>A length in points written in the ruler's unit — <c>1.25"</c>, <c>3.2 cm</c>.</summary>
    public string FormatLength(double points, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        return this.Unit == OfficeRulerUnit.Inches
            ? (points / PointsPerInch).ToString("0.##", culture) + "\""
            : (points / PointsPerCentimeter).ToString("0.##", culture) + " cm";
    }


    // ---------------------------------------------------------------------------------------------
    // Drawing
    // ---------------------------------------------------------------------------------------------

    /// <summary>The left margin's shaded span and the right one's, in view pixels.</summary>
    public (double Start, double End) LeftMarginSpan => (this.PageToView(0), this.PageToView(this.LeftMargin));

    public (double Start, double End) RightMarginSpan
        => (this.PageToView(this.PageWidth - this.RightMargin), this.PageToView(this.PageWidth));

    /// <summary>The white text-column span, in view pixels.</summary>
    public (double Start, double End) TextSpan
        => (this.PageToView(this.LeftMargin), this.PageToView(this.PageWidth - this.RightMargin));

    /// <summary>Each marker's x in view pixels.</summary>
    public double MarkerX(OfficeRulerMarker marker) => marker switch
    {
        OfficeRulerMarker.FirstLineIndent => this.MarginToView(this.Indents.FirstLineStart),
        OfficeRulerMarker.HangingIndent or OfficeRulerMarker.LeftIndent => this.MarginToView(this.Indents.Left),
        OfficeRulerMarker.RightIndent => this.MarginToView(this.TextWidth - this.Indents.Right),
        OfficeRulerMarker.LeftMargin => this.PageToView(this.LeftMargin),
        OfficeRulerMarker.RightMargin => this.PageToView(this.PageWidth - this.RightMargin),
        _ => double.NaN
    };


    /// <summary>
    /// The ticks across the page, numbered from the left margin the way Word numbers them (the margin
    /// is zero, and the shaded margins count up away from it without a sign).
    /// </summary>
    /// <remarks>
    /// Inches get eighths when there is room, quarters when there is less, halves when zoomed far out;
    /// centimetres get halves or nothing. A tick closer than 4px to its neighbour is dropped rather than
    /// drawn as a smear.
    /// </remarks>
    public IReadOnlyList<OfficeRulerTick> Ticks(CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;

        var unit = this.UnitPoints;
        var unitPixels = unit * this.Scale;
        var divisions = this.Unit == OfficeRulerUnit.Inches
            ? unitPixels >= 64 ? 8 : unitPixels >= 32 ? 4 : unitPixels >= 12 ? 2 : 1
            : unitPixels >= 16 ? 2 : 1;

        // Label every unit, or every other one once they crowd.
        var labelEvery = unitPixels >= 18 ? 1 : unitPixels >= 9 ? 2 : 5;

        var step = unit / divisions;
        var ticks = new List<OfficeRulerTick>();

        // Walk out from the left margin both ways so that zero lands exactly on it.
        var before = (int)Math.Floor(this.LeftMargin / step);
        var after = (int)Math.Floor((this.PageWidth - this.LeftMargin) / step);

        for (var i = -before; i <= after; i++)
        {
            if (i == 0)
                continue;   // the margin boundary is drawn as the shading edge, not a tick

            var points = i * step;
            var x = this.MarginToView(points);
            var position = Math.Abs(i) % divisions;

            if (position == 0)
            {
                var number = Math.Abs(i) / divisions;
                var label = number % labelEvery == 0 ? number.ToString(culture) : null;
                ticks.Add(new OfficeRulerTick(x, label is null ? 0.5 : 1, label));
            }
            else if (divisions % 2 == 0 && position == divisions / 2)
                ticks.Add(new OfficeRulerTick(x, 0.5, null));
            else
                ticks.Add(new OfficeRulerTick(x, 0.25, null));
        }

        return ticks;
    }


    // ---------------------------------------------------------------------------------------------
    // Pointer
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// What is under a pointer. <paramref name="y"/> is 0–1 down the ruler's height, because the first
    /// line triangle sits on the top half and the hanging triangle and box on the bottom half — at
    /// zero indent all three are at the same x and only height tells them apart.
    /// </summary>
    public OfficeRulerHit HitTest(double x, double y, double tolerance = 6)
    {
        bool Near(OfficeRulerMarker marker) => Math.Abs(this.MarkerX(marker) - x) <= tolerance;

        if (y < 0.45 && Near(OfficeRulerMarker.FirstLineIndent))
            return new(OfficeRulerMarker.FirstLineIndent);

        if (y >= 0.45 && y < 0.75 && Near(OfficeRulerMarker.HangingIndent))
            return new(OfficeRulerMarker.HangingIndent);

        if (y >= 0.75 && Near(OfficeRulerMarker.LeftIndent))
            return new(OfficeRulerMarker.LeftIndent);

        if (y >= 0.45 && Near(OfficeRulerMarker.RightIndent))
            return new(OfficeRulerMarker.RightIndent);

        for (var i = 0; i < this.TabStops.Count; i++)
        {
            if (Math.Abs(this.MarginToView(this.TabStops[i].Position) - x) <= tolerance)
                return new(OfficeRulerMarker.TabStop, i);
        }

        if (Near(OfficeRulerMarker.LeftMargin))
            return new(OfficeRulerMarker.LeftMargin);

        if (Near(OfficeRulerMarker.RightMargin))
            return new(OfficeRulerMarker.RightMargin);

        return new(OfficeRulerMarker.None);
    }


    /// <summary>Rounds a position in points to the drag grid.</summary>
    public double SnapPoints(double points)
    {
        var snap = this.Snap;
        return snap <= 0 ? points : Math.Round(points / snap) * snap;
    }


    /// <summary>
    /// The indents after dragging <paramref name="marker"/> to view x <paramref name="x"/>, snapped and
    /// held inside the page so the text column never goes narrower than <see cref="MinimumTextWidth"/>.
    /// </summary>
    public OfficeIndents DragIndent(OfficeRulerMarker marker, double x, bool snap = true)
    {
        var at = this.ViewToMargin(x);
        if (snap)
            at = this.SnapPoints(at);

        var indents = this.Indents;
        var width = this.TextWidth;

        // Indents may run out into the margins, but not off the page.
        var minLeft = -this.LeftMargin;
        var maxLeft = width - indents.Right - MinimumTextWidth;

        switch (marker)
        {
            case OfficeRulerMarker.FirstLineIndent:
                var first = Math.Clamp(at, minLeft, maxLeft);
                return indents with { FirstLine = first - indents.Left };

            case OfficeRulerMarker.HangingIndent:
                // The first line stays where it is, so its offset absorbs the move.
                var left = Math.Clamp(at, minLeft, maxLeft);
                return new OfficeIndents(left, indents.FirstLineStart - left, indents.Right);

            case OfficeRulerMarker.LeftIndent:
                // Both move together, and neither may leave the page.
                var lowest = Math.Min(indents.Left, indents.FirstLineStart);
                var highest = Math.Max(indents.Left, indents.FirstLineStart);
                var delta = at - indents.Left;
                delta = Math.Clamp(delta, minLeft - lowest, maxLeft - highest);
                return indents with { Left = indents.Left + delta };

            case OfficeRulerMarker.RightIndent:
                var furthestLeft = Math.Max(indents.Left, indents.FirstLineStart);
                var right = width - at;
                right = Math.Clamp(right, -this.RightMargin, width - furthestLeft - MinimumTextWidth);
                return indents with { Right = right };

            default:
                return indents;
        }
    }


    /// <summary>
    /// The margins after dragging a margin boundary — left or right — to view x, snapped and holding
    /// the text column to at least <see cref="MinimumTextWidth"/>.
    /// </summary>
    public (double Left, double Right) DragMargin(OfficeRulerMarker marker, double x, bool snap = true)
    {
        var at = this.ViewToPage(x);
        if (snap)
            at = this.SnapPoints(at);

        return marker switch
        {
            OfficeRulerMarker.LeftMargin => (
                Math.Clamp(at, 0, this.PageWidth - this.RightMargin - MinimumTextWidth),
                this.RightMargin),
            OfficeRulerMarker.RightMargin => (
                this.LeftMargin,
                Math.Clamp(this.PageWidth - at, 0, this.PageWidth - this.LeftMargin - MinimumTextWidth)),
            _ => (this.LeftMargin, this.RightMargin)
        };
    }


    /// <summary>A tab stop at view x, snapped and inside the text column; null when x is outside it.</summary>
    public OfficeTabStop? TabAt(double x, OfficeTabAlignment alignment = OfficeTabAlignment.Left, bool snap = true)
    {
        var at = this.ViewToMargin(x);
        if (snap)
            at = this.SnapPoints(at);

        if (at <= 0 || at >= this.TextWidth)
            return null;

        return new OfficeTabStop(at, alignment);
    }


    /// <summary>The tab stops with one added, kept in position order, replacing any already at that position.</summary>
    public IReadOnlyList<OfficeTabStop> WithTab(OfficeTabStop tab)
        => this.TabStops
            .Where(x => Math.Abs(x.Position - tab.Position) > 0.5)
            .Append(tab)
            .OrderBy(x => x.Position)
            .ToList();


    /// <summary>
    /// The tab stops after dragging the one at <paramref name="index"/> to view x. Dragged off the
    /// ruler vertically (<paramref name="y"/> outside -0.5–1.5) it is removed, which is how Word deletes one.
    /// </summary>
    public IReadOnlyList<OfficeTabStop> DragTab(int index, double x, double y, bool snap = true)
    {
        if (index < 0 || index >= this.TabStops.Count)
            return this.TabStops;

        var list = this.TabStops.ToList();
        var tab = list[index];
        list.RemoveAt(index);

        if (y < -0.5 || y > 1.5)
            return list;

        var moved = this.TabAt(x, tab.Alignment, snap);
        if (moved is null)
            return list;

        list.Add(moved.Value);
        return list.OrderBy(t => t.Position).ToList();
    }


    /// <summary>The next tab alignment in the ruler's corner selector — Left → Center → Right → Decimal → Left.</summary>
    public static OfficeTabAlignment NextAlignment(OfficeTabAlignment current)
        => current == OfficeTabAlignment.Decimal ? OfficeTabAlignment.Left : current + 1;
}
