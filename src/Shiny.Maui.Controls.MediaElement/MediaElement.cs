namespace Shiny.Maui.Controls.Media;

/// <summary>
/// Plays local and remote audio/video on iOS, Android, Windows, macOS AppKit and (via the companion
/// <c>Shiny.Maui.Controls.MediaElement.Linux</c> package) GTK4, behind one API.
/// </summary>
/// <remarks>
/// <para>
/// The transport bar is drawn by Shiny rather than handed to the platform, so every piece of it —
/// <see cref="ShowPlayPauseButton"/>, <see cref="ShowSeekBar"/>, <see cref="ShowVolumeControl"/>,
/// <see cref="ShowFullScreenButton"/> — toggles independently and looks the same everywhere. Native
/// transport UIs can't do that: only Windows exposes per-element visibility, and the rest are
/// all-or-nothing.
/// </para>
/// <para>
/// The player lives in an <see cref="IMediaPlayerBackend"/> that outlives any one view. That's what lets
/// fullscreen swap in a second surface without re-buffering, and lets audio keep running with the video
/// surface detached while the app is backgrounded.
/// </para>
/// </remarks>
/// <example>
/// <code language="xaml">
/// &lt;media:MediaElement Source="https://example.com/clip.mp4"
///                     AutoPlay="True"
///                     ShowVolumeControl="False"
///                     EnableBackgroundPlayback="True" /&gt;
/// </code>
/// </example>
public partial class MediaElement : ContentView
{
    readonly Grid rootGrid;
    readonly MediaSurface surface;

    // A fullscreen mirror borrows its owner's player instead of creating one, and must never dispose it.
    readonly MediaElement? mirrorOwner;
    IMediaPlayerBackend? backend;

    IDispatcherTimer? positionTimer;
    IDispatcherTimer? autoHideTimer;

    // Guards the Position property against treating the 250ms player read-back as a seek request.
    bool isPushingPosition;
    bool isScrubbing;
    bool isFullScreenApplied;
    bool isFullScreenTransitioning;
    ContentPage? fullScreenPage;
    CancellationTokenSource? openCts;

    // Play() called before the element had a player (no handler yet); honored once one is created.
    bool playRequested;

    /// <summary>Creates a media element with the platform backend registered by <c>UseShinyMediaElement()</c>.</summary>
    public MediaElement() : this(null)
    {
    }

    // The fullscreen page builds one of these to share the owner's player.
    internal MediaElement(MediaElement? owner)
    {
        this.mirrorOwner = owner;

        // An owner's player is created when its handler connects, never here — see EnsureBackend. A
        // fullscreen mirror only borrows a player that is already running, which holds no static root of its own.
        this.backend = owner?.backend;

        this.surface = new MediaSurface { Backend = this.backend };

        this.rootGrid = new Grid
        {
            BackgroundColor = Colors.Black,
            Children = { this.surface }
        };

        this.BuildTransportBar();
        this.Content = this.rootGrid;

        if (this.backend is not null)
        {
            this.Capabilities = this.backend.Capabilities;
            this.SubscribeBackend(this.backend);
        }

        this.InitializeCommands();
        this.ApplyTransportStyling();
        this.ApplyTransportVisibility();

        if (owner is not null)
        {
            this.AdoptFrom(owner);
            this.SyncFromBackend();
        }
    }


    /// <summary>
    /// Seed every visual from the player that is already running.
    /// </summary>
    /// <remarks>
    /// A fullscreen mirror is born mid-playback, so starting from property defaults means the fullscreen
    /// page opens reading <c>0:00 / 0:00</c> with an empty scrubber over a video that is 30 seconds in —
    /// and stays that way until the next position tick, which never comes if playback is paused.
    /// </remarks>
    void SyncFromBackend()
    {
        if (this.backend is null)
            return;

        this.CurrentState = this.backend.State;
        this.Duration = this.backend.Duration;
        this.Capabilities = this.backend.Capabilities;
        this.SetVideoSizeFromBackend();

        this.UpdatePlayPauseGlyph();
        this.UpdateBusyOverlay();
        this.ApplyTransportVisibility();
        this.PushPositionFromPlayer();

        if (this.CurrentState == MediaElementState.Playing)
            this.StartPositionTimer();
    }


    // ── backend lifecycle ────────────────────────────────────────────────────────────────────────

    void SubscribeBackend(IMediaPlayerBackend b)
    {
        b.StateChanged += this.OnBackendStateChanged;
        b.MediaOpened += this.OnBackendOpened;
        b.VideoSizeChanged += this.OnBackendVideoSizeChanged;
        b.MediaEnded += this.OnBackendEnded;
        b.Failed += this.OnBackendFailed;
        b.PictureInPictureChanged += this.OnBackendPipChanged;
        b.RemoteCommandReceived += this.OnBackendRemoteCommand;
    }

    void UnsubscribeBackend(IMediaPlayerBackend b)
    {
        b.StateChanged -= this.OnBackendStateChanged;
        b.MediaOpened -= this.OnBackendOpened;
        b.VideoSizeChanged -= this.OnBackendVideoSizeChanged;
        b.MediaEnded -= this.OnBackendEnded;
        b.Failed -= this.OnBackendFailed;
        b.PictureInPictureChanged -= this.OnBackendPipChanged;
        b.RemoteCommandReceived -= this.OnBackendRemoteCommand;
    }

    /// <summary>
    /// Create the platform player the first time the element gets a handler (and again after a disconnect).
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Not in the constructor.</b> A backend registers itself with process-wide sources the moment it
    /// exists — Android's static Picture-in-Picture event, <c>NSNotificationCenter.DefaultCenter</c> observers on
    /// Apple, ExoPlayer's own playback thread — and every one of those roots the backend, whose events root this
    /// element. The only teardown is a handler disconnect, so an element that never got a handler (built and
    /// discarded, a template instance that was never realized) kept a live player forever, and — with a
    /// <see cref="Source"/> set — was even buffering it.
    /// </para>
    /// <para>
    /// Runs from <see cref="OnHandlerChanging"/> so the player is on <see cref="MediaSurface.Backend"/> before
    /// the surface's own handler connects. Everything configured beforehand is pushed on connect:
    /// <see cref="ApplyBackendSettings"/>, the pending <see cref="Source"/>, and a <see cref="Play"/> call.
    /// </para>
    /// </remarks>
    void EnsureBackend()
    {
        if (this.backend is not null || this.mirrorOwner is not null)
            return;

        var created = MediaPlayerBackends.Create();
        if (created is null)
            return;

        this.backend = created;
        this.Capabilities = created.Capabilities;
        this.SubscribeBackend(created);
        this.surface.Backend = created;
        this.ApplyTransportVisibility();
    }

    protected override void OnHandlerChanging(HandlerChangingEventArgs args)
    {
        base.OnHandlerChanging(args);

        if (args.NewHandler is not null)
        {
            this.EnsureBackend();
            return;
        }

        // The control is leaving the visual tree for good (page popped, template swapped). A fullscreen
        // mirror only borrows the player, so it detaches; the owner tears the whole thing down. Background
        // playback deliberately does NOT keep it alive here — backgrounding the app doesn't disconnect
        // handlers, so reaching this point means the page itself is gone.
        this.StopTimers();

        if (this.backend is null)
            return;

        this.UnsubscribeBackend(this.backend);

        if (this.mirrorOwner is null)
            this.backend.Dispose();

        this.backend = null;
        this.surface.Backend = null;
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        if (this.Handler is null || this.backend is null)
            return;

        this.ApplyBackendSettings();
        this.ApplyTimerInterval();

        // A mirror inherits a player that's already loaded; only the owner opens sources.
        if (this.mirrorOwner is null && this.Source is not null && this.backend.State == MediaElementState.None)
            this.LoadSource(this.Source);

        if (this.playRequested)
        {
            this.playRequested = false;
            this.backend.Play();
        }

        this.RestartAutoHide();
    }

    void ApplyBackendSettings()
    {
        if (this.backend is null)
            return;

        this.backend.SetVolume(this.Volume);
        this.backend.SetMuted(this.IsMuted);
        this.backend.SetRate(this.PlaybackRate);
        this.backend.SetLooping(this.IsLooping);
        this.backend.SetAspect(this.Aspect);
        this.backend.SetKeepScreenOn(this.KeepScreenOn);
        this.ApplyBackgroundPlayback();
    }

    void ApplyBackgroundPlayback()
        => this.backend?.SetBackgroundPlayback(this.EnableBackgroundPlayback, this.Metadata);

    // Copy the owner's configuration onto the fullscreen mirror so it looks and behaves the same.
    void AdoptFrom(MediaElement owner)
    {
        this.ShowTransportBar = owner.ShowTransportBar;
        this.ShowPlayPauseButton = owner.ShowPlayPauseButton;
        this.ShowSeekBar = owner.ShowSeekBar;
        this.ShowVolumeControl = owner.ShowVolumeControl;
        this.ShowFullScreenButton = owner.ShowFullScreenButton;
        this.ShowTimeLabels = owner.ShowTimeLabels;
        this.ShowPictureInPictureButton = owner.ShowPictureInPictureButton;
        this.AutoHideTransportBar = owner.AutoHideTransportBar;
        this.TransportBarAutoHideDelay = owner.TransportBarAutoHideDelay;
        this.TransportBarBackgroundColor = owner.TransportBarBackgroundColor;
        this.ControlColor = owner.ControlColor;
        this.SeekBarColor = owner.SeekBarColor;
        this.VideoBackgroundColor = owner.VideoBackgroundColor;
        this.Volume = owner.Volume;
        this.IsMuted = owner.IsMuted;
        this.Duration = owner.Duration;
        this.CurrentState = owner.CurrentState;
        this.VideoSize = owner.VideoSize;
        this.Aspect = owner.Aspect;

        // Shows the "collapse" glyph and routes its tap back to the owner, which owns the modal page.
        this.SetValue(IsFullScreenProperty, true);
        this.isFullScreenApplied = true;
    }


    // ── source loading ───────────────────────────────────────────────────────────────────────────

    async void LoadSource(MediaSource? source)
    {
        if (this.backend is null)
            return;

        this.openCts?.Cancel();
        this.openCts?.Dispose();
        this.openCts = new CancellationTokenSource();
        var ct = this.openCts.Token;

        this.HideError();
        this.Duration = TimeSpan.Zero;

        // Cleared with the duration rather than left standing: a page sizing itself to the last video would
        // otherwise hold that shape over the black frame of a source that turns out to be a different one
        // (or audio-only), until the new size arrives.
        this.VideoSize = Size.Zero;
        this.SetPositionFromPlayer(TimeSpan.Zero);

        try
        {
            await this.backend.OpenAsync(source, ct).ConfigureAwait(true);

            if (ct.IsCancellationRequested)
                return;

            this.ApplyBackendSettings();

            if (source is not null && this.AutoPlay)
                this.backend.Play();
        }
        catch (OperationCanceledException)
        {
            // superseded by a newer Source assignment
        }
        catch (Exception ex)
        {
            this.RaiseFailure(new MediaFailure($"Could not open media source '{source}'", ex));
        }
    }


    // ── backend events ───────────────────────────────────────────────────────────────────────────

    void OnBackendStateChanged(object? sender, MediaElementState state)
    {
        this.CurrentState = state;
        this.UpdatePlayPauseGlyph();
        this.UpdateBusyOverlay();

        if (state == MediaElementState.Playing)
            this.StartPositionTimer();
        else
            this.StopPositionTimer();

        this.RestartAutoHide();
        this.StateChanged?.Invoke(this, state);
    }

    void OnBackendOpened(object? sender, EventArgs e)
    {
        if (this.backend is null)
            return;

        this.Duration = this.backend.Duration;
        this.Capabilities = this.backend.Capabilities;

        // Seeded here as well as from VideoSizeChanged, because the two platforms disagree about which
        // comes first: Windows/GTK/AVPlayer have the size by the time they say the media is open, ExoPlayer
        // reports it from a callback that lands afterwards. Whichever arrives second is the no-op.
        this.SetVideoSizeFromBackend();

        this.ApplyTransportVisibility();
        this.UpdateTimeLabels();
        this.MediaOpened?.Invoke(this, EventArgs.Empty);
    }

    void OnBackendVideoSizeChanged(object? sender, EventArgs e) => this.SetVideoSizeFromBackend();

    void SetVideoSizeFromBackend()
    {
        if (this.backend is null || this.VideoSize == this.backend.VideoSize)
            return;

        this.VideoSize = this.backend.VideoSize;
        this.VideoSizeChanged?.Invoke(this, this.VideoSize);
    }

    void OnBackendEnded(object? sender, EventArgs e)
    {
        this.StopPositionTimer();
        this.ShowTransportBarNow();
        this.MediaEnded?.Invoke(this, EventArgs.Empty);
    }

    void OnBackendFailed(object? sender, MediaFailure failure) => this.RaiseFailure(failure);

    void RaiseFailure(MediaFailure failure)
    {
        this.CurrentState = MediaElementState.Failed;
        this.StopPositionTimer();
        this.ShowError(failure.Message);
        this.MediaFailed?.Invoke(this, failure);
    }

    void OnBackendPipChanged(object? sender, bool active)
    {
        this.IsPictureInPictureActive = active;
        this.PictureInPictureChanged?.Invoke(this, active);
    }

    void OnBackendRemoteCommand(object? sender, MediaRemoteCommand command)
    {
        // The backend has already acted; this only keeps the control's own surface in step.
        if (command == MediaRemoteCommand.Seek)
            this.PushPositionFromPlayer();

        this.UpdatePlayPauseGlyph();
    }


    // ── position ticking ─────────────────────────────────────────────────────────────────────────

    void StartPositionTimer()
    {
        this.positionTimer ??= this.CreatePositionTimer();
        if (this.positionTimer is { IsRunning: false })
            this.positionTimer.Start();
    }

    IDispatcherTimer? CreatePositionTimer()
    {
        var timer = this.Dispatcher?.CreateTimer();
        if (timer is null)
            return null;

        timer.Interval = this.PositionUpdateInterval;
        timer.IsRepeating = true;
        timer.Tick += (_, _) => this.PushPositionFromPlayer();
        return timer;
    }

    void StopPositionTimer()
    {
        if (this.positionTimer is { IsRunning: true })
            this.positionTimer.Stop();
    }

    void ApplyTimerInterval()
    {
        this.positionTimer ??= this.CreatePositionTimer();
        if (this.positionTimer is not null)
            this.positionTimer.Interval = this.PositionUpdateInterval;
    }

    void PushPositionFromPlayer()
    {
        if (this.backend is null || this.isScrubbing)
            return;

        if (this.Duration != this.backend.Duration)
        {
            this.Duration = this.backend.Duration;
            this.UpdateTimeLabels();
        }

        this.BufferedProgress = this.backend.BufferedProgress;
        this.SetPositionFromPlayer(this.backend.Position);
    }

    void SetPositionFromPlayer(TimeSpan position)
    {
        this.isPushingPosition = true;
        try
        {
            this.Position = position;
        }
        finally
        {
            this.isPushingPosition = false;
        }

        this.UpdateSeekBar();
        this.UpdateTimeLabels();
        this.PositionChanged?.Invoke(this, position);
    }

    void StopTimers()
    {
        this.StopPositionTimer();
        if (this.autoHideTimer is { IsRunning: true })
            this.autoHideTimer.Stop();

        this.openCts?.Cancel();
        this.openCts?.Dispose();
        this.openCts = null;
    }


    // ── public operations ────────────────────────────────────────────────────────────────────────

    /// <summary>Start or resume playback.</summary>
    public void Play()
    {
        if (this.backend is null)
            this.playRequested = this.mirrorOwner is null; // no player until the handler connects — remember it
        else
            this.backend.Play();
        this.RestartAutoHide();
    }

    /// <summary>Suspend playback at the current position.</summary>
    public void Pause()
    {
        this.playRequested = false;
        this.backend?.Pause();
        this.ShowTransportBarNow();
    }

    /// <summary>Halt playback and rewind to the start.</summary>
    public void Stop()
    {
        this.playRequested = false;
        this.backend?.Stop();
        this.SetPositionFromPlayer(TimeSpan.Zero);
        this.ShowTransportBarNow();
    }

    /// <summary>Flip between playing and paused.</summary>
    public void TogglePlayPause()
    {
        if (this.CurrentState == MediaElementState.Playing)
            this.Pause();
        else
            this.Play();
    }

    /// <summary>Move the playhead to <paramref name="position"/>, clamped to the media's length.</summary>
    public Task SeekAsync(TimeSpan position, CancellationToken ct = default)
    {
        if (this.backend is null)
            return Task.CompletedTask;

        var duration = this.Duration;
        if (duration > TimeSpan.Zero && position > duration)
            position = duration;

        if (position < TimeSpan.Zero)
            position = TimeSpan.Zero;

        this.backend.Seek(position);
        this.SetPositionFromPlayer(position);
        this.SeekCompleted?.Invoke(this, position);
        this.RestartAutoHide();

        return Task.CompletedTask;
    }

    /// <summary>Flip <see cref="IsMuted"/>.</summary>
    public void ToggleMute() => this.IsMuted = !this.IsMuted;

    /// <summary>Flip <see cref="IsFullScreen"/>.</summary>
    public void ToggleFullScreen()
    {
        // The fullscreen mirror doesn't own the modal page — bounce the request to the inline control.
        var target = this.mirrorOwner ?? this;
        target.IsFullScreen = !target.IsFullScreen;
    }

    /// <summary>
    /// Ask the OS to detach the video into a floating always-on-top window. Returns <c>false</c> where the
    /// platform, OS version, or app manifest doesn't allow it — it never throws for lack of support.
    /// </summary>
    public Task<bool> TryEnterPictureInPictureAsync()
        => this.backend?.TryEnterPictureInPictureAsync() ?? Task.FromResult(false);

    /// <summary>Return from Picture-in-Picture to inline playback. A no-op when not in PiP.</summary>
    public Task ExitPictureInPictureAsync()
        => this.backend?.ExitPictureInPictureAsync() ?? Task.CompletedTask;


    // ── fullscreen ───────────────────────────────────────────────────────────────────────────────

    async void ApplyFullScreen(bool value)
    {
        // A mirror is born fullscreen; it must not try to push a page of its own.
        if (this.mirrorOwner is not null || value == this.isFullScreenApplied || this.isFullScreenTransitioning)
            return;

        var navigation = this.FindNavigation();
        if (navigation is null)
        {
            // No page to push onto (unit test, detached template). Revert rather than lie about the state.
            this.SetValue(IsFullScreenProperty, this.isFullScreenApplied);
            return;
        }

        this.isFullScreenTransitioning = true;
        try
        {
            if (value)
            {
                this.fullScreenPage = new MediaFullScreenPage(this);
                this.isFullScreenApplied = true;
                await navigation.PushModalAsync(this.fullScreenPage, false).ConfigureAwait(true);
            }
            else
            {
                this.isFullScreenApplied = false;
                if (this.fullScreenPage is not null)
                    await navigation.PopModalAsync(false).ConfigureAwait(true);

                this.fullScreenPage = null;

                // The mirror's surface took ownership of the player's output; claim it back or we stay dark.
                this.surface.RebindOutput();
            }

            this.FullScreenChanged?.Invoke(this, value);
        }
        catch (Exception ex)
        {
            this.isFullScreenApplied = !value;
            this.SetValue(IsFullScreenProperty, this.isFullScreenApplied);
            this.RaiseFailure(new MediaFailure("Could not toggle fullscreen", ex));
        }
        finally
        {
            this.isFullScreenTransitioning = false;
        }
    }

    // Called by the fullscreen page when it's dismissed by the platform back gesture rather than our button.
    internal void OnFullScreenPageDismissed()
    {
        if (!this.isFullScreenApplied)
            return;

        this.isFullScreenApplied = false;
        this.fullScreenPage = null;
        this.SetValue(IsFullScreenProperty, false);
        this.surface.RebindOutput();
        this.FullScreenChanged?.Invoke(this, false);
    }

    INavigation? FindNavigation()
    {
        Element? element = this;
        while (element is not null)
        {
            if (element is Page page)
                return page.Navigation;

            element = element.Parent;
        }

        return Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation;
    }


    // ── events ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>Raised on the UI thread whenever <see cref="CurrentState"/> changes.</summary>
    public event EventHandler<MediaElementState>? StateChanged;

    /// <summary>Raised once the source is loaded and <see cref="Duration"/> is known.</summary>
    public event EventHandler? MediaOpened;

    /// <summary>
    /// Raised when <see cref="VideoSize"/> becomes known, or changes. Carries the new size.
    /// </summary>
    /// <remarks>
    /// Use this rather than reading <see cref="VideoSize"/> inside <see cref="MediaOpened"/>: on Android the
    /// size arrives from a separate player callback that routinely lands <i>after</i> the media is open, so
    /// the read-at-open version of the same code reports <see cref="Size.Zero"/> there for real video.
    /// </remarks>
    public event EventHandler<Size>? VideoSizeChanged;

    /// <summary>Raised when playback reaches the end. Not raised while <see cref="IsLooping"/>.</summary>
    public event EventHandler? MediaEnded;

    /// <summary>Raised when the source can't be opened or playback aborts.</summary>
    public event EventHandler<MediaFailure>? MediaFailed;

    /// <summary>Raised every <see cref="PositionUpdateInterval"/> while playing, and after each seek.</summary>
    public event EventHandler<TimeSpan>? PositionChanged;

    /// <summary>Raised after a seek has been handed to the player.</summary>
    public event EventHandler<TimeSpan>? SeekCompleted;

    /// <summary>Raised when fullscreen is entered or left, however it was triggered.</summary>
    public event EventHandler<bool>? FullScreenChanged;

    /// <summary>Raised when the video enters or leaves a Picture-in-Picture window.</summary>
    public event EventHandler<bool>? PictureInPictureChanged;
}
