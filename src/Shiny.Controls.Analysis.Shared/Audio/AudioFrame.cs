namespace Shiny.Controls.Analysis;

/// <summary>
/// A run of audio handed to an <see cref="IAudioAnalyzer"/>: interleaved, normalized float samples (-1..1) in the
/// format the analyzer asked for (<see cref="IAudioAnalyzer.Format"/>), plus where in the media they play.
/// </summary>
/// <remarks>
/// <para>
/// Unlike a <see cref="VideoFrame"/>, an audio frame owns its buffer and is safe to keep: audio is small (100 ms of
/// 16 kHz mono is 6.4 KB), and a speech recognizer that buffers a sentence before it commits needs to.
/// </para>
/// <para>
/// <see cref="Position"/> is derived, not read from the platform: neither the Apple processing tap nor the Android
/// audio sink reports a usable timestamp, so the host counts samples since the last seek and anchors them to the
/// player position at that seek. It is accurate to the buffer, which is what captions need.
/// </para>
/// </remarks>
public sealed class AudioFrame
{
    /// <summary>Creates a frame over <paramref name="samples"/>, which it takes ownership of.</summary>
    public AudioFrame(ReadOnlyMemory<float> samples, int sampleRate, int channels, TimeSpan position, bool isDiscontinuity = false)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (channels <= 0)
            throw new ArgumentOutOfRangeException(nameof(channels));

        this.Samples = samples;
        this.SampleRate = sampleRate;
        this.Channels = channels;
        this.Position = position;
        this.IsDiscontinuity = isDiscontinuity;
    }

    /// <summary>Interleaved samples, normalized to -1..1.</summary>
    public ReadOnlyMemory<float> Samples { get; }

    /// <summary>Samples per second, per channel.</summary>
    public int SampleRate { get; }

    /// <summary>Channel count; samples are interleaved when greater than one.</summary>
    public int Channels { get; }

    /// <summary>Samples per channel in this frame.</summary>
    public int FrameCount => this.Samples.Length / this.Channels;

    /// <summary>How long this frame plays for.</summary>
    public TimeSpan Duration => TimeSpan.FromSeconds((double)this.FrameCount / this.SampleRate);

    /// <summary>Where the first sample plays: the media position for a <c>MediaElement</c>, time since capture started for a <c>CameraView</c>.</summary>
    public TimeSpan Position { get; }

    /// <summary>
    /// <c>true</c> when this frame does not follow on from the previous one — the first frame after a seek, or after
    /// the pipeline had to drop audio because the analyzer fell behind. A recognizer should finish (or abandon) the
    /// utterance in progress instead of joining two unrelated pieces of speech.
    /// </summary>
    public bool IsDiscontinuity { get; }

    /// <summary>Writes the samples as signed 16-bit PCM into <paramref name="destination"/>, which must hold <c>Samples.Length</c> values.</summary>
    public void CopyToPcm16(Span<short> destination)
    {
        var source = this.Samples.Span;
        if (destination.Length < source.Length)
            throw new ArgumentException("Destination is smaller than the frame.", nameof(destination));

        for (var i = 0; i < source.Length; i++)
            destination[i] = ToPcm16(source[i]);
    }

    /// <summary>
    /// The samples as little-endian signed 16-bit PCM bytes — the format speech-to-text services take (with
    /// <see cref="AudioAnalysisFormat.Speech"/>, 16 kHz mono, it is exactly what <c>Shiny.Speech</c> providers expect).
    /// </summary>
    public byte[] ToPcm16Bytes()
    {
        var source = this.Samples.Span;
        var bytes = new byte[source.Length * 2];
        for (var i = 0; i < source.Length; i++)
        {
            var v = ToPcm16(source[i]);
            bytes[i * 2] = (byte)v;
            bytes[i * 2 + 1] = (byte)(v >> 8);
        }
        return bytes;
    }

    /// <summary>The root-mean-square level of the frame, 0..1 — enough to drive a level meter or a simple voice gate.</summary>
    public float GetRms()
    {
        var source = this.Samples.Span;
        if (source.Length == 0)
            return 0f;

        double sum = 0;
        foreach (var s in source)
            sum += s * s;
        return (float)Math.Sqrt(sum / source.Length);
    }

    static short ToPcm16(float sample)
    {
        var clamped = Math.Clamp(sample, -1f, 1f);
        return (short)Math.Round(clamped * short.MaxValue);
    }
}
