using Microsoft.Maui.Graphics;
using Shiny.Controls.Camera;
using Xamarin.Google.MLKit.Vision.Common;
using Xamarin.Google.MLKit.Vision.Text;
using Xamarin.Google.MLKit.Vision.Text.Latin;

namespace Shiny.Maui.Controls.Camera.Ocr;

public partial class TextRecognizer
{
    // Created lazily (and closed by ReleaseNativeResources) rather than eagerly per instance: an ML Kit client
    // holds native model memory until Close(), so an analyzer that was constructed but never attached, or has
    // been detached, must not keep one alive.
    Xamarin.Google.MLKit.Vision.Text.ITextRecognizer? client;

    Xamarin.Google.MLKit.Vision.Text.ITextRecognizer Client()
        => this.client ??= TextRecognition.GetClient(TextRecognizerOptions.DefaultOptions);

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

    // Document path: detect the document quad in managed code (no OpenCV), deskew it natively with
    // Matrix.SetPolyToPoly (a true 4-point perspective warp), then OCR the flat crop. Falls back to shared
    // whole-frame OCR when no plausible document is found.
    private async partial Task<RecognizedDocument> RecognizeDocumentCoreAsync(CameraFrame frame, CancellationToken ct)
    {
        if (frame is not AndroidCameraFrame)
            return new RecognizedDocument([], null);

        int w = frame.Width, h = frame.Height;
        var lum = frame.GetLuminance().ToArray();

        if (!ManagedDocumentDetector.TryDetect(lum, w, h, out var tl, out var tr, out var br, out var bl))
            return new RecognizedDocument(await this.RecognizeAsync(frame, ct).ConfigureAwait(false), null);

        // Re-assign the corners to TL/TR/BR/BL by their *upright* position so the deskewed crop comes out
        // upright (the sensor buffer may be rotated 90/270). src points stay in sensor pixels for the warp.
        var sensor = new[] { tl, tr, br, bl };
        var up = new PointF[4];
        for (var i = 0; i < 4; i++)
            up[i] = CoordinateTransform.ApplyOrientation(sensor[i], frame.Rotation, frame.IsMirrored);

        int iTL = 0, iTR = 0, iBR = 0, iBL = 0;
        float minSum = float.MaxValue, maxSum = float.MinValue, minDiff = float.MaxValue, maxDiff = float.MinValue;
        for (var i = 0; i < 4; i++)
        {
            float s = up[i].X + up[i].Y, d = up[i].X - up[i].Y;
            if (s < minSum) { minSum = s; iTL = i; }
            if (s > maxSum) { maxSum = s; iBR = i; }
            if (d > maxDiff) { maxDiff = d; iTR = i; }
            if (d < minDiff) { minDiff = d; iBL = i; }
        }

        int uw = frame.Rotation is 90 or 270 ? h : w;
        int uh = frame.Rotation is 90 or 270 ? w : h;
        var dstW = Math.Clamp((int)(Math.Max(Len(up[iTL], up[iTR]), Len(up[iBL], up[iBR])) * uw), 64, uw);
        var dstH = Math.Clamp((int)(Math.Max(Len(up[iTL], up[iBL]), Len(up[iTR], up[iBR])) * uh), 64, uh);

        var src = new[]
        {
            sensor[iTL].X * w, sensor[iTL].Y * h, sensor[iTR].X * w, sensor[iTR].Y * h,
            sensor[iBR].X * w, sensor[iBR].Y * h, sensor[iBL].X * w, sensor[iBL].Y * h
        };
        var dst = new float[] { 0, 0, dstW, 0, dstW, dstH, 0, dstH };

        using var srcBitmap = LuminanceToBitmap(lum, w, h);
        using var matrix = new Android.Graphics.Matrix();
        matrix.SetPolyToPoly(src, 0, dst, 0, 4);

        using var warped = Android.Graphics.Bitmap.CreateBitmap(dstW, dstH, Android.Graphics.Bitmap.Config.Argb8888!);
        using (var canvas = new Android.Graphics.Canvas(warped))
        using (var paint = new Android.Graphics.Paint { FilterBitmap = true })
            canvas.DrawBitmap(srcBitmap, matrix, paint);

        var input = InputImage.FromBitmap(warped, 0);
        var result = await GmsTaskAwaiter.AwaitAsync(this.Client().Process(input)).ConfigureAwait(false);

        var blocks = new List<RecognizedText>();
        if (result is Text text)
        {
            foreach (var block in text.TextBlocks)
            {
                foreach (var line in block.Lines)
                {
                    var r = line.BoundingBox;
                    if (r == null)
                        continue;
                    // boxes are already in upright document space
                    var box = new RectF((float)r.Left / dstW, (float)r.Top / dstH, (float)r.Width() / dstW, (float)r.Height() / dstH);
                    blocks.Add(new RecognizedText(line.Text ?? string.Empty, box));
                }
            }
        }

        var quad = new DocumentQuad(up[iTL], up[iTR], up[iBR], up[iBL]);
        return new RecognizedDocument(blocks, quad);
    }

    static float Len(PointF a, PointF b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    // Region OCR. MLKit has no region-of-interest and no minimum-text-height, so the crop is done here: build
    // the sensor-space luminance bitmap once, then let Bitmap.CreateBitmap crop, orient and upscale it in a
    // single filtered pass. Recognizing that crop is what makes small text (a plate, a distant sign) legible —
    // MLKit downscales its input, so text that is a large fraction of a small image survives where the same
    // text in a full frame does not.
    async Task<List<RecognizedText>> RecognizeRegionAsync(CameraFrame frame, RectF roi, TextRecognitionOptions options)
    {
        int w = frame.Width, h = frame.Height;
        var lum = frame.GetLuminance().ToArray();

        // the ROI is in upright space; the luminance plane is raw sensor space
        var clamped = Clamp(roi);
        var sensor = CoordinateTransform.InvertOrientation(clamped, frame.Rotation, frame.IsMirrored);

        var x = Math.Clamp((int)MathF.Floor(sensor.X * w), 0, w - 1);
        var y = Math.Clamp((int)MathF.Floor(sensor.Y * h), 0, h - 1);
        var cw = Math.Clamp((int)MathF.Ceiling(sensor.Width * w), 1, w - x);
        var ch = Math.Clamp((int)MathF.Ceiling(sensor.Height * h), 1, h - y);

        using var srcBitmap = LuminanceToBitmap(lum, w, h);

        // sensor -> upright is rotate-then-mirror (the same order CoordinateTransform.ApplyOrientation uses),
        // then the upscale. CreateBitmap re-normalizes the result's origin, so the crop comes out upright.
        using var matrix = new Android.Graphics.Matrix();
        matrix.PostRotate(frame.Rotation);
        if (frame.IsMirrored)
            matrix.PostScale(-1f, 1f);

        // upright crop height in pixels, before scaling — that is what MinimumInputHeight is measured against
        var uprightH = frame.Rotation is 90 or 270 ? cw : ch;
        if (options.MinimumInputHeight > 0 && uprightH < options.MinimumInputHeight)
        {
            var scale = (float)options.MinimumInputHeight / uprightH;
            matrix.PostScale(scale, scale);
        }

        using var crop = Android.Graphics.Bitmap.CreateBitmap(srcBitmap, x, y, cw, ch, matrix, true);
        if (crop == null)
            return [];

        var input = InputImage.FromBitmap(crop, 0);
        var result = await GmsTaskAwaiter.AwaitAsync(this.Client().Process(input)).ConfigureAwait(false);

        var blocks = new List<RecognizedText>();
        if (result is Text text)
        {
            var lines = text.TextBlocks
                .SelectMany(b => b.Lines)
                .Where(l => l.BoundingBox != null);

            foreach (var line in lines)
            {
                var r = line.BoundingBox!;
                // the crop is already upright, so its own space maps linearly onto the upright ROI
                var box = new RectF(
                    clamped.X + (float)r.Left / crop.Width * clamped.Width,
                    clamped.Y + (float)r.Top / crop.Height * clamped.Height,
                    (float)r.Width() / crop.Width * clamped.Width,
                    (float)r.Height() / crop.Height * clamped.Height);
                blocks.Add(new RecognizedText(line.Text ?? string.Empty, box));
            }
        }
        return blocks;
    }


    static RectF Clamp(RectF r)
    {
        var x = Math.Clamp(r.X, 0f, 1f);
        var y = Math.Clamp(r.Y, 0f, 1f);
        return new RectF(x, y, Math.Clamp(r.Width, 0.001f, 1f - x), Math.Clamp(r.Height, 0.001f, 1f - y));
    }


    static Android.Graphics.Bitmap LuminanceToBitmap(byte[] lum, int w, int h)
    {
        var pixels = new int[w * h];
        for (var i = 0; i < pixels.Length; i++)
        {
            int v = lum[i];
            pixels[i] = unchecked((int)0xFF000000) | (v << 16) | (v << 8) | v;
        }
        return Android.Graphics.Bitmap.CreateBitmap(pixels, w, h, Android.Graphics.Bitmap.Config.Argb8888!);
    }

    private async partial Task<List<RecognizedText>> RecognizeCoreAsync(CameraFrame frame, TextRecognitionOptions options, CancellationToken ct)
    {
        if (frame is not AndroidCameraFrame android)
            return [];

        if (options.RegionOfInterest is { } roi)
            return await this.RecognizeRegionAsync(frame, roi, options).ConfigureAwait(false);

        var mediaImage = android.Proxy.Image;
        if (mediaImage == null)
            return [];

        var rotation = android.Proxy.ImageInfo.RotationDegrees;
        var input = InputImage.FromMediaImage(mediaImage, rotation);

        var uprightW = rotation is 90 or 270 ? frame.Height : frame.Width;
        var uprightH = rotation is 90 or 270 ? frame.Width : frame.Height;

        var result = await GmsTaskAwaiter.AwaitAsync(this.Client().Process(input)).ConfigureAwait(false);

        var blocks = new List<RecognizedText>();
        if (result is Text text)
        {
            foreach (var block in text.TextBlocks)
            {
                foreach (var line in block.Lines)
                {
                    var r = line.BoundingBox;
                    if (r == null)
                        continue;
                    var raw = new RectF((float)r.Left / uprightW, (float)r.Top / uprightH,
                        (float)r.Width() / uprightW, (float)r.Height() / uprightH);
                    var box = CoordinateTransform.ApplyOrientation(raw, 0, frame.IsMirrored);
                    blocks.Add(new RecognizedText(line.Text ?? string.Empty, box));
                }
            }
        }
        return blocks;
    }
}
