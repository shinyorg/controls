using Microsoft.Maui.Graphics;
using Shiny.Controls.Camera;

namespace Shiny.Maui.Controls.Analysis.Ai;

/// <summary>
/// Platform bridge for the two native steps the AI scanner needs: cheap per-frame document <b>detection</b>
/// and, only when a document is confirmed and the scan is armed, one-shot JPEG <b>encoding</b> of that frame.
/// Detection is implemented natively where it pays off (Apple Vision document segmentation) and falls back to
/// the managed <see cref="ManagedDocumentEdgeDetector"/> elsewhere; encoding is always native (Core Image /
/// Android YUV→JPEG / Windows BitmapEncoder). On bare net10.0 there are no real camera frames, so encoding is
/// a no-op and the analyzer stays inert.
/// </summary>
sealed partial class DocumentImageExtractor
{
    /// <summary>
    /// Detect the document outline in the frame (run every frame — must be cheap). Returns <c>null</c> when no
    /// document is present. Corners are normalized (0..1) in upright, mirror-corrected image space.
    /// </summary>
    public partial DocumentQuad? Detect(VideoFrame frame);

    /// <summary>
    /// Encode the document region of the frame to JPEG bytes — called at most once per detected document, while
    /// the live frame is still valid, just before the AI call. <paramref name="cropUpright"/> is the (already
    /// padded) region to keep, in upright normalized space; implementations orient the buffer, crop to it and
    /// JPEG-encode. Returns <c>null</c> when encoding isn't supported on the platform (bare net10.0).
    /// </summary>
    public partial byte[]? Encode(VideoFrame frame, RectF cropUpright);
}
