# FloatingToolbar

**MAUI + Blazor.** A toolbar that floats over a control the way a tooltip does — icons, labels, badges, dropdown menus and an overflow, **horizontal or vertical**, anchored to whichever control triggered it.

Anchoring is the tooltip's: MAUI shares `AnchorTriggerBinder` and `TooltipPlacementSolver`, Blazor shares `tooltip.js`. Reach for `ShinyToolbar` when you want a docked page-level bar; reach for this when the bar belongs to a *control*.

## MAUI

```xml
<Grid>
    <ScrollView>
        <VerticalStackLayout>
            <!-- One bar, every row: each points at the same instance. -->
            <Border BindingContext="{Binding Row1}" shiny:FloatingToolbar.AttachTo="{x:Reference bar}" />
            <Border BindingContext="{Binding Row2}" shiny:FloatingToolbar.AttachTo="{x:Reference bar}" />
        </VerticalStackLayout>
    </ScrollView>

    <!-- Draws nothing itself; it is a handle the rows point at. Put it OUTSIDE the ScrollView. -->
    <shiny:FloatingToolbar x:Name="bar"
                           Trigger="Tap"
                           Placement="Top"
                           Orientation="Horizontal"
                           ItemClickedCommand="{Binding ItemClickedCommand}">
        <shiny:FloatingToolbar.Items>
            <shiny:ShinyToolbarItem Text="Cut" />
            <shiny:ShinyToolbarItem IsSeparator="True" />
            <shiny:ShinyToolbarItem Text="More">
                <shiny:ShinyToolbarItem.Children>
                    <shiny:ShinyToolbarItem Text="Rename" />
                </shiny:ShinyToolbarItem.Children>
            </shiny:ShinyToolbarItem>
        </shiny:FloatingToolbar.Items>
    </shiny:FloatingToolbar>
</Grid>
```

The item type is **`ShinyToolbarItem`**, not `ToolbarItem` — MAUI already has a `ToolbarItem` and the two collide in XAML.

For a single target use `Target="{x:Reference card}"`, `TargetName="card"`, or wrap the target as the toolbar's content.

## Blazor

```razor
<FloatingToolbar Target=".card"
                 Trigger="TooltipTrigger.Hover"
                 Orientation="ToolbarOrientation.Vertical"
                 Placement="TooltipPlacement.Right"
                 Items="@actions"
                 ItemClicked="OnAction" />

@code {
    List<ToolbarItem> actions = [
        new() { Icon = svg, Text = "Cut" },
        new() { IsSeparator = true },
        new() { Icon = svg, Text = "Format", Children = [ new() { Text = "Bold" } ] }
    ];

    void OnAction(FloatingToolbarItemEventArgs e) { /* e.Item, e.TargetIndex */ }
}
```

`Target` is a **CSS selector**. One matching many elements binds all of them: one bar serves the list and `e.TargetIndex` says which was triggered. Reuses the existing `ToolbarItem` type.

## Parameters

| Parameter | Type | Default | Notes |
|---|---|---|---|
| `Items` | item list | empty | Items with `Children` open a dropdown; those may nest. |
| `Target` | `View`/`TargetName` (MAUI) · selector (Blazor) | — | Or wrap the target as content. |
| `Trigger` | `TooltipTrigger` | `Tap` (MAUI) / `Click` (Blazor) | `Manual`, `Tap`/`Click`, `LongPress`, `Hover`, `Focus`; Blazor adds `HoverOrFocus`. |
| `Orientation` | `ToolbarOrientation` | `Horizontal` | Also decides whether overflow measures width or height. |
| `Placement` | `TooltipPlacement` | `Top` | `Auto` picks the side with room. |
| `IsOpen` | `bool` | `false` | Two-way. |
| `ShowLabels` | `bool` | `false` | Text beside the icon. Menus are always labelled. |
| `OverflowEnabled` / `MaxVisibleItems` | `bool` / `int` | `true` / `0` | Zero measures what fits. **The cap counts the overflow button.** |
| `ShowDelay` / `HideDelay` | `int` | `0` / `250` | `HideDelay` is the grace period — see Rules. |
| `LongPressDelay` / `AutoDismissDelay` | `int` | `450` / `0` | |
| `DismissOnItemClick` | `bool` | `true` | |
| `DismissOnTapOutside` | `bool` | `true` | MAUI only. |
| `Animation` / `AnimationLength` | `TooltipAnimation` / `int` | `Scale` / `140` | `None`, `Fade`, `Scale`, `Slide`. Zero length snaps. |
| `Offset` / `ScreenMargin` | `double` | `8` / `12` | |
| `BarColor` / `ForegroundColor` / `CornerRadius` | | theme | |

**Events:** `ItemClicked` → `FloatingToolbarItemEventArgs` (MAUI: `Item`, `Target`, `Context`; Blazor: `Item`, `TargetIndex`), `Opened`, `Closed`. MAUI adds the matching commands.
**Methods:** MAUI `Show()` / `ShowFor(view)` / `Hide()` / `Toggle()`; Blazor `ShowAsync(index)` / `HideAsync()`.

## Rules

- **Never set `HideDelay="0"` on a hover bar.** The pointer has to cross the gap between the target and the buttons; without the grace period the bar is gone before it arrives and no item can ever be clicked.
- **Put the MAUI element outside the `ScrollView`**, as a sibling in a `Grid`. It draws nothing, but it needs a home that does not scroll away.
- **The overflow cap counts the `⋯` button.** A cap of 3 draws 2 real items and the overflow.
- **`Hover` needs a pointer** — it never fires on a phone. Use `LongPress` or `Tap` for touch.
- Triggering a *different* target while the bar is open re-anchors rather than closing.
- MAUI's overlay layer sits **below** the tooltip layer on purpose: item tooltips must draw over the bar.
