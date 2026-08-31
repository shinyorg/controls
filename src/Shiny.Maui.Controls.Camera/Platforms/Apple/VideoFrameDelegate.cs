using AVFoundation;
using CoreImage;
using CoreMedia;
using CoreVideo;
using Foundation;
using UIKit;

namespace Shiny.Maui.Controls.Camera;

// Single sample-buffer delegate for the AVCaptureVideoDataOutput. Does two jobs per frame:
//   1. when an effect chain is set, render the filtered frame into the overlay UIImageView;
//   2. when frames are wanted, hand a managed AppleCameraFrame to the analysis pipeline.
sealed class VideoFrameDelegate : AVCaptureVideoDataOutputSampleBufferDelegate
{
    // GPU-backed where we can get a Metal device: the default CIContext can fall back to a CPU renderer, which
    // is survivable for a colour matrix and hopeless for a convolution like the comic/sketch looks.
    readonly CIContext context = CreateContext();
    readonly WeakReference<UIImageView> filterTarget;

    static CIContext CreateContext()
    {
        try
        {
            if (Metal.MTLDevice.SystemDefault is { } device)
                return CIContext.FromMetalDevice(device);
        }
        catch (Exception)
        {
            // no Metal device (some simulators) — fall through to the default renderer
        }

        return new CIContext();
    }

    public VideoFrameDelegate(UIImageView filterTarget)
        => this.filterTarget = new WeakReference<UIImageView>(filterTarget);

    // The whole chain is swapped as one array reference so a frame never renders through a half-updated
    // chain — volatile gives us the publication barrier without locking the capture queue.
    volatile CIFilter[] filters = [];

    public CIFilter[] Filters
    {
        get => this.filters;
        set => this.filters = value ?? [];
    }

    /// <summary>Returns true while frames should be wrapped and pushed to the pipeline.</summary>
    public Func<bool>? WantFrames;

    /// <summary>Receives each wrapped frame (off the capture queue). The callee owns/disposes it.</summary>
    public Action<AppleCameraFrame>? OnFrame;

    /// <summary>Invoked (off the capture queue) when a frame raises an exception, so it can be surfaced.</summary>
    public Action<Exception>? OnError;

    /// <summary>Set by the handler so wrapped frames carry mirroring metadata.</summary>
    public volatile bool Mirrored;

    /// <summary>Whether the analyzer is handed the filtered frame - see CameraView.AnalyzerSeesEffects.</summary>
    public volatile bool AnalyzerSeesEffects;

    /// <summary>Where a filtered frame is drawn before it is handed on. Created on first use.</summary>
    readonly FilteredFrameBuffer filtered = new();

    /// <summary>Releases the scratch buffer with the delegate that owns it.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            this.filtered.Dispose();

        base.Dispose(disposing);
    }

    /// <summary>When set, each frame is composited with the overlay and appended to the burn-in recording.</summary>
    public volatile AppleVideoOverlayRecorder? Recorder;

    public override void DidOutputSampleBuffer(AVCaptureOutput captureOutput, CMSampleBuffer sampleBuffer, AVCaptureConnection connection)
    {
        var handedOff = false;
        try
        {
            var pixelBuffer = sampleBuffer.GetImageBuffer() as CVPixelBuffer;
            if (pixelBuffer == null)
                return;

            try
            {
                var chain = this.filters;
                if (chain.Length > 0 && this.filterTarget.TryGetTarget(out var view))
                    this.RenderFiltered(chain, pixelBuffer, view);

                var recorder = this.Recorder;
                if (this.OnFrame != null && this.WantFrames?.Invoke() == true)
                {
                    // The frame as the preview draws it, for an analyzer that has asked to see what
                    // the camera looks like rather than what the sensor produced. The buffer is this
                    // delegate's own and is reused, which is safe because the pipeline runs one
                    // analysis at a time - see FilteredFrameBuffer.
                    if (this.AnalyzerSeesEffects
                        && chain.Length > 0
                        && this.RenderForAnalyzer(chain, pixelBuffer) is { } effected)
                    {
                        this.OnFrame(AppleCameraFrame.Wrap(effected, rotation: 0, mirrored: this.Mirrored));
                        recorder?.AppendVideo(sampleBuffer);
                        return;
                    }

                    // ⚠️ Borrow only when nothing is going to write to this buffer. The recorder composites
                    // effects and the burn-in overlay back into it (AppleVideoOverlayRecorder.Composite), so
                    // with one attached a borrowed frame would be read by the analyzer on one thread while
                    // the encoder mutated it on another — an OCR pass over a half-drawn HUD. A copy costs
                    // 8.3 MB at 1080p, which is why it is now only paid on the frames an analyzer actually
                    // takes (see CameraPipeline.WantsFrame) rather than on every delivered frame.
                    if (recorder == null)
                    {
                        this.OnFrame(AppleCameraFrame.Borrow(sampleBuffer, pixelBuffer, rotation: 0, mirrored: this.Mirrored));
                        handedOff = true;
                    }
                    else
                    {
                        this.OnFrame(AppleCameraFrame.Copy(pixelBuffer, rotation: 0, mirrored: this.Mirrored));
                    }
                }

                // composite + encode LAST, so analyzers/preview see the clean frame and only the file gets
                // the overlay. Safe alongside a borrowed frame because the two are mutually exclusive above.
                recorder?.AppendVideo(sampleBuffer);
            }
            finally
            {
                if (!handedOff)
                    pixelBuffer.Dispose();
            }
        }
        catch (Exception ex)
        {
            // This is a native AVFoundation callback — a managed exception must never escape into ObjC or
            // the app hard-crashes. Swallow per-frame failures (report the first) and keep the camera alive.
            this.OnError?.Invoke(ex);
        }
        finally
        {
            // A borrowed frame owns the sample buffer now and disposes it when the analysis finishes.
            if (!handedOff)
                sampleBuffer.Dispose();
        }
    }

    // Reused across frames so a steady-state render allocates nothing here.
    readonly List<CIImage> produced = [];

    /// <summary>
    /// Runs the effect chain into the scratch buffer, for an analyzer rather than for the screen.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="RenderFiltered"/> because the destinations have nothing in common:
    /// that one produces an image for a view, this one fills a pixel buffer another consumer will
    /// read. The recipe is identical, and both render at the source extent for the reason spelled
    /// out there.
    /// </remarks>
    CVPixelBuffer? RenderForAnalyzer(CIFilter[] chain, CVPixelBuffer pixelBuffer)
    {
        using var input = new CIImage(pixelBuffer);

        this.produced.Clear();
        var output = AppleCameraFilters.Apply(input, chain, this.produced);

        try
        {
            return output is null ? null : this.filtered.Render(this.context, output, input.Extent);
        }
        catch (Exception)
        {
            // One frame without the effect on it beats taking the capture pipeline down.
            return null;
        }
        finally
        {
            foreach (var image in this.produced)
                image.Dispose();

            this.produced.Clear();
        }
    }

    void RenderFiltered(CIFilter[] chain, CVPixelBuffer pixelBuffer, UIImageView view)
    {
        using var input = new CIImage(pixelBuffer);

        // One render for the whole chain: Core Image concatenates the recipe and evaluates it once below, so
        // stacking N effects does not cost N passes over the frame.
        this.produced.Clear();
        var output = AppleCameraFilters.Apply(input, chain, this.produced);

        try
        {
            if (output == null)
                return;

            // Render at the SOURCE extent, not the filter's own. Several Core Image filters report an
            // infinite or grown extent — CIPixellate is defined over the whole plane, CIGaussianBlur bleeds
            // outward — and asking for that either fails outright (a null CGImage, so the preview shows
            // nothing) or hands back an image that no longer matches the frame. A preview always wants
            // exactly the frame it was given.
            var cg = this.context.CreateCGImage(output, input.Extent);
            if (cg == null)
                return;

            var image = UIImage.FromImage(cg);
            cg.Dispose();
            this.Publish(image, view);
        }
        finally
        {
            // only now that the render has actually evaluated the recipe
            foreach (var image in this.produced)
                image.Dispose();

            this.produced.Clear();
        }
    }

    UIImage? pending;
    int updateQueued;

    // Hand the newest frame to the UI without flooding the main thread.
    //
    // Posting a block per delivered frame is fine while the filter is cheap, but a heavy one (comic, sketch)
    // renders slower than the capture interval, and the main thread then falls behind a queue that only grows
    // — the whole UI, preview included, appears to lock up. Instead we keep exactly one pending image and one
    // queued update: late frames replace the pending image rather than adding to a backlog, so the main thread
    // always does at most one assignment per turn and naturally renders the most recent frame it can.
    void Publish(UIImage image, UIImageView view)
    {
        // the superseded image was never handed to UIKit, so we still own it
        Interlocked.Exchange(ref this.pending, image)?.Dispose();

        if (Interlocked.Exchange(ref this.updateQueued, 1) == 1)
            return;

        view.BeginInvokeOnMainThread(() =>
        {
            Interlocked.Exchange(ref this.updateQueued, 0);

            var next = Interlocked.Exchange(ref this.pending, null);
            if (next is not null)
                view.Image = next; // UIKit retains it and releases whatever it replaced
        });
    }
}
