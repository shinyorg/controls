using Microsoft.Maui.Graphics;
using Shiny.Controls.Camera;

namespace Shiny.Maui.Controls.Analysis.Ai;

// Bare net10.0 has no real camera frames, but the managed edge detector still works off any luminance plane
// (useful for tests). There's no image encoder here, so Encode returns null and the analyzer stays inert.
partial class DocumentImageExtractor
{
    public partial DocumentQuad? Detect(VideoFrame frame) => ManagedDocumentEdgeDetector.Detect(frame);

    public partial byte[]? Encode(VideoFrame frame, RectF cropUpright) => null;
}
