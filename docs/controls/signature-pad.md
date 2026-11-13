# SignaturePad

[← All Shiny Controls](../../README.md)

A signature capture control that opens in a FloatingPanel overlay (MAUI) or SheetView (Blazor). Users draw on a canvas and tap Sign to export the signature as a PNG. The Sign button is disabled until the user actually draws something.

The drawing surface inside the sheet is a [SignatureView](signature-view.md). To put a signature on the page itself — a form, a card, a table row — use SignatureView directly.

**Important:** Like FloatingPanel, SignaturePad must be placed inside an `OverlayHost` or `ShinyContentPage` on MAUI — it uses a FloatingPanel internally.

While the pad is open the control automatically suppresses system navigation gestures that would otherwise steal edge-started strokes (restoring them on close): on iOS, the navigation controller's interactive "swipe back" pop; on Android, the system back edge-swipe (API 29+, which the SignatureView surface excludes itself from) and — when the pad is hosted inside a `TabbedPage` — the swipe-between-tabs gesture. So strokes that begin near the screen edges are drawn instead of navigating away.

```xml
<!-- MAUI — must be inside ShinyContentPage.Panels or OverlayHost -->
<shiny:ShinyContentPage xmlns:shiny="http://shiny.net/maui/controls">
    <shiny:ShinyContentPage.PageContent>
        <VerticalStackLayout Padding="20" Spacing="10">
            <Button Text="Capture Signature" Command="{Binding OpenSignatureCommand}" />
            <Image Source="{Binding SignatureImage}" HeightRequest="150" Aspect="AspectFit" />
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

```razor
<!-- Blazor -->
<SignaturePad @bind-IsOpen="isOpen"
              StrokeColor="#000000"
              SignatureBackgroundColor="#F8F8F8"
              StrokeWidth="3"
              SignButtonColor="#6C63FF"
              CancelButtonColor="#94A3B8"
              Signed="OnSigned"
              Cancelled="OnCancelled" />
```

| Property | Type | Default | Description |
|---|---|---|---|
| IsOpen | bool | false | Opens/closes the signature panel (TwoWay) |
| Position | FloatingPanelPosition | Bottom | Panel slide direction (Bottom, BottomTabs, Top) |
| IsLocked | bool | true | Prevents drag dismiss |
| Detent | DetentValue | Half | Panel snap position |
| StrokeColor | Color | Black | Drawing stroke color |
| SignatureBackgroundColor | Color | White | Canvas background |
| StrokeWidth | double | 3.0 | Drawing stroke width |
| SignButtonText | string | "Sign" | Sign button label |
| CancelButtonText | string | "Cancel" | Cancel button label |
| SignButtonColor | Color | Blue | Sign button background |
| CancelButtonColor | Color | Gray | Cancel button background |
| ShowCancelButton | bool | true | Show/hide cancel button |
| PanelBackgroundColor | Color | White | Panel background |
| PanelCornerRadius | double | 16 | Panel corner radius |
| HasBackdrop | bool | true | Backdrop behind panel |
| ExportWidth | int | 600 | Exported PNG width |
| ExportHeight | int | 200 | Exported PNG height |
| SignCommand | ICommand? | null | Invoked on sign with `SignatureImageEventArgs` |
| CancelCommand | ICommand? | null | Invoked on cancel |

Blazor uses CSS color strings instead of `Color`, `SheetDirection` instead of `FloatingPanelPosition`, and `Signed` is `EventCallback<byte[]>` (raw PNG bytes).

Events: `Signed` fires with `SignatureImageEventArgs` (MAUI) or `byte[]` (Blazor). `Cancelled` fires on cancel.

### Automation ids (MAUI)

Set `AutomationId` on the control and its parts are named after it, for UI tests (Appium, UI Automation, DevFlow). Without an id the parts get none.

| Part | Id |
|---|---|
| Sheet content (lives in the floating panel) | `<id>_Popup` |
| Drawing surface | `<id>_Canvas` |
| Sign / cancel / clear | `<id>_Sign` / `<id>_Cancel` / `<id>_Clear` |

The Clear button shows once something is drawn.
