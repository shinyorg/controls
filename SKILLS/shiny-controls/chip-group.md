# ChipGroup

A bound set of chips: one per item in `ItemsSource`, selectable singly or in any number, reported back
through `SelectedItem` and `SelectedItems`. `ChipGroup` on MAUI, `ChipGroup<TItem>` on Blazor, in the
**core** package on both.

## Which control

| | Options come from | Selection is |
|---|---|---|
| `ButtonGroup` (see `button-group.md`) | markup | an **index** |
| **`ChipGroup`** | a bound collection | the **items themselves** |
| `TagEntry` (see `tag-entry.md`) | the user typing | the strings typed |

Use `ChipGroup` whenever the options arrive as data. It also wraps, so a dozen filters flow onto several
lines instead of becoming one very long segmented control.

## Basic Usage

```xml
<!-- choice chips - Single is the DEFAULT -->
<shiny:ChipGroup ItemsSource="{Binding Ranges}" SelectedItem="{Binding Range}" />

<!-- filter chips -->
<shiny:ChipGroup ItemsSource="{Binding Filters}"
                 SelectionMode="Multiple"
                 SelectedItems="{Binding SelectedFilters}"
                 MaxSelectionCount="3" />

<!-- action chips: no state at all -->
<shiny:ChipGroup ItemsSource="{Binding Actions}"
                 SelectionMode="None"
                 ChipTappedCommand="{Binding RunCommand}" />

<!-- objects -->
<shiny:ChipGroup ItemsSource="{Binding Teams}"
                 SelectedItem="{Binding Team}"
                 DisplayMemberPath="Name" />
```

## Properties (MAUI)

**Items** — `ItemsSource` (`IEnumerable`), `ItemTemplate` (`DataTemplate`, the item is the binding
context), `ItemDisplayBinding` (`BindingBase`), `DisplayMemberPath` (`string`; ignored when a binding is
set).

**Selection** — `SelectionMode` (`None`/`Single`/`Multiple`, default **Single**), `SelectedItem`
(TwoWay), `SelectedItems` (`IList`, TwoWay), `AllowDeselect` (false), `MaxSelectionCount` (0 =
unlimited), `ShowSelectionCheck` (true).

**Behaviour** — `AllowRemove` (false), `IsReadOnly` (false).

**Layout** — `Wrap` (true), `HorizontalSpacing` (8), `VerticalSpacing` (8).

**Chrome** — `ChipBackgroundColor?`, `ChipTextColor?`, `SelectedChipBackgroundColor?`,
`SelectedChipTextColor?`, `ChipBorderColor?`, `ChipCornerRadius`, `DisabledOpacity` (0.38).

Events: `SelectionChanged`, `ChipTapped`, `ChipRemoving` (cancellable), `ChipRemoved`. Commands:
`SelectionChangedCommand` (+`Parameter`), `ChipTappedCommand`, `ChipRemovedCommand`. Methods:
`Select(item)`, `Deselect(item)`, `ClearSelection()`, `IsSelected(item)`; `Items` is what it currently
has chips for.

## Rules

- `SelectionMode` defaults to **Single** — the opposite of `ButtonGroup`, whose default is `None`.
  Selection is what a chip group is for.
- Re-tapping the selected chip in `Single` does **nothing** unless `AllowDeselect` is on. In `Multiple`
  a second tap always toggles.
- At `MaxSelectionCount` a tap on an unselected chip does nothing; the oldest pick is **not** dropped.
- `SelectedItem` and `SelectedItems` are both kept accurate in every mode.
- The bound `SelectedItems` list is written **into** rather than replaced, so an `ObservableCollection`
  on a view model keeps its identity. A read-only or fixed-size list (an array) is left alone.
- Anything selected that the source no longer offers is dropped.
- A `null` item gets no chip.

## What a chip shows

`ToString()` unless told otherwise. `ItemDisplayBinding` wins over `DisplayMemberPath`. Prefer a binding
over reflection: the path is compiled, so it survives trimming, and a converter or `StringFormat` comes
free.

`ItemTemplate` replaces the **label only** — the check and the ✕ are never part of it.

```xml
<shiny:ChipGroup ItemsSource="{Binding Teams}" SelectionMode="Multiple">
    <shiny:ChipGroup.ItemTemplate>
        <DataTemplate x:DataType="local:Team">
            <HorizontalStackLayout Spacing="6">
                <Label Text="{Binding Name}" FontSize="14" VerticalTextAlignment="Center" />
                <shiny:PillView Text="{Binding Members}" Type="Info" FontSize="10" />
            </HorizontalStackLayout>
        </DataTemplate>
    </shiny:ChipGroup.ItemTemplate>
</shiny:ChipGroup>
```

## Removing

`AllowRemove` is **off** by default. When on, the item is taken out of the source if the source is a
writable `IList`; otherwise the event is the only signal and the view model owns the removal.
`ChipRemoving` is the cancellable seam — refuse it, do the work, remove the item yourself.

## Layout

Chips wrap by default. `Wrap="False"` keeps them on one line — **on MAUI wrap the group in a horizontal
`ScrollView`**, because it does no scrolling of its own; on Blazor the row scrolls by itself.

## Chrome

Unselected = transparent with an outline. Selected = the theme's secondary container, no outline, with a
leading check (`ShowSelectionCheck`). Set a fill and the ink is computed from it by WCAG luminance, the
same rule `PillView` uses.

## Blazor

Generic — `ChipGroup<TItem>`, `TItem` inferred from `ItemsSource`. Parameters mirror MAUI, with these
differences:

- `DisplaySelector` (`Func<TItem, string>`) replaces `ItemDisplayBinding`/`DisplayMemberPath`.
- `ChipContent` is a `RenderFragment<TItem>`.
- `ChipRemoving` is a predicate (`Func<TItem, bool>`, return false to refuse) rather than a cancellable
  event.
- `ReadOnly` and `Disabled` rather than `IsReadOnly`/`IsEnabled`.
- `CornerRadius` (px, `-1` follows the theme) rather than `ChipCornerRadius`.
- `SelectAsync` / `DeselectAsync` / `ClearSelectionAsync` / `TapAsync` / `RemoveAsync`.
- `@bind-SelectedItem` and `@bind-SelectedItems`.
- `SelectedItem` cannot express "nothing selected" for a **value type** — bind `SelectedItems`, or use
  a nullable type argument (`ChipGroup<int?>`).
- Unmatched attributes land on the container, but `class` and `style` stay the component's — add to the
  class list with `CssClass`.

```razor
<ChipGroup ItemsSource="@ranges" @bind-SelectedItem="range" />

<ChipGroup ItemsSource="@filters"
           SelectionMode="ChipSelectionMode.Multiple"
           @bind-SelectedItems="selectedFilters"
           MaxSelectionCount="3" />

<ChipGroup ItemsSource="@teams" DisplaySelector="@(t => t.Name)" @bind-SelectedItem="team" />

<ChipGroup ItemsSource="@recipients" AllowRemove ChipRemoving="@(r => recipients.Count > 1)" />

@code {
    string? range;
    IReadOnlyList<string> selectedFilters = [];
}
```

## Code Generation Guidance

- Bind the **items**, never an index. An index is a `ButtonGroup`.
- Bind `ItemsSource` to an `ObservableCollection` on MAUI and it is watched live.
- Use `SelectionMode="None"` + `ChipTappedCommand` for action chips; do not fake it with a selection
  mode you then clear.
- Set `MaxSelectionCount` when the backend has a limit — a visible cap beats a validation error after
  submit.
- Leave the colour properties unset so the theme carries through.
- Do **not** reach for this when the user types their own values — that is `TagEntry`.
