using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Shiny.Maui.Controls.Camera.Media;

// BitmapDecoder can apply the EXIF orientation itself (ExifOrientationMode) and scales during the decode;
// BitmapEncoder writes no metadata on a fresh encode, so the JPEG path splices the source EXIF back in via
// JpegExif, the same as Android.
static partial class MediaImaging
{
    static async Task<MediaPhoto?> TranscodeAsync(byte[] source, MediaImageRequest request)
    {
        try
        {
            using var input = new InMemoryRandomAccessStream();
            await input.WriteAsync(source.AsBuffer());
            input.Seek(0);

            var decoder = await BitmapDecoder.CreateAsync(input);
            var orientation = await ReadOrientationAsync(decoder, source);

            var bake = request.BakesRotation;
            var swap = bake && JpegExif.SwapsDimensions(orientation);
            var storedWidth = (int)decoder.PixelWidth;
            var storedHeight = (int)decoder.PixelHeight;
            var (orientedWidth, orientedHeight) = swap ? (storedHeight, storedWidth) : (storedWidth, storedHeight);
            var (targetWidth, targetHeight) = request.Fit(orientedWidth, orientedHeight);

            // the transform scales in stored orientation; the EXIF rotation is applied after it
            var (scaledWidth, scaledHeight) = swap ? (targetHeight, targetWidth) : (targetWidth, targetHeight);
            var transform = new BitmapTransform
            {
                ScaledWidth = (uint)scaledWidth,
                ScaledHeight = (uint)scaledHeight,
                InterpolationMode = BitmapInterpolationMode.Fant
            };

            var alpha = request.Format == MediaImageFormat.Png ? BitmapAlphaMode.Premultiplied : BitmapAlphaMode.Ignore;
            var pixels = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                alpha,
                transform,
                bake ? ExifOrientationMode.RespectExifOrientation : ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.ColorManageToSRgb
            );

            using var output = new InMemoryRandomAccessStream();
            BitmapEncoder encoder;
            if (request.Format == MediaImageFormat.Png)
            {
                encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
            }
            else
            {
                var settings = new BitmapPropertySet
                {
                    ["ImageQuality"] = new BitmapTypedValue(NormalizeQuality(request.Quality), Windows.Foundation.PropertyType.Single)
                };
                encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, output, settings);
            }

            encoder.SetPixelData(BitmapPixelFormat.Bgra8, alpha, (uint)targetWidth, (uint)targetHeight, 96, 96, pixels.DetachPixelData());
            await encoder.FlushAsync();

            var bytes = new byte[output.Size];
            output.Seek(0);
            await output.ReadAsync(bytes.AsBuffer(), (uint)bytes.Length, InputStreamOptions.None);

            if (request.Format == MediaImageFormat.Jpeg)
                bytes = ApplyJpegMetadata(bytes, source, request, orientation, targetWidth, targetHeight);

            return new MediaPhoto(bytes, targetWidth, targetHeight, ContentTypeFor(request.Format));
        }
        catch (Exception ex) when (ex is COMException or ArgumentException)
        {
            return null; // not an image the platform codecs understand
        }
    }

    static async Task<int> ReadOrientationAsync(BitmapDecoder decoder, byte[] source)
    {
        if (JpegExif.IsJpeg(source))
            return JpegExif.ReadOrientation(source);

        try
        {
            var props = await decoder.BitmapProperties.GetPropertiesAsync(["System.Photo.Orientation"]);
            if (props.TryGetValue("System.Photo.Orientation", out var value) && value.Value is ushort o && o is >= 1 and <= 8)
                return o;
        }
        catch
        {
            // the container has no property handler for it — treat as upright
        }
        return 1;
    }
}
