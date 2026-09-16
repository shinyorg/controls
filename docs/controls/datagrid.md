# DataGrid

[← All Shiny Controls](../../README.md)

A feature-rich data grid for both hosts, modeled on MudBlazor's DataGrid. Blazor renders a semantic
HTML `<table>` (generic `DataGrid<TItem>`); MAUI is a pure cross-platform composite (a `Grid` header
over a virtualized `CollectionView`, no native handlers). Same feature surface on both: typed
`PropertyColumn` + `TemplateColumn`, sorting (single + multi), column **filtering** (menu / row /
toolbar quick-search), **multi-level grouping** with expandable groups, **summary (total) rows**
under the grid and inside every group (Count/Sum/Average/Min/Max/Custom), single/multi **selection** with checkboxes, inline **editing**
(cell + form), **detail ("breakdown") rows**, a **tree/hierarchy mode** (`TreeDataGrid`) with lazy child
loading, **paging**, **virtualization**, column **resize/reorder**, **frozen columns** and a
frozen (sticky) header, loading + empty states, a `ServerData` delegate for server-side data, and
density/striped/bordered/hover styling, and **column formatting** - display presets, alignment, a
null placeholder, prefix/suffix, wrapping and conditional per-cell styling, none of which need a cell
template. Colors follow the theme tokens.

```razor
@* Blazor *@
<DataGrid TItem="Person" Items="people" MultiSelection="true"
          SortMode="DataGridSortMode.Multiple" FilterMode="DataGridFilterMode.Menu"
          Groupable="true" EditMode="DataGridEditMode.Form"
          Dense="true" Striped="true" Hover="true" FixedHeader="true" Height="420px"
          FrozenColumns="1" FrozenEndColumns="1"
          ColumnResizeMode="DataGridColumnResizeMode.Column" MinColumnWidth="60" MaxColumnWidth="420"
          DragDropColumnReordering="true">
    <Columns>
        <PropertyColumn Property="x => x.FirstName" Title="First"
                        Width="25%" MinWidth="80px" MaxWidth="260px" />
        <PropertyColumn Property="x => x.Age" StringFormat="N0" Width="120px" />
        <PropertyColumn Property="x => x.Salary" DisplayAs="DataGridColumnFormat.Currency"
                        Decimals="0" Width="180px"
                        CellStyle="@(p => p.Salary < 100000 ? new DataGridCellStyle { TextColor = "#c62828" } : null)" />
        <PropertyColumn Property="x => x.Reviewed" DisplayAs="DataGridColumnFormat.Date" NullText="—" />
        <TemplateColumn Title="Status" Sortable="false" Resizable="false" Width="140px">
            <CellTemplate><Pill Text="@(context.Item.Active ? "Active" : "Inactive")" /></CellTemplate>
        </TemplateColumn>
    </Columns>
    <PagerContent><DataGridPager TItem="Person" /></PagerContent>
</DataGrid>
```

```xml
<!-- MAUI -->
<shiny:DataGrid ItemsSource="{Binding People}" SelectionMode="Multiple"
                SortMode="Multiple" FilterMode="Menu" Groupable="True"
                PageSize="20" EditMode="Form" AllowColumnResize="True" AllowColumnReorder="True"
                HorizontalScroll="True" DefaultColumnWidth="140" FrozenColumns="1"
                MinColumnWidth="70" MaxColumnWidth="400"
                DragDropColumnReordering="True"
                Striped="True" Bordered="True">
    <shiny:DataGridColumn Title="First" PropertyName="FirstName" Width="*"
                          MinWidth="90" MaxWidth="260" />
    <shiny:DataGridColumn Title="Age" PropertyName="Age" Width="Auto" />
    <shiny:DataGridColumn Title="Department" PropertyName="Department" WidthPercent="30" />
    <shiny:DataGridColumn Title="Salary" PropertyName="Salary"
                          DisplayAs="Currency" Decimals="0" Width="*"
                          CellStyle="{Binding SalaryStyle}">
        <shiny:DataGridColumn.Aggregate>
            <shiny:DataGridAggregateDefinition Type="Sum" Format="C0" />
        </shiny:DataGridColumn.Aggregate>
    </shiny:DataGridColumn>
    <shiny:DataGridColumn Title="Reviewed" PropertyName="LastReview"
                          DisplayAs="Date" NullText="—" Width="*" />
    <shiny:DataGridTemplateColumn Title="Status" Width="110" Editable="False"
                                  Resizable="False" Frozen="End">
        <shiny:DataGridTemplateColumn.CellTemplate>
            <DataTemplate><shiny:PillView Text="{Binding StatusText}" /></DataTemplate>
        </shiny:DataGridTemplateColumn.CellTemplate>
    </shiny:DataGridTemplateColumn>
</shiny:DataGrid>
```

Reflection-based string-path columns are annotated for trimming; set a column's `ValueGetter`/
`ValueSetter` (MAUI) for fully reflection-free AOT.

Header titles ellipsize and clip to their own column, so budget columns to the width you have: a
phone-width grid fits roughly **3–4** columns, fewer once `AllowColumnResize`, `AllowColumnReorder`,
`Groupable`, or `FilterMode="Menu"` add their glyphs to each header. On MAUI those glyphs take only the
width they need and the title gets the rest (in a column too narrow for both, the title keeps 60% and
the glyphs clip). Columns with no `Width` are `*`
and split whatever the `Auto` columns leave behind — or give the grid more room than it has and turn on
horizontal scrolling instead.

**Column widths** — Blazor takes any CSS length on `Width` (`"160px"`, `"12rem"`, `"25%"`). MAUI takes a
`GridLength` (`"*"`, `"2*"`, `"Auto"`, `"160"`) plus **`WidthPercent`** (1–100), which wins over `Width`
when set: outside `HorizontalScroll` it resolves to a star of the same factor — a star factor *is* a
percentage, since the Grid divides the available width in the ratio of the factors — and under
`HorizontalScroll` it resolves against the scroller's own width, so percentages summing past 100 are what
make the grid scroll. Percentages are the way to write one layout that reads the same on both hosts.

**Column formatting** — the ordinary reasons to write a cell template, offered as column properties
instead. `DisplayAs` picks a preset — `Currency`, `Percent`, `Number`, `Date`, `Time`, `DateTime`,
`FileSize`, `Boolean` (a glyph, or your own `TrueText`/`FalseText`), `Enum` (its `[Description]`, else
its name split on PascalCase) — and `Decimals` tunes the numeric ones. A raw .NET format string still
works and wins over the preset: `StringFormat` on both hosts (MAUI also accepts the old
`"{}{0:C0}"` binding dialect; Blazor's `Format` is a still-working alias). `NullText` covers a null or
empty value, `Prefix`/`Suffix` decorate a real one, and `TextFormatter` takes the raw value and returns
the string when none of that fits — all of it feeding one code path, so a cell, the quick-filter search
index and a group header can no longer disagree about what a value reads as.

`Alignment` (and `HeaderAlignment`, which follows it by default) defaults to `Auto`: quantities align
right, everything else left. `Wrap` plus `MaxLines` lets one column breathe while the rest stay on a
line. `CellStyle` takes the row item and returns colours/weight for that one cell — red negatives, an
amber overdue cell — returning `null` to keep the themed default. It is evaluated when a row binds, so
it follows list recycling but not a live property change on the item.

```razor
@* Blazor - colours are CSS, so a theme token works as well as a hex *@
<PropertyColumn Property="x => x.Balance" DisplayAs="DataGridColumnFormat.Currency" Decimals="0"
                NullText="—" Suffix=" USD"
                CellStyle="@(a => a.Balance < 0
                    ? new DataGridCellStyle { TextColor = "var(--shiny-color-error)", Bold = true }
                    : null)" />
```

```xml
<!-- MAUI - CellStyle/TextFormatter are bindable, so they come from the view model -->
<shiny:DataGridColumn Title="Balance" PropertyName="Balance"
                      DisplayAs="Currency" Decimals="0" NullText="—" Suffix=" USD"
                      CellStyle="{Binding BalanceStyle}" />
```

**Column reordering** — **`DragDropColumnReordering`, off by default on both hosts**: drag a header onto
another and a marker shows the edge it will land on; dropping to the right of a column puts it *after*
that column. MAUI additionally offers ‹ › reorder arrows under the separate `AllowColumnReorder` — the
accessible, no-drag path to the same thing — so a grid can enable either, both, or neither. Each drop
raises `ColumnReordered`, which is what you persist to restore a user's layout; Blazor keeps the order on
the grid (`ResetColumnOrder()` clears it), MAUI moves the column in `Columns` itself. On MAUI under
`HorizontalScroll` the drag claims sideways gestures that start on a header, so the grid is scrolled by
dragging a row instead.

**Column resizing** — switch it on per grid (Blazor `ColumnResizeMode`, MAUI `AllowColumnResize="True"`)
and drag the right edge of a header. Any column can opt out with `Resizable="false"`: it keeps its width
and shows no handle. Bound the drag with `MinWidth` / `MaxWidth` per column — CSS strings on Blazor
(`MinWidth="80px"`), doubles on MAUI (`MinWidth="90"`) — falling back to the grid's `MinColumnWidth`
(48) / `MaxColumnWidth` (unbounded). Those grid-level values bound the **drag**, not the layout, so a
deliberately narrow `Width="40"` column stays 40 wide; only a column's own bounds also constrain its
declared width. A `MaxWidth` below the `MinWidth` loses — the floor wins, leaving a column the user can
still see. Blazor adds `ColumnResizeMode.Container`, which takes whatever one column gains out of the
next resizable one so the grid's total width holds, plus a `ColumnResized` callback (persist the widths)
and `ResetColumnWidths()`.

**Frozen header** — MAUI's header is always frozen (it sits in its own row above the `CollectionView`).
Blazor needs `FixedHeader="true"` **and** a `Height`: the header sticks against the scroller, and
without a capped height nothing scrolls.

**Frozen columns** — pin a contiguous run at either edge, per column (`Frozen="Start"` / `"End"`) or by
count on the grid (`FrozenColumns` / `FrozenEndColumns`, which also pins the multi-select checkbox
column). Content slides underneath the pinned block, which paints an opaque background and repeats the
row's own stripe/selection state. On **MAUI this needs `HorizontalScroll="True"`** — without sideways
scrolling there is nothing to pin against — and in that mode star widths cannot survive the scroller's
unbounded measure, so each resolves to `DefaultColumnWidth` (150 by default) x its star factor. Blazor
needs no extra flag; give the pinned columns a px `Width` and the offsets are exact on the first paint.

**Grouping** — `GroupBy` is a list of columns, outermost first, and grouping is on whenever it has an
entry: `<shiny:DataGrid.GroupBy>` / `GroupBy="{Binding GroupColumns}"` on MAUI, `@bind-GroupBy` on
Blazor. Levels nest, each header indenting from its parent and carrying the count of every row beneath
it. `Groupable` is a separate switch: it adds the ⊞ button to each header so the *user* can add or
remove a level (the glyph numbers itself once more than one is in play) — it does not gate a grouping
you declared yourself. Groups order by key (`GroupSortDirection`, ascending by default),
`GroupsInitiallyExpanded` sets whether they start open, `ExpandAllGroups()`/`CollapseAllGroups()` drive
them from code, and `GroupHeaderTemplate` replaces the header's content. Collapse state is tracked per
group *path*, so a "West" under one department is independent of the "West" under another. Paging is
skipped while grouped — a page boundary would slice a group in half.

**Summary (total) rows** — `SummaryRows` holds any number of rows, each a set of cells pointing at
columns. A cell either **aggregates** its column (`Aggregate="Sum"`, plus `Count`/`Average`/`Min`/`Max`,
or `Custom` with a `CustomAggregate` delegate over the rows) or simply **fills its slot with a label**
(`Text="Total"`, `Alignment="End"`) — which is what puts the word in one column and the number in the
next. A column with no cell is left blank, and `CellTemplate` (MAUI) / the cell's child content (Blazor)
takes over the slot entirely. Stack rows for a subtotal / tax / total block.

The same declarations serve the grid's own footer **and** every group, each aggregating exactly the rows
under it; `Scope="Grid"` / `"Group"` narrows a row to one of the two. `GroupSummaryPlacement` says where
a group's rows sit: `Footer` (the default) puts them after the group's rows so they collapse with them,
`Header` puts them under the group's title so the totals stay visible while it is collapsed, `Both` does
both, `None` leaves groups plain. An aggregate with no `StringFormat` is formatted the way its column's
own cells are — the total under a currency column is currency, without repeating the format — except a
`Count`, which is always a plain number. The older per-column `Aggregate`/`FooterTemplate` still works
and produces the single footer row it always did.

```razor
@* Blazor *@
<DataGrid TItem="Sale" Items="sales" @bind-GroupBy="groupBy" Groupable="true"
          GroupSummaryPlacement="DataGridGroupSummaryPlacement.Footer">
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
    </SummaryRows>
</DataGrid>
@code { IReadOnlyList<string> groupBy = ["Department", "Region"]; }
```

```xml
<!-- MAUI -->
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

**Detail ("breakdown") rows** — set a `RowDetailTemplate` and the grid grows a caret column at the
leading edge; expanding a row opens a full-width row beneath it that can host any controls you like.
`ExpandMode="Single"` keeps one open at a time, `IsRowExpandable` vetoes rows with nothing to show, and
`ExpandRow`/`CollapseRow`/`ExpandAll`/`CollapseAll` drive it from code. The breakdown stays pinned to
the leading edge while the columns scroll sideways, and expansion is keyed on the data item, so it
survives sorting, filtering and paging.

Give it a `RowDetailLoader` and the breakdown loads on demand: the caret turns into a **spinner** while
the fetch runs, the detail row shows `RowDetailLoadingTemplate` (a spinner by default), and
`RowDetailTemplate` is not built until the load completes — so it can assume its data arrived. The
loader returns no value; fill an observable property on the item and let the template bind to it as
usual. Each item loads once, `InvalidateRowDetail(item)` refetches, and a throw collapses the row and
raises `RowDetailLoadFailed`.

**`IsBusy`** is true while any children or detail load is in flight — a read-only bindable on MAUI, a
property plus `IsBusyChanged` on Blazor, with `IsRowBusy(item)` for a single row. Bind a page-level
indicator to it; the per-row spinners are drawn either way. It is distinct from `IsLoading`/`Loading`,
which you set yourself to cover the grid while its own data loads.

```razor
@* Blazor *@
<DataGrid TItem="Order" Items="orders" RowDetailLoader="LoadLinesAsync" IsBusyChanged="b => busy = b">
    <Columns>…</Columns>
    <RowDetailTemplate>
        @foreach (var line in lines[context.Id]) { <div>@line.Sku</div> }
    </RowDetailTemplate>
    <RowDetailLoadingTemplate><span class="shiny-dg-busy"></span> Loading…</RowDetailLoadingTemplate>
</DataGrid>
```

```xml
<!-- MAUI -->
<shiny:DataGrid ItemsSource="{Binding People}" RowDetailLoader="{Binding LoadActivity}">
    <shiny:DataGrid.RowDetailTemplate>
        <DataTemplate x:DataType="local:Person">
            <VerticalStackLayout>
                <Label Text="{Binding FirstName, StringFormat='Breakdown for {0}'}" />
                <Label Text="{Binding Salary, StringFormat='Salary: {0:C0}'}" />
            </VerticalStackLayout>
        </DataTemplate>
    </shiny:DataGrid.RowDetailTemplate>
    <shiny:DataGridColumn Title="First" PropertyName="FirstName" />
</shiny:DataGrid>
```

## Dark mode

The grid's own chrome is token-driven and needs nothing set. One MAUI-specific fix worth knowing
about: the pager's glyph buttons no longer pick up the host app's implicit `<Style TargetType="Button">`.
The .NET MAUI template's style sets a `Gray600` background on the `Disabled` visual state, which meant
the first/previous buttons grew an opaque slab in dark mode exactly when they were *not* pressable.
Internal parts now carry their own visual states and express disabled as opacity. See
[Styling & theming](styling.md#dark-mode).
