using System.Collections.ObjectModel;
using Shiny.Blazor.Controls.Kanban;
using Shouldly;
using Xunit;
using Path = System.IO.Path;

namespace Shiny.Blazor.Controls.Tests;

/// <summary>
/// Host-level cover for the Blazor <see cref="KanbanView"/>.
/// </summary>
/// <remarks>
/// The bucketing and the move arithmetic are tested exhaustively against the engine in
/// <c>Shiny.Maui.Controls.Tests.KanbanBoardTests</c>, and the engine is the same file on both hosts,
/// so nothing here re-asserts them. These cover the half that only exists in Blazor: that parameters
/// reach the board, that the markup helpers emit CSS a browser will actually parse, and - the
/// load-bearing one - that the mirrored engine has not drifted from the MAUI copy.
/// </remarks>
public class KanbanViewTests
{
    static KanbanView Build(out ObservableCollection<KanbanCard> cards, out ObservableCollection<KanbanColumn> columns)
    {
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

        var view = new KanbanView { Columns = columns, Cards = cards };
        view.RebuildBoard();
        return view;
    }


    [Fact]
    public void ParametersReachTheBoard()
    {
        var view = Build(out _, out _);

        view.Board.Columns.Count.ShouldBe(3);
        view.Board.Lane("todo", null)!.Cards.Select(c => c.Id).ShouldBe(["a", "b"]);
        view.Board.CountIn("doing").ShouldBe(1);
    }


    [Fact]
    public void GroupedModeBucketsIntoSwimlanes()
    {
        var view = Build(out var cards, out _);
        cards[0].SwimlaneId = "team-b";

        view.Swimlanes =
        [
            new KanbanSwimlane { Id = "team-a", Title = "Team A" },
            new KanbanSwimlane { Id = "team-b", Title = "Team B" }
        ];
        view.SwimlaneMode = KanbanSwimlaneMode.Grouped;
        view.RebuildBoard();

        view.Board.Lane("todo", "team-b")!.Cards.Select(c => c.Id).ShouldBe(["a"]);
        view.Board.Lane("todo", "team-a")!.Cards.Select(c => c.Id).ShouldBe(["b"]);
    }


    [Fact]
    public async Task MoveCardRunsTheSameEventsADropDoes()
    {
        var view = Build(out var cards, out _);

        KanbanMove? moving = null;
        KanbanMove? moved = null;
        view.OnCardMoving = Callback<KanbanCardMovingEventArgs>(e => moving = e.Move);
        view.OnCardMoved = Callback<KanbanCardMovedEventArgs>(e => moved = e.Move);

        (await view.MoveCardAsync(cards[0], "done")).ShouldBeTrue();

        moving.ShouldNotBeNull();
        moved.ShouldNotBeNull();
        cards[0].ColumnId.ShouldBe("done");
    }


    [Fact]
    public async Task CancellingCardMovingLeavesTheCardWhereItWas()
    {
        var view = Build(out var cards, out _);
        view.OnCardMoving = Callback<KanbanCardMovingEventArgs>(e => e.Cancel = true);

        (await view.MoveCardAsync(cards[0], "done")).ShouldBeFalse();
        cards[0].ColumnId.ShouldBe("todo");
    }


    [Fact]
    public async Task ABlockedWipLimitRaisesDropRejected()
    {
        var view = Build(out var cards, out _);
        view.WipBehavior = KanbanWipBehavior.Block;
        view.RebuildBoard();

        KanbanDropRejectedEventArgs? rejected = null;
        view.OnDropRejected = Callback<KanbanDropRejectedEventArgs>(e => rejected = e);

        (await view.MoveCardAsync(cards[0], "doing")).ShouldBeFalse();

        rejected.ShouldNotBeNull();
        rejected.Reason.ShouldBe(KanbanDropRejection.WipLimitReached);
        cards[0].ColumnId.ShouldBe("todo");
    }


    // =============================================================================================
    // The drag handshake
    // =============================================================================================

    [Fact]
    public void DragStartReportsOnlyTheLanesThatWillTakeTheCard()
    {
        var view = Build(out var cards, out var columns);
        columns[2].AllowDrop = false;
        view.WipBehavior = KanbanWipBehavior.Block;
        view.RebuildBoard();

        var allowed = view.OnDragStartJs(cards[0].Id);

        // "done" refuses drops outright and "doing" is full, so only the card's own column is left.
        allowed.ShouldBe(["todo"]);
    }


    [Fact]
    public void DragStartOnAnUnknownCardReportsNothingRatherThanThrowing()
        => Build(out _, out _).OnDragStartJs("nope").ShouldBeEmpty();


    [Fact]
    public async Task ADropCommitsThroughTheSamePathAsMoveCard()
    {
        var view = Build(out var cards, out _);

        await view.OnDropJs(cards[0].Id, "done", "", 0);

        cards[0].ColumnId.ShouldBe("done");
        cards[0].Order.ShouldBe(0);
    }


    [Fact]
    public async Task ADropOnAnUnknownCardIsIgnored()
        => await Should.NotThrowAsync(() => Build(out _, out _).OnDropJs("nope", "done", "", 0));


    // =============================================================================================
    // Markup helpers
    // =============================================================================================

    [Fact]
    public void CountLabelShowsTheLimitWhenThereIsOne()
    {
        var view = Build(out _, out var columns);

        Invoke<string>(view, "CountLabel", columns[1]).ShouldBe("1 / 1");
        Invoke<string>(view, "CountLabel", columns[0]).ShouldBe("2");
    }


    [Fact]
    public void WidthsAreEmittedWithAnInvariantDecimalSeparator()
    {
        // A comma here is not a cosmetic problem: "width:280,5px" is not a length a browser parses,
        // and the column silently collapses for every user in a comma-decimal locale.
        var view = Build(out _, out var columns);
        columns[0].Width = 280.5;
        view.RebuildBoard();

        var style = Invoke<string>(view, "CellStyle", columns[0]);

        style.ShouldContain("280.5px");
        style.ShouldNotContain(",5");
    }


    [Fact]
    public void ACollapsedColumnGetsTheCollapsedWidth()
    {
        var view = Build(out _, out var columns);
        columns[0].IsCollapsed = true;
        view.RebuildBoard();

        Invoke<string>(view, "CellStyle", columns[0]).ShouldContain("52px");
    }


    [Fact]
    public void ContentStyleCarriesTheSpacingsRatherThanTheRoot()
    {
        // They ride on the content element because a caller's splatted style replaces the root's
        // own outright, and would take every custom property with it.
        var style = Invoke<string>(Build(out _, out _), "ContentStyle");

        style.ShouldContain("--shiny-kanban-col-gap:12px");
        style.ShouldContain("--shiny-kanban-card-gap:8px");
        style.ShouldContain("--shiny-kanban-cell-min-h:120px");
    }


    [Fact]
    public void DueDatesAreClassifiedAgainstTheDueSoonWindow()
    {
        var view = Build(out _, out _);

        Invoke<string>(view, "DueClass", DateTimeOffset.Now.AddDays(-1)).ShouldBe("is-overdue");
        Invoke<string>(view, "DueClass", DateTimeOffset.Now.AddHours(6)).ShouldBe("is-soon");
        Invoke<string>(view, "DueClass", DateTimeOffset.Now.AddDays(30)).ShouldBe(string.Empty);
    }


    // =============================================================================================
    // Mirror drift
    // =============================================================================================

    /// <summary>
    /// The engine and the model types are one file each, copied into both hosts. Nothing but this
    /// test stops the copies diverging - and a divergence would not break a build, it would quietly
    /// make a drop behave one way on a phone and another way in a browser.
    /// </summary>
    [Theory]
    [InlineData("KanbanBoard.cs")]
    [InlineData("KanbanCard.cs")]
    [InlineData("KanbanColumn.cs")]
    [InlineData("KanbanSwimlane.cs")]
    [InlineData("KanbanLabel.cs")]
    [InlineData("KanbanEnums.cs")]
    [InlineData("KanbanEventArgs.cs")]
    public void TheMirroredFilesAgreeWithTheMauiOriginals(string file)
    {
        var src = FindSrcRoot();

        var maui = Normalize(File.ReadAllText(Path.Combine(src, "Shiny.Maui.Controls", "Kanban", file)));
        var blazor = Normalize(File.ReadAllText(Path.Combine(src, "Shiny.Blazor.Controls", "Kanban", file)));

        blazor.ShouldBe(maui, $"{file} has drifted between the two hosts.");
    }


    /// <summary>Strips the two things the copies are allowed to differ by: the namespace and the cross-reference.</summary>
    static string Normalize(string source) => source
        .Replace("Shiny.Maui.Controls.Kanban", "$ns")
        .Replace("Shiny.Blazor.Controls.Kanban", "$ns")
        .Replace("Shiny.Maui.Controls/Kanban/", "$path")
        .Replace("Shiny.Blazor.Controls/Kanban/", "$path")
        .Replace("\r\n", "\n");


    static string FindSrcRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src");
            if (Directory.Exists(Path.Combine(candidate, "Shiny.Blazor.Controls")))
                return candidate;

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate src/ from the test output directory.");
    }


    // =============================================================================================
    // Plumbing
    // =============================================================================================

    static Microsoft.AspNetCore.Components.EventCallback<T> Callback<T>(Action<T> handler)
        => Microsoft.AspNetCore.Components.EventCallback.Factory.Create(new object(), handler);

    /// <summary>
    /// Calls one of the component's own markup helpers. They are private because nothing but the
    /// generated render method should call them - which is exactly why they need a test of their own.
    /// </summary>
    static T Invoke<T>(KanbanView view, string name, params object?[] args)
    {
        var method = typeof(KanbanView).GetMethod(
            name,
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public
        ) ?? throw new MissingMethodException(nameof(KanbanView), name);

        return (T)method.Invoke(method.IsStatic ? null : view, args)!;
    }
}
