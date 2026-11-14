using AVFoundation;
using AudioToolbox;
using CoreGraphics;
using CoreImage;
using CoreMedia;
using CoreVideo;
using Foundation;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Platform;

namespace Shiny.Maui.Controls.Camera;

// Owns an AVAssetWriter that burns the camera's effects — and any legacy IVideoOverlayRenderer — into every
// recorded frame. Shared by the iOS/MacCatalyst and macOS handlers whenever there is something to composite;
// with no effects and no overlay the raw-feed fast path (AVCaptureMovieFileOutput) is used instead. Frames and
// audio arrive off the capture queues, so all state is guarded.
//
// Each frame is processed in two stages, in this order:
//   1. pixel effects — the Core Image chain is rendered back into the same CVPixelBuffer, so the recording
//      matches the filtered preview rather than the raw sensor feed;
//   2. compositing — the buffer is wrapped in a CGBitmapContext, flipped to the UIKit top-left origin MAUI
//      Graphics expects, and every IDrawEffect (then the legacy overlay) is drawn on top.
// The (now-composited) sample buffer is appended straight to the video input, so no separate pixel-buffer pool
// is needed. Frames arrive already upright and front-mirror-corrected via the handler's OrientConnections, so
// overlay text renders the right way up.
sealed class AppleVideoOverlayRecorder
{
    readonly IVideoOverlayRenderer? overlay;
    readonly CameraEffectChain chain;
    readonly CIFilter[] filters;
    readonly CIContext? ciContext;
    readonly CameraFacing facing;
    readonly string path;
    readonly bool includeAudio;

    /// <summary>
    /// Target video bitrate, or null for AVFoundation's default for the dimensions.
    /// </summary>
    /// <remarks>
    /// This path has to be given the bitrate explicitly because it does not go through
    /// <c>AVCaptureMovieFileOutput</c> — it owns its own <c>AVAssetWriter</c>. Leaving it unset was a real
    /// inconsistency rather than a gap: with an overlay attached the encode silently changed codec *and*
    /// bitrate compared to the same recording without one, so toggling a burn-in overlay changed the size and
    /// fidelity of the output for reasons nothing in the API hinted at.
    /// </remarks>
    readonly int? videoBitrate;

    readonly object gate = new();
    readonly TaskCompletionSource<CameraVideo> tcs = new();

    /// <summary>
    /// Present when the overlay can hand over pre-rendered layers and there are no draw effects to run —
    /// see <see cref="AppleLayerCompositor"/>. Draw effects are arbitrary caller code holding a canvas, so
    /// their presence rules the fast path out entirely rather than merely complicating it.
    /// </summary>
    readonly AppleLayerCompositor? layerCompositor;

    AVAssetWriter? writer;
    AVAssetWriterInput? videoInput;
    AVAssetWriterInput? audioInput;
    CGColorSpace? colorSpace;
    CMTime startPts = CMTime.Invalid;
    CMTime lastPts = CMTime.Invalid;
    long frameIndex;
    bool finished;

    public AppleVideoOverlayRecorder(
        string path,
        bool includeAudio,
        CameraFacing facing,
        CameraEffectChain chain,
        IVideoOverlayRenderer? overlay,
        int? videoBitrate = null
    )
    {
        this.path = path;
        this.includeAudio = includeAudio;
        this.facing = facing;
        this.chain = chain;
        this.overlay = overlay;
        this.videoBitrate = videoBitrate;
        this.filters = AppleCameraFilters.Create(chain);

        // Only pay for a CIContext when there is actually a pixel chain to render through it. The layer
        // compositor owns a second one; it is Metal-backed and is built lazily on its first frame.
        this.ciContext = this.filters.Length > 0 ? new CIContext() : null;

        this.layerCompositor = chain.DrawEffects.Count == 0 && overlay is ICompositedVideoOverlayRenderer c
            ? new AppleLayerCompositor(c)
            : null;
    }

    /// <summary>
    /// Supplies the analyzer's current boxes and typed result to draw effects. Set by the handler; invoked once
    /// per recorded frame on the capture queue, so it must be lock-light and must not touch bindables.
    /// </summary>
    public Func<(IReadOnlyList<OverlayBox> Overlays, object? Result)>? AnalyzerSnapshot;

    public Task<CameraVideo> Task => this.tcs.Task;

    // Called on the video capture queue for every frame while recording. Lazily starts the writer on the
    // first frame (dimensions are read from the buffer), then composites + appends.
    public void AppendVideo(CMSampleBuffer sampleBuffer)
    {
        if (Volatile.Read(ref this.finished))
            return;

        using var pixelBuffer = sampleBuffer.GetImageBuffer() as CVPixelBuffer;
        if (pixelBuffer == null)
            return;

        var pts = sampleBuffer.PresentationTimeStamp;
        lock (this.gate)
        {
            if (this.finished)
                return;
            if (this.writer == null && !this.TryStart((int)pixelBuffer.Width, (int)pixelBuffer.Height, pts))
                return;
            if (this.videoInput is not { ReadyForMoreMediaData: true })
                return; // encoder backpressure — drop this frame (lowers fps rather than desyncs)

            this.Composite(pixelBuffer, pts);
            this.videoInput.AppendSampleBuffer(sampleBuffer);
            this.lastPts = pts;
            this.frameIndex++;
        }
    }

    // Called on the audio capture queue. Dropped until the writer session has started off a video frame.
    public void AppendAudio(CMSampleBuffer sampleBuffer)
    {
        lock (this.gate)
        {
            if (this.finished || this.writer == null || this.startPts == CMTime.Invalid)
                return;
            if (this.audioInput is { ReadyForMoreMediaData: true })
                this.audioInput.AppendSampleBuffer(sampleBuffer);
        }
    }

    bool TryStart(int width, int height, CMTime pts)
    {
        try
        {
            var url = NSUrl.FromFilename(this.path);
            this.writer = new AVAssetWriter(url, AVFileTypes.QuickTimeMovie.GetConstant()!, out var err);
            if (err != null || this.writer == null)
            {
                this.Fail(err?.LocalizedDescription ?? "Could not create AVAssetWriter");
                return false;
            }

            var videoSettings = new AVVideoSettingsCompressed
            {
                Codec = AVVideoCodec.H264,
                Width = width,
                Height = height
            };

            if (this.videoBitrate is > 0 and var bitrate)
            {
                videoSettings.CodecSettings = new AVVideoCodecSettings
                {
                    AverageBitRate = bitrate
                };
            }
            this.videoInput = new AVAssetWriterInput(AVMediaTypes.Video.GetConstant()!, videoSettings)
            {
                ExpectsMediaDataInRealTime = true
            };
            if (this.writer.CanAddInput(this.videoInput))
                this.writer.AddInput(this.videoInput);

            if (this.includeAudio)
            {
                var audioSettings = new AudioSettings
                {
                    Format = AudioFormatType.MPEG4AAC,
                    SampleRate = 44100,
                    NumberChannels = 1,
                    EncoderBitRate = 64000
                };
                this.audioInput = new AVAssetWriterInput(AVMediaTypes.Audio.GetConstant()!, audioSettings)
                {
                    ExpectsMediaDataInRealTime = true
                };
                if (this.writer.CanAddInput(this.audioInput))
                    this.writer.AddInput(this.audioInput);
            }

            // Write movie fragments as capture proceeds, so a recording the process never gets to finish is
            // still playable up to the last fragment. Without it the moov atom is only written by
            // FinishWriting, and a crash, a force-quit or a battery pull leaves a file nothing will decode —
            // however many minutes went into it.
            //
            // Ten seconds because that is exactly what AVCaptureMovieFileOutput defaults to, and this class
            // exists to be the same recording with an overlay burned in. It was the same inconsistency the
            // bitrate field above documents: attaching an overlay silently swapped a crash-recoverable file
            // for one that was all-or-nothing, for reasons nothing in the API hinted at.
            //
            // Must be set before StartWriting — AVFoundation ignores it afterwards.
            this.writer.MovieFragmentInterval = new CMTime(10, 1);

            if (!this.writer.StartWriting())
            {
                this.Fail(this.writer.Error?.LocalizedDescription ?? "AVAssetWriter failed to start");
                return false;
            }
            this.writer.StartSessionAtSourceTime(pts);
            this.startPts = pts;
            return true;
        }
        catch (Exception ex)
        {
            this.Fail(ex.Message);
            return false;
        }
    }

    void Composite(CVPixelBuffer pixelBuffer, CMTime pts)
    {
        // Stage 1 — pixel effects, rendered back into the same buffer. Done before the lock below because
        // CIContext.Render wants to manage the buffer's memory itself.
        this.ApplyPixelEffects(pixelBuffer);

        if (this.chain.DrawEffects.Count == 0 && this.overlay == null)
            return;

        // Stage 2a — the GPU path, where the overlay can describe itself as images. This returns before the
        // Lock below ever happens, which is the point: that lock maps the whole IOSurface for CPU access on
        // every frame regardless of how little of it the overlay covers.
        if (this.layerCompositor is { Unavailable: false } compositor)
        {
            var elapsedForLayers = this.startPts == CMTime.Invalid
                ? TimeSpan.Zero
                : TimeSpan.FromSeconds(CMTime.Subtract(pts, this.startPts).Seconds);

            var layerContext = new VideoOverlayContext(
                elapsedForLayers, this.frameIndex, (int)pixelBuffer.Width, (int)pixelBuffer.Height, this.facing);

            if (compositor.TryComposite(pixelBuffer, layerContext))
                return;
        }

        // Stage 2b — compositing on the CPU.
        //
        // ⚠️ A biplanar capture buffer cannot host a CGBitmapContext: there is no interleaved RGB to point
        // one at, and BaseAddress on a planar buffer is not the image. So the frame is converted to a BGRA
        // scratch surface, drawn on there, and converted back — two GPU passes on top of the CPU blend.
        // That is a worse trade than never leaving BGRA, which is exactly why CameraCaptureFormat.Yuv420 is
        // documented as being for layer-composited overlays and is not the default.
        if (AppleVideoFrame.IsBiplanarFormat(pixelBuffer.PixelFormatType))
        {
            this.DrawViaScratch(pixelBuffer, pts);
            return;
        }

        this.DrawInto(pixelBuffer, pts);
    }

    CVPixelBuffer? scratch;
    CIContext? scratchContext;

    /// <summary>
    /// Draws this frame's overlay through a BGRA scratch surface, for a capture format the CPU cannot draw
    /// on directly.
    /// </summary>
    void DrawViaScratch(CVPixelBuffer pixelBuffer, CMTime pts)
    {
        int w = (int)pixelBuffer.Width, h = (int)pixelBuffer.Height;

        // Allocated once and reused for the life of the recording. A per-frame surface would be an IOSurface
        // allocation thirty times a second on the capture queue.
        this.scratch ??= new CVPixelBuffer(w, h, CVPixelFormatType.CV32BGRA, new CVPixelBufferAttributes
        {
            PixelFormatType = CVPixelFormatType.CV32BGRA,
            Width = w,
            Height = h
        });

        this.scratchContext ??= Metal.MTLDevice.SystemDefault is { } device
            ? CIContext.FromMetalDevice(device)
            : new CIContext();

        using (var source = new CIImage(pixelBuffer))
            this.scratchContext.Render(source, this.scratch);

        this.DrawInto(this.scratch, pts);

        // Back into the buffer that is about to be appended — the sample buffer the encoder receives is the
        // capture's own, so a composite left in the scratch would never reach the file.
        using (var drawn = new CIImage(this.scratch))
            this.scratchContext.Render(drawn, pixelBuffer);
    }

    /// <summary>The CPU composite itself, over any BGRA buffer.</summary>
    void DrawInto(CVPixelBuffer pixelBuffer, CMTime pts)
    {
        // Read-write lock (flags 0) so the draws land in the buffer we then encode.
        pixelBuffer.Lock((CVPixelBufferLock)0);
        try
        {
            int w = (int)pixelBuffer.Width, h = (int)pixelBuffer.Height;
            var bytesPerRow = (int)pixelBuffer.BytesPerRow;
            this.colorSpace ??= CGColorSpace.CreateDeviceRGB();

            using var ctx = new CGBitmapContext(
                pixelBuffer.BaseAddress, w, h, 8, bytesPerRow, this.colorSpace,
                (CGBitmapFlags)CGImageAlphaInfo.PremultipliedFirst | CGBitmapFlags.ByteOrder32Little);

            // CGBitmapContext is bottom-left origin; flip to the UIKit top-left convention MAUI Graphics assumes
            ctx.TranslateCTM(0, h);
            ctx.ScaleCTM(1, -1);

            var canvas = new PlatformCanvas(() => this.colorSpace!) { Context = ctx, DisplayScale = 1 };
            var elapsed = this.startPts == CMTime.Invalid
                ? TimeSpan.Zero
                : TimeSpan.FromSeconds(CMTime.Subtract(pts, this.startPts).Seconds);

            var bounds = new RectF(0, 0, w, h);
            var (overlays, analyzerResult) = this.AnalyzerSnapshot?.Invoke() ?? ([], null);
            var effectContext = new CameraEffectContext(
                elapsed, this.frameIndex, w, h, this.facing, CameraSurface.Video,
                overlays, analyzerResult);

            foreach (var draw in this.chain.DrawEffects)
                draw.Draw(canvas, bounds, effectContext);

            // the legacy per-recording overlay draws last, on top of the effect chain
            this.overlay?.DrawOverlay(canvas, bounds,
                new VideoOverlayContext(elapsed, this.frameIndex, w, h, this.facing));
        }
        finally
        {
            pixelBuffer.Unlock((CVPixelBufferLock)0);
        }
    }

    readonly List<CIImage> produced = [];

    void ApplyPixelEffects(CVPixelBuffer pixelBuffer)
    {
        if (this.ciContext is null || this.filters.Length == 0)
            return;

        using var input = new CIImage(pixelBuffer);

        this.produced.Clear();
        var output = AppleCameraFilters.Apply(input, this.filters, this.produced);

        try
        {
            if (output == null)
                return;

            // Render back over the source buffer. Core Image evaluates the recipe here, reading the buffer it
            // is also writing — safe only because Core Image snapshots the source surface before writing, and
            // it avoids a second pixel-buffer pool for every recorded frame.
            this.ciContext.Render(output, pixelBuffer);
        }
        finally
        {
            foreach (var image in this.produced)
                image.Dispose();

            this.produced.Clear();
        }
    }

    public Task<CameraVideo> FinishAsync()
    {
        AVAssetWriter? w;
        CMTime end;
        lock (this.gate)
        {
            if (this.finished)
                return this.tcs.Task;
            this.finished = true;
            w = this.writer;
            end = this.lastPts;
            this.videoInput?.MarkAsFinished();
            this.audioInput?.MarkAsFinished();
        }

        this.layerCompositor?.Dispose();

        // The scratch surface and its context only exist for a biplanar recording that had to draw on the
        // CPU. Released with the recording rather than kept: it is a full frame of IOSurface.
        this.scratch?.Dispose();
        this.scratch = null;
        this.scratchContext?.Dispose();
        this.scratchContext = null;

        if (w == null)
        {
            this.tcs.TrySetException(new InvalidOperationException("No frames were recorded"));
            return this.tcs.Task;
        }

        if (end != CMTime.Invalid)
            w.EndSessionAtSourceTime(end);

        w.FinishWriting(() =>
        {
            if (w.Status == AVAssetWriterStatus.Completed)
            {
                var duration = this.startPts != CMTime.Invalid && end != CMTime.Invalid
                    ? TimeSpan.FromSeconds(CMTime.Subtract(end, this.startPts).Seconds)
                    : (TimeSpan?)null;
                this.tcs.TrySetResult(new CameraVideo(this.path, duration));
            }
            else
            {
                this.tcs.TrySetException(new InvalidOperationException(
                    w.Error?.LocalizedDescription ?? "AVAssetWriter did not finish"));
            }
        });
        return this.tcs.Task;
    }

    void Fail(string message)
    {
        this.finished = true;
        this.tcs.TrySetException(new InvalidOperationException(message));
    }
}
