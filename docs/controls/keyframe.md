# Keyframe Animation

[← All Shiny Controls](../../README.md)

Declarative keyframe animation for MAUI views — the CSS `@keyframes` model, with the one thing MAUI's own `Animation` class can't do: **seek**. Ships as `Shiny.Maui.Controls.Keyframe` (XAML + views) on top of `Shiny.Controls.Keyframe.Shared` (the host-neutral timing engine), with `Shiny.Maui.Controls.Keyframe.Export` as an optional headless renderer.

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

`Easing` takes named curves (`CubicOut`, `BounceOut`, `Emphasized`, …) **and** CSS function syntax, so a curve copied out of a design tool or browser devtools works verbatim: `cubic-bezier(0.34, 1.56, 0.64, 1)`, `steps(8)`, `spring(0.35, 14)`. Omit `Value` on a key and it resolves to the target's **live value when playback starts**, so a re-triggered animation continues from where it is instead of snapping.

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
- Everything ticks in managed code on the UI thread today. Transform and opacity tracks map 1:1 onto `CAKeyframeAnimation` / `ObjectAnimator` / `ScalarKeyFrameAnimation`, so **native compositor offload** is the design's biggest remaining perf win — but it isn't written yet.
- **It runs on every head.** Because nothing here touches a platform SDK — the clock is an `IDispatcherTimer`, the view is a `GraphicsView` — the package ships a single `net11.0` target that iOS, Android, Windows, MacCatalyst, macOS AppKit and Linux GTK4 heads all consume.

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
