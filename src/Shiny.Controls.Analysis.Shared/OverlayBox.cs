using Microsoft.Maui.Graphics;

namespace Shiny.Controls.Analysis;

/// <summary>
/// A styled bounding box an <see cref="IFrameAnalyzer"/> asks the camera or media overlay to draw. Geometry is
/// in <b>normalized, upright image space</b> (0..1 on each axis, origin top-left, already corrected for
/// sensor rotation and front-camera mirroring); the overlay maps it into view space.
/// </summary>
/// <remarks>
/// An analyzer returns the set of boxes it currently "sees" from <see cref="IFrameAnalyzer.AnalyzeAsync"/>.
/// The pipeline keeps the last set per analyzer, so boxes persist across dropped/slow frames until the
/// analyzer returns a different set (replace) or <c>null</c> (clear). A <c>null</c> color/width falls
/// back to the overlay's own default, so a minimal analyzer can return <c>new OverlayBox(rect)</c>.
/// </remarks>
/// <param name="Rect">Normalized bounds (0..1) of the box in upright image space.</param>
/// <param name="StrokeColor">Box outline color; <c>null</c> uses the overlay default.</param>
/// <param name="Text">Optional caption drawn with the box (e.g. a decoded value or label).</param>
/// <param name="TextColor">Caption color; <c>null</c> uses the overlay default.</param>
/// <param name="FillColor">Optional translucent fill drawn inside the box.</param>
/// <param name="StrokeWidth">Box outline width; <c>0</c> uses the overlay default.</param>
public record OverlayBox(
    RectF Rect,
    Color? StrokeColor = null,
    string? Text = null,
    Color? TextColor = null,
    Color? FillColor = null,
    float StrokeWidth = 0f
);
