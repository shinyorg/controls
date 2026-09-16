# ProgressBar

[← All Shiny Controls](../../README.md)

A progress bar control with gradient fill and a configurable Vista-style shimmer pulse that sweeps left-to-right across the bar. Supports determinate, indeterminate, segmented (stepped), text overlay, and timed/value-triggered pulse animations. The fill **slides** to each new value rather than snapping, in both directions - a value that drops drains back at the same rate it filled - configurable via `AnimateProgress`, `ProgressAnimationDuration` and `ProgressAnimationEasing`, and skipped for width changes that come from layout rather than from progress.

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

| Property | Type | Default | Description |
|---|---|---|---|
| Value | double | 0 | Current value (TwoWay) |
| Minimum | double | 0 | Minimum value |
| Maximum | double | 100 | Maximum value |
| TrackColor | Color/string | #E5E7EB | Background track color |
| BarColor | Color/string | #3B82F6 | Fill bar color (when gradient disabled) |
| TrackHeight | double | 8 | Track height in px |
| CornerRadius | double/string | 4 | Corner radius |
| UseGradient | bool | false | Enable gradient fill |
| GradientStartColor | Color/string | #3B82F6 | Left gradient color |
| GradientEndColor | Color/string | #8B5CF6 | Right gradient color |
| PulseEnabled | bool | false | Enable Vista-style shimmer pulse |
| PulseOnValueChange | bool | true | Trigger pulse on value change |
| PulseInterval | TimeSpan | 0 | Trigger pulse on a timer (e.g. every 2s) |
| PulseColor | Color/string | White | Shimmer highlight color |
| PulseOpacity | double | 0.4 | Peak shimmer opacity (MAUI) |
| PulseLength | double | 0.4 | Width of shimmer as fraction of fill (0.05–1.0) |
| PulseSpeed | int | 800 | Milliseconds for one left-to-right sweep |
| ShowText | bool | false | Show percentage text overlay |
| TextFormat | string | "{0:0}%" | Text format string |
| TextColor | Color/string | White | Text color |
| FontSize | double | 11 | Text font size |
| IsIndeterminate | bool | false | Indeterminate sliding animation |
| Segments | int | 0 | Split the bar into this many steps; `0`/`1` = continuous |
| SegmentSpacing | double | 4 | Gap between segments |
| AnimateProgress | bool | true | Slide the fill to each new value instead of snapping (both directions) |
| ProgressAnimationDuration | int | 250 | Length of the fill slide in ms; `0` snaps |
| ProgressAnimationEasing | Easing/string | CubicOut / `cubic-bezier(0.33, 1, 0.68, 1)` | Curve the fill slide follows |

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

Events: `ValueChangedEvent`. Commands: `ValueChangedCommand`.
