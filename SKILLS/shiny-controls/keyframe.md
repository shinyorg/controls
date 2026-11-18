# Keyframe Animation

Declarative animation for .NET MAUI and Blazor — the CSS `@keyframes` model in XAML, plus a fluent C# timeline API, choreographed storyboards, trigger actions, **animated visual-state transitions**, **implicit property transitions**, **item transitions**, Keyframe-driven **page transitions**, reduced motion honoured by default, a drawn scene graph, and optional headless export. It covers every phase of the MAUI animation spec (dotnet/maui#40382) on both hosts today. MAUI is documented first; the Blazor package (`Shiny.Blazor.Controls.Keyframe`, same names, Web Animations API underneath) is in the **Blazor** section at the end.

**Every MAUI head.** The engine, XAML and views are platform-neutral; the package also has iOS / Android / Mac Catalyst / Windows builds for the vsync clock (`CADisplayLink`, `Choreographer`, `CompositionTarget.Rendering`), the reduced-motion query and switching off native item animations. The AppKit and GTK4 heads consume the `net11.0` build and tick on a dispatcher timer. Never gate keyframe code behind a platform check.

## The animation layer (why animations never break bindings)

Every built-in property track, transition and spec-style `KeyFrame` writes through `AnimationLayer` — MAUI's top "from handler" value slot — not `SetValue`. The animation **shows** a value without **owning** the property: the binding, style, `{DynamicResource}` in a style, visual state and trigger underneath all keep working, any of them writing the property drops the animated value, and when an animation ends without a forwards fill (or is stopped with `StopBehavior.Release`) the property shows its own value again. `AnimationLayer.GetBaseValue(view, property)` reads the un-animated value. Generate code that relies on this — don't capture and restore values by hand, and don't fear animating a bound property.

Limits: a `{DynamicResource}` set **directly** on the animated property is detached by the first frame (put it in a `Style`); a `TwoWay`-bound property receives the animated values (don't animate a property the user edits). Custom properties registered with getter/setter delegates write directly, as before; register with a `BindableProperty` to get the layer.

## Packages

| Package | What it is | Depends on |
|---|---|---|
| `Shiny.Controls.Keyframe.Shared` | Host-neutral timing engine + scene graph. `Timeline`, `Storyboard`, `Track<T>`, `IInterpolator<T>`, `Player`, easing curves, `KeyframeScene` and its layers. | `Microsoft.Maui.Graphics` |
| `Shiny.Maui.Controls.Keyframe` | XAML surface (`Animate.Keyframes`/`Animate.Choreography`, `Keyframes`/`Track`/`Key`, `KeyFrame`/`Setter`, `Choreography`), trigger actions, `StateTransitions`, `PropertyTransitions`, `ItemTransitions`, `KeyframePageTransition`, `AnimationLayer`, `KeyframeView`, `MauiClock`, the animatable-property registry. | the above + `Microsoft.Maui.Controls` + `Shiny.Maui.Controls` |
| `Shiny.Blazor.Controls.Keyframe` | The Blazor host — same component names over the Web Animations API. See **Blazor** at the end. | the shared package |
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
| `EndDelay` | `TimeSpan` | After the last iteration, before it reports finished. |
| `IterationDelay` | `TimeSpan` | Pause between iterations, holding the end pose. |
| `Easing` | `EasingFunction` | Timeline-level default; a `Key`'s own `Easing` wins. |
| `AutoPlay` | `bool` | Defaults true. Set false to drive it from code. |
| `Speed` | `double` | Rate multiplier; negative runs backwards. |
| `ReducedMotion` | `ReducedMotionBehavior` | `Auto` (default: jump to the end state when the OS asks for less motion; a loop holds its first frame), `Play` (motion that carries information), `Instant`. |
| `TargetName` | `string` | Default target for every track, instead of the attached element. |
| `Tracks` / `Frames` | `IList<Track>` / `IList<KeyFrame>` | Child `kf:Track` and `kf:KeyFrame` elements go straight in as content; both shapes can mix. |

### Spec-style `KeyFrame` + `Setter`

The storyboard shape: frames at cue points holding ordinary MAUI `Setter`s, so **any** bindable property animates (blended by type: numbers, `Color` in Oklab, `Thickness`, `CornerRadius`, `Point`, `Size`, `Rect`, `SolidColorBrush`; other types switch halfway, and `bool` stays `true` through the segment).

```xml
<kf:Keyframes x:Key="PopIn" Duration="0:0:0.4" Fill="Backwards">
  <kf:KeyFrame Cue="0%">
    <Setter Property="VisualElement.Opacity" Value="0" />
    <Setter Property="VisualElement.Scale" Value="0.9" />
  </kf:KeyFrame>
  <kf:KeyFrame Cue="60%" Easing="CubicOut">
    <Setter Property="VisualElement.Opacity" Value="1" />
    <Setter Property="VisualElement.Scale" Value="1.05" />
  </kf:KeyFrame>
  <kf:KeyFrame Cue="100%" Easing="spring(0.4)">
    <Setter Property="VisualElement.Scale" Value="1" />
  </kf:KeyFrame>
</kf:Keyframes>
```

- `Cue` takes `0.6` or `60%`. A property first set after 0% starts from its live value.
- **A `KeyFrame`'s `Easing` arrives** — it shapes the segment *ending* at that frame (the WinUI/storyboard convention). A `Key`'s `Easing` departs (CSS). Don't mix the two up.
- `Setter.TargetName` animates a named element.

### `Choreography` — storyboards in XAML

```xml
<HorizontalStackLayout>
  <kf:Animate.Choreography>
    <kf:Choreography ApplyTo="Children" Stagger="0:0:0.07">
      <kf:Keyframes Duration="0:0:0.5" Fill="Backwards"> … </kf:Keyframes>
    </kf:Choreography>
  </kf:Animate.Choreography>
  …children…
</HorizontalStackLayout>
```

`Mode` (`Parallel` | `Sequence`), `Stagger` (per child, or the gap in a sequence), `ApplyTo` (`Self` | `Children` — each child definition is a template applied to every visual child, staggered: the list entrance), `TargetName`, `AutoPlay`, `Speed`, `ReducedMotion`. Children are `Keyframes` or nested `Choreography`. Named `Choreography` because the engine's C# `Storyboard` shares the XAML namespace. `Animate.GetChoreographyPlayer(view)` reaches its player.

### Playing definitions from code and triggers

```csharp
var player = view.Play(definition);            // Keyframes or Choreography; replays continue from the current frame
bool done = await view.PlayAsync(definition);  // false when stopped or cancelled — never throws
view.StopAnimations();                         // StopBehavior.Release by default
```

Trigger actions for `EnterActions`/`ExitActions` — all take `Animation` (usually a `{StaticResource}`) and optional `TargetName`:

```xml
<DataTrigger TargetType="Button" Binding="{Binding IsBusy}" Value="True">
  <DataTrigger.EnterActions><kf:BeginAnimation Animation="{StaticResource Pulse}" /></DataTrigger.EnterActions>
  <DataTrigger.ExitActions><kf:StopAnimation Animation="{StaticResource Pulse}" /></DataTrigger.ExitActions>
</DataTrigger>
```

`BeginAnimation` (`Handoff`: `Replace` | `Continue`), `StopAnimation` (`Behavior`: `Release` | `Hold` | `Restore`; no `Animation` = stop all), `PauseAnimation`, `ResumeAnimation`, `SkipAnimationToEnd`, `SeekAnimation` (`Progress`).

## Animated visual states — `StateTransitions`

Visual state changes animate instead of snapping — `GoToState`, state triggers, and MAUI's own `Pressed`/`PointerOver`/`Focused`/`Disabled`.

```xml
<Button Text="Save">
  <kf:StateTransitions.Transitions>
    <kf:VisualTransition Duration="0:0:0.15" />
    <kf:VisualTransition To="Pressed" Duration="0:0:0.08" Easing="CubicOut" />
    <kf:VisualTransition From="Pressed" Duration="0:0:0.45" Easing="spring(0.35, 14)" />
    <kf:VisualTransition To="Error" Animation="{StaticResource Shake}" />
    <kf:StateAnimation State="Busy" Animation="{StaticResource Pulse}" />
  </kf:StateTransitions.Transitions>
  <VisualStateManager.VisualStateGroups> … unchanged … </VisualStateManager.VisualStateGroups>
</Button>
```

- `VisualTransition`: `Group`, `From`, `To` (unset or `*` = any), `Duration` (150 ms default), `Delay`, `Easing` (CubicOut default), `ReducedMotion`, `Animation` (an extra definition played on the change). Most specific match wins; no match snaps.
- `StateAnimation`: `State`, `Group`, `Animation` — runs while in the state, released on leaving.
- Interrupting mid-flight continues from the on-screen value. Setters with `TargetName` animate their element.
- `StateTransitions.GoToState(view, "Pressed", useTransitions: false)` snaps one change.

## Implicit transitions — `PropertyTransitions`

Any change of a listed property animates — binding, style, trigger or code. The value set stays in the property's own source (`AnimationLayer.GetBaseValue` returns it); only what's on screen travels, and while it does the property getter returns the on-screen value.

```xml
<BoxView Opacity="{Binding Fade}" Scale="{Binding Zoom}">
  <kf:PropertyTransitions.Transitions>
    <kf:PropertyTransition Property="Opacity" Duration="0:0:0.2" />
    <kf:PropertyTransition Property="Scale" Duration="0:0:0.5" Easing="spring(0.4, 13)" />
    <kf:PropertyTransition BindableProperty="{x:Static Label.TextColorProperty}" />
  </kf:PropertyTransitions.Transitions>
</BoxView>
```

`Property` (registered name or `All`), `BindableProperty` (any property), `Duration` (200 ms), `Delay`, `Easing`, `ReducedMotion`. Initial values (before the handler exists) don't animate.

## Item transitions — `ItemTransitions`

```xml
<CollectionView ItemsSource="{Binding Items}"
                kf:ItemTransitions.Mode="None"
                kf:ItemTransitions.Appear="{StaticResource FadeUp}"
                kf:ItemTransitions.Add="{StaticResource PopIn}"
                kf:ItemTransitions.Remove="{StaticResource SlideOut}"
                kf:ItemTransitions.Stagger="0:0:0.04" />
```

```csharp
await ItemTransitions.RemoveAsync(list, item, () => Items.Remove(item));
```

- `Mode`: `Platform` (default) or `None` — the off switch for native insert/remove/move animations (Android `SetItemAnimator(null)`, iOS/Catalyst collapsed layer timing, Windows cleared container transitions).
- `Appear` plays once per item the first time a view shows it (staggered when several realise together — the list entrance); `Add` plays for items added after load (falls back to `Appear`). Both get a backwards fill automatically.
- `Remove` only runs through `RemoveAsync` — the platform tears a removed item's view down at once. Moves are the platform's, or nothing under `None`.

## Page transitions with Keyframes

`KeyframePageTransition` implements core's `IPageTransitionAnimator`, so a push/pop/modal can be any Keyframes you write: `Enter`, `Exit`, `PopEnter`, `PopExit` (each an `IAnimationDefinition`, played on the incoming/outgoing page). Set it with `PageTransition.Animator` on a page, `PageTransition.DefaultAnimator`, or the per-call navigation overloads. See `page-transitions.md` for the seam and the built-in types.

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

Named: `Linear`, `Hold`, `Ease`, `EaseIn`, `EaseOut`, `EaseInOut` (and the CSS spellings `ease-in` …), `Emphasized`, `StepStart`, `StepEnd`, and the standard families in `In`/`Out`/`InOut` — `Quad`, `Cubic`, `Quart`, `Quint`, `Sin`, `Expo`, `Circ`, `Back`, `Elastic`, `Bounce`.

Functions:

```xml
Easing="cubic-bezier(0.34, 1.56, 0.64, 1)"
Easing="steps(8)"
Easing="spring(0.35, 14)"     // damping, frequency
Easing="bouncy(0.3)"          // SwiftUI-style bounce: 0 settles, 0.7 is very bouncy
Easing="Spring(0.4)"          // C#-style casing works too
```

In C#, the same curves are on `Easings` (`Easings.CubicOut`, `Easings.Hold`, `Easings.Bouncy(0.3)`, …). The text syntax lives in the shared `EasingSyntax` class, so a curve string means the same thing on MAUI and Blazor; `EasingSyntax.ToCss(text)` turns any curve — springs included — into CSS (`linear()` for curves CSS lacks).

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

Builder: `Create`, `Named`, `Duration`, `Delay`, `EndDelay`, `IterationDelay`, `Repeat`, `RepeatForever`, `Direction`, `PingPong`, `Fill`, `HoldEnd`, `Easing`, `StartAtIteration`, `Animate`, `AnimateAngle` (shortest-arc), `Add`, `Build`.

`Player`: `State`, `Position`, `Duration`, `Rate`, `ReducedMotion`, `RestoreOnStop`, `Finished`, `Play`, `Resume`, `Pause`, `Stop()`, `Stop(StopBehavior)` (`Hold` | `Release` | `Restore`), `Finish`, `Seek`, `SeekProgress`, `PlayAsync` (returns `false` when stopped or cancelled — it never throws, so `async void` handlers are safe). Seeking ignores reduced motion: it is direct manipulation.

`MotionPolicy.IsReducedMotion` is wired to the OS setting automatically on MAUI; replace it in tests.

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
10. **Don't reach for the compositor.** Everything ticks in managed code on the UI thread, on a vsync clock; native compositor offload is not implemented.
11. **Use `StateTransitions` / `PropertyTransitions` instead of code-behind `FadeTo` on state or value changes** — they land on the value the state or binding owns, and interrupt cleanly.
12. **Leave `ReducedMotion` on `Auto`** unless the motion carries information (progress, a value changing) — then `Play`.
13. **Use `ItemTransitions.Mode="None"`** to stop unwanted CollectionView item animations; pair with `Add` for a custom insert.

<!-- ============================== BLAZOR (Shiny.Blazor.Controls.Keyframe) ============================== -->

## Blazor

`Shiny.Blazor.Controls.Keyframe` runs the same model on the browser's **Web Animations API** (`element.animate`). Same component and property names as MAUI, so a design reads the same on both hosts. No service registration; the script is imported once per app/circuit.

```bash
dotnet add package Shiny.Blazor.Controls.Keyframe
```

```razor
@using Shiny.Blazor.Controls.Keyframe
@using Shiny.Controls.Keyframe   @* PlaybackDirection, FillMode, ReducedMotionBehavior, PlaybackState *@
```

### Razor rules that differ from XAML (generate code that follows them)

- **`Key.Value` / `Setter.Value` are strings**, as in XAML: `Value="#2563EB"`, `Value="-60%"`, `Value="1.15"`. For a number from code use `Value="@x.ToString(CultureInfo.InvariantCulture)"`.
- **Inside `@foreach` / `@if` blocks write `<PropertyTrack>`, not `<Track>`** — Razor reads `<Track>` there as the HTML `<track>` void element and refuses children. In plain markup both work.
- Enum values need their type: `Direction="PlaybackDirection.Alternate"`, `Fill="FillMode.Both"`. Times are `TimeSpan`: `Duration="TimeSpan.FromSeconds(1.2)"`. Loop forever: `Iterations="Keyframes.Infinite"`.
- Easing is curve text everywhere (`Easing="CubicOut"`, `"cubic-bezier(...)"`, `"steps(8)"`, `"spring(0.35, 14)"`, `"bouncy(0.3)"`); curves CSS lacks are sampled into CSS `linear()`, so springs play the same curve as MAUI.

### `Keyframes`

Wraps its content in one `div` and animates it (or `Target="@elementRef"` / `TargetId="id"` to animate another element, no wrapper). Tracks are children next to the content.

```razor
<Keyframes Duration="TimeSpan.FromSeconds(1.2)" Iterations="Keyframes.Infinite"
           Direction="PlaybackDirection.Alternate" Fill="FillMode.Both">
    <Track Property="Scale">
        <Key Offset="0" Value="1" />
        <Key Offset="0.5" Value="1.15" Easing="CubicOut" />
        <Key Offset="1" Value="1" />
    </Track>
    <Track Property="BackgroundColor">
        <Key Offset="0" Value="#2563EB" />
        <Key Offset="1" Value="#EC4899" />
    </Track>
    <div class="card">Pulse</div>
</Keyframes>
```

Parameters: `Duration` (300ms), `Delay`, `EndDelay`, `IterationDelay` (baked into the keyframes — WAAPI has none), `Iterations`, `IterationStart`, `Direction`, `Fill`, `Easing` (whole-iteration), `AutoPlay` (true), `Playing` (`bool?`: true plays, false stops — the Blazor trigger action), `Speed` (playbackRate; negative reverses, changing it reverses in place), `ReducedMotion` (`Auto`), `RestoreOnStop`, `Finished`, `StateChanged`, `Target`, `TargetId`, `Class`, `Style`. `Track` also takes `TargetId` (MAUI's `TargetName`).

Spec style mixes in: `<KeyFrame Cue="60%" Easing="CubicOut"><Setter Property="Opacity" Value="1" /></KeyFrame>` (`Cue`: `"60%"`, `"0.6"`, `"from"`, `"to"`).

Properties map MAUI → CSS: `Opacity`; `Scale`/`ScaleX`/`ScaleY` → `scale` (Scale multiplies with ScaleX/Y, as on MAUI); `TranslationX`/`TranslationY` → `translate` (px unless a unit is given, `%` allowed); `Rotation` (shortest arc) / `Spin` → `rotate`; `RotationX`/`RotationY` → `transform` with perspective; `AnchorX`/`AnchorY` → `transform-origin`; `BackgroundColor`/`TextColor` (blended in Oklab); `WidthRequest`/`HeightRequest`; `Margin`/`Padding` (MAUI thickness `10` / `10,20` / `l,t,r,b`). **Any other name is a raw CSS property** (`filter`, `border-radius`, `BorderRadius`, `--my-var`). Transform parts on separate tracks compose. A `Key` with no `Value` is the live value when playback starts (mid-flight included).

### Player (`@ref`)

`Play()`, `Pause()`, `Resume()`, `Stop()` / `Stop(restore)`, `Finish()`, `Seek(TimeSpan)`, `SeekProgress(0..1)` (scrubber), `SetRate(double)`, `PlayAsync(CancellationToken)` → `Task<bool>` (true = ran to the end, false = stopped/replaced/cancelled; never throws for that), `State`, `Rate`, `IsReducedMotionAsync()`. All return tasks; fire-and-forget is fine.

```razor
<Keyframes @ref="player" AutoPlay="false" Duration="TimeSpan.FromSeconds(2)" Fill="FillMode.Both"> ... </Keyframes>
<input type="range" min="0" max="1" step="0.001" @oninput="e => player!.SeekProgress(double.Parse((string)e.Value!, CultureInfo.InvariantCulture))" />
```

### `Choreography` (the storyboard)

Composes child `Keyframes`/`Choreography` into one player. `Mode` (`ChoreographyMode.Parallel` | `Sequence`), `Stagger` (per child; a gap in Sequence), `ApplyTo` (`ChoreographyApplyTo.Self` | `Children`), plus `AutoPlay`, `Playing`, `Speed`, `ReducedMotion` and the player methods. Children inside never play on their own.

```razor
@* Staggered list entrance: the Keyframes is a template applied to each child element *@
<Choreography ApplyTo="ChoreographyApplyTo.Children" Stagger="TimeSpan.FromMilliseconds(60)">
    <Keyframes Duration="TimeSpan.FromMilliseconds(400)" Fill="FillMode.Both">
        <Track Property="TranslationY"><Key Offset="0" Value="16" Easing="Emphasized" /><Key Offset="1" Value="0" /></Track>
        <Track Property="Opacity"><Key Offset="0" Value="0" /><Key Offset="1" Value="1" /></Track>
    </Keyframes>
    @foreach (var item in items) { <div class="row">@item</div> }
</Choreography>
```

### `VisualStates`

```razor
<VisualStates State="@state">
    <VisualState Name="Pressed" Style="scale: .96; opacity: .8" />      @* and/or Class="..." *@
    <VisualTransition To="Pressed" Duration="TimeSpan.FromMilliseconds(80)" Easing="CubicOut" />
    <VisualTransition From="Pressed" Easing="spring(0.35, 12)" Duration="TimeSpan.FromMilliseconds(450)" />
    <StateAnimation State="Busy">
        <Keyframes Iterations="Keyframes.Infinite" Direction="PlaybackDirection.Alternate"> ... </Keyframes>
    </StateAnimation>
    Save
</VisualStates>
```

`From`/`To` optional (`*` = any); most specific wins (both > To > From > neither); a transition without `Duration` is 150ms; no transitions = snap; first render never animates. It is a CSS transition on exactly the properties the two states set (`all` for class states), so interrupting carries on from the on-screen value. `StateAnimation` plays on the VisualStates element while its state is active and restores on exit. Unmatched attributes (events, `data-*`) land on the element.

### `ImplicitTransitions`

```razor
<ImplicitTransitions Style="@boxStyle">     @* ApplyTo="ImplicitTransitionTarget.Children" for every child *@
    <PropertyTransition Property="TranslationX" Duration="TimeSpan.FromMilliseconds(600)" Easing="spring(0.4, 9)" />
    <PropertyTransition Property="Opacity" />                  @* 250ms, Ease by default *@
    <PropertyTransition Property="All" />
</ImplicitTransitions>
```

Any change of a listed property animates (re-render, class swap, script). Transform parts cover both the individual CSS property and `transform`.

### `TransitionGroup<TItem>` (item transitions)

```razor
<TransitionGroup Items="items" Key="i => i.Id" Add="ItemTransition.FadeUp" Remove="ItemTransition.SlideRight"
                 Appear="ItemTransition.FadeUp" Stagger="TimeSpan.FromMilliseconds(40)">
    <ItemTemplate Context="item"><div class="row">@item.Title</div></ItemTemplate>
</TransitionGroup>
```

`Mode`: `ItemTransitionMode.Keyframes` (default) | `Platform` (no managed animation; `shiny-transition-item` class for your CSS) | `None` (never animate — the off switch). Presets (`ItemTransition`): `None`, `Fade`, `FadeUp`, `FadeDown`, `SlideLeft`, `SlideRight`, `Scale`, `Pop`; Remove plays the preset backwards. Custom: `<AddKeyframes>`, `<RemoveKeyframes>`, `<AppearKeyframes>` each holding a `Keyframes`. `Move` (FLIP, default true), `MoveDuration`, `MoveEasing` (Emphasized), `Duration` (250ms), `Easing` (CubicOut), `ReducedMotion`, `ItemClass`. A removed item stays in the page (pinned in place) until its exit finishes; the rest slide into the space at the same time.

### Page transitions (core `PageTransitionHost` + this package)

- One navigation, one transition: `Nav.NavigateTo(uri, PageTransitionType.Flip)` or `Nav.NavigateTo(uri, "drop")`; going back from that page reverses it.
- Custom keyframe transitions, generated as `::view-transition-old/new` CSS — put inside the host:

```razor
<PageTransitionHost ReducedMotion="PageTransitionReducedMotion.Crossfade">
    <CustomPageTransition Name="drop">
        <Enter><Keyframes Easing="BackOut"><Track Property="TranslationY"><Key Offset="0" Value="-60%" /><Key Offset="1" Value="0" /></Track></Keyframes></Enter>
        <Exit><Keyframes><Track Property="Opacity"><Key Offset="0" Value="1" /><Key Offset="1" Value="0" /></Track></Keyframes></Exit>
        @* PopEnter / PopExit optional: left out, Exit and Enter run backwards *@
    </CustomPageTransition>
    @Body
</PageTransitionHost>
```

Pages pick it with `<PageTransition Custom="drop" />`. Length is the host's/page's `Duration`. `PageTransitionHost.ReducedMotion`: `Instant` (default), `Crossfade` (no movement, no hero flights), `Play`.

### Reduced motion (Blazor)

`prefers-reduced-motion` is read through `matchMedia` and kept live. `ReducedMotion="Auto"` (default on every component): one-shots jump to their end state, loops hold their first frame, state/implicit transitions snap, list changes apply at once. `Play` always animates; `Instant` never does.

### Blazor best practices

1. Leave a re-triggerable first key without `Value` so replaying continues from the live value.
2. Scrub with `SeekProgress` from `@oninput`; reverse with `SetRate(-1)` or a negative `Speed`.
3. Drive a `Choreography`, never its children.
4. Use `PropertyTrack` inside `@foreach`/`@if`.
5. Prefer transform and opacity properties; `WidthRequest`/`Margin` animate layout every frame.
6. Use `TransitionGroup Mode="None"` as the off switch, not by removing the component.
