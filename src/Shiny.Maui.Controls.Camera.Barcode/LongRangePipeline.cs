using Microsoft.Maui.Graphics;
using Shiny.Controls.Camera;
using System.Diagnostics;
using Shiny.Controls.BarcodeReader;
using CameraFormat = Shiny.Controls.Camera.BarcodeFormat;

namespace Shiny.Maui.Controls.Camera.Barcode;

/// <summary>
/// <see cref="BarcodeScanMode.LongRange"/>: locate, decode 1D in managed code, send enlarged crops of the rest
/// to the native scanner, track across frames. Owned by one <see cref="BarcodeAnalyzer"/>; the camera
/// pipeline runs one pass at a time, so nothing here is locked.
/// </summary>
sealed class LongRangePipeline
{
    readonly BarcodeReaderEngine engine = new();
    readonly Stopwatch clock = Stopwatch.StartNew();
    long frameIndex;

    public BarcodeTracker Tracker { get; } = new();
    public AutoZoomController Zoom { get; } = new();

    public void SetFormats(IList<CameraFormat>? formats) => this.engine.Formats = FormatSet.Of(formats?.Select(ToSymbology));

    public void Reset() => this.Tracker.Clear();

    public sealed record Result(List<DetectedBarcode> Confirmed, List<RectF> Unread, TimeSpan Now, float Aspect);

    public async Task<Result> AnalyzeAsync(CameraFrame frame, BarcodeScanner native, RectF? scanWindow, int fullFrameInterval, int maxCrops, CancellationToken ct)
    {
        var now = this.clock.Elapsed;
        var index = ++this.frameIndex;
        int w = frame.Width, h = frame.Height;
        var rotation = frame.Rotation;
        var mirrored = frame.IsMirrored;
        var crops = new List<(TrackedBarcode Track, byte[] Pixels, int Width, int Height, CropPlan Plan)>();

        // Everything that touches the luminance plane happens here, synchronously: the engine borrows the
        // plane in place, and the crops for the native scanner are copied out before the first await.
        {
            var image = new LumaImage(frame.GetLuminance(), w, h);
            var candidates = this.engine.Scan(image, WindowInPixels(scanWindow, w, h, rotation, mirrored));

            var observations = new TrackObservation[candidates.Count];
            for (var i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                var read = c.Read;
                var box = Upright(read?.Box.Bounds ?? c.Box.Bounds, w, h, rotation, mirrored);
                observations[i] = new TrackObservation(box, c.Kind, read?.Text, read?.Symbology ?? Symbology.Unknown, read?.IsChecksummed ?? false, c.ElementWidth);
            }

            var tracks = this.Tracker.Update(observations, now);
            for (var i = 0; i < tracks.Length; i++)
                tracks[i].Tag = candidates[i];

            if (maxCrops > 0)
            {
                foreach (var t in this.Tracker.SelectForNative(maxCrops, now, TimeSpan.FromMilliseconds(150)))
                {
                    if (t.Tag is not BarcodeCandidate c)
                        continue;
                    // a 2D candidate is only worth a native pass if a 2D symbology was asked for
                    if (c.Kind == CandidateKind.Matrix && !this.engine.Formats.HasAnyNativeOnly)
                        continue;

                    var plan = BarcodeReaderEngine.PlanCrop(c, w, h);
                    var pixels = image.CropScaled(plan.Source, plan.Scale, out var cw, out var ch);
                    t.LastNativeAttempt = now;
                    crops.Add((t, pixels, cw, ch, plan));
                }
            }
        }

        foreach (var crop in crops)
        {
            var codes = await native.ScanLuminanceAsync(crop.Pixels, crop.Width, crop.Height, ct).ConfigureAwait(false);
            foreach (var code in codes)
            {
                // crop-normalized -> source pixels -> upright normalized
                var src = crop.Plan.Source;
                var px = new BoxF(
                    src.X + code.BoundingBox.X * src.Width,
                    src.Y + code.BoundingBox.Y * src.Height,
                    code.BoundingBox.Width * src.Width,
                    code.BoundingBox.Height * src.Height);
                this.Tracker.AddReading(crop.Track, code.Value, ToSymbology(code.Format), true, Upright(px, w, h, rotation, mirrored));
            }
        }

        if (fullFrameInterval > 0 && index % fullFrameInterval == 0)
        {
            native.ScanWindow = scanWindow;
            var codes = await native.ScanAsync(frame, ct).ConfigureAwait(false);
            if (codes.Count > 0)
            {
                var obs = new TrackObservation[codes.Count];
                for (var i = 0; i < codes.Count; i++)
                {
                    var b = codes[i].BoundingBox;
                    obs[i] = new TrackObservation(new BoxF(b.X, b.Y, b.Width, b.Height), CandidateKind.Matrix, codes[i].Value, ToSymbology(codes[i].Format), true);
                }
                this.Tracker.Update(obs, now);
            }
        }

        var confirmed = new List<DetectedBarcode>();
        var unread = new List<RectF>();
        foreach (var t in this.Tracker.Tracks)
        {
            var rect = new RectF(t.Box.X, t.Box.Y, t.Box.Width, t.Box.Height);
            if (t.IsConfirmed)
                confirmed.Add(new DetectedBarcode(t.Text!, ToFormat(t.Symbology), rect));
            else if (t.LastSeen == now)
                unread.Add(rect);
        }

        return new Result(confirmed, unread, now, rotation is 90 or 270 ? (float)h / w : (float)w / h);
    }

    static BoxF? WindowInPixels(RectF? window, int w, int h, int rotation, bool mirrored)
    {
        if (window is not { } win)
            return null;
        var raw = CoordinateTransform.InvertOrientation(win, rotation, mirrored);
        return new BoxF(raw.X * w, raw.Y * h, raw.Width * w, raw.Height * h);
    }

    static BoxF Upright(BoxF px, int w, int h, int rotation, bool mirrored)
    {
        var clamped = px.Clamp(w, h);
        var r = CoordinateTransform.ApplyOrientation(new RectF(clamped.X / w, clamped.Y / h, clamped.Width / w, clamped.Height / h), rotation, mirrored);
        return new BoxF(r.X, r.Y, r.Width, r.Height);
    }

    static Symbology ToSymbology(CameraFormat format) => format switch
    {
        CameraFormat.QrCode => Symbology.QrCode,
        CameraFormat.Aztec => Symbology.Aztec,
        CameraFormat.DataMatrix => Symbology.DataMatrix,
        CameraFormat.Pdf417 => Symbology.Pdf417,
        CameraFormat.Code128 => Symbology.Code128,
        CameraFormat.Code39 => Symbology.Code39,
        CameraFormat.Code93 => Symbology.Code93,
        CameraFormat.Codabar => Symbology.Codabar,
        CameraFormat.Ean8 => Symbology.Ean8,
        CameraFormat.Ean13 => Symbology.Ean13,
        CameraFormat.UpcA => Symbology.UpcA,
        CameraFormat.UpcE => Symbology.UpcE,
        CameraFormat.Itf => Symbology.Itf,
        _ => Symbology.Unknown
    };

    static CameraFormat ToFormat(Symbology symbology) => symbology switch
    {
        Symbology.QrCode => CameraFormat.QrCode,
        Symbology.Aztec => CameraFormat.Aztec,
        Symbology.DataMatrix => CameraFormat.DataMatrix,
        Symbology.Pdf417 => CameraFormat.Pdf417,
        Symbology.Code128 => CameraFormat.Code128,
        Symbology.Code39 => CameraFormat.Code39,
        Symbology.Code93 => CameraFormat.Code93,
        Symbology.Codabar => CameraFormat.Codabar,
        Symbology.Ean8 => CameraFormat.Ean8,
        Symbology.Ean13 => CameraFormat.Ean13,
        Symbology.UpcA => CameraFormat.UpcA,
        Symbology.UpcE => CameraFormat.UpcE,
        Symbology.Itf => CameraFormat.Itf,
        _ => CameraFormat.Unknown
    };
}
