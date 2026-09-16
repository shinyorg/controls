using Shiny.Maui.Controls.Kanban;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// The board engine: bucketing, WIP arithmetic, drop verdicts, and what a move does to everyone
/// else's order.
/// </summary>
/// <remarks>
/// <c>KanbanBoard</c> is mirrored file for file into the Blazor control, so these cover both hosts'
/// bookkeeping - the same arrangement <c>AgendaGeometry</c> already has. None of it touches a view,
/// which is the whole point of the engine being separable in the first place.
/// </remarks>
public class KanbanBoardTests
{
    static KanbanColumn Column(string id, int? wip = null) => new() { Id = id, Title = id, WipLimit = wip };

    static KanbanCard Card(string id, string column, int order, string? swimlane = null) => new()
    {
        Id = id,
        Title = id,
        ColumnId = column,
        SwimlaneId = swimlane,
        Order = order
    };

    static KanbanBoard Build(
        IEnumerable<KanbanCard> cards,
        IEnumerable<KanbanColumn>? columns = null,
        IEnumerable<KanbanSwimlane>? swimlanes = null,
        KanbanBoardOptions? options = null
    ) => new(
        columns ?? [Column("todo"), Column("doing"), Column("done")],
        cards,
        swimlanes,
        options
    );


    // =============================================================================================
    // Bucketing
    // =============================================================================================

    [Fact]
    public void CardsBucketIntoTheirColumnsInOrder()
    {
        var board = Build([Card("b", "todo", 1), Card("a", "todo", 0), Card("c", "doing", 0)]);

        board.Lane("todo", null)!.Cards.Select(c => c.Id).ShouldBe(["a", "b"]);
        board.Lane("doing", null)!.Cards.Select(c => c.Id).ShouldBe(["c"]);
        board.Lane("done", null)!.Cards.ShouldBeEmpty();
    }


    [Fact]
    public void EveryColumnGetsALaneEvenWithNoCards()
    {
        // An empty column is still a drop target. Building lanes only where cards already are means
        // the first card can never be moved into an empty column.
        var board = Build([]);

        board.Lanes.Count.ShouldBe(3);
        board.Lane("done", null).ShouldNotBeNull();
    }


    [Fact]
    public void CardsNamingAnUnknownColumnBecomeOrphansRatherThanVanishing()
    {
        var board = Build([Card("a", "todo", 0), Card("ghost", "archived", 0)]);

        board.Orphans.Select(c => c.Id).ShouldBe(["ghost"]);
        board.Lanes.SelectMany(l => l.Cards).Select(c => c.Id).ShouldBe(["a"]);
    }


    [Fact]
    public void TiedOrdersFallBackToTheOrderTheCardsArrivedIn()
    {
        var board = Build([Card("first", "todo", 0), Card("second", "todo", 0), Card("third", "todo", 0)]);

        board.Lane("todo", null)!.Cards.Select(c => c.Id).ShouldBe(["first", "second", "third"]);
    }


    // =============================================================================================
    // Swimlanes
    // =============================================================================================

    static readonly KanbanBoardOptions Grouped = new() { SwimlaneMode = KanbanSwimlaneMode.Grouped };

    [Fact]
    public void GroupedModeCrossesEveryColumnWithEverySwimlane()
    {
        var board = Build(
            [Card("a", "todo", 0, "team-a"), Card("b", "todo", 0, "team-b")],
            swimlanes: [new KanbanSwimlane { Id = "team-a" }, new KanbanSwimlane { Id = "team-b" }],
            options: Grouped
        );

        board.Lanes.Count.ShouldBe(6);
        board.Lane("todo", "team-a")!.Cards.Select(c => c.Id).ShouldBe(["a"]);
        board.Lane("todo", "team-b")!.Cards.Select(c => c.Id).ShouldBe(["b"]);
    }


    [Fact]
    public void ACardWithNoSwimlaneFallsIntoTheFirstOne()
    {
        var board = Build(
            [Card("a", "todo", 0), Card("b", "todo", 1, "nonsense")],
            swimlanes: [new KanbanSwimlane { Id = "team-a" }, new KanbanSwimlane { Id = "team-b" }],
            options: Grouped
        );

        board.Lane("todo", "team-a")!.Cards.Select(c => c.Id).ShouldBe(["a", "b"]);
    }


    [Fact]
    public void SwimlanesAreIgnoredWhenTheModeIsNone()
    {
        var board = Build(
            [Card("a", "todo", 0, "team-a"), Card("b", "todo", 1, "team-b")],
            swimlanes: [new KanbanSwimlane { Id = "team-a" }, new KanbanSwimlane { Id = "team-b" }]
        );

        board.Lanes.Count.ShouldBe(3);
        board.Lane("todo", null)!.Cards.Select(c => c.Id).ShouldBe(["a", "b"]);
    }


    // =============================================================================================
    // WIP
    // =============================================================================================

    [Fact]
    public void AWipLimitCountsTheWholeColumnNotOneSwimlane()
    {
        var board = Build(
            [Card("a", "doing", 0, "team-a"), Card("b", "doing", 0, "team-b")],
            columns: [Column("todo"), Column("doing", 2), Column("done")],
            swimlanes: [new KanbanSwimlane { Id = "team-a" }, new KanbanSwimlane { Id = "team-b" }],
            options: Grouped
        );

        board.CountIn("doing").ShouldBe(2);
        board.IsAtLimit("doing").ShouldBeTrue();
        board.Lane("doing", "team-a")!.Count.ShouldBe(1);
        board.Lane("doing", "team-a")!.IsAtLimit.ShouldBeTrue();
    }


    [Fact]
    public void BlockRefusesADropIntoAFullColumn()
    {
        var cards = new[] { Card("a", "doing", 0), Card("b", "todo", 0) };
        var board = Build(
            cards,
            columns: [Column("todo"), Column("doing", 1), Column("done")],
            options: new KanbanBoardOptions { WipBehavior = KanbanWipBehavior.Block }
        );

        var verdict = board.Evaluate(cards[1], "doing", null);

        verdict.Allowed.ShouldBeFalse();
        verdict.Reason.ShouldBe(KanbanDropRejection.WipLimitReached);
        board.PlanMove(cards[1], "doing", null, 0).ShouldBeNull();
    }


    [Fact]
    public void WarnLetsTheDropLandAnyway()
    {
        var cards = new[] { Card("a", "doing", 0), Card("b", "todo", 0) };
        var board = Build(
            cards,
            columns: [Column("todo"), Column("doing", 1), Column("done")],
            options: new KanbanBoardOptions { WipBehavior = KanbanWipBehavior.Warn }
        );

        board.Evaluate(cards[1], "doing", null).Allowed.ShouldBeTrue();
    }


    [Fact]
    public void ReorderingInsideAFullColumnIsStillAllowed()
    {
        // Sorting work already in progress adds no work in progress. Getting this backwards makes a
        // column at its limit impossible to reorder, which reads as the board having frozen.
        var cards = new[] { Card("a", "doing", 0), Card("b", "doing", 1) };
        var board = Build(
            cards,
            columns: [Column("todo"), Column("doing", 2), Column("done")],
            options: new KanbanBoardOptions { WipBehavior = KanbanWipBehavior.Block }
        );

        board.Evaluate(cards[1], "doing", null).Allowed.ShouldBeTrue();
    }


    // =============================================================================================
    // Permissions
    // =============================================================================================

    [Fact]
    public void ALockedCardCannotBeDropped()
    {
        var card = Card("a", "todo", 0);
        card.IsLocked = true;

        Build([card]).Evaluate(card, "doing", null).Reason.ShouldBe(KanbanDropRejection.CardLocked);
    }


    [Fact]
    public void AColumnThatRefusesDropsRefusesThem()
    {
        var card = Card("a", "todo", 0);
        var closed = Column("doing");
        closed.AllowDrop = false;

        Build([card], columns: [Column("todo"), closed])
            .Evaluate(card, "doing", null)
            .Reason
            .ShouldBe(KanbanDropRejection.ColumnRejectsDrop);
    }


    [Fact]
    public void AColumnThatRefusesDragsHoldsOntoItsCards()
    {
        var card = Card("a", "todo", 0);
        var pinned = Column("todo");
        pinned.AllowDrag = false;

        Build([card], columns: [pinned, Column("doing")])
            .Evaluate(card, "doing", null)
            .Reason
            .ShouldBe(KanbanDropRejection.ColumnRejectsDrag);
    }


    [Fact]
    public void AnUnknownColumnIsRefusedRatherThanThrowing()
        => Build([Card("a", "todo", 0)])
            .Evaluate(Card("a", "todo", 0), "nowhere", null)
            .Reason
            .ShouldBe(KanbanDropRejection.UnknownColumn);


    // =============================================================================================
    // Planning and applying
    // =============================================================================================

    [Fact]
    public void MovingToAnotherColumnRenumbersBothLanes()
    {
        var cards = new[]
        {
            Card("a", "todo", 0),
            Card("b", "todo", 1),
            Card("c", "todo", 2),
            Card("x", "doing", 0)
        };
        var board = Build(cards);

        var move = board.PlanMove(cards[1], "doing", null, 0);
        move.ShouldNotBeNull();
        board.Apply(move);

        cards[1].ColumnId.ShouldBe("doing");
        cards[1].Order.ShouldBe(0);
        cards[3].Order.ShouldBe(1);

        // The hole b left behind is closed up rather than leaving a gap at 1.
        cards[0].Order.ShouldBe(0);
        cards[2].Order.ShouldBe(1);
    }


    [Fact]
    public void MovingDownWithinALaneAccountsForTheCardLeavingItsOwnSlot()
    {
        // The index arrives in the destination's current terms, with the dragged card still in it.
        // Dropping "a" below "b" means index 2 on the way in and index 1 on the way out - get that
        // wrong and the card springs straight back where it started.
        var cards = new[] { Card("a", "todo", 0), Card("b", "todo", 1), Card("c", "todo", 2) };
        var board = Build(cards);

        var move = board.PlanMove(cards[0], "todo", null, 2);

        move.ShouldNotBeNull();
        move.ToIndex.ShouldBe(1);
        move.IsNoOp.ShouldBeFalse();

        board.Apply(move);
        board.Lane("todo", null)!.Cards.Select(c => c.Id).ShouldBe(["a", "b", "c"]);
        cards.OrderBy(c => c.Order).Select(c => c.Id).ShouldBe(["b", "a", "c"]);
    }


    [Fact]
    public void MovingUpWithinALaneKeepsTheIndexAsGiven()
    {
        var cards = new[] { Card("a", "todo", 0), Card("b", "todo", 1), Card("c", "todo", 2) };
        var board = Build(cards);

        var move = board.PlanMove(cards[2], "todo", null, 0);

        move!.ToIndex.ShouldBe(0);
        board.Apply(move);
        cards.OrderBy(c => c.Order).Select(c => c.Id).ShouldBe(["c", "a", "b"]);
    }


    [Fact]
    public void DroppingACardBackWhereItStartedIsANoOp()
    {
        var cards = new[] { Card("a", "todo", 0), Card("b", "todo", 1) };
        var board = Build(cards);

        board.PlanMove(cards[0], "todo", null, 0)!.IsNoOp.ShouldBeTrue();
    }


    [Fact]
    public void ANegativeIndexAppends()
    {
        var cards = new[] { Card("a", "todo", 0), Card("x", "doing", 0), Card("y", "doing", 1) };
        var board = Build(cards);

        var move = board.PlanMove(cards[0], "doing", null, -1);

        move!.ToIndex.ShouldBe(2);
        board.Apply(move);
        cards[0].Order.ShouldBe(2);
    }


    [Fact]
    public void AnOutOfRangeIndexIsClampedRatherThanThrowing()
    {
        var cards = new[] { Card("a", "todo", 0), Card("x", "doing", 0) };
        var board = Build(cards);

        board.PlanMove(cards[0], "doing", null, 99)!.ToIndex.ShouldBe(1);
    }


    [Fact]
    public void MovingBetweenSwimlanesRewritesTheSwimlaneId()
    {
        var cards = new[] { Card("a", "todo", 0, "team-a") };
        var board = Build(
            cards,
            swimlanes: [new KanbanSwimlane { Id = "team-a" }, new KanbanSwimlane { Id = "team-b" }],
            options: Grouped
        );

        var move = board.PlanMove(cards[0], "doing", "team-b", 0);

        move!.ChangedColumn.ShouldBeTrue();
        move.ChangedSwimlane.ShouldBeTrue();

        board.Apply(move);
        cards[0].SwimlaneId.ShouldBe("team-b");
        cards[0].ColumnId.ShouldBe("doing");
    }


    [Fact]
    public void AMoveRecordsWhereTheCardCameFromSoItCanBeUndone()
    {
        var cards = new[] { Card("a", "todo", 0), Card("b", "todo", 1) };
        var board = Build(cards);

        var move = board.PlanMove(cards[1], "done", null, 0)!;

        move.FromColumnId.ShouldBe("todo");
        move.FromIndex.ShouldBe(1);
        move.ToColumnId.ShouldBe("done");

        board.Apply(move);

        // The move is enough to put it back: same card, the column and index it started in.
        var back = new KanbanBoard([Column("todo"), Column("doing"), Column("done")], cards)
            .PlanMove(move.Card, move.FromColumnId, null, move.FromIndex);

        back.ShouldNotBeNull();
        back.ToColumnId.ShouldBe("todo");
    }


    [Fact]
    public void ApplyingANullMoveDoesNothing()
        => Should.NotThrow(() => Build([]).Apply(null));
}
