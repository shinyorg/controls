using Shouldly;
using Xunit;

namespace Shiny.Blazor.Controls.Tests;

/// <summary>How a page's PageTransition settings and the host's defaults resolve into what the browser runs.</summary>
public class PageTransitionTests
{
    static readonly TimeSpan HostDuration = TimeSpan.FromMilliseconds(320);

    static (PageTransitionType Type, int X, int Y, double DurationMs) Resolve(
        PageTransitionType push = PageTransitionType.Default,
        PageTransitionType pop = PageTransitionType.Default,
        PageTransitionDirection direction = PageTransitionDirection.Default,
        TimeSpan? duration = null,
        bool isPop = false,
        PageTransitionType hostDefault = PageTransitionType.Default)
        => PageTransitionHost.Resolve(push, pop, direction, duration, isPop, hostDefault, HostDuration);


    [Fact]
    public void APushUsesThePagesOwnTransition()
        => Resolve(push: PageTransitionType.Cover).Type.ShouldBe(PageTransitionType.Cover);


    [Fact]
    public void AnUnsetPageFallsBackToTheHostDefault()
        => Resolve(hostDefault: PageTransitionType.Fade).Type.ShouldBe(PageTransitionType.Fade);


    /// <summary>Default all the way down is "no animation" on the web - there is no platform animation to keep.</summary>
    [Fact]
    public void WithNothingSetNothingAnimates()
        => Resolve().Type.ShouldBe(PageTransitionType.Default);


    [Fact]
    public void GoingBackMirrorsThePushWhenPopIsUnset()
        => Resolve(push: PageTransitionType.Zoom, isPop: true).Type.ShouldBe(PageTransitionType.Zoom);


    [Fact]
    public void AnExplicitPopWinsOnTheWayBack()
    {
        Resolve(push: PageTransitionType.Zoom, pop: PageTransitionType.Fade, isPop: true).Type.ShouldBe(PageTransitionType.Fade);
        Resolve(push: PageTransitionType.Zoom, pop: PageTransitionType.Fade, isPop: false).Type.ShouldBe(PageTransitionType.Zoom);
    }


    [Fact]
    public void NativeZoomIsZoomOnTheWeb()
        => Resolve(push: PageTransitionType.NativeZoom).Type.ShouldBe(PageTransitionType.Zoom);


    [Theory]
    [InlineData(PageTransitionDirection.FromRight, 1, 0)]
    [InlineData(PageTransitionDirection.FromLeft, -1, 0)]
    [InlineData(PageTransitionDirection.FromBottom, 0, 1)]
    [InlineData(PageTransitionDirection.FromTop, 0, -1)]
    public void TheVectorPointsAtTheEdgeThePageComesFrom(PageTransitionDirection direction, int x, int y)
    {
        var plan = Resolve(push: PageTransitionType.Cover, direction: direction);
        (plan.X, plan.Y).ShouldBe((x, y));
    }


    [Theory]
    [InlineData(PageTransitionType.Slide, 1, 0)]
    [InlineData(PageTransitionType.SharedAxisY, 0, 1)]
    public void TheDefaultDirectionSuitsTheTransition(PageTransitionType type, int x, int y)
    {
        var plan = Resolve(push: type);
        (plan.X, plan.Y).ShouldBe((x, y));
    }


    [Fact]
    public void APageCanSetItsOwnDuration()
    {
        Resolve(push: PageTransitionType.Fade, duration: TimeSpan.FromMilliseconds(900)).DurationMs.ShouldBe(900);
        Resolve(push: PageTransitionType.Fade).DurationMs.ShouldBe(320);
    }
}
