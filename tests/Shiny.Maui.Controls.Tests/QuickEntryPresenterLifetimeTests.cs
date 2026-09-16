using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Animations;
using Microsoft.Maui.Controls;
using Shiny.Maui.Controls.FloatingPanel;
using Shiny.Maui.Controls.QuickEntry;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// The in-app quick entry and screen-glow presenters are app-lifetime singletons. Once a popup or
/// glow has been hidden they must let go of the page they were shown on.
/// </summary>
/// <remarks>
/// Both used to keep the last page - plus the overlay and glow views injected into it, and through
/// the service-owned content's parent chain everything under the page - until the next show.
/// </remarks>
[Collection(ApplicationResourcesCollection.Name)]
public class QuickEntryPresenterLifetimeTests
{
    public QuickEntryPresenterLifetimeTests()
    {
        TestDispatcherProvider.Install();
        TestDispatcherProvider.Instance.Timers.Clear();
        _ = new Application();
    }

    /// <summary>Stands in for "the page on screen", which the presenters read from the app's window.</summary>
    sealed class Screen
    {
        public ContentPage? Page;
    }

    static void Collect()
    {
        TestDispatcherProvider.Instance.Timers.Clear();
        for (var i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        GC.Collect();
    }

    static ContentPage NewPage(bool animationsComplete = true)
    {
        var page = new ContentPage { Content = new VerticalStackLayout { Children = { new Label { Text = "page" } } } };
        page.Handler = new AnimationHost(animationsComplete);
        return page;
    }

    /// <summary>
    /// Just enough handler for MAUI to find an <see cref="IAnimationManager"/> above the views the
    /// presenters fade - headless, <c>FadeToAsync</c> throws without one.
    /// </summary>
    /// <remarks>
    /// With animations "complete", the ticker reports the system has animations turned off, which
    /// MAUI honours by snapping every animation to its end at once. Otherwise the ticker never ticks,
    /// which is what a page that has been navigated away from looks like: a fade that never ends.
    /// </remarks>
    sealed class AnimationHost : IViewHandler, IMauiContext
    {
        public AnimationHost(bool animationsComplete)
            => this.Services = new ServiceCollection()
                .AddSingleton<IAnimationManager>(new AnimationManager(new TestTicker(animationsComplete)))
                .BuildServiceProvider();

        public IServiceProvider Services { get; }
        public IMauiHandlersFactory Handlers => throw new NotSupportedException();
        public object? PlatformView => null;
        public IView? VirtualView { get; private set; }
        IElement? IElementHandler.VirtualView => this.VirtualView;
        public bool HasContainer { get; set; }
        public object? ContainerView => null;
        public IMauiContext? MauiContext => this;
        public void SetMauiContext(IMauiContext mauiContext) { }
        public void SetVirtualView(IElement view) => this.VirtualView = (IView)view;
        public Size GetDesiredSize(double widthConstraint, double heightConstraint) => Size.Zero;
        public void PlatformArrange(Rect frame) { }
        public void UpdateValue(string property) { }
        public void Invoke(string command, object? args = null) { }
        public void DisconnectHandler() => this.VirtualView = null;

        sealed class TestTicker(bool animationsComplete) : Ticker
        {
            public override bool SystemEnabled => !animationsComplete;
            public override void Start() { }
            public override void Stop() { }
        }
    }

    static IEnumerable<Overlay> Overlays(ContentPage page)
        => ((IVisualTreeElement)page).GetVisualTreeDescendants().OfType<Overlay>();

    static IEnumerable<ScreenGlowView> Glows(ContentPage page)
        => ((IVisualTreeElement)page).GetVisualTreeDescendants().OfType<ScreenGlowView>();


    // ---------------------------------------------------------------------------------------------
    // Quick entry popup
    // ---------------------------------------------------------------------------------------------

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference ShowAndHideOnANewPage(InAppQuickEntryPresenter presenter, Screen screen, View content, QuickEntryOptions options)
    {
        var page = NewPage();
        screen.Page = page;
        presenter.PrepareAsync(options, content).GetAwaiter().GetResult();
        presenter.Show(options, 400, 60);
        Overlays(page).Single().IsShown.ShouldBeTrue();

        presenter.Hide();
        screen.Page = null;
        return new WeakReference(page);
    }

    [Fact]
    public void AHiddenPopupReleasesThePageItWasShownOn()
    {
        var screen = new Screen();
        var presenter = new InAppQuickEntryPresenter(() => screen.Page);
        var options = new QuickEntryOptions();
        var content = new Label { Text = "prompt" };   // owned by the service, which outlives every page

        var page = ShowAndHideOnANewPage(presenter, screen, content, options);
        Collect();

        page.IsAlive.ShouldBeFalse("unfixed, the singleton presenter kept the last page it was shown on");
        content.Parent.ShouldBeNull("the service's content must not stay parented under the old page");
        GC.KeepAlive(presenter);
    }

    [Fact]
    public void HidingTakesTheOverlayOffThePage()
    {
        var page = NewPage();
        var presenter = new InAppQuickEntryPresenter(() => page);
        var options = new QuickEntryOptions();

        presenter.PrepareAsync(options, new Label()).GetAwaiter().GetResult();
        presenter.Show(options, 400, 60);
        presenter.Hide();

        Overlays(page).ShouldBeEmpty();
    }

    [Fact]
    public void BackToBackShowHideShowLeavesOneShownPopup()
    {
        var page = NewPage();
        var presenter = new InAppQuickEntryPresenter(() => page);
        var options = new QuickEntryOptions();
        var content = new Label();

        presenter.PrepareAsync(options, content).GetAwaiter().GetResult();
        presenter.Show(options, 400, 60);
        presenter.Hide();
        presenter.Show(options, 400, 60);
        presenter.Hide();
        presenter.Show(options, 400, 60);

        var overlay = Overlays(page).ShouldHaveSingleItem();
        overlay.IsShown.ShouldBeTrue();
        content.Parent.ShouldNotBeNull();
        PageOverlayAncestor(content).ShouldBeSameAs(page);
    }

    [Fact]
    public void AScrimTapDismissalAlsoReleasesThePage()
    {
        var page = NewPage();
        var presenter = new InAppQuickEntryPresenter(() => page);
        var options = new QuickEntryOptions();
        var deactivated = 0;
        presenter.Deactivated = () => deactivated++;   // a service that chooses not to hide

        presenter.PrepareAsync(options, new Label()).GetAwaiter().GetResult();
        presenter.Show(options, 400, 60);
        Overlays(page).Single().IsShown = false;       // what the backdrop tap does

        deactivated.ShouldBe(1);
        Overlays(page).ShouldBeEmpty();
    }

    /// <summary>
    /// A page that is navigated away from stops ticking, so a fade can start and never finish - the
    /// popup is hidden mid-entrance and the exit never reports in. The page must still be let go.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference HideMidAnimationOnANewPage(InAppQuickEntryPresenter presenter, Screen screen, View content, QuickEntryOptions options)
    {
        var page = NewPage(animationsComplete: false);
        screen.Page = page;
        presenter.PrepareAsync(options, content).GetAwaiter().GetResult();
        presenter.Show(options, 400, 60);
        Overlays(page).Single().IsVisible.ShouldBeTrue("the entrance has begun and has not finished");

        presenter.Hide();
        screen.Page = null;
        return new WeakReference(page);
    }

    [Fact]
    public void AHideDuringAnEntranceThatNeverFinishesStillReleasesThePage()
    {
        var screen = new Screen();
        var presenter = new InAppQuickEntryPresenter(() => screen.Page);
        var options = new QuickEntryOptions();
        var content = new Label();

        // Off xUnit's synchronization context, which waits for every async void to finish - and the
        // overlay's show/hide worker is an async void that, here by design, never does.
        var run = Task.Run(() => HideMidAnimationOnANewPage(presenter, screen, content, options));
        run.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue("hiding mid-entrance hung");
        var page = run.Result;
        Collect();

        page.IsAlive.ShouldBeFalse();
        content.Parent.ShouldBeNull();

        // And the next show on a fresh page is unaffected.
        var next = NewPage();
        screen.Page = next;
        presenter.Show(options, 400, 60);
        Overlays(next).ShouldHaveSingleItem().IsShown.ShouldBeTrue();
        PageOverlayAncestor(content).ShouldBeSameAs(next);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference ShowWithoutHiding(InAppQuickEntryPresenter presenter, Screen screen, View content, QuickEntryOptions options)
    {
        var page = NewPage();
        screen.Page = page;
        presenter.PrepareAsync(options, content).GetAwaiter().GetResult();
        presenter.Show(options, 400, 60);
        return new WeakReference(page);
    }

    [Fact]
    public void NavigatingAwayWhileShownMovesThePopupAndReleasesTheOldPage()
    {
        var screen = new Screen();
        var presenter = new InAppQuickEntryPresenter(() => screen.Page);
        var options = new QuickEntryOptions();
        var content = new Label();

        var first = ShowWithoutHiding(presenter, screen, content, options);

        // The user navigates while the popup is up, then summons it again.
        var second = NewPage();
        screen.Page = second;
        presenter.Show(options, 400, 60);
        Collect();

        first.IsAlive.ShouldBeFalse();
        Overlays(second).ShouldHaveSingleItem().IsShown.ShouldBeTrue();
        PageOverlayAncestor(content).ShouldBeSameAs(second);

        presenter.Hide();
        Overlays(second).ShouldBeEmpty();
        content.Parent.ShouldBeNull();
    }

    [Fact]
    public void HideAfterTeardownAndTeardownTwiceAreHarmless()
    {
        var page = NewPage();
        var presenter = new InAppQuickEntryPresenter(() => page);
        var options = new QuickEntryOptions();

        presenter.PrepareAsync(options, new Label()).GetAwaiter().GetResult();
        presenter.Show(options, 400, 60);
        presenter.Teardown();

        Should.NotThrow(() =>
        {
            presenter.Hide();
            presenter.Teardown();
            presenter.Hide();
        });
        Overlays(page).ShouldBeEmpty();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference PreloadOnANewPage(InAppQuickEntryPresenter presenter, Screen screen, View content, QuickEntryOptions options)
    {
        var page = NewPage();
        screen.Page = page;
        presenter.PrepareAsync(options, content).GetAwaiter().GetResult();
        screen.Page = null;
        return new WeakReference(page);
    }

    [Fact]
    public void PreloadingDoesNotHoldThePage()
    {
        var screen = new Screen();
        var presenter = new InAppQuickEntryPresenter(() => screen.Page);
        var options = new QuickEntryOptions();
        var content = new Label();

        var page = PreloadOnANewPage(presenter, screen, content, options);
        Collect();

        page.IsAlive.ShouldBeFalse("unfixed, a preload left the popup attached - and the page pinned - until the first show and hide");
        content.Parent.ShouldBeNull();
        GC.KeepAlive(presenter);
    }

    [Fact]
    public void PreloadWarmsThePageButLeavesNoPopupOnIt()
    {
        var page = NewPage();
        var presenter = new InAppQuickEntryPresenter(() => page);

        presenter.PrepareAsync(new QuickEntryOptions(), new Label()).GetAwaiter().GetResult();

        // The page-owned part of the warm-up stays: the overlay layer and its host.
        ((IVisualTreeElement)page).GetVisualTreeDescendants().OfType<OverlayHost>().ShouldHaveSingleItem();
        Overlays(page).ShouldBeEmpty();
    }

    [Fact]
    public void ShowAfterPreloadStillOpensThePopup()
    {
        var page = NewPage();
        var presenter = new InAppQuickEntryPresenter(() => page);
        var options = new QuickEntryOptions();
        var content = new Label();

        presenter.PrepareAsync(options, content).GetAwaiter().GetResult();
        presenter.Show(options, 400, 60);

        Overlays(page).ShouldHaveSingleItem().IsShown.ShouldBeTrue();
        PageOverlayAncestor(content).ShouldBeSameAs(page);
        ((IVisualTreeElement)page).GetVisualTreeDescendants().OfType<OverlayHost>().ShouldHaveSingleItem("the host installed by the preload is reused, not duplicated");

        presenter.Hide();
        Overlays(page).ShouldBeEmpty();
        content.Parent.ShouldBeNull();
    }

    static ContentPage? PageOverlayAncestor(Element element)
        => Shiny.Maui.Controls.Infrastructure.PageOverlay.FindPage(element);


    // ---------------------------------------------------------------------------------------------
    // Screen glow
    // ---------------------------------------------------------------------------------------------

    static ScreenGlowOptions FastGlow() => new() { FadeDuration = TimeSpan.FromMilliseconds(10) };

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference GlowAndHideOnANewPage(InAppScreenGlowPresenter presenter, Screen screen, ScreenGlowOptions options)
    {
        var page = NewPage();
        screen.Page = page;
        presenter.ShowAsync(options).GetAwaiter().GetResult();
        Glows(page).ShouldHaveSingleItem();

        presenter.HideAsync(options).GetAwaiter().GetResult();
        screen.Page = null;
        return new WeakReference(page);
    }

    [Fact]
    public void AHiddenGlowReleasesThePageItWasShownOn()
    {
        var screen = new Screen();
        var presenter = new InAppScreenGlowPresenter(() => screen.Page);

        var page = GlowAndHideOnANewPage(presenter, screen, FastGlow());
        Collect();

        page.IsAlive.ShouldBeFalse("unfixed, the singleton glow presenter kept the last page it glowed over");
        GC.KeepAlive(presenter);
    }

    [Fact]
    public async Task HidingTheGlowTakesItOffThePage()
    {
        var page = NewPage();
        var presenter = new InAppScreenGlowPresenter(() => page);
        var options = FastGlow();

        await presenter.ShowAsync(options);
        await presenter.HideAsync(options);

        Glows(page).ShouldBeEmpty();
    }

    [Fact]
    public async Task AShowDuringTheHideFadeKeepsTheGlow()
    {
        var page = NewPage();
        var presenter = new InAppScreenGlowPresenter(() => page);
        var options = FastGlow();

        await presenter.ShowAsync(options);
        var hide = presenter.HideAsync(options);
        var show = presenter.ShowAsync(options);
        await Task.WhenAll(hide, show);

        var glow = Glows(page).ShouldHaveSingleItem();
        glow.Opacity.ShouldBe(1);
        ((VisualElement)glow.Parent).IsVisible.ShouldBeTrue();
    }

    [Fact]
    public async Task AHideDuringTheShowFadeReleasesTheGlow()
    {
        var page = NewPage();
        var presenter = new InAppScreenGlowPresenter(() => page);
        var options = FastGlow();

        var show = presenter.ShowAsync(options);
        var hide = presenter.HideAsync(options);
        await Task.WhenAll(show, hide);

        Glows(page).ShouldBeEmpty();

        // And it comes back cleanly afterwards.
        await presenter.ShowAsync(options);
        Glows(page).ShouldHaveSingleItem().Opacity.ShouldBe(1);
    }
}
