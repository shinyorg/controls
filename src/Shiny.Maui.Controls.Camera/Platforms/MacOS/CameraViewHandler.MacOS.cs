using AppKit;
using AVFoundation;
using CoreAnimation;
using CoreFoundation;
using CoreGraphics;
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
        var view = new NSView { WantsLayer = true };
        view.Layer ??= new CALayer();
        return view;
    }

    protected override void ConnectHandler(NSView platformView)
    {
        base.ConnectHandler(platformView);
        this.InitPipeline();
        if (this.VirtualView.IsActive)
            _ = this.StartAsync();
    }

    protected override void DisconnectHandler(NSView platformView)
    {
        this.TeardownPipeline();
        this.TeardownSession();
        base.DisconnectHandler(platformView);
    }


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

        this.movieOutput = new AVCaptureMovieFileOutput();
        if (this.session.CanAddOutput(this.movieOutput))
            this.session.AddOutput(this.movieOutput);

        this.session.CommitConfiguration();

        this.MainThread(() =>
        {
            this.SetupLayers();
            this.dataOutput.SetSampleBufferDelegate(this.frameDelegate, this.videoQueue);
            this.ApplyEffects(this.VirtualView.EffectChain);
        });
    }


    void SetupLayers()
    {
        var host = this.PlatformView;
        host.AutoresizesSubviews = true;

        this.previewLayer = new AVCaptureVideoPreviewLayer(this.session!)
        {
            VideoGravity = AVLayerVideoGravity.ResizeAspectFill,
            Frame = host.Bounds,
            AutoresizingMask = CAAutoresizingMask.WidthSizable | CAAutoresizingMask.HeightSizable
        };
        host.Layer!.AddSublayer(this.previewLayer);

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
        var active = filters.Length > 0;
        this.filterView.Hidden = !active;
        this.previewLayer.Hidden = active;
    }


    static readonly AVCaptureDeviceType[] DeviceTypes =
    [
        AVCaptureDeviceType.BuiltInWideAngleCamera,
        AVCaptureDeviceType.ExternalUnknown
    ];

    public Task<IReadOnlyList<CameraInfo>> GetAvailableCamerasAsync(CancellationToken ct = default)
    {
        var discovery = AVCaptureDeviceDiscoverySession.Create(DeviceTypes, AVMediaTypes.Video, AVCaptureDevicePosition.Unspecified);
        IReadOnlyList<CameraInfo> list = discovery.Devices
            .Select(d => new CameraInfo(d.UniqueID, d.LocalizedName, ToFacing(d.Position)))
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

    void AddVideoInput()
    {
        this.device = this.SelectDevice();
        if (this.device == null)
        {
            this.MainThread(() => this.MaybeVirtualView?.OnCameraError("No camera device found"));
            return;
        }

        var input = AVCaptureDeviceInput.FromDevice(this.device, out var error);
        if (error != null || input == null)
        {
            this.MainThread(() => this.MaybeVirtualView?.OnCameraError("Cannot open camera: " + error?.LocalizedDescription));
            return;
        }

        if (this.session!.CanAddInput(input))
            this.session.AddInput(input);
        this.videoInput = input;
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
