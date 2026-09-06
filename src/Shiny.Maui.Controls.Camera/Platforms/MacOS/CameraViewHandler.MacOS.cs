using AppKit;
using AVFoundation;
using CoreAnimation;
using CoreFoundation;
using CoreGraphics;
using CoreVideo;
using Foundation;
using Microsoft.Maui.Handlers;

namespace Shiny.Maui.Controls.Camera;

// macOS AppKit (NSView / AVFoundation). Best-effort: AVFoundation bindings are solid, but the MAUI
// macOS host is preview-quality, so layout/permission edge cases may need on-device tuning.
public partial class CameraViewHandler : ViewHandler<CameraView, NSView>, ICameraViewController
{
    AVCaptureSession? session;
    AVCaptureDeviceInput? videoInput;
    AVCaptureDeviceInput? audioInput;
    AVCapturePhotoOutput? photoOutput;
    AVCaptureMovieFileOutput? movieOutput;
    AVCaptureVideoDataOutput? dataOutput;
    AVCaptureAudioDataOutput? audioDataOutput;
    AppleAudioDelegate? audioDelegate;
    AppleVideoOverlayRecorder? overlayRecorder;
    AVCaptureVideoPreviewLayer? previewLayer;
    AVCaptureDevice? device;
    MacVideoFrameDelegate? frameDelegate;
    MovieRecordingDelegate? recordingDelegate;
    NSImageView? filterView;
    readonly DispatchQueue sessionQueue = new("shiny.camera.session");
    readonly DispatchQueue videoQueue = new("shiny.camera.video");

    protected override NSView CreatePlatformView()
    {
        var view = new CameraHostView { WantsLayer = true };
        view.Layer ??= new CALayer();
        view.LaidOut = this.LayoutPreview;
        return view;
    }

    /// <summary>
    /// An NSView that says when it has been laid out.
    /// </summary>
    /// <remarks>
    /// The preview is a <see cref="AVCaptureVideoPreviewLayer"/>, and a CALayer sublayer does not
    /// resize with the view that hosts it. <c>AutoresizingMask</c> looks like it should handle that
    /// and does not: on macOS a layer's autoresizing mask is only honoured by a superlayer with a
    /// layout manager, and a plain layer-backed NSView has none - so the mask is set, ignored, and
    /// the preview keeps whatever frame it was born with.
    /// <para>
    /// Which is empty. The session starts as the handler connects, before the view has been given a
    /// size, so the layer was created at 0x0 and stayed there: a camera that was running, capturing
    /// full-resolution stills, and showing a black rectangle. The subview used for filtered frames
    /// never had the problem, because NSView autoresizing is real.
    /// </para>
    /// </remarks>
    sealed class CameraHostView : NSView
    {
        public Action? LaidOut { get; set; }

        public override void Layout()
        {
            base.Layout();
            this.LaidOut?.Invoke();
        }
    }

    /// <summary>
    /// Takes the size MAUI has arranged this view at.
    /// </summary>
    /// <remarks>
    /// The one place the real size is known. The AppKit backend leaves the platform NSView's bounds
    /// at zero - MAUI's own layout says 1080x485 while <c>host.Bounds</c> reports 0x0 - so the
    /// preview layer, which is sized from those bounds, was born empty and stayed empty. A camera
    /// that ran, captured stills correctly and drew nothing.
    /// <para>
    /// The frame is pushed onto the view here rather than only onto the layer, because the filtered
    /// preview is an NSImageView subview and is sized by the same zero bounds.
    /// </para>
    /// </remarks>
    public override void PlatformArrange(Rect frame)
    {
        base.PlatformArrange(frame);

        // Every arrange, not only the first. The view keeps whatever size it was given, so a preview
        // sized once is a preview that never follows the window again - and the first arrange is
        // rarely the final one, which showed up as a band of black under the picture.
        if (this.PlatformView is { } host && frame is { Width: > 0, Height: > 0 })
        {
            var size = new CGSize(frame.Width, frame.Height);
            if (Math.Abs(host.Bounds.Width - size.Width) > 0.5 || Math.Abs(host.Bounds.Height - size.Height) > 0.5)
                host.SetFrameSize(size);
        }

        this.LayoutPreview();
    }

    /// <summary>Keeps the preview layer the size of the view it is drawn in.</summary>
    /// <remarks>
    /// Inside a transaction with actions off, or every layout pass animates the layer into its new
    /// frame over a quarter of a second - which on a window resize is a preview that visibly lags
    /// the window edge.
    /// </remarks>
    void LayoutPreview()
    {
        if (this.previewLayer is not { } layer)
            return;

        var host = this.PlatformView;
        host.WantsLayer = true;

        if (host.Layer is not { } root)
            return;

        // Re-attached if it has been orphaned. A layer-backed NSView can be handed a fresh backing
        // layer after this handler added its sublayer to the old one - the preview then still
        // exists, still has a session, and is drawn into a layer that is no longer on screen.
        if (!ReferenceEquals(layer.SuperLayer, root))
            root.AddSublayer(layer);

        var bounds = host.Bounds;

        // The platform view can still be reporting nothing - see PlatformArrange - so the size MAUI
        // arranged this view at is the better answer whenever AppKit has not caught up.
        if ((bounds.Width < 1 || bounds.Height < 1) && this.MaybeVirtualView?.Frame is { Width: > 0, Height: > 0 } arranged)
            bounds = new CGRect(0, 0, arranged.Width, arranged.Height);

        CATransaction.Begin();
        CATransaction.DisableActions = true;
        layer.Frame = bounds;
        if (this.filterView is { } filter)
            filter.Frame = bounds;
        CATransaction.Commit();

    }

    protected override void ConnectHandler(NSView platformView)
    {
        base.ConnectHandler(platformView);

        // AppKit posts this whenever the frame changes, however it was changed - which is the only
        // hook here that does not depend on the MAUI backend choosing to call something. Overriding
        // Layout() is not enough on its own: a backend that positions views by assigning Frame
        // never triggers a layout pass, and the preview then keeps the size it was created at.
        platformView.PostsFrameChangedNotifications = true;
        this.frameObserver = NSNotificationCenter.DefaultCenter.AddObserver(
            NSView.FrameChangedNotification,
            _ => this.LayoutPreview(),
            platformView
        );

        this.InitPipeline();
        if (this.VirtualView.IsActive)
            _ = this.StartAsync();
    }

    protected override void DisconnectHandler(NSView platformView)
    {
        if (this.frameObserver is { } observer)
        {
            NSNotificationCenter.DefaultCenter.RemoveObserver(observer);
            observer.Dispose();
            this.frameObserver = null;
        }

        this.TeardownPipeline();
        this.TeardownSession();
        base.DisconnectHandler(platformView);
    }

    NSObject? frameObserver;


    public Task<bool> RequestPermissionAsync(CancellationToken ct = default)
    {
        var status = AVCaptureDevice.GetAuthorizationStatus(AVAuthorizationMediaType.Video);
        return status == AVAuthorizationStatus.Authorized
            ? Task.FromResult(true)
            : AVCaptureDevice.RequestAccessForMediaTypeAsync(AVAuthorizationMediaType.Video);
    }


    public async Task StartAsync(CancellationToken ct = default)
    {
        if (!await this.RequestPermissionAsync(ct).ConfigureAwait(false))
        {
            this.MaybeVirtualView?.OnCameraError("Camera permission denied");
            return;
        }

        this.sessionQueue.DispatchAsync(() =>
        {
            try
            {
                this.ConfigureSession();
                if (this.session is { Running: false })
                    this.session.StartRunning();
            }
            catch (Exception ex)
            {
                this.MainThread(() => this.MaybeVirtualView?.OnCameraError("Failed to start camera", ex));
            }
        });
    }


    public Task StopAsync(CancellationToken ct = default)
    {
        this.OnSessionQueue(
            () =>
            {
                if (this.session is { Running: true })
                    this.session.StopRunning();
            },
            "The camera could not be stopped"
        );
        return Task.CompletedTask;
    }


    public Task<CameraPhoto> CapturePhotoAsync(CancellationToken ct = default)
    {
        if (this.photoOutput == null)
            throw new InvalidOperationException("Camera is not running");

        var settings = ApplePhotoQuality.CreateSettings(this.photoOutput, this.device, this.VirtualView.PhotoQuality);
        var del = new PhotoCaptureDelegate
        {
            // apply the same effects as the live preview so the captured still matches what the user sees
            Filters = AppleCameraFilters.Create(this.VirtualView.EffectChain),
            JpegQuality = this.VirtualView.EncoderJpegQuality
        };
        this.photoOutput.CapturePhoto(settings, del);
        return del.Task;
    }


    public Task StartVideoRecordingAsync(VideoRecordingOptions options, CancellationToken ct = default)
    {
        if (this.session is not { Running: true } || this.frameDelegate == null)
            throw new InvalidOperationException("Camera is not running");

        var path = options.FilePath ?? Path.Combine(Path.GetTempPath(), $"shiny-{Guid.NewGuid():N}.mov");

        // Anything to composite (effects or a legacy overlay) -> owned AVAssetWriter path; nothing to composite
        // -> fast native AVCaptureMovieFileOutput path.
        var chain = this.VirtualView.EffectChain;
        if (options.Overlay != null || !chain.IsEmpty)
        {
            var recorder = new AppleVideoOverlayRecorder(
                path,
                options.IncludeAudio,
                this.VirtualView.Facing,
                chain,
                options.Overlay,
                this.VirtualView.VideoBitrate
            )
            {
                AnalyzerSnapshot = this.Pipeline.Snapshot
            };
            if (options.IncludeAudio)
            {
                this.EnsureAudioInput();
                this.EnsureAudioDataOutput(recorder);
            }
            this.overlayRecorder = recorder;
            this.frameDelegate.Recorder = recorder;
            return Task.CompletedTask;
        }

        if (this.movieOutput == null)
            throw new InvalidOperationException("Camera is not running");
        if (options.IncludeAudio)
            this.EnsureAudioInput();
        this.recordingDelegate = new MovieRecordingDelegate();
        this.movieOutput.StartRecordingToOutputFile(NSUrl.FromFilename(path), this.recordingDelegate);
        return Task.CompletedTask;
    }


    public Task<CameraVideo> StopVideoRecordingAsync(CancellationToken ct = default)
    {
        if (this.overlayRecorder is { } recorder)
        {
            if (this.frameDelegate != null)
                this.frameDelegate.Recorder = null;
            if (this.audioDelegate != null)
                this.audioDelegate.Recorder = null;
            this.overlayRecorder = null;
            return recorder.FinishAsync();
        }

        if (this.movieOutput is not { Recording: true } || this.recordingDelegate == null)
            throw new InvalidOperationException("Not recording");

        this.movieOutput.StopRecording();
        return this.recordingDelegate.Task;
    }


    // Add an AVCaptureAudioDataOutput feeding the overlay recorder's AVAssetWriter (burn-in path only).
    void EnsureAudioDataOutput(AppleVideoOverlayRecorder recorder)
    {
        if (this.session == null)
            return;

        this.audioDelegate ??= new AppleAudioDelegate();
        this.audioDelegate.Recorder = recorder;

        if (this.audioDataOutput == null)
        {
            var output = new AVCaptureAudioDataOutput();
            this.session.BeginConfiguration();
            if (this.session.CanAddOutput(output))
            {
                this.session.AddOutput(output);
                this.audioDataOutput = output;
            }
            this.session.CommitConfiguration();
            this.audioDataOutput?.SetSampleBufferDelegate(this.audioDelegate, this.videoQueue);
        }
    }


    static partial void MapFacing(CameraViewHandler handler, CameraView view)
        => handler.OnSessionQueue(handler.ReconfigureInput, "The camera could not be changed");

    static partial void MapIsActive(CameraViewHandler handler, CameraView view)
    {
        if (view.IsActive)
            _ = handler.StartAsync();
        else
            _ = handler.StopAsync();
    }

    static partial void MapTorch(CameraViewHandler handler, CameraView view) { /* macOS cameras lack torch */ }
    static partial void MapFlashMode(CameraViewHandler handler, CameraView view) { }

    // As on iOS: the photo output's ceiling covers every rung, so the choice is made per capture.
    static partial void MapPhotoQuality(CameraViewHandler handler, CameraView view) { /* applied at capture time */ }

    static partial void MapZoom(CameraViewHandler handler, CameraView view) { /* macOS cameras lack zoom */ }

    static partial void MapScaleMode(CameraViewHandler handler, CameraView view)
    {
        if (handler.previewLayer != null)
            handler.previewLayer.VideoGravity = view.ScaleMode == PreviewScaleMode.AspectFit
                ? AVLayerVideoGravity.ResizeAspect
                : AVLayerVideoGravity.ResizeAspectFill;
    }

    // Display path only — the session, the analyzer and any recording in flight carry on. Hiding stops the
    // compositing; disabling the connection is what stops the session feeding the layer at all.
    static partial void MapShowPreview(CameraViewHandler handler, CameraView view)
    {
        if (handler.previewLayer is not { } layer)
            return;

        layer.Hidden = !view.ShowPreview;

        if (layer.Connection is { } connection)
            connection.Enabled = view.ShowPreview;
    }

    static partial void MapOverlay(CameraViewHandler handler, CameraView view) { /* drawn by managed overlay */ }

    // Nothing to do: a Mac's display does not rotate behind the capture device, so there is no orientation
    // to follow and nothing an explicit CameraOrientation could usefully mean here.
    static partial void MapOrientation(CameraViewHandler handler, CameraView view) { }

    // The capture format is an Apple concern — its data output is the only one asked for a pixel format.
    static partial void MapCaptureFormat(CameraViewHandler handler, CameraView view) { }

    // Same session-level reconfiguration as iOS, and refused mid-recording for the same reason — the
    // preset renegotiates the active format underneath whatever is writing the file.
    static partial void MapVideoQuality(CameraViewHandler handler, CameraView view)
        => handler.OnSessionQueue(handler.ApplyVideoSettings, "The capture settings could not be applied");


    void ApplyVideoSettings()
    {
        if (this.session is not { } s || this.overlayRecorder != null || this.movieOutput is { Recording: true })
            return;

        var preset = this.ResolvePreset();
        if (s.SessionPreset != preset && s.CanSetSessionPreset(preset))
        {
            s.BeginConfiguration();
            s.SessionPreset = preset;
            s.CommitConfiguration();
        }

        // The still dimensions the photo output may ask for belong to the active format, which the preset
        // above may just have changed.
        if (this.photoOutput is { } photo)
            ApplePhotoQuality.ConfigureOutput(photo, this.device);
    }


    /// <summary>
    /// The session preset for the requested quality, walking down the ladder until the device accepts one.
    /// </summary>
    /// <remarks>
    /// Mac cameras vary more than iPhone ones — a built-in FaceTime camera, a Continuity iPhone and a USB
    /// capture device have very different ladders — so an unsupported preset has to be discovered rather than
    /// assumed. Assigning one throws, hence <c>CanSetSessionPreset</c> before each attempt.
    /// </remarks>
    NSString ResolvePreset()
    {
        var ladder = this.MaybeVirtualView?.VideoQuality switch
        {
            VideoQuality.Lowest => new[] { AVCaptureSession.PresetLow, AVCaptureSession.Preset352x288, AVCaptureSession.Preset640x480 },
            VideoQuality.Low => new[] { AVCaptureSession.Preset640x480, AVCaptureSession.PresetMedium, AVCaptureSession.PresetLow },
            VideoQuality.Medium => new[] { AVCaptureSession.Preset1280x720, AVCaptureSession.Preset640x480, AVCaptureSession.PresetMedium },
            VideoQuality.UltraHigh => new[] { AVCaptureSession.Preset3840x2160, AVCaptureSession.Preset1920x1080, AVCaptureSession.Preset1280x720 },
            VideoQuality.Highest => new[] { AVCaptureSession.PresetHigh, AVCaptureSession.Preset1920x1080 },
            _ => new[] { AVCaptureSession.Preset1920x1080, AVCaptureSession.Preset1280x720, AVCaptureSession.PresetHigh }
        };

        foreach (var preset in ladder)
        {
            if (this.session?.CanSetSessionPreset(preset) != false)
                return preset;
        }

        return AVCaptureSession.PresetHigh;
    }


    static partial void MapEffects(CameraViewHandler handler, CameraView view)
        => handler.MainThread(() => handler.ApplyEffects(view.EffectChain));


    void ConfigureSession()
    {
        if (this.session != null)
            return;

        this.session = new AVCaptureSession { SessionPreset = this.ResolvePreset() };
        this.session.BeginConfiguration();
        this.AddVideoInput();

        this.photoOutput = new AVCapturePhotoOutput();
        if (this.session.CanAddOutput(this.photoOutput))
            this.session.AddOutput(this.photoOutput);

        // After AddOutput: the dimension ceiling is a question about this output on this session, and it
        // answers nothing until the output is attached.
        ApplePhotoQuality.ConfigureOutput(this.photoOutput, this.device);

        this.dataOutput = new AVCaptureVideoDataOutput { AlwaysDiscardsLateVideoFrames = true };
        if (this.session.CanAddOutput(this.dataOutput))
            this.session.AddOutput(this.dataOutput);

        // After AddOutput, not before: what a data output can deliver is a question about that output
        // on this session, and it answers nothing until it is attached.
        this.ApplyCaptureFormat(this.dataOutput);

        this.movieOutput = new AVCaptureMovieFileOutput();
        if (this.session.CanAddOutput(this.movieOutput))
            this.session.AddOutput(this.movieOutput);

        this.session.CommitConfiguration();

        this.MainThread(() =>
        {
            // A handler torn down while its session was still starting has no platform view left to
            // build layers on, and reaching for one throws. Nothing here is worth taking the process
            // down for: the session is on its way out with the handler.
            if (this.MaybeVirtualView is null)
                return;

            this.SetupLayers();
            this.dataOutput.SetSampleBufferDelegate(this.frameDelegate, this.videoQueue);
            this.ApplyEffects(this.VirtualView.EffectChain);
        });
    }


    /// <summary>
    /// Builds the preview, the surface filtered frames are drawn on, and the frame delegate.
    /// </summary>
    /// <remarks>
    /// Every one of those three is created here, which is why the first line matters so much. This
    /// used to reach for <c>host.Layer</c> with a null-forgiving operator, and a layer-backed NSView
    /// is not a guarantee that a backing layer exists yet - a host handed back a null there threw
    /// before the filter view and the frame delegate were built. The symptom was not an error: the
    /// session ran, stills captured perfectly, and the preview was black, filtered or not, with no
    /// frames reaching a remote viewer either, because the delegate that delivers them was never
    /// made. Asking for the layer properly is the difference.
    /// </remarks>
    void SetupLayers()
    {
        var host = this.PlatformView;
        host.AutoresizesSubviews = true;

        // WantsLayer makes AppKit create one; the fallback is for a host that still answers null.
        host.WantsLayer = true;
        host.Layer ??= new CALayer();

        this.previewLayer = new AVCaptureVideoPreviewLayer(this.session!)
        {
            VideoGravity = AVLayerVideoGravity.ResizeAspectFill,
            Frame = host.Bounds,
            AutoresizingMask = CAAutoresizingMask.WidthSizable | CAAutoresizingMask.HeightSizable
        };
        host.Layer.AddSublayer(this.previewLayer);

        // The bounds above are whatever the view had when the session started, which is usually
        // nothing at all - see CameraHostView. Every layout pass from here fixes it, and this is
        // the one for a session started after the view already had a size.
        this.LayoutPreview();

        this.filterView = new NSImageView
        {
            Frame = host.Bounds,
            AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable,
            ImageScaling = NSImageScale.AxesIndependently,
            Hidden = true
        };
        host.AddSubview(this.filterView);

        this.frameDelegate = new MacVideoFrameDelegate(this.filterView)
        {
            WantFrames = () => this.Pipeline.WantsFrame(),
            OnFrame = frame => this.Pipeline.Process(frame, default),
            Mirrored = this.VirtualView.Facing == CameraFacing.Front
        };
    }


    void ApplyEffects(CameraEffectChain chain)
    {
        if (this.frameDelegate == null || this.filterView == null || this.previewLayer == null)
            return;

        var filters = AppleCameraFilters.Create(chain);
        this.frameDelegate.Filters = filters;
        this.frameDelegate.AnalyzerSeesEffects = this.MaybeVirtualView?.AnalyzerSeesEffects == true;
        var active = filters.Length > 0;
        this.filterView.Hidden = !active;
        this.previewLayer.Hidden = active;
    }


    /// <summary>
    /// The kinds of camera discovery is asked for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Built once at runtime rather than written as a constant, because three of these five did not
    /// exist on every macOS this package supports and a discovery session cannot be handed a device
    /// type the running system has never heard of.
    /// </para>
    /// <para>
    /// What each one is for. <b>BuiltInWideAngleCamera</b> is the one every Mac has. <b>External</b>
    /// is a USB or virtual camera on macOS 14 and later; <b>ExternalUnknown</b> is what the same
    /// device was called before that, and both are asked for because the constant a device reports
    /// itself as follows the OS it is plugged into rather than this list. <b>ContinuityCamera</b> is
    /// an iPhone being used as the Mac's camera, which is the one most people actually reach for
    /// and the one whose absence was hardest to explain - it is a camera in every menu Apple ships
    /// and was in none of ours. <b>DeskViewCamera</b> is the overhead view that rides along with a
    /// Continuity Camera, which is a separate device to AVFoundation and so has to be named
    /// separately here.
    /// </para>
    /// <para>
    /// A device is returned once however many of these it matches, so asking for the old name and
    /// the new one cannot list the same webcam twice.
    /// </para>
    /// </remarks>
    static readonly AVCaptureDeviceType[] DeviceTypes = BuildDeviceTypes();

    static AVCaptureDeviceType[] BuildDeviceTypes()
    {
        var types = new List<AVCaptureDeviceType>
        {
            AVCaptureDeviceType.BuiltInWideAngleCamera,

            // No version floor on this one, and it stays in the list on newer systems too: it is
            // the name the same hardware answered to before macOS 14 renamed it.
            AVCaptureDeviceType.ExternalUnknown
        };

        // The overhead desk view. Older than the rest by a release, and macOS-only - there is no
        // Catalyst or iOS equivalent to guard for here.
        if (OperatingSystem.IsMacOSVersionAtLeast(13))
            types.Add(AVCaptureDeviceType.DeskViewCamera);

        if (OperatingSystem.IsMacOSVersionAtLeast(14))
        {
            types.Add(AVCaptureDeviceType.External);
            types.Add(AVCaptureDeviceType.ContinuityCamera);
        }

        return [.. types];
    }

    public Task<IReadOnlyList<CameraInfo>> GetAvailableCamerasAsync(CancellationToken ct = default)
    {
        var discovery = AVCaptureDeviceDiscoverySession.Create(DeviceTypes, AVMediaTypes.Video, AVCaptureDevicePosition.Unspecified);

        // Which one the system would pick if nobody chose. Carried so that an app opening a picker
        // over four cameras can start on the right one rather than on whichever enumerated first -
        // and it is the only way to tell, since none of them is meaningfully "front" or "back".
        var standard = AVCaptureDevice.GetDefaultDevice(AVMediaTypes.Video)?.UniqueID;

        IReadOnlyList<CameraInfo> list = discovery.Devices
            .Select(d => new CameraInfo(
                d.UniqueID,
                d.LocalizedName,
                ToFacing(d.Position),
                IsDefault: d.UniqueID == standard
            ))
            .ToList();

        return Task.FromResult(list);
    }

    static CameraFacing ToFacing(AVCaptureDevicePosition position) => position switch
    {
        AVCaptureDevicePosition.Front => CameraFacing.Front,
        AVCaptureDevicePosition.Back => CameraFacing.Back,
        _ => CameraFacing.External
    };

    AVCaptureDevice? SelectDevice()
    {
        var id = this.VirtualView.CameraId;
        if (!string.IsNullOrEmpty(id) && AVCaptureDevice.DeviceWithUniqueID(id) is { } byId)
            return byId;

        var position = this.VirtualView.Facing == CameraFacing.Front
            ? AVCaptureDevicePosition.Front
            : AVCaptureDevicePosition.Back;
        return DiscoverDevice(position) ?? DiscoverDevice(AVCaptureDevicePosition.Unspecified);
    }

    /// <summary>
    /// Opens the selected camera and attaches it to the session.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every failure here puts the camera that was working back. This runs from
    /// <see cref="ReconfigureInput"/>, which has already taken the old input out - so a switch to a
    /// camera that will not open used to end with a session that had no video input at all, and
    /// nothing put one back. The preview went black and stayed black, through every later switch,
    /// because each one started by removing an input that was no longer there and then failed the
    /// same way. A camera in use by another app is enough to trigger it, which on a Mac means any
    /// virtual camera whose host application is running.
    /// </para>
    /// <para>
    /// The old <c>CanAddInput</c> check had the same shape of bug in one line: the input was only
    /// added when the session would take it, and <c>videoInput</c> was assigned either way - so a
    /// refusal was recorded as a success and the next reconfigure tried to remove an input the
    /// session had never been given.
    /// </para>
    /// </remarks>
    void AddVideoInput()
    {
        var previous = this.device;
        var wanted = this.SelectDevice();

        if (wanted == null)
        {
            this.Report("No camera device found");
            this.Restore(previous);
            return;
        }

        if (!this.TryAddInput(wanted, out var problem))
        {
            this.Report(problem);

            // Back to whatever was on screen a moment ago, if it is not the one that just failed.
            if (previous is not null && previous.UniqueID != wanted.UniqueID)
                this.Restore(previous);

            return;
        }

        this.device = wanted;
    }

    /// <summary>Opens one device and hands it to the session.</summary>
    /// <returns>False with a reason, leaving the session exactly as it was found.</returns>
    bool TryAddInput(AVCaptureDevice camera, out string problem)
    {
        var input = AVCaptureDeviceInput.FromDevice(camera, out var error);

        if (error != null || input == null)
        {
            problem = $"Cannot open {camera.LocalizedName}: {error?.LocalizedDescription ?? "the device did not open"}";
            input?.Dispose();
            return false;
        }

        if (!this.session!.CanAddInput(input))
        {
            problem = $"This session will not take {camera.LocalizedName}.";
            input.Dispose();
            return false;
        }

        this.session.AddInput(input);
        this.videoInput = input;
        problem = string.Empty;
        return true;
    }

    /// <summary>Puts a camera back after a failed switch, quietly - the refusal was already said.</summary>
    void Restore(AVCaptureDevice? camera)
    {
        if (camera is null || !this.TryAddInput(camera, out _))
            return;

        this.device = camera;
    }

    /// <summary>
    /// Says something went wrong, without waiting for the main thread to hear it.
    /// </summary>
    /// <remarks>
    /// Asynchronously on purpose. This is called from work running on the session queue, sometimes
    /// between BeginConfiguration and CommitConfiguration, and the blocking hop the rest of this
    /// class uses would hold that queue until the main thread answers - which is a deadlock the
    /// moment the main thread is itself waiting on the session queue. The camera then never
    /// recovers, and there is nothing on screen to say why.
    /// </remarks>
    void Report(string message)
        => NSApplication.SharedApplication.BeginInvokeOnMainThread(
            () => this.MaybeVirtualView?.OnCameraError(message)
        );


    /// <summary>
    /// Asks the data output for BGRA.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without this the output delivers whatever macOS defaults to, which is a packed YCbCr format
    /// rather than the BGRA every consumer of <see cref="AppleCameraFrame"/> assumes.
    /// <c>ToCGImage</c> then builds a <c>CGBitmapContext</c> over the buffer with 32-bit BGRA
    /// parameters, the sizes do not describe the pixels, and construction fails with
    /// "handle is null" on every single frame.
    /// </para>
    /// <para>
    /// It went unnoticed because the two paths that draw locally never go near it: the plain preview
    /// is an AVCaptureVideoPreviewLayer the session drives itself, and the filtered preview reads
    /// the buffer through CIImage, which copes with any format. Only a frame analyzer sees the
    /// difference - so the camera looked perfect on the Mac while a remote viewfinder watching the
    /// same camera got nothing at all, frame after frame, with the failure swallowed as one dropped
    /// frame each time.
    /// </para>
    /// <para>
    /// iOS has always set this (see the Apple handler, which also honours CaptureFormat.Yuv420).
    /// Only BGRA is asked for here: the biplanar path exists for analyzers that want luma without a
    /// conversion, and nothing on this head asks for it yet.
    /// </para>
    /// </remarks>
    void ApplyCaptureFormat(AVCaptureVideoDataOutput output)
    {
        try
        {
            output.WeakVideoSettings = new CVPixelBufferAttributes
            {
                PixelFormatType = CVPixelFormatType.CV32BGRA
            }.Dictionary;
        }
        catch (Exception ex)
        {
            this.Report($"Camera capture format could not be applied: {ex.Message}");
        }
    }

    void ReconfigureInput()
    {
        if (this.session == null)
            return;

        this.session.BeginConfiguration();
        if (this.videoInput != null)
        {
            this.session.RemoveInput(this.videoInput);
            this.videoInput.Dispose();
            this.videoInput = null;
        }
        this.AddVideoInput();
        this.session.CommitConfiguration();

        // A different camera is a different active format, so the photo output's ceiling has to be
        // re-declared against it. Unlike the Apple handler this one does not route through
        // ApplyVideoSettings, so it is asked for directly. Macs swap between a built-in FaceTime camera, a
        // Continuity iPhone and USB capture devices whose still capabilities differ enormously.
        if (this.photoOutput is { } photo)
            ApplePhotoQuality.ConfigureOutput(photo, this.device);

        this.MainThread(() =>
        {
            if (this.frameDelegate != null)
                this.frameDelegate.Mirrored = this.VirtualView.Facing == CameraFacing.Front;
        });
    }


    void EnsureAudioInput()
    {
        if (this.audioInput != null || this.session == null)
            return;

        var mic = AVCaptureDevice.GetDefaultDevice(AVMediaTypes.Audio);
        if (mic == null)
            return;

        var input = AVCaptureDeviceInput.FromDevice(mic, out var err);
        if (err != null || input == null)
            return;

        this.session.BeginConfiguration();
        if (this.session.CanAddInput(input))
        {
            this.session.AddInput(input);
            this.audioInput = input;
        }
        this.session.CommitConfiguration();
    }


    static AVCaptureDevice? DiscoverDevice(AVCaptureDevicePosition position)
    {
        var discovery = AVCaptureDeviceDiscoverySession.Create(DeviceTypes, AVMediaTypes.Video, position);
        return discovery.Devices.FirstOrDefault();
    }


    /// <summary>
    /// Runs work on the session queue with the failure reported rather than thrown.
    /// </summary>
    /// <remarks>
    /// ⚠️ Load-bearing, not tidiness. An exception that escapes a <c>DispatchAsync</c> block has no
    /// caller to travel to - GCD invokes the block from a worker thread with nothing above it - so
    /// the runtime treats it as unhandled and aborts the process. A camera that cannot reconfigure
    /// itself must degrade to a message on the view, never to a crash, and every dispatch onto this
    /// queue has to go through here to be sure of that.
    /// </remarks>
    void OnSessionQueue(Action work, string whatFailed)
        => this.sessionQueue.DispatchAsync(() =>
        {
            try
            {
                work();
            }
            catch (Exception ex)
            {
                this.MainThread(() => this.MaybeVirtualView?.OnCameraError(whatFailed, ex));
            }
        });


    void TeardownSession()
    {
        this.OnSessionQueue(() =>
        {
            if (this.session == null)
                return;
            if (this.movieOutput is { Recording: true })
                this.movieOutput.StopRecording();
            if (this.session.Running)
                this.session.StopRunning();
            this.session.Dispose();
            this.session = null;
            this.videoInput?.Dispose();
            this.videoInput = null;
            this.audioInput?.Dispose();
            this.audioInput = null;
            this.photoOutput = null;
            this.movieOutput = null;
            this.dataOutput = null;
            this.audioDataOutput?.Dispose();
            this.audioDataOutput = null;
            this.overlayRecorder = null;
            this.device = null;
        }, "The camera could not be shut down cleanly");
    }


    void MainThread(Action action) => NSApplication.SharedApplication.InvokeOnMainThread(action);
}
