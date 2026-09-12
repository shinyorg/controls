# ButtonGroup

Joins related buttons into one segmented control — inner corners square off and adjacent outlines
collapse into a single edge. `ButtonGroup` on both hosts, in the **core** package.

Reach for it when several buttons are one decision or one cluster of related actions. A row of
unrelated buttons is a row of buttons. Use `ShinyTabBar` when the segments switch *pages* rather than
a value.

## Basic Usage

```xml
<!-- Actions: the group joins them and gets out of the way -->
<shiny:ButtonGroup>
    <shiny:ShinyButton Text="Archive" Appearance="Outlined" Command="{Binding ArchiveCommand}" />
    <shiny:ShinyButton Text="Report"  Appearance="Outlined" Command="{Binding ReportCommand}" />
    <shiny:ShinyButton Text="Snooze"  Appearance="Outlined" Command="{Binding SnoozeCommand}" />
</shiny:ButtonGroup>

<!-- A segmented picker: same markup, one property -->
<shiny:ButtonGroup SelectionMode="Single" SelectedIndex="{Binding RangeIndex}">
    <shiny:ShinyButton Text="Day" />
    <shiny:ShinyButton Text="Week" />
    <shiny:ShinyButton Text="Month" />
</shiny:ButtonGroup>

<!-- Split button -->
<shiny:ButtonGroup>
    <shiny:ShinyButton Text="Follow" Type="Secondary" Command="{Binding FollowCommand}" />
    <shiny:ButtonGroupSeparator />
    <shiny:ShinyButton RightMotionIcon="chevron-down" Type="Secondary"
                       SemanticProperties.Description="More follow options"
                       Command="{Binding MoreCommand}" />
</shiny:ButtonGroup>

<!-- Static segment, and a vertical rail -->
<shiny:ButtonGroup>
    <shiny:ButtonGroupText Text="USD" />
    <shiny:ShinyButton Text="−" Appearance="Outlined" Command="{Binding DecreaseCommand}" />
    <shiny:ShinyButton Text="+" Appearance="Outlined" Command="{Binding IncreaseCommand}" />
</shiny:ButtonGroup>

<shiny:ButtonGroup Orientation="Vertical">
    <shiny:ShinyButton LeftMotionIcon="zoom-in"  Appearance="Outlined" SemanticProperties.Description="Zoom in" />
    <shiny:ShinyButton LeftMotionIcon="zoom-out" Appearance="Outlined" SemanticProperties.Description="Zoom out" />
    <shiny:ShinyButton LeftMotionIcon="expand" Appearance="Outlined" SemanticProperties.Description="Reset zoom" />
</shiny:ButtonGroup>
```

## Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `Orientation` | `StackOrientation` | `Horizontal` | Which way the segments run. Inherited from `StackLayout`, but the group defaults it to Horizontal rather than StackLayout's Vertical |
| `SelectionMode` | `ButtonGroupSelectionMode` | `None` | `None` / `Single` / `Multiple` |
| `SelectedIndex` | `int` | `-1` | TwoWay. In `Multiple` it reports the first selected segment |
| `SelectedIndexes` | `IReadOnlyList<int>` | empty | Read-only, ascending |
| `AllowDeselect` | `bool` | `false` | Whether tapping the selected segment clears it in `Single` |
| `SelectedAppearance` | `ButtonAppearance` | `Filled` | How a selected segment paints |
| `UnselectedAppearance` | `ButtonAppearance` | `Outlined` | How an unselected segment paints, unless it set its own |
| `CollapseBorders` | `bool` | `true` | Pull each outlined segment a hairline into the one before it |
| `CornerRadius` | `double` | unset | Outer corners; unset follows the theme's medium corner token |
| `BorderThickness` | `double` | unset | How much of an overlap the collapse takes |
| `ClusterSpacing` | `double` | `8` | Gap between nested groups |

Methods: `Select(int)`, `Deselect(int)`, `ClearSelection()`. Events: `SelectionChanged` with
`SelectedIndex`/`SelectedIndexes`, plus `SelectionChangedCommand` / `SelectionChangedCommandParameter`.
`Segments` is the selectable buttons in visual order.

## Selection rules

- `SelectionMode="None"` (the default) means the group touches **nothing** — no selected index, and
  every segment keeps the appearance it was given.
- While a selection mode is on, the group owns each segment's `Appearance`. A segment that set its own
  `Appearance` keeps it as the *unselected* look, so one odd segment in a picker stays odd. Leaving the
  selection mode hands every appearance back.
- `Single` ignores a tap on the already-selected segment unless `AllowDeselect` is set. `Multiple`
  always toggles.
- Only `ShinyButton`s are selectable. `ButtonGroupText` and `ButtonGroupSeparator` are chrome and are
  skipped when indexing, so a divider never shifts the indexes a view model is bound to.

## Segments

- `ButtonGroupSeparator` — a hairline for a split button, between two segments of the same **filled**
  appearance. Outlined segments already have an edge; do not add one there.
- `ButtonGroupText` — a static label, unit or prefix (`Text`, `TextColor`, `FontSize`,
  `ContentPadding`, `SegmentBackgroundColor`, `BorderColor`, `CornerRadius`). It takes no taps, and is
  not a disabled button: a disabled button reads as an action that is currently unavailable, while this
  was never actionable.
- A nested `ButtonGroup` makes the outer one a **cluster**: each inner group keeps its own merged edges
  and the two are spaced by `ClusterSpacing`.

## Blazor

Parameters mirror MAUI. `Orientation` is `ToolbarOrientation`; `@bind-SelectedIndex` and
`@bind-SelectedIndexes` both work; `SelectionChanged` is an `EventCallback<IReadOnlyList<int>>`;
`CssClass` adds classes and anything else is splatted.

```razor
<ButtonGroup>
    <ShinyButton Text="Archive" Appearance="ButtonAppearance.Outlined" Clicked="ArchiveAsync" />
    <ShinyButton Text="Report"  Appearance="ButtonAppearance.Outlined" Clicked="ReportAsync" />
</ButtonGroup>

<ButtonGroup SelectionMode="ButtonGroupSelectionMode.Single" @bind-SelectedIndex="range">
    <ShinyButton Text="Day" />
    <ShinyButton Text="Week" />
    <ShinyButton Text="Month" />
</ButtonGroup>

<ButtonGroup Orientation="ToolbarOrientation.Vertical">
    <ShinyButton LeftMotionIcon="zoom-in" Appearance="ButtonAppearance.Outlined" aria-label="Zoom in" />
    <ShinyButton LeftMotionIcon="zoom-out" Appearance="ButtonAppearance.Outlined" aria-label="Zoom out" />
</ButtonGroup>
```

Blazor-side differences worth knowing:

- The joining is pure CSS over `:first-child`/`:last-child` with **logical** corner properties, so it
  mirrors under an RTL reading direction for nothing extra.
- A per-button `CornerRadius` writes `--shiny-btn-radius` inline, and inline wins — that button keeps
  its own rounding inside a group.
- A segment of a selection group renders `aria-pressed`; an ordinary action button renders none.
- The group cascades itself, and `ShinyButton` picks it up through a public `[CascadingParameter]`.
  Nothing needs wiring at the call site.

## Code Generation Guidance

- Keep **one appearance and one size** across a group. Mixing them makes the segments read as separate
  controls again, which is the thing the group exists to undo.
- Do not put a `ButtonGroupSeparator` between outlined segments — the collapsed outline is already the
  divider.
- Do not hand-roll a segmented picker out of styled buttons and an index; that is `SelectionMode`.
- Give icon-only segments a `SemanticProperties.Description` (MAUI) or `aria-label` (Blazor).
- The group forces `Spacing` to zero on MAUI. If a design wants spaced buttons, it does not want a
  group — use a `HorizontalStackLayout`.
