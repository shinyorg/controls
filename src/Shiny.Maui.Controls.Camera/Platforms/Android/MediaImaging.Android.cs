using Android.Graphics;
using Android.Media;

namespace Shiny.Maui.Controls.Camera.Media;

// BitmapFactory ignores EXIF orientation and Bitmap.Compress writes no metadata, which together are why a
// resized CameraX capture used to come back sideways: CameraX stores sensor-orientation pixels and says which
// way is up only in the EXIF tag. Orientation is read here and applied with a Matrix; metadata is carried by
// JpegExif, which splices the source's EXIF segment into the output.
static partial class MediaImaging
{
    static Task<MediaPhoto?> TranscodeAsync(byte[] source, MediaImageRequest request) => Task.Run<MediaPhoto?>(() =>
    {
        var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
        BitmapFactory.DecodeByteArray(source, 0, source.Length, bounds);
        if (bounds.OutWidth <= 0 || bounds.OutHeight <= 0)
            return null;

        var orientation = ReadOrientation(source);
        var bake = request.BakesRotation;
        var swap = bake && JpegExif.SwapsDimensions(orientation);
        var (orientedWidth, orientedHeight) = swap ? (bounds.OutHeight, bounds.OutWidth) : (bounds.OutWidth, bounds.OutHeight);
        var (targetWidth, _) = request.Fit(orientedWidth, orientedHeight);
        var scale = (double)targetWidth / orientedWidth;

        // decode at the smallest power-of-two reduction that still covers the target, so a 2048px result
        // from a 50MP original never allocates the full 200MB bitmap
        var sample = 1;
        while (bounds.OutWidth / (sample * 2) >= bounds.OutWidth * scale && bounds.OutHeight / (sample * 2) >= bounds.OutHeight * scale)
            sample *= 2;

        using var decoded = BitmapFactory.DecodeByteArray(source, 0, source.Length, new BitmapFactory.Options { InSampleSize = sample });
        if (decoded is null)
            return null;

        var matrix = new Android.Graphics.Matrix();
        var finalScale = (float)(bounds.OutWidth * scale / decoded.Width);
        matrix.SetScale(finalScale, finalScale);
        if (bake)
            matrix.PostConcat(OrientationMatrix(orientation));

        var transformed = matrix.IsIdentity
            ? decoded
            : Bitmap.CreateBitmap(decoded, 0, 0, decoded.Width, decoded.Height, matrix, true)!;

        try
        {
            var output = request.Format == MediaImageFormat.Jpeg && transformed.HasAlpha
                ? Flatten(transformed)
                : transformed;

            using var ms = new MemoryStream();
            output.Compress(
                request.Format == MediaImageFormat.Png ? Bitmap.CompressFormat.Png! : Bitmap.CompressFormat.Jpeg!,
                Math.Clamp(request.Quality, 1, 100),
                ms
            );
            var width = output.Width;
            var height = output.Height;
            if (!ReferenceEquals(output, transformed))
                output.Dispose();

            var bytes = ms.ToArray();
            if (request.Format == MediaImageFormat.Jpeg)
                bytes = ApplyJpegMetadata(bytes, source, request, orientation, width, height);

            return new MediaPhoto(bytes, width, height, ContentTypeFor(request.Format));
        }
        finally
        {
            if (!ReferenceEquals(transformed, decoded))
                transformed.Dispose();
        }
    });

    static int ReadOrientation(byte[] source)
    {
        if (JpegExif.IsJpeg(source))
            return JpegExif.ReadOrientation(source);

        // HEIF/WebP picks: the platform reader understands those containers from API 24 (HEIF from 28)
        if (!OperatingSystem.IsAndroidVersionAtLeast(24))
            return 1;

        try
        {
            using var stream = new MemoryStream(source);
            var exif = new ExifInterface(stream);
            var value = exif.GetAttributeInt(ExifInterface.TagOrientation, 1);
            return value is >= 1 and <= 8 ? value : 1;
        }
        catch
        {
            return 1;
        }
    }

    // EXIF orientation n → the transform that makes the stored pixels upright
    static Android.Graphics.Matrix OrientationMatrix(int orientation)
    {
        var m = new Android.Graphics.Matrix();
        switch (orientation)
        {
            case 2: m.SetScale(-1, 1); break;
            case 3: m.SetRotate(180); break;
            case 4: m.SetScale(1, -1); break;
            case 5: m.SetRotate(90); m.PostScale(-1, 1); break;
            case 6: m.SetRotate(90); break;
            case 7: m.SetRotate(-90); m.PostScale(-1, 1); break;
            case 8: m.SetRotate(-90); break;
        }
        return m;
    }

    // JPEG has no alpha; without a white ground a transparent PNG picked from the gallery comes out black
    static Bitmap Flatten(Bitmap source)
    {
        var flat = Bitmap.CreateBitmap(source.Width, source.Height, Bitmap.Config.Argb8888!)!;
        using var canvas = new Canvas(flat);
        canvas.DrawColor(Android.Graphics.Color.White);
        canvas.DrawBitmap(source, 0, 0, null);
        return flat;
    }
}
