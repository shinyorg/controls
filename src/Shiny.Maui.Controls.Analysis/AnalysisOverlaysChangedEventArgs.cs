namespace Shiny.Maui.Controls.Analysis;

/// <summary>The analyzer's current overlay boxes, its scan window and the size of the frames they were found in.</summary>
public class AnalysisOverlaysChangedEventArgs(IReadOnlyList<OverlayBox> overlays, RectF? scanWindow, int imageWidth, int imageHeight) : EventArgs
{
    /// <summary>The boxes to draw (normalized, upright image space). Empty when nothing is seen.</summary>
    public IReadOnlyList<OverlayBox> Overlays { get; } = overlays;

    /// <summary>The analyzer's scan window (normalized, upright image space), or null for the whole frame.</summary>
    public RectF? ScanWindow { get; } = scanWindow;

    /// <summary>Upright width of the analyzed frame, in pixels.</summary>
    public int ImageWidth { get; } = imageWidth;

    /// <summary>Upright height of the analyzed frame, in pixels.</summary>
    public int ImageHeight { get; } = imageHeight;
}
