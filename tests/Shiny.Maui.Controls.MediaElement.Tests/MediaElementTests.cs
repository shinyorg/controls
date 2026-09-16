using Microsoft.Maui.Graphics;
using Shiny.Controls.Media;
using Shiny.Maui.Controls.Media;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Media.Tests;

public class MediaElementTests : MediaTestBase
{
    [Fact]
    public void Transport_calls_reach_the_backend()
    {
        var element = new MediaElement().Connected();

        element.Play();
        element.Pause();
        element.Stop();

        this.Backend.Calls.ShouldContain("Play");
        this.Backend.Calls.ShouldContain("Pause");
        this.Backend.Calls.ShouldContain("Stop");
    }

    [Fact]
    public async Task Seek_clamps_to_the_media_length()
    {
        var element = new MediaElement().Connected();
        this.Backend.RaiseOpened(TimeSpan.FromMinutes(2));

        await element.SeekAsync(TimeSpan.FromMinutes(10));

        this.Backend.Calls.ShouldContain($"Seek({TimeSpan.FromMinutes(2)})");
    }

    [Fact]
    public async Task Seek_clamps_negative_positions_to_zero()
    {
        var element = new MediaElement().Connected();
        this.Backend.RaiseOpened(TimeSpan.FromMinutes(2));

        await element.SeekAsync(TimeSpan.FromSeconds(-5));

        this.Backend.Calls.ShouldContain($"Seek({TimeSpan.Zero})");
    }

    [Fact]
    public void Assigning_Position_from_outside_seeks()
    {
        var element = new MediaElement().Connected();
        this.Backend.RaiseOpened(TimeSpan.FromMinutes(5));

        element.Position = TimeSpan.FromSeconds(42);

        this.Backend.Calls.ShouldContain($"Seek({TimeSpan.FromSeconds(42)})");
    }

    [Fact]
    public void The_position_tick_does_not_seek_back_to_where_the_player_already_is()
    {
        // The timer writes the player's own position into the Position property. If that round-tripped
        // into a seek, remote streams would re-buffer four times a second.
        var element = new MediaElement().Connected();
        this.Backend.RaiseOpened(TimeSpan.FromMinutes(5));
        this.Backend.RaiseState(MediaElementState.Playing);

        this.Backend.Position = TimeSpan.FromSeconds(3);
        var timer = TimerWithInterval(element.PositionUpdateInterval);
        timer.ShouldNotBeNull();
        timer.Fire();

        element.Position.ShouldBe(TimeSpan.FromSeconds(3));
        this.Backend.Calls.ShouldNotContain($"Seek({TimeSpan.FromSeconds(3)})");
    }

    [Fact]
    public void The_position_timer_only_runs_while_playing()
    {
        // An element that never plays must not leave a dispatcher timer ticking. (Connecting a handler sizes
        // the timer up front, so "not running" rather than "not created" is the invariant.)
        var element = new MediaElement().Connected();
        (TimerWithInterval(element.PositionUpdateInterval)?.IsRunning ?? false).ShouldBeFalse();

        this.Backend.RaiseState(MediaElementState.Playing);
        var timer = TimerWithInterval(element.PositionUpdateInterval);
        timer.ShouldNotBeNull();
        timer.IsRunning.ShouldBeTrue();

        this.Backend.RaiseState(MediaElementState.Paused);
        timer.IsRunning.ShouldBeFalse();
    }

    [Fact]
    public void Backend_state_flows_into_CurrentState_and_the_event()
    {
        var element = new MediaElement().Connected();
        var observed = new List<MediaElementState>();
        element.StateChanged += (_, state) => observed.Add(state);

        this.Backend.RaiseState(MediaElementState.Buffering);
        this.Backend.RaiseState(MediaElementState.Playing);

        element.CurrentState.ShouldBe(MediaElementState.Playing);
        observed.ShouldBe([MediaElementState.Buffering, MediaElementState.Playing]);
    }

    [Fact]
    public void Opening_publishes_the_duration_and_capabilities()
    {
        var element = new MediaElement().Connected();
        var opened = false;
        element.MediaOpened += (_, _) => opened = true;

        this.Backend.RaiseOpened(TimeSpan.FromSeconds(90));

        opened.ShouldBeTrue();
        element.Duration.ShouldBe(TimeSpan.FromSeconds(90));
        element.Capabilities.ShouldBe(this.Backend.Capabilities);
    }

    [Fact]
    public void Opening_publishes_the_video_size_the_backend_already_knows()
    {
        // Windows, GTK and AVPlayer all have the size by the time they say the media is open.
        var element = new MediaElement().Connected();
        this.Backend.VideoSize = new Size(1920, 1080);

        this.Backend.RaiseOpened(TimeSpan.FromSeconds(90));

        element.VideoSize.ShouldBe(new Size(1920, 1080));
    }

    [Fact]
    public void A_video_size_arriving_after_the_open_is_still_published()
    {
        // ExoPlayer's onVideoSizeChanged routinely lands after the player reports ready, which is what
        // makes reading the size once inside MediaOpened wrong on Android.
        var element = new MediaElement().Connected();
        Size? reported = null;
        element.VideoSizeChanged += (_, size) => reported = size;

        this.Backend.RaiseOpened(TimeSpan.FromSeconds(90));
        element.VideoSize.ShouldBe(Size.Zero);

        this.Backend.RaiseVideoSize(new Size(1080, 1920));

        element.VideoSize.ShouldBe(new Size(1080, 1920));
        reported.ShouldBe(new Size(1080, 1920));
    }

    [Fact]
    public void The_video_size_is_reported_once_per_change()
    {
        var element = new MediaElement().Connected();
        var reports = 0;
        element.VideoSizeChanged += (_, _) => reports++;

        this.Backend.RaiseVideoSize(new Size(1280, 720));
        this.Backend.RaiseOpened(TimeSpan.FromSeconds(90));
        this.Backend.RaiseVideoSize(new Size(1280, 720));

        reports.ShouldBe(1);
    }

    [Fact]
    public async Task A_new_source_clears_the_video_size()
    {
        var element = new MediaElement().Connected();
        this.Backend.RaiseVideoSize(new Size(1280, 720));

        element.Source = MediaSource.FromUri("https://example.com/next.mp4");
        await Task.Yield();

        element.VideoSize.ShouldBe(Size.Zero);
    }

    [Fact]
    public void A_backend_failure_surfaces_as_Failed_state_and_the_event()
    {
        var element = new MediaElement().Connected();
        MediaFailure? failure = null;
        element.MediaFailed += (_, f) => failure = f;

        this.Backend.RaiseFailed("boom");

        failure!.Message.ShouldBe("boom");
        element.CurrentState.ShouldBe(MediaElementState.Failed);
    }

    [Fact]
    public void Setting_a_source_opens_it_on_the_backend()
    {
        var element = new MediaElement
        {
            Source = MediaSource.FromUri("https://example.com/clip.mp4")
        }.Connected();

        this.Backend.OpenCount.ShouldBe(1);
        this.Backend.OpenedSource.ShouldBeOfType<UriMediaSource>();
    }

    [Fact]
    public void AutoPlay_starts_playback_once_the_source_opens()
    {
        var element = new MediaElement { AutoPlay = true }.Connected();
        element.Source = MediaSource.FromUri("https://example.com/clip.mp4");

        this.Backend.Calls.ShouldContain("Play");
    }

    [Fact]
    public void Without_AutoPlay_a_new_source_stays_paused()
    {
        var element = new MediaElement().Connected();
        element.Source = MediaSource.FromUri("https://example.com/clip.mp4");

        this.Backend.Calls.ShouldNotContain("Play");
    }

    [Fact]
    public void Playback_settings_are_forwarded_as_they_change()
    {
        var element = new MediaElement().Connected();
        element.Volume = 0.4;
        element.IsMuted = true;
        element.PlaybackRate = 1.5;
        element.IsLooping = true;
        element.Aspect = MediaAspect.AspectFill;
        element.KeepScreenOn = true;

        this.Backend.Volume.ShouldBe(0.4);
        this.Backend.Muted.ShouldBeTrue();
        this.Backend.Rate.ShouldBe(1.5);
        this.Backend.Looping.ShouldBeTrue();
        this.Backend.Aspect.ShouldBe(MediaAspect.AspectFill);
        this.Backend.KeepScreenOn.ShouldBeTrue();
    }

    [Fact]
    public void Playback_settings_made_before_the_handler_connects_reach_the_player()
    {
        // XAML sets every property long before there is a handler — and so before there is a player.
        var element = new MediaElement
        {
            Volume = 0.4,
            IsMuted = true,
            PlaybackRate = 1.5,
            IsLooping = true,
            Aspect = MediaAspect.AspectFill,
            KeepScreenOn = true
        }.Connected();

        this.Backend.Volume.ShouldBe(0.4);
        this.Backend.Muted.ShouldBeTrue();
        this.Backend.Rate.ShouldBe(1.5);
        this.Backend.Looping.ShouldBeTrue();
        this.Backend.Aspect.ShouldBe(MediaAspect.AspectFill);
        this.Backend.KeepScreenOn.ShouldBeTrue();
    }

    [Theory]
    [InlineData(-0.5, 0d)]
    [InlineData(1.7, 1d)]
    public void Volume_is_clamped_to_zero_through_one(double assigned, double expected)
    {
        var element = new MediaElement { Volume = assigned };
        element.Volume.ShouldBe(expected);
    }

    [Theory]
    [InlineData(0.1, 0.25)]
    [InlineData(9d, 4d)]
    public void PlaybackRate_is_clamped_to_a_playable_range(double assigned, double expected)
    {
        var element = new MediaElement { PlaybackRate = assigned };
        element.PlaybackRate.ShouldBe(expected);
    }

    [Fact]
    public void ToggleMute_flips_the_property_and_the_backend()
    {
        var element = new MediaElement().Connected();

        element.ToggleMute();
        element.IsMuted.ShouldBeTrue();
        this.Backend.Muted.ShouldBeTrue();

        element.ToggleMute();
        element.IsMuted.ShouldBeFalse();
        this.Backend.Muted.ShouldBeFalse();
    }

    [Fact]
    public void Background_playback_publishes_the_metadata()
    {
        var metadata = new MediaMetadata { Title = "Episode 1" };
        var element = new MediaElement
        {
            EnableBackgroundPlayback = true,
            Metadata = metadata
        }.Connected();

        this.Backend.BackgroundEnabled.ShouldBeTrue();
        this.Backend.Metadata.ShouldBe(metadata);
    }

    [Fact]
    public async Task Picture_in_picture_reports_the_platform_verdict()
    {
        var element = new MediaElement().Connected();

        (await element.TryEnterPictureInPictureAsync()).ShouldBeFalse();

        this.Backend.PictureInPictureSucceeds = true;
        (await element.TryEnterPictureInPictureAsync()).ShouldBeTrue();
        element.IsPictureInPictureActive.ShouldBeTrue();
    }

    [Fact]
    public void MediaEnded_is_forwarded()
    {
        var element = new MediaElement().Connected();
        var ended = false;
        element.MediaEnded += (_, _) => ended = true;

        this.Backend.RaiseEnded();

        ended.ShouldBeTrue();
    }

    [Fact]
    public void A_remote_transport_command_refreshes_the_position()
    {
        // The OS transport UI drives the player directly; the control only has to re-read where it landed.
        var element = new MediaElement().Connected();
        this.Backend.RaiseOpened(TimeSpan.FromMinutes(3));
        this.Backend.Position = TimeSpan.FromSeconds(75);

        this.Backend.RaiseRemoteCommand(MediaRemoteCommand.Seek);

        element.Position.ShouldBe(TimeSpan.FromSeconds(75));
    }

    [Fact]
    public void A_fullscreen_mirror_starts_from_the_running_player_not_from_defaults()
    {
        // Regression: the fullscreen page used to open reading 0:00 / 0:00 with an empty scrubber over a
        // video 30 seconds in, because the mirror started from property defaults and the next position
        // tick never comes while playback is paused.
        var owner = new MediaElement().Connected();
        this.Backend.RaiseOpened(TimeSpan.FromMinutes(2));
        this.Backend.RaiseState(MediaElementState.Paused);
        this.Backend.Position = TimeSpan.FromSeconds(30);
        this.Backend.BufferedProgress = 0.5;

        var mirror = new MediaElement(owner);

        mirror.Duration.ShouldBe(TimeSpan.FromMinutes(2));
        mirror.Position.ShouldBe(TimeSpan.FromSeconds(30));
        mirror.BufferedProgress.ShouldBe(0.5);
        mirror.CurrentState.ShouldBe(MediaElementState.Paused);
        mirror.IsFullScreen.ShouldBeTrue();
    }

    [Fact]
    public void A_fullscreen_mirror_adopts_the_owners_transport_configuration()
    {
        var owner = new MediaElement
        {
            ShowVolumeControl = false,
            ShowTimeLabels = false,
            AutoHideTransportBar = false,
            SeekBarColor = Colors.Red
        };

        var mirror = new MediaElement(owner);

        mirror.ShowVolumeControl.ShouldBeFalse();
        mirror.ShowTimeLabels.ShouldBeFalse();
        mirror.AutoHideTransportBar.ShouldBeFalse();
        mirror.SeekBarColor.ShouldBe(Colors.Red);
    }

    [Fact]
    public void The_control_has_no_backend_when_none_is_registered()
    {
        MediaPlayerBackends.Factory = null;
        MediaPlayerBackends.IsSupported.ShouldBeFalse();

        // An unsupported host must still lay the page out rather than throw from the constructor.
        var element = new MediaElement { Source = MediaSource.FromUri("https://example.com/clip.mp4") }.Connected();

        Should.NotThrow(() => element.Play());
        Should.NotThrow(() => element.Stop());
        element.CurrentState.ShouldBe(MediaElementState.None);
    }


    // ── player lifecycle: created on connect, never in the constructor ──────────────────────────

    [Fact]
    public void An_element_without_a_handler_creates_no_player()
    {
        var created = 0;
        MediaPlayerBackends.Factory = () => { created++; return this.Backend; };

        var element = new MediaElement
        {
            Source = MediaSource.FromUri("https://example.com/clip.mp4"),
            AutoPlay = true,
            Volume = 0.3
        };
        element.Play();

        created.ShouldBe(0);
        this.Backend.OpenCount.ShouldBe(0);   // and nothing is buffering behind an element nobody can see
        this.Backend.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void An_element_that_never_gets_a_handler_is_collectable()
    {
        // The Android backend subscribes to a static Picture-in-Picture event (and Apple's to
        // NSNotificationCenter.DefaultCenter) as soon as it exists. Created in the constructor, that root held
        // the backend, whose events held the element — forever, because only a handler disconnect disposed it.
        MediaPlayerBackends.Factory = () => new StaticallyRootedBackend();

        var weak = CreateUnshownElement();
        for (var i = 0; i < 3 && weak.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        weak.IsAlive.ShouldBeFalse();
        StaticallyRootedBackend.Live.ShouldBe(0);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static WeakReference CreateUnshownElement()
        => new(new MediaElement { Source = MediaSource.FromUri("https://example.com/clip.mp4") });

    [Fact]
    public void Connecting_creates_the_player_opens_the_source_and_honors_an_earlier_Play()
    {
        var element = new MediaElement { Source = MediaSource.FromUri("https://example.com/clip.mp4") };
        element.Play();                       // e.g. from a page constructor, before the page is shown

        element.Connected();

        this.Backend.OpenCount.ShouldBe(1);
        this.Backend.Calls.ShouldContain("Play");
        element.Capabilities.ShouldBe(this.Backend.Capabilities);
    }

    [Fact]
    public void A_Pause_before_connecting_cancels_an_earlier_Play()
    {
        var element = new MediaElement();
        element.Play();
        element.Pause();

        element.Connected();

        this.Backend.Calls.ShouldNotContain("Play");
    }

    [Fact]
    public void Disconnecting_disposes_the_player_and_reconnecting_creates_a_new_one()
    {
        var backends = new List<FakeMediaPlayerBackend>();
        MediaPlayerBackends.Factory = () =>
        {
            var b = new FakeMediaPlayerBackend();
            backends.Add(b);
            return b;
        };

        var element = new MediaElement { Source = MediaSource.FromUri("https://example.com/clip.mp4") }.Connected();
        backends.Count.ShouldBe(1);

        element.Disconnect();
        backends[0].Disposed.ShouldBeTrue();

        // re-shown (e.g. moved to another page): used to stay inert forever with a null player
        element.Connected();
        backends.Count.ShouldBe(2);
        backends[1].OpenCount.ShouldBe(1);
    }


    /// <summary>A backend that, like the Android one, hooks a process-wide event the moment it exists.</summary>
#pragma warning disable CS0067 // the events are part of the contract; this backend never raises them
    sealed class StaticallyRootedBackend : IMediaPlayerBackend
    {
        static event EventHandler<bool>? ProcessWide;
        public static int Live;

        readonly FakeMediaPlayerBackend inner = new();

        public StaticallyRootedBackend()
        {
            ProcessWide += this.OnProcessWide;
            Interlocked.Increment(ref Live);
        }

        void OnProcessWide(object? sender, bool e) { }

        public MediaPlaybackCapabilities Capabilities => this.inner.Capabilities;
        public MediaElementState State => this.inner.State;
        public TimeSpan Position => this.inner.Position;
        public TimeSpan Duration => this.inner.Duration;
        public double BufferedProgress => this.inner.BufferedProgress;
        public Size VideoSize => this.inner.VideoSize;
        public bool IsPictureInPictureActive => false;
        public event EventHandler<MediaElementState>? StateChanged;
        public event EventHandler? MediaOpened;
        public event EventHandler? MediaEnded;
        public event EventHandler<MediaFailure>? Failed;
        public event EventHandler<bool>? PictureInPictureChanged;
        public event EventHandler<MediaRemoteCommand>? RemoteCommandReceived;
        public void SetOutput(object? nativeView) { }
        public Task OpenAsync(MediaSource? source, CancellationToken ct = default) => Task.CompletedTask;
        public void Play() { }
        public void Pause() { }
        public void Stop() { }
        public void Seek(TimeSpan position) { }
        public void SetVolume(double volume) { }
        public void SetMuted(bool muted) { }
        public void SetRate(double rate) { }
        public void SetLooping(bool looping) { }
        public void SetAspect(MediaAspect aspect) { }
        public void SetKeepScreenOn(bool keepOn) { }
        public void SetBackgroundPlayback(bool enabled, MediaMetadata? metadata) { }
        public Task<bool> TryEnterPictureInPictureAsync() => Task.FromResult(false);
        public Task ExitPictureInPictureAsync() => Task.CompletedTask;

        public void Dispose()
        {
            ProcessWide -= this.OnProcessWide;
            Interlocked.Decrement(ref Live);
        }
    }
#pragma warning restore CS0067
}
