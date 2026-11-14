using Microsoft.Maui.Graphics;

namespace Shiny.Controls.Analysis;

/// <summary>
/// A detected document outline — four corners in normalized upright image space (0..1, origin top-left,
/// already corrected for sensor rotation and mirroring). Corner labels are best-effort; consumers that only
/// need a box use <see cref="Bounds"/>.
/// </summary>
public record DocumentQuad(PointF TopLeft, PointF TopRight, PointF BottomRight, PointF BottomLeft)
{
    /// <summary>The axis-aligned bounding rectangle of the quad (normalized upright image space).</summary>
    public RectF Bounds
    {
        get
        {
            var minX = MathF.Min(MathF.Min(this.TopLeft.X, this.TopRight.X), MathF.Min(this.BottomRight.X, this.BottomLeft.X));
            var minY = MathF.Min(MathF.Min(this.TopLeft.Y, this.TopRight.Y), MathF.Min(this.BottomRight.Y, this.BottomLeft.Y));
            var maxX = MathF.Max(MathF.Max(this.TopLeft.X, this.TopRight.X), MathF.Max(this.BottomRight.X, this.BottomLeft.X));
            var maxY = MathF.Max(MathF.Max(this.TopLeft.Y, this.TopRight.Y), MathF.Max(this.BottomRight.Y, this.BottomLeft.Y));
            return new RectF(minX, minY, maxX - minX, maxY - minY);
        }
    }
}

/// <summary>
/// The OCR result for the document path: the recognized text blocks plus the detected document outline. When
/// <see cref="Quad"/> is non-null the frame was deskewed (perspective-corrected) before recognition, so the
/// blocks are in flat document space and far more accurate; when it's null no document was found and the
/// recognizer fell back to whole-frame OCR (blocks in upright frame space).
/// </summary>
public record RecognizedDocument(IReadOnlyList<RecognizedText> Blocks, DocumentQuad? Quad);
