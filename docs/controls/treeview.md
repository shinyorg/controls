# TreeView

[← All Shiny Controls](../../README.md)

Hierarchical tree control with lazy-loaded branches, configurable expand/collapse icons, single or multi-selection (checkbox per row), per-item `CanExpand`/`CanSelect` predicates, retry on load failure, optional guide lines, drag/drop reorder, and opt-in virtualization for very large trees. Available on both MAUI and Blazor.

| Initial | Expanded | Multi-level | Lazy loading | Multi-select |
|:---:|:---:|:---:|:---:|:---:|
| ![Initial](../../assets/treeview-initial.png) | ![Expanded](../../assets/treeview-expanded.png) | ![Multi-level](../../assets/treeview-deep.png) | ![Lazy load](../../assets/treeview-loading.png) | ![Multi-select](../../assets/treeview-multiselect.png) |

```xml
<shiny:TreeView x:Name="Tree"
                IndentSize="22"
                ShowGuideLines="True"
                SelectionMode="Single"
                SelectedItem="{Binding Selected, Mode=TwoWay}"
                ItemSelected="OnSelected"
                ItemExpanded="OnExpanded"
                LoadFailed="OnLoadFailed">
    <shiny:TreeView.ItemTemplate>
        <DataTemplate x:DataType="local:FileNode">
            <HorizontalStackLayout Spacing="8">
                <Label Text="{Binding Icon}" />
                <Label Text="{Binding Name}" VerticalTextAlignment="Center" />
            </HorizontalStackLayout>
        </DataTemplate>
    </shiny:TreeView.ItemTemplate>
</shiny:TreeView>
```

```csharp
// Delegates aren't bindable from XAML — wire in code-behind
Tree.ItemsSource         = roots;
Tree.ChildrenSelector    = item => (item is FileNode f && !f.LazyLoad) ? f.Children : null;
Tree.ChildrenLoader      = LoadRemoteChildrenAsync;            // covers lazy branches
Tree.HasChildrenSelector = item => item is FileNode { IsFolder: true };
Tree.CanSelectSelector   = item => item is FileNode f && !f.IsLocked;
```

**Key Properties:**

| Property | Type | Description |
|---|---|---|
| ItemsSource | IEnumerable | Root items (ignored when `RootLoader` is set) |
| RootLoader | `Func<Task<IEnumerable<object>>>` | Async loader for roots; shows a centered spinner |
| ChildrenSelector | `Func<object, IEnumerable<object>?>` | Sync children getter (return `null` to defer to loader) |
| ChildrenLoader | `Func<object, Task<IEnumerable<object>>>` | Async children loader; cached on first expand |
| HasChildrenSelector | `Func<object, bool>` | Render chevron only when true |
| CanExpandSelector | `Func<object, bool>` | Gate expand gesture (dimmed chevron when false) |
| CanSelectSelector | `Func<object, bool>` | Gate selection per item |
| SelectionMode | TreeSelectionMode | `None` / `Single` / `Multiple` (switching modes clears the current selection) |
| ShowSelectionCheckBoxes | bool | Checkbox on every row while `SelectionMode` is `Multiple` (default true) |
| CheckBoxColor | Color? | Checkbox tint (MAUI); Blazor uses the `--shiny-color-primary` token |
| SelectedItem | object? | Two-way (Single mode) |
| SelectedItems | IList\<object\>? | Two-way (Multiple mode) |
| ExpandedIcon / CollapsedIcon / RetryIcon | ImageSource? | Fall back to ▼ / ▶ / ↻ glyphs |
| IndentSize | double | Pixels of indent per depth level (default 20) |
| ShowGuideLines | bool | Vertical connector lines between parent and children |
| EnableDragDrop | bool | Drag/drop with above/below/into drop positions and visual drop indicators; event-only, never mutates data |
| IsVirtualized (MAUI) / Virtualize (Blazor) | bool | Build only the rows in and around the viewport. See [Virtualization](#virtualization) |
| RowHeight | double (MAUI) / float (Blazor) | Fixed row height while virtualized (MAUI default 40, Blazor 32) |
| OverscanCount (Blazor) | int | Rows rendered above and below the viewport while virtualized (default 6) |

**Events + Commands (MAUI):** `ItemSelected` / `ItemExpanded` / `ItemCollapsed` / `LoadFailed` / `ItemDropped` each have a matching `*Command` bindable property.

`ItemDropped` reports `Source`, `Target`, and `Position` (`Above` / `Below` reorder among siblings, `Into` drops into a folder) — your handler moves the data, then rebinds `ItemsSource` (MAUI) or calls `ReloadAsync()` (Blazor, which preserves expansion/selection state). Blazor drag/drop runs on native HTML5 drag events via a small JS module (required for Safari/Firefox `dataTransfer` support); MAUI uses platform drag gestures with a pan-gesture fallback on Mac Catalyst, AppKit, and GTK4 where those are broken or missing.

**Multi-select:** `SelectionMode="Multiple"` puts a checkbox on every row. The whole row is the hit target — tapping the row or its checkbox toggles it — and rows failing `CanSelectSelector` show a disabled box. Set `ShowSelectionCheckBoxes="False"` for the older highlight-only look.

**Public methods:** `ExpandAll(maxDepth)`, `ExpandAllAsync(maxDepth)`, `CollapseAll`, `Expand(item)`, `Collapse(item)`, `SelectAll()`, `DeselectAll()`, `SetBranchSelected(item, selected)`, `Refresh(item)`, `ReloadAsync`, `FindNode(item)` — Blazor mirrors these as `ExpandAsync` / `CollapseAsync` / `ExpandAllAsync` / `CollapseAll` / `SelectAllAsync` / `DeselectAllAsync` / `SetBranchSelectedAsync` / `RefreshAsync` / `ReloadAsync` / `FindNode`.

`ExpandAll` materializes everything `ChildrenSelector` can supply — it only leaves branches that need `ChildrenLoader`, which `ExpandAllAsync` awaits. Both stop at `maxDepth` (default 32) so a self-referencing or endlessly generated hierarchy can't expand forever. `SelectAll` / `SetBranchSelected` cover collapsed branches too, but only nodes that have been loaded — call `ExpandAllAsync()` first to check a lazy tree in full.

### Virtualization

By default every visible row (each root, plus the children of every expanded node) is built. For trees with thousands of visible rows, turn on virtualization and only the rows in and around the viewport exist; the rest are built as they scroll in.

```xml
<!-- MAUI: the tree fills a * row, so it has a bounded height and owns the scroll -->
<Grid RowDefinitions="Auto,*">
    <shiny:TreeView Grid.Row="1" IsVirtualized="True" RowHeight="36" />
</Grid>
```

```razor
@* Blazor: the parent gives the tree its height; the tree is the scroller *@
<div style="height: 480px">
    <TreeView TItem="FileNode" Virtualize="true" RowHeight="30" ItemsSource="roots" ... />
</div>
```

- **Fixed row height.** Every row is `RowHeight` tall. On MAUI `RowSpacing` is ignored; on Blazor long labels are cut off with an ellipsis.
- **Bounded height.** The tree becomes its own scroller, so give it a real height: a `*` grid row on MAUI, a sized parent (`height:100%` / `flex:1` with `min-height:0`) on Blazor. Inside a `ScrollView`, a `VerticalStackLayout` or a scrolling page it grows to its full height and builds every row anyway.
- **MAUI:** expanding or collapsing moves the rows already built instead of rebuilding them all. `RealizedRowCount` reports how many row views exist. Automation ids keep using the row's index in the full visible order, so they still identify the same rows.
- **Blazor:** rows render as a flat list through `<Virtualize>`; the hierarchy is announced with `aria-level`, `aria-setsize` and `aria-posinset`. Keyboard focus moving to a row off screen (arrows, Home, End) scrolls it into view.
- Not yet supported while virtualized: auto-scrolling while dragging near an edge, and scrolling to an item from code.

### Automation ids (MAUI)

Set `AutomationId` on the `TreeView` and each row is named by its index in the visible (flattened)
order, renamed on every expand/collapse, for UI tests:

| Part | Id |
|---|---|
| Node row (the selection tap target) | `<id>_Node_<index>` |
| Expand/collapse chevron | `<id>_Expand_<index>` |
| Multi-select checkbox | `<id>_CheckBox_<index>` (`ShinyTreeNodeCheckBox` when the tree has no id) |
| Root loading indicator / load-error label | `<id>_Loading` / `<id>_Error` |
