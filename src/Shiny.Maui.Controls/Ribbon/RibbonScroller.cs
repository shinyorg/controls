namespace Shiny.Maui.Controls.Ribbons;

/// <summary>
/// The horizontal scroller each tab's groups sit in. It sizes itself to its content's height when the
/// platform doesn't: the macOS AppKit backend's ScrollView handler has no desired-size measure of its own
/// (it only measures the content while arranging), so a horizontal ScrollView reports a height of 0 -
/// and the ribbon's body row, which is Auto, collapsed to nothing.
/// </summary>
internal sealed class RibbonScroller : ScrollView
{
    protected override Size MeasureOverride(double widthConstraint, double heightConstraint)
    {
        var size = base.MeasureOverride(widthConstraint, heightConstraint);
        if (size.Height > 0 || this.Content is not IView content)
            return size;

        var measured = content.Measure(double.PositiveInfinity, heightConstraint);
        var width = Double.IsInfinity(widthConstraint) ? measured.Width : Math.Min(measured.Width, widthConstraint);
        var height = Double.IsInfinity(heightConstraint) ? measured.Height : Math.Min(measured.Height, heightConstraint);
        return new Size(Math.Max(size.Width, width), height);
    }
}
