# IMediaService

[← All Shiny Controls](../../README.md)

Ships in `Shiny.Maui.Controls.Camera` (**MAUI only**). Registered by `UseShinyCamera()`. The `Scan…`
extensions come from the analyzer add-ons (`.Camera.Barcode`, `.Camera.Ocr`, `.Camera.Documents`,
`.Camera.Face`) — install the ones you need and the matching verbs appear on the service.

<!-- TODO: capture screenshots for media-service -->

One injectable service for everything camera- and gallery-shaped: permissions, taking a photo or video
through Shiny's **own** modal `CameraView` page, picking from the gallery, and scanning barcodes, text,
credit cards, driver's licenses, passports, health cards, receipts, invoices, business cards and faces.

The point of difference against MAUI's `IMediaPicker` is the modal. It is a page built from `CameraView`, so
it carries a scan reticle, live bounding boxes, an effect strip, your title and your instructions — none of
which the system camera UI can do, which is why apps that need any of it end up hand-rolling a camera page.
That page is what this service is.

```csharp
public class ExpenseViewModel(IMediaService media)
{
    public async Task AddReceiptPhoto()
    {
        var photo = await media.TakePhotoAsync(new PhotoCaptureOptions
        {
            Title = "Receipt",
            Instructions = "Fit the whole receipt in frame",
            CompressionQuality = 80,
            MaxDimension = 2048
        });

        if (photo is not null)
            await photo.SaveAsync(Path.Combine(FileSystem.AppDataDirectory, "receipt.jpg"));
    }
}
```

House defaults are set once at registration and every call inherits them:

```csharp
builder.UseShinyCamera(media =>
{
    media.CompressionQuality = 85;
    media.MaxDimension = 2048;
    media.OutputFormat = MediaImageFormat.Jpeg;
    media.ConfigureDefaults = o => o.Instructions ??= "Hold steady";
});
```

## The modal

Everything on the modal is a **drawn vector icon**, not a font glyph and not an emoji: a glyph needs a font
the package cannot assume, and an emoji renders at a different size, weight and colour on every head. The
practical consequence is that the modal ships **no localizable strings of its own** — the only text on it is
the `Title` and `Instructions` your app supplies, already localized.

| Chrome | Photo | Video | Scan |
|---|:-:|:-:|:-:|
| Close ✕ | ● | ● (hidden while recording) | ● |
| Torch (`AllowTorch`, `IsTorchOn`) | ● | ● | ● |
| Flip camera (`AllowCameraSwitch`) | ● | ● | ● |
| Flash auto/on/off (`AllowFlashToggle`) | ● | — | — |
| Shutter / record button | ● | ● | **absent** |
| Accept ✓ | review only | — | ● (`ShowDoneButton`) |
| Effect strip (`ShowEffectPicker`) | ● | ● | ● |
| Result count | — | elapsed time | ● (`ShowResultCount`) |

A scan modal has **no capture button at all** — not merely hidden. The camera is simply on and streaming
results, and a shutter would invite a tap with nothing for a still to be the result of.

## Zoom

`Zoom` sets the opening factor, `MaxZoom` caps how far the user can get, and `AllowZoom = false` turns it
off entirely.

```csharp
// a document scan: no zoom at all, because a zoomed frame is a cropped one
await media.ScanReceiptAsync(new MediaScanOptions { AllowZoom = false });

// a photo that can reach 4x and no further
await media.TakePhotoAsync(new PhotoCaptureOptions { MaxZoom = 4, Zoom = 2 });
```

All three are applied against the range the **handler** reports, not guessed up front, because
`CameraView.MaxZoom` is 1 until the lens has been opened and interrogated. So a `Zoom` beyond the device's
reach lands at its maximum rather than being dropped; `MaxZoom` is a ceiling that never raises a weaker
lens above what it can do, and never falls below its minimum; and they are re-applied when a lens switch
republishes the range. `AllowZoom = false` pins the usable range shut rather than only disabling the pinch
gesture — otherwise a `ConfigureCamera` hook or a binding could still zoom past 1×, and the option would be
a UI hint rather than a rule.

## Permissions

```csharp
var status = await media.RequestCameraPermissionAsync(includeMicrophone: true);
if (status == MediaPermissionStatus.Denied)
    await media.OpenSettingsAsync();
```

`MediaPermissionStatus` is `Granted` / `Denied` / `Restricted` / `Unsupported`. iOS's *limited* photo
selection counts as `Granted` — the user chose what you may see, and you may proceed. There is deliberately
no `PermanentlyDenied`: whether a second request re-prompts is not knowable the same way on both platforms,
so a value claiming to mean it would be right on one head and a guess on the other. Treat `Denied` as "offer
them `OpenSettingsAsync`", which is correct either way.

Every method that presents UI requests what it needs first and returns `null` on refusal or cancellation —
a cancelled camera is an ordinary outcome, not an exceptional one, so nothing here throws for it.

`RequestGalleryPermissionAsync(forWrite: true)` asks for the add-to-library grant rather than the read one;
on iOS these are separate, and asking for read when you only intend to save asks for more than you need.

## Capture and gallery

| Method | Returns |
|---|---|
| `TakePhotoAsync(PhotoCaptureOptions?)` | `MediaPhoto?` |
| `RecordVideoAsync(VideoCaptureOptions?)` | `MediaVideo?` |
| `PickPhotoAsync(MediaPickOptions?)` | `MediaPhoto?` |
| `PickPhotosAsync(maxCount, MediaPickOptions?)` | `IReadOnlyList<MediaPhoto>` |
| `PickVideoAsync(MediaPickOptions?)` | `MediaVideo?` |
| `GetAvailableCamerasAsync()` | `IReadOnlyList<CameraInfo>` |

`MediaPhoto` carries the encoded bytes plus `Width`/`Height`/`ContentType`, with `OpenRead()`,
`AsImageSource()` and `SaveAsync(path)`. `MediaVideo` stays a **file** — a minute of 1080p is hundreds of
megabytes and nothing good comes of holding that in memory.

### Compression

`CompressionQuality` (1–100), `MaxDimension` and `OutputFormat` (`Jpeg`/`Png`) are **nullable** on the
options, so `null` means "use the service default" and there is no way for a call site to accidentally
override a house setting it never meant to touch. `MaxDimension` is the one that actually shrinks a file —
a 12MP capture stays multi-megabyte at any compression rate. Nothing is re-encoded when nothing was asked
for: a full-size JPEG capture at quality 100 is handed straight through.

### Photo options

| Property | Default | Notes |
|---|---|---|
| Quality | `PhotoQuality.Highest` | Full sensor resolution; `Session` for scan-shaped captures |
| CompressionQuality | service default (92) | 1–100, ignored for PNG |
| MaxDimension | service default (0) | 0 keeps the captured size |
| OutputFormat | service default (Jpeg) | `Jpeg` or `Png` |
| FlashMode / AllowFlashToggle | `Auto` / `true` | |
| ShowConfirmation | `true` | Review with retake ✕ / accept ✓ before returning |

### Video options

`Quality` (default `High`/1080p), `IncludeAudio` (default `true` — which is why the service also asks for
the microphone), `MaxDuration` (stops itself), `Bitrate`, `FrameRate`, `FilePath`, `Overlay`
(an `IVideoOverlayRenderer` burned into every recorded frame) and `ShowElapsed`.

## Scanning

Each analyzer package contributes a singular verb returning `Task<T?>` — the modal closes on the first
result — and a plural one returning `IAsyncEnumerable<T>`, where the modal stays up and streams until the
user taps ✓, the caller stops enumerating, or `MaxResults`/`Timeout` is reached.

```csharp
// one code, then the modal closes
var code = await media.ScanBarcodeAsync();

// stream until the user is done
await foreach (var code in media.ScanBarcodesAsync(filterDuplicates: true))
    this.Codes.Add(code.Value);

// restrict symbologies and aim with a band
var qr = await media.ScanBarcodeAsync(
    [BarcodeFormat.QrCode],
    new MediaScanOptions { ScanWindow = new RectF(0.1f, 0.38f, 0.8f, 0.24f) }
);

var card = await media.ScanCreditCardAsync();
var licence = await media.ScanDriversLicenseAsync();   // the PDF417 on the BACK of the card
var text = await media.ScanTextStringAsync();
```

| Package | Verbs |
|---|---|
| `.Camera.Barcode` | `ScanBarcodeAsync`, `ScanBarcodesAsync` |
| `.Camera.Ocr` | `ScanTextAsync`, `ScanTextStringAsync`, `ScanTextBlocksAsync` |
| `.Camera.Documents` | `ScanCreditCard(s)Async`, `ScanDriversLicense(s)Async`, `ScanPassport(s)Async`, `ScanHealthCard(s)Async`, `ScanReceipt(s)Async`, `ScanInvoice(s)Async`, `ScanBusinessCard(s)Async`, and the generic `ScanDocumentsAsync<TDocument>(analyzer, …)` |
| `.Camera.Face` | `DetectFaceAsync`, `DetectFacesAsync` |

### Duplicate filtering

`filterDuplicates` (default `true`, except for faces) suppresses a result whose key was already returned in
this session — a code sitting in front of the lens is otherwise re-read every time it drifts out of view and
back. Keys are chosen per type: symbology + value for a barcode, the card number for a credit card, the
license number for a license, merchant + date + total for a receipt (which has no reliable identifier).
The argument **wins** over `MediaScanOptions.FilterDuplicates` when both are supplied, so the shorter
spelling is never the one that silently does nothing.

### Scan options

`ScanWindow` (normalized rect — restricts detection *and* draws the viewfinder reticle), `ShowBoundingBox`,
`MaxResults`, `Timeout`, `ShowResultCount`, `ShowDoneButton`, `VibrateOnResult`.

### Scanning with your own analyzer

`IMediaService` knows nothing about symbologies or documents; the typed verbs above are each one call to
`ScanAsync<T>`. Use it directly for an analyzer Shiny does not ship a verb for:

```csharp
var analyzer = new MyAnalyzer();
await foreach (var hit in media.ScanAsync(new MediaScanRequest<MyResult>
{
    Analyzer = analyzer,
    Subscribe = emit => analyzer.OnDetected = args => { emit(args.Result); return Task.FromResult(true); },
    DuplicateKey = r => r.Id,
    Describe = r => r.Name
}))
    ...
```

`Subscribe` exists because analyzers deliver through their own strongly-typed `OnDetected`, which the
service cannot see without knowing the analyzer's type — so the code that *does* know it does the wiring,
and everything else (permissions, presentation, arming, duplicate filtering, cancellation, teardown) is
written once. Set `OnDetected` to return `true`; the service decides when to stop.

## Effects

`ShowEffectPicker` puts a strip of looks over the preview, tapping through `MediaEffectChoices.Default`
("None", the eleven colour grades, then Comic / Sketch / Poster / Pixelate / Blur) or your own
`EffectChoices` list. `Filter` and `Effects` set the opening look without offering the picker. The chosen
look is baked into the capture, not just the preview. Leave the picker off for scanning — a stylized frame
is actively unhelpful to a detector.

## Escape hatches

`ConfigureCamera` is handed the modal's `CameraView` after it is configured and before it starts;
`ConfigurePage` is handed the page itself before presentation. Reach for them when you need something these
option classes do not surface.

## Platform notes

- **MAUI only.** Blazor has no equivalent: the modal is a MAUI page, and the browser's file input already
  covers gallery picking. Use `Shiny.Blazor.Controls.Camera` directly there.
- Frames, analyzers and capture come from `CameraView`, so every platform note in
  [CameraView](camera.md) applies — including that barcode and OCR are a no-op on Windows and the bare
  `net10.0` head, where there is no native scanner.
- `IsCameraSupported` is false where there is no camera to present, which includes before the app has a
  window.
