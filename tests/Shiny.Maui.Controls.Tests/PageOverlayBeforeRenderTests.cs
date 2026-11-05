using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Shiny.Maui.Controls.Infrastructure;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// The overlay root goes in as the page's handler connects, so the first toast (or dialog, or
/// confetti) on a page that is already showing finds it in place. Installing it at that toast
/// re-parented the page's content: its native views were detached and laid out again, and every
/// binding under it saw a null context and back, so bound lists reloaded.
/// </summary>
public class PageOverlayBeforeRenderTests
{
    public PageOverlayBeforeRenderTests() => PageOverlay.WrapBeforeRender();


    [Fact]
    public void TheRootIsInPlaceOnceThePageHasItsHandler()
    {
        var content = new VerticalStackLayout { new Label() };
        var page = new ContentPage { Content = content, BindingContext = new object() };

        page.Handler = new StubPageHandler();

        var root = page.Content.ShouldBeOfType<PageOverlay.ShinyOverlayRoot>();
        PageOverlay.ContentOf(root).ShouldBeSameAs(content);
        content.Parent.ShouldBeSameAs(root);
        root.Parent.ShouldBeSameAs(page);
        content.BindingContext.ShouldBeSameAs(page.BindingContext);
    }


    [Fact]
    public void TheFirstToastOnARenderedPageDoesNotMoveItsContent()
    {
        var content = new VerticalStackLayout { new Label() };
        var page = new ContentPage { Content = content, BindingContext = new object() };
        page.Handler = new StubPageHandler();

        var moves = 0;
        var contextChanges = 0;
        content.ParentChanged += (_, _) => moves++;
        content.BindingContextChanged += (_, _) => contextChanges++;

        // What ToastManager does for the first toast on a page.
        PageOverlay.GetOrCreateLayer<PageOverlay.ToastLayer>(PageOverlay.GetOrCreateRoot(page), 9_999);

        moves.ShouldBe(0);
        contextChanges.ShouldBe(0);
        content.BindingContext.ShouldBeSameAs(page.BindingContext);
    }


    /// <summary>
    /// Content assigned to a page that already has a handler is mapped from inside that assignment,
    /// where MAUI defers a second write to <c>Content</c>. Wrapping it from there left the content in
    /// the root with no parent. It is left alone instead, and wrapped lazily, as it always was.
    /// </summary>
    [Fact]
    public void ContentSetAfterTheHandlerKeepsThePageAsItsParent()
    {
        var page = new ContentPage();
        page.Handler = new StubPageHandler();

        var content = new Grid();
        page.Content = content;

        page.Content.ShouldBeSameAs(content);
        content.Parent.ShouldBeSameAs(page);

        var root = PageOverlay.GetOrCreateRoot(page);
        page.Content.ShouldBeSameAs(root);
        PageOverlay.ContentOf(root).ShouldBeSameAs(content);
        content.Parent.ShouldBeSameAs(root);
    }


    [Fact]
    public void ContentReplacedOnARenderedPageKeepsThePageAsItsParent()
    {
        var page = new ContentPage { Content = new Label() };
        page.Handler = new StubPageHandler();
        page.Content.ShouldBeOfType<PageOverlay.ShinyOverlayRoot>();

        var replacement = new Grid();
        page.Content = replacement;

        page.Content.ShouldBeSameAs(replacement);
        replacement.Parent.ShouldBeSameAs(page);
    }


    [Fact]
    public void ANewHandlerWrapsContentThatWasSetLater()
    {
        var page = new ContentPage();
        page.Handler = new StubPageHandler();
        var content = new Grid();
        page.Content = content;

        page.Handler = new StubPageHandler();

        var root = page.Content.ShouldBeOfType<PageOverlay.ShinyOverlayRoot>();
        content.Parent.ShouldBeSameAs(root);
    }


    [Fact]
    public void AnExistingRootIsNotWrappedAgain()
    {
        var page = new ContentPage { Content = new Grid() };
        var root = PageOverlay.GetOrCreateRoot(page);

        page.Handler = new StubPageHandler();

        page.Content.ShouldBeSameAs(root);
        root.Children.OfType<PageOverlay.ShinyOverlayRoot>().ShouldBeEmpty();
    }
}


/// <summary>A page handler that runs <see cref="PageHandler.Mapper"/> with no platform behind it.</summary>
sealed class StubPageHandler() : ViewHandler<IContentView, object>(PageHandler.Mapper), IPageHandler
{
    protected override object CreatePlatformView() => new();
}
