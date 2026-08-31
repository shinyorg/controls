using AppKit;
using AVFoundation;
using CoreGraphics;
using CoreImage;
using CoreMedia;
using CoreVideo;
using Foundation;

namespace Shiny.Maui.Controls.Camera;

// macOS equivalent of VideoFrameDelegate: filters into an NSImageView overlay and feeds the pipeline.
sealed class MacVideoFrameDelegate : AVCaptureVideoDataOutputSampleBufferDelegate
{
    // GPU-backed where we can get a Metal device: the default CIContext can fall back to a CPU renderer, which
    // is survivable for a colour matrix and hopeless for a convolution like the comic/sketch looks.
    readonly CIContext context = CreateContext();
    readonly WeakReference<NSImageView> filterTarget;

    static CIContext CreateContext()
    {
        try
        {
            if (Metal.MTLDevice.SystemDefault is { } device)
                return CIContext.FromMetalDevice(device);
        }
        catch (Exception)
        {
            // no Metal device — fall through to the default renderer
        }

        return new CIContext();
    }

    public MacVideoFrameDelegate(NSImageView filterTarget)
        => this.filterTarget = new WeakReference<NSImageView>(filterTarget);

    // The whole chain is swapped as one array reference so a frame never renders through a half-updated
    // chain — volatile gives us the publication barrier without locking the capture queue.
    volatile CIFilter[] filters = [];

    public CIFilter[] Filters
    {
        get => this.filters;
        set => this.filters = value ?? [];
    }

    public Func<bool>? WantFrames;
    public Action<AppleCameraFrame>? OnFrame;
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

                    // Same rule as iOS: borrow only when no recorder is going to composite into this buffer.
                    // See AppleCameraFrame's remarks for why sharing it with one corrupts the analysis.
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

                // composite + encode LAST so analyzers/preview see the clean frame and only the file gets the overlay
                recorder?.AppendVideo(sampleBuffer);
            }
            finally
            {
                if (!handedOff)
                    pixelBuffer.Dispose();
            }
        }
        finally
        {
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

    void RenderFiltered(CIFilter[] chain, CVPixelBuffer pixelBuffer, NSImageView view)
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

            // Render at the SOURCE extent — several filters (CIPixellate, CIGaussianBlur) report an infinite
            // or grown extent, which either fails outright or no longer matches the frame. See the iOS
            // delegate for the full reasoning.
            var cg = this.context.CreateCGImage(output, input.Extent);
            if (cg == null)
                return;

            var image = new NSImage(cg, new CGSize(cg.Width, cg.Height));
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

    NSImage? pending;
    int updateQueued;

    // Keep exactly one pending image and one queued update, so a filter that renders slower than the capture
    // interval can never build a main-thread backlog — see the iOS delegate for the full reasoning.
    void Publish(NSImage image, NSImageView view)
    {
        Interlocked.Exchange(ref this.pending, image)?.Dispose();

        if (Interlocked.Exchange(ref this.updateQueued, 1) == 1)
            return;

        view.BeginInvokeOnMainThread(() =>
        {
            Interlocked.Exchange(ref this.updateQueued, 0);

            var next = Interlocked.Exchange(ref this.pending, null);
            if (next is not null)
                view.Image = next;
        });
    }
}
