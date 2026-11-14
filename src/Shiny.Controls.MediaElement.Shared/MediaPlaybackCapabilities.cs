namespace Shiny.Controls.Media;

/// <summary>
/// What the current platform backend can actually do. Read it before offering an affordance in a UI —
/// the transport bar uses it to hide buttons that would be dead on this platform.
/// </summary>
[Flags]
public enum MediaPlaybackCapabilities
{
    /// <summary>Nothing beyond play/pause/seek.</summary>
    None = 0,

    /// <summary>
    /// Audio keeps playing when the app is backgrounded or the device is locked, with OS transport
    /// controls (lock screen, notification, or media keys) driven by <c>MediaElement.Metadata</c>.
    /// </summary>
    BackgroundAudio = 1 << 0,

    /// <summary>The video image can be detached into a floating always-on-top window.</summary>
    PictureInPicture = 1 << 1,

    /// <summary><c>PlaybackRate</c> is honoured (all backends except some legacy GTK builds).</summary>
    PlaybackRate = 1 << 2,

    /// <summary>
    /// <c>Volume</c> is honoured per-player. Where this is absent the volume slider is hidden and
    /// only <c>IsMuted</c> works (iOS Safari, for example, refuses programmatic volume).
    /// </summary>
    Volume = 1 << 3,

    /// <summary>The backend reports how much of the media it has buffered ahead of the playhead.</summary>
    BufferProgress = 1 << 4,

    /// <summary>
    /// The backend can hand the frames it plays to a frame analyzer (<c>MediaElement.Analyzer</c>) — OCR, barcodes,
    /// faces, motion over a video. Protected (DRM) content never yields frames, even where this is set.
    /// </summary>
    FrameAnalysis = 1 << 5,

    /// <summary>
    /// The backend can hand the audio it plays to an audio analyzer (<c>MediaElement.AudioAnalyzer</c>) — live
    /// captions, levels. Apple cannot tap HLS streams (they expose no audio track to tap), even where this is set.
    /// </summary>
    AudioAnalysis = 1 << 6,

    /// <summary>
    /// The backend can decode a whole local source's audio ahead of playback for an analyzer that implements
    /// <c>ISourceAudioAnalyzer</c> — captions that line up with the picture instead of trailing it.
    /// </summary>
    SourceAudioAnalysis = 1 << 7
}
