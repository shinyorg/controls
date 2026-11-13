# Resizable

[← All Shiny Controls](../../README.md)

Panels laid side by side or stacked, with draggable handles between them — the editor/sidebar/terminal layout, a master–detail split, a preview pane. Sizes are percentages of the group, groups nest, and a panel can snap shut and open again. The API follows the shadcn / Blazor Blueprint `Resizable` component: a `ResizablePanelGroup` of `ResizablePanel`s with a `ResizableHandle` between each pair.

```xml
<!-- MAUI: give the group a real size (a * row, a page) - never a ScrollView on the same axis -->
<Border Grid.Row="1" StrokeShape="RoundRectangle 8" Padding="0">
    <shiny:ResizablePanelGroup Direction="Horizontal" LayoutChanged="OnLayoutChanged">
        <shiny:ResizablePanel DefaultSize="25" MinSize="15" Collapsible="True">
            <local:Sidebar />
        </shiny:ResizablePanel>
        <shiny:ResizableHandle WithHandle="True" />
        <shiny:ResizablePanel>
            <shiny:ResizablePanelGroup Direction="Vertical">
                <shiny:ResizablePanel DefaultSize="70"><local:Editor /></shiny:ResizablePanel>
                <shiny:ResizableHandle />
                <shiny:ResizablePanel MinSize="10"><local:Terminal /></shiny:ResizablePanel>
            </shiny:ResizablePanelGroup>
        </shiny:ResizablePanel>
    </shiny:ResizablePanelGroup>
</Border>
```

```razor
<!-- Blazor: the group fills its parent (width/height 100%), so give the parent a height -->
<div style="height:480px;border:1px solid var(--shiny-color-outline-variant);border-radius:8px;overflow:hidden">
    <ResizablePanelGroup Direction="ResizableDirection.Horizontal" @bind-Sizes="sizes">
        <ResizablePanel DefaultSize="25" MinSize="15" Collapsible @bind-IsCollapsed="sidebarCollapsed">
            Sidebar
        </ResizablePanel>
        <ResizableHandle WithHandle />
        <ResizablePanel>
            <ResizablePanelGroup Direction="ResizableDirection.Vertical">
                <ResizablePanel DefaultSize="70">Editor</ResizablePanel>
                <ResizableHandle />
                <ResizablePanel MinSize="10">Terminal</ResizablePanel>
            </ResizablePanelGroup>
        </ResizablePanel>
    </ResizablePanelGroup>
</div>
```

## How it behaves

- **Dragging** a handle grows the panel on one side and shrinks the other side — the panel next to the handle first, then the next one out once that one reaches its `MinSize`, so a long drag pushes through several panels. The growing panel stops at its `MaxSize`.
- **Collapsing.** A `Collapsible` panel holds at its `MinSize`, then snaps shut to `CollapsedSize` once it is dragged more than halfway from its minimum towards closed; dragged back the same way, it snaps open to its minimum. `IsCollapsed` (two-way), `Collapse()` / `Expand()` and the `Collapsed` / `Expanded` events do the same from code, and Expand restores the size it had before.
- **Settled sizes.** `Sizes` (two-way) and `LayoutChanged` update when a change settles — the end of a drag, a key press, a collapse, an expand, `SetSizes` — not on every frame. Bind `Sizes` to save a layout and set it to restore one: it is held to the panels' limits and scaled to add up to 100, and wins over `DefaultSize` when its count matches the panels.
- **Defaults.** Panels with a `DefaultSize` take it; the rest share what is left equally.
- **The handle** is a one-unit line — the only space the group gives it — with a wider touch target laid over the panels either side. It highlights on hover (desktop) and while dragged; `WithHandle` adds a grip, which is only a cue.
- **Keyboard (Blazor).** Handles are focusable `role="separator"` elements with `aria-valuenow/min/max` and `aria-controls`. Arrow keys move by `KeyboardStep` (10%), Home/End take the panel before the handle to its minimum/maximum, Enter toggles collapse (the panel before the handle, or after it if only that one collapses). Escape cancels a drag in progress.
- **Inside a scroller (MAUI).** Pressing a handle holds an enclosing `ScrollView` off for the drag, so it resizes instead of scrolling.

## Properties

### ResizablePanelGroup

| Property | Type | Default | Description |
|---|---|---|---|
| Direction | `ResizableDirection` | Horizontal | `Horizontal` (side by side) or `Vertical` (stacked) |
| Sizes | `IReadOnlyList<double>?` | null | Each panel's size in percent. Two-way (`@bind-Sizes` on Blazor) |
| KeyboardStep (Blazor) | double | 10 | Percent moved per arrow-key press |
| LayoutChanged | event / `EventCallback<IReadOnlyList<double>>` | | The panels settled on new sizes |
| LayoutChangedCommand (MAUI) | ICommand? | null | Executed with the sizes |
| Class / Style (Blazor) | string? | | |

Methods: `SetSizes(params double[])` (MAUI) / `SetSizesAsync(...)` (Blazor), `Collapse(panel)` / `CollapseAsync(panel)`, `Expand(panel)` / `ExpandAsync(panel)`, and `Panels`.

### ResizablePanel

| Property | Type | Default | Description |
|---|---|---|---|
| DefaultSize | double? | null | Starting size in percent; unset panels share the rest |
| MinSize | double | 0 | Smallest drag size, in percent |
| MaxSize | double | 100 | Largest drag size, in percent |
| Collapsible | bool | false | Snaps shut past half its minimum |
| CollapsedSize | double | 0 | Collapsed size — 0 hides it, a few percent leaves a rail |
| IsCollapsed | bool | false | Two-way (`@bind-IsCollapsed`) |
| Size | double | | Read-only current size, updated when a resize settles |
| Order (Blazor) | int? | null | Position for a conditionally rendered panel, which would otherwise be put last |
| Id (Blazor) | string | generated | Element id the handle's `aria-controls` points at |

Events: `Collapsed`, `Expanded`. Methods: `Collapse()` / `Expand()` (MAUI), `CollapseAsync()` / `ExpandAsync()` (Blazor).

### ResizableHandle

| Property | Type | Default | Description |
|---|---|---|---|
| WithHandle | bool | false | Shows a grip in the middle of the line |
| IsEnabled (MAUI) / Disabled (Blazor) | bool | | A disabled handle does not move (and leaves the tab order on Blazor) |
| LineColor (MAUI) | Color? | theme outline-variant | Primary while hovered or dragged |
| AriaLabel (Blazor) | string | "Resize" | |

On Blazor the line colour is the `--shiny-resizable-handle-color` custom property (default `--shiny-color-outline-variant`).

## Layout notes

- The group **fills** the space it is given and never scrolls itself; a panel's content scrolls inside the panel (Blazor panels are `overflow:auto`; on MAUI put a `ScrollView` inside the panel). So the group must have a real size: a `*` grid row or a page on MAUI, a parent with a height on Blazor (a nested group fills its panel). A `VerticalStackLayout` or a `ScrollView` on the same axis has no length to divide.
- The group draws **no border**. Put it in one container that draws the edge (a `Border`, a card, a framed `div`) so the edges never double up.
