using CoreGraphics;
using Foundation;
using ImageIO;

namespace Shiny.Maui.Controls.Camera.Media;

// ImageIO does all of it natively: CGImageSource reads JPEG and HEIC (what a gallery pick usually is) with
// their full property set, the thumbnail API decodes straight to the target size with the orientation
// applied or not, and CGImageDestination writes the properties back out — EXIF, GPS, TIFF and the rest.
static partial class MediaImaging
{
    static readonly NSString OrientationKey = new("Orientation");
    static readonly NSString PixelWidthKey = new("PixelWidth");
    static readonly NSString PixelHeightKey = new("PixelHeight");
    static readonly NSString TiffKey = new("{TIFF}");
    static readonly NSString ExifKey = new("{Exif}");
    static readonly NSString ExifPixelXKey = new("PixelXDimension");
    static readonly NSString ExifPixelYKey = new("PixelYDimension");
    static readonly NSString LossyQualityKey = new("kCGImageDestinationLossyCompressionQuality");

    static Task<MediaPhoto?> TranscodeAsync(byte[] source, MediaImageRequest request) => Task.Run<MediaPhoto?>(() =>
    {
        using var data = NSData.FromArray(source);
        using var src = CGImageSource.FromData(data);
        if (src is null || src.ImageCount < 1)
            return null;

        using var props = src.CopyProperties((NSDictionary?)null, 0);
        var orientation = props?[OrientationKey] is NSNumber o ? o.Int32Value : 1;
        if (orientation is < 1 or > 8)
            orientation = 1;

        var storedWidth = props?[PixelWidthKey] is NSNumber pw ? pw.Int32Value : 0;
        var storedHeight = props?[PixelHeightKey] is NSNumber ph ? ph.Int32Value : 0;

        var bake = request.BakesRotation;
        var swap = bake && JpegExif.SwapsDimensions(orientation);
        var (orientedWidth, orientedHeight) = swap ? (storedHeight, storedWidth) : (storedWidth, storedHeight);
        var (targetWidth, targetHeight) = request.Fit(orientedWidth, orientedHeight);
        var maxPixel = Math.Max(targetWidth, targetHeight);

        using var image = src.CreateThumbnail(0, new CGImageThumbnailOptions
        {
            CreateThumbnailFromImageAlways = true,
            CreateThumbnailWithTransform = bake,
            ShouldCacheImmediately = true,
            MaxPixelSize = maxPixel > 0 ? maxPixel : null
        });
        if (image is null)
            return null;

        var width = (int)image.Width;
        var height = (int)image.Height;

        var metadata = BuildMetadata(props, request, bake ? 1 : orientation, width, height);
        if (request.Format == MediaImageFormat.Jpeg)
            metadata[LossyQualityKey] = NSNumber.FromFloat(NormalizeQuality(request.Quality));

        using var output = new NSMutableData();
        using var dest = CGImageDestination.Create(output, request.Format == MediaImageFormat.Png ? "public.png" : "public.jpeg", 1);
        if (dest is null)
            return null;

        dest.AddImage(image, metadata);
        if (!dest.Close())
            return null;

        return new MediaPhoto(output.ToArray(), width, height, ContentTypeFor(request.Format));
    });

    static NSMutableDictionary BuildMetadata(NSDictionary? source, MediaImageRequest request, int orientation, int width, int height)
    {
        if (!request.PreserveMetadata || source is null)
        {
            // dropping metadata, but a picture left in stored orientation still needs the one tag that says
            // which way is up
            var minimal = new NSMutableDictionary();
            if (orientation != 1)
                minimal[OrientationKey] = NSNumber.FromInt32(orientation);
            return minimal;
        }

        var metadata = new NSMutableDictionary(source);

        // the encoder sets these from the image it is given; carrying the source's would describe a picture
        // that no longer exists
        metadata.Remove(PixelWidthKey);
        metadata.Remove(PixelHeightKey);
        metadata[OrientationKey] = NSNumber.FromInt32(orientation);

        if (source[TiffKey] is NSDictionary tiff)
        {
            var copy = new NSMutableDictionary(tiff);
            copy[OrientationKey] = NSNumber.FromInt32(orientation);
            metadata[TiffKey] = copy;
        }

        if (source[ExifKey] is NSDictionary exif)
        {
            var copy = new NSMutableDictionary(exif);
            copy[ExifPixelXKey] = NSNumber.FromInt32(width);
            copy[ExifPixelYKey] = NSNumber.FromInt32(height);
            metadata[ExifKey] = copy;
        }

        return metadata;
    }
}
