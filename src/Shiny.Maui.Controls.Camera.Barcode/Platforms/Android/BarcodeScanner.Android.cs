using Android.Runtime;
using Microsoft.Maui.Graphics;
using Shiny.Controls.Camera;
using Xamarin.Google.MLKit.Vision.BarCode;
using Xamarin.Google.MLKit.Vision.Common;
using MlBarcode = Xamarin.Google.MLKit.Vision.Barcode.Common.Barcode;

namespace Shiny.Maui.Controls.Camera.Barcode;

/// <summary>Barcode scanning via Android MLKit.</summary>
public partial class BarcodeScanner
{
    IBarcodeScanner? client;
    IList<BarcodeFormat>? clientFormats;

    IBarcodeScanner GetClient()
    {
        // ML Kit bakes the formats into the client, so a Formats assigned after the first scan needs a new one
        if (this.client != null && ReferenceEquals(this.clientFormats, this.Formats))
            return this.client;

        this.ReleasePlatform();
        this.clientFormats = this.Formats;
        var formats = ToMlFormats(this.Formats);
        var builder = new BarcodeScannerOptions.Builder();
        if (formats == null)
            builder.SetBarcodeFormats(MlBarcode.FormatAllFormats);
        else
            builder.SetBarcodeFormats(formats[0], formats.Skip(1).ToArray());

        this.client = BarcodeScanning.GetClient(builder.Build());
        return this.client;
    }

    // ML Kit detector clients hold native model/detector memory until Close() — dropping the reference is not
    // enough, and the managed peer alone is too small to ever pressure the GC into finalizing it.
    partial void ReleasePlatform()
    {
        var old = Interlocked.Exchange(ref this.client, null);
        if (old is null)
            return;

        try
        {
            old.Close();
        }
        catch
        {
            // already closed / torn down with the process — nothing left to release
        }
        (old as IDisposable)?.Dispose();
    }

    public async partial Task<List<DetectedBarcode>> ScanAsync(CameraFrame frame, CancellationToken ct)
    {
        if (frame is not AndroidCameraFrame android)
            return [];

        var rotation = android.Proxy.ImageInfo.RotationDegrees;

        // MLKit has no region-of-interest, so to honor ScanWindow we crop the Y (luminance) plane to the band
        // and scan only that grayscale crop — far fewer pixels (faster) and it physically can't see codes
        // outside the window. `imgUpW/H` is the upright pixel size of whatever we feed MLKit; `roi` is the
        // window in MLKit's upright (un-mirrored) space so we can map cropped boxes back to the full frame.
        InputImage input;
        Android.Graphics.Bitmap? crop = null;
        int imgUpW, imgUpH;
        RectF roi;

        if (this.ScanWindow is { } window && TryCropToWindow(frame, rotation, window, out crop, out roi))
        {
            input = InputImage.FromBitmap(crop, rotation);
            imgUpW = rotation is 90 or 270 ? crop!.Height : crop!.Width;
            imgUpH = rotation is 90 or 270 ? crop!.Width : crop!.Height;
        }
        else
        {
            var mediaImage = android.Proxy.Image;
            if (mediaImage == null)
                return [];
            input = InputImage.FromMediaImage(mediaImage, rotation);
            imgUpW = rotation is 90 or 270 ? frame.Height : frame.Width;
            imgUpH = rotation is 90 or 270 ? frame.Width : frame.Height;
            roi = new RectF(0, 0, 1, 1);
        }

        try
        {
            var result = await GmsTaskAwaiter.AwaitAsync(this.GetClient().Process(input)).ConfigureAwait(false);

            var list = new List<DetectedBarcode>();
            if (result is JavaList items)
            {
                foreach (var item in items)
                {
                    if (item is not MlBarcode bc)
                        continue;

                    var value = bc.RawValue ?? bc.DisplayValue;
                    if (String.IsNullOrEmpty(value))
                        continue;

                    var r = bc.BoundingBox;
                    if (r == null)
                        continue;

                    // box -> normalized within the (cropped) upright image -> mapped into the full frame's
                    // MLKit upright space via the ROI -> mirror-corrected for the OverlayBox contract
                    var nx = roi.X + (float)r.Left / imgUpW * roi.Width;
                    var ny = roi.Y + (float)r.Top / imgUpH * roi.Height;
                    var nw = (float)r.Width() / imgUpW * roi.Width;
                    var nh = (float)r.Height() / imgUpH * roi.Height;
                    var box = CoordinateTransform.ApplyOrientation(new RectF(nx, ny, nw, nh), 0, frame.IsMirrored);
                    list.Add(new DetectedBarcode(value, Map(bc.Format), box));
                }
            }
            return list;
        }
        finally
        {
            crop?.Recycle();
        }
    }

    // Build a grayscale bitmap of just the scan window from the sensor Y plane. `roi` returns the window in
    // MLKit's upright (un-mirrored) space for mapping results back. False = scan the whole frame instead.
    static bool TryCropToWindow(CameraFrame frame, int rotation, RectF window, out Android.Graphics.Bitmap? crop, out RectF roi)
    {
        crop = null;
        // MLKit's upright space isn't mirror-corrected; our window is — un-mirror it for the result mapping
        roi = frame.IsMirrored
            ? new RectF(1f - window.X - window.Width, window.Y, window.Width, window.Height)
            : window;

        // window -> raw sensor space (top-left normalized) -> sensor pixels
        var raw = CoordinateTransform.InvertOrientation(window, rotation, frame.IsMirrored);
        int sw = frame.Width, sh = frame.Height;
        var x0 = Math.Clamp((int)MathF.Round(raw.X * sw), 0, sw - 1);
        var y0 = Math.Clamp((int)MathF.Round(raw.Y * sh), 0, sh - 1);
        var cw = Math.Clamp((int)MathF.Round(raw.Width * sw), 1, sw - x0);
        var ch = Math.Clamp((int)MathF.Round(raw.Height * sh), 1, sh - y0);
        if (cw < 16 || ch < 16) // too small / degenerate — just scan the whole frame
            return false;

        var lum = frame.GetLuminance();
        if (lum.Length < sw * sh)
            return false;

        var pixels = new int[cw * ch];
        for (var y = 0; y < ch; y++)
        {
            var src = (y0 + y) * sw + x0;
            var dst = y * cw;
            for (var x = 0; x < cw; x++)
            {
                int v = lum[src + x] & 0xFF;
                pixels[dst + x] = unchecked((int)0xFF000000) | (v << 16) | (v << 8) | v;
            }
        }
        crop = Android.Graphics.Bitmap.CreateBitmap(pixels, cw, ch, Android.Graphics.Bitmap.Config.Argb8888!);
        return true;
    }

    static BarcodeFormat Map(int format) => format switch
    {
        MlBarcode.FormatQrCode => BarcodeFormat.QrCode,
        MlBarcode.FormatAztec => BarcodeFormat.Aztec,
        MlBarcode.FormatDataMatrix => BarcodeFormat.DataMatrix,
        MlBarcode.FormatPdf417 => BarcodeFormat.Pdf417,
        MlBarcode.FormatCode128 => BarcodeFormat.Code128,
        MlBarcode.FormatCode39 => BarcodeFormat.Code39,
        MlBarcode.FormatCode93 => BarcodeFormat.Code93,
        MlBarcode.FormatCodabar => BarcodeFormat.Codabar,
        MlBarcode.FormatEan8 => BarcodeFormat.Ean8,
        MlBarcode.FormatEan13 => BarcodeFormat.Ean13,
        MlBarcode.FormatUpcA => BarcodeFormat.UpcA,
        MlBarcode.FormatUpcE => BarcodeFormat.UpcE,
        MlBarcode.FormatItf => BarcodeFormat.Itf,
        _ => BarcodeFormat.Unknown
    };

    static int[]? ToMlFormats(IList<BarcodeFormat>? formats)
    {
        if (formats == null || formats.Count == 0)
            return null;

        var set = new List<int>();
        foreach (var f in formats)
        {
            var ml = f switch
            {
                BarcodeFormat.QrCode => MlBarcode.FormatQrCode,
                BarcodeFormat.Aztec => MlBarcode.FormatAztec,
                BarcodeFormat.DataMatrix => MlBarcode.FormatDataMatrix,
                BarcodeFormat.Pdf417 => MlBarcode.FormatPdf417,
                BarcodeFormat.Code128 => MlBarcode.FormatCode128,
                BarcodeFormat.Code39 => MlBarcode.FormatCode39,
                BarcodeFormat.Code93 => MlBarcode.FormatCode93,
                BarcodeFormat.Codabar => MlBarcode.FormatCodabar,
                BarcodeFormat.Ean8 => MlBarcode.FormatEan8,
                BarcodeFormat.Ean13 => MlBarcode.FormatEan13,
                BarcodeFormat.UpcA => MlBarcode.FormatUpcA,
                BarcodeFormat.UpcE => MlBarcode.FormatUpcE,
                BarcodeFormat.Itf => MlBarcode.FormatItf,
                _ => 0
            };
            if (ml != 0)
                set.Add(ml);
        }
        return set.Count == 0 ? null : set.Distinct().ToArray();
    }
}
