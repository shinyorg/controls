# IMediaService (MAUI only)

An injectable service for everything camera- and gallery-shaped, driving Shiny's **own modal `CameraView`
page** rather than the system camera UI. Ships in `Shiny.Maui.Controls.Camera`; registered by
`.UseShinyCamera()`.

> **MAUI only.** There is no Blazor `IMediaService` — the modal is a MAUI page, and the browser's file input
> already covers gallery picking. On Blazor use `<CameraView>` from `Shiny.Blazor.Controls.Camera` directly.
> Never emit `AddShinyMediaService`, `IMediaService` or `MediaPhoto` in Blazor code.

## When to reach for it

Use `IMediaService` when the app wants a **result** — a photo, a video, a barcode, a card — and does not
want to own a camera page. Use `CameraView` directly when the camera is part of a screen the app designs
itself (a live preview embedded in a page, a viewfinder with custom chrome, a continuous background
analyzer).

## Setup

```bash
dotnet add package Shiny.Maui.Controls.Camera
# add only the analyzers whose Scan… verbs you need
dotnet add package Shiny.Maui.Controls.Camera.Barcode
dotnet add package Shiny.Maui.Controls.Camera.Ocr
dotnet add package Shiny.Maui.Controls.Camera.Documents
dotnet add package Shiny.Maui.Controls.Camera.Face
```

```csharp
builder
    .UseShinyControls()
    .UseShinyCamera(media =>
    {
        media.CompressionQuality = 85;   // 1-100
        media.MaxDimension = 2048;       // longest edge, 0 = no downscale
        media.OutputFormat = MediaImageFormat.Jpeg;
    });
```

Namespaces: `Shiny.Maui.Controls.Camera.Media` (service + options + results), plus
`Shiny.Maui.Controls.Camera.Barcode` / `.Ocr` / `.Documents` / `.Face` for the `Scan…` extensions.

Inject it like any service:

```csharp
public class ScanViewModel(IMediaService media) { … }
```

## API

| Member | Returns | Notes |
|---|---|---|
| `IsCameraSupported` | `bool` | False on bare `net10.0` and before the app has a window |
| `Options` | `MediaServiceOptions` | Live; changing it affects later calls |
| `RequestCameraPermissionAsync(includeMicrophone = false)` | `MediaPermissionStatus` | Weakest of the requested permissions |
| `RequestGalleryPermissionAsync(forWrite = false)` | `MediaPermissionStatus` | `forWrite` asks for add-to-library, not read |
| `OpenSettingsAsync()` | `Task` | The way back from a refusal |
| `GetAvailableCamerasAsync()` | `IReadOnlyList<CameraInfo>` | Feed an `Id` to `MediaCameraOptions.CameraId` |
| `TakePhotoAsync(PhotoCaptureOptions?)` | `MediaPhoto?` | |
| `RecordVideoAsync(VideoCaptureOptions?)` | `MediaVideo?` | |
| `PickPhotoAsync(MediaPickOptions?)` | `MediaPhoto?` | |
| `PickPhotosAsync(maxCount = 10, MediaPickOptions?)` | `IReadOnlyList<MediaPhoto>` | |
| `PickVideoAsync(MediaPickOptions?)` | `MediaVideo?` | |
| `ScanAsync<T>(MediaScanRequest<T>, MediaScanOptions?)` | `IAsyncEnumerable<T>` | The primitive the `Scan…` verbs are built on |

**Everything returns `null` (or an empty list) on cancel or refusal — never an exception.** Do not generate
try/catch around these for the cancel case.

`MediaPermissionStatus` = `Granted` / `Denied` / `Restricted` / `Unsupported`. There is no
`PermanentlyDenied`.

`MediaPhoto(byte[] Data, int Width, int Height, string ContentType)` with `OpenRead()`, `AsImageSource()`,
`SaveAsync(path)`. `MediaVideo(string FilePath, TimeSpan? Duration, string ContentType)` with `OpenRead()`
and `Length`.

## Capture

```csharp
var photo = await media.TakePhotoAsync(new PhotoCaptureOptions
{
    Title = "Proof of delivery",
    Instructions = "Fit the whole label in frame",
    CompressionQuality = 80,
    MaxDimension = 2048,
    ShowEffectPicker = true
});

if (photo is not null)
    this.Preview = photo.AsImageSource();
```

```csharp
var video = await media.RecordVideoAsync(new VideoCaptureOptions
{
    Quality = VideoQuality.Medium,
    MaxDuration = TimeSpan.FromSeconds(30),
    IncludeAudio = true
});
```

## Scanning

Each analyzer package adds a **singular** verb (`Task<T?>`, modal closes on the first hit) and a **plural**
one (`IAsyncEnumerable<T>`, modal stays up and streams until ✓ / `MaxResults` / `Timeout` / the caller stops
enumerating).

```csharp
// one code
var code = await media.ScanBarcodeAsync();

// stream, duplicate-filtered on symbology + value
await foreach (var code in media.ScanBarcodesAsync(filterDuplicates: true))
    this.Codes.Add(code.Value);

// symbologies + an aiming band
var qr = await media.ScanBarcodeAsync(
    [BarcodeFormat.QrCode],
    new MediaScanOptions { ScanWindow = new RectF(0.1f, 0.38f, 0.8f, 0.24f) }
);

var card     = await media.ScanCreditCardAsync();
var licence  = await media.ScanDriversLicenseAsync();   // PDF417 on the BACK of the card
var passport = await media.ScanPassportAsync();
var contact  = await media.ScanBusinessCardAsync();
var text     = await media.ScanTextStringAsync();
var faces    = await media.DetectFaceAsync();
```

| Package | Singular | Plural |
|---|---|---|
| `.Camera.Barcode` | `ScanBarcodeAsync` | `ScanBarcodesAsync` |
| `.Camera.Ocr` | `ScanTextAsync`, `ScanTextStringAsync` | `ScanTextBlocksAsync` |
| `.Camera.Documents` | `ScanCreditCardAsync`, `ScanDriversLicenseAsync`, `ScanPassportAsync`, `ScanHealthCardAsync`, `ScanReceiptAsync`, `ScanInvoiceAsync`, `ScanBusinessCardAsync` | the same names pluralized, plus generic `ScanDocumentsAsync<TDocument>(analyzer, …)` |
| `.Camera.Face` | `DetectFaceAsync` | `DetectFacesAsync` |

`filterDuplicates` defaults to `true` (`false` for faces) and **wins over**
`MediaScanOptions.FilterDuplicates` when both are given.

For an analyzer with no shipped verb, build the request yourself:

```csharp
var analyzer = new MyAnalyzer();
await foreach (var hit in media.ScanAsync(new MediaScanRequest<MyResult>
{
    Analyzer = analyzer,
    Subscribe = emit => analyzer.OnDetected = args => { emit(args.Result); return Task.FromResult(true); },
    DuplicateKey = r => r.Id,
    Describe = r => r.Name
}))
    …
```

`OnDetected` must return `true` — the service, not the analyzer, decides when to stop.

## Options

Shared by every modal (`MediaCameraOptions`):

| Property | Default | Notes |
|---|---|---|
| Title / Instructions | null | The **only** text the modal shows; supply it already localized |
| Facing / CameraId | `Back` / null | `CameraId` wins |
| AllowCameraSwitch / AllowTorch / IsTorchOn | true / true / false | |
| Zoom | 1 | Applied once the lens range is known; clamped to `MaxZoom` |
| AllowZoom | true | `false` disables pinch **and** pins the range shut |
| MaxZoom | null | Ceiling; never raises a weaker lens, never falls below its minimum |
| ScaleMode | `AspectFill` | |
| Filter / Effects | `None` / empty | Opening look |
| ShowEffectPicker / EffectChoices | false / `MediaEffectChoices.Default` | On-screen look strip |
| PermissionDeniedText | a sentence | Shown over the preview on refusal |
| ConfigureCamera / ConfigurePage | null | Escape hatches |

`PhotoCaptureOptions` adds `Quality` (`PhotoQuality.Highest`), `CompressionQuality`, `MaxDimension`,
`OutputFormat`, `FlashMode`, `AllowFlashToggle`, `ShowConfirmation`.
`VideoCaptureOptions` adds `Quality`, `IncludeAudio`, `MaxDuration`, `Bitrate`, `FrameRate`, `FilePath`,
`Overlay`, `ShowElapsed`.
`MediaScanOptions` adds `ScanWindow`, `ShowBoundingBox`, `FilterDuplicates`, `MaxResults`, `Timeout`,
`ShowResultCount`, `ShowDoneButton`, `VibrateOnResult`.

**`CompressionQuality`, `MaxDimension` and `OutputFormat` are nullable** on the options — leave them unset
to inherit the `UseShinyCamera(...)` defaults. Do not set them to a literal just to "be explicit"; that
overrides the app's house style.

## Rules for generated code

- Inject `IMediaService`; never `new MediaService(...)`.
- Never construct `MediaCapturePage` — it is internal.
- A **scan modal has no capture button**; do not offer or document one.
- The modal's buttons are drawn icons, so there are no `CancelText` / `DoneText` / `ConfirmText`
  properties. Do not invent them.
- Use `await foreach` for the plural verbs; use the singular verb rather than
  `ScanXsAsync().FirstOrDefaultAsync()`.
- Turn `AllowZoom` off for document and OCR scans — a zoomed frame is a cropped one.
- Barcode and OCR are a no-op on Windows and the bare `net10.0` head (no native scanner), exactly as for
  `CameraView`.
