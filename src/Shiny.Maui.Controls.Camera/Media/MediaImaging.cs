#if !(ANDROID || IOS || MACCATALYST || MACOS || WINDOWS)
using Microsoft.Maui.Graphics.Platform;
#endif

namespace Shiny.Maui.Controls.Camera.Media;

/// <summary>
/// Everything <see cref="MediaService"/> was asked to do to a photo, with the service defaults already folded in.
/// </summary>
/// <param name="Format">The output encoding.</param>
/// <param name="Quality">Compression rate, 1–100. Ignored for PNG.</param>
/// <param name="MaxWidth">Width limit in pixels; 0 for none.</param>
/// <param name="MaxHeight">Height limit in pixels; 0 for none.</param>
/// <param name="RotateImage">Bake the EXIF orientation into the pixels.</param>
/// <param name="PreserveMetadata">Carry the source's EXIF/GPS/TIFF metadata into the output.</param>
record MediaImageRequest(
    MediaImageFormat Format,
    int Quality,
    int MaxWidth,
    int MaxHeight,
    bool RotateImage,
    bool PreserveMetadata
)
{
    /// <summary>
    /// PNG output always has the rotation baked in. PNG orientation metadata is something almost nothing
    /// honours, so a PNG left in stored orientation would be sideways everywhere it was shown.
    /// </summary>
    public bool BakesRotation => this.RotateImage || this.Format == MediaImageFormat.Png;

    /// <summary>
    /// Fold <c>MaxDimension</c> (longest edge) into a width and height limit. Each limit is the tightest one
    /// set; 0 means unconstrained.
    /// </summary>
    public static (int Width, int Height) ResolveLimits(int maxDimension, int maxWidth, int maxHeight)
        => (Tightest(maxDimension, maxWidth), Tightest(maxDimension, maxHeight));

    static int Tightest(int a, int b)
    {
        a = Math.Max(0, a);
        b = Math.Max(0, b);
        if (a == 0) return b;
        if (b == 0) return a;
        return Math.Min(a, b);
    }

    /// <summary>
    /// The size a <paramref name="width"/> × <paramref name="height"/> picture is drawn at to fit inside the
    /// limits, aspect ratio kept. Never upscales.
    /// </summary>
    public (int Width, int Height) Fit(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return (width, height);

        var scale = 1d;
        if (this.MaxWidth > 0 && width > this.MaxWidth)
            scale = Math.Min(scale, (double)this.MaxWidth / width);
        if (this.MaxHeight > 0 && height > this.MaxHeight)
            scale = Math.Min(scale, (double)this.MaxHeight / height);

        if (scale >= 1d)
            return (width, height);

        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }
}


/// <summary>
/// Compress / downscale / rotate / re-encode for <see cref="MediaService"/>, natively per platform: ImageIO on
/// Apple, <c>BitmapFactory</c> on Android, <c>BitmapDecoder</c> on Windows.
/// </summary>
/// <remarks>
/// This used to be one implementation over <c>PlatformImage</c>. That discarded every byte of EXIF, so on
/// Android — where CameraX hands back sensor-orientation pixels and an EXIF orientation tag — any photo that
/// was resized or recompressed (which, at the default 92% quality, was nearly all of them) came back sideways
/// with nothing left in the file to say so.
/// </remarks>
static partial class MediaImaging
{
    /// <summary>
    /// Process a picked file. Always decoded, never passed through: the dimensions on <see cref="MediaPhoto"/>
    /// come from the decode. Null when the data cannot be decoded at all.
    /// </summary>
    public static async Task<MediaPhoto?> ProcessAsync(Stream source, MediaImageRequest request)
    {
        using var ms = new MemoryStream();
        await source.CopyToAsync(ms).ConfigureAwait(false);
        return await TranscodeAsync(ms.ToArray(), request).ConfigureAwait(false);
    }

    /// <summary>
    /// Re-encode an already-captured <see cref="CameraPhoto"/>. A decode failure hands back the capture as it
    /// was rather than losing the picture the user just took.
    /// </summary>
    public static async Task<MediaPhoto> ProcessAsync(CameraPhoto photo, MediaImageRequest request)
    {
        if (CanPassThrough(photo.Data, request, photo.Width, photo.Height))
            return new MediaPhoto(photo.Data, photo.Width, photo.Height, "image/jpeg");

        var processed = await TranscodeAsync(photo.Data, request).ConfigureAwait(false);
        return processed ?? new MediaPhoto(photo.Data, photo.Width, photo.Height, "image/jpeg");
    }

    /// <summary>
    /// Whether the source bytes already are the answer: a JPEG wanted as a JPEG at full quality, inside the
    /// size limits, keeping its metadata, and either already upright or allowed to stay as stored. Re-encoding
    /// that would only cost quality and time.
    /// </summary>
    internal static bool CanPassThrough(byte[] source, MediaImageRequest request, int? width, int? height)
    {
        if (request.Format != MediaImageFormat.Jpeg || request.Quality < 100 || !request.PreserveMetadata)
            return false;

        if (!JpegExif.IsJpeg(source))
            return false;

        if (request.MaxWidth > 0 || request.MaxHeight > 0)
        {
            // without known dimensions there is no telling whether it fits
            if (width is not { } w || height is not { } h)
                return false;

            if (request.Fit(w, h) != (w, h) || request.Fit(h, w) != (h, w))
                return false;
        }

        return !request.RotateImage || JpegExif.ReadOrientation(source) == 1;
    }

    /// <summary>
    /// The JPEG metadata step shared by the platforms whose encoders write none (Android, Windows). Copies the
    /// source EXIF across with the stale fields corrected, or — when metadata is being dropped but the pixels
    /// were left as stored — writes just the orientation so the picture is still the right way up.
    /// </summary>
    internal static byte[] ApplyJpegMetadata(byte[] encoded, byte[] source, MediaImageRequest request, int sourceOrientation, int width, int height)
    {
        var written = request.BakesRotation ? 1 : sourceOrientation;

        if (request.PreserveMetadata && JpegExif.ExtractExifSegment(source) is { } segment)
        {
            JpegExif.Normalize(segment, written, width, height);
            return JpegExif.InsertSegment(encoded, segment);
        }

        return written == 1
            ? encoded
            : JpegExif.InsertSegment(encoded, JpegExif.CreateOrientationSegment(written));
    }

    /// <summary>Map a 1..100 compression percentage onto the 0..1 encoder quality (clamped).</summary>
    internal static float NormalizeQuality(int percent) => Math.Clamp(percent, 1, 100) / 100f;

    internal static string ContentTypeFor(MediaImageFormat format)
        => format == MediaImageFormat.Png ? "image/png" : "image/jpeg";

#if !(ANDROID || IOS || MACCATALYST || MACOS || WINDOWS)
    // Bare net10.0 (and the GTK head): no native codec to reach for. Best effort through PlatformImage,
    // which neither rotates nor keeps metadata.
    static Task<MediaPhoto?> TranscodeAsync(byte[] source, MediaImageRequest request) => Task.Run<MediaPhoto?>(() =>
    {
        try
        {
            using var input = new MemoryStream(source);
            var image = PlatformImage.FromStream(input);
            if (image is null)
                return null;

            var (w, h) = request.Fit((int)image.Width, (int)image.Height);
            var working = (w, h) == ((int)image.Width, (int)image.Height)
                ? image
                : image.Downsize(Math.Max(w, h), disposeOriginal: true);

            using var ms = new MemoryStream();
            var format = request.Format == MediaImageFormat.Png ? ImageFormat.Png : ImageFormat.Jpeg;
            using (var encoded = working.AsStream(format, NormalizeQuality(request.Quality)))
                encoded.CopyTo(ms);

            return new MediaPhoto(ms.ToArray(), (int)working.Width, (int)working.Height, ContentTypeFor(request.Format));
        }
        catch (NotImplementedException)
        {
            return null;
        }
    });
#endif
}
