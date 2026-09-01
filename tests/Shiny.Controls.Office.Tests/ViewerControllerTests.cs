using Shiny.Controls.Office.Document;
using Shiny.Controls.Office.Presentation;
using Shiny.Controls.Office.Skia;
using Shouldly;
using Xunit;

namespace Shiny.Controls.Office.Tests;

public class DocumentControllerTests
{
    static async Task<(WordDocument Document, SkiaTextMeasurer Measurer, DocumentController Controller)> SetupAsync()
    {
        var document = await WordDocument.OpenAsync(new MemoryStream(DocumentFixture.Build()));
        var measurer = new SkiaTextMeasurer();
        var controller = new DocumentController(document, measurer);
        controller.Resize(800, 400);
        return (document, measurer, controller);
    }

    [Fact]
    public async Task ThePageIsCentredAndCappedInWidth()
    {
        var (document, measurer, controller) = await SetupAsync();
        using var _ = document;
        using var __ = measurer;

        controller.Resize(2000, 400);

        controller.PageWidth.ShouldBe(controller.MaxPageWidth, 0.01, "a wide window must not stretch the measure");
        controller.PageX.ShouldBeGreaterThan(0, "the page should be centred");
    }

    [Fact]
    public async Task ANarrowWindowUsesTheWholeWidth()
    {
        var (document, measurer, controller) = await SetupAsync();
        using var _ = document;
        using var __ = measurer;

        controller.Resize(320, 400);

        controller.PageWidth.ShouldBe(320, 0.01);
        controller.PageX.ShouldBe(0, 0.01);
    }

    [Fact]
    public async Task LayoutIsCachedUntilTheWidthChanges()
    {
        var (document, measurer, controller) = await SetupAsync();
        using var _ = document;
        using var __ = measurer;

        var first = controller.Blocks;
        controller.Scroll(50);

        controller.Blocks.ShouldBeSameAs(first, "scrolling must not re-lay-out");

        controller.Resize(500, 400);
        controller.Blocks.ShouldNotBeSameAs(first, "a resize must re-lay-out");
    }

    [Fact]
    public async Task ZoomChangesTheMeasure()
    {
        var (document, measurer, controller) = await SetupAsync();
        using var _ = document;
        using var __ = measurer;

        controller.Resize(2000, 400);
        var normal = controller.ContentWidth;

        controller.Zoom = 0.5;
        controller.ContentWidth.ShouldBeLessThan(normal);
    }

    [Fact]
    public async Task ScrollIsClampedToTheContent()
    {
        var (document, measurer, controller) = await SetupAsync();
        using var _ = document;
        using var __ = measurer;

        controller.Scroll(-1000);
        controller.Viewport.ScrollY.ShouldBe(0);

        controller.Scroll(100_000);
        controller.Viewport.ScrollY.ShouldBe(Math.Max(0, controller.Viewport.ContentHeight - controller.Viewport.Height), 0.01);
    }

    [Fact]
    public async Task ScrollingToAHeadingLandsOnIt()
    {
        var (document, measurer, controller) = await SetupAsync();
        using var _ = document;
        using var __ = measurer;

        // Laid out narrow so the document is taller than the viewport and the scroll can actually move.
        controller.Resize(300, 200);
        controller.ScrollToHeading(0);

        var heading = controller.Blocks.OfType<LaidOutParagraph>().First(x => x.Format.OutlineLevel > 0);
        controller.Viewport.ScrollY.ShouldBe(Math.Max(0, heading.Y - 8), 0.01);
    }

    [Fact]
    public async Task ChangedFiresSoTheHostRepaints()
    {
        var (document, measurer, controller) = await SetupAsync();
        using var _ = document;
        using var __ = measurer;

        var count = 0;
        controller.Changed += (_, _) => count++;

        controller.Scroll(10);
        controller.Resize(600, 400);
        controller.Zoom = 1.5;

        count.ShouldBe(3);
    }
}

public class SlideControllerTests
{
    static async Task<(SlideDeck Deck, SlideController Controller)> SetupAsync()
    {
        var deck = await SlideDeck.OpenAsync(new MemoryStream(SlideFixture.Build()));
        var controller = new SlideController(deck);
        controller.Resize(800, 600);
        return (deck, controller);
    }

    [Fact]
    public async Task FittingPreservesTheAspectRatioAndCentres()
    {
        var (deck, controller) = await SetupAsync();
        using var _ = deck;

        var placement = controller.SinglePlacement()!.Value;

        (placement.Width / placement.Height).ShouldBe(deck.AspectRatio, 0.01);
        placement.X.ShouldBeGreaterThanOrEqualTo(0);

        // Letterboxed: a 16:9 slide in a 4:3 viewport leaves space above and below, not beside.
        placement.Y.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task NavigationIsClampedToTheDeck()
    {
        var (deck, controller) = await SetupAsync();
        using var _ = deck;

        controller.CanGoPrevious.ShouldBeFalse();
        controller.Previous();
        controller.Index.ShouldBe(0);

        controller.Next();
        controller.Index.ShouldBe(1);
        controller.CanGoNext.ShouldBeFalse();

        controller.Next();
        controller.Index.ShouldBe(1, "past the end must stay on the last slide");
    }

    [Fact]
    public async Task PresentingFitsTheSlideEdgeToEdge()
    {
        var (deck, controller) = await SetupAsync();
        using var _ = deck;

        var inline = controller.SinglePlacement()!.Value;
        inline.X.ShouldBe(controller.Margin, 0.01, "the inline viewer keeps its margin");

        controller.IsPresenting = true;
        var presented = controller.SinglePlacement()!.Value;

        presented.X.ShouldBe(0, 0.01);
        presented.Width.ShouldBe(800, 0.01);
        (presented.Width / presented.Height).ShouldBe(deck.AspectRatio, 0.01, "fitting still never distorts");
    }

    [Fact]
    public async Task PresentingLeavesTheThumbnailGrid()
    {
        var (deck, controller) = await SetupAsync();
        using var _ = deck;

        controller.Mode = SlideViewMode.Grid;
        controller.IsPresenting = true;

        controller.Mode.ShouldBe(SlideViewMode.Single, "a wall of thumbnails is how you find a slide, not how you show one");
    }

    [Fact]
    public async Task PresentingRaisesChangedSoTheSurfaceRepaints()
    {
        var (deck, controller) = await SetupAsync();
        using var _ = deck;

        var count = 0;
        controller.Changed += (_, _) => count++;

        controller.IsPresenting = true;
        controller.IsPresenting = true;

        count.ShouldBe(1, "re-asserting the same state is not a change");
    }

    [Fact]
    public async Task GridModeLaysOutThumbnailsInRows()
    {
        var (deck, controller) = await SetupAsync();
        using var _ = deck;

        controller.Mode = SlideViewMode.Grid;
        controller.ThumbnailWidth = 180;

        var thumbnails = controller.VisibleThumbnails().ToList();
        thumbnails.Count.ShouldBe(2);

        // Both fit on one row at this width, so they share a y and differ in x.
        thumbnails[0].Y.ShouldBe(thumbnails[1].Y, 0.01);
        thumbnails[1].X.ShouldBeGreaterThan(thumbnails[0].X);
    }

    [Fact]
    public async Task ThumbnailHitTestingFindsTheSlideUnderAPoint()
    {
        var (deck, controller) = await SetupAsync();
        using var _ = deck;

        controller.Mode = SlideViewMode.Grid;
        var second = controller.VisibleThumbnails().Last();

        controller.ThumbnailAt(second.X + 5, second.Y + 5).ShouldBe(1);
        controller.ThumbnailAt(5000, 5000).ShouldBe(-1);
    }

    [Fact]
    public async Task GridScrollingIsClamped()
    {
        var (deck, controller) = await SetupAsync();
        using var _ = deck;

        controller.Mode = SlideViewMode.Grid;

        controller.Scroll(-500);
        controller.ScrollY.ShouldBe(0);

        controller.Scroll(100_000);
        controller.ScrollY.ShouldBe(Math.Max(0, controller.GridHeight() - controller.ViewportHeight), 0.01);
    }

    [Fact]
    public async Task ScrollingDoesNothingInSingleMode()
    {
        var (deck, controller) = await SetupAsync();
        using var _ = deck;

        controller.Scroll(200);
        controller.ScrollY.ShouldBe(0, "single mode fits one slide; there is nothing to scroll");
    }

    [Fact]
    public async Task NarrowingTheViewportReducesTheColumnCount()
    {
        var (deck, controller) = await SetupAsync();
        using var _ = deck;

        controller.Mode = SlideViewMode.Grid;
        controller.ThumbnailWidth = 180;

        controller.Resize(800, 600);
        var wide = controller.GridColumns();

        controller.Resize(220, 600);
        controller.GridColumns().ShouldBeLessThan(wide);
        controller.GridColumns().ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task ChangedFiresOnNavigationAndModeChanges()
    {
        var (deck, controller) = await SetupAsync();
        using var _ = deck;

        var count = 0;
        controller.Changed += (_, _) => count++;

        controller.Next();
        controller.Mode = SlideViewMode.Grid;
        controller.Resize(500, 500);

        count.ShouldBe(3);
    }
}
