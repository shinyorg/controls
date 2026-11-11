using Microsoft.Maui.Controls;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// How a page's transition settings resolve into what the platform runs, hero pairing between two
/// pages, and how an entrance layout hides its children before it plays.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class PageTransitionTests : IDisposable
{
    public PageTransitionTests()
    {
        TestDispatcherProvider.Install();
        _ = new Application();
        Reset();
    }

    public void Dispose() => Reset();

    static void Reset()
    {
        PageTransition.DefaultTransition = PageTransitionType.Default;
        PageTransition.Duration = TimeSpan.FromMilliseconds(320);
    }


    [Fact]
    public void APushUsesThePagesOwnTransition()
    {
        var page = new ContentPage();
        PageTransition.SetPush(page, PageTransitionType.Cover);

        PageTransition.ResolvePush(page).Type.ShouldBe(PageTransitionType.Cover);
    }


    [Fact]
    public void APageWithoutATransitionFallsBackToTheAppDefault()
    {
        PageTransition.DefaultTransition = PageTransitionType.Fade;

        PageTransition.ResolvePush(new ContentPage()).Type.ShouldBe(PageTransitionType.Fade);
        PageTransition.ResolvePush(null).Type.ShouldBe(PageTransitionType.Fade);
    }


    [Fact]
    public void WithNoAppDefaultThePlatformsOwnAnimationIsKept()
        => PageTransition.ResolvePush(new ContentPage()).Type.ShouldBe(PageTransitionType.Default);


    /// <summary>Setting only Push gives a matched pair: the page leaves the way it came.</summary>
    [Fact]
    public void AnUnsetPopMirrorsThePush()
    {
        var page = new ContentPage();
        PageTransition.SetPush(page, PageTransitionType.Zoom);

        var pop = PageTransition.ResolvePop(page);
        pop.Type.ShouldBe(PageTransitionType.Zoom);
        pop.IsPop.ShouldBeTrue();
    }


    [Fact]
    public void AnExplicitPopWinsOverThePush()
    {
        var page = new ContentPage();
        PageTransition.SetPush(page, PageTransitionType.Zoom);
        PageTransition.SetPop(page, PageTransitionType.Fade);

        PageTransition.ResolvePop(page).Type.ShouldBe(PageTransitionType.Fade);
    }


    [Fact]
    public void APopFallsBackToTheAppDefaultWhenThePageSetsNothing()
    {
        PageTransition.DefaultTransition = PageTransitionType.SharedAxisX;
        PageTransition.ResolvePop(new ContentPage()).Type.ShouldBe(PageTransitionType.SharedAxisX);
    }


    [Theory]
    [InlineData(PageTransitionType.Slide, PageTransitionDirection.FromRight)]
    [InlineData(PageTransitionType.Cover, PageTransitionDirection.FromRight)]
    [InlineData(PageTransitionType.SharedAxisX, PageTransitionDirection.FromRight)]
    [InlineData(PageTransitionType.SharedAxisY, PageTransitionDirection.FromBottom)]
    public void TheDefaultDirectionSuitsTheTransition(PageTransitionType type, PageTransitionDirection expected)
    {
        var page = new ContentPage();
        PageTransition.SetPush(page, type);

        PageTransition.ResolvePush(page).Direction.ShouldBe(expected);
    }


    [Theory]
    [InlineData(PageTransitionDirection.FromRight, 1, 0)]
    [InlineData(PageTransitionDirection.FromLeft, -1, 0)]
    [InlineData(PageTransitionDirection.FromBottom, 0, 1)]
    [InlineData(PageTransitionDirection.FromTop, 0, -1)]
    public void TheVectorPointsAtTheEdgeThePageComesFrom(PageTransitionDirection direction, int x, int y)
    {
        var plan = new PageTransitionPlan(PageTransitionType.Cover, direction, false, TimeSpan.Zero);
        plan.Vector.ShouldBe((x, y));
        plan.IsVertical.ShouldBe(y != 0);
    }


    [Fact]
    public void APageCanSetItsOwnDuration()
    {
        var page = new ContentPage();
        PageTransition.SetPush(page, PageTransitionType.Fade);
        PageTransition.SetDuration(page, TimeSpan.FromMilliseconds(900));

        PageTransition.ResolvePush(page).Duration.ShouldBe(TimeSpan.FromMilliseconds(900));
        PageTransition.ResolvePush(new ContentPage()).Duration.ShouldBe(TimeSpan.FromMilliseconds(320));
    }


    [Theory]
    [InlineData(PageTransitionType.Default, false)]
    [InlineData(PageTransitionType.None, false)]
    [InlineData(PageTransitionType.Fade, true)]
    public void OnlyARealTransitionCountsAsAnimated(PageTransitionType type, bool animated)
        => new PageTransitionPlan(type, PageTransitionDirection.FromRight, false, TimeSpan.Zero).IsAnimated.ShouldBe(animated);


    [Fact]
    public void HeroesPairByName()
    {
        var (from, card) = PageWithHero("photo");
        var (to, header) = PageWithHero("photo");

        var pairs = PageTransition.FindHeroPairs(from, to);

        pairs.Count.ShouldBe(1);
        pairs[0].From.ShouldBe(card);
        pairs[0].To.ShouldBe(header);
    }


    [Fact]
    public void HeroesWithDifferentNamesDoNotPair()
    {
        var (from, _) = PageWithHero("photo");
        var (to, _) = PageWithHero("avatar");

        PageTransition.FindHeroPairs(from, to).ShouldBeEmpty();
    }


    [Fact]
    public void AHiddenHeroDoesNotFly()
    {
        var (from, card) = PageWithHero("photo");
        var (to, _) = PageWithHero("photo");
        card.IsVisible = false;

        PageTransition.FindHeroPairs(from, to).ShouldBeEmpty();
    }


    /// <summary>A shared hero needs something to fly over, so an unset transition crossfades.</summary>
    [Fact]
    public void ASharedHeroTurnsAnUnsetTransitionIntoAFade()
    {
        var (from, _) = PageWithHero("photo");
        var (to, _) = PageWithHero("photo");

        PageTransition.Resolve(from, to, false).Type.ShouldBe(PageTransitionType.Fade);
        PageTransition.Resolve(to, from, true).Type.ShouldBe(PageTransitionType.Fade);
    }


    [Fact]
    public void ASharedHeroLeavesAnExplicitTransitionAlone()
    {
        var (from, _) = PageWithHero("photo");
        var (to, _) = PageWithHero("photo");
        PageTransition.SetPush(to, PageTransitionType.Cover);

        PageTransition.Resolve(from, to, false).Type.ShouldBe(PageTransitionType.Cover);
    }


    /// <summary>The children are hidden the moment the property is set, so the first frame never shows them in place.</summary>
    [Theory]
    [InlineData(PageEntranceAnimation.Fade, 0, 0, 1)]
    [InlineData(PageEntranceAnimation.FadeUp, 0, 24, 1)]
    [InlineData(PageEntranceAnimation.SlideIn, -32, 0, 1)]
    [InlineData(PageEntranceAnimation.Scale, 0, 0, 0.92)]
    public void AnEntranceHidesItsChildrenUntilItPlays(PageEntranceAnimation animation, double x, double y, double scale)
    {
        var child = new Label();
        var layout = new VerticalStackLayout { child };

        PageTransition.SetEntrance(layout, animation);

        child.Opacity.ShouldBe(0);
        child.TranslationX.ShouldBe(x);
        child.TranslationY.ShouldBe(y);
        child.Scale.ShouldBe(scale, 0.0001);
    }


    [Fact]
    public void TurningAnEntranceOffShowsTheChildrenAgain()
    {
        var child = new Label();
        var layout = new VerticalStackLayout { child };
        PageTransition.SetEntrance(layout, PageEntranceAnimation.FadeUp);

        PageTransition.SetEntrance(layout, PageEntranceAnimation.None);

        child.Opacity.ShouldBe(1);
        child.TranslationY.ShouldBe(0);
    }


    [Fact]
    public void AChildAddedBeforeTheEntrancePlaysIsHiddenToo()
    {
        var layout = new VerticalStackLayout();
        PageTransition.SetEntrance(layout, PageEntranceAnimation.Fade);

        var late = new Label();
        layout.Add(late);

        late.Opacity.ShouldBe(0);
    }


    [Fact]
    public async Task AChildAddedWhileTheEntrancePlaysAnimatesInToo()
    {
        var host = new FrozenAnimationHost();
        var first = new Label();
        var layout = host.Attach(new VerticalStackLayout { first });
        PageTransition.SetEntranceDelay(layout, 0);
        PageTransition.SetEntrance(layout, PageEntranceAnimation.FadeUp);

        // Loaded is what starts the entrance; a headless layout never raises it.
        typeof(PageEntrance)
            .GetMethod("OnLoaded", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(null, [layout, EventArgs.Empty]);

        var late = new Label();
        layout.Add(late);
        await host.Finish();

        first.Opacity.ShouldBe(1, 0.001);
        late.Opacity.ShouldBe(1, 0.001);
        late.TranslationY.ShouldBe(0, 0.001);
    }


    static (ContentPage Page, BoxView Hero) PageWithHero(string name)
    {
        var hero = new BoxView();
        PageTransition.SetHero(hero, name);
        return (new ContentPage { Content = new VerticalStackLayout { new Label(), hero } }, hero);
    }
}
