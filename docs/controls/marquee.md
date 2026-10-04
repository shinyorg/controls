# Marquee

[← All Shiny Controls](../../README.md)

Content that scrolls endlessly, in the style of [Magic UI's marquee](https://magicui.design/docs/components/marquee). It can scroll horizontally, vertically, or along any angle. The content is laid end to end in several copies, so the loop has no seam. Both hosts have it, in the **core** packages.

`Angle` sets the direction of travel, in degrees:

- **0** scrolls left (the default).
- **90** scrolls up.
- **180** scrolls right.
- **270** scrolls down.
- **Any other value** travels along that angle. For example, `-15` drifts left and slightly down.

`Reverse` runs the same axis the other way.

## MAUI

A view can't be cloned, so the content comes from a template. Give the marquee an `ItemsSource` and an `ItemTemplate`; a `DataTemplateSelector` works too. With no `ItemsSource`, the template is used once per copy and inherits the marquee's `BindingContext`, which gives you a single repeating piece of content.

```xml
<shiny:Marquee ItemsSource="{Binding Reviews}"
               ItemTemplate="{StaticResource ReviewCard}"
               FadeEdges="True"
               PauseOnHover="True" />

<!-- the second row, the other way -->
<shiny:Marquee ItemsSource="{Binding Reviews}" ItemTemplate="{StaticResource ReviewCard}" Reverse="True" />

<!-- vertical: give it a height -->
<shiny:Marquee Angle="90" HeightRequest="300" ItemsSource="{Binding Tags}" ItemTemplate="{StaticResource Chip}" Speed="30" />

<!-- any angle -->
<shiny:Marquee Angle="-15" HeightRequest="200" ItemsSource="{Binding Tags}" ItemTemplate="{StaticResource Chip}" />
```

How it runs:

- **Clipping:** the marquee clips its content to its own bounds.
- **Timing:** the movement runs on the window's shared frame ticker. Every marquee and motion icon on a window uses that one timer.
- **When it stops:** it stops while unloaded or paused, and while the OS has reduced motion turned on (iOS and Mac Catalyst *Reduce Motion*, Android *Remove animations*, Windows animations off).
- **Accessibility:** every copy after the first is hidden from screen readers.

## Blazor

Put the content inside the component. It is rendered once per copy.

```razor
<Marquee FadeEdges="true" PauseOnHover="true">
    @foreach (var review in reviews)
    {
        <ReviewCard Review="review" />
    }
</Marquee>

<Marquee Angle="90" Speed="30" Style="height:300px">...</Marquee>
<Marquee Angle="-15" Style="height:220px">...</Marquee>
```

How it runs:

- **Animation:** the movement is a CSS animation, Magic UI's own approach. Each copy slides back by its own length plus the gap.
- **Script:** a small script only measures. It adds copies when the minimum doesn't fill the box, turns `Speed` into a duration, and pads upright items on an angled strip.
- **Fade:** the fade is a CSS mask, so it works over any background.
- **Reduced motion:** with `prefers-reduced-motion: reduce`, the marquee holds still.
- **Accessibility:** copies after the first are `aria-hidden`.

## Properties

| Property | Type | Default | Description |
|---|---|---|---|
| ItemsSource | IEnumerable | null | Items to scroll **(MAUI)** |
| ItemTemplate | DataTemplate | null | How each item is drawn **(MAUI)** |
| ChildContent | RenderFragment | — | Content to repeat **(Blazor)** |
| Angle | double | 0 | Direction of travel in degrees (0 left, 90 up, 180 right, 270 down) |
| Reverse | bool | false | Runs the other way along the same axis |
| Duration | TimeSpan | 40 s | How long one pass of the content takes |
| Speed | double | 0 | Units (DIPs or CSS px) per second. When positive it replaces `Duration`, so the pace doesn't depend on how much content there is |
| Gap | double | 16 | Space between items and between copies |
| Repeat | int | 4 | The fewest copies. More are added when it takes more to fill the viewport |
| PauseOnHover | bool | false | Stops while a mouse or pen is over it |
| PauseOnPress | bool | false | Stops while a finger or button is held down |
| IsRunning | bool | true | False holds it where it is |
| IsPaused | bool (read-only) | — | True whenever it is standing still **(MAUI)** |
| FadeEdges | bool | false | Fades the content out at both ends of the axis |
| FadeLength | double | 48 | Length of each fade |
| FadeColor | Color? | theme Surface | The color the edges fade into **(MAUI)**. MAUI has no alpha mask, so the fade is painted over the content; set this to whatever is behind the marquee |
| KeepContentUpright | bool | true | At an angle, whether items stay level or tilt with the direction of travel like a ribbon |
| RespectReducedMotion | bool | true | Holds still when the OS or browser asks for reduced motion |

## Sizing

- At 0° and 180° the marquee is as tall as its tallest item, and fills the width it is given.
- **Vertical and angled marquees need a height:** `HeightRequest` on MAUI, a `height` style on Blazor.
  - On MAUI, a vertical marquee with no height is as tall as one pass of its items.
  - On Blazor, an angled marquee falls back to 160px.

## Any angle: how it works and its limits

- **MAUI** lays the items out along the angle through the centre of the viewport and slides the whole run along that line. Upright items are spaced by how much of the line they actually cover (`|w·cos θ| + |h·sin θ|`), so spacing is exact at every angle.
- **Blazor** turns a horizontal strip to the angle and centres it in the box.
  - Upright items are turned back individually.
  - The strip is padded by half the tallest item's projection along it.
  - Spacing is exact at 0° and 90°. In between it is close but can differ slightly from MAUI, especially for tall, narrow items at steep angles.
- **Copies:** on both hosts, enough copies are made to span the viewport's length along the axis. The corners of the box that the band doesn't cross are empty, as in Magic UI's angled demo.
- **macOS (AppKit) on MAUI:** when the viewport grows enough to need more copies, they are added after layout. AppKit never draws views added after layout, so on that head the extra copies may not appear. Setting `Repeat` high enough up front avoids it.
