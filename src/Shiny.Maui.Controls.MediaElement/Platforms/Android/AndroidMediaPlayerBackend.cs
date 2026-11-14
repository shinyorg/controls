using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.Content;
using AndroidX.Media3.Common;
using AndroidX.Media3.ExoPlayer;
using AndroidX.Media3.Session;
using AndroidX.Media3.UI;
using Microsoft.Maui.ApplicationModel;
using MediaItem = AndroidX.Media3.Common.MediaItem;
using AndroidMediaMetadata = AndroidX.Media3.Common.MediaMetadata;
using MediaMetadata = Shiny.Controls.Media.MediaMetadata;

namespace Shiny.Maui.Controls.Media;

/// <summary>
/// The Android backend, built on Media3/ExoPlayer with a <see cref="PlayerView"/> as the output surface
/// (its own controller switched off — Shiny draws the transport bar).
/// </summary>
/// <remarks>
/// ExoPlayer rather than <c>android.media.MediaPlayer</c>: it brings HLS and DASH, a real buffered-ahead
/// figure for the scrubber, and the <see cref="MediaSession"/> that the background notification and the
/// lock screen are built from.
/// </remarks>
partial class AndroidMediaPlayerBackend : IMediaPlayerBackend
{
    readonly Context context;
    readonly IExoPlayer player;
    readonly AndroidPlayerListener listener;

    PlayerView? output;
    Android.Views.TextureView? textureOutput;
    MediaSession? session;
    MediaAspect aspect = MediaAspect.AspectFit;
    MediaMetadata? metadata;
    bool backgroundEnabled;
    bool serviceStarted;
    bool disposed;

    public AndroidMediaPlayerBackend()
    {
        this.context = Android.App.Application.Context;

        // the audio tee for analysis is built into the sink here: ExoPlayer only builds its audio sink with the
        // player, so it cannot be added later. It does nothing while no analyzer wants audio.
        this.audioTee = new AnalysisAudioTee(this);
        this.player = new ExoPlayerBuilder(this.context, new AnalysisRenderersFactory(this.context, this.audioTee)).Build()!;
        this.player.PlayWhenReady = false;

        this.listener = new AndroidPlayerListener(
            this.OnPlaybackStateChanged,
            _ => this.SyncState(),
            this.OnPlayerError,
            this.OnVideoSizeChanged);

        this.player.AddListener(this.listener);

        AndroidMediaIntegration.PictureInPictureModeChanged += this.OnPipModeChanged;
    }


    public MediaPlaybackCapabilities Capabilities
    {
        get
        {
            var capabilities = MediaPlaybackCapabilities.BackgroundAudio
                | MediaPlaybackCapabilities.PlaybackRate
                | MediaPlaybackCapabilities.Volume
                | MediaPlaybackCapabilities.BufferProgress
                | MediaPlaybackCapabilities.FrameAnalysis
                | MediaPlaybackCapabilities.AudioAnalysis
                | MediaPlaybackCapabilities.SourceAudioAnalysis;

            if (OperatingSystem.IsAndroidVersionAtLeast(26)
                && this.context.PackageManager?.HasSystemFeature(PackageManager.FeaturePictureInPicture) == true)
            {
                capabilities |= MediaPlaybackCapabilities.PictureInPicture;
            }

            return capabilities;
        }
    }

    public MediaElementState State { get; private set; } = MediaElementState.None;

    public TimeSpan Position => TimeSpan.FromMilliseconds(Math.Max(0, this.player.CurrentPosition));

    public TimeSpan Duration { get; private set; }

    public double BufferedProgress => Math.Clamp(this.player.BufferedPercentage / 100d, 0d, 1d);

    public Size VideoSize { get; private set; }

    public bool IsPictureInPictureActive { get; private set; }

    public event EventHandler<MediaElementState>? StateChanged;
    public event EventHandler? MediaOpened;
    public event EventHandler? VideoSizeChanged;
    public event EventHandler? MediaEnded;
    public event EventHandler<MediaFailure>? Failed;
    public event EventHandler<bool>? PictureInPictureChanged;

    // Never raised on Android by design: the media notification's buttons are wired straight into the
    // ExoPlayer by MediaSession, so the resulting state change already comes back through
    // OnIsPlayingChanged. There is no separate command to forward.
#pragma warning disable CS0067
    public event EventHandler<MediaRemoteCommand>? RemoteCommandReceived;
#pragma warning restore CS0067


    // ── output ───────────────────────────────────────────────────────────────────────────────────

    public void SetOutput(object? nativeView)
    {
        if (this.output is not null)
            this.output.Player = null;

        if (this.textureOutput is not null)
        {
            this.player.ClearVideoTextureView(this.textureOutput);
            this.textureOutput = null;
        }

        // VideoText renders through a bare TextureView: PlayerView defaults to a SurfaceView, which punches
        // a hole through the window and so can never be masked by the view drawing over it.
        if (nativeView is IVideoTextureOutput texture)
        {
            this.output = null;
            this.textureOutput = texture.TextureView;
            this.player.SetVideoTextureView(this.textureOutput);
            return;
        }

        this.output = nativeView as PlayerView;

        if (this.output is null)
            return;

        this.output.UseController = false;
        this.output.Player = this.player;
        this.ApplyResizeMode();
    }


    // ── source ───────────────────────────────────────────────────────────────────────────────────

    public Task OpenAsync(MediaSource? source, CancellationToken ct = default)
    {
        this.Duration = TimeSpan.Zero;
        this.VideoSize = Size.Zero;

        if (source is null)
        {
            this.player.ClearMediaItems();
            this.SetState(MediaElementState.None);
            return Task.CompletedTask;
        }

        var uri = ResolveUri(source);
        if (uri is null)
        {
            this.RaiseFailed(new MediaFailure($"Could not resolve media source '{source}'"));
            return Task.CompletedTask;
        }

        this.SetState(MediaElementState.Opening);

        var item = new MediaItem.Builder()
            .SetUri(uri)!
            .SetMediaMetadata(this.BuildMetadata())!
            .Build()!;

        this.currentItem = item;
        this.ApplyAnalysisEffects();
        this.analysisSink?.OnDiscontinuity(TimeSpan.Zero);
        this.player.SetMediaItem(item);
        this.player.Prepare();
        return Task.CompletedTask;
    }

    static string? ResolveUri(MediaSource source) => source switch
    {
        UriMediaSource { Uri: { } uri } => uri.AbsoluteUri,
        FileMediaSource { Path: { Length: > 0 } path } => path.StartsWith("file://", StringComparison.Ordinal)
            ? path
            : "file://" + path,
        // MAUI packs Resources/Raw into the APK's assets, and ExoPlayer's AssetDataSource reads that scheme.
        ResourceMediaSource { Path: { Length: > 0 } path } => "asset:///" + path.TrimStart('/'),
        _ => null
    };

    AndroidMediaMetadata BuildMetadata()
    {
        var builder = new AndroidMediaMetadata.Builder();

        if (this.metadata?.Title is { Length: > 0 } title)
            builder.SetTitle(title);

        if (this.metadata?.Artist is { Length: > 0 } artist)
            builder.SetArtist(artist);

        if (this.metadata?.Album is { Length: > 0 } album)
            builder.SetAlbumTitle(album);

        if (this.metadata?.ArtworkUri is { Length: > 0 } artwork)
            builder.SetArtworkUri(Android.Net.Uri.Parse(artwork));

        return builder.Build()!;
    }


    // ── transport ────────────────────────────────────────────────────────────────────────────────

    public void Play() => this.player.Play();

    public void Pause() => this.player.Pause();

    public void Stop()
    {
        this.player.Pause();
        this.player.SeekTo(0);
        this.analysisSink?.OnDiscontinuity(TimeSpan.Zero);
        this.SetState(MediaElementState.Stopped);
    }

    public void Seek(TimeSpan position)
    {
        this.player.SeekTo((long)position.TotalMilliseconds);
        this.analysisSink?.OnDiscontinuity(position);
    }

    // ExoPlayer has no mute flag — muting is volume 0 — so the pre-mute level has to be remembered here,
    // or unmuting would restore full volume regardless of where the user had left the slider.
    double lastVolume = 1d;
    bool muted;

    public void SetVolume(double volume)
    {
        this.lastVolume = Math.Clamp(volume, 0d, 1d);

        if (!this.muted)
            this.player.Volume = (float)this.lastVolume;
    }

    public void SetMuted(bool muted)
    {
        this.muted = muted;
        this.player.Volume = muted ? 0f : (float)this.lastVolume;
    }

    public void SetRate(double rate) => this.player.SetPlaybackSpeed((float)rate);

    public void SetLooping(bool looping)
        => this.player.RepeatMode = looping
            ? BasePlayer.InterfaceConsts.RepeatModeOne
            : BasePlayer.InterfaceConsts.RepeatModeOff;

    public void SetAspect(MediaAspect aspect)
    {
        this.aspect = aspect;
        this.ApplyResizeMode();
    }

    void ApplyResizeMode()
    {
        if (this.output is null)
            return;

        this.output.ResizeMode = this.aspect switch
        {
            MediaAspect.AspectFill => AspectRatioFrameLayout.ResizeModeZoom,
            MediaAspect.Fill => AspectRatioFrameLayout.ResizeModeFill,
            _ => AspectRatioFrameLayout.ResizeModeFit
        };
    }

    public void SetKeepScreenOn(bool keepOn)
    {
        if (this.output is not null)
            this.output.KeepScreenOn = keepOn;
    }


    // ── background playback ──────────────────────────────────────────────────────────────────────

    public void SetBackgroundPlayback(bool enabled, MediaMetadata? metadata)
    {
        this.metadata = metadata;

        // Without a wake mode the CPU (and wifi, for a stream) sleeps with the screen and playback dies a
        // few seconds after the device locks — the classic "it works until I put the phone down" bug.
        this.player.SetWakeMode(enabled ? C.WakeModeNetwork : C.WakeModeNone);

        if (enabled)
        {
            this.EnsureSession();
            this.StartService();
        }
        else
        {
            this.StopService();
            this.ReleaseSession();
        }

        this.backgroundEnabled = enabled;
    }

    void EnsureSession()
    {
        if (this.session is not null)
            return;

        // A stable id keeps Android from treating a re-created session as a second app in the shade.
        this.session = new MediaSession.Builder(this.context, this.player)
            .SetId("shiny-media-" + this.context.PackageName)!
            .Build();

        ShinyMediaSessionService.ActiveSession = this.session;
    }

    void StartService()
    {
        if (this.serviceStarted)
            return;

        try
        {
            var intent = new Intent(this.context, typeof(ShinyMediaSessionService));
            ContextCompat.StartForegroundService(this.context, intent);
            this.serviceStarted = true;
        }
        catch (Exception ex)
        {
            // Most often a missing POST_NOTIFICATIONS grant on API 33+. Playback still works in the
            // foreground, so this degrades rather than fails.
            this.Failed?.Invoke(this, new MediaFailure(
                "Background playback could not start its foreground service. Check the POST_NOTIFICATIONS permission.", ex));
        }
    }

    void StopService()
    {
        if (!this.serviceStarted)
            return;

        try
        {
            this.context.StopService(new Intent(this.context, typeof(ShinyMediaSessionService)));
        }
        catch (Exception)
        {
            // already gone
        }

        this.serviceStarted = false;
    }

    void ReleaseSession()
    {
        if (this.session is null)
            return;

        ShinyMediaSessionService.ActiveSession = null;
        this.session.Release();
        this.session.Dispose();
        this.session = null;
    }


    // ── picture-in-picture ───────────────────────────────────────────────────────────────────────

    public Task<bool> TryEnterPictureInPictureAsync()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
            return Task.FromResult(false);

        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (activity is null)
            return Task.FromResult(false);

        try
        {
            var builder = new PictureInPictureParams.Builder();

            if (this.VideoSize.Width > 0 && this.VideoSize.Height > 0)
            {
                // Android rejects anything outside 1:2.39 … 2.39:1 with an IllegalArgumentException, so a
                // very tall or very wide clip has to be clamped rather than passed through.
                var ratio = this.VideoSize.Width / this.VideoSize.Height;
                ratio = Math.Clamp(ratio, 1d / 2.39d, 2.39d);
                builder.SetAspectRatio(new Android.Util.Rational((int)Math.Round(ratio * 1000), 1000));
            }

            return Task.FromResult(activity.EnterPictureInPictureMode(builder.Build()!));
        }
        catch (Exception)
        {
            // The activity has to opt in with android:supportsPictureInPicture="true"; without it this
            // throws rather than returning false.
            return Task.FromResult(false);
        }
    }

    public Task ExitPictureInPictureAsync()
    {
        // Android has no "leave PiP" call — the user taps the expand affordance, or the app brings its
        // activity back to the front, which is the app's decision to make rather than the control's.
        return Task.CompletedTask;
    }

    void OnPipModeChanged(object? sender, bool inPip)
        => OnMain(() =>
        {
            this.IsPictureInPictureActive = inPip;
            this.PictureInPictureChanged?.Invoke(this, inPip);
        });


    // ── player events ────────────────────────────────────────────────────────────────────────────

    void OnPlaybackStateChanged(int playbackState)
        => OnMain(() =>
        {
            if (playbackState == BasePlayer.InterfaceConsts.StateReady && this.Duration == TimeSpan.Zero)
            {
                var duration = this.player.Duration;

                // C.TIME_UNSET (Long.MinValue + 1) is ExoPlayer's "not known / live"; surfacing it as a
                // duration would put the scrubber at a nonsense length.
                if (duration > 0 && duration != C.TimeUnset)
                {
                    this.Duration = TimeSpan.FromMilliseconds(duration);
                    this.MediaOpened?.Invoke(this, EventArgs.Empty);
                }
            }

            if (playbackState == BasePlayer.InterfaceConsts.StateEnded)
                this.MediaEnded?.Invoke(this, EventArgs.Empty);

            this.SyncState();
        });

    void OnVideoSizeChanged()
        => OnMain(() =>
        {
            var size = this.player.VideoSize;
            if (size is null)
                return;

            var next = new Size(size.Width, size.Height);
            if (next == this.VideoSize)
                return;

            this.VideoSize = next;
            this.VideoSizeChanged?.Invoke(this, EventArgs.Empty);
        });

    void OnPlayerError(PlaybackException? error)
        => OnMain(() => this.RaiseFailed(new MediaFailure(
            error?.LocalizedMessage ?? error?.Message ?? "Playback failed")));

    void SyncState()
    {
        if (this.State == MediaElementState.Failed)
            return;

        var next = this.player.PlaybackState switch
        {
            var s when s == BasePlayer.InterfaceConsts.StateBuffering => MediaElementState.Buffering,
            var s when s == BasePlayer.InterfaceConsts.StateReady =>
                this.player.IsPlaying ? MediaElementState.Playing : MediaElementState.Paused,
            var s when s == BasePlayer.InterfaceConsts.StateEnded => MediaElementState.Paused,
            _ => this.State == MediaElementState.Opening ? MediaElementState.Opening : MediaElementState.None
        };

        this.SetState(next);
    }

    void SetState(MediaElementState state)
    {
        if (this.State == state)
            return;

        this.State = state;
        this.StateChanged?.Invoke(this, state);
    }

    void RaiseFailed(MediaFailure failure)
    {
        this.State = MediaElementState.Failed;
        this.StateChanged?.Invoke(this, MediaElementState.Failed);
        this.Failed?.Invoke(this, failure);
    }

    static void OnMain(Action action)
    {
        if (MainThread.IsMainThread)
            action();
        else
            MainThread.BeginInvokeOnMainThread(action);
    }


    public void Dispose()
    {
        if (this.disposed)
            return;

        this.disposed = true;

        AndroidMediaIntegration.PictureInPictureModeChanged -= this.OnPipModeChanged;

        this.StopService();
        this.ReleaseSession();

        this.player.RemoveListener(this.listener);
        this.listener.Dispose();

        if (this.output is not null)
        {
            this.output.Player = null;
            this.output = null;
        }

        if (this.textureOutput is not null)
        {
            this.player.ClearVideoTextureView(this.textureOutput);
            this.textureOutput = null;
        }

        this.player.Release();
        this.player.Dispose();
    }
}
