# CameraView

A cross-platform camera control for **.NET MAUI** (iOS, Android, Windows, macOS AppKit) and **Blazor WebAssembly**. Live preview with zoom, torch, lens/device selection, photo + video capture, and live color filters — plus a pluggable **frame-analysis pipeline**. Each analyzer raises its **own strongly-typed event** for its result (barcode value, faces, recognized text, a structured document) and returns **styled bounding boxes** (`OverlayBox`) that the overlay draws.

- **MAUI core**: `Shiny.Maui.Controls.Camera` — `CameraView` (handler-based `View`) + `CameraOverlayView` (drop-in box overlay), backed by AVFoundation (Apple), CameraX (Android), Media Capture (Windows). Register with `.UseShinyCamera()`.
- **Blazor core**: `Shiny.Blazor.Controls.Camera` — `<CameraView>` component over `getUserMedia` / `MediaRecorder` / `BarcodeDetector`.
- **Analyzer add-ons** (MAUI): `Shiny.Maui.Controls.Camera.Barcode`, `.Camera.Face`, `.Camera.Motion`, `.Camera.Ocr`, `.Camera.Documents` (Invoice / Receipt / DriversLicense / HealthCard / CreditCard / Passport), `.Camera.Ai` (AI document scanner — detect-then-send-to-`IChatClient`). Add only what you need.
- **AI document scanner**: `Shiny.Maui.Controls.Camera.Ai` (MAUI) / `Shiny.Blazor.Controls.Camera.Ai` (Blazor) — detect a document is *present*, then send that one frame to a **Microsoft.Extensions.AI `IChatClient`** for structured extraction. See *AI document scanner* below.
- **Shared contracts**: `Shiny.Controls.Camera` namespace — `IFrameAnalyzer`, `FrameAnalyzer` (base), `OverlayBox`, `CameraFrame`, `CoordinateTransform`, the document building blocks (`RecognizedText`, `DocumentField`, `DocumentLineItem`, `DocumentDetectedEventArgs<T>`, `IDocumentParser<T>`), and the enums (`CameraFacing`, `CameraFilter`, `CameraFlashMode`, `PreviewScaleMode`).

**Prefer `IMediaService` when you want a result, not a screen.** The same package registers a MAUI-only
`IMediaService` that presents a modal `CameraView` page for you — permissions, photo/video capture with
programmatic compression, gallery picking, zoom limits, and one-line `ScanBarcodeAsync` /
`ScanCreditCardAsync` / `ScanTextAsync` verbs contributed by the analyzer add-ons. Use `CameraView`
directly (below) only when the camera is part of a screen the app designs itself. See
**media-service.md**.

**One analyzer at a time:** `CameraView.Analyzer` holds a **single** `IFrameAnalyzer` (null = none) — assign or swap it live; it's the content property so it can be declared inline in XAML.

**Two channels per analyzer:** a typed *event* carries the semantic result (**always an array** — a frame can hold several barcodes/faces); the *return value* of `AnalyzeAsync` is the styled `OverlayBox`es to draw. A returned set persists until the analyzer returns a different set (replace) or `null` (clear).

## Installation

### .NET MAUI

```bash
dotnet add package Shiny.Maui.Controls.Camera
# optional analyzers
dotnet add package Shiny.Maui.Controls.Camera.Barcode
dotnet add package Shiny.Maui.Controls.Camera.Face
dotnet add package Shiny.Maui.Controls.Camera.Motion
dotnet add package Shiny.Maui.Controls.Camera.Ocr
dotnet add package Shiny.Maui.Controls.Camera.Documents   # Invoice / Receipt / DriversLicense / HealthCard / CreditCard / Passport
dotnet add package Shiny.Maui.Controls.Camera.Ai          # AI document scanner — detect a document, then parse it with Microsoft.Extensions.AI
```

```csharp
builder
    .UseShinyControls()
    .UseShinyCamera();
```

```xml
xmlns:cam="http://shiny.net/maui/camera"
```

**Permissions (consumer responsibility):**
- iOS / Mac Catalyst / macOS — `NSCameraUsageDescription` (+ `NSMicrophoneUsageDescription` for video-with-audio) in `Info.plist`; macOS camera entitlement when sandboxed.
- Android — `<uses-permission android:name="android.permission.CAMERA" />` (+ `RECORD_AUDIO` for audio). **Minimum SDK 23** (CameraX).
- Windows — `webcam` (+ `microphone`) capability in `Package.appxmanifest`.

### Blazor

```bash
dotnet add package Shiny.Blazor.Controls.Camera
```

```razor
@using Shiny.Blazor.Controls.Camera
@using Shiny.Controls.Camera
```

`getUserMedia` requires a secure context (HTTPS or `localhost`).

## Basic Usage — MAUI

```xml
<cam:CameraView x:Name="Camera"
                Facing="Back"
                ScaleMode="AspectFill"
                Zoom="1"
                IsPinchToZoomEnabled="True"
                IsTorchOn="False"
                Filter="None" />
```

Zoom is driven by whatever you bind to `Zoom`; `IsPinchToZoomEnabled="True"` adds a two-finger pinch on the
preview as a second driver of the same property. Both go through the same clamp, so a slider bound to `Zoom`
follows the pinch and neither can leave `MinZoom`..`MaxZoom`:

```xml
<Slider Minimum="{Binding MinZoom, Source={x:Reference Camera}}"
        Maximum="{Binding MaxZoom, Source={x:Reference Camera}}"
        Value="{Binding Zoom, Source={x:Reference Camera}, Mode=TwoWay}" />
```

```csharp
// The preview auto-starts when the view is added (IsActive defaults true) and the control requests camera
// permission itself — handle a denial (or any error) via CameraError. Toggle IsActive for lifecycle; it is
// safe to set any time (unlike StartAsync/RequestPermissionAsync, which need the handler already connected).
this.Camera.CameraError += (_, e) => status = e.Message;   // e.g. "Camera permission denied"

protected override void OnAppearing()
{
    base.OnAppearing();
    this.Camera.IsActive = true;    // resume (no-op on first show — it already auto-started)
}

protected override void OnDisappearing()
{
    base.OnDisappearing();
    this.Camera.IsActive = false;   // release the camera while off-screen
}

// Capture a still photo — the current Filter is baked into the JPEG, so preview and photo match
CameraPhoto photo = await this.Camera.CapturePhotoAsync();   // photo.Data is JPEG bytes

// Record video (audio optional, defaults on). NOTE: recorded video is NOT filtered (it records the raw feed)
await this.Camera.StartVideoRecordingAsync(new VideoRecordingOptions { IncludeAudio = true });
CameraVideo video = await this.Camera.StopVideoRecordingAsync();   // video.FilePath

// Burn an overlay INTO the recorded file (watermark, timestamp, reticle) — composited into every frame,
// not just the live preview. DrawOverlay runs off the UI thread per encoded frame; draw in pixel space.
// iOS / Mac Catalyst / macOS / Android; Windows records the raw feed for now. Omit Overlay for the fast path.
await this.Camera.StartVideoRecordingAsync(new VideoRecordingOptions
{
    IncludeAudio = true,
    Overlay = new DelegateVideoOverlay((canvas, frame, ctx) =>
    {
        canvas.FontColor = Colors.White;
        canvas.FontSize = Math.Max(24, frame.Height * 0.04f);
        canvas.DrawString(ctx.Elapsed.ToString(@"mm\:ss"), 20, 20, frame.Width, 60,
            HorizontalAlignment.Left, VerticalAlignment.Top);
    })
    // or Overlay = new DrawableVideoOverlay(existingCameraOverlayDrawable) to reuse an IDrawable
});
CameraVideo overlaid = await this.Camera.StopVideoRecordingAsync();

// Apple writes movie fragments every 10s on BOTH recording paths — the raw one (AVCaptureMovieFileOutput's
// own default) and the burn-in one — so a recording the process never gets to finish is still playable up to
// the last fragment. That matters for long captures: a crash, a force-quit or a battery pull would otherwise
// leave a file with no moov atom that nothing will decode, however many minutes went into it.
// Android has NO equivalent: CameraX's Recorder muxes through MediaMuxer, which cannot write fragmented MP4,
// so an unfinished recording there is lost. If a hard kill mid-capture is a real risk on Android, record in
// segments — a run of short StartVideoRecordingAsync/StopVideoRecordingAsync calls — rather than one long one.

// Recording quality is a SESSION setting, not a per-recording one — set it before/independently of a
// recording, not inside VideoRecordingOptions. Default is High (1080p) on every platform.
this.Camera.VideoQuality = VideoQuality.Medium;   // 720p
this.Camera.VideoFrameRate = 24;                  // null = platform default; the cheapest lever on heat/size
this.Camera.VideoBitrate = 8_000_000;             // null = platform default for the resolution

// Apple only: recording audio SHARES the audio session with other apps by default, so starting a recording
// no longer stops the user's music/podcast/nav app — and, just as importantly, their pressing play no longer
// interrupts the capture session (which would stop video too). Set false only when a clean audio track
// matters more than their playback.
this.Camera.MixWithOtherAudio = false;            // default true

// Orientation is also a SESSION setting, and it covers the preview, stills, recorded video AND the frames
// an analyzer sees — on Apple they all hang off connections on one session, so they cannot disagree.
// Default is Device: the camera follows the display as it rotates.
this.Camera.Orientation = CameraOrientation.Device;

// Pin it when several recordings have to agree with each other — segmented continuous capture, where each
// segment is its own StartVideoRecordingAsync call. Left on Device, a device rotated between segments gives
// you files that disagree, and a later concat/trim takes its transform from the first track and renders the
// rest sideways. The landscape members are named for where the device's TOP EDGE points, deliberately:
// every platform's LandscapeLeft/LandscapeRight pair means something different from the others'.
this.Camera.Orientation = CameraOrientation.LandscapeTopLeft;

// Flip lens
this.Camera.Facing = this.Camera.Facing == CameraFacing.Back ? CameraFacing.Front : CameraFacing.Back;

// Live colour filter — sugar over Effects; always applied FIRST in the chain
this.Camera.Filter = CameraFilter.Noir;

// Effects chain — colour grades, spatial looks, compositing, post-capture transforms. Applied to the
// preview, to captured photos AND to recorded video (see the Effects section for per-platform coverage).
this.Camera.Effects.Add(CameraEffects.Comic);
```

> `RequestPermissionAsync()` / `StartAsync()` / `StopAsync()` / `GetAvailableCamerasAsync()` are still available
> for explicit control, but they route through the platform handler and **no-op (return `false`/empty) if the
> handler isn't connected yet** — e.g. when called in `OnAppearing` on first show. Don't gate startup on them;
> prefer `IsActive` + `CameraError`. Call `GetAvailableCamerasAsync()` once the view is loaded (e.g. from a
> `Loaded`/handler-ready hook) so the lens list isn't empty.

### Selecting a specific camera

`Facing` picks by position; `CameraId` pins an exact device (multiple back lenses, USB webcams on macOS).

```csharp
IReadOnlyList<CameraInfo> cameras = await this.Camera.GetAvailableCamerasAsync();
this.Camera.CameraId = cameras.First(c => c.Name.Contains("USB")).Id;   // null => fall back to Facing
```

### Photo quality

Stills default to `PhotoQuality.Highest` — the device's **full sensor resolution** at the platform's best quality — and this is **independent of `VideoQuality`**. Before this existed the still rode the session preset, so the default 1080p session handed back a ~2MP photo from a 12MP sensor with no way to ask for more.

```csharp
// the default; nothing to set for a full-resolution photo
var photo = await this.Camera.CapturePhotoAsync();

// a picture that is an input to something else rather than a photograph -
// scanning, a thumbnail, a frame posted to a service
this.Camera.PhotoQuality = PhotoQuality.Session;   // preview-sized, instant, smallest

// full resolution but shutter-first, for hand-held or burst-ish use
this.Camera.PhotoQuality = PhotoQuality.Balanced;

this.Camera.PhotoJpegQuality = 0.95;               // 0.0-1.0, clamped; default 0.9
```

The rung is read **per capture** on Apple and Windows, so it can be changed live — including from off the view (a setting, or a remote control endpoint) — with no session reconfiguration and no preview blink. Android is the exception: CameraX fixes the capture mode when `ImageCapture` is built, so a change rebinds the use cases and, like `VideoQuality`, is **ignored while a recording is running**.

## Frame Analysis (the differentiator)

Set **one** `IFrameAnalyzer` on `Camera.Analyzer` (swap it any time; `null` clears). The pipeline streams frames off the UI thread with drop-on-busy back-pressure. The analyzer **always draws its bounding boxes**, but **only delivers a result while it is _armed_** — so you get live boxes without a flood of events. **Arm with `Camera.Scan()` / `Camera.ScanCommand`**; the next confirmed detection is delivered once, then the analyzer goes quiet until armed again (single-shot). To keep scanning, an `OnDetected` handler returns `true` (see below).

```csharp
using Shiny.Maui.Controls.Camera.Barcode;
using Shiny.Maui.Controls.Camera.Documents;
using Shiny.Maui.Controls.Camera.Motion;
using FaceAnalyzer = Shiny.Maui.Controls.Camera.Face.FaceAnalyzer;

var barcode = new BarcodeAnalyzer();                          // native scanner — Apple Vision / MLKit (iOS+Android)
// fires once per frame with EVERY code in view (array); .First is the first one
barcode.BarcodesDetected += (_, e) => status = $"{e.Barcodes.Count} code(s): {e.First.Format} {e.First.Value}";

Camera.Analyzer = barcode;                                    // single analyzer — assigning replaces any prior one
// ... later, from a "Scan" button:
Camera.Scan();                                                // arm — next frame's codes fire BarcodesDetected once
```

Delivery is on the UI thread (the pipeline marshals it), so handlers can touch UI directly.

### Arming a scan (the trigger)

Boxes are continuous; **results are pulled, not pushed**. Bind a button/Fab to **`Camera.ScanCommand`** (or call `Camera.Scan()`), which arms the active analyzer for one scan. Default is **single-shot** — one result per arm. To control continuation, set the analyzer's **`OnDetected`** — a `Func<TArgs, Task<bool>>` run (on the UI thread) with the detection; **`return true` to keep scanning** (stay armed), `false` to stop until the next `Scan()`. It can be async (validate against a DB, show a confirm) before deciding. `Camera.StopScanning()` disarms.

```xml
<cam:CameraView x:Name="Camera">
    <cam:BarcodeAnalyzer OnDetected="{Binding OnBarcodes}" />
</cam:CameraView>
<shiny:Fab Icon="scan" Command="{Binding Source={x:Reference Camera}, Path=ScanCommand}" />
```

```csharp
public ObservableCollection<string> Codes { get; } = new();

// e.Barcodes is the full set in the frame; return true => keep scanning, false => disarm
public Func<BarcodesDetectedEventArgs, Task<bool>> OnBarcodes => async e =>
{
    foreach (var b in e.Barcodes)
        if (!Codes.Contains(b.Value)) Codes.Add(b.Value);
    return Codes.Count < 5;                      // stop after 5
};
```

The analyzer's typed event (`BarcodesDetected`, …) and bound `Command` still fire alongside `OnDetected`, but **only while armed** — they're passive observers and can't influence continuation. The same set lingering in view won't re-deliver (it re-fires only when the set of detections changes).

### Swapping / disabling the analyzer

`Camera.Analyzer` is a single bindable property — **assign a new analyzer at runtime and the running pipeline
picks it up live** (seamless on Apple/Windows; Android rebinds its capture use-cases automatically); set it to
`null` to stop analysis. To turn the analyzer off *without losing its bindings/state*, set
**`FrameAnalyzer.IsEnabled`** (bool, default `true`) instead of clearing it — its command/event bindings stay
wired and it resumes instantly when re-enabled. While the analyzer is disabled (or null) the camera behaves as
if it had none (so, e.g., Android can record video again).

```xml
<cam:CameraView Facing="Back">
    <!-- toggle live from a switch; binding + state are preserved while off -->
    <cam:BarcodeAnalyzer BarcodesDetectedCommand="{Binding ScanCommand}"
                         IsEnabled="{Binding IsToggled, Source={x:Reference ScanSwitch}}" />
</cam:CameraView>
```

`IsEnabled` (run or not) is distinct from `ShowBoundingBox` (run, but draw nothing).

### Rate-limiting an analyzer — override `WantsFrame`, never `AnalyzeAsync`

An analyzer that only needs a few passes a second must declare that by overriding **`WantsFrame()`**, which
the pipeline asks on the capture thread **before the platform builds a frame at all**:

```csharp
public override bool WantsFrame() => timeProvider.GetUtcNow() >= this.nextPassAt;

public override async ValueTask<IReadOnlyList<OverlayBox>?> AnalyzeAsync(CameraFrame frame, CancellationToken ct)
{
    this.nextPassAt = timeProvider.GetUtcNow() + Cadence;
    ...
}
```

⚠️ **Do not generate the throttle as an early return from `AnalyzeAsync`.** By then the frame exists, and on
Apple building one can mean a full-frame pixel copy — 8.3 MB at 1080p, on the Large Object Heap. An analyzer
that runs five passes a second but bails out of the other twenty-five was paying for thirty frames a second
and discarding 25 of them. `WantsFrame` is what turns that into five.

It must be cheap and must not block or throw — it runs on the capture callback, ahead of the encoder. Read a
cached deadline, not a setting. (Default is `true`: every frame.)

### Ambient light and other whole-frame statistics

For a consumer that wants a *number* rather than an image — an exposure or ambient-light average — use
**`CameraFrame.SampleLuminance(destination, columns, rows)`**, which fills an evenly spaced grid by reading
only those pixels off the native buffer. `GetLuminance()` materializes the entire plane (a 2 MB array at
1080p, converted pixel by pixel), so reading a 32×32 grid out of it pays two million reads for a thousand.

> **Picking among several detectors at runtime:** build the analyzer instances once, keep their `OnDetected`
> handlers wired, and assign the chosen one to `Camera.Analyzer` from a picker (see the sample's `CameraPage`).
> Only one runs at a time.

### Restricting the scan area (`ScanWindow`)

Set **`FrameAnalyzer.ScanWindow`** (a `RectF?` in normalized upright space, `null` = whole frame) to limit
both **detection** and the **overlay** to a region: only detections inside it are reported and drawn, and the
built-in overlay **dims everything outside it and frames a viewfinder reticle**. Great for a "point the barcode
here" band — it both **prevents picking up other codes** in the shot and **speeds up scanning**. Barcode honors
it **natively**: Apple Vision's `regionOfInterest` (iOS/macOS) and a Y-plane crop fed to MLKit (Android) so the
engine only processes that band (no-op on Windows / bare `net10.0`).

```csharp
// a center band; the overlay frames it as a viewfinder (x, y, w, h normalized)
barcode.ScanWindow = new RectF(0.1f, 0.4f, 0.8f, 0.2f);
barcode.ScanWindow = null;                                 // back to the whole frame
```

```xml
<!-- in XAML bind it to a VM RectF? property (the value is a struct, so set it in code/VM) -->
<cam:BarcodeAnalyzer ScanWindow="{Binding Band}" OnDetected="{Binding OnBarcodes}" />
```

The reticle/scrim colors are tunable on `CameraOverlayView` (`ScanWindowColor`, `ScanWindowScrimColor`; set a
color to `null` to drop that part). `CameraView.ScanWindow` (read-only) mirrors the active analyzer's window.

**OCR honors it natively too, and for OCR it is not optional.** `OcrAnalyzer` crops to the scan window before
recognizing rather than post-filtering what came back, because the platform engines **discard text below a
minimum height** (Apple Vision's default is 1/32 of the image height — ~34px in a 1080p frame) and **downscale
before recognizing**. Small, distant text — a license plate, a road sign, a shelf label — therefore never
survives whole-frame OCR no matter how the results are filtered. Two knobs go with it:

| Property | Meaning |
|---|---|
| `OcrAnalyzer.MinimumTextHeight` | Smallest text to look for, as a fraction of the recognized image's height. `0` = platform default. Apple only (MLKit/Windows expose no equivalent). |
| `OcrAnalyzer.MinimumInputHeight` | Upscale the scan-window crop to at least this many pixels tall before recognizing. `0` = off. Only applies when a scan window is set. This is what buys small text back on Android. |

```csharp
// read small text inside a narrow band — crop to it, then upscale the crop before recognizing
var ocr = new OcrAnalyzer
{
    ScanWindow        = new RectF(0.2f, 0.55f, 0.6f, 0.25f),
    MinimumTextHeight = 0.012f,   // Vision's 1/32 default would drop this text outright
    MinimumInputHeight = 720      // the crop is ~270px tall off 1080p; take it to 720
};
```

For a custom analyzer, call the recognizer directly — the same crop/upscale path, and results always come back
in **full-frame** coordinates, never crop space:

```csharp
var text = await this.recognizer.RecognizeAsync(
    frame,
    new TextRecognitionOptions(RegionOfInterest: region, MinimumTextHeight: 0.012f, MinimumInputHeight: 720),
    ct);
```

`TextRecognitionOptions` is the per-frame cache key, so two analyzers asking for the **same** region on one frame
still cost a single OCR pass, while one watching a different region gets its own.

### Declaring the analyzer in XAML + Commands (MVVM)

Analyzers are `BindableObject`s, so you can declare the one inside `<cam:CameraView>` (its content property is
`Analyzer`) under the `cam:` prefix, and bind the analyzer's **`…Command`** to a ViewModel — the command fires
(on the UI thread) with the same args as the event. It inherits the camera's `BindingContext`.

```xml
<cam:CameraView Facing="Back" Filter="Chrome">
    <!-- run the analyzer for its command only, no box: -->
    <cam:MotionAnalyzer MotionChangedCommand="{Binding MotionCommand}" ShowBoundingBox="False" />
</cam:CameraView>
```

Each analyzer exposes the command matching its event: `BarcodesDetectedCommand`, `MotionChangedCommand`,
`FacesDetectedCommand`, `TextRecognizedCommand`, and `DocumentDetectedCommand` (documents). Bind a
`Command<T>` whose `T` is that analyzer's event-args type. These fire **only while armed** (after `Scan()`)
and **can't decide whether to keep scanning** — for that, bind the analyzer's **`OnDetected`**
(`Func<TArgs, Task<bool>>`, `return true` to stay armed; see *Arming a scan*) instead of / alongside the
command. Commands and `OnDetected` are MAUI-only (Blazor uses `RequestBarcodeAsync` / `EventCallback`s).

Restrict `BarcodeAnalyzer` to specific symbologies with `Formats` — settable inline in XAML as a
comma-separated list (a `TypeConverter` parses it; case-insensitive; omit = all). The filter is applied
natively (Vision `Symbologies` / MLKit `SetBarcodeFormats`):

```xml
<cam:BarcodeAnalyzer BarcodesDetectedCommand="{Binding ScanCommand}"
                     Formats="QrCode,Ean13,Code128" />
```

In code it's an `IList<BarcodeFormat>?`: `new BarcodeAnalyzer { Formats = [BarcodeFormat.QrCode] }`. Set it
before the analyzer starts scanning (constructor / XAML); on Android the scanner client is built on the first
frame, so mutating `Formats` mid-session has no effect until the analyzer is re-created.

### Controlling the overlay per analyzer

- `IsEnabled` (bool, default `true`) — run the analyzer or not (see *Enabling / disabling analyzers* above).
- `ShowBoundingBox` (bool, default `true`) — set `False` to run an analyzer purely for its event/command
  and draw nothing. Every built-in analyzer honors it, so it disables box rendering for any of them.
- `OverlayProvider` (code-level `Func<TArgs, IReadOnlyList<OverlayBox>?>`) — return the exact boxes to draw
  for a detection, or `null` for none. When unset, the analyzer draws its own default styled box.

```csharp
barcode.OverlayProvider = e => e.Value.StartsWith("OK")
    ? [ new OverlayBox(e.BoundingBox, Colors.Lime, e.Value) ]
    : null;   // don't box anything that doesn't start with "OK"
```

### Drawing bounding boxes

Drop a `CameraOverlayView` over the `CameraView` in the same cell and point it at the camera — it auto-subscribes and redraws. Each analyzer styles its own boxes (color/text); `Default*` fills in anything unset.

```xml
<Grid>
    <cam:CameraView x:Name="Camera" ScaleMode="AspectFill" />
    <cam:CameraOverlayView Camera="{x:Reference Camera}" InputTransparent="True" />
</Grid>
```

(Low-level alternative: a `GraphicsView` + `CameraOverlayDrawable`, fed from `Camera.OverlaysChanged` — only needed for custom rendering.)

### Motion — localized regions

`MotionAnalyzer` clusters movement into **separate regions**, so motion in two spots yields two boxes rather
than one box spanning both. `MotionChanged` carries `MotionEventArgs.Regions` (one normalized `RectF` per
moving area) plus `Region` (their union, for back-compat) and `Intensity` (0..1). Tune detection with
`PixelThreshold` / `AreaThreshold` (overall trigger), `SampleStride` (speed), and `GridColumns` /
`CellThreshold` (how finely motion is split into regions). Boxes still honor `ShowBoundingBox` and
`OverlayProvider`.

The **event is debounced**: `MotionChanged` fires `InMotion=true` only after `EnterFrames` consecutive
frames above threshold (default 3) and `InMotion=false` only after `ExitFrames` below it (default 5) — so a
single noisy frame or a brief pause mid-movement doesn't fire it. Raise `EnterFrames` if motion is still too
twitchy. The **overlay boxes are not debounced** — they track every frame so they stay responsive.

### Documents — typed events (Invoice, Receipt, BusinessCard, DriversLicense, HealthCard, CreditCard, Passport)

Each document type is its **own analyzer with its own strongly-typed event** (`DocumentDetected` typed to its payload). Every payload is a strong record with **nullable fields** (only what was found is set). Ships `InvoiceAnalyzer` (`Invoice`, order lines in `.Lines`), `ReceiptAnalyzer` (`Receipt` — line items in `.Lines`, per-tax breakdown in `.Taxes`, plus subtotal/tip/discount/total), `BusinessCardAnalyzer` (`BusinessCard` — emails in `.Emails`, phones in `.Phones`, plus name/title/company/website/address), `DriversLicenseAnalyzer` (`DriversLicense`), `HealthCardAnalyzer` (`HealthCard`), `CreditCardAnalyzer` (`CreditCard`), `PassportAnalyzer` (`Passport`).

**Fields accumulate across frames.** A document that reveals its fields gradually (number, then name, then expiry) is **merged** over up to `AccumulationFrames` frames (default 5) and `DocumentDetected` fires **once** with the richest combined record — not a stream of partials. It fires early when the parser reports the read complete, and won't re-fire while the same document stays in view; `ResetAfterEmptyFrames` (default 5) re-arms once the document leaves the frame. Set `AccumulationFrames = 1` for the old fire-every-frame behavior. (`DriversLicenseAnalyzer` is PDF417/AAMVA — complete in one read — so it isn't accumulated.)

```csharp
var invoice = new InvoiceAnalyzer();                         // OCR + rules
invoice.DocumentDetected += (_, e) =>
{
    Invoice doc = e.Document;
    status = $"Invoice {doc.Number} — total {doc.Total}, {doc.Lines.Count} line(s)";
};

var license = new DriversLicenseAnalyzer();                 // PDF417 + AAMVA parse (deterministic)
license.DocumentDetected += (_, e) =>
    status = $"{e.Document.FirstName} {e.Document.LastName} — {e.Document.Number}";

Camera.Analyzer = invoice;   // one analyzer at a time — swap to `license` when you want to scan a license instead
Camera.Scan();               // arm — DocumentDetected fires once on the next confirmed read (bind OnDetected to keep scanning)
```

- **Driver's licenses** are decoded from the back's **PDF417 barcode** (read with the native scanner — Apple Vision / Android MLKit) and parsed against the **AAMVA** standard — the parse is deterministic. Reads only on iOS/Android/macOS (the native scanner; a no-op on Windows and bare `net10.0`). Works for US states and the Canadian provinces that emit an AAMVA PDF417 (BC, AB, SK, MB, NS, NB, PEI, NL); dates auto-switch to Canadian `CCYYMMDD` order (inferred from the country element or the province code `DAJ`, surfaced as `DriversLicense.Jurisdiction`). **Ontario and Quebec licences carry no PDF417 barcode, so they don't scan** — use a custom OCR-backed `DocumentAnalyzer` for those.
- **Passports** parse the **MRZ** (the two `<<<` lines, ICAO TD3) → `Passport` (number, surname, given names, nationality, issuing country, DOB, expiry, sex) — MRZ parse is deterministic.
- **Credit cards**: `CreditCard.Type` (Visa/Mastercard/Amex/…) and number validity come from the IIN prefix + Luhn (deterministic); name/expiry/company are best-effort OCR. `Cvv` is on the back and PCI-sensitive, so it's almost always `null` from a front scan.
- **Receipts** parse the merchant header, purchased line items (`Receipt.Lines`), a **per-tax breakdown** (`Receipt.Taxes`, each with an optional rate) plus `Subtotal` / `Tax` (sum) / `Tip` / `Discount` / `Total`, and best-effort `PaymentMethod` / `CardLast4` / `Currency` / `Date` / `Time` — OCR + rules, so swap in a custom `IDocumentParser<Receipt>` for accuracy.
- **Business cards** parse the cardholder `Name` + `JobTitle`, the `Company`, the contact channels — `Emails` and typed `Phones` (each tagged Mobile/Office/Fax… from its line label), `Website`, and a best-effort `Address`. Email / phone / URL are matched deterministically; name / title / company are heuristic (cards have no fixed layout). `BusinessCard.Email` / `.Phone` convenience props return the first of each. OCR + rules — swap in a custom `IDocumentParser<BusinessCard>` (e.g. an LLM) for production accuracy.
- **Health cards** are OCR + best-effort rules tuned for **Canadian** cards: the parser detects the issuing province from on-card keywords and applies that province's number format — Quebec/RAMQ (4 letters + 8 digits), Ontario/OHIP (10 digits + 2-letter version code), BC PHN (10), Alberta/AHCIP (9), etc. — surfacing `HealthCard.Province` and a `Plan` field. Unknown layouts fall back to the longest plausible digit run.
- **Invoices / receipts / business cards / health cards / credit cards** are OCR + best-effort rules. Swap the rules by passing a custom `IDocumentParser<T>`:
  `new InvoiceAnalyzer(new MyInvoiceParser())`, where `MyInvoiceParser : IDocumentParser<Invoice>` returns the typed payload + the boxes to draw. (`OcrAnalyzer` itself just raises `TextRecognized` with raw `RecognizedText` blocks.)

**Payload records** (all data properties nullable; enums default to `Unknown`/`Unspecified` — populated only when found):

```csharp
record Invoice(string? Number, DateOnly? Date, decimal? Total, IReadOnlyList<InvoiceLine> Lines, IReadOnlyList<DocumentField> Fields);
record InvoiceLine(string? Description, decimal? Quantity, decimal? UnitPrice, decimal? Amount, RectF? Bounds);
record Receipt(string? Merchant, string? MerchantPhone, string? ReceiptNumber, DateOnly? Date, TimeOnly? Time, IReadOnlyList<ReceiptLine> Lines, decimal? Subtotal, IReadOnlyList<ReceiptTax> Taxes, decimal? Tax, decimal? Tip, decimal? Discount, decimal? Total, string? Currency, string? PaymentMethod, string? CardLast4, IReadOnlyList<DocumentField> Fields);
record ReceiptLine(string? Description, decimal? Quantity, decimal? UnitPrice, decimal? Amount, RectF? Bounds);
record ReceiptTax(string? Label, decimal? Rate, decimal? Amount, RectF? Bounds);
record BusinessCard(string? Name, string? JobTitle, string? Company, IReadOnlyList<string> Emails, IReadOnlyList<BusinessCardPhone> Phones, string? Website, string? Address, IReadOnlyList<DocumentField> Fields);   // .Email / .Phone convenience props = first of each
record BusinessCardPhone(string Number, string? Type, RectF? Bounds);   // Type = Mobile / Office / Fax / … from the line label
record DriversLicense(string? Number, string? FirstName, string? LastName, DateOnly? DateOfBirth, DateOnly? Expiry, string? Address, string? Jurisdiction, IReadOnlyList<DocumentField> Fields);
record HealthCard(string? Number, string? Name, DateOnly? Expiry, string? Issuer, string? Province, IReadOnlyList<DocumentField> Fields);
record CreditCard(CreditCardType Type, string? Number, DateOnly? Expiry, string? FirstName, string? LastName, string? CompanyName, string? Cvv, IReadOnlyList<DocumentField> Fields);
enum CreditCardType { Unknown, Visa, Mastercard, Amex, Discover, DinersClub, JCB, UnionPay, Maestro }
record Passport(string? Number, string? Surname, string? GivenNames, string? Nationality, string? IssuingCountry, DateOnly? DateOfBirth, DateOnly? Expiry, PassportSex Sex, IReadOnlyList<DocumentField> Fields);
enum PassportSex { Unspecified, Male, Female }
record DocumentField(string Label, string? Value, RectF? Bounds, float Confidence);   // each payload's Fields bag
```

### Writing a custom document analyzer

For an OCR-backed document, derive from **`DocumentAnalyzer<TDocument>`** and supply an **`IDocumentParser<TDocument>`** — don't derive from `FrameAnalyzer` directly. The base runs the shared OCR recognizer, calls your parser, raises the typed `DocumentDetected` (+ `DocumentDetectedCommand`) on the UI thread, draws the parser's boxes, and honors `IsEnabled`/`ShowBoundingBox`/`OverlayProvider`. You write only the payload + the parse rules. Three pieces:

```csharp
using Shiny.Controls.Camera;                  // RecognizedText, DocumentField, IDocumentParser<T>, OverlayBox
using Shiny.Maui.Controls.Camera.Documents;   // DocumentAnalyzer<T>

// 1) payload — nullable fields (only what was found is set) + a Fields bag for extras
//    (BusinessCard already ships — this LoyaltyCard is just an illustrative custom type)
public record LoyaltyCard(string? Program, string? MemberName, string? MemberNumber, IReadOnlyList<DocumentField> Fields);

// 2) parser — turn OCR'd lines into the payload + the boxes to draw. RecognizedText is already normalized + upright.
public sealed partial class LoyaltyCardParser : IDocumentParser<LoyaltyCard>
{
    [GeneratedRegex(@"\b\d[\d ]{8,}\d\b")] private static partial Regex Number();

    public bool TryParse(IReadOnlyList<RecognizedText> text, out LoyaltyCard document, out IReadOnlyList<OverlayBox> boxes)
    {
        document = null!; boxes = [];
        var numberLine = text.FirstOrDefault(t => Number().IsMatch(t.Text));
        if (numberLine is null) return false;   // cheap "is this my document?" signal — bail fast, else clears overlay

        var number  = Number().Match(numberLine.Text).Value.Replace(" ", "");
        var program = text.FirstOrDefault()?.Text;
        var fields = new List<DocumentField> { new("Program", program, text.FirstOrDefault()?.BoundingBox), new("Member #", number, numberLine.BoundingBox) };
        document = new LoyaltyCard(program, null, number, fields);
        boxes = fields.Where(f => f.Bounds is not null).Select(f => new OverlayBox(f.Bounds!.Value, Colors.Lime, f.Label)).ToList();
        return true;
    }
}

// 3) analyzer — one-liner; wires the parser
public sealed class LoyaltyCardAnalyzer : DocumentAnalyzer<LoyaltyCard>
{
    public LoyaltyCardAnalyzer() : base(new LoyaltyCardParser()) { }
    public LoyaltyCardAnalyzer(IDocumentParser<LoyaltyCard> parser) : base(parser) { }  // allow swap-in
    public override string Id => "myapp.camera.loyaltycard";
}
```

Rules: `TryParse` runs on the analysis thread (keep it fast, allocation-light, no UI); return `false` (lead with a cheap signal check) when it isn't your document so it doesn't misfire each frame; OCR is native so it needs iOS/Android/Windows/macOS (not bare `net10.0`); a remote/LLM parser is fine — the analyzer drops frames while busy (one in flight). To only change rules on a built-in type, skip the new analyzer and pass a parser to the existing one: `new BusinessCardAnalyzer(new MyLlmBusinessCardParser())`.

**Opt into frame accumulation** (optional): `IDocumentParser<T>` has two default interface methods you can override so the analyzer merges your document across frames before firing instead of emitting every partial read. `Merge(accumulated, incoming)` fills in fields seen on later frames (typically `accumulated.X ?? incoming.X`, and `return incoming` when it's a *different* document); `IsComplete(document)` lets it fire early once the key fields are present. The defaults (replace + never-complete) preserve the legacy fire-every-`AccumulationFrames`-frames behavior, so existing custom parsers keep working untouched.

```csharp
public LoyaltyCard Merge(LoyaltyCard a, LoyaltyCard b) => a with
{
    Program = a.Program ?? b.Program, MemberName = a.MemberName ?? b.MemberName,
    MemberNumber = a.MemberNumber ?? b.MemberNumber,
    Fields = a.Fields.Count >= b.Fields.Count ? a.Fields : b.Fields
};
public bool IsComplete(LoyaltyCard d) => d.MemberNumber is not null;
```

### AI document scanner (`Shiny.Maui.Controls.Camera.Ai` — detect, then let an LLM read it)

When you don't want to write parse rules — or the document is free-form — use **`AiDocumentAnalyzer<TDocument>`**. It splits the work to save time and money: a **cheap, native presence detector** runs every frame (Apple Vision document segmentation; managed edge detection on Android/Windows — **no OCR**) and draws a live outline, but the **(paid) model call fires at most once per document**, only when one is **steadily in view** and the analyzer is **armed**. At that moment it encodes *just that one frame* to JPEG (cropped to the document) and sends it to a **Microsoft.Extensions.AI `IChatClient`**, parsing the reply straight into `TDocument` via MEAI **structured output**. The model call runs **off the analysis thread**, so the preview never stalls.

```csharp
using Shiny.Maui.Controls.Camera.Ai;
using Shiny.Controls.Camera;          // AiDocument (built-in schema-free payload)

// any IChatClient with a vision model — Azure OpenAI / OpenAI / Ollama / …
IChatClient chat = serviceProvider.GetRequiredService<IChatClient>();

// 1) zero-setup: free-form AiDocument (DocumentType + Summary + label/value Fields), trim/AOT-safe
var ai = new AiDocumentAnalyzer(chat) { Prompt = "Extract every field from this document." };
ai.DocumentDetected += (_, e) =>
{
    AiDocument doc = e.Document;       // doc.DocumentType, doc.Summary, doc.Fields[]
    foreach (var f in doc.Fields) Console.WriteLine($"{f.Label}: {f.Value}");
};

// 2) strongly typed: your own record (give it context-backed JsonSerializerOptions for AOT)
public record Invoice(string? Number, decimal? Total, string[] LineItems);
var typed = new AiDocumentAnalyzer<Invoice>(chat) { SerializerOptions = MyJsonContext.Default.Options };
typed.DocumentDetected += (_, e) => { Invoice inv = e.Document; /* ... */ };

Camera.Analyzer = ai;
Camera.Scan();   // arm — the model is called once the document is held steady; OnDetected → true keeps scanning
```

It reuses the document delivery model — `DocumentDetected` event, `DocumentDetectedCommand`, `OnDetected` (`Func<DocumentDetectedEventArgs<TDocument>, Task<bool>>`), `ShowBoundingBox`, `OverlayProvider`. Tuning: `Prompt`, `Options` (`ChatOptions` — model id/temperature), `SerializerOptions`, `StabilityFrames` (frames a doc must persist before shipping, default 3), `CropPadding` / `SendWholeFrame`, `BoxColor`. An `Error` event surfaces network/auth/parse failures without tearing down the pipeline. Encoding is native per platform (Apple Core Image, Android YUV→JPEG, Windows `BitmapEncoder`); **bare `net10.0` has no encoder, so the analyzer is inert there** (detection still runs).

**Blazor parity** (`Shiny.Blazor.Controls.Camera.Ai`): assign a **`DocumentAnalyzer`** to the camera's `Analyzer` (an in-browser luminance/edge heuristic detects presence + draws the outline), then drive it with **`AiDocumentScanner<TDocument>`** (or `AiDocumentScanner` for `AiDocument`):

```razor
@using Shiny.Blazor.Controls.Camera.Ai
@code {
    readonly AiDocumentScanner scanner = new(chatClient);   // IChatClient
    // camera.Analyzer = new DocumentAnalyzer();
    async Task Scan()
    {
        AiDocument? doc = await scanner.ScanAsync(camera);   // waits for a steady doc, ships the frame, parses
    }
}
```

`ScanAsync` awaits `CameraView.RequestDocumentImageAsync` (the gated "next steadily-present document → cropped JPEG") then sends it to the `IChatClient`. Give the scanner context-backed `SerializerOptions` for trim/AOT-safe WASM (the non-generic `AiDocumentScanner` does this for you).

### Capture & stop on detection ("scan then freeze")

There's no declarative capture/stop flag — do it explicitly inside the analyzer's `OnDetected`. Arm with
`Scan()`, then on the confirmed detection grab a still and/or stop and `return false` to disarm:

```xml
<cam:CameraView x:Name="Camera">
    <cam:PassportAnalyzer OnDetected="{Binding OnPassport}" />
</cam:CameraView>
```

```csharp
public Func<DocumentDetectedEventArgs<Passport>, Task<bool>> OnPassport => async e =>
{
    Passport p = e.Document;
    var photo = await Camera.CaptureAndStopAsync();   // full-res still, then stop the session
    ShowThumbnail(photo);                              // tap it -> Camera.StartAsync() to resume
    return false;                                      // single-shot: disarm
};
```

- The detection passed to `OnDetected` is the **confirmed** one — for documents the merged record; for `MotionAnalyzer`, motion **starting**.
- `CaptureAndStopAsync()` grabs the still and stops, returning the `CameraPhoto` (which also raises `MediaCaptured`). Use `CapturePhotoAsync()` alone to capture without stopping.
- To resume after a stop, call `Camera.StartAsync()` then `Camera.Scan()` to re-arm.

## Effects (pluggable filters)

`CameraView.Effects` is an ordered, live collection applied to **the preview, captured photos and recorded
video**. `CameraView.Filter` still works and is sugar: the chosen `CameraFilter` is always applied **first**,
so setting both is well-defined.

There are four effect kinds, because there are four genuinely different mechanisms. Implement one or more:

| Interface | What it does | Use for |
|---|---|---|
| `IColorEffect` | returns a `ColorMatrix4x5` | colour grades — honoured on **every** platform and surface |
| `INativeEffect` | returns a `NativeEffectDescriptor` (per-backend GPU program) | spatial looks — comic, sketch, blur, distortion |
| `IDrawEffect` | `Draw(ICanvas, RectF, CameraEffectContext)` | compositing — face masks, stickers, watermarks |
| `ICaptureEffect` | `ValueTask<byte[]> ApplyAsync(byte[] jpeg, ct)` | slow post-capture work — AI stylization |

### Built-ins

```csharp
// 12 colour grades — the CameraFilter enum values, also reachable as effects
CameraEffects.Mono, Noir, Sepia, Invert, Vivid, Cool, Warm, Fade, Chrome, Instant, Tonal

// 5 spatial looks (need to see a pixel's neighbours, so no colour matrix can express them)
CameraEffects.Comic, Sketch, Posterize, Pixelate, Blur

camera.Filter = CameraFilter.Noir;          // applied first
camera.Effects.Add(CameraEffects.Comic);    // then this
```

### Custom effects

```csharp
// colour — works everywhere
camera.Effects.Add(new ColorEffect("my.look", new ColorMatrix4x5([
    1.0f, 0,    0,    0, 0.02f,
    0,    0.9f, 0,    0, 0.02f,
    0,    0,    1.0f, 0, 0.02f,
    0,    0,    0,    1, 0
])));

// compositing — runs on preview, stills and recordings from one implementation
camera.Effects.Add(new DelegateDrawEffect("watermark", (canvas, frame, ctx) =>
{
    canvas.FontColor = Colors.White;
    canvas.FontSize = frame.Height * 0.04f;
    canvas.DrawString($"{ctx.Elapsed:mm\\:ss}", 20, 20, 300, 40,
        HorizontalAlignment.Left, VerticalAlignment.Top);
}));
```

`IDrawEffect.Draw` runs **off the UI thread** once per frame, in **frame pixel space** (origin top-left,
`ctx.Width`/`ctx.Height`), front camera already un-mirrored. `ctx.Overlays` carries the analyzer's latest boxes
and `ctx.AnalyzerResult` its latest ungated typed result — that is how an effect anchors to something the
camera is tracking. `ctx.Surface` tells you which of `Preview` / `Photo` / `Video` you are drawing into.

### Coverage is uneven — ask before you offer

```csharp
if (CameraView.GetEffectSupport(CameraEffects.Comic) != EffectSupport.Full)
    ComicButton.IsEnabled = false;    // Full | ColorOnly | StillOnly | Unsupported
```

| | Apple (iOS/Catalyst/macOS) | Android | Blazor | Windows |
|---|---|---|---|---|
| Colour effects, preview | ✅ | ✅ API 31+ | ✅ CSS | ❌ |
| Colour effects, photo | ✅ | ✅ all API levels | ✅ | ✅ |
| Spatial effects, preview | ✅ Core Image | ✅ API 33+ (Blur: 31+) | ✅ all five, CSS/SVG filter | ❌ |
| Spatial effects, photo | ✅ | ✅ managed CPU pass | ✅ same CSS/SVG, baked in | ✅ managed CPU pass |
| Draw effects | ✅ all surfaces | ✅ all surfaces | ❌ not yet | preview + photo |
| Effects in **recorded video** | ✅ pixel + draw | ⚠️ draw only | ❌ | ❌ |

### Face masks (Messenger-style)

```csharp
camera.Analyzer = new FaceAnalyzer { DetectLandmarks = true };   // required — off by default (costs per frame)
camera.Effects.Add(new FaceMaskEffect
{
    Mask = await LoadImageAsync("shades.png"),   // or set OnDraw for custom drawing
    Anchor = FaceAnchor.EyeCenter,               // EyeCenter | FaceCenter | Forehead | Nose | Mouth
    Scale = 2.4f,                                // multiples of eye distance
    Smoothing = 0.35f                            // 0 = raw (juddery — the pipeline drops frames)
});
```

`DetectedFace.Landmarks` (`FaceLandmarks`) gives `LeftEye`/`RightEye`/`NoseBase`/`MouthLeft`/`MouthRight`/
`MouthBottom` plus derived `EyeCenter`, `EyeDistance` and `Roll`. Populated by Apple Vision and Android MLKit
only; Windows and the managed head report boxes with `Landmarks == null`. Eyes are named **as seen on screen**,
so the front camera's mirroring is already accounted for.

### AI stylization (`Shiny.Maui.Controls.Camera.Ai` / `Shiny.Blazor.Controls.Camera.Ai`)

```csharp
// MAUI — a capture effect, so CapturePhotoAsync returns the stylized image
camera.Effects.Add(new AiPhotoStylizer(imageGenerator)
{
    Prompt = "Redraw this photo as a comic-book illustration…"    // default is already comic
});

// Blazor — no automatic capture stage, so call it yourself
var jpeg = await camera.CapturePhotoAsync();
var comic = await stylizer.StylizeAsync(jpeg);
```

Built on Microsoft.Extensions.AI's `IImageGenerator` (`EditImageAsync`), so any provider works. **Seconds of
latency and a per-image cost** — present it as capture → spinner → reveal, never on a frame loop. On failure it
raises `Error` and returns the original photo. `IImageGenerator` is still evaluation-only, so consuming apps
need `<NoWarn>$(NoWarn);MEAI001</NoWarn>`.

> Procedural `CameraEffects.Comic` and the AI stylizer are complementary, not alternatives: the first is a free,
> offline, live viewfinder; the second is a generated image on the shutter. Adding both gives the App Store
> photo-toy flow.

## Basic Usage — Blazor

```razor
<CameraView @ref="camera"
            Facing="CameraFacing.Back"
            CameraId="@cameraId"
            Analyzer="analyzer"
            ShowOverlay="true"
            Filter="filter"
            Effects="effects"
            BarcodesDetected="OnBarcodes"
            OnError="m => status = m"
            Style="width:100%;height:100%;" />

<button @onclick="ScanOne">Scan</button>
<button @onclick="ScanMany">Scan 5</button>

@code {
    CameraView? camera;
    CameraFilter filter = CameraFilter.None;

    // Effects is a PARAMETER on Blazor, not the live IList it is on MAUI — assign a new list to change it.
    // All five spatial looks run in the browser (Blur as a CSS filter, the rest as generated SVG filters).
    IReadOnlyList<ICameraEffect>? effects = [CameraEffects.Comic];

    CameraAnalyzer? analyzer = new BarcodeAnalyzer();   // single analyzer; set null to disable, swap to change
    string? cameraId;
    string? status;
    readonly List<string> codes = new();
    readonly CancellationTokenSource cts = new();

    // fires (gated) with every code in the frame while a RequestBarcodeAsync is outstanding
    void OnBarcodes(IReadOnlyList<CameraBarcode> b) => status = $"{b.Count} code(s): {b[0].Format} {b[0].Value}";

    // gated request/response: arm, await the NEXT decode, then go quiet. The await is the gate.
    async Task ScanOne()
    {
        var b = await camera!.RequestBarcodeAsync();           // resolves on the next barcode, then quiet
        status = $"{b.Format}: {b.Value}";
    }

    // "keep scanning" == call it again in a loop
    async Task ScanMany()
    {
        for (var i = 0; i < 5; i++)
        {
            var b = await camera!.RequestBarcodeAsync(cts.Token);
            if (!codes.Contains(b.Value)) codes.Add(b.Value);
        }
    }

    // list/pick a lens — labels populate only after the camera has started (permission granted)
    async Task LoadLenses()
    {
        var cams = await camera!.GetAvailableCamerasAsync();   // IReadOnlyList<CameraDevice> (Id, Name)
        cameraId = cams.FirstOrDefault()?.Id;                  // bound to CameraId, overrides Facing
    }

    async Task Photo() { var jpeg = await camera!.CapturePhotoAsync(); }            // byte[] (filtered to match preview)
}
```

Blazor mirrors the MAUI single-analyzer shape: assign a typed **`Analyzer`** (today `BarcodeAnalyzer`, which carries `ScanWindow`; `FaceAnalyzer` is a placeholder that reports "not supported") — `null` disables analysis. It mirrors the gated model **imperatively** via `@ref`: boxes draw continuously, but a decoded value is only delivered through **`RequestBarcodeAsync(ct)`** — it arms the detector, resolves on the next barcode, then goes quiet (`await` = the gate; looping = "keep scanning"). `ct` cancels an outstanding request. The **`BarcodesDetected`** `EventCallback<IReadOnlyList<CameraBarcode>>` still exists but is **gated** — it fires (with every code in the frame) only while a `RequestBarcodeAsync` is outstanding, so the default is quiet (no per-frame firehose). Set `Analyzer.ScanWindow` to a normalized `RectF` to restrict scanning to a band (the JS overlay dims outside it and draws a reticle). Barcode scanning uses the browser `BarcodeDetector` (Chromium only); on unsupported browsers `OnError` fires once and preview continues. `OverlaysChanged` (`IReadOnlyList<OverlayBox>`) and `ShowOverlay="true"` (JS-canvas boxes) are unaffected. Changing `Facing`/`CameraId`/`Analyzer`/`ShowOverlay` while running re-acquires the stream; `Filter` and `Effects` update live and are baked into `CapturePhotoAsync` stills. `Effects` is an `IReadOnlyList<ICameraEffect>` **parameter** rather than MAUI's live `IList` — mutating the list in place is not observed, so assign a new list to change the chain. All twelve colour grades resolve to a CSS `filter` shorthand; all five spatial looks run too — `Blur` as CSS, and `Comic`/`Sketch`/`Posterize`/`Pixelate` as SVG `<filter>` definitions generated into the document and referenced as `url(#id)`. `GetEffectSupport` reports `Full` for all of them in the browser. Two analyzers run in the browser: **`BarcodeAnalyzer`** (native `BarcodeDetector`) and **`DocumentAnalyzer`** (an in-browser presence heuristic that pairs with `RequestDocumentImageAsync` + the `Shiny.Blazor.Controls.Camera.Ai` `AiDocumentScanner` — see *AI document scanner* above). Face/motion/OCR analyzers are MAUI-native.

## Properties (MAUI `CameraView`)

| Property | Type | Default | Description |
|---|---|---|---|
| `Facing` | `CameraFacing` | `Back` | `Back` / `Front` / `External` |
| `CameraId` | `string?` | `null` | Exact device id (overrides `Facing`); from `GetAvailableCamerasAsync()` |
| `IsActive` | `bool` | `true` | Whether the session runs |
| `Zoom` | `double` | `1` | Clamped to `MinZoom`..`MaxZoom` |
| `MinZoom` / `MaxZoom` | `double` | `1` | Reported zoom range |
| `IsPinchToZoomEnabled` | `bool` | `false` | Two-finger pinch on the preview drives `Zoom`. Writes through `Zoom`, so it's clamped to `MinZoom`..`MaxZoom` and anything else bound to `Zoom` (slider, view model) tracks it live. No-op where the device reports no range (`MinZoom == MaxZoom`) — e.g. macOS |
| `IsTorchOn` | `bool` | `false` | Continuous torch |
| `FlashMode` | `CameraFlashMode` | `Off` | Still-capture flash (`Off`/`On`/`Auto`) |
| `ScaleMode` | `PreviewScaleMode` | `AspectFill` | `AspectFill` / `AspectFit` |
| `ShowPreview` | `bool` | `true` | Whether the live picture is drawn. **The display path only — not the session.** `false` takes the preview off screen while capture, the analyzer and any recording in flight carry on; a burned-in overlay keeps being burned in. Use it for a dash cam or an unattended capture where a full-screen moving image is a distraction and a battery cost. Do **not** reach for `IsActive` to do this — that stops the camera and ends the recording |
| `Filter` | `CameraFilter` | `None` | `None`/`Mono`/`Noir`/`Sepia`/`Invert`/`Vivid`/`Cool`/`Warm`/`Fade`/`Chrome`/`Instant`/`Tonal`. Sugar over `Effects` — applied **first** in the chain |
| `Effects` | `IList<ICameraEffect>` | empty | Ordered, live effects chain — colour, spatial, compositing and post-capture. See **Effects** above for per-platform coverage |
| `EffectChain` | `CameraEffectChain` | `Empty` | Read-only immutable snapshot of `Filter` + `Effects` that the handlers render against |
| `LastAnalyzerResult` | `object?` | `null` | The active analyzer's latest **ungated** result (updated every frame), handed to draw effects as `ctx.AnalyzerResult` |
| `ShowDetectionOverlay` | `bool` | `true` | Surface overlay boxes for the overlay |
| `Analyzer` | `IFrameAnalyzer?` | `null` | The single frame analyzer (content property) — assign/swap live; toggle via its `IsEnabled` |
| `PhotoQuality` | `PhotoQuality` | `Highest` | How much the device spends on a still: `Session` (whatever the session is already sized for — the pre-1.3 behaviour, fastest and smallest), `Balanced` (full sensor resolution, fast shutter), `Highest` (full sensor at the platform's best quality — multi-frame fusion on Apple, `CAPTURE_MODE_MAXIMIZE_QUALITY` on CameraX). **Independent of `VideoQuality`** — a 1080p session still takes a full-resolution photo. Applied per capture on Apple/Windows (no reconfiguration); rebinds the CameraX use cases on Android, and is ignored there while recording |
| `PhotoJpegQuality` | `double` | `0.9` | JPEG compression for captured stills, 0.0-1.0, clamped. Read at capture time. Only consulted when this control does the encoding: on Apple an unfiltered capture is returned exactly as the platform encoded it, so it applies there only to the re-encode that `Effects`/`Filter` force |
| `VideoQuality` | `VideoQuality` | `High` | Target capture resolution: `Lowest`/`Low` (480p)/`Medium` (720p)/`High` (1080p)/`UltraHigh` (4K)/`Highest`. Session-level — changing it reconfigures the camera; unsupported rungs fall back to the nearest supported one |
| `VideoBitrate` | `int?` | `null` | Target encoding bitrate in bits/sec; `null` = platform default for the resolution |
| `VideoFrameRate` | `int?` | `null` | Target capture frame rate; `null` = platform default. Clamped to what the device's active format supports |
| `MixWithOtherAudio` | `bool` | `true` | **Apple only.** Recording audio shares the device's audio session, leaving other apps' playback running — and stops their playback from interrupting the capture. Read when a recording with `IncludeAudio` starts |
| `Orientation` | `CameraOrientation` | `Device` | How preview, stills, video **and analyzer frames** are oriented: `Device` (follow the display) / `Portrait` / `PortraitUpsideDown` / `LandscapeTopLeft` / `LandscapeTopRight`. Session-level; a change is **deferred while recording**. Ignored on macOS/Windows |
| `IsRecording` | `bool` (get) | `false` | Recording in progress |
| `Overlays` | `IReadOnlyList<OverlayBox>` | empty | Latest overlay boxes (read-only) |
| `ScanWindow` | `RectF?` (get) | `null` | The active analyzer's scan window (read-only mirror) |

**Methods:** `Scan()` (arm the analyzer) · `StopScanning()` (disarm) · `RequestPermissionAsync` · `StartAsync` · `StopAsync` · `CapturePhotoAsync` → `CameraPhoto` · `CaptureAndStopAsync` → `CameraPhoto` (capture a still then stop) · `StartVideoRecordingAsync` / `StopVideoRecordingAsync` → `CameraVideo` · `GetAvailableCamerasAsync` → `IReadOnlyList<CameraInfo>`.
**Commands:** `ScanCommand` (`ICommand` form of `Scan()` — bind a button/Fab to it).
**Events:** `MediaCaptured` · `VideoCaptured` · `OverlaysChanged` (presentation only) · `CameraError`. For results, arm with `Scan()` then handle the analyzer's `OnDetected` (or its typed event / `Command`, both gated by arming): `BarcodesDetected`, `FacesDetected`, `MotionChanged`, `TextRecognized`, `DocumentDetected`. Detection events always carry an **array** (a frame can hold several).
**Analyzer props (every analyzer):** `IsEnabled` · `ShowBoundingBox` · `ScanWindow` (`RectF?` — restrict detection + draw a viewfinder reticle) · `OverlayProvider` · `OnDetected` (`Func<TArgs, Task<bool>>` — `true` keeps scanning) · `IsArmed` (read-only). Documents add `AccumulationFrames` / `ResetAfterEmptyFrames`; `MotionAnalyzer` adds `EnterFrames` / `ExitFrames`.

## Code-Generation Rules

- XAML namespace is `xmlns:cam="http://shiny.net/maui/camera"`; prefix `cam` is the assembly default.
- The preview auto-starts (`IsActive` defaults `true`) and requests permission itself — handle denials via `CameraError` and toggle `IsActive` for lifecycle. `RequestPermissionAsync`/`StartAsync`/`GetAvailableCamerasAsync` route through the handler and no-op/return empty if called before the view is rendered (e.g. in `OnAppearing` on first show), so **don't gate startup on `RequestPermissionAsync()`** — that returns `false` when the handler isn't connected yet and looks like a denied permission. Add the platform permission entries (see Installation) — omitting `NSCameraUsageDescription` crashes iOS instantly.
- `CameraView` is the native preview surface. To draw boxes, layer a `CameraOverlayView` over it in the same `Grid` cell with `Camera="{x:Reference Camera}"` — don't try to draw inside the `CameraView` itself.
- Subscribe to the analyzer's **typed event** for results (`BarcodeAnalyzer.BarcodesDetected`, `FaceAnalyzer.FacesDetected`, `MotionAnalyzer.MotionChanged`, `OcrAnalyzer.TextRecognized`, `*Analyzer.DocumentDetected`) — detection events carry an **array** (a frame can hold several). Don't read semantic data off `OverlaysChanged` — that channel is presentation only.
- `OverlayBox.Rect` is normalized (0..1), upright, mirror-corrected. The overlay converts via `CoordinateTransform.MapToView(...)` — never assume raw pixel coordinates.
- `BarcodeAnalyzer` and `DriversLicenseAnalyzer` (PDF417/AAMVA) read with the **native scanner** — Apple Vision on iOS/macOS and Android MLKit — so they only produce results there; both are a **no-op on Windows and bare `net10.0`** (no native barcode scanner). `FaceAnalyzer`, `OcrAnalyzer`, and the OCR-backed document analyzers (`InvoiceAnalyzer`, `ReceiptAnalyzer`, `BusinessCardAnalyzer`, `HealthCardAnalyzer`, `CreditCardAnalyzer`) need native OCR/ML and only produce results on iOS/Android/Windows/macOS (not bare `net10.0`). `MotionAnalyzer` is managed and works everywhere.
- **OCR runs once per frame, shared.** Every OCR-backed analyzer (`OcrAnalyzer` + all the document analyzers) uses the same `TextRecognizer`, which caches its result on the frame instance keyed by `TextRecognitionOptions` — so enabling Invoice + Receipt + HealthCard + CreditCard + Passport together still does **one** OCR pass per frame, not five. Analyzers asking for *different* regions of interest each get their own pass, which is the intended cost: a region is a fraction of the pixels, so it is usually still cheaper than one whole-frame pass. The shared pass runs with Vision **language correction off** (it corrupts structured fields like license/MRZ/card numbers, totals, and dates by snapping codes to dictionary words); parsers fuzzy-match the raw text.
- **Document analyzers deskew before OCR.** The document analyzers (not `OcrAnalyzer`) call `RecognizeDocumentAsync`, which detects the document, perspective-corrects (deskews) it, then OCRs the flat crop — a big accuracy win for angled cards/IDs, since flat text reads far more reliably. Per platform: **iOS/macOS** = Vision (`VNDetectDocumentSegmentationRequest`) + Core Image; **Windows** = OpenCvSharp (Canny → largest convex quad → `WarpPerspective`); **Android** = a dependency-free managed detector (Otsu + largest bright region + extreme corners) + native `Matrix.SetPolyToPoly` warp (no OpenCV, so the package stays trim/AOT-clean); **bare net10.0** = no-op. When no document is found it falls back to whole-frame OCR. The overlay becomes the detected document outline (`DocumentAnalyzer.BoxColor`). Everything keeps the live `CameraView` preview — this is a frame analyzer, not a modal scanner.
- Custom analyzers should derive from `FrameAnalyzer` (not implement `IFrameAnalyzer` directly) so delivery marshals to the UI thread, they get `IsEnabled` + `ShowBoundingBox` + arming, and their `Command`/`OnDetected` bind in XAML. Deliver a confirmed result with `Deliver(args, raiseEvent, command, onDetected)` — it's gated by arming (does nothing while disarmed), consumes the arm so a lingering detection won't re-fire, and re-arms when `onDetected` returns `true`. Expose your own typed `OnDetected` (`Func<TArgs, Task<bool>>`) bindable property and pass it through. Return boxes via `ResolveOverlay(args, OverlayProvider, () => defaultBoxes)` (independent of arming — boxes always draw; suppressed only when `ShowBoundingBox` is `false`).
- All analyzers live under the single `xmlns:cam="http://shiny.net/maui/camera"` prefix; declare the **one** active analyzer inside `<cam:CameraView>` (content property = `Analyzer`). Bind results with `…Command="{Binding …}"`; the analyzer inherits the camera's `BindingContext`. To offer several detectors, build them once and assign the chosen one to `Camera.Analyzer` (see the sample).
- Invoice/health-card parsing is **best-effort rules** — swap accuracy in via a custom `IDocumentParser<T>`. Driver's-license parsing is deterministic (AAMVA).
- **AI document scanner** (`AiDocumentAnalyzer` / Blazor `AiDocumentScanner`) needs an `IChatClient` (a **vision** model) supplied by the consumer — it ships images, not text. It detects presence cheaply every frame but only calls the model **while armed + the document is steady** (one call per document), so it stays cost-efficient. For trim/AOT, pass `SerializerOptions` built from a `JsonSerializerContext` for your `TDocument` (the built-in `AiDocument` already is). MAUI encoding is native per platform; **inert on bare `net10.0`** (no encoder).
- Use `QRCodeView`/`BarcodeView` from `Shiny.Maui.Controls.Barcodes` to *render* (generate) a code; use the CameraView `BarcodeAnalyzer` to *scan* one. They are different packages for different jobs.

## Common Pitfalls

- **`PhotoQuality` and `VideoQuality` are different knobs, and `Highest` is not free.** A full-sensor capture takes noticeably longer than a preview-sized one — tens to hundreds of milliseconds on `Highest`, where Apple fuses several exposures and CameraX runs its maximise-quality path. That is the right trade for a shutter press and the wrong one for a scanner or a thumbnail: set `PhotoQuality.Session` for those. On Apple there is also a ceiling worth knowing about — the still can only be as large as the **active format** supports, and the active format is chosen by the session preset, so a device that restricts stills on a low preset caps out lower; raising `VideoQuality` raises that ceiling. Windows `PhotoQuality.Session` is the old behaviour and is a grab of the **preview frame**, not a real capture; the other two rungs go to the device's photo stream.

- **Analyzer geometry maps cleanly into the recorded frame.** `OverlayBox` / `RecognizedText.BoundingBox` are normalized upright coordinates and `IVideoOverlayRenderer` draws in encoded-frame pixel space, so drawing a detection into the recording is `box.X * context.Width`, `box.Y * context.Height`. That is only correct because the analyzed frame and the encoded frame share a field of view: on iOS/macOS they are literally the same sample buffer, and on Android the handler binds a shared **`ViewPort`** whenever `ImageAnalysis` and `VideoCapture` are bound together (without it CameraX sizes analysis 4:3 and the recorder 16:9, and a box would sit visibly off the thing it is boxing). The ViewPort is applied *only* in that combined case, so no existing single-use-case setup has its recorded field of view moved.
- **Android: video + analyzer together works, but costs `ImageCapture`** — `Preview + VideoCapture + ImageAnalysis` is a guaranteed CameraX combination at LIMITED hardware level, so recording while an analyzer runs is supported (a dash cam reading signs or plates off its own feed). A *fourth* use case needs LEVEL_3, which most phones are not, so `ImageCapture` is dropped for the duration of a recording that has an enabled analyzer attached — `CapturePhotoAsync` throws a message saying exactly that, and photo capture returns automatically when the recording stops. Outside a recording, nothing changes: analyzer + `ImageCapture` bind together as before.
- **Burn-in video overlay ≠ live overlay** — the `CameraOverlayView` / `CameraOverlayDrawable` only paint the on-screen preview; nothing they draw reaches the saved file. To composite into the *recording*, set `VideoRecordingOptions.Overlay` (`IVideoOverlayRenderer` / `DelegateVideoOverlay` / `DrawableVideoOverlay`). `DrawOverlay` runs **off the UI thread** once per encoded frame — read UI state via a volatile/immutable snapshot, never touch UI objects — and draws in **frame pixel space** (`ctx.Width`/`Height`), origin top-left, front camera already un-mirrored. Supported on iOS / Mac Catalyst / macOS / Android; **Windows throws `PlatformNotSupportedException`** for now (record without the overlay, or use the on-preview overlay). Omitting `Overlay` keeps the fast native recorder.

- **An overlay that caches its own output should say so.** Implement `ICompositedVideoOverlayRenderer` alongside `IVideoOverlayRenderer` and return `VideoOverlayLayer`s from `GetLayers(context)` — each is an `IImage`, the `RectF` it lands on in frame pixels, and a `Version` that changes when (and only when) the image's contents change. A HUD, watermark or telemetry panel typically repaints once or twice a second while being drawn onto thirty frames, and this is what lets the platform stop redrawing it: on Apple the recorder then composites with Core Image on the GPU and **never maps the capture buffer into CPU memory at all**, which is a fixed per-frame cost that does not scale down with how little of the frame the overlay covers. On Android the recording surface is already hardware-composited, so it changes the draw calls but not the cost.
  - ⚠️ **`null` and empty are different answers.** `null` means "draw me the ordinary way this frame" and falls back to `DrawOverlay`; an **empty list** means "there is genuinely nothing to draw" and leaves the frame untouched. Returning empty when you meant null produces a recording with no overlay on it and no error anywhere.
  - ⚠️ **A `Version` that never moves burns the first frame into the whole recording** — the platform caches its own representation of the layer against it. Derive it from whatever already tells the renderer it must repaint.
  - Layers are **borrowed**: the images must stay alive and unmodified until the next `GetLayers` call.
  - Bottom-most first; the platform composites in the order given.
  - Any `IDrawEffect` in `CameraView.Effects` is arbitrary canvas code, so a chain containing one keeps the ordinary draw path.
  - **Pair it with `CaptureFormat = CameraCaptureFormat.Yuv420` on Apple** — see the next bullet. Layers are what make that format worth asking for.

- **Apple: the capture pixel format is a per-frame cost you can stop paying.** `CameraView.CaptureFormat` defaults to `Bgra32`, which is not what the camera produces — the sensor delivers biplanar YCbCr, so BGRA means the capture pipeline colour-converts every frame for as long as the preview is up (idle, not recording, no analyzer — it does not matter), and the hardware encoder converts it back to YUV to write the file. Set `CameraCaptureFormat.Yuv420` to ask for the native format; a device that does not list it silently keeps BGRA.
  - ⚠️ **Only opt in when the overlay is layer-composited and there are no draw effects.** A `CGBitmapContext` cannot be built over a biplanar buffer, so anything drawing on the CPU makes the recorder convert to a scratch BGRA surface and back on every frame — worse than never leaving BGRA. `ICompositedVideoOverlayRenderer` never touches the CPU and is unaffected.
  - `CameraFrame.Format` reports what the frame actually holds, and luminance gets **cheaper**: NV12's first plane already *is* luminance, so `SampleLuminance` reads one byte per sample instead of four plus arithmetic. `ToCGImage()` becomes a GPU conversion, paid only on frames an analyzer actually takes.
  - Android (CameraX) and Windows pick their own formats; the property is ignored there.

- **Frames are only delivered while something wants them.** An attached analyzer, a live effect chain, or a burn-in recording — any one is enough, and with none of the three the sample-buffer delegate is detached entirely on Apple. This is a layer below `IFrameAnalyzer.WantsFrame()`, which decides whether a *delivered* frame is worth wrapping. Nothing to do for it; it matters because a plain preview now costs nothing on the video queue.

```csharp
sealed class HudOverlay : ICompositedVideoOverlayRenderer
{
    IImage? panel;          // re-rendered a couple of times a second, not per frame
    long version;

    public void DrawOverlay(ICanvas canvas, RectF frame, VideoOverlayContext ctx)
        => canvas.DrawImage(this.panel!, 32, 32, this.panel!.Width, this.panel.Height);

    public IReadOnlyList<VideoOverlayLayer>? GetLayers(VideoOverlayContext ctx)
        => this.panel is null
            ? null   // nothing cached yet — let the platform call DrawOverlay
            : [new VideoOverlayLayer(this.panel, new RectF(32, 32, this.panel.Width, this.panel.Height), this.version)];

    void Repaint(IImage rendered)
    {
        this.panel = rendered;
        this.version++;   // ⚠️ without this the platform keeps compositing the old image
    }
}
```
- **Effect coverage differs by platform and surface** — check `CameraView.GetEffectSupport(effect)` and grey the control out rather than shipping one that silently does nothing. Specifics: **Apple** applies everything everywhere, including into recorded video. **Android** applies colour effects to the preview from API 31 and to photos on every API level; spatial *shaders* need API 33 (Blur only needs 31, since it maps to `RenderEffect.CreateBlurEffect`), and below that the preview is unfiltered while the photo still comes back filtered via a managed CPU pass. Recorded video on Android gets **draw effects only** — the preview's `RenderEffect` lives on `PreviewView`, not on the `VideoCapture` use case, so pixel effects do not reach the file. **Windows** has no live-preview effect hook at all (`StillOnly`). The Android preview renders in `PreviewView` *Compatible* (TextureView) mode so the effect can be applied — *Performance* mode (SurfaceView) ignores it.
- **Effect order is meaningful and preserved** — `[Comic, Mono]` is not `[Mono, Comic]`. Consecutive *colour* effects are collapsed into a single matrix for speed, but a spatial effect between them stops the fold, so nothing is silently reordered.
- **Draw effects run off the UI thread, once per frame, on three surfaces** — read mutable state through a volatile field or an immutable snapshot, never touch UI objects, and expect to be called concurrently for the preview and an in-progress recording. `FaceMaskEffect` keeps per-surface smoothing state for exactly this reason.
- **`ICaptureEffect` is slow by design** — an AI stylizer adds seconds to `CapturePhotoAsync`. Show a busy state; never put one on a frame loop.
- **Blazor: publish-only interop failures come from trimming.** `dotnet run` is untrimmed, so a JS-interop DTO problem only appears once the WASM app is published — as `An exception occurred executing JS interop: DeserializeNoConstructor …`. The cause is that JS interop's trimmer annotation stops at the **array** type and never reaches the element type, so any DTO marshaled as `T[]` loses its constructor and accessors. The control's own DTOs (`CameraDevice`, `CameraOverlayBox`, `CameraBarcode`) are rooted for you; if you add your own array-shaped interop, root the element type with `[DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties, typeof(YourDto))]` on the interop method, and verify against `obj/Release/<tfm>/linked/` rather than a debug run.
- **Writing an AGSL shader for a custom `INativeEffect`?** It compiles only on-device at API 33+, and a compile error is *not* thrown to you — the step is dropped and the effect silently does nothing, which is indistinguishable from "not supported here". Subscribe to `CameraView.CameraError`, which now reports the compile message. The trap that bit the built-ins: **`flat` is a reserved interpolation qualifier**, so `float3 flat = …` kills the whole shader. Same for `smooth`, `sample`, `varying`, `attribute`, `centroid`.
- **"Camera permission denied" but the preview works** — you're gating on `RequestPermissionAsync()` in `OnAppearing` before the handler is connected (it returns `false` → looks denied, and the early-return also leaves the lens list empty). Don't gate on it; rely on auto-start + `CameraError`, and load cameras once the view is loaded.
- **Android minSdk** — CameraX requires API 23+. Set `<SupportedOSPlatformVersion>` to 23 or higher in the consuming app or you'll get a manifest-merge error.
- **macOS** — best-effort host; multiple webcams enumerate via `GetAvailableCamerasAsync()`; FaceTime + USB devices report an `External`/unspecified facing, so use `CameraId` rather than `Facing` to choose.
- **Blazor barcode on Firefox/Safari** — `BarcodeDetector` is Chromium-only; feature-detect and provide a fallback if you need universal coverage. Filters (CSS) and capture/record work everywhere.
- **Overlay misaligned** — prefer `CameraOverlayView` (it tracks `ScaleMode`/aspect for you). If you hand-roll a `CameraOverlayDrawable`, set `ScaleMode` to match `CameraView.ScaleMode` and `ImageAspect` from `CameraOverlaysChangedEventArgs.ImageWidth/Height`.
- **Box flicker / not persisting** — an analyzer's boxes stay drawn until it returns a *different* set or `null`; return the same set (or nothing while busy) to keep them. Returning `null`/empty every frame clears them.

- **To hide the picture without stopping the camera, use `ShowPreview`, never `IsActive`.** They read alike and are not alike: `IsActive = false` tears the capture session down, which ends any recording in progress and needs the whole pipeline rebound to come back. `ShowPreview = false` touches only what is on screen — the session keeps running, frames keep reaching the analyzer, and the file keeps being written with its overlay burned in. Toggling it mid-recording is safe on every platform by construction: on Android it hides the `PreviewView` and makes no CameraX call at all, precisely because a rebind there would drop the recording.
- **Video quality lives on `CameraView`, not `VideoRecordingOptions`** — a common wrong guess. Both AVFoundation (session preset) and CameraX (`QualitySelector` on the `Recorder`) fix the capture resolution when the session is configured, so it cannot be a per-recording argument without reconfiguring the camera every time recording starts. Set `VideoQuality` / `VideoBitrate` / `VideoFrameRate` once, or bind them to a setting; a change while running reconfigures the session and is **ignored mid-recording** (it would truncate the file being written) — it lands on the next recording instead. On Apple the preset sizes the *whole* session, so dropping it also shrinks what the preview and any frame analyzer see.
- **iOS takes the camera away without telling you — the handler now notices.** AVFoundation suspends a capture session on a phone call, another app claiming the device, a second foreground app in Split View, or system thermal/power pressure. Nothing is raised by the framework: the preview layer holds its last frame and a running recording simply stops receiving any, so a `AVAssetWriter`-backed clip ends at the moment of the interruption while the app carries on looking healthy. `CameraView.CameraError` now fires with the reason, and the session is restarted on `AVCaptureSessionInterruptionEnded` (and on a `MediaServicesWereReset` runtime error) as long as `IsActive` is still `true`. Backgrounding is deliberately not reported — it raises the same notification on every app switch and is ordinary lifecycle — but it still resumes through the same path. **For unattended recording, handle `CameraError`**: it is the only signal that footage stopped.
- **Recording no longer evicts the user's audio on iOS, and their audio no longer kills the recording.** An `AVCaptureSession` configures and activates the app's shared `AVAudioSession` by default, and never with `MixWithOthers` — so starting a recording interrupted whatever was playing (music over CarPlay/Bluetooth, a podcast, a nav app), and anything starting playback afterwards interrupted the *capture session*, which stops **video** as well as audio. The handler now turns that automatic configuration off and sets `PlayAndRecord` + `MixWithOthers` itself, and only when `IncludeAudio` is set — a video-only recording touches the audio session not at all. Set `CameraView.MixWithOtherAudio = false` for the old exclusive behaviour, which is right only when a clean audio track beats the user's playback (the mic hears the speakers, and nothing removes that afterwards). `AllowBluetooth` (HFP) is deliberately never requested: it would drag a car or headset link into mono call quality mid-song; `AllowBluetoothA2DP` is output-only and costs nothing.
- **The camera follows the device now, and that is a behaviour change.** Every `AVCaptureConnection` defaults to portrait and only the frame-delivery one was ever oriented, so on Apple a landscape-held device previewed sideways, recorded sideways through the native movie path, and wrote portrait EXIF onto stills; on Android the target rotation was whatever the display happened to be at the moment the use cases were built, so launching already in landscape produced rotated files with nothing in the code saying so. `CameraView.Orientation` now defaults to `Device` and drives all of it. An app that relied on always-portrait output must set `Orientation = CameraOrientation.Portrait` explicitly.
- **Orientation covers the analyzer too, and that is not a leak — it is the point.** On Apple the video data output that feeds `Analyzer` is the same connection the burn-in recorder reads, so a recording cannot be oriented independently of what the analyzer sees. For a landscape-mounted device "upright" genuinely *is* landscape, and the analyzer should be looking at the same scene the file gets. What this does mean is that a **normalized `ScanWindow` is orientation-dependent**: a rect calibrated against a portrait frame lands somewhere else entirely on a landscape one, so an app that supports both needs a calibration per orientation rather than one shared set of percentages.
- **A change is deferred while recording, so pin it for segmented capture.** Re-orienting a connection transposes the pixel buffer, and an encoder is configured with fixed dimensions off its first frame — feeding it transposed buffers does not rotate the file, it corrupts it. The pending value is applied when the recording finishes. It follows that a caller doing *segmented* continuous capture (each segment its own `StartVideoRecordingAsync`) must pin an explicit `CameraOrientation` rather than leaving `Device`, or a rotation between segments yields files that disagree — and the usual way that surfaces is a later concat or trim taking its transform from the first track and rendering every other one sideways.
- **`LandscapeLeft` means the opposite thing on different platforms, which is why this enum does not use it.** `UIInterfaceOrientationLandscapeLeft` is `UIDeviceOrientationLandscapeRight` (Apple's own headers note it), and `AVCaptureVideoOrientation` follows the *interface* convention while most code reading a rotation holds a *device* one. `CameraOrientation.LandscapeTopLeft`/`LandscapeTopRight` name where the device's **top edge** physically points, and the handlers own the mapping. The explicit members assume a portrait-natural device on Android — true of every phone, not of every tablet; `Device` reads the display rotation and has no such assumption.
- **`Highest` is not free** — it resolves to 4K on modern hardware, which for a continuously-recording app means storage filling fast and a hot device. If size or thermals matter, drop `VideoFrameRate` before you drop resolution: halving the frame rate roughly halves encode work and costs far less perceptually on a mostly-static scene.

## When to Use What

- **Just take a photo / record video** → `CapturePhotoAsync` / `StartVideoRecordingAsync`; no analyzer needed.
- **Scan a barcode/QR** → set `Camera.Analyzer = new BarcodeAnalyzer()` (on Blazor, `Analyzer="@(new BarcodeAnalyzer())"`).
- **Restrict the scan to a band** → set the analyzer's `ScanWindow` (the overlay frames it as a viewfinder).
- **Detect/box faces** → set `Camera.Analyzer = new FaceAnalyzer()`.
- **Trigger on movement (security cam)** → set `Camera.Analyzer = new MotionAnalyzer()`, set `OnDetected` to `return true` (stay armed for continuous monitoring), and `Scan()` once to start.
- **Read raw text** → set `Camera.Analyzer = new OcrAnalyzer()` and handle `TextRecognized`.
- **Parse an invoice** → set `Camera.Analyzer = new InvoiceAnalyzer()` and handle `DocumentDetected` (`Invoice` with `.Lines`).
- **Parse a receipt** → set `Camera.Analyzer = new ReceiptAnalyzer()` and handle `DocumentDetected` (`Receipt` with `.Lines`, `.Taxes`, subtotal/tip/total).
- **Scan a business card** → set `Camera.Analyzer = new BusinessCardAnalyzer()` and handle `DocumentDetected` (`BusinessCard` — name/title/company + `.Emails` / `.Phones` / website).
- **Scan a driver's license / health card** → set `Camera.Analyzer` to `DriversLicenseAnalyzer` (deterministic AAMVA) or `HealthCardAnalyzer` from `.Camera.Documents`.
- **Scan a passport** → `PassportAnalyzer` (deterministic MRZ); **read a credit card** → `CreditCardAnalyzer` (brand+number deterministic).
- **Parse a free-form or unknown document with AI** → `Camera.Analyzer = new AiDocumentAnalyzer(chatClient)` from `.Camera.Ai` (detects presence, then sends one frame to a Microsoft.Extensions.AI `IChatClient`). On Blazor, assign a `DocumentAnalyzer` and drive it with `AiDocumentScanner`. Use a strongly-typed `AiDocumentAnalyzer<T>` when you have a fixed schema.
- **Offer a choice of detectors** → build them once, assign the chosen one to `Camera.Analyzer` (only one runs at a time).
- **Apply a live colour grade** → set `Filter` (or add the matching `CameraEffects.*` to `Effects`).
- **Apply a comic / sketch / blur look** → add `CameraEffects.Comic` (or `Sketch`/`Posterize`/`Pixelate`/`Blur`) to `Effects` — procedural, offline, live on the preview.
- **Ship your own look** → add a `ColorEffect` (matrix, works everywhere) or an `INativeEffect` with a `NativeEffectDescriptor` (per-backend GPU program) to `Effects`.
- **Stick something on the user's face** → `FaceAnalyzer { DetectLandmarks = true }` + a `FaceMaskEffect` in `Effects`.
- **Draw a watermark / timestamp into preview, photos and video** → add a `DelegateDrawEffect` to `Effects` (the older recording-only `VideoRecordingOptions.Overlay` still works).
- **"Redraw my photo as a comic" like the App Store apps** → add an `AiPhotoStylizer` (`.Camera.Ai`) to `Effects`; pair it with `CameraEffects.Comic` for a matching live viewfinder.
- **Pick a specific lens / webcam** → `GetAvailableCamerasAsync()` + `CameraId`.
