namespace Shiny.Maui.Controls.Media;

public partial class MediaElement
{
    // ── source & playback ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// What to play. Accepts a bare string in XAML (<c>Source="https://…/clip.mp4"</c>), which
    /// <see cref="MediaSource.Parse"/> classifies as a URI, a filesystem path, or a packaged resource.
    /// Assigning a new source replaces whatever is playing; assigning <c>null</c> clears the player.
    /// </summary>
    public static readonly BindableProperty SourceProperty = BindableProperty.Create(
        nameof(Source), typeof(MediaSource), typeof(MediaElement), null, propertyChanged: OnSourceChanged);

    /// <summary>Begin playing as soon as the source is opened. Default <c>false</c>.</summary>
    public static readonly BindableProperty AutoPlayProperty = BindableProperty.Create(
        nameof(AutoPlay), typeof(bool), typeof(MediaElement), false);

    /// <summary>Restart from the beginning on reaching the end. Default <c>false</c>. Suppresses <see cref="MediaEnded"/>.</summary>
    public static readonly BindableProperty IsLoopingProperty = BindableProperty.Create(
        nameof(IsLooping), typeof(bool), typeof(MediaElement), false,
        propertyChanged: (b, _, v) => ((MediaElement)b).backend?.SetLooping((bool)v));

    /// <summary>Output volume, 0..1. Default <c>1</c>. Independent of <see cref="IsMuted"/>.</summary>
    public static readonly BindableProperty VolumeProperty = BindableProperty.Create(
        nameof(Volume), typeof(double), typeof(MediaElement), 1d,
        coerceValue: (_, v) => Math.Clamp((double)v, 0d, 1d),
        propertyChanged: (b, _, v) => ((MediaElement)b).backend?.SetVolume((double)v));

    /// <summary>Silence output without disturbing <see cref="Volume"/>. Default <c>false</c>.</summary>
    public static readonly BindableProperty IsMutedProperty = BindableProperty.Create(
        nameof(IsMuted), typeof(bool), typeof(MediaElement), false,
        propertyChanged: OnIsMutedChanged);

    /// <summary>Playback speed; <c>1</c> is normal. Ignored without <see cref="MediaPlaybackCapabilities.PlaybackRate"/>.</summary>
    public static readonly BindableProperty PlaybackRateProperty = BindableProperty.Create(
        nameof(PlaybackRate), typeof(double), typeof(MediaElement), 1d,
        coerceValue: (_, v) => Math.Clamp((double)v, 0.25d, 4d),
        propertyChanged: (b, _, v) => ((MediaElement)b).backend?.SetRate((double)v));

    /// <summary>
    /// The playhead. Updated from the player every <see cref="PositionUpdateInterval"/> while playing;
    /// assigning it from your own code (or a binding) seeks. Two-way by default.
    /// </summary>
    public static readonly BindableProperty PositionProperty = BindableProperty.Create(
        nameof(Position), typeof(TimeSpan), typeof(MediaElement), TimeSpan.Zero, BindingMode.TwoWay,
        propertyChanged: OnPositionChanged);

    /// <summary>Total length of the current media, or <see cref="TimeSpan.Zero"/> until it opens (and for live streams).</summary>
    public static readonly BindableProperty DurationProperty = BindableProperty.Create(
        nameof(Duration), typeof(TimeSpan), typeof(MediaElement), TimeSpan.Zero, BindingMode.OneWayToSource);

    /// <summary>
    /// Pixel dimensions of the video track, or <see cref="Size.Zero"/> until the source opens and for
    /// audio-only media. Read-only; bind to it to size the player to whatever it is actually playing.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Do not read this once at <see cref="MediaElement.MediaOpened"/> and stop.</b> On Android it is
    /// routinely still <see cref="Size.Zero"/> at that point — ExoPlayer reports the size from a separate
    /// callback that lands after the player is ready — and an adaptive stream can change it mid-playback.
    /// Bind to it, or handle <see cref="MediaElement.VideoSizeChanged"/>.
    /// </remarks>
    public static readonly BindableProperty VideoSizeProperty = BindableProperty.Create(
        nameof(VideoSize), typeof(Size), typeof(MediaElement), Size.Zero, BindingMode.OneWayToSource);

    /// <summary>The player's lifecycle state. Read-only; bind to it to drive your own UI.</summary>
    public static readonly BindableProperty CurrentStateProperty = BindableProperty.Create(
        nameof(CurrentState), typeof(MediaElementState), typeof(MediaElement), MediaElementState.None,
        BindingMode.OneWayToSource);

    /// <summary>How much is buffered ahead of the playhead, 0..1. Drawn as the seek bar's secondary track.</summary>
    public static readonly BindableProperty BufferedProgressProperty = BindableProperty.Create(
        nameof(BufferedProgress), typeof(double), typeof(MediaElement), 0d, BindingMode.OneWayToSource);

    /// <summary>How the video image is scaled into the control. Default <see cref="MediaAspect.AspectFit"/>.</summary>
    public static readonly BindableProperty AspectProperty = BindableProperty.Create(
        nameof(Aspect), typeof(MediaAspect), typeof(MediaElement), MediaAspect.AspectFit,
        propertyChanged: (b, _, v) =>
        {
            var self = (MediaElement)b;
            self.backend?.SetAspect((MediaAspect)v);
            self.ApplyOverlayScaling();
        });

    /// <summary>Keep the display awake while playing. Default <c>false</c>.</summary>
    public static readonly BindableProperty KeepScreenOnProperty = BindableProperty.Create(
        nameof(KeepScreenOn), typeof(bool), typeof(MediaElement), false,
        propertyChanged: (b, _, v) => ((MediaElement)b).backend?.SetKeepScreenOn((bool)v));

    /// <summary>
    /// How often the playhead is read back from the player into <see cref="Position"/>. Default 250ms.
    /// Positions are polled rather than pushed so every backend ticks at the same rate.
    /// </summary>
    public static readonly BindableProperty PositionUpdateIntervalProperty = BindableProperty.Create(
        nameof(PositionUpdateInterval), typeof(TimeSpan), typeof(MediaElement), TimeSpan.FromMilliseconds(250),
        propertyChanged: (b, _, _) => ((MediaElement)b).ApplyTimerInterval());


    // ── background playback & picture-in-picture ─────────────────────────────────────────────────

    /// <summary>
    /// Keep audio playing when the app is backgrounded or the device locks, with OS transport controls
    /// (lock screen, media notification, SMTC) driven by <see cref="Metadata"/>. Default <c>false</c>.
    /// </summary>
    /// <remarks>
    /// Only honoured where <see cref="Capabilities"/> includes
    /// <see cref="MediaPlaybackCapabilities.BackgroundAudio"/>, and each platform needs one manifest opt-in
    /// from the <b>app</b> (a library can't add it): iOS/Catalyst need the <c>audio</c>
    /// <c>UIBackgroundModes</c> entry, and Android needs the <c>POST_NOTIFICATIONS</c> runtime permission for
    /// the media notification to appear. Video stops rendering while backgrounded unless the user is in
    /// Picture-in-Picture — see <see cref="TryEnterPictureInPictureAsync"/>.
    /// </remarks>
    public static readonly BindableProperty EnableBackgroundPlaybackProperty = BindableProperty.Create(
        nameof(EnableBackgroundPlayback), typeof(bool), typeof(MediaElement), false,
        propertyChanged: (b, _, _) => ((MediaElement)b).ApplyBackgroundPlayback());

    /// <summary>
    /// Now-playing information for the OS transport UI while <see cref="EnableBackgroundPlayback"/> is on.
    /// Assign a new instance to update it — the control doesn't watch the object's own properties.
    /// </summary>
    public static readonly BindableProperty MetadataProperty = BindableProperty.Create(
        nameof(Metadata), typeof(MediaMetadata), typeof(MediaElement), null,
        propertyChanged: (b, _, _) => ((MediaElement)b).ApplyBackgroundPlayback());

    /// <summary>What this platform's backend supports. Read it to hide affordances that would be dead here.</summary>
    public static readonly BindableProperty CapabilitiesProperty = BindableProperty.Create(
        nameof(Capabilities), typeof(MediaPlaybackCapabilities), typeof(MediaElement),
        MediaPlaybackCapabilities.None, BindingMode.OneWayToSource);

    /// <summary>Whether the video is currently in a floating Picture-in-Picture window.</summary>
    public static readonly BindableProperty IsPictureInPictureActiveProperty = BindableProperty.Create(
        nameof(IsPictureInPictureActive), typeof(bool), typeof(MediaElement), false, BindingMode.OneWayToSource);


    // ── fullscreen ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Whether playback is showing fullscreen. Settable from anywhere — a binding, the transport bar's
    /// button, or code.
    /// </summary>
    /// <remarks>
    /// Fullscreen pushes a modal page carrying a second surface bound to the <i>same</i> player, so the
    /// stream keeps running rather than re-buffering, and the inline control keeps its place in your layout.
    /// </remarks>
    public static readonly BindableProperty IsFullScreenProperty = BindableProperty.Create(
        nameof(IsFullScreen), typeof(bool), typeof(MediaElement), false, BindingMode.TwoWay,
        propertyChanged: OnIsFullScreenChanged);


    // ── transport bar visibility ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Master switch for the built-in transport bar. Turn it off to drive playback entirely from your own
    /// UI via the commands. Default <c>true</c>.
    /// </summary>
    public static readonly BindableProperty ShowTransportBarProperty = BindableProperty.Create(
        nameof(ShowTransportBar), typeof(bool), typeof(MediaElement), true,
        propertyChanged: (b, _, _) => ((MediaElement)b).ApplyTransportVisibility());

    /// <summary>Show the play/pause button. Default <c>true</c>.</summary>
    public static readonly BindableProperty ShowPlayPauseButtonProperty = BindableProperty.Create(
        nameof(ShowPlayPauseButton), typeof(bool), typeof(MediaElement), true,
        propertyChanged: (b, _, _) => ((MediaElement)b).ApplyTransportVisibility());

    /// <summary>Show the seek scrubber. Default <c>true</c>.</summary>
    public static readonly BindableProperty ShowSeekBarProperty = BindableProperty.Create(
        nameof(ShowSeekBar), typeof(bool), typeof(MediaElement), true,
        propertyChanged: (b, _, _) => ((MediaElement)b).ApplyTransportVisibility());

    /// <summary>
    /// Show the mute button and volume slider. Default <c>true</c>. Hidden anyway on backends without
    /// <see cref="MediaPlaybackCapabilities.Volume"/>, where only the mute button is offered.
    /// </summary>
    public static readonly BindableProperty ShowVolumeControlProperty = BindableProperty.Create(
        nameof(ShowVolumeControl), typeof(bool), typeof(MediaElement), true,
        propertyChanged: (b, _, _) => ((MediaElement)b).ApplyTransportVisibility());

    /// <summary>Show the fullscreen toggle. Default <c>true</c>.</summary>
    public static readonly BindableProperty ShowFullScreenButtonProperty = BindableProperty.Create(
        nameof(ShowFullScreenButton), typeof(bool), typeof(MediaElement), true,
        propertyChanged: (b, _, _) => ((MediaElement)b).ApplyTransportVisibility());

    /// <summary>Show the elapsed/total time labels either side of the scrubber. Default <c>true</c>.</summary>
    public static readonly BindableProperty ShowTimeLabelsProperty = BindableProperty.Create(
        nameof(ShowTimeLabels), typeof(bool), typeof(MediaElement), true,
        propertyChanged: (b, _, _) => ((MediaElement)b).ApplyTransportVisibility());

    /// <summary>
    /// Show the Picture-in-Picture button. Default <c>false</c> — it's opt-in because PiP needs an app-level
    /// manifest entry. Hidden automatically where the platform can't do PiP.
    /// </summary>
    public static readonly BindableProperty ShowPictureInPictureButtonProperty = BindableProperty.Create(
        nameof(ShowPictureInPictureButton), typeof(bool), typeof(MediaElement), false,
        propertyChanged: (b, _, _) => ((MediaElement)b).ApplyTransportVisibility());

    /// <summary>
    /// Fade the transport bar out after <see cref="TransportBarAutoHideDelay"/> of no interaction while
    /// playing, and bring it back on a tap. Default <c>true</c>; set <c>false</c> to pin it open (what you
    /// want for audio-only media).
    /// </summary>
    public static readonly BindableProperty AutoHideTransportBarProperty = BindableProperty.Create(
        nameof(AutoHideTransportBar), typeof(bool), typeof(MediaElement), true,
        propertyChanged: (b, _, _) => ((MediaElement)b).RestartAutoHide());

    /// <summary>How long the transport bar lingers before auto-hiding. Default 3 seconds.</summary>
    public static readonly BindableProperty TransportBarAutoHideDelayProperty = BindableProperty.Create(
        nameof(TransportBarAutoHideDelay), typeof(TimeSpan), typeof(MediaElement), TimeSpan.FromSeconds(3));


    // ── transport bar styling ────────────────────────────────────────────────────────────────────

    /// <summary>Background of the transport bar. Defaults to a translucent scrim over the video.</summary>
    public static readonly BindableProperty TransportBarBackgroundColorProperty = BindableProperty.Create(
        nameof(TransportBarBackgroundColor), typeof(Color), typeof(MediaElement), null,
        propertyChanged: (b, _, _) => ((MediaElement)b).ApplyTransportStyling());

    /// <summary>Tint of the transport bar's glyphs and labels. Defaults to white (it sits over video).</summary>
    public static readonly BindableProperty ControlColorProperty = BindableProperty.Create(
        nameof(ControlColor), typeof(Color), typeof(MediaElement), null,
        propertyChanged: (b, _, _) => ((MediaElement)b).ApplyTransportStyling());

    /// <summary>Played-portion colour of the seek bar. Defaults to the theme's primary colour.</summary>
    public static readonly BindableProperty SeekBarColorProperty = BindableProperty.Create(
        nameof(SeekBarColor), typeof(Color), typeof(MediaElement), null,
        propertyChanged: (b, _, _) => ((MediaElement)b).ApplyTransportStyling());

    /// <summary>Colour behind the video before the first frame arrives, and in the letterbox bars. Default black.</summary>
    public static readonly BindableProperty VideoBackgroundColorProperty = BindableProperty.Create(
        nameof(VideoBackgroundColor), typeof(Color), typeof(MediaElement), Colors.Black,
        propertyChanged: (b, _, v) => ((MediaElement)b).rootGrid.BackgroundColor = (Color)v);


    // ── CLR accessors ────────────────────────────────────────────────────────────────────────────

    /// <inheritdoc cref="SourceProperty"/>
    public MediaSource? Source
    {
        get => (MediaSource?)this.GetValue(SourceProperty);
        set => this.SetValue(SourceProperty, value);
    }

    /// <inheritdoc cref="AutoPlayProperty"/>
    public bool AutoPlay
    {
        get => (bool)this.GetValue(AutoPlayProperty);
        set => this.SetValue(AutoPlayProperty, value);
    }

    /// <inheritdoc cref="IsLoopingProperty"/>
    public bool IsLooping
    {
        get => (bool)this.GetValue(IsLoopingProperty);
        set => this.SetValue(IsLoopingProperty, value);
    }

    /// <inheritdoc cref="VolumeProperty"/>
    public double Volume
    {
        get => (double)this.GetValue(VolumeProperty);
        set => this.SetValue(VolumeProperty, value);
    }

    /// <inheritdoc cref="IsMutedProperty"/>
    public bool IsMuted
    {
        get => (bool)this.GetValue(IsMutedProperty);
        set => this.SetValue(IsMutedProperty, value);
    }

    /// <inheritdoc cref="PlaybackRateProperty"/>
    public double PlaybackRate
    {
        get => (double)this.GetValue(PlaybackRateProperty);
        set => this.SetValue(PlaybackRateProperty, value);
    }

    /// <inheritdoc cref="PositionProperty"/>
    public TimeSpan Position
    {
        get => (TimeSpan)this.GetValue(PositionProperty);
        set => this.SetValue(PositionProperty, value);
    }

    /// <inheritdoc cref="DurationProperty"/>
    public TimeSpan Duration
    {
        get => (TimeSpan)this.GetValue(DurationProperty);
        private set => this.SetValue(DurationProperty, value);
    }

    /// <inheritdoc cref="VideoSizeProperty"/>
    public Size VideoSize
    {
        get => (Size)this.GetValue(VideoSizeProperty);
        private set => this.SetValue(VideoSizeProperty, value);
    }

    /// <inheritdoc cref="CurrentStateProperty"/>
    public MediaElementState CurrentState
    {
        get => (MediaElementState)this.GetValue(CurrentStateProperty);
        private set => this.SetValue(CurrentStateProperty, value);
    }

    /// <inheritdoc cref="BufferedProgressProperty"/>
    public double BufferedProgress
    {
        get => (double)this.GetValue(BufferedProgressProperty);
        private set => this.SetValue(BufferedProgressProperty, value);
    }

    /// <inheritdoc cref="AspectProperty"/>
    public MediaAspect Aspect
    {
        get => (MediaAspect)this.GetValue(AspectProperty);
        set => this.SetValue(AspectProperty, value);
    }

    /// <inheritdoc cref="KeepScreenOnProperty"/>
    public bool KeepScreenOn
    {
        get => (bool)this.GetValue(KeepScreenOnProperty);
        set => this.SetValue(KeepScreenOnProperty, value);
    }

    /// <inheritdoc cref="PositionUpdateIntervalProperty"/>
    public TimeSpan PositionUpdateInterval
    {
        get => (TimeSpan)this.GetValue(PositionUpdateIntervalProperty);
        set => this.SetValue(PositionUpdateIntervalProperty, value);
    }

    /// <inheritdoc cref="EnableBackgroundPlaybackProperty"/>
    public bool EnableBackgroundPlayback
    {
        get => (bool)this.GetValue(EnableBackgroundPlaybackProperty);
        set => this.SetValue(EnableBackgroundPlaybackProperty, value);
    }

    /// <inheritdoc cref="MetadataProperty"/>
    public MediaMetadata? Metadata
    {
        get => (MediaMetadata?)this.GetValue(MetadataProperty);
        set => this.SetValue(MetadataProperty, value);
    }

    /// <inheritdoc cref="CapabilitiesProperty"/>
    public MediaPlaybackCapabilities Capabilities
    {
        get => (MediaPlaybackCapabilities)this.GetValue(CapabilitiesProperty);
        private set => this.SetValue(CapabilitiesProperty, value);
    }

    /// <inheritdoc cref="IsPictureInPictureActiveProperty"/>
    public bool IsPictureInPictureActive
    {
        get => (bool)this.GetValue(IsPictureInPictureActiveProperty);
        private set => this.SetValue(IsPictureInPictureActiveProperty, value);
    }

    /// <inheritdoc cref="IsFullScreenProperty"/>
    public bool IsFullScreen
    {
        get => (bool)this.GetValue(IsFullScreenProperty);
        set => this.SetValue(IsFullScreenProperty, value);
    }

    /// <inheritdoc cref="ShowTransportBarProperty"/>
    public bool ShowTransportBar
    {
        get => (bool)this.GetValue(ShowTransportBarProperty);
        set => this.SetValue(ShowTransportBarProperty, value);
    }

    /// <inheritdoc cref="ShowPlayPauseButtonProperty"/>
    public bool ShowPlayPauseButton
    {
        get => (bool)this.GetValue(ShowPlayPauseButtonProperty);
        set => this.SetValue(ShowPlayPauseButtonProperty, value);
    }

    /// <inheritdoc cref="ShowSeekBarProperty"/>
    public bool ShowSeekBar
    {
        get => (bool)this.GetValue(ShowSeekBarProperty);
        set => this.SetValue(ShowSeekBarProperty, value);
    }

    /// <inheritdoc cref="ShowVolumeControlProperty"/>
    public bool ShowVolumeControl
    {
        get => (bool)this.GetValue(ShowVolumeControlProperty);
        set => this.SetValue(ShowVolumeControlProperty, value);
    }

    /// <inheritdoc cref="ShowFullScreenButtonProperty"/>
    public bool ShowFullScreenButton
    {
        get => (bool)this.GetValue(ShowFullScreenButtonProperty);
        set => this.SetValue(ShowFullScreenButtonProperty, value);
    }

    /// <inheritdoc cref="ShowTimeLabelsProperty"/>
    public bool ShowTimeLabels
    {
        get => (bool)this.GetValue(ShowTimeLabelsProperty);
        set => this.SetValue(ShowTimeLabelsProperty, value);
    }

    /// <inheritdoc cref="ShowPictureInPictureButtonProperty"/>
    public bool ShowPictureInPictureButton
    {
        get => (bool)this.GetValue(ShowPictureInPictureButtonProperty);
        set => this.SetValue(ShowPictureInPictureButtonProperty, value);
    }

    /// <inheritdoc cref="AutoHideTransportBarProperty"/>
    public bool AutoHideTransportBar
    {
        get => (bool)this.GetValue(AutoHideTransportBarProperty);
        set => this.SetValue(AutoHideTransportBarProperty, value);
    }

    /// <inheritdoc cref="TransportBarAutoHideDelayProperty"/>
    public TimeSpan TransportBarAutoHideDelay
    {
        get => (TimeSpan)this.GetValue(TransportBarAutoHideDelayProperty);
        set => this.SetValue(TransportBarAutoHideDelayProperty, value);
    }

    /// <inheritdoc cref="TransportBarBackgroundColorProperty"/>
    public Color? TransportBarBackgroundColor
    {
        get => (Color?)this.GetValue(TransportBarBackgroundColorProperty);
        set => this.SetValue(TransportBarBackgroundColorProperty, value);
    }

    /// <inheritdoc cref="ControlColorProperty"/>
    public Color? ControlColor
    {
        get => (Color?)this.GetValue(ControlColorProperty);
        set => this.SetValue(ControlColorProperty, value);
    }

    /// <inheritdoc cref="SeekBarColorProperty"/>
    public Color? SeekBarColor
    {
        get => (Color?)this.GetValue(SeekBarColorProperty);
        set => this.SetValue(SeekBarColorProperty, value);
    }

    /// <inheritdoc cref="VideoBackgroundColorProperty"/>
    public Color VideoBackgroundColor
    {
        get => (Color)this.GetValue(VideoBackgroundColorProperty);
        set => this.SetValue(VideoBackgroundColorProperty, value);
    }


    // ── property-changed plumbing ────────────────────────────────────────────────────────────────

    static void OnSourceChanged(BindableObject bindable, object oldValue, object newValue)
        => ((MediaElement)bindable).LoadSource((MediaSource?)newValue);

    static void OnIsMutedChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var element = (MediaElement)bindable;
        element.backend?.SetMuted((bool)newValue);
        element.UpdateMuteGlyph();
    }

    static void OnPositionChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var element = (MediaElement)bindable;

        // The 250ms tick writes the player's own position back into this property. Seeking to it would
        // fight the player (and on Android restart the buffer), so only an outside write means "seek".
        if (element.isPushingPosition)
            return;

        element.backend?.Seek((TimeSpan)newValue);
        element.SeekCompleted?.Invoke(element, (TimeSpan)newValue);
    }

    static void OnIsFullScreenChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var element = (MediaElement)bindable;
        element.UpdateFullScreenGlyph();
        element.ApplyFullScreen((bool)newValue);
    }
}
