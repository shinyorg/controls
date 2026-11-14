# Media analysis (frame + audio analyzers on CameraView and MediaElement, speech captions)

## Rules for generated code
- An analyzer is assigned to a control: `Analyzer` (frames) and `AudioAnalyzer` (audio) exist on **both**
  `CameraView` and `MediaElement` (MAUI and Blazor). The same analyzer instance type works on either host.
- MAUI analyzer packages are `Shiny.Maui.Controls.Analysis.Barcode|Ocr|Face|Motion|Documents|Ai` (namespaces
  match). Never emit the old `Shiny.Maui.Controls.Camera.Barcode` etc. or `CameraFrame` — it is `VideoFrame`.
- Shared types: `using Shiny.Controls.Analysis;` (`IFrameAnalyzer`, `VideoFrame`, `OverlayBox`, `IAudioAnalyzer`,
  `AudioFrame`, `AudioAnalysisFormat`, `ISourceAudioAnalyzer`, `CaptionCue`, `ICaptionSource`, `DetectedBarcode`…);
  MAUI bases/controls: `using Shiny.Maui.Controls.Analysis;` (`FrameAnalyzer`, `AudioAnalyzer`, `CaptionView`,
  `IAnalysisHost`, `IZoomableAnalysisHost`, `AnalysisResolution`, `AppleVideoFrame`/`AndroidVideoFrame`/`WindowsVideoFrame`).
- XAML: analyzers and `CaptionView` resolve under `xmlns:media="http://shiny.net/maui/media"`,
  `xmlns:cam="http://shiny.net/maui/camera"` and `xmlns:analysis="http://shiny.net/maui/analysis"`;
  `SpeechCaptionAnalyzer` also under `xmlns:shiny="http://shiny.net/maui/controls"`.
- `MediaElement`'s content property is `Analyzer`; set `AudioAnalyzer` with property-element syntax.
- Results are delivered only while armed: call `Scan()` (or bind `ScanCommand`) on the host; an `OnDetected`
  returning `true` keeps scanning.

## MediaElement frames
```xml
<media:MediaElement x:Name="Player" Source="clip.mp4" ShowAnalysisOverlay="True">
    <media:BarcodeAnalyzer OnDetected="{Binding OnCodes}" />
</media:MediaElement>
```
- Boxes are drawn by the element (mapped for `Aspect`, also in fullscreen); `OverlaysChanged` for custom drawing.
- Seek → boxes clear, `OnDiscontinuity()` runs, next frame has `IsDiscontinuity`. `VideoFrame.Timestamp` = media position.
- Supported: iOS/Catalyst/macOS, Android, Blazor (barcode + document only, CORS + `CrossOrigin="anonymous"` for
  remote media). Not Windows/Linux. Check `Capabilities.HasFlag(MediaPlaybackCapabilities.FrameAnalysis)`.

## Captions
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
- Decodable local sources are transcribed ahead of playback (`IsTimed` → captions in sync); streams/camera are live.
- Engines: Apple `SFSpeechRecognizer` (needs `NSSpeechRecognitionUsageDescription`; device, not simulator), Android
  13+ `SpeechRecognizer`, else an `ISpeechToTextProvider` (`Provider` property or DI). `Engine="Provider"` forces it.
- Blazor: `new SpeechCaptionAnalyzer(provider)` (provider required), `<MediaElement AudioAnalyzer="@captions">` with
  `<CaptionView Source="@captions" Position="@position" />` as child content.
- Camera captions: `<cam:CameraView><cam:CameraView.AudioAnalyzer><shiny:SpeechCaptionAnalyzer/></cam:CameraView.AudioAnalyzer></cam:CameraView>`
  plus a `CaptionView` (live mode, no Position binding needed).
- `Transcript`, `Cues`, `CaptionCommitted`, `RecognitionFailed`, `ToWebVtt()`.

## Custom analyzers
- Audio: derive `AudioAnalyzer`, override `Id`, `Format` (default 16 kHz mono), `AnalyzeAsync(AudioFrame, ct)`;
  optional `OnSeek`, `OnSourceChanged`; implement `ISourceAudioAnalyzer.AnalyzeSourceAsync` for whole-source work.
  Use `Raise(...)` for UI-thread events. Audio is delivered in order and not dropped unless seconds behind.
- Native frame analyzers downcast to `AndroidVideoFrame` (use `MediaImage ?? ToBitmap()`), `AppleVideoFrame`,
  `WindowsVideoFrame` — never `AndroidCameraFrame`.
- Steering the camera (zoom/resolution): `if (this.Host is IZoomableAnalysisHost host) ...`.

## Migration (breaking)
- `BarcodeScanOptions` removed → `media.ScanBarcodeAsync(mode: BarcodeScanMode.LongRange, autoZoom: true)`.
- `MediaScanRequest<T>` → `FrameScanRequest<T>`. Blazor `CameraAnalyzer` → `Shiny.Blazor.Controls.Analysis.FrameAnalyzer`.
