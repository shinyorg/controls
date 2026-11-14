using Android.Graphics;
using AImage = Android.Media.Image;

namespace Shiny.Maui.Controls.Analysis;

/// <summary>
/// The Android base for every <see cref="VideoFrame"/>, whichever control produced it. Analyzers downcast to this —
/// never to a subclass — and take the cheapest form the frame has.
/// </summary>
/// <remarks>
/// <para>
/// A camera frame (<c>AndroidCameraFrame</c>) wraps a CameraX <c>ImageProxy</c> and has a <see cref="MediaImage"/>,
/// which ML Kit reads without a copy (<c>InputImage.FromMediaImage(image, frame.Rotation)</c>). A
/// <c>MediaElement</c> frame is a GPU readback with no <see cref="MediaImage"/>; use <see cref="ToBitmap"/>
/// (<c>InputImage.FromBitmap(bitmap, frame.Rotation)</c>). An analyzer that writes the <c>MediaImage</c> path only
/// silently finds nothing on video, so handle both:
/// </para>
/// <code>
/// var input = android.MediaImage is { } image
///     ? InputImage.FromMediaImage(image, frame.Rotation)
///     : InputImage.FromBitmap(android.ToBitmap(), frame.Rotation);
/// </code>
/// </remarks>
public abstract class AndroidVideoFrame : VideoFrame
{
    Bitmap? bitmap;

    /// <summary>The frame as an <c>android.media.Image</c>, when it is backed by one (camera frames); otherwise <c>null</c>.</summary>
    public abstract AImage? MediaImage { get; }

    /// <summary>
    /// The frame as an ARGB_8888 bitmap in native (un-rotated) orientation. Built on first call, cached for the
    /// frame's lifetime and recycled with it — do not recycle it yourself or keep it past the analysis pass.
    /// </summary>
    public Bitmap ToBitmap() => this.bitmap ??= this.CreateBitmap();

    /// <summary>Build the bitmap behind <see cref="ToBitmap"/>. Called at most once per frame.</summary>
    protected abstract Bitmap CreateBitmap();

    /// <summary>The frame as NV21 bytes (Y plane then interleaved VU), for <c>YuvImage</c> and JPEG encoding.</summary>
    public abstract byte[] ToNv21();

    /// <inheritdoc/>
    protected override void ReleaseNative()
    {
        if (this.bitmap is { } b)
        {
            this.bitmap = null;
            b.Recycle();
            b.Dispose();
        }
        this.ReleaseFrame();
    }

    /// <summary>Free the subclass's own native resources (close the image proxy, …).</summary>
    protected virtual void ReleaseFrame() { }
}
