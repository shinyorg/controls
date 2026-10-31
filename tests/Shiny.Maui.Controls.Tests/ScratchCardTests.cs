using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>The coverage grid that clips the cover, and how the card turns strokes into a reveal.</summary>
[Collection(ApplicationResourcesCollection.Name)]
public class ScratchCardTests
{
    public ScratchCardTests()
    {
        TestDispatcherProvider.Install();
        _ = new Application();
    }


    // ---------------------------------------------------------------------------------------------
    // Mask
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void AnUntouchedCoverIsOneRectangle()
    {
        var mask = new ScratchMask();
        mask.Resize(200, 100, 4);

        var rects = mask.RemainingRects();

        rects.Count.ShouldBe(1);
        rects[0].ShouldBe(new Rect(0, 0, 200, 100));
        mask.ScratchedFraction.ShouldBe(0);
    }


    [Fact]
    public void TheLastCellIsClampedToTheCard()
    {
        var mask = new ScratchMask();
        mask.Resize(10, 10, 4);

        mask.RemainingRects().ShouldHaveSingleItem().ShouldBe(new Rect(0, 0, 10, 10));
    }


    [Fact]
    public void AStrokeScratchesAlongItsLength()
    {
        var mask = new ScratchMask();
        mask.Resize(200, 100, 2);

        mask.Scratch(new Point(20, 50), new Point(180, 50), 10).ShouldBeTrue();

        mask.IsScratched(50, 25).ShouldBeTrue();
        mask.IsScratched(10, 25).ShouldBeTrue();
        mask.IsScratched(50, 5).ShouldBeFalse();
        mask.ScratchedFraction.ShouldBeGreaterThan(0.1);
        mask.ScratchedFraction.ShouldBeLessThan(0.3);
    }


    [Fact]
    public void ScratchingTheSamePlaceTwiceChangesNothing()
    {
        var mask = new ScratchMask();
        mask.Resize(100, 100, 2);

        mask.Scratch(new Point(50, 50), new Point(50, 50), 10).ShouldBeTrue();
        mask.Scratch(new Point(50, 50), new Point(50, 50), 10).ShouldBeFalse();
    }


    [Fact]
    public void RemainingRectsNeverOverlapAndCoverEverythingLeft()
    {
        var mask = new ScratchMask();
        mask.Resize(120, 80, 4);
        mask.Scratch(new Point(10, 10), new Point(110, 70), 12);
        mask.Scratch(new Point(100, 5), new Point(20, 60), 8);

        var rects = mask.RemainingRects();
        var area = rects.Sum(r => r.Width * r.Height);

        area.ShouldBe((1 - mask.ScratchedFraction) * 120 * 80, 0.001);
        for (var i = 0; i < rects.Count; i++)
            for (var j = i + 1; j < rects.Count; j++)
                rects[i].IntersectsWith(rects[j]).ShouldBeFalse($"{rects[i]} overlaps {rects[j]}");
    }


    [Fact]
    public void AResizeKeepsWhatWasScratched()
    {
        var mask = new ScratchMask();
        mask.Resize(100, 100, 4);
        mask.Scratch(new Point(0, 0), new Point(0, 100), 50);
        var before = mask.ScratchedFraction;

        mask.Resize(200, 200, 4);

        mask.ScratchedFraction.ShouldBe(before, 0.05);
        mask.IsScratched(0, 0).ShouldBeTrue();
        mask.IsScratched(mask.Columns - 1, 0).ShouldBeFalse();
    }


    // ---------------------------------------------------------------------------------------------
    // Card
    // ---------------------------------------------------------------------------------------------

    static ScratchCard Sized(double width = 200, double height = 100)
    {
        var card = new ScratchCard { RevealDuration = TimeSpan.Zero };
        card.Mask.Resize(width, height, card.CellSize);
        return card;
    }


    [Fact]
    public void ScratchingPastTheThresholdReveals()
    {
        var card = Sized();
        card.ThresholdPercent = 50;
        var revealed = 0;
        card.Revealed += (_, _) => revealed++;

        card.ScratchTo(new Point(0, 50), true);
        card.ScratchTo(new Point(200, 50), false);
        card.IsRevealed.ShouldBeFalse();

        card.ScratchTo(new Point(0, 0), true);
        card.ScratchTo(new Point(200, 0), false);
        card.ScratchTo(new Point(200, 100), true);
        card.ScratchTo(new Point(0, 100), false);

        card.IsRevealed.ShouldBeTrue();
        card.ScratchedPercent.ShouldBe(100);
        revealed.ShouldBe(1);
        card.Surface.InputTransparent.ShouldBeTrue();
        card.CoverHost.IsVisible.ShouldBeFalse();
    }


    [Fact]
    public void WithoutAutoRevealTheCardWaits()
    {
        var card = Sized();
        card.AutoReveal = false;
        card.StrokeWidth = 400;

        card.ScratchTo(new Point(100, 50), true);

        card.ScratchedPercent.ShouldBe(100, 0.001);
        card.IsRevealed.ShouldBeFalse();
    }


    [Fact]
    public void TheThresholdEventFiresOnceEvenWithoutAutoReveal()
    {
        var card = Sized();
        card.AutoReveal = false;
        card.ThresholdPercent = 30;
        var hits = new List<double>();
        object? commandParameter = null;
        card.ThresholdReached += (_, e) => hits.Add(e.Percent);
        card.ThresholdReachedCommand = new Command<double>(p => commandParameter = p);

        card.ScratchTo(new Point(0, 50), true);
        card.ScratchTo(new Point(200, 50), false);
        card.ScratchTo(new Point(0, 0), true);
        card.ScratchTo(new Point(200, 0), false);

        hits.Count.ShouldBe(1);
        hits[0].ShouldBeGreaterThanOrEqualTo(30);
        commandParameter.ShouldBe(hits[0]);
        card.IsRevealed.ShouldBeFalse();
    }


    [Fact]
    public void ResetArmsTheThresholdAgain()
    {
        var card = Sized();
        card.AutoReveal = false;
        // One fingertip dab is ~7.6% of a 200x100 card.
        card.ThresholdPercent = 5;
        var hits = 0;
        card.ThresholdReached += (_, _) => hits++;

        card.ScratchTo(new Point(100, 50), true);
        card.Reset();
        card.ScratchTo(new Point(100, 50), true);

        hits.ShouldBe(2);
    }


    [Fact]
    public void TheContentTakesNoTouchesUntilTheThreshold()
    {
        var card = Sized();
        card.AutoReveal = false;
        // One fingertip stroke across a 200x100 card is ~44%; two more top and bottom pass 60.
        card.ThresholdPercent = 60;

        card.HiddenHost.InputTransparent.ShouldBeTrue();
        card.Surface.InputTransparent.ShouldBeFalse();

        card.ScratchTo(new Point(0, 50), true);
        card.ScratchTo(new Point(200, 50), false);
        card.ScratchedPercent.ShouldBeLessThan(60);
        card.HiddenHost.InputTransparent.ShouldBeTrue();

        card.ScratchTo(new Point(0, 0), true);
        card.ScratchTo(new Point(200, 0), false);
        card.ScratchTo(new Point(0, 100), true);
        card.ScratchTo(new Point(200, 100), false);

        card.IsUnlocked.ShouldBeTrue();
        card.HiddenHost.InputTransparent.ShouldBeFalse();
        card.Surface.InputTransparent.ShouldBeTrue();
        card.IsRevealed.ShouldBeFalse();
    }


    [Fact]
    public void ScratchingStopsAtTheThreshold()
    {
        var card = Sized();
        card.AutoReveal = false;
        card.ThresholdPercent = 5;

        card.ScratchTo(new Point(100, 50), true);
        var atThreshold = card.ScratchedPercent;
        card.ScratchTo(new Point(190, 90), false);
        card.ScratchTo(new Point(10, 10), true);

        card.ScratchedPercent.ShouldBe(atThreshold);
    }


    [Fact]
    public void ResetLocksTheContentAgain()
    {
        var card = Sized();
        card.Reveal();
        card.HiddenHost.InputTransparent.ShouldBeFalse();

        card.Reset();

        card.HiddenHost.InputTransparent.ShouldBeTrue();
        card.Surface.InputTransparent.ShouldBeFalse();
    }


    [Fact]
    public void TheStrokeDefaultsToAFingertip()
        => new ScratchCard().StrokeWidth.ShouldBe(ScratchCard.DefaultStrokeWidth);


    [Fact]
    public void ProgressIsReported()
    {
        var card = Sized();
        var reports = new List<double>();
        card.ScratchProgressChanged += (_, e) => reports.Add(e.Percent);

        card.ScratchTo(new Point(20, 50), true);
        card.ScratchTo(new Point(60, 50), false);

        reports.Count.ShouldBe(2);
        reports[1].ShouldBeGreaterThan(reports[0]);
        card.ScratchedPercent.ShouldBe(reports[1]);
    }


    [Fact]
    public void AMoveWithoutAStartScratchesNothing()
    {
        var card = Sized();

        card.ScratchTo(new Point(100, 50), false);

        card.ScratchedPercent.ShouldBe(0);
    }


    [Fact]
    public void DisabledCardsDoNotScratch()
    {
        var card = Sized();
        card.IsScratchEnabled = false;

        card.ScratchTo(new Point(100, 50), true);

        card.ScratchedPercent.ShouldBe(0);
        card.Surface.InputTransparent.ShouldBeTrue();
    }


    [Fact]
    public void SettingIsRevealedRevealsAndClearingItResets()
    {
        var card = Sized();
        var revealed = 0;
        card.Revealed += (_, _) => revealed++;

        card.IsRevealed = true;
        revealed.ShouldBe(1);
        card.ScratchedPercent.ShouldBe(100);

        card.IsRevealed = false;
        card.ScratchedPercent.ShouldBe(0);
        card.Mask.ScratchedFraction.ShouldBe(0);
        card.CoverHost.IsVisible.ShouldBeTrue();
        card.CoverHost.Opacity.ShouldBe(1);
        card.Surface.InputTransparent.ShouldBeFalse();
    }


    [Fact]
    public void RevealFromCodeSyncsTheBindingAndRunsTheCommand()
    {
        var card = Sized();
        var ran = false;
        card.RevealedCommand = new Command(() => ran = true);

        card.Reveal();

        card.IsRevealed.ShouldBeTrue();
        ran.ShouldBeTrue();
    }


    [Fact]
    public void ContentAndCoverLandInTheirLayers()
    {
        var hidden = new Label();
        var cover = new BoxView();
        var card = new ScratchCard { HiddenContent = hidden, Cover = cover };

        hidden.Parent.ShouldNotBeNull();
        ((ContentView)card.CoverHost).Content.ShouldBe(cover);
    }


    [Fact]
    public void NoCoverDrawsTheFoil()
        => ((ContentView)new ScratchCard().CoverHost).Content.ShouldNotBeNull();
}
