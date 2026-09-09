# ZoomPanView

**MAUI + Blazor.** Pinch, pan and double-tap zoom over **any** content — a layout, a chart, a form, a table. `ImageViewer`'s zoom machinery with the picture taken out; both share one implementation (`ZoomPanController` on MAUI, `zoom-pan-core.js` on Blazor).

Reach for `ImageViewer` when the content **is** a picture and you want a full-screen lightbox. Reach for `ZoomPanView` when you want an inline surface that stays where you put it.

## MAUI

```xml
<shiny:ZoomPanView MaxZoom="4"
                   ZoomLevel="{Binding Zoom}"
                   DoubleTapToZoom="True">
    <VerticalStackLayout Padding="20" Spacing="12">
        <Label Text="Still a live control tree" />
        <Button Text="Still clickable" Command="{Binding TapCommand}" />
    </VerticalStackLayout>
</shiny:ZoomPanView>
```

`ContentView`-shaped, so the child is the content — no `Content=` needed in XAML. It clips to its own bounds; give it a height (or a `Border`/`Grid` row) or it will size to the content it is meant to be zooming inside.

## Blazor

```razor
<ZoomPanView MaxZoom="4"
             @bind-ZoomLevel="zoom"
             WheelMode="ZoomPanWheelMode.Modifier"
             ZoomChanged="OnZoomChanged">
    <div>
        <h3>Still a live control tree</h3>
        <button @onclick="() => clicks++">Still clickable</button>
    </div>
</ZoomPanView>
```

Give the host a height in CSS — the component sets `overflow: hidden` and `position: relative` but takes no size of its own.

## Parameters

| Parameter | Type | Default | Notes |
|---|---|---|---|
| `MinZoom` | `double` | `1` | Below 1 the content may sit smaller than its box and **stay** there. |
| `MaxZoom` | `double` | `5` | |
| `ZoomLevel` | `double` | `1` | Two-way. Writing it zooms; gestures report back through it. |
| `IsZoomed` | `bool` | — | Read-only. Past natural size, i.e. there is something to pan. |
| `IsZoomEnabled` | `bool` | `true` | Off hands the content's gestures back and drops any zoom. |
| `DoubleTapToZoom` | `bool` | `true` | Turn off when the content wants double taps of its own. |
| `DoubleTapZoom` | `double` | `2.5` | Capped by `MaxZoom`. |
| `AnimationLength` | `int` | `250` | Milliseconds. Zero snaps. |
| `WheelMode` | `ZoomPanWheelMode` | `Modifier` | **Blazor only.** `Disabled` / `Modifier` / `Always`. |
| `UseFeedback` | `bool` | `false` | **MAUI only.** |

**Methods:** `ResetZoomAsync()`, `ZoomToAsync(zoom)` (MAUI also takes an optional focus `Point`).
**Events:** `ZoomChanged` → `ZoomPanChangedEventArgs(ZoomLevel, IsZoomed)`. MAUI adds `ZoomChangedCommand`.

## Known gap: clipping on iOS

A zoomed surface is **not clipped to the control's bounds on iOS**. The zoom itself is correct — the
content scales, stays interactive, and reports its level — but content scaled past the control's box
paints over whatever is laid out around it instead of being cut off at the edge. Neither
`IsClippedToBounds` on the control, an explicit `Clip` geometry, nor an inner `Layout` with
`IsClippedToBounds` holds a scaled child in on that platform, and an enclosing `Border` does not
either. Blazor clips correctly (`overflow: hidden` on the host), so this is an iOS-only gap.

Until it is resolved, give a MAUI `ZoomPanView` room around it, or keep `MaxZoom` low enough that the
overflow does not reach neighbouring content.

## Rules

- **The content stays interactive.** Zoom is a render transform, so nothing re-flows. The pan gesture is attached only while zoomed, so at rest the content owns its gestures; on Blazor `touch-action` is claimed on the same terms, a gesture starting on a control is left to that control until the surface is zoomed, and a pan takes the pointer capture only after it has travelled far enough to be a drag.
- **Do not nest it in a `ScrollView` along the scroll axis** without a fixed height — the drag is shared and the result reads as a fight. Drive `ZoomLevel` instead where you can.
- **Wheel zoom is Blazor-only.** MAUI has no cross-platform scroll-wheel hook for a view; on a desktop MAUI head use `ZoomToAsync`/`ZoomLevel`.
- `AnimationLength="0"` snaps — and is the only path open to a target with no handler, which is what makes it testable headless.
