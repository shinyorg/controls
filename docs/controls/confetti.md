# Confetti

[← All Shiny Controls](../../README.md)

Confetti bursts on a tap, a click or from code, in the style of [Magic UI's confetti](https://magicui.design/docs/components/confetti). Both hosts are in the **core** packages and need no add-on.

The physics are canvas-confetti's (the library Magic UI wraps), copied frame for frame. The option names and defaults are the same too, so a recipe written for the web can be copied across value for value. Five presets match the Magic UI demos.

- **MAUI**: one transparent, click-through `GraphicsView` per page, in the page's overlay layer. It sits above dialogs and the quick entry popup, and below the screen glow. Its frame timer runs only while something is in the air.
- **Blazor**: one fixed, click-through `<canvas>` per document. It is created on the first burst and removed once the air is clear.

## On any tap: no code

**MAUI**: use the attached properties. They work on any view.

```xml
<Button Text="Ship it" shiny:Confetti.Trigger="Tap" />
<shiny:ShinyButton Text="Stars" shiny:Confetti.Trigger="Tap" shiny:Confetti.Preset="Stars" />
<Border shiny:Confetti.Trigger="DoubleTap">...</Border>
<Button shiny:Confetti.Trigger="Tap" shiny:Confetti.Options="{StaticResource Unicorns}" />
```

How the burst is hooked depends on the view:

- **Buttons** (`Button`, `ImageButton`, `ShinyButton`, `Fab`) are hooked through `Clicked`, because MAUI's own buttons ignore gesture recognizers. They burst from their centre.
- **Every other view** gets a `TapGestureRecognizer` and bursts from the exact point that was touched.

Either way, the view's own click still runs.

**Blazor**: wrap the element. The wrapper is `display: contents`, so it doesn't change the layout.

```razor
<Confetti Preset="ConfettiPreset.Stars">
    <ShinyButton Text="Ship it" />
</Confetti>

<Confetti Trigger="ConfettiTrigger.DoubleTap" Options="@unicorns">
    <div class="card">...</div>
</Confetti>
```

The listener runs in JavaScript, so the burst starts on the same frame as the click; on Blazor Server, nothing round-trips the network first. A click from the keyboard (Enter or Space) bursts from the centre of the control instead of the pointer.

## From code

`IConfettiService` is for celebrating something that wasn't a tap, such as a save that succeeded.

- **MAUI**: registered by `UseShinyControls()`.
- **Blazor**: registered by `AddShinyControls()` or `AddShinyConfetti()`. It is scoped, and no host component is needed.

Every call completes once the last particle has faded, so you can `await` it.

```csharp
await confetti.FireAsync(ConfettiPreset.Fireworks);
await confetti.FireAsync(ConfettiPreset.Burst, new Point(0.5, 0.3));   // Blazor: (preset, 0.5, 0.3)
await confetti.FireFromAsync(saveButton, ConfettiPreset.Stars);         // MAUI: a VisualElement; Blazor: an ElementReference
await confetti.FireAsync(new ConfettiOptions { ParticleCount = 150, Spread = 180 });
confetti.Clear();                                                       // Blazor: ClearAsync()
```

## Presets

| Preset | What it does |
|---|---|
| `Burst` | 100 particles with a 70° spread, from the origin |
| `Random` | A random angle (55–125°), spread and count, so no two taps look the same |
| `Fireworks` | 5 s of 360° bursts at random points near the top, tapering off. Ignores the origin |
| `SideCannons` | 3 s of streams from both side edges. Ignores the origin |
| `Stars` | Three waves of gold stars and sparks, with no gravity |

## ConfettiOptions

| Property | Default | Description |
|---|---|---|
| ParticleCount | 50 | Particles to launch |
| Angle | 90 | Launch direction in degrees (90 is up, 0 is right) |
| Spread | 45 | Total deviation around `Angle`, in degrees |
| StartVelocity | 45 | Launch speed per frame. Each particle gets 0.5–1.5× this |
| Decay | 0.9 | Share of its speed a particle keeps each frame |
| Gravity | 1 | 0 floats |
| Drift | 0 | Sideways drift per frame |
| Flat | false | Stops particles tumbling |
| Ticks | 200 | Frames (at 60 fps) a particle lives, fading as it goes |
| OriginX / OriginY | 0.5 / 0.5 | Launch point as a fraction of the page (MAUI) or window (Blazor) |
| Colors | 7-color palette | `Color` on MAUI, CSS strings on Blazor |
| Shapes | Square, Circle | `Square`, `Circle`, `Star` |
| Emoji | empty | Text to throw (🦄 🎉). When it has anything in it, `Shapes` and `Colors` are ignored. Use `Scalar` 2 or more |
| Scalar | 1 | Size multiplier |
| DisableForReducedMotion | false | Skips the burst when the OS or browser asks for reduced motion |

On MAUI, "reduced motion" means:

- iOS and Mac Catalyst: *Reduce Motion*.
- Android: *Remove animations* (an animator scale of 0).
- Windows: animations switched off.

## Notes

- **MAUI**: the attached trigger installs the page's overlay layer when the view loads, not on the first tap. Installing it re-parents the page's content, which would flash if it happened during the tap.
- **MAUI on AppKit** (`net10.0-macos`): AppKit never realizes a view added after layout, so the canvas may not paint there.
