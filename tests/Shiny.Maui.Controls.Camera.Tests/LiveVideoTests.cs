using Shiny.Controls.Camera;
using Shiny.Maui.Controls.Camera.Video;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Camera.Tests;

/// <summary>A clock the test moves by hand, in milliseconds.</summary>
sealed class ManualTime : TimeProvider
{
    public long Now { get; set; } = 1_000_000;
    public override long TimestampFrequency => 1000;
    public override long GetTimestamp() => this.Now;
    public void Advance(double ms) => this.Now += (long)ms;
}


public class RemoteVideoGateTests
{
    static EncodedVideoFrame Frame(bool key, VideoCodec codec = VideoCodec.H264)
        => new(codec, new byte[] { 1 }, 640, 360, 0, false, key, TimeSpan.Zero);

    readonly ManualTime time = new();
    RemoteVideoGate Gate() => new(this.time, TimeSpan.FromSeconds(1));

    [Fact]
    public void A_delta_before_the_first_key_frame_is_dropped_and_asks_for_one()
    {
        var gate = this.Gate();

        gate.Accept(Frame(key: false)).ShouldBe((false, true));
        gate.IsSynced.ShouldBeFalse();
    }

    [Fact]
    public void Requests_are_throttled_until_the_interval_passes()
    {
        var gate = this.Gate();
        gate.Accept(Frame(false)).RequestKeyFrame.ShouldBeTrue();

        this.time.Advance(500);
        gate.Accept(Frame(false)).ShouldBe((false, false));

        this.time.Advance(600);
        gate.Accept(Frame(false)).ShouldBe((false, true)); // the first request may have been lost
    }

    [Fact]
    public void A_key_frame_syncs_and_the_deltas_after_it_render()
    {
        var gate = this.Gate();
        gate.Accept(Frame(false));

        gate.Accept(Frame(true)).ShouldBe((true, false));
        gate.Accept(Frame(false)).ShouldBe((true, false));
        gate.IsSynced.ShouldBeTrue();
    }

    [Fact]
    public void Losing_sync_drops_deltas_until_the_next_key_frame()
    {
        var gate = this.Gate();
        gate.Accept(Frame(true));

        gate.LostSync().ShouldBeTrue();
        gate.Accept(Frame(false)).Render.ShouldBeFalse();
        gate.Accept(Frame(true)).Render.ShouldBeTrue();
        gate.Accept(Frame(false)).Render.ShouldBeTrue();
    }

    [Fact]
    public void A_key_frame_clears_the_throttle()
    {
        var gate = this.Gate();
        gate.Accept(Frame(false)).RequestKeyFrame.ShouldBeTrue();
        gate.Accept(Frame(true));

        // a loss right after recovering asks straight away, not a second after the last request
        gate.LostSync().ShouldBeTrue();
    }

    [Fact]
    public void Jpeg_always_renders()
    {
        var gate = this.Gate();

        // even a JPEG frame a sender mislabelled as a delta
        gate.Accept(Frame(false, VideoCodec.Jpeg)).ShouldBe((true, false));
    }

    [Fact]
    public void A_codec_change_needs_a_key_frame_of_the_new_codec()
    {
        var gate = this.Gate();
        gate.Accept(Frame(true, VideoCodec.Jpeg));

        gate.Accept(Frame(false, VideoCodec.H264)).Render.ShouldBeFalse();
        gate.Accept(Frame(true, VideoCodec.H264)).Render.ShouldBeTrue();
    }

    [Fact]
    public void Reset_starts_over()
    {
        var gate = this.Gate();
        gate.Accept(Frame(true));

        gate.Reset();
        gate.Accept(Frame(false)).ShouldBe((false, true));
    }
}


public class RemoteVideoViewTests
{
    sealed class FakeRenderer : IRemoteVideoRenderer
    {
        public List<EncodedVideoFrame> Rendered { get; } = [];
        public int Clears { get; private set; }
        public Action<EncodedVideoFrame>? OnRender { get; set; }

        public void Render(EncodedVideoFrame frame)
        {
            this.Rendered.Add(frame);
            this.OnRender?.Invoke(frame);
        }

        public void Clear() => this.Clears++;
        public void Dispose() { }
    }

    static EncodedVideoFrame Frame(bool key) => new(VideoCodec.H264, new byte[] { 1 }, 640, 360, 90, true, key, TimeSpan.Zero);

    readonly ManualTime time = new();
    readonly FakeRenderer renderer = new();
    int keyFrameRequests;

    RemoteVideoView View()
    {
        var view = new RemoteVideoView(new RemoteVideoGate(this.time, TimeSpan.FromSeconds(1)));
        view.KeyFrameNeeded += (_, _) => this.keyFrameRequests++;
        view.SetRenderer(this.renderer);
        return view;
    }

    [Fact]
    public void Defaults_to_aspect_fit()
        => new RemoteVideoView().Aspect.ShouldBe(Aspect.AspectFit);

    [Fact]
    public void Nothing_decodes_on_the_platform_neutral_build()
    {
        RemoteVideoView.CanDecode(VideoCodec.Jpeg).ShouldBeFalse();
        RemoteVideoView.CanDecode(VideoCodec.H264).ShouldBeFalse();
    }

    [Fact]
    public void A_leading_delta_is_not_rendered_and_raises_KeyFrameNeeded()
    {
        var view = this.View();

        view.Render(Frame(false));

        this.renderer.Rendered.ShouldBeEmpty();
        this.keyFrameRequests.ShouldBe(1);
    }

    [Fact]
    public void Key_frames_and_the_deltas_after_them_reach_the_renderer()
    {
        var view = this.View();

        view.Render(Frame(true));
        view.Render(Frame(false));

        this.renderer.Rendered.Count.ShouldBe(2);
        this.keyFrameRequests.ShouldBe(0);
    }

    [Fact]
    public void A_decoder_failure_raises_KeyFrameNeeded_and_gates_the_next_delta()
    {
        var view = this.View();
        view.Render(Frame(true));

        // the renderer reports the frame it just got as undecodable
        this.renderer.OnRender = _ => view.OnDecoderLostSync();
        view.Render(Frame(false));
        this.keyFrameRequests.ShouldBe(1);

        this.renderer.OnRender = null;
        view.Render(Frame(false));
        this.renderer.Rendered.Count.ShouldBe(2); // the key frame and the failed delta; the next one waited
    }

    [Fact]
    public void Frames_without_a_handler_are_dropped_and_the_stream_restarts_at_a_key_frame()
    {
        var view = new RemoteVideoView(new RemoteVideoGate(this.time, TimeSpan.FromSeconds(1)));
        view.KeyFrameNeeded += (_, _) => this.keyFrameRequests++;
        view.Render(Frame(true));

        view.SetRenderer(this.renderer);
        view.Render(Frame(false));

        this.renderer.Rendered.ShouldBeEmpty();
        this.keyFrameRequests.ShouldBe(1);
    }

    [Fact]
    public void Clear_blanks_the_renderer_and_waits_for_a_key_frame()
    {
        var view = this.View();
        view.Render(Frame(true));

        view.Clear();
        view.Render(Frame(false));

        this.renderer.Clears.ShouldBe(1);
        this.renderer.Rendered.Count.ShouldBe(1);
    }

    [Fact]
    public void A_throwing_handler_does_not_escape_Render()
    {
        var view = this.View();
        view.KeyFrameNeeded += (_, _) => throw new InvalidOperationException();

        Should.NotThrow(() => view.Render(Frame(false)));
    }
}


public class VideoEncoderTests
{
    sealed class FakeFrame(int width, int height, int rotation = 0, bool mirrored = false) : VideoFrame
    {
        public override int Width => width;
        public override int Height => height;
        public override int Rotation => rotation;
        public override bool IsMirrored => mirrored;
        public override VideoFrameFormat Format => VideoFrameFormat.Grayscale8;
        protected override byte[] MaterializeLuminance() => new byte[width * height];
    }

    sealed class FakeSession(VideoEncoderSettings settings, int width, int height, Action<EncodedVideoFrame> sink) : IVideoEncoderSession
    {
        public VideoEncoderSettings Settings { get; } = settings;
        public int Width { get; } = width;
        public int Height { get; } = height;
        public List<bool> Forced { get; } = [];
        public List<TimeSpan> Timestamps { get; } = [];
        public bool Disposed { get; private set; }
        public bool EmitKeyFrames { get; set; }

        public bool CanEncode(VideoEncoderSettings next, int w, int h)
            => w == this.Width && h == this.Height && next with { JpegQuality = 0 } == this.Settings with { JpegQuality = 0 };

        public ValueTask EncodeAsync(VideoFrame frame, int w, int h, TimeSpan timestamp, bool forceKeyFrame, CancellationToken ct)
        {
            this.Forced.Add(forceKeyFrame);
            this.Timestamps.Add(timestamp);
            var key = this.Settings.Codec == VideoCodec.Jpeg || forceKeyFrame || this.Forced.Count == 1 || this.EmitKeyFrames;
            sink(new EncodedVideoFrame(this.Settings.Codec, new byte[] { 1 }, w, h, frame.Rotation, frame.IsMirrored, key, timestamp));
            return default;
        }

        public void Dispose() => this.Disposed = true;
    }

    readonly ManualTime time = new();
    readonly List<FakeSession> sessions = [];
    readonly List<EncodedVideoFrame> encoded = [];

    VideoEncoder Encoder(Action<VideoEncoder>? configure = null)
    {
        var encoder = new VideoEncoder(this.time);
        configure?.Invoke(encoder);
        encoder.SessionFactory = (settings, w, h, sink) =>
        {
            var session = new FakeSession(settings, w, h, sink);
            this.sessions.Add(session);
            return session;
        };
        encoder.FrameEncoded += this.encoded.Add;
        return encoder;
    }

    [Fact]
    public void Defaults_match_the_wifi_profile()
    {
        var encoder = new VideoEncoder();

        encoder.Codec.ShouldBe(VideoCodec.Jpeg);
        encoder.MaxDimension.ShouldBe(640);
        encoder.FrameRate.ShouldBe(15);
        encoder.Bitrate.ShouldBe(800_000);
        encoder.JpegQuality.ShouldBe(0.6);
        encoder.KeyFrameInterval.ShouldBe(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Nothing_encodes_on_the_platform_neutral_build()
    {
        VideoEncoder.IsSupported(VideoCodec.Jpeg).ShouldBeFalse();
        VideoEncoder.IsSupported(VideoCodec.H264).ShouldBeFalse();
    }

    [Fact]
    public void Wants_no_frames_without_a_subscriber()
        => new VideoEncoder(this.time).WantsFrame().ShouldBeFalse();

    [Fact]
    public void WantsFrame_enforces_the_frame_rate()
    {
        var encoder = this.Encoder(e => e.FrameRate = 10);

        var taken = 0;
        for (var i = 0; i < 30; i++)
        {
            if (encoder.WantsFrame())
                taken++;
            this.time.Advance(1000d / 30);
        }
        taken.ShouldBeInRange(9, 11);
    }

    [Fact]
    public void A_frame_rate_change_applies_while_running()
    {
        var encoder = this.Encoder();
        encoder.FrameRate = 3; // the BLE profile

        var taken = 0;
        for (var i = 0; i < 60; i++)
        {
            if (encoder.WantsFrame())
                taken++;
            this.time.Advance(1000d / 30);
        }
        taken.ShouldBeInRange(5, 7);
    }

    [Fact]
    public async Task Encodes_at_the_scaled_size_with_the_frame_orientation_and_no_boxes()
    {
        var encoder = this.Encoder();

        var boxes = await encoder.AnalyzeAsync(new FakeFrame(1920, 1080, 90, true), default);

        boxes.ShouldBeNull();
        var frame = this.encoded.ShouldHaveSingleItem();
        (frame.Width, frame.Height).ShouldBe((640, 360));
        frame.Rotation.ShouldBe(90);
        frame.IsMirrored.ShouldBeTrue();
        frame.IsKeyFrame.ShouldBeTrue();
    }

    [Fact]
    public async Task A_profile_change_rebuilds_the_session()
    {
        var encoder = this.Encoder(e => e.Codec = VideoCodec.H264);
        await encoder.AnalyzeAsync(new FakeFrame(1280, 720), default);
        await encoder.AnalyzeAsync(new FakeFrame(1280, 720), default);
        this.sessions.Count.ShouldBe(1);

        encoder.MaxDimension = 160;
        encoder.Bitrate = 64_000;
        await encoder.AnalyzeAsync(new FakeFrame(1280, 720), default);

        this.sessions.Count.ShouldBe(2);
        this.sessions[0].Disposed.ShouldBeTrue();
        (this.sessions[1].Width, this.sessions[1].Height).ShouldBe((160, 90));
        this.sessions[1].Settings.Bitrate.ShouldBe(64_000);
    }

    [Fact]
    public async Task A_rotated_buffer_rebuilds_the_session()
    {
        var encoder = this.Encoder(e => e.Codec = VideoCodec.H264);
        await encoder.AnalyzeAsync(new FakeFrame(1280, 720), default);
        await encoder.AnalyzeAsync(new FakeFrame(720, 1280), default);

        this.sessions.Count.ShouldBe(2);
        (this.sessions[1].Width, this.sessions[1].Height).ShouldBe((360, 640));
    }

    [Fact]
    public async Task RequestKeyFrame_forces_exactly_the_next_frame()
    {
        var encoder = this.Encoder(e => e.Codec = VideoCodec.H264);
        await encoder.AnalyzeAsync(new FakeFrame(640, 360), default);

        encoder.RequestKeyFrame();
        encoder.RequestKeyFrame(); // collapses into one
        await encoder.AnalyzeAsync(new FakeFrame(640, 360), default);
        await encoder.AnalyzeAsync(new FakeFrame(640, 360), default);

        this.sessions.Single().Forced.ShouldBe([false, true, false]);
    }

    [Fact]
    public async Task The_key_frame_interval_is_backstopped()
    {
        var encoder = this.Encoder(e =>
        {
            e.Codec = VideoCodec.H264;
            e.KeyFrameInterval = TimeSpan.FromMilliseconds(500);
        });

        for (var i = 0; i < 8; i++)
        {
            await encoder.AnalyzeAsync(new FakeFrame(640, 360), default);
            this.time.Advance(100);
        }

        // the first frame is a key frame; the encoder forces the next one once 500 ms have passed
        this.sessions.Single().Forced.ShouldBe([false, false, false, false, false, true, false, false]);
    }

    [Fact]
    public async Task Timestamps_run_from_the_first_frame()
    {
        var encoder = this.Encoder();
        await encoder.AnalyzeAsync(new FakeFrame(640, 360), default);
        this.time.Advance(250);
        await encoder.AnalyzeAsync(new FakeFrame(640, 360), default);

        this.encoded.Select(f => f.Timestamp).ShouldBe([TimeSpan.Zero, TimeSpan.FromMilliseconds(250)]);
    }

    [Fact]
    public async Task Jpeg_quality_changes_without_a_new_session()
    {
        var encoder = this.Encoder();
        await encoder.AnalyzeAsync(new FakeFrame(640, 360), default);

        encoder.JpegQuality = 0.4;
        await encoder.AnalyzeAsync(new FakeFrame(640, 360), default);

        this.sessions.Count.ShouldBe(1);
        encoder.CurrentSettings.JpegQuality.ShouldBe(0.4);
    }

    [Fact]
    public async Task A_throwing_subscriber_does_not_stop_the_stream()
    {
        var encoder = this.Encoder();
        encoder.FrameEncoded += _ => throw new InvalidOperationException();

        await encoder.AnalyzeAsync(new FakeFrame(640, 360), default);
        await encoder.AnalyzeAsync(new FakeFrame(640, 360), default);

        this.encoded.Count.ShouldBe(2);
        this.sessions.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_failing_session_is_rebuilt_after_a_back_off()
    {
        var encoder = new VideoEncoder(this.time);
        var attempts = 0;
        encoder.SessionFactory = (s, w, h, sink) =>
        {
            attempts++;
            return attempts == 1 ? new ThrowingSession() : new FakeSession(s, w, h, sink);
        };
        encoder.FrameEncoded += this.encoded.Add;

        await encoder.AnalyzeAsync(new FakeFrame(640, 360), default); // throws, backs off
        await encoder.AnalyzeAsync(new FakeFrame(640, 360), default); // inside the back-off
        attempts.ShouldBe(1);

        this.time.Advance(1100);
        await encoder.AnalyzeAsync(new FakeFrame(640, 360), default);
        attempts.ShouldBe(2);
        this.encoded.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Detaching_releases_the_session()
    {
        var encoder = this.Encoder();
        await encoder.AnalyzeAsync(new FakeFrame(640, 360), default);

        ((IFrameAnalyzer)encoder).OnDetached();

        this.sessions.Single().Disposed.ShouldBeTrue();
    }

    sealed class ThrowingSession : IVideoEncoderSession
    {
        public bool CanEncode(VideoEncoderSettings settings, int width, int height) => true;
        public ValueTask EncodeAsync(VideoFrame frame, int width, int height, TimeSpan timestamp, bool forceKeyFrame, CancellationToken ct)
            => throw new InvalidOperationException("codec reclaimed");
        public void Dispose() { }
    }
}
