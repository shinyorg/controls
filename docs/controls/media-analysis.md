# Media Analysis — frame & audio analyzers, speech captions

[← All Shiny Controls](../../README.md)

Packages: `Shiny.Controls.Analysis.Shared` (contracts), `Shiny.Maui.Controls.Analysis` (MAUI pipeline, `CaptionView`),
`Shiny.Blazor.Controls.Analysis` (browser pipeline, `CaptionView`), the analyzer add-ons
`Shiny.Maui.Controls.Analysis.Barcode` / `.Ocr` / `.Face` / `.Motion` / `.Documents` / `.Ai`, and
`SpeechCaptionAnalyzer` in `Shiny.Maui.Controls.SpeechAddins` / `Shiny.Blazor.Controls.SpeechAddins`.

An **analyzer** is assigned to a control and fed what that control sees or hears. The same analyzer works on a
`CameraView` (the live camera and its microphone) and on a `MediaElement` (the video and audio it plays):

| | `CameraView` | `MediaElement` |
|---|---|---|
| `Analyzer` (`IFrameAnalyzer`) | camera frames | decoded video frames |
| `AudioAnalyzer` (`IAudioAnalyzer`) | the microphone | the audio being played |
| `Scan()` / `ScanCommand` | arms the frame analyzer | arms the frame analyzer |
| Overlay | `CameraOverlayView` | drawn by the element itself (`ShowAnalysisOverlay`) |

So OCR, barcode, face and motion detection run over a video file or stream exactly as they do over the camera,
and speech captions run over either.

## Frames on a MediaElement

```xml
<media:MediaElement x:Name="Player" Source="lecture.mp4">
    <media:OcrAnalyzer TextRecognized="OnText" />   <!-- the content property is Analyzer -->
</media:MediaElement>
```

```csharp
this.Player.Scan();   // results are delivered while armed, exactly like the camera
```

- Frames come from the decoded video at analysis size (longest edge 1280 on Android; the stream's own size on
  Apple), so boxes line up with the picture whatever the `Aspect`. The built-in overlay maps them for
  `AspectFit` / `AspectFill` / `Fill` and follows the element into fullscreen.
- **A paused video is analyzed once**, not on every tick, and a **seek clears the boxes** and calls
  `IFrameAnalyzer.OnDiscontinuity()` before the next frame (which has `VideoFrame.IsDiscontinuity` set), so an
  analyzer that tracks across frames (the long-range barcode tracker, the motion baseline) resets instead of
  reading the jump as movement.
- `VideoFrame.Timestamp` is the media position, so a result can be tied to a moment in the video.
- The barcode analyzer's auto-zoom only applies to a camera (`IZoomableAnalysisHost`); on a video it reads what is
  there.

| Platform | Frames | Audio | Whole-source audio |
|---|---|---|---|
| iOS / Mac Catalyst / macOS | `AVPlayerItemVideoOutput` | `MTAudioProcessingTap` (not HLS) | `AVAssetReader` (local files) |
| Android | Media3 GL readback effect | `TeeAudioProcessor` | `MediaExtractor` + `MediaCodec` (files, assets, progressive HTTP) |
| Blazor | canvas / `BarcodeDetector` (CORS) | AudioWorklet (CORS) | `fetch` + `decodeAudioData` (CORS) |
| Windows, Linux | — | — | — |

`MediaElement.Capabilities` reports `FrameAnalysis`, `AudioAnalysis` and `SourceAudioAnalysis`. Protected (DRM)
content never yields frames. On Android, enabling frame analysis on a source that is already playing reopens it
once at the current position (Media3 only builds its effects pipeline at prepare). Blazor's browser analyzers are
`BarcodeAnalyzer` and `DocumentAnalyzer` (there is no in-browser OCR); remote media needs CORS headers plus
`CrossOrigin="anonymous"`.

## Audio analyzers and captions

```xml
<Grid>
    <media:MediaElement x:Name="Player" Source="talk.mp4">
        <media:MediaElement.AudioAnalyzer>
            <shiny:SpeechCaptionAnalyzer x:Name="Captions" />
        </media:MediaElement.AudioAnalyzer>
    </media:MediaElement>
    <media:CaptionView Source="{x:Reference Captions}"
                       Position="{Binding Source={x:Reference Player}, Path=Position}"
                       VerticalOptions="End" Margin="16,0,16,72" />
</Grid>
```

- **Whole source first.** When the element opens a source the platform can decode, a `SpeechCaptionAnalyzer`
  (any `ISourceAudioAnalyzer`) is handed the whole source ahead of playback and transcribes it, typically many
  times faster than real time. Its captions carry the time each phrase is spoken (`IsTimed`), so a `CaptionView`
  bound to the player's `Position` shows them in step with the picture. Set `AnalyzeWholeSource="False"` to caption
  only what is played.
- **Otherwise live.** A live stream, an Apple HLS stream, or a `CameraView` microphone is captioned live, a phrase
  at a time, shortly after it is spoken. `CaptionView` then shows the newest text and clears after `LiveHoldTime`.
- **Engines.** Apple: the on-device `SFSpeechRecognizer` fed the audio as buffers (word timings; needs
  `NSSpeechRecognitionUsageDescription` and speech permission; the simulator has no on-device model). Android 13+:
  the platform `SpeechRecognizer` fed through `EXTRA_AUDIO_SOURCE`. Anything else — and Blazor, whose speech API
  only hears the microphone — uses a Shiny.Speech `ISpeechToTextProvider` (Azure, OpenAI, ElevenLabs, Whisper):
  set `Provider`, or register one. Provider audio is cut into utterances at pauses (`SpeechSegmenter`) so cues
  still get real start and end times.
- `Transcript`, `Cues`, `CaptionCommitted`, `RecognitionFailed`, and `ToWebVtt()` to save the captions.

Audio is queued per analyzer and delivered **in order without dropping** (a recognizer that misses 200 ms loses a
word); only if an analyzer falls seconds behind is the oldest audio dropped, and the next `AudioFrame` then has
`IsDiscontinuity`. Positions are counted from the last seek, because neither platform tap reports a usable
timestamp. Audio arrives converted to the analyzer's `Format` (default 16 kHz mono).

### On a CameraView

`CameraView.AudioAnalyzer` feeds the microphone to the same analyzers. On Apple it is the capture session's own
audio output, so it coexists with recording: while an audio analyzer is attached, recordings use the
AVAssetWriter path (the one burn-in overlays use) and share the audio. Android reads it with `AudioRecord`
(recording with audio at the same time depends on the device allowing two captures); Windows uses `AudioGraph`;
Blazor opens `getUserMedia({ audio })`.

## Blazor

```razor
@using Shiny.Blazor.Controls.Analysis
@using Shiny.Blazor.Controls.SpeechAddins

<MediaElement Source="media/talk.mp4"
              Analyzer="@analyzer"
              AudioAnalyzer="@captions"
              OnPositionChanged="p => position = p"
              BarcodesDetected="OnCodes">
    <CaptionView Source="@captions" Position="@position" />
</MediaElement>

@code {
    FrameAnalyzer? analyzer = new BarcodeAnalyzer();
    SpeechCaptionAnalyzer captions = new(speechProvider);   // any Shiny.Speech ISpeechToTextProvider
    TimeSpan position;
}
```

- `ScanAsync()` arms the frame analyzer; results arrive through `BarcodesDetected` / `DocumentDetected`, boxes
  through `OverlaysChanged`, problems through `OnAnalysisError`.
- Remote media needs CORS headers on the server and `CrossOrigin="anonymous"` on the element, for both frames and
  audio. The audio tap routes the element through Web Audio for the rest of its life (it still plays normally).
- `<CameraView AudioAnalyzer="@captions">` captions the microphone the same way.

## Writing your own

```csharp
public class LevelMeter : AudioAnalyzer
{
    public override string Id => "level";
    public override AudioAnalysisFormat Format => AudioAnalysisFormat.Native;

    public override ValueTask AnalyzeAsync(AudioFrame frame, CancellationToken ct)
    {
        var rms = frame.GetRms();
        this.Raise(() => this.Level = rms);
        return ValueTask.CompletedTask;
    }
}
```

A custom native frame analyzer downcasts to the **platform** frame — `AppleVideoFrame` (`PixelBuffer`,
`ToCGImage()`), `AndroidVideoFrame` (`MediaImage` when it has one, otherwise `ToBitmap()`), `WindowsVideoFrame` —
never to `AndroidCameraFrame`, or it will find nothing on video.

## Migrating from the camera-only analyzers (breaking)

| Before | After |
|---|---|
| `Shiny.Maui.Controls.Camera.Barcode` / `.Ocr` / `.Face` / `.Motion` / `.Documents` / `.Ai` packages + namespaces | `Shiny.Maui.Controls.Analysis.*` (no camera reference) |
| `Shiny.Controls.Camera.CameraFrame`, `CameraFrameFormat` | `Shiny.Controls.Analysis.VideoFrame`, `VideoFrameFormat` |
| `IFrameAnalyzer`, `OverlayBox`, `CoordinateTransform`, `PreviewScaleMode`, barcode/document result types in `Shiny.Controls.Camera` | `Shiny.Controls.Analysis` |
| `FrameAnalyzer`, `AnalysisResolution` in the camera package | `Shiny.Maui.Controls.Analysis` |
| `AppleCameraFrame`, `WindowsCameraFrame` | `AppleVideoFrame`, `WindowsVideoFrame`; `AndroidCameraFrame` now derives from `AndroidVideoFrame` |
| `FrameAnalyzer.Host` was a `CameraView` | an `IAnalysisHost`; cast to `IZoomableAnalysisHost` for zoom |
| `MediaScanRequest<T>` | `FrameScanRequest<T>` (`IMediaService` implements `IFrameScanService<MediaScanOptions>`) |
| `BarcodeScanOptions { Mode, AutoZoom }` | `ScanBarcodeAsync(mode:, autoZoom:)` |
| Blazor `CameraAnalyzer` (`Shiny.Blazor.Controls.Camera`) | `FrameAnalyzer` (`Shiny.Blazor.Controls.Analysis`) |

XAML is unaffected: the analysis namespaces are mapped into the `camera`, `media` and new `analysis` xmlns.
