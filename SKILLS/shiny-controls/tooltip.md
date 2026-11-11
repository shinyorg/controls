# Tooltip

A themed tooltip bubble that points at a target view. Ships in the **core** packages on both hosts —
`Shiny.Maui.Controls` and `Shiny.Blazor.Controls`.

Two usage shapes on both hosts:

1. **Wrapping** — put the tooltip round the thing it describes and it finds its own target.
2. **Anchored** — leave it empty and point it at something else (`Target`). This is the shape a bound,
   view-model-driven tooltip wants, because it does not have to sit anywhere near its target in the markup.

MAUI adds a third: attached properties (`TooltipProperties.Text`) for places an element does not fit —
inside a `DataTemplate` or a cell, where `{x:Reference}` cannot see out anyway.

**The bubble is not drawn where the tooltip is declared.** On MAUI it goes into a layer above the page's
content; on Blazor it goes into the browser's top layer via the popover API. Either way it is never
clipped by the scroll view, card or grid cell its target happens to live in, and never loses a z-index
argument — the two failure modes that make people give up on in-tree popovers.

## Critical: it is `IsOpen`, never `IsVisible`

`IsVisible` is `VisualElement.IsVisible` and always will be. Binding it would hide the **anchor** the
tooltip is wrapping, not the bubble, and would collide with every style and trigger that already targets
it. The open/close binding is **`IsOpen`** (two-way on both hosts).

## MAUI

```xml
xmlns:shiny="http://shiny.net/maui/controls"

<!-- 1. Wrapping: no reference needed. -->
<shiny:Tooltip Text="Saves without closing" Placement="Top" Trigger="LongPress">
    <Button Text="Apply" />
</shiny:Tooltip>

<!-- 2. Anchored and bound. -->
<shiny:Tooltip Target="{x:Reference SaveButton}"
               Title="Why is this disabled?"
               Text="Make a change first."
               Placement="Bottom"
               ShowTail="True"
               IsOpen="{Binding ShowSaveHint}"
               Command="{Binding DismissHint}" />

<!-- 3. Attached shorthand. -->
<Button Text="Sync"
        shiny:TooltipProperties.Text="Pushes local changes to the server"
        shiny:TooltipProperties.Trigger="LongPress" />
```

### Properties

| Property | Default | Notes |
| --- | --- | --- |
| `Text` / `Title` | null | Body, and an optional bold heading above it. |
| `ContentTemplate` | null | Replaces the title/text pair. Binding context is inherited, so it reaches the page's view-model. |
| `Target` | null | `{x:Reference}`. Defaults to the tooltip's own `Content` when wrapping. |
| `TargetName` | null | `x:Name` resolved through the name scope, for where `{x:Reference}` cannot reach. |
| `IsOpen` | false | **Two-way.** Not `IsVisible` — see above. |
| `Trigger` | `Manual` | `Manual` / `Tap` / `LongPress` / `Hover` / `Focus`. `Tap` anchors through `Clicked` when the anchor is a `Button` or `ImageButton` — those consume touch natively and never route it to their `GestureRecognizers`, so a tap gesture on one would never fire. |
| `Placement` | `Auto` | `Auto` / `Top` / `Bottom` / `Left` / `Right` / `Center`. |
| `ShowTail` | true | The pointer back at the target. Always off for `Center`. |
| `TailSize` | 7 | Half the tail's base width. |
| `ShowDelay` | 0 | ms a trigger must persist before opening. |
| `AutoDismissDelay` | 0 | ms before it closes itself. Zero leaves it up. |
| `LongPressDelay` | 450 | ms for the `LongPress` trigger. |
| `DismissOnTap` | true | Tapping the bubble closes it. |
| `DismissOnTapOutside` | true | Ignored for `Hover` and `Focus`. |
| `Offset` / `ScreenMargin` | 8 / 12 | Gap to the target; clearance from the page edges. |
| `MaxBubbleWidth` | 280 | Long text wraps rather than spanning the screen. |
| `BubbleColor` / `TextColor` / `BorderColor` | null | Unset follows the theme (inverse surface). |
| `CornerRadius` | unset (-1) | Negative follows the theme's corner token. |
| `BorderThickness` / `BubblePadding` / `HasShadow` | 0 / 12,8 / true | |
| `Animation` / `AnimationDuration` | `Scale` / 160 | `None` / `Fade` / `Scale` / `Slide`. |
| `Command` / `CommandParameter` | null | Runs when the bubble is tapped, before `DismissOnTap`. |
| `OpenedCommand` / `ClosedCommand` | null | |

Methods: `Show()`, `Hide()`, `Toggle()`. Events: `Opened`, `Closed`, `Tapped`.

## Blazor

```razor
@* 1. Wrapping. The wrapper is display:contents, so layout is untouched. *@
<Tooltip Text="Saves without closing" Placement="TooltipPlacement.Top">
    <ShinyButton Text="Apply" />
</Tooltip>

@* 2. Anchored by selector, bound. *@
<Tooltip Target="#save"
         Title="Why is this disabled?"
         Text="Make a change first."
         Placement="TooltipPlacement.Bottom"
         Trigger="TooltipTrigger.Manual"
         @bind-IsOpen="showHint"
         Clicked="OnHintClickedAsync" />
```

Same surface, with these differences:

- `Target` is a **CSS selector string**, not a reference. Resolved each time the bubble opens, so it works
  for content that comes and goes.
- `Trigger` adds `HoverOrFocus` (**the default**, and the accessible one — a hover-only tooltip is
  unreachable by keyboard). `Tap` is `Click`; `LongPress` is the touch equivalent of hover.
- `Clicked` replaces `Command`; `DismissOnClick` replaces `DismissOnTap`.
- `MaxWidth`, `BubbleColor` and `TextColor` are CSS strings.
- Methods are async: `ShowAsync()`, `HideAsync()`, `ToggleAsync()`.
- There is no `DismissOnTapOutside`: the bubble is in the top layer and never blocks the page.

## Placement is a preference, not a promise

`TooltipPlacementSolver` (public, pure, on MAUI; the same rules in `tooltip.js` on Blazor) applies four
rules in order. `Auto` prefers bottom, then top, then right, then left.

1. A side with no room **flips to the opposite** side.
2. If neither fits, the **roomiest** side wins.
3. The bubble is **clamped** to stay inside `ScreenMargin`.
4. The **tail slides along** the bubble's edge to keep pointing at the target it was clamped away from,
   pulled in from the corners so it always meets a straight edge.

Consequence worth stating to a user: setting `Placement="Left"` on a control hard against the left edge
gives a bubble on the right. That is deliberate.

## Gotchas

- A tooltip on a control inside a `ScrollView` follows it while open — the bubble re-places on scroll.
- A tooltip inside a `CollectionView` / `CarouselView` item template points at its own row (MAUI reads the
  item's position from the native views), so putting one in an item template is fine.
- MAUI's `Hover` trigger needs a pointer; it is a no-op on phones. Use `LongPress` there, or `Focus` for
  keyboard reachability.
- The MAUI attached form (`TooltipProperties`) creates a real `Tooltip` behind the scenes, driven from the
  target's own load/unload. Reach for the element form when you need binding, a template or a command.
- Blazor's popover fallback: a browser without the popover API still gets a fixed-position bubble, which
  can be trapped by a transformed ancestor's containing block. Every current browser has the API.

## Automation ids (MAUI)

The bubble opens in the page's overlay layer, not under the tooltip, so it is named after the tooltip's `AutomationId`. An attached tooltip (`TooltipProperties.Text`) has no id of its own and uses its target view's.

| Part | Id |
|---|---|
| The bubble (tap target) | `<id>_Bubble` |
| Title / text labels | `<id>_Title`, `<id>_Text` |
