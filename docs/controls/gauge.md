# Gauge

[← All Shiny Controls](../../README.md)

A dial gauge, such as a speedometer, tachometer, fuel or temperature gauge, or a needle-less ring meter. Its needle **moves like a real instrument's**. It accelerates toward each new `Value` and keeps its momentum when the reading changes mid-flight. With `SpringDamping` below 1 it overshoots slightly and settles back. It bounces off a peg at each end instead of swinging past the dial. A live feed that reports ten readings a second therefore turns into one smooth sweep, not a series of steps. That is how a tachometer climbs, drops on a gear change and climbs again. `SweepOnLoad` adds the ignition self-test: the first time the gauge appears, the needle swings out to the maximum and falls back.

Both hosts draw from one engine, `Shiny.Controls.Gauge.Shared`. It handles value-to-angle mapping, 1-2-5 tick generation, a fit-to-box layout and the needle physics. On **MAUI** the gauge is a `GraphicsView` drawn with Microsoft.Maui.Graphics, so it is the same control on iOS, Android, Mac Catalyst, macOS, Windows and Linux. It ticks on the shared frame clock only while the needle is moving, so a dashboard of resting gauges runs no timer at all. On **Blazor** the dial is SVG and `gauge.js` moves the needle on `requestAnimationFrame` with the same physics. That costs no render batch per frame, and on Blazor Server no round trip per frame. Without JavaScript (prerendering or static SSR) the needle is drawn at the value. `prefers-reduced-motion` makes the needle snap to each value.

```xml
<shiny:Gauge Value="{Binding Rpm}"
             Maximum="8"
             MajorTickInterval="1"
             Title="RPM"
             Units="x1000 r/min"
             ReadoutDecimals="1"
             SweepOnLoad="True"
             SpringDamping="0.55">
    <shiny:Gauge.Ranges>
        <shiny:GaugeRange Start="5.5" End="6.5" Color="#F59E0B" />
        <shiny:GaugeRange Start="6.5" End="8" Color="#EF4444" />
    </shiny:Gauge.Ranges>
</shiny:Gauge>
```

```razor
@using Shiny.Controls.Gauge

<Gauge Value="@rpm"
       Maximum="8"
       MajorTickInterval="1"
       Title="RPM"
       Units="x1000 r/min"
       ReadoutDecimals="1"
       SweepOnLoad="true"
       SpringDamping="0.55"
       Ranges="@([new(5.5, 6.5, "#F59E0B"), new(6.5, 8, "#EF4444")])" />
```

## Shapes

Angles are **compass degrees**: 0 is 12 o'clock, and angles increase clockwise. The dial is fitted to the bounding box of what is actually drawn, so a semicircle fills a wide, short space instead of leaving half a circle empty.

| Shape | StartAngle | SweepAngle |
|---|---|---|
| Speedometer (default) | -135 | 270 |
| Semicircle | -90 | 180 |
| Modern ring | -150 | 300, with `NeedleStyle="None"` |
| Full circle | 0 | 360 |

The readout sits opposite the middle of the arc, and the title sits toward it.

## Animation

| Animation | Behaviour |
|---|---|
| `Spring` (default) | A damped spring. `AnimationDuration` is its period, roughly how long the needle takes to arrive. `SpringDamping` 1 never overshoots; 0.5-0.7 is a lively analogue needle. Momentum carries through retargets. |
| `Ease` | A fixed-length ease-out from wherever the needle is. Good for slow values such as temperature. |
| `None` | Snaps. Setting `AnimationDuration` to zero does the same. |

`Sweep()` (MAUI) / `SweepAsync()` (Blazor) runs the ignition sweep on demand. A value that arrives during a sweep changes where the sweep comes back to; it does not cut the sweep short.

## Properties

| Property | Type | Default | Description |
|---|---|---|---|
| Value | double | 0 | Where the needle is heading. Clamped to the scale |
| Minimum / Maximum | double | 0 / 100 | The scale |
| StartAngle / SweepAngle | double | -135 / 270 | Compass degrees; see Shapes |
| Animation | GaugeAnimation | Spring | `Spring`, `Ease` or `None` |
| AnimationDuration | TimeSpan | 800ms | Spring period or ease length |
| SpringDamping | double | 0.65 | Damping ratio; 1 means no overshoot |
| SweepOnLoad | bool | false | Swing out to the maximum and back on first appearance |
| TrackThickness | double | 10 | Outer ring. **MAUI**: px. **Blazor**: dial units, where the radius is 100. Zero hides the track and the value arc |
| TrackColor | Color?/string | SurfaceContainerHighest | Unfilled track |
| ShowValueArc / ValueArcColor | bool / Color?/string | true / Primary | Fill from the minimum to the needle |
| ColorValueArcByRange | bool | true | The value arc takes the colour of the range the needle is in |
| Ranges | `GaugeRange` list | empty | Coloured bands (`Start`, `End`, `Color`), drawn just inside the track |
| RangeThickness | double | 4 | Band thickness. 0 hides the bands but keeps the arc colouring |
| ShowTicks / ShowTickLabels | bool | true | Tick marks and their numbers |
| MajorTickInterval | double | 0 | 0 picks a 1-2-5 step that gives about ten labels (0-240 gets 20) |
| MinorTicksPerMajor | int | 4 | Unlabelled ticks between labelled ones |
| TickLabelFormat | string | `{0:0.##}` | Composite format for the labels |
| TickColor | Color?/string | OnSurfaceVariant | Ticks and labels |
| NeedleStyle | GaugeNeedleStyle | Tapered | `Tapered`, `Line` or `None` |
| NeedleColor / HubColor | Color?/string | Primary / OnSurface | Needle and pivot cap |
| NeedleLength | double | 0.8 | Fraction of the radius |
| FaceColor | Color?/string | null | A disc behind the dial. Off by default, so the gauge never draws an edge of its own on top of its host's |
| ShowReadout / ReadoutDecimals | bool / int | true / 0 | Digital readout that counts along with the needle |
| Units / Title | string | null | Below the readout / above the pivot |
| TextColor | Color?/string | OnSurface | Readout, units and title |
| ShowPeak / PeakColor | bool / Color?/string | false / Tertiary | Peak-hold marker at the highest **reading** (never the overshoot). `ResetPeak()` clears it |
| Size | string | 220px | **Blazor**: CSS width. The height follows the dial's shape |

Read-only: `PeakValue` (both hosts), plus `NeedleValue` and `IsNeedleMoving` on MAUI. Events: `NeedleSettled` (MAUI) / `OnNeedleSettled` (Blazor).

On Blazor every colour is also a custom property (`--shiny-gauge-track`, `-arc`, `-tick`, `-needle`, `-hub`, `-text`, `-peak`) that an app stylesheet can set anywhere up the tree.

## Accessibility

MAUI sets `SemanticProperties.Description` to `"{Title} {value} {Units}"`. Blazor renders `role="meter"` with `aria-valuemin`, `-valuemax`, `-valuenow`, `-valuetext`, and `aria-label` from the title.
