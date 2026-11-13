# SignatureView

An inline signature surface for both hosts (**core** packages): put it anywhere on a page — a form, a card, a table row, a resizable panel — draw with finger/pen/mouse, export a PNG. `SignaturePad` is this same surface inside a FloatingPanel (MAUI) / SheetView (Blazor) with Sign/Cancel buttons; generate `SignatureView` whenever the signature belongs on the page rather than in a popup.

- MAUI: `Shiny.Maui.Controls.SignaturePad.SignatureView` (`shiny:` xmlns). **No** `OverlayHost` / `ShinyContentPage` needed.
- Blazor: `Shiny.Blazor.Controls.SignatureView`.

## MAUI

```xml
<ScrollView>
    <VerticalStackLayout Padding="16" Spacing="12">
        <Label Text="Customer signature" FontAttributes="Bold" />
        <shiny:SignatureView x:Name="Signature"
                             HeightRequest="180"
                             PlaceholderText="Sign here"
                             ShowSignatureLine="True"
                             StrokeCompleted="OnStroke"
                             Cleared="OnCleared" />
        <Button Text="Submit"
                IsEnabled="{Binding HasSignature, Source={x:Reference Signature}}"
                Clicked="OnSubmit" />
    </VerticalStackLayout>
</ScrollView>
```

```csharp
void OnSubmit(object? sender, EventArgs e)
{
    using var stream = Signature.ExportToPng();            // ExportWidth x ExportHeight, default 600 x 200
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    var png = ms.ToArray();
    // An ImageSource needs a fresh stream per read
    Preview.Source = ImageSource.FromStream(() => new MemoryStream(png));
}
```

Inside a bordered container, drop the surface's own outline so the edges do not double up:

```xml
<Border StrokeShape="RoundRectangle 12" Stroke="#D97706" StrokeThickness="2" Padding="0">
    <shiny:SignatureView ShowBorder="False" CornerRadius="0" HeightRequest="120" />
</Border>
```

## Blazor

```razor
<SignatureView @ref="signature"
               @bind-HasSignature="hasSignature"
               PlaceholderText="Sign here"
               ShowSignatureLine="true"
               Height="180px"
               StrokeCompleted="count => strokes = count"
               Cleared="() => strokes = 0" />

<button disabled="@(!hasSignature)" @onclick="Submit">Submit</button>
<button disabled="@(!hasSignature)" @onclick="() => signature!.UndoAsync()">Undo</button>

@code {
    SignatureView? signature;
    bool hasSignature;
    int strokes;

    async Task Submit()
    {
        byte[] png = await signature!.ExportPngAsync();     // empty when nothing is drawn
    }
}
```

## Properties

| Property | MAUI type | Blazor type | Default | Notes |
|---|---|---|---|---|
| StrokeColor | Color | string | Black / `#000000` | The ink |
| StrokeWidth | double | double | 3 | |
| SignatureBackgroundColor | Color | string | White / `#FFFFFF` | Paper; NOT themed (part of the export) |
| PlaceholderText | string? | string? | null | On screen only |
| ShowSignatureLine | bool | bool | false | "✕ ____" baseline, on screen only |
| ShowClearButton | bool | bool | true | Shown once something is drawn |
| ClearButtonText | string | string | "Clear" | |
| IsReadOnly | bool | bool | false | Shows ink, takes no strokes, hides Clear |
| ShowBorder | bool | bool | true | Turn off inside a container that draws an edge |
| BorderColor | Color? | string | theme outline-variant | |
| CornerRadius | double | double | 8 | |
| Height | (use HeightRequest) | string | `160px` | MAUI asks for 160 (`SignatureView.DefaultHeight`) when unsized |
| ExportWidth / ExportHeight | int | int | 600 / 200 | |
| HasSignature | bool (read-only bindable) | bool (`@bind-HasSignature`) | false | |
| StrokeCount | int (read-only bindable) | int (property) | 0 | |

Methods: MAUI `Clear()`, `Undo()` → bool, `ExportToPng(int? width = null, int? height = null)` → `Stream`. Blazor `ClearAsync()`, `UndoAsync()` → `Task<bool>`, `ExportPngAsync(int? width = null, int? height = null)` → `Task<byte[]>`.

Events: MAUI `StrokeCompleted`, `Cleared` (+ `StrokeCompletedCommand`, `ClearedCommand`). Blazor `StrokeCompleted` (`EventCallback<int>`, the stroke count), `Cleared`, `HasSignatureChanged`.

## Code Generation Guidance

- Prefer SignatureView for a signature that is part of a form; SignaturePad for a "tap to sign" popup.
- It is scroll-safe: a stroke holds an enclosing ScrollView off (MAUI) / the canvas is `touch-action:none` (Blazor). It can sit inside a `ScrollView` or a scrolling page — that is the intended use.
- Bind a Submit button's `IsEnabled` to `HasSignature` (MAUI `{Binding HasSignature, Source={x:Reference ...}}`, Blazor `@bind-HasSignature`).
- Export scales the ink uniformly to fit the export size and centres it (letterboxed with the paper colour); the placeholder and line are never exported.
- `ExportToPng` returns `Stream.Null` on plain `net10.0` without an `IBitmapExportService`.
- On MAUI, give it a height (`HeightRequest` or a `*` row); on Blazor use `Height` (`100%` to fill a sized parent).

### Automation ids (MAUI)

| Part | Id |
|---|---|
| Drawing surface | `<id>_Canvas` |
| Clear button | `<id>_Clear` |
