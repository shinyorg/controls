using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Shiny.Controls.Office.Presentation;
using Shiny.Controls.Office.Shapes;
using Shiny.Controls.Office.Skia;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;
using Shouldly;
using Xunit;
using D = DocumentFormat.OpenXml.Drawing;
using Package = DocumentFormat.OpenXml.Packaging.PresentationDocument;

namespace Shiny.Controls.Office.Tests;

/// <summary>
/// The PowerPoint feature push: shape format, arrangement, text, links, design, transitions,
/// animations, the show, charts, media, header &amp; footer, sections, outline, replace and export —
/// every one round-tripped through a save and checked against the schema.
/// </summary>
public class SlidePowerPointTests
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

    static SlideEditorController Controller(SlideDeck deck, int slide = 1)
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

    static async Task<SlideDeck> RoundTripAsync(SlideDeck deck)
        => await SlideDeck.OpenAsync(new MemoryStream(await SaveAsync(deck)), editable: true);

    static IReadOnlyList<string> NewValidationErrors(byte[] saved)
    {
        // Positions stripped from the path: the fixture's own known error (an empty text body on its
        // "Exotic" shape) moves index when shapes are grouped or deleted, and is not a new one.
        static List<string> Errors(byte[] bytes)
        {
            using var document = Package.Open(new MemoryStream(bytes), false);
            return new OpenXmlValidator(FileFormatVersions.Office2019)
                .Validate(document)
                .Select(x => $"{System.Text.RegularExpressions.Regex.Replace(x.Path?.XPath ?? string.Empty, @"\[\d+\]", string.Empty)}: {x.Description}")
                .ToList();
        }

        var baseline = Errors(SlideFixture.Build()).ToHashSet();
        return Errors(saved).Where(x => !baseline.Contains(x)).ToList();
    }

    static int Callout(SlideDeck deck) => deck.Slides[1].Shapes.ToList().FindIndex(x => x.Text?.PlainText == "Callout text");

    static int Body(SlideDeck deck) => deck.Slides[1].Shapes.ToList().FindIndex(x => x.Text?.PlainText.StartsWith("Top level", StringComparison.Ordinal) == true);

    // ---- shape format ----

    [Fact]
    public async Task ShapeFillKeepsSchemaOrderInFrontOfAnExistingOutline()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck);
        c.Select(Callout(deck));

        c.SetShapeFill(new ArgbColor(255, 0x11, 0x22, 0x33));

        var saved = await SaveAsync(deck);
        NewValidationErrors(saved).ShouldBeEmpty();

        using var reopened = await OpenAsync(saved);
        var shape = reopened.Slides[1].Shapes[Callout(reopened)];
        shape.Fill.Solid.ShouldBe(new ArgbColor(255, 0x11, 0x22, 0x33));
        shape.Outline.ShouldNotBeNull();
    }

    [Fact]
    public async Task GradientFillRoundTrips()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck);
        c.Select(Callout(deck));

        c.SetShapeFill(SlideFillSpec.LinearGradient(new ArgbColor(255, 255, 0, 0), new ArgbColor(255, 0, 0, 255), 45));

        var saved = await SaveAsync(deck);
        NewValidationErrors(saved).ShouldBeEmpty();

        using var reopened = await OpenAsync(saved);
        var fill = reopened.Slides[1].Shapes[Callout(reopened)].Fill;
        fill.GradientStops.Count.ShouldBe(2);
        fill.GradientAngle.ShouldBe(45, 0.01);
    }

    [Fact]
    public async Task OutlineColourWeightAndDashRoundTripAndUndo()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck);
        var index = Callout(deck);
        c.Select(index);
        var before = deck.Slides[1].Shapes[index].Outline;

        c.SetShapeOutlineColor(new ArgbColor(255, 255, 0, 0));
        c.SetShapeOutlineWeight(6);
        c.SetShapeOutlineDash(LineDash.DashDot);

        var saved = await SaveAsync(deck);
        NewValidationErrors(saved).ShouldBeEmpty();

        using (var reopened = await OpenAsync(saved))
        {
            var outline = reopened.Slides[1].Shapes[index].Outline!;
            outline.Color.ShouldBe(new ArgbColor(255, 255, 0, 0));
            outline.Width.ShouldBe(8, 0.1);
            outline.Dash.ShouldBe(LineDash.DashDot);
        }

        c.Undo();
        c.Undo();
        c.Undo();
        deck.Slides[1].Shapes[index].Outline.ShouldBe(before);

        c.RemoveShapeOutline();
        deck.Slides[1].Shapes[index].Outline.ShouldBeNull();
    }

    [Fact]
    public async Task ShadowTurnsOnAndOff()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck);
        var index = Callout(deck);
        c.Select(index);

        c.SetShapeShadow(true);
        deck.Slides[1].Shapes[index].Shadow.ShouldNotBeNull();

        var saved = await SaveAsync(deck);
        NewValidationErrors(saved).ShouldBeEmpty();

        using (var reopened = await OpenAsync(saved))
            reopened.Slides[1].Shapes[index].Shadow!.Distance.ShouldBe(SetShapeShadowCommand.Default.Distance, 0.5);

        c.SetShapeShadow(false);
        deck.Slides[1].Shapes[index].Shadow.ShouldBeNull();
    }

    [Fact]
    public async Task QuickStyleWritesThemeColours()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck);
        var index = Callout(deck);
        c.Select(index);

        c.ApplyQuickStyle(new SlideQuickStyle(SlideQuickStyleKind.ColoredFill, 2));

        var saved = await SaveAsync(deck);
        NewValidationErrors(saved).ShouldBeEmpty();

        using var reopened = await OpenAsync(saved);
        var shape = reopened.Slides[1].Shapes[index];
        shape.Fill.Solid.ShouldBe(DrawingReader.ParseHex("ED7D31"));
        shape.Text!.Paragraphs[0].Runs[0].Style.Color.ShouldBe(new ArgbColor(255, 255, 255, 255));
    }

    [Fact]
    public async Task SizeRotationAndFlipsRoundTrip()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck);
        var index = Callout(deck);
        c.Select(index);

        c.SetShapeSize(300, 120);
        c.RotateBy(90);
        c.Flip(horizontal: true);

        var saved = await SaveAsync(deck);
        NewValidationErrors(saved).ShouldBeEmpty();

        using var reopened = await OpenAsync(saved);
        var shape = reopened.Slides[1].Shapes[index];
        shape.Width.ShouldBe(300, 0.5);
        shape.Height.ShouldBe(120, 0.5);
        shape.Rotation.ShouldBe(90, 0.01);
        shape.FlipHorizontal.ShouldBeTrue();

        c.RotateBy(-90);
        deck.Slides[1].Shapes[index].Rotation.ShouldBe(0);
    }

    [Fact]
    public async Task TheRotationHandleTurnsTheShapeAndShiftSnapsToFifteenDegrees()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck);
        var index = Callout(deck);
        c.Select(index);

        var grip = c.RotationHandle()!.Value;
        var bounds = c.SelectionBounds()!.Value;
        var (cx, cy) = (bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);

        c.PointerDown(grip.X + grip.Width / 2, grip.Y + grip.Height / 2).ShouldBeTrue();

        // A quarter turn clockwise, plus a little: the grip moves from above the centre to its right.
        var radius = cy - (grip.Y + grip.Height / 2);
        c.PointerMove(cx + radius, cy + radius * 0.05, constrain: true);
        c.PointerUp();

        deck.Slides[1].Shapes[index].Rotation.ShouldBe(90, 0.01);

        c.Undo();
        deck.Slides[1].Shapes[index].Rotation.ShouldBe(0);
    }

    // ---- selection and arrangement ----

    [Fact]
    public async Task ShiftClickBuildsAMultiSelectionAndAMarqueeSelectsWhatIsInside()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck);
        c.AddShape(ShapeGeometry.Ellipse, 700, 60, 80, 80);
        var ellipse = c.SelectedShape;
        var callout = Callout(deck);

        var shape = deck.Slides[1].Shapes[callout];
        c.PointerDown(shape.X + 5, shape.Y + 5, extendSelection: true);
        c.PointerUp();
        c.SelectedShapes.Count.ShouldBe(2);
        c.SelectedShapes.ShouldContain(callout);
        c.SelectedShapes.ShouldContain(ellipse);

        c.ClearSelection();

        // A box around the ellipse only.
        c.PointerDown(690, 50);
        c.PointerMove(790, 150);
        c.Marquee.ShouldNotBeNull();
        c.PointerUp();

        c.SelectedShapes.ShouldBe([ellipse]);
    }

    [Fact]
    public async Task DraggingAMultiSelectionMovesEveryShapeAsOneUndoStep()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck);
        c.AddShape(ShapeGeometry.Ellipse, 700, 60, 80, 80);
        var ellipse = c.SelectedShape;
        var callout = Callout(deck);
        c.ToggleSelected(callout);

        var a = deck.Slides[1].Shapes[ellipse];
        var b = deck.Slides[1].Shapes[callout];
        c.SmartGuides = false;

        c.PointerDown(a.X + 10, a.Y + 10);
        for (var i = 1; i <= 5; i++)
            c.PointerMove(a.X + 10 - i * 4, a.Y + 10 + i * 2);
        c.PointerUp();

        deck.Slides[1].Shapes[ellipse].X.ShouldBe(a.X - 20, 0.5);
        deck.Slides[1].Shapes[callout].X.ShouldBe(b.X - 20, 0.5);

        c.Undo();
        deck.Slides[1].Shapes[ellipse].X.ShouldBe(a.X, 0.5);
        deck.Slides[1].Shapes[callout].X.ShouldBe(b.X, 0.5);
    }

    [Fact]
    public async Task DraggingNearTheSlideCentreSnapsToItAndShowsAGuide()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck);
        c.AddShape(ShapeGeometry.Rectangle, 100, 100, 100, 50);
        var shape = deck.Slides[1].Shapes[c.SelectedShape];

        // Its centre to within 3px of the slide's centre line.
        var target = deck.SlideWidth / 2 - shape.Width / 2 + 3;
        c.PointerDown(shape.X + 5, shape.Y + 5);
        c.PointerMove(target + 5, shape.Y + 5);

        c.ActiveGuides.ShouldContain(x => x.IsVertical && Math.Abs(x.Position - deck.SlideWidth / 2) < 0.01);
        c.PointerUp();

        var moved = deck.Slides[1].Shapes[c.SelectedShape];
        (moved.X + moved.Width / 2).ShouldBe(deck.SlideWidth / 2, 0.01);
    }

    [Fact]
    public void AlignAndDistributeArithmetic()
    {
        var shapes = new List<(int, SlideRect)>
        {
            (0, new SlideRect(10, 10, 20, 20)),
            (1, new SlideRect(100, 50, 40, 10)),
            (2, new SlideRect(50, 200, 10, 30))
        };

        SlideArrangement.Align(shapes, ShapeAlignment.Left).Select(x => x.Bounds.X).ShouldAllBe(x => x == 10);
        SlideArrangement.Align(shapes, ShapeAlignment.Right).Select(x => x.Bounds.Right).ShouldAllBe(x => x == 140);
        SlideArrangement.Align(shapes, ShapeAlignment.Middle).Select(x => x.Bounds.Y + x.Bounds.Height / 2).ShouldAllBe(x => Math.Abs(x - 120) < 0.001);
        SlideArrangement.Align(shapes, ShapeAlignment.Center, new SlideRect(0, 0, 960, 540)).Select(x => x.Bounds.X + x.Bounds.Width / 2).ShouldAllBe(x => Math.Abs(x - 480) < 0.001);

        // Widths 20 + 10 + 40 across 10..140: 60 of space, two gaps of 30.
        var spread = SlideArrangement.Distribute(shapes, horizontally: true).OrderBy(x => x.Bounds.X).ToList();
        spread[0].Bounds.X.ShouldBe(10);
        spread[1].Bounds.X.ShouldBe(60, 0.001);
        spread[2].Bounds.X.ShouldBe(100, 0.001);
    }

    [Fact]
    public async Task GroupAndUngroupRoundTripAndUndo()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck);
        c.AddShape(ShapeGeometry.Ellipse, 600, 60, 80, 80);
        var ellipse = c.SelectedShape;
        c.ToggleSelected(Callout(deck));
        var shapesBefore = deck.Slides[1].Shapes.Count;

        c.CanGroup.ShouldBeTrue();
        c.Group();

        c.Selection!.IsGroup.ShouldBeTrue();
        deck.Slides[1].Shapes.Count.ShouldBe(shapesBefore + 1);

        var saved = await SaveAsync(deck);
        NewValidationErrors(saved).ShouldBeEmpty();

        using (var reopened = await OpenAsync(saved))
        {
            var group = reopened.Slides[1].Shapes.Single(x => x.IsGroup);
            reopened.Slides[1].Shapes.Count(x => x.IsInGroup).ShouldBe(2);
            group.X.ShouldBe(deck.Slides[1].Shapes[Callout(deck)].X, 0.5);
        }

        var ellipseBefore = deck.Slides[1].Shapes.First(x => x.Geometry == ShapeGeometry.Ellipse);
        c.Ungroup();
        c.SelectedShapes.Count.ShouldBe(2);
        deck.Slides[1].Shapes.Any(x => x.IsGroup).ShouldBeFalse();

        var ellipseAfter = deck.Slides[1].Shapes.First(x => x.Geometry == ShapeGeometry.Ellipse);
        ellipseAfter.X.ShouldBe(ellipseBefore.X, 0.5);
        ellipseAfter.Y.ShouldBe(ellipseBefore.Y, 0.5);

        c.Undo();
        deck.Slides[1].Shapes.Any(x => x.IsGroup).ShouldBeTrue();
        c.Undo();
        deck.Slides[1].Shapes.Any(x => x.IsGroup).ShouldBeFalse();
        _ = ellipse;
    }

    [Fact]
    public async Task AlignmentThroughTheControllerIsOneUndoStep()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck);
        c.AddShape(ShapeGeometry.Ellipse, 600, 60, 80, 80);
        c.ToggleSelected(Callout(deck));

        c.Align(ShapeAlignment.Top);
        var tops = c.SelectedShapes.Select(i => deck.Slides[1].Shapes[i].Y).Distinct().ToList();
        tops.Count.ShouldBe(1);

        c.Undo();
        c.SelectedShapes.Select(i => deck.Slides[1].Shapes[i].Y).Distinct().Count().ShouldBe(2);
    }

    // ---- text ----

    [Fact]
    public async Task ShiftEnterWritesALineBreakAndBackspaceTakesItAway()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck);
        c.Select(Callout(deck));
        c.BeginTextEditing(0, 0);
        c.MoveCaret(c.Caret with { Offset = 7 });

        c.InsertLineBreak();

        var body = deck.Slides[1].Shapes[Callout(deck)].Text!;
        body.Paragraphs.Count.ShouldBe(1);
        body.Paragraphs[0].Runs.Count(x => x.IsBreak).ShouldBe(1);
        body.Paragraphs[0].PlainText.ShouldBe("Callout text");

        var saved = await SaveAsync(deck);
        NewValidationErrors(saved).ShouldBeEmpty();
        using (var reopened = await OpenAsync(saved))
            reopened.Slides[1].Shapes[Callout(reopened)].Text!.Paragraphs[0].Runs.Count(x => x.IsBreak).ShouldBe(1);

        c.Backspace();
        deck.Slides[1].Shapes[Callout(deck)].Text!.Paragraphs[0].Runs.Count(x => x.IsBreak).ShouldBe(0);
        deck.Slides[1].Shapes[Callout(deck)].Text!.PlainText.ShouldBe("Callout text");
    }

    [Fact]
    public async Task ChangeCaseClearFormattingAndSuperscript()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck);
        var index = Callout(deck);
        c.Select(index);
        c.BeginTextEditing(0, 0);
        c.SelectAll();

        c.ChangeCase(TextCase.Upper);
        deck.Slides[1].Shapes[index].Text!.PlainText.ShouldBe("CALLOUT TEXT");

        c.ChangeCase(TextCase.Capitalize);
        deck.Slides[1].Shapes[index].Text!.PlainText.ShouldBe("Callout Text");

        c.ToggleSuperscript();
        deck.Slides[1].Shapes[index].Text!.Paragraphs[0].Runs[0].Style.BaselineShift.ShouldBeGreaterThan(0);

        c.ClearFormatting();
        var run = deck.Slides[1].Shapes[index].Text!.Paragraphs[0].Runs[0].Style;
        run.Bold.ShouldBeFalse();
        run.BaselineShift.ShouldBe(0);

        NewValidationErrors(await SaveAsync(deck)).ShouldBeEmpty();
    }

    [Fact]
    public async Task FontGrowsAndShrinksAlongTheLadder()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck);
        c.Select(Callout(deck));
        c.BeginTextEditing(0, 0);
        c.SelectAll();

        c.GrowFont(1);
        c.CaretFormat.FontSize.ShouldBe(20);

        c.GrowFont(-1);
        c.GrowFont(-1);
        c.CaretFormat.FontSize.ShouldBe(16);
    }

    [Fact]
    public async Task LineSpacingAnchorDirectionAndAutofit()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck);
        var index = Body(deck);
        c.Select(index);

        c.SetLineSpacing(1.5);
        c.SetTextAnchor(TextAnchor.Bottom);
        c.SetTextDirection(ShapeTextDirection.Rotate270);
        c.SetAutofit(TextAutofit.ShrinkOnOverflow);

        var saved = await SaveAsync(deck);
        NewValidationErrors(saved).ShouldBeEmpty();

        using var reopened = await OpenAsync(saved);
        var shape = reopened.Slides[1].Shapes[index];
        shape.Text!.Paragraphs[0].LineSpacing.ShouldBe(1.5, 0.001);
        shape.Text.Anchor.ShouldBe(TextAnchor.Bottom);
        shape.TextDirection.ShouldBe(ShapeTextDirection.Rotate270);
        shape.Autofit.ShouldBe(TextAutofit.ShrinkOnOverflow);
    }

    // ---- links ----

    [Fact]
    public async Task TextAndShapeLinksRoundTripThroughTheirRelationships()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck);
        var index = Callout(deck);
        c.Select(index);
        c.BeginTextEditing(0, 0);
        c.SelectAll();

        c.SetHyperlink(new SlideHyperlink("https://shinylib.net/"));
        c.EndTextEditing();

        c.Select(Body(deck));
        c.SetHyperlink(new SlideHyperlink(null, Slide: 0));

        var saved = await SaveAsync(deck);
        NewValidationErrors(saved).ShouldBeEmpty();

        using var reopened = await OpenAsync(saved);
        var run = reopened.Slides[1].Shapes[index].Text!.Paragraphs[0].Runs[0];
        SlideHyperlinkCodec.Decode(run.Style.Link)!.Url.ShouldBe("https://shinylib.net/");
        run.Style.Underline.ShouldBe(UnderlineStyle.Single);

        reopened.Slides[1].Shapes[Body(reopened)].Hyperlink!.Slide.ShouldBe(0);

        using var package = Package.Open(new MemoryStream(saved), false);
        var part = package.PresentationPart!.SlideParts.First(x => x.Slide.InnerText.Contains("Callout", StringComparison.Ordinal));
        part.HyperlinkRelationships.ShouldContain(x => x.Uri.ToString() == "https://shinylib.net/");
    }

    [Fact]
    public async Task RemovingATextLinkClearsIt()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck);
        c.Select(Callout(deck));
        c.BeginTextEditing(0, 0);
        c.SelectAll();

        c.SetHyperlink(new SlideHyperlink("example.com"));
        SlideHyperlinkCodec.Decode(c.CaretFormat.Link)!.Url.ShouldBe("https://example.com");

        c.RemoveHyperlink();
        c.CaretFormat.Link.ShouldBeNull();
    }

    // ---- transitions ----

    [Fact]
    public async Task APlainTransitionIsWrittenWithoutAlternateContent()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck);

        c.SetTransition(new SlideTransition(SlideTransitionKind.Fade, SlideTransitionDirection.FromBottom, TimeSpan.FromSeconds(0.5)) { AdvanceAfter = TimeSpan.FromSeconds(3) });

        var saved = await SaveAsync(deck);
        NewValidationErrors(saved).ShouldBeEmpty();

        using (var package = Package.Open(new MemoryStream(saved), false))
        {
            var xml = package.PresentationPart!.SlideParts.First(x => x.Slide.InnerText.Contains("Callout", StringComparison.Ordinal)).Slide.OuterXml;
            xml.ShouldContain("spd=\"fast\" advTm=\"3000\"");
            xml.ShouldContain("<p:fade />");
            xml.ShouldNotContain("AlternateContent");
        }

        using var reopened = await OpenAsync(saved);
        var transition = reopened.Slides[1].Transition!;
        transition.Kind.ShouldBe(SlideTransitionKind.Fade);
        transition.AdvanceAfter.ShouldBe(TimeSpan.FromSeconds(3));
    }

    [Theory]
    [InlineData(SlideTransitionKind.Push, SlideTransitionDirection.FromLeft, 1600)]
    [InlineData(SlideTransitionKind.Wipe, SlideTransitionDirection.FromTop, 1000)]
    [InlineData(SlideTransitionKind.Split, SlideTransitionDirection.HorizontalIn, 1250)]
    [InlineData(SlideTransitionKind.Cover, SlideTransitionDirection.FromRight, 750)]
    [InlineData(SlideTransitionKind.Zoom, SlideTransitionDirection.Out, 500)]
    [InlineData(SlideTransitionKind.Reveal, SlideTransitionDirection.FromLeft, 2000)]
    [InlineData(SlideTransitionKind.Morph, SlideTransitionDirection.FromBottom, 2000)]
    public async Task EveryTransitionRoundTripsWithItsDirectionAndDuration(SlideTransitionKind kind, SlideTransitionDirection direction, int milliseconds)
    {
        using var deck = await OpenAsync();
        var c = Controller(deck);
        c.SetTransition(new SlideTransition(kind, direction, TimeSpan.FromMilliseconds(milliseconds)) { AdvanceOnClick = false });

        var saved = await SaveAsync(deck);
        NewValidationErrors(saved).ShouldBeEmpty();

        using var reopened = await OpenAsync(saved);
        var transition = reopened.Slides[1].Transition!;
        transition.Kind.ShouldBe(kind);
        transition.Duration.TotalMilliseconds.ShouldBe(milliseconds, 1);
        transition.AdvanceOnClick.ShouldBeFalse();

        if (SlideTransition.DirectionsFor(kind).Count > 0)
            transition.Direction.ShouldBe(direction);
    }

    [Fact]
    public async Task ApplyToAllGivesEverySlideTheTransitionAndUndoesAsOne()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck);
        c.SetTransitionKind(SlideTransitionKind.Push);
        c.ApplyTransitionToAll();

        deck.Slides.ShouldAllBe(x => x.Transition!.Kind == SlideTransitionKind.Push);

        c.Undo();
        deck.Slides[0].Transition.ShouldBeNull();
        deck.Slides[1].Transition!.Kind.ShouldBe(SlideTransitionKind.Push);
    }

    // ---- animations ----

    [Fact]
    public async Task AnimationsRoundTripThroughATimingTreePowerPointReads()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck);
        var callout = Callout(deck);
        var body = Body(deck);

        c.Select(callout);
        c.Animate(SlideAnimationEffect.FlyIn);
        c.Select(body);
        c.Animate(SlideAnimationEffect.Fade);
        c.UpdateAnimation(1, x => x with { Trigger = SlideAnimationTrigger.AfterPrevious, Delay = TimeSpan.FromSeconds(0.25) });
        c.Select(callout);
        c.Animate(SlideAnimationEffect.Spin, add: true);

        var saved = await SaveAsync(deck);
        NewValidationErrors(saved).ShouldBeEmpty();

        using var reopened = await OpenAsync(saved);
        var animations = reopened.Slides[1].Animations;
        animations.Select(x => x.Effect).ShouldBe([SlideAnimationEffect.FlyIn, SlideAnimationEffect.Fade, SlideAnimationEffect.Spin]);
        animations[1].Trigger.ShouldBe(SlideAnimationTrigger.AfterPrevious);
        animations[1].Delay.ShouldBe(TimeSpan.FromSeconds(0.25));
        animations[0].Duration.ShouldBe(TimeSpan.FromSeconds(0.5));
        animations[2].Class.ShouldBe(SlideAnimationClass.Emphasis);

        // Click 1 plays the fly then the fade after it; click 2 the spin.
        var schedule = SlideAnimationTimeline.Schedule(animations);
        schedule[0].Click.ShouldBe(1);
        schedule[1].Click.ShouldBe(1);
        schedule[1].Start.ShouldBe(TimeSpan.FromSeconds(0.75));
        schedule[2].Click.ShouldBe(2);
    }

    [Fact]
    public async Task ReorderingAndRemovingAnimationsAndDeletingTheirShape()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck);
        c.Select(Callout(deck));
        c.Animate(SlideAnimationEffect.Appear);
        c.Select(Body(deck));
        c.Animate(SlideAnimationEffect.Zoom);

        c.MoveAnimation(1, -1);
        deck.Slides[1].Animations[0].Effect.ShouldBe(SlideAnimationEffect.Zoom);

        // Deleting an animated shape takes its animation with it — PowerPoint repairs one left behind.
        c.Select(Body(deck));
        c.DeleteSelectedShape();
        deck.Slides[1].Animations.Select(x => x.Effect).ShouldBe([SlideAnimationEffect.Appear]);
        NewValidationErrors(await SaveAsync(deck)).ShouldBeEmpty();

        c.Undo();
        deck.Slides[1].Animations.Count.ShouldBe(2);
    }

    [Fact]
    public async Task AnimationMarkersNumberTheClicks()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck);
        c.Select(Callout(deck));
        c.Animate(SlideAnimationEffect.Fade);
        c.Select(Body(deck));
        c.Animate(SlideAnimationEffect.Fade);

        c.AnimationMarkers().ShouldBeEmpty();
        c.ShowAnimationMarkers = true;
        c.AnimationMarkers().Select(x => x.Label).ShouldBe(["1", "2"]);
    }

    // ---- the show ----

    [Fact]
    public async Task TheShowStepsThroughClicksSkipsHiddenSlidesAndEnds()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck);
        c.Select(Callout(deck));
        c.Animate(SlideAnimationEffect.Fade);
        c.AddTextBox(10, 10);

        // A third slide, hidden.
        c.NewSlide();
        c.ToggleHideSlide();
        deck.Slides[2].IsHidden.ShouldBeTrue();

        var now = TimeSpan.Zero;
        var show = new SlideShowController(deck, 0, () => now);
        show.Order.ShouldBe([0, 1]);

        show.Next().ShouldBeTrue();
        show.SlideIndex.ShouldBe(1);
        show.Click.ShouldBe(0);

        // Before the click the faded shape is hidden; mid-way it is half there.
        var callout = Callout(deck);
        show.Frame()!.Shapes[callout].Visible.ShouldBeFalse();

        show.Next();
        show.Click.ShouldBe(1);
        now += TimeSpan.FromSeconds(0.25);
        show.Frame()!.Shapes[callout].Opacity.ShouldBe(0.5, 0.01);
        now += TimeSpan.FromSeconds(1);
        show.Frame()!.Shapes.ContainsKey(callout).ShouldBeFalse();

        show.Next();
        show.IsAtEnd.ShouldBeTrue();

        var ended = false;
        show.Ended += (_, _) => ended = true;
        show.Next().ShouldBeFalse();
        ended.ShouldBeTrue();
    }

    [Fact]
    public async Task TransitionProgressAndTimedAdvance()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck);
        c.SetTransition(new SlideTransition(SlideTransitionKind.Push, SlideTransitionDirection.FromBottom, TimeSpan.FromSeconds(1)) { AdvanceAfter = TimeSpan.FromSeconds(2) });

        var now = TimeSpan.Zero;
        var show = new SlideShowController(deck, 0, () => now);
        show.Next();

        var frame = show.Frame()!;
        frame.Previous.ShouldNotBeNull();
        frame.TransitionProgress.ShouldBe(0);

        now += TimeSpan.FromSeconds(0.5);
        show.Frame()!.TransitionProgress.ShouldBe(0.5, 0.001);
        show.IsAnimating.ShouldBeTrue();

        now += TimeSpan.FromSeconds(0.6);
        show.Frame()!.Previous.ShouldBeNull();

        // Timed: two seconds after it finished arriving it moves on - to the end screen.
        show.Tick().ShouldBeFalse();
        now += TimeSpan.FromSeconds(2);
        show.Tick().ShouldBeTrue();
        show.IsAtEnd.ShouldBeTrue();
    }

    [Fact]
    public async Task BlackAndWhiteScreensTakeTheNextPress()
    {
        using var deck = await OpenAsync();
        var show = new SlideShowController(deck, 0);

        show.ToggleBlack();
        show.Frame()!.Screen.ShouldBe(SlideShowScreen.Black);
        show.Next();
        show.Screen.ShouldBe(SlideShowScreen.Slide);
        show.SlideIndex.ShouldBe(0);
    }

    [Fact]
    public async Task HiddenSlidesRoundTrip()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck, 0);
        c.ToggleHideSlide();

        var saved = await SaveAsync(deck);
        NewValidationErrors(saved).ShouldBeEmpty();

        using var reopened = await OpenAsync(saved);
        reopened.Slides[0].IsHidden.ShouldBeTrue();
        reopened.Slides[1].IsHidden.ShouldBeFalse();
    }

    [Fact]
    public async Task ShowLinksJumpToSlidesAndReturnAddresses()
    {
        using var deck = await OpenAsync();
        var show = new SlideShowController(deck, 1);

        show.Follow(new SlideHyperlink(null, 0)).ShouldBeNull();
        show.SlideIndex.ShouldBe(0);

        show.Follow(new SlideHyperlink("https://example.com")).ShouldBe("https://example.com");
        show.Follow(new SlideHyperlink(null, null, SlideShowJumps.LastSlide)).ShouldBeNull();
        show.SlideIndex.ShouldBe(1);
    }

    // ---- design ----

    [Fact]
    public async Task ApplyingAThemeRewritesTheThemePartAndTheDeckFollows()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck);
        var theme = SlideThemeDefinition.BuiltIn.First(x => x.Name == "Meadow");

        c.ApplyTheme(theme);

        c.ThemeName.ShouldBe("Meadow");
        c.ThemeColorScheme!.Accent1.ShouldBe(theme.Colors.Accent1);

        // The master stripe is filled with accent1.
        deck.Slides[1].Shapes.First(x => x.Name == "Master stripe").Fill.Solid.ShouldBe(theme.Colors.Accent1);
        deck.Slides[1].Shapes[Body(deck)].Text!.Paragraphs[0].Runs[0].Style.FontFamily.ShouldBe("Trebuchet MS");

        var saved = await SaveAsync(deck);
        NewValidationErrors(saved).ShouldBeEmpty();

        using (var reopened = await OpenAsync(saved))
            reopened.Slides[1].Shapes.First(x => x.Name == "Master stripe").Fill.Solid.ShouldBe(theme.Colors.Accent1);

        c.Undo();
        deck.Slides[1].Shapes.First(x => x.Name == "Master stripe").Fill.Solid.ShouldBe(DrawingReader.ParseHex(SlideFixture.ThemeAccent1));
    }

    [Fact]
    public async Task AColourVariantKeepsTheFonts()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck);
        var variant = SlideThemeDefinition.BuiltIn[0].Variants[3];

        c.ApplyColorVariant(variant);
        c.ThemeColorScheme!.Light1.ShouldBe(variant.Light1);
        deck.Slides[1].Shapes[Body(deck)].Text!.Paragraphs[0].Runs[0].Style.FontFamily.ShouldBe("Calibri");
    }

    [Fact]
    public async Task BackgroundsSolidPictureAndApplyToAll()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck);

        c.SetBackground(SlideBackgroundSpec.Solid(new ArgbColor(255, 1, 2, 3)));
        deck.Slides[1].Background.Solid.ShouldBe(new ArgbColor(255, 1, 2, 3));
        deck.Slides[0].Background.Solid.ShouldNotBe(new ArgbColor(255, 1, 2, 3));

        c.SetBackground(SlideBackgroundSpec.Image(Png(), "image/png"), applyToAll: true);
        deck.Slides.ShouldAllBe(x => x.Background.Image != null);

        var saved = await SaveAsync(deck);
        NewValidationErrors(saved).ShouldBeEmpty();

        using (var reopened = await OpenAsync(saved))
            reopened.Slides.ShouldAllBe(x => x.Background.Image != null);

        c.Undo();
        deck.Slides[1].Background.Solid.ShouldBe(new ArgbColor(255, 1, 2, 3));

        c.ResetBackground();
        deck.Slides[1].OwnBackground.ShouldBeNull();
    }

    [Fact]
    public async Task SlideSizeScalesTheContentAndUndoesExactly()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck);
        var before = deck.Slides[1].Shapes[Callout(deck)];

        c.SetSlideSize(960, 720);

        deck.SlideWidth.ShouldBe(960, 0.01);
        var after = deck.Slides[1].Shapes[Callout(deck)];
        after.X.ShouldBe(before.X * 0.75, 0.5);
        after.Width.ShouldBe(before.Width * 0.75, 0.5);

        var saved = await SaveAsync(deck);
        NewValidationErrors(saved).ShouldBeEmpty();

        using (var reopened = await OpenAsync(saved))
            reopened.SlideWidth.ShouldBe(960, 0.01);

        c.Undo();
        deck.SlideWidth.ShouldBe(1280, 0.01);
        deck.Slides[1].Shapes[Callout(deck)].X.ShouldBe(before.X, 0.001);
    }

    // ---- insert ----

    [Fact]
    public async Task ChartsInsertRoundTripAndTakeNewData()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck);

        c.AddChart(SlideChart.Sample(SlideChartKind.Column));
        c.SelectedChart.ShouldNotBeNull();

        var saved = await SaveAsync(deck);
        NewValidationErrors(saved).ShouldBeEmpty();

        using (var reopened = await OpenAsync(saved))
        {
            var chart = reopened.Slides[1].Shapes.Single(x => x.Chart is not null).Chart!;
            chart.Kind.ShouldBe(SlideChartKind.Column);
            chart.Categories.Count.ShouldBe(4);
            chart.Series.Count.ShouldBe(3);
            chart.Series[1].Values[1].ShouldBe(4.4);
            chart.Title.ShouldBe("Chart Title");
        }

        c.SetChartData(new SlideChart(SlideChartKind.Pie, ["A", "B"], [new SlideChartSeries("Share", [60, 40])]));
        c.SelectedChart!.Kind.ShouldBe(SlideChartKind.Pie);
        c.SelectedChart.Series[0].Values.ShouldBe([60d, 40d]);

        c.Undo();
        c.Select(deck.Slides[1].Shapes.ToList().FindIndex(x => x.Chart is not null));
        c.SelectedChart!.Kind.ShouldBe(SlideChartKind.Column);
    }

    [Fact]
    public async Task VideoIsEmbeddedTheWayPowerPointEmbedsIt()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck);
        var clip = Enumerable.Range(0, 64).Select(x => (byte)x).ToArray();

        c.AddMedia(clip, "video/mp4", isVideo: true);

        var saved = await SaveAsync(deck);
        NewValidationErrors(saved).ShouldBeEmpty();

        using var reopened = await OpenAsync(saved);
        var media = reopened.Slides[1].Shapes.Single(x => x.Media is not null).Media!;
        media.IsVideo.ShouldBeTrue();
        media.ReadAll().ShouldBe(clip);
    }

    [Fact]
    public async Task HeaderAndFooterPutsNumberedPlaceholdersOnEverySlide()
    {
        using var deck = await OpenAsync(WithFooterPlaceholders());
        var c = Controller(deck);

        c.ApplyHeaderFooter(new SlideHeaderFooter { SlideNumber = true, Footer = true, FooterText = "Confidential" }, applyToAll: true);

        var saved = await SaveAsync(deck);
        NewValidationErrors(saved).ShouldBeEmpty();

        using var reopened = await OpenAsync(saved);
        reopened.Slides[1].Shapes.Single(x => x.PlaceholderType == "sldNum").Text!.PlainText.ShouldBe("2");
        reopened.Slides[0].Shapes.Single(x => x.PlaceholderType == "ftr").Text!.PlainText.ShouldBe("Confidential");
        SlideHeaderFooter.Of(reopened.Slides[0]).Footer.ShouldBeTrue();

        c.ApplyHeaderFooter(new SlideHeaderFooter(), applyToAll: false);
        deck.Slides[1].Shapes.Any(x => x.PlaceholderType is "sldNum" or "ftr").ShouldBeFalse();
    }

    static byte[] WithFooterPlaceholders()
    {
        var buffer = new MemoryStream();
        buffer.Write(SlideFixture.Build());
        buffer.Position = 0;

        using (var document = Package.Open(buffer, true, new OpenSettings { AutoSave = false }))
        {
            var layout = document.PresentationPart!.SlideMasterParts.First().SlideLayoutParts.First();
            var tree = layout.SlideLayout.CommonSlideData!.ShapeTree!;
            uint id = 20;
            foreach (var (type, x) in new[] { (DocumentFormat.OpenXml.Presentation.PlaceholderValues.Footer, 4000000L), (DocumentFormat.OpenXml.Presentation.PlaceholderValues.SlideNumber, 9000000L) })
            {
                tree.Append(new DocumentFormat.OpenXml.Presentation.Shape(
                    new DocumentFormat.OpenXml.Presentation.NonVisualShapeProperties(
                        new DocumentFormat.OpenXml.Presentation.NonVisualDrawingProperties { Id = id++, Name = type.ToString() },
                        new DocumentFormat.OpenXml.Presentation.NonVisualShapeDrawingProperties(),
                        new DocumentFormat.OpenXml.Presentation.ApplicationNonVisualDrawingProperties(new DocumentFormat.OpenXml.Presentation.PlaceholderShape { Type = type, Index = id })),
                    new DocumentFormat.OpenXml.Presentation.ShapeProperties(new D.Transform2D(new D.Offset { X = x, Y = 6356350L }, new D.Extents { Cx = 2743200L, Cy = 365125L })),
                    new DocumentFormat.OpenXml.Presentation.TextBody(new D.BodyProperties(), new D.ListStyle(), new D.Paragraph())));
            }

            layout.SlideLayout.Save();
            document.Save();
        }

        return buffer.ToArray();
    }

    [Fact]
    public async Task FieldsInsertAtTheCaret()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck);
        c.Select(Callout(deck));
        c.BeginTextEditing(0, 0);
        c.MoveCaret(c.Caret with { Offset = 7 });

        var index = c.SelectedShape;
        c.InsertField(SlideFieldKind.SlideNumber).ShouldBeTrue();
        deck.Slides[1].Shapes[index].Text!.PlainText.ShouldBe("Callout2 text");
        c.Caret.Offset.ShouldBe(8);
        NewValidationErrors(await SaveAsync(deck)).ShouldBeEmpty();
    }

    // ---- tables ----

    [Fact]
    public async Task TableRowsColumnsMergesAndShading()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck);
        c.AddTable(2, 2, 100, 100, 400, 100);
        var index = c.SelectedShape;

        c.BeginTextEditing(c.SelectionBounds()!.Value.X + 5, c.SelectionBounds()!.Value.Y + 5);
        c.EditTable(SlideTableEdit.InsertRowBelow);
        c.EditTable(SlideTableEdit.InsertColumnRight);

        var table = deck.Slides[1].Shapes[index].Table!;
        table.Rows.Count.ShouldBe(3);
        table.ColumnWidths.Count.ShouldBe(3);
        deck.Slides[1].Shapes[index].Height.ShouldBe(150, 0.5);

        c.ExtendCellSelection(1, 2);
        c.EditTable(SlideTableEdit.MergeCells);
        deck.Slides[1].Shapes[index].Table!.Rows[1][2].IsMerged.ShouldBeTrue();

        c.SetCellFill(new ArgbColor(255, 200, 0, 0));
        c.SetTableStyle(SlideTableStyles.Gallery[1]);
        c.SetTableStyleFlags(new SlideTableStyleFlags(true, false, true, false, false, false));

        var saved = await SaveAsync(deck);
        NewValidationErrors(saved).ShouldBeEmpty();

        using var reopened = await OpenAsync(saved);
        var roundTripped = reopened.Slides[1].Shapes[index].Table!;
        roundTripped.StyleId.ShouldBe(SlideTableStyles.Gallery[1].Id);
        roundTripped.StyleFlags.FirstColumn.ShouldBeTrue();
        roundTripped.Rows[1][1].ColumnSpan.ShouldBe(2);
    }

    // ---- sections, outline, replace ----

    [Fact]
    public async Task SectionsAddRenameAndRemove()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck);

        c.AddSection("Second");
        deck.Sections.Select(x => x.Name).ShouldBe(["Default Section", "Second"]);
        deck.Sections[1].FirstSlide.ShouldBe(1);

        c.RenameSection(1, "Results");

        var saved = await SaveAsync(deck);
        NewValidationErrors(saved).ShouldBeEmpty();

        using (var reopened = await OpenAsync(saved))
            reopened.Sections.Select(x => x.Name).ShouldBe(["Default Section", "Results"]);

        c.RemoveSection(1);
        deck.Sections.Count.ShouldBe(1);
        deck.Sections[0].SlideCount.ShouldBe(2);

        c.Undo();
        c.Undo();
        c.Undo();
        deck.Sections.ShouldBeEmpty();
    }

    [Fact]
    public async Task TheRailShowsSectionsAndFoldsThem()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck);
        c.AddSection("Second");

        var rail = new SlideRailController(c);
        rail.Resize(180, 800);

        rail.VisibleSections().Count().ShouldBe(2);
        rail.VisibleItems().Count().ShouldBe(2);

        var header = rail.VisibleSections().Last();
        rail.PointerDown(header.X + 10, header.Y + 5).ShouldBeTrue();
        rail.PointerUp();

        rail.IsCollapsed(1).ShouldBeTrue();
        rail.VisibleItems().Select(x => x.Index).ShouldBe([0]);
    }

    [Fact]
    public async Task TheOutlineReadsAndWritesTitlesAndBodies()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck);

        var outline = c.Outline;
        outline[0].Title.ShouldBe("Deck Title");
        outline[1].Body.Select(x => x.Level).ShouldBe([0, 1]);

        var index = Body(deck);
        c.SetOutline(1, null, "First\n\tSecond\nThird");
        var body = deck.Slides[1].Shapes[index].Text!;
        body.Paragraphs.Select(x => x.PlainText).ShouldBe(["First", "Second", "Third"]);
        body.Paragraphs[1].Level.ShouldBe(1);

        c.SetOutline(0, "Renamed", null);
        deck.Slides[0].Title.ShouldBe("Renamed");
        NewValidationErrors(await SaveAsync(deck)).ShouldBeEmpty();
    }

    [Fact]
    public async Task ReplaceAllIsOneUndoStep()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck);
        c.Find.Query = "point";

        c.ReplaceAll("idea").ShouldBe(2);
        deck.Slides[1].Shapes[Body(deck)].Text!.PlainText.ShouldContain("Top level idea");
        deck.Slides[1].Shapes[Body(deck)].Text!.PlainText.ShouldContain("Nested idea");

        c.Undo();
        deck.Slides[1].Shapes[Body(deck)].Text!.PlainText.ShouldContain("Top level point");
    }

    // ---- views ----

    [Fact]
    public async Task ZoomAndPan()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck);
        c.Margin = 16;
        c.Resize(800, 600);

        c.Zoom.ShouldBeNull();
        var fitted = c.EffectiveZoom;

        c.ZoomAt(2, 400, 300);
        c.EffectiveZoom.ShouldBe(2);
        c.CanPan.ShouldBeTrue();
        c.PanBy(50, 0).ShouldBeTrue();

        c.ZoomToFit();
        c.EffectiveZoom.ShouldBe(fitted, 0.0001);
        c.CanPan.ShouldBeFalse();
    }

    [Fact]
    public async Task TheSorterReordersByDrag()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck, 0);
        c.Resize(900, 600);
        c.ViewMode = SlideEditorViewMode.SlideSorter;

        var thumbs = c.VisibleThumbnails().ToList();
        thumbs.Count.ShouldBe(2);
        var first = thumbs[0];
        var second = thumbs[1];
        var title = deck.Slides[0].Title;

        c.PointerDown(first.X + 10, first.Y + 10).ShouldBeTrue();
        c.PointerMove(second.X + second.Width - 2, second.Y + 10);
        c.SorterDropIndex.ShouldBe(2);
        c.PointerUp();

        deck.Slides[1].Title.ShouldBe(title);
    }

    [Fact]
    public async Task TheMasterViewRestylesTheTitleForEverySlide()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck, 0);
        c.ViewMode = SlideEditorViewMode.SlideMaster;

        var master = c.Master;
        master.Pages[0].IsMaster.ShouldBeTrue();
        master.Page!.IsMaster.ShouldBeFalse();

        master.PageIndex = 0;
        var page = master.CurrentPage!;
        page.Shapes.ShouldNotBeEmpty();

        // The fixture's master has no title placeholder; restyle through the body style instead, via a
        // placeholder on the layout page.
        master.PageIndex = 1;
        var title = master.CurrentPage!.Shapes.ToList().FindIndex(x => x.PlaceholderType == "title");
        master.Select(title);
        master.SetTextStyle(new SlideMasterTextStyle { FontFamily = "Georgia", Color = new ArgbColor(255, 200, 10, 10) });

        c.ViewMode = SlideEditorViewMode.Normal;
        var run = deck.Slides[0].Shapes.First(x => x.PlaceholderType is "title").Text!.Paragraphs[0].Runs[0].Style;
        run.FontFamily.ShouldBe("Georgia");
        run.Color.ShouldBe(new ArgbColor(255, 200, 10, 10));

        NewValidationErrors(await SaveAsync(deck)).ShouldBeEmpty();

        c.Undo();
        deck.Slides[0].Shapes.First(x => x.PlaceholderType is "title").Text!.Paragraphs[0].Runs[0].Style.FontFamily.ShouldNotBe("Georgia");
    }

    [Fact]
    public async Task TheMasterViewSetsALayoutBackground()
    {
        using var deck = await OpenAsync();
        var c = Controller(deck, 0);
        c.ViewMode = SlideEditorViewMode.SlideMaster;
        c.Master.PageIndex = 1;

        c.Master.SetBackground(SlideBackgroundSpec.Solid(new ArgbColor(255, 9, 9, 9)));
        deck.Slides[0].Background.Solid.ShouldBe(new ArgbColor(255, 9, 9, 9));
        NewValidationErrors(await SaveAsync(deck)).ShouldBeEmpty();
    }

    // ---- export ----

    [Fact]
    public async Task ExportsPngAndPdf()
    {
        using var deck = await OpenAsync();

        var png = SlideExporter.ToPng(deck, 1, 480);
        png.Take(4).ShouldBe(new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        SlideExporter.ToPngs(deck, 240).Count.ShouldBe(2);

        var pdf = SlideExporter.ToPdf(deck);
        System.Text.Encoding.ASCII.GetString(pdf, 0, 4).ShouldBe("%PDF");
    }

    [Fact]
    public async Task AnUntouchedDeckStillSavesByteIdentical()
    {
        var original = SlideFixture.Build();
        using var deck = await OpenAsync(original);
        _ = Controller(deck);
        _ = deck.Sections;

        (await SaveAsync(deck)).ShouldBe(original);
    }

    [Fact]
    public void EverySlideIconDrawsSomethingOnTheGrid()
    {
        foreach (var icon in Enum.GetValues<Shiny.Controls.Office.Icons.SlideIcon>())
        {
            var shapes = Shiny.Controls.Office.Icons.SlideIcons.Shapes(icon);
            shapes.ShouldNotBeEmpty(icon.ToString());

            foreach (var shape in shapes)
            {
                if (shape.Primitive == Shiny.Controls.Office.Icons.OfficeIconPrimitive.Path)
                {
                    shape.Vertices[0].Verb.ShouldBe(Shiny.Controls.Office.Icons.OfficeIconVerb.Move, icon.ToString());
                    shape.Vertices.ShouldAllBe(v => v.X >= 0 && v.X <= 24 && v.Y >= 0 && v.Y <= 24, icon.ToString());
                }
                else
                {
                    (shape.X >= 0 && shape.Y >= 0 && shape.X + shape.Width <= 24 && shape.Y + shape.Height <= 24).ShouldBeTrue(icon.ToString());
                }
            }
        }
    }
}
