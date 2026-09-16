using Microsoft.Maui;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Graphics;
using Shiny.Controls.Media;
using Shiny.Maui.Controls.Media;
using Xunit;

// MediaPlayerBackends.Factory is process-global — it has to be, because a MediaElement declared in XAML
// has no service provider. Tests that swap it therefore cannot run concurrently with each other.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Shiny.Maui.Controls.Media.Tests;

/// <summary>
/// Enough of a dispatcher to construct controls that queue work onto one. Dispatched work runs inline,
/// and timers are handed back so a test can tick them deliberately rather than race the clock.
/// </summary>
sealed class TestDispatcher : IDispatcher
{
    public List<TestTimer> Timers { get; } = new();

    public bool IsDispatchRequired => false;

    public bool Dispatch(Action action)
    {
        action();
        return true;
    }

    public bool DispatchDelayed(TimeSpan delay, Action action)
    {
        action();
        return true;
    }

    public IDispatcherTimer CreateTimer()
    {
        var timer = new TestTimer();
        this.Timers.Add(timer);
        return timer;
    }
}


sealed class TestTimer : IDispatcherTimer
{
    public TimeSpan Interval { get; set; }
    public bool IsRepeating { get; set; }
    public bool IsRunning { get; private set; }

    public event EventHandler? Tick;

    public void Start() => this.IsRunning = true;

    public void Stop() => this.IsRunning = false;

    /// <summary>Fire the timer as the real dispatcher would, but only when it's actually running.</summary>
    public void Fire()
    {
        if (this.IsRunning)
            this.Tick?.Invoke(this, EventArgs.Empty);
    }
}


sealed class TestDispatcherProvider : IDispatcherProvider
{
    public static readonly TestDispatcher Dispatcher = new();

    public IDispatcher? GetForCurrentThread() => Dispatcher;

    /// <summary>Idempotent — xUnit runs every class in this assembly in the one process.</summary>
    public static void Install() => DispatcherProvider.SetCurrent(new TestDispatcherProvider());
}


/// <summary>A recording stand-in for a platform player, so the control's own logic can be tested off-device.</summary>
sealed class FakeMediaPlayerBackend : IMediaPlayerBackend
{
    public List<string> Calls { get; } = new();

    public MediaPlaybackCapabilities Capabilities { get; set; } =
        MediaPlaybackCapabilities.BackgroundAudio
        | MediaPlaybackCapabilities.PlaybackRate
        | MediaPlaybackCapabilities.Volume
        | MediaPlaybackCapabilities.BufferProgress;

    public MediaElementState State { get; private set; } = MediaElementState.None;
    public TimeSpan Position { get; set; }
    public TimeSpan Duration { get; set; }
    public double BufferedProgress { get; set; }
    public Size VideoSize { get; set; }
    public bool IsPictureInPictureActive { get; private set; }

    public MediaSource? OpenedSource { get; private set; }
    public int OpenCount { get; private set; }
    public object? Output { get; private set; }
    public double Volume { get; private set; } = 1d;
    public bool Muted { get; private set; }
    public double Rate { get; private set; } = 1d;
    public bool Looping { get; private set; }
    public MediaAspect Aspect { get; private set; } = MediaAspect.AspectFit;
    public bool KeepScreenOn { get; private set; }
    public bool BackgroundEnabled { get; private set; }
    public MediaMetadata? Metadata { get; private set; }
    public bool Disposed { get; private set; }
    public bool PictureInPictureSucceeds { get; set; }

    public event EventHandler<MediaElementState>? StateChanged;
    public event EventHandler? MediaOpened;
    public event EventHandler? VideoSizeChanged;
    public event EventHandler? MediaEnded;
    public event EventHandler<MediaFailure>? Failed;
    public event EventHandler<bool>? PictureInPictureChanged;
    public event EventHandler<MediaRemoteCommand>? RemoteCommandReceived;

    public void SetOutput(object? nativeView)
    {
        this.Output = nativeView;
        this.Calls.Add($"SetOutput({(nativeView is null ? "null" : "view")})");
    }

    public Task OpenAsync(MediaSource? source, CancellationToken ct = default)
    {
        this.OpenedSource = source;
        this.OpenCount++;
        this.Calls.Add($"Open({source})");
        return Task.CompletedTask;
    }

    public void Play()
    {
        this.Calls.Add("Play");
        this.RaiseState(MediaElementState.Playing);
    }

    public void Pause()
    {
        this.Calls.Add("Pause");
        this.RaiseState(MediaElementState.Paused);
    }

    public void Stop()
    {
        this.Calls.Add("Stop");
        this.Position = TimeSpan.Zero;
        this.RaiseState(MediaElementState.Stopped);
    }

    public void Seek(TimeSpan position)
    {
        this.Calls.Add($"Seek({position})");
        this.Position = position;
    }

    public void SetVolume(double volume)
    {
        this.Volume = volume;
        this.Calls.Add($"SetVolume({volume})");
    }

    public void SetMuted(bool muted)
    {
        this.Muted = muted;
        this.Calls.Add($"SetMuted({muted})");
    }

    public void SetRate(double rate)
    {
        this.Rate = rate;
        this.Calls.Add($"SetRate({rate})");
    }

    public void SetLooping(bool looping)
    {
        this.Looping = looping;
        this.Calls.Add($"SetLooping({looping})");
    }

    public void SetAspect(MediaAspect aspect)
    {
        this.Aspect = aspect;
        this.Calls.Add($"SetAspect({aspect})");
    }

    public void SetKeepScreenOn(bool keepOn)
    {
        this.KeepScreenOn = keepOn;
        this.Calls.Add($"SetKeepScreenOn({keepOn})");
    }

    public void SetBackgroundPlayback(bool enabled, MediaMetadata? metadata)
    {
        this.BackgroundEnabled = enabled;
        this.Metadata = metadata;
        this.Calls.Add($"SetBackgroundPlayback({enabled},{metadata?.Title ?? "null"})");
    }

    public Task<bool> TryEnterPictureInPictureAsync()
    {
        this.Calls.Add("EnterPip");

        if (this.PictureInPictureSucceeds)
        {
            this.IsPictureInPictureActive = true;
            this.PictureInPictureChanged?.Invoke(this, true);
        }

        return Task.FromResult(this.PictureInPictureSucceeds);
    }

    public Task ExitPictureInPictureAsync()
    {
        this.Calls.Add("ExitPip");
        this.IsPictureInPictureActive = false;
        this.PictureInPictureChanged?.Invoke(this, false);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        this.Disposed = true;
        this.Calls.Add("Dispose");
    }


    // ── test drivers ─────────────────────────────────────────────────────────────────────────────

    public void RaiseState(MediaElementState state)
    {
        if (this.State == state)
            return;

        this.State = state;
        this.StateChanged?.Invoke(this, state);
    }

    public void RaiseOpened(TimeSpan duration)
    {
        this.Duration = duration;
        this.MediaOpened?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Report a video size the way ExoPlayer does — on its own, with no open behind it.
    /// </summary>
    public void RaiseVideoSize(Size size)
    {
        this.VideoSize = size;
        this.VideoSizeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RaiseEnded() => this.MediaEnded?.Invoke(this, EventArgs.Empty);

    public void RaiseFailed(string message)
        => this.Failed?.Invoke(this, new MediaFailure(message));

    public void RaiseRemoteCommand(MediaRemoteCommand command)
        => this.RemoteCommandReceived?.Invoke(this, command);
}


/// <summary>
/// The least a view needs to count as "has a handler". A <see cref="MediaElement"/> creates its player when a
/// handler connects and releases it when the handler goes away, so tests that drive the player attach one.
/// </summary>
sealed class StubViewHandler : IViewHandler
{
    public IElement? VirtualView { get; private set; }
    public object? PlatformView => null;
    public IMauiContext? MauiContext => null;
    public bool HasContainer { get; set; }
    public object? ContainerView => null;
    IView? IViewHandler.VirtualView => this.VirtualView as IView;

    public void SetMauiContext(IMauiContext mauiContext) { }
    public void SetVirtualView(IElement view) => this.VirtualView = view;
    public void UpdateValue(string property) { }
    public void Invoke(string command, object? args = null) { }
    public void DisconnectHandler() => this.VirtualView = null;
    public Size GetDesiredSize(double widthConstraint, double heightConstraint) => Size.Zero;
    public void PlatformArrange(Rect frame) { }
}


static class MediaElementTestExtensions
{
    /// <summary>Give the element a handler, as showing it on a page would. Returns it for chaining.</summary>
    public static MediaElement Connected(this MediaElement element)
    {
        element.Handler = new StubViewHandler();
        return element;
    }

    /// <summary>Take the handler away, as removing the element from the page would.</summary>
    public static void Disconnect(this MediaElement element) => element.Handler = null;
}


/// <summary>Installs the test dispatcher and points the control's backend factory at a fake.</summary>
public abstract class MediaTestBase : IDisposable
{
    internal FakeMediaPlayerBackend Backend { get; } = new();

    protected MediaTestBase()
    {
        TestDispatcherProvider.Install();
        TestDispatcherProvider.Dispatcher.Timers.Clear();
        MediaPlayerBackends.Factory = () => this.Backend;
    }

    internal static TestTimer? TimerWithInterval(TimeSpan interval)
        => TestDispatcherProvider.Dispatcher.Timers.FirstOrDefault(t => t.Interval == interval);

    public void Dispose() => MediaPlayerBackends.Factory = null;
}
