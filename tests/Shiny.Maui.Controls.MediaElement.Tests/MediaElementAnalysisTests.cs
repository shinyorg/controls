using Microsoft.Maui.Graphics;
using Shiny.Controls.Media;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Media.Tests;

public class MediaElementAnalysisTests : MediaTestBase
{
    [Fact]
    public void An_analyzer_is_handed_to_the_backend_as_a_sink_with_frames_enabled()
    {
        var element = new MediaElement { Analyzer = new CountingFrameAnalyzer() }.Connected();

        this.Backend.AnalysisSink.ShouldNotBeNull();
        this.Backend.AnalysisSink!.FramesEnabled.ShouldBeTrue();
        this.Backend.AnalysisSink.AudioEnabled.ShouldBeFalse();
        element.Analyzer.ShouldNotBeNull();
    }

    [Fact]
    public void No_analyzer_means_no_sink()
    {
        new MediaElement().Connected();
        this.Backend.AnalysisSink.ShouldBeNull();
    }

    [Fact]
    public void Clearing_the_analyzer_removes_the_sink()
    {
        var element = new MediaElement { Analyzer = new CountingFrameAnalyzer() }.Connected();
        element.Analyzer = null;
        this.Backend.AnalysisSink.ShouldBeNull();
    }

    [Fact]
    public void Disabling_the_analyzer_removes_the_sink_and_reenabling_restores_it()
    {
        var analyzer = new CountingFrameAnalyzer();
        new MediaElement { Analyzer = analyzer }.Connected();

        analyzer.IsEnabled = false;
        this.Backend.AnalysisSink.ShouldBeNull();

        analyzer.IsEnabled = true;
        this.Backend.AnalysisSink.ShouldNotBeNull();
    }

    [Fact]
    public void The_analyzer_sees_the_element_as_its_media_host()
    {
        var analyzer = new CountingFrameAnalyzer();
        var element = new MediaElement { Analyzer = analyzer };

        analyzer.HostForTest.ShouldBeSameAs(element);
        ((IAnalysisHost)element).SourceKind.ShouldBe(AnalysisSourceKind.Media);
        analyzer.HostForTest.ShouldNotBeAssignableTo<IZoomableAnalysisHost>();

        element.Analyzer = null;
        analyzer.HostForTest.ShouldBeNull();
    }

    [Fact]
    public void Frames_from_the_backend_reach_the_analyzer_and_draw_boxes()
    {
        var analyzer = new CountingFrameAnalyzer { Result = [new OverlayBox(new RectF(0.1f, 0.1f, 0.2f, 0.2f))] };
        var element = new MediaElement { Analyzer = analyzer }.Connected();
        AnalysisOverlaysChangedEventArgs? overlays = null;
        element.OverlaysChanged += (_, e) => overlays = e;

        var sink = this.Backend.AnalysisSink!;
        sink.WantsFrame().ShouldBeTrue();
        sink.OnFrame(new TestFrame());

        analyzer.Frames.ShouldBe(1);
        overlays.ShouldNotBeNull();
        overlays!.Overlays.Count.ShouldBe(1);
    }

    [Fact]
    public void A_seek_reported_by_the_backend_resets_a_tracking_analyzer_and_clears_its_boxes()
    {
        var analyzer = new CountingFrameAnalyzer { Result = [new OverlayBox(new RectF(0.1f, 0.1f, 0.2f, 0.2f))] };
        var element = new MediaElement { Analyzer = analyzer }.Connected();
        AnalysisOverlaysChangedEventArgs? overlays = null;
        element.OverlaysChanged += (_, e) => overlays = e;
        var sink = this.Backend.AnalysisSink!;

        sink.OnFrame(new TestFrame());
        sink.OnDiscontinuity(TimeSpan.FromSeconds(30));
        overlays!.Overlays.ShouldBeEmpty(); // boxes from before the jump are gone at once

        sink.OnFrame(new TestFrame());
        analyzer.Discontinuities.ShouldBe(1);
        analyzer.LastFrameWasDiscontinuity.ShouldBeTrue();
    }

    [Fact]
    public void An_audio_analyzer_enables_audio_on_the_sink()
    {
        new MediaElement { AudioAnalyzer = new RecordingAudioAnalyzer() }.Connected();

        this.Backend.AnalysisSink.ShouldNotBeNull();
        this.Backend.AnalysisSink!.AudioEnabled.ShouldBeTrue();
        this.Backend.AnalysisSink.FramesEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task Audio_from_the_backend_reaches_the_analyzer_at_its_format_with_counted_positions()
    {
        var analyzer = new RecordingAudioAnalyzer();
        new MediaElement { AudioAnalyzer = analyzer }.Connected();
        var sink = this.Backend.AnalysisSink!;

        // one second of 48 kHz stereo in 10 buffers -> 16 kHz mono
        for (var i = 0; i < 10; i++)
            sink.OnAudio(new float[4800 * 2], 48000, 2);

        await analyzer.WaitForFramesAsync(10);
        analyzer.Frames.All(f => f.SampleRate == 16000 && f.Channels == 1).ShouldBeTrue();
        analyzer.Frames.Sum(f => f.FrameCount).ShouldBeInRange(15990, 16010);
        analyzer.Frames[^1].Position.TotalSeconds.ShouldBe(0.9, 0.01);
    }

    [Fact]
    public async Task A_new_source_offers_the_whole_source_to_a_source_analyzer()
    {
        this.Backend.Capabilities |= MediaPlaybackCapabilities.SourceAudioAnalysis;
        this.Backend.Decoder = (_, format) => Frames(format, 3);
        var analyzer = new RecordingAudioAnalyzer();

        var element = new MediaElement { AudioAnalyzer = analyzer }.Connected();
        element.Source = MediaSource.FromUri("https://example.com/talk.mp4");

        await analyzer.WaitForSourceAsync();
        analyzer.SourceFrames.ShouldBe(3);
        analyzer.SourceChanges.ShouldBeGreaterThanOrEqualTo(1);
    }

    static async IAsyncEnumerable<AudioFrame> Frames(AudioAnalysisFormat format, int count)
    {
        for (var i = 0; i < count; i++)
        {
            await Task.Yield();
            yield return new AudioFrame(new float[1600], format.SampleRate, 1, TimeSpan.FromSeconds(i * 0.1));
        }
    }


    sealed class CountingFrameAnalyzer : FrameAnalyzer
    {
        public int Frames;
        public int Discontinuities;
        public bool LastFrameWasDiscontinuity;
        public IReadOnlyList<OverlayBox>? Result;

        public IAnalysisHost? HostForTest => this.Host;

        public override string Id => "counting";

        public override ValueTask<IReadOnlyList<OverlayBox>?> AnalyzeAsync(VideoFrame frame, CancellationToken ct)
        {
            this.Frames++;
            this.LastFrameWasDiscontinuity = frame.IsDiscontinuity;
            return ValueTask.FromResult(this.Result);
        }

        protected override void OnDiscontinuity() => this.Discontinuities++;
    }

    sealed class RecordingAudioAnalyzer : AudioAnalyzer, ISourceAudioAnalyzer
    {
        readonly TaskCompletionSource source = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<AudioFrame> Frames { get; } = [];
        public int SourceFrames;
        public int SourceChanges;

        public override string Id => "recording";

        public override ValueTask AnalyzeAsync(AudioFrame frame, CancellationToken ct)
        {
            lock (this.Frames)
                this.Frames.Add(frame);
            return ValueTask.CompletedTask;
        }

        public async ValueTask AnalyzeSourceAsync(IAsyncEnumerable<AudioFrame> audio, CancellationToken ct)
        {
            await foreach (var _ in audio.WithCancellation(ct))
                this.SourceFrames++;
            this.source.TrySetResult();
        }

        protected override void OnSourceChanged() => this.SourceChanges++;

        public async Task WaitForFramesAsync(int count)
        {
            for (var i = 0; i < 200; i++)
            {
                lock (this.Frames)
                {
                    if (this.Frames.Count >= count)
                        return;
                }
                await Task.Delay(10);
            }
            throw new TimeoutException($"Only {this.Frames.Count} audio frames arrived");
        }

        public Task WaitForSourceAsync() => this.source.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    sealed class TestFrame : VideoFrame
    {
        public override int Width => 8;
        public override int Height => 8;
        public override int Rotation => 0;
        public override bool IsMirrored => false;
        public override VideoFrameFormat Format => VideoFrameFormat.Grayscale8;
        protected override byte[] MaterializeLuminance() => new byte[64];
    }
}
