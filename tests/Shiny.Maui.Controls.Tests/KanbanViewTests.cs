using System.Collections.ObjectModel;
using Microsoft.Maui.Controls;
using Shiny.Maui.Controls.Kanban;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// Host-level cover for <see cref="KanbanView"/>. The bucketing and the move arithmetic are tested
/// exhaustively in <see cref="KanbanBoardTests"/> against the engine, so nothing here re-asserts
/// them. What these do assert is the half that only exists in MAUI: that the control survives
/// construction under an implicit style, that its bindable properties actually reach the board, that
/// a data change is observed rather than requiring a Rebuild by hand, and that a code-driven move
/// runs the same event gauntlet a drag does.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class KanbanViewTests
{
    static KanbanView Build(out ObservableCollection<KanbanCard> cards, out ObservableCollection<KanbanColumn> columns)
    {
        // Application.Current is process-wide; constructing one unconditionally rather than reusing
        // whatever a previous test left behind is what stops an implicit style leaking between them.
        _ = new Application();
        TestDispatcherProvider.Install();

        columns =
        [
            new KanbanColumn { Id = "todo", Title = "To do" },
            new KanbanColumn { Id = "doing", Title = "Doing", WipLimit = 1 },
            new KanbanColumn { Id = "done", Title = "Done" }
        ];

        cards =
        [
            new KanbanCard { Id = "a", Title = "A", ColumnId = "todo", Order = 0 },
            new KanbanCard { Id = "b", Title = "B", ColumnId = "todo", Order = 1 },
            new KanbanCard { Id = "c", Title = "C", ColumnId = "doing", Order = 0 }
        ];

        return new KanbanView { Columns = columns, Cards = cards };
    }


    [Fact]
    public void ConstructingAndBindingBuildsABoard()
    {
        var view = Build(out _, out _);

        view.Board.Columns.Count.ShouldBe(3);
        view.Board.Lane("todo", null)!.Cards.Select(c => c.Id).ShouldBe(["a", "b"]);
        view.Board.CountIn("doing").ShouldBe(1);
    }


    [Fact]
    public void AnImplicitStyleDoesNotKillTheConstructor()
    {
        // MAUI applies an implicit style from StyleableElement's own constructor, before a single
        // line of the derived control's body has run. Every property callback here is behind
        // StyleGuard for that reason, and this is the test that proves it.
        var app = new Application();
        app.Resources.Add(new Style(typeof(KanbanView))
        {
            Setters =
            {
                new Setter { Property = KanbanView.ColumnWidthProperty, Value = 320d },
                new Setter { Property = KanbanView.SwimlaneModeProperty, Value = KanbanSwimlaneMode.Grouped },
                new Setter { Property = KanbanView.ShowWipLimitsProperty, Value = false }
            }
        });

        var view = Should.NotThrow(() => new KanbanView());

        view.ColumnWidth.ShouldBe(320d);
        view.SwimlaneMode.ShouldBe(KanbanSwimlaneMode.Grouped);
    }


    [Fact]
    public void AddingACardToTheCollectionRebuildsTheBoard()
    {
        var view = Build(out var cards, out _);

        cards.Add(new KanbanCard { Id = "d", Title = "D", ColumnId = "done" });

        view.Board.Lane("done", null)!.Cards.Select(c => c.Id).ShouldBe(["d"]);
    }


    [Fact]
    public void ChangingACardsColumnFromCodeRebuildsTheBoard()
    {
        var view = Build(out var cards, out _);

        cards[0].ColumnId = "done";

        view.Board.Lane("todo", null)!.Cards.Select(c => c.Id).ShouldBe(["b"]);
        view.Board.Lane("done", null)!.Cards.Select(c => c.Id).ShouldBe(["a"]);
    }


    [Fact]
    public void AddingAColumnRebuildsTheBoard()
    {
        var view = Build(out _, out var columns);

        columns.Add(new KanbanColumn { Id = "blocked", Title = "Blocked" });

        view.Board.Columns.Count.ShouldBe(4);
        view.Board.Lane("blocked", null).ShouldNotBeNull();
    }


    [Fact]
    public void SwitchingToGroupedModeRebucketsIntoSwimlanes()
    {
        var view = Build(out var cards, out _);

        cards[0].SwimlaneId = "team-b";
        view.Swimlanes = new ObservableCollection<KanbanSwimlane>
        {
            new() { Id = "team-a", Title = "Team A" },
            new() { Id = "team-b", Title = "Team B" }
        };
        view.SwimlaneMode = KanbanSwimlaneMode.Grouped;

        view.Board.Lane("todo", "team-b")!.Cards.Select(c => c.Id).ShouldBe(["a"]);
        view.Board.Lane("todo", "team-a")!.Cards.Select(c => c.Id).ShouldBe(["b"]);
    }


    // =============================================================================================
    // Moving
    // =============================================================================================

    [Fact]
    public void MoveCardRunsTheSameEventsADragDoes()
    {
        var view = Build(out var cards, out _);

        KanbanMove? moving = null;
        KanbanMove? moved = null;
        view.CardMoving += (_, e) => moving = e.Move;
        view.CardMoved += (_, e) => moved = e.Move;

        view.MoveCard(cards[0], "done").ShouldBeTrue();

        moving.ShouldNotBeNull();
        moved.ShouldNotBeNull();
        cards[0].ColumnId.ShouldBe("done");
    }


    [Fact]
    public void CancellingCardMovingLeavesTheCardWhereItWas()
    {
        var view = Build(out var cards, out _);

        view.CardMoving += (_, e) => e.Cancel = true;
        var moved = false;
        view.CardMoved += (_, _) => moved = true;

        view.MoveCard(cards[0], "done").ShouldBeFalse();

        cards[0].ColumnId.ShouldBe("todo");
        moved.ShouldBeFalse();
    }


    [Fact]
    public void ABlockedWipLimitRaisesDropRejectedRatherThanMoving()
    {
        var view = Build(out var cards, out _);
        view.WipBehavior = KanbanWipBehavior.Block;

        KanbanDropRejectedEventArgs? rejected = null;
        view.DropRejected += (_, e) => rejected = e;

        view.MoveCard(cards[0], "doing").ShouldBeFalse();

        rejected.ShouldNotBeNull();
        rejected.Reason.ShouldBe(KanbanDropRejection.WipLimitReached);
        rejected.Column!.Id.ShouldBe("doing");
        cards[0].ColumnId.ShouldBe("todo");
    }


    [Fact]
    public void WarnLetsTheSameMoveThrough()
    {
        var view = Build(out var cards, out _);
        view.WipBehavior = KanbanWipBehavior.Warn;

        view.MoveCard(cards[0], "doing").ShouldBeTrue();

        view.Board.CountIn("doing").ShouldBe(2);
        view.Board.IsOverLimit("doing").ShouldBeTrue();
    }


    [Fact]
    public void ALockedCardIsRefusedAndSaysWhy()
    {
        var view = Build(out var cards, out _);
        cards[0].IsLocked = true;

        KanbanDropRejection? reason = null;
        view.DropRejected += (_, e) => reason = e.Reason;

        view.MoveCard(cards[0], "done").ShouldBeFalse();
        reason.ShouldBe(KanbanDropRejection.CardLocked);
    }


    // =============================================================================================
    // Collapsing
    // =============================================================================================

    [Fact]
    public void TogglingAColumnFlipsItAndRaises()
    {
        var view = Build(out _, out var columns);

        KanbanColumn? raised = null;
        view.ColumnCollapseChanged += (_, e) => raised = e.Column;

        view.ToggleColumn(columns[0]);

        columns[0].IsCollapsed.ShouldBeTrue();
        raised.ShouldBe(columns[0]);
    }


    [Fact]
    public void CollapseAllColumnsCollapsesEveryOne()
    {
        var view = Build(out _, out var columns);

        view.CollapseAllColumns();
        columns.ShouldAllBe(c => c.IsCollapsed);

        view.ExpandAllColumns();
        columns.ShouldAllBe(c => !c.IsCollapsed);
    }


    [Fact]
    public void ACollapsedColumnStillCountsAndStillTakesDrops()
    {
        // Collapsing is a display decision. A collapsed column that stopped counting would let a
        // board quietly exceed its own WIP limit by folding the column away.
        var view = Build(out var cards, out var columns);
        columns[1].IsCollapsed = true;

        view.Board.CountIn("doing").ShouldBe(1);
        view.Board.Evaluate(cards[0], "doing", null).Allowed.ShouldBeTrue();
    }


    // =============================================================================================
    // Add card
    // =============================================================================================

    [Fact]
    public void TheBoardNeverInventsACardOfItsOwn()
    {
        var view = Build(out var cards, out _);
        view.AddCardMode = KanbanAddCardMode.Button;

        // Nothing has been added: AddCardRequested is the whole contract, and a board that created
        // a blank card would leave the consumer deleting it again on cancel.
        cards.Count.ShouldBe(3);
        view.Board.Lanes.SelectMany(l => l.Cards).Count().ShouldBe(3);
    }


    [Fact]
    public void SelectingACardSetsSelectedCard()
    {
        var view = Build(out var cards, out _);

        view.SelectedCard = cards[1];

        view.SelectedCard.ShouldBe(cards[1]);
    }


    [Fact]
    public void DisposingStopsObservingTheCollections()
    {
        var view = Build(out var cards, out _);
        view.Dispose();

        Should.NotThrow(() => cards.Add(new KanbanCard { Id = "z", ColumnId = "todo" }));
        view.Board.Lane("todo", null)!.Cards.Count.ShouldBe(2);
    }
}
