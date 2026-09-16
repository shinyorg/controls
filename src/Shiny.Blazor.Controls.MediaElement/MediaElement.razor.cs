using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Shiny.Controls.Media;

namespace Shiny.Blazor.Controls.Media;

/// <summary>
/// Plays local and remote audio/video in the browser with a themed transport bar whose every piece —
/// play/pause, seek scrubber, volume, fullscreen — toggles independently. The Blazor counterpart of the
/// MAUI <c>Shiny.Maui.Controls.Media.MediaElement</c>, with the same property names and semantics.
/// </summary>
/// <remarks>
/// The bar is drawn in Razor rather than handed to the browser because HTML5's own <c>controls</c>
/// attribute is all-or-nothing (the <c>controlsList</c> attribute only subtracts download/fullscreen/cast,
/// and only in Chromium). Drawing it keeps parity with the MAUI control and lets it theme with the rest
/// of the suite.
/// </remarks>
public partial class MediaElement : IAsyncDisposable
{
    [Inject] IJSRuntime JS { get; set; } = default!;

    IJSObjectReference? module;
    DotNetObjectReference<MediaElement>? selfRef;
    ElementReference mediaEl;
    ElementReference containerEl;

    MediaBrowserCapabilities capabilities = new();
    string? errorMessage;
    bool isScrubbing;
    bool isTransportBarShown = true;
    double scrubPosition;
    CancellationTokenSource? autoHideCts;
    bool disposed;

    // Last values pushed to JS, so OnParametersSetAsync only crosses the interop boundary on real changes.
    string? appliedSource;
    double appliedVolume = -1;
    bool appliedMuted;
    double appliedRate = -1;
    bool appliedLoop;
    MediaAspect appliedAspect = (MediaAspect)(-1);
    string? appliedMetadataKey;


    // ── parameters ───────────────────────────────────────────────────────────────────────────────

    /// <summary>URL of the media to play — absolute, or relative to the app's base path.</summary>
    [Parameter] public string? Source { get; set; }

    /// <summary>
    /// Start playing as soon as the source loads. Browsers block autoplay with sound until the user has
    /// interacted with the page; pair it with <see cref="IsMuted"/> or expect <see cref="OnMediaFailed"/>.
    /// </summary>
    [Parameter] public bool AutoPlay { get; set; }

    /// <summary>Restart from the beginning on reaching the end.</summary>
    [Parameter] public bool IsLooping { get; set; }

    /// <summary>Output volume, 0..1. Ignored by browsers that refuse programmatic volume (iOS Safari).</summary>
    [Parameter] public double Volume { get; set; } = 1d;

    /// <summary>Silence output without disturbing <see cref="Volume"/>.</summary>
    [Parameter] public bool IsMuted { get; set; }

    /// <summary>Raised when <see cref="IsMuted"/> changes, so it can be bound with <c>@bind-IsMuted</c>.</summary>
    [Parameter] public EventCallback<bool> IsMutedChanged { get; set; }

    /// <summary>Playback speed; 1 is normal.</summary>
    [Parameter] public double PlaybackRate { get; set; } = 1d;

    /// <summary>How the video image is scaled into the element.</summary>
    [Parameter] public MediaAspect Aspect { get; set; } = MediaAspect.AspectFit;

    /// <summary>Image shown before the first frame is decoded.</summary>
    [Parameter] public string? Poster { get; set; }

    /// <summary>
    /// Publish <see cref="Metadata"/> to the browser's Media Session so the OS transport UI (macOS Now
    /// Playing, Android notification, Windows SMTC) can drive playback.
    /// </summary>
    /// <remarks>
    /// Whether audio actually survives the tab being hidden is the browser's call, not the app's — most
    /// keep audio running and throttle video. There is no web API to force it.
    /// </remarks>
    [Parameter] public bool EnableBackgroundPlayback { get; set; }

    /// <summary>Now-playing information for the Media Session UI.</summary>
    [Parameter] public MediaMetadata? Metadata { get; set; }

    /// <summary>Master switch for the built-in transport bar.</summary>
    [Parameter] public bool ShowTransportBar { get; set; } = true;

    /// <summary>Show the play/pause button.</summary>
    [Parameter] public bool ShowPlayPauseButton { get; set; } = true;

    /// <summary>Show the seek scrubber.</summary>
    [Parameter] public bool ShowSeekBar { get; set; } = true;

    /// <summary>Show the mute button and volume slider.</summary>
    [Parameter] public bool ShowVolumeControl { get; set; } = true;

    /// <summary>Show the fullscreen toggle.</summary>
    [Parameter] public bool ShowFullScreenButton { get; set; } = true;

    /// <summary>Show the elapsed/total time labels.</summary>
    [Parameter] public bool ShowTimeLabels { get; set; } = true;

    /// <summary>Show the Picture-in-Picture button. Hidden automatically where the browser lacks the API.</summary>
    [Parameter] public bool ShowPictureInPictureButton { get; set; }

    /// <summary>Fade the transport bar out after <see cref="TransportBarAutoHideDelay"/> of no interaction while playing.</summary>
    [Parameter] public bool AutoHideTransportBar { get; set; } = true;

    /// <summary>How long the transport bar lingers before auto-hiding.</summary>
    [Parameter] public TimeSpan TransportBarAutoHideDelay { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Extra CSS classes on the root element.</summary>
    [Parameter] public string? CssClass { get; set; }

    /// <summary>Inline style on the root element.</summary>
    [Parameter] public string? Style { get; set; }

    /// <summary>Raised whenever <see cref="CurrentState"/> changes.</summary>
    [Parameter] public EventCallback<MediaElementState> OnStateChanged { get; set; }

    /// <summary>Raised once metadata has loaded and <see cref="Duration"/> is known.</summary>
    [Parameter] public EventCallback OnMediaOpened { get; set; }

    /// <summary>
    /// Raised when <see cref="VideoWidth"/>/<see cref="VideoHeight"/> become known, or change.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="OnMediaOpened"/> because <c>loadedmetadata</c> is not the only moment the
    /// browser learns the size — an adaptive source that switches rendition reports a new one mid-playback.
    /// The MAUI control raises <c>VideoSizeChanged</c> for the same reason.
    /// </remarks>
    [Parameter] public EventCallback OnVideoSizeChanged { get; set; }

    /// <summary>Raised when playback reaches the end.</summary>
    [Parameter] public EventCallback OnMediaEnded { get; set; }

    /// <summary>Raised when the media cannot be loaded or played.</summary>
    [Parameter] public EventCallback<string> OnMediaFailed { get; set; }

    /// <summary>Raised as the playhead advances.</summary>
    [Parameter] public EventCallback<TimeSpan> OnPositionChanged { get; set; }

    /// <summary>Raised when fullscreen is entered or left, however it was triggered (including Escape).</summary>
    [Parameter] public EventCallback<bool> OnFullScreenChanged { get; set; }

    /// <summary>Raised when the video enters or leaves a Picture-in-Picture window.</summary>
    [Parameter] public EventCallback<bool> OnPictureInPictureChanged { get; set; }


    // ── read-only state ──────────────────────────────────────────────────────────────────────────

    /// <summary>The player's lifecycle state.</summary>
    public MediaElementState CurrentState { get; private set; } = MediaElementState.None;

    /// <summary>The playhead.</summary>
    public TimeSpan Position { get; private set; }

    /// <summary>Total length, or <see cref="TimeSpan.Zero"/> until known (and for live streams).</summary>
    public TimeSpan Duration { get; private set; }

    /// <summary>How much is buffered ahead of the playhead, 0..1.</summary>
    public double BufferedProgress { get; private set; }

    /// <summary>
    /// Width of the video track in pixels — the element's <c>videoWidth</c> — or 0 until metadata has
    /// loaded and for audio-only media. The MAUI control carries the same value as a <c>VideoSize</c>.
    /// </summary>
    public int VideoWidth { get; private set; }

    /// <summary>Height of the video track in pixels, or 0 while unknown. See <see cref="VideoWidth"/>.</summary>
    public int VideoHeight { get; private set; }

    /// <summary>Whether the player is showing fullscreen.</summary>
    public bool IsFullScreen { get; private set; }

    /// <summary>Whether the video is in a Picture-in-Picture window.</summary>
    public bool IsPictureInPictureActive { get; private set; }

    /// <summary>What this browser will actually honour. Probed once on start.</summary>
    public MediaBrowserCapabilities Capabilities => this.capabilities;


    // ── lifecycle ────────────────────────────────────────────────────────────────────────────────

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        var imported = await this.JS
            .InvokeAsync<IJSObjectReference>("import", "./_content/Shiny.Blazor.Controls.MediaElement/mediaelement.js")
            .ConfigureAwait(true);

        // Disposed while the module was loading: init would register document listeners nothing removes.
        if (this.disposed)
        {
            await imported.DisposeAsync().ConfigureAwait(true);
            return;
        }

        this.module = imported;

        this.selfRef = DotNetObjectReference.Create(this);

        this.capabilities = await this.module
            .InvokeAsync<MediaBrowserCapabilities>("init", this.mediaEl, this.containerEl, this.selfRef)
            .ConfigureAwait(true);

        await this.ApplyParametersAsync().ConfigureAwait(true);
        this.StateHasChanged();
    }

    protected override async Task OnParametersSetAsync()
    {
        if (this.module is not null)
            await this.ApplyParametersAsync().ConfigureAwait(true);
    }

    async Task ApplyParametersAsync()
    {
        if (this.module is null)
            return;

        if (this.appliedVolume != this.Volume)
        {
            this.appliedVolume = this.Volume;
            await this.module.InvokeVoidAsync("setVolume", this.mediaEl, this.Volume).ConfigureAwait(true);
        }

        if (this.appliedMuted != this.IsMuted)
        {
            this.appliedMuted = this.IsMuted;
            await this.module.InvokeVoidAsync("setMuted", this.mediaEl, this.IsMuted).ConfigureAwait(true);
        }

        if (this.appliedRate != this.PlaybackRate)
        {
            this.appliedRate = this.PlaybackRate;
            await this.module.InvokeVoidAsync("setRate", this.mediaEl, this.PlaybackRate).ConfigureAwait(true);
        }

        if (this.appliedLoop != this.IsLooping)
        {
            this.appliedLoop = this.IsLooping;
            await this.module.InvokeVoidAsync("setLoop", this.mediaEl, this.IsLooping).ConfigureAwait(true);
        }

        if (this.appliedAspect != this.Aspect)
        {
            this.appliedAspect = this.Aspect;
            await this.module.InvokeVoidAsync("setObjectFit", this.mediaEl, ToObjectFit(this.Aspect)).ConfigureAwait(true);
        }

        // Applied last: setting the source kicks off a load (and possibly autoplay), and everything above
        // should already be in force when the first frame arrives.
        if (this.appliedSource != this.Source)
        {
            this.appliedSource = this.Source;
            this.errorMessage = null;
            this.Duration = TimeSpan.Zero;
            this.Position = TimeSpan.Zero;

            // Cleared with the duration, so a layout sized to the last video does not hold that shape over
            // the new source until its metadata lands.
            this.VideoWidth = 0;
            this.VideoHeight = 0;
            await this.module.InvokeVoidAsync("setSource", this.mediaEl, this.Source, this.AutoPlay).ConfigureAwait(true);
        }

        await this.ApplyMediaSessionAsync().ConfigureAwait(true);
    }

    async Task ApplyMediaSessionAsync()
    {
        if (this.module is null || this.selfRef is null || !this.capabilities.MediaSession)
            return;

        // Metadata is a mutable object the caller may reuse, so compare its contents rather than its
        // identity — otherwise an in-place edit would never reach the OS transport UI.
        var key = this.EnableBackgroundPlayback
            ? $"{this.Metadata?.Title}|{this.Metadata?.Artist}|{this.Metadata?.Album}|{this.Metadata?.ArtworkUri}"
            : null;

        if (this.appliedMetadataKey == key)
            return;

        this.appliedMetadataKey = key;

        await this.module.InvokeVoidAsync(
            "setMediaSession",
            this.mediaEl,
            this.EnableBackgroundPlayback ? this.Metadata?.Title : null,
            this.EnableBackgroundPlayback ? this.Metadata?.Artist : null,
            this.EnableBackgroundPlayback ? this.Metadata?.Album : null,
            this.EnableBackgroundPlayback ? this.Metadata?.ArtworkUri : null,
            this.selfRef).ConfigureAwait(true);
    }

    static string ToObjectFit(MediaAspect aspect) => aspect switch
    {
        MediaAspect.AspectFill => "cover",
        MediaAspect.Fill => "fill",
        _ => "contain"
    };


    // ── operations ───────────────────────────────────────────────────────────────────────────────

    /// <summary>Start or resume playback.</summary>
    public async Task PlayAsync()
    {
        if (this.module is not null)
            await this.module.InvokeVoidAsync("play", this.mediaEl).ConfigureAwait(true);

        this.RestartAutoHide();
    }

    /// <summary>Suspend playback at the current position.</summary>
    public async Task PauseAsync()
    {
        if (this.module is not null)
            await this.module.InvokeVoidAsync("pause", this.mediaEl).ConfigureAwait(true);

        this.ShowTransportBarNow();
    }

    /// <summary>Halt playback and rewind to the start.</summary>
    public async Task StopAsync()
    {
        if (this.module is not null)
            await this.module.InvokeVoidAsync("stop", this.mediaEl).ConfigureAwait(true);

        this.ShowTransportBarNow();
    }

    /// <summary>Flip between playing and paused.</summary>
    public Task TogglePlayPauseAsync()
        => this.CurrentState == MediaElementState.Playing ? this.PauseAsync() : this.PlayAsync();

    /// <summary>Move the playhead.</summary>
    public async Task SeekAsync(TimeSpan position)
    {
        if (this.module is not null)
            await this.module.InvokeVoidAsync("seek", this.mediaEl, position.TotalSeconds).ConfigureAwait(true);

        this.RestartAutoHide();
    }

    /// <summary>Set <see cref="IsMuted"/>, notifying <see cref="IsMutedChanged"/>.</summary>
    public async Task SetMutedAsync(bool muted)
    {
        this.IsMuted = muted;

        if (this.module is not null)
        {
            this.appliedMuted = muted;
            await this.module.InvokeVoidAsync("setMuted", this.mediaEl, muted).ConfigureAwait(true);
        }

        await this.IsMutedChanged.InvokeAsync(muted).ConfigureAwait(true);
    }

    /// <summary>Flip <see cref="IsMuted"/>.</summary>
    public Task ToggleMuteAsync() => this.SetMutedAsync(!this.IsMuted);

    /// <summary>Enter or leave fullscreen.</summary>
    public async Task SetFullScreenAsync(bool fullScreen)
    {
        if (this.module is null)
            return;

        if (fullScreen)
        {
            // The state flag is not set here: the JS `fullscreenchange` listener owns it, so an exit by
            // Escape or the browser chrome updates the component the same way our own button does.
            await this.module.InvokeAsync<bool>("requestFullscreen", this.containerEl).ConfigureAwait(true);
        }
        else
        {
            await this.module.InvokeVoidAsync("exitFullscreen").ConfigureAwait(true);
        }
    }

    /// <summary>Flip fullscreen.</summary>
    public Task ToggleFullScreenAsync() => this.SetFullScreenAsync(!this.IsFullScreen);

    /// <summary>
    /// Detach the video into a floating Picture-in-Picture window. Returns <c>false</c> where the browser
    /// lacks the API or refuses the request.
    /// </summary>
    public async Task<bool> TryEnterPictureInPictureAsync()
        => this.module is not null
            && await this.module.InvokeAsync<bool>("requestPictureInPicture", this.mediaEl).ConfigureAwait(true);

    /// <summary>Return from Picture-in-Picture to inline playback.</summary>
    public async Task ExitPictureInPictureAsync()
    {
        if (this.module is not null)
            await this.module.InvokeVoidAsync("exitPictureInPicture").ConfigureAwait(true);
    }


    // ── JS callbacks ─────────────────────────────────────────────────────────────────────────────

    [JSInvokable]
    public async Task OnStatus(MediaStatus status)
    {
        var previousState = this.CurrentState;
        this.CurrentState = Enum.TryParse<MediaElementState>(status.State, out var parsed)
            ? parsed
            : MediaElementState.None;

        this.Duration = ToTimeSpan(status.Duration);
        this.BufferedProgress = status.Buffered;
        await this.ApplyVideoSize(status).ConfigureAwait(true);

        // A finger on the scrubber owns the thumb until it lifts; letting timeupdate write through would
        // yank it back to the player's position on every tick.
        if (!this.isScrubbing)
        {
            var position = ToTimeSpan(status.Position);
            if (position != this.Position)
            {
                this.Position = position;
                await this.OnPositionChanged.InvokeAsync(position).ConfigureAwait(true);
            }
        }

        if (this.CurrentState != previousState)
        {
            if (this.CurrentState != MediaElementState.Failed)
                this.errorMessage = null;

            await this.OnStateChanged.InvokeAsync(this.CurrentState).ConfigureAwait(true);
            this.RestartAutoHide();
        }

        this.StateHasChanged();
    }

    [JSInvokable]
    public async Task OnOpened(MediaStatus status)
    {
        this.Duration = ToTimeSpan(status.Duration);
        await this.ApplyVideoSize(status).ConfigureAwait(true);
        await this.OnMediaOpened.InvokeAsync().ConfigureAwait(true);
        this.StateHasChanged();
    }

    /// <summary>
    /// Takes the video dimensions off a snapshot, reporting only a real change.
    /// </summary>
    /// <remarks>
    /// Called from both callbacks on purpose: <c>loadedmetadata</c> is where the size normally first
    /// appears, but a source that switches rendition reports a new one through the ordinary status pushes,
    /// with no second open.
    /// </remarks>
    async Task ApplyVideoSize(MediaStatus status)
    {
        if (status.Width == this.VideoWidth && status.Height == this.VideoHeight)
            return;

        this.VideoWidth = status.Width;
        this.VideoHeight = status.Height;
        await this.OnVideoSizeChanged.InvokeAsync().ConfigureAwait(true);
    }

    [JSInvokable]
    public async Task OnEnded()
    {
        this.ShowTransportBarNow();
        await this.OnMediaEnded.InvokeAsync().ConfigureAwait(true);

        // A JS callback gets no render of its own the way a UI event handler does, and the status push
        // that arrives with `ended` rendered before this ran — so without this the bar the line above
        // just asked for never actually appears, and a finished video is left with no controls.
        this.StateHasChanged();
    }

    [JSInvokable]
    public async Task OnFailed(string message)
    {
        this.errorMessage = message;
        this.CurrentState = MediaElementState.Failed;
        await this.OnStateChanged.InvokeAsync(MediaElementState.Failed).ConfigureAwait(true);
        await this.OnMediaFailed.InvokeAsync(message).ConfigureAwait(true);
        this.StateHasChanged();
    }

    // Named with the Js suffix because the matching [Parameter] EventCallbacks already own
    // OnFullScreenChanged / OnPictureInPictureChanged, and a type can't have a property and a method
    // under one name. The JS module calls these names.
    [JSInvokable]
    public async Task OnFullScreenChangedJs(bool isFullScreen)
    {
        this.IsFullScreen = isFullScreen;
        await this.OnFullScreenChanged.InvokeAsync(isFullScreen).ConfigureAwait(true);
        this.StateHasChanged();
    }

    [JSInvokable]
    public async Task OnPictureInPictureChangedJs(bool isActive)
    {
        this.IsPictureInPictureActive = isActive;
        await this.OnPictureInPictureChanged.InvokeAsync(isActive).ConfigureAwait(true);
        this.StateHasChanged();
    }

    [JSInvokable]
    public void OnRemoteCommand(string command)
    {
        // The browser already acted on the media element; the status push that follows carries the new
        // state. Re-rendering here just keeps the bar's glyph from lagging a frame behind.
        this.StateHasChanged();
    }

    static TimeSpan ToTimeSpan(double seconds)
        => Double.IsNaN(seconds) || Double.IsInfinity(seconds) || seconds < 0
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds(seconds);


    // ── transport bar ────────────────────────────────────────────────────────────────────────────

    bool UseHourFormat => this.Duration >= TimeSpan.FromHours(1);

    string ElapsedText => MediaTimeFormatter.Format(
        this.isScrubbing ? TimeSpan.FromSeconds(this.scrubPosition) : this.Position, this.UseHourFormat);

    string DurationText => MediaTimeFormatter.Format(this.Duration, this.UseHourFormat);

    string PlayPauseLabel => this.CurrentState == MediaElementState.Playing ? "Pause" : "Play";

    string MuteLabel => this.IsMuted ? "Unmute" : "Mute";

    string FullScreenLabel => this.IsFullScreen ? "Exit full screen" : "Enter full screen";

    double SeekMax => this.Duration > TimeSpan.Zero ? this.Duration.TotalSeconds : 1d;

    double SeekValue => this.isScrubbing ? this.scrubPosition : this.Position.TotalSeconds;

    // The buffered-ahead band is painted as a gradient stop behind the thumb; a range input has no second
    // track of its own.
    string SeekBarStyle
    {
        get
        {
            var played = this.SeekMax > 0 ? this.SeekValue / this.SeekMax : 0d;
            return string.Create(CultureInfo.InvariantCulture,
                $"--shiny-media-played:{played * 100:0.##}%;--shiny-media-buffered:{this.BufferedProgress * 100:0.##}%");
        }
    }

    void OnScrubStart() => this.isScrubbing = true;

    void OnScrubbing(ChangeEventArgs args)
    {
        this.isScrubbing = true;

        if (Double.TryParse(args.Value?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            this.scrubPosition = seconds;

        this.RestartAutoHide();
    }

    async Task OnScrubCommittedAsync(ChangeEventArgs args)
    {
        if (Double.TryParse(args.Value?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            this.scrubPosition = seconds;
            this.Position = TimeSpan.FromSeconds(seconds);
            await this.SeekAsync(this.Position).ConfigureAwait(true);
        }

        this.isScrubbing = false;
    }

    async Task OnVolumeChangedAsync(ChangeEventArgs args)
    {
        if (!Double.TryParse(args.Value?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var volume))
            return;

        this.Volume = Math.Clamp(volume, 0d, 1d);
        this.appliedVolume = this.Volume;

        if (this.module is not null)
            await this.module.InvokeVoidAsync("setVolume", this.mediaEl, this.Volume).ConfigureAwait(true);

        if (this.IsMuted && this.Volume > 0)
            await this.SetMutedAsync(false).ConfigureAwait(true);

        this.RestartAutoHide();
    }

    /// <summary>
    /// A click anywhere on the picture toggles playback and wakes the transport bar.
    /// </summary>
    /// <remarks>
    /// It used to toggle the bar's visibility instead, which quietly ate the pause click. The bar hides
    /// itself after a few seconds of play — including out from under a pointer left sitting on the
    /// play/pause button, which is exactly where it is after starting playback — and the click aimed at
    /// that button reached the video instead and only brought the bar back. Pausing took two clicks and
    /// read as broken. Toggling playback here is also what every other player does with a click on the
    /// picture, and the bar now stays mounted, so a click on a visible button is never swallowed.
    /// </remarks>
    async Task OnPictureClickedAsync()
    {
        this.ShowTransportBarNow();
        await this.TogglePlayPauseAsync().ConfigureAwait(true);
    }

    void ShowTransportBarNow()
    {
        this.isTransportBarShown = true;
        this.RestartAutoHide();
    }

    void KeepTransportBarAwake() => this.ShowTransportBarNow();

    void OnPointerLeft()
    {
        if (this.AutoHideTransportBar && this.CurrentState == MediaElementState.Playing && !this.isScrubbing)
        {
            this.isTransportBarShown = false;
            this.StateHasChanged();
        }
    }

    void RestartAutoHide()
    {
        this.autoHideCts?.Cancel();
        this.autoHideCts?.Dispose();
        this.autoHideCts = null;

        // Only a playing video hides its controls. A paused frame, an audio-only track, or a failed load
        // keeps them — that's what every player does, and it keeps the buttons reachable to a screen reader.
        if (!this.AutoHideTransportBar || this.CurrentState != MediaElementState.Playing)
            return;

        var cts = new CancellationTokenSource();
        this.autoHideCts = cts;

        _ = this.HideAfterDelayAsync(cts.Token);
    }

    async Task HideAfterDelayAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(this.TransportBarAutoHideDelay, ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (ct.IsCancellationRequested || this.isScrubbing || this.CurrentState != MediaElementState.Playing)
            return;

        this.isTransportBarShown = false;
        this.StateHasChanged();
    }


    public async ValueTask DisposeAsync()
    {
        this.disposed = true;
        this.autoHideCts?.Cancel();
        this.autoHideCts?.Dispose();
        this.autoHideCts = null;

        if (this.module is not null)
        {
            try
            {
                await this.module.InvokeVoidAsync("dispose", this.mediaEl).ConfigureAwait(false);
                await this.module.DisposeAsync().ConfigureAwait(false);
            }
            catch (JSDisconnectedException)
            {
                // the circuit/page is already gone — nothing left to clean up on the JS side
            }

            this.module = null;
        }

        this.selfRef?.Dispose();
        this.selfRef = null;
    }
}
