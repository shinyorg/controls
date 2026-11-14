namespace Shiny.Controls.Analysis;

/// <summary>
/// The sample rate and channel count an <see cref="IAudioAnalyzer"/> wants its audio in. The host converts once,
/// before the frame is queued, so an analyzer never resamples for itself.
/// </summary>
/// <param name="SampleRate">Samples per second, per channel. <c>0</c> keeps whatever the source plays at.</param>
/// <param name="Channels">Channel count. <c>0</c> keeps the source's; <c>1</c> downmixes by averaging.</param>
public readonly record struct AudioAnalysisFormat(int SampleRate, int Channels)
{
    /// <summary>16 kHz mono — what speech recognizers take, and the default.</summary>
    public static AudioAnalysisFormat Speech { get; } = new(16000, 1);

    /// <summary>The source's own rate and channels, unconverted (a spectrum display, a level meter).</summary>
    public static AudioAnalysisFormat Native { get; } = new(0, 0);
}
