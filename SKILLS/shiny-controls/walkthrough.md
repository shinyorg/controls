# Walkthrough

A guided tour of a page: dim everything, cut a hole around one control at a time, and say what it does.
Ships in the **core** packages on both hosts — `Shiny.Maui.Controls` and `Shiny.Blazor.Controls`.

Onboarding, feature announcements, and walking someone through a workflow they only do once a quarter.

## The shape, and why

**Steps are declared together on the walkthrough, in order.** They are not attached to the controls they
describe. That is the whole design decision: on a real screen — nested layouts, templated cells, a control
that is only sometimes there — attached ordering scatters the sequence across the markup where nothing can
see it as a whole, so reordering means hunting, and a step whose control is conditionally hidden derails
the rest silently. A collection reorders by moving a line, and `IsVisible="False"` takes a step out of the
run cleanly.

The tour paints into a layer above the page's content (MAUI) or a fixed overlay (Blazor), so a target
inside a scroll view or a card is highlighted **where it actually is** rather than clipped by its container.

## Three ways to advance

Say all three when asked — they compose per step:

1. **The Next command** — the built-in nav row, or `NextCommand` / `NextAsync()` bound to your own button.
2. **Using the highlighted control** — `AdvanceOnTargetTap` (MAUI) / `AdvanceOnTargetClick` (Blazor). This
   is "tap Save to continue". It implies target interaction, since the tap has to reach the control.
3. **A timer** — the step's `Duration` in milliseconds. Zero (the default) waits for the user.

Plus `AdvanceOnBackdropTap` / `AdvanceOnBackdropClick` (off by default, because a stray tap would end a
tour early), and on Blazor the arrow keys / Enter / Escape.

## MAUI

```xml
xmlns:shiny="http://shiny.net/maui/controls"

<shiny:Walkthrough x:Name="Tour"
                   RememberRunKey="home-v1"
                   AutoStart="True"
                   AutoStartDelay="700"
                   UseOverlay="True"
                   OverlayOpacity="0.8"
                   IsRunning="{Binding IsTouring}"
                   CompletedCommand="{Binding TourDone}">

    <!-- No target: a centred welcome card, no cut-out. -->
    <shiny:WalkthroughStep Name="Welcome" Title="Welcome"
                           Text="Here is what is new." AnimationIn="Pop" />

    <shiny:WalkthroughStep Name="Search" Target="{x:Reference SearchBox}"
                           Title="Find anything"
                           Text="Search across every project you can see."
                           Placement="Bottom" />

    <!-- Compact, no buttons, advances itself. -->
    <shiny:WalkthroughStep Target="{x:Reference FilterSwitch}"
                           Text="Filters to yours."
                           Display="Tooltip" Duration="2500" />

    <!-- No card at all; the cut-out does the pointing. -->
    <shiny:WalkthroughStep Target="{x:Reference Avatar}"
                           Title="Your profile" Text="Tap here for settings."
                           Display="Spotlight" Highlight="Circle" />

    <!-- Live control: the tap reaches it through the hole, and using it advances. -->
    <shiny:WalkthroughStep Target="{x:Reference SaveButton}"
                           Text="Press Save to finish."
                           AllowTargetInteraction="True"
                           AdvanceOnTargetTap="True" />
</shiny:Walkthrough>

<!-- Start it from a button without any view-model code. -->
<Button Text="Show me around"
        Command="{Binding Source={x:Reference Tour}, Path=RestartCommand}" />
```

`Walkthrough` renders nothing where it sits (it is invisible and zero-size), so put it anywhere on the page.

### Walkthrough properties

| Property | Default | Notes |
| --- | --- | --- |
| `Steps` | — | The content property, so steps are just children. |
| `IsRunning` | false | **Two-way.** Set true to start; written back false when it ends. |
| `AutoStart` / `AutoStartDelay` | false / 400 | The delay lets the page finish laying out — measuring mid-entrance-animation highlights where a target *was*. |
| `RememberRunKey` | null | Runs once per user, then stays away. Unset means every time. |
| `RememberOnSkip` | true | A skipped tour counts as seen. |
| `UseOverlay` | true | Off leaves the app live and the callouts floating; also disables the cut-out. |
| `OverlayColor` / `OverlayOpacity` | theme scrim / 0.8 | |
| `Highlight` | `RoundedRectangle` | `Rectangle` / `Circle` / `Ellipse` / `None`. |
| `HighlightPadding` / `HighlightCornerRadius` | 6 / 10 | |
| `RingColor` / `RingThickness` | null / 0 | Outline traced round the cut-out. |
| `SpotlightMoveDuration` | 320 | How long the spotlight takes to travel between targets. |
| `ShowNavigation` / `ShowStepCounter` / `ShowSkip` / `ShowBack` | true | |
| `NextText` / `BackText` / `SkipText` / `FinishText` | Next / Back / Skip / Done | |
| `AdvanceOnBackdropTap` | false | |
| `ScrollToTarget` | true | Brings each target into view first. |
| `CalloutColor` / `CalloutTextColor` / `CalloutCornerRadius` / `MaxCalloutWidth` | theme / theme / unset / 320 | |
| `CalloutOffset` / `ScreenMargin` | 14 / 16 | |

Read-only: `StepCount`, `StepNumber`, `CurrentStep`, `CurrentStepIndex`, `HasRun`.

Commands to bind straight to a button: `StartCommand`, `StopCommand`, `NextCommand`, `BackCommand`,
`SkipCommand`, `RestartCommand`.

Commands raised outward: `StartedCommand`, `StepChangedCommand` (step name), `CompletedCommand`,
`SkippedCommand`, `EndedCommand` (`WalkthroughEndReason`).

Methods: `Start(fromIndex)`, `Stop()`, `Next()`, `Back()`, `Skip()`, `GoTo(name|index)`, `Reset()`,
`Restart()`. Static: `Walkthrough.ClearRun(key)`, `Walkthrough.Store`.
On MAUI, `Start()` while a stopped run is still animating out is queued (not dropped), and a `Stop()`
during start-up ends the run as `Stopped` (not remembered).

Events: `Started`, `StepChanged`, `Ended`.

### Step properties

| Property | Default | Notes |
| --- | --- | --- |
| `Target` | null | `{x:Reference}`. **Prefer this** — it is checked at compile time, so a renamed control breaks the build instead of quietly touring nothing. |
| `TargetName` | null | `x:Name`, resolved when the step shows. For controls made in code. |
| `Name` | null | For `GoTo` and `CurrentStep`. |
| `Title` / `Text` | null | |
| `Content` / `ContentTemplate` | null | Your own view instead of title/text. Binding context is the page's. |
| `IsVisible` | true | False drops the step from the run and re-numbers the counter. |
| `Display` | `Popover` | See below. |
| `Placement` | `Auto` | `Top` / `Bottom` / `Left` / `Right` / `Center`. |
| `Duration` | 0 | **Dwell** time in ms before auto-advancing. Not animation time. |
| `DurationIn` / `DurationOut` | 260 / 180 | Callout animation lengths. |
| `AnimationIn` | `Zoom` | `None` / `Fade` / `Slide` / `Zoom` / `Pop`. |
| `AnimationOut` | `Fade` | `None` / `Fade` / `Slide` / `Zoom` / `Pop`. |
| `Highlight` / `HighlightPadding` / `HighlightCornerRadius` | null | Null inherits from the walkthrough. |
| `AllowTargetInteraction` | false | Taps inside the cut-out reach the real control. |
| `AdvanceOnTargetTap` | false | Implies the above. |
| `ScrollToTarget` | null | Null inherits. |
| `EnteredCommand` / `LeftCommand` / `CommandParameter` | null | |

### The four displays

- **`Popover`** (default) — full card: title, text, "2 of 5", Back/Next/Skip, with a tail.
- **`Tooltip`** — compact bubble, tail, no title and no buttons. Pair it with `Duration`.
- **`Inline`** — the same card without a tail, beside the target rather than pointing at it.
- **`Spotlight`** — no card; the text sits on the dim and the cut-out does the pointing. Needs
  `UseOverlay`, and **falls back to `Popover`** without it, because bare text on live content is unreadable.

## Blazor

```razor
<Walkthrough @ref="tour" RememberRunKey="home-v1" AutoStart="true">
    <Steps>
        <WalkthroughStep Name="Welcome" Title="Welcome" Text="Here is what is new." />
        <WalkthroughStep Target="#search" Title="Find anything" Text="Search everything."
                         Placement="TooltipPlacement.Bottom" />
        <WalkthroughStep Target="#avatar" Title="Your profile" Text="Settings live here."
                         Display="WalkthroughDisplay.Spotlight"
                         Highlight="WalkthroughHighlight.Circle" />
        <WalkthroughStep Target="#save" Text="Press Save to finish."
                         AllowTargetInteraction="true" AdvanceOnTargetClick="true" />
    </Steps>
</Walkthrough>
```

Same surface, with these differences:

- `Target` is a **CSS selector string**.
- `AdvanceOnTargetClick`, `AdvanceOnBackdropClick`.
- Methods are async and on the component (`@ref`): `StartAsync`, `StopAsync`, `NextAsync`, `BackAsync`,
  `SkipAsync`, `GoToAsync`, `ResetAsync`, `RestartAsync`. There are no `ICommand` properties.
- Callbacks are `EventCallback`: `Started`, `StepChanged`, `Completed`, `Skipped`, `Ended`.
- Extra: `EnableKeyboard` (default true — arrows/Enter move, Escape leaves; a tour nobody can leave is a
  trap) and `LockScroll` (default true).
- Colours are CSS strings.
- `builder.Services.AddShinyWalkthrough()` registers the `localStorage` store behind `RememberRunKey`.
  It is **optional**: with no store the tour simply runs every time rather than failing.

## Remembering a run

`RememberRunKey` is what makes onboarding onboarding. Behind it:

- MAUI: `IWalkthroughStore`, defaulting to `Preferences`. Assign `Walkthrough.Store` at startup to put the
  flag somewhere shared instead. On the plain `net11.0` head (AppKit, GTK4) `Preferences` is unavailable,
  so it falls back to memory — once per launch rather than persisted.
- Blazor: `IWalkthroughStore` in DI, defaulting to `localStorage`. Register your own with
  `AddShinyWalkthrough<T>()`.

`Reset()` / `ResetAsync()` clears the flag; `Restart()` clears it and starts — that is the "show me the
tour again" menu item.

## Gotchas

- A step whose target cannot be resolved does not fail: it shows centred with no cut-out. Prefer
  `{x:Reference}` on MAUI so a typo is a build error instead.
- `AllowTargetInteraction` is implemented by fencing the backdrop with four panels around the hole rather
  than one full-screen catcher — hit testing has no notion of a hole. With `UseOverlay="False"` there are
  no panels at all and the whole app stays live.
- On MAUI, `AdvanceOnTargetTap` on a `Button` / `ImageButton` / `ShinyButton` hooks `Clicked`, not a tap
  gesture — those handle the press natively and never run gesture recognizers.
- Starting a tour before the page has been laid out measures nothing. `AutoStartDelay` exists for this;
  keep it non-zero when the page animates in.

## Automation ids (MAUI)

Set `AutomationId` on the walkthrough and the callout it puts in the page's overlay layer is named after it, for UI tests:

| Part | Id |
|---|---|
| Callout card | `<id>_Callout` |
| Title / text labels | `<id>_Title`, `<id>_Text` |
| Step counter ("1 of 4") | `<id>_Counter` |
| Skip / Back / Next (Finish) | `<id>_Skip`, `<id>_Back`, `<id>_Next` |
