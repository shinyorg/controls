namespace Shiny.Blazor.Controls.Kanban;

/// <summary>Whether the board is grouped into swimlane rows.</summary>
public enum KanbanSwimlaneMode
{
    /// <summary>One flat row of columns.</summary>
    None,

    /// <summary>Cards are grouped into a horizontal band per swimlane, each with the full set of columns.</summary>
    Grouped
}


/// <summary>What a column at its WIP limit does to an incoming card.</summary>
public enum KanbanWipBehavior
{
    /// <summary>The limit is displayed and otherwise ignored.</summary>
    None,

    /// <summary>The column is styled as over-limit, but the drop still lands.</summary>
    Warn,

    /// <summary>The drop is refused once the column is full.</summary>
    Block
}


/// <summary>Why a drop was refused.</summary>
public enum KanbanDropRejection
{
    /// <summary>It was not - the drop is allowed.</summary>
    None,

    /// <summary>There was no card.</summary>
    UnknownCard,

    /// <summary>The card sets <see cref="KanbanCard.IsLocked"/>.</summary>
    CardLocked,

    /// <summary>The card's own column does not let its cards leave.</summary>
    ColumnRejectsDrag,

    /// <summary>The destination column id matched nothing on the board.</summary>
    UnknownColumn,

    /// <summary>The destination column sets <c>AllowDrop="False"</c>.</summary>
    ColumnRejectsDrop,

    /// <summary>The destination swimlane id matched nothing on the board.</summary>
    UnknownSwimlane,

    /// <summary>The destination column is full and <see cref="KanbanWipBehavior.Block"/> is in force.</summary>
    WipLimitReached
}


/// <summary>The affordance for adding a card at the foot of a column.</summary>
public enum KanbanAddCardMode
{
    /// <summary>None. The board is display-and-drag only.</summary>
    None,

    /// <summary>A "+ Add card" button that raises <c>AddCardRequested</c> and nothing else.</summary>
    Button,

    /// <summary>
    /// A "+ Add card" button that opens a one-line composer in place; committing it raises
    /// <c>AddCardRequested</c> with the typed title.
    /// </summary>
    Inline
}


/// <summary>What the header of a column shows beside its title.</summary>
public enum KanbanColumnCount
{
    /// <summary>Nothing.</summary>
    None,

    /// <summary>The number of cards in the column.</summary>
    Count,

    /// <summary>The number of cards and the WIP limit, as <c>3 / 5</c>. Falls back to the count alone when there is no limit.</summary>
    CountAndLimit
}
