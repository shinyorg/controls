# Tooltip

[← All Shiny Controls](../../README.md)

The bubble the walkthrough is built on, usable on its own. It either **wraps** the thing it describes or
**points at** something else, and it is drawn in a page-level layer (MAUI) or the browser's top layer
(Blazor) — so it is never clipped by the scroll view, card or grid cell its target lives in, and never
loses a z-index argument.

```xml
<!-- Wrapping: no reference needed, and the wrapper does not disturb the layout. -->
<shiny:Tooltip Text="Saves without closing" Placement="Top" Trigger="LongPress">
    <Button Text="Apply" />
</shiny:Tooltip>

<!-- Anchored and bound: it does not have to sit near its target in the markup. -->
<shiny:Tooltip Target="{x:Reference SaveButton}"
               Title="Why is this disabled?"
               Text="Make a change first."
               Placement="Bottom"
               ShowTail="True"
               IsOpen="{Binding ShowSaveHint}"
               Command="{Binding DismissHint}" />

<!-- The attached shorthand, for places an element does not fit. -->
<Button Text="Sync" shiny:TooltipProperties.Text="Pushes local changes to the server" />
```

```razor
<Tooltip Text="Saves without closing" Placement="TooltipPlacement.Top">
    <ShinyButton Text="Apply" />
</Tooltip>

<Tooltip Target="#save" Title="Why is this disabled?" Text="Make a change first."
         Trigger="TooltipTrigger.Manual" @bind-IsOpen="showHint" />
```

Bind **`IsOpen`**, never `IsVisible` — that one is `VisualElement.IsVisible`, and setting it would hide the
anchor the tooltip is wrapping rather than the bubble.

`Placement` is a preference, not a promise: a side with no room flips to its opposite, then to the roomiest
of the four; the bubble is clamped to stay inside `ScreenMargin`; and the tail slides along the bubble's
edge to keep pointing at the target it was clamped away from, pulled in from the corners so it always meets
a straight edge. So `Placement="Left"` on a control hard against the left edge gives you a bubble on the
right, deliberately.

On MAUI the bubble is placed at its anchor's position in the page's overlay root and drawn in an
edge-to-edge layer, so it lines up with the anchor on pages that reach under the status bar (Android's
default since .NET 10) as well as on ones that don't.

Triggers are `Manual` / `Tap` / `LongPress` / `Hover` / `Focus` on MAUI, and `Manual` / `Hover` / `Click` /
`Focus` / `HoverOrFocus` (the default) / `LongPress` on Blazor — `HoverOrFocus` being the accessible one,
since a hover-only tooltip is unreachable by keyboard.

## Automation ids (MAUI)

The bubble opens in the page's overlay layer, not under the tooltip, so it is named after the tooltip's `AutomationId`. An attached tooltip (`TooltipProperties.Text`) has no id of its own and uses its target view's.

| Part | Id |
|---|---|
| The bubble (tap target) | `<id>_Bubble` |
| Title / text labels | `<id>_Title`, `<id>_Text` |
