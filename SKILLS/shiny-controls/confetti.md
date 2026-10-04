# Confetti

Magic UI-style confetti (canvas-confetti physics) on both hosts, in the **core** packages. No add-on, no host component.

## MAUI - attached properties (preferred for taps)

```xml
<Button Text="Ship it" shiny:Confetti.Trigger="Tap" />
<shiny:ShinyButton Text="Done" shiny:Confetti.Trigger="Tap" shiny:Confetti.Preset="Stars" />
<Border shiny:Confetti.Trigger="DoubleTap" shiny:Confetti.Preset="Random">...</Border>
```

| Attached property | Type | Default | Notes |
|---|---|---|---|
| `Confetti.Trigger` | `ConfettiTrigger` | `None` | `None` / `Tap` / `DoubleTap`. Setting `None` removes the hook |
| `Confetti.Preset` | `ConfettiPreset` | `Burst` | Ignored when `Options` is set |
| `Confetti.Options` | `ConfettiOptions?` | null | Custom recipe; its origin is replaced by the tap point |

- `Button`, `ImageButton`, `ShinyButton` and `Fab` are hooked via `Clicked` (MAUI buttons ignore gesture recognizers) and burst from their **centre**. `DoubleTap` falls back to a click on them.
- Any other view gets a `TapGestureRecognizer` added and bursts from the **touch point**.
- The view's own click/command still runs. Don't add your own TapGestureRecognizer just for confetti.

Custom options as a XAML resource:

```xml
<shiny:ConfettiOptions x:Key="Unicorns" ParticleCount="30" Spread="80" Scalar="2">
    <shiny:ConfettiOptions.Emoji>
        <x:Array Type="{x:Type x:String}"><x:String>🦄</x:String></x:Array>
    </shiny:ConfettiOptions.Emoji>
</shiny:ConfettiOptions>
```

## Blazor - wrapper component

```razor
<Confetti>                                     @* Trigger=Tap, Preset=Burst by default *@
    <button>Burst</button>
</Confetti>

<Confetti Preset="ConfettiPreset.Stars" Trigger="ConfettiTrigger.DoubleTap">
    <ShinyButton Text="Stars" />
</Confetti>

<Confetti Options="@(new ConfettiOptions { Emoji = ["🎉"], Scalar = 2 })">...</Confetti>
```

The wrapper is `display: contents`, so it doesn't change the layout. The click is handled in JS, so the burst starts with no .NET round trip, and the pointer position is the origin. A keyboard click bursts from the element's centre.

## From code - IConfettiService

The service is registered by `UseShinyControls()` (MAUI, singleton) and by `AddShinyControls()` / `AddShinyConfetti()` (Blazor, **scoped**). Inject `IConfettiService`.

```csharp
await confetti.FireAsync();                                   // canvas-confetti defaults
await confetti.FireAsync(ConfettiPreset.Fireworks);
await confetti.FireAsync(ConfettiPreset.Burst, new Point(0.5, 0.3));  // MAUI
await confetti.FireAsync(ConfettiPreset.Burst, 0.5, 0.3);             // Blazor
await confetti.FireFromAsync(view, ConfettiPreset.Stars);     // MAUI VisualElement / Blazor ElementReference
await confetti.FireFromAsync(view, new ConfettiOptions { Spread = 120 });
confetti.Clear();         // MAUI
await confetti.ClearAsync(); // Blazor
```

Every call **completes when the last particle has faded**, so `await` before showing "done" text.

## Presets

`Burst` (100 particles, 70° spread), `Random`, `Fireworks` (5 s, ignores the origin), `SideCannons` (3 s, ignores the origin), `Stars` (gold stars, no gravity).

## ConfettiOptions (canvas-confetti names and defaults)

`ParticleCount` 50, `Angle` 90 (up), `Spread` 45, `StartVelocity` 45, `Decay` 0.9, `Gravity` 1, `Drift` 0, `Flat` false, `Ticks` 200, `OriginX`/`OriginY` 0.5 (fractions of page/window), `Colors` (MAUI `IList<Color>`, Blazor `IList<string>` CSS colors), `Shapes` (`Square`, `Circle`, `Star`), `Emoji` (when it has anything in it, shapes and colors are ignored; use `Scalar` ≥ 2), `Scalar` 1, `DisableForReducedMotion` false.

## Code generation guidance

- For "confetti when X is tapped": use the attached property (MAUI) or the wrapper (Blazor), not the service.
- For "confetti when an async operation succeeds": inject `IConfettiService` in the view model.
- Origins are **normalised 0-1**, not pixels.
- Don't assign the MAUI `Colors` from strings in C#; use `Color.FromArgb`.
