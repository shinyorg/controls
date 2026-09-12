# ButtonGroup

[← All Shiny Controls](../../README.md)

Joins related buttons into one segmented control — inner corners square off and adjacent outlines collapse into a single edge. It ships in the **core** package on both hosts.

```bash
dotnet add package Shiny.Maui.Controls             # MAUI
dotnet add package Shiny.Blazor.Controls           # Blazor
```

```xml
<shiny:ButtonGroup>
    <shiny:ShinyButton Text="Archive" Appearance="Outlined" Command="{Binding ArchiveCommand}" />
    <shiny:ShinyButton Text="Report"  Appearance="Outlined" Command="{Binding ReportCommand}" />
    <shiny:ShinyButton Text="Snooze"  Appearance="Outlined" Command="{Binding SnoozeCommand}" />
</shiny:ButtonGroup>
```

```razor
<ButtonGroup>
    <ShinyButton Text="Archive" Appearance="ButtonAppearance.Outlined" Clicked="ArchiveAsync" />
    <ShinyButton Text="Report"  Appearance="ButtonAppearance.Outlined" Clicked="ReportAsync" />
    <ShinyButton Text="Snooze"  Appearance="ButtonAppearance.Outlined" Clicked="SnoozeAsync" />
</ButtonGroup>
```

It is a container for **actions** first: three buttons that belong together read as one control instead of three, and each segment keeps its own `Command`/`Clicked`. Selection is opt-in on top of that.

## Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `Orientation` | `StackOrientation` / `ToolbarOrientation` | `Horizontal` | Which way the segments run |
| `SelectionMode` | `ButtonGroupSelectionMode` | `None` | `None`, `Single` or `Multiple` |
| `SelectedIndex` | `int` | `-1` | The selected segment (TwoWay / `@bind-SelectedIndex`) |
| `SelectedIndexes` | `IReadOnlyList<int>` | empty | Every selected index, ascending |
| `AllowDeselect` | `bool` | `false` | Whether tapping the selected segment clears it in `Single` |
| `SelectedAppearance` | `ButtonAppearance` | `Filled` | How a selected segment paints |
| `UnselectedAppearance` | `ButtonAppearance` | `Outlined` | How an unselected segment paints, unless it set its own |
| `CollapseBorders` | `bool` | `true` | Pull each outlined segment a hairline into the one before it |
| `CornerRadius` | `double` | unset | The group's four outer corners; unset follows the theme |
| `BorderThickness` | `double` | unset | How much of an overlap the collapse takes (MAUI) |
| `ClusterSpacing` | `double` | `8` | Gap between nested groups (MAUI) |

Events: `SelectionChanged`, plus `SelectionChangedCommand`/`SelectionChangedCommandParameter` on MAUI.

## Selection

`SelectionMode` defaults to `None`, and with it the group touches nothing — it joins the segments visually and gets out of the way. Turn it on and the same markup becomes a segmented picker:

```xml
<shiny:ButtonGroup SelectionMode="Single" SelectedIndex="{Binding RangeIndex}">
    <shiny:ShinyButton Text="Day" />
    <shiny:ShinyButton Text="Week" />
    <shiny:ShinyButton Text="Month" />
</shiny:ButtonGroup>
```

```razor
<ButtonGroup SelectionMode="ButtonGroupSelectionMode.Single" @bind-SelectedIndex="range">
    <ShinyButton Text="Day" />
    <ShinyButton Text="Week" />
    <ShinyButton Text="Month" />
</ButtonGroup>
```

While a selection mode is on, the group owns each segment's appearance: selected segments take `SelectedAppearance`, the rest take `UnselectedAppearance` — **unless the segment set an `Appearance` of its own**, which wins, so one odd segment in a picker stays odd. Leaving the selection mode hands every appearance back.

`Single` ignores a tap on the already-selected segment: a picker that can be emptied by tapping its own answer again is a picker with no answer. `AllowDeselect="True"` allows it. `Multiple` always toggles.

Only `ShinyButton`s are selectable — a text segment or separator is chrome, and counting it would make the indexes a view model sees depend on where a divider happens to sit.

## Split buttons and static segments

`ButtonGroupSeparator` is a hairline between two segments of the same **filled** appearance, where there is no outline to separate them. `ButtonGroupText` is a static label, unit or prefix.

```xml
<shiny:ButtonGroup>
    <shiny:ShinyButton Text="Follow" Type="Secondary" Command="{Binding FollowCommand}" />
    <shiny:ButtonGroupSeparator />
    <shiny:ShinyButton RightMotionIcon="chevron-down" Type="Secondary"
                       SemanticProperties.Description="More follow options"
                       Command="{Binding MoreCommand}" />
</shiny:ButtonGroup>

<shiny:ButtonGroup>
    <shiny:ButtonGroupText Text="USD" />
    <shiny:ShinyButton Text="−" Appearance="Outlined" Command="{Binding DecreaseCommand}" />
    <shiny:ShinyButton Text="+" Appearance="Outlined" Command="{Binding IncreaseCommand}" />
</shiny:ButtonGroup>
```

```razor
<ButtonGroup>
    <ShinyButton Text="Follow" Type="ButtonType.Secondary" Clicked="FollowAsync" />
    <ButtonGroupSeparator />
    <ShinyButton RightMotionIcon="chevron-down" Type="ButtonType.Secondary"
                 aria-label="More follow options" Clicked="MoreAsync" />
</ButtonGroup>

<ButtonGroup>
    <ButtonGroupText Text="USD" />
    <ShinyButton Text="−" Appearance="ButtonAppearance.Outlined" Clicked="DecreaseAsync" />
    <ShinyButton Text="+" Appearance="ButtonAppearance.Outlined" Clicked="IncreaseAsync" />
</ButtonGroup>
```

## Vertical and nested

`Orientation` stacks the segments into a rail, with the rounding and the collapse turning with it.

A group of groups is a **cluster** rather than one control: each inner group keeps its own merged edges and the two are spaced apart, which is what makes an undo/redo pair beside a bold/italic/underline trio read as two things rather than five.

```xml
<shiny:ButtonGroup>
    <shiny:ButtonGroup>
        <shiny:ShinyButton Text="Undo" Appearance="Outlined" />
        <shiny:ShinyButton Text="Redo" Appearance="Outlined" />
    </shiny:ButtonGroup>
    <shiny:ButtonGroup>
        <shiny:ShinyButton Text="Cut"   Appearance="Outlined" />
        <shiny:ShinyButton Text="Copy"  Appearance="Outlined" />
        <shiny:ShinyButton Text="Paste" Appearance="Outlined" />
    </shiny:ButtonGroup>
</shiny:ButtonGroup>
```

## How the joining works

Worth knowing, because it is where the two hosts genuinely differ:

- **MAUI** — the group is a `StackLayout` with `Spacing` forced to zero. It resolves the theme's corner radius and hairline thickness *numerically* (through a hidden probe in its own resource chain, so a live theme swap re-runs the geometry) and pushes squared-off corners onto each segment. The collapse is a negative margin, applied only to segments that actually paint an outline — a filled segment has no edge to double up, and pulling it into its neighbour would only overlap the two fills.
- **Blazor** — the joining is pure CSS over `:first-child`/`:last-child` with logical corner properties, so it mirrors under an RTL reading direction for free and works for any segment without the group counting children. One consequence to know: a per-button `CornerRadius` is written as an inline custom property and keeps its own rounding inside a group.

`CollapseBorders="False"` turns the overlap off if a design wants the doubled edge.

## Code generation guidance

- Reach for it when several buttons are one decision or one cluster of related actions. A row of unrelated buttons is a row of buttons.
- Keep one appearance and one size across a group — mixing them makes the segments read as separate controls again, which is the thing the group exists to undo.
- Use `SelectionMode` for a segmented picker; use `ShinyTabBar` when the segments switch *pages* rather than a value.
- Give icon-only segments a `SemanticProperties.Description` (MAUI) or `aria-label` (Blazor). Blazor adds `aria-pressed` automatically for segments of a selection group, and nothing at all outside one.
