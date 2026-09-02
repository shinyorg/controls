using Microsoft.Maui.Graphics.Platform;

namespace Shiny.Maui.Controls.Camera.Media;

/// <summary>
/// Compress / downscale / re-encode for <see cref="MediaService"/>. Deliberately built on
/// <see cref="PlatformImage"/> rather than a platform bitmap API so one implementation covers every head the
/// camera package targets.
/// </summary>
static class MediaImaging
{
    /// <summary>
    /// Re-encode <paramref name="source"/> to <paramref name="format"/> at <paramref name="quality"/> (1–100),
    /// capping the longest edge at <paramref name="maxDimension"/> when that is greater than zero.
    /// </summary>
    public static Task<MediaPhoto?> ProcessAsync(
        Stream source,
        MediaImageFormat format,
        int quality,
        int maxDimension
    ) => Task.Run<MediaPhoto?>(() =>
    {
        var image = PlatformImage.FromStream(source);
        if (image is null)
            return null;

        var working = image;
        if (maxDimension > 0 && (image.Width > maxDimension || image.Height > maxDimension))
            working = image.Downsize(maxDimension, disposeOriginal: true);

        using var ms = new MemoryStream();
        using (var encoded = working.AsStream(ToImageFormat(format), NormalizeQuality(quality)))
            encoded.CopyTo(ms);

        return new MediaPhoto(ms.ToArray(), (int)working.Width, (int)working.Height, ContentTypeFor(format));
    });

    /// <summary>
    /// Re-encode an already-captured <see cref="CameraPhoto"/>. Skips the decode/encode round trip entirely
    /// when nothing was asked for — a JPEG capture at full size and default quality is handed straight
    /// through rather than being re-compressed for no reason.
    /// </summary>
    public static async Task<MediaPhoto> ProcessAsync(
        CameraPhoto photo,
        MediaImageFormat format,
        int quality,
        int maxDimension
    )
    {
        var needsResize = maxDimension > 0 && (photo.Width > maxDimension || photo.Height > maxDimension);
        var needsReencode = format != MediaImageFormat.Jpeg || quality < 100;

        if (!needsResize && !needsReencode)
            return new MediaPhoto(photo.Data, photo.Width, photo.Height, ContentTypeFor(format));

        using var source = photo.OpenRead();
        var processed = await ProcessAsync(source, format, quality, maxDimension).ConfigureAwait(false);

        // a decode failure is not a reason to lose the picture the user just took
        return processed ?? new MediaPhoto(photo.Data, photo.Width, photo.Height, "image/jpeg");
    }

    /// <summary>Map a 1..100 compression percentage onto the 0..1 encoder quality (clamped).</summary>
    internal static float NormalizeQuality(int percent) => Math.Clamp(percent, 1, 100) / 100f;

    internal static ImageFormat ToImageFormat(MediaImageFormat format)
        => format == MediaImageFormat.Png ? ImageFormat.Png : ImageFormat.Jpeg;

    internal static string ContentTypeFor(MediaImageFormat format)
        => format == MediaImageFormat.Png ? "image/png" : "image/jpeg";
}
