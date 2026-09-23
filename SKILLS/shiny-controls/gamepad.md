# Gamepad

An on-screen game controller for **MAUI (iOS, Android)** and **Blazor (mobile browsers)**, as a
full-screen overlay over a game or an inline controller. It **is** a Shiny.Gamepad `IGamepad`
(`GamepadKind.Virtual`), so game code reads it exactly like a physical controller.

- **MAUI** — `Shiny.Maui.Controls.Gaming.GamepadView` (package `Shiny.Maui.Controls.Gamepad`,
  `shiny:GamepadView` in XAML via `http://shiny.net/maui/controls`)
- **Blazor** — `Shiny.Blazor.Controls.Gaming.GamepadView` (package `Shiny.Blazor.Controls.Gamepad`;
  `@using Shiny.Blazor.Controls.Gaming`, `@using Shiny.Controls.Gaming`, `@using Shiny.Gamepad`)
- **Engine** — `Shiny.Controls.Gamepad.Shared`, namespace `Shiny.Controls.Gaming` (`GamepadLayout`,
  `GamepadElement`, `GamepadLayouts`, `GamepadPreset`, `GamepadFaceStyle`, `VirtualGamepad`,
  `VirtualGamepadManager`, `GamepadEngine`)

## Registration — always emit this

```csharp
// MAUI
builder.UseShinyGamepad();
// Blazor
builder.Services.AddShinyGamepad();
```

- Emit it **instead of** Shiny.Gamepad's `AddGamepads()` — never both, and never `AddGamepads()` after it
  (it would replace the wrapper that merges on-screen and physical pads).
- MAUI needs it for multi-touch: it registers the iOS/Android handler.
- Android physical controllers additionally need `UseShiny()` (Shiny.Hosting.Maui). The on-screen pad
  works without it.

## Rules for generated code

1. **Read input from `Gamepad`, not from the view's visuals**: `pad.Gamepad.GetState()` in a loop, or
   `ButtonChanged`/`AxisChanged` (MAUI, UI thread) / `OnButtonChanged`/`OnAxisChanged` (Blazor). Game
   code that already uses `IGamepadManager` needs nothing — the pad is in `GetGamepads()` and raises
   `Connected`.
2. **Buttons are positional.** `GamepadButton.A` = bottom face button, `B` = right, `X` = left, `Y` =
   top, whatever the label. SNES "B" reports `A`; SNES "A" reports `B`; NES B → `A`, NES A → `B`.
   PlayStation cross → `A`. Never "fix" this by remapping — it matches physical controllers.
3. Stick Y is **positive up**. Use `state.GetMovement()` / `GetLook()` for deadzoned sticks and
   `state.DPad` for the d-pad as a vector.
4. MAUI property names: **`ControllerLayout`** (not `Layout` — that hides `VisualElement.Layout`) and
   **`ControllerScale`** (not `Scale` — that scales pixels but not touch targets). Blazor uses the same
   names for parity.
5. Overlay = put the view **over** the game in the same Grid cell (MAUI) or an absolutely positioned
   box (Blazor) and leave `Sizing` at `Anchored`. Anchored shrinks itself on a portrait phone (smaller than the
   layout's design canvas) and never grows — don't compensate with `ControllerScale`. Inline = `Sizing="Uniform"` with a height.
6. Presets come from `GamepadLayouts.Get(preset)` / `GamepadLayouts.Snes()` etc. — every call returns
   a fresh, editable copy. The view clones whatever `ControllerLayout` it is given.
7. Persist edits with `layout.ToJson()` / `GamepadLayout.FromJson(json)` (source-generated, AOT-safe).
8. Haptics: `ButtonHapticFeedback` (default true) for non-directional buttons,
   `DirectionalHapticFeedback` (default false) for d-pad directions. There is no single
   `HapticFeedback` property.
9. There is no MAUI multi-touch on Mac Catalyst/Windows/net10.0 — one pointer only. Don't promise
   two-thumb play there.

## Overlay (MAUI)

```xml
<Grid>
    <local:GameCanvas />
    <shiny:GamepadView x:Name="Pad"
                       Preset="Standard"
                       FaceStyle="PlayStation"
                       IdleOpacity="0.35"
                       HideWhenControllerConnected="True"
                       ButtonChanged="OnPadButton" />
</Grid>
```

```csharp
// game loop
var s = Pad.Gamepad.GetState();
player.Velocity = new(s.GetMovement().X, -s.GetMovement().Y);
if (s.IsPressed(GamepadButton.A)) player.Jump();
```

## Overlay (Blazor)

```razor
<div class="game" style="position:relative;height:100dvh">
    <GameCanvas />
    <div style="position:absolute;inset:0;pointer-events:none">
        <GamepadView @ref="pad" Preset="GamepadPreset.Snes" IdleOpacity="0.35"
                     OnButtonChanged="OnButton" />
    </div>
</div>

@code {
    GamepadView? pad;
    void OnButton(GamepadButtonChangedEventArgs e) { /* e.Button, e.IsPressed */ }
    // in a loop: pad!.Gamepad.GetState()
}
```

## Inline controller

```xml
<shiny:GamepadView Preset="Nes" Sizing="Uniform" HeightRequest="220" />
```

## Custom layout / tweaks

```csharp
var layout = GamepadLayouts.TwinStick();
layout.Find("ls")!.IsFloating = true;        // stick centres where the thumb lands
layout.Elements.Add(new GamepadElement
{
    Id = "fire", Kind = GamepadElementKind.Button, Button = GamepadButton.RightShoulder,
    Anchor = GamepadAnchor.BottomRight, X = -250, Y = -70, Width = 64, Height = 64,
    Label = "FIRE", Color = "#DC2626", IsTurbo = true
});
Pad.ControllerLayout = layout;
```

`Kind`: `Button`, `Trigger` (drives LT/RT axis at 1.0), `DPad`, `Stick` (its `Button` =
`LeftStick`/`RightStick` picks which stick and the double-tap click). `Anchor`: TopLeft…BottomRight,
Center. `X`/`Y` are the centre's offset from the anchor, positive right/down, in dp/CSS px.

## Edit mode

`IsEditing="True"` → drag moves, second finger pinches to resize; `LayoutEdited` gives the new
layout (already written to the two-way `ControllerLayout` / `@bind-ControllerLayout`).

## Other properties

`Preset` (Nes, Snes, Standard, TwinStick, Arcade), `FaceStyle` (Xbox, PlayStation, Nintendo,
SuperNintendo, Nes; null = preset's), `Sizing`, `ControllerScale`, `DPadMode` (EightWay, FourWay),
`TurboRate`, `StickClickEnabled`, `HideWhenControllerConnected`, `IdleOpacity`, `IdleDelay`,
`PassThrough`, `ShowBody`, `PlayerIndex`, `GamepadId`, `GamepadName`, colours `ButtonColor`,
`PressedColor`, `LabelColor`, `OutlineColor`, `BodyColor`, `AccentColor` (MAUI `Color`, Blazor CSS
string or `--shiny-gamepad-*` vars). Rumble: `pad.Gamepad.SetVibration(...)` drives the device motor
when `pad.Gamepad.Supports(GamepadCapabilities.Vibration)`; Android needs the VIBRATE permission.
