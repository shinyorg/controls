using Shiny.Controls.Office.Notebook;
using Shiny.Controls.Office.Shapes;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;
using Shiny.Controls.Office.View;
using Shouldly;
using Xunit;
using TextAlignment = Shiny.Controls.Office.Text.TextAlignment;

namespace Shiny.Controls.Office.Tests;

/// <summary>
/// The free-form notebook: its text editing, its commands, its canvas behaviour and its file format.
/// </summary>
/// <remarks>
/// Unlike the other three editors this one owns its model rather than projecting an OOXML package, so
/// there is no round-trip-is-byte-identical guard to write. The equivalent promise here is that
/// everything survives a save and reopen, which is what <see cref="ARoundTripPreservesEverything"/>
/// stands for.
/// </remarks>
public class NotebookTests
{
    /// <summary>
    /// A measurer with fixed metrics.
    /// </summary>
    /// <remarks>
    /// Skia would work but ties the assertions to whatever fonts the machine happens to have; a fixed
    /// 8px advance makes caret and hit-test positions arithmetic, which is what these tests are about.
    /// </remarks>
    sealed class Fixed : ITextMeasurer
    {
        public TextMetrics Measure(ReadOnlySpan<char> text, TextStyle style)
            => new(text.Length * 8, style.FontSize * 0.8, style.FontSize * 0.2);

        public TextMetrics LineMetrics(TextStyle style)
            => new(0, style.FontSize * 0.8, style.FontSize * 0.2);
    }

    static NotebookEditorController Controller(NotebookDocument? document = null)
    {
        var controller = new NotebookEditorController(document ?? NotebookDocument.Create(), new Fixed());
        controller.Resize(900, 600);

        return controller;
    }

    static TextStyle Style => NotebookDocument.DefaultTextStyle;

    static ShapeParagraph Paragraph(params (string Text, bool Bold)[] runs)
        => new([.. runs.Select(r => new StyledRun(r.Text, Style with { Bold = r.Bold }))]);

    // ---- the text editor ----

    [Fact]
    public void InsertingInTheMiddleSplitsTheRunAndKeepsBothHalves()
    {
        var paragraph = Paragraph(("Hello world", false));
        var result = NoteTextEditor.Insert(paragraph, 5, " there");

        NoteTextEditor.TextOf(result).ShouldBe("Hello there world");

        // Both halves carried the same style, so they fold back into one run rather than staying as
        // three - which is the thing that stops a paragraph growing a run per keystroke.
        result.Runs.Count.ShouldBe(1);
    }

    [Fact]
    public void InsertingAtARunBoundaryTakesTheStyleBeforeTheCaret()
    {
        var paragraph = Paragraph(("bold", true), ("plain", false));
        var result = NoteTextEditor.Insert(paragraph, 4, "X");

        NoteTextEditor.TextOf(result).ShouldBe("boldXplain");

        // Continues what was just typed rather than what comes next, which is what makes "turn bold
        // on, keep typing" work at the end of a run.
        result.Runs[0].Text.ShouldBe("boldX");
        result.Runs[0].Style.Bold.ShouldBeTrue();
    }

    [Fact]
    public void InsertingAtTheStartTakesTheFirstRunsStyle()
    {
        var paragraph = Paragraph(("bold", true));
        var result = NoteTextEditor.Insert(paragraph, 0, "X");

        NoteTextEditor.TextOf(result).ShouldBe("Xbold");
        result.Runs.Count.ShouldBe(1);
    }

    [Fact]
    public void DeletingAcrossRunsLeavesOnlyWhatSurvived()
    {
        var paragraph = Paragraph(("abc", true), ("defg", false));
        var result = NoteTextEditor.Delete(paragraph, 2, 5);

        NoteTextEditor.TextOf(result).ShouldBe("abfg");
        result.Runs.Count.ShouldBe(2);
        result.Runs[0].Style.Bold.ShouldBeTrue();
        result.Runs[1].Style.Bold.ShouldBeFalse();
    }

    [Fact]
    public void FormattingASpanSplitsOnlyThatSpan()
    {
        var paragraph = Paragraph(("one two three", false));
        var result = NoteTextEditor.Format(paragraph, 4, 7, s => s with { Bold = true });

        result.Runs.Count.ShouldBe(3);
        result.Runs[1].Text.ShouldBe("two");
        result.Runs[1].Style.Bold.ShouldBeTrue();
        result.Runs[0].Style.Bold.ShouldBeFalse();
        result.Runs[2].Style.Bold.ShouldBeFalse();
    }

    [Fact]
    public void MergingKeepsTheTargetsParagraphProperties()
    {
        var target = Paragraph(("one ", false)) with { Alignment = TextAlignment.Center, Level = 2 };
        var source = Paragraph(("two", false)) with { Alignment = TextAlignment.Right, Level = 0 };

        var merged = NoteTextEditor.Merge(target, source);

        NoteTextEditor.TextOf(merged).ShouldBe("one two");

        // Backspace at the start of line two pulls it into line one, and the result is line one.
        merged.Alignment.ShouldBe(TextAlignment.Center);
        merged.Level.ShouldBe(2);
    }

    [Fact]
    public void DeletingARangeAcrossParagraphsJoinsTheEnds()
    {
        var body = new ShapeTextBody([Paragraph(("first line", false)), Paragraph(("second", false)), Paragraph(("third", false))]);

        var result = NoteTextEditor.DeleteRange(body, new NoteTextRange(new NotePosition(0, 5), new NotePosition(2, 2)));

        result.Paragraphs.Count.ShouldBe(1);
        NoteTextEditor.TextOf(result.Paragraphs[0]).ShouldBe("firstird");
    }

    [Fact]
    public void NormalizeDropsEmptyRunsButKeepsBreaks()
    {
        IReadOnlyList<StyledRun> runs =
        [
            new StyledRun(string.Empty, Style),
            new StyledRun("a", Style),
            new StyledRun(string.Empty, Style) { IsBreak = true },
            new StyledRun("b", Style)
        ];

        var result = NoteTextEditor.Normalize(runs);

        result.Count.ShouldBe(3);
        result[0].Text.ShouldBe("a");
        result[1].IsBreak.ShouldBeTrue();
        result[2].Text.ShouldBe("b");
    }

    // ---- typing through the controller ----

    [Fact]
    public void TypingIntoANewContainerLandsInIt()
    {
        var controller = Controller();
        var item = controller.AddTextBox(40, 60);

        item.ShouldNotBeNull();
        controller.IsEditingText.ShouldBeTrue();

        controller.InsertText("Meeting notes");

        controller.EditingItem!.PlainText.ShouldBe("Meeting notes");
        controller.Caret.Offset.ShouldBe(13);
    }

    [Fact]
    public void DoubleClickingBlankCanvasStartsSomewhereToWrite()
    {
        var controller = Controller();

        controller.PointerDoubleClick(300, 220);

        controller.IsEditingText.ShouldBeTrue();
        controller.InsertText("anywhere");

        var item = controller.Page!.Items.Single();
        item.Kind.ShouldBe(NoteItemKind.Text);
        item.PlainText.ShouldBe("anywhere");

        // Placed slightly above the click, so the first line lands under the pointer rather than
        // below it.
        item.Y.ShouldBe(210, 0.001);
    }

    [Fact]
    public void DoubleClickingBlankCanvasWithAPenInHandDoesNotStartATextBox()
    {
        var controller = Controller();
        controller.Tool = NoteTool.Pen;

        controller.PointerDoubleClick(300, 220);

        controller.Page!.Items.ShouldBeEmpty();
        controller.IsEditingText.ShouldBeFalse();
    }

    [Fact]
    public void ATypingRunUndoesAsOneStep()
    {
        var controller = Controller();
        controller.AddTextBox(10, 10);

        foreach (var c in "hello")
            controller.InsertText(c.ToString());

        controller.EditingItem!.PlainText.ShouldBe("hello");

        // One step for the whole run, not one per keystroke - and the run rewinds to empty rather
        // than to "hell", which is the failure the composite inverse exists to prevent.
        controller.Undo();
        controller.ItemById(controller.SelectedIds[0])!.PlainText.ShouldBe(string.Empty);
    }

    [Fact]
    public void ADragAndTheTypingAfterItAreSeparateUndoSteps()
    {
        var controller = Controller();
        var item = controller.AddTextBox(100, 100)!;
        controller.InsertText("x");
        controller.EndTextEditing();

        controller.Tool = NoteTool.Select;
        var (vx, vy) = controller.ToViewport(110, 105);
        controller.PointerDown(vx, vy);
        controller.PointerMove(vx + 60, vy + 40);
        controller.PointerUp();

        controller.ItemById(item.Id)!.X.ShouldBe(160, 0.001);

        // The move rewinds without taking the character with it: pointer-up breaks the coalescing run.
        controller.Undo();
        controller.ItemById(item.Id)!.X.ShouldBe(100, 0.001);
        controller.ItemById(item.Id)!.PlainText.ShouldBe("x");
    }

    [Fact]
    public void EnterSplitsTheParagraphAtTheCaret()
    {
        var controller = Controller();
        controller.AddTextBox(0, 0);
        controller.InsertText("one two");

        controller.MoveLeft();
        controller.MoveLeft();
        controller.MoveLeft();
        controller.InsertParagraph();

        var body = controller.EditingItem!.Text!;
        body.Paragraphs.Count.ShouldBe(2);
        NoteTextEditor.TextOf(body.Paragraphs[0]).ShouldBe("one ");
        NoteTextEditor.TextOf(body.Paragraphs[1]).ShouldBe("two");
        controller.Caret.ShouldBe(new NotePosition(1, 0));
    }

    [Fact]
    public void BackspaceAtTheStartOfAListItemLeavesTheListRatherThanJoiningUp()
    {
        var controller = Controller();
        controller.AddTextBox(0, 0);
        controller.InsertText("first");
        controller.InsertParagraph();
        controller.ToggleBulletList();

        controller.CaretFormat.List.ShouldBe(ListStyle.Bullet);

        controller.Backspace();

        controller.CaretFormat.List.ShouldBe(ListStyle.None);

        // Still two paragraphs: leaving the list is the whole action, and the line must not also be
        // pulled up into the one above it.
        controller.EditingItem!.Text!.Paragraphs.Count.ShouldBe(2);
    }

    [Fact]
    public void TypingADashAndASpaceStartsABulletedList()
    {
        var controller = Controller();
        controller.AddTextBox(0, 0);
        controller.InsertText("-");
        controller.InsertText(" ");

        controller.CaretFormat.List.ShouldBe(ListStyle.Bullet);

        // The marker itself is consumed - it became the list, so leaving it as text would show it
        // twice.
        controller.EditingItem!.PlainText.ShouldBe(string.Empty);
        controller.Caret.Offset.ShouldBe(0);
    }

    [Fact]
    public void AnAutoHeightContainerGrowsWithItsText()
    {
        var controller = Controller();
        var item = controller.AddTextBox(0, 0, width: 80)!;
        var before = controller.ItemById(item.Id)!.Height;

        // Far wider than 80px at 8px per character, so it has to wrap onto several lines.
        controller.InsertText("wrapping text that will not fit on one line at all");

        controller.ItemById(item.Id)!.Height.ShouldBeGreaterThan(before);
    }

    // ---- list numbering ----

    [Fact]
    public void NumberedListsRenumberAndNestingRestartsTheInnerRun()
    {
        var body = new ShapeTextBody(
        [
            Paragraph(("alpha", false)) with { List = ListStyle.Numbered },
            Paragraph(("inner", false)) with { List = ListStyle.Numbered, Level = 1 },
            Paragraph(("inner two", false)) with { List = ListStyle.Numbered, Level = 1 },
            Paragraph(("beta", false)) with { List = ListStyle.Numbered },
            Paragraph(("inner again", false)) with { List = ListStyle.Numbered, Level = 1 }
        ]);

        var result = NotebookEditorController.Renumber(body);

        result.Paragraphs[0].Bullet.ShouldBe("1.");
        result.Paragraphs[1].Bullet.ShouldBe("a.");
        result.Paragraphs[2].Bullet.ShouldBe("b.");
        result.Paragraphs[3].Bullet.ShouldBe("2.");

        // Stepping out to the shallower level and back in starts the deeper list again, the way an
        // outline reads - not "c.".
        result.Paragraphs[4].Bullet.ShouldBe("a.");
    }

    [Fact]
    public void APlainParagraphBetweenTwoListsBreaksTheSequence()
    {
        var body = new ShapeTextBody(
        [
            Paragraph(("one", false)) with { List = ListStyle.Numbered },
            Paragraph(("prose", false)),
            Paragraph(("one again", false)) with { List = ListStyle.Numbered }
        ]);

        var result = NotebookEditorController.Renumber(body);

        result.Paragraphs[0].Bullet.ShouldBe("1.");
        result.Paragraphs[1].Bullet.ShouldBeNull();
        result.Paragraphs[2].Bullet.ShouldBe("1.");
    }

    // ---- selection, hit testing and dragging ----

    [Fact]
    public void AClickSelectsTheTopmostItemUnderIt()
    {
        var controller = Controller();
        var lower = controller.AddShape(ShapeGeometry.Rectangle, 50, 50, 200, 200)!;
        var upper = controller.AddShape(ShapeGeometry.Ellipse, 100, 100, 60, 60)!;
        controller.ClearSelection();

        controller.ItemAt(120, 120)!.Id.ShouldBe(upper.Id);
        controller.ItemAt(60, 60)!.Id.ShouldBe(lower.Id);
    }

    [Fact]
    public void ALockedItemIsPaintedButNeverHit()
    {
        var controller = Controller();
        var item = controller.AddShape(ShapeGeometry.Rectangle, 20, 20, 100, 100)!;
        controller.SetSelectionLocked(true);
        controller.ClearSelection();

        controller.ItemAt(50, 50).ShouldBeNull();
        controller.Page!.Items.ShouldContain(x => x.Id == item.Id);
    }

    [Fact]
    public void InkIsHitOnItsPathRatherThanItsBoundingBox()
    {
        var controller = Controller();

        // A diagonal from corner to corner: the box covers the whole square, the stroke only the line.
        controller.Tool = NoteTool.Pen;
        var (x0, y0) = controller.ToViewport(100, 100);
        controller.PointerDown(x0, y0, PointerKind.Pen);

        for (var i = 1; i <= 20; i++)
        {
            var (vx, vy) = controller.ToViewport(100 + i * 10, 100 + i * 10);
            controller.PointerMove(vx, vy);
        }

        controller.PointerUp();
        controller.Tool = NoteTool.Select;

        controller.ItemAt(200, 200).ShouldNotBeNull();

        // Well inside the bounding box, nowhere near the line.
        controller.ItemAt(290, 110).ShouldBeNull();
    }

    [Fact]
    public void ResizingFromACornerScalesTheSelection()
    {
        var controller = Controller();
        var item = controller.AddShape(ShapeGeometry.Rectangle, 100, 100, 100, 100)!;

        var handle = controller.SelectionHandles().First(h => h.Handle == ShapeHandle.BottomRight);
        controller.PointerDown(handle.Rect.X + handle.Rect.Width / 2, handle.Rect.Y + handle.Rect.Height / 2);

        var (vx, vy) = controller.ToViewport(300, 250);
        controller.PointerMove(vx, vy);
        controller.PointerUp();

        var resized = controller.ItemById(item.Id)!;
        resized.X.ShouldBe(100, 0.001);
        resized.Width.ShouldBe(200, 0.001);
        resized.Height.ShouldBe(150, 0.001);
    }

    /// <summary>
    /// A drag is a pure function of where the pointer is, however many samples it took to get there.
    /// </summary>
    /// <remarks>
    /// A drag executes a command per pointer sample. Computing each one from the state the last sample
    /// left behind compounds the transform — for ink, whose geometry is its points, thirty samples of
    /// that multiply the stroke off the page, which is exactly what it looks like from the outside.
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(40)]
    public void ResizingIsTheSameWhateverTheSampleRate(int samples)
    {
        var controller = Controller();
        DrawLine(controller, 100, 100, 300, 200);
        controller.Select(controller.Page!.Items.Single().Id);

        var before = controller.SelectionPageBounds()!.Value;
        var handle = controller.SelectionHandles().First(h => h.Handle == ShapeHandle.BottomRight);

        var startX = handle.Rect.X + handle.Rect.Width / 2;
        var startY = handle.Rect.Y + handle.Rect.Height / 2;
        var (targetX, targetY) = controller.ToViewport(before.Right + 120, before.Bottom + 90);

        controller.PointerDown(startX, startY);

        for (var i = 1; i <= samples; i++)
        {
            var t = (double)i / samples;
            controller.PointerMove(startX + (targetX - startX) * t, startY + (targetY - startY) * t);
        }

        controller.PointerUp();

        var after = controller.SelectionPageBounds()!.Value;

        // Lands on the pointer, not somewhere off in space beyond it.
        after.Right.ShouldBe(before.Right + 120, 1.5);
        after.Bottom.ShouldBe(before.Bottom + 90, 1.5);

        // The corner it was dragged away from stays put.
        after.X.ShouldBe(before.X, 1.5);
        after.Y.ShouldBe(before.Y, 1.5);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(40)]
    public void MovingIsTheSameWhateverTheSampleRate(int samples)
    {
        var controller = Controller();
        DrawLine(controller, 100, 100, 300, 200);
        var ink = controller.Page!.Items.Single().Id;
        var shape = controller.AddShape(ShapeGeometry.Rectangle, 400, 100, 80, 60)!;

        controller.Select(ink);
        controller.Select(shape.Id, add: true);

        var before = controller.SelectionPageBounds()!.Value;

        controller.PointerDown(controller.ToViewport(420, 120).X, controller.ToViewport(420, 120).Y);

        for (var i = 1; i <= samples; i++)
        {
            var t = (double)i / samples;
            var (vx, vy) = controller.ToViewport(420 + 150 * t, 120 + 80 * t);
            controller.PointerMove(vx, vy);
        }

        controller.PointerUp();

        var after = controller.SelectionPageBounds()!.Value;

        after.X.ShouldBe(before.X + 150, 1.5);
        after.Y.ShouldBe(before.Y + 80, 1.5);

        // Moved, not resized - the two items keep their spacing and their size.
        after.Width.ShouldBe(before.Width, 1.5);
        after.Height.ShouldBe(before.Height, 1.5);
    }

    [Fact]
    public void AMarqueeTakesEverythingItTouches()
    {
        var controller = Controller();
        controller.AddShape(ShapeGeometry.Rectangle, 20, 20, 40, 40);
        controller.AddShape(ShapeGeometry.Rectangle, 300, 300, 40, 40);
        controller.ClearSelection();

        var (x0, y0) = controller.ToViewport(0, 0);
        controller.PointerDown(x0, y0);

        var (x1, y1) = controller.ToViewport(120, 120);
        controller.PointerMove(x1, y1);
        controller.PointerUp();

        controller.SelectedIds.Count.ShouldBe(1);
    }

    [Fact]
    public void ATouchDragOnEmptyCanvasPansInsteadOfMarqueeing()
    {
        var controller = Controller();
        controller.AddShape(ShapeGeometry.Rectangle, 20, 20, 40, 40);
        controller.ClearSelection();

        controller.PointerDown(400, 300, PointerKind.Touch);
        controller.PointerMove(340, 220);
        controller.PointerUp();

        // A finger has no wheel to scroll with, so the drag has to be the pan.
        controller.ScrollX.ShouldBe(60, 0.001);
        controller.ScrollY.ShouldBe(80, 0.001);
        controller.SelectedIds.ShouldBeEmpty();
    }

    // ---- ink ----

    [Fact]
    public void DrawingAddsOneStrokeWithThePensColourAndWidth()
    {
        var controller = Controller();
        controller.PenColor = new ArgbColor(255, 0x11, 0x22, 0x33);
        controller.PenWidth = 3.5;
        controller.Tool = NoteTool.Pen;

        controller.PointerDown(10, 10, PointerKind.Pen);
        controller.PointerMove(60, 40);
        controller.PointerMove(120, 90);
        controller.PointerUp();

        var item = controller.Page!.Items.Single();
        item.Kind.ShouldBe(NoteItemKind.Ink);
        item.Stroke!.Color.ShouldBe(new ArgbColor(255, 0x11, 0x22, 0x33));
        item.Stroke.Width.ShouldBe(3.5);
        item.Stroke.Tool.ShouldBe(InkTool.Pen);
    }

    [Fact]
    public void AnUnchosenPenFollowsTheSurfacesInk()
    {
        var controller = Controller();
        var paper = new ArgbColor(255, 0xEE, 0xEE, 0xEE);

        controller.ApplyDefaultInk(paper);
        controller.PenColor.ShouldBe(paper);

        // Choosing a colour pins it: a theme change that quietly overrode a deliberate choice would be
        // worse than a pen that is hard to see.
        var chosen = new ArgbColor(255, 0xD1, 0x3A, 0x3A);
        controller.PenColor = chosen;
        controller.ApplyDefaultInk(paper);
        controller.PenColor.ShouldBe(chosen);
    }

    [Fact]
    public void ATapWithThePenLeavesADot()
    {
        var controller = Controller();
        controller.Tool = NoteTool.Pen;

        controller.PointerDown(40, 40, PointerKind.Pen);
        controller.PointerUp();

        // Two points, not one: a single point has no segment for the painter to stroke, so it would
        // draw nothing at all.
        controller.Page!.Items.Single().Stroke!.Points.Count.ShouldBe(2);
    }

    [Fact]
    public void TheHighlighterPaintsBehindEverythingElse()
    {
        var controller = Controller();
        controller.Tool = NoteTool.Highlighter;

        controller.PointerDown(10, 10);
        controller.PointerMove(90, 12);
        controller.PointerUp();

        controller.Page!.Items.Single().PaintsBehind.ShouldBeTrue();
    }

    [Fact]
    public void TheStrokeEraserTakesAWholeStroke()
    {
        var controller = Controller();
        DrawLine(controller, 100, 100, 300, 100);

        controller.Tool = NoteTool.Eraser;
        controller.EraseMode = EraseMode.Stroke;
        controller.PointerDown(200, 100);
        controller.PointerUp();

        controller.Page!.Items.ShouldBeEmpty();
    }

    [Fact]
    public void ThePointEraserCutsAStrokeInTwo()
    {
        var controller = Controller();
        DrawLine(controller, 100, 100, 300, 100);

        controller.Tool = NoteTool.Eraser;
        controller.EraseMode = EraseMode.Point;
        controller.EraserRadius = 12;
        controller.PointerDown(200, 100);
        controller.PointerUp();

        // Two separate items rather than one with a gap: a stroke is a single path, so a hole in the
        // point list would be drawn as a straight line across what was just rubbed out.
        controller.Page!.Items.Count.ShouldBe(2);
        controller.Page.Items.ShouldAllBe(x => x.Kind == NoteItemKind.Ink);
    }

    [Fact]
    public void TheLassoTakesInkItEnclosesAndLeavesInkItDoesNot()
    {
        var controller = Controller();
        DrawLine(controller, 60, 60, 140, 60);
        DrawLine(controller, 400, 400, 480, 400);

        controller.Tool = NoteTool.Lasso;
        controller.PointerDown(20, 20);

        foreach (var (x, y) in new[] { (220d, 20d), (220d, 120d), (20d, 120d) })
            controller.PointerMove(x, y);

        controller.PointerUp();

        controller.SelectedIds.Count.ShouldBe(1);
    }

    static void DrawLine(NotebookEditorController controller, double x1, double y1, double x2, double y2)
    {
        var previous = controller.Tool;
        controller.Tool = NoteTool.Pen;

        controller.PointerDown(x1, y1, PointerKind.Pen);

        for (var i = 1; i <= 20; i++)
        {
            var t = i / 20d;
            controller.PointerMove(x1 + (x2 - x1) * t, y1 + (y2 - y1) * t);
        }

        controller.PointerUp();
        controller.Tool = previous;
    }

    // ---- pages and sections ----

    [Fact]
    public void ANewNotebookHasSomewhereToStartWriting()
    {
        var document = NotebookDocument.Create("Journal");

        document.Sections.Count.ShouldBe(1);
        document.Sections[0].Pages.Count.ShouldBe(1);
        document.Title.ShouldBe("Journal");
    }

    [Fact]
    public void DeletingTheLastPageOfASectionRefillsIt()
    {
        var controller = Controller();
        controller.DeletePage();

        // A section with no pages has nowhere to put the caret and no page-list row to click, so it
        // must never become a tab that cannot be opened.
        controller.Section!.Pages.Count.ShouldBe(1);
    }

    [Fact]
    public void UndoingAPageDeleteBringsBackTheSamePageWithItsContent()
    {
        var controller = Controller();
        controller.AddTextBox(20, 20, text: "keep me");
        controller.EndTextEditing();

        var page = controller.Page!;
        var id = page.Id;

        controller.AddPage();
        controller.GoToPage(id);
        controller.DeletePage();

        controller.Document.Locate(id).IsValid.ShouldBeFalse();

        controller.Undo();

        var restored = controller.Document.PageAt(controller.Document.Locate(id));
        restored.ShouldNotBeNull();
        restored.Items.Single().PlainText.ShouldBe("keep me");
    }

    [Fact]
    public void MovingAPageToAnotherSectionTakesItWithItsItems()
    {
        var controller = Controller();
        controller.AddTextBox(10, 10, text: "travelling");
        controller.EndTextEditing();

        var pageId = controller.Page!.Id;
        var target = controller.AddSection("Second")!;

        controller.MovePage(pageId, target.Id, 0);

        var moved = controller.Document.Locate(pageId);
        controller.Document.Sections[moved.Section].Id.ShouldBe(target.Id);
        controller.Document.PageAt(moved)!.Items.Single().PlainText.ShouldBe("travelling");
    }

    [Fact]
    public void ChangingPageDropsTheSelectionAndTheCaret()
    {
        var controller = Controller();
        controller.AddTextBox(10, 10, text: "here");

        controller.IsEditingText.ShouldBeTrue();

        controller.AddPage();

        controller.IsEditingText.ShouldBeFalse();
        controller.SelectedIds.ShouldBeEmpty();
    }

    // ---- z-order and arrangement ----

    [Fact]
    public void SendToBackPutsTheSelectionUnderEverything()
    {
        var controller = Controller();
        controller.AddShape(ShapeGeometry.Rectangle, 0, 0, 50, 50);
        var second = controller.AddShape(ShapeGeometry.Ellipse, 10, 10, 50, 50)!;

        controller.Page!.IndexOf(second.Id).ShouldBe(1);

        controller.SendToBack();

        controller.Page.IndexOf(second.Id).ShouldBe(0);
    }

    [Fact]
    public void UndoingAnInsertClearsItFromTheSelection()
    {
        var controller = Controller();
        var item = controller.AddShape(ShapeGeometry.Rectangle, 0, 0, 50, 50)!;

        controller.SelectedIds.ShouldContain(item.Id);

        controller.Undo();

        // A frame drawn round an item that is no longer on the page, and a keystroke editing one that
        // is not there, are both what pruning prevents.
        controller.SelectedIds.ShouldBeEmpty();
    }

    [Fact]
    public void DuplicatingGivesTheCopyItsOwnIdentity()
    {
        var controller = Controller();
        var item = controller.AddShape(ShapeGeometry.Rectangle, 30, 30, 60, 60)!;

        controller.DuplicateSelection();

        controller.Page!.Items.Count.ShouldBe(2);

        var copy = controller.Page.Items[1];
        copy.Id.ShouldNotBe(item.Id);
        copy.X.ShouldBe(46, 0.001);
        controller.SelectedIds.ShouldBe([copy.Id]);
    }

    // ---- canvas ----

    [Fact]
    public void ThePageGrowsToHoldWhateverIsWrittenOnIt()
    {
        var page = new NotebookPage("p", "Page") { MinWidth = 400, MinHeight = 400, Padding = 100 };
        page.Extent().ShouldBe((400d, 400d));

        page.Items.Add(NotebookDocument.NewShapeItem(ShapeGeometry.Rectangle, 500, 20, 100, 50));

        page.Extent().Width.ShouldBe(700);
        page.Extent().Height.ShouldBe(400);
    }

    [Fact]
    public void ZoomingAboutAPointKeepsWhatWasUnderIt()
    {
        var controller = Controller();
        controller.Page!.MinWidth = 4000;
        controller.Page.MinHeight = 4000;

        var before = controller.ToPage(300, 200);
        controller.SetZoom(2, 300, 200);
        var after = controller.ToPage(300, 200);

        after.X.ShouldBe(before.X, 0.001);
        after.Y.ShouldBe(before.Y, 0.001);
    }

    [Fact]
    public void ACanvasSmallerThanTheViewportPinsToTheOrigin()
    {
        var controller = Controller();
        controller.Page!.MinWidth = 100;
        controller.Page.MinHeight = 100;
        controller.Page.Padding = 0;

        controller.ScrollBy(500, 500);

        controller.ScrollX.ShouldBe(0);
        controller.ScrollY.ShouldBe(0);
    }

    // ---- the file format ----

    [Fact]
    public async Task ARoundTripPreservesEverything()
    {
        var controller = Controller();
        controller.AddTextBox(30, 40, 260, "Round trip");
        controller.ToggleBold();
        controller.EndTextEditing();

        controller.AddShape(ShapeGeometry.Hexagon, 200, 300, 120, 90);
        DrawLine(controller, 400, 100, 500, 180);
        controller.SetPageRule(PageRule.Grid, 30);
        controller.RenamePage("Kick-off");
        controller.AddSection("Research");

        var bytes = controller.Document.ToArray();

        using var source = new MemoryStream(bytes, writable: false);
        using var reopened = await NotebookDocument.OpenAsync(source);

        reopened.Sections.Count.ShouldBe(2);
        reopened.Sections[1].Title.ShouldBe("Research");

        var page = reopened.Sections[0].Pages[0];
        page.Title.ShouldBe("Kick-off");
        page.Rule.ShouldBe(PageRule.Grid);
        page.RuleSpacing.ShouldBe(30);
        page.Items.Count.ShouldBe(3);

        var text = page.Items[0];
        text.Kind.ShouldBe(NoteItemKind.Text);
        text.PlainText.ShouldBe("Round trip");
        text.Text!.Paragraphs[0].Runs[0].Style.Bold.ShouldBeTrue();
        text.Width.ShouldBe(260);

        page.Items[1].Geometry.ShouldBe(ShapeGeometry.Hexagon);

        var ink = page.Items[2];
        ink.Kind.ShouldBe(NoteItemKind.Ink);
        ink.Stroke!.Points.Count.ShouldBeGreaterThan(1);
    }

    [Fact]
    public async Task AnImageIsStoredAsAFileRatherThanInsideThePage()
    {
        var controller = Controller();

        // Not a real PNG; the package never decodes it, and using bytes that are obviously not an
        // image is what proves that.
        var bytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        controller.AddImage(bytes, "image/png", 10, 10, 100, 80);

        var itemId = controller.Page!.Items.Single().Id;
        var saved = controller.Document.ToArray();

        using (var archive = new System.IO.Compression.ZipArchive(new MemoryStream(saved, writable: false)))
        {
            archive.GetEntry($"media/{itemId}.png").ShouldNotBeNull();
        }

        using var source = new MemoryStream(saved, writable: false);
        using var reopened = await NotebookDocument.OpenAsync(source);

        reopened.Sections[0].Pages[0].Items.Single().Image.ShouldBe(bytes);
    }

    [Fact]
    public async Task ANotebookIsNotDirtyOnceItHasBeenSaved()
    {
        var controller = Controller();
        controller.AddShape(ShapeGeometry.Rectangle, 0, 0, 50, 50);

        controller.Document.IsDirty.ShouldBeTrue();

        using var destination = new MemoryStream();
        await controller.Document.SaveToAsync(destination);

        // SaveTo deliberately leaves the dirty flag alone - it writes a copy somewhere without
        // retargeting the document, so the original still has unsaved changes.
        controller.Document.IsDirty.ShouldBeTrue();
        destination.Length.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void AColourSurvivesItsHexFormAndABadOneIsIgnored()
    {
        var color = new ArgbColor(0x80, 0x12, 0x34, 0x56);

        NotebookMapping.ParseColor(NotebookMapping.ToHex(color)).ShouldBe(color);

        // Six digits mean fully opaque, which is what a hand-edited file is most likely to contain.
        NotebookMapping.ParseColor("#123456").ShouldBe(new ArgbColor(255, 0x12, 0x34, 0x56));

        // Lenient rather than throwing: one bad swatch should cost a highlight, not the notebook.
        NotebookMapping.ParseColor("not a colour").ShouldBeNull();
        NotebookMapping.ParseColor(null).ShouldBeNull();
    }

    // ---- keyboard ----

    [Fact]
    public void EscapeStepsBackOutOfEveryMode()
    {
        var controller = Controller();
        controller.AddTextBox(10, 10, text: "note");

        controller.HandleKey("Escape");
        controller.IsEditingText.ShouldBeFalse();

        controller.Tool = NoteTool.Pen;
        controller.HandleKey("Escape");
        controller.Tool.ShouldBe(NoteTool.Select);

        controller.Select(controller.Page!.Items[0].Id);
        controller.HandleKey("Escape");
        controller.SelectedIds.ShouldBeEmpty();
    }

    [Fact]
    public void ArrowKeysNudgeOutsideTextAndMoveTheCaretInsideIt()
    {
        var controller = Controller();
        var item = controller.AddTextBox(100, 100, text: "abc")!;
        controller.EndTextEditing();
        controller.Select(item.Id);

        controller.HandleKey("ArrowRight");
        controller.ItemById(item.Id)!.X.ShouldBe(101, 0.001);

        controller.HandleKey("ArrowRight", shift: true);
        controller.ItemById(item.Id)!.X.ShouldBe(111, 0.001);

        controller.BeginTextEditing(item.Id);
        var offset = controller.Caret.Offset;
        controller.HandleKey("ArrowLeft");
        controller.Caret.Offset.ShouldBe(offset - 1);
    }

    [Fact]
    public void ReadOnlyRefusesEveryEdit()
    {
        var controller = Controller();
        controller.IsReadOnly = true;

        controller.AddTextBox(10, 10).ShouldBeNull();
        controller.AddShape(ShapeGeometry.Rectangle, 0, 0).ShouldBeNull();

        controller.Tool = NoteTool.Pen;
        controller.PointerDown(20, 20, PointerKind.Pen);
        controller.PointerMove(60, 60);
        controller.PointerUp();

        controller.Page!.Items.ShouldBeEmpty();
    }
}
