namespace Shiny.Controls.Analysis;

/// <summary>
/// A streaming sample-rate and channel converter: feed it the platform's buffers in order and it carries its phase
/// across calls, so a stream of 44.1 kHz stereo buffers comes out as one continuous 16 kHz mono stream with no
/// clicks at the buffer edges.
/// </summary>
/// <remarks>
/// <para>
/// Downsampling averages every input sample that falls inside each output sample's window (a box filter). That is
/// a modest anti-alias filter, but speech has almost no energy above 4 kHz, so for the 16 kHz target the aliasing
/// it lets through is far below anything a recognizer notices — and it costs one add per input sample, which matters
/// on the audio thread. Upsampling interpolates linearly.
/// </para>
/// <para>
/// Not thread-safe: one converter per stream, called from one thread. Call <see cref="Reset"/> after a seek so the
/// carried phase from before the jump is not blended into the audio after it.
/// </para>
/// </remarks>
public sealed class AudioFormatConverter
{
    readonly int inRate;
    readonly int inChannels;
    readonly int outChannels;
    readonly double step;            // input frames per output frame
    readonly float[] sums;
    readonly float[] previous;
    readonly float[] current;
    double accumulated;              // input frames gathered toward the next output (downsampling)
    double fraction;                 // position between previous and current (upsampling)
    bool primed;

    /// <summary>Creates a converter from <paramref name="inRate"/>/<paramref name="inChannels"/> to <paramref name="target"/>.</summary>
    public AudioFormatConverter(int inRate, int inChannels, AudioAnalysisFormat target)
    {
        if (inRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(inRate));
        if (inChannels <= 0)
            throw new ArgumentOutOfRangeException(nameof(inChannels));

        this.inRate = inRate;
        this.inChannels = inChannels;
        this.OutputRate = target.SampleRate > 0 ? target.SampleRate : inRate;
        this.outChannels = target.Channels > 0 ? target.Channels : inChannels;
        this.step = (double)inRate / this.OutputRate;
        this.sums = new float[this.outChannels];
        this.previous = new float[this.outChannels];
        this.current = new float[this.outChannels];
    }

    /// <summary>The sample rate this converter produces.</summary>
    public int OutputRate { get; }

    /// <summary>The channel count this converter produces.</summary>
    public int OutputChannels => this.outChannels;

    /// <summary>Whether the input already matches the output, so <see cref="Convert"/> only copies.</summary>
    public bool IsPassthrough => this.inRate == this.OutputRate && this.inChannels == this.outChannels;

    /// <summary>Forget the carried phase — call after a seek or any other break in the input.</summary>
    public void Reset()
    {
        this.accumulated = 0;
        this.fraction = 0;
        this.primed = false;
        Array.Clear(this.sums);
    }

    /// <summary>
    /// Convert one buffer of interleaved input. Returns the converted interleaved samples, which may be empty when
    /// the buffer was too short to complete an output sample (the remainder carries into the next call).
    /// </summary>
    public float[] Convert(ReadOnlySpan<float> interleaved)
    {
        var inFrames = interleaved.Length / this.inChannels;
        if (inFrames == 0)
            return [];

        if (this.IsPassthrough)
            return interleaved[..(inFrames * this.inChannels)].ToArray();

        var estimate = (int)Math.Ceiling(inFrames / this.step) + 2;
        var output = new float[estimate * this.outChannels];
        var written = 0;
        Span<float> mapped = stackalloc float[this.outChannels];

        for (var f = 0; f < inFrames; f++)
        {
            this.MapChannels(interleaved.Slice(f * this.inChannels, this.inChannels), mapped);

            if (this.step >= 1d)
            {
                for (var c = 0; c < this.outChannels; c++)
                    this.sums[c] += mapped[c];
                this.accumulated += 1d;

                if (this.accumulated >= this.step)
                {
                    for (var c = 0; c < this.outChannels; c++)
                    {
                        output[written * this.outChannels + c] = this.sums[c] / (float)this.accumulated;
                        this.sums[c] = 0f;
                    }
                    written++;
                    this.accumulated -= this.step;
                    if (this.accumulated < 0)
                        this.accumulated = 0;
                }
            }
            else
            {
                mapped.CopyTo(this.current);
                if (!this.primed)
                {
                    this.current.CopyTo(this.previous, 0);
                    this.primed = true;
                }

                while (this.fraction < 1d)
                {
                    if (written >= estimate)
                        break;
                    var t = (float)this.fraction;
                    for (var c = 0; c < this.outChannels; c++)
                        output[written * this.outChannels + c] = this.previous[c] + (this.current[c] - this.previous[c]) * t;
                    written++;
                    this.fraction += this.step;
                }
                this.fraction -= 1d;
                this.current.CopyTo(this.previous, 0);
            }
        }

        return written == estimate ? output : output.AsSpan(0, written * this.outChannels).ToArray();
    }

    void MapChannels(ReadOnlySpan<float> frame, Span<float> into)
    {
        if (this.outChannels == this.inChannels)
        {
            frame.CopyTo(into);
            return;
        }

        if (this.outChannels == 1)
        {
            var sum = 0f;
            foreach (var s in frame)
                sum += s;
            into[0] = sum / frame.Length;
            return;
        }

        for (var c = 0; c < into.Length; c++)
            into[c] = frame[Math.Min(c, frame.Length - 1)];
    }
}
