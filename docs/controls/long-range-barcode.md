# Long-Range Barcode Scanning

`BarcodeAnalyzer` has a second mode for codes the native scanners give up on: across an aisle, on a high
shelf, at an angle, a little out of focus. **MAUI only** (iOS, Android, Mac Catalyst, macOS, Windows).

```xml
<cam:BarcodeAnalyzer Mode="LongRange" AutoZoom="True" />
```

- **Package**: `Shiny.Maui.Controls.Camera.Barcode`, the same analyzer and package as ordinary scanning
- **Engine**: `Shiny.Controls.BarcodeReader.Shared`, dependency-free and usable without a camera
- **Same results**: `BarcodesDetectedEventArgs`, `OnDetected`, `BarcodesDetectedCommand`, `Formats` and
  `ScanWindow` behave exactly as in `Standard` mode

## Why the native scanners miss distant codes

Apple Vision and Android ML Kit are handed the whole frame and shrink it before they look at it. A UPC
three metres away might be 60 px wide in a 1080p frame: after that shrink its bars are thinner than a
pixel, and no decoder can read bars that aren't there. Android is worse by default, because CameraX analyzes at
**640x480** unless asked otherwise.

## What it does instead

Every frame:

1. **Locate.** A structure-tensor pass over the full-resolution luminance plane finds regions of parallel
   bars (1D) and of busy, direction-less texture (2D) without decoding anything. It takes a few milliseconds.
2. **Read 1D codes in managed code.** EAN-13, UPC-A, EAN-8, UPC-E, Code 128, Code 39 and ITF are read along
   a fan of up to 15 scanlines. Each line is sampled every half pixel and averaged along the bars, and edges
   are placed to a fraction of a pixel. Characters are matched by *edge-to-similar-edge* distance, which blur
   and ink spread cancel out of. A line that fails is retried sharpened. Lines vote, EAN digits are fused
   across lines, and a check-digit tie-break catches the 1/7 and 2/8 swaps that blur causes.
3. **Hand 2D codes to the native decoder, enlarged.** QR, Data Matrix, Aztec and PDF417 candidates, plus 1D
   codes the engine could not read, are cut out of the full frame, enlarged and given to Vision / ML Kit on
   their own. At most `MaxNativeCropsPerFrame` go per frame, least recently tried first.
4. **Track.** Codes are followed across frames. A code with a check digit reports on the first frame; one
   without (Code 39, short ITF) must read on `UncheckedConfirmations` frames. A confirmed code is not sent
   to the native decoder again while it stays in view.
5. **Zoom** (opt-in). A code that has been found but is too small to read pulls `CameraView.Zoom` in until it
   can be read. Zoom is capped so the code stays in frame, held while anything is in view, and returned once
   the scene has been empty for a moment. A pinch or a bound slider moving the zoom takes control back at once.

The native scanner still runs over the whole frame every `FullFrameInterval` frames, which catches very close
codes and symbologies the locator does not frame well (Code 93, Codabar). A big, close code can therefore read a
frame or two later than in `Standard` mode, which runs the native scanner on every frame. That is why
`Standard` is still the default.

## Usage

```xml
<cam:CameraView IsPinchToZoomEnabled="True">
    <cam:BarcodeAnalyzer Mode="LongRange"
                         AutoZoom="True"
                         Formats="Ean13,UpcA,Code128"
                         OnDetected="{Binding OnBarcodes}" />
</cam:CameraView>
```

```csharp
public async Task<bool> OnBarcodes(BarcodesDetectedEventArgs args)
{
    foreach (var code in args.Barcodes)
        await this.Lookup(code.Value);
    return true; // keep scanning
}
```

As with every analyzer, results are only delivered while armed: call `CameraView.Scan()` (or bind
`ScanCommand`). Return `true` from `OnDetected` to keep scanning.

From `IMediaService`, pass a `BarcodeScanOptions` (a `MediaScanOptions` with the barcode settings added):

```csharp
var code = await media.ScanBarcodeAsync(options: new BarcodeScanOptions
{
    Mode = BarcodeScanMode.LongRange,
    AutoZoom = true
});
```

**Analysis resolution.** On Android, CameraX analyzes at 640x480 unless asked otherwise. In `LongRange` mode
the analyzer raises its camera's `AnalysisResolution` from `Default` to `FullHd` when it is attached (or when
the mode is switched on). If you set `AnalysisResolution` yourself, it is left alone. On Apple the analyzer
sees the session's own frames, sized by `VideoQuality` (1080p by default).

## Properties

| Property | Default | |
|---|---|---|
| `Mode` | `Standard` | `LongRange` turns on everything on this page (bindable). |
| `Formats` | all | Symbologies to read; comma list in XAML. Narrowing it is the biggest speed-up, and a 1D-only list skips the native crop pass entirely. |
| `AutoZoom` | `false` | Zoom in on codes found but too small to read (bindable). |
| `MaxAutoZoom` | `4` | Furthest auto-zoom goes, as a multiple of the zoom the user had. |
| `FullFrameInterval` | `3` | Run the native scanner on the whole frame every N frames (0 = never). |
| `MaxNativeCropsPerFrame` | `2` | Candidates sent to Vision / ML Kit per frame. |
| `UncheckedConfirmations` | `2` | Frames a code with no check digit must read on before it is reported. |
| `ShowCandidates` | `true` | Draw a faint box around codes found but not yet read. |
| `BoxColor` / `CandidateColor` | green / amber | Overlay colors. |
| `ScanWindow` | full frame | Restrict scanning to a normalized region; the locator only searches inside it. |
| `OnDetected`, `BarcodesDetectedCommand`, `BarcodesDetected`, `OverlayProvider` | | Same as `Standard` mode. |

Every setting except `Formats` and the result members is ignored in `Standard` mode.

`BarcodeScanner.ScanLuminanceAsync(bytes, width, height)` is the native-decoder entry point the analyzer uses
for crops. It is public, for custom analyzers that want to hand a
region to Vision / ML Kit themselves.

## Measured envelope

From `tests/Shiny.Controls.BarcodeReader.Benchmark` (synthetic EAN-13 and Code 128 at random angles on a
1280x720 frame, noise ±4 grey levels, contrast 120; exact payload match required):

| Module size | No blur | Blur σ 0.4 px | Blur σ 0.7 px |
|---|---|---|---|
| ≥ 1.6 px | 100% | 100% | 100% |
| 1.3 px | 100% | 100% | 66–100% |
| 1.1 px | 100% | 91–100% | 0% |
| 1.0 px | ~25% | ~8–33% | 0% |

Frame time on an Apple-silicon Mac: median 3.2 ms per 720p frame for locate + 1D decode. ZXing.Net on the
same frames ran at 5.4 ms and read 0–50% (its 1D readers only scan near-horizontal or near-vertical).
Below about one pixel per module a single frame does not hold the code. That is the job of `AutoZoom`.

**Not yet measured:** the head-to-head against Vision / ML Kit on real devices, and real photographs. The
benchmark takes a folder of photos (`dotnet run -c Release -- --images <folder>`, expected payload taken from
the file name up to `__`). Those numbers are the ones to trust.

## Not yet

- No Blazor version. The Blazor `CameraView` still uses the browser's `BarcodeDetector`.
- Not yet the default mode: that waits on device timing showing it is no slower for close codes.
- Heavy defocus (blur above about 0.6× the module width) defeats the edge-based decoder. A model-based decoder
  that fits blurred templates to the signal is the next step.
- Codabar and Code 93 are left to the native decoder.
