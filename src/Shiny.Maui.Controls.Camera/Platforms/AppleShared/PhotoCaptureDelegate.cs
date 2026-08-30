using AVFoundation;
using CoreGraphics;
using CoreImage;
using Foundation;
using ImageIO;

namespace Shiny.Maui.Controls.Camera;

// Bridges AVCapturePhotoOutput's delegate callback to a Task<CameraPhoto>. When effects are set, the captured
// still is run through the same Core Image chain as the live preview before encoding, so photos match what the
// user sees. Stays UIKit/AppKit-free (CoreImage + ImageIO) so the one delegate serves iOS and macOS alike.
sealed class PhotoCaptureDelegate : AVCapturePhotoCaptureDelegate
{
    readonly TaskCompletionSource<CameraPhoto> tcs = new();

    public Task<CameraPhoto> Task => this.tcs.Task;

    /// <summary>Set by the handler to the preview's current effect chain; empty = unfiltered (raw capture).</summary>
    public CIFilter[] Filters = [];

    /// <summary>
    /// Compression quality (0-1) for the re-encode below. Only consulted on the filtered path — an
    /// unfiltered capture is returned exactly as AVFoundation encoded it and never passes through ImageIO.
    /// </summary>
    public float JpegQuality = 0.9f;

    public override void DidFinishProcessingPhoto(AVCapturePhotoOutput output, AVCapturePhoto photo, NSError? error)
    {
        if (error != null)
        {
            this.tcs.TrySetException(new InvalidOperationException(error.LocalizedDescription));
            return;
        }

        try
        {
            var result = this.Filters.Length > 0
                ? Filtered(photo, this.Filters, this.JpegQuality)
                : Raw(photo);

            if (result == null)
                this.tcs.TrySetException(new InvalidOperationException("No photo data was produced"));
            else
                this.tcs.TrySetResult(result);
        }
        catch (Exception ex)
        {
            this.tcs.TrySetException(ex);
        }
    }

    static CameraPhoto? Raw(AVCapturePhoto photo)
    {
        using var data = photo.FileDataRepresentation;
        if (data == null)
            return null;

        var dims = photo.ResolvedSettings.PhotoDimensions;
        return new CameraPhoto(data.ToArray(), dims.Width, dims.Height);
    }

    static CameraPhoto? Filtered(AVCapturePhoto photo, CIFilter[] filters, float jpegQuality)
    {
        using var cg = photo.CGImageRepresentation;
        if (cg == null)
            return Raw(photo); // no CGImage to filter — fall back to the unfiltered JPEG

        // CGImageRepresentation is in stored (sensor) orientation; apply the EXIF orientation so the filtered
        // result is upright. This orientation handling is the part most worth verifying on-device.
        using var source = new CIImage(cg);
        using var oriented = source.CreateByApplyingOrientation(ReadOrientation(photo));

        // Intermediates stay alive until the render below has actually evaluated the recipe, then all go.
        var produced = new List<CIImage>(filters.Length);
        try
        {
            var outputImage = AppleCameraFilters.Apply(oriented, filters, produced);
            if (outputImage == null)
                return Raw(photo);

            using var context = new CIContext();
            // the ORIENTED source extent, not the filter's: CIPixellate reports an infinite extent and
            // CIGaussianBlur a grown one, and a captured photo should match the frame that was captured
            using var outCg = context.CreateCGImage(outputImage, oriented.Extent);
            if (outCg == null)
                return Raw(photo);

            var bytes = EncodeJpeg(outCg, jpegQuality);
            return bytes == null ? Raw(photo) : new CameraPhoto(bytes, (int)outCg.Width, (int)outCg.Height);
        }
        finally
        {
            foreach (var image in produced)
                image.Dispose();
        }
    }

    static CGImagePropertyOrientation ReadOrientation(AVCapturePhoto photo)
    {
        // CGImageRepresentation drops orientation; read the EXIF orientation (1..8) from the JPEG instead
        using var data = photo.FileDataRepresentation;
        if (data == null)
            return CGImagePropertyOrientation.Up;

        using var source = CGImageSource.FromData(data);
        var props = source?.CopyProperties((NSDictionary?)null, 0);
        if (props?[(NSString)"Orientation"] is NSNumber n)
            return (CGImagePropertyOrientation)n.Int32Value;
        return CGImagePropertyOrientation.Up;
    }

    static byte[]? EncodeJpeg(CGImage image, float quality)
    {
        using var data = new NSMutableData();
        using var dest = CGImageDestination.Create(data, "public.jpeg", 1);
        if (dest == null)
            return null;

        // Without this ImageIO picks its own default, which is both unspecified and not especially high -
        // so a filtered photo used to come back visibly softer than the same capture unfiltered, for no
        // reason the caller could see or control.
        dest.AddImage(image, new CGImageDestinationOptions { LossyCompressionQuality = quality });
        return dest.Close() ? data.ToArray() : null;
    }
}
