# ZoomPanView

**MAUI + Blazor.** Wraps any content in pinch-to-zoom, pan and double-tap zoom — a `StackLayout`, a chart, a form, a table, a `Grid` of anything at all. Not only a picture.

![ZoomPanView](../../assets/zoompanview1.png)

<!-- TODO: capture screenshots for zoom-pan-view -->

## What it is

This is [`ImageViewer`](image-viewer.md)'s zoom machinery with the picture taken out. Both hosts share one implementation with the viewer — `ZoomPanController` on MAUI, `zoom-pan-core.js` on Blazor — so the pan limits and the gesture arbitration are one piece of code rather than two.

What differs is the purpose. `ImageViewer` is a full-screen lightbox you *open*; `ZoomPanView` is an inline surface that stays where you put it and clips its content to its own bounds.

```xml
<shiny:ZoomPanView MaxZoom="4">
    <VerticalStackLayout>
        <Label Text="Pinch me" />
        <Button Text="Still clickable" />
    </VerticalStackLayout>
</shiny:ZoomPanView>
```

```razor
<ZoomPanView MaxZoom="4">
    <div>
        <h3>Pinch me</h3>
        <button>Still clickable</button>
    </div>
</ZoomPanView>
```

## The content stays live

Zoom is a render transform, so the content keeps its laid-out size: nothing re-flows, and no measure pass runs while a pinch is in progress. What is inside stays interactive at every scale, and two rules are what make that true rather than merely hoped for:

- **The pan gesture is only attached while the content is actually zoomed.** A pan recognizer on a container competes with everything inside it, so at rest the content owns its gestures outright. On Blazor the same trade is made with `touch-action`, which is only claimed once there is somewhere to pan — otherwise a `ZoomPanView` in the middle of a long page would take scrolling away from it.
- **On Blazor a gesture that starts on a control is left to that control** until the surface is zoomed, and a pan only takes the pointer capture once the pointer has travelled far enough to be a drag rather than a click. Without that, capturing on `pointerdown` swallows the click on every button inside.

## Parameters

| Parameter | Type | Default | Notes |
|---|---|---|---|
| `MinZoom` | `double` | `1` | Below 1 the content may sit smaller than its box — and **stay** there. Only a reset or double-tap-out returns it to 1. |
| `MaxZoom` | `double` | `5` | |
| `ZoomLevel` | `double` | `1` | Two-way bindable. Writing it zooms; every gesture reports back through it. |
| `IsZoomed` | `bool` | `false` | Read-only. Scaled past natural size — the same thing as "there is something to pan". |
| `IsZoomEnabled` | `bool` | `true` | Turns every gesture off without unbinding anything, and drops any zoom already applied. |
| `DoubleTapToZoom` | `bool` | `true` | Off when the content wants double taps of its own. |
| `DoubleTapZoom` | `double` | `2.5` | Capped by `MaxZoom`. |
| `AnimationLength` | `int` | `250` | Milliseconds. **Zero snaps.** |
| `WheelMode` | `ZoomPanWheelMode` | `Modifier` | **Blazor only.** `Disabled` / `Modifier` (Ctrl or ⌘ — also what a trackpad pinch sends) / `Always`. |
| `UseFeedback` | `bool` | `false` | **MAUI only.** Platform feedback on a double tap. |

**Methods** — `ResetZoomAsync()` animates back to natural size and position; `ZoomToAsync(zoom)` animates to a scale, clamped into the limits. On MAUI `ZoomToAsync` also takes an optional focus `Point` in the view's own coordinates, which keeps that point where it is.

**Events** — `ZoomChanged` carries a `ZoomPanChangedEventArgs(ZoomLevel, IsZoomed)` on every change of scale, mid-pinch included. MAUI adds `ZoomChangedCommand`.

## Platform notes

- **Wheel zoom is Blazor-only.** MAUI has no equivalent cross-platform hook for a scroll wheel over a view, so on desktop MAUI the gestures are pinch (trackpad), double-tap and the programmatic API.
- **Pinch needs two fingers**, so on a desktop MAUI head without a trackpad, `ZoomToAsync` and `ZoomLevel` are the way in.
- A `ZoomPanView` inside a `ScrollView` shares the drag with it. Give the view a fixed height and keep it out of the scroll direction where you can, or drive the zoom from `ZoomLevel` instead.

## See also

- [ImageViewer](image-viewer.md) — the lightbox built on the same machinery, for when the content *is* a picture.
