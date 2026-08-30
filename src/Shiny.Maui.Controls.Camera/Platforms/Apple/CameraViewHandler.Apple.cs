using AVFoundation;
using CoreFoundation;
using CoreGraphics;
using CoreMedia;
using CoreVideo;
using Foundation;
using Microsoft.Maui.Handlers;
using UIKit;

namespace Shiny.Maui.Controls.Camera;

// iOS + MacCatalyst (UIKit / AVFoundation). Shared via the csproj Platforms/Apple include.
public partial class CameraViewHandler : ViewHandler<CameraView, CameraPreviewView>, ICameraViewController
{
    AVCaptureSession? session;
    AVCaptureDeviceInput? videoInput;
    AVCaptureDeviceInput? audioInput;
    AVCapturePhotoOutput? photoOutput;
    AVCaptureMovieFileOutput? movieOutput;
    AVCaptureVideoDataOutput? dataOutput;
    AVCaptureAudioDataOutput? audioDataOutput;
    AppleAudioDelegate? audioDelegate;
    AppleCaptureAudioSession? appAudioSession;
    AppleVideoOverlayRecorder? overlayRecorder;
    AVCaptureDevice? device;
    VideoFrameDelegate? frameDelegate;
    MovieRecordingDelegate? recordingDelegate;
    UIImageView? filterView;
    NSObject? orientationToken;
    NSObject? interruptedToken;
    NSObject? interruptionEndedToken;
    NSObject? runtimeErrorToken;
    readonly DispatchQueue sessionQueue = new("shiny.camera.session");
    readonly DispatchQueue videoQueue = new("shiny.camera.video");

    protected override CameraPreviewView CreatePlatformView() => new();

    protected override void ConnectHandler(CameraPreviewView platformView)
    {
        base.ConnectHandler(platformView);
        this.InitPipeline();
        // Two triggers for one idempotent apply, because neither catches everything. Layout is the accurate
        // one (the interface orientation has settled by then) but a 180° flip — landscape-left to
        // landscape-right, which is a cradle being remounted the other way up — does not change the view's
        // bounds. The device notification catches that; it fires slightly ahead of the interface updating,
        // which is why it only triggers the apply rather than supplying the value. See
        // CameraPreviewView.LayoutChanged and ResolveOrientation.
        platformView.LayoutChanged = this.OrientConnections;
        UIDevice.CurrentDevice.BeginGeneratingDeviceOrientationNotifications();
        this.orientationToken = NSNotificationCenter.DefaultCenter.AddObserver(
            UIDevice.OrientationDidChangeNotification,
            _ => this.OrientConnections()
        );
        if (this.VirtualView.IsActive)
            _ = this.StartAsync();
    }

    protected override void DisconnectHandler(CameraPreviewView platformView)
    {
        platformView.LayoutChanged = null;
        if (this.orientationToken != null)
        {
            NSNotificationCenter.DefaultCenter.RemoveObserver(this.orientationToken);
            this.orientationToken.Dispose();
            this.orientationToken = null;
            UIDevice.CurrentDevice.EndGeneratingDeviceOrientationNotifications();
        }
        this.TeardownPipeline();
        this.TeardownSession();
        base.DisconnectHandler(platformView);
    }


    public Task<bool> RequestPermissionAsync(CancellationToken ct = default)
    {
        var status = AVCaptureDevice.GetAuthorizationStatus(AVAuthorizationMediaType.Video);
        if (status == AVAuthorizationStatus.Authorized)
            return Task.FromResult(true);

        return AVCaptureDevice.RequestAccessForMediaTypeAsync(AVAuthorizationMediaType.Video);
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
        settings.FlashMode = this.VirtualView.FlashMode switch
        {
            CameraFlashMode.On => AVCaptureFlashMode.On,
            CameraFlashMode.Auto => AVCaptureFlashMode.Auto,
            _ => AVCaptureFlashMode.Off
        };

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

        // Anything to composite (effects or a legacy overlay) -> owned AVAssetWriter path, which processes every
        // frame off the data output so the recording matches the preview. Nothing to composite -> fast native
        // AVCaptureMovieFileOutput path (unchanged, no perf/behavior change).
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

            // ⚠️ Load-bearing, not tidy-up. Frames are only delivered while something wants them, and a
            // burn-in recording is the one consumer that can appear with no analyzer and no effects
            // attached — without this the writer would sit waiting for frames that never arrive and the
            // recording would finish empty.
            this.UpdateFrameDelivery();
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
            this.UpdateFrameDelivery();
            return this.ReorientWhenDone(recorder.FinishAsync());
        }

        if (this.movieOutput is not { Recording: true } || this.recordingDelegate == null)
            throw new InvalidOperationException("Not recording");

        this.movieOutput.StopRecording();
        return this.ReorientWhenDone(this.recordingDelegate.Task);
    }


    // Apply whatever orientation the device reached during the recording, now that transposing the buffers can
    // no longer corrupt an encoder mid-file. See OrientConnections for why it was held off.
    Task<CameraVideo> ReorientWhenDone(Task<CameraVideo> recording)
    {
        _ = recording.ContinueWith(
            _ => this.MainThread(this.OrientConnections),
            TaskScheduler.Default);
        return recording;
    }


    // Add an AVCaptureAudioDataOutput feeding the overlay recorder (the AVAssetWriter path needs audio samples
    // routed to it; the native movie-output path handles audio itself). The audio device input is added
    // separately via EnsureAudioInput.
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


    // ---- property mappers ----

    static partial void MapFacing(CameraViewHandler handler, CameraView view)
        => handler.OnSessionQueue(handler.ReconfigureInput, "The camera could not be changed");

    static partial void MapIsActive(CameraViewHandler handler, CameraView view)
    {
        if (view.IsActive)
            _ = handler.StartAsync();
        else
            _ = handler.StopAsync();
    }

    static partial void MapTorch(CameraViewHandler handler, CameraView view)
        => handler.ApplyTorch(view.IsTorchOn);

    static partial void MapFlashMode(CameraViewHandler handler, CameraView view) { /* applied at capture time */ }

    // Read when the shutter fires, and the output's ceiling was declared generously enough to cover every
    // rung when the session was configured - so moving between them needs nothing applied ahead of time.
    static partial void MapPhotoQuality(CameraViewHandler handler, CameraView view) { /* applied at capture time */ }

    static partial void MapZoom(CameraViewHandler handler, CameraView view)
        => handler.ApplyZoom(view.Zoom);

    static partial void MapScaleMode(CameraViewHandler handler, CameraView view)
    {
        if (handler.PlatformView is { } pv)
            pv.PreviewLayer.VideoGravity = view.ScaleMode == PreviewScaleMode.AspectFit
                ? AVLayerVideoGravity.ResizeAspect
                : AVLayerVideoGravity.ResizeAspectFill;
    }

    static partial void MapOverlay(CameraViewHandler handler, CameraView view) { /* Phase 2 */ }

    static partial void MapEffects(CameraViewHandler handler, CameraView view)
        => handler.MainThread(() => handler.ApplyEffects(view.EffectChain));

    // Cheap and idempotent — orienting a connection needs no session reconfiguration, so unlike
    // MapVideoQuality below this does not blink the preview. OrientConnections declines it mid-recording.
    static partial void MapOrientation(CameraViewHandler handler, CameraView view)
        => handler.OrientConnections();

    // The preset sizes the whole session — preview, data output and movie output alike — so this is a session
    // reconfiguration, not a recording setting. Refused mid-recording: changing the preset renegotiates the
    // active format underneath a running AVAssetWriter or movie output, which corrupts the file being written.
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

        this.ApplyFrameRate();
        this.ApplyMovieOutputBitrate();

        // The preset above may have moved the active format, and the still dimensions the photo output is
        // allowed to ask for are a property of that format. ReconfigureInput routes through here too, so a
        // lens swap re-declares the ceiling for the new device as well.
        if (this.photoOutput is { } photo)
            ApplePhotoQuality.ConfigureOutput(photo, this.device);
    }


    /// <summary>
    /// The session preset for the requested quality, falling back down the ladder when the device does not
    /// support it.
    /// </summary>
    /// <remarks>
    /// <c>CanSetSessionPreset</c> has to be asked — assigning an unsupported preset throws, and the front
    /// camera on many devices tops out well below the back one, so the answer changes with
    /// <see cref="CameraView.Facing"/> and not only with the hardware. Walking downwards keeps the failure
    /// mode "smaller than you asked for" rather than "camera did not start".
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


    /// <summary>
    /// Pins the capture frame rate by clamping both the min and max frame duration on the device.
    /// </summary>
    /// <remarks>
    /// Setting only the max leaves AVFoundation free to run faster under good light, which defeats the point
    /// when the request was made for thermal or file-size reasons. The value is clamped to what the active
    /// format actually supports — asking for 60 on a format that tops out at 30 throws rather than degrading.
    /// </remarks>
    void ApplyFrameRate()
    {
        if (this.MaybeVirtualView?.VideoFrameRate is not > 0 || this.device is not { } dev)
            return;

        var requested = this.MaybeVirtualView.VideoFrameRate!.Value;

        try
        {
            var supported = dev.ActiveFormat?.VideoSupportedFrameRateRanges;
            if (supported is not { Length: > 0 })
                return;

            var max = supported.Max(r => r.MaxFrameRate);
            var min = supported.Min(r => r.MinFrameRate);
            var fps = (int)Math.Clamp(requested, Math.Ceiling(min), Math.Floor(max));
            if (fps <= 0)
                return;

            if (dev.LockForConfiguration(out var err) && err == null)
            {
                dev.ActiveVideoMinFrameDuration = new CMTime(1, fps);
                dev.ActiveVideoMaxFrameDuration = new CMTime(1, fps);
                dev.UnlockForConfiguration();
            }
        }
        catch (Exception ex)
        {
            this.MainThread(() => this.MaybeVirtualView?.OnCameraError("Could not apply the requested frame rate", ex));
        }
    }


    /// <summary>
    /// Applies the bitrate to the native (no-overlay) recording path. The burn-in path owns its own
    /// <c>AVAssetWriter</c> settings and is handed the value at construction instead.
    /// </summary>
    void ApplyMovieOutputBitrate()
    {
        if (this.MaybeVirtualView?.VideoBitrate is not > 0 || this.movieOutput is not { } output)
            return;

        var conn = output.ConnectionFromMediaType(AVMediaTypes.Video.GetConstant()!);
        if (conn == null)
            return;

        try
        {
            // Start from the codec settings the output would have used and override only the bitrate, so the
            // codec and dimensions stay whatever AVFoundation negotiated for the preset
            var settings = output.GetOutputSettings(conn)?.MutableCopy() as NSMutableDictionary
                           ?? new NSMutableDictionary();

            var props = settings[AVVideo.CompressionPropertiesKey] as NSDictionary;
            var compression = props?.MutableCopy() as NSMutableDictionary ?? new NSMutableDictionary();
            compression[AVVideo.AverageBitRateKey] = NSNumber.FromInt32(this.MaybeVirtualView.VideoBitrate!.Value);
            settings[AVVideo.CompressionPropertiesKey] = compression;

            output.SetOutputSettings(settings, conn);
        }
        catch (Exception ex)
        {
            this.MainThread(() => this.MaybeVirtualView?.OnCameraError("Could not apply the requested video bitrate", ex));
        }
    }


    // ---- internals ----

    /// <summary>
    /// Runs work on the session queue with the failure reported rather than thrown.
    /// </summary>
    /// <remarks>
    /// ⚠️ Load-bearing, not tidiness. An exception that escapes a <c>DispatchAsync</c> block has no
    /// caller to travel to - GCD invokes the block from a worker thread with nothing above it - so
    /// the runtime treats it as unhandled and aborts the process. A camera that cannot reconfigure
    /// itself must degrade to a message on the view, never to a crash.
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


    void ConfigureSession()
    {
        if (this.session != null)
            return;

        this.session = new AVCaptureSession { SessionPreset = this.ResolvePreset() };

        // The app's audio session is ours to configure, not AVFoundation's — see AppleCaptureAudioSession for
        // what the automatic configuration costs (it interrupts everything else playing, and lets everything
        // else interrupt the capture). Set before the session ever runs, since that is when the automatic
        // path would apply. A recording that wants audio configures it in EnsureAudioInput; one that does not
        // leaves the audio session untouched entirely, which is why a silent recording no longer stops music.
        this.session.AutomaticallyConfiguresApplicationAudioSession = false;

        this.ObserveSession(this.session);
        this.session.BeginConfiguration();

        this.AddVideoInput();

        this.photoOutput = new AVCapturePhotoOutput();
        if (this.session.CanAddOutput(this.photoOutput))
            this.session.AddOutput(this.photoOutput);

        // After AddOutput, and for the same reason ApplyCaptureFormat is: the photo output's dimension
        // ceiling is a question about this output attached to this session, and it answers nothing until it
        // is. Re-applied by ApplyVideoSettings whenever the preset moves the active format underneath it.
        ApplePhotoQuality.ConfigureOutput(this.photoOutput, this.device);

        this.dataOutput = new AVCaptureVideoDataOutput { AlwaysDiscardsLateVideoFrames = true };
        if (this.session.CanAddOutput(this.dataOutput))
            this.session.AddOutput(this.dataOutput);

        // After AddOutput, not before: AvailableVideoCVPixelFormatTypes is a question about this output on
        // this session with this input, and it answers nothing until the output is attached to all three.
        this.ApplyCaptureFormat();

        this.movieOutput = new AVCaptureMovieFileOutput();
        if (this.session.CanAddOutput(this.movieOutput))
            this.session.AddOutput(this.movieOutput);

        this.session.CommitConfiguration();
        this.ConfigureFocus();

        // After the commit, because the frame rate needs the device and the bitrate needs the movie output.
        // Also re-run on an input change: the front camera's supported presets and frame-rate ranges are
        // usually a subset of the back one's, so a facing switch can invalidate what was negotiated.
        this.ApplyVideoSettings();

        this.MainThread(() =>
        {
            try
            {
                if (this.PlatformView is not { } pv || this.session is not { } s)
                    return;

                pv.PreviewLayer.Session = s;
                this.SetupFilterView();
                this.UpdateFrameDelivery();
                this.OrientConnections();
                this.ReportZoomRange();
                this.ApplyZoom(this.VirtualView.Zoom);
                this.ApplyTorch(this.VirtualView.IsTorchOn);
                this.ApplyEffects(this.VirtualView.EffectChain);
            }
            catch (Exception ex)
            {
                this.MaybeVirtualView?.OnCameraError("Camera preview setup failed", ex);
            }
        });
    }


    /// <summary>
    /// Watches the session for the two things that take the camera away without asking: an interruption and a
    /// runtime error.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>AVFoundation suspends a capture session and tells nobody.</b> A phone call, another app claiming the
    /// device, a second foreground app in Split View, or the system throttling under thermal/power pressure
    /// all stop frame delivery — and the failure is completely silent. The preview layer simply holds its last
    /// frame, an <c>AVAssetWriter</c> fed off the data output stops advancing, and the app carries on running
    /// as if nothing happened. Nothing here recovers by itself either: without
    /// <c>AVCaptureSessionInterruptionEnded</c> being acted on, the session stays down until the process is
    /// restarted, and a caller that reacts to <c>IsActive</c> sees a session that still claims to be active.
    /// For anything recording unattended — a dash cam being the obvious case — that is footage lost with no
    /// indication at the time and no way to notice afterwards.
    /// </para>
    /// <para>
    /// <b>Backgrounding is excluded deliberately.</b> iOS raises
    /// <see cref="AVCaptureSessionInterruptionReason.VideoDeviceNotAvailableInBackground"/> every time the app
    /// leaves the foreground, which is ordinary lifecycle rather than a fault; reporting it would fire
    /// <see cref="CameraView.CameraError"/> on every app switch. It still resumes through the same
    /// interruption-ended path as everything else.
    /// </para>
    /// <para>
    /// The restart is gated on <see cref="CameraView.IsActive"/>: an interruption that ends after the caller
    /// has deliberately stopped the camera must not bring it back.
    /// </para>
    /// </remarks>
    void ObserveSession(AVCaptureSession s)
    {
        this.interruptedToken = AVCaptureSession.Notifications.ObserveWasInterrupted(s, (_, e) =>
        {
            var reason = ReadInterruptionReason(e.Notification);
            if (reason == AVCaptureSessionInterruptionReason.VideoDeviceNotAvailableInBackground)
                return;

            this.MainThread(() => this.MaybeVirtualView?.OnCameraError(DescribeInterruption(reason)));
        });

        this.interruptionEndedToken = AVCaptureSession.Notifications.ObserveInterruptionEnded(s, (_, _) =>
            this.RestartIfStopped());

        this.runtimeErrorToken = AVCaptureSession.Notifications.ObserveRuntimeError(s, (_, e) =>
        {
            var error = e.Error;
            this.MainThread(() => this.MaybeVirtualView?.OnCameraError(
                error?.LocalizedDescription ?? "The camera stopped unexpectedly"));

            // MediaServicesWereReset is the recoverable one — the media daemon restarted underneath us and the
            // session can simply be started again. Everything else is left down rather than spun in a retry
            // loop against a device that is not coming back.
            if (error?.Code == (long)AVError.MediaServicesWereReset)
                this.RestartIfStopped();
        });
    }


    void RestartIfStopped() => this.sessionQueue.DispatchAsync(() =>
    {
        try
        {
            if (this.MaybeVirtualView?.IsActive == true && this.session is { Running: false })
                this.session.StartRunning();
        }
        catch (Exception ex)
        {
            this.MainThread(() => this.MaybeVirtualView?.OnCameraError("Could not restart the camera", ex));
        }
    });


    static AVCaptureSessionInterruptionReason ReadInterruptionReason(NSNotification notification)
        => notification.UserInfo?[AVCaptureSession.InterruptionReasonKey] is NSNumber n
            ? (AVCaptureSessionInterruptionReason)n.Int64Value
            : default;


    static string DescribeInterruption(AVCaptureSessionInterruptionReason reason) => reason switch
    {
        AVCaptureSessionInterruptionReason.VideoDeviceInUseByAnotherClient
            => "The camera is in use by another app",
        AVCaptureSessionInterruptionReason.AudioDeviceInUseByAnotherClient
            => "The microphone is in use by another app",
        AVCaptureSessionInterruptionReason.VideoDeviceNotAvailableWithMultipleForegroundApps
            => "The camera is unavailable while another app shares the screen",
        AVCaptureSessionInterruptionReason.VideoDeviceNotAvailableDueToSystemPressure
            => "The camera was stopped by the system — the device is too hot or low on power",
        _ => "The camera was interrupted"
    };


    /// <summary>
    /// Orient every connection on the session so buffers arrive upright for the way the device is being
    /// held, and front-mirror the frame-delivery one to match the preview. The wrapped
    /// <see cref="AppleCameraFrame"/> then needs no further rotation/mirroring.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>All four connections, not just the data output.</b> The preview layer, the photo output, the movie
    /// output and the video data output each carry their own <c>AVCaptureConnection</c>, and every one of
    /// them defaults to <c>Portrait</c> — so leaving any unset does not mean "follow the device", it means
    /// "pinned to portrait forever". Only the data output was ever oriented here, which is why a
    /// landscape-held device previewed sideways and recorded sideways through the native movie path.
    /// </para>
    /// <para>
    /// <b>Deferred while recording.</b> Orienting a connection transposes the pixel buffer, and an encoder is
    /// configured with fixed dimensions from its first frame — the owned <c>AVAssetWriter</c> path reads
    /// width and height off frame one and never revisits them. Handing it transposed buffers afterwards
    /// corrupts the file rather than rotating it. <see cref="StopVideoRecordingAsync"/> re-runs this once the
    /// recording has finalized, so a rotation during a take is applied to the next one.
    /// </para>
    /// </remarks>
    void OrientConnections()
    {
        // MaybeVirtualView, not VirtualView: ReorientWhenDone can land here after the page was navigated away
        // from, and VirtualView throws rather than returning null once disconnected.
        if (this.MaybeVirtualView is not { } view)
            return;

        if (this.overlayRecorder != null || this.movieOutput is { Recording: true })
            return;

        var orientation = this.ResolveOrientation();
        var front = view.Facing == CameraFacing.Front;

        var dataConnection = this.dataOutput?.ConnectionFromMediaType(AVMediaTypes.Video.GetConstant()!);
        if (dataConnection != null)
        {
            Orient(dataConnection, orientation);
            if (dataConnection.SupportsVideoMirroring)
            {
                dataConnection.AutomaticallyAdjustsVideoMirroring = false;
                dataConnection.VideoMirrored = front;
            }

            if (this.frameDelegate != null)
                this.frameDelegate.Mirrored = false; // connection already applies orientation + mirroring
        }

        Orient(this.photoOutput?.ConnectionFromMediaType(AVMediaTypes.Video.GetConstant()!), orientation);
        Orient(this.movieOutput?.ConnectionFromMediaType(AVMediaTypes.Video.GetConstant()!), orientation);
        Orient(this.PlatformView?.PreviewLayer.Connection, orientation);
    }


    static void Orient(AVCaptureConnection? connection, AVCaptureVideoOrientation orientation)
    {
        if (connection is { SupportsVideoOrientation: true } && connection.VideoOrientation != orientation)
            connection.VideoOrientation = orientation;
    }


    /// <summary>
    /// Turn <see cref="CameraView.Orientation"/> into the AVFoundation constant to put on a connection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="CameraOrientation.Device"/> reads the <b>window scene's interface orientation</b> rather
    /// than <c>UIDevice.CurrentDevice.Orientation</c>. The device one reports <c>FaceUp</c>, <c>FaceDown</c>
    /// and <c>Unknown</c> — none of which the camera can be pointed in, and all of which a phone sitting in a
    /// cradle or flat on a desk will produce — and it also ignores whatever the app actually allows, so a
    /// portrait-locked app would still rotate its capture. The interface orientation has neither problem.
    /// </para>
    /// <para>
    /// The explicit members map through the inversion documented on <see cref="CameraOrientation"/>:
    /// <c>AVCaptureVideoOrientation</c> follows the <i>interface</i> convention, in which "landscape left"
    /// means the device's top edge points <i>right</i>. The crossed-over pair below is correct, not a typo.
    /// </para>
    /// </remarks>
    AVCaptureVideoOrientation ResolveOrientation() => this.MaybeVirtualView?.Orientation switch
    {
        CameraOrientation.Portrait => AVCaptureVideoOrientation.Portrait,
        CameraOrientation.PortraitUpsideDown => AVCaptureVideoOrientation.PortraitUpsideDown,
        CameraOrientation.LandscapeTopLeft => AVCaptureVideoOrientation.LandscapeRight,
        CameraOrientation.LandscapeTopRight => AVCaptureVideoOrientation.LandscapeLeft,
        _ => this.ReadInterfaceOrientation()
    };


    AVCaptureVideoOrientation ReadInterfaceOrientation()
        => this.PlatformView?.Window?.WindowScene?.InterfaceOrientation switch
        {
            UIInterfaceOrientation.PortraitUpsideDown => AVCaptureVideoOrientation.PortraitUpsideDown,
            UIInterfaceOrientation.LandscapeLeft => AVCaptureVideoOrientation.LandscapeLeft,
            UIInterfaceOrientation.LandscapeRight => AVCaptureVideoOrientation.LandscapeRight,
            // Portrait, Unknown, and the null the view reports before it is in a window. A view that is not
            // on screen yet gets corrected by the layout pass that puts it there.
            _ => AVCaptureVideoOrientation.Portrait
        };


    void SetupFilterView()
    {
        if (this.filterView != null)
            return;

        this.filterView = new UIImageView
        {
            ContentMode = UIViewContentMode.ScaleAspectFill,
            Hidden = true,
            ClipsToBounds = true,
            // pin to the preview with constraints — an autoresizing mask off a zero starting frame (the bounds
            // are often 0 when the session configures, before layout) leaves the view 0×0, so a filtered frame
            // renders into nothing and the preview looks black
            TranslatesAutoresizingMaskIntoConstraints = false
        };
        this.PlatformView.AddSubview(this.filterView);
        NSLayoutConstraint.ActivateConstraints(
        [
            this.filterView.LeadingAnchor.ConstraintEqualTo(this.PlatformView.LeadingAnchor),
            this.filterView.TrailingAnchor.ConstraintEqualTo(this.PlatformView.TrailingAnchor),
            this.filterView.TopAnchor.ConstraintEqualTo(this.PlatformView.TopAnchor),
            this.filterView.BottomAnchor.ConstraintEqualTo(this.PlatformView.BottomAnchor),
        ]);
        this.frameDelegate = new VideoFrameDelegate(this.filterView)
        {
            WantFrames = () => this.Pipeline.WantsFrame(),
            OnFrame = frame => this.Pipeline.Process(frame, default),
            OnError = this.OnFrameError,
            Mirrored = this.VirtualView.Facing == CameraFacing.Front
        };
    }

    /// <summary>
    /// Attaches the sample-buffer delegate only while something actually consumes frames, and detaches it
    /// again when nothing does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Not the same saving as <c>CameraPipeline.WantsFrame</c>, and it sits a layer below it.</b> That
    /// gate decides whether a delivered frame is worth wrapping — it runs <i>inside</i> the delegate, so the
    /// capture pipeline has already produced, converted and handed over the buffer by the time it is asked.
    /// This decides whether AVFoundation delivers at all.
    /// </para>
    /// <para>
    /// Three things consume frames and any one of them is enough: an attached analyzer, a live effect chain
    /// (the filtered preview is rendered from these frames), and a burn-in recording. A camera showing a
    /// plain preview with none of the three now costs nothing on the video queue — the preview layer draws
    /// itself from the session and never went through here.
    /// </para>
    /// </remarks>
    void UpdateFrameDelivery()
    {
        if (this.dataOutput is not { } output || this.frameDelegate is not { } frames)
            return;

        var wanted = this.Pipeline.HasAnalyzer
            || frames.Filters.Length > 0
            || this.overlayRecorder != null;

        if (wanted == this.frameDeliveryAttached)
            return;

        try
        {
            output.SetSampleBufferDelegate(wanted ? frames : null, wanted ? this.videoQueue : null);
            this.frameDeliveryAttached = wanted;
        }
        catch (Exception ex)
        {
            // Detaching is an optimisation; attaching is not. A failure to attach has to be visible, because
            // the analyzer or the recording behind it will otherwise be silently starved.
            if (wanted)
                this.MaybeVirtualView?.OnCameraError("Camera frame delivery could not be started", ex);
        }
    }

    bool frameDeliveryAttached;

    // The pipeline reports an analyzer being attached, detached or toggled. Apple gates frame delivery on
    // exactly that, so unlike the comment on the shared declaration suggests, it is not a no-op here.
    partial void OnAnalyzersSynced() => this.MainThread(this.UpdateFrameDelivery);

    /// <summary>
    /// Asks the data output for the pixel format the control wants, falling back to BGRA where the device
    /// does not offer it.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>A format the hardware does not list is not a request, it is a stall</b> —
    /// <c>AVCaptureVideoDataOutput</c> validates its video settings and an unsupported one throws out of the
    /// session configuration. So the availability list decides, and an unavailable format silently keeps the
    /// format that has always worked rather than taking the camera down.
    /// </remarks>
    void ApplyCaptureFormat()
    {
        if (this.dataOutput is not { } output)
            return;

        var requested = this.MaybeVirtualView?.CaptureFormat ?? CameraCaptureFormat.Bgra32;
        var wanted = requested == CameraCaptureFormat.Yuv420 && Supports(output, BiplanarFormat)
            ? BiplanarFormat
            : CVPixelFormatType.CV32BGRA;

        try
        {
            output.WeakVideoSettings = new CVPixelBufferAttributes { PixelFormatType = wanted }.Dictionary;
        }
        catch (Exception ex)
        {
            this.MaybeVirtualView?.OnCameraError("Camera capture format could not be applied", ex);
        }
    }

    /// <summary>Full range rather than video range: its luma is already 0–255, so nothing has to rescale it.</summary>
    const CVPixelFormatType BiplanarFormat = CVPixelFormatType.CV420YpCbCr8BiPlanarFullRange;

    // A live change reconfigures the running output. Cheap next to what it saves, and rare: this is a
    // property a page sets once, not one anything animates.
    static partial void MapCaptureFormat(CameraViewHandler handler, CameraView view)
    {
        if (handler.session is not { } session || handler.dataOutput is null)
            return;

        session.BeginConfiguration();
        try
        {
            handler.ApplyCaptureFormat();
        }
        finally
        {
            session.CommitConfiguration();
        }
    }

    static bool Supports(AVCaptureVideoDataOutput output, CVPixelFormatType format)
    {
        try
        {
            return output.AvailableVideoCVPixelFormatTypes?.Contains(format) == true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    int frameErrorReported;

    void OnFrameError(Exception ex)
    {
        // surface only the first frame-processing failure, on the UI thread
        if (Interlocked.Exchange(ref this.frameErrorReported, 1) == 0)
            this.MainThread(() => this.MaybeVirtualView?.OnCameraError("Frame processing failed", ex));
    }


    void ApplyEffects(CameraEffectChain chain)
    {
        if (this.frameDelegate == null || this.filterView == null)
            return;

        var filters = AppleCameraFilters.Create(chain);
        this.frameDelegate.Filters = filters;
        this.UpdateFrameDelivery();

        // Show the filtered-frame overlay on top of the live preview while any effect is active. We must NOT
        // hide the preview layer to reveal it: PreviewLayer is the view's *backing* layer and the overlay is
        // a subview (a sublayer of it), so hiding the preview layer also hides the overlay — which blanked the
        // whole preview. The overlay's frames are opaque and fill the bounds, so they cover the live preview.
        this.filterView.Hidden = filters.Length == 0;
    }


    // Adds the microphone to the session, configuring the app's audio session for it first. Both are one-time:
    // the input stays for the life of the session, so a caller recording back-to-back clips (segmented
    // recording) pays neither the reconfiguration nor the route change per clip.
    void EnsureAudioInput()
    {
        if (this.audioInput != null || this.session == null)
            return;

        // Before the input is added, not after: adding an audio input to a session whose audio session is not
        // in a record-capable category is what produces a recording that runs happily and contains silence.
        this.appAudioSession ??= new AppleCaptureAudioSession
        {
            OnError = message => this.MainThread(() => this.MaybeVirtualView?.OnCameraError(message))
        };
        this.appAudioSession.Activate(this.MaybeVirtualView?.MixWithOtherAudio ?? true);

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


    static readonly AVCaptureDeviceType[] DeviceTypes =
    [
        AVCaptureDeviceType.BuiltInWideAngleCamera,
        AVCaptureDeviceType.BuiltInUltraWideCamera,
        AVCaptureDeviceType.BuiltInTelephotoCamera
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
        this.ConfigureFocus();

        // After the commit, because the frame rate needs the device and the bitrate needs the movie output.
        // Also re-run on an input change: the front camera's supported presets and frame-rate ranges are
        // usually a subset of the back one's, so a facing switch can invalidate what was negotiated.
        this.ApplyVideoSettings();

        this.MainThread(() =>
        {
            this.OrientConnections();
            this.ReportZoomRange();
            this.ApplyZoom(this.VirtualView.Zoom);
            this.ApplyTorch(this.VirtualView.IsTorchOn);
        });
    }


    static AVCaptureDevice? DiscoverDevice(AVCaptureDevicePosition position)
    {
        var discovery = AVCaptureDeviceDiscoverySession.Create(DeviceTypes, AVMediaTypes.Video, position);
        return discovery.Devices.FirstOrDefault();
    }


    // Put the device into continuous autofocus and clear any near-limit restriction so the lens can
    // re-focus as the subject distance changes. Without this the device can sit in a one-shot focus
    // mode (or keep a "Far" range restriction), which is exactly why moving in close — e.g. to scan a
    // document — leaves the preview blurry and never recovers.
    void ConfigureFocus()
    {
        if (this.device == null)
            return;
        try
        {
            if (!this.device.LockForConfiguration(out var err) || err != null)
                return;

            if (this.device.IsFocusModeSupported(AVCaptureFocusMode.ContinuousAutoFocus))
                this.device.FocusMode = AVCaptureFocusMode.ContinuousAutoFocus;

            // allow the lens to travel all the way to its near limit (macro/close range)
            if (this.device.AutoFocusRangeRestrictionSupported)
                this.device.AutoFocusRangeRestriction = AVCaptureAutoFocusRangeRestriction.None;

            // drive focus from the centre of the frame, where the subject the user is moving toward sits
            if (this.device.FocusPointOfInterestSupported)
                this.device.FocusPointOfInterest = new CGPoint(0.5, 0.5);

            // smoother lens travel for video/preview rather than abrupt hunting
            if (this.device.SmoothAutoFocusSupported)
                this.device.SmoothAutoFocusEnabled = true;

            // keep exposure tracking too so a close subject is correctly exposed as well as focused
            if (this.device.IsExposureModeSupported(AVCaptureExposureMode.ContinuousAutoExposure))
                this.device.ExposureMode = AVCaptureExposureMode.ContinuousAutoExposure;

            this.device.UnlockForConfiguration();
        }
        catch (Exception ex)
        {
            this.MaybeVirtualView?.OnCameraError("Focus configuration failed", ex);
        }
    }


    void ApplyZoom(double zoom)
    {
        if (this.device == null)
            return;
        try
        {
            if (this.device.LockForConfiguration(out _))
            {
                var max = (double)this.device.ActiveFormat.VideoMaxZoomFactor;
                var min = (double)this.device.MinAvailableVideoZoomFactor;
                this.device.VideoZoomFactor = (System.Runtime.InteropServices.NFloat)Math.Clamp(zoom, min, max);
                this.device.UnlockForConfiguration();
            }
        }
        catch (Exception ex)
        {
            this.MaybeVirtualView?.OnCameraError("Zoom failed", ex);
        }
    }


    void ApplyTorch(bool on)
    {
        if (this.device is not { HasTorch: true })
            return;
        try
        {
            if (this.device.LockForConfiguration(out _))
            {
                if (this.device.TorchAvailable)
                    this.device.TorchMode = on ? AVCaptureTorchMode.On : AVCaptureTorchMode.Off;
                this.device.UnlockForConfiguration();
            }
        }
        catch (Exception ex)
        {
            this.MaybeVirtualView?.OnCameraError("Torch failed", ex);
        }
    }


    void ReportZoomRange()
    {
        if (this.device == null)
            return;
        var min = (double)this.device.MinAvailableVideoZoomFactor;
        var max = (double)this.device.ActiveFormat.VideoMaxZoomFactor;
        this.MaybeVirtualView?.OnZoomRangeChanged(min, Math.Min(max, 10d));
    }


    void TeardownSession()
    {
        this.OnSessionQueue(() =>
        {
            if (this.session == null)
                return;

            // Before the session is disposed — the observers are registered against it, and one firing into a
            // disposed session would land in a handler holding a dead handle.
            this.interruptedToken?.Dispose();
            this.interruptedToken = null;
            this.interruptionEndedToken?.Dispose();
            this.interruptionEndedToken = null;
            this.runtimeErrorToken?.Dispose();
            this.runtimeErrorToken = null;

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
            this.photoOutput?.Dispose();
            this.photoOutput = null;
            this.movieOutput?.Dispose();
            this.movieOutput = null;
            this.dataOutput?.Dispose();
            this.dataOutput = null;
            this.audioDataOutput?.Dispose();
            this.audioDataOutput = null;
            this.appAudioSession?.Deactivate();
            this.appAudioSession = null;
            this.overlayRecorder = null;
            this.device = null;
        }, "The camera could not be shut down cleanly");
    }


    void MainThread(Action action) => UIApplication.SharedApplication.InvokeOnMainThread(action);
}
