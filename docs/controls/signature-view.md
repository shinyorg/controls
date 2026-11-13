# SignatureView

[← All Shiny Controls](../../README.md)

An inline signature surface you can put anywhere — a form, a card, a table row, a resizable panel. Draw with a finger, pen or mouse, then export the ink as a PNG. [SignaturePad](signature-pad.md) is this same surface inside a FloatingPanel (MAUI) or SheetView (Blazor) with Sign and Cancel buttons; use SignatureView when the signature belongs on the page itself.

- **Scroll-safe.** Inside a scroller, a stroke on the surface never scrolls the page; a touch beside it still does. MAUI holds the `ScrollView` off for the length of the stroke (from the raw touch-down on iOS); Blazor uses `touch-action: none` on the canvas.
- **Edge-safe.** On Android 10+ the surface is excluded from the system Back edge-swipe, so a stroke that starts near the screen edge stays a stroke.
- **Guide, not ink.** `PlaceholderText` ("Sign here") and `ShowSignatureLine` (a "✕ ____" baseline) are drawn on screen only and never exported.
- **Exports fit.** `ExportToPng` / `ExportPngAsync` scale the ink uniformly to fit the export size and centre it on the paper, so a wide box exported to a squarer bitmap is letterboxed rather than stretched.
- **One edge.** `ShowBorder="False"` drops the surface's outline when it fills a container that already draws one.

```xml
<!-- MAUI -->
<shiny:SignatureView x:Name="Signature"
                     HeightRequest="180"
                     PlaceholderText="Sign here"
                     ShowSignatureLine="True" />

<Button Text="Submit"
        IsEnabled="{Binding HasSignature, Source={x:Reference Signature}}"
        Clicked="OnSubmit" />
```

```csharp
void OnSubmit(object? sender, EventArgs e)
{
    using var png = Signature.ExportToPng();          // ExportWidth x ExportHeight (600 x 200)
    // save / upload the stream
}
```

```razor
<!-- Blazor -->
<SignatureView @ref="signature"
               @bind-HasSignature="hasSignature"
               PlaceholderText="Sign here"
               ShowSignatureLine="true"
               Height="180px" />

<button disabled="@(!hasSignature)" @onclick="Submit">Submit</button>

@code {
    SignatureView? signature;
    bool hasSignature;

    async Task Submit()
    {
        byte[] png = await signature!.ExportPngAsync();
    }
}
```

| Property | Type | Default | Description |
|---|---|---|---|
| StrokeColor | Color / string | Black / `#000000` | The ink |
| StrokeWidth | double | 3 | Stroke width |
| SignatureBackgroundColor | Color / string | White / `#FFFFFF` | The paper. Not themed — it is part of the exported image |
| PlaceholderText | string? | null | Hint shown while empty. Never exported |
| ShowSignatureLine | bool | false | Draws a "✕ ____" baseline. Never exported |
| ShowClearButton | bool | true | A Clear button in the top corner once something is drawn |
| ClearButtonText | string | "Clear" | |
| IsReadOnly | bool | false | Shows the ink, takes no strokes, hides the clear button |
| ShowBorder | bool | true | Draws the outline. Turn off inside a card/border that already has one |
| BorderColor | Color? / string | theme outline-variant | |
| CornerRadius | double | 8 | |
| Height (Blazor) | string | `160px` | Any CSS length; `100%` fills a parent with a height. MAUI asks for 160 when nothing else sizes it (`DefaultHeight`) |
| ExportWidth / ExportHeight | int | 600 / 200 | Default export size in pixels |
| HasSignature | bool | false | Read-only bindable (MAUI) / `@bind-HasSignature` (Blazor) |
| StrokeCount | int | 0 | How many strokes are drawn |

| Member | MAUI | Blazor |
|---|---|---|
| Clear all strokes | `Clear()` | `ClearAsync()` |
| Remove the last stroke | `Undo()` → bool | `UndoAsync()` → bool |
| Export | `ExportToPng(int? width, int? height)` → `Stream` | `ExportPngAsync(int? width, int? height)` → `byte[]` |
| A stroke was kept | `StrokeCompleted` event / `StrokeCompletedCommand` | `StrokeCompleted` (`EventCallback<int>`, the stroke count) |
| The surface was emptied | `Cleared` event / `ClearedCommand` | `Cleared` |

`ExportToPng` returns `Stream.Null` on a plain `net10.0` backend that registers no `IBitmapExportService`; iOS, Mac Catalyst and Android draw natively. Blazor strokes are stored relative to the surface, so they follow it when it resizes.

### Automation ids (MAUI)

| Part | Id |
|---|---|
| Drawing surface | `<id>_Canvas` |
| Clear button | `<id>_Clear` |
