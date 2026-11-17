# Keyframe Animation

Declarative keyframe animation for .NET MAUI — the CSS `@keyframes` model in XAML, plus a fluent C# timeline API, storyboards, a drawn scene graph, and optional headless export.

**MAUI only.** There is no Blazor counterpart — the web already has `@keyframes` natively.

**Every MAUI head.** Nothing in the package touches a platform SDK — the clock is an `IDispatcherTimer` and `KeyframeView` is a `GraphicsView` — so it ships a single `net11.0` target that iOS, Android, Windows, MacCatalyst, macOS AppKit and Linux GTK4 heads all consume. Never gate keyframe code behind a platform check.

## Packages

| Package | What it is | Depends on |
|---|---|---|
| `Shiny.Controls.Keyframe.Shared` | Host-neutral timing engine + scene graph. `Timeline`, `Storyboard`, `Track<T>`, `IInterpolator<T>`, `Player`, easing curves, `KeyframeScene` and its layers. | `Microsoft.Maui.Graphics` |
| `Shiny.Maui.Controls.Keyframe` | XAML surface (`Animate.Keyframes`, `Keyframes`/`Track`/`Key`), `KeyframeView`, `MauiClock`, the animatable-property registry. | the above + `Microsoft.Maui.Controls` |
| `Shiny.Maui.Controls.Keyframe.Export` | Headless frame export + GIF encoder. **Optional** — the only thing in Keyframe that pulls SkiaSharp. | the shared package + `Microsoft.Maui.Graphics.Skia` |

Install what the task needs — animating views never requires the export package.

```bash
dotnet add package Shiny.Maui.Controls.Keyframe
```

```xml
xmlns:kf="http://shiny.net/maui/keyframe"
```

No `builder.Use…()` registration is needed. The one XAML namespace covers the shared timing types too.

## The design constraint that shapes everything

`IAnimationNode.Evaluate(t)` is a **pure function of time** — it never reads the previous frame. Generate code that relies on this rather than working around it:

- Scrubbing from a `Slider` or a gesture: just set `Progress` / call `SeekProgress(x)`.
- Reversing mid-flight: set `Player.Rate = -1` (or `KeyframeView.Speed` negative). No restart needed.
- Deterministic export: sampling at exact frame times is byte-identical every run.

## XAML — `Animate.Keyframes`

An attached property on any `VisualElement`.

```xml
<Border>
  <kf:Animate.Keyframes>
    <kf:Keyframes Duration="0:0:1.2" Iterations="Infinite" Direction="Alternate" Fill="Both">

      <kf:Track Property="Scale">
        <kf:Key Offset="0"   Value="1" />
        <kf:Key Offset="0.5" Value="1.15" Easing="CubicOut" />
        <kf:Key Offset="1"   Value="1" />
      </kf:Track>

      <kf:Track Property="BackgroundColor">
        <kf:Key Offset="0" Value="#2563EB" />
        <kf:Key Offset="1" Value="#EC4899" />
      </kf:Track>

    </kf:Keyframes>
  </kf:Animate.Keyframes>
</Border>
```

### `Keyframes`

| Property | Type | Notes |
|---|---|---|
| `Duration` | `TimeSpan` | One iteration. |
| `Delay` | `TimeSpan` | Before the first iteration. |
| `Iterations` | `double` | A number, or the literal `Infinite`. |
| `Direction` | `PlaybackDirection` | `Normal`, `Reverse`, `Alternate`, `AlternateReverse`. |
| `Fill` | `FillMode` | `None`, `Forwards`, `Backwards`, `Both` — matches CSS `animation-fill-mode`. |
| `Easing` | `EasingFunction` | Timeline-level default; a `Key`'s own `Easing` wins. |
| `AutoPlay` | `bool` | Defaults true. Set false to drive it from code. |
| `Speed` | `double` | Rate multiplier. |
| `Tracks` | `IList<Track>` | Content property — child `kf:Track` elements go straight in. |

### `Track`

- `Property` — the animated property name, resolved through the registry (below).
- `TargetName` — animate a *different* named element instead of the one carrying the attached property.
- `Keys` — content property.

### `Key`

- `Offset` — 0..1 along the timeline.
- `Value` — **omit it** and the key resolves to the target's live value when playback starts, so a re-triggered animation continues from where it is rather than snapping.
- `Easing` — shapes the segment that *starts* at this key (CSS semantics), so the curve on the final key is never used.

## Animatable properties

`Property="…"` resolves through `AnimatableProperties`, a dictionary of **hand-registered delegates** — not reflection, not compiled `Expression`, both of which work in the emulator and break under Native AOT.

Registered out of the box:

`Opacity`, `Scale`, `ScaleX`, `ScaleY`, `TranslationX`, `TranslationY`, `Rotation`, `RotationX`, `RotationY`, `Spin`, `AnchorX`, `AnchorY`, `BackgroundColor`, `WidthRequest`, `HeightRequest`, `Margin`, `Padding`.

Two behaviours worth knowing:

- **`Rotation` takes the shortest arc** — 350° → 10° turns forward 20°, not back 340°. Use **`Spin`** when multiple turns are the point.
- **`WidthRequest` / `HeightRequest` / `Margin` / `Padding` run a full measure+arrange every frame.** They work and are flagged by `AnimatableProperty.InvalidatesLayout`, but prefer transform and opacity where there's a choice.

Register your own:

```csharp
AnimatableProperties.Register(new AnimatableProperty<double>(
    "Elevation",
    v => ((MyCard)v).Elevation,
    (v, x) => ((MyCard)v).Elevation = x,
    DoubleInterpolator.Instance,
    o => Convert.ToDouble(o)));
```

## Easing

`Easing="…"` accepts **named curves** and **CSS function syntax**, so a curve copied out of a design tool or browser devtools pastes in verbatim.

Named: `Linear`, `Ease`, `EaseIn`, `EaseOut`, `EaseInOut`, `Emphasized`, `StepStart`, `StepEnd`, and the standard families in `In`/`Out`/`InOut` — `Quad`, `Cubic`, `Quart`, `Quint`, `Sin`, `Expo`, `Circ`, `Back`, `Elastic`, `Bounce`.

Functions:

```xml
Easing="cubic-bezier(0.34, 1.56, 0.64, 1)"
Easing="steps(8)"
Easing="spring(0.35, 14)"
```

In C#, the same curves are on `Easings` (`Easings.CubicOut`, …).

## C# — `TimelineBuilder` and `Player`

```csharp
var timeline = TimelineBuilder
    .Create(TimeSpan.FromSeconds(1))
    .PingPong()             // Direction(PlaybackDirection.Alternate)
    .RepeatForever()
    .Fill(FillMode.Both)
    .Animate(view, (v, x) => v.Scale = x, k => k
        .From(1)
        .Key(0.5, 1.2, Easings.CubicOut)
        .To(1))
    .Build();

var player = view.Play(timeline);   // extension on VisualElement
```

Builder: `Create`, `Named`, `Duration`, `Delay`, `EndDelay`, `Repeat`, `RepeatForever`, `Direction`, `PingPong`, `Fill`, `HoldEnd`, `Easing`, `StartAtIteration`, `Animate`, `AnimateAngle` (shortest-arc), `Add`, `Build`.

`Player`: `State`, `Position`, `Rate`, `RestoreOnStop`, `Finished`, `Play`, `Resume`, `Pause`, `Stop`, `Finish`, `Seek`, `SeekProgress`, `PlayAsync`.

```csharp
player.Rate = -1;              // reverse from wherever it is
player.SeekProgress(0.35);     // scrub
await player.PlayAsync();      // completes when the animation finishes
```

Reach the player behind a XAML animation with `Animate.GetPlayer(view)`.

### Storyboards

Compose timelines on a shared clock; they nest.

```csharp
var storyboard = new Storyboard()
    .Add(introTimeline)
    .Then(mainTimeline, gap: TimeSpan.FromMilliseconds(200))
    .Stagger(cardTimelines, interval: TimeSpan.FromMilliseconds(120));
```

`Add(node, offset)`, `Then(node, gap)`, `With(node)`, `Stagger(nodes, interval, startAt)`.

## Drawn scenes — `KeyframeScene` + `KeyframeView`

The same timing model driving a layer tree on a canvas (the Lottie-shaped lane) rather than views in the visual tree.

```csharp
var scene = new KeyframeScene(400, 200) { Background = Colors.Transparent };
var dot = scene.Add(new EllipseLayer { Size = new SizeF(28, 28), Fill = Colors.Blue });

scene.Animation = TimelineBuilder
    .Create(TimeSpan.FromSeconds(1.4))
    .AnimatePosition(dot, k => k.From(new PointF(0, 86)).To(new PointF(300, 86)))
    .AnimateFill(dot, k => k.From(Colors.Blue).To(Colors.HotPink))
    .Build();
```

```xml
<kf:KeyframeView Scene="{Binding Scene}"
                 Progress="{Binding Source={x:Reference Scrubber}, Path=Value, Mode=TwoWay}" />
<Slider x:Name="Scrubber" Minimum="0" Maximum="1" />
```

`KeyframeView` (a `GraphicsView`): `Scene`, `IsPlaying`, `Speed` (negative = reverse), `Progress` (two-way bindable — this is the scrubber hook), and read-only `Player`.

Layers: `RectangleLayer` (`CornerRadius`), `EllipseLayer`, `PathLayer` (`Data`, `WindingMode`), `TextLayer`, `ImageLayer`, all sharing `Fill`/`Stroke`/`StrokeWidth`/dash properties from `ShapeLayer`.

Layer animation extensions on `TimelineBuilder`: `AnimateOpacity`, `AnimateRotation`, `AnimateSpin`, `AnimatePosition`, `AnimatePositionX`, `AnimatePositionY`, `AnimateScale`, `AnimateScaleX`, `AnimateScaleY`, `AnimateSize`, `AnimateFill`, `AnimateStroke`, `AnimateStrokeWidth`, `AnimateStrokeDashOffset`, `AnimatePath`, `AnimateTrimStart`, `AnimateTrimEnd`, `AnimateCornerRadius`, `AnimateTextColor`, `AnimateVisibility`.

**Trim paths.** `PathLayer.TrimStart` / `TrimEnd` are fractions of the path's own measured length, so animating `AnimateTrimEnd` from 0 to 1 draws a stroke on. Trim applies to the stroke only — the fill always uses the whole path, matching Lottie. Prefer this to `AnimateStrokeDashOffset` for a reveal: `Microsoft.Maui.Graphics` specifies the dash offset in multiples of the stroke width, so a dash-based reveal changes shape whenever the stroke width does.

**Per-axis tracks.** `AnimatePositionX`/`Y` and `AnimateScaleX`/`Y` read the axis they do not own straight off the layer, so an X track and a Y track compose instead of the last one evaluated winning. Use them when the two axes have genuinely different curves; `AnimatePosition`/`AnimateScale` are still simpler when they move together.

## Offscreen export (optional package)

```csharp
var exporter = new FrameExporter(scene);
var options = new ExportOptions { Fps = 25, Scale = 2.0 };

GifEncoder.EncodeToFile("out.gif", exporter.Frames(options), options.Fps);
```

`ExportOptions`: `Fps`, `Duration`, `Size`, `Scale`, `Background`. `FrameExporter`: `Frames(options)` (lazy — never more than one frame in memory) and `FrameAt(progress, options)`.

**Always suggest 25 or 50fps when timing matters.** GIF stores delays in hundredths of a second, so only divisors of 100 are exact; 30fps writes 3cs and plays at 33.3fps, and most browsers promote 0–1cs delays to 10cs, so anything above 50fps plays far slower than asked.

Swap the rasterizer by implementing `IFrameRenderer` — `SkiaFrameRenderer` is only the default.

## Best Practices

1. **Prefer transform and opacity tracks.** `Scale`, `Translation*`, `Rotation`, `Opacity` are cheap; `WidthRequest`/`HeightRequest`/`Margin`/`Padding` cost a layout pass per frame.
2. **Omit `Value` on the first key** for anything re-triggerable, so it continues from the live value instead of snapping.
3. **Use `Spin`, not `Rotation`, for multi-turn spinners** — `Rotation` takes the shortest arc by design.
4. **Bind `KeyframeView.Progress` two-way to a `Slider`** for scrubbing; don't rebuild the timeline to seek.
5. **Reverse with a negative `Rate`/`Speed`**, not by rebuilding a reversed timeline.
6. **Register custom properties explicitly** with `AnimatableProperties.Register` — never expect reflection to work; it breaks under Native AOT.
7. **Remember the easing is on the segment that starts at the key** (CSS semantics) — the last key's curve is ignored.
8. **Set `Iterations="Infinite"` freely.** Targets are held weakly, so a looping animation on a popped page goes inert and is collected rather than pinning the visual tree.
9. **Only take `Shiny.Maui.Controls.Keyframe.Export` when actually exporting** — it's the sole SkiaSharp dependency.
10. **Don't reach for the compositor.** Everything ticks in managed code on the UI thread today; native offload (`CAKeyframeAnimation` / `ObjectAnimator` / `ScalarKeyFrameAnimation`) is designed for but not yet implemented.
