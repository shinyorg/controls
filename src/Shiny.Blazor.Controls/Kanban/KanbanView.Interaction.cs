using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Shiny.Blazor.Controls.Kanban;

/// <summary>
/// The drag: picking a card up in JavaScript, and committing where it landed in C#.
/// </summary>
/// <remarks>
/// <para>
/// The drag runs on pointer events, not on HTML5 drag-and-drop. HTML5 DnD does not fire on touch at
/// all, which on a board - the one control people most expect to drag on a phone - is the whole
/// feature missing. Pointer events are uniform across mouse, touch and pen, and they are what
/// <c>GanttView</c> already uses here.
/// </para>
/// <para>
/// The hit testing stays in JavaScript on purpose. Resolving a drop target means reading element
/// rectangles, and doing that over interop would put a render pass and a round trip between the
/// pointer moving and the insertion line following it - which is exactly the lag people read as a
/// broken board. C# is consulted once, at the start of the drag, for the set of lanes that will
/// accept this card; JavaScript then paints against that set without asking again.
/// </para>
/// </remarks>
public partial class KanbanView
{
    /// <summary>Separates the column from the swimlane in a lane key. A unit separator cannot appear in an id.</summary>
    const char LaneSeparator = '';


    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        this.rendered = true;

        if (!firstRender || this.attached)
            return;

        this.attached = true;
        this.selfRef = DotNetObjectReference.Create(this);

        var loaded = await this.JS.InvokeAsync<IJSObjectReference>(
            "import", "./_content/Shiny.Blazor.Controls/kanban.js");
        if (this.disposed) { await loaded.ReleaseLateAsync(); return; }
        this.module = loaded;

        await this.module.InvokeVoidAsync("attach", this.rootElement, this.selfRef);
    }


    /// <summary>A card may be picked up at all.</summary>
    internal bool CanDrag(KanbanCard card)
    {
        if (!this.AllowDragDrop || this.IsReadOnly || card.IsLocked)
            return false;

        var column = this.Board.Column(card.ColumnId);
        return column is null || column.AllowDrag;
    }


    /// <summary>
    /// Called once as a drag begins. Returns the lanes this card may be dropped into, so the
    /// insertion line is only ever painted somewhere the drop will actually be accepted.
    /// </summary>
    [JSInvokable]
    public string[] OnDragStartJs(string cardId)
    {
        var card = this.FindCard(cardId);
        if (card is null)
            return [];

        return this.Board.Lanes
            .Where(lane => this.Board.Evaluate(card, lane.Column.Id, lane.Swimlane?.Id).Allowed)
            .Select(lane => $"{lane.Column.Id}{LaneSeparator}{lane.Swimlane?.Id ?? string.Empty}")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }


    /// <summary>Called when the pointer is released over a lane.</summary>
    [JSInvokable]
    public async Task OnDropJs(string cardId, string columnId, string? swimlaneId, int index)
    {
        var card = this.FindCard(cardId);
        if (card is null)
            return;

        var lane = string.IsNullOrEmpty(swimlaneId) ? null : swimlaneId;
        var move = this.Board.PlanMove(card, columnId, lane, index);

        if (move is null)
        {
            await this.RaiseRejected(card, this.Board.Evaluate(card, columnId, lane).Reason, columnId);
            return;
        }

        await this.CommitMove(move);
    }


    KanbanCard? FindCard(string cardId)
        => this.Board.Lanes
            .SelectMany(lane => lane.Cards)
            .FirstOrDefault(c => string.Equals(c.Id, cardId, StringComparison.Ordinal));


    /// <summary>
    /// Runs a planned move past <see cref="OnCardMoving"/>, applies it, and re-renders.
    /// </summary>
    /// <remarks>
    /// A no-op move still goes through here rather than being short-circuited: dropping a card back
    /// where it started is the user cancelling, and a handler logging every move wants to see that
    /// it ended in nothing rather than see nothing at all.
    /// </remarks>
    async Task<bool> CommitMove(KanbanMove move)
    {
        var moving = new KanbanCardMovingEventArgs(move);

        if (this.OnCardMoving.HasDelegate)
            await this.OnCardMoving.InvokeAsync(moving);

        if (moving.Cancel)
        {
            this.RebuildBoard(render: true);
            return false;
        }

        if (move.IsNoOp)
        {
            this.RebuildBoard(render: true);
            return true;
        }

        this.Board.Apply(move);
        this.RebuildBoard(render: true);

        if (this.OnCardMoved.HasDelegate)
            await this.OnCardMoved.InvokeAsync(new KanbanCardMovedEventArgs(move));

        return true;
    }


    async Task RaiseRejected(KanbanCard card, KanbanDropRejection reason, string? columnId)
    {
        this.RebuildBoard(render: true);

        if (this.OnDropRejected.HasDelegate)
            await this.OnDropRejected.InvokeAsync(
                new KanbanDropRejectedEventArgs(card, this.Board.Column(columnId), reason));
    }
}
