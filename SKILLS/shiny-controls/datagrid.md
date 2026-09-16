# DataGrid

A feature-rich data grid for **both MAUI and Blazor**, modeled on MudBlazor's `MudDataGrid`. Blazor
renders a semantic HTML `<table>` via a generic `DataGrid<TItem>`; MAUI is a **pure cross-platform
composite** (a `Grid` header over a virtualized `CollectionView` — no native handlers), so it looks and
behaves the same on iOS/Android/Windows/Mac.

Feature surface (both hosts): typed columns (`PropertyColumn` + `TemplateColumn`), sorting (single +
multi), column **filtering** (menu / row / toolbar quick-search), **multi-level grouping** with
expandable groups, **summary (total) rows** under the grid and inside every group,
single/multi **selection** with checkboxes, inline **editing**
(cell + form), **detail ("breakdown") rows**, a **tree/hierarchy mode** (`TreeDataGrid`) with lazy child
loading, **paging**, **virtualization**, column **resize / reorder**, **frozen columns** and a
frozen (sticky) header, loading + empty states, a `ServerData` delegate, **highlighting** of rows/columns/cells, and density/striped/bordered/hover styling. Colors follow the
theme tokens (`var(--shiny-color-*)` on Blazor, `ShinyThemeKeys.Color.*` on MAUI).

## Blazor

`@typeparam TItem`; columns are child components inside `<Columns>`. `TItem` cascades to columns, so
`PropertyColumn` only needs `Property`.

```razor
<DataGrid TItem="Person" Items="people"
          SelectionMode="DataGridSelectionMode.Multiple"
          SortMode="DataGridSortMode.Multiple"
          FilterMode="DataGridFilterMode.Menu"
          Groupable="true"
          EditMode="DataGridEditMode.Form"
          Dense="true" Striped="true" Hover="true" Bordered="true"
          FixedHeader="true" Height="420px"
          FrozenColumns="1" FrozenEndColumns="1"
          ColumnResizeMode="DataGridColumnResizeMode.Column"
          MinColumnWidth="60" MaxColumnWidth="420"
          DragDropColumnReordering="true"
          CommittedItemChanges="OnSaved">
    <Columns>
        <PropertyColumn Property="x => x.FirstName" Title="First"
                        Width="25%" MinWidth="80px" MaxWidth="260px" />
        <PropertyColumn Property="x => x.Age" StringFormat="N0" />
        <PropertyColumn Property="x => x.Salary" DisplayAs="DataGridColumnFormat.Currency" Decimals="0">
            <FooterTemplate>Total: @people.Sum(p => p.Salary).ToString("C0")</FooterTemplate>
        </PropertyColumn>
        <TemplateColumn Title="Status" Sortable="false" Filterable="false" Resizable="false">
            <CellTemplate>
                <Pill Text="@(context.Item.Active ? "Active" : "Inactive")"
                      Type="@(context.Item.Active ? PillType.Success : PillType.Caution)" />
            </CellTemplate>
            <EditTemplate>
                <input @bind="context.Item.FirstName" />
            </EditTemplate>
        </TemplateColumn>
    </Columns>
    <PagerContent>
        <DataGridPager TItem="Person" />
    </PagerContent>
</DataGrid>
```

- **Columns**: `PropertyColumn<TItem,TProperty>` (`Property="x => x.Name"`, `StringFormat`, derives Title) and
  `TemplateColumn<TItem>` (`CellTemplate`/`EditTemplate`/`HeaderTemplate`/`FooterTemplate` with
  `context.Item`). Per-column flags: `Sortable`, `Filterable`, `Groupable`, `Editable`, `Hidden`,
  `Width` (any CSS length, including `"25%"`), `MinWidth`, `MaxWidth`, `Resizable`,
  `Frozen` (`DataGridFrozen.Start`/`End`; `StickyLeft`/`StickyRight` are legacy aliases), `Aggregate`.
- **Grid params**: `Items`, `ServerData` (`Func<GridState, Task<GridData<TItem>>>`), `SelectionMode`,
  `SelectedItem(s)`, `SortMode`, `FilterMode`, `QuickFilter`, `Groupable`, `Virtualize`, `EditMode`,
  `EditTrigger`, `ReadOnly`, `RowsPerPage`, `FixedHeader`, `Height`, `Dense`, `Striped`, `Bordered`,
  `Hover`, `Outlined`, `Loading`, `RowClick`, `StartedEditingItem`/`CommittedItemChanges`/
  `CanceledEditingItem`, `ColumnResizeMode`, `MinColumnWidth`/`MaxColumnWidth`, `ColumnResized`,
  `DragDropColumnReordering`, `ColumnReordered`, `ToolbarContent`,
  `NoRecordsText`/`NoRecordsContent`, `LoadingContent`.
- **Paging**: put `<DataGridPager TItem="..." />` in `<PagerContent>`.
- **Detail rows**: `<RowDetailTemplate>` (context is the item) adds a caret column at the leading edge.
  Grid params: `ExpandMode`, `ExpandOnRowClick`, `IsRowExpandable`, `ExpandedItems`(+`Changed`),
  `RowExpanded`/`RowCollapsed`. Methods: `ExpandRowAsync`/`CollapseRowAsync`/`ToggleExpandAsync`/
  `ExpandAllAsync`/`CollapseAllAsync`/`IsRowExpanded`/`InvalidateChildren`/`InvalidateRowDetail`.
- **Async detail**: `RowDetailLoader` (`Func<TItem, Task>`) + optional `<RowDetailLoadingTemplate>`,
  `RowDetailLoadFailed`. Read-only `IsBusy` (+ `IsBusyChanged`) and `IsRowBusy(item)`.
- **Tree mode**: `ChildrenSelector` (+ `ChildrenLoader`, `HasChildrenSelector`, `TreeIndentSize`,
  `ChildrenLoadFailed`) on `DataGrid` — or use `TreeDataGrid`, the same component under a clearer name.

```razor
<DataGrid TItem="Order" Items="orders" RowDetailLoader="LoadLinesAsync" IsBusyChanged="b => busy = b">
    <Columns>…</Columns>
    <RowDetailTemplate>
        @foreach (var line in lines[context.Id]) { <div>@line.Sku</div> }
    </RowDetailTemplate>
    <RowDetailLoadingTemplate><span class="shiny-dg-busy"></span> Loading…</RowDetailLoadingTemplate>
</DataGrid>
@code {
    Dictionary<int, List<Line>> lines = new();
    async Task LoadLinesAsync(Order o) => lines[o.Id] = await api.GetLinesAsync(o.Id);
}
```

```razor
<TreeDataGrid TItem="CostNode" Items="accounts"
              ChildrenSelector="n => n.Lazy ? null : n.Children"
              ChildrenLoader="LoadChildrenAsync"
              HasChildrenSelector="n => n.Lazy || n.Children.Count > 0"
              TreeIndentSize="18">
    <Columns>
        <PropertyColumn Property="x => x.Name" Title="Account" />
        <PropertyColumn Property="x => x.Budget" Format="C0" />
    </Columns>
</TreeDataGrid>
```

## MAUI

`shiny:DataGrid` with `shiny:DataGridColumn` / `shiny:DataGridTemplateColumn` children (items are
`object`; no generics — XAML-friendly). Bind a column by `PropertyName`.

```xml
<shiny:DataGrid ItemsSource="{Binding People}"
                SelectionMode="Multiple"
                SortMode="Multiple"
                FilterMode="Menu"
                Groupable="True"
                PageSize="20"
                EditMode="Form"
                AllowColumnResize="True"
                AllowColumnReorder="True"
                DragDropColumnReordering="True"
                HorizontalScroll="True"
                DefaultColumnWidth="140"
                MinColumnWidth="70"
                MaxColumnWidth="400"
                FrozenColumns="1"
                Striped="True" Bordered="True">
    <shiny:DataGridColumn Title="First" PropertyName="FirstName" Width="*"
                          MinWidth="90" MaxWidth="260" />
    <shiny:DataGridColumn Title="Age" PropertyName="Age" Width="Auto" />
    <shiny:DataGridColumn Title="Department" PropertyName="Department" WidthPercent="30" />
    <shiny:DataGridColumn Title="Salary" PropertyName="Salary"
                          DisplayAs="Currency" Decimals="0" Width="*">
        <shiny:DataGridColumn.Aggregate>
            <shiny:DataGridAggregateDefinition Type="Sum" Format="C0" />
        </shiny:DataGridColumn.Aggregate>
    </shiny:DataGridColumn>
    <shiny:DataGridTemplateColumn Title="Status" Width="110" Editable="False"
                                  Resizable="False" Frozen="End">
        <shiny:DataGridTemplateColumn.CellTemplate>
            <DataTemplate><shiny:PillView Text="{Binding StatusText}" /></DataTemplate>
        </shiny:DataGridTemplateColumn.CellTemplate>
    </shiny:DataGridTemplateColumn>
</shiny:DataGrid>
```

- **Columns**: `DataGridColumn` (`PropertyName`, `Width` as `GridLength` star/auto/abs, `StringFormat`,
  the formatting properties below,
  `CellTemplate`/`HeaderTemplate`/`EditTemplate`/`FooterTemplate`, `Sortable`/`Filterable`/`Groupable`/
  `Editable`/`Resizable`/`IsVisible`, `WidthPercent`, `MinWidth`/`MaxWidth`, `Frozen`, `Aggregate`).
  `DataGridTemplateColumn` for custom-only cells.
  Cell templates bind to the data item directly (e.g. `{Binding StatusText}`).
- **Grid params**: `ItemsSource`, `ServerData`, `SelectionMode`, `SelectedItem`/`SelectedItems`,
  `SortMode`, `FilterMode`, `Groupable`, `PageSize` (0 = no paging), `EditMode`, `EditTrigger`,
  `ReadOnly`, `AllowColumnResize`, `AllowColumnReorder`, `DragDropColumnReordering`, `ColumnReordered`,
  `HorizontalScroll`, `DefaultColumnWidth`,
  `MinColumnWidth`/`MaxColumnWidth`, `FrozenColumns`/`FrozenEndColumns`, `Dense`, `Striped`, `Bordered`,
  `ShowColumnHeaders`, `IsLoading`, `EmptyText`, `RowHeight`, `SelectionChanged`/`SelectionChangedCommand`,
  `StartedEditingItem`/`CommittedItemChanges`/`CanceledEditingItem` events.
- **Detail rows**: `RowDetailTemplate` (a `DataTemplate` whose BindingContext is the row's item) adds a
  caret column at the leading edge. Also `ExpandMode`, `ExpandOnRowTap`, `IsRowExpandable`,
  `ExpandedItems`, `RowExpanded`/`RowCollapsed` events, and `ExpandRow`/`CollapseRow`/`ToggleRow`/
  `ExpandAll`/`CollapseAll`/`IsRowExpanded`/`InvalidateChildren`/`InvalidateRowDetail`.
- **Async detail**: `RowDetailLoader` (`Func<object, Task>`, a BindableProperty) + optional
  `RowDetailLoadingTemplate`, `RowDetailLoadFailed`. Read-only bindable `IsBusy` (+ `IsBusyChanged`
  event) and `IsRowBusy(item)`.
- **Tree mode**: `ChildrenSelector` (+ `ChildrenLoader`, `HasChildrenSelector`, `TreeIndentSize`,
  `ChildrenLoadFailed`) — or use `shiny:TreeDataGrid`, the same control under a clearer name. All three
  selectors are `BindableProperty`s, so a XAML page can bind them straight to a view model.

```xml
<shiny:TreeDataGrid ItemsSource="{Binding Accounts}"
                    ChildrenSelector="{Binding ChildrenSelector}"
                    ChildrenLoader="{Binding ChildrenLoader}"
                    HasChildrenSelector="{Binding HasChildrenSelector}"
                    TreeIndentSize="18">
    <shiny:DataGridColumn Title="Account" PropertyName="Name" Width="2*" />
    <shiny:DataGridColumn Title="Budget" PropertyName="Budget" StringFormat="{}{0:C0}" Width="1.2*" />
</shiny:TreeDataGrid>
```

## Grouping & summary rows (both hosts)

`GroupBy` is a list of columns, **outermost first**, and grouping is on whenever it has an entry.
`Groupable` is a *separate* switch that only adds the ⊞ button so the user can add/remove a level - it
never gates a grouping you declared. Paging is skipped while grouped.

`SummaryRows` holds any number of rows; each cell points at a column and either **aggregates** it
(`Sum`/`Count`/`Average`/`Min`/`Max`/`Custom`) or **fills the slot with a label** (`Text="Total"`).
Columns with no cell stay blank - that is what puts the word in one column and the number in the next.
The same rows render under the grid **and** inside every group (`Scope="Grid"`/`"Group"` narrows one),
and `GroupSummaryPlacement` decides whether a group's rows sit under its rows (`Footer`, the default -
collapses with them), under its title (`Header` - stays visible when collapsed), `Both`, or `None`.

An aggregate with no `StringFormat` wears its column's own formatting (a currency column totals as
currency); a `Count` is always a plain number. The older per-column `Aggregate`/`FooterTemplate` still
works and yields the one footer row it always did.

```razor
@* Blazor *@
<DataGrid TItem="Sale" Items="sales" @bind-GroupBy="groupBy" Groupable="true"
          GroupSummaryPlacement="DataGridGroupSummaryPlacement.Footer"
          GroupsInitiallyExpanded="true" GroupSortDirection="DataGridSortDirection.Ascending">
    <Columns>
        <PropertyColumn Property="x => x.Department" />
        <PropertyColumn Property="x => x.Rep" />
        <PropertyColumn Property="x => x.Revenue" DisplayAs="DataGridColumnFormat.Currency" Decimals="0" />
    </Columns>
    <SummaryRows>
        <SummaryRow>
            <SummaryCell Column="Rep" Text="Total" Alignment="DataGridCellAlignment.End" />
            <SummaryCell Column="Revenue" Aggregate="DataGridAggregateType.Sum" />
        </SummaryRow>
        <SummaryRow Scope="DataGridSummaryScope.Grid">
            <SummaryCell Column="Rep" Text="Average" Bold="false" />
            <SummaryCell Column="Revenue" Aggregate="DataGridAggregateType.Average" Bold="false" />
        </SummaryRow>
    </SummaryRows>
</DataGrid>
@code { IReadOnlyList<string> groupBy = ["Department", "Region"]; }
```

```xml
<!-- MAUI - GroupBy takes a binding or inline <x:String> children -->
<shiny:DataGrid ItemsSource="{Binding Sales}" Groupable="True"
                GroupBy="{Binding GroupColumns}"
                GroupSummaryPlacement="Footer">
    <shiny:DataGrid.SummaryRows>
        <shiny:DataGridSummaryRow>
            <shiny:DataGridSummaryCell Column="Rep" Text="Total" Alignment="End" />
            <shiny:DataGridSummaryCell Column="Revenue" Aggregate="Sum" />
        </shiny:DataGridSummaryRow>
    </shiny:DataGrid.SummaryRows>

    <shiny:DataGridColumn Title="Department" PropertyName="Department" Width="*" />
    <shiny:DataGridColumn Title="Rep" PropertyName="Rep" Width="*" />
    <shiny:DataGridColumn Title="Revenue" PropertyName="Revenue"
                          DisplayAs="Currency" Decimals="0" Width="*" />
</shiny:DataGrid>
```

- **Summary cell**: `Column` (property name or Title), `Text`, `Aggregate`, `StringFormat`, `Alignment`
  (`Auto` follows the column), `Bold`, `CustomAggregate`, and `CellTemplate` (MAUI - context is a
  `DataGridSummaryContext`) / child content (Blazor - `SummaryContext<T>`). `Text` wins over `Aggregate`.
- **Group API**: `ExpandAllGroups()`/`CollapseAllGroups()`, MAUI `Groups` + `ToggleGroup(header)` and
  `ClearGrouping()`, Blazor `ClearGroupingAsync()`. Collapse state is keyed on the group *path*, so the
  same key under two parents stays independent.
- **Group header**: `GroupHeaderTemplate` - MAUI context is `DataGridGroupHeader`, Blazor
  `DataGridGroupInfo<T>` (`Key`, `KeyText`, `Title`, `Count`, `Level`, `Items`, `IsExpanded`).

## Column formatting (both hosts)

Reach for these **before** a `CellTemplate`/`DataGridTemplateColumn` - a template gives up sorting,
filtering and inline editing, and these do not.

| Property | What it does |
| --- | --- |
| `DisplayAs` | Preset: `Currency`, `Percent`, `Number`, `Date`, `Time`, `DateTime`, `FileSize`, `Boolean`, `Enum`, `Text`, `None` |
| `Decimals` | Places for `Number`/`Currency`/`Percent`/`FileSize`; `null` = culture default |
| `StringFormat` | Raw .NET format string; **wins over** `DisplayAs`. Blazor's `Format` is a still-working alias |
| `NullText` | Shown for a null or empty value (prefix/suffix are not applied to it) |
| `Prefix` / `Suffix` | Wrap a real value, e.g. `Suffix=" kg"` |
| `TrueText` / `FalseText` | Override the `Boolean` preset's glyphs |
| `Culture` | `CultureInfo` for this column; `null` = `CurrentCulture` |
| `TextFormatter` | `Func<value, string?>` - full control without a template. Prefix/suffix/`NullText` still apply |
| `Alignment` / `HeaderAlignment` | `Auto` (quantities right, rest left), `Start`, `Center`, `End`. Header follows `Alignment` by default |
| `Wrap` / `MaxLines` | Wrap instead of truncating; cap the height |
| `CellStyle` | `Func<item, DataGridCellStyle?>` - per-cell colour/weight/fill/border from the row |
| `Highlight` | `DataGridCellStyle` - the same paint applied to the **whole column**. See Highlighting below |

Rules that matter:

- **`Percent` multiplies by 100** (that is what .NET's `"P"` does): `0.15` renders `15%`.
- **`Enum`** uses the member's `[Description]`, else its name split on PascalCase (`InProgress` -> `In Progress`).
- **One code path.** The cell, the quick-filter search index and the group header all format through the
  same method, so they cannot disagree about what a value reads as.
- **`CellStyle` is evaluated when a row binds**, not when a property on the item changes.
- On **MAUI** `TextFormatter`, `CellStyle` and `Culture` are `BindableProperty`s, so `{Binding}` from a
  view model works; the grid pushes its `BindingContext` down to its columns for exactly that reason.
- On **Blazor** `DataGridCellStyle` colours are CSS strings, so a theme token
  (`"var(--shiny-color-error)"`) is as valid as a hex; `CssClass` there is **not** scoped to the grid's
  isolated stylesheet - declare it in app CSS.

```razor
@* Blazor *@
<PropertyColumn Property="x => x.Balance" DisplayAs="DataGridColumnFormat.Currency" Decimals="0"
                NullText="—"
                CellStyle="@(a => a.Balance < 0
                    ? new DataGridCellStyle { TextColor = "var(--shiny-color-error)", Bold = true }
                    : null)" />
<PropertyColumn Property="x => x.State" DisplayAs="DataGridColumnFormat.Enum" />
<PropertyColumn Property="x => x.Notes" Wrap="true" MaxLines="2" />
```

```xml
<!-- MAUI -->
<shiny:DataGridColumn Title="Balance" PropertyName="Balance"
                      DisplayAs="Currency" Decimals="0" NullText="—"
                      CellStyle="{Binding BalanceStyle}" />
<shiny:DataGridColumn Title="On" PropertyName="Active" DisplayAs="Boolean" Alignment="Center" />
<shiny:DataGridColumn Title="Notes" PropertyName="Notes" Wrap="True" MaxLines="2" />
```

## Highlighting rows, columns & cells (both hosts)

A **fill** that never obscures the text plus a **stroke** that traces the region, applied to whatever
you name. One type does all of it: `DataGridHighlight` *is* a `DataGridCellStyle` with targeting
members bolted on, so the paint properties are the same wherever you set them.

**The scope is derived, not declared** - from which targeting members you set:

| `Item`/`RowPredicate` | `Column` | Scope |
| --- | --- | --- |
| — | — | the whole grid |
| set | — | that row (or every row the predicate matches) |
| — | set | that column |
| set | set | the one cell where they cross |

```razor
@* Blazor *@
<DataGrid TItem="Person" Items="people" Highlights="rules"
          RowHighlight="@(p => p.Overdue ? new DataGridCellStyle { Fill = "var(--shiny-color-error)" } : null)">
    <Columns>
        <PropertyColumn Property="x => x.Salary"
                        Highlight="@(new DataGridCellStyle { Fill = "#fff3cd" })" />
    </Columns>
</DataGrid>
@code {
    DataGridHighlight<Person>[] rules =
    [
        new() { Column = nameof(Person.Salary), Fill = "gold" },
        new() { RowPredicate = p => p.Overdue, BorderColor = "crimson", BorderStyle = DataGridBorderStyle.Dashed },
        new() { Item = pinned, Column = "Notes", BorderColor = "teal", BorderStyle = DataGridBorderStyle.Solid }
    ];
}
```

```xml
<!-- MAUI -->
<shiny:DataGrid ItemsSource="{Binding People}"
                Highlights="{Binding Highlights}"
                RowHighlight="{Binding InactiveRowStyle}">
    <shiny:DataGridColumn Title="Tenure" PropertyName="YearsOfService">
        <shiny:DataGridColumn.Highlight>
            <shiny:DataGridCellStyle Fill="LightSkyBlue" FillOpacity="0.35" />
        </shiny:DataGridColumn.Highlight>
    </shiny:DataGridColumn>
</shiny:DataGrid>
```

### The paint

| Property | What it does |
| --- | --- |
| `Fill` | A wash laid **behind the text** and over the row's stripe/selection. Blazor takes a CSS colour (a theme token works); MAUI takes a `Color` |
| `FillOpacity` | 0-1, default **0.25**. `1` is a solid fill. This is what keeps dark text on a strong fill readable |
| `BorderColor` | Stroke colour. Needs a `BorderStyle` to draw |
| `BorderStyle` | `None` (default), `Solid`, `Dashed`, `Dotted`, `Double` |
| `BorderWidth` | Blazor: any CSS length, default `"2px"`. MAUI: a `double`, default `2` |
| `BorderEdges` | `Top`/`Right`/`Bottom`/`Left`/`All` flags. **Leave unset** and the grid derives them - see below |
| `TextColor`, `Bold`/`FontAttributes` | The older per-cell members, unchanged |
| `BackgroundColor` | Replaces the cell background outright, hiding the stripe/selection. `Fill` is what you usually want |

Rules that matter:

- **The fill is painted behind the text, always.** On Blazor it is emitted as a `background-image`
  gradient rather than a `background-color`, so it layers *over* the row's stripe/selection tint and
  over a frozen cell's opaque pane background instead of replacing either. On MAUI it is the cell's own
  background colour, left translucent so the row composites through it.
- **The stroke traces the perimeter of the region, not each cell in it.** A highlighted row draws a
  line above and below every cell but only one leading and one trailing edge; a highlighted column
  draws both flanks on every cell but caps only the first and last row. A cell is boxed on all four
  sides. Set `BorderEdges` explicitly to override that.
- **A block is the page, or one group when the grid is grouped** - so a column highlight is capped at a
  group header rather than drawn straight through it.
- **Precedence is widest-to-narrowest**: grid, then column, then row, then cell, and a column's own
  `CellStyle` last of all. Within one scope, later entries in `Highlights` win, and a rule beats the
  column's declared `Highlight`.
- **Merging is per member *group*, not per member.** Fill, stroke and text each come wholesale from the
  most specific style that speaks to them, so a cell rule that sets only a stroke keeps the column
  rule's fill instead of inheriting half of each.
- **Data cells only.** A column highlight does not tint that column's header, filter row or footer -
  the highlight marks the data, and the header keeps reading as chrome.
- `IsEnabled = false` leaves a rule in the collection and stops it painting.
- **Evaluated when a row binds/renders**, not when a property on the item changes - same as `CellStyle`.
  On MAUI, mutating a rule in place is not observed either: swap the rule, or call
  `RefreshHighlights()`.
- On **MAUI** `Highlights` defaults to an `ObservableCollection` the grid watches, and is a
  `BindableProperty` so a view model can hand over its own. `DataGridHighlight` is a plain class, not a
  `BindableObject`, so `{Binding}` on an individual rule property does not work - bind the collection
  or the `RowHighlight` delegate instead.
- On **MAUI** a grid that styles anything wraps each cell in a host so it can carry the paint, and adds
  a drawing layer only to the cells that actually get a stroke. Grids that style nothing pay for none
  of it.

## Behavior notes & platform nuances

- **Sorting**: click/tap a header to cycle asc → desc → none. In `Multiple` mode each header adds to the
  sort with an order badge.
- **Filtering**: `Menu` shows a per-column filter popup (type-aware operators); `Row` shows inline
  filter inputs under the header; `Toolbar` shows a single quick-search box that matches any column.
- **Editing**: Blazor `Cell` edits one cell on click (commit on blur/Enter, cancel on Escape); `Form`
  edits the whole row with Save/Cancel. MAUI uses **inline-row editing** (editors for editable columns +
  a Save/Cancel bar) for both modes — the touch-friendly model.
- **Column widths**: Blazor takes any CSS length on `Width` — `"160px"`, `"12rem"`, `"25%"`. MAUI takes a
  `GridLength` (`"*"`, `"2*"`, `"Auto"`, `"160"`) plus **`WidthPercent`** (1-100), which wins over `Width`
  when set. Outside `HorizontalScroll` a MAUI percentage resolves to a star of the same factor — a star
  factor *is* a percentage, since the Grid divides the available width in the ratio of the factors — and
  under `HorizontalScroll` it resolves against the scroller's own width, so percentages summing past 100
  are what make the grid scroll. Prefer percentages when the same layout has to read the same on both
  hosts.
- **Reorder**: both hosts have **drag-and-drop on headers via `DragDropColumnReordering`, off by
  default** — drag a header onto another and a marker shows the edge it will land on; dropping to the
  right of a column puts it *after* that column. MAUI additionally offers ‹ › reorder arrows under the
  separate `AllowColumnReorder` (the accessible, no-drag path to the same thing; a grid can enable
  either, both, or neither). Each drop raises `ColumnReordered`, which is what you persist to restore a
  user's column layout. Blazor keeps the order on the grid (`ResetColumnOrder()` clears it); MAUI moves
  the column in `Columns` itself.
  - ⚠️ On MAUI under `HorizontalScroll`, enabling drag reorder claims sideways gestures that start on a
    header, so the grid is scrolled by dragging a row instead.
- **Column resizing**: switch it on per grid (Blazor `ColumnResizeMode`, MAUI `AllowColumnResize="True"`),
  then drag the right edge of a header. Any column can opt out with `Resizable="false"` — it keeps its
  width and offers no handle.
  - **Bounds**: `MinWidth` / `MaxWidth` per column, falling back to the grid's
    `MinColumnWidth` (48 by default) / `MaxColumnWidth` (unbounded by default). Set at least a
    `MinWidth` on any column a user can drag, or they can squeeze it down to the floor and lose the
    header text. A `MaxWidth` below the `MinWidth` loses — the floor wins, so a bad pair still leaves a
    usable column.
  - The grid-level pair bounds the **drag**, not the layout: a `Width="40"` icon column stays 40 wide
    even though dragging it would stop at 48. Only a column's own `MinWidth`/`MaxWidth` bound the
    declared width as well.
  - **Blazor** takes CSS strings (`MinWidth="80px"`). A pixel value also clamps the drag; any other unit
    (`%`, `em`) is emitted as CSS but leaves the drag on the grid-level default, because a drag works in
    pixels. `ColumnResizeMode.Column` lets the grid grow; `Container` takes the difference out of the
    next resizable column so the total holds. `ColumnResized` reports the final width (persist it to
    restore widths on the next visit); `ResetColumnWidths()` drops back to the declared widths.
  - **MAUI** takes doubles (`MinWidth="90"`); `0` means "fall back to the grid". A star column outside
    `HorizontalScroll` stays a star and is not clamped — MAUI's `Grid` has no bounded star, so clamping
    would silently turn it absolute.
- **Virtualization**: Blazor opt-in via `Virtualize` (uses `<Virtualize>`, best with `FixedHeader`+`Height`,
  not combined with paging/grouping); MAUI gets it free from `CollectionView`.
- **Frozen header**: Blazor needs `FixedHeader="true"` **and** `Height` (the header sticks against the
  scroller, and without a capped height nothing scrolls). MAUI's header is always frozen — it sits in
  its own row above the `CollectionView`.
- **Frozen columns**: pin a contiguous run at each edge, either per-column (`Frozen="Start"` / `"End"`)
  or by count on the grid (`FrozenColumns` / `FrozenEndColumns`, which also pins the multi-select
  checkbox column). Only a leading/trailing run can be pinned; a `Frozen` column in the middle is
  ignored. Frozen cells paint an opaque background and sit above the scrolling ones.
  - **MAUI requires `HorizontalScroll="True"`** — without sideways scrolling there is nothing to pin
    against, so `Frozen` is a no-op. `HorizontalScroll` puts header, rows and footer in one scroller;
    star widths cannot survive its unbounded measure, so each one resolves to
    `DefaultColumnWidth` (150 by default) x its star factor.
  - Blazor needs no extra flag - the table scrolls sideways whenever the columns are wider than the
    grid. A declared non-percentage `Width` is emitted as `width` **and** `min-width`, because a table
    cell's `width` alone is only a suggestion: without it the browser compresses the columns to fit
    and nothing ever overflows (so nothing scrolls, and pinning has nothing to pin against). Use a `%`
    width when you *want* a column to shrink with the container. Give frozen columns an explicit px `Width` and the offsets are right on the first paint;
    otherwise a small JS module measures them after render.
- **Detail (breakdown) rows**: setting `RowDetailTemplate` adds a caret column at the leading edge and
  renders the template in a full-width row under whichever rows are expanded. `ExpandMode="Single"`
  keeps only one open. The detail content is pinned to the leading edge while the columns scroll
  sideways (MAUI translates the pane; Blazor uses `position: sticky`), so a breakdown never slides out
  of view. Expansion is keyed on the *data item*, so it survives sorting, filtering and paging.
- **Async loading**: both loaders put a **spinner in place of that row's caret** while they run — the
  caret is the button, so the progress goes on it. `RowDetailLoader` fetches whatever the breakdown
  needs the first time a row opens; the detail row shows `RowDetailLoadingTemplate` (default: a
  spinner) meanwhile, and `RowDetailTemplate` is **not built until the load completes**, so it can
  assume its data arrived. The loader returns **no value** — fill an observable property on the item
  (or a lookup keyed by it) and let the template bind to it, which keeps the template's context the
  item rather than a wrapper. Each item loads once; `InvalidateRowDetail(item)` refetches (immediately
  if that row is open). A throw collapses the row and raises `RowDetailLoadFailed`.
  - **`IsBusy`** is true while *any* children or detail load is in flight — a read-only bindable on
    MAUI, a property plus `IsBusyChanged` on Blazor. Bind a page-level indicator to it; per-row
    spinners are drawn either way. It is **not** `IsLoading`/`Loading`, which you set yourself to
    cover the grid while its own data loads. `IsRowBusy(item)` is the per-row form.
- **Tree mode**: hand the grid a `ChildrenSelector` and the first visible column grows an indent and a
  caret — no extra column. `ChildrenLoader` fetches a level on first expand (the caret becomes a
  spinner meanwhile) and caches it; **the selector gets first refusal**, so the loader only runs for items the
  selector returns `null` for and a tree can mix in-memory branches with fetched ones. Give
  `HasChildrenSelector` if you want leaves to render caret-free before anything is loaded.
  - Sorting and filtering apply **per level**, so children stay under their parent, and a row is kept
    when a *descendant* matches the filter — otherwise the match would be unreachable.
  - Paging pages the **roots**; footer aggregates are computed over the roots too.
  - Tree and `Groupable` are mutually exclusive — **grouping wins**.
  - `ExpandAll` opens every already-loaded level; it does not fetch, since a lazy tree's depth is
    unbounded.
- **AOT/trimming**: MAUI string-path value access uses reflection (annotated). For full trim/NativeAOT,
  set a column's `ValueGetter`/`ValueSetter`/`Comparer` to avoid reflection.

## Code Generation Guidance

- Prefer `PropertyColumn` (Blazor) / `DataGridColumn` with `PropertyName` (MAUI) for bound data; use
  `TemplateColumn`/`DataGridTemplateColumn` for custom cells, actions, or status badges.
- Enable only the features asked for (sorting/paging/filtering/grouping/editing) — they're independent
  toggles.
- Blazor paging needs `<PagerContent><DataGridPager TItem="..."/></PagerContent>`; MAUI paging is just
  `PageSize`.
- Reach for a **detail row** when the extra information is *about* the row (a breakdown, a chart, a form)
  and for **tree mode** when the extra rows are more of the same thing one level down. They compose —
  a tree row can also have a detail row — but pick the one that matches the data before using both.
- `TreeDataGrid` and `DataGrid` are the same type; prefer `TreeDataGrid` when the grid is hierarchical so
  the markup says so.
- Leave colors unset to inherit the theme; the grid is light/dark aware.
- For highlighting, reach for `Highlights` when the rules are data- or user-driven and for
  `RowHighlight`/`Column.Highlight` when there is exactly one static rule. Prefer `Fill` over
  `BackgroundColor` - it keeps the striping and selection readable underneath.
- **Budget columns to the width you actually have.** Header titles ellipsize and clip to their column
  (they no longer spill into the next one), but a phone-width grid only has room for roughly **3–4
  columns**, fewer once `AllowColumnResize`/`AllowColumnReorder`/`Groupable`/`FilterMode="Menu"` add
  their glyphs to each header. On narrow layouts prefer a handful of columns — or fold the extras into a
  single `DataGridTemplateColumn` — instead of declaring six and letting every cell render as `…`.
  A `DataGridColumn` with no `Width` is `*`, so stars split whatever the `Auto` columns leave behind.
  When the columns genuinely do not fit (5+ on a phone), set `HorizontalScroll="True"` with absolute
  widths that budget for the title plus its sort arrow and glyphs (~130–150 for a 10-letter title with
  `FilterMode="Menu"`, more with `Groupable`), and `FrozenColumns="1"` to pin the identifying column.
  Each header's glyph strip takes only its own width (MAUI); in a column too narrow for both, the title
  keeps 60% and the glyphs clip.
