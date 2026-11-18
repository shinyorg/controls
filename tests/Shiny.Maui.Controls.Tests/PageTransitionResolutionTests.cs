using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// What a navigation runs once one-shot overrides, custom animators, modal and tab properties and
/// reduced motion are in the mix, and how the managed runner (the heads without a native hook)
/// prepares and finishes a transition.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class PageTransitionResolutionTests : IDisposable
{
    public PageTransitionResolutionTests()
    {
        TestDispatcherProvider.Install();
        _ = new Application();
        Reset();
    }

    public void Dispose() => Reset();

    static void Reset()
    {
        PageTransition.DefaultTransition = PageTransitionType.Default;
        PageTransition.DefaultModalTransition = PageTransitionType.Default;
        PageTransition.DefaultAnimator = null;
        PageTransition.ShellItemTransition = PageTransitionType.Default;
        PageTransition.ReducedMotion = PageTransitionReducedMotion.Crossfade;
        PageTransition.IsReducedMotionRequested = () => false;
        PageTransition.Duration = TimeSpan.FromMilliseconds(320);
        PageTransitionOverrides.SetAmbient(null);
    }


    sealed class StubAnimator : IPageTransitionAnimator
    {
        public Task AnimateAsync(PageTransitionContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    static PageTransitionOverride Shot(PageTransitionType type, IPageTransitionAnimator? animator = null, TimeSpan? duration = null, PageTransitionDirection direction = PageTransitionDirection.Default)
        => new(type, direction, duration, animator);

    #region Precedence

    [Fact]
    public void AOneShotTypeBeatsEverythingThePageSets()
    {
        var page = new ContentPage();
        PageTransition.SetPush(page, PageTransitionType.Cover);
        PageTransition.SetAnimator(page, new StubAnimator());
        PageTransitionOverrides.Set(page, Shot(PageTransitionType.Flip));

        var resolution = PageTransition.ResolveTransition(null, page, false);

        resolution.Animator.ShouldBeNull();
        resolution.Plan.Type.ShouldBe(PageTransitionType.Flip);
    }


    [Fact]
    public void AOneShotAnimatorBeatsThePagesAnimator()
    {
        var page = new ContentPage();
        var mine = new StubAnimator();
        PageTransition.SetAnimator(page, new StubAnimator());
        PageTransitionOverrides.Set(page, Shot(PageTransitionType.Default, mine));

        PageTransition.ResolveTransition(null, page, false).Animator.ShouldBeSameAs(mine);
    }


    [Fact]
    public void AOneShotCarriesItsDirectionAndDuration()
    {
        var page = new ContentPage();
        PageTransition.SetDirection(page, PageTransitionDirection.FromLeft);
        PageTransitionOverrides.Set(page, Shot(PageTransitionType.Cover, duration: TimeSpan.FromSeconds(1), direction: PageTransitionDirection.FromBottom));

        var plan = PageTransition.ResolveTransition(null, page, false).Plan;

        plan.Direction.ShouldBe(PageTransitionDirection.FromBottom);
        plan.Duration.ShouldBe(TimeSpan.FromSeconds(1));
    }


    [Fact]
    public void AOneShotIsGoneOnceCleared()
    {
        var page = new ContentPage();
        PageTransition.SetPush(page, PageTransitionType.Cover);
        PageTransitionOverrides.Set(page, Shot(PageTransitionType.Flip));
        PageTransitionOverrides.Clear(page);

        PageTransition.ResolveTransition(null, page, false).Plan.Type.ShouldBe(PageTransitionType.Cover);
    }


    /// <summary>A pop's one-shot sits on the page leaving, not the one it reveals.</summary>
    [Fact]
    public void APopsOneShotIsKeyedOnTheOutgoingPage()
    {
        var top = new ContentPage();
        var below = new ContentPage();
        PageTransitionOverrides.Set(top, Shot(PageTransitionType.Zoom));

        PageTransition.ResolveTransition(top, below, true).Plan.Type.ShouldBe(PageTransitionType.Zoom);
        // Popping the page underneath (or pushing it) is a different navigation.
        PageTransition.ResolveTransition(below, top, true).Plan.Type.ShouldBe(PageTransitionType.Default);
    }


    /// <summary>Shell's GoToAsync overload can't know its pages, so its one-shot covers any page in the call.</summary>
    [Fact]
    public void AnAmbientOneShotCoversAnyPageButYieldsToAKeyedOne()
    {
        var any = new ContentPage();
        var keyed = new ContentPage();
        PageTransitionOverrides.SetAmbient(Shot(PageTransitionType.SharedAxisZ));
        PageTransitionOverrides.Set(keyed, Shot(PageTransitionType.Fade));

        PageTransition.ResolveTransition(null, any, false).Plan.Type.ShouldBe(PageTransitionType.SharedAxisZ);
        PageTransition.ResolveTransition(null, keyed, false).Plan.Type.ShouldBe(PageTransitionType.Fade);
    }


    [Fact]
    public void ThePagesAnimatorBeatsItsOwnType()
    {
        var page = new ContentPage();
        var animator = new StubAnimator();
        PageTransition.SetPush(page, PageTransitionType.Cover);
        PageTransition.SetAnimator(page, animator);

        var resolution = PageTransition.ResolveTransition(null, page, false);

        resolution.Animator.ShouldBeSameAs(animator);
        // What a swipe-back falls back to.
        resolution.Plan.Type.ShouldBe(PageTransitionType.Cover);
    }


    /// <summary>A page that says how it arrives isn't overridden by an app-wide animator.</summary>
    [Fact]
    public void ThePagesTypeBeatsTheDefaultAnimator()
    {
        var page = new ContentPage();
        PageTransition.SetPush(page, PageTransitionType.Cover);
        PageTransition.DefaultAnimator = new StubAnimator();

        var resolution = PageTransition.ResolveTransition(null, page, false);

        resolution.Animator.ShouldBeNull();
        resolution.Plan.Type.ShouldBe(PageTransitionType.Cover);
    }


    [Fact]
    public void TheDefaultAnimatorBeatsTheDefaultTransition()
    {
        var animator = new StubAnimator();
        PageTransition.DefaultAnimator = animator;
        PageTransition.DefaultTransition = PageTransitionType.Fade;

        PageTransition.ResolveTransition(null, new ContentPage(), false).Animator.ShouldBeSameAs(animator);
    }


    [Fact]
    public void WithNothingSetThePlatformAnimationIsKept()
    {
        var resolution = PageTransition.ResolveTransition(new ContentPage(), new ContentPage(), false);

        resolution.IsSet.ShouldBeFalse();
        resolution.Plan.Type.ShouldBe(PageTransitionType.Default);
    }


    [Fact]
    public void ExplicitNoneCountsAsSet()
    {
        var page = new ContentPage();
        PageTransition.SetPush(page, PageTransitionType.None);

        PageTransition.ResolveTransition(null, page, false).IsSet.ShouldBeTrue();
    }

    #endregion

    #region Modal

    [Fact]
    public void AModalUsesTheModalPropertyAndAPushDoesNot()
    {
        var page = new ContentPage();
        PageTransition.SetPush(page, PageTransitionType.Slide);
        PageTransition.SetModal(page, PageTransitionType.Cover);

        PageTransition.ResolveTransition(null, page, false, isModal: true).Plan.Type.ShouldBe(PageTransitionType.Cover);
        PageTransition.ResolveTransition(null, page, false, isModal: false).Plan.Type.ShouldBe(PageTransitionType.Slide);
    }


    [Fact]
    public void AModalDismissRunsTheModalPropertyInReverse()
    {
        var page = new ContentPage();
        PageTransition.SetPop(page, PageTransitionType.Fade);
        PageTransition.SetModal(page, PageTransitionType.Cover);

        var plan = PageTransition.ResolveTransition(page, null, true, isModal: true).Plan;

        plan.Type.ShouldBe(PageTransitionType.Cover);
        plan.IsPop.ShouldBeTrue();
    }


    [Fact]
    public void AModalWithoutAModalPropertyFallsBackToPushThenPop()
    {
        var page = new ContentPage();
        PageTransition.SetPush(page, PageTransitionType.Zoom);
        PageTransition.ResolveTransition(null, page, false, isModal: true).Plan.Type.ShouldBe(PageTransitionType.Zoom);

        PageTransition.SetPop(page, PageTransitionType.Fade);
        PageTransition.ResolveTransition(page, null, true, isModal: true).Plan.Type.ShouldBe(PageTransitionType.Fade);
    }


    [Fact]
    public void TheDefaultModalTransitionOnlyAppliesToModals()
    {
        PageTransition.DefaultModalTransition = PageTransitionType.Cover;
        PageTransition.DefaultTransition = PageTransitionType.Fade;

        PageTransition.ResolveTransition(null, new ContentPage(), false, isModal: true).Plan.Type.ShouldBe(PageTransitionType.Cover);
        PageTransition.ResolveTransition(null, new ContentPage(), false, isModal: false).Plan.Type.ShouldBe(PageTransitionType.Fade);
    }


    [Fact]
    public void AModalFallsBackToTheDefaultTransitionWhenNoModalDefaultIsSet()
    {
        PageTransition.DefaultTransition = PageTransitionType.Fade;

        PageTransition.ResolveTransition(null, new ContentPage(), false, isModal: true).Plan.Type.ShouldBe(PageTransitionType.Fade);
    }

    #endregion

    #region Reduced motion

    [Fact]
    public void ReducedMotionTurnsATransitionIntoAShortCrossfade()
    {
        PageTransition.IsReducedMotionRequested = () => true;
        var page = new ContentPage();
        PageTransition.SetPush(page, PageTransitionType.Flip);
        PageTransition.SetDuration(page, TimeSpan.FromSeconds(1));

        var plan = PageTransition.ResolveTransition(null, page, false).Plan;

        plan.Type.ShouldBe(PageTransitionType.Fade);
        plan.Duration.ShouldBe(TimeSpan.FromMilliseconds(200));
    }


    [Fact]
    public void ReducedMotionKeepsAShortTransitionsDuration()
    {
        PageTransition.IsReducedMotionRequested = () => true;
        var page = new ContentPage();
        PageTransition.SetPush(page, PageTransitionType.Cover);
        PageTransition.SetDuration(page, TimeSpan.FromMilliseconds(120));

        PageTransition.ResolveTransition(null, page, false).Plan.Duration.ShouldBe(TimeSpan.FromMilliseconds(120));
    }


    [Fact]
    public void ReducedMotionReplacesACustomAnimator()
    {
        PageTransition.IsReducedMotionRequested = () => true;
        var page = new ContentPage();
        PageTransition.SetAnimator(page, new StubAnimator());

        var resolution = PageTransition.ResolveTransition(null, page, false);

        resolution.Animator.ShouldBeNull();
        resolution.Plan.Type.ShouldBe(PageTransitionType.Fade);
    }


    [Fact]
    public void ReducedMotionCanTurnTransitionsOff()
    {
        PageTransition.IsReducedMotionRequested = () => true;
        PageTransition.ReducedMotion = PageTransitionReducedMotion.None;
        var page = new ContentPage();
        PageTransition.SetPush(page, PageTransitionType.Cover);

        PageTransition.ResolveTransition(null, page, false).Plan.Type.ShouldBe(PageTransitionType.None);
    }


    [Fact]
    public void ReducedMotionCanBeIgnored()
    {
        PageTransition.IsReducedMotionRequested = () => true;
        PageTransition.ReducedMotion = PageTransitionReducedMotion.Ignore;
        var page = new ContentPage();
        PageTransition.SetPush(page, PageTransitionType.Cover);

        PageTransition.ResolveTransition(null, page, false).Plan.Type.ShouldBe(PageTransitionType.Cover);
        PageTransition.HeroesAllowed.ShouldBeTrue();
    }


    /// <summary>The platform's own animation follows the platform's own reduced-motion rules.</summary>
    [Fact]
    public void ReducedMotionLeavesThePlatformAnimationAlone()
    {
        PageTransition.IsReducedMotionRequested = () => true;

        PageTransition.ResolveTransition(null, new ContentPage(), false).Plan.Type.ShouldBe(PageTransitionType.Default);
    }


    [Fact]
    public void ReducedMotionAppliesToOneShotsAndDropsHeroes()
    {
        PageTransition.IsReducedMotionRequested = () => true;
        var page = new ContentPage();
        PageTransitionOverrides.Set(page, Shot(PageTransitionType.SharedAxisZ));

        PageTransition.ResolveTransition(null, page, false).Plan.Type.ShouldBe(PageTransitionType.Fade);
        PageTransition.HeroesAllowed.ShouldBeFalse();
    }

    #endregion

    #region Tabs

    [Fact]
    public void ATabUsesTheMostSpecificTabPropertySet()
    {
        var content = new ShellContent();
        var section = new ShellSection { Items = { content } };
        var item = new ShellItem { Items = { section } };
        PageTransition.ShellItemTransition = PageTransitionType.Fade;

        PageTransition.ResolveTab(section, true).Type.ShouldBe(PageTransitionType.Fade);

        PageTransition.SetTab(item, PageTransitionType.Zoom);
        PageTransition.ResolveTab(section, true).Type.ShouldBe(PageTransitionType.Zoom);

        PageTransition.SetTab(section, PageTransitionType.SharedAxisY);
        PageTransition.ResolveTab(section, true).Type.ShouldBe(PageTransitionType.SharedAxisY);

        PageTransition.SetTab(content, PageTransitionType.Cover);
        PageTransition.ResolveTab(section, true).Type.ShouldBe(PageTransitionType.Cover);
        PageTransition.ResolveTab(item, true).Type.ShouldBe(PageTransitionType.Cover);
    }


    [Fact]
    public void ATabSwitchSlidesTheWayTheTabsGo()
    {
        PageTransition.ShellItemTransition = PageTransitionType.SharedAxisX;

        PageTransition.ResolveTab(new ShellSection(), true).Direction.ShouldBe(PageTransitionDirection.FromRight);
        PageTransition.ResolveTab(new ShellSection(), false).Direction.ShouldBe(PageTransitionDirection.FromLeft);
    }

    #endregion

    #region Managed runner

    /// <summary>The incoming page is in its starting state before the head ever draws it.</summary>
    [Fact]
    public void PreparingPutsTheIncomingPageInItsStartingState()
    {
        var from = new ContentPage();
        var to = new ContentPage();
        var plan = new PageTransitionPlan(PageTransitionType.Cover, PageTransitionDirection.FromRight, false, TimeSpan.FromMilliseconds(300));

        ManagedTransitionRunner.Prepare(plan, from, to, new Size(400, 800));

        to.TranslationX.ShouldBe(400);
        from.TranslationX.ShouldBe(0);
    }


    /// <summary>With the outgoing page already gone, a type that only moves it fades the incoming page in.</summary>
    [Fact]
    public void WithNoOutgoingPageAnOutgoingOnlyTypeFadesTheIncomingPageIn()
    {
        var to = new ContentPage();
        var plan = new PageTransitionPlan(PageTransitionType.Cover, PageTransitionDirection.FromRight, true, TimeSpan.FromMilliseconds(300));

        ManagedTransitionRunner.Prepare(plan, null, to, new Size(400, 800));

        to.Opacity.ShouldBe(0);
        to.TranslationX.ShouldBe(0);
    }


    [Fact]
    public async Task ARunEndsWithBothPagesAtRest()
    {
        var host = new FrozenAnimationHost();
        var from = host.Attach(new ContentPage());
        var to = host.Attach(new ContentPage());
        var plan = new PageTransitionPlan(PageTransitionType.SharedAxisZ, PageTransitionDirection.FromRight, false, TimeSpan.FromMilliseconds(60));

        var run = ManagedTransitionRunner.RunAsync(plan, from, to, new Size(400, 800), [], CancellationToken.None);
        to.Opacity.ShouldBe(0);
        to.Scale.ShouldBe(0.8, 0.0001);

        await host.Finish();
        await run;

        foreach (var page in new[] { from, to })
        {
            page.Opacity.ShouldBe(1);
            page.Scale.ShouldBe(1);
        }
    }


    [Fact]
    public async Task ACustomAnimatorsPagesAreResetAfterwards()
    {
        var from = new ContentPage();
        var to = new ContentPage();
        var context = new PageTransitionContext(from, to, false, false, TimeSpan.FromMilliseconds(10), new Size(400, 800));

        await PageTransitionHost.RunAnimatorAsync(new LeavesThingsMoved(), context, CancellationToken.None);

        to.TranslationX.ShouldBe(0);
        to.Opacity.ShouldBe(1);
        from.Scale.ShouldBe(1);
        from.RotationY.ShouldBe(0);
    }


    [Fact]
    public async Task AFailingAnimatorStillCompletesAndResets()
    {
        var to = new ContentPage();
        var context = new PageTransitionContext(null, to, false, false, TimeSpan.FromMilliseconds(10), new Size(400, 800));

        await PageTransitionHost.RunAnimatorAsync(new Throws(), context, CancellationToken.None);

        to.Opacity.ShouldBe(1);
    }


    sealed class LeavesThingsMoved : IPageTransitionAnimator
    {
        public Task AnimateAsync(PageTransitionContext context, CancellationToken cancellationToken)
        {
            context.ToSurface!.TranslationX = 120;
            context.ToSurface.Opacity = 0.2;
            context.FromSurface!.Scale = 0.5;
            context.FromSurface.RotationY = 30;
            return Task.CompletedTask;
        }
    }


    sealed class Throws : IPageTransitionAnimator
    {
        public Task AnimateAsync(PageTransitionContext context, CancellationToken cancellationToken)
        {
            context.ToSurface!.Opacity = 0;
            throw new InvalidOperationException("broken");
        }
    }

    #endregion
}
