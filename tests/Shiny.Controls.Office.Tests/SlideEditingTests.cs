using Shiny.Controls.Office.Presentation;
using Shiny.Controls.Office.Shapes;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;
using Shouldly;
using Xunit;
using TextAlignment = Shiny.Controls.Office.Text.TextAlignment;

namespace Shiny.Controls.Office.Tests;

/// <summary>
/// Editing a <c>.pptx</c>: the commands, the undo stack, and the promise that an untouched deck
/// saves byte-identical.
/// </summary>
public class SlideEditingTests
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

    static async Task<SlideDeck> OpenAsync(bool editable = true)
    {
        using var source = new MemoryStream(SlideFixture.Build(), writable: false);
        return await SlideDeck.OpenAsync(source, editable: editable);
    }

    /// <summary>Slide 2's bulleted body placeholder, which is the shape most tests drive.</summary>
    static int BodyShape(SlideDeck deck)
        => deck.Slides[1].Shapes.ToList().FindIndex(x => x.Text?.PlainText.Contains("Top level point") == true);

    static string TextOf(SlideDeck deck, int slide, int shape, int paragraph)
        => deck.Slides[slide].Shapes[shape].Text!.Paragraphs[paragraph].PlainText;

    static SlideEditorController Controller(SlideDeck deck, int slide = 1)
    {
        var controller = new SlideEditorController(deck, new Fixed());
        controller.Resize(960, 540);
        controller.Index = slide;
        return controller;
    }

    // ---- package integrity ----

    [Fact]
    public async Task ADeckOpenedAndSavedWithoutEditingIsByteIdentical()
    {
        var original = SlideFixture.Build();

        using var source = new MemoryStream(original, writable: false);
        using var deck = await SlideDeck.OpenAsync(source, editable: true);

        using var saved = new MemoryStream();
        await deck.SaveToAsync(saved);

        // The whole surgical-edit promise rests on this: merely opening a deck must not rewrite it.
        PackageComparer.Compare(original, saved.ToArray()).IsIdentical.ShouldBeTrue();
    }

    [Fact]
    public async Task AnEditAddsAndRemovesNothingFromThePackage()
    {
        var original = SlideFixture.Build();

        using var source = new MemoryStream(original, writable: false);
        using var deck = await SlideDeck.OpenAsync(source, editable: true);

        var shape = BodyShape(deck);
        deck.Execute(new InsertSlideTextCommand(new SlidePosition(1, shape, 0, 0), "X"));

        using var saved = new MemoryStream();
        await deck.SaveToAsync(saved);

        var diff = PackageComparer.Compare(original, saved.ToArray());

        // Parts the reader materialised are re-serialised by the SDK's only public flush, so their
        // *bytes* may move. What must never happen is a part appearing or vanishing.
        diff.Added.ShouldBeEmpty(diff.ToString());
        diff.Removed.ShouldBeEmpty(diff.ToString());
    }

    [Fact]
    public async Task SavingAnEditLosesNothingAnywhereElseInTheDeck()
    {
        var original = SlideFixture.Build();

        using var before = await SlideDeck.OpenAsync(new MemoryStream(original, writable: false));

        using var editing = await SlideDeck.OpenAsync(new MemoryStream(original, writable: false), editable: true);
        var shape = BodyShape(editing);
        editing.Execute(new InsertSlideTextCommand(new SlidePosition(1, shape, 0, 0), "X"));

        using var saved = new MemoryStream();
        await editing.SaveToAsync(saved);

        using var after = await SlideDeck.OpenAsync(new MemoryStream(saved.ToArray()));

        // The real guarantee, and a stronger one than counting rewritten parts: everything the edit
        // did not touch reads back exactly as it did before - including the slide the user never
        // opened, the notes, the themed fills and the inherited placeholder positions.
        after.Slides.Count.ShouldBe(before.Slides.Count);
        after.SlideWidth.ShouldBe(before.SlideWidth);
        after.SlideHeight.ShouldBe(before.SlideHeight);

        after.Slides[0].Title.ShouldBe(before.Slides[0].Title);
        after.Slides[1].Notes.ShouldBe(before.Slides[1].Notes);

        foreach (var (was, now) in before.Slides[0].Shapes.Zip(after.Slides[0].Shapes))
        {
            now.X.ShouldBe(was.X, 0.01);
            now.Y.ShouldBe(was.Y, 0.01);
            now.Width.ShouldBe(was.Width, 0.01);
            now.Height.ShouldBe(was.Height, 0.01);
            now.Geometry.ShouldBe(was.Geometry);
            now.Fill.Solid.ShouldBe(was.Fill.Solid);
            now.Text?.PlainText.ShouldBe(was.Text?.PlainText);
        }

        // Slide 2 changed only where the edit was.
        after.Slides[1].Shapes[shape].Text!.Paragraphs[0].PlainText
            .ShouldBe("X" + before.Slides[1].Shapes[shape].Text!.Paragraphs[0].PlainText);
    }

    [Fact]
    public async Task AReadOnlyDeckRefusesEdits()
    {
        using var deck = await OpenAsync(editable: false);
        var shape = BodyShape(deck);

        Should.Throw<InvalidOperationException>(
            () => deck.Execute(new InsertSlideTextCommand(new SlidePosition(1, shape, 0, 0), "x")));
    }

    // ---- text edits ----

    [Fact]
    public async Task InsertingTextLandsAtTheOffset()
    {
        using var deck = await OpenAsync();
        var shape = BodyShape(deck);
        var before = TextOf(deck, 1, shape, 0);

        deck.Execute(new InsertSlideTextCommand(new SlidePosition(1, shape, 0, 3), "XYZ"));

        TextOf(deck, 1, shape, 0).ShouldBe(before[..3] + "XYZ" + before[3..]);
    }

    [Fact]
    public async Task DeletingARangeRemovesExactlyThatSpan()
    {
        using var deck = await OpenAsync();
        var shape = BodyShape(deck);
        var before = TextOf(deck, 1, shape, 0);

        deck.Execute(new DeleteSlideRangeCommand(new SlideTextRange(
            new SlidePosition(1, shape, 0, 0),
            new SlidePosition(1, shape, 0, 4))));

        TextOf(deck, 1, shape, 0).ShouldBe(before[4..]);
    }

    [Fact]
    public async Task EveryEditUndoesBackToWhereItStarted()
    {
        using var deck = await OpenAsync();
        var shape = BodyShape(deck);
        var before = TextOf(deck, 1, shape, 0);

        deck.Execute(new InsertSlideTextCommand(new SlidePosition(1, shape, 0, 0), "Hello "));
        TextOf(deck, 1, shape, 0).ShouldNotBe(before);

        deck.Undo.Undo();
        TextOf(deck, 1, shape, 0).ShouldBe(before);

        deck.Undo.Redo();
        TextOf(deck, 1, shape, 0).ShouldStartWith("Hello ");
    }

    [Fact]
    public async Task SplittingAParagraphKeepsItsLevelAndBullet()
    {
        using var deck = await OpenAsync();
        var shape = BodyShape(deck);

        // The second paragraph is the nested one, at level 1.
        var nested = deck.Slides[1].Shapes[shape].Text!.Paragraphs[1];
        nested.Level.ShouldBe(1);

        deck.Execute(new SplitSlideParagraphCommand(new SlidePosition(1, shape, 1, 6)));

        var body = deck.Slides[1].Shapes[shape].Text!;
        body.Paragraphs.Count.ShouldBe(3);

        // Enter inside a bulleted list has to produce another bullet at the same level, not an
        // unformatted paragraph.
        body.Paragraphs[2].Level.ShouldBe(1);
        body.Paragraphs[1].PlainText.ShouldBe("Nested");
        body.Paragraphs[2].PlainText.ShouldBe(" point");
    }

    [Fact]
    public async Task MergingJoinsAParagraphOntoTheOneBefore()
    {
        using var deck = await OpenAsync();
        var shape = BodyShape(deck);

        var first = TextOf(deck, 1, shape, 0);
        var second = TextOf(deck, 1, shape, 1);

        deck.Execute(new MergeSlideParagraphCommand(new SlidePosition(1, shape, 1, 0)));

        var body = deck.Slides[1].Shapes[shape].Text!;
        body.Paragraphs.Count.ShouldBe(1);
        body.Paragraphs[0].PlainText.ShouldBe(first + second);
    }

    [Fact]
    public async Task DeletingAcrossParagraphsJoinsWhatIsLeft()
    {
        using var deck = await OpenAsync();
        var shape = BodyShape(deck);

        deck.Execute(new DeleteSlideRangeCommand(new SlideTextRange(
            new SlidePosition(1, shape, 0, 3),
            new SlidePosition(1, shape, 1, 6))));

        var body = deck.Slides[1].Shapes[shape].Text!;
        body.Paragraphs.Count.ShouldBe(1);
        body.Paragraphs[0].PlainText.ShouldBe("Top point");
    }

    // ---- formatting ----

    [Fact]
    public async Task FormattingASpanSplitsRunsAtTheBoundaries()
    {
        using var deck = await OpenAsync();
        var shape = BodyShape(deck);

        deck.Execute(new FormatSlideRunsCommand(
            new SlideTextRange(new SlidePosition(1, shape, 0, 0), new SlidePosition(1, shape, 0, 3)),
            ShapeTextEditorProbe.Bold(true),
            "Bold"));

        var runs = deck.Slides[1].Shapes[shape].Text!.Paragraphs[0].Runs;

        runs.Count.ShouldBeGreaterThan(1);
        runs[0].Text.ShouldBe("Top");
        runs[0].Style.Bold.ShouldBeTrue();
        runs[1].Style.Bold.ShouldBeFalse();

        // The text itself is unchanged — only where the run boundaries fall.
        string.Concat(runs.Select(x => x.Text)).ShouldBe("Top level point");
    }

    [Fact]
    public async Task FormattingLeavesEveryPropertyItDoesNotTouch()
    {
        using var deck = await OpenAsync();

        // The callout's run is already bold at 18pt; making it italic must not disturb either.
        var shape = deck.Slides[1].Shapes.ToList().FindIndex(x => x.Text?.PlainText == "Callout text");
        shape.ShouldBeGreaterThanOrEqualTo(0);

        var before = deck.Slides[1].Shapes[shape].Text!.Paragraphs[0].Runs[0].Style.FontSize;

        deck.Execute(new FormatSlideRunsCommand(
            new SlideTextRange(new SlidePosition(1, shape, 0, 0), new SlidePosition(1, shape, 0, 7)),
            ShapeTextEditorProbe.Italic(true),
            "Italic"));

        var run = deck.Slides[1].Shapes[shape].Text!.Paragraphs[0].Runs[0];

        run.Style.Italic.ShouldBeTrue();
        run.Style.Bold.ShouldBeTrue();
        run.Style.FontSize.ShouldBe(before, 0.01);
    }

    [Fact]
    public async Task ParagraphFormattingAppliesToEveryParagraphTheRangeTouches()
    {
        using var deck = await OpenAsync();
        var shape = BodyShape(deck);

        deck.Execute(new FormatSlideParagraphsCommand(
            new SlideTextRange(new SlidePosition(1, shape, 0, 0), new SlidePosition(1, shape, 1, 2)),
            ShapeTextEditorProbe.Align(TextAlignment.Center),
            "Alignment"));

        var body = deck.Slides[1].Shapes[shape].Text!;
        body.Paragraphs[0].Alignment.ShouldBe(TextAlignment.Center);
        body.Paragraphs[1].Alignment.ShouldBe(TextAlignment.Center);
    }

    // ---- geometry ----

    [Fact]
    public async Task MovingAShapeWritesItsNewPositionAndUndoesExactly()
    {
        using var deck = await OpenAsync();
        var shape = deck.Slides[1].Shapes.ToList().FindIndex(x => x.Text?.PlainText == "Callout text");

        var before = deck.Slides[1].Shapes[shape];

        deck.Execute(new SetShapeBoundsCommand(1, shape, 100, 200, 300, 80));

        var moved = deck.Slides[1].Shapes[shape];
        moved.X.ShouldBe(100, 0.5);
        moved.Y.ShouldBe(200, 0.5);
        moved.Width.ShouldBe(300, 0.5);
        moved.Height.ShouldBe(80, 0.5);

        deck.Undo.Undo();

        var restored = deck.Slides[1].Shapes[shape];
        restored.X.ShouldBe(before.X, 0.5);
        restored.Y.ShouldBe(before.Y, 0.5);
        restored.Width.ShouldBe(before.Width, 0.5);
        restored.Height.ShouldBe(before.Height, 0.5);
    }

    [Fact]
    public async Task DraggingAPlaceholderGivesItATransformItNeverHad()
    {
        using var deck = await OpenAsync(editable: true);

        // Slide 1's title inherits its whole rectangle from the layout — it has no a:xfrm at all, so
        // moving it has to create one.
        var controller = Controller(deck, slide: 0);
        var title = deck.Slides[0].Shapes.ToList().FindIndex(x => x.Text?.PlainText == "Deck Title");
        title.ShouldBeGreaterThanOrEqualTo(0);

        deck.Execute(new SetShapeBoundsCommand(0, title, 50, 60, 400, 100));

        var moved = deck.Slides[0].Shapes[title];
        moved.X.ShouldBe(50, 0.5);
        moved.Y.ShouldBe(60, 0.5);

        _ = controller;
    }

    [Fact]
    public async Task DeletingAShapeRemovesItAndUndoPutsItBackWhereItWas()
    {
        using var deck = await OpenAsync();
        var before = deck.Slides[1].Shapes.Count;
        var shape = deck.Slides[1].Shapes.ToList().FindIndex(x => x.Text?.PlainText == "Callout text");

        deck.Execute(new DeleteShapeCommand(1, shape));
        deck.Slides[1].Shapes.Count.ShouldBe(before - 1);

        deck.Undo.Undo();

        deck.Slides[1].Shapes.Count.ShouldBe(before);
        deck.Slides[1].Shapes[shape].Text!.PlainText.ShouldBe("Callout text");
    }

    // ---- controller ----

    [Fact]
    public async Task ClickingSelectsTheTopmostShapeUnderThePoint()
    {
        using var deck = await OpenAsync();
        var controller = Controller(deck);

        var callout = deck.Slides[1].Shapes.ToList().FindIndex(x => x.Text?.PlainText == "Callout text");
        var shape = deck.Slides[1].Shapes[callout];

        var point = controller.ToViewport(shape.X + 10, shape.Y + 10)!.Value;
        controller.PointerDown(point.X, point.Y).ShouldBeTrue();

        controller.SelectedShape.ShouldBe(callout);
        controller.IsEditingText.ShouldBeFalse();
    }

    [Fact]
    public async Task LayoutAndMasterShapesCannotBeSelected()
    {
        using var deck = await OpenAsync();
        var controller = Controller(deck);

        // Whatever the click lands on, it is never a shape the slide does not own — dragging one
        // would move it on every slide sharing that layout.
        foreach (var shape in deck.Slides[1].Shapes.Where(x => !x.IsEditable))
        {
            var point = controller.ToViewport(shape.X + 1, shape.Y + 1)!.Value;
            controller.ShapeAt(point.X, point.Y).ShouldNotBe(deck.Slides[1].Shapes.ToList().IndexOf(shape));
        }
    }

    [Fact]
    public async Task TypingGoesIntoTheShapeOnlyAfterEnteringItsText()
    {
        using var deck = await OpenAsync();
        var controller = Controller(deck);
        var body = BodyShape(deck);
        var shape = deck.Slides[1].Shapes[body];
        var before = TextOf(deck, 1, body, 0);

        var point = controller.ToViewport(shape.X + 4, shape.Y + 4)!.Value;

        // Selected but not in text mode: typing must not reach the document.
        controller.PointerDown(point.X, point.Y);
        controller.InsertText("no");
        TextOf(deck, 1, body, 0).ShouldBe(before);

        controller.PointerDoubleClick(point.X, point.Y);
        controller.IsEditingText.ShouldBeTrue();

        controller.MoveCaret(new SlidePosition(1, body, 0, 0));
        controller.InsertText("Yes ");
        TextOf(deck, 1, body, 0).ShouldBe("Yes " + before);
    }

    [Fact]
    public async Task TypedCharactersUndoAsOneStep()
    {
        using var deck = await OpenAsync();
        var controller = Controller(deck);
        var body = BodyShape(deck);
        var before = TextOf(deck, 1, body, 0);

        controller.Select(body);
        controller.BeginTextEditing(0, 0);
        controller.MoveCaret(new SlidePosition(1, body, 0, 0));

        foreach (var c in "abc")
            controller.InsertText(c.ToString());

        TextOf(deck, 1, body, 0).ShouldBe("abc" + before);

        controller.Undo();
        TextOf(deck, 1, body, 0).ShouldBe(before);
    }

    [Fact]
    public async Task BoldWithACaretInsideAWordBoldsTheWord()
    {
        // Held on the end mark instead, the change showed nothing until the user typed at the end of
        // the paragraph, and the button read as dead.
        using var deck = await OpenAsync();
        var controller = Controller(deck);
        var body = BodyShape(deck);
        var text = TextOf(deck, 1, body, 0);
        var end = text.IndexOf(' ');
        end.ShouldBeGreaterThan(1, "precondition: the first paragraph starts with a word");

        controller.Select(body);
        controller.BeginTextEditing(0, 0);
        controller.MoveCaret(new SlidePosition(1, body, 0, 1));
        controller.ToggleBold();

        StyleAt(deck, body, 0).Bold.ShouldBeTrue();
        StyleAt(deck, body, end - 1).Bold.ShouldBeTrue();
        StyleAt(deck, body, end + 1).Bold.ShouldBeFalse("the next word is outside the change");
    }

    static TextStyle StyleAt(SlideDeck deck, int shape, int offset)
    {
        var cursor = 0;
        foreach (var run in deck.Slides[1].Shapes[shape].Text!.Paragraphs[0].Runs)
        {
            if (offset < cursor + run.Text.Length)
                return run.Style;
            cursor += run.Text.Length;
        }

        throw new ArgumentOutOfRangeException(nameof(offset));
    }

    [Fact]
    public async Task BackspaceAtTheStartOfAParagraphJoinsItToTheOneAbove()
    {
        using var deck = await OpenAsync();
        var controller = Controller(deck);
        var body = BodyShape(deck);

        var first = TextOf(deck, 1, body, 0);
        var second = TextOf(deck, 1, body, 1);

        controller.Select(body);
        controller.BeginTextEditing(0, 0);
        controller.MoveCaret(new SlidePosition(1, body, 1, 0));
        controller.Backspace();

        deck.Slides[1].Shapes[body].Text!.Paragraphs.Count.ShouldBe(1);
        TextOf(deck, 1, body, 0).ShouldBe(first + second);

        // The caret belongs at the join, not at the start of the merged paragraph.
        controller.Caret.Paragraph.ShouldBe(0);
        controller.Caret.Offset.ShouldBe(first.Length);
    }

    [Fact]
    public async Task ResizeHandlesSurroundTheSelection()
    {
        using var deck = await OpenAsync();
        var controller = Controller(deck);
        var callout = deck.Slides[1].Shapes.ToList().FindIndex(x => x.Text?.PlainText == "Callout text");

        controller.Select(callout);

        var handles = controller.SelectionHandles().ToList();
        handles.Count.ShouldBe(8);

        var bounds = controller.SelectionBounds()!.Value;
        foreach (var (_, rect) in handles)
        {
            // Every handle is centred on the frame, so it straddles the edge rather than sitting
            // wholly inside or outside it.
            var cx = rect.X + rect.Width / 2;
            var cy = rect.Y + rect.Height / 2;

            (Math.Abs(cx - bounds.X) < 0.01 || Math.Abs(cx - bounds.Right) < 0.01 ||
             Math.Abs(cx - (bounds.X + bounds.Width / 2)) < 0.01).ShouldBeTrue();

            (Math.Abs(cy - bounds.Y) < 0.01 || Math.Abs(cy - bounds.Bottom) < 0.01 ||
             Math.Abs(cy - (bounds.Y + bounds.Height / 2)) < 0.01).ShouldBeTrue();
        }
    }

    [Fact]
    public async Task DraggingAHandleResizesFromTheOppositeEdge()
    {
        using var deck = await OpenAsync();
        var controller = Controller(deck);
        var callout = deck.Slides[1].Shapes.ToList().FindIndex(x => x.Text?.PlainText == "Callout text");

        controller.Select(callout);
        var before = deck.Slides[1].Shapes[callout];

        var handle = controller.SelectionHandles().First(x => x.Handle == ShapeHandle.BottomRight).Rect;
        var startX = handle.X + handle.Width / 2;
        var startY = handle.Y + handle.Height / 2;

        controller.PointerDown(startX, startY).ShouldBeTrue();
        controller.PointerMove(startX + 40 * controller.Scale, startY + 20 * controller.Scale);
        controller.PointerUp();

        var after = deck.Slides[1].Shapes[callout];

        // Bottom-right grows the box; the top-left corner must not move.
        after.X.ShouldBe(before.X, 0.5);
        after.Y.ShouldBe(before.Y, 0.5);
        after.Width.ShouldBe(before.Width + 40, 1.5);
        after.Height.ShouldBe(before.Height + 20, 1.5);
    }

    [Fact]
    public async Task AWholeDragIsOneUndoStep()
    {
        using var deck = await OpenAsync();
        var controller = Controller(deck);
        var callout = deck.Slides[1].Shapes.ToList().FindIndex(x => x.Text?.PlainText == "Callout text");

        controller.Select(callout);
        var before = deck.Slides[1].Shapes[callout];

        var start = controller.ToViewport(before.X + 5, before.Y + 5)!.Value;
        controller.PointerDown(start.X, start.Y);

        // Many pointer samples, as a real drag produces.
        for (var i = 1; i <= 10; i++)
            controller.PointerMove(start.X + i * 3, start.Y + i * 2);

        controller.PointerUp();

        deck.Slides[1].Shapes[callout].X.ShouldNotBe(before.X);

        controller.Undo();

        deck.Slides[1].Shapes[callout].X.ShouldBe(before.X, 0.5);
        deck.Slides[1].Shapes[callout].Y.ShouldBe(before.Y, 0.5);
    }

    [Fact]
    public async Task AddingATextBoxSelectsItAndItCanBeTypedInto()
    {
        using var deck = await OpenAsync();
        var controller = Controller(deck);
        var before = deck.Slides[1].Shapes.Count;

        controller.AddTextBox(120, 140);

        deck.Slides[1].Shapes.Count.ShouldBe(before + 1);
        controller.SelectedShape.ShouldBeGreaterThanOrEqualTo(0);

        controller.BeginTextEditing(0, 0);
        controller.InsertText("Fresh");

        controller.Selection!.Text!.PlainText.ShouldBe("Fresh");
    }

    [Fact]
    public async Task ANewTextBoxSurvivesASaveAndReopen()
    {
        using var deck = await OpenAsync();
        var controller = Controller(deck);

        controller.AddTextBox(120, 140);
        controller.BeginTextEditing(0, 0);
        controller.InsertText("Round trip");

        using var saved = new MemoryStream();
        await deck.SaveToAsync(saved);

        // Written by hand as raw OOXML, so the only real proof it is valid is reading it back.
        using var reopened = await SlideDeck.OpenAsync(new MemoryStream(saved.ToArray()));
        reopened.Slides[1].Shapes.Any(x => x.Text?.PlainText == "Round trip").ShouldBeTrue();
    }

    [Fact]
    public async Task ReadOnlyControllersIgnoreEveryGesture()
    {
        using var deck = await OpenAsync();
        var controller = Controller(deck);
        controller.IsReadOnly = true;

        var callout = deck.Slides[1].Shapes.ToList().FindIndex(x => x.Text?.PlainText == "Callout text");
        var shape = deck.Slides[1].Shapes[callout];
        var point = controller.ToViewport(shape.X + 5, shape.Y + 5)!.Value;

        controller.PointerDown(point.X, point.Y).ShouldBeFalse();
        controller.SelectedShape.ShouldBe(-1);

        controller.PointerDoubleClick(point.X, point.Y);
        controller.IsEditingText.ShouldBeFalse();
    }

    // ---- layout and hit testing ----

    [Fact]
    public async Task ClickingTextPutsTheCaretWhereTheCharacterIs()
    {
        using var deck = await OpenAsync();
        var controller = Controller(deck);
        var body = BodyShape(deck);
        var shape = deck.Slides[1].Shapes[body];

        controller.Select(body);
        controller.BeginTextEditing(0, 0);

        var layout = ShapeTextLayout.Layout(shape.Text!, shape.Width, shape.Height, new Fixed());
        var caret = ShapeTextLayout.CaretAt(layout, 0, 5, new Fixed())!.Value;

        var point = controller.ToViewport(shape.X + caret.X, shape.Y + caret.Y + caret.Height / 2)!.Value;
        var position = controller.TextPositionAt(point.X, point.Y)!.Value;

        position.Paragraph.ShouldBe(0);
        position.Offset.ShouldBe(5);
    }

    [Fact]
    public async Task TheCaretRectangleTracksTheCaret()
    {
        using var deck = await OpenAsync();
        var controller = Controller(deck);
        var body = BodyShape(deck);

        controller.Select(body);
        controller.BeginTextEditing(0, 0);
        controller.MoveCaret(new SlidePosition(1, body, 0, 0));

        var atStart = controller.CaretRect()!.Value;

        controller.MoveCaret(new SlidePosition(1, body, 0, 6));
        var later = controller.CaretRect()!.Value;

        later.X.ShouldBeGreaterThan(atStart.X);
        later.Height.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task SelectingTextProducesHighlightRectangles()
    {
        using var deck = await OpenAsync();
        var controller = Controller(deck);
        var body = BodyShape(deck);

        controller.Select(body);
        controller.BeginTextEditing(0, 0);
        controller.MoveCaret(new SlidePosition(1, body, 0, 0));
        controller.MoveCaret(new SlidePosition(1, body, 0, 3), extend: true);

        var rects = controller.TextSelectionRects().ToList();

        rects.ShouldNotBeEmpty();
        rects.ShouldAllBe(r => r.Width > 0 && r.Height > 0);
    }

    [Fact]
    public async Task NoCaretIsDrawnUntilTextEditingBegins()
    {
        using var deck = await OpenAsync();
        var controller = Controller(deck);

        controller.Select(BodyShape(deck));

        controller.CaretRect().ShouldBeNull();
        controller.TextSelectionRects().ShouldBeEmpty();
    }
}

/// <summary>
/// Reaches the internal run-property mutators from the test assembly.
/// </summary>
/// <remarks>
/// <c>ShapeTextEditor</c> is internal on purpose — it is the editor's plumbing, not API — but the
/// command tests need to hand a mutation in, so this exposes just the three they use.
/// </remarks>
static class ShapeTextEditorProbe
{
    public static Action<DocumentFormat.OpenXml.Drawing.RunProperties> Bold(bool on)
        => properties => properties.Bold = on;

    public static Action<DocumentFormat.OpenXml.Drawing.RunProperties> Italic(bool on)
        => properties => properties.Italic = on;

    public static Action<DocumentFormat.OpenXml.Drawing.ParagraphProperties> Align(TextAlignment alignment)
        => properties => properties.Alignment = alignment switch
        {
            TextAlignment.Center => DocumentFormat.OpenXml.Drawing.TextAlignmentTypeValues.Center,
            TextAlignment.Right => DocumentFormat.OpenXml.Drawing.TextAlignmentTypeValues.Right,
            TextAlignment.Justify => DocumentFormat.OpenXml.Drawing.TextAlignmentTypeValues.Justified,
            _ => DocumentFormat.OpenXml.Drawing.TextAlignmentTypeValues.Left
        };
}
