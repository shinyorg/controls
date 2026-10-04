# Marquee

Endless scrolling content in the style of Magic UI's marquee. Works horizontally, vertically or at any angle. Both hosts have it, in the **core** packages.

## MAUI

Content comes from a template, because views can't be cloned:

```xml
<shiny:Marquee ItemsSource="{Binding Logos}"
               ItemTemplate="{StaticResource LogoTemplate}"
               Gap="24"
               FadeEdges="True"
               PauseOnHover="True"
               PauseOnPress="True" />
```

- With no `ItemsSource`, the `ItemTemplate` is realized once per copy and inherits the marquee's `BindingContext`. Use this for one repeating piece of content.
- `DataTemplateSelector` is supported.
- Don't try to put child views directly inside `<shiny:Marquee>`. There is no content collection.
- `IsPaused` is a read-only bindable property. It is true when the marquee is stopped, hovered, pressed, or held by reduced motion.
- `FadeColor` (`Color?`) is the color the edges fade into. It defaults to the theme's Surface color. Set it to the background behind the marquee, because MAUI has no alpha mask and the fade is painted over the content.

## Blazor

```razor
<Marquee FadeEdges="true" PauseOnHover="true" Gap="24">
    @foreach (var logo in logos) { <img src="@logo" height="32" /> }
</Marquee>
```

- `ChildContent` is rendered once per copy. Keep stateful components out of it.
- Set the size and look with `Class` and `Style`.
- The movement is pure CSS. A small JS module only measures (copies, `Speed`, and upright padding).

## Shared properties (same names on both hosts)

| Property | Default | Notes |
|---|---|---|
| `Angle` (double) | 0 | Degrees of travel: 0 left, 90 up, 180 right, 270 down. Anything else travels diagonally (-15 drifts left and slightly down) |
| `Reverse` (bool) | false | Opposite direction on the same axis |
| `Duration` (TimeSpan) | 40s | One pass of the content |
| `Speed` (double) | 0 | Units per second. When > 0 it overrides `Duration`. Prefer it when item counts vary |
| `Gap` (double) | 16 | Between items and copies |
| `Repeat` (int) | 4 | Minimum copies. More are added automatically to fill the viewport |
| `PauseOnHover` / `PauseOnPress` (bool) | false | |
| `IsRunning` (bool) | true | False freezes it |
| `FadeEdges` (bool) | false | Gradient fade at both ends of the axis |
| `FadeLength` (double) | 48 | Length of each fade |
| `KeepContentUpright` (bool) | true | At an angle, items stay level. When false they tilt with the track like a ribbon |
| `RespectReducedMotion` (bool) | true | Holds still under the OS or browser reduced-motion setting |

## Code generation guidance

- **Vertical (90/270) and angled marquees need a height:** `HeightRequest` on MAUI, `Style="height:…"` on Blazor. Horizontal marquees size to their content.
- **"Two rows going opposite ways":** use two marquees with the same items, the second with `Reverse="True"`.
- **"Tilted / 3D" look:** use `Angle="-15"` (or similar) plus a height. Set `KeepContentUpright="False"` only if the items should tilt too.
- **Speed vs duration:** use `Speed` when the item list is dynamic, so the pace stays constant. Use `Duration` to match a design spec ("one loop every 40s").
- **No timers:** don't wrap a marquee in your own animation or timer. It runs itself and pauses when unloaded.
