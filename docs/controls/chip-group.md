# ChipGroup

[← All Shiny Controls](../../README.md)

A bound set of chips: one per item in `ItemsSource`, selectable singly or in any number, reported back through `SelectedItem` and `SelectedItems`. It ships in the **core** package on both hosts.

```bash
dotnet add package Shiny.Maui.Controls             # MAUI
dotnet add package Shiny.Blazor.Controls           # Blazor
```

```xml
<shiny:ChipGroup ItemsSource="{Binding Categories}"
                 SelectedItem="{Binding Category}"
                 DisplayMemberPath="Name" />
```

```razor
<ChipGroup ItemsSource="@categories" @bind-SelectedItem="category" DisplaySelector="@(c => c.Name)" />
```

## Which control is this?

| | Where the options come from | What the selection is |
|---|---|---|
| [`ButtonGroup`](button-group.md) | written out in markup | an **index** |
| **`ChipGroup`** | a bound collection | the **items themselves** |
| [`TagEntry`](tag-entry.md) | the user types them | the strings they typed |

A button group's `SelectedIndex` is the right shape for `Day / Week / Month` and the wrong one for a list that arrives from a service — a view model would have to translate indexes back into the objects it already has. A chip group also **wraps**, so a dozen filters flow onto as many lines as they need instead of becoming one unreadably long segmented control.

## Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `ItemsSource` | `IEnumerable` / `IEnumerable<TItem>` | `null` | What the chips stand for |
| `SelectionMode` | `ChipSelectionMode` | `Single` | `None`, `Single` or `Multiple` |
| `SelectedItem` | `object` / `TItem?` | `null` | The selected item (TwoWay / `@bind-SelectedItem`) |
| `SelectedItems` | `IList` / `IReadOnlyList<TItem>` | empty | Everything selected (TwoWay / `@bind-SelectedItems`) |
| `AllowDeselect` | `bool` | `false` | A second tap clears a single-selection group |
| `MaxSelectionCount` | `int` | `0` | Cap in multiple mode, or 0 for no limit |
| `ShowSelectionCheck` | `bool` | `true` | A selected chip draws a leading check |
| `AllowRemove` | `bool` | `false` | Every chip carries a ✕ |
| `IsReadOnly` / `ReadOnly` | `bool` | `false` | Chips stay crisp, selection and removal are blocked |
| `Wrap` | `bool` | `true` | Chips flow onto as many lines as they need |
| `ItemTemplate` / `ChipContent` | `DataTemplate` / `RenderFragment<TItem>` | `null` | Replaces a chip's label |
| `ChipBackgroundColor`, `ChipTextColor`, `SelectedChipBackgroundColor`, `SelectedChipTextColor`, `ChipBorderColor` | | unset | Chip chrome; unset follows the theme |
| `ChipCornerRadius` / `CornerRadius` | `double` | unset | Chip corner radius |

**MAUI** adds `ItemDisplayBinding` (a `BindingBase`) and `DisplayMemberPath` (a property name), `HorizontalSpacing`/`VerticalSpacing`, `DisabledOpacity`, the events `SelectionChanged`, `ChipTapped`, `ChipRemoving` (cancellable) and `ChipRemoved`, plus `SelectionChangedCommand`, `ChipTappedCommand` and `ChipRemovedCommand`. Methods: `Select(item)`, `Deselect(item)`, `ClearSelection()`, `IsSelected(item)`, and `Items` for what it currently has chips for.

**Blazor** is generic — `ChipGroup<TItem>`, with `TItem` inferred from `ItemsSource`. It adds `DisplaySelector` (`Func<TItem, string>`), `ChipRemoving` as a predicate (`Func<TItem, bool>` — the cancellable-event equivalent), `Disabled`, `CssClass`, and `SelectAsync`/`DeselectAsync`/`ClearSelectionAsync`/`TapAsync`/`RemoveAsync`.

## Selection

`SelectionMode` is `Single` by default, which is the opposite of `ButtonGroup` — selection is what a chip group is *for*, while a button group is a row of actions that only becomes a picker when asked.

```xml
<!-- choice chips -->
<shiny:ChipGroup ItemsSource="{Binding Ranges}" SelectedItem="{Binding Range}" />

<!-- filter chips -->
<shiny:ChipGroup ItemsSource="{Binding Filters}"
                 SelectionMode="Multiple"
                 SelectedItems="{Binding SelectedFilters}"
                 MaxSelectionCount="3" />

<!-- action chips: no state at all, every tap is reported -->
<shiny:ChipGroup ItemsSource="{Binding Actions}"
                 SelectionMode="None"
                 ChipTappedCommand="{Binding RunCommand}" />
```

Re-tapping the selected chip in `Single` does nothing unless `AllowDeselect` is on — a picker that can be emptied by tapping its own answer again is a picker with no answer. In `Multiple` a second tap always toggles.

At `MaxSelectionCount` a tap on an unselected chip does **nothing**. The oldest selection is not dropped: silently unpicking something the user chose is worse than refusing the new one.

`SelectedItem` and `SelectedItems` are both kept accurate whatever the mode, so a view model can read either. On MAUI the bound `SelectedItems` list is written **into** rather than replaced, so an `ObservableCollection` on a view model stays the same instance and whatever is watching it goes on working. A fixed-size or read-only list (an array) is left alone — there is nowhere to write, and throwing over it would turn a reasonable binding into a crash.

> On Blazor, `SelectedItem` cannot express "nothing selected" for a **value type** — `default(TItem)` is a perfectly good item. Bind `SelectedItems`, or make the type nullable (`ChipGroup<int?>`).

Anything selected that the source no longer offers is dropped: a selection with no chip to show for it is one the user cannot see and cannot undo.

## What a chip shows

Unset, a chip shows the item's `ToString()`. For anything richer:

```xml
<shiny:ChipGroup ItemsSource="{Binding Teams}" DisplayMemberPath="Name" />
<shiny:ChipGroup ItemsSource="{Binding Teams}" ItemDisplayBinding="{Binding Members, StringFormat='{0} people'}" />
```

```razor
<ChipGroup ItemsSource="@teams" DisplaySelector="@(t => t.Name)" />
```

`ItemDisplayBinding` is a binding rather than a property name looked up by reflection: the path is compiled, so it survives trimming, and a converter or a `StringFormat` comes along for free. It wins over `DisplayMemberPath` when both are set.

`ItemTemplate` (MAUI) and `ChipContent` (Blazor) replace a chip's **label**. The selection check and the remove affordance are never part of the template: every chip keeps the same state and the same way out, however it is drawn.

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

`AllowRemove` puts a ✕ on every chip. It is **off** by default — most chip groups are a fixed set of filters, and a ✕ on every one of them invites the user to destroy the picker.

The item is taken out of the source when the source is a list that can be written to. When it is not — a LINQ projection, an array — the event is the only signal and the view model owns the removal, which is also how a chip that needs a server round-trip first is handled:

```xml
<shiny:ChipGroup ItemsSource="{Binding Recipients}" AllowRemove="True" ChipRemoving="OnChipRemoving" />
```
```csharp
void OnChipRemoving(object sender, ChipRemovingEventArgs e) => e.Cancel = !this.CanRemove(e.Item);
```

```razor
<ChipGroup ItemsSource="@recipients" AllowRemove ChipRemoving="@(r => recipients.Count > 1)" />
```

## Layout

Chips wrap by default. `Wrap="False"` keeps them on one line — on Blazor the row then scrolls sideways on its own; **on MAUI put the group in a horizontal `ScrollView`**, or the overflow is simply off the edge:

```xml
<ScrollView Orientation="Horizontal" HorizontalScrollBarVisibility="Never">
    <shiny:ChipGroup ItemsSource="{Binding Languages}" SelectionMode="Multiple" Wrap="False" />
</ScrollView>
```

## Chrome

An unselected chip is transparent with an outline, so the page behind shows through and a group over a card does not paint its own slab. A selected chip is a filled surface — the theme's secondary container — and draws no outline, because a hairline of the unselected colour around the fill reads as a chip that is both states at once.

Set `ChipBackgroundColor` or `SelectedChipBackgroundColor` and the chip's ink is computed from it by WCAG luminance — the same rule [`PillView`](pillview.md) uses — so a brand colour stays readable.

`ShowSelectionCheck` is on by default: fill alone carries selection only for someone who can compare a chip with the ones beside it.

## Read-only and disabled

`IsReadOnly`/`ReadOnly` keeps the chips crisp and the selection visible but blocks changing it, and takes the remove affordance away. Disabling the group also dims it. Read-only is "these are the values", disabled is "not right now".

## Code generation guidance

- Bind `ItemsSource` to an `ObservableCollection` on MAUI and it is watched live; a plain `List` works too, the control just rebuilds the chips itself.
- Bind `SelectedItem` for a single choice and `SelectedItems` for a multiple one — not an index. If an index is genuinely what you want, that is a [`ButtonGroup`](button-group.md).
- Use `SelectionMode="None"` plus `ChipTappedCommand` for a row of action chips; do not fake it with a selection mode you then clear.
- Set `MaxSelectionCount` when the backend has a limit — the cap is visible behaviour rather than a validation error after submit.
- Leave the colour properties unset so the theme carries through.
