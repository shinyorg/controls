using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using DocumentFormat.OpenXml.Validation;
using Shiny.Controls.Office.Presentation;
using Shiny.Controls.Office.Text;
using Shouldly;
using Xunit;
using D = DocumentFormat.OpenXml.Drawing;
using Package = DocumentFormat.OpenXml.Packaging.PresentationDocument;

namespace Shiny.Controls.Office.Tests;

/// <summary>
/// Nudging, stacking order, the shape clipboard, layouts, speaker notes, groups, table cells and the
/// slide rail.
/// </summary>
public class SlideObjectEditingTests
{
    sealed class Fixed : ITextMeasurer
    {
        public TextMetrics Measure(ReadOnlySpan<char> text, TextStyle style)
            => new(text.Length * 8, style.FontSize * 0.8, style.FontSize * 0.2);

        public TextMetrics LineMetrics(TextStyle style)
            => new(0, style.FontSize * 0.8, style.FontSize * 0.2);
    }

    static byte[] Png() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    static Task<SlideDeck> OpenAsync(byte[]? bytes = null)
        => SlideDeck.OpenAsync(new MemoryStream(bytes ?? SlideFixture.Build(), writable: false), editable: true);

    /// <summary>A viewport the deck's own size with no margin: viewport and slide coordinates agree.</summary>
    static SlideEditorController Controller(SlideDeck deck, int slide = 0)
    {
        var controller = new SlideEditorController(deck, new Fixed()) { Margin = 0 };
        controller.Resize(deck.SlideWidth, deck.SlideHeight);
        controller.Index = slide;
        return controller;
    }

    static async Task<byte[]> SaveAsync(SlideDeck deck)
    {
        using var saved = new MemoryStream();
        await deck.SaveToAsync(saved);
        return saved.ToArray();
    }

    static IReadOnlyList<string> NewValidationErrors(byte[] saved)
    {
        static List<string> Errors(byte[] bytes)
        {
            using var document = Package.Open(new MemoryStream(bytes), false);
            return new OpenXmlValidator(FileFormatVersions.Office2019)
                .Validate(document)
                .Select(x => $"{x.Path?.XPath}: {x.Description}")
                .ToList();
        }

        var baseline = Errors(SlideFixture.Build()).ToHashSet();
        return Errors(saved).Where(x => !baseline.Contains(x)).ToList();
    }

    static byte[] Modify(Action<Package> change, byte[]? source = null)
    {
        var buffer = new MemoryStream();
        buffer.Write(source ?? SlideFixture.Build());
        buffer.Position = 0;

        using (var document = Package.Open(buffer, true, new OpenSettings { AutoSave = false }))
        {
            change(document);
            document.Save();
        }

        return buffer.ToArray();
    }

    static int Editable(SlideDeck deck, int slide, Func<SlideShape, bool>? where = null)
        => deck.Slides[slide].Shapes.ToList().FindIndex(x => x.IsEditable && !x.IsInGroup && (where?.Invoke(x) ?? true));

    static void Click(SlideEditorController c, SlideShape shape)
    {
        c.PointerDown(shape.X + shape.Width / 2, shape.Y + shape.Height / 2);
        c.PointerUp();
    }

    // ---- nudge ----

    [Fact]
    public async Task ArrowsNudgeTheSelectedShapeAndARunIsOneUndoStep()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck, 1);
        var shape = Editable(deck, 1);
        c.Select(shape);
        var start = deck.Slides[1].Shapes[shape];

        c.Nudge(1, 0).ShouldBeTrue();
        c.Nudge(1, 0);
        c.Nudge(0, -1, fine: true);

        var moved = deck.Slides[1].Shapes[shape];
        moved.X.ShouldBe(start.X + 16, 0.01);
        moved.Y.ShouldBe(start.Y - 1, 0.01);
        c.Index.ShouldBe(1);

        c.Undo();
        deck.Slides[1].Shapes[shape].X.ShouldBe(start.X, 0.01);
        deck.Slides[1].Shapes[shape].Y.ShouldBe(start.Y, 0.01);
    }

    [Fact]
    public async Task ArrowsDoNothingToShapesWithoutASelectionOrInsideText()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck, 1);

        c.Nudge(1, 0).ShouldBeFalse();

        c.Select(Editable(deck, 1, x => x.Text is not null));
        c.BeginTextEditing(0, 0);
        c.Nudge(1, 0).ShouldBeFalse();
    }

    // ---- stacking order ----

    [Fact]
    public async Task BringToFrontAndSendToBackReorderTheTreeAndKeepTheSelection()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck, 1);
        c.AddShape(Shapes.ShapeGeometry.Rectangle, 10, 10, 50, 50);
        var added = c.Selection!;

        c.SendToBack();
        c.Selection!.Name.ShouldBe(added.Name);
        deck.Slides[1].Shapes.Where(x => x.IsEditable).First().ShouldBe(c.Selection);

        c.BringToFront();
        deck.Slides[1].Shapes.Last(x => x.IsEditable).ShouldBe(c.Selection);

        c.SendBackward();
        deck.Slides[1].Shapes.Last(x => x.IsEditable).ShouldNotBe(c.Selection);

        c.Undo();
        deck.Slides[1].Shapes.Last(x => x.IsEditable).Name.ShouldBe(added.Name);
        NewValidationErrors(await SaveAsync(deck)).ShouldBeEmpty();
    }

    // ---- clipboard ----

    [Fact]
    public async Task CopyAndPasteOntoTheSameSlideCascadesAndUsesFreshIds()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck, 1);
        c.AddShape(Shapes.ShapeGeometry.Hexagon, 100, 100, 80, 60);
        var original = c.Selection!;

        c.CopyShape().ShouldBeTrue();
        c.Paste();
        c.Paste();

        var copies = deck.Slides[1].Shapes.Where(x => x.Geometry == Shapes.ShapeGeometry.Hexagon).ToList();
        copies.Count.ShouldBe(3);
        copies[1].X.ShouldBe(original.X + 16, 0.01);
        copies[2].X.ShouldBe(original.X + 32, 0.01);
        c.Selection.ShouldBe(copies[2]);

        var saved = await SaveAsync(deck);
        using var document = Package.Open(new MemoryStream(saved), false);
        var tree = document.PresentationPart!.SlideParts.Single(x => x.Slide.Descendants<Shape>().Count() > 3).Slide.CommonSlideData!.ShapeTree!;
        var ids = tree.Descendants<NonVisualDrawingProperties>().Select(x => x.Id!.Value).ToList();
        ids.Distinct().Count().ShouldBe(ids.Count);
        NewValidationErrors(saved).ShouldBeEmpty();
    }

    [Fact]
    public async Task APictureCopiedToAnotherSlideBringsItsImageWithIt()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck, 0);
        c.AddPicture(Png(), "image/png", 40, 40, 120, 90);
        c.CopyShape();

        c.Index = 1;
        c.Paste();

        var pasted = c.Selection!;
        pasted.Image.ShouldNotBeNull();
        pasted.X.ShouldBe(40, 0.01);

        using var reopened = await OpenAsync(await SaveAsync(deck));
        reopened.Slides[1].Shapes.Count(x => x.Image is not null).ShouldBe(1);
    }

    [Fact]
    public async Task APictureCanBePastedIntoAnotherDeck()
    {
        using var source = await OpenAsync();
        var from = Controller(source, 0);
        from.AddPicture(Png(), "image/png", 40, 40, 120, 90);
        from.CopyShape();

        using var target = await OpenAsync();
        var to = Controller(target, 1);
        to.Clipboard = from.Clipboard;
        to.Paste();

        using var reopened = await OpenAsync(await SaveAsync(target));
        reopened.Slides[1].Shapes.Single(x => x.Image is not null).Image!.ShouldBe(Png());
    }

    [Fact]
    public async Task CutRemovesTheShapeAndUndoPutsItBack()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck, 1);
        var count = deck.Slides[1].Shapes.Count;
        c.Select(Editable(deck, 1));

        c.CutShape().ShouldBeTrue();
        deck.Slides[1].Shapes.Count.ShouldBe(count - 1);
        c.CanPaste.ShouldBeTrue();

        c.Undo();
        deck.Slides[1].Shapes.Count.ShouldBe(count);
    }

    [Fact]
    public async Task DuplicateShapeLeavesTheClipboardAlone()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck, 1);
        c.Select(Editable(deck, 1));
        var count = deck.Slides[1].Shapes.Count;

        c.DuplicateShape();

        deck.Slides[1].Shapes.Count.ShouldBe(count + 1);
        c.Clipboard.ShouldBeNull();
        c.Undo();
        deck.Slides[1].Shapes.Count.ShouldBe(count);
    }

    // ---- layouts ----

    /// <summary>The fixture with a second, "Title Slide" layout that has a subtitle instead of a body.</summary>
    static byte[] WithTwoLayouts() => Modify(document =>
    {
        var master = document.PresentationPart!.SlideMasterParts.Single();
        var layout = master.AddNewPart<SlideLayoutPart>();
        layout.SlideLayout = new SlideLayout(
            new CommonSlideData(new ShapeTree(
                new NonVisualGroupShapeProperties(
                    new NonVisualDrawingProperties { Id = 1U, Name = string.Empty },
                    new NonVisualGroupShapeDrawingProperties(),
                    new ApplicationNonVisualDrawingProperties()),
                new GroupShapeProperties(new D.TransformGroup()),
                Placeholder(2U, "Title 1", PlaceholderValues.CenteredTitle, null, 1524000, 1122363),
                Placeholder(3U, "Subtitle 2", PlaceholderValues.SubTitle, 1U, 1524000, 3602038)))
            { Name = "Title Slide" })
        { Type = SlideLayoutValues.Title };
        layout.AddPart(master);

        master.SlideMaster.SlideLayoutIdList!.Append(new SlideLayoutId { Id = 2147483650U, RelationshipId = master.GetIdOfPart(layout) });
        master.SlideMaster.Save();

        static Shape Placeholder(uint id, string name, PlaceholderValues type, uint? index, long x, long y)
        {
            var ph = new PlaceholderShape { Type = type };
            if (index is not null)
                ph.Index = index;

            return new Shape(
                new NonVisualShapeProperties(
                    new NonVisualDrawingProperties { Id = id, Name = name },
                    new NonVisualShapeDrawingProperties(),
                    new ApplicationNonVisualDrawingProperties(ph)),
                new ShapeProperties(new D.Transform2D(new D.Offset { X = x, Y = y }, new D.Extents { Cx = 9144000, Cy = 1000000 })),
                new TextBody(new D.BodyProperties(), new D.ListStyle(), new D.Paragraph()));
        }
    });

    [Fact]
    public async Task TheLayoutListComesFromTheMasterAndMarksTheCurrentOne()
    {
        using var deck = await OpenAsync(WithTwoLayouts());
        var c = Controller(deck, 1);

        c.Layouts.Count.ShouldBe(2);
        c.Layouts.Count(x => x.IsCurrent).ShouldBe(1);
        c.Layouts[1].Name.ShouldBe("Title Slide");
    }

    [Fact]
    public async Task ChangingLayoutCarriesTheTitleAcrossAndMovesItToTheNewPlace()
    {
        using var deck = await OpenAsync(WithTwoLayouts());
        var c = Controller(deck, 1);
        var title = deck.Slides[1].Title;
        var other = deck.Slides[0];

        c.SetLayout(c.Layouts.Single(x => x.Name == "Title Slide"));

        deck.Slides[1].Title.ShouldBe(title);
        c.Layouts.Single(x => x.IsCurrent).Name.ShouldBe("Title Slide");

        // The other slide on the old layout is untouched.
        deck.Slides[0].Shapes.Select(x => (x.X, x.Y)).ShouldBe(other.Shapes.Select(x => (x.X, x.Y)));

        var saved = await SaveAsync(deck);
        NewValidationErrors(saved).ShouldBeEmpty();

        using var reopened = await OpenAsync(saved);
        reopened.Slides[1].Title.ShouldBe(title);
        reopened.Slides[0].Shapes.Count.ShouldBe(other.Shapes.Count);

        c.Undo();
        deck.Slides[1].Title.ShouldBe(title);
        c.Layouts.Single(x => x.IsCurrent).Name.ShouldNotBe("Title Slide");
    }

    [Fact]
    public async Task NewSlideCanTakeAChosenLayout()
    {
        using var deck = await OpenAsync(WithTwoLayouts());
        var c = Controller(deck, 1);

        c.NewSlide(c.Layouts.Single(x => x.Name == "Title Slide"));

        c.Index.ShouldBe(2);
        deck.Slides[2].Shapes.Where(x => x.IsEditable).Select(x => x.Prompt?.PlainText)
            .ShouldBe(["Click to add title", "Click to add subtitle"]);
    }

    // ---- notes ----

    [Fact]
    public async Task NotesCanBeWrittenOnASlideThatHadNoneInADeckWithNoNotesMaster()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck, 0);
        deck.Slides[0].Notes.ShouldBeNull();

        c.SetNotes("First line\n\nThird line");

        c.Notes.ShouldBe($"First line{Environment.NewLine}{Environment.NewLine}Third line");

        var saved = await SaveAsync(deck);
        NewValidationErrors(saved).ShouldBeEmpty();

        using var reopened = await OpenAsync(saved);
        reopened.Slides[0].Notes.ShouldBe(c.Notes);

        c.Undo();
        c.Notes.ShouldBeNull();
    }

    [Fact]
    public async Task EditingExistingNotesReplacesThemAndOneUndoRestoresThem()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck, 1);
        var before = c.Notes;
        before.ShouldNotBeNull();

        c.SetNotes("Rewritten");
        c.Notes.ShouldBe("Rewritten");

        c.Undo();
        c.Notes.ShouldBe(before);
    }

    [Fact]
    public async Task SettingTheSameNotesIsNotAnEdit()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck, 1);

        c.SetNotes(c.Notes);
        c.CanUndo.ShouldBeFalse();
        deck.IsDirty.ShouldBeFalse();
    }

    // ---- groups ----

    /// <summary>
    /// Slide 1 with a group whose child space is half its drawn size: a 100x100 child at child (0,0)
    /// draws 200x200 at slide (100,100).
    /// </summary>
    static byte[] WithScaledGroup() => Modify(document =>
    {
        var slide = document.PresentationPart!.SlideParts.First(x => x.Slide.Descendants<Shape>().Count() == 1).Slide;
        var emu = (double px) => (long)Math.Round(px * 9525);

        slide.CommonSlideData!.ShapeTree!.Append(new GroupShape(
            new NonVisualGroupShapeProperties(
                new NonVisualDrawingProperties { Id = 50U, Name = "Group 1" },
                new NonVisualGroupShapeDrawingProperties(),
                new ApplicationNonVisualDrawingProperties()),
            new GroupShapeProperties(new D.TransformGroup(
                new D.Offset { X = emu(100), Y = emu(100) },
                new D.Extents { Cx = emu(400), Cy = emu(200) },
                new D.ChildOffset { X = 0, Y = 0 },
                new D.ChildExtents { Cx = emu(200), Cy = emu(100) })),
            Child(51U, "Left", 0, "Left child"),
            Child(52U, "Right", 100, "Right child")));
        slide.Save();

        Shape Child(uint id, string name, double x, string text) => new(
            new NonVisualShapeProperties(
                new NonVisualDrawingProperties { Id = id, Name = name },
                new NonVisualShapeDrawingProperties(),
                new ApplicationNonVisualDrawingProperties()),
            new ShapeProperties(
                new D.Transform2D(new D.Offset { X = emu(x), Y = 0 }, new D.Extents { Cx = emu(100), Cy = emu(100) }),
                new D.PresetGeometry(new D.AdjustValueList()) { Preset = D.ShapeTypeValues.Rectangle }),
            new TextBody(new D.BodyProperties(), new D.ListStyle(), new D.Paragraph(new D.Run(new D.Text(text)))));
    });

    [Fact]
    public async Task GroupChildrenAreDrawnThroughTheGroupsScale()
    {
        using var deck = await OpenAsync(WithScaledGroup());
        var right = deck.Slides[0].Shapes.Single(x => x.Name == "Right");

        right.X.ShouldBe(300, 0.5);
        right.Y.ShouldBe(100, 0.5);
        right.Width.ShouldBe(200, 0.5);
        right.Height.ShouldBe(200, 0.5);
    }

    [Fact]
    public async Task AClickSelectsTheGroupAndADoubleClickGoesIntoIt()
    {
        using var deck = await OpenAsync(WithScaledGroup());
        var c = Controller(deck, 0);

        c.PointerDown(400, 200);
        c.PointerUp();
        c.Selection!.IsGroup.ShouldBeTrue();

        c.PointerDoubleClick(400, 200);
        c.Selection!.Name.ShouldBe("Right");
        c.IsInsideGroup.ShouldBeTrue();
        c.IsEditingText.ShouldBeTrue();

        // The caret lands where the pointer was, past the end of the short text.
        c.InsertText(" edited");
        deck.Slides[0].Shapes.Single(x => x.Name == "Right").Text!.PlainText.ShouldBe("Right child edited");
    }

    [Fact]
    public async Task MovingAGroupChildWritesItInTheGroupsUnits()
    {
        using var deck = await OpenAsync(WithScaledGroup());
        var c = Controller(deck, 0);
        c.PointerDoubleClick(200, 200);
        c.EndTextEditing();
        c.Selection!.Name.ShouldBe("Left");

        c.Nudge(1, 0);    // 8 slide pixels = 4 child units

        var left = deck.Slides[0].Shapes.Single(x => x.Name == "Left");
        left.X.ShouldBe(108, 0.5);

        using var reopened = await OpenAsync(await SaveAsync(deck));
        reopened.Slides[0].Shapes.Single(x => x.Name == "Left").X.ShouldBe(108, 0.5);
    }

    [Fact]
    public async Task MovingTheGroupCarriesItsChildren()
    {
        using var deck = await OpenAsync(WithScaledGroup());
        var c = Controller(deck, 0);
        c.PointerDown(400, 200);
        c.PointerUp();

        c.Nudge(0, 1);

        deck.Slides[0].Shapes.Single(x => x.Name == "Right").Y.ShouldBe(108, 0.5);
        NewValidationErrors(await SaveAsync(deck)).ShouldBeEmpty();
    }

    [Fact]
    public async Task DeletingAGroupChildUndoesBackIntoTheGroup()
    {
        using var deck = await OpenAsync(WithScaledGroup());
        var c = Controller(deck, 0);
        c.PointerDoubleClick(200, 200);
        c.EndTextEditing();

        c.DeleteSelectedShape();
        deck.Slides[0].Shapes.Any(x => x.Name == "Left").ShouldBeFalse();

        c.Undo();
        var left = deck.Slides[0].Shapes.Single(x => x.Name == "Left");
        left.IsInGroup.ShouldBeTrue();
        left.X.ShouldBe(100, 0.5);
    }

    // ---- tables ----

    [Fact]
    public async Task ATableCanBeMovedNowThatItsTransformIsWritten()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck, 1);
        c.AddTable(2, 2, 100, 100, 400, 200);

        c.Nudge(1, 1);

        c.Selection!.X.ShouldBe(108, 0.5);
        c.Selection!.Y.ShouldBe(108, 0.5);
    }

    [Fact]
    public async Task DoubleClickingATableCellTypesIntoThatCellAndItSaves()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck, 1);
        c.AddTable(2, 2, 100, 100, 400, 200);
        var table = c.SelectedShape;

        // Bottom-right cell of a 400x200 table at (100,100).
        c.PointerDoubleClick(400, 250);
        c.ActiveCell.ShouldBe((1, 1));
        c.InsertText("Total");
        c.InsertParagraph();
        c.InsertText("42");

        var cell = deck.Slides[1].Shapes[table].Table!.Rows[1][1].Text!;
        cell.Paragraphs.Select(x => x.PlainText).ShouldBe(["Total", "42"]);
        deck.Slides[1].Shapes[table].Table!.Rows[0][0].Text!.PlainText.ShouldBe("");

        c.SelectAll();
        c.ToggleBold();
        deck.Slides[1].Shapes[table].Table!.Rows[1][1].Text!.Paragraphs[0].Runs[0].Style.Bold.ShouldBeTrue();

        var saved = await SaveAsync(deck);
        NewValidationErrors(saved).ShouldBeEmpty();
        using var reopened = await OpenAsync(saved);
        reopened.Slides[1].Shapes.Single(x => x.Table is not null).Table!.Rows[1][1].Text!.PlainText
            .ShouldBe($"Total{Environment.NewLine}42");
    }

    [Fact]
    public async Task AClickInAnotherCellMovesTheCaretThere()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck, 1);
        c.AddTable(2, 2, 100, 100, 400, 200);
        c.PointerDoubleClick(150, 150);
        c.ActiveCell.ShouldBe((0, 0));

        c.PointerDown(450, 150);
        c.PointerUp();

        c.ActiveCell.ShouldBe((0, 1));
        c.IsEditingText.ShouldBeTrue();
        c.CaretRect()!.Value.X.ShouldBeGreaterThan(300);
    }

    [Fact]
    public async Task UndoingCellTypingRestoresTheCell()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck, 1);
        c.AddTable(2, 2, 100, 100, 400, 200);
        var table = c.SelectedShape;
        c.PointerDoubleClick(150, 150);
        c.InsertText("abc");

        c.Undo();

        deck.Slides[1].Shapes[table].Table!.Rows[0][0].Text!.PlainText.ShouldBe("");
    }

    [Fact]
    public void ACellSpanningColumnsTakesTheirWidths()
    {
        var table = new SlideTable(
            [100, 100, 100],
            [50, 50],
            [
                [new SlideTableCell(null, null, ColumnSpan: 2), new SlideTableCell(null, null, IsMerged: true), new SlideTableCell(null, null)],
                [new SlideTableCell(null, null), new SlideTableCell(null, null), new SlideTableCell(null, null)]
            ]);

        table.CellBounds(0, 0, 300, 100).ShouldBe((0d, 0d, 200d, 50d));
        table.CellBounds(0, 2, 300, 100).ShouldBe((200d, 0d, 100d, 50d));
        table.CellAt(150, 25, 300, 100).ShouldBe((0, 0));
    }

    // ---- rail ----

    static SlideRailController Rail(SlideEditorController editor)
    {
        // 180 wide: 12 + 22 + 134 + 12, so thumbnails are 134 x 75.375 on a 16:9 deck.
        var rail = new SlideRailController(editor);
        rail.Resize(180, 400);
        return rail;
    }

    [Fact]
    public async Task TappingAThumbnailOpensItsSlide()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck, 0);
        var rail = Rail(c);
        var second = rail.VisibleItems().Single(x => x.Index == 1);

        rail.PointerDown(second.X + 10, second.Y + 10);
        rail.PointerUp();

        c.Index.ShouldBe(1);
        rail.VisibleItems().Single(x => x.IsSelected).Index.ShouldBe(1);
    }

    [Fact]
    public async Task DraggingAThumbnailPastAnotherMovesTheSlide()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck, 0);
        c.NewSlide();                                 // three slides: title, new, content
        var titles = deck.Slides.Select(x => x.Title).ToList();
        var rail = Rail(c);
        var items = rail.VisibleItems().ToList();

        rail.PointerDown(items[0].X + 10, items[0].Y + 10);
        rail.PointerMove(items[0].X + 10, items[0].Y + 30);
        rail.IsDragging.ShouldBeTrue();
        rail.PointerMove(items[2].X + 10, items[2].Y + items[2].Height);
        rail.DropGap.ShouldBe(3);
        rail.IsDropMeaningful.ShouldBeTrue();
        rail.PointerUp();

        deck.Slides.Select(x => x.Title).ShouldBe([titles[1], titles[2], titles[0]]);
        c.Index.ShouldBe(2);

        c.Undo();
        deck.Slides.Select(x => x.Title).ShouldBe(titles);
    }

    [Fact]
    public async Task ASmallWobbleIsStillATapAndDroppingBesideItselfMovesNothing()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck, 0);
        var rail = Rail(c);
        var first = rail.VisibleItems().First();

        rail.PointerDown(first.X + 10, first.Y + 10);
        rail.PointerMove(first.X + 12, first.Y + 13);
        rail.IsDragging.ShouldBeFalse();

        rail.PointerMove(first.X + 10, first.Y + 30);
        rail.IsDropMeaningful.ShouldBeFalse();
        rail.PointerUp();

        c.CanUndo.ShouldBeFalse();
    }

    [Fact]
    public async Task AReadOnlyRailSelectsButNeverDrags()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck, 0);
        c.IsReadOnly = true;
        var rail = Rail(c);
        var first = rail.VisibleItems().First();

        rail.PointerDown(first.X + 10, first.Y + 10);
        rail.PointerMove(first.X + 10, first.Y + 200);
        rail.IsDragging.ShouldBeFalse();
    }
}
