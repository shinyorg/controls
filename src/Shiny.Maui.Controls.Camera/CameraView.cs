using System.Windows.Input;
using Microsoft.Maui.Graphics;

namespace Shiny.Maui.Controls.Camera;

/// <summary>
/// A cross-platform camera preview with zoom / torch / lens control, still capture, and a pluggable
/// frame-analysis pipeline. A <b>single</b> <see cref="Analyzer"/> (barcode, face, motion, OCR, documents)
/// runs against each frame at a time — set or swap it freely while the camera is running. The analyzer
/// raises its own strongly-typed event for semantic results and returns styled <see cref="OverlayBox"/>es
/// that are drawn over the preview. Backed by AVFoundation (iOS/macOS), CameraX (Android) and MediaCapture
/// (Windows).
/// </summary>
[ContentProperty(nameof(Analyzer))]
public partial class CameraView : View
{
    /// <summary>
    /// The single frame analyzer run against each frame (null = no analysis). Assign or swap it at any time —
    /// the running session picks up the change. It's the content property, so it can be declared inline in XAML.
    /// </summary>
    public static readonly BindableProperty AnalyzerProperty = BindableProperty.Create(
        nameof(Analyzer), typeof(IFrameAnalyzer), typeof(CameraView), propertyChanged: OnAnalyzerChanged);

    /// <inheritdoc cref="AnalyzerProperty"/>
    public IFrameAnalyzer? Analyzer
    {
        get => (IFrameAnalyzer?)this.GetValue(AnalyzerProperty);
        set => this.SetValue(AnalyzerProperty, value);
    }

    public CameraView()
    {
        this.ScanCommand = new Command(this.Scan);
        this.InitializeEffects();
    }

    static void OnAnalyzerChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (CameraView)bindable;
        if (oldValue is FrameAnalyzer previous && ReferenceEquals(previous.Host, view))
            previous.Host = null;
        if (newValue is FrameAnalyzer next)
            next.Host = view;

        // a declared/assigned analyzer inherits this view's BindingContext so its Commands bind
        if (newValue is BindableObject bo)
            SetInheritedBindingContext(bo, view.BindingContext);
    }

    /// <summary>
    /// Arm the analyzer for one scan: it stays silent (still drawing boxes) until its next confirmed detection,
    /// which it then delivers once. The analyzer's <c>OnDetected</c> returning <c>true</c> keeps it armed
    /// (continuous scanning); otherwise it disarms until the next call. Bind a button/Fab to
    /// <see cref="ScanCommand"/>, or call this directly.
    /// </summary>
    public void Scan()
    {
        if (this.Analyzer is FrameAnalyzer { IsEnabled: true } analyzer)
            analyzer.Arm();
    }

    /// <summary>Command form of <see cref="Scan"/> — arms the analyzer for one scan.</summary>
    public ICommand ScanCommand { get; }

    /// <summary>Disarm the analyzer, cancelling any in-progress scan. Boxes keep drawing; results stop until the next <see cref="Scan"/>.</summary>
    public void StopScanning()
    {
        if (this.Analyzer is FrameAnalyzer analyzer)
            analyzer.Disarm();
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        if (this.Analyzer is BindableObject analyzer)
            SetInheritedBindingContext(analyzer, this.BindingContext);
    }

    /// <summary>Raised on the UI thread when a still photo has been captured.</summary>
    public event EventHandler<CameraPhoto>? MediaCaptured;

    /// <summary>Raised on the UI thread when a video recording finishes.</summary>
    public event EventHandler<CameraVideo>? VideoCaptured;

    /// <summary>Raised on the UI thread when the camera or pipeline reports an error.</summary>
    public event EventHandler<CameraErrorEventArgs>? CameraError;

    /// <summary>
    /// Raised on the UI thread whenever the analyzer's set of overlay boxes changes. This is a
    /// presentation-only channel for drawing; subscribe to the analyzer's own typed event for semantic
    /// results (e.g. the decoded barcode value).
    /// </summary>
    public event EventHandler<CameraOverlaysChangedEventArgs>? OverlaysChanged;

    ICameraViewController? Controller => this.Handler as ICameraViewController;


    /// <summary>Request camera permission. Returns <c>true</c> when granted.</summary>
    public Task<bool> RequestPermissionAsync(CancellationToken ct = default)
        => this.Controller?.RequestPermissionAsync(ct) ?? Task.FromResult(false);

    /// <summary>List the cameras available on this device (use a <see cref="CameraInfo.Id"/> for <see cref="CameraId"/>).</summary>
    public Task<IReadOnlyList<CameraInfo>> GetAvailableCamerasAsync(CancellationToken ct = default)
        => this.Controller?.GetAvailableCamerasAsync(ct) ?? Task.FromResult<IReadOnlyList<CameraInfo>>([]);

    /// <summary>Start the capture session and preview.</summary>
    public Task StartAsync(CancellationToken ct = default)
        => this.Controller?.StartAsync(ct) ?? Task.CompletedTask;

    /// <summary>Stop the capture session and preview.</summary>
    public Task StopAsync(CancellationToken ct = default)
        => this.Controller?.StopAsync(ct) ?? Task.CompletedTask;

    /// <summary>
    /// Capture a single still photo. Also raises <see cref="MediaCaptured"/>.
    /// </summary>
    /// <remarks>
    /// The photo comes back with the full <see cref="Effects"/> chain applied, in three stages: pixel effects
    /// are baked in by the platform capture path, draw effects are composited on top, and any
    /// <see cref="ICaptureEffect"/> runs last. That last stage can be slow by design — an AI stylizer is a
    /// network round-trip — so show a busy state around this call when one is in the chain.
    /// </remarks>
    public async Task<CameraPhoto> CapturePhotoAsync(CancellationToken ct = default)
    {
        if (this.Controller is null)
            throw new InvalidOperationException("CameraView handler is not connected");

        var photo = await this.Controller.CapturePhotoAsync(ct).ConfigureAwait(false);

#if ANDROID || IOS || MACCATALYST || WINDOWS || MACOS
        var chain = this.EffectChain;
        photo = Internal.StillCompositor.Composite(photo, chain, this.Facing);
        photo = await Internal.StillCompositor
            .ApplyCaptureEffectsAsync(photo, chain, ct)
            .ConfigureAwait(false);
#endif

        this.MediaCaptured?.Invoke(this, photo);
        return photo;
    }


    /// <summary>
    /// Capture a single still photo and then stop the capture session, atomically. Raises
    /// <see cref="MediaCaptured"/> for the photo (via <see cref="CapturePhotoAsync"/>) and returns it. Handy from
    /// an analyzer's <c>OnDetected</c> handler for a "scan then freeze" flow — capture the still, then
    /// <c>return false</c> to disarm.
    /// </summary>
    public async Task<CameraPhoto> CaptureAndStopAsync(CancellationToken ct = default)
    {
        var photo = await this.CapturePhotoAsync(ct).ConfigureAwait(false);
        await this.StopAsync(ct).ConfigureAwait(false);
        return photo;
    }


    /// <summary>Start recording video. Audio is included unless disabled via <paramref name="options"/>.</summary>
    public async Task StartVideoRecordingAsync(VideoRecordingOptions? options = null, CancellationToken ct = default)
    {
        if (this.Controller is null)
            throw new InvalidOperationException("CameraView handler is not connected");

        await this.Controller.StartVideoRecordingAsync(options ?? new VideoRecordingOptions(), ct).ConfigureAwait(false);
        this.IsRecording = true;
    }


    /// <summary>Stop the in-progress recording. Also raises <see cref="VideoCaptured"/>.</summary>
    public async Task<CameraVideo> StopVideoRecordingAsync(CancellationToken ct = default)
    {
        if (this.Controller is null)
            throw new InvalidOperationException("CameraView handler is not connected");

        var video = await this.Controller.StopVideoRecordingAsync(ct).ConfigureAwait(false);
        this.IsRecording = false;
        this.VideoCaptured?.Invoke(this, video);
        return video;
    }


    /// <summary>Invoked by the handler/pipeline to publish the latest overlay boxes + the analyzer's scan window. Raises <see cref="OverlaysChanged"/>.</summary>
    public void OnOverlaysChanged(IReadOnlyList<OverlayBox> overlays, RectF? scanWindow, int imageWidth, int imageHeight)
    {
        this.Overlays = overlays;
        this.ScanWindow = scanWindow;
        this.OverlaysChanged?.Invoke(this, new CameraOverlaysChangedEventArgs(overlays, scanWindow, imageWidth, imageHeight));
    }

    /// <summary>Invoked by the handler to report an error. Raises <see cref="CameraError"/>.</summary>
    public void OnCameraError(string message, Exception? exception = null)
        => this.CameraError?.Invoke(this, new CameraErrorEventArgs(message, exception));

    /// <summary>Invoked by the handler to publish the supported zoom range it discovered.</summary>
    public void OnZoomRangeChanged(double minZoom, double maxZoom)
    {
        this.MinZoom = minZoom;
        this.MaxZoom = maxZoom;
    }
}


/// <summary>Carries the analyzer's latest overlay boxes (and scan window) to <see cref="CameraView.OverlaysChanged"/> subscribers.</summary>
public class CameraOverlaysChangedEventArgs(IReadOnlyList<OverlayBox> overlays, RectF? scanWindow, int imageWidth, int imageHeight) : EventArgs
{
    /// <summary>The analyzer's boxes, in normalized upright image space.</summary>
    public IReadOnlyList<OverlayBox> Overlays { get; } = overlays;

    /// <summary>The analyzer's scan window (normalized upright space), or null when it scans the full frame.</summary>
    public RectF? ScanWindow { get; } = scanWindow;

    /// <summary>Width of the analyzed image in pixels.</summary>
    public int ImageWidth { get; } = imageWidth;

    /// <summary>Height of the analyzed image in pixels.</summary>
    public int ImageHeight { get; } = imageHeight;
}


/// <summary>Carries an error from <see cref="CameraView.CameraError"/>.</summary>
public class CameraErrorEventArgs(string message, Exception? exception) : EventArgs
{
    /// <summary>A human-readable description of the failure.</summary>
    public string Message { get; } = message;

    /// <summary>The underlying exception, if any.</summary>
    public Exception? Exception { get; } = exception;
}
