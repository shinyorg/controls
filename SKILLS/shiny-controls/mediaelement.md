# MediaElement

Cross-platform audio/video playback for .NET MAUI and Blazor.

**Packages** — add-ons, not part of the main control packages:

| Package | Targets |
| --- | --- |
| `Shiny.Maui.Controls.MediaElement` | iOS, Android, Windows, Mac Catalyst, macOS AppKit |
| `Shiny.Maui.Controls.MediaElement.Linux` | Linux GTK4 (`Microsoft.Maui.Platforms.Linux.Gtk4` head) |
| `Shiny.Blazor.Controls.MediaElement` | Blazor WebAssembly |

Backends: AVPlayer (Apple), Media3/ExoPlayer (Android), `Windows.Media.Playback.MediaPlayer` (Windows), `GtkMediaFile` (Linux), HTML5 media (Blazor).

---

## Setup

### MAUI

```bash
dotnet add package Shiny.Maui.Controls.MediaElement
```

```csharp
builder
    .UseShinyControls()
    .UseShinyMediaElement();
```

```xml
xmlns:media="http://shiny.net/maui/media"
```

Namespace: `Shiny.Maui.Controls.Media` (shared enums/DTOs live in `Shiny.Controls.Media`).

### Linux GTK4

There is no Linux target framework, so the GTK backend ships as its own package and replaces — does not
supplement — the normal registration:

```csharp
builder.UseShinyMediaElementGtk();   // instead of UseShinyMediaElement()
```

Decoding is GStreamer's via GTK's media backend, so the machine needs `gtk4-media-gstreamer`
(Fedora/Arch) or `libgtk-4-media-gstreamer` (Debian/Ubuntu). Without it the control lays out and reports
a load error through `MediaFailed`.

### Blazor

```bash
dotnet add package Shiny.Blazor.Controls.MediaElement
```

```razor
@using Shiny.Blazor.Controls.Media
@using Shiny.Controls.Media
```

No `Program.cs` registration — the component imports its own JS module.

---

## Basic usage

```xml
<media:MediaElement Source="https://example.com/clip.mp4"
                    AutoPlay="True"
                    Aspect="AspectFit"
                    HeightRequest="220" />
```

```razor
<MediaElement Source="https://example.com/clip.mp4"
              AutoPlay="true"
              Aspect="MediaAspect.AspectFit"
              Style="max-width:640px;aspect-ratio:16/9;" />
```

### Source

MAUI's `Source` is a `MediaSource` with a `TypeConverter`, so a bare string in XAML is classified for you:

| Value | Becomes | Resolves to |
| --- | --- | --- |
| `https://…`, `http://…`, an HLS/DASH manifest | `UriMediaSource` | streamed |
| a rooted path, or `file:///…` | `FileMediaSource` | the device filesystem |
| anything else (`intro.mp4`, `clips/intro.mp4`) | `ResourceMediaSource` | a `Resources/Raw` file in the app package |

Build them explicitly with `MediaSource.FromUri/FromFile/FromResource`. On Blazor `Source` is just a URL
string.

---

## Show / hide controls

**The transport bar is drawn by Shiny, not the platform.** That is what makes per-element visibility
possible at all — native transport UI is all-or-nothing everywhere except Windows. It also means one look
across every target, themed from the active Shiny theme pack.

| Property | Default | Hides |
| --- | --- | --- |
| `ShowTransportBar` | true | the whole bar (drive playback from your own UI via the commands) |
| `ShowPlayPauseButton` | true | the play/pause button |
| `ShowSeekBar` | true | the scrubber |
| `ShowVolumeControl` | true | the mute button + volume slider |
| `ShowFullScreenButton` | true | the fullscreen toggle |
| `ShowTimeLabels` | true | the elapsed / total labels |
| `ShowPictureInPictureButton` | false | the PiP button (opt-in; needs an app manifest entry) |

`AutoHideTransportBar` (default true) fades the bar after `TransportBarAutoHideDelay` (3s) **while playing
only** — a paused frame or an audio-only track keeps its controls, which is both conventional and what
keeps the buttons reachable to a screen reader. Set it false for audio.

Styling: `TransportBarBackgroundColor`, `ControlColor`, `SeekBarColor`, `VideoBackgroundColor`. Leave
`SeekBarColor` unset and the scrubber follows `Shiny.Color.Primary` from the theme.

---

## Commands (MAUI) / methods (Blazor)

| MAUI command | Blazor method |
| --- | --- |
| `PlayCommand` | `PlayAsync()` |
| `PauseCommand` | `PauseAsync()` |
| `StopCommand` | `StopAsync()` |
| `TogglePlayPauseCommand` | `TogglePlayPauseAsync()` |
| `SeekCommand` | `SeekAsync(TimeSpan)` |
| `MuteCommand` | `ToggleMuteAsync()` / `SetMutedAsync(bool)` |
| `ToggleFullScreenCommand` | `ToggleFullScreenAsync()` / `SetFullScreenAsync(bool)` |
| `PictureInPictureCommand` | `TryEnterPictureInPictureAsync()` / `ExitPictureInPictureAsync()` |

MAUI also exposes the plain methods (`Play()`, `Pause()`, `Stop()`, `SeekAsync()`, `ToggleMute()`,
`ToggleFullScreen()`).

`SeekCommand` accepts a `TimeSpan`, a number of seconds, or a string of either. **A bare number is
seconds** — `CommandParameter="30"` seeks to 0:30, not thirty days; use `"00:01:30"` for a clock value.
`MuteCommand` toggles with no parameter and sets outright when given a `bool`.
`PictureInPictureCommand.CanExecute` is false where the platform can't do PiP, so a bound button disables
itself.

```xml
<Button Text="Skip to 1:30"
        Command="{Binding Source={x:Reference Player}, Path=SeekCommand}"
        CommandParameter="00:01:30" />
```

---

## Properties

| Property | Type | Default | Notes |
| --- | --- | --- | --- |
| `Source` | `MediaSource` (MAUI) / `string?` (Blazor) | null | see above |
| `AutoPlay` | bool | false | play once the source opens |
| `IsLooping` | bool | false | suppresses `MediaEnded` |
| `Volume` | double | 1 | clamped 0..1 |
| `IsMuted` | bool | false | independent of `Volume` |
| `PlaybackRate` | double | 1 | clamped 0.25..4 |
| `Position` | TimeSpan | 0 | two-way; read back every `PositionUpdateInterval`, assigning it seeks |
| `Duration` | TimeSpan | 0 | read-only; zero until opened, and for live streams |
| `CurrentState` | `MediaElementState` | None | None / Opening / Buffering / Playing / Paused / Stopped / Failed |
| `VideoSize` | `Size` | 0 x 0 | MAUI only — read-only pixel size of the video track; zero for audio-only. Blazor splits it into `VideoWidth`/`VideoHeight` ints |
| `BufferedProgress` | double | 0 | 0..1; the scrubber's secondary track |
| `Aspect` | `MediaAspect` | AspectFit | AspectFit / AspectFill / Fill |
| `KeepScreenOn` | bool | false | inhibit display sleep while playing |
| `PositionUpdateInterval` | TimeSpan | 250ms | MAUI only — how often the playhead is polled |
| `IsFullScreen` | bool | false | two-way |
| `EnableBackgroundPlayback` | bool | false | see below |
| `Metadata` | `MediaMetadata` | null | Title / Artist / Album / ArtworkUri |
| `Capabilities` | `MediaPlaybackCapabilities` | None | read-only |
| `Poster` | string | null | Blazor only |

**Events (MAUI):** `StateChanged`, `MediaOpened`, `VideoSizeChanged`, `MediaEnded`, `MediaFailed`,
`PositionChanged`, `SeekCompleted`, `FullScreenChanged`, `PictureInPictureChanged`.
**Blazor:** the same as `On*` `EventCallback` parameters (`OnStateChanged`, `OnMediaOpened`,
`OnVideoSizeChanged`, `OnMediaEnded`, `OnMediaFailed`, `OnPositionChanged`, `OnFullScreenChanged`,
`OnPictureInPictureChanged`), plus `@bind-IsMuted`.

---

## Sizing the player to the video

`VideoSize` is what a layout that must fit its source reads — a portrait clip in a fixed landscape box is
fitted to the box's *height* and drawn as a narrow sliver down the middle.

```xml
<media:MediaElement x:Name="Player" Source="{Binding Clip}" Aspect="AspectFit" />
```

```csharp
this.Player.VideoSizeChanged += (_, size) =>
{
    if (size.Height > 0)
        this.Player.HeightRequest = this.Player.Width * (size.Height / size.Width);
};
```

⚠️ **Never read `VideoSize` once inside `MediaOpened` and stop there.** Android reports the size from a
separate player callback that routinely lands *after* the media is open, so the read-at-open version of this
code reports `0 x 0` on Android for perfectly ordinary video. Bind the property or handle
`VideoSizeChanged` — which also covers an adaptive stream changing rendition mid-playback. Generate the
event/binding form, not the one-shot read.

---

## Background playback

```xml
<media:MediaElement Source="{Binding EpisodeUrl}"
                    EnableBackgroundPlayback="True"
                    AutoHideTransportBar="False"
                    ShowFullScreenButton="False" />
```

```csharp
player.Metadata = new MediaMetadata
{
    Title = "Episode 42",
    Artist = "The Show",
    ArtworkUri = "https://example.com/art.jpg"
};
```

Audio keeps playing with the app backgrounded or the device locked, and `Metadata` drives the OS transport
UI — `MPNowPlayingInfoCenter` + `MPRemoteCommandCenter` on Apple, a Media3 `MediaSession` behind a
`mediaPlayback` foreground service on Android, SMTC on Windows, `navigator.mediaSession` on Blazor.

**Two manifest opt-ins are the app's, not the library's:**

- **iOS / Mac Catalyst** — add `audio` to `UIBackgroundModes` in `Info.plist`. Without it iOS silences the
  session the moment the app leaves the foreground, which is the usual reason "background audio doesn't
  work".
- **Android** — the service and its permissions are contributed to your merged manifest automatically, but
  API 33+ needs the **`POST_NOTIFICATIONS` runtime grant** or the media notification never appears.

Assign a *new* `MediaMetadata` instance to update it; the control doesn't watch the object's properties.

Video stops rendering while backgrounded unless the user is in Picture-in-Picture — that's a platform
rule, not a library limitation.

---

## Picture-in-Picture

```csharp
if (!await player.TryEnterPictureInPictureAsync())
    await this.DisplayAlert("PiP", "Not available on this device", "OK");
```

Returns `false` — never throws — where the platform, OS version or manifest doesn't allow it.

**Android** additionally needs, in the app:

```csharp
[Activity(SupportsPictureInPicture = true,
          ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | /* … */ )]
public class MainActivity : MauiAppCompatActivity
{
    public override void OnPictureInPictureModeChanged(bool isInPictureInPictureMode, Configuration? config)
    {
        base.OnPictureInPictureModeChanged(isInPictureInPictureMode, config);
        AndroidMediaIntegration.NotifyPictureInPictureModeChanged(isInPictureInPictureMode);
    }
}
```

Android reports PiP transitions only to the activity, so without that forward the control never learns the
user collapsed the window (PiP still works; `IsPictureInPictureActive` just goes stale).

---

## Fullscreen

`IsFullScreen` / `ToggleFullScreen()` push a modal page carrying a second surface bound to the **same
player**, so the stream keeps running instead of re-buffering and the inline control keeps its place in
your layout. Android's back gesture collapses it. On Blazor it's the Fullscreen API on the container
element, and the `fullscreenchange` listener means an exit via Escape updates `IsFullScreen` too.

---

## Capabilities — check before you offer

Support genuinely differs, so read `Capabilities` rather than assuming. The built-in transport bar already
hides what the backend can't do.

| | Background audio | PiP | Playback rate | Volume | Buffer progress |
| --- | --- | --- | --- | --- | --- |
| iOS / Mac Catalyst | ✅ | ✅ | ✅ | ✅ | ✅ |
| Android | ✅ | ✅ (API 26+) | ✅ | ✅ | ✅ |
| Windows | ✅ (SMTC) | ❌ no per-element API | ✅ | ✅ | ✅ |
| macOS AppKit | ✅ | ❌ not advertised | ✅ | ✅ | ✅ |
| Linux GTK4 | ❌ no MPRIS yet | ❌ | ❌ | ✅ | ❌ |
| Blazor | browser's call | ✅ where available | ✅ | ⚠️ probed at runtime | ✅ |

Notes: WinUI has no per-element PiP (a compact-overlay `AppWindow` shrinks the whole app window, which is a
different thing, so it isn't offered as PiP). GTK continues playing while the window is hidden because a
desktop process isn't suspended, but there are no OS transport controls. iOS Safari refuses programmatic
volume, which the Blazor backend detects by writing a value and reading it back.

---

## Substituting the player

`IMediaPlayerBackend` owns the platform player; the view is pushed into it via `SetOutput`. Replace it
process-wide:

```csharp
MediaPlayerBackends.Factory = () => new MyBackend();
```

Useful for tests (no device needed) and for plugging in a different player.

**The player is created when the element's handler connects, not in its constructor**, and disposed when the
handler disconnects (a re-shown element creates a fresh one and re-opens its `Source`). A backend registers
with process-wide sources as soon as it exists (Android's PiP event, `NSNotificationCenter` on Apple,
ExoPlayer's playback thread), so creating it eagerly kept every never-shown element alive forever. Everything
set beforehand — `Source`, `Volume`, `IsMuted`, `PlaybackRate`, `IsLooping`, `Aspect`, `KeepScreenOn`,
background playback, and a `Play()` call — is applied on connect. Consequences for generated code: a
`MediaElement` must be in the visual tree to play (it is not an off-screen audio player), `Capabilities` is
`None` until it has a handler, and a unit test that drives a fake backend must give the element a handler
first (a stub `IViewHandler` assigned to `element.Handler` is enough).

---

## Generation rules

- Add-on packages — never assume `Shiny.Maui.Controls` alone is enough. MAUI needs
  `.UseShinyMediaElement()` (or `.UseShinyMediaElementGtk()` on Linux) in `MauiProgram`.
- MAUI XAML needs `xmlns:media="http://shiny.net/maui/media"`.
- For audio-only players set `AutoHideTransportBar="False"` and `ShowFullScreenButton="False"`.
- When generating background playback, always mention the platform manifest opt-ins — the code alone
  won't work.
- `CommandParameter` for `SeekCommand`: bare numbers are seconds, colon-separated values are clock times.
- Don't offer a PiP or volume control unconditionally; gate on `Capabilities`, or just let the built-in
  transport bar do it.
