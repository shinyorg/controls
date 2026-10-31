using Microsoft.Maui.Graphics;
using Shiny.Controls.Camera;
using ZXing;
using ZXing.Common;
using ZxFormat = ZXing.BarcodeFormat;
using BarcodeFormat = Shiny.Controls.Camera.BarcodeFormat;

namespace Shiny.Maui.Controls.Camera.Barcode;

/// <summary>
/// Barcode scanning via the managed ZXing.Net decoder — Windows (there is no camera-based native scanner) and
/// the bare net10.0 target. Decodes the frame's luminance plane, so it works on any <see cref="CameraFrame"/>.
/// </summary>
public partial class BarcodeScanner
{
    // every symbology the named BarcodeFormat values cover — the same set Vision / ML Kit read when Formats is null
    static readonly ZxFormat[] AllFormats =
    [
        ZxFormat.QR_CODE, ZxFormat.AZTEC, ZxFormat.DATA_MATRIX, ZxFormat.PDF_417,
        ZxFormat.CODE_128, ZxFormat.CODE_39, ZxFormat.CODE_93, ZxFormat.CODABAR,
        ZxFormat.EAN_8, ZxFormat.EAN_13, ZxFormat.UPC_A, ZxFormat.UPC_E, ZxFormat.ITF
    ];

    public partial Task<List<DetectedBarcode>> ScanAsync(CameraFrame frame, CancellationToken ct)
    {
        int w = frame.Width, h = frame.Height;
        if (w <= 0 || h <= 0)
            return Task.FromResult(new List<DetectedBarcode>());

        // the luminance plane is cached on the frame; copy it out because the frame is only valid until the
        // analyzer pass returns and the decode runs off the caller's thread
        var lum = frame.GetLuminance().ToArray();
        if (lum.Length < w * h)
            return Task.FromResult(new List<DetectedBarcode>());

        var formats = ToZxFormats(this.Formats);
        var window = this.ScanWindow;
        int rotation = frame.Rotation;
        var mirrored = frame.IsMirrored;

        // ZXing is pure CPU work — keep it off the camera/UI thread
        return Task.Run(() => Decode(lum, w, h, rotation, mirrored, window, formats), ct);
    }

    public partial Task<List<DetectedBarcode>> ScanLuminanceAsync(byte[] luminance, int width, int height, CancellationToken ct)
    {
        if (width <= 0 || height <= 0 || luminance.Length < width * height)
            return Task.FromResult(new List<DetectedBarcode>());

        // an unrotated, unmirrored image with no window is exactly what Decode takes
        var formats = ToZxFormats(this.Formats);
        return Task.Run(() => Decode(luminance, width, height, 0, false, null, formats), ct);
    }

    static List<DetectedBarcode> Decode(byte[] lum, int w, int h, int rotation, bool mirrored, RectF? window, ZxFormat[] formats)
    {
        // ZXing has no region-of-interest either, but PlanarYUVLuminanceSource crops the Y plane in place, so the
        // scan window costs nothing and it physically can't see codes outside it
        int left = 0, top = 0, cw = w, ch = h;
        if (window is { } win)
        {
            var raw = CoordinateTransform.InvertOrientation(win, rotation, mirrored);
            var x0 = Math.Clamp((int)MathF.Round(raw.X * w), 0, w - 1);
            var y0 = Math.Clamp((int)MathF.Round(raw.Y * h), 0, h - 1);
            var x1 = Math.Clamp((int)MathF.Round((raw.X + raw.Width) * w), x0 + 1, w);
            var y1 = Math.Clamp((int)MathF.Round((raw.Y + raw.Height) * h), y0 + 1, h);
            if (x1 - x0 >= 16 && y1 - y0 >= 16) // too small / degenerate — scan the whole frame
            {
                left = x0;
                top = y0;
                cw = x1 - x0;
                ch = y1 - y0;
            }
        }

        var source = new PlanarYUVLuminanceSource(lum, w, h, left, top, cw, ch, false);
        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true, // 1D codes held vertically
            Options = new DecodingOptions
            {
                PossibleFormats = formats,
                TryHarder = true,
                TryInverted = true // light-on-dark codes, e.g. a QR shown in a dark-mode app
            }
        };

        var results = reader.DecodeMultiple(source);
        var list = new List<DetectedBarcode>();
        if (results == null)
            return list;

        foreach (var result in results)
        {
            if (String.IsNullOrEmpty(result.Text) || result.ResultPoints is not { Length: > 0 } points)
                continue;

            var raw = Bounds(points, left, top, w, h);
            var box = CoordinateTransform.ApplyOrientation(raw, rotation, mirrored);
            list.Add(new DetectedBarcode(result.Text, Map(result.BarcodeFormat), box));
        }
        return list;
    }

    // ZXing reports finder/guard points rather than a box, relative to the crop. Union them, map back into the
    // full frame, and normalize. A 1D read is two points on one scan line — give it a height so it still draws.
    static RectF Bounds(ResultPoint[] points, int left, int top, int w, int h)
    {
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var p in points)
        {
            if (p == null)
                continue;
            minX = Math.Min(minX, p.X);
            minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X);
            maxY = Math.Max(maxY, p.Y);
        }

        var bw = maxX - minX;
        var bh = maxY - minY;
        var minSide = Math.Max(bw, bh) * 0.25f;
        if (bh < minSide)
        {
            minY -= (minSide - bh) / 2;
            bh = minSide;
        }
        if (bw < minSide)
        {
            minX -= (minSide - bw) / 2;
            bw = minSide;
        }

        var x = Math.Clamp((left + minX) / w, 0f, 1f);
        var y = Math.Clamp((top + minY) / h, 0f, 1f);
        return new RectF(x, y, Math.Clamp(bw / w, 0f, 1f - x), Math.Clamp(bh / h, 0f, 1f - y));
    }

    static BarcodeFormat Map(ZxFormat format) => format switch
    {
        ZxFormat.QR_CODE => BarcodeFormat.QrCode,
        ZxFormat.AZTEC => BarcodeFormat.Aztec,
        ZxFormat.DATA_MATRIX => BarcodeFormat.DataMatrix,
        ZxFormat.PDF_417 => BarcodeFormat.Pdf417,
        ZxFormat.CODE_128 => BarcodeFormat.Code128,
        ZxFormat.CODE_39 => BarcodeFormat.Code39,
        ZxFormat.CODE_93 => BarcodeFormat.Code93,
        ZxFormat.CODABAR => BarcodeFormat.Codabar,
        ZxFormat.EAN_8 => BarcodeFormat.Ean8,
        ZxFormat.EAN_13 => BarcodeFormat.Ean13,
        ZxFormat.UPC_A => BarcodeFormat.UpcA,
        ZxFormat.UPC_E => BarcodeFormat.UpcE,
        ZxFormat.ITF => BarcodeFormat.Itf,
        _ => BarcodeFormat.Unknown
    };

    static ZxFormat[] ToZxFormats(IList<BarcodeFormat>? formats)
    {
        if (formats == null || formats.Count == 0)
            return AllFormats;

        var set = new List<ZxFormat>();
        foreach (var f in formats)
        {
            ZxFormat? zx = f switch
            {
                BarcodeFormat.QrCode => ZxFormat.QR_CODE,
                BarcodeFormat.Aztec => ZxFormat.AZTEC,
                BarcodeFormat.DataMatrix => ZxFormat.DATA_MATRIX,
                BarcodeFormat.Pdf417 => ZxFormat.PDF_417,
                BarcodeFormat.Code128 => ZxFormat.CODE_128,
                BarcodeFormat.Code39 => ZxFormat.CODE_39,
                BarcodeFormat.Code93 => ZxFormat.CODE_93,
                BarcodeFormat.Codabar => ZxFormat.CODABAR,
                BarcodeFormat.Ean8 => ZxFormat.EAN_8,
                BarcodeFormat.Ean13 => ZxFormat.EAN_13,
                BarcodeFormat.UpcA => ZxFormat.UPC_A,
                BarcodeFormat.UpcE => ZxFormat.UPC_E,
                BarcodeFormat.Itf => ZxFormat.ITF,
                _ => null
            };
            if (zx is { } value && !set.Contains(value))
                set.Add(value);
        }
        return set.Count == 0 ? AllFormats : set.ToArray();
    }
}
