# Keyframe Animation

[← All Shiny Controls](../../README.md)

Declarative animation for MAUI and Blazor views — the CSS `@keyframes` model, with the things MAUI's own `Animation` class can't do: **seek**, reverse mid-flight, and animate without breaking bindings. On top of the keyframes sit choreographed storyboards, trigger actions, animated visual-state transitions, implicit property transitions, item transitions and Keyframe page transitions, with reduced motion honoured by default. Together they cover every phase of the MAUI animation spec ([dotnet/maui#40382](https://github.com/dotnet/maui/issues/40382)), on both hosts. Ships as `Shiny.Maui.Controls.Keyframe` (XAML + views) and `Shiny.Blazor.Controls.Keyframe` (the same names over the Web Animations API) on top of `Shiny.Controls.Keyframe.Shared` (the host-neutral timing engine), with `Shiny.Maui.Controls.Keyframe.Export` as an optional headless renderer. Blazor: see [keyframe-blazor.md](keyframe-blazor.md).

```xml
xmlns:kf="http://shiny.net/maui/keyframe"

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

`Easing` takes named curves (`CubicOut`, `BounceOut`, `Emphasized`, `Hold`, …) **and** CSS function syntax, so a curve copied out of a design tool or browser devtools works verbatim: `cubic-bezier(0.34, 1.56, 0.64, 1)`, `steps(8)`, `spring(0.35, 14)`, `bouncy(0.3)`. The syntax is shared with Blazor, where springs become CSS `linear()`. Omit `Value` on a key and it resolves to the target's **live value when playback starts**, so a re-triggered animation continues from where it is instead of snapping.

The same model drives C# timelines and storyboards:

```csharp
var timeline = TimelineBuilder
    .Create(TimeSpan.FromSeconds(1))
    .PingPong()
    .RepeatForever()
    .Animate(view, (v, x) => v.Scale = x, k => k
        .From(1)
        .Key(0.5, 1.2, Easings.CubicOut)
        .To(1))
    .Build();

var player = view.Play(timeline);

player.Rate = -1;                 // reverse, mid-flight
player.SeekProgress(0.35);        // scrub from a slider or gesture
await player.PlayAsync();
```

`Keyframes` also takes `EndDelay`, `IterationDelay` (a pause between iterations, holding the end pose), `ReducedMotion` (`Auto` jumps to the end state when the OS asks for less motion; `Play` animates anyway; `Instant` never animates) and `TargetName`.

### The animation layer

Every built-in track writes through `AnimationLayer` — MAUI's top "from handler" value slot — instead of `SetValue`. The animation shows a value without owning the property: bindings, styles, visual states and triggers keep working underneath, any of them writing the property drops the animated value, and a run that ends without a forwards fill (or is stopped with `StopBehavior.Release`) leaves the property showing its own value. `FadeToAsync` writes a local value instead, which masks the binding from then on. `AnimationLayer.GetBaseValue` reads the value without the animation.

**Notes:** a `{DynamicResource}` set directly on the animated property is still detached by the first frame (set it in a `Style` and it stays live); a `TwoWay`-bound property receives the animated values.

### KeyFrame + Setter (the storyboard shape)

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

Any bindable property animates, blended by type. A frame's `Easing` shapes the segment *arriving* at it (WinUI convention); a `Key`'s shapes the one leaving it (CSS).

### Choreography, playing from code, trigger actions

```xml
<kf:Animate.Choreography>
  <kf:Choreography ApplyTo="Children" Stagger="0:0:0.07">
    <kf:Keyframes Duration="0:0:0.5" Fill="Backwards"> … </kf:Keyframes>
  </kf:Choreography>
</kf:Animate.Choreography>

<DataTrigger TargetType="Button" Binding="{Binding IsBusy}" Value="True">
  <DataTrigger.EnterActions><kf:BeginAnimation Animation="{StaticResource Pulse}" /></DataTrigger.EnterActions>
  <DataTrigger.ExitActions><kf:StopAnimation Animation="{StaticResource Pulse}" /></DataTrigger.ExitActions>
</DataTrigger>
```

```csharp
await card.PlayAsync(popIn);   // true when it finishes, false when stopped or cancelled
card.StopAnimations();         // hands every property back to its binding/style/state
```

`Choreography`: `Mode` (Parallel/Sequence), `Stagger`, `ApplyTo` (Self/Children), `TargetName`, `AutoPlay`, `Speed`, `ReducedMotion`. Actions: `BeginAnimation`, `StopAnimation`, `PauseAnimation`, `ResumeAnimation`, `SkipAnimationToEnd`, `SeekAnimation`.

### Animated visual states

```xml
<Button Text="Save">
  <kf:StateTransitions.Transitions>
    <kf:VisualTransition Duration="0:0:0.15" />
    <kf:VisualTransition To="Pressed" Duration="0:0:0.08" Easing="CubicOut" />
    <kf:VisualTransition To="Error" Animation="{StaticResource Shake}" />
    <kf:StateAnimation State="Busy" Animation="{StaticResource Pulse}" />
  </kf:StateTransitions.Transitions>
  <VisualStateManager.VisualStateGroups> … unchanged … </VisualStateManager.VisualStateGroups>
</Button>
```

State changes from `GoToState`, state triggers and MAUI's own Pressed/PointerOver/Focused/Disabled animate. The most specific `VisualTransition` wins (Group, then From+To); with none matching, the change snaps. A change interrupted mid-flight continues from the value on screen. `StateTransitions.GoToState(view, name, useTransitions: false)` snaps a single change.

### Implicit transitions

```xml
<BoxView Opacity="{Binding Fade}">
  <kf:PropertyTransitions.Transitions>
    <kf:PropertyTransition Property="Opacity" Duration="0:0:0.2" />
    <kf:PropertyTransition Property="Scale" Easing="spring(0.4, 13)" Duration="0:0:0.5" />
  </kf:PropertyTransitions.Transitions>
</BoxView>
```

Any change, from any source, animates. The value that was set stays in the property's own source (`AnimationLayer.GetBaseValue` returns it) and only the visible value moves toward it; while it moves, the property getter returns the on-screen value. `Property="All"` covers every registered property; `BindableProperty="{x:Static …}"` reaches any other property.

### Item transitions

```xml
<CollectionView kf:ItemTransitions.Mode="None"
                kf:ItemTransitions.Appear="{StaticResource FadeUp}"
                kf:ItemTransitions.Add="{StaticResource PopIn}"
                kf:ItemTransitions.Remove="{StaticResource SlideOut}" />
```

`Mode="None"` switches off the platform's item insert/remove/move animations (Android, iOS, Mac Catalyst, Windows). `Appear` plays once per item, staggered as items appear together; `Add` plays for items added later. A removal can't be intercepted after the fact, so `ItemTransitions.RemoveAsync(list, item, () => Items.Remove(item))` plays `Remove` first.

### Page transitions

`KeyframePageTransition` (`Enter`, `Exit`, `PopEnter`, `PopExit`) plugs into core's page-transition animator seam, so a push, pop or modal can be any Keyframes. See [Page Transitions](page-transitions.md).

…and a drawn scene graph rendered to a canvas, the Lottie-shaped lane:

```csharp
var scene = new KeyframeScene(400, 200);
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
```

**Notes:**
- **Evaluation is a pure function of time.** `Evaluate(t)` never looks at the previous frame, which is what makes scrubbing, mid-flight reversal, deterministic export, and non-flaky timing tests possible at all rather than merely approximable.
- **Colour blends in Oklab by default** — sRGB interpolation dips through grey at the midpoint. `ColorInterpolator.Srgb` and `.LinearRgb` are there if you want them.
- **Angles take the shortest arc.** `Rotation` 350° → 10° turns forward 20°, not back 340°. Use `Spin` when you genuinely want multiple turns.
- **Targets are held weakly**, so an infinite animation on a popped page goes inert and gets collected instead of pinning the visual tree.
- **The XAML property registry is explicit, not reflective** — `Property="Opacity"` resolves through hand-registered delegates in `AnimatableProperties`, because reflection and compiled `Expression` both work in the emulator and break under Native AOT. Register your own with `AnimatableProperties.Register(...)`.
- **Layout-affecting properties are the perf cliff.** `WidthRequest`, `HeightRequest`, `Margin` and `Padding` run a full measure/arrange every frame; they work, and `AnimatableProperty.InvalidatesLayout` flags them, but prefer transform and opacity where you have the choice.
- **The clock is vsync.** `CADisplayLink` on iOS and Mac Catalyst (asking for the display's full rate; ProMotion iPhones also need `CADisableMinimumFrameDurationOnPhone` in Info.plist), `Choreographer` on Android, `CompositionTarget.Rendering` on Windows, each with the platform frame timestamp. It never touches MAUI's own ticker, so `FadeTo` and friends are unaffected. The AppKit and GTK heads use the `net11.0` build on a dispatcher timer.
- **Reduced motion is honoured by default** on every animation, transition and item animation (iOS Reduce Motion, Android "Remove animations", Windows animation setting). Seeking is direct manipulation and is never affected.
- Everything ticks in managed code on the UI thread. Native compositor offload (`CAKeyframeAnimation` / `ObjectAnimator` / `ScalarKeyFrameAnimation`) isn't written.
- **It runs on every head.** The engine, XAML and views are platform-neutral; the iOS / Android / Mac Catalyst / Windows builds exist only for the clock, the reduced-motion query and the item-animation off switch.

## Offscreen export (optional package)

`Shiny.Maui.Controls.Keyframe.Export` samples a scene at exact frame times and yields frames lazily, so a long export never holds more than one frame in memory. It is a separate package because it is the only thing in Keyframe that needs a rasterizer — SkiaSharp — and nothing that animates a view ever touches it.

```bash
dotnet add package Shiny.Maui.Controls.Keyframe.Export
```

```csharp
var exporter = new FrameExporter(scene);
var options = new ExportOptions { Fps = 25, Scale = 2.0 };

GifEncoder.EncodeToFile("out.gif", exporter.Frames(options), options.Fps);
```

**Notes:**
- Frame times come from `index / fps` in ticks — never accumulated, and never through `TimeSpan.FromSeconds`, which rounds to whole milliseconds and would quantise every frame at 60fps.
- GIF stores delays in hundredths of a second, so only divisors of 100 are exact: **25 or 50fps** when timing matters. 30fps writes 3cs and actually plays at 33.3fps, and most browsers promote 0–1cs delays to 10cs, so anything above 50fps plays far slower than asked.
- `IFrameRenderer` is an interface — swap `SkiaFrameRenderer` for your own rasterizer if you'd rather not take the Skia dependency.
- MP4/video export is out of scope (it needs a real codec); pipe `FrameExporter.Frames()` to ffmpeg's stdin. APNG/WebP aren't implemented, and there's no Lottie parser yet — though `KeyframeScene` is the scene graph one would need.
