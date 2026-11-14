using Shiny.Maui.Controls.Camera.Video;

namespace Shiny.Maui.Controls.Camera;

/// <summary>
/// Turns a <see cref="CameraView"/>'s frames into a live video stream: each frame the camera delivers is
/// scaled down, compressed as JPEG or H.264, and handed to <see cref="FrameEncoded"/> for the app to send.
/// The other side draws them with a <see cref="RemoteVideoView"/>. Built for video calls, where the transport
/// (Wi-Fi, Bluetooth) is the app's business and the frames are small and steady.
/// </summary>
/// <remarks>
/// <para>
/// It is a <see cref="FrameAnalyzer"/>, so it is assigned to <see cref="CameraView.Analyzer"/>, which takes
/// one analyzer. While it is the camera's analyzer it cannot also scan barcodes or faces. It draws nothing on
/// the preview, and it needs no <see cref="CameraView.Scan"/>: frames flow for as long as it is attached,
/// enabled and has a <see cref="FrameEncoded"/> subscriber.
/// </para>
/// <para>
/// <b>Frames are encoded un-rotated.</b> Each <see cref="EncodedVideoFrame"/> says how to rotate and mirror it
/// upright, and the receiver does that, so turning the phone costs nothing on the sending side. On iOS and Mac
/// Catalyst the capture connection already delivers upright, mirrored buffers, so those frames report a
/// rotation of 0.
/// </para>
/// <para>
/// <b>Every setting can change while it runs.</b> A transport that moves from Wi-Fi to Bluetooth can drop
/// <see cref="MaxDimension"/>, <see cref="FrameRate"/> and <see cref="Bitrate"/> mid-call. JPEG quality applies
/// from the next frame; anything that changes an H.264 encoder's shape rebuilds it, and the first frame from
/// the new encoder is a key frame.
/// </para>
/// <para>
/// Codec support: JPEG everywhere (iOS, Mac Catalyst, macOS, Android, Windows). H.264 on iOS, Mac Catalyst and
/// macOS (VideoToolbox) and on Android (MediaCodec, when the device has an AVC encoder). Windows reports
/// <c>IsSupported(VideoCodec.H264) == false</c>. Check <see cref="IsSupported"/> before choosing a codec.
/// </para>
/// </remarks>
public partial class VideoEncoder : FrameAnalyzer
{
    readonly TimeProvider time;
    readonly FrameRateGate gate;
    volatile VideoEncoderSettings settings;
    int keyFrameRequested;

    // analysis thread only (AnalyzeAsync / OnDetached never overlap - see IFrameAnalyzer.OnDetached)
    IVideoEncoderSession? session;
    long? firstFrame;
    long retryAfter;
    long lastKeyFrameTicks = long.MinValue;

    /// <summary>Create an encoder with the Wi-Fi defaults: JPEG, 640 px, 15 fps.</summary>
    public VideoEncoder() : this(TimeProvider.System) { }

    internal VideoEncoder(TimeProvider time)
    {
        this.time = time;
        this.settings = this.Snapshot();
        this.gate = new FrameRateGate(this.settings.FrameRate, time.TimestampFrequency);
    }

    /// <inheritdoc/>
    public override string Id => "shiny.camera.video";

    /// <summary>How each frame is compressed. Default <see cref="VideoCodec.Jpeg"/>.</summary>
    /// <remarks>
    /// Changing it mid-stream switches on the next frame, and that frame is a key frame. Choosing a codec this
    /// platform can't encode (see <see cref="IsSupported"/>) produces no frames rather than throwing.
    /// </remarks>
    public static readonly BindableProperty CodecProperty = BindableProperty.Create(
        nameof(Codec), typeof(VideoCodec), typeof(VideoEncoder), VideoCodec.Jpeg, propertyChanged: OnSettingChanged);

    /// <inheritdoc cref="CodecProperty"/>
    public VideoCodec Codec
    {
        get => (VideoCodec)this.GetValue(CodecProperty);
        set => this.SetValue(CodecProperty, value);
    }

    /// <summary>
    /// The longest edge of an encoded frame, in pixels. A larger camera frame is scaled down to it, keeping its
    /// aspect ratio (both edges are rounded down to even numbers); a smaller one is never scaled up. Default 640.
    /// Zero or less disables the limit.
    /// </summary>
    public static readonly BindableProperty MaxDimensionProperty = BindableProperty.Create(
        nameof(MaxDimension), typeof(int), typeof(VideoEncoder), 640, propertyChanged: OnSettingChanged);

    /// <inheritdoc cref="MaxDimensionProperty"/>
    public int MaxDimension
    {
        get => (int)this.GetValue(MaxDimensionProperty);
        set => this.SetValue(MaxDimensionProperty, value);
    }

    /// <summary>
    /// The most frames per second to encode. Default 15. Enforced in <see cref="WantsFrame"/>, before the
    /// platform builds a frame, so the frames in between cost nothing. The camera's own rate is the ceiling.
    /// Zero or less encodes every frame.
    /// </summary>
    public static readonly BindableProperty FrameRateProperty = BindableProperty.Create(
        nameof(FrameRate), typeof(double), typeof(VideoEncoder), 15d, propertyChanged: OnSettingChanged);

    /// <inheritdoc cref="FrameRateProperty"/>
    public double FrameRate
    {
        get => (double)this.GetValue(FrameRateProperty);
        set => this.SetValue(FrameRateProperty, value);
    }

    /// <summary>The H.264 target bitrate in bits per second. Default 800 000. Ignored for JPEG.</summary>
    public static readonly BindableProperty BitrateProperty = BindableProperty.Create(
        nameof(Bitrate), typeof(int), typeof(VideoEncoder), 800_000, propertyChanged: OnSettingChanged);

    /// <inheritdoc cref="BitrateProperty"/>
    public int Bitrate
    {
        get => (int)this.GetValue(BitrateProperty);
        set => this.SetValue(BitrateProperty, value);
    }

    /// <summary>JPEG quality from 0 (smallest) to 1 (best). Default 0.6. Ignored for H.264.</summary>
    public static readonly BindableProperty JpegQualityProperty = BindableProperty.Create(
        nameof(JpegQuality), typeof(double), typeof(VideoEncoder), 0.6d, propertyChanged: OnSettingChanged);

    /// <inheritdoc cref="JpegQualityProperty"/>
    public double JpegQuality
    {
        get => (double)this.GetValue(JpegQualityProperty);
        set => this.SetValue(JpegQualityProperty, value);
    }

    /// <summary>
    /// The longest an H.264 stream goes between key frames. Default 2 seconds. A receiver that joins late or
    /// loses a frame waits at most this long without <see cref="RequestKeyFrame"/>. <see cref="TimeSpan.Zero"/>
    /// makes every frame a key frame. Ignored for JPEG, where every frame is one.
    /// </summary>
    public static readonly BindableProperty KeyFrameIntervalProperty = BindableProperty.Create(
        nameof(KeyFrameInterval), typeof(TimeSpan), typeof(VideoEncoder), TimeSpan.FromSeconds(2), propertyChanged: OnSettingChanged);

    /// <inheritdoc cref="KeyFrameIntervalProperty"/>
    public TimeSpan KeyFrameInterval
    {
        get => (TimeSpan)this.GetValue(KeyFrameIntervalProperty);
        set => this.SetValue(KeyFrameIntervalProperty, value);
    }

    /// <summary>
    /// Raised with each encoded frame, <b>on the encoder's thread, not the UI thread</b>: the camera's analysis
    /// thread, or VideoToolbox's output thread for H.264 on Apple. Hand the frame to the transport and return;
    /// touching UI from here needs a dispatch. An exception thrown by a handler is swallowed so it can't stop
    /// the stream.
    /// </summary>
    /// <remarks>
    /// Without a subscriber the encoder takes no frames at all. The frame's <see cref="EncodedVideoFrame.Data"/>
    /// is a fresh array the handler may keep.
    /// </remarks>
    public event Action<EncodedVideoFrame>? FrameEncoded;

    /// <summary>
    /// Make the next encoded frame a key frame, e.g. because the receiver raised
    /// <see cref="RemoteVideoView.KeyFrameNeeded"/>. Cheap and safe from any thread; requests that arrive before
    /// the next frame collapse into one. A no-op for JPEG, where every frame already is one.
    /// </summary>
    public void RequestKeyFrame() => Interlocked.Exchange(ref this.keyFrameRequested, 1);

    /// <summary>Whether this platform can encode <paramref name="codec"/>.</summary>
    public static bool IsSupported(VideoCodec codec) => PlatformIsSupported(codec);

    /// <summary>
    /// Replaces the platform encoder in tests. Takes the settings, output width and height, and the sink.
    /// </summary>
    internal Func<VideoEncoderSettings, int, int, Action<EncodedVideoFrame>, IVideoEncoderSession?>? SessionFactory { get; set; }

    /// <inheritdoc/>
    /// <remarks>
    /// The frame-rate gate lives here, not in <see cref="AnalyzeAsync"/>: refusing a frame here means the
    /// platform never wraps it (on Apple a refused frame is not even copied).
    /// </remarks>
    public override bool WantsFrame()
        => this.FrameEncoded is not null && this.gate.ShouldPass(this.time.GetTimestamp());

    /// <inheritdoc/>
    /// <remarks>Never returns overlay boxes: the encoder draws nothing on the preview.</remarks>
    public override async ValueTask<IReadOnlyList<OverlayBox>?> AnalyzeAsync(VideoFrame frame, CancellationToken ct)
    {
        if (this.FrameEncoded is null)
            return null;

        var now = this.time.GetTimestamp();
        if (now < this.retryAfter)
            return null;

        var current = this.settings;
        var (width, height) = VideoScaling.Fit(frame.Width, frame.Height, current.MaxDimension);
        if (width == 0)
            return null;

        try
        {
            if (this.session is not { } active || !active.CanEncode(current, width, height))
            {
                this.session?.Dispose();
                this.session = null;
                this.session = this.CreateSession(current, width, height);
                if (this.session is null)
                    return null; // the codec isn't available here; IsSupported says so
            }

            this.firstFrame ??= now;
            var timestamp = this.time.GetElapsedTime(this.firstFrame.Value, now);
            var force = Interlocked.Exchange(ref this.keyFrameRequested, 0) == 1 || this.KeyFrameDue(current, now);

            await this.session.EncodeAsync(frame, width, height, timestamp, force, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch
        {
            // A broken encoder (a codec the OS reclaimed, a format it refused) is rebuilt, but not on every
            // frame: back off for a second so a permanent failure doesn't thrash.
            this.session?.Dispose();
            this.session = null;
            this.retryAfter = now + this.time.TimestampFrequency;
        }
        return null;
    }

    /// <inheritdoc/>
    protected override void OnDetached()
    {
        base.OnDetached();
        this.session?.Dispose();
        this.session = null;
        this.firstFrame = null;
        this.retryAfter = 0;
        Interlocked.Exchange(ref this.lastKeyFrameTicks, long.MinValue);
        this.gate.Reset();
    }

    IVideoEncoderSession? CreateSession(VideoEncoderSettings current, int width, int height)
    {
        // a new session always opens with a key frame; restart the interval from it
        Interlocked.Exchange(ref this.lastKeyFrameTicks, long.MinValue);
        return this.SessionFactory is { } factory
            ? factory(current, width, height, this.Emit)
            : CreatePlatformSession(current, width, height, this.Emit);
    }

    // H.264 encoders keep their own key-frame interval; this is the backstop for one that drifts past it, and
    // what makes a sub-second interval work on Android, whose interval is whole seconds.
    bool KeyFrameDue(VideoEncoderSettings current, long now)
    {
        if (current.Codec != VideoCodec.H264)
            return false;

        var last = Interlocked.Read(ref this.lastKeyFrameTicks);
        return last != long.MinValue && this.time.GetElapsedTime(last, now) >= current.KeyFrameInterval;
    }

    void Emit(EncodedVideoFrame frame)
    {
        if (frame.IsKeyFrame)
            Interlocked.Exchange(ref this.lastKeyFrameTicks, this.time.GetTimestamp());

        try
        {
            this.FrameEncoded?.Invoke(frame);
        }
        catch
        {
            // a throwing consumer must not stop the stream (or, on Apple, unwind into VideoToolbox)
        }
    }

    static void OnSettingChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var encoder = (VideoEncoder)bindable;
        encoder.settings = encoder.Snapshot();
        encoder.gate.SetRate(encoder.settings.FrameRate);
    }

    VideoEncoderSettings Snapshot() => new(
        this.Codec,
        this.MaxDimension,
        this.FrameRate,
        this.Bitrate > 0 ? this.Bitrate : 800_000,
        Math.Clamp(this.JpegQuality, 0.01, 1d),
        this.KeyFrameInterval < TimeSpan.Zero ? TimeSpan.Zero : this.KeyFrameInterval
    );

    internal VideoEncoderSettings CurrentSettings => this.settings;

    private static partial bool PlatformIsSupported(VideoCodec codec);

    private static partial IVideoEncoderSession? CreatePlatformSession(
        VideoEncoderSettings settings, int width, int height, Action<EncodedVideoFrame> sink);
}
