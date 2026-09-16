# ProgressLine

[← All Shiny Controls](../../README.md)

The thin line that runs across the **top or bottom of the window** while something loads. A sibling of `ProgressBar`, not a mode of it: `ProgressBar` fills a slot you gave it in a layout, whereas `ProgressLine` is page chrome - it has no slot, it moves itself onto the page edge, and it knows about the navigation bar, the tab bar and the safe area so it lands *against* them rather than under them. The drawing is `ProgressBar`'s, so the gradient, the shimmer and the animated fill all behave identically.

On MAUI, a line declared anywhere in a page's markup **relocates itself** onto the edge named by `Position` (set `Dock="False"` to keep it inline). Declaring it as a direct child of the page alongside the layout works: when XAML swaps the page's `Content` out from under it, the line re-docks onto the new content. The inset resolves from one rule: a bar earns an offset exactly when it is painted inside the same coordinate space the line is - so a `ShinyTabBar` docked over a Shell page pushes the line up by its height, while a `ShinyTabbedPage`, a native `TabbedPage` a native `NavigationPage`, or Shell's own nav bar and bottom tabs need no offset because the page's content area already excludes their chrome. On Blazor the equivalent is `position: fixed` with `env(safe-area-inset-*)`, plus `Anchor="Container"` to run the line along a panel's edge instead of the window's, and a `--shiny-progressline-offset` custom property any ancestor can set to push it below an `AppLayout` header.

```xml
<!-- Declared here, rendered across the top of the page -->
<shiny:ProgressLine Position="Top"
                    Value="{Binding Progress}"
                    BarColor="#F97316"
                    LineHeight="4" />
```

```razor
<ProgressLine Position="ProgressLinePosition.Top" Value="@progress" BarColor="#F97316" LineHeight="4" />
```

It is also driven from code, with no markup on the page at all, via `IProgressLineService` (MAUI: registered by `UseShinyControls()`; Blazor: `AddShinyControls()`/`AddShinyProgressLine()` plus one `<ProgressLineHost />` in the layout). Runs are **reference-counted**, so two overlapping operations produce one line that stays up until the slower of them lands:

```csharp
using var run = progressLine.Start(c => c.BarColor = Colors.Orange);
run.SetProgress(0.6);   // or report nothing and let it trickle
run.Complete();         // sweeps to 100%, then fades - Dispose() does the same
```

With nothing reported, the line **trickles**: it decelerates toward `TrickleCeiling` (0.9) and never arrives, because a line that reaches 100% on its own has told the user the work finished when it has not. `Indeterminate = true` runs the sweep instead.

| Property | Type | Default | Description |
|---|---|---|---|
| Position | ProgressLinePosition | Top | Which page edge the line runs along |
| Value / Minimum / Maximum | double | 0 / 0 / 100 | Progress, as on `ProgressBar` |
| IsIndeterminate | bool | false | Sweeping animation instead of a fill |
| BarColor | Color?/string | theme Primary | Fill color |
| TrackColor | Color?/string | Transparent | The unfilled remainder - off by default, unlike `ProgressBar` |
| LineHeight | double | 3 | Thickness |
| CornerRadius | double/string | 0 | Square by default, so the line meets the window edges |
| UseGradient | bool | false | Enable gradient fill |
| GradientStartColor / GradientEndColor | Color?/string | theme Primary / Tertiary | Gradient ends |
| PulseEnabled | bool | false | Shimmer sheen along the fill |
| AnimateProgress / ProgressAnimationDuration / ProgressAnimationEasing | bool / int / Easing | true / 250 / CubicOut | Fill slide, as on `ProgressBar` |
| IsActive | bool | true | The animated show/hide switch (not `IsVisible`) |
| FadeDuration | int | 200 | Length of the `IsActive` fade in ms |
| Dock | bool | true | **MAUI** - relocate onto the page edge; `False` keeps it inline |
| AutoInset | bool | true | **MAUI** - offset past the nav/tab bar and safe area |
| Offset | Thickness/string | 0 | Extra distance from the edge, on top of `AutoInset` |
| Anchor | ProgressLineAnchor | Viewport | **Blazor** - `Container` pins to the nearest positioned ancestor |
| RespectSafeArea | bool | true | **Blazor** - clear the notch/home indicator via `env(safe-area-inset-*)` |

`ProgressLineConfig` (the `Start` argument) carries the same appearance settings plus `Trickle`, `StartProgress`, `TrickleCeiling`, `TrickleInterval` and `TrickleRate`.
