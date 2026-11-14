using Microsoft.Maui.Graphics;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Camera.Tests;

public class AudioFormatConverterTests
{
    [Fact]
    public void Matching_formats_pass_through_unchanged()
    {
        var converter = new AudioFormatConverter(16000, 1, AudioAnalysisFormat.Speech);
        converter.IsPassthrough.ShouldBeTrue();
        converter.Convert([0.1f, 0.2f, 0.3f]).ShouldBe([0.1f, 0.2f, 0.3f]);
    }

    [Fact]
    public void Stereo_48k_becomes_mono_16k_averaging_channels()
    {
        var converter = new AudioFormatConverter(48000, 2, AudioAnalysisFormat.Speech);
        var stereo = new float[48000 * 2];
        for (var i = 0; i < 48000; i++)
        {
            stereo[i * 2] = 0.5f;
            stereo[i * 2 + 1] = -0.1f;
        }

        var mono = converter.Convert(stereo);

        mono.Length.ShouldBeInRange(15999, 16001);
        mono.All(s => Math.Abs(s - 0.2f) < 0.0001f).ShouldBeTrue();
    }

    [Fact]
    public void The_phase_carries_across_buffers_so_nothing_is_lost_at_the_edges()
    {
        var converter = new AudioFormatConverter(44100, 1, AudioAnalysisFormat.Speech);
        var total = 0;
        // 441-frame buffers do not divide evenly by the 2.75625 ratio
        for (var i = 0; i < 100; i++)
            total += converter.Convert(new float[441]).Length;

        total.ShouldBeInRange(15999, 16001);
    }

    [Fact]
    public void Upsampling_interpolates()
    {
        var converter = new AudioFormatConverter(8000, 1, AudioAnalysisFormat.Speech);
        var output = converter.Convert(new float[8000]);
        output.Length.ShouldBeInRange(15998, 16002);
    }
}

public class SpeechSegmenterTests
{
    static AudioFrame Frame(float level, TimeSpan position, int samples = 1600)
        => new(Enumerable.Repeat(level, samples).ToArray(), 16000, 1, position);

    [Fact]
    public void An_utterance_between_pauses_comes_out_with_its_span()
    {
        var segmenter = new SpeechSegmenter();
        var segments = new List<SpeechSegment>();
        var t = TimeSpan.Zero;

        for (var i = 0; i < 10; i++, t += TimeSpan.FromMilliseconds(100))
            segments.AddRange(segmenter.Push(Frame(0.001f, t)));   // 1 s of room tone
        var speechStart = t;
        for (var i = 0; i < 15; i++, t += TimeSpan.FromMilliseconds(100))
            segments.AddRange(segmenter.Push(Frame(0.3f, t)));     // 1.5 s of speech
        for (var i = 0; i < 10; i++, t += TimeSpan.FromMilliseconds(100))
            segments.AddRange(segmenter.Push(Frame(0.001f, t)));   // 1 s of quiet

        segments.Count.ShouldBe(1);
        segments[0].Start.ShouldBe(speechStart);
        (segments[0].End - segments[0].Start).TotalSeconds.ShouldBe(1.5, 0.15);
    }

    [Fact]
    public void A_long_monologue_is_cut_at_the_cap()
    {
        var segmenter = new SpeechSegmenter { MaxUtterance = TimeSpan.FromSeconds(2) };
        var segments = new List<SpeechSegment>();
        var t = TimeSpan.Zero;
        for (var i = 0; i < 50; i++, t += TimeSpan.FromMilliseconds(100))
            segments.AddRange(segmenter.Push(Frame(0.3f, t)));

        segments.Count.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void A_discontinuity_ends_the_utterance_in_progress()
    {
        var segmenter = new SpeechSegmenter();
        for (var i = 0; i < 10; i++)
            segmenter.Push(Frame(0.3f, TimeSpan.FromMilliseconds(i * 100)));

        var jumped = new AudioFrame(new float[1600], 16000, 1, TimeSpan.FromSeconds(60), isDiscontinuity: true);
        segmenter.Push(jumped).Count.ShouldBe(1);
    }
}

public class CaptionCueTests
{
    readonly CaptionCue[] cues =
    [
        new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "one"),
        new(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4.5), "two"),
        new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(6), "three")
    ];

    [Theory]
    [InlineData(0.5, null)]
    [InlineData(1.0, "one")]
    [InlineData(2.5, null)]
    [InlineData(4.4, "two")]
    [InlineData(5.99, "three")]
    [InlineData(6.0, null)]
    public void CueAt_finds_the_cue_playing(double seconds, string? expected)
        => this.cues.CueAt(TimeSpan.FromSeconds(seconds))?.Text.ShouldBe(expected);

    [Fact]
    public void WebVtt_has_the_header_and_timings()
    {
        var vtt = this.cues.ToWebVtt();
        vtt.ShouldStartWith("WEBVTT");
        vtt.ShouldContain("00:00:03.000 --> 00:00:04.500\ntwo");
    }
}

public class AudioAnalysisPipelineTests
{
    sealed class Recorder : IAudioAnalyzer, ISourceAudioAnalyzer
    {
        public readonly List<string> Events = [];
        public bool TakeSources = true;
        public string Id => "recorder";
        public bool WantsSource => this.TakeSources;

        public ValueTask AnalyzeAsync(AudioFrame frame, CancellationToken ct)
        {
            lock (this.Events)
                this.Events.Add($"frame@{frame.Position.TotalMilliseconds:0}{(frame.IsDiscontinuity ? "!" : "")}");
            return ValueTask.CompletedTask;
        }

        public async ValueTask AnalyzeSourceAsync(IAsyncEnumerable<AudioFrame> audio, CancellationToken ct)
        {
            var n = 0;
            await foreach (var _ in audio.WithCancellation(ct))
                n++;
            lock (this.Events)
                this.Events.Add($"source:{n}");
        }

        public void OnSeek(TimeSpan position)
        {
            lock (this.Events)
                this.Events.Add($"seek@{position.TotalMilliseconds:0}");
        }

        public void OnSourceChanged()
        {
            lock (this.Events)
                this.Events.Add("source-changed");
        }

        public async Task<string[]> WaitAsync(int count)
        {
            for (var i = 0; i < 300; i++)
            {
                lock (this.Events)
                {
                    if (this.Events.Count >= count)
                        return this.Events.ToArray();
                }
                await Task.Delay(10);
            }
            lock (this.Events)
                throw new TimeoutException(String.Join(", ", this.Events));
        }
    }

    [Fact]
    public async Task Seeks_land_in_order_and_re_anchor_positions()
    {
        var pipeline = new AudioAnalysisPipeline();
        var analyzer = new Recorder();
        pipeline.SetAnalyzer(analyzer);

        pipeline.Push(new float[1600], 16000, 1);
        pipeline.Push(new float[1600], 16000, 1);
        pipeline.Discontinuity(TimeSpan.FromSeconds(30));
        pipeline.Push(new float[1600], 16000, 1);

        var events = await analyzer.WaitAsync(4);
        events.ShouldBe(["frame@0!", "frame@100", "seek@30000", "frame@30000!"]);
    }

    [Fact]
    public async Task A_taken_source_stops_live_audio_until_it_turns_out_empty()
    {
        var pipeline = new AudioAnalysisPipeline();
        var analyzer = new Recorder();
        pipeline.SetAnalyzer(analyzer);

        pipeline.TryAnalyzeSource(_ => Empty()).ShouldBeTrue();
        pipeline.WantsAudio().ShouldBeFalse();

        (await analyzer.WaitAsync(1)).ShouldBe(["source:0"]);
        // nothing decoded: live delivery resumes
        for (var i = 0; i < 100 && !pipeline.WantsAudio(); i++)
            await Task.Delay(10);
        pipeline.WantsAudio().ShouldBeTrue();

        static async IAsyncEnumerable<AudioFrame> Empty()
        {
            await Task.Yield();
            yield break;
        }
    }

    [Fact]
    public void An_analyzer_that_declines_sources_keeps_live_audio()
    {
        var pipeline = new AudioAnalysisPipeline();
        pipeline.SetAnalyzer(new Recorder { TakeSources = false });

        pipeline.TryAnalyzeSource(_ => throw new InvalidOperationException("not asked")).ShouldBeFalse();
        pipeline.WantsAudio().ShouldBeTrue();
    }

    [Fact]
    public async Task A_source_change_is_delivered_before_the_new_source()
    {
        var pipeline = new AudioAnalysisPipeline();
        var analyzer = new Recorder();
        pipeline.SetAnalyzer(analyzer);

        pipeline.SourceChanged();
        pipeline.TryAnalyzeSource(format => One(format)).ShouldBeTrue();

        (await analyzer.WaitAsync(2)).ShouldBe(["source-changed", "source:1"]);

        static async IAsyncEnumerable<AudioFrame> One(AudioAnalysisFormat format)
        {
            await Task.Yield();
            yield return new AudioFrame(new float[160], format.SampleRate, 1, TimeSpan.Zero);
        }
    }

    [Fact]
    public void Nothing_is_wanted_without_an_enabled_analyzer()
    {
        var pipeline = new AudioAnalysisPipeline();
        pipeline.WantsAudio().ShouldBeFalse();
        pipeline.HasAnalyzer.ShouldBeFalse();
    }
}

public class FrameDiscontinuityTests
{
    sealed class Tracking : IFrameAnalyzer
    {
        public int Resets;
        public readonly List<bool> Flags = [];
        public string Id => "tracking";

        public ValueTask<IReadOnlyList<OverlayBox>?> AnalyzeAsync(VideoFrame frame, CancellationToken ct)
        {
            this.Flags.Add(frame.IsDiscontinuity);
            return ValueTask.FromResult<IReadOnlyList<OverlayBox>?>([new OverlayBox(new RectF(0, 0, 1, 1))]);
        }

        public void OnDiscontinuity() => this.Resets++;
    }

    sealed class Frame : VideoFrame
    {
        public override int Width => 4;
        public override int Height => 4;
        public override int Rotation => 0;
        public override bool IsMirrored => false;
        public override VideoFrameFormat Format => VideoFrameFormat.Grayscale8;
        protected override byte[] MaterializeLuminance() => new byte[16];
    }

    [Fact]
    public void Only_the_first_frame_after_a_jump_is_flagged_and_the_reset_runs_before_it()
    {
        var pipeline = new FrameAnalysisPipeline();
        var analyzer = new Tracking();
        IReadOnlyList<OverlayBox> latest = [];
        pipeline.OnOverlays = (boxes, _, _, _) => latest = boxes;
        pipeline.SetAnalyzer(analyzer);

        pipeline.Process(new Frame(), default);
        latest.Count.ShouldBe(1);

        pipeline.SignalDiscontinuity();
        latest.ShouldBeEmpty();

        pipeline.Process(new Frame(), default);
        pipeline.Process(new Frame(), default);

        analyzer.Flags.ShouldBe([false, true, false]);
        analyzer.Resets.ShouldBe(1);
    }
}
