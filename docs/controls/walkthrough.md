# Walkthrough

[← All Shiny Controls](../../README.md)

A guided tour of a page on both hosts: dim everything, cut an animated spotlight around one control at a
time, and say what it does. Onboarding, feature announcements, and workflows people only do once a quarter.

The steps are declared **together on the walkthrough, in order** — not attached to the controls they
describe. That is the point of the control. On a real screen (nested layouts, templated cells, a control
that is only sometimes there) attached ordering scatters the sequence across the markup where nothing can
see it as a whole: reordering means hunting, and a step whose control is conditionally hidden derails the
rest silently. Here reordering is moving a line, and `IsVisible="False"` takes a step out of the run and
re-numbers the counter.

The tour paints into a layer above the page's content, so a target inside a scroll view or a card is
highlighted **where it actually is** rather than clipped by its container.

A step advances **three ways**, which compose per step:

1. **The Next command** — the built-in nav row, or `NextCommand` / `NextAsync()` on your own button.
2. **Using the highlighted control** — `AdvanceOnTargetTap` (MAUI) / `AdvanceOnTargetClick` (Blazor). This
   is "tap Save to continue"; it implies target interaction, since the tap has to reach the control.
3. **A timer** — the step's `Duration` in milliseconds. Zero (the default) waits for the user.

Four displays: **`Popover`** (card, tail, counter and Back/Next/Skip — the default), **`Tooltip`**
(compact, no buttons), **`Inline`** (card without a tail, beside the target), and **`Spotlight`** (no card
at all — the text sits on the dim and the cut-out does the pointing). Or replace the body entirely with
`Content` / `ContentTemplate`.

`RememberRunKey` is what makes onboarding onboarding: the tour runs once per user and then stays out of the
way. It is backed by a replaceable `IWalkthroughStore` — `Preferences` on MAUI, `localStorage` on Blazor —
so the flag can live with the rest of your user state instead. `Restart()` clears it and runs again, which
is the "show me the tour again" menu item.

```xml
<shiny:Walkthrough x:Name="Tour"
                   RememberRunKey="home-v1"
                   AutoStart="True"
                   UseOverlay="True"
                   OverlayOpacity="0.8">

    <!-- No target: a centred welcome card, no cut-out. -->
    <shiny:WalkthroughStep Title="Welcome" Text="Here is what is new." AnimationIn="Pop" />

    <shiny:WalkthroughStep Target="{x:Reference SearchBox}"
                           Title="Find anything"
                           Text="Search across every project you can see."
                           Placement="Bottom" />

    <!-- No card; the cut-out does the pointing. -->
    <shiny:WalkthroughStep Target="{x:Reference Avatar}"
                           Title="Your profile"
                           Display="Spotlight" Highlight="Circle" />

    <!-- Live control: the tap reaches it through the hole, and using it advances. -->
    <shiny:WalkthroughStep Target="{x:Reference SaveButton}"
                           Text="Press Save to finish."
                           AllowTargetInteraction="True"
                           AdvanceOnTargetTap="True" />
</shiny:Walkthrough>
```

```razor
<Walkthrough @ref="tour" RememberRunKey="home-v1" AutoStart="true">
    <Steps>
        <WalkthroughStep Title="Welcome" Text="Here is what is new." />
        <WalkthroughStep Target="#search" Title="Find anything" Text="Search everything."
                         Placement="TooltipPlacement.Bottom" />
        <WalkthroughStep Target="#avatar" Title="Your profile"
                         Display="WalkthroughDisplay.Spotlight"
                         Highlight="WalkthroughHighlight.Circle" />
        <WalkthroughStep Target="#save" Text="Press Save to finish."
                         AllowTargetInteraction="true" AdvanceOnTargetClick="true" />
    </Steps>
</Walkthrough>
```

Targets are `{x:Reference}` on MAUI — prefer it, because it is checked when the XAML compiles, so a renamed
control breaks the build instead of quietly producing a tour that highlights nothing — or a CSS selector on
Blazor. `Walkthrough` renders nothing where it sits, so put it anywhere on the page. Blazor adds keyboard
navigation (arrows and Enter move, Escape leaves) and a scroll lock, both on by default; register the
`localStorage` store with `builder.Services.AddShinyWalkthrough()`.

On MAUI a `Stop()` that lands while the tour is still starting (before the first step shows) ends it as
`Stopped`, so nothing is remembered and an auto-start will show it again. A `Start()` made while a run is
still tearing down (spotlight shrinking, dim fading) is queued and runs once the old run is gone.

On MAUI the spotlight is measured against the page's overlay root, which reaches under the status bar, so
the layer it draws in reaches under it too. It used to keep a layout's default safe-area inset, which on
an edge-to-edge Android page put every spotlight a status bar's height below its target.

`AllowTargetInteraction` is implemented by fencing the backdrop with four transparent panels *around* the
cut-out rather than one full-screen catcher — hit testing has no notion of a hole, so the hole has to be a
gap between panels.

## Foldables

With a fold posture source registered (MAUI), each callout is placed inside the segment its target is in, so it never opens across the hinge from what it points at. A centred callout goes to the larger segment. See [Foldables](foldables.md).

## Automation ids (MAUI)

Set `AutomationId` on the walkthrough and the callout it puts in the page's overlay layer is named after it, for UI tests:

| Part | Id |
|---|---|
| Callout card | `<id>_Callout` |
| Title / text labels | `<id>_Title`, `<id>_Text` |
| Step counter ("1 of 4") | `<id>_Counter` |
| Skip / Back / Next (Finish) | `<id>_Skip`, `<id>_Back`, `<id>_Next` |
