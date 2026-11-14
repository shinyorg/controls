namespace Shiny.Controls.Analysis;

/// <summary>
/// Splits a stream of audio into utterances at pauses — a simple energy voice-activity detector. A recognizer that
/// reports no timings of its own (a cloud speech-to-text call) can then be handed one utterance at a time and the
/// caption takes the utterance's start and end, which is what keeps cloud captions in step with the picture.
/// </summary>
/// <remarks>
/// <para>
/// The threshold adapts: it tracks the quietest recent level as the noise floor and calls anything
/// <see cref="SpeechRatio"/> times louder speech, so music beds and room tone do not read as one endless utterance.
/// It is not a model — a loud, continuous soundtrack under dialogue will merge utterances, and the
/// <see cref="MaxUtterance"/> cap is what bounds them then.
/// </para>
/// <para>Not thread-safe: feed it from one thread, in order.</para>
/// </remarks>
public sealed class SpeechSegmenter
{
    readonly List<float> buffer = [];
    int sampleRate;
    TimeSpan bufferStart;
    TimeSpan silence;
    bool inSpeech;
    float noiseFloor = 0.01f;

    /// <summary>How long a pause ends an utterance. Default 500 ms.</summary>
    public TimeSpan MinSilence { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>The longest utterance before it is cut regardless. Default 15 s.</summary>
    public TimeSpan MaxUtterance { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Utterances shorter than this are dropped as noise. Default 250 ms.</summary>
    public TimeSpan MinUtterance { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>How far above the noise floor counts as speech. Default 3.</summary>
    public float SpeechRatio { get; set; } = 3f;

    /// <summary>
    /// Feed one mono frame (any rate, but every frame at the same one). Returns the utterances it completed, usually
    /// none; each carries its own samples and media-time span.
    /// </summary>
    public IReadOnlyList<SpeechSegment> Push(AudioFrame frame)
    {
        if (frame.Channels != 1)
            throw new ArgumentException("SpeechSegmenter takes mono audio; analyze with AudioAnalysisFormat.Speech.", nameof(frame));

        var done = new List<SpeechSegment>();
        if (frame.IsDiscontinuity || (this.sampleRate != 0 && this.sampleRate != frame.SampleRate))
            this.Flush(done);

        this.sampleRate = frame.SampleRate;
        var rms = frame.GetRms();
        this.noiseFloor = rms < this.noiseFloor
            ? rms * 0.5f + this.noiseFloor * 0.5f
            : this.noiseFloor * 0.999f + rms * 0.001f;
        var speaking = rms > Math.Max(0.004f, this.noiseFloor * this.SpeechRatio);

        if (!this.inSpeech)
        {
            if (!speaking)
                return done;

            this.inSpeech = true;
            this.silence = TimeSpan.Zero;
            this.bufferStart = frame.Position;
            this.buffer.Clear();
        }

        foreach (var s in frame.Samples.Span)
            this.buffer.Add(s);

        this.silence = speaking ? TimeSpan.Zero : this.silence + frame.Duration;
        var length = TimeSpan.FromSeconds((double)this.buffer.Count / this.sampleRate);
        if (this.silence >= this.MinSilence || length >= this.MaxUtterance)
            this.Flush(done);

        return done;
    }

    /// <summary>End the stream: returns the utterance in progress, if any.</summary>
    public IReadOnlyList<SpeechSegment> Complete()
    {
        var done = new List<SpeechSegment>();
        this.Flush(done);
        return done;
    }

    void Flush(List<SpeechSegment> into)
    {
        if (this.inSpeech && this.buffer.Count > 0 && this.sampleRate > 0)
        {
            // trim the trailing silence that ended it, so the cue does not linger
            var trailing = (int)(Math.Min(this.silence.TotalSeconds, this.buffer.Count / (double)this.sampleRate) * this.sampleRate);
            var count = this.buffer.Count - trailing;
            var length = TimeSpan.FromSeconds((double)count / this.sampleRate);
            if (length >= this.MinUtterance)
            {
                var samples = this.buffer.GetRange(0, count).ToArray();
                into.Add(new SpeechSegment(this.bufferStart, this.bufferStart + length, new AudioFrame(samples, this.sampleRate, 1, this.bufferStart)));
            }
        }

        this.inSpeech = false;
        this.silence = TimeSpan.Zero;
        this.buffer.Clear();
    }
}

/// <summary>An utterance cut out of the audio by <see cref="SpeechSegmenter"/>.</summary>
/// <param name="Start">Where it starts, in media time.</param>
/// <param name="End">Where it ends, in media time.</param>
/// <param name="Audio">Its samples, mono.</param>
public record SpeechSegment(TimeSpan Start, TimeSpan End, AudioFrame Audio);
