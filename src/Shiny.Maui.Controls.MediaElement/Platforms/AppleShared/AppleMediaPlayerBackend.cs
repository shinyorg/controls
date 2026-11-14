using AVFoundation;
using CoreFoundation;
using CoreMedia;
using Foundation;
#if IOS || MACCATALYST
using AVKit;
using UIKit;
#endif

namespace Shiny.Maui.Controls.Media;

/// <summary>
/// The AVFoundation backend, shared by iOS, Mac Catalyst and macOS AppKit. Owns an <see cref="AVPlayer"/>
/// that outlives any view: <see cref="SetOutput"/> just re-points the layer, which is what makes
/// fullscreen and background audio work without re-buffering.
/// </summary>
partial class AppleMediaPlayerBackend : IMediaPlayerBackend
{
    readonly AVPlayer player = new();
    readonly AppleNowPlaying nowPlaying;

    AVPlayerItem? item;
    IApplePlayerOutput? output;

    IDisposable? statusObserver;
    IDisposable? timeControlObserver;
    IDisposable? loadedRangesObserver;
    IDisposable? presentationSizeObserver;
    NSObject? endObserver;
    NSObject? backgroundObserver;
    NSObject? foregroundObserver;

    MediaAspect aspect = MediaAspect.AspectFit;
    MediaMetadata? metadata;
    bool backgroundEnabled;
    bool looping;
    bool keepScreenOn;
    double rate = 1d;
    bool isDetachedForBackground;
    bool disposed;

#if IOS || MACCATALYST
    AVPictureInPictureController? pipController;
    ApplePipDelegate? pipDelegate;
#endif
#if MACOS
    NSObject? idleActivity;
#endif

    public AppleMediaPlayerBackend()
    {
        this.nowPlaying = new AppleNowPlaying(this.HandleRemoteCommand, this.Seek);

        this.timeControlObserver = this.player.AddObserver(
            "timeControlStatus", NSKeyValueObservingOptions.New, _ => this.OnMain(this.SyncState));

#if IOS || MACCATALYST
        // Video decode is killed when the app backgrounds; audio survives only if the layer lets go of the
        // player first. Without this the "background playback" switch does nothing on iOS.
        this.backgroundObserver = NSNotificationCenter.DefaultCenter.AddObserver(
            UIApplication.DidEnterBackgroundNotification, _ => this.OnEnterBackground());

        this.foregroundObserver = NSNotificationCenter.DefaultCenter.AddObserver(
            UIApplication.WillEnterForegroundNotification, _ => this.OnEnterForeground());
#endif
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

#if IOS || MACCATALYST
            // macOS AVKit can do PiP too, but the AppKit MAUI host is preview-quality and untested here,
            // so it is deliberately not advertised there rather than offering a button that may do nothing.
            if (AVPictureInPictureController.IsPictureInPictureSupported)
                capabilities |= MediaPlaybackCapabilities.PictureInPicture;
#endif
            return capabilities;
        }
    }

    public MediaElementState State { get; private set; } = MediaElementState.None;

    public TimeSpan Position
    {
        get
        {
            var seconds = this.player.CurrentTime.Seconds;
            return Double.IsNaN(seconds) || Double.IsInfinity(seconds)
                ? TimeSpan.Zero
                : TimeSpan.FromSeconds(seconds);
        }
    }

    public TimeSpan Duration { get; private set; }

    public double BufferedProgress { get; private set; }

    public Size VideoSize { get; private set; }

    public bool IsPictureInPictureActive { get; private set; }

    public event EventHandler<MediaElementState>? StateChanged;
    public event EventHandler? MediaOpened;
    public event EventHandler? VideoSizeChanged;
    public event EventHandler? MediaEnded;
    public event EventHandler<MediaFailure>? Failed;
    // Only raised on iOS/Catalyst, where AVPictureInPictureController is wired up; macOS advertises no
    // PiP capability, so nothing raises it there.
#pragma warning disable CS0067
    public event EventHandler<bool>? PictureInPictureChanged;
#pragma warning restore CS0067

    public event EventHandler<MediaRemoteCommand>? RemoteCommandReceived;


    // ── output ───────────────────────────────────────────────────────────────────────────────────

    public void SetOutput(object? nativeView)
    {
        // Drop the old layer's reference first: two AVPlayerLayers pointed at one AVPlayer is undefined,
        // and the stale one keeps rendering the last frame.
        if (this.output is not null)
            this.output.PlayerLayer.Player = null;

        this.output = nativeView as IApplePlayerOutput;

        if (this.output is null)
        {
#if IOS || MACCATALYST
            this.TeardownPip();
#endif
            return;
        }

        this.output.PlayerLayer.Player = this.player;
        this.ApplyGravity();

#if IOS || MACCATALYST
        if (this.output.SupportsPictureInPicture)
            this.SetupPip(this.output.PlayerLayer);
        else
            this.TeardownPip();
#endif
    }


    // ── source ───────────────────────────────────────────────────────────────────────────────────

    public Task OpenAsync(MediaSource? source, CancellationToken ct = default)
    {
        this.TeardownItem();
        this.Duration = TimeSpan.Zero;
        this.BufferedProgress = 0;
        this.VideoSize = Size.Zero;

        if (source is null)
        {
            this.player.ReplaceCurrentItemWithPlayerItem(null);
            this.SetState(MediaElementState.None);
            return Task.CompletedTask;
        }

        var url = AppleMediaSourceResolver.Resolve(source);
        if (url is null)
        {
            this.RaiseFailed(new MediaFailure($"Could not resolve media source '{source}'"));
            return Task.CompletedTask;
        }

        this.SetState(MediaElementState.Opening);

        var asset = AVAsset.FromUrl(url);
        this.item = AVPlayerItem.FromAsset(asset);
        this.OnItemCreated(this.item, asset);

        this.statusObserver = this.item.AddObserver(
            "status", NSKeyValueObservingOptions.New, _ => this.OnMain(this.OnItemStatusChanged));

        this.loadedRangesObserver = this.item.AddObserver(
            "loadedTimeRanges", NSKeyValueObservingOptions.New, _ => this.OnMain(this.UpdateBuffered));

        this.presentationSizeObserver = this.item.AddObserver(
            "presentationSize", NSKeyValueObservingOptions.New, _ => this.OnMain(this.UpdateVideoSize));

        this.endObserver = NSNotificationCenter.DefaultCenter.AddObserver(
            AVPlayerItem.DidPlayToEndTimeNotification, this.OnPlayedToEnd, this.item);

        this.player.ReplaceCurrentItemWithPlayerItem(this.item);
        return Task.CompletedTask;
    }

    void OnItemStatusChanged()
    {
        if (this.item is null)
            return;

        switch (this.item.Status)
        {
            case AVPlayerItemStatus.ReadyToPlay:
                this.OnItemReady(this.item);
                this.Duration = ToTimeSpan(this.item.Duration);
                this.UpdateVideoSize();
                this.UpdateBuffered();
                this.MediaOpened?.Invoke(this, EventArgs.Empty);
                this.SyncState();
                this.PublishNowPlaying();
                break;

            case AVPlayerItemStatus.Failed:
                var description = this.item.Error?.LocalizedDescription ?? "The media could not be loaded";
                this.RaiseFailed(new MediaFailure(description));
                break;
        }
    }

    void OnPlayedToEnd(NSNotification notification)
        => this.OnMain(() =>
        {
            if (this.looping)
            {
                this.player.Seek(CMTime.Zero);
                this.player.Play();
                this.analysisSink?.OnDiscontinuity(TimeSpan.Zero);
                return;
            }

            this.SetState(MediaElementState.Paused);
            this.MediaEnded?.Invoke(this, EventArgs.Empty);
        });

    void UpdateVideoSize()
    {
        if (this.item is null)
            return;

        var size = this.item.PresentationSize;
        var next = new Size(size.Width, size.Height);

        // The presentationSize KVO fires for reasons other than a change — and once per rendition switch on
        // an adaptive stream, which is usually the same size again. Only a real change is worth waking the
        // control (and anything it has re-laid out) for.
        if (next == this.VideoSize)
            return;

        this.VideoSize = next;
        this.VideoSizeChanged?.Invoke(this, EventArgs.Empty);
    }

    void UpdateBuffered()
    {
        if (this.item is null || this.Duration <= TimeSpan.Zero)
        {
            this.BufferedProgress = 0;
            return;
        }

        var ranges = this.item.LoadedTimeRanges;
        if (ranges.Length == 0)
        {
            this.BufferedProgress = 0;
            return;
        }

        var last = ranges[^1].CMTimeRangeValue;
        var end = last.Start.Seconds + last.Duration.Seconds;
        if (Double.IsNaN(end))
        {
            this.BufferedProgress = 0;
            return;
        }

        this.BufferedProgress = Math.Clamp(end / this.Duration.TotalSeconds, 0d, 1d);
    }


    // ── transport ────────────────────────────────────────────────────────────────────────────────

    public void Play()
    {
        this.player.Play();

        // Assigning Rate is what actually applies a non-1× speed; Play() alone always resumes at 1×.
        if (Math.Abs(this.rate - 1d) > Double.Epsilon)
            this.player.Rate = (float)this.rate;

        this.PublishNowPlaying();
    }

    public void Pause()
    {
        this.player.Pause();
        this.PublishNowPlaying();
    }

    public void Stop()
    {
        this.player.Pause();
        this.player.Seek(CMTime.Zero);
        this.analysisSink?.OnDiscontinuity(TimeSpan.Zero);
        this.SetState(MediaElementState.Stopped);
        this.PublishNowPlaying();
    }

    public void Seek(TimeSpan position)
    {
        // Zero tolerance so the frame lands where the scrubber says it will; the default tolerance snaps
        // to the nearest keyframe, which on a sparse-keyframe stream can be seconds away.
        var time = CMTime.FromSeconds(position.TotalSeconds, 600);
        this.player.Seek(time, CMTime.Zero, CMTime.Zero);
        this.analysisSink?.OnDiscontinuity(position);
        this.PublishNowPlaying();
    }

    public void SetVolume(double volume) => this.player.Volume = (float)Math.Clamp(volume, 0d, 1d);

    public void SetMuted(bool muted) => this.player.Muted = muted;

    public void SetRate(double rate)
    {
        this.rate = rate;

        // Only push it through while running — assigning Rate to a paused player starts it.
        if (this.player.Rate > 0)
            this.player.Rate = (float)rate;
    }

    public void SetLooping(bool looping) => this.looping = looping;

    public void SetAspect(MediaAspect aspect)
    {
        this.aspect = aspect;
        this.ApplyGravity();
    }

    void ApplyGravity()
    {
        if (this.output is null)
            return;

        this.output.PlayerLayer.VideoGravity = this.aspect switch
        {
            MediaAspect.AspectFill => AVLayerVideoGravity.ResizeAspectFill,
            MediaAspect.Fill => AVLayerVideoGravity.Resize,
            _ => AVLayerVideoGravity.ResizeAspect
        };
    }

    public void SetKeepScreenOn(bool keepOn)
    {
        if (this.keepScreenOn == keepOn)
            return;

        this.keepScreenOn = keepOn;

#if IOS || MACCATALYST
        UIApplication.SharedApplication.IdleTimerDisabled = keepOn;
#elif MACOS
        if (keepOn)
        {
            this.idleActivity ??= NSProcessInfo.ProcessInfo.BeginActivity(
                NSActivityOptions.IdleDisplaySleepDisabled, "Shiny media playback");
        }
        else if (this.idleActivity is not null)
        {
            NSProcessInfo.ProcessInfo.EndActivity(this.idleActivity);
            this.idleActivity = null;
        }
#endif
    }


    // ── background playback ──────────────────────────────────────────────────────────────────────

    public void SetBackgroundPlayback(bool enabled, MediaMetadata? metadata)
    {
        this.backgroundEnabled = enabled;
        this.metadata = metadata;

        if (enabled)
            this.nowPlaying.Enable(metadata, this.Duration, this.Position, this.player.Rate);
        else
            this.nowPlaying.Disable();
    }

    void PublishNowPlaying()
    {
        if (this.backgroundEnabled)
            this.nowPlaying.Update(this.metadata, this.Duration, this.Position, this.player.Rate);
    }

    void OnEnterBackground()
    {
        if (!this.backgroundEnabled || this.output is null)
            return;

        this.output.PlayerLayer.Player = null;
        this.isDetachedForBackground = true;
    }

    void OnEnterForeground()
    {
        if (!this.isDetachedForBackground || this.output is null)
            return;

        this.output.PlayerLayer.Player = this.player;
        this.isDetachedForBackground = false;
        this.ApplyGravity();
    }

    void HandleRemoteCommand(MediaRemoteCommand command)
        => this.OnMain(() =>
        {
            switch (command)
            {
                case MediaRemoteCommand.Play:
                    this.Play();
                    break;
                case MediaRemoteCommand.Pause:
                    this.Pause();
                    break;
                case MediaRemoteCommand.Stop:
                    this.Stop();
                    break;
                case MediaRemoteCommand.TogglePlayPause:
                    if (this.player.Rate > 0)
                        this.Pause();
                    else
                        this.Play();
                    break;
            }

            this.RemoteCommandReceived?.Invoke(this, command);
        });


    // ── picture-in-picture ───────────────────────────────────────────────────────────────────────

#if IOS || MACCATALYST
    void SetupPip(AVPlayerLayer layer)
    {
        if (!AVPictureInPictureController.IsPictureInPictureSupported)
            return;

        this.TeardownPip();

        this.pipDelegate = new ApplePipDelegate(active => this.OnMain(() =>
        {
            this.IsPictureInPictureActive = active;
            this.PictureInPictureChanged?.Invoke(this, active);
        }));

        this.pipController = new AVPictureInPictureController(layer) { Delegate = this.pipDelegate };
    }

    void TeardownPip()
    {
        this.pipController?.Dispose();
        this.pipController = null;
        this.pipDelegate?.Dispose();
        this.pipDelegate = null;
    }
#endif

    public Task<bool> TryEnterPictureInPictureAsync()
    {
#if IOS || MACCATALYST
        if (this.pipController is { PictureInPicturePossible: true })
        {
            this.pipController.StartPictureInPicture();
            return Task.FromResult(true);
        }
#endif
        return Task.FromResult(false);
    }

    public Task ExitPictureInPictureAsync()
    {
#if IOS || MACCATALYST
        if (this.pipController is { PictureInPictureActive: true })
            this.pipController.StopPictureInPicture();
#endif
        return Task.CompletedTask;
    }


    // ── state ────────────────────────────────────────────────────────────────────────────────────

    void SyncState()
    {
        if (this.State == MediaElementState.Failed)
            return;

        var next = this.player.TimeControlStatus switch
        {
            AVPlayerTimeControlStatus.Playing => MediaElementState.Playing,
            AVPlayerTimeControlStatus.WaitingToPlayAtSpecifiedRate => MediaElementState.Buffering,
            _ => this.State == MediaElementState.Stopped ? MediaElementState.Stopped : MediaElementState.Paused
        };

        // A player with no item is idle, not paused.
        if (this.item is null)
            next = MediaElementState.None;

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

    static TimeSpan ToTimeSpan(CMTime time)
    {
        var seconds = time.Seconds;
        return Double.IsNaN(seconds) || Double.IsInfinity(seconds)
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds(seconds);
    }

    // KVO and notification callbacks are not guaranteed to arrive on the main thread, and everything they
    // touch ends in a MAUI property write.
    void OnMain(Action action)
    {
        if (NSThread.IsMain)
            action();
        else
            DispatchQueue.MainQueue.DispatchAsync(action);
    }


    // ── teardown ─────────────────────────────────────────────────────────────────────────────────

    void TeardownItem()
    {
        this.TeardownAnalysis();
        this.statusObserver?.Dispose();
        this.statusObserver = null;
        this.loadedRangesObserver?.Dispose();
        this.loadedRangesObserver = null;
        this.presentationSizeObserver?.Dispose();
        this.presentationSizeObserver = null;

        if (this.endObserver is not null)
        {
            NSNotificationCenter.DefaultCenter.RemoveObserver(this.endObserver);
            this.endObserver = null;
        }

        this.item = null;
    }

    public void Dispose()
    {
        if (this.disposed)
            return;

        this.disposed = true;

        this.SetKeepScreenOn(false);
        this.nowPlaying.Dispose();
        this.TeardownItem();

#if IOS || MACCATALYST
        this.TeardownPip();
#endif
        this.timeControlObserver?.Dispose();
        this.timeControlObserver = null;

        if (this.backgroundObserver is not null)
        {
            NSNotificationCenter.DefaultCenter.RemoveObserver(this.backgroundObserver);
            this.backgroundObserver = null;
        }

        if (this.foregroundObserver is not null)
        {
            NSNotificationCenter.DefaultCenter.RemoveObserver(this.foregroundObserver);
            this.foregroundObserver = null;
        }

        if (this.output is not null)
        {
            this.output.PlayerLayer.Player = null;
            this.output = null;
        }

        this.player.Pause();
        this.player.ReplaceCurrentItemWithPlayerItem(null);
        this.player.Dispose();
    }
}


#if IOS || MACCATALYST
class ApplePipDelegate : AVPictureInPictureControllerDelegate
{
    readonly Action<bool> onChanged;

    public ApplePipDelegate(Action<bool> onChanged) => this.onChanged = onChanged;

    public override void DidStartPictureInPicture(AVPictureInPictureController controller)
        => this.onChanged(true);

    public override void DidStopPictureInPicture(AVPictureInPictureController controller)
        => this.onChanged(false);
}
#endif
