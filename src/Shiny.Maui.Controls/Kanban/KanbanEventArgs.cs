namespace Shiny.Maui.Controls.Kanban;

/// <summary>
/// Raised once a drop target has been resolved but before anything moves, carrying the whole
/// consequence so a handler can veto it.
/// </summary>
/// <remarks>
/// This is the hook for "only a reviewer may move a card into Done" and for pushing the move to a
/// server before believing it. Cancel and the card springs back to where it came from.
/// </remarks>
public class KanbanCardMovingEventArgs : EventArgs
{
    internal KanbanCardMovingEventArgs(KanbanMove move) => this.Move = move;

    /// <summary>What would happen. Nothing has been applied yet.</summary>
    public KanbanMove Move { get; }

    /// <summary>The card the user dragged.</summary>
    public KanbanCard Card => this.Move.Card;

    /// <summary>Set to true to abandon the move.</summary>
    public bool Cancel { get; set; }
}


/// <summary>Raised after a card has been moved and the model rewritten.</summary>
public class KanbanCardMovedEventArgs : EventArgs
{
    internal KanbanCardMovedEventArgs(KanbanMove move) => this.Move = move;

    /// <summary>
    /// The applied move. Keep it to implement undo - it records the column, swimlane and index the
    /// card came from as well as where it went.
    /// </summary>
    public KanbanMove Move { get; }

    public KanbanCard Card => this.Move.Card;
}


/// <summary>
/// Raised when a drop is refused, whether by a WIP limit, a locked card or a column that does not
/// take drops.
/// </summary>
/// <remarks>
/// Worth handling: a refused drop is otherwise completely silent, and "I dragged it and nothing
/// happened" is the single most common complaint about a Kanban board.
/// </remarks>
public class KanbanDropRejectedEventArgs : EventArgs
{
    internal KanbanDropRejectedEventArgs(KanbanCard card, KanbanColumn? column, KanbanDropRejection reason)
    {
        this.Card = card;
        this.Column = column;
        this.Reason = reason;
    }

    /// <summary>The card that was being dragged.</summary>
    public KanbanCard Card { get; }

    /// <summary>Where it was aimed, when that column exists.</summary>
    public KanbanColumn? Column { get; }

    /// <summary>What refused it.</summary>
    public KanbanDropRejection Reason { get; }
}


/// <summary>Raised when a card is tapped.</summary>
public class KanbanCardEventArgs(KanbanCard card) : EventArgs
{
    public KanbanCard Card { get; } = card;
}


/// <summary>Raised when a column's header or its collapse chevron is used.</summary>
public class KanbanColumnEventArgs(KanbanColumn column) : EventArgs
{
    public KanbanColumn Column { get; } = column;
}


/// <summary>
/// Raised when the user asks for a new card in a column - the inline "+ Add card" affordance.
/// </summary>
/// <remarks>
/// The board deliberately does not create the card. It has no idea what a card means in your
/// domain, and a board that invented a blank one would leave you deleting it again on cancel.
/// Handle this, add a <see cref="KanbanCard"/> to your own collection, and the board will render it.
/// </remarks>
public class KanbanAddCardEventArgs(KanbanColumn column, KanbanSwimlane? swimlane) : EventArgs
{
    /// <summary>The column the "+" was pressed in.</summary>
    public KanbanColumn Column { get; } = column;

    /// <summary>The swimlane it was pressed in, or null when swimlanes are off.</summary>
    public KanbanSwimlane? Swimlane { get; } = swimlane;

    /// <summary>
    /// The title typed into the inline composer, when <c>KanbanView.AddCardMode</c> is
    /// <c>Inline</c>. Empty when the composer is disabled and the "+" is a plain button.
    /// </summary>
    public string Title { get; init; } = string.Empty;
}


/// <summary>Raised after the board has been rebuilt - the hook for a "3 cards are over WIP" banner.</summary>
public class KanbanBoardBuiltEventArgs(KanbanBoard board) : EventArgs
{
    public KanbanBoard Board { get; } = board;
}
