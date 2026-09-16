# Kanban

A board for **both MAUI and Blazor**: columns of cards you drag between, with enforced WIP limits,
swimlanes, collapsible columns, cancellable moves and an inline add-card composer.

- **MAUI** — `Shiny.Maui.Controls.Kanban.KanbanView` (core package, `shiny:KanbanView` in XAML)
- **Blazor** — `Shiny.Blazor.Controls.Kanban.KanbanView`

## The one thing to understand first

**Every decision lives in `KanbanBoard`, and neither control makes one of its own.** Bucketing, WIP
counts, drop verdicts, move planning and order renumbering are all there, free of any UI type, and
the file is mirrored verbatim into both packages (a test fails if the copies drift). When generating
code, put model work against `KanbanBoard`, not against the view.

## Model

```csharp
using Shiny.Maui.Controls.Kanban;   // or Shiny.Blazor.Controls.Kanban

var columns = new ObservableCollection<KanbanColumn>
{
    new() { Id = "todo",  Title = "To do" },
    new() { Id = "doing", Title = "In progress", WipLimit = 2, Color = "#F59E0B" },
    new() { Id = "done",  Title = "Done" }
};

var cards = new ObservableCollection<KanbanCard>
{
    new() { Id = "1", ColumnId = "todo",  Order = 0, Title = "Audit theme tokens" },
    new() { Id = "2", ColumnId = "doing", Order = 0, Title = "Drag on GTK4",
            AssigneeName = "Ada Lovelace", DueDate = DateTimeOffset.Now.AddDays(1),
            Badge = "SH-398", Color = "#EF4444" }
};

cards[1].Labels.Add(new KanbanLabel("bug", "#EF4444"));
```

Cards **must be `KanbanCard`** — there is no generic `TItem`. Map a domain object onto one and put
the original in `KanbanCard.Item`; every event and template hands the card back. A drop rewrites
`ColumnId`, `SwimlaneId` and `Order`, which is why the type is fixed.

**Colours are strings, not platform colours** — `"#2563eb"`, `"rebeccapurple"` — on `KanbanCard`,
`KanbanColumn`, `KanbanSwimlane` and `KanbanLabel`. Never generate `Colors.Red` for these.

`KanbanColumn`: `Id`, `Title`, `Description`, `Color`, `WipLimit`, `IsCollapsed`, `AllowDrop`,
`AllowDrag`, `AllowAdd`, `Width`, `Item`.
`KanbanCard`: `Id`, `ColumnId`, `SwimlaneId`, `Title`, `Description`, `Color`, `AssigneeName`,
`AssigneeImage`, `DueDate`, `Badge`, `Order`, `IsLocked`, `Labels`, `Item`.
`KanbanSwimlane`: `Id`, `Title`, `Color`, `IsCollapsed`, `Item`.

## WIP limits — two independent switches

- `ShowWipLimits` (default `true`) — whether an over-limit column is *styled* as over-limit.
- `WipBehavior` — whether it *refuses* the drop: `None`, `Warn` (**default**, drop still lands),
  `Block`.

A `WipLimit` counts the **whole column across every swimlane**. Reordering inside a column is always
allowed however full it is. A refused drop raises `DropRejected` with a `KanbanDropRejection`:
`CardLocked`, `ColumnRejectsDrag`, `ColumnRejectsDrop`, `WipLimitReached`, `UnknownColumn`,
`UnknownSwimlane`.

## Swimlanes

`SwimlaneMode` defaults to `None`, and in that mode `KanbanCard.SwimlaneId` is **ignored**. Set it to
`Grouped` and supply `Swimlanes` to get bands, each carrying the full set of columns. A card whose
swimlane id matches nothing falls into the first lane. Collapsing a band or a column is display only
— the cards still count and the lane still takes drops. A collapsed column is a `CollapsedColumnWidth`
spine: its header stacks the chevron over a compact count (`3/4`), and its lanes paint nothing (no
cards, no empty placeholder, no well) while remaining transparent drop targets.

## Moving

Every move — dragged or called — runs `Evaluate` → `CardMoving` (cancellable) → `Apply` →
`CardMoved`. A `KanbanMove` records `FromColumnId`, `FromSwimlaneId`, `FromIndex`, `ToColumnId`,
`ToSwimlaneId`, `ToIndex`, `IsNoOp`, `ChangedColumn`, `ChangedSwimlane` — so undo is a stack of
moves. Orders are renumbered from zero, never given fractional values.

## Adding a card

`AddCardMode` is `None` by default; `Button` raises the request immediately, `Inline` opens a
one-line composer first. **The board never creates the card** — handle `AddCardRequested` (MAUI) /
`OnAddCardRequested` (Blazor) and add a `KanbanCard` to your own collection.

## MAUI

```xml
<shiny:KanbanView Cards="{Binding Cards}"
                  Columns="{Binding Columns}"
                  Swimlanes="{Binding Swimlanes}"
                  SwimlaneMode="Grouped"
                  WipBehavior="Block"
                  ShowWipLimits="True"
                  AddCardMode="Inline"
                  ColumnWidth="260"
                  SelectedCard="{Binding SelectedCard, Mode=TwoWay}"
                  CardMoving="OnCardMoving"
                  CardMoved="OnCardMoved"
                  DropRejected="OnDropRejected"
                  AddCardRequested="OnAddCardRequested" />
```

Methods: `MoveCard(card, columnId, swimlaneId, index)`, `RebuildBoard()`, `ToggleColumn(column)`,
`ToggleSwimlane(swimlane)`, `CollapseAllColumns()`, `ExpandAllColumns()`, `ScrollToCard(card)`,
`ScrollToColumn(column)`. Property: `Board` (the current `KanbanBoard`).

Events: `CardMoving` (cancellable), `CardMoved`, `DropRejected`, `CardTapped`,
`ColumnCollapseChanged`, `AddCardRequested`, `BoardBuilt`.
Commands: `CardTappedCommand`, `CardMovedCommand`, `AddCardCommand`.

The drag is a `PanGestureRecognizer` on every platform — **never** generate a
`DragGestureRecognizer`/`DropGestureRecognizer` for this control.

## Blazor

```razor
@using Shiny.Blazor.Controls.Kanban

<div style="height:560px">
    <KanbanView @ref="board"
                Cards="cards"
                Columns="columns"
                Swimlanes="swimlanes"
                SwimlaneMode="KanbanSwimlaneMode.Grouped"
                WipBehavior="KanbanWipBehavior.Block"
                AddCardMode="KanbanAddCardMode.Inline"
                @bind-SelectedCard="selected"
                OnCardMoving="OnCardMoving"
                OnCardMoved="OnCardMoved"
                OnDropRejected="OnDropRejected"
                OnAddCardRequested="OnAddCardRequested" />
</div>
```

The component fills its parent and **needs a bounded height**.

Methods: `MoveCardAsync(card, columnId, swimlaneId, index)`, `RebuildBoard()`, `ToggleColumn`,
`ToggleSwimlane`, `CollapseAllColumns()`, `ExpandAllColumns()`, `ScrollToCardAsync(card)`.
Callbacks: `OnCardMoving`, `OnCardMoved`, `OnDropRejected`, `OnCardTapped`,
`OnColumnCollapseChanged`, `OnAddCardRequested`, `OnBoardBuilt`.

The drag is on **pointer events**, not HTML5 drag-and-drop (DnD never fires on touch).

## Display and layout

Switches (both hosts): `ShowLabels`, `ShowAssignee`, `ShowDueDate`, `DescriptionLineLimit` (0 hides),
`ColumnCountDisplay` (`None`/`Count`/`CountAndLimit`), `DueSoonWindow`, `DueDateFormat`,
`EmptyColumnText`, `Culture`, `IsReadOnly`, `AllowDragDrop`, `AllowColumnCollapse`,
`AllowSwimlaneCollapse`.

Layout: `ColumnWidth` (280), `CollapsedColumnWidth` (52), `ColumnSpacing` (12), `CardSpacing` (8),
`MinColumnHeight` (120), and MAUI-only `ColumnPadding`.

Prefer these over a template. Reach for `CardTemplate`, `ColumnHeaderTemplate`,
`SwimlaneHeaderTemplate` or `EmptyColumnTemplate` only when the card is genuinely a different shape.
The drag belongs to the board, which wraps whatever the template produces — a custom card never needs
its own gesture.
