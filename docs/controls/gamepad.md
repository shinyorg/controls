# Gamepad

[← All Shiny Controls](../../README.md)

An on-screen game controller for **MAUI** (iOS, Android) and **Blazor** (built for mobile browsers). Lay it over a game as a full-screen overlay, or place it inline like any other control. Five layouts — **NES**, **Super Nintendo**, the modern **Xbox / PlayStation** two-stick pad, a **twin-stick** shooter pad, and a six-button **arcade** panel — each relabelled with an Xbox, PlayStation, Nintendo, Super Nintendo or NES face style.

**It is a real controller.** `GamepadView.Gamepad` is a [Shiny.Gamepad](https://www.nuget.org/packages/Shiny.Gamepad) `IGamepad` of kind `GamepadKind.Virtual`. With the registration below it is also reported by `IGamepadManager.GetGamepads()` next to any physical controller, and it connects and disconnects as the view appears and goes. Game code written against Shiny.Gamepad needs no changes to be played with it.

| Package | |
|---|---|
| `Shiny.Maui.Controls.Gamepad` | `GamepadView` for MAUI — `shiny:GamepadView` in XAML |
| `Shiny.Blazor.Controls.Gamepad` | `<GamepadView>` for Blazor |
| `Shiny.Controls.Gamepad.Shared` | The engine both hosts share: layouts, multi-touch hit testing, stick and d-pad maths, `VirtualGamepad`, `VirtualGamepadManager` |

<!-- TODO: capture screenshots for gamepad -->

## Setup

```csharp
// MAUI - MauiProgram.cs
builder.UseShinyGamepad();

// Blazor - Program.cs
builder.Services.AddShinyGamepad();
```

Call these **instead of** Shiny.Gamepad's own `AddGamepads()`. Each one registers the platform's physical-controller manager itself, then wraps it in a `VirtualGamepadManager`, so one `IGamepadManager` reports both kinds. An `AddGamepads()` called afterwards would replace the wrapper.

- On MAUI, `UseShinyGamepad()` also registers the native multi-touch handler on iOS and Android, so it is required there.
- **Android physical controllers** also need Shiny's hosting (`UseShiny()` from `Shiny.Hosting.Maui`). Without it the on-screen pad still works, physical controllers are not reported, and a trace line says why.
- Blazor registers everything **Scoped**. On Blazor Server, a singleton would share one user's pad with every connected user.
- On Blazor the component works without `AddShinyGamepad()` too. It just isn't listed by `IGamepadManager`, and `HideWhenControllerConnected` has nothing to watch.

## Overlay a game

```xml
<Grid>
    <local:MyGameView />

    <shiny:GamepadView x:Name="Pad"
                       Preset="Snes"
                       IdleOpacity="0.35"
                       HideWhenControllerConnected="True" />
</Grid>
```

```razor
<div style="position:relative;height:100vh">
    <MyGame />
    <div style="position:absolute;inset:0;pointer-events:none">
        <GamepadView Preset="GamepadPreset.Snes" IdleOpacity="0.35" HideWhenControllerConnected="true" />
    </div>
</div>
```

The default `Sizing="Anchored"` pins each element to a corner or edge. The d-pad stays under the left thumb and the buttons under the right, in portrait or landscape. On a view smaller than the layout's design canvas, such as a portrait phone, the layout shrinks to fit so the two clusters never meet. It never grows past its natural, thumb-sized scale. **A touch that lands on no element falls through to the game underneath** (`PassThrough`, on by default):

- **iOS**: the platform view declines the hit test.
- **Android**: the view declines `ACTION_DOWN`, so the parent offers the touch to the next view down.
- **Blazor**: only the hit shapes take pointer events.

## Inline controller

```xml
<shiny:GamepadView Preset="Nes" Sizing="Uniform" HeightRequest="220" />
```

`Sizing="Uniform"` lays the whole layout out on its own design canvas and scales it uniformly into the space it's given, drawing the controller body behind the buttons. Use `ShowBody` to force the body on or off.

## Reading input

```csharp
// every frame - exactly as for a physical controller
var state = Pad.Gamepad.GetState();
var move = state.GetMovement();          // left stick with a deadzone
if (state.IsPressed(GamepadButton.A)) Jump();

// or events (the view's are raised on the UI thread)
Pad.ButtonChanged += (_, e) => Debug.WriteLine($"{e.Button} {(e.IsPressed ? "down" : "up")}");
```

On Blazor, use `OnButtonChanged`, `OnAxisChanged`, or `@ref` plus `view.Gamepad.GetState()`.

Stick Y is **positive upward**, as on every Shiny.Gamepad backend. Magnitude is clamped to 1.

### Buttons are positional

`GamepadButton.A` is always the **bottom** face button and `B` the **right** one, whatever is printed on them — exactly as Shiny.Gamepad reports physical controllers:

| Layout | Label → reports |
|---|---|
| SNES | bottom **B** → `A`, right **A** → `B`, left **Y** → `X`, top **X** → `Y` |
| NES | left **B** → `A`, right **A** → `B` (as Nintendo maps them on its later pads) |
| Standard + PlayStation style | cross → `A`, circle → `B`, square → `X`, triangle → `Y` |

A face style only relabels. It never changes what a position reports.

## Layouts and face styles

| `Preset` | Elements | Default `FaceStyle` |
|---|---|---|
| `Nes` | d-pad, B, A, Select, Start | `Nes` |
| `Snes` | d-pad, Y/X/B/A diamond, L, R, Select, Start | `SuperNintendo` |
| `Standard` (default) | two sticks, d-pad, diamond, LB/RB, LT/RT, View/Menu, Home | `Xbox` |
| `TwinStick` | two sticks, pause | `Xbox` |
| `Arcade` | stick, two rows of three buttons, Select, Start | `Xbox` |

`FaceStyle` overrides the preset's labels: `Xbox`, `PlayStation` (drawn cross/circle/square/triangle glyphs), `Nintendo` (Switch labels, ZL/ZR, −/+), `SuperNintendo` (coloured B/A/Y/X) or `Nes`.

## Play feel

| Feature | How |
|---|---|
| Multi-touch | A thumb on the stick while the other presses buttons. iOS and Android track every finger natively; other MAUI platforms read one pointer (enough for a mouse). |
| Thumb rolls | A finger between two neighbouring face buttons presses both. Sliding a finger from one button onto another moves the press. |
| D-pad | `DPadMode="EightWay"` (diagonals, default) or `FourWay`. The d-pad keeps its finger until it lifts, so a thumb that drifts off the edge keeps steering. |
| Floating stick | `GamepadElement.IsFloating = true`: the stick centres wherever the thumb lands within its `FloatingZone`. |
| Stick click | Double-tap and hold a stick for L3/R3 (`StickClickEnabled`). |
| Triggers | Report their axis at `1.0` while held. |
| Turbo | `GamepadElement.IsTurbo = true` rapid-fires while held, at `TurboRate` presses per second. |
| Haptics | `ButtonHapticFeedback` (on) ticks for non-directional buttons. `DirectionalHapticFeedback` (off) ticks each time the d-pad takes a new direction. Turbo repeats never tick. Blazor uses `navigator.vibrate` (Android browsers only). |
| Rumble | `Gamepad.SetVibration(...)` drives the device's motor. Android needs the `VIBRATE` permission, iOS plays a fixed-length buzz, and browsers vibrate on Android only. |
| Step aside | `HideWhenControllerConnected` hides the pad, and lets every touch through, while a physical controller is connected. |
| Idle fade | `IdleOpacity` / `IdleDelay` fade the pad after a pause, so it stops covering the game. |

## Let the player rearrange it

```xml
<shiny:GamepadView IsEditing="{Binding Editing}"
                   ControllerLayout="{Binding SavedLayout}"
                   LayoutEdited="OnLayoutEdited" />
```

```csharp
void OnLayoutEdited(object? sender, GamepadLayout layout)
    => Preferences.Set("pad", layout.ToJson());

// later
Pad.ControllerLayout = GamepadLayout.FromJson(Preferences.Get("pad", ""));
```

In edit mode a finger drags an element and a second finger pinches it to resize. `ControllerLayout` is two-way (`@bind-ControllerLayout` on Blazor). The view always works on its own copy, so your instance is never changed underneath you. On MAUI the property is `ControllerLayout`, not `Layout`, which would hide `VisualElement.Layout(Rect)`.

## Custom layouts

```csharp
var layout = GamepadLayouts.Nes();                 // start from a preset...
layout.Elements.Add(new GamepadElement             // ...or build one from nothing
{
    Id = "turbo-a",
    Kind = GamepadElementKind.Button,
    Button = GamepadButton.B,
    Anchor = GamepadAnchor.BottomRight,
    X = -80, Y = -170, Width = 56, Height = 56,
    Label = "A", Color = "#C8202E", IsTurbo = true
});
Pad.ControllerLayout = layout;
```

`X`/`Y` are the element centre's offset from its `Anchor`, in device-independent units (positive is right and down). `Kind` is `Button`, `Trigger`, `DPad` or `Stick`. `Shape` is `Circle`, `Pill` or `Rounded`.

## Properties

| Property | Default | |
|---|---|---|
| `Preset` | `Standard` | Built-in layout |
| `ControllerLayout` | `null` | Custom/saved layout, replacing `Preset` (two-way) |
| `FaceStyle` | `null` | Labels; null uses the layout's own |
| `Sizing` | `Anchored` | `Anchored` overlay or `Uniform` inline |
| `ControllerScale` | `1.0` | Size multiplier in Anchored mode |
| `DPadMode` | `EightWay` | |
| `IsEditing` | `false` | Drag / pinch to rearrange |
| `TurboRate` | `10` | Presses per second |
| `StickClickEnabled` | `true` | Double-tap-hold for L3/R3 |
| `ButtonHapticFeedback` | `true` | Tick on non-directional buttons |
| `DirectionalHapticFeedback` | `false` | Tick on each new d-pad direction |
| `HideWhenControllerConnected` | `false` | |
| `IdleOpacity` / `IdleDelay` | `1.0` / 4s | `1.0` turns fading off |
| `PassThrough` | `true` | Misses reach the view beneath |
| `ShowBody` | `null` | Null draws it in Uniform only |
| `PlayerIndex` | `null` | Reported by `Gamepad.PlayerIndex` |
| `GamepadId` / `GamepadName` | generated / "On-Screen Gamepad" | |
| `ButtonColor`, `PressedColor`, `LabelColor`, `OutlineColor`, `BodyColor`, `AccentColor` | dark translucent | MAUI `Color`; Blazor CSS strings (or the `--shiny-gamepad-*` variables) |

Events: `ButtonChanged`, `AxisChanged`, `LayoutEdited` (Blazor: `OnButtonChanged`, `OnAxisChanged`, `OnLayoutEdited`).

## Platform notes

- **MAUI**: multi-touch and pass-through on iOS and Android. Mac Catalyst, Windows and the plain `net10.0` build read a single pointer and don't pass touches through.
- **Blazor Server**: every pointer is a round trip, so the knob trails the thumb by the connection latency, as does every input the game reads. WebAssembly has no such cost.
- **Browsers**: physical controllers stay invisible until the player presses a button on one (the W3C Gamepad API's anti-fingerprinting rule), so `HideWhenControllerConnected` takes effect on that first press.
- A `VirtualGamepad` that has been disconnected stays disconnected. When a MAUI view is shown again it hands out a new instance with the same `GamepadId`, the same way a physical controller comes back as a new object after it reconnects.
