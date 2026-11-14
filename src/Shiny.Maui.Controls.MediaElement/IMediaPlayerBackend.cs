namespace Shiny.Maui.Controls.Media;

/// <summary>
/// The platform player behind a <see cref="MediaElement"/> — AVPlayer (iOS/macOS/Catalyst), ExoPlayer
/// (Android), Windows.Media.Playback.MediaPlayer (Windows), or GtkMediaFile (Linux, from the companion
/// <c>Shiny.Maui.Controls.MediaElement.Linux</c> package).
/// </summary>
/// <remarks>
/// <para>
/// A backend owns the <b>player</b>, never the view. The video output surface is created by the handler and
/// pushed in through <see cref="SetOutput"/>, which is what makes the two hard requirements work:
/// entering fullscreen moves playback to a second surface on a modal page without re-buffering, and
/// backgrounding detaches the surface entirely while audio keeps running.
/// </para>
/// <para>
/// Implement this (plus register a factory on <see cref="MediaPlayerBackends"/>) to plug in your own
/// player. Everything except <see cref="OpenAsync"/> is called on the UI thread.
/// </para>
/// </remarks>
public interface IMediaPlayerBackend : IDisposable
{
    /// <summary>What this backend can do beyond play/pause/seek. Drives which transport buttons are offered.</summary>
    MediaPlaybackCapabilities Capabilities { get; }

    /// <summary>The current lifecycle state.</summary>
    MediaElementState State { get; }

    /// <summary>The playhead position. Polled by <see cref="MediaElement"/> while playing.</summary>
    TimeSpan Position { get; }

    /// <summary>Total length, or <see cref="TimeSpan.Zero"/> until the source has been opened (or when live).</summary>
    TimeSpan Duration { get; }

    /// <summary>How much is buffered ahead, as a 0..1 fraction of <see cref="Duration"/>. Always 0 without <see cref="MediaPlaybackCapabilities.BufferProgress"/>.</summary>
    double BufferedProgress { get; }

    /// <summary>Pixel dimensions of the video track, or <see cref="Size.Zero"/> for audio-only media.</summary>
    Size VideoSize { get; }

    /// <summary>Whether the video is currently detached into a Picture-in-Picture window.</summary>
    bool IsPictureInPictureActive { get; }

    /// <summary>Raised on the UI thread whenever <see cref="State"/> changes.</summary>
    event EventHandler<MediaElementState>? StateChanged;

    /// <summary>Raised on the UI thread once the source is loaded and <see cref="Duration"/>/<see cref="VideoSize"/> are known.</summary>
    event EventHandler? MediaOpened;

    /// <summary>Raised on the UI thread when <see cref="VideoSize"/> becomes known, or changes.</summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Separate from <see cref="MediaOpened"/>, and it has to be.</b> ExoPlayer reports the video size
    /// from its own <c>onVideoSizeChanged</c> callback, which routinely lands <i>after</i> the player reaches
    /// ready — so a control that read the size once at open would report <see cref="Size.Zero"/> on Android
    /// for real video. AVPlayer has the size by <c>ReadyToPlay</c>, but also re-reports
    /// <c>presentationSize</c> when an adaptive stream switches rendition, which the same read-once control
    /// would then have stale.
    /// </para>
    /// <para>
    /// A no-op default implementation is supplied so a backend written against an earlier version still
    /// compiles. Such a backend never reports a change, and <see cref="MediaElement.VideoSize"/> is then
    /// whatever was readable when <see cref="MediaOpened"/> fired — which is the behaviour that existed
    /// before this event, not a regression.
    /// </para>
    /// </remarks>
    event EventHandler? VideoSizeChanged
    {
        add { }
        remove { }
    }

    /// <summary>Raised on the UI thread when playback reaches the end (not raised while looping).</summary>
    event EventHandler? MediaEnded;

    /// <summary>Raised on the UI thread when the source cannot be opened or playback aborts.</summary>
    event EventHandler<MediaFailure>? Failed;

    /// <summary>Raised on the UI thread when <see cref="IsPictureInPictureActive"/> changes.</summary>
    event EventHandler<bool>? PictureInPictureChanged;

    /// <summary>
    /// Raised on the UI thread when the OS transport UI (lock screen, media notification, SMTC flyout,
    /// headset button) asks for something, so the control can keep its own state in step.
    /// </summary>
    event EventHandler<MediaRemoteCommand>? RemoteCommandReceived;

    /// <summary>
    /// Bind the player's video output to a native view, or pass <c>null</c> to unbind. Called by the
    /// handler on connect/disconnect and when entering/leaving fullscreen. Audio is unaffected.
    /// </summary>
    void SetOutput(object? nativeView);

    /// <summary>Load <paramref name="source"/>, replacing whatever was playing. Pass <c>null</c> to clear.</summary>
    Task OpenAsync(MediaSource? source, CancellationToken ct = default);

    /// <summary>Start or resume playback.</summary>
    void Play();

    /// <summary>Suspend playback in place.</summary>
    void Pause();

    /// <summary>Halt playback and rewind to the start.</summary>
    void Stop();

    /// <summary>Move the playhead. Clamped by the backend to the valid range.</summary>
    void Seek(TimeSpan position);

    /// <summary>Set output volume, 0..1.</summary>
    void SetVolume(double volume);

    /// <summary>Mute or unmute without disturbing <see cref="SetVolume"/>.</summary>
    void SetMuted(bool muted);

    /// <summary>Set the playback rate; 1.0 is normal speed.</summary>
    void SetRate(double rate);

    /// <summary>Whether playback restarts from the beginning on reaching the end.</summary>
    void SetLooping(bool looping);

    /// <summary>How the video image is scaled into the output view.</summary>
    void SetAspect(MediaAspect aspect);

    /// <summary>Keep the display awake while playing.</summary>
    void SetKeepScreenOn(bool keepOn);

    /// <summary>
    /// Turn background audio on/off and publish (or clear) the now-playing metadata shown in the OS
    /// transport UI. A no-op without <see cref="MediaPlaybackCapabilities.BackgroundAudio"/>.
    /// </summary>
    void SetBackgroundPlayback(bool enabled, MediaMetadata? metadata);

    /// <summary>
    /// Ask the OS to detach the video into a floating window. Returns <c>false</c> when the platform,
    /// the OS version, or the app's manifest doesn't allow it — never throws for an unsupported platform.
    /// </summary>
    Task<bool> TryEnterPictureInPictureAsync();

    /// <summary>Return from Picture-in-Picture to inline playback. A no-op when not in PiP.</summary>
    Task ExitPictureInPictureAsync();

    /// <summary>
    /// Hand the backend the sink for frame and audio analysis, or <c>null</c> to stop. Called again whenever the
    /// analyzers change; read <see cref="IMediaAnalysisSink.FramesEnabled"/> and <see cref="IMediaAnalysisSink.AudioEnabled"/>
    /// and attach or remove taps to match. Advertise what works with <see cref="MediaPlaybackCapabilities.FrameAnalysis"/>
    /// and <see cref="MediaPlaybackCapabilities.AudioAnalysis"/>.
    /// </summary>
    /// <remarks>
    /// A no-op default is supplied so a backend written against an earlier version still compiles; such a backend
    /// never feeds an analyzer, which is what it did before analysis existed.
    /// </remarks>
    void SetAnalysisSink(IMediaAnalysisSink? sink) { }

    /// <summary>
    /// Decode <paramref name="source"/>'s audio ahead of playback, converted to <paramref name="format"/> and positioned
    /// in media time, or return <c>null</c> when this backend cannot (a stream, protected content, no decoder). Used to
    /// give an <see cref="ISourceAudioAnalyzer"/> the whole source at once. Advertise it with
    /// <see cref="MediaPlaybackCapabilities.SourceAudioAnalysis"/>. Default <c>null</c>.
    /// </summary>
    IAsyncEnumerable<AudioFrame>? DecodeAudio(MediaSource source, AudioAnalysisFormat format, CancellationToken ct) => null;
}


/// <summary>A playback failure reported by a backend.</summary>
/// <param name="Message">Human-readable description, suitable for the control's error overlay.</param>
/// <param name="Exception">The underlying exception, when the backend surfaced one.</param>
public record MediaFailure(string Message, Exception? Exception = null);


/// <summary>A transport request that arrived from the OS rather than from the app's own UI.</summary>
public enum MediaRemoteCommand
{
    /// <summary>Resume playback.</summary>
    Play,

    /// <summary>Suspend playback.</summary>
    Pause,

    /// <summary>Halt playback.</summary>
    Stop,

    /// <summary>Flip between play and pause (a headset button click).</summary>
    TogglePlayPause,

    /// <summary>The user scrubbed on the OS transport UI; the backend has already applied the seek.</summary>
    Seek
}
