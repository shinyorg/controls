using Android.Graphics;
using AndroidX.Camera.Core;
using AndroidX.Camera.Lifecycle;
using AndroidX.Camera.Video;
using AndroidX.Camera.View;
using AndroidX.Core.Content;
using Java.Lang;
using Microsoft.Maui.Handlers;
using AView = Android.Views.View;
using AWidget = Android.Widget;

namespace Shiny.Maui.Controls.Camera;

// Android (CameraX). Included only for the net10.0-android TFM.
public partial class CameraViewHandler : ViewHandler<CameraView, AWidget.FrameLayout>, ICameraViewController
{
    PreviewView? previewView;
    CameraLifecycleOwner? lifecycleOwner;
    ProcessCameraProvider? cameraProvider;
    ImageCapture? imageCapture;
    VideoCapture? videoCapture;
    ImageAnalysis? imageAnalysis;
    Recorder? recorder;
    Recording? activeRecording;
    VideoRecordListener? recordListener;
    Java.Util.Concurrent.IExecutorService? analysisExecutor;
    ICamera? camera;
    // Burn-in video overlay: when set, BindUseCases attaches an OverlayEffect to the VideoCapture use case so
    // the renderer is composited into the recorded file.
    IVideoOverlayRenderer? recordingOverlay;
    AndroidX.Camera.Effects.OverlayEffect? overlayEffect;
    Android.OS.HandlerThread? overlayThread;
    // True from StartVideoRecordingAsync until the recording finalizes. CameraX guarantees Preview plus two
    // more use cases at LIMITED hardware level and a fourth only at LEVEL_3, so something has to give when an
    // analyzer and a recording are both live — and ImageCapture is it (see BindUseCases). Tracking the wish
    // separately from the binding is what lets a scanner app that never records keep photo capture.
    bool wantsVideoCapture;
    // The use-case shape currently bound. A change in either dimension (an analyzer toggling, a recording
    // starting or finishing) is what triggers a rebind.
    (bool Analyzing, bool Video) boundShape;
    DisplayRotationListener? displayListener;

    protected override AWidget.FrameLayout CreatePlatformView()
    {
        var layout = new AWidget.FrameLayout(this.Context);
        this.previewView = new PreviewView(this.Context)
        {
            LayoutParameters = new AWidget.FrameLayout.LayoutParams(
                Android.Views.ViewGroup.LayoutParams.MatchParent,
                Android.Views.ViewGroup.LayoutParams.MatchParent)
        };
        // PreviewView defaults to Performance mode, which renders into a SurfaceView whose content is
        // composited on a separate surface that ignores View.SetRenderEffect — so the live colour filter
        // never appears. Compatible mode renders into a TextureView, which honours the RenderEffect colour
        // matrix we apply in ApplyFilter.
        this.previewView.SetImplementationMode(PreviewView.ImplementationMode.Compatible!);
        layout.AddView(this.previewView);
        return layout;
    }

    protected override void ConnectHandler(AWidget.FrameLayout platformView)
    {
        base.ConnectHandler(platformView);
        this.lifecycleOwner = new CameraLifecycleOwner();
        this.InitPipeline();
        this.displayListener = DisplayRotationListener.Register(this.Context, this.ApplyTargetRotation);
        if (this.VirtualView.IsActive)
            _ = this.StartAsync();
    }

    protected override void DisconnectHandler(AWidget.FrameLayout platformView)
    {
        this.TeardownPipeline();
        try
        {
            this.displayListener?.Unregister();
            this.imageAnalysis?.ClearAnalyzer();
            this.cameraProvider?.UnbindAll();
            this.DisposeOverlayEffect();
            this.overlayThread?.QuitSafely();
            this.lifecycleOwner?.Destroy();
            this.analysisExecutor?.Shutdown();
        }
        catch { /* tearing down */ }
        this.recordingOverlay = null;
        this.wantsVideoCapture = false;
        this.boundShape = default;
        this.displayListener = null;
        this.overlayThread = null;
        this.analysisExecutor = null;
        this.camera = null;
        this.imageCapture = null;
        this.imageAnalysis = null;
        this.lifecycleOwner = null;
        base.DisconnectHandler(platformView);
    }


    Task<ProcessCameraProvider> GetProviderAsync()
    {
        if (this.cameraProvider != null)
            return Task.FromResult(this.cameraProvider);

        var tcs = new TaskCompletionSource<ProcessCameraProvider>();
        var future = ProcessCameraProvider.GetInstance(this.Context);
        future.AddListener(new Runnable(() =>
        {
            try { tcs.TrySetResult((ProcessCameraProvider)future.Get()!); }
            catch (System.Exception ex) { tcs.TrySetException(ex); }
        }), ContextCompat.GetMainExecutor(this.Context));
        return tcs.Task;
    }


    public async Task<IReadOnlyList<CameraInfo>> GetAvailableCamerasAsync(CancellationToken ct = default)
    {
        var provider = await this.GetProviderAsync().ConfigureAwait(false);
        var list = new List<CameraInfo>();
        foreach (var info in provider.AvailableCameraInfos)
        {
            var c2 = AndroidX.Camera.Camera2.InterOp.Camera2CameraInfo.From(info);
            var id = c2.CameraId;
            var lens = c2.GetCameraCharacteristic(Android.Hardware.Camera2.CameraCharacteristics.LensFacing!) as Java.Lang.Integer;
            var facing = lens?.IntValue() switch
            {
                (int)Android.Hardware.Camera2.LensFacing.Front => CameraFacing.Front,
                (int)Android.Hardware.Camera2.LensFacing.Back => CameraFacing.Back,
                _ => CameraFacing.External
            };
            list.Add(new CameraInfo(id, $"{facing} camera ({id})", facing));
        }
        return list;
    }


    public async Task<bool> RequestPermissionAsync(CancellationToken ct = default)
    {
        var status = await MainThread.InvokeOnMainThreadAsync(
            () => Permissions.RequestAsync<Permissions.Camera>()
        ).ConfigureAwait(false);
        return status == PermissionStatus.Granted;
    }


    public async Task StartAsync(CancellationToken ct = default)
    {
        if (!await this.RequestPermissionAsync(ct).ConfigureAwait(false))
        {
            this.MaybeVirtualView?.OnCameraError("Camera permission denied");
            return;
        }

        var ctx = this.Context;
        var future = ProcessCameraProvider.GetInstance(ctx);
        future.AddListener(new Runnable(() =>
        {
            try
            {
                this.cameraProvider = (ProcessCameraProvider)future.Get()!;
                this.lifecycleOwner!.Start();
                this.BindUseCases();
            }
            catch (System.Exception ex)
            {
                this.MaybeVirtualView?.OnCameraError("Failed to start camera", ex);
            }
        }), ContextCompat.GetMainExecutor(ctx));
    }


    public Task StopAsync(CancellationToken ct = default)
    {
        this.lifecycleOwner?.Stop();
        return Task.CompletedTask;
    }


    public Task<CameraPhoto> CapturePhotoAsync(CancellationToken ct = default)
    {
        if (this.imageCapture == null)
            throw new InvalidOperationException(this.activeRecording != null
                ? "Photo capture is unavailable while recording with a frame analyzer active — CameraX has no use-case budget left for ImageCapture on this hardware. It returns when the recording stops."
                : "Camera is not running");

        this.imageCapture.FlashMode = this.VirtualView.FlashMode switch
        {
            CameraFlashMode.On => ImageCapture.FlashModeOn,
            CameraFlashMode.Auto => ImageCapture.FlashModeAuto,
            _ => ImageCapture.FlashModeOff
        };

        // apply the same filter as the live preview so the captured still matches what the user sees
        var cb = new ImageCapturedCallback(this.VirtualView.EffectChain);
        this.imageCapture.TakePicture(ContextCompat.GetMainExecutor(this.Context)!, cb);
        return cb.Task;
    }


    public async Task StartVideoRecordingAsync(VideoRecordingOptions options, CancellationToken ct = default)
    {
        // VideoCapture may not be bound yet — with an analyzer running the camera sits on
        // Preview + ImageCapture + ImageAnalysis until a recording actually asks for it. Ask for it now, then
        // rebind if either that or a burn-in overlay (which needs an OverlayEffect attached to VideoCapture)
        // means the current binding is wrong. With no analyzer and no overlay this is the old path untouched.
        //
        // Draw effects are folded into the same OverlayEffect as the legacy per-recording overlay, so a face
        // mask that's visible on the preview also lands in the file. Pixel effects do NOT — the preview's
        // RenderEffect lives on the PreviewView, not on the VideoCapture use case, so recorded video is
        // unfiltered on Android until the CameraX CameraEffect path replaces it.
        var overlay = Internal.EffectVideoOverlay.Create(
            this.VirtualView.EffectChain, options.Overlay, this.Pipeline.Snapshot);

        var needsRebind = this.videoCapture == null || overlay != null;
        this.wantsVideoCapture = true;
        this.recordingOverlay = overlay;

        if (needsRebind)
            await MainThread.InvokeOnMainThreadAsync(this.BindUseCases).ConfigureAwait(false);

        if (this.recorder == null)
        {
            this.wantsVideoCapture = false;
            this.recordingOverlay = null;
            throw new InvalidOperationException("Camera is not running");
        }

        var withAudio = options.IncludeAudio;
        if (withAudio)
        {
            var mic = await MainThread.InvokeOnMainThreadAsync(
                () => Permissions.RequestAsync<Permissions.Microphone>()).ConfigureAwait(false);
            withAudio = mic == PermissionStatus.Granted;
        }

        var path = options.FilePath ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"shiny-{Guid.NewGuid():N}.mp4");
        var outputOptions = new FileOutputOptions.Builder(new Java.IO.File(path)).Build();

        var pending = this.recorder.PrepareRecording(this.Context, outputOptions);
        if (withAudio)
            pending = pending.WithAudioEnabled();

        this.recordListener = new VideoRecordListener(path);
        this.activeRecording = pending.Start(ContextCompat.GetMainExecutor(this.Context)!, this.recordListener);
    }


    public Task<CameraVideo> StopVideoRecordingAsync(CancellationToken ct = default)
    {
        if (this.activeRecording == null || this.recordListener == null)
            throw new InvalidOperationException("Not recording");

        this.activeRecording.Stop();
        var task = this.recordListener.Task;
        this.activeRecording = null;
        var hadOverlay = this.recordingOverlay != null;
        this.recordingOverlay = null;
        // release the claim on VideoCapture so ImageCapture can come back if an analyzer is still running
        this.wantsVideoCapture = false;

        // once the recording finalizes, rebind: detach the overlay effect (if any) and restore the use-case
        // shape now that the recording no longer needs VideoCapture.
        _ = task.ContinueWith(
            _ => MainThread.BeginInvokeOnMainThread(() =>
            {
                if (hadOverlay)
                    this.BindUseCases(); // rebind without the overlay effect
                this.RebindIfModeChanged();
                // Pick up any rotation that happened during the take. ApplyTargetRotation held VideoCapture
                // back while the recording owned it, and neither rebind above is guaranteed to run.
                this.ApplyTargetRotation();
            }),
            TaskScheduler.Default);
        return task;
    }


    // Build the Recorder for the requested quality and bitrate.
    //
    // The fallback strategy is what stops a quality request being a compatibility cliff: capture ladders vary
    // wildly across Android hardware, and QualitySelector.From with no fallback simply produces no supported
    // quality on a device that lacks the exact rung — which surfaces as a camera that will not bind, not as a
    // lower-resolution recording. LowerQualityOrHigherThan degrades first (smaller files are the safer
    // surprise) and only goes up if there is nothing below.
    Recorder BuildRecorder()
    {
        var quality = this.VirtualView.VideoQuality switch
        {
            VideoQuality.Lowest => Quality.Lowest!,
            VideoQuality.Low => Quality.Sd!,
            VideoQuality.Medium => Quality.Hd!,
            VideoQuality.High => Quality.Fhd!,
            VideoQuality.UltraHigh => Quality.Uhd!,
            _ => Quality.Highest!
        };

        var builder = new Recorder.Builder()
            .SetQualitySelector(QualitySelector.From(quality, FallbackStrategy.LowerQualityOrHigherThan(quality)!)!);

        // CameraX rejects a non-positive target outright, so a nonsense value is dropped rather than passed on
        if (this.VirtualView.VideoBitrate is > 0 and var bitrate)
            builder.SetTargetVideoEncodingBitRate(bitrate);

        return builder.Build();
    }


    // Frame rate rides on VideoCapture rather than the Recorder. It is a *range* in CameraX, and passing the
    // requested value as both bounds is deliberate: a range would let the device drift back up to its
    // preferred rate under good light, which defeats the point when the reason for asking was thermals or
    // file size rather than motion.
    VideoCapture BuildVideoCapture(Recorder rec)
    {
        if (this.VirtualView.VideoFrameRate is not > 0)
            return VideoCapture.WithOutput(rec);

        var fps = this.VirtualView.VideoFrameRate!.Value;

        // Builder.Build() is bound as Java.Lang.Object (the generic VideoCapture<T> erases in the binding),
        // so the cast is not optional
        return (VideoCapture)new VideoCapture.Builder(rec)
            .SetTargetFrameRate(new Android.Util.Range(Java.Lang.Integer.ValueOf(fps), Java.Lang.Integer.ValueOf(fps)))
            .Build();
    }


    // Force ImageAnalysis and VideoCapture onto the same field of view.
    //
    // CameraX sizes each use case independently — ImageAnalysis lands on a 4:3 buffer by default while the
    // Recorder is 16:9 — so the two see *different crops of the sensor*. That is invisible until something
    // maps a detection from one into the other: an analyzer's normalized bounding box drawn into the recorded
    // frame (the mapping IVideoOverlayRenderer documents) would sit visibly off the thing it is boxing, and
    // vertically off by the full 4:3-vs-16:9 letterbox. A ViewPort is CameraX's mechanism for giving every use
    // case in the group one shared crop rect, which makes that mapping correct by construction.
    //
    // Applied ONLY when analysis and recording are bound together — the case that could not exist at all
    // before — so no existing single-use-case configuration has its recorded field of view moved underneath it.
    ViewPort? BuildSharedViewPort(int rotation)
    {
        // The PreviewView's own ViewPort matches what the user is looking at, which is the most useful thing
        // to align to. It is null until the view has been laid out, so fall back to the Recorder's 16:9 at
        // the rotation the use cases were just given — reading it back off the Preview use case would report
        // the display rotation Preview defaulted to, which is not necessarily the one in force here.
        var fromPreview = this.previewView?.ViewPort;
        if (fromPreview != null)
            return fromPreview;

        return new ViewPort.Builder(new Android.Util.Rational(16, 9), rotation)
            .SetScaleType(ViewPort.FillCenter)
            .Build();
    }


    AndroidX.Camera.Effects.OverlayEffect CreateOverlayEffect(IVideoOverlayRenderer overlay)
    {
        if (this.overlayThread == null)
        {
            this.overlayThread = new Android.OS.HandlerThread("shiny.camera.overlay");
            this.overlayThread.Start();
        }
        var handler = new Android.OS.Handler(this.overlayThread.Looper!);
        var effect = new AndroidX.Camera.Effects.OverlayEffect(
            AndroidX.Camera.Core.CameraEffect.VideoCapture,
            3, // queue depth: frames buffered before dropping
            handler,
            new OverlayErrorConsumer(msg => this.MaybeVirtualView?.OnCameraError("Video overlay failed: " + msg)));
        effect.SetOnDrawListener(new OverlayDrawListener(this.Context, this.VirtualView.Facing, overlay));
        this.overlayEffect = effect;
        return effect;
    }

    void DisposeOverlayEffect()
    {
        try { this.overlayEffect?.Close(); }
        catch { /* already closed / detached */ }
        this.overlayEffect?.Dispose();
        this.overlayEffect = null;
    }


    // The use-case shape BindUseCases would produce right now. Compared against boundShape to decide whether
    // anything actually needs rebinding.
    (bool Analyzing, bool Video) DesiredShape()
    {
        var analyzing = this.Pipeline.HasAnalyzer;
        return (analyzing, this.wantsVideoCapture || !analyzing);
    }

    // Re-bind use cases when the wanted shape has moved away from the bound one — an analyzer being added,
    // removed or toggled, or a recording starting/finishing. Invoked via the OnAnalyzersSynced hook and after
    // a recording finalizes. A no-op while not started, mid-recording, or when the shape is unchanged.
    partial void OnAnalyzersSynced()
    {
        if (this.cameraProvider == null || this.lifecycleOwner == null)
            return; // not started yet — BindUseCases will read the current set when it runs
        if (this.activeRecording != null)
            return; // can't swap use cases mid-recording; deferred until it finalizes
        if (this.DesiredShape() == this.boundShape)
            return; // shape unchanged (e.g. a 2nd analyzer added) — runner set already updated, no rebind

        MainThread.BeginInvokeOnMainThread(this.RebindIfModeChanged);
    }

    void RebindIfModeChanged()
    {
        // re-check after marshalling to the main thread: state may have moved on
        if (this.cameraProvider != null
            && this.activeRecording == null
            && this.DesiredShape() != this.boundShape)
            this.BindUseCases();
    }


    // ---- property mappers ----

    static partial void MapFacing(CameraViewHandler handler, CameraView view) => handler.BindUseCases();

    static partial void MapIsActive(CameraViewHandler handler, CameraView view)
    {
        if (view.IsActive)
            _ = handler.StartAsync();
        else
            _ = handler.StopAsync();
    }

    static partial void MapTorch(CameraViewHandler handler, CameraView view)
        => handler.camera?.CameraControl.EnableTorch(view.IsTorchOn);

    static partial void MapFlashMode(CameraViewHandler handler, CameraView view) { /* applied at capture time */ }

    static partial void MapZoom(CameraViewHandler handler, CameraView view)
        => handler.camera?.CameraControl.SetZoomRatio((float)view.Zoom);

    static partial void MapScaleMode(CameraViewHandler handler, CameraView view)
    {
        if (handler.previewView != null)
            handler.previewView.SetScaleType(view.ScaleMode == PreviewScaleMode.AspectFit
                ? PreviewView.ScaleType.FitCenter
                : PreviewView.ScaleType.FillCenter);
    }

    // ⚠️ Hidden, never unbound, and that is not a shortcut. Dropping the Preview use case would be the
    // larger saving — the camera stops producing preview frames altogether — but every use-case change in
    // this handler goes through BindUseCases, which begins with cameraProvider.UnbindAll() and so drops any
    // recording in flight. A property that stops a dash cam filming because somebody hid the picture has
    // failed at the one thing the consumer needs, so the Preview use case stays bound and only the view
    // goes away: SurfaceFlinger stops compositing it and the TextureView stops uploading a texture per
    // frame, with no CameraX call made at all.
    //
    // Invisible rather than Gone: Gone takes the view out of layout, which resizes the PreviewView to
    // nothing and hands CameraX a surface change it did not ask for. Invisible keeps the geometry and only
    // stops the drawing, so coming back is free and the crop the ViewPort is derived from never moves.
    static partial void MapShowPreview(CameraViewHandler handler, CameraView view)
    {
        if (handler.previewView != null)
            handler.previewView.Visibility = view.ShowPreview
                ? Android.Views.ViewStates.Visible
                : Android.Views.ViewStates.Invisible;
    }

    static partial void MapOverlay(CameraViewHandler handler, CameraView view) { /* Phase 2 */ }

    // No rebind: target rotation is settable on a bound use case, so this costs nothing and does not blink
    // the preview. Unlike VideoQuality it is therefore safe to call mid-recording — ApplyTargetRotation skips
    // VideoCapture while one is running.
    // CameraX picks the format for each use case; there is no equivalent knob to turn here.
    static partial void MapCaptureFormat(CameraViewHandler handler, CameraView view) { }

    static partial void MapOrientation(CameraViewHandler handler, CameraView view)
        => handler.ApplyTargetRotation();

    static partial void MapEffects(CameraViewHandler handler, CameraView view) => handler.ApplyEffects(view.EffectChain);

    // Quality is baked into the Recorder at bind time, so a change means a rebind. Refused mid-recording:
    // rebinding tears down the Recorder that owns the file being written, which would truncate the clip. The
    // new value lands on the next binding — the one the recording already triggers when it stops.
    static partial void MapVideoQuality(CameraViewHandler handler, CameraView view)
    {
        if (handler.cameraProvider != null && handler.activeRecording == null)
            handler.BindUseCases();
    }


    // Capture mode and the resolution selector are baked into ImageCapture at bind time, so a change means
    // a rebind - the same bargain as MapVideoQuality above, and refused mid-recording for the same reason.
    // PhotoJpegQuality rides along on the same rebind rather than getting a mapper of its own.
    static partial void MapPhotoQuality(CameraViewHandler handler, CameraView view)
    {
        if (handler.cameraProvider != null && handler.activeRecording == null)
            handler.BindUseCases();
    }


    // ---- internals ----

    void ApplyEffects(CameraEffectChain chain)
    {
        // RenderEffect is API 31+. Below that the preview stays clean while captured stills are still
        // filtered — reported as EffectSupport.StillOnly rather than silently doing nothing.
        if (this.previewView == null || !OperatingSystem.IsAndroidVersionAtLeast(31))
            return;

        this.previewView.SetRenderEffect(AndroidCameraFilters.CreatePreviewEffect(
            chain,
            // surface shader compile failures instead of silently dropping the effect
            message => this.MaybeVirtualView?.OnCameraError(message)));
    }

    void BindUseCases()
    {
        if (this.cameraProvider == null || this.previewView == null || this.lifecycleOwner == null)
            return;

        var preview = new Preview.Builder().Build();
        preview.SurfaceProvider = this.previewView.SurfaceProvider;

        var selectorBuilder = new CameraSelector.Builder();
        if (!string.IsNullOrEmpty(this.VirtualView.CameraId))
            selectorBuilder.AddCameraFilter(new CameraIdFilter(this.VirtualView.CameraId!));
        else
            selectorBuilder.RequireLensFacing(this.VirtualView.Facing == CameraFacing.Front
                ? CameraSelector.LensFacingFront
                : CameraSelector.LensFacingBack);
        var selector = selectorBuilder.Build();

        this.imageCapture = null;
        this.imageAnalysis = null;
        this.videoCapture = null;
        this.recorder = null;

        // Every use case below is built with this. Preview is excluded on purpose — see ApplyTargetRotation.
        var rotation = this.ResolveTargetRotation();

        // Use-case budget. CameraX guarantees Preview + 2 more at LIMITED hardware level; a fourth use case
        // needs LEVEL_3, which most phones are not. Preview + VideoCapture + ImageAnalysis IS a guaranteed
        // LIMITED combination, so analysis and recording are not actually mutually exclusive — a live-analysis
        // recorder (a dash cam reading signs or plates off its own feed) is supportable. What does not fit is
        // ImageCapture on top, so that is the one dropped, and only for as long as a recording is running.
        var analyzing = this.Pipeline.HasAnalyzer;
        var video = this.wantsVideoCapture || !analyzing;
        var useCases = new List<UseCase> { preview };

        if (!(analyzing && video))
        {
            // CAPTURE_MODE is fixed at build time, which is why PhotoQuality rebinds rather than being set
            // per shot the way FlashMode is. The resolution selector is what actually lifts the still off
            // the preview-sized default: without one CameraX picks a resolution to match the other bound
            // use cases, so a 1080p preview quietly capped the photo at 1080p.
            var captureBuilder = new ImageCapture.Builder()
                .SetTargetRotation(rotation)!
                .SetCaptureMode(this.VirtualView.PhotoQuality == PhotoQuality.Highest
                    ? ImageCapture.CaptureModeMaximizeQuality
                    : ImageCapture.CaptureModeMinimizeLatency)!
                .SetJpegQuality((int)System.Math.Round(this.VirtualView.EncoderJpegQuality * 100, MidpointRounding.AwayFromZero))!;

            if (this.VirtualView.PhotoQuality != PhotoQuality.Session)
            {
                // ResolutionStrategy.HighestAvailableStrategy asks for the sensor's largest supported still
                // and falls back down on its own, which is the behaviour PhotoQuality promises.
                captureBuilder = captureBuilder.SetResolutionSelector(
                    new AndroidX.Camera.Core.ResolutionSelector.ResolutionSelector.Builder()
                        .SetResolutionStrategy(AndroidX.Camera.Core.ResolutionSelector.ResolutionStrategy.HighestAvailableStrategy!)!
                        .Build()!
                )!;
            }

            this.imageCapture = captureBuilder.Build();
            useCases.Add(this.imageCapture);
        }

        if (analyzing)
        {
            this.analysisExecutor ??= Java.Util.Concurrent.Executors.NewSingleThreadExecutor();
            this.imageAnalysis = new ImageAnalysis.Builder()
                .SetBackpressureStrategy(ImageAnalysis.StrategyKeepOnlyLatest!)!
                .SetTargetRotation(rotation)!
                .Build();
            this.imageAnalysis.SetAnalyzer(this.analysisExecutor!, new FrameAnalyzerBridge(this));
            useCases.Add(this.imageAnalysis);
        }

        if (video)
        {
            this.recorder = this.BuildRecorder();
            this.videoCapture = this.BuildVideoCapture(this.recorder);
            this.videoCapture.TargetRotation = rotation;
            useCases.Add(this.videoCapture);
        }

        this.cameraProvider.UnbindAll();
        this.DisposeOverlayEffect();

        // A UseCaseGroup is needed for the burn-in overlay effect, and also whenever analysis and recording
        // run together so they can share a ViewPort (see BuildSharedViewPort).
        var overlayEffect = this.recordingOverlay is { } overlay && this.videoCapture != null
            ? this.CreateOverlayEffect(overlay)
            : null;
        var sharedViewPort = analyzing && video ? this.BuildSharedViewPort(rotation) : null;

        if (overlayEffect != null || sharedViewPort != null)
        {
            var groupBuilder = new UseCaseGroup.Builder();
            foreach (var uc in useCases)
                groupBuilder.AddUseCase(uc);
            if (overlayEffect != null)
                groupBuilder.AddEffect(overlayEffect);
            if (sharedViewPort != null)
                groupBuilder.SetViewPort(sharedViewPort);
            this.camera = this.cameraProvider.BindToLifecycle(this.lifecycleOwner, selector, groupBuilder.Build());
        }
        else
        {
            this.camera = this.cameraProvider.BindToLifecycle(this.lifecycleOwner, selector, useCases.ToArray());
        }
        this.boundShape = (analyzing, video);

        this.ApplyEffects(this.VirtualView.EffectChain);

        this.ReportZoomRange();
        this.camera.CameraControl.SetZoomRatio((float)this.VirtualView.Zoom);
        this.camera.CameraControl.EnableTorch(this.VirtualView.IsTorchOn);
    }


    void ReportZoomRange()
    {
        var zoomState = this.camera?.CameraInfo.ZoomState?.Value as IZoomState;
        if (zoomState != null)
            this.MaybeVirtualView?.OnZoomRangeChanged(zoomState.MinZoomRatio, zoomState.MaxZoomRatio);
    }


    /// <summary>
    /// Push the resolved target rotation onto the bound use cases.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CameraX takes target rotation per use case and it can be changed on a bound one — no rebind, no
    /// preview hiccup — which is what makes following the display cheap. Left unset it defaults to the
    /// display rotation <i>at the moment the use case was built</i> and never moves again: launch the app
    /// already in landscape and every recording came out rotated, with nothing in the code saying so.
    /// </para>
    /// <para>
    /// <b>Preview is deliberately not in the list.</b> <c>PreviewView</c> owns its own rotation handling and
    /// CameraX's guidance is not to fight it; setting a target rotation on the Preview use case while a
    /// PreviewView is displaying it distorts what the user sees. The shared ViewPort is given the resolved
    /// rotation directly instead (see <see cref="BuildSharedViewPort"/>).
    /// </para>
    /// <para>
    /// <b>VideoCapture is skipped while recording.</b> CameraX fixes the rotation when the recording starts,
    /// so setting it mid-take does nothing to the file in progress — but it would leave the use case
    /// disagreeing with the encoder for the remainder of it. <see cref="StopVideoRecordingAsync"/> rebinds
    /// once the recording finalizes, which picks up whatever the display reached in the meantime.
    /// </para>
    /// </remarks>
    void ApplyTargetRotation()
    {
        if (this.MaybeVirtualView == null)
            return;

        var rotation = this.ResolveTargetRotation();

        if (this.imageCapture != null)
            this.imageCapture.TargetRotation = rotation;

        if (this.imageAnalysis != null)
            this.imageAnalysis.TargetRotation = rotation;

        if (this.videoCapture != null && this.activeRecording == null)
            this.videoCapture.TargetRotation = rotation;
    }


    /// <summary>
    /// Turn <see cref="CameraView.Orientation"/> into a <c>Surface.ROTATION_*</c> value.
    /// </summary>
    /// <remarks>
    /// <see cref="CameraOrientation.Device"/> reads the display rotation, which is exact on any device.
    /// The explicit members go through the table on <see cref="CameraOrientation"/>, which assumes a
    /// portrait-natural device — true of every phone, not of every tablet.
    /// </remarks>
    int ResolveTargetRotation() => this.MaybeVirtualView?.Orientation switch
    {
        CameraOrientation.Portrait => (int)Android.Views.SurfaceOrientation.Rotation0,
        CameraOrientation.PortraitUpsideDown => (int)Android.Views.SurfaceOrientation.Rotation180,
        CameraOrientation.LandscapeTopLeft => (int)Android.Views.SurfaceOrientation.Rotation90,
        CameraOrientation.LandscapeTopRight => (int)Android.Views.SurfaceOrientation.Rotation270,
        _ => this.ReadDisplayRotation()
    };


    // The view's own display once it is attached; the default display before that (Display is null on a view
    // that is not in a window yet, which is the state during the first BindUseCases).
    int ReadDisplayRotation()
    {
        var display = this.previewView?.Display
            ?? (this.Context.GetSystemService(Android.Content.Context.DisplayService)
                as Android.Hardware.Display.DisplayManager)
                ?.GetDisplay(Android.Views.Display.DefaultDisplay);

        return (int)(display?.Rotation ?? Android.Views.SurfaceOrientation.Rotation0);
    }
}


/// <summary>
/// Fires when the display rotates, so capture orientation can follow it.
/// </summary>
/// <remarks>
/// A layout pass would be the cheaper signal and is what the Apple handler leans on, but it does not catch
/// the case that matters most here: rotating 180° — landscape-left to landscape-right, a cradle remounted
/// the other way up — leaves the view's bounds identical, so nothing re-lays out while the footage quietly
/// starts recording upside down. <c>DisplayManager</c> reports it. This is also what CameraX's own samples
/// use.
/// </remarks>
sealed class DisplayRotationListener(Action onRotated)
    : Java.Lang.Object, Android.Hardware.Display.DisplayManager.IDisplayListener
{
    Android.Hardware.Display.DisplayManager? manager;

    public static DisplayRotationListener? Register(Android.Content.Context context, Action onRotated)
    {
        if (context.GetSystemService(Android.Content.Context.DisplayService)
            is not Android.Hardware.Display.DisplayManager manager)
            return null;

        var listener = new DisplayRotationListener(onRotated) { manager = manager };
        manager.RegisterDisplayListener(listener, null);
        return listener;
    }

    public void Unregister()
    {
        this.manager?.UnregisterDisplayListener(this);
        this.manager = null;
    }

    public void OnDisplayAdded(int displayId) { }

    public void OnDisplayRemoved(int displayId) { }

    public void OnDisplayChanged(int displayId) => onRotated();
}
