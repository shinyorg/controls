# ProgressBar

A progress bar control with gradient fill and a Vista-style shimmer pulse that sweeps left-to-right. Available on both MAUI and Blazor.

The fill **slides** to each new value rather than snapping, and it does so in both directions - a value that drops drains back at the same rate it filled, instead of the fill-only easing most progress bars ship, which makes a downward correction read as a glitch. Only a value change animates: a width change driven by layout (the control being measured, `TrackHeight` changing, leaving indeterminate mode) snaps, because animating those makes the bar visibly reflow every time its container resizes.

For the thin line that runs across the **top or bottom of the window** rather than filling a slot in your layout, use [ProgressLine](progressline.md) - a sibling control that shares this one's drawing but docks itself to a page edge.

## MAUI

**Namespace**: `Shiny.Maui.Controls`
**XAML Namespace**: `http://shiny.net/maui/controls` (prefix: `shiny`)

### Basic Usage

```xml
<shiny:ProgressBar Value="{Binding Progress}"
                   TrackHeight="12"
                   CornerRadius="6"
                   UseGradient="True"
                   GradientStartColor="#3B82F6"
                   GradientEndColor="#8B5CF6"
                   PulseEnabled="True"
                   PulseOnValueChange="True"
                   PulseLength="0.4"
                   PulseSpeed="800" />
```

### Properties

| Property | Type | Default | Description |
|---|---|---|---|
| Value | double | 0 | Current progress value (TwoWay) |
| Minimum | double | 0 | Minimum value |
| Maximum | double | 100 | Maximum value |
| TrackColor | Color | #E5E7EB | Background track color |
| BarColor | Color | #3B82F6 | Fill bar color (non-gradient) |
| TrackHeight | double | 8 | Track height px |
| CornerRadius | double | 4 | Corner radius |
| UseGradient | bool | false | Enable gradient fill |
| GradientStartColor | Color | #3B82F6 | Left gradient color |
| GradientEndColor | Color | #8B5CF6 | Right gradient color |
| PulseEnabled | bool | false | Enable shimmer pulse |
| PulseOnValueChange | bool | true | Pulse on value change |
| PulseInterval | TimeSpan | 0 | Repeating pulse timer |
| PulseColor | Color | White | Shimmer color |
| PulseOpacity | double | 0.4 | Peak shimmer opacity |
| PulseLength | double | 0.4 | Shimmer width fraction (0.05–1.0) |
| PulseSpeed | int | 800 | Sweep duration in ms |
| ShowText | bool | false | Show text overlay |
| TextFormat | string | "{0:0}%" | Text format (receives %) |
| TextColor | Color | White | Text color |
| FontSize | double | 11 | Text font size |
| IsIndeterminate | bool | false | Indeterminate mode |
| Segments | int | 0 | Split the bar into this many steps; `0`/`1` = continuous |
| SegmentSpacing | double | 4 | Gap between segments |
| AnimateProgress | bool | true | Slide the fill to each new value instead of snapping |
| ProgressAnimationDuration | int | 250 | Slide length in ms; `0` snaps |
| ProgressAnimationEasing | Easing | CubicOut | Curve the slide follows |

### Events

| Event | Args | Description |
|---|---|---|
| ValueChangedEvent | double | Fires on value change |

### Commands

| Command | Parameter | Description |
|---|---|---|
| ValueChangedCommand | double | Fires on value change |

### Examples

```xml
<!-- Gradient with pulse on interval -->
<shiny:ProgressBar Value="{Binding Progress}"
                   UseGradient="True"
                   GradientStartColor="#6366F1"
                   GradientEndColor="#EC4899"
                   PulseEnabled="True"
                   PulseOnValueChange="False"
                   PulseInterval="0:00:02"
                   PulseLength="0.6"
                   PulseSpeed="1200" />

<!-- Indeterminate -->
<shiny:ProgressBar IsIndeterminate="True"
                   UseGradient="True"
                   GradientStartColor="#14B8A6"
                   GradientEndColor="#3B82F6" />

<!-- Simple with text -->
<shiny:ProgressBar Value="75"
                   TrackHeight="20"
                   CornerRadius="10"
                   ShowText="True" />
```

## Blazor

**Namespace**: `Shiny.Blazor.Controls`

### Basic Usage

```razor
<ProgressBar Value="@progress"
             TrackHeight="12"
             CornerRadius="6px"
             UseGradient="true"
             GradientStartColor="#3B82F6"
             GradientEndColor="#8B5CF6"
             PulseEnabled="true"
             PulseOnValueChange="true"
             PulseLength="0.4"
             PulseSpeed="800" />
```

### Properties

| Property | Type | Default | Description |
|---|---|---|---|
| Value | double | 0 | Current progress value |
| ValueChanged | EventCallback<double> | - | Two-way binding callback |
| Minimum | double | 0 | Minimum value |
| Maximum | double | 100 | Maximum value |
| TrackColor | string | "#E5E7EB" | Background track color |
| BarColor | string | "#3B82F6" | Fill bar color (non-gradient) |
| TrackHeight | double | 8 | Track height px |
| CornerRadius | string | "4px" | CSS corner radius |
| UseGradient | bool | false | Enable gradient fill |
| GradientStartColor | string | "#3B82F6" | Left gradient color |
| GradientEndColor | string | "#8B5CF6" | Right gradient color |
| PulseEnabled | bool | false | Enable shimmer pulse |
| PulseOnValueChange | bool | true | Pulse on value change |
| PulseInterval | TimeSpan | 0 | Repeating pulse timer |
| PulseColor | string | "rgba(255,255,255,0.4)" | Shimmer color |
| PulseLength | double | 0.4 | Shimmer width fraction (0.05–1.0) |
| PulseSpeed | int | 800 | Sweep duration in ms |
| AnimateProgress | bool | true | Slide the fill to each new value instead of snapping |
| ProgressAnimationDuration | int | 250 | Slide length in ms; `0` snaps |
| ProgressAnimationEasing | string | "cubic-bezier(0.33, 1, 0.68, 1)" | CSS timing function for the slide |
| ShowText | bool | false | Show text overlay |
| TextFormat | string | "{0:0}%" | Text format (receives %) |
| TextColor | string | "#FFFFFF" | Text color |
| FontSize | double | 11 | Text font size |
| IsIndeterminate | bool | false | Indeterminate mode |
| Segments | int | 0 | Split the bar into this many steps; `0`/`1` = continuous |
| SegmentSpacing | double | 4 | Gap between segments in px |
| CssClass | string? | null | Additional CSS class |
| AdditionalAttributes | IDictionary | null | Unmatched HTML attributes |

### Examples

```razor
<!-- Gradient with pulse on interval -->
<ProgressBar Value="@progress"
             UseGradient="true"
             GradientStartColor="#6366F1"
             GradientEndColor="#EC4899"
             PulseEnabled="true"
             PulseOnValueChange="false"
             PulseInterval="@TimeSpan.FromSeconds(2)"
             PulseLength="0.6"
             PulseSpeed="1200" />

<!-- Indeterminate -->
<ProgressBar IsIndeterminate="true"
             UseGradient="true"
             GradientStartColor="#14B8A6"
             GradientEndColor="#3B82F6" />

<!-- Simple with text -->
<ProgressBar Value="75"
             TrackHeight="20"
             CornerRadius="10px"
             ShowText="true" />
```

## Segments (Steps)

Set `Segments` to split the bar into separate steps with a gap between them instead of one solid bar - the stepped look of a delivery tracker or a multi-stage upload. `0` (the default) or `1` draws the continuous bar.

```xml
<shiny:ProgressBar Value="{Binding CompletedStops}"
                   Maximum="11"
                   Segments="11"
                   SegmentSpacing="6"
                   TrackHeight="12"
                   CornerRadius="6"
                   BarColor="#22A33A" />
```

```razor
<ProgressBar Value="@completedStops"
             Maximum="11"
             Segments="11"
             SegmentSpacing="6"
             TrackHeight="12"
             CornerRadius="6px"
             BarColor="#22A33A" />
```

- The fill runs across the steps in order, so a value part-way through a step lights that step **partially**. For whole steps, make `Maximum` (or the value's step size) line up with the segment count - `Maximum="11" Segments="11"`, or `Segments="10"` with a value moving in 10s.
- `TrackColor`, `BarColor` and `CornerRadius` apply to each step. A gradient is spread across the run: each step is painted with the gradient's colour at its centre.
- The fill slide still animates, and it travels step by step through the gaps.
- `ShowText` centres the text over the whole bar.
- Indeterminate mode and the pulse sheen always use the continuous bar.

## Pulse Behavior

The pulse is a Vista-style shimmer: a translucent gradient highlight (transparent -> color -> transparent) that sweeps from left edge to right edge of the filled portion.

- `PulseLength` controls the width of the sheen: `0.2` = narrow beam, `0.6` = wide glow, `1.0` = full bar.
- `PulseSpeed` controls how fast it sweeps: `500` = fast, `1200` = slow and smooth.
- Triggered by `PulseOnValueChange="True"` (fires on every value update) or `PulseInterval` (fires on a timer).
- Both triggers can be active simultaneously.

## Fill Animation

The fill transitions to each new value: a MAUI `Animation` driving the fill width, and a CSS `transition` on Blazor. Both are symmetric, so the bar drains as smoothly as it fills.

- `AnimateProgress="False"` or `ProgressAnimationDuration="0"` snaps instead.
- Blazor honours `prefers-reduced-motion: reduce` and drops the transition.
- Indeterminate mode has no width transition on either host - the sweep owns width and transform, and a transition only fights it.
- Retargeting mid-slide continues from where the bar actually is rather than jumping to the previous target.
