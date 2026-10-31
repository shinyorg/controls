# Gauge

A dial gauge (speedometer, tachometer, fuel, temperature, or a needle-less ring meter) whose needle **animates itself like a real instrument's**. Available on both MAUI and Blazor in the **core** packages, over the shared `Shiny.Controls.Gauge.Shared` engine (scale mapping, ticks, fit-to-box layout and needle physics).

## When to use

- A value read at a glance against a scale, especially one that changes continuously (RPM, speed, CPU, temperature).
- Use `ProgressBar` / `ProgressLine` for task completion instead. A gauge is for a *measurement*, not progress.

## Rules for generated code

- **Bind `Value` directly to the live reading and let the needle animate.** Do not tween the value in the view model. The spring keeps its momentum between readings, so a feed of ten readings a second becomes one smooth sweep. A view-model tween fights it.
- Angles are **compass degrees**: 0 = 12 o'clock, increasing clockwise. Speedometer `-135`/`270` (default); semicircle `-90`/`180`; ring `-150`/`300` with `NeedleStyle="None"`.
- Size it and let it fit. **MAUI**: `WidthRequest`/`HeightRequest` (default 220×220); a semicircle fits a box about 1.4:1 wide. **Blazor**: `Size="200px"` (a CSS width); the height follows the dial's shape. Never put it in its own scroller.
- `FaceColor` is null by default, so the gauge draws no edge or background of its own. Only set it when the gauge is *not* already on a card.
- A tachometer is conventionally scaled 0-8 with `Units="x1000 r/min"` and `ReadoutDecimals="1"`, not 0-8000.
- `TrackThickness` / `RangeThickness`: **MAUI** values are px. **Blazor** values are dial units (radius = 100), so the defaults look the same at about 200px.
- Enums (`GaugeAnimation`, `GaugeNeedleStyle`) live in `Shiny.Controls.Gauge`. Blazor pages need `@using Shiny.Controls.Gauge`. Do **not** name a sample namespace or folder `Gauge` (use `Gauges`); `Sample.Features.Gauge` would shadow the control type.

## MAUI

**Namespace**: `Shiny.Maui.Controls` · **XAML**: `xmlns:shiny="http://shiny.net/maui/controls"`

```xml
<shiny:Gauge Value="{Binding Rpm}"
             Maximum="8"
             MajorTickInterval="1"
             Title="RPM"
             Units="x1000 r/min"
             ReadoutDecimals="1"
             SweepOnLoad="True"
             SpringDamping="0.55"
             WidthRequest="200" HeightRequest="200">
    <shiny:Gauge.Ranges>
        <shiny:GaugeRange Start="5.5" End="6.5" Color="#F59E0B" />
        <shiny:GaugeRange Start="6.5" End="8" Color="#EF4444" />
    </shiny:Gauge.Ranges>
</shiny:Gauge>
```

`Ranges` items are `BindableObject`s and inherit the gauge's `BindingContext`, so `Start="{Binding Redline}"` works. Methods: `Sweep()`, `ResetPeak()`. Read-only: `PeakValue` (bindable), `NeedleValue`, `IsNeedleMoving`. Event: `NeedleSettled`. Colours are `Color?`; null follows the theme token.

## Blazor

```razor
@using Shiny.Controls.Gauge

<Gauge @ref="gauge"
       Value="@speed"
       Maximum="240"
       MajorTickInterval="20"
       Units="km/h"
       ShowPeak="true"
       Ranges="ranges"
       OnNeedleSettled="OnSettled"
       Size="220px" />

@code {
    Gauge? gauge;
    static readonly IReadOnlyList<GaugeRange> ranges = [new(200, 240, "#EF4444")];
    // await gauge!.SweepAsync();  gauge!.ResetPeak();
}
```

`GaugeRange` is a record `(double Start, double End, string Color)`; any CSS colour or `var(--…)` works. Colours are `string?`; null follows the theme. Each colour is also a custom property: `--shiny-gauge-track`, `-arc`, `-tick`, `-needle`, `-hub`, `-text`, `-peak`. Renders `role="meter"` with aria values. Static SSR and prerender draw the needle at the value; `prefers-reduced-motion` snaps it.

## Properties (both hosts)

| Property | Default | Notes |
|---|---|---|
| Value / Minimum / Maximum | 0 / 0 / 100 | Value is clamped |
| StartAngle / SweepAngle | -135 / 270 | Compass degrees |
| Animation | Spring | `Spring`, `Ease`, `None` |
| AnimationDuration | 800ms | Spring period / ease length; zero snaps |
| SpringDamping | 0.65 | 1 = no overshoot; 0.5-0.7 lively |
| SweepOnLoad | false | Ignition self-test on first appearance |
| TrackThickness / TrackColor | 10 / SurfaceContainerHighest | 0 hides track and value arc |
| ShowValueArc / ValueArcColor | true / Primary | |
| ColorValueArcByRange | true | Arc takes the colour of the range the needle is in |
| Ranges / RangeThickness | empty / 4 | RangeThickness 0 keeps colouring without drawing bands |
| ShowTicks / ShowTickLabels | true / true | |
| MajorTickInterval / MinorTicksPerMajor | 0 (auto 1-2-5) / 4 | |
| TickLabelFormat / TickColor | `{0:0.##}` / OnSurfaceVariant | |
| NeedleStyle / NeedleColor / NeedleLength / HubColor | Tapered / Primary / 0.8 / OnSurface | `None` = ring gauge |
| FaceColor | null | |
| ShowReadout / ReadoutDecimals / Units / Title / TextColor | true / 0 / null / null / OnSurface | Readout counts along with the needle |
| ShowPeak / PeakColor | false / Tertiary | Peak tracks readings, never the overshoot |
