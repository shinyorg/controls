namespace Shiny.Controls.Analysis;

/// <summary>
/// A pluggable audio analyzer — the audio counterpart to <see cref="IFrameAnalyzer"/>. Assign one to a
/// <c>MediaElement</c> (the audio it plays) or a <c>CameraView</c> (the microphone it captures) and it is handed the
/// audio as it goes by, converted to <see cref="Format"/>. Results are raised through the analyzer's own typed events
/// (a transcript, a level, a detected sound); nothing is drawn for it.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Audio is not dropped the way frames are.</b> A frame analyzer that falls behind just misses frames; a
/// speech recognizer that misses 200 ms loses a word. Each analyzer gets its own bounded queue, filled from the
/// platform's audio thread and drained by <see cref="AnalyzeAsync"/> one frame at a time, in order. Only when the
/// queue overflows (the analyzer is slower than real time for seconds on end) is the oldest audio discarded, and the
/// next frame then carries <see cref="AudioFrame.IsDiscontinuity"/>.
/// </para>
/// <para>
/// Prefer deriving from <c>AudioAnalyzer</c> (in <c>Shiny.Maui.Controls.Analysis</c>), which marshals events onto the
/// UI thread and adds <c>IsEnabled</c>.
/// </para>
/// </remarks>
public interface IAudioAnalyzer
{
    /// <summary>Stable identifier for the analyzer.</summary>
    string Id { get; }

    /// <summary>The format to convert audio to before it is queued. Default <see cref="AudioAnalysisFormat.Speech"/> (16 kHz mono).</summary>
    AudioAnalysisFormat Format => AudioAnalysisFormat.Speech;

    /// <summary>
    /// Whether the analyzer wants audio right now. Called on the platform's audio thread before anything is
    /// converted or queued — return <c>false</c> to skip a buffer for nothing. Must be cheap and must not block.
    /// Default <c>true</c>.
    /// </summary>
    bool WantsAudio() => true;

    /// <summary>
    /// Analyze one frame. Frames arrive in order, one at a time, off the UI thread. Honor <paramref name="ct"/>,
    /// which is cancelled when the analyzer is detached or its host stops.
    /// </summary>
    ValueTask AnalyzeAsync(AudioFrame frame, CancellationToken ct);

    /// <summary>
    /// Playback jumped to <paramref name="position"/> (a seek, a new source). Finish or abandon whatever was in
    /// progress; the next frame starts there with <see cref="AudioFrame.IsDiscontinuity"/> set. Called on the
    /// analysis thread, between frames. Default no-op.
    /// </summary>
    void OnSeek(TimeSpan position) { }

    /// <summary>
    /// A new source opened (a different video, or the host restarted its capture). Drop everything kept from the
    /// previous one — a transcript, a running level. Called on the analysis thread, in order with the audio. Default
    /// no-op.
    /// </summary>
    void OnSourceChanged() { }

    /// <summary>The analyzer has gone live on a host. Paired strictly with <see cref="OnDetached"/>. Default no-op.</summary>
    void OnAttached() { }

    /// <summary>
    /// The analyzer is no longer live — removed, disabled, or its host torn down. Release native resources here.
    /// Never runs while <see cref="AnalyzeAsync"/> is in flight. Default no-op.
    /// </summary>
    void OnDetached() { }
}

/// <summary>
/// An <see cref="IAudioAnalyzer"/> that can also take a whole source <i>ahead of</i> playback. When a
/// <c>MediaElement</c> opens a seekable source it can decode (a local file, a bundled asset, a progressive download
/// the platform can read), it hands the decoded audio to <see cref="AnalyzeSourceAsync"/> as fast as it decodes —
/// typically tens of times real time — and does <b>not</b> stream live audio to the analyzer for that source.
/// </summary>
/// <remarks>
/// <para>
/// This is what makes captions line up: a live recognizer commits a sentence a second or two after it ends, so live
/// captions always trail the picture, while a transcript made up front carries the time each word was spoken and can
/// be shown exactly in step with the playhead.
/// </para>
/// <para>
/// When the host cannot decode the source (a live stream, protected content, a platform without a decoder) it never
/// calls this and streams live audio through <see cref="IAudioAnalyzer.AnalyzeAsync"/> instead, so an analyzer that
/// implements both works everywhere and is simply better where it can be.
/// </para>
/// </remarks>
public interface ISourceAudioAnalyzer : IAudioAnalyzer
{
    /// <summary>
    /// Whether to take whole sources at all. Default <c>true</c>; return <c>false</c> to get live audio even where the
    /// host could decode ahead (to caption only what is actually played, say).
    /// </summary>
    bool WantsSource => true;

    /// <summary>
    /// Analyze a whole source. <paramref name="audio"/> yields the source's audio in <see cref="IAudioAnalyzer.Format"/>,
    /// from the start, each frame positioned in media time. Cancelled when the source changes or the analyzer is
    /// detached.
    /// </summary>
    ValueTask AnalyzeSourceAsync(IAsyncEnumerable<AudioFrame> audio, CancellationToken ct);
}
