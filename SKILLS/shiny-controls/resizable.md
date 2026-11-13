# Resizable

Resizable panel groups for both hosts (**core** packages), modelled on the shadcn / Blazor Blueprint `Resizable`: a `ResizablePanelGroup` (Direction Horizontal/Vertical) containing `ResizablePanel`s with a `ResizableHandle` between each pair. Sizes are **percentages** of the group (after the 1-unit handle lines). Groups nest. Panels can be collapsible.

- MAUI: `Shiny.Maui.Controls.ResizablePanelGroup` / `ResizablePanel` / `ResizableHandle` / `ResizableDirection` (`shiny:` xmlns). The group is a `Layout`; its children are the panels and handles.
- Blazor: `Shiny.Blazor.Controls.ResizablePanelGroup` / `ResizablePanel` / `ResizableHandle` / `ResizableDirection`.

## MAUI

```xml
<Grid RowDefinitions="Auto,*">
    <Label Text="Workspace" />
    <!-- the group fills the * row; the Border is the only edge -->
    <Border Grid.Row="1" StrokeShape="RoundRectangle 8" Stroke="LightGray" Padding="0">
        <shiny:ResizablePanelGroup x:Name="Workspace" Direction="Horizontal" LayoutChanged="OnLayoutChanged">
            <shiny:ResizablePanel x:Name="Sidebar" DefaultSize="25" MinSize="15" Collapsible="True"
                                  Collapsed="OnSidebarCollapsed" Expanded="OnSidebarExpanded">
                <local:SidebarView />
            </shiny:ResizablePanel>
            <shiny:ResizableHandle WithHandle="True" />
            <shiny:ResizablePanel MinSize="30">
                <shiny:ResizablePanelGroup Direction="Vertical">
                    <shiny:ResizablePanel DefaultSize="70" MinSize="20">
                        <local:EditorView />
                    </shiny:ResizablePanel>
                    <shiny:ResizableHandle />
                    <shiny:ResizablePanel MinSize="10">
                        <ScrollView><local:TerminalView /></ScrollView>
                    </shiny:ResizablePanel>
                </shiny:ResizablePanelGroup>
            </shiny:ResizablePanel>
        </shiny:ResizablePanelGroup>
    </Border>
</Grid>
```

```csharp
void OnLayoutChanged(object? sender, ResizableLayoutChangedEventArgs e)
    => Preferences.Set("workspace", String.Join(",", e.Sizes));   // restore with Workspace.SetSizes(...) or bind Sizes

void ToggleSidebar()
{
    if (Sidebar.IsCollapsed) Sidebar.Expand(); else Sidebar.Collapse();
}
```

MVVM: `Sizes="{Binding WorkspaceSizes}"` (two-way, `IReadOnlyList<double>`), `IsCollapsed="{Binding SidebarCollapsed}"` (two-way), `LayoutChangedCommand`.

## Blazor

```razor
<div style="height:480px;border:1px solid var(--shiny-color-outline-variant);border-radius:8px;overflow:hidden">
    <ResizablePanelGroup @ref="group" Direction="ResizableDirection.Horizontal" @bind-Sizes="sizes" LayoutChanged="Save">
        <ResizablePanel @ref="sidebar" DefaultSize="25" MinSize="15" Collapsible @bind-IsCollapsed="sidebarCollapsed">
            <Sidebar />
        </ResizablePanel>
        <ResizableHandle WithHandle />
        <ResizablePanel MinSize="30">
            <ResizablePanelGroup Direction="ResizableDirection.Vertical">
                <ResizablePanel DefaultSize="70">Editor</ResizablePanel>
                <ResizableHandle />
                <ResizablePanel MinSize="10">Terminal</ResizablePanel>
            </ResizablePanelGroup>
        </ResizablePanel>
    </ResizablePanelGroup>
</div>

@code {
    ResizablePanelGroup? group;
    ResizablePanel? sidebar;
    IReadOnlyList<double>? sizes;      // load a saved layout here to restore it
    bool sidebarCollapsed;

    Task Save(IReadOnlyList<double> s) => /* persist */ Task.CompletedTask;
    Task Toggle() => sidebarCollapsed ? sidebar!.ExpandAsync() : sidebar!.CollapseAsync();
    Task Reset() => group!.SetSizesAsync(25, 75);
}
```

## API

**ResizablePanelGroup**: `Direction` (Horizontal), `Sizes` (two-way percent list; wins over defaults when the count matches; clamped to limits and scaled to 100), `LayoutChanged` (MAUI event with `ResizableLayoutChangedEventArgs.Sizes` + `LayoutChangedCommand`; Blazor `EventCallback<IReadOnlyList<double>>`), Blazor `KeyboardStep` (10), `Class`, `Style`. Methods: MAUI `SetSizes(params double[])`, `Collapse(panel)`, `Expand(panel)`; Blazor `SetSizesAsync`, `CollapseAsync`, `ExpandAsync`; `Panels`.

**ResizablePanel**: `DefaultSize` (double?, unset panels share the rest), `MinSize` (0), `MaxSize` (100), `Collapsible` (false), `CollapsedSize` (0 = hidden, a few % = rail), `IsCollapsed` (two-way), read-only `Size`; events `Collapsed`, `Expanded`; MAUI `Collapse()`/`Expand()`, Blazor `CollapseAsync()`/`ExpandAsync()`; Blazor-only `Order` (for conditionally rendered panels), `Id`, `Class`, `Style`.

**ResizableHandle**: `WithHandle` (false — grip cue only; it drags either way). MAUI `IsEnabled`, `LineColor` (null = theme outline-variant, primary on hover/drag). Blazor `Disabled`, `AriaLabel`, `Class`, `Style`; CSS var `--shiny-resizable-handle-color`.

## Behaviour

- Drag moves space: the growing side's adjacent panel grows (to its MaxSize); the other side shrinks nearest-first, cascading to the next panel once one hits MinSize.
- Collapsible: holds at MinSize, snaps to CollapsedSize once dragged more than halfway from MinSize towards closed; snaps back open to MinSize the same way. Expand restores the pre-collapse size.
- `Sizes`/`LayoutChanged`/`Size` update when a change settles (drag end, key, collapse/expand, SetSizes), not per frame.
- Blazor keyboard: handles are focusable `role="separator"`; arrows ± `KeyboardStep`, Home/End = min/max of the panel before the handle, Enter = toggle collapse, Escape cancels a drag.
- MAUI: pressing a handle holds an enclosing ScrollView off for the drag; the handle's 16-unit touch target overlaps the panels around its 1-unit line.

## Code Generation Guidance

- The group FILLS its container and never scrolls. MAUI: put it in a `*` Grid row / page / `Border` in a `*` row — never inside a `VerticalStackLayout` or a `ScrollView` on the same axis. Blazor: give the parent a height (or use it inside `.demo-fill`-style full-height layouts); a nested group fills its panel automatically.
- Panel content that can overflow scrolls INSIDE the panel (MAUI: a `ScrollView` inside the `ResizablePanel`; Blazor panels are already `overflow:auto`).
- The group draws no border — wrap it in exactly one bordered container.
- `DefaultSize` values should add up to ≤ 100; leave one panel without a default to take the remainder.
- Always put a `ResizableHandle` between every pair of panels; a handle not between two panels is ignored.
