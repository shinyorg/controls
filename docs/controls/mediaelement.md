# MediaElement

[← All Shiny Controls](../../README.md)

> Separate packages: `Shiny.Maui.Controls.MediaElement` (+ `.Linux` for GTK4) and `Shiny.Blazor.Controls.MediaElement`.

Plays local and remote **audio and video** on iOS, Android, Windows, macOS AppKit, Linux GTK4 and Blazor, behind one API. Backed by AVPlayer (Apple), Media3/ExoPlayer (Android), `Windows.Media.Playback` (Windows), GtkMediaFile (Linux) and HTML5 media (Blazor).

```xml
<media:MediaElement Source="https://example.com/clip.mp4"
                    AutoPlay="True"
                    Aspect="AspectFit"
                    ShowVolumeControl="False"
                    EnableBackgroundPlayback="True" />
```

**The transport bar is drawn by Shiny, not the platform.** That is the whole reason each piece toggles on its own: native transport UI is all-or-nothing everywhere except Windows (iOS `AVPlayerViewController` has a single `showsPlaybackControls`, HTML5's `controlsList` only subtracts download/fullscreen/cast, and GTK's `GtkMediaControls` has no knobs at all). Drawing it also means one look across all six targets, themed from your Shiny theme pack — the scrubber picks up `Shiny.Color.Primary` unless you set `SeekBarColor`. Toggle `ShowTransportBar`, `ShowPlayPauseButton`, `ShowSeekBar`, `ShowVolumeControl`, `ShowFullScreenButton`, `ShowTimeLabels` and `ShowPictureInPictureButton` independently; `AutoHideTransportBar` fades the bar after `TransportBarAutoHideDelay` **while playing only**, so a paused frame and an audio-only track keep their controls reachable. A faded bar keeps its place rather than being torn down, and a click on the picture itself toggles playback — so the click you aim at play/pause pauses on the first press whether or not the bar happened to fade out from under the pointer first.

**Commands on MAUI, methods on Blazor.** `PlayCommand`, `PauseCommand`, `StopCommand`, `TogglePlayPauseCommand`, `SeekCommand`, `MuteCommand`, `ToggleFullScreenCommand`, `PictureInPictureCommand` (whose `CanExecute` is false where the platform can't), plus `Play()`, `Pause()`, `Stop()`, `SeekAsync()`, `ToggleMute()`. `SeekCommand` takes a `TimeSpan`, a number of seconds, or a string of either — `CommandParameter="30"` is thirty *seconds*, and `"00:01:30"` is ninety.

**The player outlives the view.** An `IMediaPlayerBackend` owns the platform player and the view is pushed into it, which is what makes the two hard parts work: entering fullscreen hands the same player to a second surface on a modal page (no re-buffering, and your layout is left alone), and backgrounding detaches the video surface entirely while audio keeps running. It is also the extension point — assign `MediaPlayerBackends.Factory` to substitute a fake in tests or plug in your own player. The player is created when the element's handler connects — never in its constructor — and disposed when the handler disconnects, so a `MediaElement` that is built but never shown (a discarded view, an unrealized template) holds no player, no buffering source and no process-wide subscription, and is collectable like any other view; one that is removed and shown again gets a fresh player and re-opens its `Source`. Everything configured before that (`Source`, volume/mute/rate/looping/aspect, `KeepScreenOn`, background playback, and a `Play()` call) is applied on connect. `Capabilities` reads `None` until then.

**Background playback** (`EnableBackgroundPlayback` + `Metadata`) keeps audio going with the device locked and publishes now-playing information to the OS: `MPNowPlayingInfoCenter` + `MPRemoteCommandCenter` on Apple, a Media3 `MediaSession` behind a `mediaPlayback` foreground service on Android, SMTC on Windows, and `navigator.mediaSession` on Blazor. The library contributes the Android service and permissions to your merged manifest, but two opt-ins are the **app's** to make: iOS/Catalyst need the `audio` entry in `UIBackgroundModes`, and Android 13+ needs the `POST_NOTIFICATIONS` runtime grant for the notification to appear.

**Picture-in-Picture** (`TryEnterPictureInPictureAsync()`) is how video stays visible while backgrounded: `AVPictureInPictureController` on iOS/Catalyst, `EnterPictureInPictureMode` on Android 8+, and `requestPictureInPicture()` on Blazor. Android also needs `SupportsPictureInPicture = true` on your activity, and should forward `OnPictureInPictureModeChanged` to `AndroidMediaIntegration.NotifyPictureInPictureModeChanged` so the control learns when the user collapses the window.

**Ask before you offer.** Support genuinely differs, so `Capabilities` (a `MediaPlaybackCapabilities` flags enum: `BackgroundAudio`, `PictureInPicture`, `PlaybackRate`, `Volume`, `BufferProgress`) reports what the current backend will actually honour, and the transport bar hides what it can't. Windows has no per-element PiP API; GTK has no playback-rate control and no buffered-ahead figure; iOS Safari refuses programmatic volume, which the Blazor backend detects by writing a value and reading it back.

| Property | Type | Default | Notes |
| --- | --- | --- | --- |
| Source | MediaSource | null | URI, filesystem path, or a `Resources/Raw` file — a bare string in XAML is classified for you |
| AutoPlay | bool | false | Play as soon as the source opens |
| IsLooping | bool | false | Suppresses `MediaEnded` |
| Volume | double | 1 | Clamped 0..1 |
| IsMuted | bool | false | Independent of `Volume` |
| PlaybackRate | double | 1 | Clamped 0.25..4 |
| Position | TimeSpan | 0 | Two-way; read back every `PositionUpdateInterval`, assigning it seeks |
| Duration | TimeSpan | 0 | Read-only; zero until opened, and for live streams |
| CurrentState | MediaElementState | None | None / Opening / Buffering / Playing / Paused / Stopped / Failed |
| VideoSize | Size | 0 x 0 | Read-only; pixel size of the video track, zero for audio-only — bind it (or handle `VideoSizeChanged`) to size the player to what it's playing |
| BufferedProgress | double | 0 | 0..1, drawn as the scrubber's secondary track |
| Aspect | MediaAspect | AspectFit | AspectFit / AspectFill / Fill |
| KeepScreenOn | bool | false | Inhibits display sleep while playing |
| IsFullScreen | bool | false | Two-way; pushes/pops the fullscreen page |
| EnableBackgroundPlayback | bool | false | See the manifest opt-ins above |
| Metadata | MediaMetadata | null | Title / Artist / Album / ArtworkUri for the OS transport UI |
| Capabilities | MediaPlaybackCapabilities | None | Read-only; what this backend honours |

**Events:** `StateChanged`, `MediaOpened`, `VideoSizeChanged`, `MediaEnded`, `MediaFailed`, `PositionChanged`, `SeekCompleted`, `FullScreenChanged`, `PictureInPictureChanged`.

> ⚠️ Don't read `VideoSize` once inside `MediaOpened`. Android reports the size from a separate player
> callback that routinely lands *after* the media is open, so that version of the code reports `0 x 0` there
> for real video. Bind the property or handle `VideoSizeChanged`, which fires whenever the size becomes known
> or changes (an adaptive stream switching rendition changes it mid-playback).

Blazor mirrors all of it as `[Parameter]`s with `On*` `EventCallback`s, and adds `Poster`. Its video size is
exposed as the `VideoWidth`/`VideoHeight` ints the browser reports, with `OnVideoSizeChanged`. **Linux** ships separately as `Shiny.Maui.Controls.MediaElement.Linux` — there is no Linux target framework, so the GTK4 backend can't live in the main package — and is registered with `UseShinyMediaElementGtk()` instead of `UseShinyMediaElement()`; decoding needs `gtk4-media-gstreamer` (Fedora/Arch) or `libgtk-4-media-gstreamer` (Debian/Ubuntu) installed.
