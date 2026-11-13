# SignaturePad

A signature capture control that opens in a FloatingPanel overlay (MAUI) or SheetView (Blazor). The user draws on a canvas and taps Sign to export the signature as a PNG image. The Sign button is disabled until the user actually draws something. Like FloatingPanel, the MAUI SignaturePad **must** be placed inside an `OverlayHost` or `ShinyContentPage` — it uses a FloatingPanel internally.

The drawing surface inside the sheet is a `SignatureView` (see signature-view.md). For a signature that belongs on the page itself — a form field, a card — generate `SignatureView` directly instead of a SignaturePad.

## Important: Placement Requirement (MAUI)

SignaturePad uses a FloatingPanel internally, so it **must** live inside an `OverlayHost` or `ShinyContentPage.Panels` — just like a standalone FloatingPanel. Placing it outside an overlay host will not display correctly.

## Basic Usage with ShinyContentPage (MAUI)

```xml
<shiny:ShinyContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
                         xmlns:shiny="http://shiny.net/maui/controls"
                         x:Class="MyApp.SignaturePage">
    <shiny:ShinyContentPage.PageContent>
        <VerticalStackLayout Padding="20" Spacing="10">
            <Button Text="Capture Signature" Command="{Binding OpenSignatureCommand}" />
            <Image Source="{Binding SignatureImage}"
                   HeightRequest="150"
                   Aspect="AspectFit"
                   IsVisible="{Binding HasSignature}" />
        </VerticalStackLayout>
    </shiny:ShinyContentPage.PageContent>
    <shiny:ShinyContentPage.Panels>
        <shiny:SignaturePad IsOpen="{Binding IsSignatureOpen}"
                            StrokeColor="Black"
                            SignatureBackgroundColor="#F8F8F8"
                            StrokeWidth="3"
                            SignButtonColor="#6C63FF"
                            CancelButtonColor="#94A3B8"
                            SignCommand="{Binding HandleSignedCommand}"
                            CancelCommand="{Binding HandleCancelledCommand}" />
    </shiny:ShinyContentPage.Panels>
</shiny:ShinyContentPage>
```

## Basic Usage with OverlayHost (MAUI)

```xml
<ContentPage>
    <Grid>
        <ScrollView>
            <!-- page content -->
            <Button Text="Capture Signature" Command="{Binding OpenSignatureCommand}" />
        </ScrollView>

        <shiny:OverlayHost>
            <shiny:SignaturePad IsOpen="{Binding IsSignatureOpen}"
                                StrokeColor="Black"
                                SignCommand="{Binding HandleSignedCommand}" />
        </shiny:OverlayHost>
    </Grid>
</ContentPage>
```

## Blazor Usage

```razor
<button @onclick="() => isOpen = true">Capture Signature</button>

@if (signatureDataUrl != null)
{
    <img src="@signatureDataUrl" alt="Captured signature"
         style="max-width:100%;border:1px solid #E5E7EB;border-radius:8px;" />
}

<SignaturePad @bind-IsOpen="isOpen"
              StrokeColor="#000000"
              SignatureBackgroundColor="#F8F8F8"
              StrokeWidth="3"
              SignButtonColor="#6C63FF"
              CancelButtonColor="#94A3B8"
              Signed="OnSigned"
              Cancelled="OnCancelled" />

@code {
    bool isOpen;
    string? signatureDataUrl;

    void OnSigned(byte[] pngBytes)
    {
        var base64 = Convert.ToBase64String(pngBytes);
        signatureDataUrl = $"data:image/png;base64,{base64}";
    }

    void OnCancelled() { }
}
```

## SignaturePad Properties (MAUI)

| Property | Type | Default | Binding Mode | Description |
|---|---|---|---|---|
| `IsOpen` | `bool` | `false` | TwoWay | Opens/closes the signature panel |
| `Position` | `FloatingPanelPosition` | `Bottom` | OneWay | Panel slide direction (`Bottom`, `BottomTabs`, `Top`) |
| `IsLocked` | `bool` | `true` | OneWay | Prevents drag dismiss of the panel |
| `Detent` | `DetentValue` | `Half` | OneWay | Panel snap position |
| `StrokeColor` | `Color` | `Black` | OneWay | Drawing stroke color |
| `SignatureBackgroundColor` | `Color` | `White` | OneWay | Canvas background color |
| `StrokeWidth` | `double` | `3.0` | OneWay | Drawing stroke width |
| `SignButtonText` | `string` | `"Sign"` | OneWay | Sign button label |
| `CancelButtonText` | `string` | `"Cancel"` | OneWay | Cancel button label |
| `SignButtonColor` | `Color` | `Blue` | OneWay | Sign button background |
| `CancelButtonColor` | `Color` | `Gray` | OneWay | Cancel button background |
| `ShowCancelButton` | `bool` | `true` | OneWay | Show/hide cancel button |
| `PanelBackgroundColor` | `Color` | `White` | OneWay | Panel background color |
| `PanelCornerRadius` | `double` | `16` | OneWay | Panel corner radius |
| `HasBackdrop` | `bool` | `true` | OneWay | Show backdrop behind panel |
| `ExportWidth` | `int` | `600` | OneWay | Exported PNG width in pixels |
| `ExportHeight` | `int` | `200` | OneWay | Exported PNG height in pixels |
| `SignCommand` | `ICommand?` | `null` | OneWay | Command invoked on sign with `SignatureImageEventArgs` |
| `CancelCommand` | `ICommand?` | `null` | OneWay | Command invoked on cancel |

## SignaturePad Properties (Blazor)

| Parameter | Type | Default | Description |
|---|---|---|---|
| `IsOpen` | `bool` | `false` | Opens/closes the sheet (two-way via `@bind-IsOpen`) |
| `Direction` | `SheetDirection` | `Bottom` | Sheet slide direction |
| `IsLocked` | `bool` | `true` | Prevents drag dismiss |
| `Detent` | `DetentValue` | `Half` | Sheet snap position |
| `StrokeColor` | `string` | `"#000000"` | CSS stroke color |
| `SignatureBackgroundColor` | `string` | `"#FFFFFF"` | CSS canvas background |
| `StrokeWidth` | `double` | `3` | Stroke width |
| `SignButtonText` | `string` | `"Sign"` | Sign button label |
| `CancelButtonText` | `string` | `"Cancel"` | Cancel button label |
| `SignButtonColor` | `string` | `"#6C63FF"` | CSS sign button color |
| `CancelButtonColor` | `string` | `"#94A3B8"` | CSS cancel button color |
| `ShowCancelButton` | `bool` | `true` | Show/hide cancel button |
| `PanelBackgroundColor` | `string` | `"#FFFFFF"` | CSS panel background |
| `PanelCornerRadius` | `double` | `16` | Panel corner radius |
| `HasBackdrop` | `bool` | `true` | Show backdrop |
| `ExportWidth` | `int` | `600` | Exported PNG width |
| `ExportHeight` | `int` | `200` | Exported PNG height |

## SignaturePad Events

**MAUI:**

| Event | Args | Description |
|---|---|---|
| `Signed` | `SignatureImageEventArgs` | Fires when the user taps Sign; contains `ImageStream` (PNG) |
| `Cancelled` | `EventArgs` | Fires when the user taps Cancel |

**Blazor:**

| Event | Type | Description |
|---|---|---|
| `Signed` | `EventCallback<byte[]>` | Fires with PNG bytes on sign |
| `Cancelled` | `EventCallback` | Fires on cancel |

## SignatureImageEventArgs (MAUI)

| Property | Type | Description |
|---|---|---|
| `ImageStream` | `Stream` | PNG image stream of the captured signature |

## SignaturePad ViewModel Pattern (MAUI)

```csharp
public partial class SignatureViewModel : ObservableObject
{
    [ObservableProperty]
    bool isSignatureOpen;

    [ObservableProperty]
    ImageSource? signatureImage;

    public bool HasSignature => SignatureImage != null;

    [RelayCommand]
    void OpenSignature() => IsSignatureOpen = true;

    [RelayCommand]
    void HandleSigned(SignatureImageEventArgs args)
    {
        var ms = new MemoryStream();
        args.ImageStream.CopyTo(ms);
        ms.Position = 0;
        SignatureImage = ImageSource.FromStream(() => ms);
        OnPropertyChanged(nameof(HasSignature));
    }

    [RelayCommand]
    void HandleCancelled() { }

    [RelayCommand]
    void ClearSignature()
    {
        SignatureImage = null;
        OnPropertyChanged(nameof(HasSignature));
    }
}
```

## Code Generation Guidance

- **MAUI**: SignaturePad must be placed inside `ShinyContentPage.Panels` or `OverlayHost` — it wraps a FloatingPanel internally
- **Blazor**: SignaturePad uses `SheetView` internally; no special host required
- The Sign button is automatically disabled until the user draws on the canvas
- The canvas resets automatically after signing or cancelling
- The Clear button appears once something is drawn
- On MAUI, `SignCommand` receives `SignatureImageEventArgs` with an `ImageStream` (PNG)
- On Blazor, `Signed` callback receives `byte[]` (raw PNG bytes)
- Set `ExportWidth`/`ExportHeight` to control the resolution of the exported PNG. The drawn signature (captured in the on-screen canvas coordinate space) is scaled **uniformly to fit and centered** within those dimensions, so the whole signature is preserved without clipping or aspect-ratio distortion even when the canvas and the export size differ — if the aspect ratios differ the image is letterboxed with the background color
- Panel closes automatically after sign or cancel
- While the pad is open the control automatically suppresses system navigation gestures that would otherwise steal edge-started strokes, and restores them on close — no consumer code required. On iOS this is the navigation controller's interactive "swipe back" pop; on Android it's the system back edge-swipe (API 29+, via gesture-exclusion rects) plus, when the pad sits inside a `TabbedPage`, the `ViewPager2` swipe-between-tabs gesture

### Automation ids (MAUI)

Set `AutomationId` on the control and its parts are named after it, for UI tests (Appium, UI Automation, DevFlow). Without an id the parts get none.

| Part | Id |
|---|---|
| Sheet content (lives in the floating panel) | `<id>_Popup` |
| Drawing surface | `<id>_Canvas` |
| Sign / cancel / clear | `<id>_Sign` / `<id>_Cancel` / `<id>_Clear` |
