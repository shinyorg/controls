using Microsoft.Maui.Graphics;

namespace Shiny.Maui.Controls.Camera;

public partial class CameraView
{
    /// <summary>Which physical camera to use. Default <see cref="CameraFacing.Back"/>.</summary>
    public static readonly BindableProperty FacingProperty = BindableProperty.Create(
        nameof(Facing), typeof(CameraFacing), typeof(CameraView), CameraFacing.Back);

    /// <summary>
    /// Exact device id to use (from <see cref="GetAvailableCamerasAsync"/>). When null, the camera is
    /// chosen by <see cref="Facing"/>. Setting this overrides <see cref="Facing"/>.
    /// </summary>
    public static readonly BindableProperty CameraIdProperty = BindableProperty.Create(
        nameof(CameraId), typeof(string), typeof(CameraView), null);

    /// <summary>Whether the session should be running. Default <c>true</c>.</summary>
    public static readonly BindableProperty IsActiveProperty = BindableProperty.Create(
        nameof(IsActive), typeof(bool), typeof(CameraView), true);

    /// <summary>Turn the torch (continuous flashlight) on/off where supported. Default <c>false</c>.</summary>
    public static readonly BindableProperty IsTorchOnProperty = BindableProperty.Create(
        nameof(IsTorchOn), typeof(bool), typeof(CameraView), false);

    /// <summary>Flash behaviour for still capture. Default <see cref="CameraFlashMode.Off"/>.</summary>
    public static readonly BindableProperty FlashModeProperty = BindableProperty.Create(
        nameof(FlashMode), typeof(CameraFlashMode), typeof(CameraView), CameraFlashMode.Off);

    /// <summary>Current zoom factor (1.0 = no zoom). Clamped to <see cref="MinZoom"/>..<see cref="MaxZoom"/>.</summary>
    public static readonly BindableProperty ZoomProperty = BindableProperty.Create(
        nameof(Zoom), typeof(double), typeof(CameraView), 1d, coerceValue: CoerceZoom);

    /// <summary>Smallest supported zoom factor (reported by the handler).</summary>
    public static readonly BindableProperty MinZoomProperty = BindableProperty.Create(
        nameof(MinZoom), typeof(double), typeof(CameraView), 1d);

    /// <summary>Largest supported zoom factor (reported by the handler).</summary>
    public static readonly BindableProperty MaxZoomProperty = BindableProperty.Create(
        nameof(MaxZoom), typeof(double), typeof(CameraView), 1d);

    /// <summary>
    /// Whether a two-finger pinch on the preview drives <see cref="Zoom"/>. Default <c>false</c>.
    /// </summary>
    /// <remarks>
    /// The gesture writes to <see cref="Zoom"/> like any other caller, so it is clamped to
    /// <see cref="MinZoom"/>..<see cref="MaxZoom"/> and stays in step with anything else bound to it
    /// (a slider, a view model). On a device that reports no zoom range — <c>MinZoom == MaxZoom</c>, which is
    /// every macOS camera — pinching is a no-op.
    /// </remarks>
    public static readonly BindableProperty IsPinchToZoomEnabledProperty = BindableProperty.Create(
        nameof(IsPinchToZoomEnabled), typeof(bool), typeof(CameraView), false,
        propertyChanged: OnPinchToZoomEnabledChanged);

    /// <summary>How the preview fills the view. Default <see cref="PreviewScaleMode.AspectFill"/>.</summary>
    public static readonly BindableProperty ScaleModeProperty = BindableProperty.Create(
        nameof(ScaleMode), typeof(PreviewScaleMode), typeof(CameraView), PreviewScaleMode.AspectFill);

    /// <summary>Whether the built-in bounding-box overlay is drawn. Default <c>true</c>.</summary>
    public static readonly BindableProperty ShowDetectionOverlayProperty = BindableProperty.Create(
        nameof(ShowDetectionOverlay), typeof(bool), typeof(CameraView), true);

    /// <summary>
    /// Live color filter applied to the preview. Default <see cref="CameraFilter.None"/>.
    /// </summary>
    /// <remarks>
    /// Sugar over <see cref="Effects"/>: the chosen filter is materialized as the <b>first</b> effect in the
    /// chain, so it composes predictably with anything else in <see cref="Effects"/>. For looks beyond the
    /// twelve built-in colour grades — comic, sketch, face masks, AI stylization — add an
    /// <see cref="ICameraEffect"/> to <see cref="Effects"/> instead.
    /// </remarks>
    public static readonly BindableProperty FilterProperty = BindableProperty.Create(
        nameof(Filter), typeof(CameraFilter), typeof(CameraView), CameraFilter.None,
        propertyChanged: (b, _, _) => ((CameraView)b).RebuildEffectChain());

    /// <summary>
    /// How much the device should spend on a still capture. Default <see cref="Camera.PhotoQuality.Highest"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately independent of <see cref="VideoQuality"/>. The session preset sizes video frames and the
    /// preview; this sizes what <see cref="CapturePhotoAsync"/> hands back, and on every platform the still
    /// pipeline can reach past the session's video resolution to the full sensor. Before this property
    /// existed the two were the same knob, so the default 1080p session produced a 2MP photo — see
    /// <see cref="Camera.PhotoQuality"/> for why that is the wrong default for a photograph.
    /// </para>
    /// <para>
    /// Applied at capture time on Apple and Windows, so a change takes effect on the next shot with no
    /// session reconfiguration and no preview hiccup. Android is the exception: CameraX fixes the capture
    /// mode when <c>ImageCapture</c> is built, so a change there rebinds the use cases, and — like
    /// <see cref="VideoQuality"/> — is ignored while a recording is running.
    /// </para>
    /// <para>
    /// There is a ceiling on Apple worth knowing about: the still can only be as large as the <i>active
    /// format</i> supports, and the active format is chosen by the session preset. On current phones a 1080p
    /// format still offers full-sensor stills, which is how capturing a photo mid-recording works at all, but
    /// a device that restricts it will cap out lower. Raising <see cref="VideoQuality"/> raises that ceiling.
    /// </para>
    /// </remarks>
    public static readonly BindableProperty PhotoQualityProperty = BindableProperty.Create(
        nameof(PhotoQuality), typeof(PhotoQuality), typeof(CameraView), PhotoQuality.Highest);

    /// <summary>
    /// JPEG compression quality for captured stills, from 0.0 (smallest) to 1.0 (best). Default 0.9.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only reaches the encoder when this control is the one encoding. On Apple an unfiltered capture is
    /// returned as the platform encoded it and this value is not consulted; it applies to the re-encode that
    /// happens when <see cref="Effects"/> or <see cref="Filter"/> are set, which previously ran at ImageIO's
    /// unspecified default. Android passes it to CameraX and Windows to the JPEG encoder, so both honour it
    /// on every capture.
    /// </para>
    /// <para>
    /// Values outside 0.0-1.0 are clamped rather than throwing: this is a dial, and a capture failing because
    /// a binding produced 1.2 would be the worse outcome.
    /// </para>
    /// </remarks>
    public static readonly BindableProperty PhotoJpegQualityProperty = BindableProperty.Create(
        nameof(PhotoJpegQuality), typeof(double), typeof(CameraView), 0.9d);

    /// <summary>
    /// Target capture resolution for video recording. Default <see cref="Camera.VideoQuality.High"/> (1080p).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a property of the <i>session</i>, not of an individual recording, which is why it lives here
    /// rather than on <see cref="VideoRecordingOptions"/>: both AVFoundation and CameraX fix the capture
    /// resolution when the session is configured, so changing it per recording would mean reconfiguring the
    /// session — a visible hiccup in the preview — every time recording started. Set it once, or bind it to a
    /// setting; changing it while running reconfigures the session, so treat it as a settings-level knob and
    /// not something to toggle per clip.
    /// </para>
    /// <para>
    /// Devices that cannot deliver the requested rung fall back to the nearest supported one — see
    /// <see cref="Camera.VideoQuality"/>.
    /// </para>
    /// </remarks>
    public static readonly BindableProperty VideoQualityProperty = BindableProperty.Create(
        nameof(VideoQuality), typeof(VideoQuality), typeof(CameraView), VideoQuality.High);

    /// <summary>
    /// Target video encoding bitrate in bits per second, or null (default) to let the platform choose for the
    /// selected <see cref="VideoQuality"/>.
    /// </summary>
    /// <remarks>
    /// A hint, not a contract — encoders treat it as a target and none of the platforms guarantee it. Worth
    /// setting when file size matters more than fidelity (long continuous recording), or when the platform
    /// default is visibly too low for the subject. As a rough scale, 1080p30 defaults land around 10-17 Mbps.
    /// </remarks>
    public static readonly BindableProperty VideoBitrateProperty = BindableProperty.Create(
        nameof(VideoBitrate), typeof(int?), typeof(CameraView), null);

    /// <summary>
    /// Target capture frame rate, or null (default) to let the platform choose.
    /// </summary>
    /// <remarks>
    /// The cheapest lever there is on encode cost and thermals — halving the frame rate roughly halves the
    /// work per second of footage, and for a mostly-static scene it costs far less perceptually than dropping
    /// resolution. Like <see cref="VideoBitrate"/> it is a request: the device clamps it to a range its active
    /// format supports.
    /// </remarks>
    public static readonly BindableProperty VideoFrameRateProperty = BindableProperty.Create(
        nameof(VideoFrameRate), typeof(int?), typeof(CameraView), null);

    /// <summary>
    /// Whether recording audio shares the device with whatever else is playing, instead of interrupting it.
    /// Default <c>true</c>. Apple platforms only.
    /// </summary>
    /// <remarks>
    /// <para>
    /// iOS hands an <c>AVCaptureSession</c> the app's audio session by default, and the configuration it
    /// applies is exclusive: starting a recording stops music, a podcast or a navigation app mid-sentence, and
    /// — the direction that is far harder to diagnose — anything else starting playback afterwards
    /// <i>interrupts the capture</i>, which stops video as well as audio. Left at <c>true</c> the session is
    /// configured to mix instead, so neither side evicts the other. This is the right default for anything
    /// recording continuously (a dash cam, a body cam, a long take) and for anything a driver has running.
    /// </para>
    /// <para>
    /// Set it to <c>false</c> only when the recording's audio is the point and background playback would
    /// pollute it — the microphone hears what the speakers are playing, and no processing removes it
    /// afterwards.
    /// </para>
    /// <para>
    /// Read when a recording with <see cref="VideoRecordingOptions.IncludeAudio"/> starts, so set it before
    /// then; changing it mid-recording does nothing. A recording without audio never touches the audio session
    /// at all and so never interrupts anything, whatever this is set to.
    /// </para>
    /// </remarks>
    public static readonly BindableProperty MixWithOtherAudioProperty = BindableProperty.Create(
        nameof(MixWithOtherAudio), typeof(bool), typeof(CameraView), true);

    /// <summary>
    /// How captured output is oriented. Default <see cref="CameraOrientation.Device"/> — the camera follows
    /// the device as it rotates.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a property of the <i>session</i>, not of an individual recording or capture, which is why it
    /// lives here rather than on <see cref="VideoRecordingOptions"/>. On Apple platforms the preview layer,
    /// the photo output and the video data output all hang off connections on one session, and the data
    /// output is the same one that feeds <see cref="CameraView.Analyzer"/> — so a recording cannot be
    /// oriented independently of what an analyzer sees. That coupling is the correct behaviour rather than a
    /// limitation: for a landscape-mounted device, "upright" genuinely <i>is</i> landscape, and the analyzer
    /// should be looking at the same scene the file gets.
    /// </para>
    /// <para>
    /// <b>A change is deferred while a recording is in progress, and that is not politeness.</b> Re-orienting
    /// the capture connection swaps the pixel buffer's width and height, and an encoder is configured with
    /// fixed dimensions off its first frame — on Apple the owned <c>AVAssetWriter</c> path literally reads
    /// them from frame one. Feeding it transposed buffers afterwards does not produce a rotated file, it
    /// produces a corrupt one, and for anything recording unattended that is discovered long after the
    /// footage mattered. The pending value is applied when the recording finishes.
    /// </para>
    /// <para>
    /// It follows that a caller which needs one orientation across <i>several</i> recordings — segmented
    /// continuous capture, where each segment is its own <see cref="CameraView.StartVideoRecordingAsync"/>
    /// call — must pin an explicit value rather than leaving this at <see cref="CameraOrientation.Device"/>.
    /// Otherwise a device rotated between segments yields a set of files that disagree, and the usual way
    /// that surfaces is a concatenation or trim taking its transform from the first track and rendering
    /// every later one sideways.
    /// </para>
    /// <para>
    /// macOS and Windows ignore this: neither has a rotating display behind the capture device.
    /// </para>
    /// </remarks>
    public static readonly BindableProperty OrientationProperty = BindableProperty.Create(
        nameof(Orientation), typeof(CameraOrientation), typeof(CameraView), CameraOrientation.Device);

    /// <summary>
    /// The pixel format the capture pipeline is asked for. Defaults to
    /// <see cref="CameraCaptureFormat.Bgra32"/>, which is what every release before this one delivered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// See <see cref="CameraCaptureFormat"/> for what this actually costs. In short: on Apple, BGRA is a
    /// full-frame colour conversion per frame for as long as the preview is running, and
    /// <see cref="CameraCaptureFormat.Yuv420"/> is the format the sensor and the encoder both wanted.
    /// </para>
    /// <para>
    /// ⚠️ <b>Only opt in when the overlay is a <see cref="ICompositedVideoOverlayRenderer"/> and there are
    /// no draw effects.</b> Anything that has to draw on the CPU makes the recorder convert to a scratch
    /// BGRA surface and back on every frame, which is a worse trade than never leaving BGRA.
    /// </para>
    /// <para>
    /// Applied when the capture session is configured, and re-applied live if it changes afterwards. A
    /// device that does not offer the requested format keeps BGRA rather than failing.
    /// </para>
    /// </remarks>
    public static readonly BindableProperty CaptureFormatProperty = BindableProperty.Create(
        nameof(CaptureFormat), typeof(CameraCaptureFormat), typeof(CameraView), CameraCaptureFormat.Bgra32);

    /// <summary>Whether a video recording is currently in progress (updated by the control).</summary>
    public static readonly BindableProperty IsRecordingProperty = BindableProperty.Create(
        nameof(IsRecording), typeof(bool), typeof(CameraView), false, BindingMode.OneWayToSource);

    /// <summary>The most recent overlay boxes from the analyzer (read-only; updated by the pipeline).</summary>
    public static readonly BindableProperty OverlaysProperty = BindableProperty.Create(
        nameof(Overlays), typeof(IReadOnlyList<OverlayBox>), typeof(CameraView), Array.Empty<OverlayBox>());

    /// <summary>
    /// The analyzer's current scan window in normalized upright space (read-only; mirrors
    /// <see cref="FrameAnalyzer.ScanWindow"/>), or null when it scans the full frame. The built-in overlay
    /// dims outside it and frames a viewfinder reticle.
    /// </summary>
    public static readonly BindableProperty ScanWindowProperty = BindableProperty.Create(
        nameof(ScanWindow), typeof(RectF?), typeof(CameraView), null);


    /// <inheritdoc cref="FacingProperty"/>
    public CameraFacing Facing
    {
        get => (CameraFacing)this.GetValue(FacingProperty);
        set => this.SetValue(FacingProperty, value);
    }

    /// <inheritdoc cref="CameraIdProperty"/>
    public string? CameraId
    {
        get => (string?)this.GetValue(CameraIdProperty);
        set => this.SetValue(CameraIdProperty, value);
    }

    /// <inheritdoc cref="IsActiveProperty"/>
    public bool IsActive
    {
        get => (bool)this.GetValue(IsActiveProperty);
        set => this.SetValue(IsActiveProperty, value);
    }

    /// <inheritdoc cref="IsTorchOnProperty"/>
    public bool IsTorchOn
    {
        get => (bool)this.GetValue(IsTorchOnProperty);
        set => this.SetValue(IsTorchOnProperty, value);
    }

    /// <inheritdoc cref="FlashModeProperty"/>
    public CameraFlashMode FlashMode
    {
        get => (CameraFlashMode)this.GetValue(FlashModeProperty);
        set => this.SetValue(FlashModeProperty, value);
    }

    /// <inheritdoc cref="ZoomProperty"/>
    public double Zoom
    {
        get => (double)this.GetValue(ZoomProperty);
        set => this.SetValue(ZoomProperty, value);
    }

    /// <inheritdoc cref="MinZoomProperty"/>
    public double MinZoom
    {
        get => (double)this.GetValue(MinZoomProperty);
        set => this.SetValue(MinZoomProperty, value);
    }

    /// <inheritdoc cref="MaxZoomProperty"/>
    public double MaxZoom
    {
        get => (double)this.GetValue(MaxZoomProperty);
        set => this.SetValue(MaxZoomProperty, value);
    }

    /// <inheritdoc cref="IsPinchToZoomEnabledProperty"/>
    public bool IsPinchToZoomEnabled
    {
        get => (bool)this.GetValue(IsPinchToZoomEnabledProperty);
        set => this.SetValue(IsPinchToZoomEnabledProperty, value);
    }

    /// <inheritdoc cref="ScaleModeProperty"/>
    public PreviewScaleMode ScaleMode
    {
        get => (PreviewScaleMode)this.GetValue(ScaleModeProperty);
        set => this.SetValue(ScaleModeProperty, value);
    }

    /// <inheritdoc cref="ShowDetectionOverlayProperty"/>
    public bool ShowDetectionOverlay
    {
        get => (bool)this.GetValue(ShowDetectionOverlayProperty);
        set => this.SetValue(ShowDetectionOverlayProperty, value);
    }

    /// <inheritdoc cref="FilterProperty"/>
    public CameraFilter Filter
    {
        get => (CameraFilter)this.GetValue(FilterProperty);
        set => this.SetValue(FilterProperty, value);
    }

    /// <inheritdoc cref="PhotoQualityProperty"/>
    public PhotoQuality PhotoQuality
    {
        get => (PhotoQuality)this.GetValue(PhotoQualityProperty);
        set => this.SetValue(PhotoQualityProperty, value);
    }

    /// <inheritdoc cref="PhotoJpegQualityProperty"/>
    public double PhotoJpegQuality
    {
        get => (double)this.GetValue(PhotoJpegQualityProperty);
        set => this.SetValue(PhotoJpegQualityProperty, value);
    }

    /// <summary>
    /// <see cref="PhotoJpegQuality"/> clamped to the 0.0-1.0 the encoders accept.
    /// </summary>
    /// <remarks>
    /// NaN is sent back to the default rather than clamped, because <c>Math.Clamp</c> passes it through — both
    /// comparisons against a NaN are false — and an encoder handed NaN does something unhelpful and
    /// platform-specific. A binding that produces one is a bug in the caller, but the sane recovery is the
    /// default quality, not a zero-quality photo.
    /// </remarks>
    internal float EncoderJpegQuality
    {
        get
        {
            var quality = this.PhotoJpegQuality;
            return double.IsNaN(quality) ? 0.9f : (float)Math.Clamp(quality, 0d, 1d);
        }
    }

    /// <inheritdoc cref="VideoQualityProperty"/>
    public VideoQuality VideoQuality
    {
        get => (VideoQuality)this.GetValue(VideoQualityProperty);
        set => this.SetValue(VideoQualityProperty, value);
    }

    /// <inheritdoc cref="VideoBitrateProperty"/>
    public int? VideoBitrate
    {
        get => (int?)this.GetValue(VideoBitrateProperty);
        set => this.SetValue(VideoBitrateProperty, value);
    }

    /// <inheritdoc cref="VideoFrameRateProperty"/>
    public int? VideoFrameRate
    {
        get => (int?)this.GetValue(VideoFrameRateProperty);
        set => this.SetValue(VideoFrameRateProperty, value);
    }

    /// <inheritdoc cref="MixWithOtherAudioProperty"/>
    public bool MixWithOtherAudio
    {
        get => (bool)this.GetValue(MixWithOtherAudioProperty);
        set => this.SetValue(MixWithOtherAudioProperty, value);
    }

    /// <inheritdoc cref="CaptureFormatProperty"/>
    public CameraCaptureFormat CaptureFormat
    {
        get => (CameraCaptureFormat)this.GetValue(CaptureFormatProperty);
        set => this.SetValue(CaptureFormatProperty, value);
    }

    /// <inheritdoc cref="OrientationProperty"/>
    public CameraOrientation Orientation
    {
        get => (CameraOrientation)this.GetValue(OrientationProperty);
        set => this.SetValue(OrientationProperty, value);
    }

    /// <inheritdoc cref="IsRecordingProperty"/>
    public bool IsRecording
    {
        get => (bool)this.GetValue(IsRecordingProperty);
        private set => this.SetValue(IsRecordingProperty, value);
    }

    /// <inheritdoc cref="OverlaysProperty"/>
    public IReadOnlyList<OverlayBox> Overlays
    {
        get => (IReadOnlyList<OverlayBox>)this.GetValue(OverlaysProperty);
        private set => this.SetValue(OverlaysProperty, value);
    }

    /// <inheritdoc cref="ScanWindowProperty"/>
    public RectF? ScanWindow
    {
        get => (RectF?)this.GetValue(ScanWindowProperty);
        private set => this.SetValue(ScanWindowProperty, value);
    }


    static object CoerceZoom(BindableObject bindable, object value)
    {
        var view = (CameraView)bindable;
        var zoom = (double)value;
        var min = view.MinZoom <= 0 ? 1d : view.MinZoom;
        var max = view.MaxZoom < min ? min : view.MaxZoom;
        return Math.Clamp(zoom, min, max);
    }
}
