using CoreGraphics;
using CoreImage;
using CoreVideo;

namespace Shiny.Maui.Controls.Camera;

/// <summary>
/// A reusable surface for handing an analyzer the frame as the preview draws it.
/// </summary>
/// <remarks>
/// <para>
/// One buffer, reused for the life of the delegate and reallocated only when the frame size
/// changes. That is safe because the pipeline analyses one frame at a time - <c>WantsFrame</c>
/// refuses a frame while a pass is in flight - so the buffer handed out is never the one being
/// rendered into.
/// </para>
/// <para>
/// BGRA because that is what everything downstream of <see cref="AppleCameraFrame"/> expects, and
/// IOSurface-backed so Core Image can render into it on the GPU rather than reading it back.
/// </para>
/// </remarks>
sealed class FilteredFrameBuffer : IDisposable
{
    CVPixelBuffer? buffer;
    nint width;
    nint height;

    /// <summary>Draws a filtered image into the scratch buffer, or null if it could not be made.</summary>
    public CVPixelBuffer? Render(CIContext context, CIImage image, CGRect extent)
    {
        var w = (nint)extent.Width;
        var h = (nint)extent.Height;

        if (w <= 0 || h <= 0)
            return null;

        if (this.buffer is null || this.width != w || this.height != h)
        {
            this.buffer?.Dispose();
            this.buffer = new CVPixelBuffer(
                w,
                h,
                CVPixelFormatType.CV32BGRA,
                new CVPixelBufferAttributes { PixelFormatType = CVPixelFormatType.CV32BGRA }
            );

            this.width = w;
            this.height = h;
        }

        if (this.buffer is null)
            return null;

        // Rendered at the source extent for the same reason the preview is - see RenderFiltered.
        context.Render(image, this.buffer, extent, null);
        return this.buffer;
    }

    public void Dispose()
    {
        this.buffer?.Dispose();
        this.buffer = null;
    }
}
