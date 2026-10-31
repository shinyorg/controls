using Foundation;
using Microsoft.Maui.Graphics;
using Shiny.Controls.Camera;
using Vision;

namespace Shiny.Maui.Controls.Camera.Barcode;

/// <summary>Barcode scanning via Apple Vision (iOS, MacCatalyst, macOS).</summary>
public partial class BarcodeScanner
{
    public partial Task<List<DetectedBarcode>> ScanAsync(CameraFrame frame, CancellationToken ct)
    {
        if (frame is not AppleCameraFrame apple)
            return Task.FromResult(new List<DetectedBarcode>());

        var cg = apple.ToCGImage();
        if (cg == null)
            return Task.FromResult(new List<DetectedBarcode>());

        var tcs = new TaskCompletionSource<List<DetectedBarcode>>();
        var request = new VNDetectBarcodesRequest((req, err) =>
        {
            var list = new List<DetectedBarcode>();
            if (err == null && req.GetResults<VNBarcodeObservation>() is { } results)
            {
                foreach (var obs in results)
                {
                    var payload = obs.PayloadStringValue;
                    if (String.IsNullOrEmpty(payload))
                        continue;

                    var bb = obs.BoundingBox; // normalized, origin bottom-left
                    var raw = new RectF((float)bb.X, (float)(1 - bb.Y - bb.Height), (float)bb.Width, (float)bb.Height);
                    var box = CoordinateTransform.ApplyOrientation(raw, frame.Rotation, frame.IsMirrored);
                    list.Add(new DetectedBarcode(payload, Map(obs.Symbology), box));
                }
            }
            tcs.TrySetResult(list);
            cg.Dispose();
        });

        try
        {
            var symbologies = ToSymbologies(this.Formats);
            if (symbologies != null)
                request.Symbologies = symbologies;

            // Restrict Vision to the scan window so it only decodes that band — fewer pixels to process
            // and it physically can't pick up codes outside it. RegionOfInterest is normalized in the
            // (unoriented) image's space with a BOTTOM-LEFT origin, so invert our upright window back to
            // raw sensor space and flip Y.
            if (this.ScanWindow is { } window)
            {
                var raw = CoordinateTransform.InvertOrientation(window, frame.Rotation, frame.IsMirrored);
                var x = Math.Clamp(raw.X, 0f, 1f);
                var y = Math.Clamp(1f - raw.Y - raw.Height, 0f, 1f);
                var w = Math.Clamp(raw.Width, 0f, 1f - x);
                var h = Math.Clamp(raw.Height, 0f, 1f - y);
                if (w > 0 && h > 0)
                    request.RegionOfInterest = new CoreGraphics.CGRect(x, y, w, h);
            }

            using var handler = new VNImageRequestHandler(cg, new NSDictionary());
            handler.Perform([request], out _);
        }
        catch (Exception ex)
        {
            cg.Dispose();
            tcs.TrySetException(ex);
        }

        return tcs.Task;
    }

    public partial Task<List<DetectedBarcode>> ScanLuminanceAsync(byte[] luminance, int width, int height, CancellationToken ct)
    {
        if (width <= 0 || height <= 0 || luminance.Length < width * height)
            return Task.FromResult(new List<DetectedBarcode>());

        var list = new List<DetectedBarcode>();
        using var provider = new CoreGraphics.CGDataProvider(luminance, 0, width * height);
        using var gray = CoreGraphics.CGColorSpace.CreateDeviceGray();
        using var cg = new CoreGraphics.CGImage(width, height, 8, 8, width, gray, CoreGraphics.CGBitmapFlags.None, provider, null, false, CoreGraphics.CGColorRenderingIntent.Default);

        var request = new VNDetectBarcodesRequest((req, err) =>
        {
            if (err != null || req.GetResults<VNBarcodeObservation>() is not { } results)
                return;
            foreach (var obs in results)
            {
                var payload = obs.PayloadStringValue;
                if (String.IsNullOrEmpty(payload))
                    continue;
                var bb = obs.BoundingBox; // normalized, origin bottom-left
                list.Add(new DetectedBarcode(payload, Map(obs.Symbology), new RectF((float)bb.X, (float)(1 - bb.Y - bb.Height), (float)bb.Width, (float)bb.Height)));
            }
        });

        var symbologies = ToSymbologies(this.Formats);
        if (symbologies != null)
            request.Symbologies = symbologies;

        // Perform is synchronous: the completion handler has run by the time it returns
        using var handler = new VNImageRequestHandler(cg, new NSDictionary());
        handler.Perform([request], out _);
        return Task.FromResult(list);
    }

    static BarcodeFormat Map(VNBarcodeSymbology symbology) => symbology switch
    {
        VNBarcodeSymbology.QR => BarcodeFormat.QrCode,
        VNBarcodeSymbology.Aztec => BarcodeFormat.Aztec,
        VNBarcodeSymbology.DataMatrix => BarcodeFormat.DataMatrix,
        VNBarcodeSymbology.Pdf417 => BarcodeFormat.Pdf417,
        VNBarcodeSymbology.Code128 => BarcodeFormat.Code128,
        VNBarcodeSymbology.Code39 or VNBarcodeSymbology.Code39Checksum
            or VNBarcodeSymbology.Code39FullAscii or VNBarcodeSymbology.Code39FullAsciiChecksum => BarcodeFormat.Code39,
        VNBarcodeSymbology.Code93 or VNBarcodeSymbology.Code93i => BarcodeFormat.Code93,
        VNBarcodeSymbology.Codabar => BarcodeFormat.Codabar,
        VNBarcodeSymbology.Ean8 => BarcodeFormat.Ean8,
        VNBarcodeSymbology.Ean13 => BarcodeFormat.Ean13,
        VNBarcodeSymbology.Upce => BarcodeFormat.UpcE,
        VNBarcodeSymbology.I2OF5 or VNBarcodeSymbology.I2OF5Checksum or VNBarcodeSymbology.Itf14 => BarcodeFormat.Itf,
        _ => BarcodeFormat.Unknown
    };

    static VNBarcodeSymbology[]? ToSymbologies(IList<BarcodeFormat>? formats)
    {
        if (formats == null || formats.Count == 0)
            return null;

        var set = new List<VNBarcodeSymbology>();
        foreach (var f in formats)
        {
            switch (f)
            {
                case BarcodeFormat.QrCode: set.Add(VNBarcodeSymbology.QR); break;
                case BarcodeFormat.Aztec: set.Add(VNBarcodeSymbology.Aztec); break;
                case BarcodeFormat.DataMatrix: set.Add(VNBarcodeSymbology.DataMatrix); break;
                case BarcodeFormat.Pdf417: set.Add(VNBarcodeSymbology.Pdf417); break;
                case BarcodeFormat.Code128: set.Add(VNBarcodeSymbology.Code128); break;
                case BarcodeFormat.Code39: set.Add(VNBarcodeSymbology.Code39); break;
                case BarcodeFormat.Code93: set.Add(VNBarcodeSymbology.Code93); break;
                case BarcodeFormat.Codabar: set.Add(VNBarcodeSymbology.Codabar); break;
                case BarcodeFormat.Ean8: set.Add(VNBarcodeSymbology.Ean8); break;
                // Vision reports UPC-A as EAN-13 with a leading zero, so both map through EAN-13.
                case BarcodeFormat.Ean13 or BarcodeFormat.UpcA: set.Add(VNBarcodeSymbology.Ean13); break;
                case BarcodeFormat.UpcE: set.Add(VNBarcodeSymbology.Upce); break;
                case BarcodeFormat.Itf: set.Add(VNBarcodeSymbology.I2OF5); break;
            }
        }
        return set.Count == 0 ? null : set.Distinct().ToArray();
    }
}
